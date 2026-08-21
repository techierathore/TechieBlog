using Microsoft.Extensions.Configuration;
using TechieBlog.Configuration;

namespace TechieBlog.Tests.Configuration;

/// <summary>
/// REQ-NFR-029 — where the rolling log file goes, and how much disk it may ever occupy.
/// </summary>
/// <remarks>
/// Two defects are pinned here. The first is the two-log-folder mess: a bare relative sink path
/// resolves against the process working directory, which differs between <c>dotnet run --project</c>
/// (project folder), the built executable (wherever it was launched from) and a container
/// (<c>WORKDIR</c>) — measured on 2026-08-10 as 6.2 MB in the repository-root <c>logs/</c> and
/// 305 MB in <c>source/TechieBlog/logs/</c> for one application on one day. The second is that the
/// old settings capped a FILE (50 MB) and retained 7 of them, bounding disk at 350 MB while
/// appearing to be a limit. These tests assert the anchor and the arithmetic directly.
/// </remarks>
public class LogFileSettingsTests
{
    private static readonly string AnchorPath =
        Path.Combine(Path.GetTempPath(), "techieblog-log-anchor");

    /// <summary>
    /// Builds an in-memory configuration from the supplied values, omitting any that is null.
    /// </summary>
    /// <param name="values">Configuration entries to publish.</param>
    /// <returns>A configuration root carrying only the supplied values.</returns>
    private static IConfiguration BuildConfiguration(params (string Key, string? Value)[] values)
    {
        var data = values
            .Where(entry => entry.Value != null)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    /// <summary>
    /// With nothing configured the log directory hangs off the supplied anchor, not the working
    /// directory — which is the whole fix for the two-folder defect.
    /// </summary>
    [Fact]
    public void ResolveAnchorsTheDefaultDirectoryOnTheSuppliedPath()
    {
        var settings = LogFileSettings.Resolve(BuildConfiguration(), AnchorPath);

        Assert.Equal(
            Path.Combine(AnchorPath, LogFileSettings.DefaultFolderName),
            settings.DirectoryPath);
    }

    /// <summary>
    /// The same configuration resolved from two different working directories produces one path —
    /// the property the previous relative sink path did not have.
    /// </summary>
    [Fact]
    public void ResolveIsIndependentOfTheWorkingDirectory()
    {
        var configuration = BuildConfiguration();
        var originalWorkingDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            var fromTemp = LogFileSettings.Resolve(configuration, AnchorPath);

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            var fromBaseDirectory = LogFileSettings.Resolve(configuration, AnchorPath);

            Assert.Equal(fromTemp.FilePathTemplate, fromBaseDirectory.FilePathTemplate);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalWorkingDirectory);
        }
    }

    /// <summary>
    /// An explicitly configured absolute directory is honoured verbatim, which is how a container
    /// puts its logs on a mounted volume.
    /// </summary>
    [Fact]
    public void ResolveHonoursAnAbsoluteConfiguredDirectory()
    {
        var configured = Path.Combine(Path.GetTempPath(), "techieblog-configured-logs");
        var settings = LogFileSettings.Resolve(
            BuildConfiguration((LogFileSettings.PathKey, configured)), AnchorPath);

        Assert.Equal(Path.GetFullPath(configured), settings.DirectoryPath);
    }

    /// <summary>
    /// A relative configured directory resolves against the anchor, never against the working
    /// directory — a relative setting must not reintroduce the defect the anchor exists to remove.
    /// </summary>
    [Fact]
    public void ResolveResolvesARelativeConfiguredDirectoryAgainstTheAnchor()
    {
        var settings = LogFileSettings.Resolve(
            BuildConfiguration((LogFileSettings.PathKey, "diagnostics")), AnchorPath);

        Assert.Equal(Path.Combine(AnchorPath, "diagnostics"), settings.DirectoryPath);
    }

    /// <summary>
    /// The file template lives inside the resolved directory and carries the shared file-name stem
    /// Serilog appends its date and sequence to.
    /// </summary>
    [Fact]
    public void ResolveComposesTheFileTemplateInsideTheDirectory()
    {
        var settings = LogFileSettings.Resolve(BuildConfiguration(), AnchorPath);

        Assert.Equal(
            Path.Combine(settings.DirectoryPath, LogFileSettings.FileNameTemplate),
            settings.FilePathTemplate);
    }

    /// <summary>
    /// The shipped defaults bound total disk at 100 MB per host — the number the previous
    /// configuration never stated and which turned out to be 350 MB.
    /// </summary>
    [Fact]
    public void WorstCaseTotalIsTheProductOfTheTwoLimits()
    {
        var settings = LogFileSettings.Resolve(BuildConfiguration(), AnchorPath);

        Assert.Equal(LogFileSettings.DefaultSizeLimitBytes, settings.SizeLimitBytes);
        Assert.Equal(LogFileSettings.DefaultRetainedFileCountLimit, settings.RetainedFileCountLimit);
        Assert.Equal(100L * 1024 * 1024, settings.WorstCaseTotalBytes);
    }

    /// <summary>
    /// Raising either limit raises the bound by exactly that factor, so the arithmetic an operator
    /// has to do is multiplication and nothing else.
    /// </summary>
    [Fact]
    public void WorstCaseTotalTracksConfiguredLimits()
    {
        var settings = LogFileSettings.Resolve(
            BuildConfiguration(
                (LogFileSettings.SizeLimitBytesKey, "1048576"),
                (LogFileSettings.RetainedFileCountLimitKey, "3")),
            AnchorPath);

        Assert.Equal(3L * 1024 * 1024, settings.WorstCaseTotalBytes);
    }

    /// <summary>
    /// A non-positive size cap would disable rolling altogether, so it falls back to the default
    /// rather than being honoured into an unbounded log.
    /// </summary>
    [Fact]
    public void ResolveRejectsANonPositiveSizeLimit()
    {
        var settings = LogFileSettings.Resolve(
            BuildConfiguration((LogFileSettings.SizeLimitBytesKey, "0")), AnchorPath);

        Assert.Equal(LogFileSettings.DefaultSizeLimitBytes, settings.SizeLimitBytes);
    }

    /// <summary>
    /// A non-positive retention count would leave nothing on disk, so it falls back too.
    /// </summary>
    [Fact]
    public void ResolveRejectsANonPositiveRetentionCount()
    {
        var settings = LogFileSettings.Resolve(
            BuildConfiguration((LogFileSettings.RetainedFileCountLimitKey, "-1")), AnchorPath);

        Assert.Equal(LogFileSettings.DefaultRetainedFileCountLimit, settings.RetainedFileCountLimit);
    }

    /// <summary>
    /// The file sink is on unless a deployment turns it off, so a fresh clone still gets a log file.
    /// </summary>
    [Fact]
    public void FileSinkIsEnabledByDefault()
    {
        Assert.True(LogFileSettings.Resolve(BuildConfiguration(), AnchorPath).Enabled);
    }

    /// <summary>
    /// The container sets <c>LogFileEnabled=false</c>; a disabled sink occupies no disk at all,
    /// which is what the worst-case figure must then report.
    /// </summary>
    [Fact]
    public void DisabledFileSinkOccupiesNoDisk()
    {
        var settings = LogFileSettings.Resolve(
            BuildConfiguration((LogFileSettings.EnabledKey, "false")), AnchorPath);

        Assert.False(settings.Enabled);
        Assert.Equal(0L, settings.WorstCaseTotalBytes);
    }
}
