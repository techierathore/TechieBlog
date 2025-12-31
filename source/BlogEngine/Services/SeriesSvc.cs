using BlogEngine.Common;
using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for blog series operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for CRUD operations on blog series.</para>
/// <para><b>Dependencies:</b> IBlogSeriesRepo, IBlogPostRepo for data access, SlugGenerator for URL slugs.</para>
/// </remarks>
public class SeriesSvc
{
    private readonly IBlogSeriesRepo SeriesRepo;
    private readonly IBlogPostRepo PostRepo;
    private readonly ILogger<SeriesSvc> _logger;

    public SeriesSvc(IBlogSeriesRepo seriesRepo, IBlogPostRepo postRepo, ILogger<SeriesSvc> logger)
    {
        SeriesRepo = seriesRepo;
        PostRepo = postRepo;
        _logger = logger;
    }

    /// <summary>
    /// Gets all series ordered by name.
    /// </summary>
    /// <returns>List of all series.</returns>
    public IEnumerable<BlogSeries> GetAllSeries()
    {
        try
        {
            return SeriesRepo.GetAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all series");
            return Enumerable.Empty<BlogSeries>();
        }
    }

    /// <summary>
    /// Gets all series with their post counts.
    /// </summary>
    /// <returns>Series with PostCount field populated.</returns>
    public IEnumerable<BlogSeries> GetAllWithCounts()
    {
        try
        {
            return SeriesRepo.GetAllWithCounts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting series with counts");
            return Enumerable.Empty<BlogSeries>();
        }
    }

    /// <summary>
    /// Gets a single series by ID.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Series if found, null otherwise.</returns>
    public BlogSeries GetSeries(long seriesId)
    {
        try
        {
            return SeriesRepo.GetSingle(seriesId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting series by ID: {SeriesId}", seriesId);
            return null;
        }
    }

    /// <summary>
    /// Gets a series by its URL slug, including its posts.
    /// </summary>
    /// <param name="slug">URL-friendly slug.</param>
    /// <returns>Series with Posts populated if found, null otherwise.</returns>
    public BlogSeries GetSeriesBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            var series = SeriesRepo.GetBySlug(slug);
            if (series != null)
            {
                series.Posts = PostRepo.GetPostsBySeries(series.SeriesId).ToList();
            }
            return series;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting series by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Gets posts for a series ordered by part number.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>List of posts in the series.</returns>
    public IEnumerable<BlogPost> GetPostsInSeries(long seriesId)
    {
        try
        {
            return PostRepo.GetPostsBySeries(seriesId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting posts for series ID: {SeriesId}", seriesId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the next available part number for a series.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Next part number (max + 1).</returns>
    public int GetNextPartNumber(long seriesId)
    {
        try
        {
            return PostRepo.GetMaxPartNumberInSeries(seriesId) + 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting next part number for series ID: {SeriesId}", seriesId);
            return 1;
        }
    }

    /// <summary>
    /// Creates a new series with validation and slug generation.
    /// </summary>
    /// <param name="series">The series to create.</param>
    /// <returns>Result with created series on success, error message on failure.</returns>
    public Result<BlogSeries> CreateSeries(BlogSeries series)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (string.IsNullOrWhiteSpace(series.Name))
            return Result<BlogSeries>.Failure("Series name is required");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(series.Slug))
        {
            series.Slug = SlugGenerator.GenerateSlug(series.Name);
        }

        // Check for duplicate slug
        if (SeriesRepo.SlugExists(series.Slug))
        {
            series.Slug = SlugGenerator.GenerateUniqueSlug(series.Slug, 1);
            int counter = 2;
            while (SeriesRepo.SlugExists(series.Slug) && counter < 100)
            {
                series.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(series.Name), counter);
                counter++;
            }
        }

        // Set timestamps
        series.CreatedOn = DateTime.UtcNow;
        series.UpdatedOn = DateTime.UtcNow;

        // Set default status if not provided
        if (string.IsNullOrWhiteSpace(series.Status))
        {
            series.Status = "In Progress";
        }

        try
        {
            var seriesId = SeriesRepo.InsertToGetId(series);
            series.SeriesId = seriesId;
            _logger.LogInformation("Created series '{Name}' with ID {SeriesId}", series.Name, seriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create series: {Name}", series.Name);
            return Result<BlogSeries>.Failure($"Failed to create series: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing series.
    /// </summary>
    /// <param name="series">The series to update.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogSeries> UpdateSeries(BlogSeries series)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (series.SeriesId <= 0)
            return Result<BlogSeries>.Failure("Invalid series ID");

        if (string.IsNullOrWhiteSpace(series.Name))
            return Result<BlogSeries>.Failure("Series name is required");

        // Check if series exists
        var existing = SeriesRepo.GetSingle(series.SeriesId);
        if (existing == null)
            return Result<BlogSeries>.Failure("Series not found");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(series.Slug))
        {
            series.Slug = SlugGenerator.GenerateSlug(series.Name);
        }

        // Check for duplicate slug (exclude current series)
        if (SeriesRepo.SlugExists(series.Slug, series.SeriesId))
        {
            series.Slug = SlugGenerator.GenerateUniqueSlug(series.Slug, 1);
            int counter = 2;
            while (SeriesRepo.SlugExists(series.Slug, series.SeriesId) && counter < 100)
            {
                series.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(series.Name), counter);
                counter++;
            }
        }

        series.UpdatedOn = DateTime.UtcNow;

        try
        {
            SeriesRepo.Update(series);
            _logger.LogInformation("Updated series '{Name}' with ID {SeriesId}", series.Name, series.SeriesId);
            return Result<BlogSeries>.Success(series);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update series ID {SeriesId}: {Name}", series.SeriesId, series.Name);
            return Result<BlogSeries>.Failure($"Failed to update series: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a series (insert or update based on SeriesId).
    /// </summary>
    /// <param name="series">The series to save.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogSeries> SaveSeries(BlogSeries series)
    {
        if (series == null)
            return Result<BlogSeries>.Failure("Series cannot be null");

        if (series.SeriesId <= 0)
        {
            return CreateSeries(series);
        }
        else
        {
            return UpdateSeries(series);
        }
    }

    /// <summary>
    /// Deletes a series and removes series association from all its posts.
    /// </summary>
    /// <param name="seriesId">ID of the series to delete.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result DeleteSeries(long seriesId)
    {
        if (seriesId <= 0)
            return Result.Failure("Invalid series ID");

        var existing = SeriesRepo.GetSingle(seriesId);
        if (existing == null)
            return Result.Failure("Series not found");

        try
        {
            // First remove series association from all posts
            PostRepo.ClearSeriesFromPosts(seriesId);

            // Then delete the series
            SeriesRepo.Delete(seriesId);
            _logger.LogInformation("Deleted series ID {SeriesId}", seriesId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete series ID {SeriesId}", seriesId);
            return Result.Failure($"Failed to delete series: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets series navigation info for a specific post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>SeriesNavigation if post is part of a series, null otherwise.</returns>
    public SeriesNavigation GetSeriesNavigation(long postId)
    {
        try
        {
            var post = PostRepo.GetSingle(postId);
            if (post?.SeriesId == null)
                return null;

            var series = SeriesRepo.GetSingle(post.SeriesId.Value);
            if (series == null)
                return null;

            var seriesPosts = PostRepo.GetPostsBySeries(post.SeriesId.Value)
                .Where(p => p.Published)
                .OrderBy(p => p.SeriesPartNumber)
                .ToList();

            var currentIndex = seriesPosts.FindIndex(p => p.PostID == postId);
            if (currentIndex < 0)
                return null;

            return new SeriesNavigation
            {
                SeriesName = series.Name,
                SeriesSlug = series.Slug,
                CurrentPart = post.SeriesPartNumber ?? 0,
                TotalParts = seriesPosts.Count,
                PreviousPost = currentIndex > 0 ? seriesPosts[currentIndex - 1] : null,
                NextPost = currentIndex < seriesPosts.Count - 1 ? seriesPosts[currentIndex + 1] : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting series navigation for post ID: {PostId}", postId);
            return null;
        }
    }
}
