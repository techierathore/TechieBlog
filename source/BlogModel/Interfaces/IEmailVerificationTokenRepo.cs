using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for persisted double opt-in verification tokens.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stores and redeems the single-use, 24-hour tokens that confirm an
/// anonymous commenter's, rater's or subscriber's email address. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> <c>EmailVerificationSvc</c> calls <see cref="InsertTokenAsync"/>
/// when a submission is queued and <see cref="ConsumeAsync"/> when the emailed link is opened.
/// A scheduled cleanup calls <see cref="DeleteExpiredAsync"/>.</para>
///
/// <para><b>Dependencies:</b> Implemented by <c>EmailVerificationTokenRepo</c> over the
/// <c>EmailVerificationToken</c> table (migration 014). Explicitly NOT an in-memory store -
/// contrast the legacy <c>PasswordResetTokenRepo</c>, whose tokens die with the process.</para>
///
/// <para><b>Usage:</b> Redemption must go through <see cref="ConsumeAsync"/>, never through a
/// read-then-write pair, so that two concurrent clicks cannot both succeed.</para>
/// </remarks>
public interface IEmailVerificationTokenRepo : IGenericRepository<EmailVerificationToken>
{
    /// <summary>
    /// Reads a token row without redeeming it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns the row whatever its state, so callers can tell
    /// "expired" apart from "never existed" when explaining a failure.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="token">The token string from the verification link.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The token row, or null when no such token exists.</returns>
    Task<EmailVerificationToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a newly issued token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller supplies the random token string and the expiry.</para>
    /// <para><b>Side Effects:</b> Inserts one row into <c>EmailVerificationToken</c>.</para>
    /// </remarks>
    /// <param name="token">The token to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated primary key.</returns>
    Task<long> InsertTokenAsync(EmailVerificationToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically redeems a token exactly once.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to the <c>ConsumeEmailVerificationToken</c>
    /// stored function, which checks "unused and unexpired" and flips the state in the same
    /// statement. A second click therefore returns null.</para>
    /// <para><b>Side Effects:</b> Sets <c>IsUsed</c> and <c>ConsumedOn</c> on success.</para>
    /// </remarks>
    /// <param name="token">The token string from the verification link.</param>
    /// <param name="cancellationToken">Cancels the redemption.</param>
    /// <returns>The redeemed token row, or null when it was unknown, already used or expired.</returns>
    Task<EmailVerificationToken?> ConsumeAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes tokens whose expiry has passed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Housekeeping only; expired tokens are already refused by
    /// <see cref="ConsumeAsync"/>.</para>
    /// <para><b>Side Effects:</b> Deletes rows.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The number of rows removed.</returns>
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts how many tokens an address has been issued since a given instant.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Feeds the rate limit that stops an address being used to
    /// spray verification mail at a third party.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="email">The address to count for; matched case-insensitively.</param>
    /// <param name="since">The UTC instant to count from.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of tokens issued in the window.</returns>
    Task<int> CountRecentByEmailAsync(string email, DateTime since, CancellationToken cancellationToken = default);
}
