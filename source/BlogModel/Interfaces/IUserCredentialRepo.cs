using BlogModels.Models;

namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for reading and rotating stored password credentials.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Isolates the credential columns of <c>BlogUser</c> so the login path
/// can fetch a password hash (REQ-NFR-002) and write an upgraded one without going through the
/// wide <c>UpdateBlogUser</c> function, which would overwrite unrelated profile fields.</para>
///
/// <para><b>Code Flow:</b> <c>AuthSvc.AppLogin</c> → <see cref="GetByEmail"/> →
/// <c>PasswordHasher.Verify</c> → <see cref="UpdatePasswordHash"/> when the stored hash is
/// legacy or the work factor is stale.</para>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.UserCredentialRepo</c>
/// against the PostgreSQL functions created by <c>017-SecurityAndTokenPersistence.sql</c>.</para>
///
/// <para><b>Usage:</b> Register as a transient service in <c>BlogSvcInitializer</c>.</para>
/// </remarks>
public interface IUserCredentialRepo
{
    /// <summary>
    /// Loads the credential row for an email address.
    /// </summary>
    /// <param name="emailId">The login email address.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    UserCredential? GetByEmail(string emailId);

    /// <summary>
    /// Loads the credential row for a user identifier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    UserCredential? GetByUserId(long userId);

    /// <summary>
    /// Replaces the stored password hash and sets the forced-change flag.
    /// </summary>
    /// <param name="userId">The user whose credential is being rotated.</param>
    /// <param name="passwordHash">The new encoded PBKDF2 hash.</param>
    /// <param name="mustChangePassword">Whether the user must change the password at next login.</param>
    void UpdatePasswordHash(long userId, string passwordHash, bool mustChangePassword);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    //
    // The login path is the hottest data-access path in the application: every request that restores
    // a session reads a credential. The defaults below run the synchronous twin so an unconverted
    // implementer keeps compiling, but they still park a thread — UserCredentialRepo overrides all
    // three with genuine async Dapper.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Loads the credential row for an email address, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Email addresses are stored lowercased; the lookup normalises the
    /// argument so a differently-cased login still resolves.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="emailId">The login email address.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    Task<UserCredential?> GetByEmailAsync(string emailId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByEmail(emailId));

    /// <summary>
    /// Loads the credential row for a user identifier, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    Task<UserCredential?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
        => Task.FromResult(GetByUserId(userId));

    /// <summary>
    /// Replaces the stored password hash and sets the forced-change flag, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user whose credential is being rotated.</param>
    /// <param name="passwordHash">The new encoded PBKDF2 hash.</param>
    /// <param name="mustChangePassword">Whether the user must change the password at next login.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task UpdatePasswordHashAsync(
        long userId,
        string passwordHash,
        bool mustChangePassword,
        CancellationToken cancellationToken = default)
    {
        UpdatePasswordHash(userId, passwordHash, mustChangePassword);
        return Task.CompletedTask;
    }
}
