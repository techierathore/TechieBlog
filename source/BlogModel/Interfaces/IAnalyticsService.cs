namespace BlogModels.Interfaces;

/// <summary>
/// Popular-post ranking and per-post engagement statistics (BRD-61).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published read-side contract for analytics. Reads never fail a page —
/// a query problem yields an empty list or a zeroed value after being logged, so a dashboard tile
/// degrades rather than the whole screen.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A caller asks for popular posts over a window, or one post's engagement.</item>
///   <item>The service normalises the arguments (window and count are clamped to sane bounds).</item>
///   <item><c>IAnalyticsRepo</c> / <c>IPostViewRepo</c> execute the aggregate queries.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>IAnalyticsRepo</c>, <c>IPostViewRepo</c>.</para>
///
/// <para><b>Usage:</b> Implemented by <c>BlogEngine.Services.AnalyticsSvc</c>, registered transient.</para>
/// </remarks>
public interface IAnalyticsService
{
    /// <summary>
    /// Ranks published posts by views recorded in the last <paramref name="days"/> days.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ranking is by total views in the window, then unique views,
    /// then comment count, then most recently published.</para>
    /// <para><b>Flow:</b> clamp arguments → compute the window start → aggregate and rank.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="days">Length of the ranking window in days; values below one are clamped to one.</param>
    /// <param name="maxCount">Maximum posts to return; clamped to 1..100.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Ranked posts, most viewed first; empty when nothing qualifies.</returns>
    Task<IReadOnlyList<PopularPost>> GetPopularPostsAsync(
        int days, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads full engagement statistics for a single post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Combines views, comments and ratings for the post.</para>
    /// <para><b>Flow:</b> validate id → single aggregate query → return statistics.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="postId">The post to describe.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The statistics; a zeroed value carrying the id when the post has no activity.</returns>
    Task<PostEngagement?> GetPostEngagementAsync(long postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads total and unique view counts for a single post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Total views are rows; unique views are distinct visitor hashes.</para>
    /// <para><b>Flow:</b> validate id → count query → return counts.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="postId">The post to count.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The counts; zeroes when the post has never been viewed.</returns>
    Task<PostViewCounts> GetPostViewCountsAsync(long postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads engagement statistics for the busiest published posts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same statistics as <see cref="GetPostEngagementAsync"/>, ordered
    /// by total views over all time.</para>
    /// <para><b>Flow:</b> clamp count → aggregate query → return rows.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="maxCount">Maximum posts to return; clamped to 1..100.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Statistics rows, most viewed first.</returns>
    Task<IReadOnlyList<PostEngagement>> GetTopEngagementAsync(
        int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ranks published posts by views recorded inside an explicit date range.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same ranking rule as the rolling-window overload, but bounded at
    /// both ends so an admin can ask about a past period rather than only "the last N days".</para>
    /// <para><b>Flow:</b> order the range → clamp the count → aggregate and rank.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="fromUtc">Start of the range; swapped with <paramref name="toUtc"/> if inverted.</param>
    /// <param name="toUtc">End of the range, exclusive.</param>
    /// <param name="maxCount">Maximum posts to return; clamped to 1..100.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Ranked posts, most viewed first; empty when nothing qualifies.</returns>
    Task<IReadOnlyList<PopularPost>> GetPopularPostsInRangeAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads site-wide daily readership across a date range, with quiet days included as zeroes.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A day with no traffic is a real data point — omitting it would
    /// compress the x-axis and make a quiet week look busy.</para>
    /// <para><b>Flow:</b> order the range → aggregate per day → fill the missing days with zeroes.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="fromUtc">Start of the range.</param>
    /// <param name="toUtc">End of the range, exclusive.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>One point per day in the range, oldest first.</returns>
    Task<IReadOnlyList<ViewTrendPoint>> GetViewTrendAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the share of readership each category attracted inside a date range.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Views are attributed to the category of the viewed post;
    /// uncategorised posts are reported together rather than dropped.</para>
    /// <para><b>Flow:</b> order the range → clamp the count → aggregate and rank.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="fromUtc">Start of the range.</param>
    /// <param name="toUtc">End of the range, exclusive.</param>
    /// <param name="maxCount">Maximum categories to return; clamped to 1..100.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Categories ranked by views, busiest first; empty when the range is quiet.</returns>
    Task<IReadOnlyList<CategoryEngagement>> GetCategoryEngagementAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the headline view, comment and rating figures for a date range.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every figure is scoped to the range, so the dashboard's tiles and
    /// its charts always describe the same period.</para>
    /// <para><b>Flow:</b> order the range → single aggregate query → return the summary.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="fromUtc">Start of the range.</param>
    /// <param name="toUtc">End of the range, exclusive.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The summary; a zeroed instance when nothing happened in the range.</returns>
    Task<AnalyticsSummary> GetSummaryAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
