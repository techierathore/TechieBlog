using System.Net;
using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Composes the double opt-in confirmation message and hands it to <see cref="IEmailService"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Joins the verification flow of [REQ-FN-048] to the real transport
/// delivered by [REQ-FN-033], without either side knowing about the other's internals.</para>
///
/// <para><b>Code Flow:</b> <c>EmailVerificationSvc</c> supplies the address, the name, what is
/// being confirmed and the single-use link; this class renders a short HTML and plain-text body
/// and calls <see cref="IEmailService.SendAsync"/>.</para>
///
/// <para><b>This one really mails the link. Its development counterpart logs it.</b> Two
/// implementations of <see cref="IVerificationEmailSender"/> exist and the difference is not
/// cosmetic:</para>
/// <list type="table">
///   <item>
///     <term><see cref="VerificationEmailSender"/> (this class)</term>
///     <description>Hands the message to <see cref="IEmailService"/>, which delivers it over SMTP
///     when a host is configured — the link reaches the address's owner, and only they can confirm
///     the submission.</description>
///   </item>
///   <item>
///     <term><c>LoggingVerificationEmailSender</c> (sibling file)</term>
///     <description>Writes the confirmation <b>URL</b> to the application log so a developer can
///     click it with no mailbox. That convenience is also its danger: wherever it is registered,
///     <b>anyone who can read the log can confirm anyone else's email address</b>, which defeats
///     the entire point of double opt-in. It is a development-only stand-in and must never be
///     registered in production.</description>
///   </item>
/// </list>
/// <para>Note the second-order confusion: even this class only <i>really</i> sends when
/// <c>IEmailService</c> resolves to <c>SmtpEmailService</c>. With no SMTP host configured it
/// resolves to <c>ConsoleEmailService</c>, which logs the message and reports success — so "the
/// send succeeded" is never on its own evidence that a mail was delivered.</para>
///
/// <para><b>Dependencies:</b> <see cref="IEmailService"/> - which resolves to the SMTP sender
/// when a host is configured and to the console sender otherwise - and
/// <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> A delivery failure is turned into an exception so the calling service can
/// roll the pending submission back; silently reporting success would leave a comment stranded
/// behind a link that was never delivered. This is the deliberate exception to the codebase's
/// <c>Result</c> convention — an undeliverable confirmation is not a case the caller may
/// ignore. Requires no authorization; the caller decides whether the address should be mailed.</para>
/// </remarks>
public class VerificationEmailSender : IVerificationEmailSender
{
    private readonly IEmailService emailService;
    private readonly ILogger<VerificationEmailSender> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerificationEmailSender"/> class.
    /// </summary>
    /// <param name="emailService">The transport-neutral email service.</param>
    /// <param name="logger">Logger for delivery failures.</param>
    public VerificationEmailSender(IEmailService emailService, ILogger<VerificationEmailSender> logger)
    {
        this.emailService = emailService;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Renders and delivers the confirmation message. A failure
    /// <b>throws</b> rather than returning, so the calling service can undo the pending submission
    /// it just wrote.</para>
    /// <para><b>Flow:</b> build the message → send → return on success, or log the purpose and the
    /// reason and throw.</para>
    /// <para><b>Side Effects:</b> <b>Sends a real email containing a working, single-use
    /// confirmation link.</b> Logs an error on failure — deliberately without the URL, so a log
    /// reader cannot confirm the address from the log alone. Performs no throttling of its own; the
    /// caller owns that.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The confirmation email could not be delivered. The caller is expected to roll back the
    /// pending submission rather than leave the visitor waiting for a link that never arrives.
    /// </exception>
    public async Task SendVerificationEmailAsync(
        string toEmail, string displayName, string purpose, string verificationUrl)
    {
        var message = BuildMessage(toEmail, displayName, purpose, verificationUrl);
        var result = await emailService.SendAsync(message).ConfigureAwait(false);
        if (result.IsSuccess)
            return;

        logger.LogError("Could not deliver the {Purpose} confirmation email: {Reason}",
            purpose, result.ErrorMessage);
        throw new InvalidOperationException(
            $"The confirmation email could not be delivered: {result.ErrorMessage}");
    }

    /// <summary>
    /// Renders the confirmation message.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The copy states the two facts a recipient needs - the link
    /// works once and expires in 24 hours - and that ignoring the message cancels the
    /// submission, which is what makes silence a safe default for someone whose address was
    /// entered by a stranger.</para>
    /// <para><b>Side Effects:</b> None. Every interpolated value is HTML-encoded, so a name
    /// typed into a public form cannot inject markup into the message.</para>
    /// </remarks>
    /// <param name="toEmail">The address being confirmed.</param>
    /// <param name="displayName">The name supplied with the submission; may be null.</param>
    /// <param name="purpose">What is being confirmed.</param>
    /// <param name="verificationUrl">The single-use confirmation link.</param>
    /// <returns>The message to deliver.</returns>
    private static EmailMessage BuildMessage(
        string toEmail, string displayName, string purpose, string verificationUrl)
    {
        var subjectNoun = DescribePurpose(purpose);
        var greetingName = string.IsNullOrWhiteSpace(displayName) ? "there" : displayName.Trim();
        var safeName = WebUtility.HtmlEncode(greetingName);
        var safeUrl = WebUtility.HtmlEncode(verificationUrl);

        return new EmailMessage
        {
            ToAddress = toEmail,
            ToName = greetingName,
            Subject = $"Please confirm your {subjectNoun}",
            HtmlBody =
                $"<p>Hello {safeName},</p>" +
                $"<p>Please confirm your {subjectNoun} by opening the link below. " +
                "It works once and expires in 24 hours.</p>" +
                $"<p><a href=\"{safeUrl}\">Confirm my {subjectNoun}</a></p>" +
                "<p>If you did not submit anything, ignore this email and nothing will be posted.</p>",
            TextBody =
                $"Hello {greetingName},\r\n\r\n" +
                $"Please confirm your {subjectNoun} by opening this link. " +
                "It works once and expires in 24 hours.\r\n\r\n" +
                $"{verificationUrl}\r\n\r\n" +
                "If you did not submit anything, ignore this email and nothing will be posted."
        };
    }

    /// <summary>
    /// Turns a purpose code into a noun for the message copy.
    /// </summary>
    /// <param name="purpose">One of the <see cref="EmailVerificationPurpose"/> values.</param>
    /// <returns>A human-readable noun.</returns>
    private static string DescribePurpose(string purpose)
    {
        if (string.Equals(purpose, EmailVerificationPurpose.Comment, StringComparison.OrdinalIgnoreCase))
            return "comment";

        if (string.Equals(purpose, EmailVerificationPurpose.Rating, StringComparison.OrdinalIgnoreCase))
            return "rating";

        if (string.Equals(purpose, EmailVerificationPurpose.Subscription, StringComparison.OrdinalIgnoreCase))
            return "subscription";

        return "submission";
    }
}
