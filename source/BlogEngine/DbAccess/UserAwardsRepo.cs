using BlogModels.Interfaces;
using BlogModels.Models;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserAward data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for UserAward entities using Dapper. Awards are the
/// "Awards &amp; Recognition" block of <c>/resume</c> and the rows maintained at <c>/admin/awards</c>.</para>
///
/// <para><b>Code Flow:</b> A page or service injects <see cref="IUserAwardsRepo"/>, calls an
/// <c>…Async</c> member, and the member routes through the protected helpers on
/// <c>GenericRepository</c>, which open the connection asynchronously and flow the cancellation token
/// into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL, <see cref="DbTimestamp"/>.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage. Both twins execute the
/// same SQL constant, so they cannot drift apart.</para>
///
/// <para><b>Timestamp binding (REQ-NFR-026, trap 1):</b> <c>UserAwards.CreatedOn</c> is declared
/// <c>TIMESTAMP</c> without time zone, while callers supply <c>DateTime.UtcNow</c>, whose
/// <c>Kind</c> is <c>Utc</c>. Npgsql picks the wire type from the Kind and would send
/// <c>timestamptz</c>, leaving PostgreSQL to convert the instant into the session time zone before
/// storing it. <see cref="DbTimestamp.AsTimestamp(DateTime)"/> drops the Kind so the instant is
/// stored exactly as supplied, whatever the server's <c>TimeZone</c> happens to be.</para>
/// </remarks>
public class UserAwardsRepo : GenericRepository<UserAward>, IUserAwardsRepo
{
    private const string AwardColumns = @"
            AwardId, UserId, AwardTitle, AwardDescription, BadgeImagePath,
            AwardUrl, AwardYear, DisplayOrder, CreatedOn";

    private const string SelectAllSql = @"
            SELECT " + AwardColumns + @"
            FROM userawards
            ORDER BY DisplayOrder ASC";

    private const string SelectByUserIdSql = @"
            SELECT " + AwardColumns + @"
            FROM userawards
            WHERE UserId = @UserId
            ORDER BY DisplayOrder ASC";

    private const string SelectByIdSql = @"
            SELECT " + AwardColumns + @"
            FROM userawards
            WHERE AwardId = @AwardId";

    private const string SelectPagedSql = @"
            SELECT " + AwardColumns + @"
            FROM userawards
            ORDER BY DisplayOrder ASC
            LIMIT @PageSize OFFSET @Offset";

    private const string InsertSql = @"
            INSERT INTO userawards (UserId, AwardTitle, AwardDescription, BadgeImagePath, AwardUrl, AwardYear, DisplayOrder, CreatedOn)
            VALUES (@UserId, @AwardTitle, @AwardDescription, @BadgeImagePath, @AwardUrl, @AwardYear, @DisplayOrder, @CreatedOn)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING AwardId";

    private const string UpdateSql = @"
            UPDATE userawards SET
                UserId = @UserId,
                AwardTitle = @AwardTitle,
                AwardDescription = @AwardDescription,
                BadgeImagePath = @BadgeImagePath,
                AwardUrl = @AwardUrl,
                AwardYear = @AwardYear,
                DisplayOrder = @DisplayOrder
            WHERE AwardId = @AwardId";

    private const string DeleteSql = "DELETE FROM userawards WHERE AwardId = @AwardId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public UserAwardsRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every award in the table, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Display order is the sequence the resume renders, so it is applied
    /// in SQL rather than left to each caller to re-sort.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All awards, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<UserAward>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserAward>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every award belonging to a user, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic parent-key member and the named member are the same
    /// query for this entity — an award's only parent is its user.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetByUserIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's awards, or an empty sequence when they have none.</returns>
    public override Task<IEnumerable<UserAward>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return GetByUserIdAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Gets every award belonging to a user, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The public resume and the admin maintenance screen both read a
    /// single user's awards, so this is the hot path for the entity.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query filtered on user → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's awards, or an empty sequence when they have none.</returns>
    public async Task<IEnumerable<UserAward>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserAward>(
            SelectByUserIdSql, new { UserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single award by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>,
    /// which is how the admin screen reports "this row has already been deleted".</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetByIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="awardId">The award identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The award, or <c>null</c> when no row carries that key.</returns>
    public override Task<UserAward?> GetSingleAsync(long awardId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(awardId, cancellationToken);
    }

    /// <summary>
    /// Gets a single award by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="awardId">The award identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The award, or <c>null</c> when no row carries that key.</returns>
    public async Task<UserAward?> GetByIdAsync(long awardId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<UserAward>(
            SelectByIdSql, new { AwardId = awardId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single award by INT identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGSERIAL</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetByIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="awardId">The award identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The award, or <c>null</c> when no row carries that key.</returns>
    public override Task<UserAward?> GetIntSingleAsync(int awardId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(awardId, cancellationToken);
    }

    /// <summary>
    /// Gets a page of awards, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a long award list never crosses the
    /// wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<UserAward>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserAward>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new award, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserAwards</c>.</para>
    /// </remarks>
    /// <param name="award">The award to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(UserAward award, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(award), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts an award and returns the generated identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key.</para>
    /// <para><b>Flow:</b> normalise the timestamp → INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserAwards</c>.</para>
    /// </remarks>
    /// <param name="award">The award to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>AwardId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(UserAward award, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(award), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new award and returns its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The name the admin screen uses; the behaviour is exactly the
    /// RETURNING insert, so it forwards rather than duplicating the SQL.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserAwards</c>.</para>
    /// </remarks>
    /// <param name="award">The award to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>AwardId</c>.</returns>
    public Task<long> CreateAsync(UserAward award, CancellationToken cancellationToken = default)
    {
        return InsertToGetIdAsync(award, cancellationToken);
    }

    /// <summary>
    /// Updates an existing award, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c> is deliberately not written — an edit must not
    /// restamp when the award was recorded.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>AwardId</c>.</para>
    /// </remarks>
    /// <param name="award">The award carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(UserAward award, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(award), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an award by identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an unknown identifier affects no rows and is treated as a
    /// no-op rather than an error, so a double submit is harmless.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row from <c>UserAwards</c>.</para>
    /// </remarks>
    /// <param name="awardId">The award identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    public async Task DeleteAsync(long awardId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(DeleteSql, new { AwardId = awardId }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every award in the table, in display order.
    /// </summary>
    /// <returns>All awards.</returns>
    public override IEnumerable<UserAward> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserAward>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets every award belonging to a user, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The user's awards.</returns>
    public override IEnumerable<UserAward> GetAllById(long userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Gets every award belonging to a user, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The user's awards.</returns>
    public IEnumerable<UserAward> GetByUserId(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserAward>(SelectByUserIdSql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Gets a single award by its identifier.
    /// </summary>
    /// <param name="awardId">The award identifier.</param>
    /// <returns>The award, or <c>null</c> when not found.</returns>
    public override UserAward? GetSingle(long awardId)
    {
        return GetById(awardId);
    }

    /// <summary>
    /// Gets a single award by its identifier.
    /// </summary>
    /// <param name="awardId">The award identifier.</param>
    /// <returns>The award, or <c>null</c> when not found.</returns>
    public UserAward? GetById(long awardId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserAward>(SelectByIdSql, new { AwardId = awardId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single award by INT identifier.
    /// </summary>
    /// <param name="awardId">The award identifier.</param>
    /// <returns>The award, or <c>null</c> when not found.</returns>
    public override UserAward? GetIntSingle(int awardId)
    {
        return GetById(awardId);
    }

    /// <summary>
    /// Gets a page of awards.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<UserAward> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserAward>(SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new award.
    /// </summary>
    /// <param name="award">The award to persist.</param>
    public override void Insert(UserAward award)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(award));
    }

    /// <summary>
    /// Inserts an award and returns the generated identifier.
    /// </summary>
    /// <param name="award">The award to persist.</param>
    /// <returns>The generated <c>AwardId</c>.</returns>
    public override long InsertToGetId(UserAward award)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(award));
    }

    /// <summary>
    /// Creates a new award and returns its identifier.
    /// </summary>
    /// <param name="award">The award to persist.</param>
    /// <returns>The generated <c>AwardId</c>.</returns>
    public long Create(UserAward award)
    {
        return InsertToGetId(award);
    }

    /// <summary>
    /// Updates an existing award.
    /// </summary>
    /// <param name="award">The award carrying the new values.</param>
    public override void Update(UserAward award)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(award));
    }

    /// <summary>
    /// Deletes an award by identifier.
    /// </summary>
    /// <param name="awardId">The award identifier.</param>
    public void Delete(long awardId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteSql, new { AwardId = awardId });
    }

    // =================================================================================================
    // Parameter binding shared by both twins.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter object both insert statements bind.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c> is normalised through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> because the column is <c>TIMESTAMP</c> without
    /// time zone; a <c>Kind = Utc</c> value would otherwise be sent as <c>timestamptz</c> and shifted
    /// into the session time zone on the way in.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="award">The award being persisted.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildInsertParameters(UserAward award)
    {
        return new
        {
            award.UserId,
            award.AwardTitle,
            award.AwardDescription,
            award.BadgeImagePath,
            award.AwardUrl,
            award.AwardYear,
            award.DisplayOrder,
            CreatedOn = DbTimestamp.AsTimestamp(award.CreatedOn)
        };
    }

    /// <summary>
    /// Builds the parameter object the update statement binds.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c> is absent by design — an edit must not restamp
    /// when the award was first recorded.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="award">The award being updated.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildUpdateParameters(UserAward award)
    {
        return new
        {
            award.AwardId,
            award.UserId,
            award.AwardTitle,
            award.AwardDescription,
            award.BadgeImagePath,
            award.AwardUrl,
            award.AwardYear,
            award.DisplayOrder
        };
    }
}
