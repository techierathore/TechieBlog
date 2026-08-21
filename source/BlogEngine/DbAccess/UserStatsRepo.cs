using BlogModels.Interfaces;
using BlogModels.Models;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserStat data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for UserStat entities using Dapper. Statistics are
/// the headline tiles on the portfolio home page and the About and Community blocks of
/// <c>/resume</c>, maintained at <c>/admin/stats</c>.</para>
///
/// <para><b>Code Flow:</b> <c>UserStatsSvc</c> injects this repository, calls an <c>…Async</c>
/// member, and the member routes through the protected helpers on <c>GenericRepository</c>, which
/// open the connection asynchronously and flow the cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage. Both twins execute the
/// same SQL constant, so they cannot drift apart.</para>
///
/// <para><b>No timestamp columns:</b> <c>UserStats</c> carries no <c>TIMESTAMP</c> column, so the
/// <c>42883</c> trap that applies to its award and skill siblings cannot arise here.</para>
/// </remarks>
public class UserStatsRepo : GenericRepository<UserStat>, IUserStatsRepo
{
    private const string StatColumns = "StatId, UserId, StatLabel, StatValue, StatCategory, DisplayOrder";

    private const string SelectAllSql = @"
            SELECT " + StatColumns + @"
            FROM userstats
            ORDER BY DisplayOrder ASC";

    private const string SelectByUserIdSql = @"
            SELECT " + StatColumns + @"
            FROM userstats
            WHERE UserId = @UserId
            ORDER BY DisplayOrder ASC";

    private const string SelectByUserIdAndCategorySql = @"
            SELECT " + StatColumns + @"
            FROM userstats
            WHERE UserId = @UserId AND StatCategory = @Category
            ORDER BY DisplayOrder ASC";

    private const string SelectByIdSql = @"
            SELECT " + StatColumns + @"
            FROM userstats
            WHERE StatId = @StatId";

    private const string SelectPagedSql = @"
            SELECT " + StatColumns + @"
            FROM userstats
            ORDER BY DisplayOrder ASC
            LIMIT @PageSize OFFSET @Offset";

    private const string InsertSql = @"
            INSERT INTO userstats (UserId, StatLabel, StatValue, StatCategory, DisplayOrder)
            VALUES (@UserId, @StatLabel, @StatValue, @StatCategory, @DisplayOrder)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING StatId";

    private const string UpdateSql = @"
            UPDATE userstats SET
                UserId = @UserId,
                StatLabel = @StatLabel,
                StatValue = @StatValue,
                StatCategory = @StatCategory,
                DisplayOrder = @DisplayOrder
            WHERE StatId = @StatId";

    private const string DeleteSql = "DELETE FROM userstats WHERE StatId = @StatId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public UserStatsRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every statistic in the table, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Display order is the tile order the resume renders, so it is
    /// applied in SQL rather than left to each caller to re-sort.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All statistics, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<UserStat>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserStat>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every statistic belonging to a user, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generic parent-key member and the named member are the same
    /// query for this entity — a statistic's only parent is its user.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetByUserIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's statistics, or an empty sequence when they have none.</returns>
    public override Task<IEnumerable<UserStat>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return GetByUserIdAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Gets every statistic belonging to a user, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The resume splits the returned rows into the About and Community
    /// blocks in memory rather than issuing one query per block, so this single read serves both.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query filtered on user → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's statistics, or an empty sequence when they have none.</returns>
    public async Task<IEnumerable<UserStat>> GetByUserIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserStat>(
            SelectByUserIdSql, new { UserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a user's statistics within one category, in display order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Category matching is exact and the column is nullable, so an
    /// uncategorised statistic is never returned by this member — only by
    /// <see cref="GetByUserIdAsync"/>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query filtered on user and
    /// category → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="category">The category name to filter on.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching statistics, or an empty sequence when none match.</returns>
    public async Task<IEnumerable<UserStat>> GetByUserIdAndCategoryAsync(long userId, string category, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserStat>(
            SelectByUserIdAndCategorySql,
            new { UserId = userId, Category = category },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single statistic by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>,
    /// which is how <c>UserStatsSvc</c> reports "this statistic has already been deleted".</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetByIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="statId">The statistic identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistic, or <c>null</c> when no row carries that key.</returns>
    public override Task<UserStat?> GetSingleAsync(long statId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(statId, cancellationToken);
    }

    /// <summary>
    /// Gets a single statistic by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="statId">The statistic identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistic, or <c>null</c> when no row carries that key.</returns>
    public async Task<UserStat?> GetByIdAsync(long statId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<UserStat>(
            SelectByIdSql, new { StatId = statId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single statistic by INT identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGSERIAL</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetByIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="statId">The statistic identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistic, or <c>null</c> when no row carries that key.</returns>
    public override Task<UserStat?> GetIntSingleAsync(int statId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(statId, cancellationToken);
    }

    /// <summary>
    /// Gets a page of statistics, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a long statistics list never crosses
    /// the wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<UserStat>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserStat>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new statistic, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserStats</c>.</para>
    /// </remarks>
    /// <param name="stat">The statistic to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(UserStat stat, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(stat), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a statistic and returns the generated identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserStats</c>.</para>
    /// </remarks>
    /// <param name="stat">The statistic to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>StatId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(UserStat stat, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(stat), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new statistic and returns its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The name <c>UserStatsSvc</c> uses; the behaviour is exactly the
    /// RETURNING insert, so it forwards rather than duplicating the SQL.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/>.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserStats</c>.</para>
    /// </remarks>
    /// <param name="stat">The statistic to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>StatId</c>.</returns>
    public Task<long> CreateAsync(UserStat stat, CancellationToken cancellationToken = default)
    {
        return InsertToGetIdAsync(stat, cancellationToken);
    }

    /// <summary>
    /// Updates an existing statistic, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> All five editable fields are written together, which is what makes
    /// the reorder path a plain sequence of updates rather than a special statement.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>StatId</c>.</para>
    /// </remarks>
    /// <param name="stat">The statistic carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(UserStat stat, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(stat), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a statistic by identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an unknown identifier affects no rows and is treated as a
    /// no-op rather than an error, so a double submit is harmless; <c>UserStatsSvc</c> checks for
    /// existence first so the admin screen can still report a row that had already gone.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row from <c>UserStats</c>.</para>
    /// </remarks>
    /// <param name="statId">The statistic identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    public async Task DeleteAsync(long statId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(DeleteSql, new { StatId = statId }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every statistic in the table, in display order.
    /// </summary>
    /// <returns>All statistics.</returns>
    public override IEnumerable<UserStat> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserStat>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets every statistic belonging to a user, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The user's statistics.</returns>
    public override IEnumerable<UserStat> GetAllById(long userId)
    {
        return GetByUserId(userId);
    }

    /// <summary>
    /// Gets every statistic belonging to a user, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The user's statistics.</returns>
    public IEnumerable<UserStat> GetByUserId(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserStat>(SelectByUserIdSql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Gets a user's statistics within one category, in display order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="category">The category name to filter on.</param>
    /// <returns>The matching statistics.</returns>
    public IEnumerable<UserStat> GetByUserIdAndCategory(long userId, string category)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserStat>(
            SelectByUserIdAndCategorySql, new { UserId = userId, Category = category }).ToList();
    }

    /// <summary>
    /// Gets a single statistic by its identifier.
    /// </summary>
    /// <param name="statId">The statistic identifier.</param>
    /// <returns>The statistic, or <c>null</c> when not found.</returns>
    public override UserStat? GetSingle(long statId)
    {
        return GetById(statId);
    }

    /// <summary>
    /// Gets a single statistic by its identifier.
    /// </summary>
    /// <param name="statId">The statistic identifier.</param>
    /// <returns>The statistic, or <c>null</c> when not found.</returns>
    public UserStat? GetById(long statId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserStat>(SelectByIdSql, new { StatId = statId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single statistic by INT identifier.
    /// </summary>
    /// <param name="statId">The statistic identifier.</param>
    /// <returns>The statistic, or <c>null</c> when not found.</returns>
    public override UserStat? GetIntSingle(int statId)
    {
        return GetById(statId);
    }

    /// <summary>
    /// Gets a page of statistics.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<UserStat> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserStat>(SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new statistic.
    /// </summary>
    /// <param name="stat">The statistic to persist.</param>
    public override void Insert(UserStat stat)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(stat));
    }

    /// <summary>
    /// Inserts a statistic and returns the generated identifier.
    /// </summary>
    /// <param name="stat">The statistic to persist.</param>
    /// <returns>The generated <c>StatId</c>.</returns>
    public override long InsertToGetId(UserStat stat)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(stat));
    }

    /// <summary>
    /// Creates a new statistic and returns its identifier.
    /// </summary>
    /// <param name="stat">The statistic to persist.</param>
    /// <returns>The generated <c>StatId</c>.</returns>
    public long Create(UserStat stat)
    {
        return InsertToGetId(stat);
    }

    /// <summary>
    /// Updates an existing statistic.
    /// </summary>
    /// <param name="stat">The statistic carrying the new values.</param>
    public override void Update(UserStat stat)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(stat));
    }

    /// <summary>
    /// Deletes a statistic by identifier.
    /// </summary>
    /// <param name="statId">The statistic identifier.</param>
    public void Delete(long statId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteSql, new { StatId = statId });
    }

    // =================================================================================================
    // Parameter binding shared by both twins.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter object both insert statements bind.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The generated key is excluded — PostgreSQL assigns it.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="stat">The statistic being persisted.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildInsertParameters(UserStat stat)
    {
        return new
        {
            stat.UserId,
            stat.StatLabel,
            stat.StatValue,
            stat.StatCategory,
            stat.DisplayOrder
        };
    }

    /// <summary>
    /// Builds the parameter object the update statement binds.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Carries the key as well as the editable fields, because the
    /// statement matches on it.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="stat">The statistic being updated.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildUpdateParameters(UserStat stat)
    {
        return new
        {
            stat.StatId,
            stat.UserId,
            stat.StatLabel,
            stat.StatValue,
            stat.StatCategory,
            stat.DisplayOrder
        };
    }
}
