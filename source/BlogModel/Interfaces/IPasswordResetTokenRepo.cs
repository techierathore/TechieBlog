using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for the single-use tokens that authorise a password reset.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns <c>PasswordResetToken</c>. This is a security-carrying table: the token
/// string in a reset email is, for its lifetime, a bearer credential for one account. The contract is
/// therefore deliberately narrow — resolve a token, consume it, sweep expired rows — and it is the
/// caller, not the repository, that decides whether a resolved token is still usable.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Request — <c>AuthSvc</c> generates a token and persists it with the inherited
///         <c>InsertAsync</c>.</item>
///   <item>Validate — the reset page calls <see cref="GetByTokenAsync"/> and checks expiry and used
///         state itself.</item>
///   <item>Consume — after the password is written, <c>AuthSvc</c> calls
///         <see cref="MarkUsedAsync"/>.</item>
///   <item>Sweep — <see cref="DeleteExpiredTokensAsync"/> removes rows past their expiry.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.PasswordResetTokenRepo</c> over
/// Dapper and PostgreSQL. Consumed by <c>BlogEngine.Services.AuthSvc</c>.</para>
///
/// <para><b>Usage — the validation rule lives in the caller.</b> <see cref="GetByTokenAsync"/>
/// deliberately returns the row whatever its state, so <c>AuthSvc</c> can distinguish "unknown",
/// "expired" and "already used" and log them differently. That means a caller who treats a non-null
/// result as "valid" has accepted an expired or spent token. Check <c>ExpiresOn</c> and <c>IsUsed</c>
/// before honouring one. Equally, <see cref="MarkUsedAsync"/> reports nothing, so single use is not
/// enforced here — two requests racing on the same token can both resolve it as unused.</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> The three <c>…Async</c> members carry default
/// implementations that call their synchronous twin and wrap the result with <c>Task.FromResult</c>.
/// <b>An inherited default is not asynchronous and does not observe the token at all</b> — it runs
/// inline, parks the calling thread for the whole round trip, and throws synchronously rather than
/// returning a faulted task. <c>PasswordResetTokenRepo</c> overrides all three with genuine async Dapper
/// and does honour the token; any other implementer still inheriting the defaults is unconverted.</para>
/// </remarks>
public interface IPasswordResetTokenRepo : IGenericRepository<PasswordResetToken>
{
    /// <summary>
    /// Gets a password reset token by its token string.
    /// </summary>
    /// <param name="token">The token string taken from the reset link; matched exactly.</param>
    /// <returns>The token record whatever its expiry or used state, or <c>null</c> when no such token
    /// exists. A non-null result does <b>not</b> mean the token may be honoured — see the type remarks.</returns>
    PasswordResetToken? GetByToken(string token);

    /// <summary>
    /// Marks a token as used.
    /// </summary>
    /// <param name="tokenId">Token ID to mark as used. An unknown identifier affects no rows and is a
    /// no-op, not an error. Nothing is reported back, so this cannot be used to win a race for a
    /// token.</param>
    void MarkUsed(long tokenId);

    /// <summary>
    /// Deletes expired tokens (cleanup).
    /// </summary>
    void DeleteExpiredTokens();

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    //
    // The defaults run the synchronous twin so an unconverted implementer keeps compiling; they are
    // correct but still block. PasswordResetTokenRepo overrides all of them.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a reset token by its opaque token string, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns the row whatever its expiry or used state, so the caller
    /// can tell "expired" from "already used" from "unknown".</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="token">The token string taken from the reset link; matched exactly.</param>
    /// <param name="cancellationToken">Cancels the query; ignored by the inherited default.</param>
    /// <returns>The token record whatever its expiry or used state, or <c>null</c> when no such token
    /// exists. A non-null result does <b>not</b> mean the token may be honoured.</returns>
    Task<PasswordResetToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByToken(token));

    /// <summary>
    /// Marks a token as consumed, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Sets <c>IsUsed</c> on one row.</para>
    /// </remarks>
    /// <param name="tokenId">Token ID to mark as used. An unknown identifier is a no-op.</param>
    /// <param name="cancellationToken">Cancels the statement; ignored by the inherited default.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so single use
    /// is not enforced by this member.</returns>
    Task MarkUsedAsync(long tokenId, CancellationToken cancellationToken = default)
    {
        MarkUsed(tokenId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes tokens whose expiry has passed, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Removes rows from <c>PasswordResetToken</c>.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the statement; ignored by the inherited default.</param>
    /// <returns>A task that completes when the cleanup has run. No count is reported; deleting nothing
    /// is the normal case.</returns>
    Task DeleteExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        DeleteExpiredTokens();
        return Task.CompletedTask;
    }
}
