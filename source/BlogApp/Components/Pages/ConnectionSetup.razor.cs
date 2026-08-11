using BlogApp.Services;
using BlogModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlogApp.Components.Pages;

/// <summary>
/// Code-behind for BlogApp's connection-setup and connection-settings screen.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-FN-047: capture the site's PostgreSQL connection string,
/// prove it works before accepting it, store it encrypted, and offer a way to change or remove
/// it.</para>
/// <para><b>Code Flow:</b> the screen binds to <see cref="Settings"/> → <see cref="TestConnection"/>
/// probes the server → <see cref="SaveAndContinue"/> re-probes and persists, then relaunches the
/// app so the new connection reaches the DI graph → <see cref="ForgetConnection"/> clears the
/// stored value and relaunches into first-run state.</para>
/// <para><b>Dependencies:</b> <see cref="IConnectionStore"/>, <see cref="ConnectionProbe"/>,
/// <see cref="AppRestarter"/>, <see cref="ConnectionContext"/>.</para>
/// <para><b>Usage:</b> Routed at <see cref="SetupRoute"/> (first run) and
/// <see cref="SettingsRoute"/> (reconfiguration).</para>
/// </remarks>
public partial class ConnectionSetup : ComponentBase
{
    /// <summary>First-run route; the only route an unconfigured BlogApp can reach.</summary>
    public const string SetupRoute = "/blogapp/connection";

    /// <summary>Reconfiguration route, reachable once a connection exists.</summary>
    public const string SettingsRoute = "/blogapp/connection/settings";

    /// <summary>Route the app returns to after a cancelled reconfiguration.</summary>
    private const string LoginRoute = "/login";

    /// <summary>
    /// Message shown when the credential store refused the save (REQ-NFR-033).
    /// </summary>
    /// <remarks>
    /// The exception raised here comes from the DPAPI-backed <c>ConnectionStore</c> and its text
    /// carries the local account name and the credential path. It is logged in full by the
    /// <c>catch</c> that sets this message; the screen shows only the curated sentence, matching the
    /// pattern REQ-NFR-031 established across the engine services.
    /// </remarks>
    private const string SaveFailureMessage =
        "The connection could not be saved. The underlying error is recorded in the application log.";

    /// <summary>Message shown when the stored connection could not be cleared. See <see cref="SaveFailureMessage"/>.</summary>
    private const string ForgetFailureMessage =
        "The connection could not be removed. The underlying error is recorded in the application log.";

    private bool isTesting;
    private bool isSaving;
    private string successMessage;
    private string errorMessage;
    private string restartMessage;

    /// <summary>
    /// The connection store the settings are read from and written to.
    /// </summary>
    [Inject]
    public IConnectionStore ConnectionStore { get; set; }

    /// <summary>
    /// The read-only probe used to validate the settings before they are stored.
    /// </summary>
    [Inject]
    public ConnectionProbe ConnectionProbe { get; set; }

    /// <summary>
    /// Relaunches BlogApp so a changed connection reaches the dependency graph.
    /// </summary>
    [Inject]
    public AppRestarter AppRestarter { get; set; }

    /// <summary>
    /// The connection the running process booted with, used to pre-fill the form.
    /// </summary>
    [Inject]
    public ConnectionContext ConnectionContext { get; set; }

    /// <summary>
    /// Navigation used to leave the screen when the operator cancels.
    /// </summary>
    [Inject]
    public NavigationManager NavigationManager { get; set; }

    /// <summary>
    /// Structured logger for setup outcomes.
    /// </summary>
    [Inject]
    public ILogger<ConnectionSetup> Logger { get; set; }

    /// <summary>
    /// The settings bound to the form.
    /// </summary>
    public ConnectionSettings Settings { get; private set; } = new ConnectionSettings();

    /// <summary>
    /// The port as text, because the underlying input control binds strings.
    /// </summary>
    /// <remarks>
    /// A value that is not a number leaves <see cref="ConnectionSettings.Port"/> at zero, which
    /// <see cref="ConnectionSettings.IsComplete"/> rejects, so the Save button stays disabled
    /// rather than producing a malformed connection string.
    /// </remarks>
    public string PortText
    {
        get => Settings.Port > 0 ? Settings.Port.ToString() : string.Empty;
        set => Settings.Port = int.TryParse(value, out var parsedPort) ? parsedPort : 0;
    }

    /// <summary>
    /// Indicates whether the screen is being used to change an existing connection.
    /// </summary>
    public bool IsReconfiguring =>
        NavigationManager.Uri.Contains(SettingsRoute, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Indicates whether a probe, a save or a restart is in flight.
    /// </summary>
    public bool IsBusy => isTesting || isSaving;

    /// <summary>
    /// Reports whether a route belongs to the connection screens.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used by <c>ConnectionGuard</c> to decide what an unconfigured
    /// BlogApp is allowed to open. Keeping the test next to the route constants means the guard and
    /// the page can never disagree about which routes are reachable before setup.</para>
    /// <para><b>Flow:</b> case-insensitive comparison against both routes.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="relativePath">A site-relative path beginning with a slash.</param>
    /// <returns><c>true</c> when the path is one of the connection screens.</returns>
    public static bool IsConnectionRoute(string relativePath)
    {
        return string.Equals(relativePath, SetupRoute, StringComparison.OrdinalIgnoreCase)
            || string.Equals(relativePath, SettingsRoute, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        var existing = ConnectionContext.Settings;
        if (existing == null)
        {
            return;
        }

        // Pre-fill from the connection in force so a host or password change does not
        // mean retyping every field.
        Settings = new ConnectionSettings
        {
            Host = existing.Host,
            Port = existing.Port,
            Database = existing.Database,
            Username = existing.Username,
            Password = existing.Password,
            SslMode = existing.SslMode
        };
    }

    /// <summary>
    /// Probes the server described by the form without storing anything.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Gives the operator a definite answer before they commit. The
    /// probe's message is rendered verbatim, so an Npgsql authentication or host-resolution failure
    /// reaches the screen in full rather than as a generic "could not connect".</para>
    /// <para><b>Flow:</b> clear previous messages → probe → record success or failure.</para>
    /// <para><b>Side Effects:</b> Opens one read-only PostgreSQL connection.</para>
    /// </remarks>
    /// <returns>A task that completes when the probe has finished.</returns>
    public async Task TestConnection()
    {
        if (IsBusy)
        {
            return;
        }

        isTesting = true;
        ClearMessages();

        try
        {
            var probeResult = await ConnectionProbe.TestAsync(Settings);
            if (probeResult.IsSuccess)
            {
                successMessage = probeResult.Data;
            }
            else
            {
                errorMessage = probeResult.ErrorMessage;
            }
        }
        finally
        {
            isTesting = false;
        }
    }

    /// <summary>
    /// Validates the settings, stores them encrypted, and relaunches BlogApp.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The probe runs again on save rather than trusting an earlier
    /// "Test connection" click, so settings edited after a successful test cannot slip through
    /// unverified — an invalid connection string is never persisted. Once stored, the app relaunches
    /// because the engine repositories bind their connection string when the container is built;
    /// see <see cref="AppRestarter"/> for why that is the deliberate choice.</para>
    /// <para><b>Flow:</b> probe → store → announce the restart → relaunch.</para>
    /// <para><b>Side Effects:</b> Writes to the OS credential store and terminates the process.</para>
    /// </remarks>
    /// <returns>A task that completes when the save has finished or failed.</returns>
    public async Task SaveAndContinue()
    {
        if (IsBusy)
        {
            return;
        }

        isSaving = true;
        ClearMessages();

        try
        {
            // REQ-NFR-027: refuse the save before the probe rather than after it. A blob without
            // usable secrets fails ConnectionSettings.IsComplete, so saving it would restart the
            // app straight back onto this screen with nothing said about why.
            if (!Settings.HasUsableSecrets())
            {
                errorMessage =
                    $"Both site secrets are required. The JWT signing key must be at least " +
                    $"{AppSecrets.MinimumJwtSigningKeyLength} characters and the encryption key at " +
                    $"least {AppSecrets.MinimumEncryptionKeyLength}. Copy them from the website's " +
                    $"configuration — they must match it exactly.";
                return;
            }

            var probeResult = await ConnectionProbe.TestAsync(Settings);
            if (probeResult.IsFailure)
            {
                errorMessage = probeResult.ErrorMessage;
                return;
            }

            await ConnectionStore.SaveAsync(Settings);
            successMessage = probeResult.Data;
            Logger.LogInformation("Connection settings saved for {Server}", Settings.ToDisplayLabel());

            await RestartWithMessage("Connection saved. Restarting BlogApp...");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save the connection settings");
            errorMessage = SaveFailureMessage;
        }
        finally
        {
            isSaving = false;
        }
    }

    /// <summary>
    /// Removes the stored connection and returns BlogApp to its first-run state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The REQ-FN-047 acceptance criterion is that deleting the stored
    /// connection sends the app back to the setup screen. Clearing the credential and relaunching
    /// does exactly that: the new process finds nothing in secure storage and opens on
    /// <see cref="SetupRoute"/>.</para>
    /// <para><b>Flow:</b> clear the store → announce the restart → relaunch.</para>
    /// <para><b>Side Effects:</b> Deletes the credential and terminates the process.</para>
    /// </remarks>
    /// <returns>A task that completes when the connection has been cleared.</returns>
    public async Task ForgetConnection()
    {
        if (IsBusy)
        {
            return;
        }

        isSaving = true;
        ClearMessages();

        try
        {
            await ConnectionStore.ClearAsync();
            await RestartWithMessage("Connection removed. Restarting BlogApp...");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to clear the connection settings");
            errorMessage = ForgetFailureMessage;
        }
        finally
        {
            isSaving = false;
        }
    }

    /// <summary>
    /// Leaves the settings screen without changing anything.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only offered while reconfiguring; a first run has nowhere to
    /// return to.</para>
    /// <para><b>Flow:</b> navigate to the sign-in screen.</para>
    /// <para><b>Side Effects:</b> Changes the route.</para>
    /// </remarks>
    public void GoBack()
    {
        NavigationManager.NavigateTo(LoginRoute);
    }

    /// <summary>
    /// Shows a restart notice and relaunches the application.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> If the relaunch cannot be performed the operator is told to
    /// restart manually rather than being left staring at a stale message, because the settings
    /// have already been persisted at that point.</para>
    /// <para><b>Flow:</b> render the notice → restart → report failure if it did not happen.</para>
    /// <para><b>Side Effects:</b> Terminates the process on success.</para>
    /// </remarks>
    /// <param name="message">The notice to display while the app relaunches.</param>
    /// <returns>A task that completes when the restart has been attempted.</returns>
    private async Task RestartWithMessage(string message)
    {
        restartMessage = message;
        StateHasChanged();

        var restarted = await AppRestarter.RestartAsync();
        if (!restarted)
        {
            restartMessage = "Settings saved. Close and reopen BlogApp for them to take effect.";
        }
    }

    /// <summary>
    /// Clears the success, failure and restart notices before a new action runs.
    /// </summary>
    private void ClearMessages()
    {
        successMessage = null;
        errorMessage = null;
        restartMessage = null;
    }
}
