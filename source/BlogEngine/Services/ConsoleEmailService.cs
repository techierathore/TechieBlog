using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Development email transport that writes messages to the log instead of sending them.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps a clone-and-run checkout usable with no mail server. It is the
/// fallback the DI factory selects when <c>EmailSettings:SmtpHost</c> is not configured
/// (REQ-FN-033).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>BlogSvcInitializer</c> resolves <c>IEmailService</c> and finds no SMTP host.</item>
///   <item>This implementation is returned and every send is written to the Serilog file and
///         console sinks at Information level.</item>
///   <item>Nothing leaves the machine, so a developer can follow a reset or verification link
///         straight out of the log.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>ILogger&lt;ConsoleEmailService&gt;</c> only.</para>
///
/// <para><b>Usage:</b> Never register this explicitly in a deployed environment — configure
/// <c>EmailSettings:SmtpHost</c> and the SMTP transport takes over automatically.</para>
/// </remarks>
public class ConsoleEmailService : IEmailService
{
    private readonly ILogger<ConsoleEmailService> logger;

    /// <summary>
    /// Initializes the development email transport.
    /// </summary>
    /// <param name="logger">Logger the messages are written to.</param>
    public ConsoleEmailService(ILogger<ConsoleEmailService> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Writes a password-reset link to the log. <b>No email is sent.</b>
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deliberately does NOT inherit <see cref="IEmailService"/>'s
    /// documentation, which promises delivery — this implementation delivers nothing. The reset
    /// link is written verbatim to the Serilog console and file sinks so a developer can paste it
    /// into a browser and walk the reset flow without a mail server.</para>
    /// <para><b>Flow:</b> format one Information entry → return a completed task.</para>
    /// <para><b>Side Effects:</b> Writes one log entry. <b>Nothing leaves the machine</b>, so a
    /// user who was told "check your inbox" will find nothing there. Never test deliverability
    /// against this implementation, and never let it reach an environment real people use — a
    /// reset link in a log file is a password reset available to anyone with log access.</para>
    /// </remarks>
    /// <param name="email">Recipient address; logged, not mailed.</param>
    /// <param name="resetUrl">Absolute reset URL including the token; logged in full.</param>
    /// <returns>An already-completed task — there is no transport to wait for.</returns>
    public Task SendPasswordResetEmail(string email, string resetUrl)
    {
        logger.LogInformation(
            "[DEV EMAIL] Password reset for {Email}. Reset URL: {ResetUrl}. " +
            "No SMTP host is configured, so nothing was sent.",
            email, resetUrl);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes an outbound message to the log. <b>No email is sent.</b>
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A missing recipient is still reported as a failed
    /// <c>Result</c>, so the caller's validation path behaves identically to the SMTP transport's.
    /// Everything past that point is a log write: the success this returns means "the message was
    /// well-formed", <b>not</b> "the message was delivered". A newsletter run against this
    /// implementation will mark every recipient as sent while nobody receives anything.</para>
    /// <para><b>Flow:</b> validate the recipient → write one Information entry → return success.</para>
    /// <para><b>Side Effects:</b> Writes one log entry. No network I/O of any kind. The body is not
    /// logged; only the recipient, the subject and the unsubscribe URL are.</para>
    /// </remarks>
    /// <param name="message">The message that would have been sent.</param>
    /// <returns>Failure when no recipient was supplied; otherwise success, meaning only that the
    /// message was accepted for logging.</returns>
    public Task<Result> SendAsync(EmailMessage message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.ToAddress))
            return Task.FromResult(Result.Failure("A recipient address is required."));

        logger.LogInformation(
            "[DEV EMAIL] To {ToAddress} — {Subject}. Unsubscribe: {UnsubscribeUrl}. " +
            "No SMTP host is configured, so nothing was sent.",
            message.ToAddress,
            message.Subject,
            string.IsNullOrWhiteSpace(message.UnsubscribeUrl) ? "(none)" : message.UnsubscribeUrl);

        return Task.FromResult(Result.Success());
    }
}
