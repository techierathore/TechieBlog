using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for persisted double opt-in email verification. [REQ-FN-048]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the acceptance criteria: a token works exactly once, an expired
/// token is refused, consuming a comment token moves it into the moderation queue, and a
/// verified address posts without re-confirming.</para>
/// <para><b>Code Flow:</b> Each test wires the real service over in-memory repositories that
/// reproduce the stored-function semantics from migration script 014.</para>
/// <para><b>Dependencies:</b> xUnit and the fakes in this folder.</para>
/// <para><b>Usage:</b> Pure unit tests - no database, no SMTP.</para>
/// </remarks>
public class EmailVerificationSvcTests
{
    /// <summary>
    /// A freshly issued token can be redeemed once; the identical second attempt is refused,
    /// so a forwarded or re-opened link cannot confirm anything twice.
    /// </summary>
    [Fact]
    public async Task VerificationTokenWorksExactlyOnce()
    {
        var context = new VerificationContext();
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Comment, 42, "203.0.113.7");

        var first = await context.Service.ConsumeAsync(issued.Data.Token);
        var second = await context.Service.ConsumeAsync(issued.Data.Token);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
    }

    /// <summary>
    /// A token whose 24-hour window has closed is refused even though it was never used.
    /// </summary>
    [Fact]
    public async Task VerificationTokenExpires()
    {
        var context = new VerificationContext();
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Comment, 42, null);
        context.TokenRepo.Tokens.Single().ExpiresOn = DateTime.UtcNow.AddMinutes(-1);

        var result = await context.Service.ConsumeAsync(issued.Data.Token);

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// A token is issued with a 24-hour life, matching the stated policy.
    /// </summary>
    [Fact]
    public async Task VerificationTokenLastsTwentyFourHours()
    {
        var context = new VerificationContext();

        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Comment, 42, null);

        var lifetime = issued.Data.ExpiresOn - issued.Data.IssuedOn;
        Assert.Equal(24, Math.Round(lifetime.TotalHours));
    }

    /// <summary>
    /// Consuming a comment token promotes the comment from PendingVerification into the
    /// moderation queue - it does NOT publish it.
    /// </summary>
    [Fact]
    public async Task ConsumingCommentTokenQueuesForModeration()
    {
        var context = new VerificationContext();
        var commentId = await context.CommentRepo.InsertPendingAsync(new BlogComment
        {
            PostID = 7,
            GivenBy = "Ada",
            Email = "ada@example.com",
            Comment = "Hello",
            ModerationStatus = CommentModerationStatus.PendingVerification
        });
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Comment, commentId, null);

        await context.Service.ConsumeAsync(issued.Data.Token);

        var comment = context.CommentRepo.GetSingle(commentId);
        Assert.NotNull(comment);
        Assert.Equal(CommentModerationStatus.PendingApproval, comment.ModerationStatus);
        Assert.False(comment.Published);
    }

    /// <summary>
    /// Consuming a rating token makes the parked score start counting towards the aggregates.
    /// </summary>
    [Fact]
    public async Task ConsumingRatingTokenCountsTheScore()
    {
        var context = new VerificationContext();
        var ratingId = await context.RatingRepo.UpsertByEmailAsync(7, "ada@example.com", 5, null, false);
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Rating, ratingId, null);

        await context.Service.ConsumeAsync(issued.Data.Token);

        Assert.Equal(1, context.RatingRepo.GetCountByPost(7));
    }

    /// <summary>
    /// Consuming a subscription token confirms the pending subscriber. Regression: the service
    /// previously promoted only comments and ratings, so a subscriber written by the subscribe
    /// form with IsConfirmed = false could never become confirmed and never received an issue.
    /// </summary>
    [Fact]
    public async Task ConsumingSubscriptionTokenConfirmsTheSubscriber()
    {
        var context = new VerificationContext();
        var subscriberId = context.SubscriberRepo.InsertToGetId(new Subscriber
        {
            Email = "ada@example.com",
            Name = "Ada",
            SubscribedOn = DateTime.UtcNow,
            IsConfirmed = false
        });
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Subscription, subscriberId, null);

        await context.Service.ConsumeAsync(issued.Data.Token);

        Assert.True(context.SubscriberRepo.GetSingle(subscriberId)!.IsConfirmed);
    }

    /// <summary>
    /// Replaying a subscription link cannot confirm a second time: the token is refused, so the
    /// subscriber count of confirmed rows does not move.
    /// </summary>
    [Fact]
    public async Task ReplayedSubscriptionTokenDoesNotConfirmAgain()
    {
        var context = new VerificationContext();
        var subscriberId = context.SubscriberRepo.InsertToGetId(new Subscriber
        {
            Email = "ada@example.com",
            Name = "Ada",
            SubscribedOn = DateTime.UtcNow,
            IsConfirmed = false
        });
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Subscription, subscriberId, null);
        await context.Service.ConsumeAsync(issued.Data.Token);

        // Force the row back to pending; a refused replay must NOT re-promote it.
        context.SubscriberRepo.UpdateStatus(subscriberId, false);
        var replay = await context.Service.ConsumeAsync(issued.Data.Token);

        Assert.True(replay.IsFailure);
        Assert.False(context.SubscriberRepo.GetSingle(subscriberId)!.IsConfirmed);
    }

    /// <summary>
    /// After a successful confirmation the address joins the registry, so the next submission
    /// from it skips verification entirely.
    /// </summary>
    [Fact]
    public async Task VerifiedAddressSkipsConfirmation()
    {
        var context = new VerificationContext();
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Comment, 42, null);

        await context.Service.ConsumeAsync(issued.Data.Token);

        Assert.True(await context.Service.IsAddressVerifiedAsync("ada@example.com"));
    }

    /// <summary>
    /// The registry lookup ignores case, so a reader who capitalises their address differently
    /// is still recognised.
    /// </summary>
    [Fact]
    public async Task VerifiedAddressLookupIgnoresCase()
    {
        var context = new VerificationContext();
        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Comment, 42, null);
        await context.Service.ConsumeAsync(issued.Data.Token);

        Assert.True(await context.Service.IsAddressVerifiedAsync("Ada@Example.COM"));
    }

    /// <summary>
    /// An address an administrator has blocked is not treated as verified, so its submissions
    /// are stopped at the door.
    /// </summary>
    [Fact]
    public async Task BlockedAddressIsNotTreatedAsVerified()
    {
        var context = new VerificationContext();
        var issued = await context.Service.IssueAsync(
            "spammer@example.com", "Bot", EmailVerificationPurpose.Comment, 42, null);
        await context.Service.ConsumeAsync(issued.Data.Token);

        await context.VerifiedEmailRepo.SetBlockedAsync("spammer@example.com", true);

        Assert.False(await context.Service.IsAddressVerifiedAsync("spammer@example.com"));
    }

    /// <summary>
    /// An unknown or malformed token is refused rather than throwing.
    /// </summary>
    [Fact]
    public async Task UnknownTokenIsRefused()
    {
        var context = new VerificationContext();

        var result = await context.Service.ConsumeAsync("not-a-real-token");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// Once an address has been sent its hourly allowance of confirmation emails, further
    /// requests are refused, so the feature cannot be aimed at a stranger's inbox.
    /// </summary>
    [Fact]
    public async Task IssueRefusesWhenRateLimitReached()
    {
        var context = new VerificationContext();
        context.TokenRepo.RecentByEmailCount = 5;

        var issued = await context.Service.IssueAsync(
            "ada@example.com", "Ada", EmailVerificationPurpose.Comment, 42, null);

        Assert.True(issued.IsFailure);
        Assert.Empty(context.EmailSender.SentUrls);
    }

    /// <summary>
    /// The confirmation link is absolute and points at the verify route, so it works from an
    /// email client.
    /// </summary>
    [Fact]
    public async Task IssuedLinkPointsAtVerifyRoute()
    {
        var context = new VerificationContext();

        await context.Service.IssueAsync("ada@example.com", "Ada", EmailVerificationPurpose.Comment, 42, null);

        Assert.StartsWith("https://blog.example/verify/", context.EmailSender.SentUrls.Single(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Wires the service under test over in-memory repositories.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Keeps the arrange step of every test to one line.</para>
    /// <para><b>Dependencies:</b> The fakes in this folder.</para>
    /// <para><b>Usage:</b> Reach into the exposed repositories to seed or inspect state.</para>
    /// </remarks>
    private sealed class VerificationContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VerificationContext"/> class.
        /// </summary>
        public VerificationContext()
        {
            TokenRepo = new FakeEmailVerificationTokenRepo();
            VerifiedEmailRepo = new FakeVerifiedEmailRepo();
            CommentRepo = new FakeBlogCommentRepo();
            RatingRepo = new FakePostRatingRepo();
            SubscriberRepo = new FakeSubscriberRepo();
            EmailSender = new FakeVerificationEmailSender();
            SiteSettingsService = new FakeSiteSettingsService();
            Service = new EmailVerificationSvc(
                TokenRepo,
                VerifiedEmailRepo,
                CommentRepo,
                RatingRepo,
                SubscriberRepo,
                EmailSender,
                new FakeConfiguration(new Dictionary<string, string?>
                {
                    ["SiteSettings:BaseUrl"] = "https://blog.example"
                }),
                SiteSettingsService,
                NullLogger<EmailVerificationSvc>.Instance);
        }

        /// <summary>Gets the settings stub that drives the moderation setting [BRD-38].</summary>
        public FakeSiteSettingsService SiteSettingsService { get; }

        /// <summary>Gets the in-memory token store.</summary>
        public FakeEmailVerificationTokenRepo TokenRepo { get; }

        /// <summary>Gets the in-memory verified-address registry.</summary>
        public FakeVerifiedEmailRepo VerifiedEmailRepo { get; }

        /// <summary>Gets the in-memory comment store.</summary>
        public FakeBlogCommentRepo CommentRepo { get; }

        /// <summary>Gets the in-memory rating store.</summary>
        public FakePostRatingRepo RatingRepo { get; }

        /// <summary>Gets the in-memory newsletter subscriber store.</summary>
        public FakeSubscriberRepo SubscriberRepo { get; }

        /// <summary>Gets the sender that captures confirmation links.</summary>
        public FakeVerificationEmailSender EmailSender { get; }

        /// <summary>Gets the service under test.</summary>
        public EmailVerificationSvc Service { get; }
    }
}
