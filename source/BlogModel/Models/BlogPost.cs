/// <summary>
/// Represents a blog post entity in the TechieBlog application.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Core content entity containing post title, content, metadata, and status.</para>
/// <para><b>Usage:</b> Used by BlogPostRepo for data access and BlogSvc for business logic.</para>
/// </remarks>
namespace BlogModels;

public class BlogPost
{
    /// <summary>
    /// Unique identifier for the blog post.
    /// </summary>
    public long PostID { get; set; }

    /// <summary>
    /// Display title of the blog post.
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// URL-friendly identifier auto-generated from title.
    /// Used for SEO-friendly URLs like /blog/my-post-title.
    /// </summary>
    public string Slug { get; set; }

    /// <summary>
    /// UI-specific page title (may differ from Title).
    /// </summary>
    public string UIPageTitle { get; set; }

    /// <summary>
    /// Short summary/excerpt shown in post listings.
    /// </summary>
    public string Abstract { get; set; }

    /// <summary>
    /// Full post content in Markdown format.
    /// </summary>
    public string PostContent { get; set; }

    /// <summary>
    /// Timestamp when post was first created.
    /// </summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Timestamp of last modification.
    /// </summary>
    public DateTime UpdatedOn { get; set; }

    /// <summary>
    /// Foreign key to BlogUser - the post author.
    /// </summary>
    public long UserID { get; set; }

    /// <summary>
    /// Comma-separated tag names for display.
    /// </summary>
    public string Tags { get; set; }

    /// <summary>
    /// Foreign key to Category.
    /// </summary>
    public int CategoryId { get; set; }

    /// <summary>
    /// Author's full name (computed from BlogUser).
    /// </summary>
    public string BlogWriter { get; set; }

    /// <summary>
    /// Path to featured/hero image for the post.
    /// </summary>
    public string FeaturedImage { get; set; }

    /// <summary>
    /// Publication status: false = draft, true = published.
    /// </summary>
    public bool Published { get; set; }

    /// <summary>
    /// Timestamp when post was first published.
    /// </summary>
    public DateTime? PublishedOn { get; set; }

    /// <summary>
    /// UTC timestamp when post is scheduled to publish (null if not scheduled).
    /// </summary>
    public DateTime? ScheduledPublishOn { get; set; }

    /// <summary>
    /// Returns true if post is scheduled for future publication.
    /// </summary>
    public bool IsScheduled => !Published && ScheduledPublishOn.HasValue && ScheduledPublishOn > DateTime.UtcNow;

    /// <summary>
    /// Returns the current status of the post: Published, Scheduled, or Draft.
    /// </summary>
    public string Status => Published ? "Published" : IsScheduled ? "Scheduled" : "Draft";

    /// <summary>
    /// Soft delete flag. True means post is deleted but retained in database.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Timestamp when post was soft deleted.
    /// </summary>
    public DateTime? DeletedOn { get; set; }

    /// <summary>
    /// Count of comments on this post (computed).
    /// </summary>
    public long CommentCount { get; set; }

    /// <summary>
    /// Total blog count (for dashboard statistics).
    /// </summary>
    public int BlogCount { get; set; }

    /// <summary>
    /// Series this post belongs to (null if standalone).
    /// </summary>
    public long? SeriesId { get; set; }

    /// <summary>
    /// Part number within the series (1-based).
    /// </summary>
    public int? SeriesPartNumber { get; set; }

    /// <summary>
    /// Series name (populated when part of a series).
    /// </summary>
    public string SeriesName { get; set; }

    /// <summary>
    /// Series slug (populated when part of a series).
    /// </summary>
    public string SeriesSlug { get; set; }
}