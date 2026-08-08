namespace BlogModels;

/// <summary>
/// Total and unique view counts for one blog post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Makes the two readership numbers required by BRD-60 explicit and gives
/// them one agreed definition across the engine.</para>
///
/// <para><b>Definitions:</b></para>
/// <list type="bullet">
///   <item><b>Total views</b> — the number of rows in <c>PostViews</c> for the post. The tracker
///         writes at most one row per visitor per post per de-duplication window, so a page
///         refresh inside a single reading session does not inflate the number.</item>
///   <item><b>Unique views</b> — the number of distinct <c>VisitorHash</c> values for the post,
///         i.e. distinct visitors over all time.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Returned by <c>IAnalyticsService.GetPostViewCountsAsync</c>; also embedded
/// in <see cref="PostEngagement"/>.</para>
/// </remarks>
public class PostViewCounts
{
    /// <summary>
    /// The post these counts describe. A post with no traffic still yields an instance with this
    /// set and both counts zero, rather than null.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// Rows in <c>PostViews</c> for the post, all time. Session-like rather than hit-like, per the
    /// definition above.
    /// </summary>
    public int TotalViews { get; set; }

    /// <summary>
    /// Distinct <c>VisitorHash</c> values for the post, all time. Bounded above by
    /// <see cref="TotalViews"/>; the two are equal when every visitor read the post exactly once.
    /// Because the hash is derived from IP and user agent it approximates people rather than
    /// identifying them — do not present it as an exact headcount.
    /// </summary>
    public int UniqueViews { get; set; }
}
