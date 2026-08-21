namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing LoginLog data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Persists and reads the sign-in audit trail — who signed in, from which
/// address, and when.</para>
///
/// <para><b>Code Flow:</b> a caller resolves <c>ILoginLogRepo</c>, calls an <c>…Async</c> member,
/// and the member routes through the protected helpers on <c>GenericRepository</c>, which open the
/// connection asynchronously and flow the cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL, the <c>loginlog</c> table.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage.</para>
///
/// <para><b>REQ-FN-051 — repaired.</b> Both INSERT statements used to hard-code
/// <c>success = true</c> and <c>attemptedemail = ''</c>, which made the audit trail structurally
/// incapable of recording a failed sign-in — the single event an abuse investigation most needs —
/// and left a recorded attempt unattributable because the address column was always blank. The
/// statements now bind every column from the entity, the <see cref="LoginLog"/> model exposes
/// <c>Success</c>, <c>AttemptedEmail</c> and <c>UserAgent</c>, and <c>AuthSvc</c> writes one row per
/// attempt, so a run of failures against one address is visible in the log.</para>
///
/// <para><b>Unknown accounts.</b> A failed attempt often names an address that matches no account,
/// so <c>userid</c> is a nullable foreign key and <see cref="BuildWriteParameters"/> maps a missing
/// or non-positive <c>LoginUserId</c> to SQL <c>NULL</c>. Writing <c>0</c> instead would violate the
/// foreign key and lose the very rows the trail exists to keep.</para>
///
/// <para><b>The password is never a parameter of this path.</b> Look at the six columns bound by
/// <see cref="BuildWriteParameters"/> — user id, attempted address, outcome, client address, user
/// agent and timestamp. There is no password column, no password parameter, and no overload that
/// accepts one, so a submitted secret cannot reach the audit trail even by accident. That matters
/// more here than anywhere else in the repository layer: an audit table is long-lived, widely read
/// and frequently exported, and it is written on exactly the failed attempts where the value typed
/// into the password box is most likely to be a real password mistyped into the wrong field. The
/// outcome is recorded as a <c>bool</c>; <b>why</b> an attempt failed is deliberately not stored,
/// because "wrong password" versus "no such account" is precisely the distinction an attacker wants
/// and an investigator can infer from the pattern instead.</para>
///
/// <para><b>Column widths.</b> Three of those values are attacker-controlled and unbounded on the
/// wire, so <see cref="BuildWriteParameters"/> clips each to its column's declared width before
/// binding: <c>attemptedemail</c> to 255, <c>ipaddress</c> to 100 and <c>useragent</c> to 500. An
/// over-long user agent must cost its own tail, not the whole row — a <c>22001</c> string-data
/// overflow would abort the INSERT and erase the record of the attempt, which hands an attacker a
/// way to go unlogged.</para>
/// </remarks>
public class LoginLogRepo : GenericRepository<LoginLog>, ILoginLogRepo
{
    /// <summary>Width of the <c>attemptedemail</c> column.</summary>
    private const int AttemptedEmailMaxLength = 255;

    /// <summary>Width of the <c>ipaddress</c> column.</summary>
    private const int ClientIpMaxLength = 100;

    /// <summary>Width of the <c>useragent</c> column.</summary>
    private const int UserAgentMaxLength = 500;

    private const string SelectColumns = @"
            SELECT logid AS LoginLogId, userid AS LoginUserId, attemptedon AS LoginDateTime,
                   COALESCE(ipaddress, '') AS ClientIP,
                   COALESCE(attemptedemail, '') AS AttemptedEmail,
                   success AS Success,
                   COALESCE(useragent, '') AS UserAgent
            FROM loginlog";

    private const string SelectAllSql = SelectColumns + " ORDER BY attemptedon DESC";

    private const string SelectByIdSql = SelectColumns + " WHERE logid = @LogId";

    private const string SelectByUserSql =
        SelectColumns + " WHERE userid = @UserId ORDER BY attemptedon DESC";

    private const string SelectPagedSql =
        SelectColumns + " ORDER BY attemptedon DESC LIMIT @PageSize OFFSET @OffSet";

    private const string SelectByAttemptedEmailSql =
        SelectColumns + @" WHERE LOWER(attemptedemail) = LOWER(@AttemptedEmail)
            ORDER BY attemptedon DESC LIMIT @MaxRows";

    // REQ-FN-051: every column is bound from the entity. Nothing here is hard-coded, so a refused
    // attempt records success = false and the address that was tried.
    private const string InsertSql = @"
            INSERT INTO loginlog (userid, attemptedemail, success, ipaddress, useragent, attemptedon)
            VALUES (@LoginUserId, @AttemptedEmail, @Success, @ClientIP, @UserAgent, @LoginDateTime)";

    private const string InsertReturningIdSql = InsertSql + " RETURNING logid";

    private const string UpdateSql = @"
            UPDATE loginlog SET
                userid = @LoginUserId,
                attemptedemail = @AttemptedEmail,
                success = @Success,
                ipaddress = @ClientIP,
                useragent = @UserAgent,
                attemptedon = @LoginDateTime
            WHERE logid = @LoginLogId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public LoginLogRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every audit row, newest attempt first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Newest-first is the order an investigation reads in, so it is
    /// applied in SQL rather than left to the caller.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All audit rows, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<LoginLog>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<LoginLog>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets one audit row by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The audit row's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The audit row, or <c>null</c> when no row carries that key.</returns>
    public override async Task<LoginLog?> GetSingleAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<LoginLog>(
            SelectByIdSql, new { LogId = singleId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets one audit row by INT identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The audit row's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The audit row, or <c>null</c> when no row carries that key.</returns>
    public override Task<LoginLog?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Gets every audit row belonging to one user, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The user is the parent key for this entity, so the generic
    /// "all by id" lookup and the named user lookup are the same query.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetUserLoginLogsAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The user identifier to filter on.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's audit rows, newest first.</returns>
    public override Task<IEnumerable<LoginLog>> GetAllByIdAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return GetUserLoginLogsAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Gets every audit row belonging to one user, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Filtering happens in SQL so a busy account's history never
    /// crosses the wire in full only to be discarded in memory.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="appUserId">The user identifier to filter on.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's audit rows, newest first, or an empty sequence when there are none.</returns>
    public async Task<IEnumerable<LoginLog>> GetUserLoginLogsAsync(long appUserId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<LoginLog>(
            SelectByUserSql, new { UserId = appUserId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the most recent attempts made against one address, newest first (REQ-FN-051).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A brute-force run is a burst of attempts against one address,
    /// most of them failures, and the address — not the user id — is the only key that spans the
    /// whole run, because an attempt against an unknown account has no user id at all. The match is
    /// case-insensitive because the address the attacker types is not normalised for them.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered, capped query →
    /// materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="attemptedEmail">The address that was typed into the sign-in form.</param>
    /// <param name="maxRows">Upper bound on the rows returned; an audit table only grows.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching attempts, newest first, or an empty sequence when there are none.</returns>
    public async Task<IEnumerable<LoginLog>> GetRecentByAttemptedEmailAsync(
        string attemptedEmail, int maxRows = 50, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attemptedEmail))
            return [];

        return await QueryAsync<LoginLog>(
            SelectByAttemptedEmailSql,
            new { AttemptedEmail = attemptedEmail.Trim(), MaxRows = maxRows },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of audit rows, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL; an audit table only grows, so reading
    /// it whole is never acceptable.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<LoginLog>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<LoginLog>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records one sign-in attempt, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key, so the plain INSERT
    /// is used rather than the RETURNING form. See the type remarks for REQ-FN-051: this statement
    /// records every attempt as a success, which is a defect tracked separately.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>loginlog</c>.</para>
    /// </remarks>
    /// <param name="entity">The attempt to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(LoginLog entity, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildWriteParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records one sign-in attempt and returns its generated identifier, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key. Shares <see cref="InsertSql"/> with
    /// <see cref="InsertAsync"/>, so the two can never write different columns.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>loginlog</c>.</para>
    /// </remarks>
    /// <param name="entity">The attempt to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>logid</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(LoginLog entity, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildWriteParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates one audit row, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Updating an audit row is unusual but supported for correction;
    /// the key itself is never rewritten.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>logid</c>.</para>
    /// </remarks>
    /// <param name="entityToUpdate">The audit row carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(LoginLog entityToUpdate, CancellationToken cancellationToken = default)
    {
        var parameters = BuildWriteParameters(entityToUpdate);
        parameters.Add("LoginLogId", entityToUpdate.LoginLogId);
        await ExecuteAsync(UpdateSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a sign-out time.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The <c>loginlog</c> table has no sign-out column in the current
    /// schema, so there is nothing to write and the call reports success. Kept so a caller written
    /// against the older schema still compiles.</para>
    /// <para><b>Flow:</b> no I/O.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="appUserId">The user signing out.</param>
    /// <param name="logOutTime">When the sign-out happened.</param>
    /// <param name="cancellationToken">Unused; present for contract symmetry.</param>
    /// <returns><c>true</c> always.</returns>
    public Task<bool> UpdateLogOutAsync(long appUserId, DateTime logOutTime, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(UpdateLogOut(appUserId, logOutTime));
    }

    /// <summary>
    /// Binds the columns shared by the insert and update statements.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>attemptedon</c> is a <c>TIMESTAMP</c> column and the model
    /// supplies <c>DateTime.UtcNow</c>, whose <c>Kind</c> is <c>Utc</c>. Npgsql infers the wire type
    /// from the Kind, so an unnormalised value is sent as <c>timestamptz</c> and PostgreSQL then
    /// converts it into the session time zone on the way into the column — recording the wrong
    /// instant on any host whose session zone is not UTC, silently and without a build or test
    /// failure. <c>DbTimestamp.AsTimestamp</c> drops the Kind without moving the instant.</para>
    /// <para><b>Flow:</b> copy the writable fields, normalising the timestamp.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="entity">The audit row being written.</param>
    /// <returns>Parameters for the write statement.</returns>
    private static DynamicParameters BuildWriteParameters(LoginLog entity)
    {
        var parameters = new DynamicParameters();
        parameters.Add("LoginUserId", entity.LoginUserId is > 0 ? entity.LoginUserId : null);
        parameters.Add("AttemptedEmail", Truncate(entity.AttemptedEmail, AttemptedEmailMaxLength));
        parameters.Add("Success", entity.Success);
        parameters.Add("ClientIP", Truncate(entity.ClientIP, ClientIpMaxLength));
        parameters.Add("UserAgent", Truncate(entity.UserAgent, UserAgentMaxLength));
        parameters.Add("LoginDateTime", DbTimestamp.AsTimestamp(entity.LoginDateTime));
        return parameters;
    }

    /// <summary>
    /// Clips a value to the width of its column.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The audit columns are bounded <c>VARCHAR</c>s and the values come
    /// straight off the wire, where an attacker chooses their length. An over-long user agent must
    /// truncate, not throw — losing the whole audit row to a <c>22001</c> is exactly the outcome an
    /// abuse investigation cannot afford.</para>
    /// <para><b>Flow:</b> null guard → length check → substring.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="value">The value to clip.</param>
    /// <param name="maxLength">The column's width in characters.</param>
    /// <returns>The value, never longer than <paramref name="maxLength"/> and never <c>null</c>.</returns>
    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every audit row, newest attempt first.
    /// </summary>
    /// <returns>All audit rows.</returns>
    public override IEnumerable<LoginLog> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<LoginLog>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets one audit row by INT identifier.
    /// </summary>
    /// <param name="singleId">The audit row's identifier.</param>
    /// <returns>The audit row, or <c>null</c> when not found.</returns>
    public override LoginLog? GetIntSingle(int singleId)
    {
        return GetSingle(singleId);
    }

    /// <summary>
    /// Gets one audit row by its identifier.
    /// </summary>
    /// <param name="singleId">The audit row's identifier.</param>
    /// <returns>The audit row, or <c>null</c> when not found.</returns>
    public override LoginLog? GetSingle(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<LoginLog>(SelectByIdSql, new { LogId = singleId });
    }

    /// <summary>
    /// Gets every audit row belonging to one user.
    /// </summary>
    /// <param name="appUserId">The user identifier to filter on.</param>
    /// <returns>The user's audit rows, newest first.</returns>
    public IEnumerable<LoginLog> GetUserLoginLogs(long appUserId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<LoginLog>(SelectByUserSql, new { UserId = appUserId }).ToList();
    }

    /// <summary>
    /// Gets every audit row belonging to one user.
    /// </summary>
    /// <param name="singleId">The user identifier to filter on.</param>
    /// <returns>The user's audit rows, newest first.</returns>
    public override IEnumerable<LoginLog> GetAllById(long singleId)
    {
        return GetUserLoginLogs(singleId);
    }

    /// <summary>
    /// Gets a page of audit rows.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<LoginLog> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection.Query<LoginLog>(SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }).ToList();
    }

    /// <summary>
    /// Records one sign-in attempt.
    /// </summary>
    /// <param name="entity">The attempt to record.</param>
    public override void Insert(LoginLog entity)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Records one sign-in attempt and returns its generated identifier.
    /// </summary>
    /// <param name="entity">The attempt to record.</param>
    /// <returns>The generated <c>logid</c>.</returns>
    public override long InsertToGetId(LoginLog entity)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Updates one audit row.
    /// </summary>
    /// <param name="entityToUpdate">The audit row carrying the new values.</param>
    public override void Update(LoginLog entityToUpdate)
    {
        var parameters = BuildWriteParameters(entityToUpdate);
        parameters.Add("LoginLogId", entityToUpdate.LoginLogId);
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, parameters);
    }

    /// <summary>
    /// Records a sign-out time.
    /// </summary>
    /// <remarks>
    /// The current schema has no sign-out column, so this is a no-op kept for source compatibility.
    /// </remarks>
    /// <param name="appUserId">The user signing out.</param>
    /// <param name="logOutTime">When the sign-out happened.</param>
    /// <returns><c>true</c> always.</returns>
    public bool UpdateLogOut(long appUserId, DateTime logOutTime)
    {
        return true;
    }
}
