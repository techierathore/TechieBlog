/// <summary>
/// Represents a user's favorite/bookmark of a blog post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Stores the relationship between users and their favorited posts.</para>
/// <para><b>Usage:</b> Used by UserFavoriteRepo for data access and FavoriteSvc for business logic.</para>
/// </remarks>
namespace BlogModels;

public class UserFavorite
{
    /// <summary>
    /// Unique identifier for the favorite record.
    /// </summary>
    public long FavoriteId { get; set; }

    /// <summary>
    /// Foreign key to the favorited post.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// Foreign key to the user who favorited the post.
    /// </summary>
    public long UserId { get; set; }

    /// <summary>
    /// Timestamp when the favorite was created.
    /// </summary>
    public DateTime CreatedOn { get; set; }

    /// <summary>
    /// Navigation property for the associated blog post.
    /// Populated when loading favorites with post details.
    /// </summary>
    public BlogPost? Post { get; set; }
}
