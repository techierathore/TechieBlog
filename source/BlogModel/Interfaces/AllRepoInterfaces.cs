using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;
public interface IBlogUserRepo : IGenericRepository<AppUser>
{
    AppUser GetLoginUser(string aLoginEmail, string aPassword);
    AppUser GetUserByEmail(string aLoginEmail);
    AppUser GetUserByMobile(string aMobileNo);

    /// <summary>
    /// Retrieves a user by their username (case-insensitive).
    /// </summary>
    /// <param name="username">The username to search for.</param>
    /// <returns>AppUser if found, null otherwise.</returns>
    AppUser? GetByUsername(string username);

    /// <summary>
    /// Retrieves the site owner (user with IsSiteOwner=true).
    /// </summary>
    /// <returns>AppUser if found, null otherwise.</returns>
    AppUser? GetSiteOwner();

    /// <summary>
    /// Retrieves all users who have written at least one blog post.
    /// </summary>
    /// <returns>Collection of authors.</returns>
    IEnumerable<AppUser> GetAllAuthors();

    /// <summary>
    /// Updates a user's username.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="username">The new username.</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool UpdateUsername(long userId, string username);

    /// <summary>
    /// Sets a user as the site owner, removing the flag from any previous owner.
    /// </summary>
    /// <param name="userId">The user ID to set as site owner.</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool SetSiteOwner(long userId);

    /// <summary>
    /// Checks if a username is available (not already taken).
    /// </summary>
    /// <param name="username">The username to check.</param>
    /// <returns>True if available, false if taken.</returns>
    bool IsUsernameAvailable(string username);

    /// <summary>
    /// Updates only the resume-related fields for a user.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="resumeData">AppUser object containing resume field values.</param>
    /// <returns>True if successful, false otherwise.</returns>
    bool UpdateResumeFields(long userId, AppUser resumeData);
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

    /// <summary>
    /// Searches posts by title, abstract, content, and tags.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="pageSize">Number of results per page.</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <returns>Matching published posts.</returns>
    IEnumerable<BlogPost> SearchPosts(string query, int pageSize = 10, int offset = 0);

    /// <summary>
    /// Gets the count of search results.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>Count of matching posts.</returns>
    int GetSearchResultCount(string query);
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
    
    /// <summary>
    /// Deletes a comment by ID.
    /// </summary>
    /// <param name="commentId">Comment ID to delete.</param>
    void Delete(long commentId);
    
    /// <summary>
    /// Gets all pending (unapproved) comments.
    /// </summary>
    /// <returns>List of unapproved comments.</returns>
    IEnumerable<BlogComment> GetPendingComments();
    
    /// <summary>
    /// Gets total count of comments.
    /// </summary>
    /// <returns>Total comment count.</returns>
    int GetTotalCount();
    
    /// <summary>
    /// Gets count of pending (unapproved) comments.
    /// </summary>
    /// <returns>Pending comment count.</returns>
    int GetPendingCount();
}
/// <summary>
/// Repository interface for UserEvent data access operations.
/// </summary>
public interface IUserEventRepo : IGenericRepository<UserEvent>
{
    /// <summary>
    /// Gets all events for a user filtered by event type.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="eventType">The event type to filter by (e.g., "Experience", "Speaking").</param>
    /// <returns>Collection of matching events ordered by DisplayOrder.</returns>
    IEnumerable<UserEvent> GetByUserAndType(long userId, string eventType);

    /// <summary>
    /// Deletes an event by ID.
    /// </summary>
    /// <param name="eventId">Event ID to delete.</param>
    void Delete(long eventId);

    /// <summary>
    /// Updates the display order for multiple events.
    /// </summary>
    /// <param name="eventOrders">Dictionary of EventId to DisplayOrder.</param>
    void UpdateDisplayOrders(Dictionary<long, int> eventOrders);
}

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

/// <summary>
/// Repository interface for PostRating data access operations.
/// </summary>
/// <remarks>
/// <para><b>Story:</b> FIX-013 - Star Ratings Implementation (Epic 4, FR15-16)</para>
/// </remarks>
public interface IPostRatingRepo : IGenericRepository<PostRating>
{
    /// <summary>
    /// Gets a user's rating for a specific post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <param name="userId">User ID.</param>
    /// <returns>PostRating if found, null otherwise.</returns>
    PostRating GetByPostAndUser(long postId, long userId);

    /// <summary>
    /// Gets the average rating for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>Average rating (0 if no ratings).</returns>
    double GetAverageByPost(long postId);

    /// <summary>
    /// Gets the total number of ratings for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>Count of ratings.</returns>
    int GetCountByPost(long postId);

    /// <summary>
    /// Gets rating statistics for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>PostRatingStats with average and count.</returns>
    PostRatingStats GetStatsByPost(long postId);

    /// <summary>
    /// Gets top-rated posts for popular content lists.
    /// </summary>
    /// <param name="count">Number of posts to return.</param>
    /// <param name="minRatings">Minimum number of ratings required.</param>
    /// <returns>Post IDs ordered by average rating.</returns>
    IEnumerable<long> GetTopRatedPostIds(int count = 10, int minRatings = 1);

    /// <summary>
    /// Deletes a rating by ID.
    /// </summary>
    /// <param name="ratingId">Rating ID to delete.</param>
    void Delete(long ratingId);
}

/// <summary>
/// Repository interface for UserFavorite data access operations.
/// </summary>
/// <remarks>
/// <para><b>Story:</b> FIX-014 - Favorites/Bookmarks Implementation (Epic 4, FR17)</para>
/// </remarks>
public interface IUserFavoriteRepo : IGenericRepository<UserFavorite>
{
    /// <summary>
    /// Gets a favorite by post and user IDs.
    /// </summary>
    /// <param name="postId">The post ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>UserFavorite if found, null otherwise.</returns>
    UserFavorite GetByPostAndUser(long postId, long userId);

    /// <summary>
    /// Gets all favorites for a user with post details.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <returns>List of user favorites ordered by creation date descending.</returns>
    IEnumerable<UserFavorite> GetByUser(long userId, int limit = 50, int offset = 0);

    /// <summary>
    /// Gets the count of favorites for a post.
    /// </summary>
    /// <param name="postId">The post ID.</param>
    /// <returns>Number of users who favorited the post.</returns>
    int GetCountByPost(long postId);

    /// <summary>
    /// Deletes a favorite by post and user IDs.
    /// </summary>
    /// <param name="postId">The post ID.</param>
    /// <param name="userId">The user ID.</param>
    void Delete(long postId, long userId);

    /// <summary>
    /// Gets the total count of favorites for a user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Total number of favorites.</returns>
    int GetCountByUser(long userId);
}

/// <summary>
/// Repository interface for Subscriber data access operations.
/// </summary>
public interface ISubscriberRepo : IGenericRepository<Subscriber>
{
    /// <summary>
    /// Gets a subscriber by email address.
    /// </summary>
    /// <param name="email">Email address to search.</param>
    /// <returns>Subscriber if found, null otherwise.</returns>
    Subscriber GetByEmail(string email);

    /// <summary>
    /// Checks if an email is already subscribed.
    /// </summary>
    /// <param name="email">Email to check.</param>
    /// <returns>True if email exists, false otherwise.</returns>
    bool EmailExists(string email);

    /// <summary>
    /// Gets all active subscribers.
    /// </summary>
    /// <returns>List of active subscribers.</returns>
    IEnumerable<Subscriber> GetActiveSubscribers();

    /// <summary>
    /// Gets subscribers filtered by active status.
    /// </summary>
    /// <param name="isActive">Active status filter.</param>
    /// <returns>Filtered list of subscribers.</returns>
    IEnumerable<Subscriber> GetByStatus(bool isActive);

    /// <summary>
    /// Searches subscribers by email.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <returns>Matching subscribers.</returns>
    IEnumerable<Subscriber> SearchByEmail(string query);

    /// <summary>
    /// Updates subscriber active status.
    /// </summary>
    /// <param name="subscriberId">Subscriber ID.</param>
    /// <param name="isActive">New active status.</param>
    void UpdateStatus(long subscriberId, bool isActive);

    /// <summary>
    /// Gets total subscriber count.
    /// </summary>
    /// <returns>Count of all subscribers.</returns>
    int GetTotalCount();

    /// <summary>
    /// Gets active subscriber count.
    /// </summary>
    /// <returns>Count of active subscribers.</returns>
    int GetActiveCount();
}
