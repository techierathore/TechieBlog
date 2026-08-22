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
/// it. Since REQ-FN-062 it captures the head's MEDIA connection as well — images are not stored in
/// the database, so a desktop head that knows only where the database lives writes every upload to
/// its own machine and the website never sees the picture.</para>
/// <para><b>Code Flow:</b> the screen binds to <see cref="Settings"/> → <see cref="TestConnection"/>
/// probes the server and <see cref="TestMediaLocation"/> probes the uploads folder →
/// <see cref="SaveAndContinue"/> re-probes the database and persists, then relaunches the
/// app so the new connection reaches the DI graph → <see cref="ForgetConnection"/> clears the
/// stored value and relaunches into first-run state.</para>
/// <para><b>Dependencies:</b> <see cref="IConnectionStore"/>, <see cref="ConnectionProbe"/>,
/// <see cref="MediaLocationProbe"/>, <see cref="AppRestarter"/>, <see cref="ConnectionContext"/>.</para>
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
    /// <remarks>
    /// BlogApp's own entry point rather than <c>/login</c> (REQ-UI-063): an operator who cancels
    /// out of the settings screen is normally still signed in, and sending them to the sign-in
    /// screen in that state lands them on the PUBLIC blog through <c>LoginPage</c>'s
    /// already-signed-in branch. <see cref="DesktopStart"/> returns them to the admin surface they
    /// came from, and still sends them to sign in when they are not.
    /// </remarks>
    private const string LoginRoute = DesktopStart.Route;

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

    /// <summary>Alert heading shown above <c>successMessage</c> for a database probe or a save.</summary>
    private const string ConnectionSuccessTitle = "Connection OK";

    /// <summary>Alert heading shown above <c>errorMessage</c> for a database probe or a save.</summary>
    private const string ConnectionFailureTitle = "Connection failed";

    /// <summary>Alert heading shown above <c>successMessage</c> for a media probe.</summary>
    private const string MediaSuccessTitle = "Media destination OK";

    /// <summary>Alert heading shown above <c>errorMessage</c> for a media probe.</summary>
    private const string MediaFailureTitle = "Images would not reach the site";

    /// <summary>Alert heading shown above <c>successMessage</c> for the media migration.</summary>
    private const string MigrationSuccessTitle = "Images sent to the server";

    /// <summary>Alert heading shown above <c>errorMessage</c> for the media migration.</summary>
    private const string MigrationFailureTitle = "Could not send the images";

    private bool isTesting;
    private bool isTestingMedia;
    private bool isMigrating;
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
    /// The probe that proves the configured media folder can actually be written to (REQ-FN-062).
    /// </summary>
    [Inject]
    public MediaLocationProbe MediaLocationProbe { get; set; }

    /// <summary>
    /// Sends images already on this machine up to the server over the configured SSH connection
    /// (REQ-FN-062).
    /// </summary>
    [Inject]
    public MediaMigrator MediaMigrator { get; set; }

    /// <summary>
    /// Opens the OS file and folder pickers, so a path on this machine is chosen rather than typed.
    /// </summary>
    [Inject]
    public FilePickerService FilePickerService { get; set; }

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
    /// Heading rendered above whichever result message the screen is currently showing.
    /// </summary>
    /// <remarks>
    /// One alert pair serves both probes, so the heading has to name the probe that ran - a media
    /// folder that could not be written to must not be announced as "Connection failed".
    /// </remarks>
    public string ResultTitle { get; private set; } = ConnectionSuccessTitle;

    /// <summary>
    /// Heading rendered above the failure alert. See <see cref="ResultTitle"/>.
    /// </summary>
    public string FailureTitle { get; private set; } = ConnectionFailureTitle;

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
    /// Folder whose contents the migration action sends to the server.
    /// </summary>
    /// <remarks>
    /// Defaults to the desktop head's own uploads folder, which is where every upload made before
    /// the SFTP transport existed was written — so the common case needs no browsing at all.
    /// </remarks>
    public string MigrateFromFolder { get; set; } = MediaMigrator.DefaultLocalUploadsFolder();

    /// <summary>
    /// The SSH port as text, because the underlying input control binds strings (REQ-FN-062).
    /// </summary>
    /// <remarks>
    /// A non-numeric value leaves <see cref="ConnectionSettings.SftpPort"/> at zero, which
    /// <see cref="ConnectionSettings.HasMediaLocation"/> rejects, so the probe refuses rather than
    /// attempting a connection to a port that cannot exist.
    /// </remarks>
    public string SftpPortText
    {
        get => Settings.SftpPort > 0 ? Settings.SftpPort.ToString() : string.Empty;
        set => Settings.SftpPort = int.TryParse(value, out var parsedPort) ? parsedPort : 0;
    }

    /// <summary>
    /// Indicates whether the screen is being used to change an existing connection.
    /// </summary>
    public bool IsReconfiguring =>
        NavigationManager.Uri.Contains(SettingsRoute, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Indicates whether a probe, a save or a restart is in flight.
    /// </summary>
    public bool IsBusy => isTesting || isTestingMedia || isMigrating || isSaving;

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
        //
        // The two site secrets are carried across as well (added 2026-08-22 with REQ-FN-062).
        // They were previously omitted while the password beside them was copied, which made the
        // omission read as an oversight rather than a policy - and it had a real cost: SaveAndContinue
        // refuses a blob without usable secrets, so ANY visit to the settings screen, including one
        // that only wanted to change the media folder, meant re-entering both site keys by hand.
        // Nothing is weakened by copying them: the values are already decrypted in this process
        // (AppSecrets was initialised from them at startup) and both fields render as passwords.
        Settings = new ConnectionSettings
        {
            Host = existing.Host,
            Port = existing.Port,
            Database = existing.Database,
            Username = existing.Username,
            Password = existing.Password,
            SslMode = existing.SslMode,
            JwtSigningKey = existing.JwtSigningKey,
            AppEncryptionKey = existing.AppEncryptionKey,
            MediaTransport = string.IsNullOrWhiteSpace(existing.MediaTransport)
                ? MediaTransports.None
                : existing.MediaTransport,
            SftpHost = existing.SftpHost,
            SftpPort = existing.SftpPort > 0 ? existing.SftpPort : ConnectionSettings.DefaultSftpPort,
            SftpUsername = existing.SftpUsername,
            SftpPassword = existing.SftpPassword,
            SftpPrivateKeyPath = existing.SftpPrivateKeyPath,
            SftpPrivateKeyPassphrase = existing.SftpPrivateKeyPassphrase,
            SftpUploadsPath = existing.SftpUploadsPath,
            MediaRootPath = existing.MediaRootPath,
            SiteBaseUrl = existing.SiteBaseUrl
        };
    }

    /// <summary>
    /// Proves the configured media folder is reachable and writable, without storing anything.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The media half of the screen needs its own answer for a reason
    /// the database half does not have: a wrong database gives an error the moment a screen queries
    /// it, whereas a wrong media folder is SILENT — <c>BlogImageService</c> writes happily into
    /// whatever directory it is handed, so the upload succeeds and only the website is missing the
    /// picture. Proving the write here is what converts that silence into something the operator can
    /// see while they are still on the screen that can fix it.</para>
    /// <para>Unlike <see cref="SaveAndContinue"/>, which re-probes the database before persisting, a
    /// blank media folder is NOT a reason to refuse a save: it is the supported "uploads stay on this
    /// machine" configuration and is what every existing installation already has.</para>
    /// <para><b>Flow:</b> clear previous messages → probe → record success or failure.</para>
    /// <para><b>Side Effects:</b> May create the uploads directory; writes and deletes one file.</para>
    /// </remarks>
    /// <returns>A task that completes when the probe has finished.</returns>
    public async Task TestMediaLocation()
    {
        if (IsBusy)
        {
            return;
        }

        isTestingMedia = true;
        ClearMessages();
        ResultTitle = MediaSuccessTitle;
        FailureTitle = MediaFailureTitle;

        try
        {
            var probeResult = await MediaLocationProbe.TestAsync(Settings);
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
            isTestingMedia = false;
        }
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
    /// Chooses the SSH private key with the OS file picker.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A cancelled dialog leaves the field alone rather than clearing
    /// it — cancelling means "never mind", not "erase what I had".</para>
    /// <para><b>Side Effects:</b> Opens a modal dialog.</para>
    /// </remarks>
    /// <returns>A task that completes when the dialog has closed.</returns>
    public async Task BrowseForPrivateKey()
    {
        var chosen = await FilePickerService.PickFileAsync("Choose your SSH private key");
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            Settings.SftpPrivateKeyPath = chosen;
        }
    }

    /// <summary>
    /// Chooses the folder of images to send to the server.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> As with the key picker, a cancelled dialog changes nothing.</para>
    /// <para><b>Side Effects:</b> Opens a modal dialog.</para>
    /// </remarks>
    /// <returns>A task that completes when the dialog has closed.</returns>
    public async Task BrowseForMigrationFolder()
    {
        var chosen = await FilePickerService.PickFolderAsync("Choose the folder holding your images");
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            MigrateFromFolder = chosen;
        }
    }

    /// <summary>
    /// Sends images already on this machine up to the server (REQ-FN-062).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Repairs the rows created before the SFTP transport existed. Those
    /// rows already point at <c>/uploads/…</c> on the server, so only the FILES are misplaced;
    /// copying them up makes the existing rows resolve without a single database write or a
    /// re-upload. Deliberately reuses the connection the operator has just proved with
    /// <c>Test</c>, rather than sending them to a terminal to restate the same credentials.</para>
    /// <para><b>Flow:</b> busy guard → clear messages → migrate → report.</para>
    /// <para><b>Side Effects:</b> Writes files on the server.</para>
    /// </remarks>
    /// <returns>A task that completes when the migration has finished.</returns>
    public async Task MigrateLocalMedia()
    {
        if (IsBusy)
        {
            return;
        }

        isMigrating = true;
        ClearMessages();
        ResultTitle = MigrationSuccessTitle;
        FailureTitle = MigrationFailureTitle;

        try
        {
            var result = await MediaMigrator.MigrateAsync(Settings, MigrateFromFolder);
            if (result.IsSuccess)
            {
                successMessage = result.Data;
            }
            else
            {
                errorMessage = result.ErrorMessage;
            }
        }
        finally
        {
            isMigrating = false;
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
        ResultTitle = ConnectionSuccessTitle;
        FailureTitle = ConnectionFailureTitle;
    }
}
