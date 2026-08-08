using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BlogApp.Services;

/// <summary>
/// Relaunches BlogApp so a changed database connection takes effect.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The BlogEngine repositories are constructed with the connection string at
/// dependency-registration time — that is deliberate, and it is the same graph the web host builds
/// (<c>BlogSvcInitializer</c>). A MAUI application builds its service provider once, during
/// <c>CreateMauiApp</c>, so changing the connection string means rebuilding the process rather than
/// the container. Restarting keeps BlogApp's DI graph byte-for-byte identical to the website's
/// instead of forking a second, drift-prone registration path (REQ-FN-046, REQ-FN-047).</para>
/// <para><b>Code Flow:</b> connection-setup screen saves the settings → calls
/// <see cref="RestartAsync"/> → a fresh process starts and reads the new settings at startup →
/// the current process quits.</para>
/// <para><b>Dependencies:</b> <see cref="Process"/>, <c>Microsoft.Maui.Controls.Application</c>.</para>
/// <para><b>Usage:</b> Registered as a singleton; only the connection screens call it.</para>
/// </remarks>
public class AppRestarter
{
    /// <summary>Grace period that lets the UI paint its "restarting" message before the window closes.</summary>
    private static readonly TimeSpan QuitDelay = TimeSpan.FromMilliseconds(600);

    private readonly ILogger<AppRestarter> logger;

    /// <summary>
    /// Creates the restarter.
    /// </summary>
    /// <param name="logger">Structured logger for restart attempts and failures.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <c>null</c>.</exception>
    public AppRestarter(ILogger<AppRestarter> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts a second instance of BlogApp and closes this one.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Uses <see cref="Environment.ProcessPath"/> so the relaunch works
    /// for both the unpackaged desktop build and a future packaged one. If the new process cannot be
    /// started the current one is left running and the caller is told, because quitting without a
    /// successor would strand the operator with no window.</para>
    /// <para><b>Flow:</b> resolve the executable → start it → wait out the grace period → quit.</para>
    /// <para><b>Side Effects:</b> Spawns a process and terminates the application.</para>
    /// </remarks>
    /// <returns>
    /// <c>true</c> when the successor process started and the shutdown was requested;
    /// <c>false</c> when the restart could not be performed.
    /// </returns>
    public async Task<bool> RestartAsync()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            logger.LogError("Cannot restart BlogApp: the executable path is unknown");
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(executablePath) { UseShellExecute = true });
            logger.LogInformation("Relaunching BlogApp from {ExecutablePath}", executablePath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to relaunch BlogApp from {ExecutablePath}", executablePath);
            return false;
        }

        await Task.Delay(QuitDelay).ConfigureAwait(false);

        // Quit must be raised on the UI thread: called from a background continuation it is
        // silently ignored and the outgoing instance keeps its window open alongside the new one.
        MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit());
        return true;
    }
}
