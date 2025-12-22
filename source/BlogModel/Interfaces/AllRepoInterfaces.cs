using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;
public interface IBlogUserRepo : IGenericRepository<AppUser>
{
    AppUser GetLoginUser(string aLoginEmail, string aPassword);
    AppUser GetUserByEmail(string aLoginEmail);
    AppUser GetUserByMobile(string aMobileNo);
}
public interface ISvcTokenRepo : IGenericRepository<SvcToken>
{ SvcToken GetSvcToken(long aAppUserId, string aLoginToken); }
public interface IUserLoginRepository : IGenericRepository<UserLogin>
{
    UserLogin GetUserByToken(long aUserId, string aToken);
}
public interface ILoginLogRepo : IGenericRepository<LoginLog>
{ }
public interface IBlogImageRepo : IGenericRepository<BlogImage>
{ }
public interface IBlogPostRepo : IGenericRepository<BlogPost>
{
    /// <summary>
    /// Gets blog count statistics for dashboard.
    /// </summary>
    BlogPost GetTheCounts();

    /// <summary>
    /// Gets a blog post by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>BlogPost if found, null otherwise.</returns>
    BlogPost GetBySlug(string slug);

    /// <summary>
    /// Gets published posts for public display with pagination.
    /// </summary>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>List of published, non-deleted posts.</returns>
    IEnumerable<BlogPost> GetPublishedPosts(int pageSize, int offset);

    /// <summary>
    /// Soft deletes a post by setting IsDeleted flag.
    /// </summary>
    /// <param name="postId">The post ID to delete.</param>
    void SoftDelete(long postId);

    /// <summary>
    /// Checks if a slug already exists in the database.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludePostId">Post ID to exclude (for updates).</param>
    /// <returns>True if slug exists, false otherwise.</returns>
    bool SlugExists(string slug, long excludePostId = 0);

    /// <summary>
    /// Gets the most recent published post (featured post).
    /// </summary>
    /// <returns>Most recent published post, or null if none.</returns>
    BlogPost GetFeaturedPost();

    /// <summary>
    /// Gets the total count of published posts.
    /// </summary>
    /// <returns>Count of published, non-deleted posts.</returns>
    int GetPublishedPostCount();

    /// <summary>
    /// Gets published posts filtered by category ID.
    /// </summary>
    /// <param name="categoryId">Category ID to filter by.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>List of published posts in the category.</returns>
    IEnumerable<BlogPost> GetPostsByCategory(long categoryId, int pageSize, int offset);

    /// <summary>
    /// Gets the count of published posts in a category.
    /// </summary>
    /// <param name="categoryId">Category ID.</param>
    /// <returns>Count of posts.</returns>
    int GetPostCountByCategory(long categoryId);

    /// <summary>
    /// Gets all scheduled posts for admin view.
    /// </summary>
    /// <returns>Posts with ScheduledPublishOn set and not yet published.</returns>
    IEnumerable<BlogPost> GetScheduledPosts();

    /// <summary>
    /// Gets posts that are due for publishing (scheduled time has passed).
    /// </summary>
    /// <param name="now">Current UTC time.</param>
    /// <returns>Posts ready to be published.</returns>
    IEnumerable<BlogPost> GetDueScheduledPosts(DateTime now);

    /// <summary>
    /// Gets posts belonging to a series, ordered by part number.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Posts in the series ordered by SeriesPartNumber.</returns>
    IEnumerable<BlogPost> GetPostsBySeries(long seriesId);

    /// <summary>
    /// Gets count of posts in a series.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Number of posts.</returns>
    int GetPostCountBySeries(long seriesId);

    /// <summary>
    /// Gets the highest part number in a series.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Max part number, or 0 if no posts.</returns>
    int GetMaxPartNumberInSeries(long seriesId);

    /// <summary>
    /// Clears series association from all posts in a series.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    void ClearSeriesFromPosts(long seriesId);
}
/// <summary>
/// Repository interface for BlogTag data access operations.
/// </summary>
public interface IBlogTagRepo : IGenericRepository<BlogTag>
{
    /// <summary>
    /// Gets a tag by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>BlogTag if found, null otherwise.</returns>
    BlogTag GetBySlug(string slug);

    /// <summary>
    /// Checks if a tag slug already exists.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeTagId">Tag ID to exclude (for updates).</param>
    /// <returns>True if slug exists, false otherwise.</returns>
    bool SlugExists(string slug, long excludeTagId = 0);

    /// <summary>
    /// Gets all tags with post counts.
    /// </summary>
    /// <returns>Tags with computed PostCount field.</returns>
    IEnumerable<BlogTag> GetAllWithCounts();

    /// <summary>
    /// Searches tags by name for autocomplete.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>Matching tags.</returns>
    IEnumerable<BlogTag> SearchTags(string query);

    /// <summary>
    /// Deletes a tag by ID.
    /// </summary>
    /// <param name="tagId">Tag ID to delete.</param>
    void Delete(long tagId);

    /// <summary>
    /// Gets tags for a specific post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>Tags associated with the post.</returns>
    IEnumerable<BlogTag> GetTagsForPost(long postId);

    /// <summary>
    /// Sets tags for a post (replaces existing).
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <param name="tagIds">List of tag IDs to associate.</param>
    void SetTagsForPost(long postId, IEnumerable<long> tagIds);

    /// <summary>
    /// Gets posts by tag ID.
    /// </summary>
    /// <param name="tagId">Tag ID.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>Posts with this tag.</returns>
    IEnumerable<BlogPost> GetPostsByTag(long tagId, int pageSize, int offset);

    /// <summary>
    /// Gets count of posts with a specific tag.
    /// </summary>
    /// <param name="tagId">Tag ID.</param>
    /// <returns>Count of posts.</returns>
    int GetPostCountByTag(long tagId);
}

/// <summary>
/// Repository interface for Category data access operations.
/// </summary>
public interface ICategoryRepo : IGenericRepository<Category>
{
    /// <summary>
    /// Gets a category by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>Category if found, null otherwise.</returns>
    Category GetBySlug(string slug);

    /// <summary>
    /// Checks if a category slug already exists.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeCategoryId">Category ID to exclude (for updates).</param>
    /// <returns>True if slug exists, false otherwise.</returns>
    bool SlugExists(string slug, long excludeCategoryId = 0);

    /// <summary>
    /// Gets all categories with post counts.
    /// </summary>
    /// <returns>Categories with computed PostCount field.</returns>
    IEnumerable<Category> GetAllWithCounts();

    /// <summary>
    /// Deletes a category by ID.
    /// </summary>
    /// <param name="categoryId">Category ID to delete.</param>
    void Delete(long categoryId);
}

public interface IBlogCommentRepo : IGenericRepository<BlogComment>
{
    void ApproveBlogComment(long BlogCommentID);
    IEnumerable<BlogComment> GetPagedUnAppComments(int PageSize, int OffSet);
    IEnumerable<BlogComment> GetPostParentComments(long BlogPostID);
    IEnumerable<BlogComment> GetPostChildComments(long BlogPostID);
    AdminCounts GetAdminCounts();
}
public interface IUserEventRepo : IGenericRepository<UserEvent>
{ }

/// <summary>
/// Repository interface for BlogSeries data access operations.
/// </summary>
public interface IBlogSeriesRepo : IGenericRepository<BlogSeries>
{
    /// <summary>
    /// Gets a series by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>BlogSeries if found, null otherwise.</returns>
    BlogSeries GetBySlug(string slug);

    /// <summary>
    /// Checks if a series slug already exists.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeSeriesId">Series ID to exclude (for updates).</param>
    /// <returns>True if slug exists, false otherwise.</returns>
    bool SlugExists(string slug, long excludeSeriesId = 0);

    /// <summary>
    /// Gets all series with post counts.
    /// </summary>
    /// <returns>Series with computed PostCount field.</returns>
    IEnumerable<BlogSeries> GetAllWithCounts();

    /// <summary>
    /// Deletes a series by ID.
    /// </summary>
    /// <param name="seriesId">Series ID to delete.</param>
    void Delete(long seriesId);
}

/// <summary>
/// Repository interface for PasswordResetToken data access operations.
/// </summary>
public interface IPasswordResetTokenRepo : IGenericRepository<PasswordResetToken>
{
    /// <summary>
    /// Gets a password reset token by its token string.
    /// </summary>
    /// <param name="token">The token string.</param>
    /// <returns>PasswordResetToken if found, null otherwise.</returns>
    PasswordResetToken GetByToken(string token);

    /// <summary>
    /// Marks a token as used.
    /// </summary>
    /// <param name="tokenId">Token ID to mark as used.</param>
    void MarkUsed(long tokenId);

    /// <summary>
    /// Deletes expired tokens (cleanup).
    /// </summary>
    void DeleteExpiredTokens();
}
