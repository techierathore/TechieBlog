using BlogModels;
using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Code-behind for the admin dashboard: headline counters, attention items, recent activity and
/// the popular-post ranking.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Completes REQ-UI-019 / REQ-FN-036 (BRD-62). Every tile now reports a real
/// number. The page previously hardcoded <c>TotalUsers = 1</c>, <c>TotalSubscribers = 1</c>,
/// <c>TotalComments = 0</c> and <c>PendingComments = 0</c>, and listed recent posts with a zero view
/// count in place of a genuine popular ranking.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>IDashboardService</c> supplies every aggregate count in one query — users, comments,
///         pending comments, subscribers and posts.</item>
///   <item><c>BlogSvc</c> supplies the post list, which is the only source that separates drafts
///         from scheduled posts and carries the timestamps behind recent activity.</item>
///   <item><c>IAnalyticsService</c> ranks the popular posts by real recorded views.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>IDashboardService</c>, <c>IAnalyticsService</c> and
/// <c>BlogEngine.Services.BlogSvc</c>. All three are engine-layer services — the page never reaches
/// a repository directly.</para>
///
/// <para><b>Usage:</b> Rendered at <c>/AdminDashboard</c> and <c>/admin</c> behind the
/// <c>EditorOrAbove</c> policy.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> Both service calls now carry a cancellation token
/// tied to the component's lifetime, so an admin who navigates away before the counts query returns
/// releases the connection instead of leaving it running for a page that no longer exists.</para>
/// </remarks>
public partial class AdminDashboard : ComponentBase, IDisposable
{
    /// <summary>
    /// Ranking window, in days, behind the popular-posts panel.
    /// </summary>
    private const int PopularPostWindowDays = 30;

    /// <summary>
    /// Number of posts listed in the popular-posts panel.
    /// </summary>
    private const int PopularPostCount = 5;

    /// <summary>
    /// Number of entries listed in the recent-activity panel.
    /// </summary>
    private const int RecentActivityCount = 5;

    /// <summary>
    /// Post read service supplying the draft / scheduled split and recent-activity timestamps.
    /// </summary>
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; } = default!;

    /// <summary>
    /// Aggregate-count service behind every headline tile.
    /// </summary>
    /// <remarks>
    /// The tiles used to render constants; this is the single call that makes them real
    /// (REQ-FN-036).
    /// </remarks>
    [Inject]
    public IDashboardService DashboardService { get; set; } = default!;

    /// <summary>
    /// Analytics read service supplying the real popular-post ranking.
    /// </summary>
    /// <remarks>
    /// The panel used to fabricate rows with a hardcoded zero view count; it now reports the
    /// same ranking the analytics dashboard shows (REQ-UI-044).
    /// </remarks>
    [Inject]
    public IAnalyticsService AnalyticsService { get; set; } = default!;

    // Stats
    public int TotalPosts { get; set; }
    public int TotalUsers { get; set; }
    public int TotalComments { get; set; }
    public int TotalSubscribers { get; set; }
    public int PostsThisMonth { get; set; }
    public int UsersThisMonth { get; set; }
    public int SubscribersThisMonth { get; set; }

    // Attention Items
    public int PendingComments { get; set; }
    public int ScheduledPosts { get; set; }
    public int DraftPosts { get; set; }

    // Lists
    public List<ActivityItem> RecentActivities { get; set; } = new();
    public List<PopularPost> PopularPosts { get; set; } = new();

    /// <summary>
    /// Cancels the outstanding dashboard reads when the circuit tears the component down.
    /// </summary>
    private readonly CancellationTokenSource componentCancellation = new();

    /// <summary>
    /// Loads every panel on first render.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts, the post breakdown and the popular ranking come from
    /// three independent services, so a failure in one leaves the others rendering.</para>
    /// <para><b>Flow:</b> read the aggregate counts, then the post breakdown, then the ranking.</para>
    /// <para><b>Side Effects:</b> None beyond populating component state.</para>
    /// </remarks>
    /// <returns>A task that completes once every panel has its data.</returns>
    protected override async Task OnInitializedAsync()
    {
        var cancellationToken = componentCancellation.Token;

        ApplyCounts(await DashboardService.GetAdminCountsAsync(cancellationToken));
        LoadPostBreakdown();
        PopularPosts = (await AnalyticsService
            .GetPopularPostsAsync(PopularPostWindowDays, PopularPostCount, cancellationToken)).ToList();
    }

    /// <summary>
    /// Cancels the outstanding reads when the circuit tears the component down.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An admin who navigates away mid-load leaves the counts and ranking
    /// queries with no reader; cancelling returns those connections to the pool at once.</para>
    /// <para><b>Flow:</b> cancel → dispose.</para>
    /// <para><b>Side Effects:</b> Faults the in-flight reads with cancellation; both services absorb
    /// it and return zeroed values that are never rendered.</para>
    /// </remarks>
    public void Dispose()
    {
        componentCancellation.Cancel();
        componentCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Copies the aggregate counts onto the tile properties.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The subscribers tile is unqualified, so it reports every
    /// subscriber row rather than only the confirmed ones; the confirmed subset is available as
    /// <c>ActiveSubscriberCount</c> should the tile ever be split.</para>
    /// <para><b>Flow:</b> straight projection — the service has already absorbed any query failure
    /// and returns zeroes rather than throwing.</para>
    /// <para><b>Side Effects:</b> Sets component state only.</para>
    /// </remarks>
    /// <param name="counts">Counts read from the engine layer.</param>
    private void ApplyCounts(AdminCounts counts)
    {
        TotalPosts = counts.BlogCount;
        TotalUsers = counts.UserCount;
        UsersThisMonth = counts.NewUsersThisMonth;
        TotalComments = counts.CommentCount;
        PendingComments = counts.UnAppComments;
        TotalSubscribers = counts.SubscriberCount;
        SubscribersThisMonth = counts.NewSubscribersThisMonth;
    }

    /// <summary>
    /// Derives the draft / scheduled split, the posts-this-month figure and recent activity.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The aggregate query counts every unpublished post as a draft;
    /// only the post list distinguishes a scheduled post from a true draft, so that split is
    /// computed here.</para>
    /// <para><b>Flow:</b> read all posts as an administrator, partition them, then build activity.</para>
    /// <para><b>Side Effects:</b> Sets component state only; a read failure leaves the breakdown at
    /// zero without disturbing the tiles already populated from the counts service.</para>
    /// </remarks>
    private void LoadPostBreakdown()
    {
        var posts = BlogService.GetAllPosts(0, true)?.ToList() ?? new List<BlogPost>();
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        DraftPosts = posts.Count(p => !p.Published && !p.IsScheduled);
        ScheduledPosts = posts.Count(p => p.IsScheduled);
        PostsThisMonth = posts.Count(p => p.CreatedOn >= startOfMonth);

        BuildRecentActivity(posts);
    }

    /// <summary>
    /// Builds the recent-activity feed from the most recently published posts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only published posts with a publication timestamp are activity;
    /// the newest entries win.</para>
    /// <para><b>Flow:</b> project published posts onto activity items, order by timestamp, take the
    /// panel's row budget.</para>
    /// <para><b>Side Effects:</b> Replaces <see cref="RecentActivities"/>.</para>
    /// </remarks>
    /// <param name="posts">Every post visible to an administrator.</param>
    private void BuildRecentActivity(List<BlogPost> posts)
    {
        RecentActivities = posts
            .Where(p => p.Published && p.PublishedOn.HasValue)
            .OrderByDescending(p => p.PublishedOn)
            .Take(RecentActivityCount)
            .Select(p => new ActivityItem
            {
                Type = "Post",
                Title = "New post published",
                Detail = p.Title ?? "Untitled",
                Timestamp = p.PublishedOn ?? DateTime.UtcNow
            })
            .ToList();
    }

    /// <summary>
    /// Maps an activity type onto the Lucide icon that represents it.
    /// </summary>
    /// <remarks>
    /// The UI design spec forbids emoji and text glyphs as icons, so this returns
    /// Lucide names rendered through &lt;LucideIcon&gt;.
    /// </remarks>
    /// <param name="type">Activity type, e.g. "Post" or "Comment".</param>
    /// <returns>A Lucide icon name in kebab-case.</returns>
    public string GetActivityIcon(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "post" => "file-text",
            "user" => "user",
            "comment" => "message-square",
            "subscriber" => "mail",
            _ => "circle"
        };
    }

    /// <summary>
    /// Renders a timestamp as a short relative age for the activity feed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Minutes below an hour, hours below a day, days below a week,
    /// weeks below a month, then an absolute date.</para>
    /// <para><b>Flow:</b> subtract from the current UTC instant and pick the coarsest unit that
    /// still reads naturally.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="timestamp">The UTC instant the activity happened.</param>
    /// <returns>A short relative description such as "3h ago".</returns>
    public string GetTimeAgo(DateTime timestamp)
    {
        var diff = DateTime.UtcNow - timestamp;

        if (diff.TotalMinutes < 1)
            return "Just now";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays}d ago";
        if (diff.TotalDays < 30)
            return $"{(int)(diff.TotalDays / 7)}w ago";

        return timestamp.ToString("MMM dd");
    }
}
