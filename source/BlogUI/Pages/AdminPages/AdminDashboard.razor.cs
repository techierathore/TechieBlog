/// <summary>
/// Code-behind for AdminDashboard component.
/// Provides the main administrative dashboard functionality.
/// </summary>
using Microsoft.AspNetCore.Components;
using BlogModels;

namespace BlogUI.Pages.AdminPages
{
    /// <summary>
    /// Partial class containing state and logic for AdminDashboard.razor.
    /// </summary>
    public partial class AdminDashboard : ComponentBase
    {
        [Inject]
        public BlogEngine.Services.BlogSvc BlogService { get; set; }

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

        protected override async Task OnInitializedAsync()
        {
            await LoadDashboardData();
        }

        private Task LoadDashboardData()
        {
            try
            {
                // Load posts using service - get all posts for admin
                var posts = BlogService.GetAllPosts(0, true)?.ToList() ?? new List<BlogPost>();
                TotalPosts = posts.Count;
                DraftPosts = posts.Count(p => !p.Published && !p.IsScheduled);
                ScheduledPosts = posts.Count(p => p.IsScheduled);

                var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
                PostsThisMonth = posts.Count(p => p.CreatedOn >= startOfMonth);

                // Popular posts (by most recent published for now)
                PopularPosts = posts
                    .Where(p => p.Published)
                    .OrderByDescending(p => p.PublishedOn)
                    .Take(5)
                    .Select(p => new PopularPost { Title = p.Title ?? "Untitled", Views = 0 })
                    .ToList();

                // Placeholder values for users/comments (these would come from auth service)
                TotalUsers = 1;
                UsersThisMonth = 0;
                TotalSubscribers = 1;
                SubscribersThisMonth = 0;
                TotalComments = 0;
                PendingComments = 0;

                // Build recent activity from posts
                BuildRecentActivity(posts);
            }
            catch (Exception)
            {
                // On error, show default values
                TotalPosts = 0;
                TotalUsers = 0;
                TotalComments = 0;
                TotalSubscribers = 0;
            }

            return Task.CompletedTask;
        }

        private void BuildRecentActivity(List<BlogPost> posts)
        {
            RecentActivities = new List<ActivityItem>();

            // Add recent published posts
            foreach (var post in posts.Where(p => p.Published && p.PublishedOn.HasValue)
                                       .OrderByDescending(p => p.PublishedOn)
                                       .Take(5))
            {
                RecentActivities.Add(new ActivityItem
                {
                    Type = "Post",
                    Title = "New post published",
                    Detail = post.Title ?? "Untitled",
                    Timestamp = post.PublishedOn ?? DateTime.UtcNow
                });
            }

            // Sort by timestamp
            RecentActivities = RecentActivities.OrderByDescending(a => a.Timestamp).Take(5).ToList();
        }

        public string GetActivityIcon(string type)
        {
            return type.ToLower() switch
            {
                "post" => "\u2713",       // Checkmark
                "user" => "\uD83D\uDC64", // Person silhouette
                "comment" => "\uD83D\uDCAC", // Speech bubble
                "subscriber" => "\u2709",  // Envelope
                _ => "\u2022"              // Bullet
            };
        }

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

    /// <summary>
    /// Represents a recent activity item for the dashboard.
    /// </summary>
    public class ActivityItem
    {
        public string Type { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// Represents a popular post for the dashboard.
    /// </summary>
    public class PopularPost
    {
        public string Title { get; set; } = string.Empty;
        public int Views { get; set; }
    }
}
