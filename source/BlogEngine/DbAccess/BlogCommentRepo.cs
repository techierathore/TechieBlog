namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for blog comments, including the anonymous submission and
/// moderation workflow.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every SQL statement that touches <c>BlogComment</c>.
/// Since [REQ-FN-022] a comment is identified by an anonymous name + email pair and moves
/// through <c>ModerationStatus</c>; the old "keyed to a signed-in user" model is gone.</para>
///
/// <para><b>Code Flow:</b> Public reads filter on <c>ModerationStatus = 'Approved'</c>; the
/// moderation queue reads <c>'PendingApproval'</c>; a freshly submitted comment sits in
/// <c>'PendingVerification'</c> and is visible to nobody.</para>
///
/// <para><b>Dependencies:</b> <see cref="GenericRepository{TEntity}"/> for the connection
/// factory, Dapper for mapping, and the stored function <c>MarkCommentEmailVerified</c>
/// from migration script 014.</para>
///
/// <para><b>Usage:</b> Registered per request by <c>EngagementSvcInitializer</c>.
/// Every statement uses <see cref="DynamicParameters"/> or an anonymous parameter object -
/// no SQL is ever concatenated from user input.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> every member has an <c>…Async</c> twin carrying a
/// <see cref="CancellationToken"/>, and every one of them opens its connection asynchronously —
/// through the protected helpers on <see cref="GenericRepository{TEntity}"/>, or through
/// <c>GetOpenConnectionAsync</c> where a member genuinely needs a connection of its own for two
/// statements. The members that were already async still called the blocking
/// <c>GetOpenConnection</c>, which parks a thread-pool thread for the whole TCP, TLS and
/// authentication handshake; that is most of the stall this requirement exists to remove, and it is
/// invisible to both the compiler and the test suite. Their tokens were added to the existing
/// signatures rather than to new overloads, because a <c>FooAsync(x, ct = default)</c> beside a
/// <c>FooAsync(x)</c> makes every existing call ambiguous at the call site.</para>
///
/// <para><b>Timestamps:</b> every <see cref="DateTime"/> bound here goes through
/// <see cref="DbTimestamp.AsTimestamp(DateTime)"/>. Npgsql picks the wire type from the value's
/// <see cref="DateTimeKind"/>, so a <c>DateTime.UtcNow</c> is sent as <c>timestamptz</c> — which
/// matches none of the <c>TIMESTAMP</c> columns this schema declares.</para>
/// </remarks>
public class BlogCommentRepo : GenericRepository<BlogComment>, IBlogCommentRepo
{
    private const string SelectAllSql = "SELECT * FROM blogcomment ORDER BY commentid DESC";

    private const string SelectParentCommentsSql = @"
            SELECT * FROM blogcomment
             WHERE postid = @BlogPostId
               AND parentcommentid IS NULL
               AND moderationstatus = @ApprovedStatus
             ORDER BY givenon DESC";

    private const string SelectChildCommentsSql = @"
            SELECT * FROM blogcomment
             WHERE postid = @BlogPostId
               AND parentcommentid IS NOT NULL
               AND moderationstatus = @ApprovedStatus
             ORDER BY givenon ASC";

    private const string SelectAdminCountsSql = @"
            SELECT
              (SELECT COUNT(*) FROM BlogPost WHERE IsDeleted = FALSE OR IsDeleted IS NULL) AS BlogCount,
              (SELECT COUNT(*) FROM blogcomment) AS CommentCount,
              (SELECT COUNT(*) FROM blogcomment WHERE moderationstatus = @QueuedStatus) AS UnAppComments,
              (SELECT COUNT(*) FROM bloguser) AS UserCount";

    private const string SelectByIdSql = "SELECT * FROM blogcomment WHERE commentid = @CommentId";

    private const string SelectPagedSql =
        "SELECT * FROM blogcomment ORDER BY givenon DESC LIMIT @PageSize OFFSET @OffSet";

    private const string SelectPagedQueuedSql = @"
            SELECT * FROM blogcomment
             WHERE moderationstatus = @QueuedStatus
             ORDER BY givenon DESC
             LIMIT @PageSize OFFSET @OffSet";

    private const string SelectQueuedSql = @"
            SELECT * FROM blogcomment
             WHERE moderationstatus = @QueuedStatus
             ORDER BY givenon DESC";

    private const string InsertReturningIdSql = @"
            INSERT INTO blogcomment
                (postid, givenon, givenby, email, comment, published, parentcommentid,
                 userid, isemailverified, moderationstatus, verifiedon,
                 authoripaddress, authoruseragent)
            VALUES
                (@PostId, @GivenOn, @GivenBy, @Email, @Comment, @IsPublished, @ParentCommentId,
                 @UserId, @IsEmailVerified, @ModerationStatus, @VerifiedOn,
                 @AuthorIpAddress, @AuthorUserAgent)
            RETURNING commentid";

    private const string UpdateSql = @"
            UPDATE blogcomment
               SET postid = @PostId,
                   givenby = @GivenBy,
                   email = @Email,
                   comment = @Comment,
                   parentcommentid = @ParentCommentId
             WHERE commentid = @CommentId";

    private const string ApproveSql = @"
            UPDATE blogcomment
               SET moderationstatus = @ModerationStatus,
                   published = TRUE
             WHERE commentid = @CommentId";

    private const string SetModerationStatusSql = @"
            UPDATE blogcomment
               SET moderationstatus = @ModerationStatus,
                   published = @IsPublished
             WHERE commentid = @CommentId";

    /// <summary>
    /// The bulk moderation UPDATE, with a placeholder for the approval-only verified guard.
    /// </summary>
    /// <remarks>
    /// The placeholder is filled from a constant chosen by a boolean, never from caller input, so
    /// the composed statement is still fully parameterised.
    /// </remarks>
    private const string SetModerationStatusBulkSqlFormat = @"
            UPDATE blogcomment
               SET moderationstatus = @ModerationStatus,
                   published = @IsPublished
             WHERE commentid = ANY(@CommentIds){0}";

    /// <summary>
    /// Approving carries the same guard as the single-comment path: a comment whose address was
    /// never confirmed can never be published, however it was selected in the grid.
    /// </summary>
    private const string VerifiedGuardSql = " AND isemailverified = TRUE";

    private const string DeleteRepliesBulkSql =
        "DELETE FROM blogcomment WHERE parentcommentid = ANY(@CommentIds)";

    private const string DeleteBulkSql = "DELETE FROM blogcomment WHERE commentid = ANY(@CommentIds)";

    private const string MarkEmailVerifiedSql = "SELECT MarkCommentEmailVerified(@pCommentId)";

    private const string DeleteSql = "DELETE FROM blogcomment WHERE commentid = @CommentId";

    private const string CountAllSql = "SELECT COUNT(*) FROM blogcomment";

    private const string CountQueuedSql =
        "SELECT COUNT(*) FROM blogcomment WHERE moderationstatus = @QueuedStatus";

    private const string CountRecentByEmailSql =
        "SELECT COUNT(*) FROM blogcomment WHERE LOWER(email) = LOWER(@Email) AND givenon >= @Since";

    private const string CountRecentByIpSql =
        "SELECT COUNT(*) FROM blogcomment WHERE authoripaddress = @IpAddress AND givenon >= @Since";

    /// <summary>
    /// Initializes a new instance of the <see cref="BlogCommentRepo"/> class.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string.</param>
    public BlogCommentRepo(string connectionString) : base(connectionString)
    {
    }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every comment, newest first, regardless of moderation state, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Administrative view only - it deliberately includes
    /// unverified and rejected rows.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All comments.</returns>
    public override async Task<IEnumerable<BlogComment>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogComment>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the approved comment thread for a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Loads approved top-level comments and approved replies in
    /// two queries and stitches the replies onto their parents in memory. Unconfirmed and
    /// unapproved comments are excluded by both queries, so they can never appear publicly. The
    /// two reads are sequential rather than concurrent: a Dapper connection is not safe for
    /// overlapping commands, and running them together would need two connections to save one
    /// round trip on a page that already has the post body in hand.</para>
    /// <para><b>Flow:</b> read parents → short-circuit when empty → read replies → graft.</para>
    /// <para><b>Side Effects:</b> None — read-only queries.</para>
    /// </remarks>
    /// <param name="postId">The post whose thread is wanted.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>Approved top-level comments, each with its approved replies attached.</returns>
    public override async Task<IEnumerable<BlogComment>> GetAllByIdAsync(long postId, CancellationToken cancellationToken = default)
    {
        var parents = await GetPostParentCommentsAsync(postId, cancellationToken).ConfigureAwait(false);
        if (parents == null)
            return Enumerable.Empty<BlogComment>();

        var parentList = parents.ToList();
        if (parentList.Count == 0)
            return parentList;

        var children = await GetPostChildCommentsAsync(postId, cancellationToken).ConfigureAwait(false);
        GraftReplies(parentList, children);
        return parentList;
    }

    /// <summary>
    /// Gets the approved top-level comments on a post, newest first, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reads <c>blogcomment</c> with inline SQL — no stored function —
    /// filtering on <c>parentcommentid IS NULL</c> and <c>moderationstatus = 'Approved'</c>. The
    /// approval filter is what keeps an unverified or rejected comment off the public page; it is
    /// applied in SQL, not by the caller, so no caller can forget it. Ordering is
    /// <c>givenon DESC</c> — newest thread first, which is the order the post page renders.</para>
    /// <para><b>Flow:</b> bind the post id and the <c>Approved</c> constant → helper opens the
    /// connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="blogPostId">The post whose top-level comments are wanted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Approved top-level comments, newest first; an empty sequence when the post has none.</returns>
    public async Task<IEnumerable<BlogComment>> GetPostParentCommentsAsync(long blogPostId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BlogPostId", blogPostId);
        parameters.Add("ApprovedStatus", CommentModerationStatus.Approved);
        return await QueryAsync<BlogComment>(
            SelectParentCommentsSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the approved replies on a post, oldest first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The mirror of <see cref="GetPostParentCommentsAsync"/> against
    /// the same <c>blogcomment</c> table, selecting <c>parentcommentid IS NOT NULL</c> and the same
    /// <c>Approved</c> guard. The ordering is deliberately the opposite of the parent query —
    /// <c>givenon ASC</c> — because a reply chain reads as a conversation and must run forwards in
    /// time even though the threads it hangs off run backwards.</para>
    /// <para><b>Flow:</b> bind the post id and the <c>Approved</c> constant → helper opens the
    /// connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="blogPostId">The post whose replies are wanted.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Approved replies across every thread on the post, oldest first; empty when there are none.</returns>
    public async Task<IEnumerable<BlogComment>> GetPostChildCommentsAsync(long blogPostId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("BlogPostId", blogPostId);
        parameters.Add("ApprovedStatus", CommentModerationStatus.Approved);
        return await QueryAsync<BlogComment>(
            SelectChildCommentsSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the four headline counts for the admin dashboard, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One statement with four scalar sub-selects — over
    /// <c>BlogPost</c>, <c>blogcomment</c> (twice) and <c>bloguser</c> — so the dashboard tiles are
    /// read in a single round trip and can never show four numbers taken at four different instants.
    /// The post count excludes soft-deleted rows (<c>IsDeleted = FALSE OR IsDeleted IS NULL</c>); the
    /// comment count deliberately does not filter on moderation state, while the queue count uses
    /// <c>PendingApproval</c> only.</para>
    /// <para><b>Flow:</b> bind the queued status → helper opens the connection asynchronously →
    /// single-row aggregate → fall back to an all-zero <c>AdminCounts</c> if no row comes back.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The dashboard counts; never <c>null</c> — an empty result yields zeroes.</returns>
    public async Task<AdminCounts> GetAdminCountsAsync(CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);

        // The aggregate query always yields exactly one row, so the fallback is defensive only: it
        // keeps the admin dashboard rendering zeroes instead of throwing if the query ever returns none.
        var counts = await QueryFirstOrDefaultAsync<AdminCounts>(
            SelectAdminCountsSql, parameters, cancellationToken).ConfigureAwait(false);
        return counts ?? new AdminCounts();
    }

    /// <summary>
    /// Gets a single comment by its 32-bit id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="commentId">The comment id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The comment, or <c>null</c>.</returns>
    public override Task<BlogComment?> GetIntSingleAsync(int commentId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(commentId, cancellationToken);
    }

    /// <summary>
    /// Gets a single comment by id, whatever its moderation state, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> No visibility filter — this is the moderator's lookup, and it
    /// must be able to fetch the row it is about to approve or reject.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="commentId">The comment id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The comment, or <c>null</c>.</returns>
    public override async Task<BlogComment?> GetSingleAsync(long commentId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("CommentId", commentId);
        return await QueryFirstOrDefaultAsync<BlogComment>(
            SelectByIdSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of all comments for the administration grid, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a busy site's history never crosses
    /// the wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A page of comments, newest first.</returns>
    public override async Task<IEnumerable<BlogComment>> GetPagedDataAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return await QueryAsync<BlogComment>(SelectPagedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of the moderation queue under its legacy name, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Kept only so the older <c>UnAppComments</c> naming still resolves;
    /// it adds nothing and forwards verbatim. New code should call
    /// <see cref="GetModerationQueueAsync"/>.</para>
    /// <para><b>Flow:</b> forward the task directly — not marked <c>async</c>, so no state machine is
    /// allocated for a pure delegation.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A page of comments awaiting approval, newest first.</returns>
    public Task<IEnumerable<BlogComment>> GetPagedUnAppCommentsAsync(int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return GetModerationQueueAsync(pageSize, offSet, cancellationToken);
    }

    /// <summary>
    /// Gets a page of comments awaiting approval, newest first, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Inline SQL over <c>blogcomment</c> filtered to
    /// <c>moderationstatus = 'PendingApproval'</c> — a comment still in <c>PendingVerification</c> is
    /// deliberately absent, because its author has not yet confirmed the address and the moderator
    /// has nothing to decide. Newest first so the freshest submissions are triaged before the backlog.</para>
    /// <para><b>Flow:</b> bind the queued status and the window → helper opens the connection
    /// asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page of the queue, or an empty sequence past the end.</returns>
    public async Task<IEnumerable<BlogComment>> GetModerationQueueAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offset);
        return await QueryAsync<BlogComment>(
            SelectPagedQueuedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the whole moderation queue unpaged, newest first, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same <c>PendingApproval</c> filter as
    /// <see cref="GetModerationQueueAsync"/> with no <c>LIMIT</c>, for callers that need the full
    /// backlog — the queue badge and the bulk-moderation screen. The absent limit is safe only
    /// because the queue is drained by hand; if it ever grows unbounded, page it.</para>
    /// <para><b>Flow:</b> bind the queued status → helper opens the connection asynchronously →
    /// buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Every comment awaiting approval, newest first; empty when the queue is clear.</returns>
    public async Task<IEnumerable<BlogComment>> GetPendingCommentsAsync(CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);
        return await QueryAsync<BlogComment>(SelectQueuedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a comment without returning its id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Written in terms of the key-returning insert so the two cannot
    /// drift apart — a half-converted insert pair is the easiest way to ship a blocking write path
    /// that looks converted.</para>
    /// <para><b>Flow:</b> delegate to <see cref="InsertToGetIdAsync"/> and discard the key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>blogcomment</c>.</para>
    /// </remarks>
    /// <param name="comment">The comment to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(BlogComment comment, CancellationToken cancellationToken = default)
    {
        await InsertToGetIdAsync(comment, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a comment and returns its generated id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Persists the anonymous identity and the moderation state.
    /// A null or empty status defaults to <c>PendingVerification</c> so a caller can never
    /// accidentally create a publicly visible comment.</para>
    /// <para><b>Flow:</b> build parameters → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>blogcomment</c>.</para>
    /// </remarks>
    /// <param name="comment">The comment to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated comment id.</returns>
    public override async Task<long> InsertToGetIdAsync(BlogComment comment, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(comment), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a comment that must not be published yet, returning its id, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The name states the caller's intent — a submission from the public
    /// form. It is not a second insert path: it forwards to <see cref="InsertToGetIdAsync"/>, whose
    /// parameter builder defaults a missing status to <c>PendingVerification</c> and derives
    /// <c>published</c> from the status, so "pending" is enforced by the shared statement rather than
    /// by this method trusting its own name.</para>
    /// <para><b>Flow:</b> forward the task directly — not marked <c>async</c>, so no state machine is
    /// allocated for a pure delegation.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>blogcomment</c>.</para>
    /// </remarks>
    /// <param name="comment">The comment to insert.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated comment id.</returns>
    public Task<long> InsertPendingAsync(BlogComment comment, CancellationToken cancellationToken = default)
    {
        return InsertToGetIdAsync(comment, cancellationToken);
    }

    /// <summary>
    /// Updates the editable fields of a comment, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Moderation state is NOT changed here - use
    /// <see cref="SetModerationStatusAsync"/> so that Published always stays in step with it.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>commentid</c>.</para>
    /// </remarks>
    /// <param name="commentToUpdate">The comment carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(BlogComment commentToUpdate, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            UpdateSql, BuildUpdateParameters(commentToUpdate), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Approves a single comment and publishes it, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Writes <c>moderationstatus = 'Approved'</c> and
    /// <c>published = TRUE</c> in one UPDATE, so the two columns can never disagree about whether a
    /// reader may see the row. Note the asymmetry with
    /// <see cref="SetModerationStatusBulkAsync"/>: the bulk path additionally requires
    /// <c>isemailverified = TRUE</c>, this single-comment path does not — it is the moderator's
    /// explicit override for one row they have looked at.</para>
    /// <para><b>Flow:</b> bind the <c>Approved</c> constant and the key → helper opens the connection
    /// asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Publishes one row; an unknown id matches nothing and is a silent
    /// no-op, so a double click is harmless.</para>
    /// </remarks>
    /// <param name="blogCommentId">The comment to approve.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public async Task ApproveBlogCommentAsync(long blogCommentId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("ModerationStatus", CommentModerationStatus.Approved);
        parameters.Add("CommentId", blogCommentId);
        await ExecuteAsync(ApproveSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves one comment to an arbitrary moderation status, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The general form of approval and rejection. <c>published</c> is
    /// never taken from the caller — it is derived from the status by <c>IsApproving</c>, so only
    /// <c>Approved</c> can ever set it and every other status clears it. That is why rejecting a
    /// comment removes it from the public page without a second statement.</para>
    /// <para><b>Flow:</b> derive the published flag from the status → bind → helper opens the
    /// connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row's moderation state and visibility; an unknown id is
    /// a silent no-op.</para>
    /// </remarks>
    /// <param name="commentId">The comment to move.</param>
    /// <param name="moderationStatus">The new status; see <c>CommentModerationStatus</c>.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public async Task SetModerationStatusAsync(long commentId, string moderationStatus, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("ModerationStatus", moderationStatus);
        parameters.Add("IsPublished", IsApproving(moderationStatus));
        parameters.Add("CommentId", commentId);
        await ExecuteAsync(SetModerationStatusSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Moves many comments to one moderation status in a single statement, without blocking.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The ids are matched with <c>commentid = ANY(@CommentIds)</c> — a
    /// single array parameter, not an interpolated <c>IN</c> list — so an arbitrarily large selection
    /// is still one parameterised statement. When the status is <c>Approved</c> the statement gains
    /// <c>AND isemailverified = TRUE</c>: a comment whose address was never confirmed can never be
    /// published, however it was ticked in the grid. That guard is the reason the returned count can
    /// be smaller than the number of ids passed in, and the caller should treat the difference as
    /// "skipped, unverified" rather than as an error.</para>
    /// <para><b>Not SQL injection:</b> the statement is composed with <c>string.Format</c>, but the
    /// only substituted value is one of two compile-time constants selected by a <c>bool</c>. No
    /// caller input reaches the SQL text; every value is a <see cref="DynamicParameters"/> binding.</para>
    /// <para><b>Flow:</b> normalise the ids (drop nulls, zeros and duplicates) → return 0 when the
    /// selection is empty → choose the guard → bind → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates the moderation state and visibility of every matched row.</para>
    /// </remarks>
    /// <param name="commentIds">The comments to move; nulls, zeros and duplicates are discarded.</param>
    /// <param name="moderationStatus">The new status; see <c>CommentModerationStatus</c>.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>The number of rows actually changed; <c>0</c> when the selection was empty.</returns>
    public async Task<int> SetModerationStatusBulkAsync(
        IEnumerable<long> commentIds, string moderationStatus, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(commentIds);
        if (ids.Length == 0)
            return 0;

        var isApproving = IsApproving(moderationStatus);

        var parameters = new DynamicParameters();
        parameters.Add("ModerationStatus", moderationStatus);
        parameters.Add("IsPublished", isApproving);
        parameters.Add("CommentIds", ids);

        var sql = string.Format(
            SetModerationStatusBulkSqlFormat, isApproving ? VerifiedGuardSql : string.Empty);

        return await ExecuteAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes many comments and their replies on one connection, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Two DELETEs against <c>blogcomment</c>, replies first. There is no
    /// cascade on <c>parentcommentid</c>, so deleting a parent on its own would leave its children in
    /// the table with a dangling parent — invisible to every read path, which all start from parents,
    /// and therefore unrecoverable through the UI. Both statements match with <c>= ANY(@CommentIds)</c>
    /// on the same bound array. Note the returned count is the parent count only; the replies removed
    /// alongside are not counted.</para>
    /// <para><b>Flow:</b> normalise the ids → return 0 when empty → take one connection with
    /// <c>GetOpenConnectionAsync</c> (the single-statement helpers do not fit two statements) →
    /// delete replies → delete parents → return the parent row count.</para>
    /// <para><b>Side Effects:</b> Permanently removes rows from <c>blogcomment</c>. Not a soft delete
    /// and not transactional — the two statements share a connection but not an explicit transaction,
    /// so a failure between them leaves the replies gone and the parents present.</para>
    /// </remarks>
    /// <param name="commentIds">The comments to delete; nulls, zeros and duplicates are discarded.</param>
    /// <param name="cancellationToken">Cancels the statements.</param>
    /// <returns>The number of top-level rows deleted; <c>0</c> when the selection was empty.</returns>
    public async Task<int> DeleteBulkAsync(IEnumerable<long> commentIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeIds(commentIds);
        if (ids.Length == 0)
            return 0;

        var parameters = new DynamicParameters();
        parameters.Add("CommentIds", ids);

        // Two statements on one connection, so the helper's one-statement-per-connection shape does
        // not apply. Replies are deleted first: parentcommentid has no cascade, so removing a parent
        // outright would orphan its children into an unreachable, invisible state.
        await using var connection = await GetOpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var deleteReplies = new CommandDefinition(
            DeleteRepliesBulkSql, parameters, cancellationToken: cancellationToken);
        await connection.ExecuteAsync(deleteReplies).ConfigureAwait(false);

        var deleteParents = new CommandDefinition(
            DeleteBulkSql, parameters, cancellationToken: cancellationToken);
        return await connection.ExecuteAsync(deleteParents).ConfigureAwait(false);
    }

    /// <summary>
    /// Records that a comment's author confirmed their address, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The one member here that calls a stored function rather than
    /// inline SQL — <c>SELECT MarkCommentEmailVerified(@pCommentId)</c>, added by migration script
    /// 014. The function owns the transition, so the promotion from <c>PendingVerification</c> to
    /// <c>PendingApproval</c>, the <c>isemailverified</c> flag and the <c>verifiedon</c> stamp are
    /// applied together in the database rather than in three statements from here. It returns the
    /// number of rows it touched, so a stale or already-consumed link yields <c>0</c> and this method
    /// returns <c>false</c> instead of throwing.</para>
    /// <para><b>Flow:</b> bind <c>pCommentId</c> → helper opens the connection asynchronously →
    /// <c>QuerySingleAsync</c> (the function always yields exactly one row) → compare to zero.</para>
    /// <para><b>Side Effects:</b> Advances one comment's verification state inside the function.</para>
    /// </remarks>
    /// <param name="commentId">The comment whose address was confirmed.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns><c>true</c> when a row was updated; <c>false</c> for an unknown or already-verified id.</returns>
    public async Task<bool> MarkEmailVerifiedAsync(long commentId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("pCommentId", commentId);
        var affected = await QuerySingleAsync<long>(
            MarkEmailVerifiedSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Deletes a single comment by id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A hard delete from <c>blogcomment</c> matched on
    /// <c>commentid</c> — the WHERE clause is present and keyed, so this can never clear the table.
    /// Unlike <see cref="DeleteBulkAsync"/> it does <b>not</b> remove replies first: deleting a
    /// parent through this path orphans its children. Prefer the bulk member, which is safe for a
    /// selection of one, whenever the row might be a thread root.</para>
    /// <para><b>Flow:</b> bind the key → helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Permanently removes one row; an unknown id is a silent no-op.</para>
    /// </remarks>
    /// <param name="commentId">The comment to delete.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the statement has run.</returns>
    public async Task DeleteAsync(long commentId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("CommentId", commentId);
        await ExecuteAsync(DeleteSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts every comment in the table, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>SELECT COUNT(*) FROM blogcomment</c> with no filter at all —
    /// unverified, rejected and unpublished rows are all included, because this drives the admin
    /// grid's paging total and must match what the grid can actually page through.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → <c>QuerySingleAsync</c>, not
    /// <c>ExecuteScalarAsync</c>, so a missing row throws rather than returning a plausible
    /// <c>0</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The total number of comment rows.</returns>
    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<int>(CountAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts the comments awaiting moderation, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts <c>moderationstatus = 'PendingApproval'</c> only, so the
    /// queue badge agrees exactly with what <see cref="GetPendingCommentsAsync"/> returns. Comments
    /// still in <c>PendingVerification</c> are excluded — a moderator cannot act on them, so counting
    /// them would show work that does not exist.</para>
    /// <para><b>Flow:</b> bind the queued status → helper opens the connection asynchronously →
    /// <c>QuerySingleAsync</c>, so an empty result throws rather than reading as "queue clear".</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of comments awaiting approval.</returns>
    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);
        return await QuerySingleAsync<int>(CountQueuedSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts one address's recent comments for rate limiting, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The email half of the anti-flood check. Matching is
    /// <c>LOWER(email) = LOWER(@Email)</c> so an attacker cannot reset their own budget by changing
    /// case, and the address is trimmed before binding for the same reason. A blank address short
    /// circuits to <c>0</c> without touching the database — an anonymous submission with no address
    /// is rejected by validation before it reaches a rate limit.</para>
    /// <para><b>Timestamps:</b> <paramref name="since"/> is bound through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/>. <c>givenon</c> is <c>TIMESTAMP</c> without
    /// time zone, and a <c>DateTime.UtcNow</c> carries <c>Kind = Utc</c>, which Npgsql would send as
    /// <c>timestamptz</c>; the helper drops the Kind without moving the instant.</para>
    /// <para><b>Flow:</b> guard the blank address → trim and normalise → helper opens the connection
    /// asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">The commenter's address; blank or whitespace yields <c>0</c>.</param>
    /// <param name="since">Start of the window, inclusive.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of comments that address posted at or after <paramref name="since"/>.</returns>
    public async Task<int> CountRecentByEmailAsync(string email, DateTime since, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            return 0;

        var parameters = new DynamicParameters();
        parameters.Add("Email", email.Trim());
        parameters.Add("Since", DbTimestamp.AsTimestamp(since));
        return await ExecuteScalarAsync<int>(
            CountRecentByEmailSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts one IP address's recent comments for rate limiting, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The address half of the anti-flood check, and the one that still
    /// applies when a spammer varies the email on every submission. Matching on
    /// <c>authoripaddress</c> is exact and case-sensitive — unlike the email path — because the
    /// column holds a normalised address written by the request pipeline, not user input. A blank
    /// address yields <c>0</c> without a round trip.</para>
    /// <para><b>Timestamps:</b> <paramref name="since"/> is bound through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> for the same <c>timestamptz</c> reason as
    /// <see cref="CountRecentByEmailAsync"/>.</para>
    /// <para><b>Flow:</b> guard the blank address → trim and normalise → helper opens the connection
    /// asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="ipAddress">The submitting address; blank or whitespace yields <c>0</c>.</param>
    /// <param name="since">Start of the window, inclusive.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of comments that address posted at or after <paramref name="since"/>.</returns>
    public async Task<int> CountRecentByIpAsync(string ipAddress, DateTime since, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return 0;

        var parameters = new DynamicParameters();
        parameters.Add("IpAddress", ipAddress.Trim());
        parameters.Add("Since", DbTimestamp.AsTimestamp(since));
        return await ExecuteScalarAsync<int>(
            CountRecentByIpSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every comment, newest first, regardless of moderation state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Administrative view only - it deliberately includes
    /// unverified and rejected rows.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>All comments.</returns>
    public override IEnumerable<BlogComment> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogComment>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets the approved comment thread for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Loads approved top-level comments and approved replies in
    /// two queries and stitches the replies onto their parents in memory. Unconfirmed and
    /// unapproved comments are excluded by both queries, so they can never appear publicly.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The post whose thread is wanted.</param>
    /// <returns>Approved top-level comments, each with its approved replies attached.</returns>
    public override IEnumerable<BlogComment> GetAllById(long postId)
    {
        var parents = GetPostParentComments(postId);
        if (parents == null)
            return Enumerable.Empty<BlogComment>();

        var parentList = parents.ToList();
        GraftReplies(parentList, GetPostChildComments(postId));
        return parentList;
    }

    /// <summary>
    /// Gets the approved top-level comments for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only <c>Approved</c> rows are returned; this is the query
    /// that guarantees an unconfirmed comment never reaches the page.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="blogPostId">The post id.</param>
    /// <returns>Approved top-level comments, newest first.</returns>
    public IEnumerable<BlogComment> GetPostParentComments(long blogPostId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("BlogPostId", blogPostId);
        parameters.Add("ApprovedStatus", CommentModerationStatus.Approved);
        return connection.Query<BlogComment>(SelectParentCommentsSql, parameters).ToList();
    }

    /// <summary>
    /// Gets the approved replies for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same approval filter as the parent query, so a reply from
    /// an unconfirmed address stays hidden even when its parent is public.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="blogPostId">The post id.</param>
    /// <returns>Approved replies, oldest first.</returns>
    public IEnumerable<BlogComment> GetPostChildComments(long blogPostId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("BlogPostId", blogPostId);
        parameters.Add("ApprovedStatus", CommentModerationStatus.Approved);
        return connection.Query<BlogComment>(SelectChildCommentsSql, parameters).ToList();
    }

    /// <summary>
    /// Gets the dashboard counters.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The "unapproved" counter reports the moderation queue -
    /// confirmed comments awaiting a decision - not everything that is merely unpublished.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>Populated <see cref="AdminCounts"/>.</returns>
    public AdminCounts GetAdminCounts()
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);
        return connection.QueryFirstOrDefault<AdminCounts>(SelectAdminCountsSql, parameters)
            ?? new AdminCounts();
    }

    /// <summary>
    /// Gets a single comment by its 32-bit id.
    /// </summary>
    /// <param name="commentId">The comment id.</param>
    /// <returns>The comment, or null.</returns>
    public override BlogComment? GetIntSingle(int commentId)
    {
        return GetSingle(commentId);
    }

    /// <summary>
    /// Gets a single comment by id, whatever its moderation state.
    /// </summary>
    /// <param name="commentId">The comment id.</param>
    /// <returns>The comment, or null.</returns>
    public override BlogComment? GetSingle(long commentId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("CommentId", commentId);
        return connection.QueryFirstOrDefault<BlogComment>(SelectByIdSql, parameters);
    }

    /// <summary>
    /// Gets a page of all comments for the administration grid.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>A page of comments, newest first.</returns>
    public override IEnumerable<BlogComment> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return connection.Query<BlogComment>(SelectPagedSql, parameters).ToList();
    }

    /// <summary>
    /// Gets a page of the moderation queue.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Confirmed-but-undecided comments only.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>Queued comments, newest first.</returns>
    public IEnumerable<BlogComment> GetPagedUnAppComments(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);
        parameters.Add("PageSize", pageSize);
        parameters.Add("OffSet", offSet);
        return connection.Query<BlogComment>(SelectPagedQueuedSql, parameters).ToList();
    }

    /// <summary>
    /// Inserts a comment without returning its id.
    /// </summary>
    /// <param name="comment">The comment to insert.</param>
    public override void Insert(BlogComment comment)
    {
        InsertToGetId(comment);
    }

    /// <summary>
    /// Inserts a comment and returns its generated id.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Persists the anonymous identity and the moderation state.
    /// A null or empty status defaults to <c>PendingVerification</c> so a caller can never
    /// accidentally create a publicly visible comment.</para>
    /// <para><b>Side Effects:</b> Inserts one row.</para>
    /// </remarks>
    /// <param name="comment">The comment to insert.</param>
    /// <returns>The generated comment id.</returns>
    public override long InsertToGetId(BlogComment comment)
    {
        using var connection = GetOpenConnection();
        return connection.QuerySingle<long>(InsertReturningIdSql, BuildInsertParameters(comment));
    }

    /// <summary>
    /// Updates the editable fields of a comment.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Moderation state is NOT changed here - use
    /// <see cref="SetModerationStatusAsync"/> so that Published always stays in step with it.</para>
    /// <para><b>Side Effects:</b> Updates one row.</para>
    /// </remarks>
    /// <param name="commentToUpdate">The comment carrying the new values.</param>
    public override void Update(BlogComment commentToUpdate)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(commentToUpdate));
    }

    /// <summary>
    /// Approves a comment, making it publicly visible.
    /// </summary>
    /// <param name="blogCommentId">The comment to approve.</param>
    public void ApproveBlogComment(long blogCommentId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("ModerationStatus", CommentModerationStatus.Approved);
        parameters.Add("CommentId", blogCommentId);
        connection.Execute(ApproveSql, parameters);
    }

    /// <summary>
    /// Deletes a comment permanently.
    /// </summary>
    /// <param name="commentId">The comment to delete.</param>
    public void Delete(long commentId)
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("CommentId", commentId);
        connection.Execute(DeleteSql, parameters);
    }

    /// <summary>
    /// Gets the whole moderation queue.
    /// </summary>
    /// <returns>Confirmed comments awaiting a decision, newest first.</returns>
    public IEnumerable<BlogComment> GetPendingComments()
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);
        return connection.Query<BlogComment>(SelectQueuedSql, parameters).ToList();
    }

    /// <summary>
    /// Gets the total number of comments in any state.
    /// </summary>
    /// <returns>The comment count.</returns>
    public int GetTotalCount()
    {
        using var connection = GetOpenConnection();
        return connection.QuerySingle<int>(CountAllSql);
    }

    /// <summary>
    /// Gets the size of the moderation queue.
    /// </summary>
    /// <returns>The number of confirmed comments awaiting a decision.</returns>
    public int GetPendingCount()
    {
        using var connection = GetOpenConnection();
        var parameters = new DynamicParameters();
        parameters.Add("QueuedStatus", CommentModerationStatus.PendingApproval);
        return connection.QuerySingle<int>(CountQueuedSql, parameters);
    }

    // =================================================================================================
    // Shared helpers.
    // =================================================================================================

    /// <summary>
    /// Attaches each reply to the parent comment it belongs to.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The thread is two levels deep, so a single pass keyed on
    /// <c>ParentCommentID</c> is enough. A reply whose parent is not in the list is dropped, which
    /// is correct: its parent was filtered out as unapproved, so the reply must not be shown either.</para>
    /// <para><b>Flow:</b> group the replies once → assign each parent its group.</para>
    /// <para><b>Side Effects:</b> Replaces <c>Replies</c> on every parent in the list.</para>
    /// </remarks>
    /// <param name="parents">The approved top-level comments.</param>
    /// <param name="children">The approved replies; may be null.</param>
    private static void GraftReplies(List<BlogComment> parents, IEnumerable<BlogComment>? children)
    {
        var repliesByParent = (children ?? Enumerable.Empty<BlogComment>())
            .Where(reply => reply.ParentCommentID.HasValue)
            .GroupBy(reply => reply.ParentCommentID!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

        foreach (var parent in parents)
        {
            parent.Replies = repliesByParent.TryGetValue(parent.CommentID, out var replies)
                ? replies
                : [];
        }
    }

    /// <summary>
    /// Tests whether a moderation status publishes the comment.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>Published</c> is set only for <c>Approved</c>; every other
    /// status clears it, so a rejected or re-queued comment disappears from the public page. The
    /// comparison ignores case so a status persisted with different casing still publishes.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="moderationStatus">The status being applied.</param>
    /// <returns><c>true</c> when the comment should become visible.</returns>
    private static bool IsApproving(string moderationStatus)
    {
        return string.Equals(
            moderationStatus, CommentModerationStatus.Approved, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reduces a caller's id sequence to a clean, duplicate-free array.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A selection built from a UI grid can contain repeats and
    /// placeholder zeros; neither should reach the database.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="commentIds">The raw ids, possibly null.</param>
    /// <returns>The distinct positive ids.</returns>
    private static long[] NormalizeIds(IEnumerable<long> commentIds)
    {
        return commentIds == null
            ? []
            : commentIds.Where(id => id > 0).Distinct().ToArray();
    }

    /// <summary>
    /// Builds the parameter set for an insert, defaulting the moderation state safely.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A missing status becomes <c>PendingVerification</c>, so a caller
    /// that forgets to set one cannot accidentally create a publicly visible comment. Both
    /// timestamps are normalised to <see cref="DateTimeKind.Unspecified"/> because Npgsql picks the
    /// wire type from the value's Kind, and a <c>Utc</c> value would be sent as <c>timestamptz</c>
    /// where the column is <c>TIMESTAMP</c>.</para>
    /// <para><b>Flow:</b> default the status → default and normalise the stamps → bind.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="comment">The comment being inserted.</param>
    /// <returns>Populated Dapper parameters.</returns>
    private static DynamicParameters BuildInsertParameters(BlogComment comment)
    {
        var moderationStatus = string.IsNullOrWhiteSpace(comment.ModerationStatus)
            ? CommentModerationStatus.PendingVerification
            : comment.ModerationStatus;

        var givenOn = comment.GivenOn == default ? DateTime.UtcNow : comment.GivenOn;

        var parameters = new DynamicParameters();
        parameters.Add("PostId", comment.PostID);
        parameters.Add("GivenOn", DbTimestamp.AsTimestamp(givenOn));
        parameters.Add("GivenBy", comment.GivenBy);
        parameters.Add("Email", comment.Email);
        parameters.Add("Comment", comment.Comment);
        parameters.Add("IsPublished", IsApproving(moderationStatus));
        parameters.Add("ParentCommentId", comment.ParentCommentID);
        parameters.Add("UserId", comment.UserId);
        parameters.Add("IsEmailVerified", comment.IsEmailVerified);
        parameters.Add("ModerationStatus", moderationStatus);
        parameters.Add("VerifiedOn", DbTimestamp.AsTimestamp(comment.VerifiedOn));
        parameters.Add("AuthorIpAddress", comment.AuthorIpAddress);
        parameters.Add("AuthorUserAgent", comment.AuthorUserAgent);
        return parameters;
    }

    /// <summary>
    /// Builds the parameter set shared by both update paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the editable fields are bound; the moderation columns are
    /// deliberately absent so an edit cannot silently publish or unpublish a comment.</para>
    /// <para><b>Flow:</b> bind the editable columns and the key.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="comment">The comment being written.</param>
    /// <returns>Populated Dapper parameters.</returns>
    private static DynamicParameters BuildUpdateParameters(BlogComment comment)
    {
        var parameters = new DynamicParameters();
        parameters.Add("PostId", comment.PostID);
        parameters.Add("GivenBy", comment.GivenBy);
        parameters.Add("Email", comment.Email);
        parameters.Add("Comment", comment.Comment);
        parameters.Add("ParentCommentId", comment.ParentCommentID);
        parameters.Add("CommentId", comment.CommentID);
        return parameters;
    }
}
