using Serilog;
using TechieBlog.Configuration;

namespace TechieBlog.Tests.Configuration;

/// <summary>
/// REQ-NFR-036 — the rolling file sink bounds TOTAL log volume, not just per-file size.
/// </summary>
/// <remarks>
/// <para><b>What this pins, and why arithmetic was not enough.</b>
/// <see cref="LogFileSettingsTests"/> asserts that
/// <c>SizeLimitBytes * RetainedFileCountLimit</c> is computed correctly, but a correct product is
/// only a bound if Serilog actually enforces both factors. The defect this requirement closed was
/// precisely a configuration that looked bounded and was not: <c>retainedFileCountLimit</c> was set
/// and <c>fileSizeLimitBytes</c> was left at Serilog's 1 GB default with
/// <c>rollOnFileSizeLimit</c> false, so the sink neither rolled nor evicted — it silently STOPPED
/// WRITING at the ceiling. These tests therefore drive the real sink and look at the real files.</para>
///
/// <para>Tiny limits (a few KB) stand in for the shipped 10 MB so the tests stay fast; the mechanism
/// under test is identical, and the shipped numbers are asserted separately below. Everything is
/// written under <c>tests/.artifacts/harness/</c>, which is gitignored.</para>
/// </remarks>
public class LogVolumeBoundTests : IDisposable
{
    /// <summary>Per-file cap used by the behavioural tests: 2 KB.</summary>
    private const long TestSizeLimitBytes = 2048;

    /// <summary>Files retained by the behavioural tests.</summary>
    private const int TestRetainedFileCountLimit = 3;

    /// <summary>Throwaway directory this test's log files are written into.</summary>
    private readonly string logDirectory;

    /// <summary>
    /// Creates an empty, uniquely named log directory under the gitignored harness folder.
    /// </summary>
    public LogVolumeBoundTests()
    {
        logDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "log-volume-bound",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(logDirectory);
    }

    /// <summary>Removes the throwaway log directory.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(logDirectory))
                Directory.Delete(logDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Writes the given number of events through a sink configured exactly as the heads configure
    /// theirs, then closes it so every file is flushed and unlocked.
    /// </summary>
    /// <param name="eventCount">Number of events to write.</param>
    /// <param name="rollOnFileSizeLimit">Whether the sink rolls when it reaches the size cap.</param>
    private void WriteEvents(int eventCount, bool rollOnFileSizeLimit = true)
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "volume-.log"),
                rollingInterval: RollingInterval.Infinite,
                retainedFileCountLimit: TestRetainedFileCountLimit,
                fileSizeLimitBytes: TestSizeLimitBytes,
                rollOnFileSizeLimit: rollOnFileSizeLimit)
            .CreateLogger();

        for (var index = 0; index < eventCount; index++)
        {
            logger.Information("Filler event {Index} {Padding}", index, new string('x', 200));
        }

        logger.Dispose();
    }

    /// <summary>Total bytes currently occupied by the log directory.</summary>
    /// <returns>The sum of every file's length.</returns>
    private long TotalBytesOnDisk() =>
        new DirectoryInfo(logDirectory).GetFiles().Sum(file => file.Length);

    /// <summary>
    /// Enough traffic to fill many files leaves only the retained number behind — the eviction that
    /// turns a per-file cap into a disk cap.
    /// </summary>
    [Fact]
    public void SinkEvictsOldFilesBeyondTheRetentionLimit()
    {
        WriteEvents(eventCount: 400);

        var files = Directory.GetFiles(logDirectory);

        Assert.InRange(files.Length, 2, TestRetainedFileCountLimit);
    }

    /// <summary>
    /// Total bytes on disk stay within <c>SizeLimitBytes * RetainedFileCountLimit</c> however much
    /// is written — the acceptance criterion of REQ-NFR-036 stated as an observation, not a product.
    /// </summary>
    [Fact]
    public void TotalVolumeStaysWithinTheConfiguredBound()
    {
        WriteEvents(eventCount: 2000);

        var boundBytes = TestSizeLimitBytes * TestRetainedFileCountLimit;

        Assert.True(
            TotalBytesOnDisk() <= boundBytes,
            $"Log directory holds {TotalBytesOnDisk()} bytes, above the {boundBytes} byte bound.");
    }

    /// <summary>
    /// Writing far more than one file's worth actually produces several files — proof the size cap
    /// rolls rather than truncating, which is what makes retention reachable at all.
    /// </summary>
    [Fact]
    public void SizeLimitRollsRatherThanStoppingTheSink()
    {
        WriteEvents(eventCount: 400);

        Assert.True(
            Directory.GetFiles(logDirectory).Length > 1,
            "The sink never rolled, so retention would never evict and the bound would not hold.");
    }

    /// <summary>
    /// The defect this requirement closed, pinned: without <c>rollOnFileSizeLimit</c> the sink
    /// writes ONE file and goes silent at the cap, so a retention count bounds nothing.
    /// </summary>
    [Fact]
    public void WithoutRollOnFileSizeLimitTheSinkStopsWritingInsteadOfRolling()
    {
        WriteEvents(eventCount: 400, rollOnFileSizeLimit: false);

        Assert.Single(Directory.GetFiles(logDirectory));
    }

    /// <summary>
    /// The shipped web-host defaults bound disk at 100 MB, and say so in one place.
    /// </summary>
    [Fact]
    public void ShippedWebHostDefaultsBoundDiskAtOneHundredMegabytes()
    {
        Assert.Equal(
            100L * 1024 * 1024,
            LogFileSettings.DefaultSizeLimitBytes * LogFileSettings.DefaultRetainedFileCountLimit);
    }
}
