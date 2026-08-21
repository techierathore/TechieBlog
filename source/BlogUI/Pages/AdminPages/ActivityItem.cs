namespace BlogUI.Pages.AdminPages;

/// <summary>
/// A single entry in the admin dashboard's recent-activity feed.
/// </summary>
public class ActivityItem
{
    /// <summary>
    /// Activity category, e.g. "Post", "User", "Comment" or "Subscriber".
    /// Drives the icon chosen by <see cref="AdminDashboard.GetActivityIcon"/>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Short headline describing what happened.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Supporting detail, typically the affected item's title.
    /// </summary>
    public string Detail { get; set; } = string.Empty;

    /// <summary>
    /// When the activity occurred, in UTC.
    /// </summary>
    public DateTime Timestamp { get; set; }
}
