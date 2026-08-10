using BlogEngine.Common;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechieBlog.Tests.TestDoubles;
using Xunit;

namespace TechieBlog.Tests.Newsletters;

/// <summary>
/// Behaviour of the newsletter unsubscribe path (REQ-FN-032, BRD-59).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The acceptance clause for REQ-FN-032 is "the unsubscribe link removes the
/// subscriber", and it went unmet for the life of the feature: the mailed URL pointed at a route no
/// page was ever registered for. These tests pin the service half of the contract — that a token
/// actually flips the subscriber, that following the same link twice is a no-op rather than an
/// error, and that an unresolvable token cannot be used to discover whether a guessed token is
/// real.</para>
///
/// <para><b>Dependencies:</b> xUnit, NSubstitute for <c>INewsletterRepo</c>, and the shared
/// <c>StubConfiguration</c> test double. No database and no host, so these run in the ordinary unit
/// pass; the route itself is covered by the Playwright smoke, because a green build is not evidence
/// that a Razor page is reachable anonymously.</para>
///
/// <para><b>Usage:</b> A failure here means an unsubscribe link has stopped removing subscribers,
/// has started throwing on a repeat visit, or has begun leaking token existence.</para>
/// </remarks>
public class NewsletterUnsubscribeTests
{
    private const string BaseUrl = "https://blog.example";
    private const string KnownToken = "9f8e7d6c5b4a39281706f5e4d3c2b1a09f8e7d6c5b4a39281706f5e4d3c2b1a0";

    /// <summary>
    /// A token belonging to a subscriber who is still on the list deactivates that subscriber and
    /// reports it as a fresh opt-out, which is the acceptance clause the requirement names.
    /// </summary>
    [Fact]
    public async Task UnsubscribeRemovesAnActiveSubscriber()
    {
        var repo = Substitute.For<INewsletterRepo>();
        repo.GetSubscriberByUnsubscribeTokenAsync(KnownToken, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscriber());

        var result = await BuildService(repo).UnsubscribeAsync(KnownToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
        await repo.Received(1).DeactivateSubscriberAsync(42L, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Re-opening the same link — a mail client that prefetches, a reader who clicks twice — reports
    /// the already-off case and writes nothing, rather than failing and telling the reader their
    /// unsubscribe did not work.
    /// </summary>
    [Fact]
    public async Task ReopeningTheLinkReportsAlreadyUnsubscribed()
    {
        var repo = Substitute.For<INewsletterRepo>();
        var subscriber = ActiveSubscriber();
        subscriber.IsConfirmed = false;
        repo.GetSubscriberByUnsubscribeTokenAsync(KnownToken, Arg.Any<CancellationToken>())
            .Returns(subscriber);

        var result = await BuildService(repo).UnsubscribeAsync(KnownToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(UnsubscribeOutcome.AlreadyUnsubscribed, result.Data);
        await repo.DidNotReceive().DeactivateSubscriberAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A token that resolves to nobody is refused, and no deactivation is attempted against a
    /// guessed identifier.
    /// </summary>
    [Fact]
    public async Task UnknownTokenIsRefused()
    {
        var repo = Substitute.For<INewsletterRepo>();
        repo.GetSubscriberByUnsubscribeTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Subscriber?)null);

        var result = await BuildService(repo).UnsubscribeAsync("not-a-real-token");

        Assert.True(result.IsFailure);
        Assert.Equal(UnsubscribeOutcome.NotRecognised, result.Data);
        await repo.DidNotReceive().DeactivateSubscriberAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A blank token is refused before the repository is touched, so a probe of the route costs no
    /// query.
    /// </summary>
    [Fact]
    public async Task BlankTokenIsRefusedWithoutQuerying()
    {
        var repo = Substitute.For<INewsletterRepo>();

        var result = await BuildService(repo).UnsubscribeAsync("   ");

        Assert.True(result.IsFailure);
        await repo.DidNotReceive()
            .GetSubscriberByUnsubscribeTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unknown token and a blank one produce the identical message, so the route cannot be used
    /// as an oracle that tells an attacker which guessed tokens belong to real subscribers.
    /// </summary>
    [Fact]
    public async Task UnknownAndBlankTokensAreIndistinguishable()
    {
        var repo = Substitute.For<INewsletterRepo>();
        repo.GetSubscriberByUnsubscribeTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((Subscriber?)null);
        var service = BuildService(repo);

        var unknown = await service.UnsubscribeAsync("not-a-real-token");
        var blank = await service.UnsubscribeAsync(string.Empty);

        Assert.Equal(blank.ErrorMessage, unknown.ErrorMessage);
    }

    /// <summary>
    /// Surrounding whitespace — the stray space a reader's mail client can leave on a wrapped URL —
    /// is trimmed before the lookup, so a token still resolves.
    /// </summary>
    [Fact]
    public async Task TokenIsTrimmedBeforeLookup()
    {
        var repo = Substitute.For<INewsletterRepo>();
        repo.GetSubscriberByUnsubscribeTokenAsync(KnownToken, Arg.Any<CancellationToken>())
            .Returns(ActiveSubscriber());

        var result = await BuildService(repo).UnsubscribeAsync($"  {KnownToken}  ");

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A repository failure is reported as a transient problem rather than as an invalid link: an
    /// outage is not a bad token, and telling a reader their link is dead would send them away for
    /// good instead of back to it.
    /// </summary>
    [Fact]
    public async Task RepositoryFailureIsNotReportedAsAnInvalidLink()
    {
        var repo = Substitute.For<INewsletterRepo>();
        repo.GetSubscriberByUnsubscribeTokenAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Subscriber?>(_ => throw new InvalidOperationException("connection refused"));
        var service = BuildService(repo);

        var outage = await service.UnsubscribeAsync(KnownToken);
        var blank = await service.UnsubscribeAsync(string.Empty);

        Assert.True(outage.IsFailure);
        Assert.NotEqual(blank.ErrorMessage, outage.ErrorMessage);
    }

    /// <summary>
    /// The mailed URL is absolute and points at the route the unsubscribe page is registered on. A
    /// relative or misspelled path is exactly the defect this cluster fixed, and it is invisible
    /// until a subscriber tries to opt out.
    /// </summary>
    [Fact]
    public void UnsubscribeUrlIsAbsoluteAndPointsAtTheRoute()
    {
        var url = BuildService(Substitute.For<INewsletterRepo>()).BuildUnsubscribeUrl(KnownToken);

        Assert.Equal($"{BaseUrl}/unsubscribe/{KnownToken}", url);
    }

    /// <summary>
    /// A missing token yields an empty URL rather than a link to the bare route, so a broken send
    /// cannot mail a footer whose "Unsubscribe" anchor silently goes nowhere useful.
    /// </summary>
    [Fact]
    public void MissingTokenYieldsNoUrl()
    {
        var url = BuildService(Substitute.For<INewsletterRepo>()).BuildUnsubscribeUrl(string.Empty);

        Assert.Equal(string.Empty, url);
    }

    /// <summary>
    /// Builds the service under test over the supplied repository.
    /// </summary>
    /// <param name="repo">The newsletter repository double.</param>
    /// <returns>A service configured with a known base URL.</returns>
    private static NewsletterSvc BuildService(INewsletterRepo repo)
    {
        return new NewsletterSvc(
            repo,
            new RecordingEmailService(),
            new MarkdownRenderer(),
            new StubConfiguration(new Dictionary<string, string?> { ["SiteSettings:BaseUrl"] = BaseUrl }),
            Substitute.For<ILogger<NewsletterSvc>>());
    }

    /// <summary>
    /// A subscriber who is on the list and holds the known token.
    /// </summary>
    /// <returns>A confirmed subscriber.</returns>
    private static Subscriber ActiveSubscriber() => new Subscriber
    {
        SubscriberId = 42,
        Email = "reader@example.test",
        Name = "Reader",
        IsConfirmed = true,
        IsActive = true,
        UnsubscribeToken = KnownToken
    };
}
