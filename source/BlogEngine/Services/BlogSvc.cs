using BlogEngine.Common;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for blog post operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for CRUD operations on blog posts.</para>
/// <para><b>Dependencies:</b> IBlogPostRepo for data access, SlugGenerator for URL slugs.</para>
/// </remarks>
public class BlogSvc
{
    private readonly IBlogPostRepo PostRepo;
    private readonly ILogger<BlogSvc> _logger;

    public BlogSvc(IBlogPostRepo aPostRepo, ILogger<BlogSvc> logger)
    {
        PostRepo = aPostRepo;
        _logger = logger;
    }

    /// <summary>
    /// Gets all posts based on user role.
    /// </summary>
    /// <param name="aUserId">User ID for filtering.</param>
    /// <param name="aIsAdmin">True if user has admin/editor privileges.</param>
    /// <returns>List of posts visible to the user.</returns>
    public IEnumerable<BlogPost> GetAllPosts(long aUserId, bool aIsAdmin)
    {
        try
        {
            IEnumerable<BlogPost> vReturnVal;
            if (aIsAdmin)
            {
                vReturnVal = PostRepo.GetAll();
            }
            else vReturnVal = PostRepo.GetAllById(aUserId);
            return vReturnVal;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting posts for user {UserId}, isAdmin: {IsAdmin}", aUserId, aIsAdmin);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets a single post by ID.
    /// </summary>
    /// <param name="aSingleId">Post ID.</param>
    /// <returns>BlogPost if found, null otherwise.</returns>
    public BlogPost GetSinglePost(long aSingleId)
    {
        try
        {
            return PostRepo.GetSingle(aSingleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting post by ID: {PostId}", aSingleId);
            return null;
        }
    }

    /// <summary>
    /// Gets a post by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug.</param>
    /// <returns>BlogPost if found, null otherwise.</returns>
    public BlogPost GetPostBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;
            return PostRepo.GetBySlug(slug);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting post by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Gets published posts for public display with pagination.
    /// </summary>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>List of published posts.</returns>
    public IEnumerable<BlogPost> GetPublishedPosts(int pageSize, int offset)
    {
        try
        {
            return PostRepo.GetPublishedPosts(pageSize, offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting published posts. PageSize: {PageSize}, Offset: {Offset}", pageSize, offset);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets blog count statistics for dashboard.
    /// </summary>
    /// <returns>BlogPost with count statistics.</returns>
    public BlogPost GetBlogCounts()
    {
        try
        {
            return PostRepo.GetTheCounts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting blog counts");
            return new BlogPost { BlogCount = 0 };
        }
    }

    /// <summary>
    /// Gets the most recent published post (featured post).
    /// </summary>
    /// <returns>Most recent published post, or null if none.</returns>
    public BlogPost GetFeaturedPost()
    {
        try
        {
            return PostRepo.GetFeaturedPost();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting featured post");
            return null;
        }
    }

    /// <summary>
    /// Gets the total count of published posts.
    /// </summary>
    /// <returns>Count of published, non-deleted posts.</returns>
    public int GetPublishedPostCount()
    {
        try
        {
            return PostRepo.GetPublishedPostCount();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting published post count");
            return 0;
        }
    }

    /// <summary>
    /// Gets published posts filtered by category ID.
    /// </summary>
    /// <param name="categoryId">Category ID to filter by.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>List of published posts in the category.</returns>
    public IEnumerable<BlogPost> GetPostsByCategory(long categoryId, int pageSize, int offset)
    {
        try
        {
            return PostRepo.GetPostsByCategory(categoryId, pageSize, offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting posts by category {CategoryId}", categoryId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the count of published posts in a category.
    /// </summary>
    /// <param name="categoryId">Category ID.</param>
    /// <returns>Count of posts.</returns>
    public int GetPostCountByCategory(long categoryId)
    {
        try
        {
            return PostRepo.GetPostCountByCategory(categoryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting post count for category {CategoryId}", categoryId);
            return 0;
        }
    }

    /// <summary>
    /// Creates a new blog post with validation and slug generation.
    /// </summary>
    /// <param name="post">The blog post to create.</param>
    /// <returns>Result with created post on success, error message on failure.</returns>
    public Result<BlogPost> CreatePost(BlogPost post)
    {
        // Validate required fields
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (string.IsNullOrWhiteSpace(post.Title))
            return Result<BlogPost>.Failure("Title is required");

        if (string.IsNullOrWhiteSpace(post.PostContent))
            return Result<BlogPost>.Failure("Content is required");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateSlug(post.Title);
        }

        // Handle duplicate slug by appending timestamp
        if (PostRepo.SlugExists(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateUniqueSlug(post.Slug, 1);
            // Keep checking until we find a unique slug
            int counter = 2;
            while (PostRepo.SlugExists(post.Slug) && counter < 100)
            {
                post.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(post.Title), counter);
                counter++;
            }
        }

        // Set timestamps
        post.CreatedOn = DateTime.UtcNow;
        post.IsDeleted = false;

        try
        {
            var postId = PostRepo.InsertToGetId(post);
            post.PostID = postId;
            _logger.LogInformation("Created post '{Title}' with ID {PostId}", post.Title, postId);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create post: {Title}", post.Title);
            return Result<BlogPost>.Failure($"Failed to create post: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing blog post.
    /// </summary>
    /// <param name="post">The blog post to update.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogPost> UpdatePost(BlogPost post)
    {
        // Validate required fields
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (post.PostID <= 0)
            return Result<BlogPost>.Failure("Invalid post ID");

        if (string.IsNullOrWhiteSpace(post.Title))
            return Result<BlogPost>.Failure("Title is required");

        if (string.IsNullOrWhiteSpace(post.PostContent))
            return Result<BlogPost>.Failure("Content is required");

        // Check if post exists
        var existing = PostRepo.GetSingle(post.PostID);
        if (existing == null)
            return Result<BlogPost>.Failure("Post not found");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(post.Slug))
        {
            post.Slug = SlugGenerator.GenerateSlug(post.Title);
        }

        // Handle duplicate slug (exclude current post)
        if (PostRepo.SlugExists(post.Slug, post.PostID))
        {
            post.Slug = SlugGenerator.GenerateUniqueSlug(post.Slug, 1);
            int counter = 2;
            while (PostRepo.SlugExists(post.Slug, post.PostID) && counter < 100)
            {
                post.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(post.Title), counter);
                counter++;
            }
        }

        // Set update timestamp
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            PostRepo.Update(post);
            _logger.LogInformation("Updated post '{Title}' with ID {PostId}", post.Title, post.PostID);
            return Result<BlogPost>.Success(post);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update post ID {PostId}: {Title}", post.PostID, post.Title);
            return Result<BlogPost>.Failure($"Failed to update post: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a post (insert or update based on PostID).
    /// </summary>
    /// <param name="post">The post to save.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogPost> SavePost(BlogPost post)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (post.PostID <= 0)
        {
            return CreatePost(post);
        }
        else
        {
            return UpdatePost(post);
        }
    }

    /// <summary>
    /// Soft deletes a blog post.
    /// </summary>
    /// <param name="postId">ID of the post to delete.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result DeletePost(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        // Check if post exists
        var existing = PostRepo.GetSingle(postId);
        if (existing == null)
            return Result.Failure("Post not found");

        if (existing.IsDeleted)
            return Result.Failure("Post is already deleted");

        try
        {
            PostRepo.SoftDelete(postId);
            _logger.LogInformation("Deleted post ID {PostId}", postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete post ID {PostId}", postId);
            return Result.Failure($"Failed to delete post: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a post as draft (not published).
    /// </summary>
    /// <param name="post">The post to save.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogPost> SaveDraft(BlogPost post)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        return SavePost(post);
    }

    /// <summary>
    /// Publishes a post (sets Published = true and PublishedOn).
    /// </summary>
    /// <param name="post">The post to publish.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogPost> PublishPost(BlogPost post)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        post.Published = true;
        post.UpdatedOn = DateTime.UtcNow;

        // Only set PublishedOn if not already set (first publish)
        if (!post.PublishedOn.HasValue)
        {
            post.PublishedOn = DateTime.UtcNow;
        }

        return SavePost(post);
    }

    /// <summary>
    /// Unpublishes a post (sets Published = false, keeps PublishedOn for history).
    /// </summary>
    /// <param name="postId">ID of the post to unpublish.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result UnpublishPost(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = PostRepo.GetSingle(postId);
        if (post == null)
            return Result.Failure("Post not found");

        if (!post.Published)
            return Result.Failure("Post is already unpublished");

        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            PostRepo.Update(post);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to unpublish post: {ex.Message}");
        }
    }

    /// <summary>
    /// Quick publishes a post by ID.
    /// </summary>
    /// <param name="postId">ID of the post to publish.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result QuickPublish(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = PostRepo.GetSingle(postId);
        if (post == null)
            return Result.Failure("Post not found");

        if (post.Published)
            return Result.Failure("Post is already published");

        post.Published = true;
        post.UpdatedOn = DateTime.UtcNow;
        post.ScheduledPublishOn = null; // Clear any schedule
        if (!post.PublishedOn.HasValue)
        {
            post.PublishedOn = DateTime.UtcNow;
        }

        try
        {
            PostRepo.Update(post);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to publish post: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all scheduled posts for admin view.
    /// </summary>
    /// <returns>List of posts scheduled for future publication.</returns>
    public IEnumerable<BlogPost> GetScheduledPosts()
    {
        try
        {
            return PostRepo.GetScheduledPosts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting scheduled posts");
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets posts that are due for publishing (scheduled time has passed).
    /// </summary>
    /// <returns>Posts ready to be published.</returns>
    public IEnumerable<BlogPost> GetDueScheduledPosts()
    {
        try
        {
            return PostRepo.GetDueScheduledPosts(DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting due scheduled posts");
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Schedules a post for future publication.
    /// </summary>
    /// <param name="post">The post to schedule.</param>
    /// <param name="scheduledUtc">UTC time when the post should be published.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogPost> SchedulePost(BlogPost post, DateTime scheduledUtc)
    {
        if (post == null)
            return Result<BlogPost>.Failure("Post cannot be null");

        if (scheduledUtc <= DateTime.UtcNow)
            return Result<BlogPost>.Failure("Scheduled time must be in the future");

        post.ScheduledPublishOn = scheduledUtc;
        post.Published = false;
        post.UpdatedOn = DateTime.UtcNow;

        return SavePost(post);
    }

    /// <summary>
    /// Cancels a scheduled post (reverts to draft).
    /// </summary>
    /// <param name="postId">ID of the post to cancel scheduling.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result CancelSchedule(long postId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID");

        var post = PostRepo.GetSingle(postId);
        if (post == null)
            return Result.Failure("Post not found");

        if (!post.ScheduledPublishOn.HasValue)
            return Result.Failure("Post is not scheduled");

        post.ScheduledPublishOn = null;
        post.UpdatedOn = DateTime.UtcNow;

        try
        {
            PostRepo.Update(post);
            return Result.Success();
        }
        catch (Exception ex)
        {
            return Result.Failure($"Failed to cancel schedule: {ex.Message}");
        }
    }
}
