using Microsoft.Extensions.Logging;

namespace TechieBlog.Tests.Dashboard;

/// <summary>
/// Minimal <see cref="ILogger{TCategoryName}"/> that records the entries written to it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets a test assert that a swallowed exception was at least logged, which is
/// the whole point of catching it in the first place. [REQ-FN-036]</para>
/// <para><b>Code Flow:</b> <see cref="Log{TState}"/> appends the formatted message and its level to
/// <see cref="Entries"/>; scopes are ignored.</para>
/// <para><b>Dependencies:</b> Microsoft.Extensions.Logging.Abstractions.</para>
/// <para><b>Usage:</b> Inject in place of a real logger, then inspect <see cref="Entries"/>.</para>
/// </remarks>
/// <typeparam name="T">Log category, matching the service under test.</typeparam>
public class RecordingLogger<T> : ILogger<T>
{
    private readonly List<(LogLevel Level, string Message, Exception? Error)> entries = new();

    /// <summary>
    /// Gets every entry written to this logger, in order.
    /// </summary>
    public IReadOnlyList<(LogLevel Level, string Message, Exception? Error)> Entries => entries;

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        entries.Add((logLevel, formatter(state, exception), exception));
    }
}
