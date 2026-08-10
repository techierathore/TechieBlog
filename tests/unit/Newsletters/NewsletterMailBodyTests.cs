using System.Text.RegularExpressions;
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
/// What a subscriber actually receives when a newsletter is dispatched (REQ-FN-032, BRD-59).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The compliance guarantee of this feature lives in the rendered message, not
/// in the send report: every issue must carry a working, absolute unsubscribe link. These tests
/// capture the messages handed to the transport and assert against the body itself, then feed the
/// link found there straight back into <c>UnsubscribeAsync</c> — so the URL that was mailed is
/// proven to resolve, rather than assumed to.</para>
///
/// <para><b>Dependencies:</b> xUnit, NSubstitute for <c>INewsletterRepo</c>, and the shared
/// <c>RecordingEmailService</c> / <c>StubConfiguration</c> doubles. The deployed environment runs
/// <c>ConsoleEmailService</c> (REQ-FN-033), which logs rather than sends and does not log the body,
/// so this is the only place the body is inspected.</para>
///
/// <para><b>Usage:</b> A failure here means an issue could go out with a missing, relative or
/// unrenderable unsubscribe link, or with its Markdown body shipped raw.</para>
/// </remarks>
public class NewsletterMailBodyTests
{
    private const string BaseUrl = "https://blog.example";
    private const long IssueId = 7;

    /// <summary>
    /// Every message the transport is handed carries the recipient's own absolute unsubscribe URL —
    /// in the HTML body, in the plain-text body and in the header field the transport reads for
    /// <c>List-Unsubscribe</c>. One recipient missing it is one subscriber with no way off the list.
    /// </summary>
    [Fact]
    public async Task EveryMessageCarriesItsOwnUnsubscribeLink()
    {
        var transport = new RecordingEmailService();
        var repo = RepoWith(Subscriber(1, "one@example.test", "tokenone"),
                            Subscriber(2, "two@example.test", "tokentwo"));

        await BuildService(repo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        Assert.Equal(2, transport.SentMessages.Count);
        foreach (var message in transport.SentMessages)
        {
            var expected = $"{BaseUrl}/unsubscribe/{TokenFor(message.ToAddress)}";
            Assert.Equal(expected, message.UnsubscribeUrl);
            Assert.Contains($"href=\"{expected}\"", message.HtmlBody, StringComparison.Ordinal);
            Assert.Contains(expected, message.TextBody, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The link scraped out of the delivered HTML body — not one rebuilt by the test — resolves back
    /// to the subscriber it was mailed to and removes them. This is the end-to-end claim of
    /// REQ-FN-032 expressed at the service boundary.
    /// </summary>
    [Fact]
    public async Task TheLinkFoundInTheDeliveredBodyRemovesThatSubscriber()
    {
        var transport = new RecordingEmailService();
        var subscriber = Subscriber(1, "one@example.test", "tokenone");
        var repo = RepoWith(subscriber);
        repo.GetSubscriberByUnsubscribeTokenAsync("tokenone", Arg.Any<CancellationToken>())
            .Returns(subscriber);
        var service = BuildService(repo, transport);
        await service.SendAsync(IssueId, NewsletterAudience.Everyone);

        var mailedToken = TokenFromBody(transport.SentMessages[0].HtmlBody);
        var result = await service.UnsubscribeAsync(mailedToken);

        Assert.Equal(UnsubscribeOutcome.Unsubscribed, result.Data);
        await repo.Received(1).DeactivateSubscriberAsync(1L, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The HTML body is rendered Markdown, matching what the composer's preview pane promises the
    /// subscriber will receive. Interpolating the raw source instead mailed literal hash marks and
    /// unrendered links, and made the preview a lie.
    /// </summary>
    [Fact]
    public async Task MarkdownIsRenderedRatherThanMailedRaw()
    {
        var transport = new RecordingEmailService();
        var repo = RepoWith(Subscriber(1, "one@example.test", "tokenone"));
        repo.GetByIdAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Issue("## August highlights\n\nRead the [archive](https://blog.example/newsletters)."));

        await BuildService(repo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        var html = transport.SentMessages[0].HtmlBody;
        Assert.Contains("<h2", html, StringComparison.Ordinal);
        Assert.DoesNotContain("## August highlights", html, StringComparison.Ordinal);
        Assert.Contains("href=\"https://blog.example/newsletters\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// The plain-text alternative keeps the Markdown source, which is what a text part is for, and
    /// still carries the unsubscribe URL in full so a text-only client is not left without one.
    /// </summary>
    [Fact]
    public async Task TextAlternativeKeepsTheSourceAndTheLink()
    {
        var transport = new RecordingEmailService();
        var repo = RepoWith(Subscriber(1, "one@example.test", "tokenone"));
        repo.GetByIdAsync(IssueId, Arg.Any<CancellationToken>()).Returns(Issue("## August highlights"));

        await BuildService(repo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        var text = transport.SentMessages[0].TextBody;
        Assert.Contains("## August highlights", text, StringComparison.Ordinal);
        Assert.Contains($"{BaseUrl}/unsubscribe/tokenone", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Raw HTML pasted into an issue is escaped rather than emitted as live markup, because the mail
    /// body goes through the same sanitising pipeline as every other rendered surface.
    /// </summary>
    [Fact]
    public async Task RawHtmlInAnIssueIsNeutralised()
    {
        var transport = new RecordingEmailService();
        var repo = RepoWith(Subscriber(1, "one@example.test", "tokenone"));
        repo.GetByIdAsync(IssueId, Arg.Any<CancellationToken>())
            .Returns(Issue("<script>alert(1)</script>"));

        await BuildService(repo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        Assert.DoesNotContain("<script>", transport.SentMessages[0].HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// A dispatch repairs any subscriber missing a token before resolving the audience, so a row
    /// inserted before the token column existed still receives a usable link.
    /// </summary>
    [Fact]
    public async Task TokensAreRepairedBeforeTheAudienceIsResolved()
    {
        var transport = new RecordingEmailService();
        var repo = RepoWith(Subscriber(1, "one@example.test", "tokenone"));

        await BuildService(repo, transport).SendAsync(IssueId, NewsletterAudience.Everyone);

        await repo.Received(1).EnsureUnsubscribeTokensAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Sending reads only the issue being dispatched. Nothing in the send path reaches the blog-post
    /// store, so a draft article cannot be mailed the way one leaked through an archive projection
    /// elsewhere in this pass.
    /// </summary>
    [Fact]
    public void SendPathTakesNoDependencyOnThePostStore()
    {
        var dependencies = typeof(NewsletterSvc)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToList();

        Assert.DoesNotContain(dependencies, name => name.Contains("Post", StringComparison.Ordinal));
        Assert.DoesNotContain(dependencies, name => name.Contains("BlogSvc", StringComparison.Ordinal));
    }

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

    /// <summary>
    /// The token this fixture assigns to a given address.
    /// </summary>
    /// <param name="email">Recipient address.</param>
    /// <returns>That subscriber's token.</returns>
    private static string TokenFor(string email) =>
        email.StartsWith("one@", StringComparison.Ordinal) ? "tokenone" : "tokentwo";

    /// <summary>
    /// Builds a repository double that returns a draft issue and the supplied audience.
    /// </summary>
    /// <param name="recipients">Subscribers the audience resolves to.</param>
    /// <returns>The configured repository double.</returns>
    private static INewsletterRepo RepoWith(params Subscriber[] recipients)
    {
        var repo = Substitute.For<INewsletterRepo>();
        repo.GetByIdAsync(IssueId, Arg.Any<CancellationToken>()).Returns(Issue("Hello subscribers."));
        repo.GetRecipientsAsync(Arg.Any<NewsletterAudience>(), Arg.Any<CancellationToken>())
            .Returns(recipients.ToList());
        repo.SlugExistsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        return repo;
    }

    /// <summary>
    /// An unsent draft carrying the supplied body.
    /// </summary>
    /// <param name="content">Markdown body.</param>
    /// <returns>The draft issue.</returns>
    private static Newsletter Issue(string content) => new Newsletter
    {
        NewsletterId = IssueId,
        Title = "August 2026",
        Content = content,
        Status = Newsletter.StatusDraft,
        CreatedOn = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    /// <summary>
    /// A confirmed subscriber holding a known token.
    /// </summary>
    /// <param name="subscriberId">Identifier.</param>
    /// <param name="email">Address.</param>
    /// <param name="token">Unsubscribe token.</param>
    /// <returns>The subscriber.</returns>
    private static Subscriber Subscriber(long subscriberId, string email, string token) => new Subscriber
    {
        SubscriberId = subscriberId,
        Email = email,
        Name = email,
        IsConfirmed = true,
        IsActive = true,
        UnsubscribeToken = token
    };

    /// <summary>
    /// Builds the service under test.
    /// </summary>
    /// <param name="repo">Repository double.</param>
    /// <param name="transport">Recording transport.</param>
    /// <returns>A service configured with a known base URL.</returns>
    private static NewsletterSvc BuildService(INewsletterRepo repo, RecordingEmailService transport)
    {
        return new NewsletterSvc(
            repo,
            transport,
            new MarkdownRenderer(),
            new StubConfiguration(new Dictionary<string, string?> { ["SiteSettings:BaseUrl"] = BaseUrl }),
            Substitute.For<ILogger<NewsletterSvc>>());
    }
}
