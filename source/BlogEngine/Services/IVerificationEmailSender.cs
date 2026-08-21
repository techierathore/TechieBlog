namespace BlogEngine.Services;

/// <summary>
/// Delivers the double opt-in confirmation message.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The seam between <c>EmailVerificationSvc</c> and whatever actually
/// carries mail. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> The verification service persists a token, builds the
/// <c>/verify/{token}</c> link and hands it here; delivery is somebody else's problem.</para>
///
/// <para><b>Dependencies:</b> None. The shipped default,
/// <see cref="LoggingVerificationEmailSender"/>, writes the link to the log exactly as
/// <c>ConsoleEmailService</c> does for password resets, so the flow is testable before real
/// mail exists.</para>
///
/// <para><b>Usage:</b> REQ-FN-033 introduces a real SMTP <c>IEmailService</c>. Point this
/// interface at it with a one-class adapter and register that adapter after
/// <c>EngagementSvcInitializer</c> runs - the registration there uses <c>TryAdd</c>
/// semantics precisely so a real sender wins.</para>
/// </remarks>
public interface IVerificationEmailSender
{
    /// <summary>
    /// Sends the confirmation link to the address being verified.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The link is single-use and expires in 24 hours; the copy
    /// should say so, and should make clear that ignoring the message cancels the submission.</para>
    /// <para><b>Flow:</b> Called once per issued token, after the token row is committed - so a
    /// delivery failure never leaves a live token unsent.</para>
    /// <para><b>Side Effects:</b> Sends an email.</para>
    /// </remarks>
    /// <param name="toEmail">The address being confirmed.</param>
    /// <param name="displayName">The name the visitor supplied; may be null.</param>
    /// <param name="purpose">What is being confirmed - a comment, a rating or a subscription.</param>
    /// <param name="verificationUrl">The absolute, single-use confirmation link.</param>
    /// <returns>A task that completes when the message has been handed to the transport.</returns>
    Task SendVerificationEmailAsync(string toEmail, string displayName, string purpose, string verificationUrl);
}
