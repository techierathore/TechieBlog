using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for post rating operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for the star rating system.</para>
/// <para><b>Dependencies:</b> IPostRatingRepo for data access.</para>
/// <para><b>Story:</b> FIX-013 - Star Ratings Implementation (Epic 4, FR15-16)</para>
/// </remarks>
public class RatingSvc
{
    private readonly IPostRatingRepo _ratingRepo;
    private readonly ILogger<RatingSvc> _logger;

    public RatingSvc(IPostRatingRepo ratingRepo, ILogger<RatingSvc> logger)
    {
        _ratingRepo = ratingRepo;
        _logger = logger;
    }

    /// <summary>
    /// Rates a post (insert or update existing rating).
    /// </summary>
    /// <param name="postId">Post ID to rate.</param>
    /// <param name="userId">User ID submitting the rating.</param>
    /// <param name="rating">Rating value (1-5).</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result RatePost(long postId, long userId, int rating)
    {
        // Validate rating value
        if (rating < 1 || rating > 5)
            return Result.Failure("Rating must be between 1 and 5.");

        if (postId <= 0)
            return Result.Failure("Invalid post ID.");

        if (userId <= 0)
            return Result.Failure("You must be logged in to rate posts.");

        try
        {
            // Check for existing rating
            var existing = _ratingRepo.GetByPostAndUser(postId, userId);

            if (existing != null)
            {
                // Update existing rating
                existing.Rating = rating;
                existing.UpdatedOn = DateTime.UtcNow;
                _ratingRepo.Update(existing);
                _logger.LogInformation("User {UserId} updated rating for post {PostId} to {Rating}", 
                    userId, postId, rating);
            }
            else
            {
                // Insert new rating
                var newRating = new PostRating
                {
                    PostId = postId,
                    UserId = userId,
                    Rating = rating,
                    CreatedOn = DateTime.UtcNow
                };
                _ratingRepo.Insert(newRating);
                _logger.LogInformation("User {UserId} rated post {PostId} with {Rating} stars", 
                    userId, postId, rating);
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rating post {PostId} by user {UserId}", postId, userId);
            return Result.Failure("Failed to submit rating. Please try again.");
        }
    }

    /// <summary>
    /// Gets the current user's rating for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <param name="userId">User ID.</param>
    /// <returns>Rating value (1-5) or null if not rated.</returns>
    public int? GetUserRating(long postId, long userId)
    {
        if (userId <= 0) return null;

        try
        {
            var rating = _ratingRepo.GetByPostAndUser(postId, userId);
            return rating?.Rating;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user rating for post {PostId}", postId);
            return null;
        }
    }

    /// <summary>
    /// Gets rating statistics for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>PostRatingStats with average and count.</returns>
    public PostRatingStats GetPostRatingStats(long postId)
    {
        try
        {
            return _ratingRepo.GetStatsByPost(postId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rating stats for post {PostId}", postId);
            return new PostRatingStats { AverageRating = 0, RatingCount = 0 };
        }
    }

    /// <summary>
    /// Gets rating statistics for a post including the current user's rating.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <param name="userId">Current user ID (0 if not logged in).</param>
    /// <returns>PostRatingStats with average, count, and user's rating.</returns>
    public PostRatingStats GetPostRatingStatsWithUserRating(long postId, long userId)
    {
        try
        {
            var stats = _ratingRepo.GetStatsByPost(postId);
            
            if (userId > 0)
            {
                var userRating = _ratingRepo.GetByPostAndUser(postId, userId);
                stats.UserRating = userRating?.Rating;
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rating stats with user rating for post {PostId}", postId);
            return new PostRatingStats { AverageRating = 0, RatingCount = 0 };
        }
    }

    /// <summary>
    /// Gets the average rating for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>Average rating (0.0 - 5.0).</returns>
    public double GetAverageRating(long postId)
    {
        try
        {
            return _ratingRepo.GetAverageByPost(postId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting average rating for post {PostId}", postId);
            return 0;
        }
    }

    /// <summary>
    /// Gets the rating count for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>Number of ratings.</returns>
    public int GetRatingCount(long postId)
    {
        try
        {
            return _ratingRepo.GetCountByPost(postId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rating count for post {PostId}", postId);
            return 0;
        }
    }

    /// <summary>
    /// Gets top-rated post IDs for popular content lists.
    /// </summary>
    /// <param name="count">Number of posts to return.</param>
    /// <param name="minRatings">Minimum number of ratings required.</param>
    /// <returns>List of post IDs ordered by average rating.</returns>
    public IEnumerable<long> GetTopRatedPostIds(int count = 10, int minRatings = 1)
    {
        try
        {
            return _ratingRepo.GetTopRatedPostIds(count, minRatings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top rated posts");
            return Enumerable.Empty<long>();
        }
    }

    /// <summary>
    /// Removes a user's rating for a post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <param name="userId">User ID.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result RemoveRating(long postId, long userId)
    {
        if (userId <= 0)
            return Result.Failure("You must be logged in to remove ratings.");

        try
        {
            var existing = _ratingRepo.GetByPostAndUser(postId, userId);
            if (existing == null)
                return Result.Failure("No rating found for this post.");

            _ratingRepo.Delete(existing.RatingId);
            _logger.LogInformation("User {UserId} removed rating for post {PostId}", userId, postId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing rating for post {PostId} by user {UserId}", postId, userId);
            return Result.Failure("Failed to remove rating. Please try again.");
        }
    }
}
