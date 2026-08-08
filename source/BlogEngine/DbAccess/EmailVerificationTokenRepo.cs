namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for the persisted double opt-in verification tokens.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Durable storage for the single-use, 24-hour tokens that confirm an
/// anonymous visitor's email address. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> A token is inserted when a submission is queued, and redeemed by
/// <see cref="ConsumeAsync"/>, which delegates to the <c>ConsumeEmailVerificationToken</c>
/// stored function so the check-and-flip happens in one statement.</para>
///
/// <para><b>Dependencies:</b> <see cref="GenericRepository{TEntity}"/>, Dapper, and the
/// <c>EmailVerificationToken</c> table plus its stored function from migration script 014.</para>
///
/// <para><b>Usage:</b> This is deliberately a DATABASE repository, unlike the legacy
/// <c>PasswordResetTokenRepo</c> whose <c>ConcurrentDictionary</c> loses every outstanding
/// token on restart. A verification link mailed on Friday still has to work on Saturday.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> every member has an <c>…Async</c> twin carrying a
/// <see cref="CancellationToken"/>, and every one of them opens its connection asynchronously through
/// the protected helpers on <see cref="GenericRepository{TEntity}"/> — the previous async members
/// still called the blocking <c>GetOpenConnection</c>, which parks a thread-pool thread for the whole
/// TCP, TLS and authentication handshake and defeats the point of being async at all.</para>
///
/// <para><b>Timestamps:</b> every <see cref="DateTime"/> bound here goes through
/// <see cref="DbTimestamp.AsTimestamp(DateTime)"/>. Npgsql picks the wire type from the value's
/// <see cref="DateTimeKind"/>, so a <c>DateTime.UtcNow</c> is sent as <c>timestamptz</c> — which
/// matches none of the <c>TIMESTAMP</c> columns this schema declares.</para>
/// </remarks>
public class EmailVerificationTokenRepo : GenericRepository<EmailVerificationToken>, IEmailVerificationTokenRepo
{
    /// <summary>
    /// The column list shared by every token read.
    /// </summary>
    private const string TokenColumns =
        "TokenId, Token, Email, Purpose, TargetId, DisplayName, IssuedOn, ExpiresOn, " +
        "ConsumedOn, IsUsed, RequestIpAddress";

    private const string SelectByTokenSql =
        "SELECT " + TokenColumns + " FROM EmailVerificationToken WHERE Token = @Token";

    private const string SelectAllSql =
        "SELECT " + TokenColumns + " FROM EmailVerificationToken ORDER BY IssuedOn DESC";

    private const string SelectByTargetSql =
        "SELECT " + TokenColumns + @" FROM EmailVerificationToken
           WHERE TargetId = @TargetId
           ORDER BY IssuedOn DESC";

    private const string SelectPagedSql =
        "SELECT " + TokenColumns + @" FROM EmailVerificationToken
           ORDER BY IssuedOn DESC
           LIMIT @PageSize OFFSET @OffSet";

    private const string SelectByIdSql =
        "SELECT " + TokenColumns + " FROM EmailVerificationToken WHERE TokenId = @TokenId";

    private const string ConsumeSql = "SELECT * FROM ConsumeEmailVerificationToken(@pToken)";

    private const string DeleteExpiredSql = "DELETE FROM EmailVerificationToken WHERE ExpiresOn < @Now";

    private const string CountRecentByEmailSql = @"
            SELECT COUNT(*) FROM EmailVerificationToken
             WHERE LOWER(Email) = LOWER(@Email) AND IssuedOn >= @Since";

    /// <summary>
    /// The parameterised INSERT shared by the sync and async insert paths.
    /// </summary>
    private const string InsertSql =
        @"INSERT INTO EmailVerificationToken
             (Token, Email, Purpose, TargetId, DisplayName, IssuedOn, ExpiresOn,
              IsUsed, RequestIpAddress)
          VALUES
             (@Token, @Email, @Purpose, @TargetId, @DisplayName, @IssuedOn, @ExpiresOn,
              FALSE, @RequestIpAddress)
          RETURNING TokenId";

    private const string UpdateSql = @"
            UPDATE EmailVerificationToken
               SET IsUsed = @IsUsed,
                   ConsumedOn = @ConsumedOn
             WHERE TokenId = @TokenId";

    /// <summary>
    /// Initializes a new instance of the <see cref="EmailVerificationTokenRepo"/> class.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public EmailVerificationTokenRepo(string connectionString) : base(connectionString)
    {
    }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Looks a token up by its secret without redeeming it, and without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A read-only inspection of <c>EmailVerificationToken</c> — it does
    /// <b>not</b> check expiry and does <b>not</b> mark the token used. Use it to explain a link to a
    /// visitor ("this link has already been used"); use <see cref="ConsumeAsync"/> to act on one, as
    /// only that path is atomic. A blank secret short circuits to <c>null</c> without a round trip,
    /// so an empty query string cannot turn into a table scan.</para>
    /// <para><b>Projection:</b> the full <c>TokenColumns</c> set — <c>TokenId, Token, Email, Purpose,
    /// TargetId, DisplayName, IssuedOn, ExpiresOn, ConsumedOn, IsUsed, RequestIpAddress</c> — so the
    /// caller can distinguish expired from already-consumed.</para>
    /// <para><b>Flow:</b> guard the blank secret → trim → helper opens the connection asynchronously →
    /// first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="token">The token secret from the verification link.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The token row, or <c>null</c> when the secret is blank or unknown.</returns>
    public async Task<EmailVerificationToken?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("Token", token.Trim());
        return await QueryFirstOrDefaultAsync<EmailVerificationToken>(
            SelectByTokenSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues a token and returns its generated id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Inline <c>INSERT … RETURNING TokenId</c> into
    /// <c>EmailVerificationToken</c>. <c>IsUsed</c> is written as a literal <c>FALSE</c> rather than
    /// taken from the entity, and <c>ConsumedOn</c> is not in the column list at all — a token cannot
    /// be born spent, whatever the caller passes. <c>ExpiresOn</c> comes from the entity, so the
    /// 24-hour window is the service's policy, not this repository's.</para>
    /// <para><b>Timestamps:</b> <c>IssuedOn</c> and <c>ExpiresOn</c> are bound through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/>; both columns are <c>TIMESTAMP</c> without time
    /// zone and a <c>Utc</c>-kinded value would otherwise go on the wire as <c>timestamptz</c>.</para>
    /// <para><b>Flow:</b> build parameters → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read the key.</para>
    /// <para><b>Side Effects:</b> Adds one unspent row to <c>EmailVerificationToken</c>.</para>
    /// </remarks>
    /// <param name="token">The token to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>TokenId</c>.</returns>
    public async Task<long> InsertTokenAsync(EmailVerificationToken token, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertSql, BuildInsertParameters(token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Redeems a token once and atomically, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The only correct way to act on a verification link. It calls the
    /// stored function <c>SELECT * FROM ConsumeEmailVerificationToken(@pToken)</c> (migration script
    /// 014) rather than reading, validating and updating from here, because those three steps done
    /// separately leave a window in which a link clicked twice — a mail client prefetching it, say —
    /// verifies twice. The function checks the secret, the expiry and the unused flag, flips
    /// <c>IsUsed</c> and stamps <c>ConsumedOn</c>, all in one statement, and returns the row only if
    /// it actually consumed it.</para>
    /// <para><b>Null semantics:</b> <c>null</c> covers every failure alike — unknown secret, expired,
    /// or already used. That is deliberate: the caller must not be able to tell an attacker which.</para>
    /// <para><b>Flow:</b> guard the blank secret → trim → helper opens the connection asynchronously →
    /// call the function → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> Marks the token used and stamps <c>ConsumedOn</c> inside the
    /// function. Not idempotent by design — a second call returns <c>null</c>.</para>
    /// </remarks>
    /// <param name="token">The token secret from the verification link.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The consumed token, carrying the <c>Email</c>, <c>Purpose</c> and <c>TargetId</c> the caller
    /// must act on; <c>null</c> when the secret was blank, unknown, expired or already spent.
    /// </returns>
    public async Task<EmailVerificationToken?> ConsumeAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("pToken", token.Trim());
        return await QueryFirstOrDefaultAsync<EmailVerificationToken>(
            ConsumeSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Purges tokens whose window has closed, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Housekeeping. <c>DELETE FROM EmailVerificationToken WHERE
    /// ExpiresOn &lt; @Now</c> — the WHERE clause is on expiry alone, so a <b>consumed but still
    /// in-window</b> token survives and a live unspent one is never removed. The cut-off is computed
    /// here from <c>DateTime.UtcNow</c> rather than taken from the caller, so no caller can widen it.</para>
    /// <para><b>Timestamps:</b> the cut-off is bound through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> — <c>DateTime.UtcNow</c> carries
    /// <c>Kind = Utc</c>, which Npgsql would send as <c>timestamptz</c> against a <c>TIMESTAMP</c>
    /// column.</para>
    /// <para><b>Flow:</b> stamp now → helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Permanently removes expired rows. Safe to run repeatedly; a second
    /// run deletes nothing.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The number of rows removed; <c>0</c> when nothing had expired.</returns>
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Now", DbTimestamp.AsTimestamp(DateTime.UtcNow));
        return await ExecuteAsync(DeleteExpiredSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts how many tokens one address was recently issued, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The rate limit that stops this feature being used as a mail
    /// cannon: every issued token means an email was sent to the address, so an attacker who can
    /// request tokens freely can flood a third party's inbox. Matching is
    /// <c>LOWER(Email) = LOWER(@Email)</c> so case cannot reset the budget, and the address is
    /// trimmed first for the same reason. A blank address yields <c>0</c> without a round trip.</para>
    /// <para><b>Timestamps:</b> <paramref name="since"/> is bound through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/>; <c>IssuedOn</c> is <c>TIMESTAMP</c> without
    /// time zone.</para>
    /// <para><b>Flow:</b> guard the blank address → trim and normalise → helper opens the connection
    /// asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">The address being rate-limited; blank or whitespace yields <c>0</c>.</param>
    /// <param name="since">Start of the window, inclusive.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of tokens issued to that address at or after <paramref name="since"/>.</returns>
    public async Task<int> CountRecentByEmailAsync(string email, DateTime since, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return 0;

        var parameters = new DynamicParameters();
        parameters.Add("Email", email.Trim());
        parameters.Add("Since", DbTimestamp.AsTimestamp(since));
        return await ExecuteScalarAsync<int>(
            CountRecentByEmailSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every token, newest first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Administrative view; used and expired rows are included, because
    /// the operator auditing the flow needs to see the ones that failed.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All token rows.</returns>
    public override async Task<IEnumerable<EmailVerificationToken>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<EmailVerificationToken>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every token issued for one pending row, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic "by id" contract is interpreted as "by TargetId",
    /// which is the only meaningful parent key a token has.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="targetId">The pending comment, rating or subscriber id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Tokens issued for that row, newest first.</returns>
    public override async Task<IEnumerable<EmailVerificationToken>> GetAllByIdAsync(long targetId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("TargetId", targetId);
        return await QueryAsync<EmailVerificationToken>(
            SelectByTargetSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of tokens, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a busy site's token history never
    /// crosses the wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A page of tokens, newest first.</returns>
    public override async Task<IEnumerable<EmailVerificationToken>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return await QueryAsync<EmailVerificationToken>(
            SelectPagedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single token row by primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tokenId">The token id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The token row, or <c>null</c>.</returns>
    public override async Task<EmailVerificationToken?> GetSingleAsync(long tokenId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("TokenId", tokenId);
        return await QueryFirstOrDefaultAsync<EmailVerificationToken>(
            SelectByIdSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single token row by its 32-bit primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tokenId">The token id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The token row, or <c>null</c>.</returns>
    public override Task<EmailVerificationToken?> GetIntSingleAsync(int tokenId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(tokenId, cancellationToken);
    }

    /// <summary>
    /// Inserts a token row, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The INSERT returns the key whether or not the caller wants it, so
    /// this member is written in terms of the key-returning one rather than duplicating the SQL —
    /// half a converted insert pair is the easiest way to ship a blocking write path that looks
    /// converted.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/> and discard the key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>EmailVerificationToken</c>.</para>
    /// </remarks>
    /// <param name="token">The token to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(EmailVerificationToken token, CancellationToken cancellationToken = default)
    {
        await InsertToGetIdAsync(token, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a token row and returns its generated id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The verification flow needs the key back so the mailed link can
    /// be traced to the row it protects.</para>
    /// <para><b>Flow:</b> normalise the timestamps → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>EmailVerificationToken</c>.</para>
    /// </remarks>
    /// <param name="token">The token to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated token id.</returns>
    public override async Task<long> InsertToGetIdAsync(EmailVerificationToken token, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertSql, BuildInsertParameters(token), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the mutable state of a token row, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the consumption state is mutable. Prefer
    /// <see cref="ConsumeAsync"/>, which does the same job atomically — a read-then-write pair lets
    /// two concurrent clicks both succeed.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>TokenId</c>.</para>
    /// </remarks>
    /// <param name="token">The token carrying the new state.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(EmailVerificationToken token, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(token), cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every token, newest first.
    /// </summary>
    /// <returns>All token rows.</returns>
    public override IEnumerable<EmailVerificationToken> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<EmailVerificationToken>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets every token issued for one pending row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic "by id" contract is interpreted as
    /// "by TargetId", which is the only meaningful parent key a token has.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="targetId">The pending comment, rating or subscriber id.</param>
    /// <returns>Tokens issued for that row, newest first.</returns>
    public override IEnumerable<EmailVerificationToken> GetAllById(long targetId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("TargetId", targetId);
        return connection.Query<EmailVerificationToken>(SelectByTargetSql, parameters).ToList();
    }

    /// <summary>
    /// Gets a page of tokens.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>A page of tokens, newest first.</returns>
    public override IEnumerable<EmailVerificationToken> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return connection.Query<EmailVerificationToken>(SelectPagedSql, parameters).ToList();
    }

    /// <summary>
    /// Gets a single token row by primary key.
    /// </summary>
    /// <param name="tokenId">The token id.</param>
    /// <returns>The token row, or null.</returns>
    public override EmailVerificationToken? GetSingle(long tokenId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("TokenId", tokenId);
        return connection.QueryFirstOrDefault<EmailVerificationToken>(SelectByIdSql, parameters);
    }

    /// <summary>
    /// Gets a single token row by its 32-bit primary key.
    /// </summary>
    /// <param name="tokenId">The token id.</param>
    /// <returns>The token row, or null.</returns>
    public override EmailVerificationToken? GetIntSingle(int tokenId)
    {
        return GetSingle(tokenId);
    }

    /// <summary>
    /// Inserts a token row.
    /// </summary>
    /// <param name="token">The token to insert.</param>
    public override void Insert(EmailVerificationToken token)
    {
        using var connection = GetOpenConnection();
        connection.ExecuteScalar<long>(InsertSql, BuildInsertParameters(token));
    }

    /// <summary>
    /// Inserts a token row and returns its generated id.
    /// </summary>
    /// <param name="token">The token to insert.</param>
    /// <returns>The generated token id.</returns>
    public override long InsertToGetId(EmailVerificationToken token)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertSql, BuildInsertParameters(token));
    }

    /// <summary>
    /// Updates the mutable state of a token row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the consumption state is mutable. Prefer
    /// <see cref="ConsumeAsync"/>, which does the same job atomically.</para>
    /// <para><b>Side Effects:</b> Updates one row.</para>
    /// </remarks>
    /// <param name="token">The token carrying the new state.</param>
    public override void Update(EmailVerificationToken token)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(token));
    }

    /// <summary>
    /// Builds the parameter set for a token insert.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unset <c>IssuedOn</c> defaults to now, so a caller that only
    /// fills in the token and the expiry still writes a usable row. Both timestamps are normalised to
    /// <see cref="DateTimeKind.Unspecified"/> so Npgsql sends <c>timestamp</c> rather than
    /// <c>timestamptz</c>, which is what the columns declare.</para>
    /// <para><b>Flow:</b> default the issue stamp → normalise both stamps → bind.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="token">The token being inserted.</param>
    /// <returns>Populated Dapper parameters.</returns>
    private static DynamicParameters BuildInsertParameters(EmailVerificationToken token)
    {
        var issuedOn = token.IssuedOn == default ? DateTime.UtcNow : token.IssuedOn;

        var parameters = new DynamicParameters();
        parameters.Add("Token", token.Token);
        parameters.Add("Email", token.Email);
        parameters.Add("Purpose", token.Purpose);
        parameters.Add("TargetId", token.TargetId);
        parameters.Add("DisplayName", token.DisplayName);
        parameters.Add("IssuedOn", DbTimestamp.AsTimestamp(issuedOn));
        parameters.Add("ExpiresOn", DbTimestamp.AsTimestamp(token.ExpiresOn));
        parameters.Add("RequestIpAddress", token.RequestIpAddress);
        return parameters;
    }

    /// <summary>
    /// Builds the parameter set shared by both update paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>ConsumedOn</c> is normalised for the same reason as the insert
    /// stamps; a <c>null</c> stays <c>null</c> so an un-consumed token is written as SQL NULL rather
    /// than the zero date.</para>
    /// <para><b>Flow:</b> normalise the timestamp → bind the mutable columns and the key.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="token">The token being written.</param>
    /// <returns>Populated Dapper parameters.</returns>
    private static DynamicParameters BuildUpdateParameters(EmailVerificationToken token)
    {
        var parameters = new DynamicParameters();
        parameters.Add("IsUsed", token.IsUsed);
        parameters.Add("ConsumedOn", DbTimestamp.AsTimestamp(token.ConsumedOn));
        parameters.Add("TokenId", token.TokenId);
        return parameters;
    }
}
