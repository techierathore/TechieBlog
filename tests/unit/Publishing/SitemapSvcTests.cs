using System.Globalization;
using System.Xml.Linq;
using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TechieBlog.Tests.Dashboard;
using TechieBlog.Tests.TestDoubles;

namespace TechieBlog.Tests.Publishing;

/// <summary>
/// Unit tests for <see cref="SitemapSvc"/> — document shape, inclusion rules, date formatting and
/// the per-section swallow-and-log contract.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>sitemap.xml</c> is served anonymously and decides what a crawler is
/// invited to fetch, cache and surface, so its inclusion rules are a disclosure question as much as
/// an SEO one. These tests pin that the post section reads through the published-only repository
/// member, that a failing section is skipped rather than faulting the whole document, that dates are
/// emitted in the Gregorian W3C subset whatever culture the server runs under, and that the async
/// twin produces a byte-identical document.</para>
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IBlogPostRepo"/>,
/// <see cref="ICategoryRepo"/> and <see cref="IBlogTagRepo"/>; <see cref="StubConfiguration"/> for
/// <c>SiteSettings:BaseUrl</c>; <see cref="RecordingLogger{T}"/> to prove a swallowed section failure
/// was logged. <c>System.Xml.Linq</c> parses the output, which doubles as the well-formedness
/// assertion. No database.</para>
/// </remarks>
public class SitemapSvcTests
{
    private const string BaseUrl = "https://techieblog.example";

    private static readonly XNamespace SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private readonly IBlogPostRepo postRepo = Substitute.For<IBlogPostRepo>();
    private readonly ICategoryRepo categoryRepo = Substitute.For<ICategoryRepo>();
    private readonly IBlogTagRepo tagRepo = Substitute.For<IBlogTagRepo>();
    private readonly RecordingLogger<SitemapSvc> logger = new();

    /// <summary>
    /// Gives every repository an empty result by default so an individual test only has to arrange
    /// the section it is about.
    /// </summary>
    public SitemapSvcTests()
    {
        ArrangePosts();
        ArrangeCategories();
        ArrangeTags();
    }

    // -------------------------------------------------------------------------------------------
    // Document shape
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The document is a well-formed sitemap-protocol 0.9 <c>urlset</c> carrying the XML prologue, so
    /// a crawler can parse it rather than treating the response as broken.
    /// </summary>
    [Fact]
    public void SitemapIsAWellFormedUrlsetDocument()
    {
        // Arrange
        var service = CreateService();

        // Act
        var xml = service.GenerateSitemap();

        // Assert
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", xml, StringComparison.Ordinal);
        var root = XDocument.Parse(xml).Root;
        Assert.NotNull(root);
        Assert.Equal(SitemapNs + "urlset", root!.Name);
    }

    /// <summary>
    /// The site root leads the document at priority 1.0 and a daily change frequency, and carries a
    /// <c>lastmod</c> of today because the home page reflects whatever was published most recently.
    /// </summary>
    [Fact]
    public void SitemapLeadsWithTheSiteRootAtTopPriority()
    {
        // Arrange
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        var root = urls[0];
        Assert.Equal($"{BaseUrl}/", root.Loc);
        Assert.Equal("daily", root.ChangeFreq);
        Assert.Equal("1.0", root.Priority);
        Assert.Equal(DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), root.LastMod);
    }

    /// <summary>
    /// With nothing published and no taxonomy rows the document still ships — one entry, the site
    /// root — rather than an empty or absent body.
    /// </summary>
    [Fact]
    public void SitemapContainsOnlyTheRootWhenTheSiteIsEmpty()
    {
        // Arrange
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        var only = Assert.Single(urls);
        Assert.Equal($"{BaseUrl}/", only.Loc);
    }

    /// <summary>
    /// Sections are emitted root, posts, categories, tags, and each carries the priority and change
    /// frequency that matches how often that surface actually changes.
    /// </summary>
    [Fact]
    public void SitemapEmitsPostsThenCategoriesThenTagsWithTheirOwnPriorities()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "async-all-the-way", PublishedOn = new DateTime(2026, 3, 4) });
        ArrangeCategories(new Category { Slug = "dotnet" });
        ArrangeTags(new BlogTag { Slug = "blazor" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal(
            new[]
            {
                $"{BaseUrl}/",
                $"{BaseUrl}/post/async-all-the-way",
                $"{BaseUrl}/category/dotnet",
                $"{BaseUrl}/tag/blazor"
            },
            urls.Select(url => url.Loc));
        Assert.Equal("monthly", urls[1].ChangeFreq);
        Assert.Equal("0.8", urls[1].Priority);
        Assert.Equal("weekly", urls[2].ChangeFreq);
        Assert.Equal("0.6", urls[2].Priority);
        Assert.Equal("weekly", urls[3].ChangeFreq);
        Assert.Equal("0.5", urls[3].Priority);
    }

    /// <summary>
    /// Every post, category and tag in the source data produces exactly one entry, so a crawler is
    /// not handed duplicates that waste its crawl budget.
    /// </summary>
    [Fact]
    public void SitemapEmitsOneEntryPerRow()
    {
        // Arrange
        ArrangePosts(
            new BlogPost { Slug = "first" },
            new BlogPost { Slug = "second" },
            new BlogPost { Slug = "third" });
        ArrangeCategories(new Category { Slug = "one" }, new Category { Slug = "two" });
        ArrangeTags(new BlogTag { Slug = "alpha" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal(7, urls.Count);
        Assert.Equal(urls.Select(url => url.Loc).Distinct().Count(), urls.Count);
    }

    // -------------------------------------------------------------------------------------------
    // Base URL
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The configured base URL is used for every <c>loc</c> and its trailing slash is trimmed once at
    /// construction, so no entry can come out with a doubled slash.
    /// </summary>
    [Fact]
    public void SitemapTrimsTheTrailingSlashFromTheConfiguredBaseUrl()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "trailing" });
        var service = CreateService("https://blog.example.com/");

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal("https://blog.example.com/", urls[0].Loc);
        Assert.Equal("https://blog.example.com/post/trailing", urls[1].Loc);
        Assert.DoesNotContain(urls, url => url.Loc.Contains("com//", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deployment that forgets to set <c>SiteSettings:BaseUrl</c> falls back to
    /// <c>https://localhost</c> rather than emitting relative or empty locations. The sitemap is then
    /// silently useless in production, which is exactly why the fallback is pinned here.
    /// </summary>
    [Fact]
    public void SitemapFallsBackToLocalhostWhenTheBaseUrlIsNotConfigured()
    {
        // Arrange
        var service = CreateService(baseUrl: null);

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal("https://localhost/", urls[0].Loc);
    }

    // -------------------------------------------------------------------------------------------
    // Inclusion rules
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The post section reads through the published-only repository member with the documented
    /// 10,000-row page, and never touches an unfiltered listing — a draft URL in the sitemap is an
    /// invitation for a crawler to fetch and surface embargoed content.
    /// </summary>
    [Fact]
    public void SitemapReadsPostsThroughThePublishedOnlyListing()
    {
        // Arrange
        var service = CreateService();

        // Act
        service.GenerateSitemap();

        // Assert
        postRepo.Received(1).GetPublishedPosts(10000, 0);
        postRepo.DidNotReceive().GetAll();
        postRepo.DidNotReceive().GetPagedData(Arg.Any<int>(), Arg.Any<int>());
    }

    /// <summary>
    /// No authenticated or newsletter route is ever emitted — the sitemap advertises only the site
    /// root and the three public content surfaces.
    /// </summary>
    [Fact]
    public void SitemapNeverAdvertisesAuthenticatedOrNewsletterRoutes()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "public-post" });
        ArrangeCategories(new Category { Slug = "public-category" });
        ArrangeTags(new BlogTag { Slug = "public-tag" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.All(urls, url => Assert.True(
            url.Loc == $"{BaseUrl}/"
            || url.Loc.StartsWith($"{BaseUrl}/post/", StringComparison.Ordinal)
            || url.Loc.StartsWith($"{BaseUrl}/category/", StringComparison.Ordinal)
            || url.Loc.StartsWith($"{BaseUrl}/tag/", StringComparison.Ordinal),
            $"Unexpected URL in sitemap: {url.Loc}"));
    }

    // -------------------------------------------------------------------------------------------
    // lastmod
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A post's <c>lastmod</c> is its publication date, which is the date a crawler uses to decide
    /// whether re-fetching is worthwhile.
    /// </summary>
    [Fact]
    public void PostLastModPrefersThePublicationDate()
    {
        // Arrange
        ArrangePosts(new BlogPost
        {
            Slug = "dated",
            CreatedOn = new DateTime(2026, 1, 2),
            PublishedOn = new DateTime(2026, 5, 6)
        });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal("2026-05-06", urls[1].LastMod);
    }

    /// <summary>
    /// A published post with no publication timestamp — a back-filled or migrated row — still gets a
    /// <c>lastmod</c>, falling back to its creation date rather than omitting the element.
    /// </summary>
    [Fact]
    public void PostLastModFallsBackToTheCreationDate()
    {
        // Arrange
        ArrangePosts(new BlogPost
        {
            Slug = "migrated",
            CreatedOn = new DateTime(2025, 11, 30),
            PublishedOn = null
        });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal("2025-11-30", urls[1].LastMod);
    }

    /// <summary>
    /// Category and tag pages carry no <c>lastmod</c>: their freshness is that of their newest post
    /// and the taxonomy row does not track it, and the protocol treats a missing element as "unknown"
    /// but a wrong date as truth.
    /// </summary>
    [Fact]
    public void CategoryAndTagEntriesOmitLastMod()
    {
        // Arrange
        ArrangeCategories(new Category { Slug = "dotnet" });
        ArrangeTags(new BlogTag { Slug = "blazor" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Null(urls[1].LastMod);
        Assert.Null(urls[2].LastMod);
    }

    /// <summary>
    /// Dates are written with the invariant Gregorian pattern, so a server whose current culture uses
    /// a non-Gregorian calendar cannot emit a <c>lastmod</c> no crawler can parse.
    /// </summary>
    [Fact]
    public void LastModIsGregorianWhateverTheServerCultureIs()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "cultured", PublishedOn = new DateTime(2026, 5, 6) });
        var service = CreateService();
        var original = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            // Act
            var urls = ParseUrls(service.GenerateSitemap());

            // Assert
            Assert.Equal("2026-05-06", urls[1].LastMod);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    // -------------------------------------------------------------------------------------------
    // Per-section failure handling
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A failing post read is logged and its section skipped; the root, categories and tags still
    /// ship, because a partial sitemap keeps a site indexable whereas a 500 teaches a crawler to back
    /// off.
    /// </summary>
    [Fact]
    public void SitemapSkipsThePostSectionWhenThePostReadFails()
    {
        // Arrange
        postRepo.GetPublishedPosts(Arg.Any<int>(), Arg.Any<int>())
            .Throws(new InvalidOperationException("post read exploded"));
        ArrangeCategories(new Category { Slug = "dotnet" });
        ArrangeTags(new BlogTag { Slug = "blazor" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal(
            new[] { $"{BaseUrl}/", $"{BaseUrl}/category/dotnet", $"{BaseUrl}/tag/blazor" },
            urls.Select(url => url.Loc));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error != null);
    }

    /// <summary>
    /// A failing category read is logged and its section skipped; posts and tags still ship.
    /// </summary>
    [Fact]
    public void SitemapSkipsTheCategorySectionWhenTheCategoryReadFails()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "still-here" });
        categoryRepo.GetAll().Throws(new InvalidOperationException("category read exploded"));
        ArrangeTags(new BlogTag { Slug = "blazor" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Equal(
            new[] { $"{BaseUrl}/", $"{BaseUrl}/post/still-here", $"{BaseUrl}/tag/blazor" },
            urls.Select(url => url.Loc));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// A failing tag read is logged and its section skipped; the document still closes cleanly, so
    /// the endpoint returns valid XML rather than a truncated body.
    /// </summary>
    [Fact]
    public void SitemapSkipsTheTagSectionWhenTheTagReadFails()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "still-here" });
        ArrangeCategories(new Category { Slug = "dotnet" });
        tagRepo.GetAll().Throws(new InvalidOperationException("tag read exploded"));
        var service = CreateService();

        // Act
        var xml = service.GenerateSitemap();

        // Assert
        var urls = ParseUrls(xml);
        Assert.Equal(
            new[] { $"{BaseUrl}/", $"{BaseUrl}/post/still-here", $"{BaseUrl}/category/dotnet" },
            urls.Select(url => url.Loc));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// When all three reads fail the endpoint still returns a well-formed, empty-but-for-the-root
    /// document, which a crawler treats as "nothing to add today" rather than as a broken site.
    /// </summary>
    [Fact]
    public void SitemapStillShipsWhenEverySectionFails()
    {
        // Arrange
        postRepo.GetPublishedPosts(Arg.Any<int>(), Arg.Any<int>()).Throws(new InvalidOperationException("posts"));
        categoryRepo.GetAll().Throws(new InvalidOperationException("categories"));
        tagRepo.GetAll().Throws(new InvalidOperationException("tags"));
        var service = CreateService();

        // Act
        var urls = ParseUrls(service.GenerateSitemap());

        // Assert
        Assert.Single(urls);
        Assert.Equal(3, logger.Entries.Count(entry => entry.Level == LogLevel.Error));
    }

    // -------------------------------------------------------------------------------------------
    // Async twin
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The async twin produces a byte-identical document to the synchronous one given the same data,
    /// which is what stops the two surfaces drifting while both exist.
    /// </summary>
    [Fact]
    public async Task AsyncSitemapIsIdenticalToTheSynchronousDocument()
    {
        // Arrange
        ArrangePosts(
            new BlogPost { Slug = "one", PublishedOn = new DateTime(2026, 2, 3) },
            new BlogPost { Slug = "two", CreatedOn = new DateTime(2026, 1, 1) });
        ArrangeCategories(new Category { Slug = "dotnet" }, new Category { Slug = "web" });
        ArrangeTags(new BlogTag { Slug = "blazor" });
        var service = CreateService();

        // Act
        var synchronous = service.GenerateSitemap();
        var asynchronous = await service.GenerateSitemapAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(synchronous, asynchronous);
    }

    /// <summary>
    /// The async twin routes all three reads through the repositories' async members with the
    /// caller's token flowed in, rather than blocking on the synchronous ones.
    /// </summary>
    [Fact]
    public async Task AsyncSitemapReadsThroughTheAsyncRepositoryMembersWithTheCallersToken()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        var service = CreateService();

        // Act
        await service.GenerateSitemapAsync(cancellation.Token);

        // Assert
        await postRepo.Received(1).GetPublishedPostsAsync(10000, 0, cancellation.Token);
        await categoryRepo.Received(1).GetAllAsync(cancellation.Token);
        await tagRepo.Received(1).GetAllAsync(cancellation.Token);
        postRepo.DidNotReceive().GetPublishedPosts(Arg.Any<int>(), Arg.Any<int>());
    }

    /// <summary>
    /// The async twin applies the same per-section guard: a failing read — a cancellation included —
    /// is logged and skipped while the rest of the document still ships.
    /// </summary>
    [Fact]
    public async Task AsyncSitemapSkipsASectionWhoseReadFails()
    {
        // Arrange
        postRepo.GetPublishedPostsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async post read exploded"));
        ArrangeCategories(new Category { Slug = "dotnet" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(await service.GenerateSitemapAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(new[] { $"{BaseUrl}/", $"{BaseUrl}/category/dotnet" }, urls.Select(url => url.Loc));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// A failing async category read is logged and skipped on its own; the post and tag sections
    /// still ship, so one broken taxonomy query cannot cost the whole sitemap.
    /// </summary>
    [Fact]
    public async Task AsyncSitemapSkipsTheCategorySectionWhenTheCategoryReadFails()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "still-here" });
        categoryRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async category read exploded"));
        ArrangeTags(new BlogTag { Slug = "blazor" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(await service.GenerateSitemapAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            new[] { $"{BaseUrl}/", $"{BaseUrl}/post/still-here", $"{BaseUrl}/tag/blazor" },
            urls.Select(url => url.Loc));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// A failing async tag read is logged and skipped, and the document still closes cleanly so the
    /// endpoint returns parsable XML rather than a truncated body.
    /// </summary>
    [Fact]
    public async Task AsyncSitemapSkipsTheTagSectionWhenTheTagReadFails()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "still-here" });
        ArrangeCategories(new Category { Slug = "dotnet" });
        tagRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async tag read exploded"));
        var service = CreateService();

        // Act
        var urls = ParseUrls(await service.GenerateSitemapAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            new[] { $"{BaseUrl}/", $"{BaseUrl}/post/still-here", $"{BaseUrl}/category/dotnet" },
            urls.Select(url => url.Loc));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The async twin emits the same priorities and change frequencies per section as the synchronous
    /// one, which is the part of the contract a crawler actually acts on.
    /// </summary>
    [Fact]
    public async Task AsyncSitemapEmitsTheSamePrioritiesPerSection()
    {
        // Arrange
        ArrangePosts(new BlogPost { Slug = "async-post", PublishedOn = new DateTime(2026, 7, 8) });
        ArrangeCategories(new Category { Slug = "dotnet" });
        ArrangeTags(new BlogTag { Slug = "blazor" });
        var service = CreateService();

        // Act
        var urls = ParseUrls(await service.GenerateSitemapAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("1.0", urls[0].Priority);
        Assert.Equal("0.8", urls[1].Priority);
        Assert.Equal("2026-07-08", urls[1].LastMod);
        Assert.Equal("0.6", urls[2].Priority);
        Assert.Equal("0.5", urls[3].Priority);
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds the service under test over the substituted repositories.
    /// </summary>
    /// <param name="baseUrl">Value for <c>SiteSettings:BaseUrl</c>; null leaves the key unset.</param>
    /// <returns>A configured <see cref="SitemapSvc"/>.</returns>
    private SitemapSvc CreateService(string? baseUrl = BaseUrl)
    {
        var config = new StubConfiguration(new Dictionary<string, string?> { ["SiteSettings:BaseUrl"] = baseUrl });
        return new SitemapSvc(postRepo, categoryRepo, tagRepo, config, logger);
    }

    /// <summary>
    /// Arranges both the synchronous and the async published-post reads with the same rows, so a test
    /// exercises whichever surface the service reaches for.
    /// </summary>
    /// <param name="posts">The published posts the repository should return.</param>
    private void ArrangePosts(params BlogPost[] posts)
    {
        postRepo.GetPublishedPosts(Arg.Any<int>(), Arg.Any<int>()).Returns(posts);
        postRepo.GetPublishedPostsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(posts.AsEnumerable());
    }

    /// <summary>
    /// Arranges both the synchronous and the async category reads with the same rows.
    /// </summary>
    /// <param name="categories">The categories the repository should return.</param>
    private void ArrangeCategories(params Category[] categories)
    {
        categoryRepo.GetAll().Returns(categories);
        categoryRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(categories.AsEnumerable());
    }

    /// <summary>
    /// Arranges both the synchronous and the async tag reads with the same rows.
    /// </summary>
    /// <param name="tags">The tags the repository should return.</param>
    private void ArrangeTags(params BlogTag[] tags)
    {
        tagRepo.GetAll().Returns(tags);
        tagRepo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(tags.AsEnumerable());
    }

    /// <summary>
    /// Parses a sitemap document into its entries, in document order.
    /// </summary>
    /// <param name="xml">The generated sitemap.</param>
    /// <returns>One record per <c>&lt;url&gt;</c> element.</returns>
    private static IReadOnlyList<SitemapEntry> ParseUrls(string xml)
    {
        var root = XDocument.Parse(xml).Root!;
        return root.Elements(SitemapNs + "url")
            .Select(url => new SitemapEntry(
                url.Element(SitemapNs + "loc")!.Value,
                url.Element(SitemapNs + "lastmod")?.Value,
                url.Element(SitemapNs + "changefreq")?.Value,
                url.Element(SitemapNs + "priority")?.Value))
            .ToList();
    }

    /// <summary>
    /// One parsed <c>&lt;url&gt;</c> element.
    /// </summary>
    /// <param name="Loc">Absolute location.</param>
    /// <param name="LastMod">Last-modified date, or null when the element was omitted.</param>
    /// <param name="ChangeFreq">Change-frequency hint.</param>
    /// <param name="Priority">Relative priority.</param>
    private sealed record SitemapEntry(string Loc, string? LastMod, string? ChangeFreq, string? Priority);
}
