using Microsoft.Extensions.Configuration;

namespace TechieBlog.Configuration;

/// <summary>
/// Resolves where the rolling log file is written and how much disk it may ever occupy
/// (REQ-NFR-013, REQ-NFR-029).
/// </summary>
/// <remarks>
/// <para><b>THE DEFECT THIS REPLACES — two log folders for one application.</b> The sink path was
/// the bare relative string <c>logs/techieblog-.log</c>. A relative sink path resolves against the
/// process WORKING DIRECTORY, and this repository is started three different ways:</para>
/// <list type="bullet">
///   <item><c>dotnet run --project source/TechieBlog/TechieBlog.csproj</c> sets the child's working
///     directory to the PROJECT folder → <c>source/TechieBlog/logs/</c>.</item>
///   <item>The built executable, launched from the repository root → <c>logs/</c> at the root.</item>
///   <item>A container with <c>WORKDIR /app</c> → <c>/app/logs</c>, inside the image layer.</item>
/// </list>
/// <para>Measured on 2026-08-10, before this change: <b>6.2 MB</b> in the repository-root
/// <c>logs/</c> and <b>305 MB</b> in <c>source/TechieBlog/logs/</c>. Same application, same day, two
/// destinations, and nothing in the log said which one an operator should be reading.</para>
///
/// <para><b>The fix is to anchor, not to guess.</b> The path is resolved against
/// <see cref="AppContext.BaseDirectory"/> — the folder the assembly was loaded from — which is
/// IDENTICAL however the host is launched and from whatever working directory. It is deliberately
/// NOT <c>IHostEnvironment.ContentRootPath</c>: the host's content root defaults to
/// <see cref="Directory.GetCurrentDirectory()"/>, so anchoring on it would have re-created exactly
/// the bug above under a different name. <see cref="PathKey"/> overrides the anchor with an explicit
/// directory when a deployment wants its logs on a mounted volume.</para>
///
/// <para><b>Bounding the VOLUME, which the previous settings did not.</b> The old configuration
/// capped each file at 50 MB and then rolled, keeping 7 — a worst case of 350 MB, and the 305 MB
/// measured above is that bound working exactly as written. Capping a file is not capping a disk.
/// The defaults here are 10 MB × 10 files = <b>100 MB worst case per host</b>, which
/// <see cref="WorstCaseTotalBytes"/> states in one place so nobody has to multiply it themselves. A
/// deployment may raise either number; the product of the two is the number that matters.</para>
///
/// <para><b>Development is LOUD ON PURPOSE and that is not the thing to fix.</b> Blazor's
/// render-tree and SignalR categories at Debug cost roughly 61 KB of log per request against 124
/// bytes in Production, and that detail is what makes a circuit defect diagnosable at all. It stays.
/// The bound above is what stops it filling a disk, which is the actual problem — an unbounded
/// verbose logger and a bounded verbose logger are very different objects.</para>
///
/// <para><b>Code Flow:</b> <c>Program.cs</c> calls <see cref="Resolve"/> against the bootstrap
/// configuration before the host exists, and passes the result straight to Serilog's file sink.
/// <see cref="Enabled"/> gates whether the sink is attached at all.</para>
///
/// <para><b>Dependencies:</b> <see cref="IConfiguration"/> only; no host, no logger.</para>
///
/// <para><b>Usage:</b> In a container set <c>LogFileEnabled=false</c> — Docker captures stdout and a
/// file written inside the container lands in an ephemeral layer that a redeploy discards. Set
/// <c>LogFilePath</c> to a mounted directory instead if a file is genuinely wanted.</para>
/// </remarks>
public sealed class LogFileSettings
{
    /// <summary>Configuration path switching the rolling file sink on or off.</summary>
    public const string EnabledKey = "LogFile:Enabled";

    /// <summary>Configuration path of the DIRECTORY log files are written into.</summary>
    /// <remarks>
    /// A directory, not a file name: the file name carries Serilog's date and sequence suffixes and
    /// is not a deployment's business. A relative value is resolved against the anchor directory.
    /// </remarks>
    public const string PathKey = "LogFile:Path";

    /// <summary>Configuration path of the per-file size cap in bytes.</summary>
    public const string SizeLimitBytesKey = "LogFile:SizeLimitBytes";

    /// <summary>Configuration path of the number of rolled files kept.</summary>
    public const string RetainedFileCountLimitKey = "LogFile:RetainedFileCountLimit";

    /// <summary>Configuration path of the shared-write flag.</summary>
    public const string SharedKey = "LogFile:Shared";

    /// <summary>Folder created under the anchor when no explicit directory is configured.</summary>
    public const string DefaultFolderName = "logs";

    /// <summary>File name template; Serilog inserts the date and sequence before the extension.</summary>
    public const string FileNameTemplate = "techieblog-.log";

    /// <summary>Per-file cap when none is configured: 10 MB.</summary>
    public const long DefaultSizeLimitBytes = 10L * 1024 * 1024;

    /// <summary>Files retained when no limit is configured.</summary>
    /// <remarks>
    /// Ten 10 MB files is 100 MB, which is a bound a small VPS can absorb without thinking about it,
    /// and enough history that a defect noticed the next morning is still on disk.
    /// </remarks>
    public const int DefaultRetainedFileCountLimit = 10;

    /// <summary>
    /// Creates a resolved settings instance.
    /// </summary>
    /// <param name="enabled">Whether the file sink is attached.</param>
    /// <param name="directoryPath">Absolute directory log files are written into.</param>
    /// <param name="filePathTemplate">Absolute path handed to Serilog's file sink.</param>
    /// <param name="sizeLimitBytes">Per-file size cap in bytes.</param>
    /// <param name="retainedFileCountLimit">Number of rolled files kept.</param>
    /// <param name="shared">Whether a second process may append to the same file.</param>
    private LogFileSettings(
        bool enabled,
        string directoryPath,
        string filePathTemplate,
        long sizeLimitBytes,
        int retainedFileCountLimit,
        bool shared)
    {
        Enabled = enabled;
        DirectoryPath = directoryPath;
        FilePathTemplate = filePathTemplate;
        SizeLimitBytes = sizeLimitBytes;
        RetainedFileCountLimit = retainedFileCountLimit;
        Shared = shared;
    }

    /// <summary>Whether the rolling file sink should be attached at all.</summary>
    public bool Enabled { get; }

    /// <summary>Absolute directory the log files live in.</summary>
    public string DirectoryPath { get; }

    /// <summary>Absolute path template passed to Serilog's file sink.</summary>
    public string FilePathTemplate { get; }

    /// <summary>Per-file size cap in bytes, after which the sink rolls within the day.</summary>
    public long SizeLimitBytes { get; }

    /// <summary>Number of rolled files retained; older files are deleted.</summary>
    public int RetainedFileCountLimit { get; }

    /// <summary>Whether a second host instance appends to the same file.</summary>
    public bool Shared { get; }

    /// <summary>
    /// The most disk this host's logs can ever occupy, in bytes.
    /// </summary>
    /// <remarks>
    /// The number an operator actually needs, and the one the old configuration never stated:
    /// per-file cap × files retained, or zero when the sink is off.
    /// </remarks>
    public long WorstCaseTotalBytes => Enabled ? SizeLimitBytes * RetainedFileCountLimit : 0L;

    /// <summary>
    /// Resolves the file-sink settings from configuration against a fixed anchor directory.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A configured directory wins; a relative one is made absolute
    /// against <paramref name="anchorDirectoryPath"/> rather than against the working directory,
    /// which is the whole point of this type. A non-positive size or retention value is a
    /// misconfiguration that would either disable rolling or delete every file, so both fall back to
    /// the defaults instead of being honoured.</para>
    /// <para><b>Flow:</b> read the switch → resolve the directory → clamp the two limits → compose
    /// the file template.</para>
    /// <para><b>Side Effects:</b> None; pure. It does not create the directory — Serilog does that
    /// when it opens the first file.</para>
    /// </remarks>
    /// <param name="configuration">Configuration to read the <c>LogFile</c> section from.</param>
    /// <param name="anchorDirectoryPath">Directory relative paths resolve against; pass
    /// <see cref="AppContext.BaseDirectory"/>.</param>
    /// <returns>The resolved settings.</returns>
    /// <exception cref="ArgumentNullException">Any argument is <c>null</c>.</exception>
    public static LogFileSettings Resolve(IConfiguration configuration, string anchorDirectoryPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(anchorDirectoryPath);

        var enabled = configuration.GetValue(EnabledKey, true);

        var configuredPath = configuration[PathKey];
        var directoryPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(anchorDirectoryPath, DefaultFolderName)
            : Path.GetFullPath(configuredPath.Trim(), anchorDirectoryPath);
        directoryPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));

        var sizeLimitBytes = configuration.GetValue(SizeLimitBytesKey, DefaultSizeLimitBytes);
        if (sizeLimitBytes <= 0)
        {
            sizeLimitBytes = DefaultSizeLimitBytes;
        }

        var retainedFileCountLimit = configuration.GetValue(
            RetainedFileCountLimitKey, DefaultRetainedFileCountLimit);
        if (retainedFileCountLimit <= 0)
        {
            retainedFileCountLimit = DefaultRetainedFileCountLimit;
        }

        return new LogFileSettings(
            enabled,
            directoryPath,
            Path.Combine(directoryPath, FileNameTemplate),
            sizeLimitBytes,
            retainedFileCountLimit,
            configuration.GetValue(SharedKey, true));
    }
}
