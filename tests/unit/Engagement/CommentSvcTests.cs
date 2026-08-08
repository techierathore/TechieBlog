using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for anonymous comment submission and the moderation workflow. [REQ-FN-022]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the two acceptance criteria - an unconfirmed comment never appears
/// publicly, and a confirmed one enters the moderation queue - plus the captcha and spam gates
/// that replace the old sign-in barrier.</para>
/// <para><b>Code Flow:</b> Each test drives the real <see cref="CommentSvc"/> over in-memory
/// repositories and the real spam guard, substituting only the captcha verdict.</para>
/// <para><b>Dependencies:</b> xUnit and the fakes in this folder.</para>
/// <para><b>Usage:</b> Pure unit tests - no database, no SMTP.</para>
/// </remarks>
public class CommentSvcTests
{
    /// <summary>
    /// A comment from an unknown address is stored in PendingVerification, is not published and
    /// does not appear in the post's public thread.
    /// </summary>
    [Fact]
    public async Task UnconfirmedCommentIsNeverPublic()
    {
        var context = new CommentContext();

        var result = await context.Service.SubmitCommentAsync(BuildSubmission());

        var stored = context.CommentRepo.Comments.Single();
        Assert.True(result.IsSuccess);
        Assert.Equal(CommentModerationStatus.PendingVerification, stored.ModerationStatus);
        Assert.False(stored.Published);
        Assert.Empty(context.Service.GetCommentsByPostId(7));
    }

    /// <summary>
    /// An unconfirmed comment is not in the moderation queue either - it is not yet a
    /// moderator's problem.
    /// </summary>
    [Fact]
    public async Task UnconfirmedCommentIsNotQueued()
    {
        var context = new CommentContext();

        await context.Service.SubmitCommentAsync(BuildSubmission());

        Assert.Empty(context.Service.GetPendingComments());
    }

    /// <summary>
    /// Clicking the emailed confirmation link moves the comment into the moderation queue while
    /// leaving it invisible to readers.
    /// </summary>
    [Fact]
    public async Task ConfirmedCommentEntersModerationQueue()
    {
        var context = new CommentContext();
        await context.Service.SubmitCommentAsync(BuildSubmission());
        var token = context.TokenRepo.Tokens.Single().Token;

        await context.VerificationService.ConsumeAsync(token);

        var queued = context.Service.GetPendingComments().Single();
        Assert.Equal(CommentModerationStatus.PendingApproval, queued.ModerationStatus);
        Assert.False(queued.Published);
        Assert.Empty(context.Service.GetCommentsByPostId(7));
    }

    /// <summary>
    /// Only an administrator's approval makes a confirmed comment public.
    /// </summary>
    [Fact]
    public async Task ApprovedCommentBecomesPublic()
    {
        var context = new CommentContext();
        await context.Service.SubmitCommentAsync(BuildSubmission());
        await context.VerificationService.ConsumeAsync(context.TokenRepo.Tokens.Single().Token);
        var commentId = context.CommentRepo.Comments.Single().CommentID;

        var approval = context.Service.ApproveComment(commentId);

        Assert.True(approval.IsSuccess);
        Assert.Single(context.Service.GetCommentsByPostId(7));
    }

    /// <summary>
    /// A comment whose address was never confirmed cannot be approved, which is the last line of
    /// defence behind the "never appears publicly" rule.
    /// </summary>
    [Fact]
    public async Task ApproveRefusesUnverifiedComment()
    {
        var context = new CommentContext();
        await context.Service.SubmitCommentAsync(BuildSubmission());
        var commentId = context.CommentRepo.Comments.Single().CommentID;

        var approval = context.Service.ApproveComment(commentId);

        Assert.True(approval.IsFailure);
    }

    /// <summary>
    /// An address already on the verified registry skips confirmation and lands straight in the
    /// moderation queue, with no email sent.
    /// </summary>
    [Fact]
    public async Task VerifiedAddressCommentSkipsVerification()
    {
        var context = new CommentContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        var result = await context.Service.SubmitCommentAsync(BuildSubmission());

        Assert.False(result.Data.IsEmailVerificationRequired);
        Assert.Equal(CommentModerationStatus.PendingApproval, result.Data.ModerationStatus);
        Assert.Empty(context.EmailSender.SentUrls);
    }

    /// <summary>
    /// A wrong captcha answer blocks the write entirely - nothing is persisted and the caller is
    /// told to try again with a fresh challenge.
    /// </summary>
    [Fact]
    public async Task WrongCaptchaBlocksTheWrite()
    {
        var context = new CommentContext();
        context.CaptchaService.IsAnswerAccepted = false;

        var result = await context.Service.SubmitCommentAsync(BuildSubmission());

        Assert.True(result.IsFailure);
        Assert.Empty(context.CommentRepo.Comments);
    }

    /// <summary>
    /// A submission the spam guard rejects is never written, and the visitor is not told which
    /// rule caught it.
    /// </summary>
    [Fact]
    public async Task SpamSubmissionIsRejected()
    {
        var context = new CommentContext();
        var submission = BuildSubmission();
        submission.HoneypotValue = "filled-by-a-bot";

        var result = await context.Service.SubmitCommentAsync(submission);

        Assert.True(result.IsFailure);
        Assert.Empty(context.CommentRepo.Comments);
        Assert.DoesNotContain("honeypot", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A submission with no email address is refused before any gate runs.
    /// </summary>
    [Fact]
    public async Task SubmissionRequiresEmailAddress()
    {
        var context = new CommentContext();
        var submission = BuildSubmission();
        submission.AuthorEmail = "  ";

        var result = await context.Service.SubmitCommentAsync(submission);

        Assert.True(result.IsFailure);
        Assert.Empty(context.CommentRepo.Comments);
    }

    /// <summary>
    /// The anonymous name and address supplied by the visitor are what gets stored - a comment
    /// no longer needs a signed-in user id.
    /// </summary>
    [Fact]
    public async Task AnonymousIdentityIsPersisted()
    {
        var context = new CommentContext();

        await context.Service.SubmitCommentAsync(BuildSubmission());

        var stored = context.CommentRepo.Comments.Single();
        Assert.Equal("Ada Lovelace", stored.GivenBy);
        Assert.Equal("ada@example.com", stored.Email);
        Assert.Null(stored.UserId);
    }

    /// <summary>
    /// Rejecting a queued comment clears its published state and files it under the chosen
    /// status.
    /// </summary>
    [Fact]
    public async Task RejectedCommentLeavesTheQueue()
    {
        var context = new CommentContext();
        await context.Service.SubmitCommentAsync(BuildSubmission());
        await context.VerificationService.ConsumeAsync(context.TokenRepo.Tokens.Single().Token);
        var commentId = context.CommentRepo.Comments.Single().CommentID;

        await context.Service.RejectCommentAsync(commentId, true);

        Assert.Empty(context.Service.GetPendingComments());
        Assert.Equal(CommentModerationStatus.Spam, context.CommentRepo.Comments.Single().ModerationStatus);
    }

    /// <summary>
    /// The default posture is moderated: a confirmed anonymous comment waits in the queue and
    /// stays out of the public thread until an administrator acts. [BRD-38]
    /// </summary>
    [Fact]
    public async Task ModerationIsTheDefaultForAnonymousComments()
    {
        var context = new CommentContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        var result = await context.Service.SubmitCommentAsync(BuildSubmission());

        Assert.True(context.SiteSettingsService.Settings.AreCommentsModerated);
        Assert.Equal(CommentModerationStatus.PendingApproval, result.Data.ModerationStatus);
        Assert.False(context.CommentRepo.Comments.Single().Published);
        Assert.Empty(context.Service.GetCommentsByPostId(7));
        Assert.Single(context.Service.GetPendingComments());
    }

    /// <summary>
    /// Turning the moderation setting off publishes a comment from an already-confirmed address
    /// immediately, with no queue step. [BRD-38]
    /// </summary>
    [Fact]
    public async Task ModerationOffPublishesConfirmedAddressImmediately()
    {
        var context = new CommentContext();
        context.SiteSettingsService.Settings.AreCommentsModerated = false;
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        var result = await context.Service.SubmitCommentAsync(BuildSubmission());

        Assert.Equal(CommentModerationStatus.Approved, result.Data.ModerationStatus);
        Assert.True(context.CommentRepo.Comments.Single().Published);
        Assert.Single(context.Service.GetCommentsByPostId(7));
        Assert.Empty(context.Service.GetPendingComments());
    }

    /// <summary>
    /// With moderation off, clicking the confirmation link publishes the comment rather than
    /// parking it in a queue nobody is watching. [BRD-38]
    /// </summary>
    [Fact]
    public async Task ModerationOffPublishesOnEmailConfirmation()
    {
        var context = new CommentContext();
        context.SiteSettingsService.Settings.AreCommentsModerated = false;
        await context.Service.SubmitCommentAsync(BuildSubmission());

        await context.VerificationService.ConsumeAsync(context.TokenRepo.Tokens.Single().Token);

        Assert.Equal(CommentModerationStatus.Approved, context.CommentRepo.Comments.Single().ModerationStatus);
        Assert.Single(context.Service.GetCommentsByPostId(7));
    }

    /// <summary>
    /// Even with moderation off, an unconfirmed address is still invisible - the setting governs
    /// approval, never the double opt-in gate. [BRD-38]
    /// </summary>
    [Fact]
    public async Task ModerationOffStillRequiresEmailConfirmation()
    {
        var context = new CommentContext();
        context.SiteSettingsService.Settings.AreCommentsModerated = false;

        var result = await context.Service.SubmitCommentAsync(BuildSubmission());

        Assert.True(result.Data.IsEmailVerificationRequired);
        Assert.Equal(CommentModerationStatus.PendingVerification, context.CommentRepo.Comments.Single().ModerationStatus);
        Assert.Empty(context.Service.GetCommentsByPostId(7));
    }

    /// <summary>
    /// A settings store that cannot be read falls back to moderating, because publishing a
    /// comment that should have been reviewed cannot be undone. [BRD-38]
    /// </summary>
    [Fact]
    public async Task UnreadableModerationSettingFallsBackToModerating()
    {
        var context = new CommentContext();
        context.SiteSettingsService.ThrowOnRead = true;
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        var result = await context.Service.SubmitCommentAsync(BuildSubmission());

        Assert.Equal(CommentModerationStatus.PendingApproval, result.Data.ModerationStatus);
        Assert.Empty(context.Service.GetCommentsByPostId(7));
    }

    /// <summary>
    /// Bulk approval publishes every selected confirmed comment in one action. [BRD-39]
    /// </summary>
    [Fact]
    public async Task BulkApprovePublishesEverySelectedComment()
    {
        var context = new CommentContext();
        var ids = await QueueConfirmedCommentsAsync(context, 3);

        var result = await context.Service.ApproveCommentsAsync(ids);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Data);
        Assert.Equal(3, context.Service.GetCommentsByPostId(7).Count());
        Assert.Empty(context.Service.GetPendingComments());
    }

    /// <summary>
    /// Bulk approval carries the single-comment guard: a comment whose address was never
    /// confirmed is skipped rather than published, and the count reports the skip. [BRD-39]
    /// </summary>
    [Fact]
    public async Task BulkApproveSkipsUnconfirmedComments()
    {
        var context = new CommentContext();
        var confirmed = await QueueConfirmedCommentsAsync(context, 1);
        await context.Service.SubmitCommentAsync(BuildSubmission("grace@example.com"));
        var unconfirmed = context.CommentRepo.Comments
            .Single(c => c.ModerationStatus == CommentModerationStatus.PendingVerification).CommentID;

        var result = await context.Service.ApproveCommentsAsync([.. confirmed, unconfirmed]);

        Assert.Equal(1, result.Data);
        Assert.Single(context.Service.GetCommentsByPostId(7));
        Assert.Equal(
            CommentModerationStatus.PendingVerification,
            context.CommentRepo.Comments.Single(c => c.CommentID == unconfirmed).ModerationStatus);
    }

    /// <summary>
    /// Bulk spam marking reaches the database rather than only the grid: the comments leave the
    /// public thread and are filed as spam. [BRD-39]
    /// </summary>
    [Fact]
    public async Task BulkSpamRemovesCommentsFromPublicView()
    {
        var context = new CommentContext();
        var ids = await QueueConfirmedCommentsAsync(context, 2);
        await context.Service.ApproveCommentsAsync(ids);

        var result = await context.Service.RejectCommentsAsync(ids, isSpam: true);

        Assert.Equal(2, result.Data);
        Assert.Empty(context.Service.GetCommentsByPostId(7));
        Assert.All(context.CommentRepo.Comments, c =>
        {
            Assert.Equal(CommentModerationStatus.Spam, c.ModerationStatus);
            Assert.False(c.Published);
        });
    }

    /// <summary>
    /// Bulk deletion takes the replies with the parent, because a reply whose parent is gone can
    /// never be rendered. [BRD-39]
    /// </summary>
    [Fact]
    public async Task BulkDeleteRemovesRepliesWithTheirParent()
    {
        var context = new CommentContext();
        var parentIds = await QueueConfirmedCommentsAsync(context, 1);
        var reply = BuildSubmission("grace@example.com");
        reply.ParentCommentId = parentIds[0];
        await context.Service.SubmitCommentAsync(reply);

        var result = await context.Service.DeleteCommentsAsync(parentIds);

        Assert.Equal(1, result.Data);
        Assert.Empty(context.CommentRepo.Comments);
    }

    /// <summary>
    /// Applying a bulk action with nothing selected is a harmless no-op, not an error - the
    /// moderator simply had no rows ticked. [BRD-39]
    /// </summary>
    [Fact]
    public async Task BulkActionWithNoSelectionChangesNothing()
    {
        var context = new CommentContext();
        await QueueConfirmedCommentsAsync(context, 1);

        var result = await context.Service.ApproveCommentsAsync([]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data);
        Assert.Single(context.Service.GetPendingComments());
    }

    /// <summary>
    /// Submits and confirms a number of comments, leaving them in the moderation queue.
    /// </summary>
    /// <param name="context">The wired test context.</param>
    /// <param name="count">How many comments to queue.</param>
    /// <returns>The ids of the queued comments.</returns>
    private static async Task<List<long>> QueueConfirmedCommentsAsync(CommentContext context, int count)
    {
        var ids = new List<long>();
        for (var index = 0; index < count; index++)
        {
            await context.Service.SubmitCommentAsync(BuildSubmission($"reader{index}@example.com"));
            var token = context.TokenRepo.Tokens[^1].Token;
            await context.VerificationService.ConsumeAsync(token);
            ids.Add(context.CommentRepo.Comments[^1].CommentID);
        }

        return ids;
    }

    /// <summary>
    /// Builds a well-formed anonymous submission.
    /// </summary>
    /// <param name="authorEmail">The commenter's address.</param>
    /// <returns>The submission.</returns>
    private static CommentSubmission BuildSubmission(string authorEmail)
    {
        var submission = BuildSubmission();
        submission.AuthorEmail = authorEmail;
        return submission;
    }

    /// <summary>
    /// Builds a well-formed anonymous submission.
    /// </summary>
    /// <returns>The submission.</returns>
    private static CommentSubmission BuildSubmission()
    {
        return new CommentSubmission
        {
            PostId = 7,
            AuthorName = "Ada Lovelace",
            AuthorEmail = "ada@example.com",
            CommentText = "A thoughtful reply about the analytical engine.",
            CaptchaChallengeId = "challenge",
            CaptchaAnswer = "ABCDE",
            RenderedOn = DateTime.UtcNow.AddMinutes(-2),
            IpAddress = "203.0.113.7",
            UserAgent = "Mozilla/5.0"
        };
    }

    /// <summary>
    /// Wires the comment service, the verification service and their stores together.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Exercises the real interaction between submission, verification and
    /// moderation instead of mocking the seam under test.</para>
    /// <para><b>Dependencies:</b> The fakes in this folder.</para>
    /// <para><b>Usage:</b> Reach into the exposed repositories to seed or inspect state.</para>
    /// </remarks>
    private sealed class CommentContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CommentContext"/> class.
        /// </summary>
        public CommentContext()
        {
            CommentRepo = new FakeBlogCommentRepo();
            TokenRepo = new FakeEmailVerificationTokenRepo();
            VerifiedEmailRepo = new FakeVerifiedEmailRepo();
            EmailSender = new FakeVerificationEmailSender();
            CaptchaService = new FakeCaptchaService();
            SiteSettingsService = new FakeSiteSettingsService();
            VerificationService = new EmailVerificationSvc(
                TokenRepo,
                VerifiedEmailRepo,
                CommentRepo,
                new FakePostRatingRepo(),
                new FakeSubscriberRepo(),
                EmailSender,
                new FakeConfiguration(new Dictionary<string, string?>
                {
                    ["SiteSettings:BaseUrl"] = "https://blog.example"
                }),
                SiteSettingsService,
                NullLogger<EmailVerificationSvc>.Instance);
            Service = new CommentSvc(
                CommentRepo,
                CaptchaService,
                new CommentSpamGuard(CommentRepo, NullLogger<CommentSpamGuard>.Instance),
                VerificationService,
                SiteSettingsService,
                NullLogger<CommentSvc>.Instance);
        }

        /// <summary>Gets the in-memory comment store.</summary>
        public FakeBlogCommentRepo CommentRepo { get; }

        /// <summary>Gets the settings stub that drives the moderation setting [BRD-38].</summary>
        public FakeSiteSettingsService SiteSettingsService { get; }

        /// <summary>Gets the in-memory token store.</summary>
        public FakeEmailVerificationTokenRepo TokenRepo { get; }

        /// <summary>Gets the in-memory verified-address registry.</summary>
        public FakeVerifiedEmailRepo VerifiedEmailRepo { get; }

        /// <summary>Gets the sender that captures confirmation links.</summary>
        public FakeVerificationEmailSender EmailSender { get; }

        /// <summary>Gets the captcha stub whose verdict a test controls.</summary>
        public FakeCaptchaService CaptchaService { get; }

        /// <summary>Gets the real verification service under test.</summary>
        public EmailVerificationSvc VerificationService { get; }

        /// <summary>Gets the comment service under test.</summary>
        public CommentSvc Service { get; }
    }
}
