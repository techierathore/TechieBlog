using BlogModels;

namespace BlogEngine.Services;

/// <summary>
/// Issues, delivers and redeems double opt-in email verification tokens.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published contract behind "confirm your email address" for
/// anonymous comments, ratings and subscriptions. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A submission is stored in a pending state and the caller invokes
///   <see cref="IssueAsync"/> with the pending row's id.</item>
///   <item>A single-use token valid for 24 hours is PERSISTED - not held in memory - and mailed
///   as <c>/verify/{token}</c>.</item>
///   <item>The verify page calls <see cref="ConsumeAsync"/>, which redeems the token atomically,
///   promotes the pending row and records the address as verified.</item>
///   <item>Later submissions from that address short-circuit on
///   <see cref="IsAddressVerifiedAsync"/> and skip the whole dance.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IEmailVerificationTokenRepo"/>,
/// <see cref="IVerifiedEmailRepo"/>, the comment and rating repositories, and
/// <see cref="IVerificationEmailSender"/>.</para>
///
/// <para><b>Usage:</b> The token is the only secret; treat it like a password. Never log it,
/// never put it in a query string that ends up in a referrer header.</para>
/// </remarks>
public interface IEmailVerificationService
{
    /// <summary>
    /// Issues a token for a pending submission and mails the confirmation link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Refuses to issue when the address has already been sent
    /// several tokens in the last hour, so the feature cannot be turned into a mail cannon
    /// aimed at a third party. The token is written before the mail is sent, so a delivery
    /// failure never produces a link that does not work.</para>
    /// <para><b>Flow:</b> validate, rate-limit, generate, persist, build link, send.</para>
    /// <para><b>Side Effects:</b> Inserts one token row and sends one email.</para>
    /// </remarks>
    /// <param name="email">The address to confirm.</param>
    /// <param name="displayName">The name supplied with the submission; may be null.</param>
    /// <param name="purpose">One of the <see cref="EmailVerificationPurpose"/> values.</param>
    /// <param name="targetId">The pending comment, rating or subscriber id.</param>
    /// <param name="ipAddress">The origin of the request; may be null.</param>
    /// <returns>The issued token on success, or a failure carrying a visitor-safe message.</returns>
    Task<Result<EmailVerificationToken>> IssueAsync(
        string email, string displayName, string purpose, long targetId, string? ipAddress);

    /// <summary>
    /// Redeems a token from a confirmation link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A token works EXACTLY ONCE and only inside its 24-hour
    /// window; the check and the state flip happen in one SQL statement, so two concurrent
    /// clicks cannot both win. On success the address joins the verified registry and the
    /// pending row is promoted for ALL THREE purposes - a comment moves into the moderation
    /// queue (it does NOT become publicly visible), a rating starts counting towards the
    /// aggregates, and a subscriber's <c>IsConfirmed</c> flag is set.</para>
    /// <para><b>Flow:</b> consume atomically, promote the target, record the address.</para>
    /// <para><b>Side Effects:</b> Updates the token, the target row and the verified registry.</para>
    /// </remarks>
    /// <param name="token">The token from the link.</param>
    /// <returns>The redeemed token on success, or a failure explaining why it was refused.</returns>
    Task<Result<EmailVerificationToken>> ConsumeAsync(string token);

    /// <summary>
    /// Tests whether an address may submit without confirming again.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> True only for an address in the verified registry that has
    /// not been blocked by an administrator.</para>
    /// <para><b>Flow:</b> Single registry lookup.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="email">The address to test.</param>
    /// <returns>True when confirmation can be skipped.</returns>
    Task<bool> IsAddressVerifiedAsync(string email);
}
