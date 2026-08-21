using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access for comments, their moderation state and the spam guard's counters.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the <c>BlogComment</c> table across the whole anonymous-comment
/// lifecycle: pending verification, moderation queue, approval and public display.</para>
///
/// <para><b>Code Flow:</b> a comment crosses two independent gates, and this contract has a distinct
/// member for each stage between them.</para>
/// <list type="number">
///   <item>Submit — <c>CommentSpamGuard</c> calls <see cref="CountRecentByEmailAsync"/> and
///         <see cref="CountRecentByIpAsync"/> to rate-limit, then <c>CommentSvc</c> writes the row with
///         <see cref="InsertPendingAsync"/>: unverified and unapproved.</item>
///   <item>Gate 1, the author — the opt-in link flips
///         <see cref="MarkEmailVerifiedAsync"/>.</item>
///   <item>Gate 2, the moderator — the queue is read with <see cref="GetModerationQueueAsync"/> (or the
///         older <see cref="GetPendingCommentsAsync"/> / <see cref="GetPagedUnAppCommentsAsync"/>) and
///         cleared with <see cref="SetModerationStatusAsync"/>, <see cref="ApproveBlogCommentAsync"/>,
///         or their bulk forms.</item>
///   <item>Display — the post page reads <see cref="GetPostParentCommentsAsync"/> and
///         <see cref="GetPostChildCommentsAsync"/> separately and grafts the replies onto their parents
///         in memory; there is no recursive query.</item>
///   <item>Count — <see cref="GetTotalCountAsync"/>, <see cref="GetPendingCountAsync"/> and
///         <see cref="GetAdminCountsAsync"/> feed the dashboard.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.BlogCommentRepo</c>.</para>
///
/// <para><b>Usage:</b> Injected into <c>CommentSvc</c> and <c>CommentSpamGuard</c>. Nothing is
/// publicly visible until both the address is confirmed and a moderator approves it — the two gates are
/// independent, so a caller must never infer one from the other. Both display reads apply that filter
/// in SQL, so an unapproved comment cannot reach a page even if the caller forgets to check. This
/// contract has no <c>Result</c> surface: expected outcomes are expressed in the return value (a
/// <c>bool</c>, a row count, <c>null</c>, an empty sequence) and any data-access failure is thrown.</para>
///
/// <para><b>Cancellation (REQ-NFR-026).</b> The nine members below the <c>…Async</c> divider carry
/// default implementations that go through <c>RepoSyncBridge</c>: a pre-cancelled token yields a
/// cancelled task and a thrown exception yields a faulted task, so failures observed through
/// <c>await</c> look the same either way. <b>They are still not asynchronous</b> — the operation runs
/// inline on the calling thread, parks it for the whole round trip, and a token cancelled <i>after</i>
/// the call starts has no effect. The members declared above them are abstract and genuinely
/// asynchronous in every implementer. <c>BlogCommentRepo</c> overrides the bridged nine; an implementer
/// that still inherits them is unconverted, however green the build is.</para>
/// </remarks>
public interface IBlogCommentRepo : IGenericRepository<BlogComment>
{
    /// <summary>
    /// Approves a comment so it becomes publicly visible.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Approval is the only transition that sets <c>Published</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row.</para>
    /// </remarks>
    /// <param name="blogCommentId">The comment to approve.</param>
    void ApproveBlogComment(long blogCommentId);

    /// <summary>
    /// Reads a page of comments still awaiting approval.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>The unapproved comments for the requested page.</returns>
    IEnumerable<BlogComment> GetPagedUnAppComments(int pageSize, int offSet);

    /// <summary>
    /// Gets the top-level comments on a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Filters on the published/approved state server-side, so an
    /// unapproved comment can never reach the page even if the caller forgets to check.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="blogPostId">The post whose thread is being read.</param>
    /// <returns>The post's visible root comments.</returns>
    IEnumerable<BlogComment> GetPostParentComments(long blogPostId);

    /// <summary>
    /// Gets the replies on a post, for grafting onto their parents.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies the same visibility filter as the root query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="blogPostId">The post whose thread is being read.</param>
    /// <returns>The post's visible reply comments.</returns>
    IEnumerable<BlogComment> GetPostChildComments(long blogPostId);

    /// <summary>
    /// Gets the headline counts shown on the admin dashboard.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One aggregate query covering posts, comments, the moderation
    /// backlog and users, so the dashboard costs a single round trip.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <returns>The populated counts; never <c>null</c>.</returns>
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

    /// <summary>
    /// Inserts an anonymous comment in its initial, invisible state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Writes the anonymous identity (name, email), the abuse
    /// forensics columns and the caller-supplied moderation status. Published is always
    /// false at this point - nothing is visible before an administrator approves it.</para>
    /// <para><b>Side Effects:</b> Inserts one row into <c>BlogComment</c>.</para>
    /// </remarks>
    /// <param name="comment">The comment to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated comment id.</returns>
    Task<long> InsertPendingAsync(BlogComment comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Promotes a comment out of PendingVerification into the moderation queue.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to the <c>MarkCommentEmailVerified</c> stored
    /// function, which only touches a row still in PendingVerification. Replaying a consumed
    /// link therefore cannot resurrect a rejected comment.</para>
    /// <para><b>Side Effects:</b> Sets IsEmailVerified, VerifiedOn and ModerationStatus.</para>
    /// </remarks>
    /// <param name="commentId">The comment to promote.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>True when a row was promoted, false when it was already past that state.</returns>
    Task<bool> MarkEmailVerifiedAsync(long commentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the moderation status of a comment and keeps Published in step with it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Published is set only for <c>Approved</c>; every other
    /// status clears it, so a rejected or re-queued comment disappears from the public page.</para>
    /// <para><b>Side Effects:</b> Updates one row.</para>
    /// </remarks>
    /// <param name="commentId">The comment to change.</param>
    /// <param name="moderationStatus">One of the <see cref="CommentModerationStatus"/> values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the update has been applied.</returns>
    Task SetModerationStatusAsync(long commentId, string moderationStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the moderation status of many comments in a single statement.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The bulk equivalent of <see cref="SetModerationStatusAsync"/>,
    /// with the same Published-follows-Approved rule. One statement rather than one per id, so a
    /// moderator clearing a hundred-row queue does not make a hundred round trips and cannot leave
    /// the batch half applied.</para>
    /// <para><b>Side Effects:</b> Updates every matching row.</para>
    /// </remarks>
    /// <param name="commentIds">The comments to change; ignored when empty.</param>
    /// <param name="moderationStatus">One of the <see cref="CommentModerationStatus"/> values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The number of rows actually updated.</returns>
    Task<int> SetModerationStatusBulkAsync(
        IEnumerable<long> commentIds, string moderationStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes many comments in a single statement.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Permanent removal - use a Rejected or Spam status instead when
    /// the moderator may want the row back.</para>
    /// <para><b>Side Effects:</b> Deletes every matching row.</para>
    /// </remarks>
    /// <param name="commentIds">The comments to delete; ignored when empty.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>The number of rows actually deleted.</returns>
    Task<int> DeleteBulkAsync(IEnumerable<long> commentIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a page of the moderation queue.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only comments whose address has been confirmed
    /// (status PendingApproval) are queued; unconfirmed submissions stay invisible to
    /// moderators as well as to the public.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The queued comments, newest first.</returns>
    Task<IEnumerable<BlogComment>> GetModerationQueueAsync(
        int pageSize, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts recent comments from one email address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Feeds the spam guard's per-address rate limit.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="email">The address to count for; matched case-insensitively.</param>
    /// <param name="since">The UTC instant to count from.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of comments in the window.</returns>
    Task<int> CountRecentByEmailAsync(string email, DateTime since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts recent comments from one IP address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Feeds the spam guard's per-origin rate limit, which
    /// catches a bot cycling through disposable addresses.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="ipAddress">The origin to count for.</param>
    /// <param name="since">The UTC instant to count from.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of comments in the window.</returns>
    Task<int> CountRecentByIpAsync(string ipAddress, DateTime since, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------------------------------------
    // Async twins of the remaining blocking members — REQ-NFR-026.
    //
    // Each ships with a default implementation that runs its synchronous twin, so an implementer
    // that has not been converted — including the in-memory test doubles under tests/unit — keeps
    // compiling untouched. A bridged member still parks a thread for the whole round trip, so a
    // repository that inherits one is unconverted however green the build is.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Approves a comment so it becomes publicly visible, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Approval is the only transition that sets <c>Published</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row.</para>
    /// </remarks>
    /// <param name="blogCommentId">The comment to approve.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task ApproveBlogCommentAsync(long blogCommentId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => ApproveBlogComment(blogCommentId), cancellationToken);

    /// <summary>
    /// Reads a page of comments still awaiting approval, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Confirmed-but-undecided comments only; an unconfirmed submission
    /// is not a moderator's problem yet.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered page query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The unapproved comments for the requested page.</returns>
    Task<IEnumerable<BlogComment>> GetPagedUnAppCommentsAsync(
        int pageSize, int offSet, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetPagedUnAppComments(pageSize, offSet), cancellationToken);

    /// <summary>
    /// Gets the top-level comments on a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Filters on the approved state server-side, so an unapproved
    /// comment can never reach the page even if the caller forgets to check.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="blogPostId">The post whose thread is being read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post's visible root comments, newest first.</returns>
    Task<IEnumerable<BlogComment>> GetPostParentCommentsAsync(
        long blogPostId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetPostParentComments(blogPostId), cancellationToken);

    /// <summary>
    /// Gets the replies on a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Applies the same visibility filter as the root query, so a reply
    /// from an unconfirmed address stays hidden even when its parent is public.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="blogPostId">The post whose thread is being read.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post's visible reply comments, oldest first.</returns>
    Task<IEnumerable<BlogComment>> GetPostChildCommentsAsync(
        long blogPostId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetPostChildComments(blogPostId), cancellationToken);

    /// <summary>
    /// Gets the headline counts shown on the admin dashboard, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One aggregate query covering posts, comments, the moderation
    /// backlog and users, so the dashboard costs a single round trip.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → aggregate query → first row.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The populated counts; never <c>null</c>.</returns>
    Task<AdminCounts> GetAdminCountsAsync(CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(GetAdminCounts, cancellationToken);

    /// <summary>
    /// Deletes a comment by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Permanent removal — set a Rejected or Spam status instead when
    /// the moderator may want the row back.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes at most one row.</para>
    /// </remarks>
    /// <param name="commentId">Comment ID to delete.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    Task DeleteAsync(long commentId, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => Delete(commentId), cancellationToken);

    /// <summary>
    /// Gets the whole moderation queue, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unpaged counterpart of <see cref="GetModerationQueueAsync"/>,
    /// carrying the same "confirmed but undecided" filter.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The queued comments, newest first.</returns>
    Task<IEnumerable<BlogComment>> GetPendingCommentsAsync(CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(GetPendingComments, cancellationToken);

    /// <summary>
    /// Gets the total number of comments in any state, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts every row including rejected and unconfirmed ones — this
    /// is the administrative total, not the number a reader can see.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → COUNT → scalar.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The total comment count.</returns>
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(GetTotalCount, cancellationToken);

    /// <summary>
    /// Gets the size of the moderation queue, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts confirmed comments awaiting a decision, which is the
    /// number a moderator has to act on.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered COUNT → scalar.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The pending comment count.</returns>
    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(GetPendingCount, cancellationToken);
}
