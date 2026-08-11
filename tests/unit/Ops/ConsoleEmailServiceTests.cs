using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using TechieBlog.Tests.Dashboard;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Covers <see cref="ConsoleEmailService"/> — the development transport the DI factory selects when
/// no SMTP host is configured, which logs every message instead of sending it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> This class is the one every clone-and-run checkout uses, so its contract
/// is what the rest of the codebase is written against. Two parts of that contract matter. It must
/// reject a message with no recipient with a failed <c>Result</c>, exactly as the SMTP transport
/// does, or the caller's validation path behaves differently in development from production. And
/// the rendered body must be written <b>separately, at Debug</b>: the body is the only place a
/// newsletter's unsubscribe footer can be inspected without a mail server, and it is also the
/// noisiest and most sensitive thing the class handles, so it has to be off unless Debug is
/// enabled. [REQ-FN-033, REQ-NFR-016]</para>
///
/// <para><b>Dependencies:</b> <see cref="RecordingLogger{T}"/> and a level-gated variant of it. No
/// network of any kind.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite.</para>
/// </remarks>
public class ConsoleEmailServiceTests
{
    /// <summary>
    /// A <see cref="RecordingLogger{T}"/> that reports a fixed minimum level, so a test can prove
    /// the Debug-guarded branch really is guarded.
    /// </summary>
    /// <typeparam name="T">Log category, matching the service under test.</typeparam>
    private sealed class LevelGatedLogger<T> : ILogger<T>
    {
        private readonly List<(LogLevel Level, string Message)> entries = new();
        private readonly LogLevel minimumLevel;

        /// <summary>
        /// Creates a logger that accepts entries at or above one level.
        /// </summary>
        /// <param name="minimumLevel">The lowest level this logger reports as enabled.</param>
        public LevelGatedLogger(LogLevel minimumLevel) => this.minimumLevel = minimumLevel;

        /// <summary>Gets every entry that was actually written, in order.</summary>
        public IReadOnlyList<(LogLevel Level, string Message)> Entries => entries;

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                entries.Add((logLevel, formatter(state, exception)));
        }
    }

    /// <summary>
    /// Builds a well-formed newsletter message.
    /// </summary>
    /// <returns>A message with a recipient, a subject, an HTML body and an unsubscribe link.</returns>
    private static EmailMessage ValidMessage() => new()
    {
        ToAddress = "reader@example.com",
        Subject = "This week on TechieBlog",
        HtmlBody = "<p>Hello</p>",
        UnsubscribeUrl = "https://blog.test/unsubscribe?t=abc"
    };

    /// <summary>
    /// A null message is rejected with the same failure a missing recipient produces, so the caller
    /// never faults on a dereference inside the transport.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task NullMessageIsRejected()
    {
        // Arrange
        var service = new ConsoleEmailService(new RecordingLogger<ConsoleEmailService>());

        // Act
        var result = await service.SendAsync(null!);

        // Assert
        Assert.Equal("A recipient address is required.", result.ErrorMessage);
    }

    /// <summary>
    /// A message with a blank recipient is reported as a failure rather than logged, so the
    /// development transport's validation path matches the SMTP transport's.
    /// </summary>
    /// <param name="toAddress">The absent or blank recipient under test.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankRecipientIsRejected(string toAddress)
    {
        // Arrange
        var recordingLogger = new RecordingLogger<ConsoleEmailService>();
        var service = new ConsoleEmailService(recordingLogger);
        var message = ValidMessage();
        message.ToAddress = toAddress;

        // Act
        var result = await service.SendAsync(message);

        // Assert
        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// A rejected message is not logged at all, so a malformed send cannot be mistaken for a
    /// delivered one when reading the log.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task RejectedMessageIsNotLogged()
    {
        // Arrange
        var recordingLogger = new RecordingLogger<ConsoleEmailService>();
        var service = new ConsoleEmailService(recordingLogger);
        var message = ValidMessage();
        message.ToAddress = string.Empty;

        // Act
        await service.SendAsync(message);

        // Assert
        Assert.Empty(recordingLogger.Entries);
    }

    /// <summary>
    /// A well-formed message succeeds — meaning only that it was accepted for logging, never that
    /// anything was delivered.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task WellFormedMessageSucceeds()
    {
        // Arrange
        var service = new ConsoleEmailService(new RecordingLogger<ConsoleEmailService>());

        // Act
        var result = await service.SendAsync(ValidMessage());

        // Assert
        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// The Information entry carries the unsubscribe link, which is the value an operator most often
    /// needs to read back out of a development newsletter run.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task InformationEntryCarriesTheUnsubscribeUrl()
    {
        // Arrange
        var recordingLogger = new RecordingLogger<ConsoleEmailService>();
        var service = new ConsoleEmailService(recordingLogger);

        // Act
        await service.SendAsync(ValidMessage());

        // Assert
        Assert.Contains("https://blog.test/unsubscribe?t=abc", recordingLogger.Entries[0].Message);
    }

    /// <summary>
    /// A message with no unsubscribe link logs the placeholder "(none)" rather than an empty gap, so
    /// a missing footer is visible in the log rather than merely absent.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task MissingUnsubscribeUrlLogsPlaceholder()
    {
        // Arrange
        var recordingLogger = new RecordingLogger<ConsoleEmailService>();
        var service = new ConsoleEmailService(recordingLogger);
        var message = ValidMessage();
        message.UnsubscribeUrl = "   ";

        // Act
        await service.SendAsync(message);

        // Assert
        Assert.Contains("Unsubscribe: (none)", recordingLogger.Entries[0].Message);
    }

    /// <summary>
    /// With Debug enabled the rendered body is written as a second, separate entry, which is what
    /// makes an unsubscribe footer inspectable without a mail server.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task BodyIsLoggedSeparatelyWhenDebugIsEnabled()
    {
        // Arrange
        var gatedLogger = new LevelGatedLogger<ConsoleEmailService>(LogLevel.Debug);
        var service = new ConsoleEmailService(gatedLogger);

        // Act
        await service.SendAsync(ValidMessage());

        // Assert
        Assert.Contains("<p>Hello</p>", gatedLogger.Entries[1].Message);
    }

    /// <summary>
    /// With Debug disabled the body is never rendered into a log message at all — the guard is a
    /// real branch, not decoration, and a large send would otherwise pay that allocation once per
    /// recipient.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task BodyIsNotLoggedWhenDebugIsDisabled()
    {
        // Arrange
        var gatedLogger = new LevelGatedLogger<ConsoleEmailService>(LogLevel.Information);
        var service = new ConsoleEmailService(gatedLogger);

        // Act
        await service.SendAsync(ValidMessage());

        // Assert
        Assert.Single(gatedLogger.Entries);
    }

    /// <summary>
    /// A message with no HTML part logs its plain-text body instead, so a text-only send is not
    /// recorded as an empty one.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task TextOnlyMessageLogsItsTextBody()
    {
        // Arrange
        var gatedLogger = new LevelGatedLogger<ConsoleEmailService>(LogLevel.Debug);
        var service = new ConsoleEmailService(gatedLogger);
        var message = ValidMessage();
        message.HtmlBody = string.Empty;
        message.TextBody = "Hello in plain text";

        // Act
        await service.SendAsync(message);

        // Assert
        Assert.Contains("Hello in plain text", gatedLogger.Entries[1].Message);
    }

    /// <summary>
    /// The password-reset helper writes the reset URL verbatim, because pasting it out of the log is
    /// the only way to walk the reset flow with no mail server.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task PasswordResetLinkIsLoggedVerbatim()
    {
        // Arrange
        var recordingLogger = new RecordingLogger<ConsoleEmailService>();
        var service = new ConsoleEmailService(recordingLogger);

        // Act
        await service.SendPasswordResetEmail("reader@example.com", "https://blog.test/reset?t=abc");

        // Assert
        Assert.Contains("https://blog.test/reset?t=abc", Assert.Single(recordingLogger.Entries).Message);
    }
}
