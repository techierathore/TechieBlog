using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Popular-post ranking and per-post engagement statistics.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-FN-035 (BRD-61) on top of the view rows written by
/// <c>PostViewTracker</c>, combining them with comment and rating counts.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The caller asks for a ranking window or a single post's statistics.</item>
///   <item>Arguments are clamped here rather than in SQL, so a caller cannot ask for an unbounded
///         result set or a negative window.</item>
///   <item>The repository runs the aggregate query and the result is returned as-is.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>IAnalyticsRepo</c>, <c>IPostViewRepo</c> and <c>ILogger</c>.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as <c>IAnalyticsService</c>.
/// Analytics decorate a page rather than being the page, so a query failure is logged and degrades
/// to an empty or zeroed value instead of throwing.</para>
/// </remarks>
public class AnalyticsSvc : IAnalyticsService
{
    private const int MaxResultCount = 100;

    private readonly IAnalyticsRepo analyticsRepo;
    private readonly IPostViewRepo postViewRepo;
    private readonly ILogger<AnalyticsSvc> logger;

    /// <summary>
    /// Initializes the analytics service.
    /// </summary>
    /// <param name="analyticsRepo">Ranking and engagement data access.</param>
    /// <param name="postViewRepo">Post-view count data access.</param>
    /// <param name="logger">Logger for query failures.</param>
    public AnalyticsSvc(
        IAnalyticsRepo analyticsRepo,
        IPostViewRepo postViewRepo,
        ILogger<AnalyticsSvc> logger)
    {
        this.analyticsRepo = analyticsRepo;
        this.postViewRepo = postViewRepo;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PopularPost>> GetPopularPostsAsync(
        int days, int maxCount, CancellationToken cancellationToken = default)
    {
        var windowDays = days < 1 ? 1 : days;
        var count = Math.Clamp(maxCount, 1, MaxResultCount);

        try
        {
            var sinceUtc = DateTime.UtcNow.AddDays(-windowDays);
            return await analyticsRepo
                .GetPopularPostsAsync(sinceUtc, count, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rank popular posts over {WindowDays} days", windowDays);
            return new List<PopularPost>();
        }
    }

    /// <inheritdoc />
    public async Task<PostEngagement?> GetPostEngagementAsync(
        long postId, CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
            return new PostEngagement();

        try
        {
            var engagement = await analyticsRepo
                .GetPostEngagementAsync(postId, cancellationToken).ConfigureAwait(false);
            return engagement ?? new PostEngagement { PostId = postId };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read engagement statistics for post {PostId}", postId);
            return new PostEngagement { PostId = postId };
        }
    }

    /// <inheritdoc />
    public async Task<PostViewCounts> GetPostViewCountsAsync(
        long postId, CancellationToken cancellationToken = default)
    {
        if (postId <= 0)
            return new PostViewCounts();

        try
        {
            return await postViewRepo.GetCountsAsync(postId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read view counts for post {PostId}", postId);
            return new PostViewCounts { PostId = postId };
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PostEngagement>> GetTopEngagementAsync(
        int maxCount, CancellationToken cancellationToken = default)
    {
        var count = Math.Clamp(maxCount, 1, MaxResultCount);

        try
        {
            return await analyticsRepo.GetTopEngagementAsync(count, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read top engagement statistics");
            return new List<PostEngagement>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PopularPost>> GetPopularPostsInRangeAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        var (rangeStart, rangeEnd) = OrderRange(fromUtc, toUtc);
        var count = Math.Clamp(maxCount, 1, MaxResultCount);

        try
        {
            return await analyticsRepo
                .GetPopularPostsInRangeAsync(rangeStart, rangeEnd, count, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to rank popular posts between {RangeStart} and {RangeEnd}",
                rangeStart, rangeEnd);
            return new List<PopularPost>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ViewTrendPoint>> GetViewTrendAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var (rangeStart, rangeEnd) = OrderRange(fromUtc, toUtc);

        try
        {
            var recorded = await analyticsRepo
                .GetViewTrendAsync(rangeStart, rangeEnd, cancellationToken).ConfigureAwait(false);
            return FillQuietDays(recorded, rangeStart, rangeEnd);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read the view trend between {RangeStart} and {RangeEnd}",
                rangeStart, rangeEnd);
            return new List<ViewTrendPoint>();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CategoryEngagement>> GetCategoryEngagementAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default)
    {
        var (rangeStart, rangeEnd) = OrderRange(fromUtc, toUtc);
        var count = Math.Clamp(maxCount, 1, MaxResultCount);

        try
        {
            return await analyticsRepo
                .GetCategoryEngagementAsync(rangeStart, rangeEnd, count, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read category engagement between {RangeStart} and {RangeEnd}",
                rangeStart, rangeEnd);
            return new List<CategoryEngagement>();
        }
    }

    /// <inheritdoc />
    public async Task<AnalyticsSummary> GetSummaryAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
    {
        var (rangeStart, rangeEnd) = OrderRange(fromUtc, toUtc);

        try
        {
            return await analyticsRepo
                .GetSummaryAsync(rangeStart, rangeEnd, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to summarise analytics between {RangeStart} and {RangeEnd}",
                rangeStart, rangeEnd);
            return new AnalyticsSummary();
        }
    }

    /// <summary>
    /// Puts a caller-supplied range the right way round and snaps it to whole days.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A picker lets an admin choose an end date before a start date;
    /// swapping is friendlier than returning nothing. The upper bound is exclusive and lands on the
    /// day after the chosen end date, so "1 Aug to 7 Aug" includes all of 7 August.</para>
    /// <para><b>Flow:</b> swap if inverted → truncate the start → advance the end to the next day.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="fromUtc">Caller's start of range.</param>
    /// <param name="toUtc">Caller's end of range.</param>
    /// <returns>An ordered, day-aligned half-open range.</returns>
    private static (DateTime RangeStart, DateTime RangeEnd) OrderRange(DateTime fromUtc, DateTime toUtc)
    {
        var start = fromUtc <= toUtc ? fromUtc : toUtc;
        var end = fromUtc <= toUtc ? toUtc : fromUtc;
        return (start.Date, end.Date.AddDays(1));
    }

    /// <summary>
    /// Expands a sparse per-day aggregate into one point for every day in the range.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> SQL only returns days that have rows. A chart plotted from that
    /// sparse set would silently drop quiet days and overstate the trend.</para>
    ///
    /// <para><b>Duplicate days — REQ-FN-056.</b> This used to index the recorded points with
    /// <c>ToDictionary(point =&gt; point.Day.Date)</c>, which throws the instant two rows truncate to
    /// the same calendar day. The throw happened inside the caller's read <c>try</c>, so it was
    /// swallowed and the ENTIRE trend degraded to an empty list — the chart blanked with only a
    /// misleading "failed to read" line to go on. A <c>ToLookup</c> cannot throw on a repeated key, so
    /// one bad row can no longer take the whole series down. The first row for a repeated day is the
    /// one plotted: the rows are not merged, because <c>UniqueViews</c> is distinct <i>within</i> a
    /// day and summing two partial rows would over-count it (see <see cref="ViewTrendPoint"/>).
    /// Collapsing rows does lose readership, so it is never silent — a repeated day is a defect in the
    /// aggregate query and is reported as a warning naming the range and the number of days affected.</para>
    ///
    /// <para><b>Flow:</b> index the recorded days → count any day carrying more than one row → walk
    /// the range → emit the first recorded point for the day or a zeroed one → warn if any day
    /// repeated.</para>
    /// <para><b>Side Effects:</b> Writes a warning when the recorded set repeats a day; otherwise
    /// none.</para>
    /// </remarks>
    /// <param name="recorded">Days that actually had traffic.</param>
    /// <param name="rangeStart">Inclusive first day of the range.</param>
    /// <param name="rangeEnd">Exclusive last day of the range.</param>
    /// <returns>One point per day, oldest first.</returns>
    private IReadOnlyList<ViewTrendPoint> FillQuietDays(
        IReadOnlyList<ViewTrendPoint> recorded, DateTime rangeStart, DateTime rangeEnd)
    {
        var byDay = recorded.ToLookup(point => point.Day.Date);
        var repeatedDayCount = byDay.Count(dayRows => dayRows.Skip(1).Any());
        var points = new List<ViewTrendPoint>();

        for (var day = rangeStart.Date; day < rangeEnd; day = day.AddDays(1))
        {
            points.Add(byDay[day].FirstOrDefault() ?? new ViewTrendPoint { Day = day });
        }

        if (repeatedDayCount > 0)
        {
            logger.LogWarning(
                "The view trend between {RangeStart} and {RangeEnd} carried more than one aggregate row "
                + "for {RepeatedDayCount} day(s); the first row for each of those days was plotted and "
                + "the rest discarded",
                rangeStart, rangeEnd, repeatedDayCount);
        }

        return points;
    }
}
