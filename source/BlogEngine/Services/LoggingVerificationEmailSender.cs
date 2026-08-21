using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Development fallback that writes the confirmation link to the log instead of sending mail.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the whole double opt-in flow be exercised end to end before the
/// real SMTP sender of REQ-FN-033 exists. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> Formats the link into a single structured log entry that a developer
/// can copy into the browser.</para>
///
/// <para><b>Dependencies:</b> <see cref="ILogger{TCategoryName}"/> only - deliberately no
/// transport, no configuration and no network.</para>
///
/// <para><b>Usage:</b> Registered with <c>TryAdd</c> semantics, so registering a real
/// <see cref="IVerificationEmailSender"/> anywhere in the host replaces it. This class must
/// never reach production: a verification link in a log file is a verification link anyone with
/// log access can redeem.</para>
/// </remarks>
public class LoggingVerificationEmailSender : IVerificationEmailSender
{
    private readonly ILogger<LoggingVerificationEmailSender> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoggingVerificationEmailSender"/> class.
    /// </summary>
    /// <param name="logger">Logger the link is written to.</param>
    public LoggingVerificationEmailSender(ILogger<LoggingVerificationEmailSender> logger)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Writes the confirmation link to the log at Warning level. <b>No email is sent.</b>
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deliberately does NOT inherit
    /// <see cref="IVerificationEmailSender"/>'s documentation, which promises delivery. The entry
    /// is logged at <c>Warning</c> rather than <c>Information</c> precisely so that an operator who
    /// has accidentally deployed without a mail transport sees it: every unsent confirmation raises
    /// a warning naming the address that will never hear back.</para>
    /// <para><b>Flow:</b> write one Warning entry → return a completed task.</para>
    /// <para><b>Side Effects:</b> Writes one log entry; no network I/O. The consequence for the
    /// double opt-in flow is that <b>no visitor can ever self-serve a confirmation</b> — their
    /// comment, rating or subscription stays pending and invisible until someone reads the link out
    /// of the log. The token itself is live and single-use, so anyone with log access can redeem
    /// another person's confirmation.</para>
    /// </remarks>
    /// <param name="toEmail">The address that would have been mailed.</param>
    /// <param name="displayName">The name the visitor supplied; may be null.</param>
    /// <param name="purpose">What is being confirmed — a comment, a rating or a subscription.</param>
    /// <param name="verificationUrl">The absolute, single-use confirmation link, logged in full.</param>
    /// <returns>An already-completed task — there is no transport to wait for.</returns>
    public Task SendVerificationEmailAsync(string toEmail, string displayName, string purpose, string verificationUrl)
    {
        logger.LogWarning(
            "No mail transport is configured. Verification link for {Purpose} to {Email} ({DisplayName}): {VerificationUrl}",
            purpose, toEmail, displayName, verificationUrl);
        return Task.CompletedTask;
    }
}
