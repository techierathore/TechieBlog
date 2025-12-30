using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for user favorite/bookmark operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for managing user favorites.</para>
/// <para><b>Dependencies:</b> IUserFavoriteRepo, IBlogPostRepo for data access.</para>
/// <para><b>Story:</b> FIX-014 - Favorites/Bookmarks Implementation (Epic 4, FR17)</para>
/// </remarks>
public class FavoriteSvc
{
    private readonly IUserFavoriteRepo _favoriteRepo;
    private readonly IBlogPostRepo _postRepo;
    private readonly ILogger<FavoriteSvc> _logger;

    public FavoriteSvc(IUserFavoriteRepo favoriteRepo, IBlogPostRepo postRepo, ILogger<FavoriteSvc> logger)
    {
        _favoriteRepo = favoriteRepo;
        _postRepo = postRepo;
        _logger = logger;
    }

    /// <summary>
    /// Adds a post to user's favorites.
    /// </summary>
    /// <param name="postId">The post ID to favorite.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result AddFavorite(long postId, long userId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID.");
        
        if (userId <= 0)
            return Result.Failure("Invalid user ID.");

        try
        {
            // Check if already favorited
            var existing = _favoriteRepo.GetByPostAndUser(postId, userId);
            if (existing != null)
            {
                _logger.LogDebug("Post {PostId} already favorited by user {UserId}", postId, userId);
                return Result.Success(); // Already favorited - treat as success
            }

            // Verify post exists and is published
            var post = _postRepo.GetSingle(postId);
            if (post == null || !post.Published || post.IsDeleted)
            {
                return Result.Failure("Post not found or not available.");
            }

            var favorite = new UserFavorite
            {
                PostId = postId,
                UserId = userId,
                CreatedOn = DateTime.UtcNow
            };

            _favoriteRepo.Insert(favorite);
            _logger.LogInformation("User {UserId} favorited post {PostId}", userId, postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding favorite for user {UserId}, post {PostId}", userId, postId);
            return Result.Failure("Failed to add favorite. Please try again.");
        }
    }

    /// <summary>
    /// Removes a post from user's favorites.
    /// </summary>
    /// <param name="postId">The post ID to unfavorite.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result RemoveFavorite(long postId, long userId)
    {
        if (postId <= 0)
            return Result.Failure("Invalid post ID.");
        
        if (userId <= 0)
            return Result.Failure("Invalid user ID.");

        try
        {
            _favoriteRepo.Delete(postId, userId);
            _logger.LogInformation("User {UserId} removed favorite for post {PostId}", userId, postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing favorite for user {UserId}, post {PostId}", userId, postId);
            return Result.Failure("Failed to remove favorite. Please try again.");
        }
    }

    /// <summary>
    /// Toggles favorite status for a post.
    /// </summary>
    /// <param name="postId">The post ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>Result with new favorited state (true = favorited, false = unfavorited).</returns>
    public Result<bool> ToggleFavorite(long postId, long userId)
    {
        if (postId <= 0)
            return Result<bool>.Failure("Invalid post ID.");
        
        if (userId <= 0)
            return Result<bool>.Failure("Invalid user ID.");

        try
        {
            var existing = _favoriteRepo.GetByPostAndUser(postId, userId);
            if (existing != null)
            {
                // Remove favorite
                _favoriteRepo.Delete(postId, userId);
                _logger.LogInformation("User {UserId} toggled off favorite for post {PostId}", userId, postId);
                return Result<bool>.Success(false);
            }
            else
            {
                // Verify post exists and is published
                var post = _postRepo.GetSingle(postId);
                if (post == null || !post.Published || post.IsDeleted)
                {
                    return Result<bool>.Failure("Post not found or not available.");
                }

                // Add favorite
                var favorite = new UserFavorite
                {
                    PostId = postId,
                    UserId = userId,
                    CreatedOn = DateTime.UtcNow
                };
                _favoriteRepo.Insert(favorite);
                _logger.LogInformation("User {UserId} toggled on favorite for post {PostId}", userId, postId);
                return Result<bool>.Success(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling favorite for user {UserId}, post {PostId}", userId, postId);
            return Result<bool>.Failure("Failed to update favorite. Please try again.");
        }
    }

    /// <summary>
    /// Checks if a post is favorited by a user.
    /// </summary>
    /// <param name="postId">The post ID.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>True if favorited, false otherwise.</returns>
    public bool IsFavorited(long postId, long userId)
    {
        if (postId <= 0 || userId <= 0)
            return false;

        try
        {
            return _favoriteRepo.GetByPostAndUser(postId, userId) != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking favorite status for user {UserId}, post {PostId}", userId, postId);
            return false;
        }
    }

    /// <summary>
    /// Gets user's favorited posts.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="limit">Maximum number of results.</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <returns>List of favorited posts with metadata.</returns>
    public IEnumerable<UserFavorite> GetUserFavorites(long userId, int limit = 50, int offset = 0)
    {
        if (userId <= 0)
            return Enumerable.Empty<UserFavorite>();

        try
        {
            return _favoriteRepo.GetByUser(userId, limit, offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting favorites for user {UserId}", userId);
            return Enumerable.Empty<UserFavorite>();
        }
    }

    /// <summary>
    /// Gets the count of favorites for a post.
    /// </summary>
    /// <param name="postId">The post ID.</param>
    /// <returns>Number of users who favorited the post.</returns>
    public int GetFavoriteCount(long postId)
    {
        if (postId <= 0)
            return 0;

        try
        {
            return _favoriteRepo.GetCountByPost(postId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting favorite count for post {PostId}", postId);
            return 0;
        }
    }

    /// <summary>
    /// Gets the count of user's favorites.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <returns>Total number of favorites.</returns>
    public int GetUserFavoriteCount(long userId)
    {
        if (userId <= 0)
            return 0;

        try
        {
            return _favoriteRepo.GetCountByUser(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting favorite count for user {UserId}", userId);
            return 0;
        }
    }

    /// <summary>
    /// Checks favorite status for multiple posts at once (for batch loading).
    /// </summary>
    /// <param name="postIds">List of post IDs to check.</param>
    /// <param name="userId">The user ID.</param>
    /// <returns>Dictionary mapping post IDs to favorited status.</returns>
    public Dictionary<long, bool> GetFavoriteStatusBatch(IEnumerable<long> postIds, long userId)
    {
        var result = new Dictionary<long, bool>();
        
        if (userId <= 0 || postIds == null || !postIds.Any())
            return result;

        try
        {
            var favorites = _favoriteRepo.GetByUser(userId, limit: 1000);
            var favoritedPostIds = favorites.Select(f => f.PostId).ToHashSet();
            
            foreach (var postId in postIds)
            {
                result[postId] = favoritedPostIds.Contains(postId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batch favorite status for user {UserId}", userId);
            // Return empty result - callers should handle missing entries
        }

        return result;
    }
}
