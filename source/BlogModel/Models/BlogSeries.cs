/// <summary>
/// Represents a series/collection of related blog posts.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Groups related posts into multi-part series for sequential reading.</para>
/// <para><b>Usage:</b> Used by BlogSeriesRepo for data access and SeriesSvc for business logic.</para>
/// </remarks>
namespace BlogModels;

public class BlogSeries
{
    /// <summary>
    /// Unique identifier for the series.
    /// </summary>
    public long SeriesId { get; set; }

    /// <summary>
    /// Display name of the series.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL-friendly identifier auto-generated from name.
    /// Used for SEO-friendly URLs like /series/getting-started-with-dotnet.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Description of the series shown on the series page.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Status of the series: "In Progress" or "Complete".
    /// </summary>
    public string Status { get; set; } = "In Progress";

    /// <summary>
    /// Foreign key to BlogUser - the series author.
    /// </summary>
    public long AuthorId { get; set; }

    /// <summary>
    /// Timestamp when series was first created.
    /// </summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Timestamp of last modification.
    /// </summary>
    public DateTime UpdatedOn { get; set; }

    /// <summary>
    /// Author's full name (computed from BlogUser).
    /// </summary>
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// Number of posts in this series (computed).
    /// </summary>
    public int PostCount { get; set; }

    /// <summary>
    /// Collection of posts in this series (populated by service).
    /// </summary>
    public List<BlogPost> Posts { get; set; } = new();

    /// <summary>
    /// Returns true if series is marked as complete.
    /// </summary>
    public bool IsComplete => Status == "Complete";
}

/// <summary>
/// Navigation helper for series posts (prev/next links).
/// </summary>
public class SeriesNavigation
{
    /// <summary>
    /// Name of the series.
    /// </summary>
    public string SeriesName { get; set; } = string.Empty;

    /// <summary>
    /// URL slug of the series.
    /// </summary>
    public string SeriesSlug { get; set; } = string.Empty;

    /// <summary>
    /// Current part number (1-based).
    /// </summary>
    public int CurrentPart { get; set; }

    /// <summary>
    /// Total number of published parts.
    /// </summary>
    public int TotalParts { get; set; }

    /// <summary>
    /// Previous post in the series (null if first).
    /// </summary>
    public BlogPost PreviousPost { get; set; }

    /// <summary>
    /// Next post in the series (null if last).
    /// </summary>
    public BlogPost NextPost { get; set; }
}
