using BlogEngine.Common;
using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for tag operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for CRUD operations on tags.</para>
/// <para><b>Dependencies:</b> IBlogTagRepo for data access, SlugGenerator for URL slugs.</para>
/// </remarks>
public class TagSvc
{
    private readonly IBlogTagRepo TagRepo;
    private readonly ILogger<TagSvc> _logger;

    public TagSvc(IBlogTagRepo tagRepo, ILogger<TagSvc> logger)
    {
        TagRepo = tagRepo;
        _logger = logger;
    }

    /// <summary>
    /// Gets all tags ordered by name.
    /// </summary>
    /// <returns>List of all tags.</returns>
    public IEnumerable<BlogTag> GetAllTags()
    {
        try
        {
            return TagRepo.GetAll();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tags");
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Gets all tags with their post counts.
    /// </summary>
    /// <returns>Tags with PostCount field populated.</returns>
    public IEnumerable<BlogTag> GetAllWithCounts()
    {
        try
        {
            return TagRepo.GetAllWithCounts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tags with counts");
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Gets a single tag by ID.
    /// </summary>
    /// <param name="tagId">Tag ID.</param>
    /// <returns>BlogTag if found, null otherwise.</returns>
    public BlogTag GetSingleTag(long tagId)
    {
        try
        {
            return TagRepo.GetSingle(tagId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tag by ID: {TagId}", tagId);
            return null;
        }
    }

    /// <summary>
    /// Gets a tag by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug.</param>
    /// <returns>BlogTag if found, null otherwise.</returns>
    public BlogTag GetTagBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;
            return TagRepo.GetBySlug(slug);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tag by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Searches tags by name for autocomplete.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>Matching tags.</returns>
    public IEnumerable<BlogTag> SearchTags(string query)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
                return TagRepo.GetAll().Take(10);
            return TagRepo.SearchTags(query);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching tags with query: {Query}", query);
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Gets or creates a tag by name.
    /// </summary>
    /// <param name="tagName">Tag name to find or create.</param>
    /// <returns>Existing or newly created tag.</returns>
    public BlogTag GetOrCreateTag(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
            return null;

        var slug = SlugGenerator.GenerateSlug(tagName.Trim());
        var existing = TagRepo.GetBySlug(slug);
        if (existing != null)
            return existing;

        var tag = new BlogTag
        {
            TagName = tagName.Trim(),
            Slug = slug
        };
        tag.TagId = TagRepo.InsertToGetId(tag);
        return tag;
    }

    /// <summary>
    /// Creates a new tag with validation and slug generation.
    /// </summary>
    /// <param name="tag">The tag to create.</param>
    /// <returns>Result with created tag on success, error message on failure.</returns>
    public Result<BlogTag> CreateTag(BlogTag tag)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (string.IsNullOrWhiteSpace(tag.TagName))
            return Result<BlogTag>.Failure("Tag name is required");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateSlug(tag.TagName);
        }

        // Check for duplicate slug
        if (TagRepo.SlugExists(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateUniqueSlug(tag.Slug, 1);
            int counter = 2;
            while (TagRepo.SlugExists(tag.Slug) && counter < 100)
            {
                tag.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(tag.TagName), counter);
                counter++;
            }
        }

        try
        {
            var tagId = TagRepo.InsertToGetId(tag);
            tag.TagId = tagId;
            _logger.LogInformation("Created tag '{TagName}' with ID {TagId}", tag.TagName, tagId);
            return Result<BlogTag>.Success(tag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create tag: {TagName}", tag.TagName);
            return Result<BlogTag>.Failure($"Failed to create tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing tag.
    /// </summary>
    /// <param name="tag">The tag to update.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogTag> UpdateTag(BlogTag tag)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (tag.TagId <= 0)
            return Result<BlogTag>.Failure("Invalid tag ID");

        if (string.IsNullOrWhiteSpace(tag.TagName))
            return Result<BlogTag>.Failure("Tag name is required");

        // Check if tag exists
        var existing = TagRepo.GetSingle(tag.TagId);
        if (existing == null)
            return Result<BlogTag>.Failure("Tag not found");

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(tag.Slug))
        {
            tag.Slug = SlugGenerator.GenerateSlug(tag.TagName);
        }

        // Check for duplicate slug (exclude current tag)
        if (TagRepo.SlugExists(tag.Slug, tag.TagId))
        {
            tag.Slug = SlugGenerator.GenerateUniqueSlug(tag.Slug, 1);
            int counter = 2;
            while (TagRepo.SlugExists(tag.Slug, tag.TagId) && counter < 100)
            {
                tag.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(tag.TagName), counter);
                counter++;
            }
        }

        try
        {
            TagRepo.Update(tag);
            _logger.LogInformation("Updated tag '{TagName}' with ID {TagId}", tag.TagName, tag.TagId);
            return Result<BlogTag>.Success(tag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update tag ID {TagId}: {TagName}", tag.TagId, tag.TagName);
            return Result<BlogTag>.Failure($"Failed to update tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a tag (insert or update based on TagId).
    /// </summary>
    /// <param name="tag">The tag to save.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogTag> SaveTag(BlogTag tag)
    {
        if (tag == null)
            return Result<BlogTag>.Failure("Tag cannot be null");

        if (tag.TagId <= 0)
        {
            return CreateTag(tag);
        }
        else
        {
            return UpdateTag(tag);
        }
    }

    /// <summary>
    /// Deletes a tag.
    /// </summary>
    /// <param name="tagId">ID of the tag to delete.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result DeleteTag(long tagId)
    {
        if (tagId <= 0)
            return Result.Failure("Invalid tag ID");

        var existing = TagRepo.GetSingle(tagId);
        if (existing == null)
            return Result.Failure("Tag not found");

        try
        {
            TagRepo.Delete(tagId);
            _logger.LogInformation("Deleted tag ID {TagId}", tagId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete tag ID {TagId}", tagId);
            return Result.Failure($"Failed to delete tag: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets tags for a specific post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>Tags associated with the post.</returns>
    public IEnumerable<BlogTag> GetTagsForPost(long postId)
    {
        try
        {
            return TagRepo.GetTagsForPost(postId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tags for post ID {PostId}", postId);
            return Enumerable.Empty<BlogTag>();
        }
    }

    /// <summary>
    /// Sets tags for a post (replaces existing).
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <param name="tagIds">List of tag IDs to associate.</param>
    public void SetTagsForPost(long postId, IEnumerable<long> tagIds)
    {
        try
        {
            TagRepo.SetTagsForPost(postId, tagIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting tags for post ID {PostId}", postId);
        }
    }

    /// <summary>
    /// Gets posts filtered by tag.
    /// </summary>
    /// <param name="tagId">Tag ID.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>List of published posts with the tag.</returns>
    public IEnumerable<BlogPost> GetPostsByTag(long tagId, int pageSize, int offset)
    {
        try
        {
            return TagRepo.GetPostsByTag(tagId, pageSize, offset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting posts for tag ID {TagId}", tagId);
            return Enumerable.Empty<BlogPost>();
        }
    }

    /// <summary>
    /// Gets the count of posts with a specific tag.
    /// </summary>
    /// <param name="tagId">Tag ID.</param>
    /// <returns>Count of posts.</returns>
    public int GetPostCountByTag(long tagId)
    {
        try
        {
            return TagRepo.GetPostCountByTag(tagId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting post count for tag ID {TagId}", tagId);
            return 0;
        }
    }
}
