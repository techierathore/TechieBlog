using System.Net.Mail;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Business logic for blog comments: anonymous submission, double opt-in, spam screening,
/// the moderation workflow and the administrative counters.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [REQ-FN-022] re-keyed comments from a signed-in user id to an
/// anonymous name + email pair. This service owns the rules that make that safe: a captcha, a
/// spam screen, mandatory email confirmation and administrator approval - in that order.</para>
///
/// <para><b>Code Flow:</b> <see cref="SubmitCommentAsync"/> validates, proves the visitor is
/// human, screens for spam, writes the comment in an invisible state and - unless the address
/// is already on the verified registry - issues a confirmation link. Confirming moves the
/// comment into the moderation queue; only an administrator's approval makes it public - unless
/// the <c>Blog.AreCommentsModerated</c> site setting is off, in which case a confirmed comment
/// publishes straight away [BRD-38]. An unconfirmed comment is visible to nobody either way, not
/// even to moderators.</para>
///
/// <para><b>Dependencies:</b> <see cref="IBlogCommentRepo"/>, <see cref="ICaptchaService"/>,
/// <see cref="ICommentSpamGuard"/>, <see cref="IEmailVerificationService"/>,
/// <see cref="ISiteSettingsService"/>.</para>
///
/// <para><b>Usage:</b> Registered per request. The read methods are safe for the public page
/// because the repository filters on the approved status.</para>
/// </remarks>
public class CommentSvc
{
    private readonly IBlogCommentRepo commentRepo;
    private readonly ICaptchaService captchaService;
    private readonly ICommentSpamGuard spamGuard;
    private readonly IEmailVerificationService emailVerificationService;
    private readonly ISiteSettingsService siteSettingsService;
    private readonly ILogger<CommentSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommentSvc"/> class.
    /// </summary>
    /// <param name="commentRepo">Comment data access.</param>
    /// <param name="captchaService">Proves the submitter is human.</param>
    /// <param name="spamGuard">Screens submissions for automated abuse.</param>
    /// <param name="emailVerificationService">Issues and tracks double opt-in confirmations.</param>
    /// <param name="siteSettingsService">Supplies the comment-moderation site setting [BRD-38].</param>
    /// <param name="logger">Logger for operational and security events.</param>
    public CommentSvc(
        IBlogCommentRepo commentRepo,
        ICaptchaService captchaService,
        ICommentSpamGuard spamGuard,
        IEmailVerificationService emailVerificationService,
        ISiteSettingsService siteSettingsService,
        ILogger<CommentSvc> logger)
    {
        this.commentRepo = commentRepo;
        this.captchaService = captchaService;
        this.spamGuard = spamGuard;
        this.emailVerificationService = emailVerificationService;
        this.siteSettingsService = siteSettingsService;
        this.logger = logger;
    }

    /// <summary>
    /// Reads whether an approved-before-display step is currently required.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [BRD-38] The site setting decides. Any failure reading it is
    /// answered with <c>true</c> - the safe direction, because moderating a comment that did not
    /// need it is recoverable and publishing one that did is not.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns><c>true</c> when a moderator must approve a comment before it is shown.</returns>
    private async Task<bool> IsModerationRequiredAsync()
    {
        try
        {
            var settings = await siteSettingsService.GetSettingsAsync().ConfigureAwait(false);
            return settings?.AreCommentsModerated ?? true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the comment-moderation setting; moderating by default");
            return true;
        }
    }

    /// <summary>
    /// Accepts a comment from an anonymous visitor.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Four gates in order - field validation, captcha, spam screen,
    /// email confirmation. The comment row is written only after the first three pass, and it is
    /// written in <c>PendingVerification</c> unless the address is already verified, in which
    /// case it goes straight to the moderation queue. Nothing this method writes is ever
    /// publicly visible.</para>
    /// <para><b>Flow:</b> validate, captcha, spam, insert, issue token (or queue directly).</para>
    /// <para><b>Side Effects:</b> Inserts a comment; may insert a verification token and send an
    /// email; consumes the captcha challenge whether or not the answer was right.</para>
    /// </remarks>
    /// <param name="submission">The raw visitor submission.</param>
    /// <returns>What the visitor should be told, or a failure carrying a safe message.</returns>
    public async Task<Result<CommentSubmissionOutcome>> SubmitCommentAsync(CommentSubmission submission)
    {
        var validation = ValidateSubmission(submission);
        if (validation.IsFailure)
            return Result<CommentSubmissionOutcome>.Failure(validation.ErrorMessage);

        var gateResult = await RunAbuseGatesAsync(submission).ConfigureAwait(false);
        if (gateResult.IsFailure)
            return Result<CommentSubmissionOutcome>.Failure(gateResult.ErrorMessage);

        try
        {
            var isVerified = await emailVerificationService
                .IsAddressVerifiedAsync(submission.AuthorEmail).ConfigureAwait(false);
            var isModerated = await IsModerationRequiredAsync().ConfigureAwait(false);
            var comment = BuildPendingComment(submission, isVerified, isModerated);
            comment.CommentID = await commentRepo.InsertPendingAsync(comment).ConfigureAwait(false);
            return await FinishSubmissionAsync(submission, comment, isVerified).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to accept a comment for post {PostId}", submission.PostId);
            return Result<CommentSubmissionOutcome>.Failure("We could not save your comment. Please try again.");
        }
    }

    /// <summary>
    /// Runs the captcha and spam gates.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The captcha is required for every anonymous submission; a
    /// signed-in visitor is exempt because the sign-in already proved they are human. A failed
    /// captcha blocks the write and the caller must render a NEW challenge. The spam verdict is
    /// reported to the visitor in deliberately vague terms.</para>
    /// <para><b>Side Effects:</b> Consumes the captcha challenge.</para>
    /// </remarks>
    /// <param name="submission">The submission under test.</param>
    /// <returns>Success when both gates pass.</returns>
    private async Task<Result> RunAbuseGatesAsync(CommentSubmission submission)
    {
        if (submission.UserId is not > 0)
        {
            var captchaResult = captchaService.Validate(submission.CaptchaChallengeId, submission.CaptchaAnswer);
            if (captchaResult.IsFailure)
                return captchaResult;
        }

        var spamVerdict = await spamGuard.EvaluateAsync(submission).ConfigureAwait(false);
        if (spamVerdict.IsSpam)
            return Result.Failure("Your comment could not be accepted. Please try again later.");

        return Result.Success();
    }

    /// <summary>
    /// Issues the confirmation link, or reports that the comment is already queued.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A verified address skips confirmation entirely. When a token
    /// cannot be issued the just-written comment is deleted, so no invisible orphan is left that
    /// the visitor can never rescue.</para>
    /// <para><b>Side Effects:</b> May send an email; may delete the comment on failure.</para>
    /// </remarks>
    /// <param name="submission">The original submission.</param>
    /// <param name="comment">The comment that was written.</param>
    /// <param name="isVerified">Whether the address was already verified.</param>
    /// <returns>The outcome to show the visitor.</returns>
    private async Task<Result<CommentSubmissionOutcome>> FinishSubmissionAsync(
        CommentSubmission submission, BlogComment comment, bool isVerified)
    {
        if (isVerified)
        {
            var isApproved = comment.ModerationStatus == CommentModerationStatus.Approved;
            logger.LogInformation("Comment {CommentId} on post {PostId} accepted as {Status}",
                comment.CommentID, comment.PostID, comment.ModerationStatus);
            return Result<CommentSubmissionOutcome>.Success(new CommentSubmissionOutcome
            {
                CommentId = comment.CommentID,
                ModerationStatus = comment.ModerationStatus,
                IsEmailVerificationRequired = false,
                Message = isApproved
                    ? "Thank you. Your comment has been posted."
                    : "Thank you. Your comment is awaiting moderation."
            });
        }

        var issued = await emailVerificationService.IssueAsync(
            submission.AuthorEmail, submission.AuthorName, EmailVerificationPurpose.Comment,
            comment.CommentID, submission.IpAddress).ConfigureAwait(false);

        if (issued.IsFailure)
        {
            commentRepo.Delete(comment.CommentID);
            return Result<CommentSubmissionOutcome>.Failure(issued.ErrorMessage);
        }

        return Result<CommentSubmissionOutcome>.Success(new CommentSubmissionOutcome
        {
            CommentId = comment.CommentID,
            ModerationStatus = CommentModerationStatus.PendingVerification,
            IsEmailVerificationRequired = true,
            Message = "Almost there - check your inbox and click the link to confirm your comment."
        });
    }

    /// <summary>
    /// Maps a submission onto a comment entity in its initial, invisible state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The anti-abuse fields are dropped here - only the identity,
    /// the body and the forensic columns are persisted.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="submission">The visitor submission.</param>
    /// <param name="isVerified">Whether the address is already on the verified registry.</param>
    /// <param name="isModerated">Whether a moderator must approve before display [BRD-38].</param>
    /// <returns>The entity to insert.</returns>
    private static BlogComment BuildPendingComment(
        CommentSubmission submission, bool isVerified, bool isModerated)
    {
        var now = DateTime.UtcNow;

        // An unconfirmed address is invisible regardless of the setting - the moderation
        // question only arises once the commenter has proved the address is theirs.
        var status = isVerified
            ? isModerated ? CommentModerationStatus.PendingApproval : CommentModerationStatus.Approved
            : CommentModerationStatus.PendingVerification;

        return new BlogComment
        {
            PostID = submission.PostId,
            ParentCommentID = submission.ParentCommentId is > 0 ? submission.ParentCommentId : null,
            GivenOn = now,
            GivenBy = submission.AuthorName.Trim(),
            Email = submission.AuthorEmail.Trim(),
            Comment = submission.CommentText.Trim(),
            UserId = submission.UserId,
            IsEmailVerified = isVerified,
            VerifiedOn = isVerified ? now : null,
            ModerationStatus = status,
            Published = status == CommentModerationStatus.Approved,
            AuthorIpAddress = submission.IpAddress,
            AuthorUserAgent = Truncate(submission.UserAgent, 500)
        };
    }

    /// <summary>
    /// Checks the submitted fields.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Server-side validation is authoritative; the data annotations
    /// on <see cref="CommentSubmission"/> only help the browser.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="submission">The submission under test.</param>
    /// <returns>Success when every field is acceptable.</returns>
    private static Result ValidateSubmission(CommentSubmission submission)
    {
        if (submission == null)
            return Result.Failure("No comment was supplied.");

        if (submission.PostId <= 0)
            return Result.Failure("The comment is not attached to a post.");

        if (string.IsNullOrWhiteSpace(submission.AuthorName))
            return Result.Failure("Please tell us your name.");

        if (!IsEmailWellFormed(submission.AuthorEmail))
            return Result.Failure("Please enter a valid email address.");

        if (string.IsNullOrWhiteSpace(submission.CommentText))
            return Result.Failure("Please write a comment.");

        return submission.CommentText.Trim().Length > 850
            ? Result.Failure("Comments are limited to 850 characters.")
            : Result.Success();
    }

    /// <summary>
    /// Tests whether an address is syntactically usable.
    /// </summary>
    /// <param name="email">The address to test.</param>
    /// <returns>True when the address parses.</returns>
    private static bool IsEmailWellFormed(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        return MailAddress.TryCreate(email.Trim(), out _);
    }

    /// <summary>
    /// Trims a string to a maximum length.
    /// </summary>
    /// <param name="value">The value to trim; may be null.</param>
    /// <param name="maximumLength">The maximum length to keep.</param>
    /// <returns>The trimmed value, or null.</returns>
    private static string Truncate(string value, int maximumLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
            return value;

        return value.Substring(0, maximumLength);
    }

    /// <summary>
    /// Gets the approved comment thread for a post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The repository filters on the approved status, so nothing
    /// unconfirmed or unmoderated can leak onto the page.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The post id.</param>
    /// <returns>Approved top-level comments with their replies; empty on error.</returns>
    public IEnumerable<BlogComment> GetCommentsByPostId(long postId)
    {
        try
        {
            return commentRepo.GetAllById(postId) ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting comments for post ID: {PostId}", postId);
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets the whole moderation queue.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only confirmed comments are queued - an unconfirmed one is
    /// not a moderator's problem yet.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>Comments awaiting a decision; empty on error.</returns>
    public IEnumerable<BlogComment> GetPendingComments()
    {
        try
        {
            return commentRepo.GetPendingComments() ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting pending comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets a page of the moderation queue.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>Queued comments; empty on error.</returns>
    public async Task<IEnumerable<BlogComment>> GetModerationQueueAsync(int pageSize, int offset)
    {
        try
        {
            return await commentRepo.GetModerationQueueAsync(pageSize, offset).ConfigureAwait(false)
                ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting the moderation queue");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets a page of all comments for the administration grid.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>A page of comments; empty on error.</returns>
    public IEnumerable<BlogComment> GetPagedComments(int pageSize, int offset)
    {
        try
        {
            return commentRepo.GetPagedData(pageSize, offset) ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting paged comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets a page of the moderation queue for the administration grid.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>A page of queued comments; empty on error.</returns>
    public IEnumerable<BlogComment> GetPagedUnapprovedComments(int pageSize, int offset)
    {
        try
        {
            return commentRepo.GetPagedUnAppComments(pageSize, offset) ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting paged unapproved comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets a single comment whatever its state.
    /// </summary>
    /// <param name="commentId">The comment id.</param>
    /// <returns>The comment, or null.</returns>
    public BlogComment? GetComment(long commentId)
    {
        try
        {
            return commentRepo.GetSingle(commentId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting comment ID: {CommentId}", commentId);
            return null;
        }
    }

    /// <summary>
    /// Adds a comment on behalf of an administrator.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the back-office path: the address is taken on trust,
    /// so the comment skips confirmation and lands straight in the moderation queue. It still
    /// does not become visible without an approval. Visitor submissions must use
    /// <see cref="SubmitCommentAsync"/>, which applies the captcha and spam gates.</para>
    /// <para><b>Side Effects:</b> Inserts one comment row.</para>
    /// </remarks>
    /// <param name="comment">The comment to add.</param>
    /// <returns>The created comment, or a failure message.</returns>
    public Result<BlogComment> AddComment(BlogComment comment)
    {
        if (comment == null)
            return Result<BlogComment>.Failure("Comment cannot be null");

        if (comment.PostID <= 0)
            return Result<BlogComment>.Failure("Invalid post ID");

        if (string.IsNullOrWhiteSpace(comment.GivenBy) || string.IsNullOrWhiteSpace(comment.Email))
            return Result<BlogComment>.Failure("Name and email are required");

        if (string.IsNullOrWhiteSpace(comment.Comment))
            return Result<BlogComment>.Failure("Comment text is required");

        return InsertAdministrativeComment(comment);
    }

    /// <summary>
    /// Writes a back-office comment into the moderation queue.
    /// </summary>
    /// <param name="comment">The validated comment.</param>
    /// <returns>The created comment, or a failure message.</returns>
    private Result<BlogComment> InsertAdministrativeComment(BlogComment comment)
    {
        try
        {
            comment.GivenOn = DateTime.UtcNow;
            comment.Published = false;
            comment.IsEmailVerified = true;
            comment.VerifiedOn = DateTime.UtcNow;
            comment.ModerationStatus = CommentModerationStatus.PendingApproval;
            comment.CommentID = commentRepo.InsertToGetId(comment);
            logger.LogInformation("Created comment ID {CommentId} for post {PostId} by {GivenBy}",
                comment.CommentID, comment.PostID, comment.GivenBy);
            return Result<BlogComment>.Success(comment);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create comment for post {PostId}", comment.PostID);
            return Result<BlogComment>.Failure($"Failed to create comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Approves a comment, making it publicly visible.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Refuses to approve a comment whose address has not been
    /// confirmed, which is the last line of defence behind the "an unconfirmed comment never
    /// appears publicly" rule.</para>
    /// <para><b>Side Effects:</b> Sets the status to Approved and Published to true.</para>
    /// </remarks>
    /// <param name="commentId">The comment to approve.</param>
    /// <returns>Success, or a failure message.</returns>
    public Result ApproveComment(long commentId)
    {
        if (commentId <= 0)
            return Result.Failure("Invalid comment ID");

        try
        {
            var existing = commentRepo.GetSingle(commentId);
            if (existing == null)
                return Result.Failure("Comment not found");

            if (!existing.IsEmailVerified)
                return Result.Failure("This comment's email address has not been confirmed yet.");

            commentRepo.ApproveBlogComment(commentId);
            logger.LogInformation("Approved comment ID {CommentId}", commentId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to approve comment ID {CommentId}", commentId);
            return Result.Failure($"Failed to approve comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Rejects a comment, or files it as spam.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Rejection keeps the row - the evidence is useful and the
    /// address can be blocked later - but clears Published so the comment disappears.</para>
    /// <para><b>Side Effects:</b> Updates the moderation status.</para>
    /// </remarks>
    /// <param name="commentId">The comment to reject.</param>
    /// <param name="isSpam">True to file it as spam rather than a plain rejection.</param>
    /// <returns>Success, or a failure message.</returns>
    public async Task<Result> RejectCommentAsync(long commentId, bool isSpam)
    {
        if (commentId <= 0)
            return Result.Failure("Invalid comment ID");

        try
        {
            var status = isSpam ? CommentModerationStatus.Spam : CommentModerationStatus.Rejected;
            await commentRepo.SetModerationStatusAsync(commentId, status).ConfigureAwait(false);
            logger.LogInformation("Comment {CommentId} moved to {Status}", commentId, status);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to reject comment ID {CommentId}", commentId);
            return Result.Failure("Failed to reject the comment.");
        }
    }

    /// <summary>
    /// Approves many comments at once from the moderation queue.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [BRD-39] The bulk half of the moderation queue. Comments whose
    /// address was never confirmed are skipped rather than published, so the returned count can be
    /// lower than the number selected - the caller should report that difference honestly.</para>
    /// <para><b>Side Effects:</b> Sets the status to Approved and Published to true.</para>
    /// </remarks>
    /// <param name="commentIds">The comments to approve.</param>
    /// <returns>The number approved, or a failure message.</returns>
    public async Task<Result<int>> ApproveCommentsAsync(IEnumerable<long> commentIds)
    {
        return await RunBulkAsync(
            commentIds,
            ids => commentRepo.SetModerationStatusBulkAsync(ids, CommentModerationStatus.Approved),
            "approve").ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects many comments at once, or files them all as spam.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [BRD-39] Mirrors <see cref="RejectCommentAsync"/> - the rows
    /// survive so the addresses can be blocked later, but Published is cleared.</para>
    /// <para><b>Side Effects:</b> Updates the moderation status of every selected comment.</para>
    /// </remarks>
    /// <param name="commentIds">The comments to reject.</param>
    /// <param name="isSpam">True to file them as spam rather than plain rejections.</param>
    /// <returns>The number rejected, or a failure message.</returns>
    public async Task<Result<int>> RejectCommentsAsync(IEnumerable<long> commentIds, bool isSpam)
    {
        var status = isSpam ? CommentModerationStatus.Spam : CommentModerationStatus.Rejected;
        return await RunBulkAsync(
            commentIds,
            ids => commentRepo.SetModerationStatusBulkAsync(ids, status),
            status.ToLowerInvariant()).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes many comments at once, permanently.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [BRD-39] Replies to a deleted comment go with it, because a
    /// reply whose parent is gone can never be rendered.</para>
    /// <para><b>Side Effects:</b> Deletes the selected comments and their replies.</para>
    /// </remarks>
    /// <param name="commentIds">The comments to delete.</param>
    /// <returns>The number deleted, or a failure message.</returns>
    public async Task<Result<int>> DeleteCommentsAsync(IEnumerable<long> commentIds)
    {
        // An explicit lambda rather than a method group: DeleteBulkAsync now carries an optional
        // CancellationToken (REQ-NFR-026), and a method group whose signature differs from the
        // delegate's by an optional parameter is not convertible.
        return await RunBulkAsync(
            commentIds, ids => commentRepo.DeleteBulkAsync(ids), "delete").ConfigureAwait(false);
    }

    /// <summary>
    /// Runs one bulk moderation action and reports how many rows it touched.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An empty selection is a no-op success, not an error - the
    /// moderator simply had nothing ticked.</para>
    /// <para><b>Side Effects:</b> Whatever <paramref name="action"/> does.</para>
    /// </remarks>
    /// <param name="commentIds">The raw selection.</param>
    /// <param name="action">The repository call to run against the cleaned id list.</param>
    /// <param name="actionName">Verb used in the log and the failure message.</param>
    /// <returns>The number of rows affected, or a failure message.</returns>
    private async Task<Result<int>> RunBulkAsync(
        IEnumerable<long> commentIds, Func<long[], Task<int>> action, string actionName)
    {
        var ids = commentIds == null ? [] : commentIds.Where(id => id > 0).Distinct().ToArray();
        if (ids.Length == 0)
            return Result<int>.Success(0);

        try
        {
            var affected = await action(ids).ConfigureAwait(false);
            logger.LogInformation(
                "Bulk {Action} applied to {Affected} of {Selected} comments",
                actionName, affected, ids.Length);
            return Result<int>.Success(affected);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk {Action} failed for {Selected} comments", actionName, ids.Length);
            return Result<int>.Failure($"Failed to {actionName} the selected comments.");
        }
    }

    /// <summary>
    /// Deletes a comment permanently.
    /// </summary>
    /// <param name="commentId">The comment to delete.</param>
    /// <returns>Success, or a failure message.</returns>
    public Result DeleteComment(long commentId)
    {
        if (commentId <= 0)
            return Result.Failure("Invalid comment ID");

        try
        {
            var existing = commentRepo.GetSingle(commentId);
            if (existing == null)
                return Result.Failure("Comment not found");

            commentRepo.Delete(commentId);
            logger.LogInformation("Deleted comment ID {CommentId}", commentId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete comment ID {CommentId}", commentId);
            return Result.Failure($"Failed to delete comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the editable fields of a comment.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Moderation state is not touched here; use
    /// <see cref="ApproveComment"/> or <see cref="RejectCommentAsync"/>.</para>
    /// <para><b>Side Effects:</b> Updates one row.</para>
    /// </remarks>
    /// <param name="comment">The comment carrying the new values.</param>
    /// <returns>The updated comment, or a failure message.</returns>
    public Result<BlogComment> UpdateComment(BlogComment comment)
    {
        if (comment == null)
            return Result<BlogComment>.Failure("Comment cannot be null");

        if (comment.CommentID <= 0)
            return Result<BlogComment>.Failure("Invalid comment ID");

        try
        {
            var existing = commentRepo.GetSingle(comment.CommentID);
            if (existing == null)
                return Result<BlogComment>.Failure("Comment not found");

            commentRepo.Update(comment);
            logger.LogInformation("Updated comment ID {CommentId}", comment.CommentID);
            return Result<BlogComment>.Success(comment);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update comment ID {CommentId}", comment.CommentID);
            return Result<BlogComment>.Failure($"Failed to update comment: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the dashboard counters.
    /// </summary>
    /// <returns>Populated counters; zeroed on error.</returns>
    public AdminCounts GetAdminCounts()
    {
        try
        {
            return commentRepo.GetAdminCounts();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting admin counts");
            return new AdminCounts();
        }
    }

    /// <summary>
    /// Gets every comment, newest first.
    /// </summary>
    /// <returns>All comments; empty on error.</returns>
    public IEnumerable<BlogComment> GetAllComments()
    {
        try
        {
            return commentRepo.GetAll() ?? Enumerable.Empty<BlogComment>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all comments");
            return Enumerable.Empty<BlogComment>();
        }
    }

    /// <summary>
    /// Gets the total number of comments in any state.
    /// </summary>
    /// <returns>The count; 0 on error.</returns>
    public int GetTotalCount()
    {
        try
        {
            return commentRepo.GetTotalCount();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting total comment count");
            return 0;
        }
    }

    /// <summary>
    /// Gets the size of the moderation queue.
    /// </summary>
    /// <returns>The count; 0 on error.</returns>
    public int GetPendingCount()
    {
        try
        {
            return commentRepo.GetPendingCount();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting pending comment count");
            return 0;
        }
    }
}
