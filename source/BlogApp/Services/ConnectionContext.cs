namespace BlogApp.Services;

/// <summary>
/// The connection BlogApp booted with, exposed to the UI for status display.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The BlogEngine repositories are bound to a connection string when the DI
/// graph is registered, so the connection in force is fixed for the lifetime of the process. This
/// singleton records it once at startup and lets any screen show where the app is pointed — the
/// login screen's "Connected to" chip and the admin topbar's connection badge (REQ-UI-051).</para>
/// <para><b>Code Flow:</b> <c>MauiProgram</c> loads the stored settings → constructs this context →
/// registers it → screens inject it.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> <see cref="IsConfigured"/> is the gate that decides whether the app opens at
/// the connection-setup screen or at login.</para>
/// </remarks>
public class ConnectionContext
{
    /// <summary>Label shown when BlogApp has never been pointed at a database.</summary>
    public const string NotConfiguredLabel = "Not configured";

    /// <summary>
    /// Creates the context around the settings loaded at startup.
    /// </summary>
    /// <param name="settings">
    /// The stored settings, or <c>null</c> when this is a first run or the connection was cleared.
    /// </param>
    public ConnectionContext(ConnectionSettings settings)
    {
        Settings = settings;
    }

    /// <summary>
    /// The settings the running process was configured with, or <c>null</c> when unconfigured.
    /// </summary>
    public ConnectionSettings Settings { get; }

    /// <summary>
    /// Indicates whether BlogApp booted with a usable database connection.
    /// </summary>
    public bool IsConfigured => Settings != null && Settings.IsComplete();

    /// <summary>
    /// Password-free label describing the connected server, safe to render on screen.
    /// </summary>
    public string DisplayLabel => IsConfigured ? Settings.ToDisplayLabel() : NotConfiguredLabel;
}
