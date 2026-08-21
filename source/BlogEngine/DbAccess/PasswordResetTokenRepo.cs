using BlogEngine.DaCore;
using BlogModels.Models;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Database-backed repository for password-reset tokens (REQ-NFR-019, BRD-5).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists reset tokens in the <c>PasswordResetToken</c> table so a link
/// mailed to a user still works after an application restart and across every instance behind a
/// load balancer. This replaces the in-memory singleton kept under ADR-008, which silently
/// invalidated every outstanding link whenever the process recycled.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>AuthSvc.RequestPasswordResetAsync</c> calls <see cref="InsertAsync"/> with a 24-hour token.</item>
///   <item>The user opens the link; <see cref="GetByTokenAsync"/> resolves it.</item>
///   <item><c>AuthSvc.ResetPasswordAsync</c> calls <see cref="MarkUsedAsync"/> so the token is single-use.</item>
///   <item>A maintenance pass calls <see cref="DeleteExpiredTokensAsync"/>.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Dapper and the PostgreSQL functions created by
/// <c>017-SecurityAndTokenPersistence.sql</c>.</para>
///
/// <para><b>Related:</b> the anonymous email-verification token store (REQ-FN-048) is a separate
/// table with a deliberately similar shape; the two are not merged because their lifetimes,
/// owners and expiry rules differ.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> the <c>…Async</c> members open the connection
/// asynchronously and flow the cancellation token; the synchronous twins are retained only until
/// the last caller migrates and execute the same SQL constants. Both twins bind their timestamps
/// through <see cref="DbTimestamp.AsTimestamp(DateTime)"/> — see <see cref="InsertToGetIdAsync"/>
/// for why that is load-bearing rather than decorative.</para>
///
/// <para><b>Usage:</b> Registered as a transient service in <c>BlogSvcInitializer</c>.</para>
/// </remarks>
public class PasswordResetTokenRepo : GenericRepository<PasswordResetToken>, IPasswordResetTokenRepo
{
    private const string SelectByTokenSql = "SELECT * FROM GetPasswordResetTokenByToken(@pToken)";

    private const string MarkUsedSql = "SELECT MarkPasswordResetTokenUsed(@pTokenId)";

    private const string DeleteExpiredSql = "SELECT DeleteExpiredPasswordResetToken()";

    private const string InsertSql =
        "SELECT InsertPasswordResetToken(@pUserId, @pToken, @pCreatedAt, @pExpiresAt)";

    private const string SelectByIdSql = "SELECT * FROM GetPasswordResetTokenById(@pTokenId)";

    private const string SelectByUserSql = "SELECT * FROM GetPasswordResetTokenByUser(@pUserId)";

    private const string SelectAllSql = @"
            SELECT TokenId, UserId, Token, CreatedAt, ExpiresAt, IsUsed
            FROM PasswordResetToken ORDER BY TokenId DESC";

    private const string SelectPagedSql = @"
            SELECT TokenId, UserId, Token, CreatedAt, ExpiresAt, IsUsed
            FROM PasswordResetToken ORDER BY TokenId DESC LIMIT @pPageSize OFFSET @pOffSet";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public PasswordResetTokenRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Resolves a reset token by its opaque token string, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns the row regardless of expiry or used state — the
    /// caller distinguishes "expired" from "already used" from "unknown" to give an accurate
    /// message. A blank token is rejected before a round trip is spent on it.</para>
    /// <para><b>Flow:</b> guard → helper opens the connection asynchronously → call
    /// <c>GetPasswordResetTokenByToken</c>.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="token">The token string taken from the reset link.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The token record, or <c>null</c> when no such token exists.</returns>
    public async Task<PasswordResetToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("pToken", token);

        return await QueryFirstOrDefaultAsync<PasswordResetToken>(
            SelectByTokenSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a token as consumed so it cannot be replayed, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reset links are single-use; the row is kept rather than
    /// deleted so an audit of recent resets stays possible until cleanup runs.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → call
    /// <c>MarkPasswordResetTokenUsed</c>.</para>
    /// <para><b>Side Effects:</b> Sets <c>IsUsed</c> on one row.</para>
    /// </remarks>
    /// <param name="tokenId">Identifier of the token to consume.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public async Task MarkUsedAsync(long tokenId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pTokenId", tokenId);

        await ExecuteAsync(MarkUsedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes tokens whose expiry has passed, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Expired tokens carry no value and would otherwise accumulate
    /// forever; used tokens older than their expiry go with them.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → call
    /// <c>DeleteExpiredPasswordResetToken</c>.</para>
    /// <para><b>Side Effects:</b> Removes rows from <c>PasswordResetToken</c>.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the cleanup has run.</returns>
    public async Task DeleteExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(DeleteExpiredSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new reset token, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generated identity is written back onto the entity so the
    /// caller can mark the token used later without a second lookup.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/> → assign the id.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>PasswordResetToken</c>.</para>
    /// </remarks>
    /// <param name="entity">The token to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(PasswordResetToken entity, CancellationToken cancellationToken = default)
    {
        entity.TokenId = await InsertToGetIdAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new reset token and returns its generated identifier, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Timestamps are supplied by the caller in UTC so token expiry
    /// does not depend on the database server's time zone, and are re-stamped as
    /// <see cref="DateTimeKind.Unspecified"/> by <see cref="DbTimestamp.AsTimestamp(DateTime)"/>
    /// before they are bound. That normalisation is not cosmetic — see the note below.</para>
    /// <para><b>Flow:</b> bind parameters through the timestamp guard → helper opens the connection
    /// asynchronously → call <c>InsertPasswordResetToken</c> → read the returned key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>PasswordResetToken</c>.</para>
    /// </remarks>
    /// <param name="entity">The token to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>TokenId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(PasswordResetToken entity, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertSql, BuildInsertParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a single token by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The administrative lookup, keyed on the surrogate id rather than
    /// the secret. It applies no expiry or used filter, so it can fetch the row a caller is about to
    /// mark consumed. The reset flow itself never uses this member — a user presents a token string,
    /// not an id, so <see cref="GetByTokenAsync"/> is the path that matters there.</para>
    /// <para><b>Flow:</b> bind <c>pTokenId</c> → helper opens the connection asynchronously → call
    /// the stored function <c>GetPasswordResetTokenById</c> → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only call.</para>
    /// </remarks>
    /// <param name="tokenId">The token identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The token record, or <c>null</c> when not found.</returns>
    public override async Task<PasswordResetToken?> GetSingleAsync(long tokenId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pTokenId", tokenId);

        return await QueryFirstOrDefaultAsync<PasswordResetToken>(
            SelectByIdSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a single token by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the 32-bit key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second stored function to call.</para>
    /// <para><b>Flow:</b> widen → forward the task directly to <see cref="GetSingleAsync"/> — not
    /// marked <c>async</c>, so no state machine is allocated for a pure delegation.</para>
    /// <para><b>Side Effects:</b> None — read-only call.</para>
    /// </remarks>
    /// <param name="tokenId">The token identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The token record, or <c>null</c> when not found.</returns>
    public override Task<PasswordResetToken?> GetIntSingleAsync(int tokenId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(tokenId, cancellationToken);
    }

    /// <summary>
    /// Retrieves every token issued to one user, newest first, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every token ever issued to the user, spent and unspent alike —
    /// the ordering and the filtering both live inside <c>GetPasswordResetTokenByUser</c>, not here,
    /// so a change to what "newest first" means is a migration rather than a code change. A user who
    /// has never requested a reset yields an empty sequence, never <c>null</c>.</para>
    /// <para><b>Flow:</b> bind <c>pUserId</c> → helper opens the connection asynchronously → call the
    /// stored function → buffered, materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only call.</para>
    /// </remarks>
    /// <param name="userId">The owning user identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's reset tokens, or an empty sequence when they have none.</returns>
    public override async Task<IEnumerable<PasswordResetToken>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pUserId", userId);

        return await QueryAsync<PasswordResetToken>(
            SelectByUserSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves every reset token in the system, newest first, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The one read here that is inline SQL rather than a stored
    /// function, because there is no <c>GetAllPasswordResetTokens</c> to call. Its projection is
    /// therefore written out in full — <c>TokenId, UserId, Token, CreatedAt, ExpiresAt, IsUsed</c>,
    /// the complete entity — and ordered <c>TokenId DESC</c>, which stands in for "newest first"
    /// because the key is generated in issue order. Unpaged and unfiltered: this is a maintenance
    /// member, and on a busy site the paged twin is the one to use.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised
    /// list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All token records, newest first; an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<PasswordResetToken>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<PasswordResetToken>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a page of reset tokens, newest first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The paged form of <see cref="GetAllAsync"/>, sharing its
    /// projection and its <c>TokenId DESC</c> ordering so a page boundary means the same thing in
    /// both. Paging is applied in SQL, so a long reset history never crosses the wire in full.</para>
    /// <para><b>Flow:</b> bind the window → helper opens the connection asynchronously →
    /// LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page of token records.</returns>
    public override async Task<IEnumerable<PasswordResetToken>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pPageSize", pageSize);
        parameters.Add("pOffSet", offSet);

        return await QueryAsync<PasswordResetToken>(
            SelectPagedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the mutable state of an existing token, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the consumed flag is mutable, so an update is expressed
    /// as "mark used" when the entity says so.</para>
    /// <para><b>Flow:</b> delegate to <see cref="MarkUsedAsync"/> when <c>IsUsed</c> is set.</para>
    /// <para><b>Side Effects:</b> May update one row.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The token whose state should be written.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override Task UpdateAsync(PasswordResetToken entityToUpdate, CancellationToken cancellationToken = default)
    {
        return entityToUpdate != null && entityToUpdate.IsUsed
            ? MarkUsedAsync(entityToUpdate.TokenId, cancellationToken)
            : Task.CompletedTask;
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // =================================================================================================

    /// <summary>
    /// Resolves a reset token by its opaque token string.
    /// </summary>
    /// <param name="token">The token string taken from the reset link.</param>
    /// <returns>The token record, or <c>null</c> when no such token exists.</returns>
    public PasswordResetToken? GetByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("pToken", token);

        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<PasswordResetToken>(SelectByTokenSql, parameters);
    }

    /// <summary>
    /// Marks a token as consumed so it cannot be replayed.
    /// </summary>
    /// <param name="tokenId">Identifier of the token to consume.</param>
    public void MarkUsed(long tokenId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pTokenId", tokenId);

        using var connection = GetOpenConnection();
        connection.Execute(MarkUsedSql, parameters);
    }

    /// <summary>
    /// Deletes tokens whose expiry has passed.
    /// </summary>
    public void DeleteExpiredTokens()
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteExpiredSql);
    }

    /// <summary>
    /// Inserts a new reset token.
    /// </summary>
    /// <param name="entity">The token to persist.</param>
    public override void Insert(PasswordResetToken entity)
    {
        entity.TokenId = InsertToGetId(entity);
    }

    /// <summary>
    /// Inserts a new reset token and returns its generated identifier.
    /// </summary>
    /// <param name="entity">The token to persist.</param>
    /// <returns>The generated <c>TokenId</c>.</returns>
    public override long InsertToGetId(PasswordResetToken entity)
    {
        using var connection = GetOpenConnection();
        return connection.QuerySingle<long>(InsertSql, BuildInsertParameters(entity));
    }

    /// <summary>
    /// Retrieves a single token by its identifier.
    /// </summary>
    /// <param name="tokenId">The token identifier.</param>
    /// <returns>The token record, or <c>null</c> when not found.</returns>
    public override PasswordResetToken? GetSingle(long tokenId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pTokenId", tokenId);

        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<PasswordResetToken>(SelectByIdSql, parameters);
    }

    /// <summary>
    /// Retrieves a single token by its identifier.
    /// </summary>
    /// <param name="tokenId">The token identifier.</param>
    /// <returns>The token record, or <c>null</c> when not found.</returns>
    public override PasswordResetToken? GetIntSingle(int tokenId)
    {
        return GetSingle(tokenId);
    }

    /// <summary>
    /// Retrieves every token issued to one user, newest first.
    /// </summary>
    /// <param name="userId">The owning user identifier.</param>
    /// <returns>The user's reset tokens.</returns>
    public override IEnumerable<PasswordResetToken> GetAllById(long userId)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pUserId", userId);

        using var connection = GetOpenConnection();
        return connection.Query<PasswordResetToken>(SelectByUserSql, parameters).ToList();
    }

    /// <summary>
    /// Retrieves every reset token in the system, newest first.
    /// </summary>
    /// <returns>All token records.</returns>
    public override IEnumerable<PasswordResetToken> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<PasswordResetToken>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Retrieves a page of reset tokens, newest first.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>The requested page of token records.</returns>
    public override IEnumerable<PasswordResetToken> GetPagedData(int pageSize, int offSet)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pPageSize", pageSize);
        parameters.Add("pOffSet", offSet);

        using var connection = GetOpenConnection();
        return connection.Query<PasswordResetToken>(SelectPagedSql, parameters).ToList();
    }

    /// <summary>
    /// Updates the mutable state of an existing token.
    /// </summary>
    /// <param name="entityToUpdate">The token whose state should be written.</param>
    public override void Update(PasswordResetToken entityToUpdate)
    {
        if (entityToUpdate != null && entityToUpdate.IsUsed)
            MarkUsed(entityToUpdate.TokenId);
    }

    // =================================================================================================
    // Parameter builder — shared by both insert twins so neither can lose the timestamp guard.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter set for <c>InsertPasswordResetToken</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The timestamps are re-stamped as <c>Unspecified</c> deliberately.
    /// <c>AuthSvc</c> supplies <c>DateTime.UtcNow</c>, whose Kind is <c>Utc</c>, and Npgsql infers
    /// <c>timestamptz</c> from that Kind. <c>InsertPasswordResetToken</c> declares <c>TIMESTAMP</c>
    /// (without time zone) and PostgreSQL resolves function overloads strictly, so the inferred
    /// signature matched no function and every reset request failed with <c>42883</c> — silently,
    /// because the forgot-password page returns the same generic message whether or not a mail was
    /// actually sent.</para>
    ///
    /// <para>Setting <c>DbType</c> is <b>not</b> sufficient here: since Npgsql 6, <c>DbType.DateTime</c>
    /// itself maps to <c>timestamptz</c>, and asking for <c>timestamp</c> while the value still carries
    /// <c>Kind = Utc</c> is rejected outright. Normalising the Kind is what actually changes the wire
    /// type, which is exactly what <see cref="DbTimestamp.AsTimestamp(DateTime)"/> does.</para>
    ///
    /// <para>The instants are unchanged — only the Kind label is dropped — so the values stay UTC,
    /// matching the <c>TIMESTAMP</c> columns and every other table in this schema. The sibling
    /// email-verification store avoids this trap only because it inserts with plain SQL, where
    /// PostgreSQL casts the argument to the column type for us.</para>
    ///
    /// <para><b>Because both insert twins bind through here, neither can lose the guard.</b> A green
    /// build would not tell you if one had.</para>
    /// </remarks>
    /// <param name="entity">The token being inserted.</param>
    /// <returns>The parameters Dapper binds to the stored function.</returns>
    private static DynamicParameters BuildInsertParameters(PasswordResetToken entity)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pUserId", entity.UserId);
        parameters.Add("pToken", entity.Token);
        parameters.Add("pCreatedAt", DbTimestamp.AsTimestamp(entity.CreatedAt));
        parameters.Add("pExpiresAt", DbTimestamp.AsTimestamp(entity.ExpiresAt));
        return parameters;
    }
}
