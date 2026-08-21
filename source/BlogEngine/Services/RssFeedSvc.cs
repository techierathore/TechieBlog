using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Builds the RSS 2.0 document served at <c>/feed.xml</c> (REQ-FN-037, BRD-63).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Publishes recent posts as a machine-readable feed so a reader application
/// can subscribe to the blog. It is the sibling of <see cref="SitemapSvc"/>: both are anonymous,
/// crawler-facing projections of the same content, and both therefore decide <i>what is public</i>.
/// That makes the inclusion rule below a disclosure question, not a formatting one.</para>
///
/// <para><b>What is included — and what is deliberately not:</b></para>
/// <list type="bullet">
///   <item><b>Included:</b> the most recent <see cref="MaxItems"/> <i>published</i> posts, newest
///     first, each with title, link, description, publication date and author.</item>
///   <item><b>Excluded — drafts, scheduled-but-unpublished and soft-deleted posts.</b> The item
///     list comes from <see cref="IBlogPostRepo.GetPublishedPostsAsync"/>, whose SQL filters on
///     <c>Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL)</c>. There is no second,
///     divergent definition of "public" in this file — no predicate is re-implemented here, so the
///     feed cannot drift away from the listing pages the way a hand-written filter would. This is
///     load bearing: a feed is pulled by aggregators that cache and re-publish, so an embargoed
///     post that leaks here is effectively unretractable.</item>
///   <item><b>Excluded — the post body.</b> Only <see cref="BlogPost.Abstract"/> (or a short plain
///     text excerpt when the abstract is blank) is emitted. Full-content syndication is a product
///     decision this requirement does not make, and shipping the rendered body would also mean
///     re-hosting every embedded HTML fragment in someone else's reader.</item>
/// </list>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The host maps <c>GET /feed.xml</c> (alias <c>GET /rss.xml</c>) to
///     <see cref="GenerateFeedAsync"/> and wraps it in the fifteen-minute <c>Feed</c> output-cache
///     policy, so this method runs at most once per policy window however many readers poll.</item>
///   <item>Channel metadata is read from the site settings aggregate, so the administrator's site
///     title and meta description reach the feed without a second configuration surface.</item>
///   <item>Items are projected from the published-post listing and the document is assembled with
///     <see cref="XDocument"/>.</item>
/// </list>
///
/// <para><b>Why <see cref="XDocument"/> and not a <c>StringBuilder</c>.</b> Post titles and
/// abstracts are author-supplied free text: an ampersand, an angle bracket or a stray control
/// character in a title would produce a document every reader rejects outright. The XML writer
/// escapes on every value it emits, so well-formedness is a property of the construction rather
/// than of remembering to call an escaping helper at each of a dozen concatenation sites. (This is
/// the one place it differs from <see cref="SitemapSvc"/>, which only ever emits slugs and dates.)
/// </para>
///
/// <para><b>Error contract — deliberately not <c>Result</c>.</b> Its caller is a minimal API
/// endpoint that must return a syntactically valid XML body or nothing useful at all. A total
/// failure therefore returns an <i>empty but well-formed</i> channel after logging the exception,
/// which a reader treats as "nothing new today" rather than as a broken feed that should be
/// dropped from the subscription list.</para>
///
/// <para><b>Dependencies:</b> <see cref="IBlogPostRepo"/> for content,
/// <see cref="ISiteSettingsService"/> for the channel title and description, <c>IConfiguration</c>
/// for <c>SiteSettings:BaseUrl</c>, and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>. Requires no authorization
/// — the endpoint is anonymous by design, which is exactly why the inclusion rule above must stay
/// published-only. Set <c>SiteSettings:BaseUrl</c> per deployment: the fallback is
/// <c>https://localhost</c>, and a feed full of localhost links is silently useless in
/// production.</para>
/// </remarks>
public class RssFeedSvc
{
    /// <summary>
    /// Media type the feed must be served with, including the charset the document declares.
    /// </summary>
    /// <remarks>
    /// Readers and browser auto-discovery both dispatch on this type; served as
    /// <c>text/html</c> or <c>application/xml</c> the same bytes are treated as a web page.
    /// </remarks>
    public const string ContentType = "application/rss+xml; charset=utf-8";

    /// <summary>
    /// Canonical site-relative path the feed is served from.
    /// </summary>
    /// <remarks>
    /// Shared by the host's endpoint mapping, the <c>&lt;atom:link rel="self"&gt;</c> element and
    /// the auto-discovery tag in the document head, so the advertised URL and the real one cannot
    /// drift apart.
    /// </remarks>
    public const string FeedPath = "/feed.xml";

    /// <summary>
    /// Number of posts carried in the feed.
    /// </summary>
    /// <remarks>
    /// Twenty is the conventional window: large enough that a reader polling daily never misses a
    /// post, small enough that the document stays a few kilobytes. A feed is a recent-items window,
    /// not an archive — that is what the sitemap and the listing pages are for.
    /// </remarks>
    public const int MaxItems = 20;

    private const string AtomNamespaceUri = "http://www.w3.org/2005/Atom";
    private const string DublinCoreNamespaceUri = "http://purl.org/dc/elements/1.1/";
    private const int DescriptionExcerptLength = 300;

    private readonly IBlogPostRepo postRepo;
    private readonly ISiteSettingsService siteSettingsService;
    private readonly string baseUrl;
    private readonly ILogger<RssFeedSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RssFeedSvc"/> class.
    /// </summary>
    /// <remarks>
    /// The base URL is read once and its trailing slash trimmed, so every link is a simple
    /// concatenation of base plus a leading-slash path — the same rule <see cref="SitemapSvc"/>
    /// uses, so the two documents cannot disagree about a post's address.
    /// </remarks>
    /// <param name="postRepo">Post data access; supplies the published-only listing.</param>
    /// <param name="siteSettingsService">Site settings, read for the channel title and description.</param>
    /// <param name="config">Application configuration, read for <c>SiteSettings:BaseUrl</c>.</param>
    /// <param name="logger">Logger for generation failures.</param>
    public RssFeedSvc(
        IBlogPostRepo postRepo,
        ISiteSettingsService siteSettingsService,
        IConfiguration config,
        ILogger<RssFeedSvc> logger)
    {
        this.postRepo = postRepo;
        this.siteSettingsService = siteSettingsService;
        this.baseUrl = config["SiteSettings:BaseUrl"]?.TrimEnd('/') ?? "https://localhost";
        this.logger = logger;
    }

    /// <summary>
    /// Generates the complete RSS 2.0 document for the most recent published posts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Emits one channel describing the site and up to
    /// <see cref="MaxItems"/> items, newest first. Every item carries the five fields the
    /// requirement names — title, link, description, publication date and author — plus a
    /// permalink <c>guid</c> so a reader can tell an edited post from a new one.</para>
    /// <para><b>Flow:</b> read settings → read published posts → build channel → append one item
    /// per post → serialise with an XML declaration.</para>
    /// <para><b>Side Effects:</b> Reads settings and one repository; writes nothing. On an
    /// unexpected failure it logs the exception and substitutes an empty channel, so the endpoint
    /// never returns malformed XML.</para>
    /// </remarks>
    /// <returns>
    /// An RSS 2.0 document. Never null, and always well formed — an empty channel when generation
    /// failed outright.
    /// </returns>
    public async Task<string> GenerateFeedAsync()
    {
        var channelTitle = "TechieBlog";
        var channelDescription = string.Empty;

        try
        {
            var settings = await siteSettingsService.GetSettingsAsync().ConfigureAwait(false);
            channelTitle = string.IsNullOrWhiteSpace(settings.SiteTitle) ? channelTitle : settings.SiteTitle;
            channelDescription = string.IsNullOrWhiteSpace(settings.MetaDescription)
                ? settings.SiteTagline ?? string.Empty
                : settings.MetaDescription;
        }
        catch (Exception ex)
        {
            // Settings are decoration here; the posts are the payload. A settings failure must not
            // cost a subscriber their feed, so the shipped defaults are used and the read is logged.
            logger.LogError(ex, "Error reading site settings for the RSS feed");
        }

        var posts = new List<BlogPost>();
        try
        {
            posts.AddRange(await postRepo.GetPublishedPostsAsync(MaxItems, 0).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading published posts for the RSS feed");
        }

        try
        {
            return BuildDocument(channelTitle, channelDescription, posts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating the RSS feed");
            return BuildDocument(channelTitle, channelDescription, new List<BlogPost>());
        }
    }

    /// <summary>
    /// Assembles the RSS document from channel metadata and a post list.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Declares the Atom and Dublin Core namespaces on the root, both
    /// of which RSS 2.0 readers understand: Atom supplies the self link that tells an aggregator
    /// where the feed lives, Dublin Core supplies the author name (see
    /// <see cref="BuildItem"/>).</para>
    /// <para><b>Flow:</b> root element → channel metadata → one item per post → serialise with an
    /// XML declaration.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="channelTitle">Site title shown as the feed name in a reader.</param>
    /// <param name="channelDescription">Site description shown under the feed name.</param>
    /// <param name="posts">Published posts, newest first.</param>
    /// <returns>The serialised RSS 2.0 document.</returns>
    private string BuildDocument(string channelTitle, string channelDescription, List<BlogPost> posts)
    {
        XNamespace atom = AtomNamespaceUri;
        XNamespace dublinCore = DublinCoreNamespaceUri;

        var channel = new XElement(
            "channel",
            new XElement("title", channelTitle),
            new XElement("link", baseUrl + "/"),
            new XElement("description", channelDescription),
            new XElement("language", "en"),
            new XElement("generator", "TechieBlog"),
            new XElement("lastBuildDate", FormatDate(DateTime.UtcNow)),
            new XElement(
                atom + "link",
                new XAttribute("href", baseUrl + FeedPath),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")));

        foreach (var post in posts)
        {
            channel.Add(BuildItem(post, dublinCore));
        }

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                "rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "atom", AtomNamespaceUri),
                new XAttribute(XNamespace.Xmlns + "dc", DublinCoreNamespaceUri),
                channel));

        return document.Declaration + Environment.NewLine + document.ToString();
    }

    /// <summary>
    /// Projects one published post onto an RSS <c>item</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The link and the <c>guid</c> are the same permalink, with
    /// <c>isPermaLink="true"</c>, so a reader that has seen the post recognises it after an edit
    /// instead of showing it twice. <c>pubDate</c> prefers the publication date and falls back to
    /// the creation date, matching <see cref="SitemapSvc"/>. REQ-FN-057 widened
    /// <c>BlogPostRepo.SelectPublishedSql</c> to select <c>PublishedOn</c>, so that preference now
    /// actually takes effect; before it did, the column arrived null on every row and the fallback
    /// fired always, dating the whole feed by when posts were drafted.</para>
    /// <para><b>Author is emitted as <c>dc:creator</c>, not <c>author</c>.</b> RSS 2.0 defines
    /// <c>&lt;author&gt;</c> as an <i>email address</i>. The only author identity this projection
    /// carries is a display name, and publishing staff email addresses on an anonymous feed would
    /// hand a harvester a spam list. <c>dc:creator</c> is the element every modern reader shows as
    /// the author for exactly this reason.</para>
    /// <para><b>Flow:</b> build permalink → title, link, guid, description, date → optional
    /// creator and category.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="post">The published post to project.</param>
    /// <param name="dublinCore">The Dublin Core namespace the creator element is written in.</param>
    /// <returns>The item element.</returns>
    private XElement BuildItem(BlogPost post, XNamespace dublinCore)
    {
        var permalink = $"{baseUrl}/post/{post.Slug}";
        var published = post.PublishedOn ?? post.CreatedOn;

        var item = new XElement(
            "item",
            new XElement("title", post.Title ?? string.Empty),
            new XElement("link", permalink),
            new XElement("guid", new XAttribute("isPermaLink", "true"), permalink),
            new XElement("description", BuildDescription(post)),
            new XElement("pubDate", FormatDate(published)));

        if (!string.IsNullOrWhiteSpace(post.BlogWriter))
        {
            item.Add(new XElement(dublinCore + "creator", post.BlogWriter.Trim()));
        }

        return item;
    }

    /// <summary>
    /// Produces the plain-text summary shown under an item's title in a reader.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Prefers the author-written abstract, which is the summary the
    /// listing pages already show. When it is blank an excerpt of the body is derived instead, with
    /// markup and markdown emphasis removed and whitespace collapsed, because a reader that shows
    /// raw markdown is worse than one that shows a slightly clipped sentence. The excerpt is capped
    /// at <see cref="DescriptionExcerptLength"/> characters and cut at a word boundary.</para>
    /// <para><b>Flow:</b> abstract → else strip the body → collapse whitespace → truncate.</para>
    /// <para><b>Side Effects:</b> None. No escaping is applied: the XML writer escapes every value
    /// it emits, and pre-escaping here would double-encode.</para>
    /// </remarks>
    /// <param name="post">The post being summarised.</param>
    /// <returns>A plain-text summary, possibly empty.</returns>
    private static string BuildDescription(BlogPost post)
    {
        if (!string.IsNullOrWhiteSpace(post.Abstract))
        {
            return post.Abstract.Trim();
        }

        if (string.IsNullOrWhiteSpace(post.PostContent))
        {
            return string.Empty;
        }

        var stripped = Regex.Replace(post.PostContent, "<[^>]+>", " ");
        stripped = Regex.Replace(stripped, @"[#*_`>\[\]\(\)!]", " ");
        stripped = Regex.Replace(stripped, @"\s+", " ").Trim();

        if (stripped.Length <= DescriptionExcerptLength)
        {
            return stripped;
        }

        var clipped = stripped.Substring(0, DescriptionExcerptLength);
        var lastSpace = clipped.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            clipped = clipped.Substring(0, lastSpace);
        }

        return clipped + "…";
    }

    /// <summary>
    /// Formats a timestamp as the RFC 1123 date RSS 2.0 requires.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Dates read out of PostgreSQL carry
    /// <see cref="DateTimeKind.Unspecified"/> and are stored in UTC, so the kind is stated
    /// explicitly before formatting — otherwise the round-trip pattern would stamp the server's
    /// local offset onto a UTC instant and every item would be dated wrongly by the offset. The
    /// invariant culture is mandatory: the day and month abbreviations in an RFC 1123 date are
    /// English by definition, and a server running under another culture would emit a date no
    /// reader can parse.</para>
    /// <para><b>Flow:</b> normalise the kind → format with the round-trip <c>R</c> pattern.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="value">The timestamp to format, interpreted as UTC.</param>
    /// <returns>An RFC 1123 date string, for example <c>Sat, 09 Aug 2026 10:15:00 GMT</c>.</returns>
    private static string FormatDate(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return utc.ToString("R", CultureInfo.InvariantCulture);
    }
}
