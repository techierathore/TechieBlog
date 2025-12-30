/// <summary>
/// Represents a user's star rating for a blog post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stores user-post-rating relationships for the star rating system.</para>
/// <para><b>Usage:</b> Used by PostRatingRepo for data access and RatingSvc for business logic.</para>
/// <para><b>Story:</b> FIX-013 - Star Ratings Implementation (Epic 4, FR15-16)</para>
/// </remarks>
namespace BlogModels;

public class PostRating
{
    /// <summary>
    /// Unique identifier for the rating.
    /// </summary>
    public long RatingId { get; set; }

    /// <summary>
    /// ID of the post being rated.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// ID of the user who submitted the rating.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Rating value (1-5 stars).
    /// </summary>
    public int Rating { get; set; }

    /// <summary>
    /// Date when the rating was first created.
    /// </summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Date when the rating was last updated (null if never updated).
    /// </summary>
    public DateTime? UpdatedOn { get; set; }
}

/// <summary>
/// Represents aggregate rating statistics for a post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a DTO for returning average rating and count together.</para>
/// </remarks>
public class PostRatingStats
{
    /// <summary>
    /// Average rating for the post (0 if no ratings).
    /// </summary>
    public double AverageRating { get; set; }

    /// <summary>
    /// Total number of ratings for the post.
    /// </summary>
    public int RatingCount { get; set; }

    /// <summary>
    /// The current user's rating for this post (null if not rated).
    /// </summary>
    public int? UserRating { get; set; }
}
