namespace BlogEngine.Services;

/// <summary>
/// Outbound email for the whole engine — transactional mail and bulk newsletter dispatch alike.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One published contract every feature that needs to send mail depends on
/// (REQ-FN-033). Newsletter dispatch (REQ-FN-032) and double opt-in verification (REQ-FN-048) both
/// build on <see cref="SendAsync"/>; password reset keeps its purpose-built method.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A caller builds an <c>EmailMessage</c> (or calls the password-reset helper).</item>
///   <item>Dependency injection resolves the implementation: <c>SmtpEmailService</c> when
///         <c>EmailSettings:SmtpHost</c> is configured, otherwise <c>ConsoleEmailService</c>, which
///         keeps development running without a mail server.</item>
///   <item>The implementation delivers the message and reports the outcome. A failure is always
///         logged with the recipient address before it is surfaced — never swallowed.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>EmailMessage</c> and <c>Result</c> from <c>BlogModels</c>.</para>
///
/// <para><b>Usage:</b> Inject <c>IEmailService</c>; never construct an implementation directly, or
/// the environment-based fallback is bypassed.</para>
/// </remarks>
public interface IEmailService
{
    /// <summary>
    /// Sends a password-reset email carrying the reset link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The reset URL is built by the caller (<c>AuthSvc</c>) and
    /// embedded in a fixed template.</para>
    /// <para><b>Flow:</b> render the template → deliver through the configured transport.</para>
    /// <para><b>Side Effects:</b> Sends email. A delivery failure is logged with context and then
    /// rethrown, so <c>AuthSvc</c> reports the reset as failed rather than telling the user a mail
    /// is on its way that never left.</para>
    /// <para><b>Naming:</b> the name deliberately lacks the <c>Async</c> suffix because this member
    /// pre-dates the standard and is on the authentication hot path; renaming it would churn call
    /// sites in another cluster's files for no behavioural gain.</para>
    /// </remarks>
    /// <param name="email">Recipient email address.</param>
    /// <param name="resetUrl">Absolute password-reset URL including the token.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    /// <exception cref="Exception">Rethrown after logging when delivery fails.</exception>
    Task SendPasswordResetEmail(string email, string resetUrl);

    /// <summary>
    /// Sends an arbitrary message through the configured transport.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Recipient and subject are required. When
    /// <c>EmailMessage.UnsubscribeUrl</c> is set the sender adds a <c>List-Unsubscribe</c> header,
    /// which bulk mail needs for deliverability.</para>
    /// <para><b>Flow:</b> validate the message → build the transport message → deliver → return the
    /// outcome.</para>
    /// <para><b>Side Effects:</b> Sends email. Failures are logged with the recipient address and
    /// returned as a failed <c>Result</c> so a bulk send can record the address and carry on
    /// instead of aborting the whole run.</para>
    /// </remarks>
    /// <param name="message">The message to deliver.</param>
    /// <returns>Success when the transport accepted the message; failure carrying the reason
    /// otherwise.</returns>
    Task<Result> SendAsync(EmailMessage message);
}
