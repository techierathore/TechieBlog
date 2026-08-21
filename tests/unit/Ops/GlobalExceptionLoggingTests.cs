using Serilog;
using TechieBlog.Observability;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Unit tests for <see cref="GlobalExceptionLogging"/> (REQ-NFR-013, BRD-90).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The whole point of the requirement is that a crash outside the request
/// pipeline still reaches the rolling file. Asserting that a handler was subscribed would prove
/// nothing about that, so every test here configures a real Serilog logger with a real file sink in
/// a temporary directory, invokes the production handler, and then reads the file back off disk.
/// A handler that logs into an unflushed buffer during a crash writes nothing, and only reading the
/// file catches that.</para>
/// <para><b>Dependencies:</b> xUnit, Serilog with the file sink. No host, no database — the linked
/// production source file is compiled straight into this assembly (see the test csproj).</para>
/// <para><b>Note on isolation:</b> Serilog's <c>Log.Logger</c> is process-global, so these tests
/// live in one class; xUnit runs the tests of a single class sequentially, which is the isolation
/// this suite needs. No other suite in the repository touches the static facade.</para>
/// </remarks>
public class GlobalExceptionLoggingTests : IDisposable
{
    private readonly string logDirectory;

    /// <summary>
    /// Creates the temporary log directory this test class's sinks write into.
    /// </summary>
    public GlobalExceptionLoggingTests()
    {
        logDirectory = Path.Combine(Path.GetTempPath(), "techieblog-observability-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logDirectory);
    }

    /// <summary>
    /// An exception that escapes to the AppDomain is written to the rolling file with its message,
    /// its stack and the terminating flag, so an operator triaging a silent crash has something to
    /// read.
    /// </summary>
    [Fact]
    public void UnhandledExceptionReachesTheRollingFile()
    {
        ConfigureFileLogger();

        GlobalExceptionLogging.HandleUnhandledException(
            null,
            new UnhandledExceptionEventArgs(new InvalidOperationException("boom from a background thread"), false));

        Log.CloseAndFlush();
        var contents = ReadLog();

        Assert.Contains("Unhandled exception escaped to the AppDomain", contents);
        Assert.Contains("boom from a background thread", contents);
    }

    /// <summary>
    /// When the runtime reports it is terminating, the handler drains the sink itself: the entry is
    /// readable on disk without the test calling CloseAndFlush, which is the behaviour that makes a
    /// fatal crash diagnosable at all.
    /// </summary>
    [Fact]
    public void TerminatingCrashIsFlushedByTheHandler()
    {
        ConfigureFileLogger();

        GlobalExceptionLogging.HandleUnhandledException(
            null,
            new UnhandledExceptionEventArgs(new ApplicationException("fatal on the way out"), true));

        var contents = ReadLog();

        Assert.Contains("fatal on the way out", contents);
        // Serilog renders a bool scalar lower-cased, so the entry reads "(terminating: true)".
        Assert.Contains("terminating: true", contents);
    }

    /// <summary>
    /// A non-terminating notification must not close the logger: CloseAndFlush swaps in a silent
    /// instance, so flushing here would blind every later log call in a process that is still
    /// running. Logging after the handler must still produce output.
    /// </summary>
    [Fact]
    public void NonTerminatingCrashLeavesTheLoggerUsable()
    {
        ConfigureFileLogger();

        GlobalExceptionLogging.HandleUnhandledException(
            null,
            new UnhandledExceptionEventArgs(new InvalidOperationException("survivable"), false));
        Log.Information("Still logging after a non terminating crash");

        Log.CloseAndFlush();
        var contents = ReadLog();

        Assert.Contains("Still logging after a non terminating crash", contents);
    }

    /// <summary>
    /// A non-CLS exception object - anything that is not an <see cref="Exception"/> - is tolerated
    /// rather than throwing inside the handler, which would replace a diagnosable crash with an
    /// undiagnosable one.
    /// </summary>
    [Fact]
    public void NonExceptionPayloadIsStillRecorded()
    {
        ConfigureFileLogger();

        GlobalExceptionLogging.HandleUnhandledException(
            null,
            new UnhandledExceptionEventArgs("a bare string thrown from native code", false));

        Log.CloseAndFlush();
        var contents = ReadLog();

        Assert.Contains("Unhandled exception escaped to the AppDomain", contents);
    }

    /// <summary>
    /// A faulted task nobody awaited is logged at Error and, critically, marked observed - on a host
    /// running with the legacy ThrowUnobservedTaskExceptions setting an unclaimed exception kills
    /// the process on the finaliser thread.
    /// </summary>
    [Fact]
    public void UnobservedTaskExceptionIsLoggedAndObserved()
    {
        ConfigureFileLogger();
        var eventArgs = new UnobservedTaskExceptionEventArgs(
            new AggregateException(new TimeoutException("nobody awaited me")));

        GlobalExceptionLogging.HandleUnobservedTaskException(null, eventArgs);

        Log.CloseAndFlush();
        var contents = ReadLog();

        Assert.True(eventArgs.Observed);
        Assert.Contains("Unobserved task exception was collected by the finalizer", contents);
        Assert.Contains("nobody awaited me", contents);
    }

    /// <summary>
    /// Wiring twice does not subscribe twice: a duplicated subscription would log every crash twice
    /// and, worse, run the flush-and-close path twice on the way out.
    /// </summary>
    [Fact]
    public void WireIsIdempotent()
    {
        GlobalExceptionLogging.Wire();

        var wiredAgain = GlobalExceptionLogging.Wire();

        Assert.False(wiredAgain);
    }

    /// <summary>
    /// The real runtime event reaches the production handler once <see cref="GlobalExceptionLogging.Wire"/>
    /// has run: a faulted task is dropped without being awaited, the garbage collector finalises it,
    /// and the entry appears in the file. This is the end-to-end proof that the subscription itself
    /// works, not just the handler body.
    /// </summary>
    [Fact]
    public void WiredHandlerCatchesRealUnobservedTaskExceptions()
    {
        GlobalExceptionLogging.Wire();
        ConfigureFileLogger();

        DropFaultedTask();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Log.CloseAndFlush();
        var contents = ReadLog();

        Assert.Contains("Unobserved task exception was collected by the finalizer", contents);
        Assert.Contains("dropped on the floor", contents);
    }

    /// <summary>
    /// Removes the temporary log directory created for the test class.
    /// </summary>
    public void Dispose()
    {
        Log.CloseAndFlush();
        try
        {
            Directory.Delete(logDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A sink that has not released its handle yet must not fail an otherwise passing test.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Points the static Serilog facade at a rolling file inside this test's temporary directory.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the sink shape both heads configure - daily rolling file,
    /// unbuffered - so what the tests assert is what production does.</para>
    /// <para><b>Flow:</b> build the logger configuration and assign <see cref="Log.Logger"/>.</para>
    /// <para><b>Side Effects:</b> Replaces the process-wide logger.</para>
    /// </remarks>
    private void ConfigureFileLogger()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "crash-.log"),
                rollingInterval: RollingInterval.Day,
                shared: true)
            .CreateLogger();
    }

    /// <summary>
    /// Reads every log file the test's sink produced.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The rolling sink decorates the file name with the date, so the
    /// exact name is not known to the test; it reads whatever landed in the directory. The share mode
    /// is permissive because the sink may still hold the handle open.</para>
    /// <para><b>Flow:</b> enumerate → open each file for shared read → concatenate.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The concatenated contents of every log file, or an empty string when none exist.</returns>
    private string ReadLog()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var file in Directory.GetFiles(logDirectory, "crash-*.log"))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            builder.AppendLine(reader.ReadToEnd());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates a faulted task and lets it go out of scope without observing its exception.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Kept in a separate non-inlined method so the task reference is
    /// unreachable by the time the collection runs; a task still rooted on the calling frame is never
    /// finalised and the event never fires.</para>
    /// <para><b>Flow:</b> start a throwing task → wait for it to fault → return, dropping it.</para>
    /// <para><b>Side Effects:</b> None beyond the faulted task awaiting finalisation.</para>
    /// </remarks>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void DropFaultedTask()
    {
        var faulted = Task.Run(() => throw new InvalidOperationException("dropped on the floor"));
        SpinWait.SpinUntil(() => faulted.IsCompleted, TimeSpan.FromSeconds(5));
    }
}
