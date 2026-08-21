using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Proves the web head's rolling file sink actually enforces its size cap and can be shared by two
/// hosts at once (REQ-NFR-029).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A 341 MB file existed under a configured 50 MB cap, so reading the
/// configuration back proves nothing. Every test here writes past the limit against a real sink on
/// a real temporary directory and then looks at the bytes on disk, which is the only evidence the
/// requirement accepts.</para>
///
/// <para><b>Code Flow:</b> each test builds a logger with the same sink arguments
/// <c>Program.cs</c> uses — daily rolling interval, size cap, <c>rollOnFileSizeLimit</c> and
/// <c>shared</c> — writes enough events to exceed the cap, disposes the logger and inspects the
/// resulting files.</para>
///
/// <para><b>Dependencies:</b> <c>Serilog</c> and <c>Serilog.Sinks.File</c>, pinned to the same
/// versions the heads use, plus a temporary directory per test.</para>
///
/// <para><b>Usage:</b> <c>dotnet test</c>. Each test cleans up its own directory.</para>
/// </remarks>
public class RollingLogFileTests
{
    private const long SmallSizeLimitBytes = 8 * 1024;

    /// <summary>
    /// Writing well past the configured cap rolls into a numbered companion file instead of growing
    /// one file without bound — the failure that let a single day's log reach 341 MB.
    /// </summary>
    [Fact]
    public void SizeCapRollsTheFileWhenExceeded()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            WriteEvents(directory, shared: false, eventCount: 400);

            var files = Directory.GetFiles(directory, "techieblog-*.log");
            Assert.True(files.Length > 1,
                $"expected the sink to roll, but only {files.Length} file(s) were produced");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// No individual file is allowed to run far past the cap, so disk usage stays bounded by
    /// the limit multiplied by the retained file count rather than by how long the host has been up.
    /// </summary>
    [Fact]
    public void NoSingleFileGrowsFarBeyondTheCap()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            WriteEvents(directory, shared: false, eventCount: 400);

            foreach (var file in Directory.GetFiles(directory, "techieblog-*.log"))
            {
                var length = new FileInfo(file).Length;

                // The sink checks the limit before writing, so the last event may take the file
                // slightly over. Anything approaching twice the cap would mean it is not enforced.
                Assert.True(length < SmallSizeLimitBytes * 2,
                    $"{Path.GetFileName(file)} reached {length} bytes against a {SmallSizeLimitBytes} byte cap");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The size cap still rolls when the sink is opened in shared mode, which settles the question of
    /// whether the two options are mutually exclusive in this version of Serilog.Sinks.File: they are
    /// not, so the head does not have to give one of them up.
    /// </summary>
    [Fact]
    public void SizeCapStillRollsWhenTheFileIsShared()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            WriteEvents(directory, shared: true, eventCount: 400);

            var files = Directory.GetFiles(directory, "techieblog-*.log");
            Assert.True(files.Length > 1,
                $"expected a shared sink to roll on size, but only {files.Length} file(s) were produced");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Two loggers open at the same time, standing in for two hosts, append to one shared file
    /// instead of the second silently starting its own _001 sequence and diverging.
    /// </summary>
    [Fact]
    public void TwoConcurrentWritersShareOneFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            using (var first = BuildLogger(directory, shared: true, sizeLimitBytes: 8 * 1024 * 1024))
            using (var second = BuildLogger(directory, shared: true, sizeLimitBytes: 8 * 1024 * 1024))
            {
                for (var index = 0; index < 20; index++)
                {
                    first.Information("host one event {Index}", index);
                    second.Information("host two event {Index}", index);
                }
            }

            var files = Directory.GetFiles(directory, "techieblog-*.log");
            Assert.Single(files);

            var contents = File.ReadAllText(files[0]);
            Assert.Contains("host one event 19", contents);
            Assert.Contains("host two event 19", contents);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A shared writer that rolls on size keeps every event: the roll is not a place where the
    /// second host's lines can be silently dropped or overwritten.
    /// </summary>
    [Fact]
    public void SharedWritersLoseNoEventsAcrossARoll()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            // Retention is deliberately raised out of the way: the head keeps 7 files, and at an
            // 8 KB cap this test rolls far more than that, so the default would delete the early
            // files and make a working sink look lossy.
            using (var first = BuildLogger(directory, shared: true, sizeLimitBytes: SmallSizeLimitBytes, retainedFileCountLimit: 500))
            using (var second = BuildLogger(directory, shared: true, sizeLimitBytes: SmallSizeLimitBytes, retainedFileCountLimit: 500))
            {
                var payload = new string('x', 200);
                for (var index = 0; index < 200; index++)
                {
                    first.Information("host one {Index} {Payload}", index, payload);
                    second.Information("host two {Index} {Payload}", index, payload);
                }
            }

            var contents = string.Concat(
                Directory.GetFiles(directory, "techieblog-*.log").Select(File.ReadAllText));

            Assert.Equal(200, CountOccurrences(contents, "host one "));
            Assert.Equal(200, CountOccurrences(contents, "host two "));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Counts non-overlapping occurrences of a marker in the concatenated log text.
    /// </summary>
    /// <param name="text">The text to scan.</param>
    /// <param name="marker">The marker to count.</param>
    /// <returns>The number of occurrences.</returns>
    private static int CountOccurrences(string text, string marker)
    {
        var count = 0;
        var index = text.IndexOf(marker, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
        }

        return count;
    }

    /// <summary>
    /// Writes enough events through a real sink to take the file past the configured cap.
    /// </summary>
    /// <param name="directory">The directory the sink writes into.</param>
    /// <param name="shared">Whether the file is opened for shared write.</param>
    /// <param name="eventCount">How many events to emit.</param>
    private static void WriteEvents(string directory, bool shared, int eventCount)
    {
        using var logger = BuildLogger(directory, shared, SmallSizeLimitBytes);
        var payload = new string('x', 200);
        for (var index = 0; index < eventCount; index++)
        {
            logger.Information("event {Index} {Payload}", index, payload);
        }
    }

    /// <summary>
    /// Builds a logger configured exactly as the web head's file sink is.
    /// </summary>
    /// <param name="directory">The directory the sink writes into.</param>
    /// <param name="shared">Whether the file is opened for shared write.</param>
    /// <param name="sizeLimitBytes">The per-file size cap.</param>
    /// <param name="retainedFileCountLimit">How many rolled files to keep; the head keeps 7.</param>
    /// <returns>A disposable logger.</returns>
    private static Logger BuildLogger(
        string directory, bool shared, long sizeLimitBytes, int retainedFileCountLimit = 7) =>
        new LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Information)
            .WriteTo.File(
                path: Path.Combine(directory, "techieblog-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainedFileCountLimit,
                fileSizeLimitBytes: sizeLimitBytes,
                rollOnFileSizeLimit: true,
                shared: shared,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

    /// <summary>
    /// Creates an empty temporary directory for one test to write logs into.
    /// </summary>
    /// <returns>The directory path.</returns>
    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"techieblog-logtest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
