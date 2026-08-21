using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TechieBlog.Tests.Dashboard;
using TechieBlog.Tests.TestDoubles;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Covers <see cref="VerificationEmailSender"/> — the class that renders the double opt-in
/// confirmation message and turns a delivery failure into an exception the caller must handle.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Three things about this class are load-bearing and none of them are
/// visible to the compiler. The display name is typed into a public, unauthenticated form and is
/// interpolated straight into an HTML body, so it must be encoded or a stranger's "name" becomes
/// markup in someone else's inbox. The purpose code selects the noun the recipient reads, and an
/// unknown code has to degrade to a neutral word rather than render the raw code. And a failed
/// send must <b>throw</b>, against the codebase's usual <c>Result</c> convention, because the
/// calling service uses that exception to roll back the pending submission — swallowing it would
/// strand a comment behind a link that was never delivered. [REQ-FN-048, REQ-NFR-016]</para>
///
/// <para><b>Dependencies:</b> <see cref="RecordingEmailService"/> as the transport,
/// <see cref="RecordingLogger{T}"/> where the log content itself is asserted, NSubstitute where
/// only the call needs shaping. No SMTP server and no database.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite.</para>
/// </remarks>
public class VerificationEmailSenderTests
{
    private readonly RecordingEmailService transport = new();
    private readonly VerificationEmailSender sender;

    /// <summary>
    /// Wires the sender under test to the recording transport.
    /// </summary>
    public VerificationEmailSenderTests()
    {
        sender = new VerificationEmailSender(transport, NullLogger<VerificationEmailSender>.Instance);
    }

    /// <summary>
    /// The confirmation message is addressed to the address being confirmed and to nobody else.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task SendsToTheAddressBeingConfirmed()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync(
            "reader@example.com", "Ada", EmailVerificationPurpose.Comment, "https://blog.test/confirm?t=abc");

        // Assert
        Assert.Equal("reader@example.com", Assert.Single(transport.SentMessages).ToAddress);
    }

    /// <summary>
    /// The purpose code selects the noun in the subject line, so a visitor confirming a rating is
    /// not told to confirm a comment.
    /// </summary>
    /// <param name="purpose">The purpose code supplied by the calling service.</param>
    /// <param name="expectedSubject">The subject the recipient should read.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Theory]
    [InlineData(EmailVerificationPurpose.Comment, "Please confirm your comment")]
    [InlineData(EmailVerificationPurpose.Rating, "Please confirm your rating")]
    [InlineData(EmailVerificationPurpose.Subscription, "Please confirm your subscription")]
    public async Task SubjectNamesWhatIsBeingConfirmed(string purpose, string expectedSubject)
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync("reader@example.com", "Ada", purpose, "https://blog.test/c");

        // Assert
        Assert.Equal(expectedSubject, Assert.Single(transport.SentMessages).Subject);
    }

    /// <summary>
    /// The purpose codes are matched case-insensitively, so a value that round-tripped through the
    /// database in a different casing still renders its own noun.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task PurposeMatchingIgnoresCase()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync("reader@example.com", "Ada", "sUbScRiPtIoN", "https://blog.test/c");

        // Assert
        Assert.Equal("Please confirm your subscription", Assert.Single(transport.SentMessages).Subject);
    }

    /// <summary>
    /// An unrecognised purpose code degrades to the neutral noun "submission" rather than leaking
    /// the raw code into copy a visitor reads.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task UnknownPurposeUsesNeutralNoun()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync("reader@example.com", "Ada", "GuestbookEntry", "https://blog.test/c");

        // Assert
        Assert.Equal("Please confirm your submission", Assert.Single(transport.SentMessages).Subject);
    }

    /// <summary>
    /// A missing display name is replaced with a neutral greeting, so the message never opens with
    /// "Hello ,".
    /// </summary>
    /// <param name="displayName">The absent or blank name supplied with the submission.</param>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task BlankDisplayNameFallsBackToNeutralGreeting(string? displayName)
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync(
            "reader@example.com", displayName!, EmailVerificationPurpose.Comment, "https://blog.test/c");

        // Assert
        Assert.Contains("Hello there,", Assert.Single(transport.SentMessages).TextBody);
    }

    /// <summary>
    /// Surrounding whitespace is trimmed from the display name before it is greeted, so a pasted
    /// name does not push the greeting apart.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task DisplayNameIsTrimmed()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync(
            "reader@example.com", "  Ada  ", EmailVerificationPurpose.Comment, "https://blog.test/c");

        // Assert
        Assert.Equal("Ada", Assert.Single(transport.SentMessages).ToName);
    }

    /// <summary>
    /// A display name carrying markup is HTML-encoded before it reaches the HTML body, so a name
    /// typed into the public comment form cannot inject a script or a second link into a message
    /// somebody else opens.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task DisplayNameIsHtmlEncodedInTheHtmlBody()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync(
            "reader@example.com",
            "<script>alert(1)</script>",
            EmailVerificationPurpose.Comment,
            "https://blog.test/c");

        // Assert
        Assert.DoesNotContain("<script>", Assert.Single(transport.SentMessages).HtmlBody);
    }

    /// <summary>
    /// A confirmation URL carrying an ampersand-separated query is HTML-encoded inside the anchor's
    /// href, so a multi-parameter link cannot terminate the attribute and break the markup.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ConfirmationUrlIsHtmlEncodedInTheAnchor()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync(
            "reader@example.com",
            "Ada",
            EmailVerificationPurpose.Comment,
            "https://blog.test/confirm?t=abc&p=comment");

        // Assert
        Assert.Contains("href=\"https://blog.test/confirm?t=abc&amp;p=comment\"",
            Assert.Single(transport.SentMessages).HtmlBody);
    }

    /// <summary>
    /// The plain-text body carries the confirmation link verbatim — unencoded — because a text part
    /// is not markup and an encoded ampersand there would produce a link that does not resolve.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task TextBodyCarriesTheUrlVerbatim()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync(
            "reader@example.com",
            "Ada",
            EmailVerificationPurpose.Comment,
            "https://blog.test/confirm?t=abc&p=comment");

        // Assert
        Assert.Contains("https://blog.test/confirm?t=abc&p=comment",
            Assert.Single(transport.SentMessages).TextBody);
    }

    /// <summary>
    /// The copy states the two facts a recipient needs to act — that the link works once and
    /// expires in 24 hours — so silence is a safe default for someone whose address was entered by
    /// a stranger.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task CopyStatesTheSingleUseExpiry()
    {
        // Arrange, Act
        await sender.SendVerificationEmailAsync(
            "reader@example.com", "Ada", EmailVerificationPurpose.Comment, "https://blog.test/c");

        // Assert
        Assert.Contains("It works once and expires in 24 hours.",
            Assert.Single(transport.SentMessages).TextBody);
    }

    /// <summary>
    /// A delivery failure throws rather than returning quietly, so the calling service can roll back
    /// the pending submission instead of leaving the visitor waiting for a link that never arrives.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task DeliveryFailureThrows()
    {
        // Arrange
        transport.FailForAddress = "reader@example.com";

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendVerificationEmailAsync(
            "reader@example.com", "Ada", EmailVerificationPurpose.Comment, "https://blog.test/c"));
    }

    /// <summary>
    /// The failure log names the purpose and the transport's reason but deliberately omits the
    /// confirmation URL, so a log reader cannot redeem the link the recipient never received.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task FailureLogOmitsTheConfirmationUrl()
    {
        // Arrange
        var recordingLogger = new RecordingLogger<VerificationEmailSender>();
        var failing = Substitute.For<IEmailService>();
        failing.SendAsync(Arg.Any<EmailMessage>()).Returns(Result.Failure("mailbox full"));
        var loggingSender = new VerificationEmailSender(failing, recordingLogger);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => loggingSender.SendVerificationEmailAsync(
            "reader@example.com", "Ada", EmailVerificationPurpose.Comment, "https://blog.test/secret-token"));

        // Assert
        Assert.DoesNotContain("secret-token", Assert.Single(recordingLogger.Entries).Message);
    }

    /// <summary>
    /// The exception surfaced to the caller carries the transport's own reason, so the operator who
    /// reads the rolled-back submission's log can tell a rejected mailbox from an unreachable host.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task ThrownExceptionCarriesTransportReason()
    {
        // Arrange
        var failing = Substitute.For<IEmailService>();
        failing.SendAsync(Arg.Any<EmailMessage>()).Returns(Result.Failure("mailbox full"));
        var failingSender = new VerificationEmailSender(failing, NullLogger<VerificationEmailSender>.Instance);

        // Act
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingSender.SendVerificationEmailAsync(
                "reader@example.com", "Ada", EmailVerificationPurpose.Comment, "https://blog.test/c"));

        // Assert
        Assert.Equal("The confirmation email could not be delivered: mailbox full", error.Message);
    }

    /// <summary>
    /// The development stand-in writes the link at Warning level — not Information — so an operator
    /// who has deployed with no mail transport sees one warning per address that will never hear back.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task LoggingSenderWarnsRatherThanInforms()
    {
        // Arrange
        var recordingLogger = new RecordingLogger<LoggingVerificationEmailSender>();
        var loggingSender = new LoggingVerificationEmailSender(recordingLogger);

        // Act
        await loggingSender.SendVerificationEmailAsync(
            "reader@example.com", "Ada", EmailVerificationPurpose.Comment, "https://blog.test/c");

        // Assert
        Assert.Equal(LogLevel.Warning, Assert.Single(recordingLogger.Entries).Level);
    }

    /// <summary>
    /// The development stand-in writes the confirmation link in full, which is the whole reason it
    /// exists — and the reason it must never be registered where real people use the site.
    /// </summary>
    /// <returns>A task that completes when the assertion has run.</returns>
    [Fact]
    public async Task LoggingSenderWritesTheLinkInFull()
    {
        // Arrange
        var recordingLogger = new RecordingLogger<LoggingVerificationEmailSender>();
        var loggingSender = new LoggingVerificationEmailSender(recordingLogger);

        // Act
        await loggingSender.SendVerificationEmailAsync(
            "reader@example.com", "Ada", EmailVerificationPurpose.Comment, "https://blog.test/confirm?t=abc");

        // Assert
        Assert.Contains("https://blog.test/confirm?t=abc", Assert.Single(recordingLogger.Entries).Message);
    }
}
