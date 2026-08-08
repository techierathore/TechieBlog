using BlogEngine.DaCore;
using BlogModels;
using BlogModels.Interfaces;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for popular-post ranking and per-post engagement statistics.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Answers BRD-61 in SQL. Views, comments and ratings live in three tables, so
/// doing this per post in C# would mean three round trips per row; each query here returns a whole
/// ranked or aggregated result set in one trip.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="GetPopularPostsAsync"/> joins <c>BlogPost</c> to <c>PostViews</c> restricted
///         to the ranking window, groups by post and orders by the aggregate.</item>
///   <item><see cref="GetPostEngagementAsync"/> and <see cref="GetTopEngagementAsync"/> use
///         correlated sub-selects so a post with no activity still returns a zeroed row.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Dapper, Npgsql (via <c>DbConnectionFactory</c>), and the
/// <c>BlogPost</c>, <c>PostViews</c>, <c>BlogComment</c> and <c>PostRating</c> tables.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as <c>IAnalyticsRepo</c>.
/// Every query filters to published, non-deleted posts, so unpublished drafts never surface in an
/// analytics listing.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> Three things changed and each removes a distinct
/// defect the previous shape carried:</para>
/// <list type="number">
///   <item><b>The connection is opened asynchronously.</b> Every member already returned a
///   <c>Task</c>, but each opened its connection with the blocking <c>GetOpenConnection()</c>, so the
///   TCP, TLS and authentication round trips still parked a thread-pool thread before a single row
///   moved. Routing through the protected helpers makes that impossible to forget.</item>
///   <item><b>The cancellation token flows into the command.</b> An admin who changes the date range
///   twice used to leave the first set of four aggregate queries running to completion against the
///   database with nobody left to read the answer.</item>
///   <item><b>Every bound <c>DateTime</c> passes through <see cref="DbTimestamp.AsTimestamp"/>.</b>
///   The range bounds arrive as <c>DateTime.UtcNow.Date</c>, whose <c>Kind</c> is <c>Utc</c>, and
///   Npgsql infers the wire type from the Kind — so the parameter went out as <c>timestamptz</c>
///   while <c>PostViews.ViewedOn</c> is declared <c>TIMESTAMP</c>. PostgreSQL then coerces the
///   column at the session time zone, which silently shifts the day boundaries of every trend point
///   on any host not running in UTC. The build never sees it (REQ-NFR-026 trap 1).</item>
/// </list>
///
/// <para>The synchronous twins were rewritten to run the same SQL constants directly rather than
/// blocking on their async counterparts with <c>.GetAwaiter().GetResult()</c>, which inside a Blazor
/// Server circuit is a deadlock risk.</para>
/// </remarks>
public class AnalyticsRepo : GenericRepository<PostEngagement>, IAnalyticsRepo
{
    /// <summary>
    /// Restricts every analytics query to content a reader could actually have viewed.
    /// </summary>
    private const string VisiblePostPredicate =
        "p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)";

    /// <summary>
    /// Correlated aggregate columns shared by the engagement queries.
    /// </summary>
    private const string EngagementColumns = @"
        p.PostId, p.Title, COALESCE(p.Slug, '') AS Slug,
        (SELECT COUNT(*)::int FROM PostViews v WHERE v.PostId = p.PostId) AS TotalViews,
        (SELECT COUNT(DISTINCT v.VisitorHash)::int FROM PostViews v WHERE v.PostId = p.PostId) AS UniqueViews,
        (SELECT COUNT(*)::int FROM BlogComment c WHERE c.PostId = p.PostId) AS CommentCount,
        (SELECT COUNT(*)::int FROM BlogComment c WHERE c.PostId = p.PostId AND c.Published = TRUE) AS ApprovedCommentCount,
        (SELECT COUNT(*)::int FROM PostRating r WHERE r.PostId = p.PostId) AS RatingCount,
        COALESCE((SELECT AVG(r.Rating) FROM PostRating r WHERE r.PostId = p.PostId), 0)::double precision AS AverageRating,
        (SELECT MAX(v.ViewedOn) FROM PostViews v WHERE v.PostId = p.PostId) AS LastViewedOn";

    /// <summary>
    /// Ranking columns shared by the rolling-window and closed-range popular-post queries.
    /// </summary>
    private const string PopularPostColumns = @"
            p.PostId, p.Title, COALESCE(p.Slug, '') AS Slug, p.PublishedOn,
            COUNT(v.ViewId)::int AS TotalViews,
            COUNT(DISTINCT v.VisitorHash)::int AS UniqueViews,
            (SELECT COUNT(*)::int FROM BlogComment c WHERE c.PostId = p.PostId) AS CommentCount,
            (SELECT COUNT(*)::int FROM PostRating r WHERE r.PostId = p.PostId) AS RatingCount,
            COALESCE((SELECT AVG(r.Rating) FROM PostRating r WHERE r.PostId = p.PostId), 0)::double precision AS AverageRating";

    /// <summary>
    /// Ranking order shared by both popular-post queries, so the two can never diverge.
    /// </summary>
    private const string PopularPostOrdering = @"
            GROUP BY p.PostId, p.Title, p.Slug, p.PublishedOn
            ORDER BY TotalViews DESC, UniqueViews DESC, CommentCount DESC, p.PublishedOn DESC NULLS LAST
            LIMIT @MaxCount";

    private const string SelectPopularPostsSql = $@"
            SELECT {PopularPostColumns}
            FROM BlogPost p
            INNER JOIN PostViews v ON v.PostId = p.PostId AND v.ViewedOn >= @SinceUtc
            WHERE {VisiblePostPredicate}
            {PopularPostOrdering}";

    private const string SelectPopularPostsInRangeSql = $@"
            SELECT {PopularPostColumns}
            FROM BlogPost p
            INNER JOIN PostViews v
                ON v.PostId = p.PostId AND v.ViewedOn >= @FromUtc AND v.ViewedOn < @ToUtc
            WHERE {VisiblePostPredicate}
            {PopularPostOrdering}";

    private const string SelectPostEngagementSql =
        $"SELECT {EngagementColumns} FROM BlogPost p WHERE p.PostId = @PostId";

    private const string SelectTopEngagementSql = $@"
            SELECT {EngagementColumns}
            FROM BlogPost p
            WHERE {VisiblePostPredicate}
            ORDER BY TotalViews DESC, CommentCount DESC, p.PublishedOn DESC NULLS LAST
            LIMIT @MaxCount";

    private const string SelectPagedEngagementSql = $@"
            SELECT {EngagementColumns} FROM BlogPost p
            WHERE {VisiblePostPredicate}
            ORDER BY TotalViews DESC LIMIT @PageSize OFFSET @OffSet";

    private const string SelectViewTrendSql = $@"
            SELECT DATE_TRUNC('day', v.ViewedOn) AS Day,
                   COUNT(v.ViewId)::int AS TotalViews,
                   COUNT(DISTINCT v.VisitorHash)::int AS UniqueViews
            FROM PostViews v
            INNER JOIN BlogPost p ON p.PostId = v.PostId
            WHERE v.ViewedOn >= @FromUtc AND v.ViewedOn < @ToUtc AND {VisiblePostPredicate}
            GROUP BY DATE_TRUNC('day', v.ViewedOn)
            ORDER BY Day";

    private const string SelectCategoryEngagementSql = $@"
            SELECT COALESCE(p.CategoryId, 0) AS CategoryId,
                   COALESCE(c.CategoryName, 'Uncategorised') AS CategoryName,
                   COUNT(v.ViewId)::int AS TotalViews,
                   COUNT(DISTINCT v.VisitorHash)::int AS UniqueViews,
                   COUNT(DISTINCT p.PostId)::int AS PostCount
            FROM PostViews v
            INNER JOIN BlogPost p ON p.PostId = v.PostId
            LEFT JOIN Category c ON c.CategoryId = p.CategoryId
            WHERE v.ViewedOn >= @FromUtc AND v.ViewedOn < @ToUtc AND {VisiblePostPredicate}
            GROUP BY COALESCE(p.CategoryId, 0), COALESCE(c.CategoryName, 'Uncategorised')
            ORDER BY TotalViews DESC, CategoryName
            LIMIT @MaxCount";

    private const string SelectSummarySql = $@"
            SELECT
                (SELECT COUNT(*)::int FROM PostViews v INNER JOIN BlogPost p ON p.PostId = v.PostId
                 WHERE v.ViewedOn >= @FromUtc AND v.ViewedOn < @ToUtc AND {VisiblePostPredicate}) AS TotalViews,
                (SELECT COUNT(DISTINCT v.VisitorHash)::int FROM PostViews v INNER JOIN BlogPost p ON p.PostId = v.PostId
                 WHERE v.ViewedOn >= @FromUtc AND v.ViewedOn < @ToUtc AND {VisiblePostPredicate}) AS UniqueViews,
                (SELECT COUNT(DISTINCT v.PostId)::int FROM PostViews v INNER JOIN BlogPost p ON p.PostId = v.PostId
                 WHERE v.ViewedOn >= @FromUtc AND v.ViewedOn < @ToUtc AND {VisiblePostPredicate}) AS PostsWithTraffic,
                (SELECT COUNT(*)::int FROM BlogComment c
                 WHERE c.GivenOn >= @FromUtc AND c.GivenOn < @ToUtc) AS CommentCount,
                (SELECT COUNT(*)::int FROM PostRating r
                 WHERE r.CreatedOn >= @FromUtc AND r.CreatedOn < @ToUtc) AS RatingCount,
                COALESCE((SELECT AVG(r.Rating) FROM PostRating r
                 WHERE r.CreatedOn >= @FromUtc AND r.CreatedOn < @ToUtc), 0)::double precision AS AverageRating";

    /// <summary>
    /// Initializes the repository with the PostgreSQL connection string.
    /// </summary>
    /// <param name="connectionString">Connection string supplied by <c>BlogSvcInitializer</c>.</param>
    public AnalyticsRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Ranks published posts by views recorded inside a rolling window, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The join is an INNER JOIN, so a post with no views in the window
    /// is absent rather than present with a zero — the panel is a ranking, not a catalogue. Ties break
    /// on unique views, then comments, then recency, so the order is total rather than arbitrary.</para>
    /// <para><b>Flow:</b> normalise the window bound → helper opens the connection asynchronously →
    /// grouped aggregate → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="sinceUtc">Only views at or after this UTC time are counted.</param>
    /// <param name="maxCount">Maximum posts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Ranked posts, most viewed first; empty when nothing was viewed.</returns>
    public async Task<IReadOnlyList<PopularPost>> GetPopularPostsAsync(
        DateTime sinceUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SinceUtc", DbTimestamp.AsTimestamp(sinceUtc));
        parameters.Add("MaxCount", maxCount);

        var rows = await QueryAsync<PopularPost>(SelectPopularPostsSql, parameters, cancellationToken)
            .ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Reads full engagement statistics for one post, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Correlated sub-selects rather than joins, so a post with no views,
    /// no comments and no ratings still returns a row of zeroes instead of vanishing. An unknown post
    /// id is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → single-row aggregate → first row
    /// or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">The post to describe.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistics, or <c>null</c> when the post does not exist.</returns>
    public async Task<PostEngagement?> GetPostEngagementAsync(
        long postId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<PostEngagement>(
            SelectPostEngagementSql, new { PostId = postId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads engagement statistics for the most-viewed published posts, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unlike the popular-post ranking this window is all time, because
    /// the admin overview answers "which posts matter" rather than "what is moving now".</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → aggregate → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="maxCount">Maximum posts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Statistics rows, most viewed first.</returns>
    public async Task<IReadOnlyList<PostEngagement>> GetTopEngagementAsync(
        int maxCount, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync<PostEngagement>(
            SelectTopEngagementSql, new { MaxCount = maxCount }, cancellationToken).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Ranks published posts by views recorded inside a closed date range, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same ranking rule as the rolling-window query — it shares the
    /// column list and the ordering clause so the two can never report different orders for the same
    /// data — but bounded at both ends so an admin can ask about a past period. The upper bound is
    /// exclusive, which is what keeps consecutive ranges from double-counting the boundary day.</para>
    /// <para><b>Flow:</b> normalise both bounds → helper opens the connection asynchronously →
    /// grouped aggregate → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="fromUtc">Inclusive lower bound on <c>ViewedOn</c>.</param>
    /// <param name="toUtc">Exclusive upper bound on <c>ViewedOn</c>.</param>
    /// <param name="maxCount">Maximum posts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Ranked posts, most viewed first; empty when nothing was viewed in the range.</returns>
    public async Task<IReadOnlyList<PopularPost>> GetPopularPostsInRangeAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        var parameters = BuildRangeParameters(fromUtc, toUtc);
        parameters.Add("MaxCount", maxCount);

        var rows = await QueryAsync<PopularPost>(
            SelectPopularPostsInRangeSql, parameters, cancellationToken).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Aggregates site-wide views per calendar day inside a date range, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Days with no traffic produce no row; the service fills those gaps
    /// so the chart's x-axis stays proportional. Grouping happens in SQL because pulling every view
    /// row across the wire to bucket it in C# is exactly the round trip this repository exists to
    /// avoid.</para>
    /// <para><b>Flow:</b> normalise both bounds → helper opens the connection asynchronously →
    /// <c>DATE_TRUNC</c> grouping → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="fromUtc">Inclusive lower bound on <c>ViewedOn</c>.</param>
    /// <param name="toUtc">Exclusive upper bound on <c>ViewedOn</c>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One point per day that has traffic, oldest first; empty when the range is quiet.</returns>
    public async Task<IReadOnlyList<ViewTrendPoint>> GetViewTrendAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync<ViewTrendPoint>(
            SelectViewTrendSql, BuildRangeParameters(fromUtc, toUtc), cancellationToken).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Aggregates views by the category of the viewed post, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A post with no category is attributed to a single "Uncategorised"
    /// bucket rather than dropped, so the category shares still add up to the site total.</para>
    /// <para><b>Flow:</b> normalise both bounds → helper opens the connection asynchronously → left
    /// join to <c>Category</c> and group → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="fromUtc">Inclusive lower bound on <c>ViewedOn</c>.</param>
    /// <param name="toUtc">Exclusive upper bound on <c>ViewedOn</c>.</param>
    /// <param name="maxCount">Maximum categories to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Categories ranked by views, busiest first; empty when the range is quiet.</returns>
    public async Task<IReadOnlyList<CategoryEngagement>> GetCategoryEngagementAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        var parameters = BuildRangeParameters(fromUtc, toUtc);
        parameters.Add("MaxCount", maxCount);

        var rows = await QueryAsync<CategoryEngagement>(
            SelectCategoryEngagementSql, parameters, cancellationToken).ConfigureAwait(false);
        return rows.AsList();
    }

    /// <summary>
    /// Reads the headline view, comment and rating figures for a date range, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Six figures in one statement of scalar sub-selects, so the tiles
    /// on the analytics dashboard are guaranteed to describe one consistent snapshot rather than six
    /// separately-timed reads. An empty result is impossible for this shape, but the null coalesce
    /// keeps the contract's "zeroed instance" promise honest if the provider ever returns none.</para>
    /// <para><b>Flow:</b> normalise both bounds → helper opens the connection asynchronously → single
    /// row of scalar sub-selects → return it or a zeroed summary.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="fromUtc">Inclusive lower bound on the activity timestamps.</param>
    /// <param name="toUtc">Exclusive upper bound on the activity timestamps.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The summary; a zeroed instance when nothing happened in the range.</returns>
    public async Task<AnalyticsSummary> GetSummaryAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var summary = await QueryFirstOrDefaultAsync<AnalyticsSummary>(
            SelectSummarySql, BuildRangeParameters(fromUtc, toUtc), cancellationToken).ConfigureAwait(false);
        return summary ?? new AnalyticsSummary();
    }

    /// <summary>
    /// Engagement rows are projections, not a stored entity; the top 100 stand in for "all".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "All engagement" is unbounded and nothing consumes it, so the
    /// generic contract is satisfied with the same ranked head the admin overview shows.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetTopEngagementAsync"/> and widen the result.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Engagement statistics for the 100 most-viewed published posts.</returns>
    public override async Task<IEnumerable<PostEngagement>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetTopEngagementAsync(GetAllRowBudget, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads engagement statistics for one post, addressed as a parent id.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Engagement has no parent relationship, so the id is read as the
    /// post's own key and the result is a sequence of at most one.</para>
    /// <para><b>Flow:</b> single-post lookup → wrap in a sequence or return an empty one.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A single-element sequence, or an empty one when the post does not exist.</returns>
    public override async Task<IEnumerable<PostEngagement>> GetAllByIdAsync(
        long singleId, CancellationToken cancellationToken = default)
    {
        var engagement = await GetPostEngagementAsync(singleId, cancellationToken).ConfigureAwait(false);
        return engagement == null ? Array.Empty<PostEngagement>() : new[] { engagement };
    }

    /// <summary>
    /// Reads one page of engagement statistics ordered by total views, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so the aggregate never computes more rows
    /// than the page needs.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET aggregate →
    /// materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One page of engagement statistics.</returns>
    public override async Task<IEnumerable<PostEngagement>> GetPagedDataAsync(
        int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<PostEngagement>(
            SelectPagedEngagementSql,
            new { PageSize = pageSize, OffSet = offSet },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads engagement statistics for one post, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Alias of <see cref="GetPostEngagementAsync"/> required by the
    /// generic contract.</para>
    /// <para><b>Flow:</b> delegate.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistics, or <c>null</c> when the post does not exist.</returns>
    public override Task<PostEngagement?> GetSingleAsync(
        long singleId, CancellationToken cancellationToken = default)
    {
        return GetPostEngagementAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Integer-keyed single read required by <c>GenericRepository</c>, without blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key; <c>PostId</c> is <c>BIGINT</c>, so there is no
    /// second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Post identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistics, or <c>null</c> when the post does not exist.</returns>
    public override Task<PostEngagement?> GetIntSingleAsync(
        int singleId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Engagement statistics are computed, never inserted.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The numbers are derived from views, comments and ratings; writing
    /// one directly would create a figure no source table supports.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entity">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override Task InsertAsync(PostEngagement entity, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Engagement statistics are computed, never inserted.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> See <see cref="InsertAsync"/>.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entity">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override Task<long> InsertToGetIdAsync(PostEngagement entity, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Engagement statistics are computed, never updated.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> See <see cref="InsertAsync"/>.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entityToUpdate">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override Task UpdateAsync(PostEngagement entityToUpdate, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift, and none of
    // them blocks on a task: doing that inside a Blazor Server circuit risks a deadlock (trap 7).
    // =================================================================================================

    /// <summary>
    /// Engagement rows are projections, not a stored entity; the top 100 stand in for "all".
    /// </summary>
    /// <returns>Engagement statistics for the 100 most-viewed published posts.</returns>
    public override IEnumerable<PostEngagement> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<PostEngagement>(
            SelectTopEngagementSql, new { MaxCount = GetAllRowBudget }).ToList();
    }

    /// <summary>
    /// Reads engagement statistics for one post, addressed as a parent id.
    /// </summary>
    /// <param name="singleId">Post identifier.</param>
    /// <returns>A single-element sequence, or an empty one when the post does not exist.</returns>
    public override IEnumerable<PostEngagement> GetAllById(long singleId)
    {
        var engagement = GetSingle(singleId);
        return engagement == null ? Array.Empty<PostEngagement>() : new[] { engagement };
    }

    /// <summary>
    /// Reads one page of engagement statistics ordered by total views.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>One page of engagement statistics.</returns>
    public override IEnumerable<PostEngagement> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection.Query<PostEngagement>(
            SelectPagedEngagementSql, new { PageSize = pageSize, OffSet = offSet }).ToList();
    }

    /// <summary>
    /// Reads engagement statistics for one post.
    /// </summary>
    /// <param name="singleId">Post identifier.</param>
    /// <returns>The statistics, or <c>null</c> when the post does not exist.</returns>
    public override PostEngagement? GetSingle(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<PostEngagement>(
            SelectPostEngagementSql, new { PostId = singleId });
    }

    /// <summary>
    /// Integer-keyed single read required by <c>GenericRepository</c>.
    /// </summary>
    /// <param name="singleId">Post identifier.</param>
    /// <returns>The statistics, or <c>null</c> when the post does not exist.</returns>
    public override PostEngagement? GetIntSingle(int singleId) => GetSingle(singleId);

    /// <summary>
    /// Engagement statistics are computed, never inserted.
    /// </summary>
    /// <param name="entity">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Insert(PostEngagement entity) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Engagement statistics are computed, never inserted.
    /// </summary>
    /// <param name="entity">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override long InsertToGetId(PostEngagement entity) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Engagement statistics are computed, never updated.
    /// </summary>
    /// <param name="entityToUpdate">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Update(PostEngagement entityToUpdate) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Rows returned when the generic "all engagement" contract is invoked.
    /// </summary>
    private const int GetAllRowBudget = 100;

    /// <summary>
    /// Message shared by every unsupported write member.
    /// </summary>
    private const string ComputedOnlyMessage = "Engagement statistics are computed from source tables.";

    /// <summary>
    /// Binds a date range so PostgreSQL receives two <c>TIMESTAMP</c> values.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Callers hand these bounds down from <c>DateTime.UtcNow.Date</c>,
    /// so their <c>Kind</c> is <c>Utc</c>. Npgsql picks the wire type from the Kind, so an untreated
    /// bound goes out as <c>timestamptz</c> while <c>PostViews.ViewedOn</c>, <c>BlogComment.GivenOn</c>
    /// and <c>PostRating.CreatedOn</c> are all declared <c>TIMESTAMP</c>. PostgreSQL then coerces the
    /// column at the session time zone, which moves every day boundary by the host's offset — a
    /// silent off-by-one on the first and last point of every trend, on any deployment not running in
    /// UTC. <see cref="DbTimestamp.AsTimestamp"/> drops the Kind without moving the instant.</para>
    /// <para><b>Flow:</b> normalise both bounds → bind as <c>FromUtc</c> and <c>ToUtc</c>.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="fromUtc">Inclusive lower bound.</param>
    /// <param name="toUtc">Exclusive upper bound.</param>
    /// <returns>Parameters carrying the normalised range.</returns>
    private static DynamicParameters BuildRangeParameters(DateTime fromUtc, DateTime toUtc)
    {
        var parameters = new DynamicParameters();
        parameters.Add("FromUtc", DbTimestamp.AsTimestamp(fromUtc));
        parameters.Add("ToUtc", DbTimestamp.AsTimestamp(toUtc));
        return parameters;
    }
}
