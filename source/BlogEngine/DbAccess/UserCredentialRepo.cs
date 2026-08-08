using BlogEngine.DaCore;
using BlogModels.Interfaces;
using BlogModels.Models;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for reading and rotating stored password credentials (REQ-NFR-002).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Salted PBKDF2 hashes cannot be compared inside SQL, so login now reads
/// the stored hash and verifies it in application code. This repository is the only place that
/// touches <c>BlogUser.LoginPass</c> and <c>BlogUser.MustChangePassword</c>, which keeps the
/// hash out of the wide user projections and avoids the profile-clobbering
/// <c>UpdateBlogUser</c> round trip when only the password changes.</para>
///
/// <para><b>Code Flow:</b> <c>AuthSvc</c> → <see cref="GetByEmailAsync"/> → verify →
/// <see cref="UpdatePasswordHashAsync"/> when the credential must be upgraded or reset.</para>
///
/// <para><b>Dependencies:</b> Dapper, and the <c>GetUserCredentialByEmail</c>,
/// <c>GetUserCredentialById</c> and <c>UpdateUserPassword</c> PostgreSQL functions created by
/// <c>017-SecurityAndTokenPersistence.sql</c>.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> this is the hottest path in the application — every
/// sign-in and every session restore reads a credential — so the <c>…Async</c> members open the
/// connection asynchronously and flow the cancellation token. The synchronous twins are retained
/// only until the last caller migrates and execute the same SQL constants.</para>
///
/// <para><b>Usage:</b> Registered as a transient service in <c>BlogSvcInitializer</c>.</para>
/// </remarks>
public class UserCredentialRepo : GenericRepository<UserCredential>, IUserCredentialRepo
{
    private const string SelectByEmailSql = "SELECT * FROM GetUserCredentialByEmail(@pLoginMail)";

    private const string SelectByUserIdSql = "SELECT * FROM GetUserCredentialById(@pUserId)";

    private const string UpdatePasswordSql =
        "SELECT UpdateUserPassword(@pUserId, @pLoginPass, @pMustChangePassword)";

    private const string NotEnumeratedMessage = "Credentials are never enumerated.";

    private const string NotCreatedHereMessage = "Accounts are created through BlogUserRepo.";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public UserCredentialRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Loads the credential row for an email address, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Email addresses are stored lowercased; the lookup trims the
    /// argument so a login pasted with trailing whitespace still resolves, and the function itself
    /// compares case-insensitively (<c>020-CaseInsensitiveEmailLookup.sql</c>). An unknown address
    /// is a normal answer and yields <c>null</c> rather than an exception, because the sign-in path
    /// must not distinguish "no such account" from "wrong password".</para>
    /// <para><b>Flow:</b> guard → normalise → helper opens the connection asynchronously → call
    /// <c>GetUserCredentialByEmail</c>.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="emailId">The login email address.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public async Task<UserCredential?> GetByEmailAsync(string emailId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("pLoginMail", emailId.Trim());

        return await QueryFirstOrDefaultAsync<UserCredential>(
            SelectByEmailSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads the credential row for a user identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used by the change-password flow, which already knows the
    /// signed-in user's id.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → call
    /// <c>GetUserCredentialById</c>.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public async Task<UserCredential?> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pUserId", userId);

        return await QueryFirstOrDefaultAsync<UserCredential>(
            SelectByUserIdSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Replaces the stored password hash and sets the forced-change flag, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Writes only the credential columns and stamps
    /// <c>UpdatedOn</c>, so a silent re-hash during login cannot overwrite profile fields that
    /// the caller did not load.</para>
    /// <para><b>Flow:</b> bind parameters → helper opens the connection asynchronously → call
    /// <c>UpdateUserPassword</c>.</para>
    /// <para><b>Side Effects:</b> Updates one <c>BlogUser</c> row.</para>
    /// </remarks>
    /// <param name="userId">The user whose credential is being rotated.</param>
    /// <param name="passwordHash">The new encoded PBKDF2 hash.</param>
    /// <param name="mustChangePassword">Whether the user must change the password at next login.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public async Task UpdatePasswordHashAsync(
        long userId,
        string passwordHash,
        bool mustChangePassword,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            UpdatePasswordSql,
            BuildUpdateParameters(userId, passwordHash, mustChangePassword),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Not supported — credentials are only ever read by email or by user id.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Enumerating password hashes has no legitimate caller, so the
    /// member refuses rather than offering a query that would.</para>
    /// <para><b>Flow:</b> return a faulted task, so an <c>await</c>-only caller observes the refusal
    /// exactly as a converted repository's failures are observed.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A faulted task carrying <see cref="NotSupportedException"/>.</returns>
    public override Task<IEnumerable<UserCredential>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromException<IEnumerable<UserCredential>>(new NotSupportedException(NotEnumeratedMessage));
    }

    /// <summary>
    /// Not supported — credentials are only ever read by email or by user id.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic contract's "all children of a parent" shape has no
    /// meaning for a credential, and the only way to satisfy it would be a statement that returns
    /// password hashes in bulk. Refusing is the design.</para>
    /// <para><b>Flow:</b> return a faulted task, so an <c>await</c>-only caller observes the refusal
    /// exactly as any other failure on this repository.</para>
    /// <para><b>Side Effects:</b> None — no SQL is executed and no connection is opened.</para>
    /// </remarks>
    /// <param name="userId">Unused.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A faulted task carrying <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always, once the returned task is awaited.</exception>
    public override Task<IEnumerable<UserCredential>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return Task.FromException<IEnumerable<UserCredential>>(new NotSupportedException(NotEnumeratedMessage));
    }

    /// <summary>
    /// Not supported — credentials are only ever read by email or by user id.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is enumeration with a window on it. A credential grid
    /// would be a hash-dumping endpoint waiting to be found, so no statement exists for it.</para>
    /// <para><b>Flow:</b> return a faulted task.</para>
    /// <para><b>Side Effects:</b> None — no SQL is executed and no connection is opened.</para>
    /// </remarks>
    /// <param name="pageSize">Unused.</param>
    /// <param name="offSet">Unused.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A faulted task carrying <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always, once the returned task is awaited.</exception>
    public override Task<IEnumerable<UserCredential>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return Task.FromException<IEnumerable<UserCredential>>(new NotSupportedException(NotEnumeratedMessage));
    }

    /// <summary>
    /// Loads the credential row for a user identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic contract's key lookup, which for this entity is the
    /// user id — a credential has no identity of its own, it is a narrow projection of a
    /// <c>BlogUser</c> row. Forwarding rather than writing a second statement is what keeps that
    /// true.</para>
    /// <para><b>Flow:</b> forward the task directly to <see cref="GetByUserIdAsync"/> — not marked
    /// <c>async</c>, so no state machine is allocated for a pure delegation.</para>
    /// <para><b>Side Effects:</b> None — read-only call to <c>GetUserCredentialById</c>.</para>
    /// </remarks>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public override Task<UserCredential?> GetSingleAsync(long userId, CancellationToken cancellationToken = default)
    {
        return GetByUserIdAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Loads the credential row for a user identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the 32-bit key and reuses the same lookup; the underlying
    /// column is <c>BIGINT</c>, so there is no second statement to write and no narrowing to get
    /// wrong.</para>
    /// <para><b>Flow:</b> widen → forward the task directly to <see cref="GetByUserIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only call to <c>GetUserCredentialById</c>.</para>
    /// </remarks>
    /// <param name="userId">The user identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public override Task<UserCredential?> GetIntSingleAsync(int userId, CancellationToken cancellationToken = default)
    {
        return GetByUserIdAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Not supported — accounts are created through <c>BlogUserRepo</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A credential is not an entity that can exist on its own — it is
    /// two columns of a <c>BlogUser</c> row. Allowing an insert here would mean a code path that
    /// creates a password without creating the account it belongs to.</para>
    /// <para><b>Flow:</b> return a faulted task.</para>
    /// <para><b>Side Effects:</b> None — no SQL is executed and no connection is opened.</para>
    /// </remarks>
    /// <param name="entity">Unused.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A faulted task carrying <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always, once the returned task is awaited.</exception>
    public override Task InsertAsync(UserCredential entity, CancellationToken cancellationToken = default)
    {
        return Task.FromException(new NotSupportedException(NotCreatedHereMessage));
    }

    /// <summary>
    /// Not supported — accounts are created through <c>BlogUserRepo</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The key-returning half of the insert pair, refused for the same
    /// reason as <see cref="InsertAsync"/>. Both halves refuse together — a half-refusing insert pair
    /// would leave exactly one usable way to create an orphaned credential.</para>
    /// <para><b>Flow:</b> return a faulted task.</para>
    /// <para><b>Side Effects:</b> None — no SQL is executed and no connection is opened.</para>
    /// </remarks>
    /// <param name="entity">Unused.</param>
    /// <param name="cancellationToken">Ignored.</param>
    /// <returns>A faulted task carrying <see cref="NotSupportedException"/>.</returns>
    /// <exception cref="NotSupportedException">Always, once the returned task is awaited.</exception>
    public override Task<long> InsertToGetIdAsync(UserCredential entity, CancellationToken cancellationToken = default)
    {
        return Task.FromException<long>(new NotSupportedException(NotCreatedHereMessage));
    }

    /// <summary>
    /// Rotates the credential carried by the supplied entity, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic contract's update, narrowed to the only thing this
    /// repository is allowed to write. It unpacks the entity into the three values
    /// <c>UpdateUserPassword</c> accepts, so an entity carrying stale profile fields cannot smuggle
    /// them into the statement — the projection is fixed by the stored function, not by the caller.</para>
    /// <para><b>Flow:</b> unpack the entity → forward the task directly to
    /// <see cref="UpdatePasswordHashAsync"/>.</para>
    /// <para><b>Side Effects:</b> Updates <c>LoginPass</c>, <c>MustChangePassword</c> and
    /// <c>UpdatedOn</c> on one <c>BlogUser</c> row. An unknown user id matches nothing inside the
    /// function and is a silent no-op.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The credential holding the new hash and flag.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override Task UpdateAsync(UserCredential entityToUpdate, CancellationToken cancellationToken = default)
    {
        return UpdatePasswordHashAsync(
            entityToUpdate.UserId,
            entityToUpdate.LoginPass,
            entityToUpdate.MustChangePassword,
            cancellationToken);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // =================================================================================================

    /// <summary>
    /// Loads the credential row for an email address.
    /// </summary>
    /// <param name="emailId">The login email address.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public UserCredential? GetByEmail(string emailId)
    {
        if (string.IsNullOrWhiteSpace(emailId))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("pLoginMail", emailId.Trim());

        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<UserCredential>(SelectByEmailSql, parameters);
    }

    /// <summary>
    /// Loads the credential row for a user identifier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public UserCredential? GetByUserId(long userId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pUserId", userId);

        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<UserCredential>(SelectByUserIdSql, parameters);
    }

    /// <summary>
    /// Replaces the stored password hash and sets the forced-change flag.
    /// </summary>
    /// <param name="userId">The user whose credential is being rotated.</param>
    /// <param name="passwordHash">The new encoded PBKDF2 hash.</param>
    /// <param name="mustChangePassword">Whether the user must change the password at next login.</param>
    public void UpdatePasswordHash(long userId, string passwordHash, bool mustChangePassword)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdatePasswordSql, BuildUpdateParameters(userId, passwordHash, mustChangePassword));
    }

    /// <summary>
    /// Not supported — credentials are only ever read by email or by user id.
    /// </summary>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override IEnumerable<UserCredential> GetAll()
    {
        throw new NotSupportedException(NotEnumeratedMessage);
    }

    /// <summary>
    /// Not supported — credentials are only ever read by email or by user id.
    /// </summary>
    /// <param name="userId">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override IEnumerable<UserCredential> GetAllById(long userId)
    {
        throw new NotSupportedException(NotEnumeratedMessage);
    }

    /// <summary>
    /// Not supported — credentials are only ever read by email or by user id.
    /// </summary>
    /// <param name="pageSize">Unused.</param>
    /// <param name="offSet">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override IEnumerable<UserCredential> GetPagedData(int pageSize, int offSet)
    {
        throw new NotSupportedException(NotEnumeratedMessage);
    }

    /// <summary>
    /// Loads the credential row for a user identifier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public override UserCredential? GetSingle(long userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Loads the credential row for a user identifier.
    /// </summary>
    /// <param name="userId">The user identifier.</param>
    /// <returns>The credential if the account exists; otherwise <c>null</c>.</returns>
    public override UserCredential? GetIntSingle(int userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Not supported — accounts are created through <c>BlogUserRepo</c>.
    /// </summary>
    /// <param name="entity">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Insert(UserCredential entity)
    {
        throw new NotSupportedException(NotCreatedHereMessage);
    }

    /// <summary>
    /// Not supported — accounts are created through <c>BlogUserRepo</c>.
    /// </summary>
    /// <param name="entity">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override long InsertToGetId(UserCredential entity)
    {
        throw new NotSupportedException(NotCreatedHereMessage);
    }

    /// <summary>
    /// Rotates the credential carried by the supplied entity.
    /// </summary>
    /// <param name="entityToUpdate">The credential holding the new hash and flag.</param>
    public override void Update(UserCredential entityToUpdate)
    {
        UpdatePasswordHash(entityToUpdate.UserId, entityToUpdate.LoginPass, entityToUpdate.MustChangePassword);
    }

    // =================================================================================================
    // Parameter builder — shared by both rotation twins.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter set for <c>UpdateUserPassword</c>.
    /// </summary>
    /// <param name="userId">The user whose credential is being rotated.</param>
    /// <param name="passwordHash">The new encoded PBKDF2 hash.</param>
    /// <param name="mustChangePassword">Whether the user must change the password at next login.</param>
    /// <returns>The parameters Dapper binds to the stored function.</returns>
    private static DynamicParameters BuildUpdateParameters(long userId, string passwordHash, bool mustChangePassword)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pUserId", userId);
        parameters.Add("pLoginPass", passwordHash);
        parameters.Add("pMustChangePassword", mustChangePassword);
        return parameters;
    }
}
