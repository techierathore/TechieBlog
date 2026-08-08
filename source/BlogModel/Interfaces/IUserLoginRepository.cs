using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access for issued sign-in sessions.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Records one row per successful sign-in so a session can be resolved from
/// its token and later revoked.</para>
/// <para><b>Code Flow:</b> <c>AuthSvc</c> writes a row through the inherited <c>InsertAsync</c> when a
/// sign-in succeeds, and later resolves it with <see cref="GetUserByTokenAsync"/> to confirm the
/// session is still live.</para>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.UserLoginRepo</c>.</para>
///
/// <para><b>Usage:</b> The token is a bearer credential for the session it names, so the lookup keys on
/// the user <i>and</i> the token together — a token belonging to one account can never validate against
/// another. Revocation is a delete: an absent row and an unknown token are indistinguishable through
/// this contract, which is deliberate, because a caller must treat both as "not signed in".</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> <see cref="GetUserByTokenAsync"/> carries a default
/// implementation that calls its synchronous twin and wraps the result with <c>Task.FromResult</c>.
/// <b>An inherited default is not asynchronous and does not observe the token at all</b> — it runs
/// inline and parks the calling thread for the whole round trip. <c>UserLoginRepo</c> overrides it with
/// genuine async Dapper; any other implementer still inheriting the default is unconverted.</para>
/// </remarks>
public interface IUserLoginRepository : IGenericRepository<UserLogin>
{
    /// <summary>
    /// Resolves an active session from its owner and token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Matching on both the user and the token prevents a token
    /// belonging to one account from validating against another.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The session's owner.</param>
    /// <param name="token">The issued token value.</param>
    /// <returns>The session row, or <c>null</c> when it does not exist or has been revoked.</returns>
    UserLogin? GetUserByToken(long userId, string token);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over the member above.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves an active session from its owner and token, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Matching on both the user and the token prevents a token
    /// belonging to one account from validating against another.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The session's owner.</param>
    /// <param name="token">The issued token value.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The session row, or <c>null</c> when it does not exist or has been revoked.</returns>
    Task<UserLogin?> GetUserByTokenAsync(long userId, string token, CancellationToken cancellationToken = default)
        => Task.FromResult(GetUserByToken(userId, token));
}
