namespace BlogModels;

/// <summary>
/// One calendar day of site-wide readership, used to plot the analytics view trend.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the "views trend" panel of BRD-60/BRD-61. The ranking and engagement
/// contracts answer "which post", never "which day", so the trend needs its own shape.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>IAnalyticsRepo.GetViewTrendAsync</c> groups <c>PostViews</c> by day inside the
///         requested range and returns one instance per day that has traffic.</item>
///   <item><c>IAnalyticsService.GetViewTrendAsync</c> fills the gaps, so a day with no traffic still
///         appears as a zero rather than silently collapsing the x-axis.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Consumed by the admin analytics dashboard (REQ-UI-044). Only published,
/// non-deleted posts contribute, so a draft's traffic never appears in the trend.</para>
/// </remarks>
public class ViewTrendPoint
{
    /// <summary>
    /// The day being described, truncated to midnight UTC. The day boundary is UTC, not the
    /// viewer's local midnight, so a reader in a distant time zone sees their evening traffic
    /// attributed to the following day.
    /// </summary>
    public DateTime Day { get; set; }

    /// <summary>
    /// Rows in <c>PostViews</c> across all posts on that day. Zero is a real, meaningful value here
    /// — the service fills gaps precisely so a quiet day plots as zero instead of vanishing and
    /// compressing the axis.
    /// </summary>
    public int TotalViews { get; set; }

    /// <summary>
    /// Distinct visitors on that day. Distinct <i>within the day only</i>, so these values must
    /// never be summed across points to obtain a period total — the same visitor returning on three
    /// days contributes three times.
    /// </summary>
    public int UniqueViews { get; set; }

    /// <summary>
    /// Short axis label for the day, for example "Aug 07".
    /// </summary>
    /// <remarks>
    /// Derived rather than stored so the chart and any accessible text fallback always agree.
    /// </remarks>
    public string Label => Day.ToString("MMM dd");
}
