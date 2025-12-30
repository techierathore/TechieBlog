using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for blog comment operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides business logic for CRUD operations on comments.</para>
/// <para><b>Dependencies:</b> IBlogCommentRepo for data access.</para>
/// </remarks>
public class CommentSvc
{
    private readonly IBlogCommentRepo CommentRepo;
    private readonly ILogger<CommentSvc> _logger;

    public CommentSvc(IBlogCommentRepo repo, ILogger<CommentSvc> logger)
    {
        CommentRepo = repo;
        _logger = logger;
    }

    /// <summary>
    /// Gets all comments for a specific post, including replies.
    /// </summary>
    /// <param name="postId">The post ID.</param>
    /// <returns>List of comments with nested replies.</returns>
    public IEnumerable<BlogComment> GetCommentsByPostId(long postId)
    {
        try
        {
            return CommentRepo.GetAllById(postId) ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting comments for post ID: {PostId}", postId);
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets all pending (unapproved) comments.
    /// </summary>
    /// <returns>List of unapproved comments.</returns>
    public IEnumerable<BlogComment> GetPendingComments()
    {
        try
        {
            return CommentRepo.GetPendingComments() ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets all comments with pagination.
    /// </summary>
    /// <param name="pageSize">Number of comments per page.</param>
    /// <param name="offset">Number of comments to skip.</param>
    /// <returns>Paginated list of comments.</returns>
    public IEnumerable<BlogComment> GetPagedComments(int pageSize, int offset)
    {
        try
        {
            return CommentRepo.GetPagedData(pageSize, offset) ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets unapproved comments with pagination.
    /// </summary>
    /// <param name="pageSize">Number of comments per page.</param>
    /// <param name="offset">Number of comments to skip.</param>
    /// <returns>Paginated list of unapproved comments.</returns>
    public IEnumerable<BlogComment> GetPagedUnapprovedComments(int pageSize, int offset)
    {
        try
        {
            return CommentRepo.GetPagedUnAppComments(pageSize, offset) ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged unapproved comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets a single comment by ID.
    /// </summary>
    /// <param name="commentId">The comment ID.</param>
    /// <returns>BlogComment if found, null otherwise.</returns>
    public BlogComment GetComment(long commentId)
    {
        try
        {
            return CommentRepo.GetSingle(commentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting comment ID: {CommentId}", commentId);
            return null;
        }
    }

    /// <summary>
    /// Adds a new comment.
    /// </summary>
    /// <param name="comment">The comment to add.</param>
    /// <returns>Result with the created comment on success, error message on failure.</returns>
    public Result<BlogComment> AddComment(BlogComment comment)
    {
        if (comment == null)
            return Result<BlogComment>.Failure("Comment cannot be null");

        if (comment.PostID <= 0)
            return Result<BlogComment>.Failure("Invalid post ID");

        if (string.IsNullOrWhiteSpace(comment.GivenBy))
            return Result<BlogComment>.Failure("Name is required");

        if (string.IsNullOrWhiteSpace(comment.Email))
            return Result<BlogComment>.Failure("Email is required");

        if (string.IsNullOrWhiteSpace(comment.Comment))
            return Result<BlogComment>.Failure("Comment text is required");

        try
        {
            comment.GivenOn = DateTime.UtcNow;
            // Default to not published (requires approval)
            comment.Published = false;
            
            var commentId = CommentRepo.InsertToGetId(comment);
            comment.CommentID = commentId;
            
            _logger.LogInformation("Created comment ID {CommentId} for post {PostId} by {GivenBy}", 
                commentId, comment.PostID, comment.GivenBy);
            
            return Result<BlogComment>.Success(comment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create comment for post {PostId}", comment.PostID);
            return Result<BlogComment>.Failure($"Failed to create comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Approves a comment, making it visible on the blog.
    /// </summary>
    /// <param name="commentId">The comment ID to approve.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result ApproveComment(long commentId)
    {
        if (commentId <= 0)
            return Result.Failure("Invalid comment ID");

        try
        {
            var existing = CommentRepo.GetSingle(commentId);
            if (existing == null)
                return Result.Failure("Comment not found");

            CommentRepo.ApproveBlogComment(commentId);
            
            _logger.LogInformation("Approved comment ID {CommentId}", commentId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve comment ID {CommentId}", commentId);
            return Result.Failure($"Failed to approve comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes a comment.
    /// </summary>
    /// <param name="commentId">The comment ID to delete.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result DeleteComment(long commentId)
    {
        if (commentId <= 0)
            return Result.Failure("Invalid comment ID");

        try
        {
            var existing = CommentRepo.GetSingle(commentId);
            if (existing == null)
                return Result.Failure("Comment not found");

            CommentRepo.Delete(commentId);
            
            _logger.LogInformation("Deleted comment ID {CommentId}", commentId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete comment ID {CommentId}", commentId);
            return Result.Failure($"Failed to delete comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing comment.
    /// </summary>
    /// <param name="comment">The comment to update.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Result<BlogComment> UpdateComment(BlogComment comment)
    {
        if (comment == null)
            return Result<BlogComment>.Failure("Comment cannot be null");

        if (comment.CommentID <= 0)
            return Result<BlogComment>.Failure("Invalid comment ID");

        try
        {
            var existing = CommentRepo.GetSingle(comment.CommentID);
            if (existing == null)
                return Result<BlogComment>.Failure("Comment not found");

            CommentRepo.Update(comment);
            
            _logger.LogInformation("Updated comment ID {CommentId}", comment.CommentID);
            return Result<BlogComment>.Success(comment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update comment ID {CommentId}", comment.CommentID);
            return Result<BlogComment>.Failure($"Failed to update comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets admin counts for dashboard (posts, comments, users, etc.).
    /// </summary>
    /// <returns>AdminCounts object with statistics.</returns>
    public AdminCounts GetAdminCounts()
    {
        try
        {
            return CommentRepo.GetAdminCounts();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting admin counts");
            return new AdminCounts();
        }
    }

    /// <summary>
    /// Gets all comments ordered by date descending.
    /// </summary>
    /// <returns>List of all comments.</returns>
    public IEnumerable<BlogComment> GetAllComments()
    {
        try
        {
            return CommentRepo.GetAll() ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets the total count of comments.
    /// </summary>
    /// <returns>Total comment count.</returns>
    public int GetTotalCount()
    {
        try
        {
            return CommentRepo.GetTotalCount();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total comment count");
            return 0;
        }
    }

    /// <summary>
    /// Gets the count of pending (unapproved) comments.
    /// </summary>
    /// <returns>Pending comment count.</returns>
    public int GetPendingCount()
    {
        try
        {
            return CommentRepo.GetPendingCount();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending comment count");
            return 0;
        }
    }
}
