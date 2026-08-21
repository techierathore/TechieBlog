using Serilog;

namespace TechieBlog.Observability;

/// <summary>
/// Last-resort logging for exceptions that escape every other handler in an executable head
/// (REQ-NFR-013, BRD-90).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> ASP.NET Core's exception middleware only sees exceptions raised inside the
/// request pipeline. A failure on a background thread, inside a fire-and-forget task, during host
/// composition or while the process is shutting down bypasses it entirely and would otherwise kill
/// the process with nothing written to the rolling file. This type installs the two runtime-level
/// hooks that close that gap, and guarantees the sink is drained before the process dies.</para>
///
/// <para><b>Code Flow:</b> <see cref="Wire"/> is called from <c>Program.cs</c> immediately after
/// <c>Log.Logger</c> is assigned and before the host builder is created, so a crash during
/// composition is still recorded. It subscribes <see cref="HandleUnhandledException"/> to
/// <see cref="AppDomain.UnhandledException"/> and <see cref="HandleUnobservedTaskException"/> to
/// <see cref="TaskScheduler.UnobservedTaskException"/>. Both handlers are public and side-effect
/// contained so the unit tests can invoke them directly against a temporary file sink.</para>
///
/// <para><b>Dependencies:</b> Serilog's static <see cref="Log"/> facade. This is one of the two
/// places where static logging is correct rather than a smell — the startup boundary has no
/// container and therefore no <c>ILogger&lt;T&gt;</c> to inject (Coding Standards §Logging).</para>
///
/// <para><b>Usage:</b> <c>GlobalExceptionLogging.Wire();</c> once per process, right after the
/// logger is configured. Repeat calls are ignored, so a test host that composes twice does not end
/// up logging every crash twice.</para>
/// </remarks>
public static class GlobalExceptionLogging
{
    /// <summary>
    /// Message template used when an exception reaches the AppDomain handler.
    /// </summary>
    public const string UnhandledMessage =
        "Unhandled exception escaped to the AppDomain (terminating: {IsTerminating})";

    /// <summary>
    /// Message template used when a faulted task is finalised without its exception being observed.
    /// </summary>
    public const string UnobservedMessage = "Unobserved task exception was collected by the finalizer";

    /// <summary>
    /// Zero until the handlers are subscribed, one afterwards. Guards against double subscription.
    /// </summary>
    private static int WiredFlag;

    /// <summary>
    /// Subscribes the process-wide exception handlers.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Wiring is idempotent because a second subscription would log
    /// every subsequent crash twice and flush the sink twice. The first caller wins; later callers
    /// are told they were a no-op so a host can assert on it.</para>
    /// <para><b>Flow:</b> compare-and-swap the guard → subscribe both events on success.</para>
    /// <para><b>Side Effects:</b> Installs process-wide event handlers that live for the lifetime of
    /// the AppDomain.</para>
    /// </remarks>
    /// <returns><c>true</c> when this call installed the handlers; <c>false</c> when they were
    /// already installed by an earlier call.</returns>
    public static bool Wire()
    {
        if (Interlocked.Exchange(ref WiredFlag, 1) == 1)
            return false;

        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        return true;
    }

    /// <summary>
    /// Records an exception that no other handler caught, then drains the sink if the process is dying.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Logged at <c>Fatal</c> because the CLR raises this event
    /// immediately before terminating the process in the normal case. <see cref="Log.CloseAndFlush"/>
    /// is called <i>only</i> when the runtime says it is terminating: closing the logger swaps in a
    /// silent instance, so flushing on a non-terminating notification would blind every later log
    /// call in a process that is still alive. The non-terminating path needs no flush anyway — the
    /// unbuffered file sink has already written the event to disk by the time this returns.</para>
    /// <para><b>Flow:</b> unwrap the exception object → <c>Log.Fatal</c> → flush when terminating.</para>
    /// <para><b>Side Effects:</b> Writes to the rolling file and console sinks; may close the
    /// logger.</para>
    /// </remarks>
    /// <param name="sender">The AppDomain raising the event; unused.</param>
    /// <param name="eventArgs">Carries the escaped exception and the terminating flag.</param>
    public static void HandleUnhandledException(object? sender, UnhandledExceptionEventArgs eventArgs)
    {
        // ExceptionObject is typed as object because a non-CLS exception can be thrown from
        // native or another language; the cast yields null there and the message still lands.
        var exception = eventArgs.ExceptionObject as Exception;
        Log.Fatal(exception, UnhandledMessage, eventArgs.IsTerminating);

        if (eventArgs.IsTerminating)
            Log.CloseAndFlush();
    }

    /// <summary>
    /// Records the exception of a faulted task nobody awaited and marks it observed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Logged at <c>Error</c> rather than <c>Fatal</c> — the process is
    /// healthy, a background operation was not. <c>SetObserved</c> is mandatory: on a host running
    /// with the legacy <c>ThrowUnobservedTaskExceptions</c> setting the runtime rethrows on the
    /// finaliser thread and kills the process if nobody claims the exception, so the diagnostic hook
    /// would itself become an outage.</para>
    /// <para><b>Flow:</b> <c>Log.Error</c> → <c>SetObserved</c>.</para>
    /// <para><b>Side Effects:</b> Writes to the sinks and mutates the event args.</para>
    /// </remarks>
    /// <param name="sender">The faulted task, or <c>null</c>; unused.</param>
    /// <param name="eventArgs">Carries the aggregate exception and the observed flag.</param>
    public static void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        Log.Error(eventArgs.Exception, UnobservedMessage);
        eventArgs.SetObserved();
    }
}
