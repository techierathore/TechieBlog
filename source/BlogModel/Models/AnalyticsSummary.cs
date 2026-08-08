namespace BlogModels;

/// <summary>
/// The headline figures shown as stat tiles at the top of the analytics dashboard.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Answers BRD-60/BRD-61's "how is the blog doing" question in one round trip,
/// so four tiles do not cost four queries.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>IAnalyticsRepo.GetSummaryAsync</c> aggregates views, comments and ratings for the
///         requested range in a single statement.</item>
///   <item><c>IAnalyticsService.GetSummaryAsync</c> normalises the range and degrades a query
///         failure to a zeroed instance, so a tile shows zero rather than breaking the page.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Consumed by the admin analytics dashboard (REQ-UI-044). Every figure is
/// scoped to the requested range, so changing the range changes every tile.</para>
/// </remarks>
public class AnalyticsSummary
{
    /// <summary>
    /// Recorded views inside the range.
    /// </summary>
    public int TotalViews { get; set; }

    /// <summary>
    /// Distinct visitors inside the range, counted across the whole site.
    /// </summary>
    public int UniqueViews { get; set; }

    /// <summary>
    /// Comments posted inside the range.
    /// </summary>
    public int CommentCount { get; set; }

    /// <summary>
    /// Ratings submitted inside the range.
    /// </summary>
    public int RatingCount { get; set; }

    /// <summary>
    /// Mean rating submitted inside the range, or zero when nothing was rated.
    /// </summary>
    public double AverageRating { get; set; }

    /// <summary>
    /// Published posts that were viewed at least once inside the range.
    /// </summary>
    public int PostsWithTraffic { get; set; }
}
