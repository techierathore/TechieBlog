

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserLogin data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for the <c>userlogins</c> table in PostgreSQL —
/// one row per issued session, so a token can be resolved back to its owner and revoked.</para>
///
/// <para><b>Code Flow:</b> <c>AuthSvc.IssueSessionAsync</c> writes a row on every successful
/// sign-in; <c>AuthSvc.GetUserByTokenAsync</c> reads it back on every session restore.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Registered by <c>BlogSvcInitializer</c> as <c>IUserLoginRepository</c>. Call
/// the <c>…Async</c> members; the synchronous twins exist only until the last caller migrates
/// (REQ-NFR-026) and are deleted in the final stage, at which point the <c>…Async</c> members are the
/// whole surface.</para>
///
/// <para><b>Note:</b> PostgreSQL stores unquoted identifiers as lowercase, which is why every
/// column here is written lowercase.</para>
///
/// <para><b>Projection:</b> every read here is <c>SELECT *</c>, so the projection tracks the table
/// rather than being pinned by this file. That is why adding a column to <c>userlogins</c> needs no
/// change here — and equally why a column renamed in a migration silently stops binding instead of
/// failing to compile. The write statements name their columns explicitly, so only the reads have
/// this property. The column name <c>exiprydate</c> is misspelled in the schema; it is spelled that
/// way here deliberately so the parameter binds.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> the <c>…Async</c> members open the connection
/// asynchronously and flow the cancellation token; the synchronous twins are retained only until
/// the last caller migrates and execute the same SQL constants. Every <see cref="DateTime"/> is
/// bound through <c>DbTimestamp.AsTimestamp</c> because all four date columns are <c>TIMESTAMP</c>
/// without time zone.</para>
/// </remarks>
public class UserLoginRepo : GenericRepository<UserLogin>, IUserLoginRepository
{
    private const string SelectByUserSql =
        "SELECT * FROM userlogins WHERE userid = @UserId ORDER BY logindate DESC";

    private const string SelectAllSql = "SELECT * FROM userlogins ORDER BY loginid";

    private const string SelectByIdSql = "SELECT * FROM userlogins WHERE loginid = @LoginId";

    private const string SelectByTokenSql = @"
            SELECT * FROM userlogins
            WHERE userid = @pUserId AND logintoken = @pLoginToken AND tokenstatus = 'ValidToken'";

    private const string InsertSql = @"
            INSERT INTO userlogins (userid, logindate, logintoken, tokenstatus, exiprydate, issuedate)
            VALUES (@UserId, @LoginDate, @LoginToken, @TokenStatus, @ExipryDate, @IssueDate)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING loginid";

    private const string UpdateSql = @"
            UPDATE userlogins
            SET logindate = @LoginDate, logintoken = @LoginToken, tokenstatus = @TokenStatus,
                exiprydate = @ExipryDate, issuedate = @IssueDate
            WHERE loginid = @LoginId";

    private const string SelectPagedSql =
        "SELECT * FROM userlogins ORDER BY loginid LIMIT @PageSize OFFSET @OffSet";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public UserLoginRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Retrieves every session issued to one user, newest first, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Newest first, because a session list is only ever read to find
    /// the current one.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The session owner's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's sessions, or an empty sequence when they have none.</returns>
    public override async Task<IEnumerable<UserLogin>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserLogin>(
            SelectByUserSql, new { UserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves every recorded session, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reads every row of <c>userlogins</c>, revoked and expired
    /// included — this is an audit view, and hiding a revoked session is exactly what would make it
    /// useless. Ordered <c>loginid</c> ascending, i.e. oldest first, which is the opposite of
    /// <see cref="GetAllByIdAsync"/>; the per-user list is read to find the current session, this one
    /// is read as a history. Unpaged, so prefer <see cref="GetPagedDataAsync"/> on a busy site.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised
    /// list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All sessions, oldest first; an empty sequence when none have been issued.</returns>
    public override async Task<IEnumerable<UserLogin>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserLogin>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a single session by its INT identifier, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the 32-bit key and reuses the BIGINT lookup; <c>loginid</c>
    /// is <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → forward the task directly to <see cref="GetSingleAsync"/> — not
    /// marked <c>async</c>, so no state machine is allocated for a pure delegation.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginId">The session identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The session, or <c>null</c> when the key is unknown.</returns>
    public override Task<UserLogin?> GetIntSingleAsync(int loginId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(loginId, cancellationToken);
    }

    /// <summary>
    /// Resolves an active session from its owner and token, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Matching on the user, the token <i>and</i> the valid status means
    /// a revoked token stops working before it expires, and a token belonging to one account cannot
    /// validate against another.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → three-way match.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The session's owner.</param>
    /// <param name="token">The issued token value.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The session row, or <c>null</c> when it does not exist or has been revoked.</returns>
    public async Task<UserLogin?> GetUserByTokenAsync(long userId, string token, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<UserLogin>(
            SelectByTokenSql,
            new { pUserId = userId, pLoginToken = token },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a single session by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Lookup by the surrogate key only — no status filter, so it
    /// returns revoked and expired sessions too. It is therefore <b>not</b> a session-validation
    /// path: authenticating a token is <see cref="GetUserByTokenAsync"/>, which additionally requires
    /// the owner to match and the status to be <c>ValidToken</c>. An unknown key is a normal answer
    /// and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> bind the key → helper opens the connection asynchronously → first row or
    /// <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="loginId">The session identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The session, or <c>null</c> when the key is unknown.</returns>
    public override async Task<UserLogin?> GetSingleAsync(long loginId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<UserLogin>(
            SelectByIdSql, new { LoginId = loginId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a session and returns its generated identifier, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key.</para>
    /// <para><b>Flow:</b> bind through the timestamp guard → helper opens the connection
    /// asynchronously → INSERT … RETURNING.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>userlogins</c>.</para>
    /// </remarks>
    /// <param name="entity">The session to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>loginid</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(UserLogin entity, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildWriteParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a session, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The sign-in path does not need the generated key, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> bind through the timestamp guard → helper opens the connection
    /// asynchronously → INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>userlogins</c>.</para>
    /// </remarks>
    /// <param name="entity">The session to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(UserLogin entity, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildWriteParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates a recorded session, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Revocation is expressed by writing a new <c>tokenstatus</c>, so
    /// the whole mutable row is rewritten together.</para>
    /// <para><b>Flow:</b> bind through the timestamp guard → helper opens the connection
    /// asynchronously → UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>loginid</c>.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The session carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(UserLogin entityToUpdate, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(entityToUpdate), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves a page of sessions, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The paged form of <see cref="GetAllAsync"/>, sharing its
    /// <c>ORDER BY loginid</c> so a page boundary means the same thing in both. The ordering is on
    /// the key rather than on <c>logindate</c> precisely because paging needs a total order that
    /// cannot tie — two sessions issued in the same second would otherwise be free to swap pages
    /// between requests and be shown twice or not at all.</para>
    /// <para><b>Flow:</b> bind the window → helper opens the connection asynchronously →
    /// LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<UserLogin>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserLogin>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // =================================================================================================

    /// <summary>
    /// Retrieves every session issued to one user, newest first.
    /// </summary>
    /// <param name="userId">The session owner's identifier.</param>
    /// <returns>The user's sessions.</returns>
    public override IEnumerable<UserLogin> GetAllById(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserLogin>(SelectByUserSql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Retrieves every recorded session.
    /// </summary>
    /// <returns>All sessions.</returns>
    public override IEnumerable<UserLogin> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserLogin>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Retrieves a single session by its INT identifier.
    /// </summary>
    /// <param name="loginId">The session identifier.</param>
    /// <returns>The session, or <c>null</c> when the key is unknown.</returns>
    public override UserLogin? GetIntSingle(int loginId)
    {
        return GetSingle(loginId);
    }

    /// <summary>
    /// Resolves an active session from its owner and token.
    /// </summary>
    /// <param name="userId">The session's owner.</param>
    /// <param name="token">The issued token value.</param>
    /// <returns>The session row, or <c>null</c> when it does not exist or has been revoked.</returns>
    public UserLogin? GetUserByToken(long userId, string token)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<UserLogin>(
            SelectByTokenSql, new { pUserId = userId, pLoginToken = token });
    }

    /// <summary>
    /// Retrieves a single session by its identifier.
    /// </summary>
    /// <param name="loginId">The session identifier.</param>
    /// <returns>The session, or <c>null</c> when the key is unknown.</returns>
    public override UserLogin? GetSingle(long loginId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<UserLogin>(SelectByIdSql, new { LoginId = loginId });
    }

    /// <summary>
    /// Records a session and returns its generated identifier.
    /// </summary>
    /// <param name="entity">The session to record.</param>
    /// <returns>The generated <c>loginid</c>.</returns>
    public override long InsertToGetId(UserLogin entity)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Records a session.
    /// </summary>
    /// <param name="entity">The session to record.</param>
    public override void Insert(UserLogin entity)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Updates a recorded session.
    /// </summary>
    /// <param name="entityToUpdate">The session carrying the new values.</param>
    public override void Update(UserLogin entityToUpdate)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(entityToUpdate));
    }

    /// <summary>
    /// Retrieves a page of sessions.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<UserLogin> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserLogin>(SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }).ToList();
    }

    // =================================================================================================
    // Parameter builders — shared by both twins so the bound columns cannot drift.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter set for the two insert statements.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> All four date columns are declared <c>TIMESTAMP</c> without time
    /// zone (<c>006-FixUserLoginTable.sql</c>), so each value is re-stamped as
    /// <c>Unspecified</c> before binding. Npgsql picks the wire type from the value's
    /// <see cref="DateTimeKind"/>, and a <c>Utc</c> value is sent as <c>timestamptz</c>; that is
    /// harmless on plain parameterised SQL, where PostgreSQL casts to the column type, but it is the
    /// exact mechanism behind the <c>42883</c> failure on the stored-function paths (REQ-NFR-026).
    /// Normalising here keeps the two write paths honest whichever statement they use.</para>
    /// </remarks>
    /// <param name="entity">The session being written.</param>
    /// <returns>The anonymous parameter object Dapper binds.</returns>
    private static object BuildWriteParameters(UserLogin entity)
    {
        return new
        {
            entity.UserId,
            LoginDate = DbTimestamp.AsTimestamp(entity.LoginDate),
            entity.LoginToken,
            entity.TokenStatus,
            ExipryDate = DbTimestamp.AsTimestamp(entity.ExipryDate),
            IssueDate = DbTimestamp.AsTimestamp(entity.IssueDate)
        };
    }

    /// <summary>
    /// Builds the parameter set for the update statement.
    /// </summary>
    /// <param name="entity">The session being updated.</param>
    /// <returns>The anonymous parameter object Dapper binds.</returns>
    private static object BuildUpdateParameters(UserLogin entity)
    {
        return new
        {
            entity.LoginId,
            LoginDate = DbTimestamp.AsTimestamp(entity.LoginDate),
            entity.LoginToken,
            entity.TokenStatus,
            ExipryDate = DbTimestamp.AsTimestamp(entity.ExipryDate),
            IssueDate = DbTimestamp.AsTimestamp(entity.IssueDate)
        };
    }
}
