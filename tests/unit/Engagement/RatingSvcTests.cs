using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for the email-keyed rating service. [REQ-FN-023]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the re-key: one rating per EMAIL per post, changeable, with
/// aggregates that count only confirmed addresses.</para>
/// <para><b>Code Flow:</b> Each test drives the real <see cref="RatingSvc"/> over in-memory
/// repositories, substituting only the captcha verdict.</para>
/// <para><b>Dependencies:</b> xUnit and the fakes in this folder.</para>
/// <para><b>Usage:</b> Pure unit tests - no database, no SMTP.</para>
/// </remarks>
public class RatingSvcTests
{
    /// <summary>
    /// A second rating from the same address updates the first instead of adding a row, and
    /// does so without any signed-in user.
    /// </summary>
    [Fact]
    public async Task RatingIsKeyedByEmailNotUser()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        await context.Service.SubmitRatingAsync(BuildSubmission(4));
        await context.Service.SubmitRatingAsync(BuildSubmission(2));

        var stored = Assert.Single(context.RatingRepo.Ratings);
        Assert.Equal(2, stored.Rating);
        Assert.Null(stored.UserId);
    }

    /// <summary>
    /// The identity key ignores case, so the same reader typing their address differently does
    /// not get a second vote.
    /// </summary>
    [Fact]
    public async Task RatingKeyIgnoresEmailCase()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");
        await context.Service.SubmitRatingAsync(BuildSubmission(4));

        var second = BuildSubmission(1);
        second.Email = "ADA@EXAMPLE.COM";
        await context.Service.SubmitRatingAsync(second);

        Assert.Single(context.RatingRepo.Ratings);
    }

    /// <summary>
    /// A rating from an unconfirmed address is parked: it is stored, but it does not move the
    /// public average or count.
    /// </summary>
    [Fact]
    public async Task UnverifiedRatingIsExcludedFromAggregates()
    {
        var context = new RatingContext();

        var result = await context.Service.SubmitRatingAsync(BuildSubmission(5));

        Assert.True(result.Data.IsEmailVerificationRequired);
        Assert.Single(context.RatingRepo.Ratings);
        Assert.Equal(0, context.Service.GetRatingCount(7));
        Assert.Equal(0, context.Service.GetAverageRating(7));
    }

    /// <summary>
    /// Once the emailed link is clicked, the parked score starts counting towards the average.
    /// </summary>
    [Fact]
    public async Task ConfirmedRatingCountsTowardsAverage()
    {
        var context = new RatingContext();
        await context.Service.SubmitRatingAsync(BuildSubmission(5));

        await context.VerificationService.ConsumeAsync(context.TokenRepo.Tokens.Single().Token);

        Assert.Equal(1, context.Service.GetRatingCount(7));
        Assert.Equal(5, context.Service.GetAverageRating(7));
    }

    /// <summary>
    /// A verified address rates in one step, with no confirmation email.
    /// </summary>
    [Fact]
    public async Task VerifiedAddressRatesImmediately()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        var result = await context.Service.SubmitRatingAsync(BuildSubmission(4));

        Assert.False(result.Data.IsEmailVerificationRequired);
        Assert.Equal(1, context.Service.GetRatingCount(7));
        Assert.Empty(context.EmailSender.SentUrls);
    }

    /// <summary>
    /// A verified reader who changes their mind changes the average rather than adding a vote.
    /// </summary>
    [Fact]
    public async Task ChangedRatingReplacesTheAverage()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");
        await context.Service.SubmitRatingAsync(BuildSubmission(5));

        await context.Service.SubmitRatingAsync(BuildSubmission(1));

        Assert.Equal(1, context.Service.GetRatingCount(7));
        Assert.Equal(1, context.Service.GetAverageRating(7));
    }

    /// <summary>
    /// A score outside the one-to-five range is refused before anything is written.
    /// </summary>
    [Fact]
    public async Task RatingRequiresScoreInRange()
    {
        var context = new RatingContext();

        var result = await context.Service.SubmitRatingAsync(BuildSubmission(6));

        Assert.True(result.IsFailure);
        Assert.Empty(context.RatingRepo.Ratings);
    }

    /// <summary>
    /// A submission with no usable email address is refused, because the address is the
    /// identity key.
    /// </summary>
    [Fact]
    public async Task RatingRequiresEmailAddress()
    {
        var context = new RatingContext();
        var submission = BuildSubmission(4);
        submission.Email = "not-an-address";

        var result = await context.Service.SubmitRatingAsync(submission);

        Assert.True(result.IsFailure);
        Assert.Empty(context.RatingRepo.Ratings);
    }

    /// <summary>
    /// A wrong captcha answer from an anonymous rater blocks the write.
    /// </summary>
    [Fact]
    public async Task WrongCaptchaBlocksTheRating()
    {
        var context = new RatingContext();
        context.CaptchaService.IsAnswerAccepted = false;

        var result = await context.Service.SubmitRatingAsync(BuildSubmission(4));

        Assert.True(result.IsFailure);
        Assert.Empty(context.RatingRepo.Ratings);
    }

    /// <summary>
    /// The top-rated query counts only confirmed ratings, so a post cannot be pushed up the
    /// chart by unconfirmed submissions.
    /// </summary>
    [Fact]
    public async Task TopRatedIgnoresUnverifiedRatings()
    {
        var context = new RatingContext();

        await context.Service.SubmitRatingAsync(BuildSubmission(5));

        Assert.Empty(context.Service.GetTopRatedPostIds());
    }

    /// <summary>
    /// A reader can withdraw the score they left anonymously, keyed by their address.
    /// </summary>
    [Fact]
    public async Task RatingCanBeRemovedByEmail()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");
        await context.Service.SubmitRatingAsync(BuildSubmission(4));

        var removal = await context.Service.RemoveRatingAsync(7, "ada@example.com");

        Assert.True(removal.IsSuccess);
        Assert.Empty(context.RatingRepo.Ratings);
    }

    /// <summary>
    /// Changing a rating updates the existing row in place - the same row id survives - so the
    /// "one rating per email per post" key is never broken by a delete-and-reinsert. [BRD-41]
    /// </summary>
    [Fact]
    public async Task ChangedRatingUpdatesTheSameRow()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");
        var first = await context.Service.SubmitRatingAsync(BuildSubmission(5));

        var second = await context.Service.SubmitRatingAsync(BuildSubmission(2));

        Assert.Equal(first.Data.RatingId, second.Data.RatingId);
        var stored = Assert.Single(context.RatingRepo.Ratings);
        Assert.Equal(2, stored.Rating);
        Assert.NotNull(stored.UpdatedOn);
    }

    /// <summary>
    /// A reader can change their mind repeatedly and still holds exactly one vote; only the last
    /// score counts. [BRD-41]
    /// </summary>
    [Fact]
    public async Task RepeatedChangesLeaveExactlyOneRating()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        foreach (var score in new[] { 5, 1, 3, 4 })
        {
            await context.Service.SubmitRatingAsync(BuildSubmission(score));
        }

        Assert.Single(context.RatingRepo.Ratings);
        Assert.Equal(1, context.Service.GetRatingCount(7));
        Assert.Equal(4, context.Service.GetAverageRating(7));
    }

    /// <summary>
    /// The uniqueness is per address, not global: two different readers each keep their own row
    /// on the same post and both count towards the average. [BRD-40, BRD-42]
    /// </summary>
    [Fact]
    public async Task DifferentAddressesEachKeepTheirOwnRating()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");
        await context.VerifiedEmailRepo.RecordVerifiedAsync("grace@example.com", "Grace");

        await context.Service.SubmitRatingAsync(BuildSubmission(5));
        var other = BuildSubmission(3);
        other.Email = "grace@example.com";
        await context.Service.SubmitRatingAsync(other);

        Assert.Equal(2, context.RatingRepo.Ratings.Count);
        Assert.Equal(2, context.Service.GetRatingCount(7));
        Assert.Equal(4, context.Service.GetAverageRating(7));
    }

    /// <summary>
    /// The same address rating two different posts is two votes, not a conflict - the key is the
    /// pair, not the address alone. [BRD-41]
    /// </summary>
    [Fact]
    public async Task SameAddressRatesEachPostSeparately()
    {
        var context = new RatingContext();
        await context.VerifiedEmailRepo.RecordVerifiedAsync("ada@example.com", "Ada");

        await context.Service.SubmitRatingAsync(BuildSubmission(5));
        var otherPost = BuildSubmission(2);
        otherPost.PostId = 8;
        await context.Service.SubmitRatingAsync(otherPost);

        Assert.Equal(2, context.RatingRepo.Ratings.Count);
        Assert.Equal(5, context.Service.GetAverageRating(7));
        Assert.Equal(2, context.Service.GetAverageRating(8));
    }

    /// <summary>
    /// Changing a score before the address is confirmed still leaves one parked row, and the
    /// public average stays untouched until the link is clicked. [BRD-41, BRD-42]
    /// </summary>
    [Fact]
    public async Task ChangedUnverifiedRatingStaysASingleParkedRow()
    {
        var context = new RatingContext();
        await context.Service.SubmitRatingAsync(BuildSubmission(5));

        await context.Service.SubmitRatingAsync(BuildSubmission(1));

        var stored = Assert.Single(context.RatingRepo.Ratings);
        Assert.Equal(1, stored.Rating);
        Assert.False(stored.IsEmailVerified);
        Assert.Equal(0, context.Service.GetRatingCount(7));
    }

    /// <summary>
    /// Builds a well-formed anonymous rating submission.
    /// </summary>
    /// <param name="rating">The score to submit.</param>
    /// <returns>The submission.</returns>
    private static RatingSubmission BuildSubmission(int rating)
    {
        return new RatingSubmission
        {
            PostId = 7,
            Email = "ada@example.com",
            DisplayName = "Ada Lovelace",
            Rating = rating,
            CaptchaChallengeId = "challenge",
            CaptchaAnswer = "ABCDE",
            IpAddress = "203.0.113.7"
        };
    }

    /// <summary>
    /// Wires the rating service, the verification service and their stores together.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Exercises the real interaction between rating, verification and
    /// aggregation.</para>
    /// <para><b>Dependencies:</b> The fakes in this folder.</para>
    /// <para><b>Usage:</b> Reach into the exposed repositories to seed or inspect state.</para>
    /// </remarks>
    private sealed class RatingContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RatingContext"/> class.
        /// </summary>
        public RatingContext()
        {
            RatingRepo = new FakePostRatingRepo();
            TokenRepo = new FakeEmailVerificationTokenRepo();
            VerifiedEmailRepo = new FakeVerifiedEmailRepo();
            EmailSender = new FakeVerificationEmailSender();
            CaptchaService = new FakeCaptchaService();
            VerificationService = new EmailVerificationSvc(
                TokenRepo,
                VerifiedEmailRepo,
                new FakeBlogCommentRepo(),
                RatingRepo,
                new FakeSubscriberRepo(),
                EmailSender,
                new FakeConfiguration(new Dictionary<string, string?>
                {
                    ["SiteSettings:BaseUrl"] = "https://blog.example"
                }),
                new FakeSiteSettingsService(),
                NullLogger<EmailVerificationSvc>.Instance);
            Service = new RatingSvc(
                RatingRepo,
                CaptchaService,
                VerificationService,
                NullLogger<RatingSvc>.Instance);
        }

        /// <summary>Gets the in-memory rating store.</summary>
        public FakePostRatingRepo RatingRepo { get; }

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

        /// <summary>Gets the rating service under test.</summary>
        public RatingSvc Service { get; }
    }
}
