namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for the registry of confirmed email addresses.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Remembers which addresses have completed double opt-in, so a
/// returning reader is never asked to confirm twice. [REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> <c>EmailVerificationSvc</c> reads through
/// <see cref="IsVerifiedAsync"/> before queuing a submission and writes through
/// <see cref="RecordVerifiedAsync"/> once a token has been consumed.</para>
///
/// <para><b>Dependencies:</b> <see cref="GenericRepository{TEntity}"/>, Dapper, and the
/// <c>VerifiedEmail</c> table plus the <c>RecordVerifiedEmail</c> stored function from
/// migration script 014.</para>
///
/// <para><b>Usage:</b> Every lookup is case-insensitive, matching the unique index on
/// <c>LOWER(Email)</c>.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> every member has an <c>…Async</c> twin carrying a
/// <see cref="CancellationToken"/>, and every one of them opens its connection asynchronously through
/// the protected helpers on <see cref="GenericRepository{TEntity}"/>. The members that were already
/// async took their token on the existing signature rather than on a new overload, because a
/// <c>FooAsync(x, ct = default)</c> beside a <c>FooAsync(x)</c> makes every existing call ambiguous.
/// The synchronous twins execute the same SQL constants and are deleted in the final stage.</para>
/// </remarks>
public class VerifiedEmailRepo : GenericRepository<VerifiedEmail>, IVerifiedEmailRepo
{
    /// <summary>
    /// The column list shared by every registry read.
    /// </summary>
    private const string VerifiedEmailColumns =
        "VerifiedEmailId, Email, DisplayName, VerifiedOn, LastUsedOn, IsBlocked";

    private const string SelectByEmailSql =
        "SELECT " + VerifiedEmailColumns + " FROM VerifiedEmail WHERE LOWER(Email) = LOWER(@Email)";

    private const string SelectAllSql =
        "SELECT " + VerifiedEmailColumns + " FROM VerifiedEmail ORDER BY VerifiedOn DESC";

    private const string SelectByIdSql =
        "SELECT " + VerifiedEmailColumns + " FROM VerifiedEmail WHERE VerifiedEmailId = @VerifiedEmailId";

    private const string SelectPagedSql =
        "SELECT " + VerifiedEmailColumns + @" FROM VerifiedEmail
           ORDER BY VerifiedOn DESC
           LIMIT @PageSize OFFSET @OffSet";

    private const string RecordVerifiedSql = "SELECT RecordVerifiedEmail(@pEmail, @pDisplayName)";

    private const string SetBlockedSql =
        "UPDATE VerifiedEmail SET IsBlocked = @IsBlocked WHERE LOWER(Email) = LOWER(@Email)";

    private const string UpdateSql = @"
            UPDATE VerifiedEmail
               SET DisplayName = @DisplayName,
                   LastUsedOn = @LastUsedOn,
                   IsBlocked = @IsBlocked
             WHERE VerifiedEmailId = @VerifiedEmailId";

    /// <summary>
    /// Initializes a new instance of the <see cref="VerifiedEmailRepo"/> class.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public VerifiedEmailRepo(string connectionString) : base(connectionString)
    {
    }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets a registry entry by address, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Inline SQL over <c>VerifiedEmail</c> matching
    /// <c>LOWER(Email) = LOWER(@Email)</c> — the same expression the table's unique index is built on,
    /// so the lookup is index-backed rather than a scan, and it can never find a row the registry
    /// would treat as a duplicate. A blank address short circuits to <c>null</c> without a round trip.</para>
    /// <para><b>Projection:</b> the full <c>VerifiedEmailColumns</c> set — <c>VerifiedEmailId, Email,
    /// DisplayName, VerifiedOn, LastUsedOn, IsBlocked</c>. <c>IsBlocked</c> is included precisely so
    /// the caller can distinguish "confirmed" from "confirmed and banned"; see
    /// <see cref="IsVerifiedAsync"/>, which collapses the two.</para>
    /// <para><b>Flow:</b> guard the blank address → trim → helper opens the connection asynchronously
    /// → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">The address to look up; compared case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The registry entry, or <c>null</c> when the address has never been confirmed.</returns>
    public async Task<VerifiedEmail?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var parameters = new DynamicParameters();
        parameters.Add("Email", email.Trim());
        return await QueryFirstOrDefaultAsync<VerifiedEmail>(
            SelectByEmailSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decides whether an address may skip confirmation, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The gate <c>EmailVerificationSvc</c> consults before deciding to
    /// mail a token at all. "Verified" here means <b>present and not blocked</b> — a banned address
    /// answers <c>false</c> even though it is in the registry, so blocking an abuser demotes them all
    /// the way back to needing confirmation rather than merely revoking a badge. Because the two
    /// failure modes collapse to the same <c>false</c>, a caller that needs to tell "never confirmed"
    /// from "banned" must call <see cref="GetByEmailAsync"/> instead.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetByEmailAsync"/> → treat <c>null</c> or
    /// <c>IsBlocked</c> as not verified. One round trip, not two.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">The address to test; blank or whitespace yields <c>false</c>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when the address is confirmed and not blocked.</returns>
    public async Task<bool> IsVerifiedAsync(string email, CancellationToken cancellationToken = default)
    {
        var existing = await GetByEmailAsync(email, cancellationToken).ConfigureAwait(false);
        return existing != null && !existing.IsBlocked;
    }

    /// <summary>
    /// Records that an address completed confirmation, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Calls the stored function
    /// <c>SELECT RecordVerifiedEmail(@pEmail, @pDisplayName)</c> (migration script 014), which is an
    /// upsert on <c>LOWER(Email)</c>: a first-time address is inserted, a returning one has its
    /// display name and <c>LastUsedOn</c> refreshed. Doing that in the function rather than as a
    /// read-then-write here is what keeps two concurrent confirmations of the same address from both
    /// inserting and colliding with the unique index. Importantly, it does <b>not</b> clear
    /// <c>IsBlocked</c> — a banned address that re-confirms stays banned, so this path cannot be used
    /// to launder a ban.</para>
    /// <para><b>Flow:</b> trim the address → bind → helper opens the connection asynchronously →
    /// <c>QuerySingleAsync</c>, since the function always yields exactly one row.</para>
    /// <para><b>Side Effects:</b> Inserts or refreshes exactly one row in <c>VerifiedEmail</c>.</para>
    /// </remarks>
    /// <param name="email">The confirmed address; trimmed before binding.</param>
    /// <param name="displayName">The name to remember for this address.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The <c>VerifiedEmailId</c> of the inserted or refreshed row.</returns>
    public async Task<long> RecordVerifiedAsync(string email, string displayName, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pEmail", email?.Trim());
        parameters.Add("pDisplayName", displayName);
        return await QuerySingleAsync<long>(
            RecordVerifiedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Bans or unbans a confirmed address, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A one-column UPDATE of <c>IsBlocked</c>, matched on
    /// <c>LOWER(Email)</c> so the ban lands on the row every read path resolves to, whatever case the
    /// operator typed. Setting it makes <see cref="IsVerifiedAsync"/> answer <c>false</c>, which is
    /// how a ban actually takes effect — the address is not deleted, so unbanning restores the
    /// original <c>VerifiedOn</c> history rather than starting it over.</para>
    /// <para><b>Flow:</b> trim the address → bind → helper opens the connection asynchronously →
    /// execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates the matching row. An address that was never confirmed
    /// matches nothing and is a silent no-op — banning pre-emptively does not work, and the caller
    /// gets no signal that it did not; check with <see cref="GetByEmailAsync"/> first if that
    /// matters.</para>
    /// </remarks>
    /// <param name="email">The address to ban or unban; compared case-insensitively.</param>
    /// <param name="isBlocked"><c>true</c> to ban, <c>false</c> to restore.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the statement has run.</returns>
    public async Task SetBlockedAsync(string email, bool isBlocked, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Email", email?.Trim());
        parameters.Add("IsBlocked", isBlocked);
        await ExecuteAsync(SetBlockedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every registry entry, most recently confirmed first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Administrative view; blocked addresses are included, because the
    /// operator managing bans is exactly who needs to see them.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All registry rows.</returns>
    public override async Task<IEnumerable<VerifiedEmail>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<VerifiedEmail>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a registry entry by primary key as a single-item sequence, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A verified address has no parent entity, so the generic "by id"
    /// contract degenerates to a primary-key lookup.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetSingleAsync"/> → wrap or empty.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="verifiedEmailId">The registry row id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching row, or an empty sequence.</returns>
    public override async Task<IEnumerable<VerifiedEmail>> GetAllByIdAsync(long verifiedEmailId, CancellationToken cancellationToken = default)
    {
        var existing = await GetSingleAsync(verifiedEmailId, cancellationToken).ConfigureAwait(false);
        return existing == null ? Enumerable.Empty<VerifiedEmail>() : new[] { existing };
    }

    /// <summary>
    /// Gets a page of registry entries, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a long registry never crosses the
    /// wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A page of registry rows.</returns>
    public override async Task<IEnumerable<VerifiedEmail>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return await QueryAsync<VerifiedEmail>(SelectPagedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a registry entry by primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="verifiedEmailId">The registry row id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The row, or <c>null</c>.</returns>
    public override async Task<VerifiedEmail?> GetSingleAsync(long verifiedEmailId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("VerifiedEmailId", verifiedEmailId);
        return await QueryFirstOrDefaultAsync<VerifiedEmail>(
            SelectByIdSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a registry entry by its 32-bit primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="verifiedEmailId">The registry row id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The row, or <c>null</c>.</returns>
    public override Task<VerifiedEmail?> GetIntSingleAsync(int verifiedEmailId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(verifiedEmailId, cancellationToken);
    }

    /// <summary>
    /// Inserts or refreshes a registry entry, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The upsert already returns the key, so the plain insert is
    /// written in terms of the key-returning one; leaving them separate would risk one being
    /// converted while the other still blocked.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/> and discard the key.</para>
    /// <para><b>Side Effects:</b> Inserts or updates one row in <c>VerifiedEmail</c>.</para>
    /// </remarks>
    /// <param name="verifiedEmail">The address to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(VerifiedEmail verifiedEmail, CancellationToken cancellationToken = default)
    {
        await InsertToGetIdAsync(verifiedEmail, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts or refreshes a registry entry and returns its id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to the <c>RecordVerifiedEmail</c> stored function,
    /// which upserts on <c>LOWER(Email)</c> and refreshes the last-used stamp in one statement, so
    /// two concurrent confirmations of the same address cannot create two rows.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → stored function → read scalar.</para>
    /// <para><b>Side Effects:</b> Inserts or updates one row in <c>VerifiedEmail</c>.</para>
    /// </remarks>
    /// <param name="verifiedEmail">The address to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The registry row id.</returns>
    public override async Task<long> InsertToGetIdAsync(VerifiedEmail verifiedEmail, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pEmail", verifiedEmail.Email);
        parameters.Add("pDisplayName", verifiedEmail.DisplayName);
        return await QuerySingleAsync<long>(
            RecordVerifiedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the mutable fields of a registry entry, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The address itself and its first-confirmed stamp are immutable —
    /// rewriting either would destroy the history the registry exists to keep. <c>LastUsedOn</c> is
    /// normalised through <see cref="DbTimestamp.AsTimestamp(DateTime?)"/> so a value whose Kind is
    /// Utc is still sent as a <c>timestamp</c>, matching the column.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>VerifiedEmailId</c>.</para>
    /// </remarks>
    /// <param name="verifiedEmail">The entry carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(VerifiedEmail verifiedEmail, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            UpdateSql, BuildUpdateParameters(verifiedEmail), cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every registry entry, most recently confirmed first.
    /// </summary>
    /// <returns>All registry rows.</returns>
    public override IEnumerable<VerifiedEmail> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<VerifiedEmail>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets a registry entry by primary key, as a single-item sequence.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A verified address has no parent entity, so the generic
    /// "by id" contract degenerates to a primary-key lookup.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="verifiedEmailId">The registry row id.</param>
    /// <returns>The matching row, or an empty sequence.</returns>
    public override IEnumerable<VerifiedEmail> GetAllById(long verifiedEmailId)
    {
        var existing = GetSingle(verifiedEmailId);
        return existing == null ? Enumerable.Empty<VerifiedEmail>() : new[] { existing };
    }

    /// <summary>
    /// Gets a page of registry entries.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>A page of registry rows.</returns>
    public override IEnumerable<VerifiedEmail> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return connection.Query<VerifiedEmail>(SelectPagedSql, parameters).ToList();
    }

    /// <summary>
    /// Gets a registry entry by primary key.
    /// </summary>
    /// <param name="verifiedEmailId">The registry row id.</param>
    /// <returns>The row, or null.</returns>
    public override VerifiedEmail? GetSingle(long verifiedEmailId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("VerifiedEmailId", verifiedEmailId);
        return connection.QueryFirstOrDefault<VerifiedEmail>(SelectByIdSql, parameters);
    }

    /// <summary>
    /// Gets a registry entry by its 32-bit primary key.
    /// </summary>
    /// <param name="verifiedEmailId">The registry row id.</param>
    /// <returns>The row, or null.</returns>
    public override VerifiedEmail? GetIntSingle(int verifiedEmailId)
    {
        return GetSingle(verifiedEmailId);
    }

    /// <summary>
    /// Inserts or refreshes a registry entry.
    /// </summary>
    /// <param name="verifiedEmail">The address to record.</param>
    public override void Insert(VerifiedEmail verifiedEmail)
    {
        InsertToGetId(verifiedEmail);
    }

    /// <summary>
    /// Inserts or refreshes a registry entry and returns its id.
    /// </summary>
    /// <param name="verifiedEmail">The address to record.</param>
    /// <returns>The registry row id.</returns>
    public override long InsertToGetId(VerifiedEmail verifiedEmail)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("pEmail", verifiedEmail.Email);
        parameters.Add("pDisplayName", verifiedEmail.DisplayName);
        return connection.ExecuteScalar<long>(RecordVerifiedSql, parameters);
    }

    /// <summary>
    /// Updates the mutable fields of a registry entry.
    /// </summary>
    /// <param name="verifiedEmail">The entry carrying the new values.</param>
    public override void Update(VerifiedEmail verifiedEmail)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(verifiedEmail));
    }

    /// <summary>
    /// Builds the parameter set shared by both update paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>LastUsedOn</c> is normalised to
    /// <see cref="DateTimeKind.Unspecified"/> because Npgsql picks the wire type from the value's
    /// Kind: a <c>Utc</c> value is sent as <c>timestamptz</c>, which does not match the
    /// <c>TIMESTAMP</c> column this schema declares. A <c>null</c> stays <c>null</c>.</para>
    /// <para><b>Flow:</b> normalise the timestamp → bind the mutable columns and the key.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="verifiedEmail">The entry being written.</param>
    /// <returns>Populated Dapper parameters.</returns>
    private static DynamicParameters BuildUpdateParameters(VerifiedEmail verifiedEmail)
    {
        var parameters = new DynamicParameters();
        parameters.Add("DisplayName", verifiedEmail.DisplayName);
        parameters.Add("LastUsedOn", DbTimestamp.AsTimestamp(verifiedEmail.LastUsedOn));
        parameters.Add("IsBlocked", verifiedEmail.IsBlocked);
        parameters.Add("VerifiedEmailId", verifiedEmail.VerifiedEmailId);
        return parameters;
    }
}
