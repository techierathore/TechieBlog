namespace BlogModels.Models;

/// <summary>
/// One issued password-reset link — the server-side half of "forgot my password".
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A reset link has to be redeemable exactly once, by exactly one account,
/// for a bounded period. None of that can be carried in the URL itself without letting the holder
/// forge it, so the link contains nothing but an opaque random string and every rule lives in this
/// row: <see cref="UserId"/> says whose password may be changed, <see cref="ExpiresAt"/> bounds the
/// window and <see cref="IsUsed"/> makes redemption single-shot.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>AuthSvc.RequestPasswordResetAsync</c> generates the token, writes a row with
///         <see cref="IsUsed"/> <c>false</c> and <see cref="ExpiresAt"/> 24 hours out, and emails
///         the link.</item>
///   <item>The visitor opens the link; <c>AuthSvc.ValidateResetTokenAsync</c> loads the row through
///         <c>PasswordResetTokenRepo.GetByTokenAsync</c> and rejects it if it is unknown, past
///         <see cref="ExpiresAt"/> or already <see cref="IsUsed"/>.</item>
///   <item><c>AuthSvc.ResetPasswordAsync</c> revalidates, rehashes the new password and calls
///         <c>MarkUsedAsync</c>, which sets <see cref="IsUsed"/> so the same link cannot be
///         replayed.</item>
///   <item><c>DeleteExpiredTokensAsync</c> reaps rows later; expiry is enforced in code on read,
///         never by the database, so an unreaped row is still safely refused.</item>
/// </list>
///
/// <para><b>Dependencies:</b> The <c>PasswordResetToken</c> table created by
/// <c>PostgresScripts/017-SecurityAndTokenPersistence.sql</c>, and the four functions in that
/// script that the repository calls (<c>InsertPasswordResetToken</c>,
/// <c>GetPasswordResetTokenByToken</c>, <c>MarkPasswordResetTokenUsed</c>,
/// <c>DeleteExpiredPasswordResetToken</c>). Property names match the column names
/// case-insensitively, which is what lets Dapper bind without an alias.</para>
///
/// <para><b>Usage:</b> A data carrier — it validates nothing itself. Never treat a materialised
/// instance as proof that a reset is permitted; a row is only usable when <see cref="IsUsed"/> is
/// <c>false</c> AND <see cref="ExpiresAt"/> is still in the future, and both checks live in
/// <c>AuthSvc</c>. Reading the row is not the same as passing the check.</para>
///
/// <para><b>Security:</b> <see cref="Token"/> is a bearer credential: whoever holds it can take
/// over the account it names. It must never be logged, never rendered into a page, and never
/// returned from an API — the only place it legitimately appears is inside the emailed link. Note
/// that requesting a reset for an address that does not exist must still look identical to the
/// caller, or the endpoint becomes an account-enumeration oracle.</para>
/// </remarks>
public class PasswordResetToken
{
    /// <summary>
    /// Surrogate key of the token row (<c>TokenId</c>, <c>BIGSERIAL</c>). Zero on an instance that
    /// has not been inserted yet; the value is assigned by the database sequence, not the caller.
    /// </summary>
    public long TokenId { get; set; }

    /// <summary>
    /// The <c>BlogUser</c> whose password this link may change. Enforced by a foreign key with
    /// <c>ON DELETE CASCADE</c>, so deleting an account also destroys every outstanding reset link
    /// for it — a deleted user cannot be resurrected through a link that was already in flight.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// The opaque secret carried in the emailed link — 256 bits of cryptographic randomness,
    /// base64url encoded, at most 255 characters and uniquely indexed. It is stored verbatim
    /// rather than hashed, so anyone with read access to this table can mint a session as any user
    /// who has an outstanding request.
    /// </summary>
    /// <remarks>
    /// A bearer credential. Never log it, never render it, never include it in an error message.
    /// </remarks>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// When the link was issued. UTC, supplied by the application rather than by the column default
    /// so that the issue time and <see cref="ExpiresAt"/> are always measured on the same clock.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the link stops being redeemable — UTC, set 24 hours after
    /// <see cref="CreatedAt"/> by <c>AuthSvc</c>. Compared against <c>DateTime.UtcNow</c> on every
    /// validation, so comparing it against a local-time value would silently widen or shrink the
    /// window by the server's UTC offset.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the link has already been redeemed. Set once by <c>MarkPasswordResetTokenUsed</c>
    /// and never cleared; the row is kept afterwards rather than deleted, so a second click on the
    /// same link is refused as "already used" instead of the more confusing "unknown token".
    /// </summary>
    public bool IsUsed { get; set; }
}
