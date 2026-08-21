using System.Text.RegularExpressions;
using BlogEngine.Common;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TechieBlog.Tests.TestDoubles;
using Xunit;

namespace TechieBlog.Tests.Newsletters;

/// <summary>
/// The unsubscribe credential mailed in a newsletter is scoped to that issue (REQ-FN-060).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-059 gave <c>Subscriber.UnsubscribeToken</c> a lifecycle — burned on
/// use, 400-day expiry, rotated on re-consent — but nothing called
/// <c>SubscriberSvc.IssueUnsubscribeTokenAsync</c>, so <c>NewsletterSvc</c> kept reading the single
/// row-level token off the recipient row and the SAME credential shipped in every issue an address
/// ever received. Its blast radius was "every mail we have ever sent you"; one forwarded archive
/// mail handed the recipient a link that worked against every past and future issue. These tests pin
/// the narrowed contract at the service boundary.</para>
///
/// <para><b>The design being pinned, and the trap in the obvious alternative.</b> Each send ADDS a
/// token row scoped to that (subscriber, issue) pair; it does not rotate the row-level one. The
/// literal reading — "the newest send supersedes the older tokens" — would refuse a subscriber who
/// receives issue #1 and issue #2 and then clicks Unsubscribe in the OLDER mail, which is a
/// CAN-SPAM-shaped failure and strictly worse than the over-broad credential it would be fixing.
/// <see cref="AnOlderIssuesLinkStillUnsubscribes"/> is the test that would fail if a future change
/// "finished" this the other way, and the full reasoning is in the header of
/// <c>027-PerIssueUnsubscribeToken.sql</c>. Rotation still happens, on re-consent only, which is
/// what <see cref="ATokenIssuedBeforeAReConsentIsRefused"/> covers.</para>
///
/// <para><b>Dependencies:</b> xUnit, NSubstitute for <c>INewsletterRepo</c> and
/// <c>ISubscriberRepo</c>, and the shared <c>RecordingEmailService</c> / <c>StubConfiguration</c>
/// doubles. No database and no SMTP.</para>
///
/// <para><b>Stub the member the service actually awaits.</b> Every repository member used here is a
/// DEFAULT interface implementation, and Castle DynamicProxy intercepts those too — an unstubbed
/// <c>GetByNewsletterTokenAsync</c> returns a completed task holding <c>null</c> rather than falling
/// through to the default body. That is the failure mode recorded on this exact code path under
/// REQ-NFR-026, where a stubbed synchronous twin left the service treating a known address as new
/// and mailing a second confirmation link. If an assertion here disagrees with a stub that looks
/// right, check which member the service calls before changing the assertion.</para>
/// </remarks>
public class PerIssueUnsubscribeTokenTests
{
    private const string BaseUrl = "https://blog.example";
    private const long IssueId = 7;
    private const long SecondIssueId = 8;
    private const long SubscriberId = 42;
    private const string RowLevelToken = "rowleveltoken";

    /// <summary>
    /// A dispatch issues a token bound to the issue being sent and to the recipient, rather than
    /// reusing the row-level one — the defect this requirement exists to remove.
    /// </summary>
    [Fact]
    public async Task SendIssuesATokenScopedToThatIssue()
    {
        var subscriberRepo = SubscriberRepoDouble();
        var transport = new RecordingEmailService();

        await BuildNewsletterService(subscriberRepo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        await subscriberRepo.Received(1).IssueTokenForNewsletterAsync(
            SubscriberId, IssueId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The link that actually reaches the subscriber carries the freshly issued per-issue token, not
    /// the row-level one. Issuing a token and then mailing the old value would look correct in the
    /// database and still leak the wide credential.
    /// </summary>
    [Fact]
    public async Task TheMailedLinkCarriesThePerIssueTokenNotTheRowLevelOne()
    {
        var subscriberRepo = SubscriberRepoDouble();
        var transport = new RecordingEmailService();

        await BuildNewsletterService(subscriberRepo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        var mailed = TokenFromBody(transport.SentMessages[0].HtmlBody);
        Assert.NotEqual(RowLevelToken, mailed);
        Assert.Equal($"{BaseUrl}/unsubscribe/{mailed}", transport.SentMessages[0].UnsubscribeUrl);
    }

    /// <summary>
    /// Two issues sent to the same subscriber carry two different tokens, which is the whole point:
    /// a credential that leaks out of one issue authorises that issue and nothing else.
    /// </summary>
    [Fact]
    public async Task TwoIssuesToOneSubscriberCarryDifferentTokens()
    {
        var subscriberRepo = SubscriberRepoDouble();
        var transport = new RecordingEmailService();
        var service = BuildNewsletterService(subscriberRepo, transport);

        await service.SendAsync(IssueId, NewsletterAudience.Everyone);
        await service.SendAsync(SecondIssueId, NewsletterAudience.Everyone);

        Assert.Equal(2, transport.SentMessages.Count);
        var first = TokenFromBody(transport.SentMessages[0].HtmlBody);
        var second = TokenFromBody(transport.SentMessages[1].HtmlBody);
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// A per-issue token is 64 hexadecimal characters — 256 bits from a cryptographically secure
    /// RNG, matching the column width. A token is a bearer credential that authorises a state change
    /// on a stranger's row, so a short or predictable one is a security defect, not a cosmetic one.
    /// </summary>
    [Fact]
    public async Task IssuedTokensAreFullWidthHexadecimal()
    {
        var subscriberRepo = SubscriberRepoDouble();
        var transport = new RecordingEmailService();

        await BuildNewsletterService(subscriberRepo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        var mailed = TokenFromBody(transport.SentMessages[0].HtmlBody);
        Assert.Matches("^[0-9a-f]{64}$", mailed);
    }

    /// <summary>
    /// When a per-issue token cannot be issued the message still goes out carrying the row-level
    /// token. A coarser credential is a far smaller harm than a mailing with no working way off it,
    /// which is what skipping the link or failing the send would produce.
    /// </summary>
    [Fact]
    public async Task AFailedIssuanceFallsBackToTheRowLevelTokenRatherThanMailingNoLink()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        subscriberRepo.IssueTokenForNewsletterAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var transport = new RecordingEmailService();

        await BuildNewsletterService(subscriberRepo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        Assert.Equal(
            $"{BaseUrl}/unsubscribe/{RowLevelToken}", transport.SentMessages[0].UnsubscribeUrl);
    }

    /// <summary>
    /// Redeeming a per-issue token opts the subscriber out through the per-issue path, which burns
    /// that token row and records the withdrawal in one statement — the row-level burn must not be
    /// used, because it would spend a credential the reader never presented.
    /// </summary>
    [Fact]
    public async Task APerIssueTokenUnsubscribesThroughThePerIssuePath()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        subscriberRepo.GetByNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>())
            .Returns(HolderOf("issueone", issuedOn: Now.AddDays(-3)));
        subscriberRepo.RedeemNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>()).Returns(true);

        var result = await BuildSubscriberService(subscriberRepo).UnsubscribeByTokenAsync("issueone");

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
        await subscriberRepo.Received(1).RedeemNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>());
        await subscriberRepo.DidNotReceive().RecordWithdrawalAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// THE CASE THE OBVIOUS DESIGN BREAKS. A subscriber who received issue #1 and issue #2 and then
    /// opens the OLDER mail is unsubscribed, not told their link is invalid. Refusing a genuine
    /// withdrawal because a newer issue went out is a compliance failure; if this test is ever
    /// changed to expect a refusal, read the header of 027-PerIssueUnsubscribeToken.sql first.
    /// </summary>
    [Fact]
    public async Task AnOlderIssuesLinkStillUnsubscribes()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        var older = HolderOf("issueone", issuedOn: Now.AddDays(-30));
        subscriberRepo.GetByNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>()).Returns(older);
        subscriberRepo.RedeemNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>()).Returns(true);

        // A newer issue has since been mailed; its token exists but has not been opened.
        subscriberRepo.GetByNewsletterTokenAsync("issuetwo", Arg.Any<CancellationToken>())
            .Returns(HolderOf("issuetwo", issuedOn: Now.AddDays(-1)));

        var result = await BuildSubscriberService(subscriberRepo).UnsubscribeByTokenAsync("issueone");

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
    }

    /// <summary>
    /// A token issued BEFORE the subscriber's current consent instant is refused. That is how
    /// "unsubscribe tokens rotate on re-consent" survives a subscriber holding several live tokens:
    /// re-consent moves ConfirmedOn forward and invalidates every token issued under the previous
    /// consent at once, so a link that leaked out of an archived mailbox cannot opt an address out
    /// again after its owner deliberately came back.
    /// </summary>
    [Fact]
    public async Task ATokenIssuedBeforeAReConsentIsRefused()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        var stale = HolderOf("issueone", issuedOn: Now.AddDays(-30));
        stale.ConfirmedOn = Now.AddDays(-2);
        subscriberRepo.GetByNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>()).Returns(stale);

        var result = await BuildSubscriberService(subscriberRepo).UnsubscribeByTokenAsync("issueone");

        Assert.True(result.IsFailure);
        await subscriberRepo.DidNotReceive()
            .RedeemNewsletterTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A per-issue token that was already spent performs no second state change, so a link that has
    /// done its work cannot remove an address again after the owner resubscribed.
    /// </summary>
    [Fact]
    public async Task ABurnedPerIssueTokenIsRefused()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        var burned = HolderOf("issueone", issuedOn: Now.AddDays(-3));
        burned.UnsubscribeTokenUsedOn = Now.AddDays(-2);
        subscriberRepo.GetByNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>()).Returns(burned);

        var result = await BuildSubscriberService(subscriberRepo).UnsubscribeByTokenAsync("issueone");

        Assert.True(result.IsFailure);
        await subscriberRepo.DidNotReceive()
            .RedeemNewsletterTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A per-issue token opened after the subscriber has already left reports the already-off case
    /// and writes nothing, rather than erroring — so the second issue's unopened link, and a mail
    /// client that prefetches, are both harmless.
    /// </summary>
    [Fact]
    public async Task APerIssueTokenForAnAlreadyWithdrawnSubscriberIsANoOp()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        var withdrawn = HolderOf("issuetwo", issuedOn: Now.AddDays(-3));
        withdrawn.UnsubscribedOn = Now.AddHours(-1);
        subscriberRepo.GetByNewsletterTokenAsync("issuetwo", Arg.Any<CancellationToken>()).Returns(withdrawn);

        var result = await BuildSubscriberService(subscriberRepo).UnsubscribeByTokenAsync("issuetwo");

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.AlreadyUnsubscribed, result.Data);
        await subscriberRepo.DidNotReceive()
            .RedeemNewsletterTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A row-level token — every unsubscribe link already sitting in a delivered mail — still
    /// resolves and still removes the subscriber. Narrowing the credential for future issues must
    /// never strand the links already out in the world.
    /// </summary>
    [Fact]
    public async Task ALegacyRowLevelTokenStillUnsubscribes()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        subscriberRepo.GetByNewsletterTokenAsync(RowLevelToken, Arg.Any<CancellationToken>())
            .Returns((Subscriber)null);
        subscriberRepo.GetByUnsubscribeTokenAsync(RowLevelToken, Arg.Any<CancellationToken>())
            .Returns(HolderOf(RowLevelToken, issuedOn: null));
        subscriberRepo.RecordWithdrawalAsync(SubscriberId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await BuildSubscriberService(subscriberRepo).UnsubscribeByTokenAsync(RowLevelToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
        await subscriberRepo.Received(1).RecordWithdrawalAsync(SubscriberId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unknown token, a refused per-issue token and a blank one all come back with the identical
    /// wording, so the anonymous route cannot be used to test whether a guessed token belongs to a
    /// real subscriber. Adding a token store must not add a way to distinguish the failures.
    /// </summary>
    [Fact]
    public async Task EveryRefusalIsIndistinguishable()
    {
        var subscriberRepo = Substitute.For<ISubscriberRepo>();
        var stale = HolderOf("issueone", issuedOn: Now.AddDays(-30));
        stale.ConfirmedOn = Now.AddDays(-2);
        subscriberRepo.GetByNewsletterTokenAsync("issueone", Arg.Any<CancellationToken>()).Returns(stale);
        var service = BuildSubscriberService(subscriberRepo);

        var superseded = await service.UnsubscribeByTokenAsync("issueone");
        var unknown = await service.UnsubscribeByTokenAsync("nothing-resolves-to-this");
        var blank = await service.UnsubscribeByTokenAsync("   ");

        Assert.Equal(blank.ErrorMessage, unknown.ErrorMessage);
        Assert.Equal(blank.ErrorMessage, superseded.ErrorMessage);
    }

    /// <summary>
    /// Issuing a per-issue token touches no consent column and does not rotate the row-level token.
    /// Handing someone a link is not a consent decision, and a send that quietly rewrote the
    /// row-level token would be the superseding design this requirement rejected.
    /// </summary>
    [Fact]
    public async Task IssuingATokenDoesNotRotateOrReConsentAnything()
    {
        var subscriberRepo = SubscriberRepoDouble();

        await BuildNewsletterService(subscriberRepo, new RecordingEmailService())
            .SendAsync(IssueId, NewsletterAudience.Everyone);

        await subscriberRepo.DidNotReceive().RotateUnsubscribeTokenAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await subscriberRepo.DidNotReceive().RecordConsentAsync(
            Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A fixed "now" the per-token timestamps are positioned around.
    /// </summary>
    private static DateTime Now => DateTime.UtcNow;

    /// <summary>
    /// A subscriber double as the per-issue lookup projects one: the token properties describe the
    /// matched TOKEN row, the consent columns are the subscriber's own.
    /// </summary>
    /// <param name="token">The token that was presented.</param>
    /// <param name="issuedOn">When that token was issued; <c>null</c> for a legacy token.</param>
    /// <returns>A confirmed subscriber holding the supplied token.</returns>
    private static Subscriber HolderOf(string token, DateTime? issuedOn) => new Subscriber
    {
        SubscriberId = SubscriberId,
        Email = "reader@example.test",
        Name = "Reader",
        IsConfirmed = true,
        IsActive = true,
        ConfirmedOn = Now.AddDays(-90),
        UnsubscribeToken = token,
        UnsubscribeTokenIssuedOn = issuedOn
    };

    /// <summary>
    /// A subscriber repository double whose per-issue insert succeeds.
    /// </summary>
    /// <returns>The configured repository double.</returns>
    private static ISubscriberRepo SubscriberRepoDouble()
    {
        var repo = Substitute.For<ISubscriberRepo>();
        repo.IssueTokenForNewsletterAsync(
            Arg.Any<long>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return repo;
    }

    /// <summary>
    /// Builds the subscriber service over a repository double.
    /// </summary>
    /// <param name="subscriberRepo">The repository double.</param>
    /// <returns>The service under test.</returns>
    private static SubscriberSvc BuildSubscriberService(ISubscriberRepo subscriberRepo)
    {
        return new SubscriberSvc(subscriberRepo, NullLogger<SubscriberSvc>.Instance);
    }

    /// <summary>
    /// Builds the newsletter service wired to a real subscriber service over the supplied double.
    /// </summary>
    /// <param name="subscriberRepo">The subscriber repository double the token issuance runs against.</param>
    /// <param name="transport">The recording transport.</param>
    /// <returns>The service under test, configured with a known base URL.</returns>
    private static NewsletterSvc BuildNewsletterService(
        ISubscriberRepo subscriberRepo, RecordingEmailService transport)
    {
        var newsletterRepo = Substitute.For<INewsletterRepo>();
        newsletterRepo.GetByIdAsync(IssueId, Arg.Any<CancellationToken>()).Returns(Issue(IssueId));
        newsletterRepo.GetByIdAsync(SecondIssueId, Arg.Any<CancellationToken>()).Returns(Issue(SecondIssueId));
        newsletterRepo.GetRecipientsAsync(Arg.Any<NewsletterAudience>(), Arg.Any<CancellationToken>())
            .Returns(new List<Subscriber> { Recipient() });
        newsletterRepo.SlugExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);

        return new NewsletterSvc(
            newsletterRepo,
            transport,
            new MarkdownRenderer(),
            new StubConfiguration(new Dictionary<string, string?> { ["SiteSettings:BaseUrl"] = BaseUrl }),
            NullLogger<NewsletterSvc>.Instance,
            BuildSubscriberService(subscriberRepo));
    }

    /// <summary>
    /// The single recipient every send in this fixture resolves to.
    /// </summary>
    /// <returns>A confirmed subscriber holding the row-level token.</returns>
    private static Subscriber Recipient() => new Subscriber
    {
        SubscriberId = SubscriberId,
        Email = "reader@example.test",
        Name = "Reader",
        IsConfirmed = true,
        IsActive = true,
        UnsubscribeToken = RowLevelToken
    };

    /// <summary>
    /// An unsent draft.
    /// </summary>
    /// <param name="newsletterId">The issue identifier.</param>
    /// <returns>The draft issue.</returns>
    private static Newsletter Issue(long newsletterId) => new Newsletter
    {
        NewsletterId = newsletterId,
        Title = $"Issue {newsletterId}",
        Content = "Hello subscribers.",
        Status = Newsletter.StatusDraft,
        CreatedOn = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>
    /// Reads the unsubscribe token back out of a rendered HTML body.
    /// </summary>
    /// <param name="htmlBody">The delivered body.</param>
    /// <returns>The token carried by the footer's unsubscribe anchor.</returns>
    private static string TokenFromBody(string htmlBody)
    {
        var match = Regex.Match(htmlBody, @"href=""[^""]*/unsubscribe/([^""]+)""");
        Assert.True(match.Success, "The delivered body carried no unsubscribe link.");
        return match.Groups[1].Value;
    }
}
