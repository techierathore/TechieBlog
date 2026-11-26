namespace BlogApp.Services;

/// <summary>
/// Decides where BlogApp keeps everything it writes on this computer: the normal per-user folder,
/// or an isolated test profile folder named on the command line.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets BlogApp be started for automated checks without touching the owner's
/// own setup. The owner's saved connection normally points at the live site database; a check must
/// neither read it nor overwrite it, and must create no file outside the folder it was given.
/// Added 2026-11-21 with REQ-UI-066…071, whose BlogApp checks need exactly that.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>BlogApp.exe --profile &lt;absolute folder&gt;</c> (or <c>--profile=&lt;folder&gt;</c>)
///         turns isolation on; anything else leaves BlogApp exactly as it was.</item>
///   <item>When isolated, <see cref="AppDataRoot"/> is that folder: logs, uploads and the
///         connection file live under it, and <c>ConnectionStore</c> never reads or writes the OS
///         credential store, so the owner's saved connection is invisible to this run.</item>
///   <item><see cref="ApplyWebViewDataFolder"/> points WebView2's own data (cache, local storage
///         with the sign-in token) into the profile too, before the web view is created.</item>
///   <item><see cref="RestartArguments"/> carries the switch across BlogApp's own restart after a
///         connection is saved, so the relaunched process stays isolated.</item>
/// </list>
///
/// <para><b>Dependencies:</b> none beyond the base class library.</para>
///
/// <para><b>Usage:</b> read by <c>MauiProgram</c>, <c>ConnectionStore</c>, <c>MediaMigrator</c> and
/// <c>AppRestarter</c>. A relative or missing folder is refused (isolation stays off and the
/// reason is logged by the caller), because a relative path would resolve against whatever
/// directory BlogApp happened to be started from.</para>
/// </remarks>
public static class DesktopProfile
{
    /// <summary>
    /// The command-line switch that names the isolated profile folder.
    /// </summary>
    public const string ProfileSwitch = "--profile";

    private const string WebViewDataFolderVariable = "WEBVIEW2_USER_DATA_FOLDER";

    private static readonly Lazy<string?> IsolatedRootValue =
        new(() => ReadProfile(Environment.GetCommandLineArgs()));

    /// <summary>
    /// The isolated profile folder, or <c>null</c> when BlogApp runs normally.
    /// </summary>
    public static string? IsolatedRoot => IsolatedRootValue.Value;

    /// <summary>
    /// True when this process was started with a valid <c>--profile</c> folder.
    /// </summary>
    public static bool IsIsolated => IsolatedRoot != null;

    /// <summary>
    /// The folder BlogApp writes its logs, uploads and connection file under.
    /// </summary>
    public static string AppDataRoot => IsolatedRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TechieBlog", "BlogApp");

    /// <summary>
    /// The arguments a restart must pass so the new process keeps the same profile.
    /// </summary>
    public static string RestartArguments => IsIsolated ? $"{ProfileSwitch} \"{IsolatedRoot}\"" : string.Empty;

    /// <summary>
    /// Finds the profile folder in a command line.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> accepts <c>--profile &lt;folder&gt;</c> and
    /// <c>--profile=&lt;folder&gt;</c>; the folder must be an absolute path.</para>
    /// <para><b>Side Effects:</b> none.</para>
    /// </remarks>
    /// <param name="args">The process arguments.</param>
    /// <returns>The full folder path, or <c>null</c>.</returns>
    public static string? ReadProfile(IReadOnlyList<string> args)
    {
        if (args == null)
            return null;

        for (var index = 0; index < args.Count; index++)
        {
            var arg = args[index] ?? string.Empty;
            string? value = null;
            if (arg.Equals(ProfileSwitch, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Count)
                value = args[index + 1];
            else if (arg.StartsWith(ProfileSwitch + "=", StringComparison.OrdinalIgnoreCase))
                value = arg[(ProfileSwitch.Length + 1)..];

            if (value == null)
                continue;

            value = value.Trim().Trim('"');
            return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null;
        }

        return null;
    }

    /// <summary>
    /// When isolated, points WebView2's data folder into the profile, before any web view exists.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> WebView2 keeps its cache and the page's local storage (which
    /// holds the sign-in token) in a user-data folder; without this it would use the normal per-user
    /// folder even in an isolated run.</para>
    /// <para><b>Side Effects:</b> creates the folder and sets a variable on this process only.</para>
    /// </remarks>
    public static void ApplyWebViewDataFolder()
    {
        if (!IsIsolated)
            return;

        var folder = Path.Combine(IsolatedRoot!, "WebView2");
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable(WebViewDataFolderVariable, folder);
    }
}
