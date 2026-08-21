using System.Data;
using BlogModels;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// In-memory stand-in for <see cref="IBlogCommentRepo"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the comment tests exercise the real service logic without a
/// PostgreSQL instance, while still modelling the moderation state machine faithfully.</para>
/// <para><b>Code Flow:</b> Comments live in a list keyed by a monotonically increasing id;
/// the promotion and status rules mirror the stored functions in migration script 014.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Set <see cref="RecentByEmailCount"/> or <see cref="RecentByIpCount"/>
/// to drive the spam guard's rate limits.</para>
/// </remarks>
public class FakeBlogCommentRepo : IBlogCommentRepo
{
    private readonly List<BlogComment> comments = new();
    private long nextId = 1;

    /// <summary>
    /// Gets the comments this fake currently holds.
    /// </summary>
    public IReadOnlyList<BlogComment> Comments => comments;

    /// <summary>
    /// Gets or sets the value returned by <see cref="CountRecentByEmailAsync"/>.
    /// </summary>
    public int RecentByEmailCount { get; set; }

    /// <summary>
    /// Gets or sets the value returned by <see cref="CountRecentByIpAsync"/>.
    /// </summary>
    public int RecentByIpCount { get; set; }

    /// <inheritdoc />
    public Task<long> InsertPendingAsync(BlogComment comment, CancellationToken cancellationToken = default)
    {
        comment.CommentID = nextId++;
        comments.Add(comment);
        return Task.FromResult(comment.CommentID);
    }

    /// <inheritdoc />
    public Task<bool> MarkEmailVerifiedAsync(long commentId, CancellationToken cancellationToken = default)
    {
        var comment = comments.FirstOrDefault(c => c.CommentID == commentId);
        if (comment == null || comment.ModerationStatus != CommentModerationStatus.PendingVerification)
            return Task.FromResult(false);

        comment.IsEmailVerified = true;
        comment.VerifiedOn = DateTime.UtcNow;
        comment.ModerationStatus = CommentModerationStatus.PendingApproval;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task SetModerationStatusAsync(
        long commentId,
        string moderationStatus,
        CancellationToken cancellationToken = default)
    {
        var comment = comments.FirstOrDefault(c => c.CommentID == commentId);
        if (comment != null)
        {
            comment.ModerationStatus = moderationStatus;
            comment.Published = moderationStatus == CommentModerationStatus.Approved;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> SetModerationStatusBulkAsync(
        IEnumerable<long> commentIds,
        string moderationStatus,
        CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(commentIds);
        var isApproving = moderationStatus == CommentModerationStatus.Approved;
        var affected = 0;

        foreach (var comment in comments.Where(c => ids.Contains(c.CommentID)))
        {
            // Mirrors the SQL guard: an unconfirmed address can never be published.
            if (isApproving && !comment.IsEmailVerified)
                continue;

            comment.ModerationStatus = moderationStatus;
            comment.Published = isApproving;
            affected++;
        }

        return Task.FromResult(affected);
    }

    /// <inheritdoc />
    public Task<int> DeleteBulkAsync(IEnumerable<long> commentIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(commentIds);
        comments.RemoveAll(c => c.ParentCommentID != null && ids.Contains(c.ParentCommentID.Value));
        return Task.FromResult(comments.RemoveAll(c => ids.Contains(c.CommentID)));
    }

    /// <summary>
    /// Reduces an id sequence to the distinct positive ids, as the real repository does.
    /// </summary>
    /// <param name="commentIds">The raw ids, possibly null.</param>
    /// <returns>The cleaned id set.</returns>
    private static HashSet<long> NormalizeIds(IEnumerable<long> commentIds)
    {
        return commentIds == null ? [] : commentIds.Where(id => id > 0).ToHashSet();
    }

    /// <inheritdoc />
    public Task<IEnumerable<BlogComment>> GetModerationQueueAsync(
        int pageSize,
        int offset,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(GetPendingComments().Skip(offset).Take(pageSize));
    }

    /// <inheritdoc />
    public Task<int> CountRecentByEmailAsync(
        string email,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RecentByEmailCount);
    }

    /// <inheritdoc />
    public Task<int> CountRecentByIpAsync(
        string ipAddress,
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(RecentByIpCount);
    }

    /// <inheritdoc />
    public void ApproveBlogComment(long blogCommentId)
    {
        SetModerationStatusAsync(blogCommentId, CommentModerationStatus.Approved).Wait();
    }

    /// <inheritdoc />
    public IEnumerable<BlogComment> GetPagedUnAppComments(int pageSize, int offSet)
    {
        return GetPendingComments().Skip(offSet).Take(pageSize);
    }

    /// <inheritdoc />
    public IEnumerable<BlogComment> GetPostParentComments(long blogPostId)
    {
        return comments.Where(c =>
            c.PostID == blogPostId &&
            c.ParentCommentID == null &&
            c.ModerationStatus == CommentModerationStatus.Approved).ToList();
    }

    /// <inheritdoc />
    public IEnumerable<BlogComment> GetPostChildComments(long blogPostId)
    {
        return comments.Where(c =>
            c.PostID == blogPostId &&
            c.ParentCommentID != null &&
            c.ModerationStatus == CommentModerationStatus.Approved).ToList();
    }

    /// <inheritdoc />
    public AdminCounts GetAdminCounts()
    {
        return new AdminCounts { CommentCount = comments.Count, UnAppComments = GetPendingCount() };
    }

    /// <inheritdoc />
    public void Delete(long commentId)
    {
        comments.RemoveAll(c => c.CommentID == commentId);
    }

    /// <inheritdoc />
    public IEnumerable<BlogComment> GetPendingComments()
    {
        return comments
            .Where(c => c.ModerationStatus == CommentModerationStatus.PendingApproval)
            .ToList();
    }

    /// <inheritdoc />
    public int GetTotalCount() => comments.Count;

    /// <inheritdoc />
    public int GetPendingCount() => GetPendingComments().Count();

    /// <inheritdoc />
    public IDbConnection GetOpenConnection() => throw new NotSupportedException("The fake repository has no database.");

    /// <inheritdoc />
    public long InsertToGetId(BlogComment comment)
    {
        comment.CommentID = nextId++;
        comments.Add(comment);
        return comment.CommentID;
    }

    /// <inheritdoc />
    public void Insert(BlogComment comment) => InsertToGetId(comment);

    /// <inheritdoc />
    public void Update(BlogComment commentToUpdate)
    {
        var index = comments.FindIndex(c => c.CommentID == commentToUpdate.CommentID);
        if (index >= 0)
            comments[index] = commentToUpdate;
    }

    /// <inheritdoc />
    public BlogComment? GetSingle(long commentId) => comments.FirstOrDefault(c => c.CommentID == commentId);

    /// <inheritdoc />
    public BlogComment? GetIntSingle(int commentId) => GetSingle(commentId);

    /// <inheritdoc />
    public IEnumerable<BlogComment> GetAll() => comments.ToList();

    /// <inheritdoc />
    public IEnumerable<BlogComment> GetPagedData(int pageSize, int offSet) => comments.Skip(offSet).Take(pageSize).ToList();

    /// <inheritdoc />
    public IEnumerable<BlogComment> GetAllById(long postId)
    {
        var parents = GetPostParentComments(postId).ToList();
        var children = GetPostChildComments(postId).ToList();
        foreach (var parent in parents)
        {
            parent.Replies = children.Where(c => c.ParentCommentID == parent.CommentID).ToList();
        }

        return parents;
    }
}
