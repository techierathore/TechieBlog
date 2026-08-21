namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for popularity ranking and per-post engagement statistics.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Serves BRD-61 by joining views, comments and ratings in SQL rather than
/// making the service issue one query per metric per post.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="GetPopularPostsAsync"/> aggregates <c>PostViews</c> inside a time window and
///         ranks published posts by that aggregate.</item>
///   <item><see cref="GetPostEngagementAsync"/> returns one post's full statistics.</item>
///   <item><see cref="GetTopEngagementAsync"/> returns the same shape for the busiest posts, for an
///         admin overview table.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.AnalyticsRepo</c> over Dapper
/// and PostgreSQL.</para>
///
/// <para><b>Usage:</b> Only published, non-deleted posts are ever returned.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> Every member already returned a <c>Task</c>, but the
/// implementation opened its connection synchronously and no member accepted a cancellation token, so
/// a query the reader had already navigated away from still ran to completion on a parked thread. The
/// token was added <i>to the existing signatures</i> rather than as a second overload: an
/// <c>Async(args)</c> / <c>Async(args, ct)</c> pair makes every existing call ambiguous, and the error
/// surfaces at the call site rather than here.</para>
/// </remarks>
public interface IAnalyticsRepo
{
    /// <summary>
    /// Ranks published posts by views recorded inside a rolling window.
    /// </summary>
    /// <param name="sinceUtc">Only views at or after this UTC time are counted.</param>
    /// <param name="maxCount">Maximum posts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Ranked posts, most viewed first; empty when nothing was viewed.</returns>
    Task<IReadOnlyList<PopularPost>> GetPopularPostsAsync(
        DateTime sinceUtc, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads full engagement statistics for one post.
    /// </summary>
    /// <param name="postId">The post to describe.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistics, or null when the post does not exist.</returns>
    Task<PostEngagement?> GetPostEngagementAsync(long postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads engagement statistics for the most-viewed published posts.
    /// </summary>
    /// <param name="maxCount">Maximum posts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Statistics rows, most viewed first.</returns>
    Task<IReadOnlyList<PostEngagement>> GetTopEngagementAsync(
        int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ranks published posts by views recorded inside a closed date range.
    /// </summary>
    /// <remarks>
    /// The rolling-window overload cannot answer "last month" because it has no upper bound; the
    /// admin dashboard's date-range filter needs both ends (REQ-UI-044).
    /// </remarks>
    /// <param name="fromUtc">Inclusive lower bound on <c>ViewedOn</c>.</param>
    /// <param name="toUtc">Exclusive upper bound on <c>ViewedOn</c>.</param>
    /// <param name="maxCount">Maximum posts to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Ranked posts, most viewed first; empty when nothing was viewed in the range.</returns>
    Task<IReadOnlyList<PopularPost>> GetPopularPostsInRangeAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates site-wide views per calendar day inside a date range.
    /// </summary>
    /// <param name="fromUtc">Inclusive lower bound on <c>ViewedOn</c>.</param>
    /// <param name="toUtc">Exclusive upper bound on <c>ViewedOn</c>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One point per day that has traffic, oldest first; empty when the range is quiet.</returns>
    Task<IReadOnlyList<ViewTrendPoint>> GetViewTrendAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregates views by the category of the viewed post inside a date range.
    /// </summary>
    /// <param name="fromUtc">Inclusive lower bound on <c>ViewedOn</c>.</param>
    /// <param name="toUtc">Exclusive upper bound on <c>ViewedOn</c>.</param>
    /// <param name="maxCount">Maximum categories to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Categories ranked by views, busiest first; empty when the range is quiet.</returns>
    Task<IReadOnlyList<CategoryEngagement>> GetCategoryEngagementAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the headline view, comment and rating figures for a date range.
    /// </summary>
    /// <param name="fromUtc">Inclusive lower bound on the activity timestamps.</param>
    /// <param name="toUtc">Exclusive upper bound on the activity timestamps.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The summary; a zeroed instance when nothing happened in the range.</returns>
    Task<AnalyticsSummary> GetSummaryAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
