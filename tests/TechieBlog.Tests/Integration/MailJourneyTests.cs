using BlogEngine;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using BlogUI.Pages.BlogPages;
using Bunit;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using TechieBlog.Tests.Engagement;
using TrBlazeUI.Primitives.Extensions;

namespace TechieBlog.Tests.Integration;

/// <summary>
/// [REQ-NFR-045] [REQ-FN-033] [REQ-FN-048] The whole confirmation journey, end to end, with real SMTP
/// mail: a reader rates, comments and subscribes; the confirmation mail arrives at a mail catcher; the
/// link in it is opened on the real <c>/verify/{token}</c> page; and the rating counts, the comment
/// leaves "waiting for confirmation", the subscriber is confirmed.
/// </summary>
/// <remarks>
/// <para>Nothing is faked on the mail path: the engine's own registrations are used, the mail server is
/// saved through the Settings service exactly as the Settings screen saves it, and the mail leaves
/// through <c>SmtpEmailService</c> over TCP to <see cref="MailCatcher"/>. Only the captcha is replaced —
/// it is not what this test is about.</para>
/// <para>The database is a throwaway <c>tbtest_…</c> database (<see cref="TempDatabase"/>), created and
/// dropped by this class. Written after the 2026-11-26 live report of a rating whose confirmation mail
/// was never sent.</para>
/// </remarks>
public sealed class MailJourneyTests : BunitContext, IClassFixture<TempDatabase>
{
    private const string SiteAddress = "https://blog.journey.test";

    private readonly TempDatabase database;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailJourneyTests"/> class.
    /// </summary>
    /// <param name="database">The throwaway database.</param>
    public MailJourneyTests(TempDatabase database)
    {
        this.database = database;
    }

    /// <summary>
    /// Rate → the mail arrives → the link is opened → the rating counts.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-045 rating journey: the mail arrives at the catcher, the link is opened, the rating counts")]
    public async Task RatingJourneyEndsWithTheRatingCounted()
    {
        await using var catcher = await StartAsync();
        var ratings = Services.GetRequiredService<RatingSvc>();
        var postId = await FirstPostIdAsync();
        var countBefore = await ratings.GetRatingCountAsync(postId);

        var submitted = await ratings.SubmitRatingAsync(new RatingSubmission
        {
            PostId = postId, Email = "rater@journey.test", DisplayName = "Rater", Rating = 4,
            CaptchaChallengeId = "c", CaptchaAnswer = "a"
        });
        Assert.Equal("We sent a link to r•••@journey.test. Open it within 24 hours to confirm your rating.", submitted.Data!.Message);

        OpenLink(await catcher.WaitForAsync("rater@journey.test"), "verify-confirmed");

        Assert.Equal(countBefore + 1, await ratings.GetRatingCountAsync(postId));
    }

    /// <summary>
    /// Comment → the mail arrives → the link is opened → the comment is no longer waiting for confirmation.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-045 comment journey: the mail arrives at the catcher, the link is opened, the comment is confirmed")]
    public async Task CommentJourneyEndsWithTheCommentConfirmed()
    {
        await using var catcher = await StartAsync();
        var submitted = await Services.GetRequiredService<CommentSvc>().SubmitCommentAsync(new CommentSubmission
        {
            PostId = await FirstPostIdAsync(), AuthorName = "Commenter", AuthorEmail = "commenter@journey.test",
            CommentText = "A real comment that goes through the real confirmation mail.",
            CaptchaChallengeId = "c", CaptchaAnswer = "a", RenderedOn = DateTime.UtcNow.AddMinutes(-2),
            IpAddress = "203.0.113.9", UserAgent = "journey-test"
        });
        Assert.StartsWith("We sent a link to c•••@journey.test.", submitted.Data!.Message);

        OpenLink(await catcher.WaitForAsync("commenter@journey.test"), "verify-confirmed");

        var status = await ScalarAsync<string>(
            "SELECT ModerationStatus FROM BlogComment WHERE CommentID = @Id", new { Id = submitted.Data.CommentId });
        Assert.NotEqual(CommentModerationStatus.PendingVerification, status);
    }

    /// <summary>
    /// Subscribe → the mail arrives → the link is opened → the subscriber is confirmed.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-045 subscription journey: the mail arrives at the catcher, the link is opened, the subscriber is confirmed")]
    public async Task SubscriptionJourneyEndsWithTheSubscriberConfirmed()
    {
        await using var catcher = await StartAsync();
        var submitted = await Services.GetRequiredService<SubscriberSvc>()
            .SubscribePendingAsync("reader@journey.test", "Reader", Xunit.TestContext.Current.CancellationToken);
        Assert.True(submitted.Data);

        OpenLink(await catcher.WaitForAsync("reader@journey.test"), "verify-subscribed");

        var subscriber = await Services.GetRequiredService<ISubscriberRepo>()
            .GetByEmailAsync("reader@journey.test", Xunit.TestContext.Current.CancellationToken);
        Assert.True(subscriber!.IsConfirmed);
    }

    /// <summary>
    /// The mail server refuses the address: the reader is told, with the masked address, that nothing was
    /// saved, and no rating is left waiting in the database.
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-045 a refused mail leaves no rating waiting and says so with the masked address")]
    public async Task RefusedMailLeavesNoRatingWaiting()
    {
        await using var catcher = await StartAsync();
        catcher.RejectRecipientsWith = "550 5.1.1 Mailbox unavailable";
        var postId = await FirstPostIdAsync();

        var submitted = await Services.GetRequiredService<RatingSvc>().SubmitRatingAsync(new RatingSubmission
        {
            PostId = postId, Email = "bounce@journey.test", Rating = 2, CaptchaChallengeId = "c", CaptchaAnswer = "a"
        });

        var waiting = await ScalarAsync<long>(
            "SELECT COUNT(*) FROM PostRating WHERE LOWER(Email) = 'bounce@journey.test'", null);
        Assert.Equal((EmailVerificationSvc.SendFailedMessage("bounce@journey.test"), 0L), (submitted.ErrorMessage, waiting));
    }

    /// <summary>
    /// Starts the catcher, wires the engine against the throwaway database, and saves the catcher as the
    /// mail server through the Settings service — the same call the Settings screen makes.
    /// </summary>
    /// <returns>The running catcher.</returns>
    private async Task<MailCatcher> StartAsync()
    {
        Assert.SkipWhen(database.ConnectionString == null, database.SkipReason ?? "No database.");
        var catcher = MailCatcher.Start();

        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SiteSettings:BaseUrl"] = SiteAddress })
            .Build());
        Services.AddLogging();
        BlogSvcInitializer.Initialize(Services, database.ConnectionString!);
        Services.AddScoped<ICaptchaService>(_ => new FakeCaptchaService());
        Services.AddTrBlazeUIPrimitives();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var siteSettings = Services.GetRequiredService<ISiteSettingsService>();
        var settings = await siteSettings.GetEditableSettingsAsync();
        settings.Smtp = new SmtpSettings
        {
            Host = "127.0.0.1", Port = catcher.Port, IsSslEnabled = false,
            FromAddress = "blog@journey.test", FromName = "Journey"
        };
        var saved = await siteSettings.SaveSettingsAsync(settings);
        Assert.True(saved.IsSuccess, saved.ErrorMessage);
        return catcher;
    }

    /// <summary>
    /// Opens the mailed link the way a browser does: the <c>/verify/{token}</c> page renders with the
    /// token from the link and redeems it.
    /// </summary>
    /// <param name="mail">The caught mail.</param>
    /// <param name="expectedState">The page's <c>data-testid</c> for success.</param>
    private void OpenLink(CaughtMail mail, string expectedState)
    {
        var link = mail.VerificationLink();
        Assert.StartsWith($"{SiteAddress}/verify/", link);

        var page = Render<VerifyEmail>(parameters => parameters.Add(p => p.Token, link[(link.LastIndexOf('/') + 1)..]));
        try
        {
            page.WaitForElement($"[data-testid={expectedState}]", TimeSpan.FromSeconds(60));
        }
        catch (Exception waitFailure)
        {
            throw new InvalidOperationException($"The confirmation page did not reach {expectedState}. It shows: {page.Markup}", waitFailure);
        }
    }

    /// <summary>
    /// The post the journey rates and comments on: the first published one, or a new one written by the
    /// first seeded user when the fresh database holds none.
    /// </summary>
    /// <returns>The post id.</returns>
    private async Task<long> FirstPostIdAsync()
    {
        var existing = await ScalarAsync<long?>("SELECT MIN(PostId) FROM BlogPost", null, isNullAllowed: true);
        if (existing is > 0)
            return existing.Value;

        return await ScalarAsync<long>(
            "INSERT INTO BlogPost (Title, PostContent, UserId, Published, Slug) " +
            "SELECT 'Mail journey post', 'Body', MIN(UserId), TRUE, 'mail-journey-post' FROM BlogUser RETURNING PostId", null);
    }

    private async Task<T> ScalarAsync<T>(string sql, object? parameters, bool isNullAllowed = false)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        var value = await connection.ExecuteScalarAsync<T>(sql, parameters);
        return value is null && !isNullAllowed ? throw new InvalidOperationException($"No value for: {sql}") : value!;
    }
}
