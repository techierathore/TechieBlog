namespace BlogModels;

/// <summary>
/// Aggregate counts rendered on the admin dashboard tiles.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One value carrying every headline number the dashboard shows, so the
/// screen makes a single service call instead of hardcoding constants (BRD-62).</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The dashboard calls <c>IDashboardService.GetAdminCountsAsync</c>.</item>
///   <item><c>IAdminCountsRepo</c> runs one query of scalar sub-selects across the content,
///         identity, engagement and subscriber tables.</item>
///   <item>Every property is populated; an empty database yields zeroes, never null.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> The original five properties are unchanged so the legacy
/// <c>CommentSvc.GetAdminCounts</c> path keeps working; the properties it does not populate simply
/// stay zero.</para>
/// </remarks>
public class AdminCounts
{
    /// <summary>
    /// All comments, approved or not.
    /// </summary>
    public int CommentCount { get; set; }

    /// <summary>
    /// Comments awaiting moderation.
    /// </summary>
    public int UnAppComments { get; set; }

    /// <summary>
    /// Blog posts that have not been soft-deleted.
    /// </summary>
    public int BlogCount { get; set; }

    /// <summary>
    /// Tags defined on the site.
    /// </summary>
    public int TagCount { get; set; }

    /// <summary>
    /// Registered users across all roles.
    /// </summary>
    public int UserCount { get; set; }

    /// <summary>
    /// Uploaded images.
    /// </summary>
    public int ImageCount { get; set; }

    /// <summary>
    /// Posts currently published and visible to readers.
    /// </summary>
    public int PublishedPostCount { get; set; }

    /// <summary>
    /// Posts still unpublished (drafts and scheduled).
    /// </summary>
    public int DraftPostCount { get; set; }

    /// <summary>
    /// Categories defined on the site.
    /// </summary>
    public int CategoryCount { get; set; }

    /// <summary>
    /// Newsletter subscribers, active or not.
    /// </summary>
    public int SubscriberCount { get; set; }

    /// <summary>
    /// Subscribers who are confirmed and have not unsubscribed.
    /// </summary>
    public int ActiveSubscriberCount { get; set; }

    /// <summary>
    /// Newsletter issues created, in any status.
    /// </summary>
    public int NewsletterCount { get; set; }

    /// <summary>
    /// Newsletter issues that have been dispatched.
    /// </summary>
    public int SentNewsletterCount { get; set; }

    /// <summary>
    /// Recorded post views across the whole site.
    /// </summary>
    public int TotalPostViews { get; set; }

    /// <summary>
    /// Users who registered on or after the first day of the current month.
    /// </summary>
    /// <remarks>
    /// Backs the "+N this month" sub-label on the users tile, which previously rendered a
    /// hardcoded zero (REQ-UI-019).
    /// </remarks>
    public int NewUsersThisMonth { get; set; }

    /// <summary>
    /// Subscribers who signed up on or after the first day of the current month.
    /// </summary>
    /// <remarks>
    /// Backs the "+N this month" sub-label on the subscribers tile, which previously rendered a
    /// hardcoded zero (REQ-UI-019).
    /// </remarks>
    public int NewSubscribersThisMonth { get; set; }
}
