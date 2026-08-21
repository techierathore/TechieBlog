using System.Xml.Linq;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TechieBlog.Tests.TestDoubles;
using Xunit;

namespace TechieBlog.Tests.Feeds;

/// <summary>
/// Unit tests for <see cref="RssFeedSvc"/> (REQ-FN-037, REQ-UI-046, BRD-63).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The feed is an anonymous, machine-consumed surface, so two classes of
/// defect matter more here than anywhere else in the engine. First, disclosure: an aggregator
/// caches and re-publishes what it is given, so a draft that reaches the feed is effectively
/// unretractable — these tests pin that the service reads through the published-only repository
/// member and nothing else. Second, well-formedness: author-supplied titles and abstracts are
/// interpolated into XML, and a single unescaped ampersand makes every reader reject the whole
/// document, silently.</para>
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IBlogPostRepo"/> and
/// <see cref="ISiteSettingsService"/>; <see cref="StubConfiguration"/> for the base URL;
/// <see cref="NullLogger{T}"/>. No database, no host.</para>
/// </remarks>
public class RssFeedSvcTests
{
    private const string BaseUrl = "https://blog.example.com";

    private readonly IBlogPostRepo postRepo = Substitute.For<IBlogPostRepo>();
    private readonly ISiteSettingsService siteSettingsService = Substitute.For<ISiteSettingsService>();
    private readonly RssFeedSvc service;

    /// <summary>
    /// Wires the service under test to substituted dependencies, a configured base URL carrying a
    /// trailing slash (so the trimming is exercised by every test) and a default settings
    /// aggregate.
    /// </summary>
    public RssFeedSvcTests()
    {
        siteSettingsService.GetSettingsAsync().Returns(new SiteSettings
        {
            SiteTitle = "TechieBlog",
            MetaDescription = "Notes on .NET"
        });

        service = new RssFeedSvc(
            postRepo,
            siteSettingsService,
            new StubConfiguration(new Dictionary<string, string?> { ["SiteSettings:BaseUrl"] = BaseUrl + "/" }),
            NullLogger<RssFeedSvc>.Instance);
    }

    /// <summary>
    /// The document parses as XML and is an RSS 2.0 feed with exactly one channel, which is the
    /// minimum every reader checks before it looks at a single item.
    /// </summary>
    [Fact]
    public async Task FeedIsWellFormedRssTwoPointZero()
    {
        GivenPosts(BuildPost(1, "First Post", "first-post"));

        var document = XDocument.Parse(await service.GenerateFeedAsync());

        Assert.Equal("rss", document.Root!.Name.LocalName);
        Assert.Equal("2.0", document.Root.Attribute("version")!.Value);
        Assert.Single(document.Root.Elements("channel"));
    }

    /// <summary>
    /// Items come from the published-only repository read. This is the disclosure gate: the
    /// unfiltered admin listing must never be the source for an anonymous feed, and asserting on
    /// the member that was called is the only way to catch a swap that still happens to return
    /// published rows in the test data.
    /// </summary>
    [Fact]
    public async Task FeedReadsThroughThePublishedOnlyProjection()
    {
        GivenPosts(BuildPost(1, "First Post", "first-post"));

        await service.GenerateFeedAsync();

        await postRepo.Received(1).GetPublishedPostsAsync(RssFeedSvc.MaxItems, 0, Arg.Any<CancellationToken>());
        postRepo.DidNotReceive().GetAll();
    }

    /// <summary>
    /// Every item carries the five fields the requirement names — title, link, description,
    /// publication date and author — plus the permalink guid a reader uses to tell an edited post
    /// from a new one.
    /// </summary>
    [Fact]
    public async Task ItemCarriesTitleLinkDescriptionDateAndAuthor()
    {
        var post = BuildPost(7, "Reading Query Plans", "reading-query-plans");
        post.Abstract = "How to read EXPLAIN output.";
        post.BlogWriter = "Sam Rathore";
        post.CreatedOn = new DateTime(2026, 8, 1, 9, 30, 0, DateTimeKind.Utc);
        GivenPosts(post);

        var item = await SingleItemAsync();

        Assert.Equal("Reading Query Plans", item.Element("title")!.Value);
        Assert.Equal($"{BaseUrl}/post/reading-query-plans", item.Element("link")!.Value);
        Assert.Equal("How to read EXPLAIN output.", item.Element("description")!.Value);
        Assert.Equal("Sat, 01 Aug 2026 09:30:00 GMT", item.Element("pubDate")!.Value);
        Assert.Equal(
            "Sam Rathore",
            item.Element(XName.Get("creator", "http://purl.org/dc/elements/1.1/"))!.Value);
        Assert.Equal($"{BaseUrl}/post/reading-query-plans", item.Element("guid")!.Value);
        Assert.Equal("true", item.Element("guid")!.Attribute("isPermaLink")!.Value);
    }

    /// <summary>
    /// A title containing XML metacharacters is escaped, not injected: the document still parses
    /// and the title round-trips as text rather than becoming markup. One unescaped ampersand in
    /// one post title would otherwise take the whole feed down for every subscriber.
    /// </summary>
    [Fact]
    public async Task HostileTitleIsEscapedRatherThanInjected()
    {
        var post = BuildPost(2, "Tips & Tricks for <script>alert(1)</script>", "tips-and-tricks");
        post.Abstract = "Cost < benefit & \"quotes\"";
        GivenPosts(post);

        var xml = await service.GenerateFeedAsync();
        var item = XDocument.Parse(xml).Descendants("item").Single();

        Assert.Equal("Tips & Tricks for <script>alert(1)</script>", item.Element("title")!.Value);
        Assert.Equal("Cost < benefit & \"quotes\"", item.Element("description")!.Value);
        Assert.DoesNotContain("<script>", xml, StringComparison.Ordinal);
    }

    /// <summary>
    /// The channel advertises itself with an Atom self link at the canonical feed path, so an
    /// aggregator that reached the document by any alias records one subscription rather than two.
    /// </summary>
    [Fact]
    public async Task ChannelAdvertisesItsCanonicalSelfLink()
    {
        GivenPosts();

        var channel = XDocument.Parse(await service.GenerateFeedAsync()).Descendants("channel").Single();
        var selfLink = channel.Elements(XName.Get("link", "http://www.w3.org/2005/Atom")).Single();

        Assert.Equal(BaseUrl + RssFeedSvc.FeedPath, selfLink.Attribute("href")!.Value);
        Assert.Equal("self", selfLink.Attribute("rel")!.Value);
        Assert.Equal("application/rss+xml", selfLink.Attribute("type")!.Value);
        Assert.Equal(BaseUrl + "/", channel.Element("link")!.Value);
    }

    /// <summary>
    /// The channel title and description come from the administrator's site settings, so the feed
    /// a reader subscribes to is named the same as the site rather than carrying a build-time
    /// constant.
    /// </summary>
    [Fact]
    public async Task ChannelMetadataComesFromSiteSettings()
    {
        siteSettingsService.GetSettingsAsync().Returns(new SiteSettings
        {
            SiteTitle = "Rathore on .NET",
            MetaDescription = "Deep dives"
        });
        GivenPosts();

        var channel = XDocument.Parse(await service.GenerateFeedAsync()).Descendants("channel").Single();

        Assert.Equal("Rathore on .NET", channel.Element("title")!.Value);
        Assert.Equal("Deep dives", channel.Element("description")!.Value);
    }

    /// <summary>
    /// A post with no abstract still gets a readable summary: the body is reduced to plain text
    /// rather than shipping raw markdown or HTML into someone else's reader.
    /// </summary>
    [Fact]
    public async Task BlankAbstractFallsBackToAPlainTextExcerpt()
    {
        var post = BuildPost(3, "Markdown", "markdown");
        post.Abstract = string.Empty;
        post.PostContent = "## Heading\n\nSome **bold** text with <em>markup</em>.";
        GivenPosts(post);

        var description = (await SingleItemAsync()).Element("description")!.Value;

        Assert.DoesNotContain("**", description, StringComparison.Ordinal);
        Assert.DoesNotContain("<em>", description, StringComparison.Ordinal);
        Assert.Contains("bold", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// A repository failure produces an empty but well-formed channel instead of an exception, so
    /// a reader sees "nothing new" rather than dropping a feed that returned a 500.
    /// </summary>
    [Fact]
    public async Task RepositoryFailureStillProducesAWellFormedEmptyChannel()
    {
        postRepo.GetPublishedPostsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<BlogPost>>>(_ => throw new InvalidOperationException("database down"));

        var document = XDocument.Parse(await service.GenerateFeedAsync());

        Assert.Single(document.Descendants("channel"));
        Assert.Empty(document.Descendants("item"));
    }

    /// <summary>
    /// A timestamp read out of PostgreSQL arrives as <see cref="DateTimeKind.Unspecified"/>. It is
    /// stored in UTC, so it must be stamped GMT rather than having the server's local offset
    /// applied — otherwise every item in the feed is dated wrongly by that offset, which is
    /// invisible on a UTC development machine and wrong everywhere else.
    /// </summary>
    [Fact]
    public async Task UnspecifiedKindTimestampIsTreatedAsUtc()
    {
        var post = BuildPost(4, "Dated", "dated");
        post.CreatedOn = new DateTime(2026, 1, 5, 6, 7, 8, DateTimeKind.Unspecified);
        GivenPosts(post);

        var item = await SingleItemAsync();

        Assert.Equal("Mon, 05 Jan 2026 06:07:08 GMT", item.Element("pubDate")!.Value);
    }

    /// <summary>
    /// Points the substituted repository at the supplied posts.
    /// </summary>
    /// <param name="posts">The published posts the feed should be built from.</param>
    private void GivenPosts(params BlogPost[] posts)
    {
        postRepo.GetPublishedPostsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<BlogPost>>(posts));
    }

    /// <summary>
    /// Generates the feed and returns its only item.
    /// </summary>
    /// <returns>The single <c>item</c> element.</returns>
    private async Task<XElement> SingleItemAsync()
    {
        return XDocument.Parse(await service.GenerateFeedAsync()).Descendants("item").Single();
    }

    /// <summary>
    /// Builds a minimal published post.
    /// </summary>
    /// <param name="postId">Identifier.</param>
    /// <param name="title">Post title.</param>
    /// <param name="slug">URL slug.</param>
    /// <returns>A published post carrying the supplied identity.</returns>
    private static BlogPost BuildPost(long postId, string title, string slug)
    {
        return new BlogPost
        {
            PostID = postId,
            Title = title,
            Slug = slug,
            Abstract = "Summary of " + title,
            Published = true,
            CreatedOn = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc)
        };
    }
}
