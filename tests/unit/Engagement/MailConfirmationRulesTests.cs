using BlogEngine.Services;
using BlogModels;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// [REQ-UI-027] [REQ-UI-029] [REQ-UI-030] [REQ-FN-033] What a reader is told, and what is kept, after a
/// rating, a comment or a subscription: the link goes to a masked address; a mail that cannot be sent
/// leaves nothing waiting; with no mail server the form says confirmation is not available now.
/// </summary>
/// <remarks>
/// Written after the 2026-11-26 live report: a reader's rating was parked behind a link that was never
/// mailed, and the reader was told to check the inbox.
/// </remarks>
public class MailConfirmationRulesTests
{
    /// <summary>
    /// A rating from an unconfirmed address, with a mail server set, says where the link went, masked.
    /// </summary>
    [Fact]
    public async Task RatingSaysWhereTheLinkWentMasked()
    {
        var context = new Context();

        var result = await context.Ratings.SubmitRatingAsync(Rating());

        Assert.Equal("We sent a link to a•••@example.com. Open it within 24 hours to confirm your rating.", result.Data!.Message);
    }

    /// <summary>
    /// With no mail server set, a rating from an unconfirmed address is refused with the
    /// "not available" sentence and nothing is written.
    /// </summary>
    [Fact]
    public async Task RatingRefusedWhenNoMailServer()
    {
        var context = new Context(isMailServerSet: false);

        var result = await context.Ratings.SubmitRatingAsync(Rating());

        Assert.Equal((EmailVerificationSvc.ConfirmationUnavailableMessage, 0), (result.ErrorMessage, context.RatingRepo.Ratings.Count));
    }

    /// <summary>
    /// With no mail server, an address that already confirmed once still rates — no mail is needed.
    /// </summary>
    [Fact]
    public async Task ConfirmedAddressRatesWithoutMailServer()
    {
        var context = new Context(isMailServerSet: false);
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        var result = await context.Ratings.SubmitRatingAsync(Rating());

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// When the mail cannot be sent, the rating is removed, the token can never be redeemed, and the
    /// reader is told, with the masked address, that nothing was saved.
    /// </summary>
    [Fact]
    public async Task FailedMailLeavesNoRatingWaiting()
    {
        var context = new Context();
        context.EmailSender.IsFailing = true;

        var result = await context.Ratings.SubmitRatingAsync(Rating());

        Assert.Equal(
            (EmailVerificationSvc.SendFailedMessage("ada@example.com"), 0, true),
            (result.ErrorMessage, context.RatingRepo.Ratings.Count, context.TokenRepo.Tokens.All(t => t.IsUsed)));
    }

    /// <summary>
    /// The send-failure sentence names the masked address, never the full one.
    /// </summary>
    [Fact]
    public void SendFailedMessageIsMasked()
    {
        Assert.DoesNotContain("ada@example.com", EmailVerificationSvc.SendFailedMessage("ada@example.com"));
    }

    /// <summary>
    /// With no mail server, a comment from an unconfirmed address is refused and nothing is stored.
    /// </summary>
    [Fact]
    public async Task CommentRefusedWhenNoMailServer()
    {
        var context = new Context(isMailServerSet: false);

        var result = await context.Comments.SubmitCommentAsync(Comment());

        Assert.Equal((EmailVerificationSvc.ConfirmationUnavailableMessage, 0), (result.ErrorMessage, context.CommentRepo.Comments.Count));
    }

    /// <summary>
    /// A comment says where the link went, masked.
    /// </summary>
    [Fact]
    public async Task CommentSaysWhereTheLinkWentMasked()
    {
        var context = new Context();

        var result = await context.Comments.SubmitCommentAsync(Comment());

        Assert.StartsWith("We sent a link to a•••@example.com.", result.Data!.Message);
    }

    /// <summary>
    /// When the subscription mail cannot be sent, the pending subscriber row is removed.
    /// </summary>
    [Fact]
    public async Task FailedMailLeavesNoSubscriberWaiting()
    {
        var context = new Context();
        context.EmailSender.IsFailing = true;

        var result = await context.Subscribers.SubscribePendingAsync("ada@example.com", "Ada", TestContext.Current.CancellationToken);

        Assert.Equal((false, 0), (result.IsSuccess, context.SubscriberRepo.Subscribers.Count));
    }

    /// <summary>
    /// With no mail server, a subscription is refused and no pending row is written.
    /// </summary>
    [Fact]
    public async Task SubscriptionRefusedWhenNoMailServer()
    {
        var context = new Context(isMailServerSet: false);

        var result = await context.Subscribers.SubscribePendingAsync("ada@example.com", "Ada", TestContext.Current.CancellationToken);

        Assert.Equal((EmailVerificationSvc.ConfirmationUnavailableMessage, 0), (result.ErrorMessage, context.SubscriberRepo.Subscribers.Count));
    }

    private static RatingSubmission Rating()
    {
        return new RatingSubmission
        {
            PostId = 7, Email = "ada@example.com", DisplayName = "Ada", Rating = 4,
            CaptchaChallengeId = "challenge", CaptchaAnswer = "ABCDE", IpAddress = "203.0.113.7"
        };
    }

    private static CommentSubmission Comment()
    {
        return new CommentSubmission
        {
            PostId = 7, AuthorName = "Ada Lovelace", AuthorEmail = "ada@example.com",
            CommentText = "A thoughtful reply about the analytical engine.",
            CaptchaChallengeId = "challenge", CaptchaAnswer = "ABCDE",
            RenderedOn = DateTime.UtcNow.AddMinutes(-2), IpAddress = "203.0.113.7", UserAgent = "Mozilla/5.0"
        };
    }

    /// <summary>
    /// The rating, comment and subscriber services over the in-memory fakes, with a mail server that is
    /// either set or not.
    /// </summary>
    private sealed class Context
    {
        public Context(bool isMailServerSet = true)
        {
            var siteSettings = new FakeSiteSettingsService();
            if (isMailServerSet)
                siteSettings.Settings.Smtp = new SmtpSettings { Host = "smtp.site.test", FromAddress = "blog@site.test" };

            var mailServerSource = new MailServerSource(
                siteSettings, new FakeConfiguration(new()), NullLogger<MailServerSource>.Instance);
            var verification = new EmailVerificationSvc(
                TokenRepo, VerifiedEmailRepo, CommentRepo, RatingRepo, SubscriberRepo, EmailSender,
                new FakeConfiguration(new() { ["SiteSettings:BaseUrl"] = "https://blog.example" }),
                siteSettings, NullLogger<EmailVerificationSvc>.Instance, null, mailServerSource);

            Ratings = new RatingSvc(RatingRepo, new FakeCaptchaService(), verification, NullLogger<RatingSvc>.Instance);
            Comments = new CommentSvc(
                CommentRepo, new FakeCaptchaService(),
                new CommentSpamGuard(CommentRepo, NullLogger<CommentSpamGuard>.Instance),
                verification, siteSettings, NullLogger<CommentSvc>.Instance);
            Subscribers = new SubscriberSvc(SubscriberRepo, NullLogger<SubscriberSvc>.Instance, verification);
        }

        public FakePostRatingRepo RatingRepo { get; } = new FakePostRatingRepo();

        public FakeBlogCommentRepo CommentRepo { get; } = new FakeBlogCommentRepo();

        public FakeSubscriberRepo SubscriberRepo { get; } = new FakeSubscriberRepo();

        public FakeEmailVerificationTokenRepo TokenRepo { get; } = new FakeEmailVerificationTokenRepo();

        public FakeVerifiedEmailRepo VerifiedEmailRepo { get; } = new FakeVerifiedEmailRepo();

        public FakeVerificationEmailSender EmailSender { get; } = new FakeVerificationEmailSender();

        public RatingSvc Ratings { get; }

        public CommentSvc Comments { get; }

        public SubscriberSvc Subscribers { get; }
    }
}
