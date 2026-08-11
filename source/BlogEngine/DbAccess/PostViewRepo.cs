using BlogEngine.DaCore;
using BlogModels;
using BlogModels.Interfaces;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for the <c>PostViews</c> analytics table.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives the long-dormant <c>PostViews</c> table its first writer (BRD-60) and
/// serves the total/unique aggregates the analytics service reports.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>PostViewTracker</c> calls <see cref="RecordViewAsync"/> on a post page render.</item>
///   <item>The insert is conditional — <c>INSERT ... SELECT ... WHERE NOT EXISTS</c> — so the
///         de-duplication decision and the write happen in one statement and two simultaneous
///         requests cannot both slip a row through a read-then-write gap.</item>
///   <item>Aggregate reads serve total views and unique views from the maintained rollup row.</item>
/// </list>
///
/// <para><b>[REQ-NFR-034] The read path no longer aggregates <c>PostViews</c>.</b> Every figure the
/// public post page shows now comes from <c>PostViewCount</c>, a one-row-per-post rollup created by
/// migration 028 and maintained by <see cref="RecordViewAsync"/> inside the very statement that
/// records the view. Two properties follow and both matter:</para>
/// <list type="bullet">
///   <item><b>The read is constant work.</b> <c>WHERE PostId = @PostId</c> against the rollup's
///     primary key returns one row whether <c>PostViews</c> holds seventeen rows or seventeen
///     million. The old query's plan was <c>Aggregate → Sort (Sort Key: visitorhash) → Seq Scan on
///     postviews</c>, which is linear in a table that only ever grows.</item>
///   <item><b>The counters cannot silently drift.</b> They move only as part of a successful
///     conditional insert, in the same transaction, so there is no window in which a view exists but
///     is uncounted. The one way to break that invariant is to write <c>PostViews</c> through the
///     generic <see cref="InsertAsync"/>/<see cref="Insert"/> members, which bypass both the
///     de-duplication rule and the rollup — they exist only to satisfy
///     <c>GenericRepository&lt;T&gt;</c> and nothing in the application calls them. If a bulk import
///     is ever added, it must re-run the backfill in script 028 afterwards.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Dapper, Npgsql (via <c>DbConnectionFactory</c>), the
/// <c>PostViews.VisitorHash</c> column added by migration 015, the <c>PostViewCount</c> rollup table
/// and the <c>IdxPostViewsPostIdVisitorHash</c> index added by migrations 028 and 015.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as <c>IPostViewRepo</c>.
/// The legacy <c>ViewerIp</c> column is deliberately written as NULL — no raw address is stored.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> This repository sits on the hottest path in the
/// application: <see cref="RecordViewAsync"/> runs once per post-page render. Its three members
/// already returned tasks but opened the connection with the blocking <c>GetOpenConnection()</c>, so
/// every article view still parked a thread-pool thread for the connection handshake before the
/// conditional insert began. Both writes now bind their timestamps through
/// <see cref="DbTimestamp.AsTimestamp"/>: <c>PostViews.ViewedOn</c> is declared <c>TIMESTAMP</c> and
/// the caller passes <c>DateTime.UtcNow</c>, whose <c>Kind</c> makes Npgsql send <c>timestamptz</c>,
/// which PostgreSQL then coerces at the session time zone — enough to push a view into the wrong
/// calendar day on the trend chart, and enough to shift the de-duplication window.</para>
/// </remarks>
public class PostViewRepo : GenericRepository<PostView>, IPostViewRepo
{
    private const string ViewColumns = "ViewId, PostId, ViewedOn, ViewerIp, COALESCE(VisitorHash, '') AS VisitorHash";

    /// <summary>
    /// [REQ-NFR-034] Records the view and moves the rollup counters in ONE statement.
    /// </summary>
    /// <remarks>
    /// <para>Three CTEs, and the order they see the world in is the whole point:</para>
    /// <list type="number">
    ///   <item><c>Seen</c> asks whether this visitor has EVER viewed this post. Every CTE in a
    ///     statement observes the same snapshot, so this is evaluated against the table as it was
    ///     <i>before</i> <c>Written</c> inserts — which is exactly the question
    ///     <c>COUNT(DISTINCT VisitorHash)</c> would have answered. The probe is covered end to end
    ///     by <c>IdxPostViewsPostIdVisitorHash (PostId, VisitorHash, ViewedOn DESC)</c>, so it is an
    ///     index lookup that stops at the first tuple, not a scan.</item>
    ///   <item><c>Written</c> is the original conditional insert, unchanged: the de-duplication test
    ///     and the write stay one atomic statement, so two simultaneous renders by the same visitor
    ///     still cannot both decide "not seen yet".</item>
    ///   <item><c>Rolled</c> selects <c>FROM Written</c> — so it produces one row when a view was
    ///     actually recorded and NO rows when the view was de-duplicated. That single dependency is
    ///     what keeps the counters honest: they can only move when a <c>PostViews</c> row moved
    ///     them, in the same transaction, so the rollup cannot drift from its source unless the
    ///     insert itself failed. <c>ON CONFLICT</c> creates the rollup row on a post's first ever
    ///     view, so no separate "seed the counters" step exists to forget.</item>
    /// </list>
    /// <para>A de-duplicated view is a no-op in both tables, which is why <c>UniqueViews</c> is not
    /// adjusted on that path: being inside the de-duplication window <i>implies</i> the visitor is
    /// already counted.</para>
    /// <para>PostgreSQL runs a data-modifying CTE exactly once and always to completion whether or
    /// not the primary query reads its output, so <c>Rolled</c> executes even though the final
    /// SELECT only reads <c>Written</c>.</para>
    /// </remarks>
    private const string InsertIfNotSeenSql = @"
            WITH Seen AS (
                SELECT EXISTS (
                    SELECT 1 FROM PostViews
                    WHERE PostId = @PostId
                      AND VisitorHash = @VisitorHash) AS VisitorKnown
            ),
            Written AS (
                INSERT INTO PostViews (PostId, ViewedOn, ViewerIp, VisitorHash)
                SELECT @PostId, @ViewedOn, NULL, @VisitorHash
                WHERE NOT EXISTS (
                    SELECT 1 FROM PostViews
                    WHERE PostId = @PostId
                      AND VisitorHash = @VisitorHash
                      AND ViewedOn > @WindowStart)
                RETURNING ViewId
            ),
            Rolled AS (
                INSERT INTO PostViewCount (PostId, TotalViews, UniqueViews, UpdatedOn)
                SELECT @PostId,
                       1,
                       CASE WHEN (SELECT VisitorKnown FROM Seen) THEN 0 ELSE 1 END,
                       @ViewedOn
                FROM Written
                ON CONFLICT (PostId) DO UPDATE
                    SET TotalViews  = PostViewCount.TotalViews  + EXCLUDED.TotalViews,
                        UniqueViews = PostViewCount.UniqueViews + EXCLUDED.UniqueViews,
                        UpdatedOn   = EXCLUDED.UpdatedOn
                RETURNING PostId
            )
            SELECT COUNT(*)::int FROM Written";

    /// <summary>
    /// [REQ-NFR-034] Reads one post's readership figures by primary key — constant work.
    /// </summary>
    /// <remarks>
    /// This replaced <c>SELECT COUNT(*), COUNT(DISTINCT VisitorHash) FROM PostViews WHERE
    /// PostId = @PostId</c>, whose measured plan was <c>Aggregate → Sort → Seq Scan on postviews</c>.
    /// A post with no rollup row has never been viewed and yields no row at all; the caller turns
    /// that into a zeroed <c>PostViewCounts</c>, which is the same answer the aggregate gave.
    /// </remarks>
    private const string SelectCountsSql = @"
            SELECT PostId, TotalViews, UniqueViews
            FROM PostViewCount
            WHERE PostId = @PostId";

    private const string SelectSiteTotalSql = "SELECT COUNT(*)::int FROM PostViews";

    private const string SelectAllSql =
        $"SELECT {ViewColumns} FROM PostViews ORDER BY ViewedOn DESC LIMIT {RecentViewBudgetText}";

    private const string SelectByPostSql =
        $"SELECT {ViewColumns} FROM PostViews WHERE PostId = @PostId ORDER BY ViewedOn DESC";

    private const string SelectPagedSql =
        $"SELECT {ViewColumns} FROM PostViews ORDER BY ViewedOn DESC LIMIT @PageSize OFFSET @OffSet";

    private const string SelectByViewIdSql =
        $"SELECT {ViewColumns} FROM PostViews WHERE ViewId = @ViewId";

    private const string InsertSql =
        "INSERT INTO PostViews (PostId, ViewedOn, ViewerIp, VisitorHash) VALUES (@PostId, @ViewedOn, NULL, @VisitorHash)";

    private const string InsertReturningIdSql = @"
            INSERT INTO PostViews (PostId, ViewedOn, ViewerIp, VisitorHash)
            VALUES (@PostId, @ViewedOn, NULL, @VisitorHash) RETURNING ViewId";

    /// <summary>
    /// Row ceiling on the unfiltered listing, as SQL text so it can live inside a <c>const</c>.
    /// </summary>
    private const string RecentViewBudgetText = "1000";

    /// <summary>
    /// Initializes the repository with the PostgreSQL connection string.
    /// </summary>
    /// <param name="connectionString">Connection string supplied by <c>BlogSvcInitializer</c>.</param>
    public PostViewRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Records a post view unless the visitor already viewed the post inside the window, without
    /// blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The de-duplication test and the write are one statement, so two
    /// simultaneous requests from the same visitor cannot both pass a separate read and then both
    /// insert. The window is applied as an absolute lower bound derived from the view's own timestamp
    /// rather than from <c>now()</c>, which keeps the decision reproducible. A negative window is
    /// treated as its magnitude, so a misconfigured value can never turn the guard into a look-ahead
    /// that matches nothing.</para>
    /// <para><b>Flow:</b> normalise both timestamps → helper opens the connection asynchronously →
    /// conditional insert → report whether a row was written.</para>
    /// <para><b>Side Effects:</b> Writes at most one row to <c>PostViews</c> and, when it does, moves
    /// the matching <c>PostViewCount</c> rollup row by the same amount in the same statement.</para>
    /// <para><b>[REQ-NFR-034]</b> The result is read as a scalar rather than as an affected-row count
    /// because the statement now ends in a <c>SELECT</c> over the insert's <c>RETURNING</c> output;
    /// <c>ExecuteAsync</c> would report <c>-1</c> for that shape and every view would look
    /// de-duplicated.</para>
    /// </remarks>
    /// <param name="postId">The viewed post.</param>
    /// <param name="visitorHash">Salted, irreversible visitor hash.</param>
    /// <param name="viewedOn">UTC timestamp of the view.</param>
    /// <param name="dedupeWindowHours">Hours during which a repeat view is not counted again.</param>
    /// <param name="cancellationToken">Cancels the conditional insert.</param>
    /// <returns><c>true</c> when a new row was written; <c>false</c> when the view was de-duplicated.</returns>
    public async Task<bool> RecordViewAsync(
        long postId,
        string visitorHash,
        DateTime viewedOn,
        int dedupeWindowHours,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PostId", postId);
        parameters.Add("VisitorHash", visitorHash);
        parameters.Add("ViewedOn", DbTimestamp.AsTimestamp(viewedOn));
        parameters.Add("WindowStart", DbTimestamp.AsTimestamp(viewedOn.AddHours(-Math.Abs(dedupeWindowHours))));

        var rowsWritten = await ExecuteScalarAsync<int>(InsertIfNotSeenSql, parameters, cancellationToken)
            .ConfigureAwait(false);
        return rowsWritten > 0;
    }

    /// <summary>
    /// Reads total and unique view counts for one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Total views are rows; unique views are distinct visitor hashes over
    /// all time. Both are read from the maintained <c>PostViewCount</c> rollup rather than recomputed,
    /// so the definitions are unchanged and only the cost of answering is. A post nobody has read has
    /// no rollup row, and the null coalesce turns that into the same zeroed answer the old aggregate
    /// produced.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → primary-key lookup → counts or a
    /// zeroed value carrying the post id.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// <para><b>[REQ-NFR-034] This is the per-render read, and it is now O(1).</b> It used to be
    /// <c>COUNT(*)</c> + <c>COUNT(DISTINCT VisitorHash)</c> over every <c>PostViews</c> row belonging
    /// to the post — measured as <c>Aggregate → Sort → Seq Scan</c> — on every single article view.
    /// It is a unique-index probe of one row, and stays one row at any table size.</para>
    /// </remarks>
    /// <param name="postId">The post to count.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Counts for the post; zeroes when it has never been viewed.</returns>
    public async Task<PostViewCounts> GetCountsAsync(long postId, CancellationToken cancellationToken = default)
    {
        var counts = await QueryFirstOrDefaultAsync<PostViewCounts>(
            SelectCountsSql, new { PostId = postId }, cancellationToken).ConfigureAwait(false);
        return counts ?? new PostViewCounts { PostId = postId };
    }

    /// <summary>
    /// Counts every recorded view across the site, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A plain row count over <c>PostViews</c>; the table only ever holds
    /// de-duplicated rows, so no further filtering is needed.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → scalar count.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Total rows in <c>PostViews</c>.</returns>
    public async Task<int> GetSiteTotalViewsAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(SelectSiteTotalSql, null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the most recent view rows, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>PostViews</c> grows without bound, so "all" is capped at the
    /// most recent 1000 rows; analytics callers use the aggregate members instead.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → capped listing → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The most recent 1000 view rows, newest first.</returns>
    public override async Task<IEnumerable<PostView>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<PostView>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads every recorded view for one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Newest first, because a view history is read from the present
    /// backwards.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered listing → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">The post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>View rows for the post, newest first.</returns>
    public override async Task<IEnumerable<PostView>> GetAllByIdAsync(
        long singleId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<PostView>(
            SelectByPostSql, new { PostId = singleId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one page of view rows, newest first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a large view log never crosses the wire
    /// in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One page of view rows.</returns>
    public override async Task<IEnumerable<PostView>> GetPagedDataAsync(
        int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<PostView>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads a single view row by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">View identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The view row, or <c>null</c> when it does not exist.</returns>
    public override async Task<PostView?> GetSingleAsync(
        long singleId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<PostView>(
            SelectByViewIdSql, new { ViewId = singleId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Integer-keyed single read required by <c>GenericRepository</c>, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key; <c>ViewId</c> is <c>BIGINT</c>.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">View identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The view row, or <c>null</c> when it does not exist.</returns>
    public override Task<PostView?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Unconditional insert required by <c>GenericRepository</c>, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This member skips the de-duplication rule entirely — prefer
    /// <see cref="RecordViewAsync"/>, which applies it. It exists to satisfy the generic contract.</para>
    /// <para><b>Flow:</b> build parameters → helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>PostViews</c>.</para>
    /// </remarks>
    /// <param name="entity">The view row to write.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(PostView entity, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Unconditional insert returning the generated identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same columns as <see cref="InsertAsync"/> — the two share the
    /// parameter builder so they cannot drift — with PostgreSQL returning the identity from the insert
    /// itself rather than costing a second round trip.</para>
    /// <para><b>Flow:</b> build parameters → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read the single value.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>PostViews</c>.</para>
    /// </remarks>
    /// <param name="entity">The view row to write.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>ViewId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(PostView entity, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// View rows are immutable analytics facts and are never updated.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Editing a recorded view would rewrite history the aggregates are
    /// derived from.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entityToUpdate">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override Task UpdateAsync(PostView entityToUpdate, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ImmutableRowMessage);

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Synchronous listing required by <c>GenericRepository</c>; analytics callers use the
    /// aggregate methods instead.
    /// </summary>
    /// <returns>The most recent 1000 view rows, newest first.</returns>
    public override IEnumerable<PostView> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<PostView>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Reads every recorded view for one post.
    /// </summary>
    /// <param name="singleId">The post identifier.</param>
    /// <returns>View rows for the post, newest first.</returns>
    public override IEnumerable<PostView> GetAllById(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<PostView>(SelectByPostSql, new { PostId = singleId }).ToList();
    }

    /// <summary>
    /// Reads one page of view rows, newest first.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>One page of view rows.</returns>
    public override IEnumerable<PostView> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection.Query<PostView>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }).ToList();
    }

    /// <summary>
    /// Reads a single view row by its identifier.
    /// </summary>
    /// <param name="singleId">View identifier.</param>
    /// <returns>The view row, or <c>null</c> when it does not exist.</returns>
    public override PostView? GetSingle(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<PostView>(SelectByViewIdSql, new { ViewId = singleId });
    }

    /// <summary>
    /// Integer-keyed single read required by <c>GenericRepository</c>.
    /// </summary>
    /// <param name="singleId">View identifier.</param>
    /// <returns>The view row, or <c>null</c> when it does not exist.</returns>
    public override PostView? GetIntSingle(int singleId) => GetSingle(singleId);

    /// <summary>
    /// Unconditional insert required by <c>GenericRepository</c>; prefer
    /// <see cref="RecordViewAsync"/>, which applies the de-duplication rule.
    /// </summary>
    /// <param name="entity">The view row to write.</param>
    public override void Insert(PostView entity)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(entity));
    }

    /// <summary>
    /// Unconditional insert returning the generated identifier.
    /// </summary>
    /// <param name="entity">The view row to write.</param>
    /// <returns>The generated <c>ViewId</c>.</returns>
    public override long InsertToGetId(PostView entity)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(entity));
    }

    /// <summary>
    /// View rows are immutable analytics facts and are never updated.
    /// </summary>
    /// <param name="entityToUpdate">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Update(PostView entityToUpdate)
    {
        throw new NotSupportedException(ImmutableRowMessage);
    }

    /// <summary>
    /// Message shared by every unsupported update member.
    /// </summary>
    private const string ImmutableRowMessage = "Post view rows are immutable analytics records.";

    /// <summary>
    /// Builds the parameter set shared by both insert overloads.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>ViewerIp</c> is never bound — the raw address is not stored. A
    /// default timestamp is filled with the current instant so a caller cannot record a view at the
    /// zero date.</para>
    /// <para><b>Flow:</b> copy post id, normalise the timestamp for the <c>TIMESTAMP</c> column, copy
    /// the visitor hash.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="view">The view row being written.</param>
    /// <returns>Parameters for the insert statement.</returns>
    private static DynamicParameters BuildInsertParameters(PostView view)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PostId", view.PostId);
        parameters.Add("ViewedOn", DbTimestamp.AsTimestamp(view.ViewedOn == default ? DateTime.UtcNow : view.ViewedOn));
        parameters.Add("VisitorHash", view.VisitorHash);
        return parameters;
    }
}
