using System.Globalization;
using System.Text;
using BlogModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Builds the <c>sitemap.xml</c> document served to search-engine crawlers.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Publishes the canonical list of crawlable URLs so search engines discover
/// posts, category pages and tag pages without following links from the home page. It is the one
/// place that decides <i>what is public</i> for indexing purposes, which makes its inclusion rules
/// a disclosure question, not just an SEO one.</para>
///
/// <para><b>What is included — and what is deliberately not:</b></para>
/// <list type="bullet">
///   <item><b>Included:</b> the site root; every <i>published</i> post at <c>/post/{slug}</c>;
///     every category at <c>/category/{slug}</c>; every tag at <c>/tag/{slug}</c>.</item>
///   <item><b>Excluded — drafts and scheduled posts.</b> The post list comes from
///     <c>IBlogPostRepo.GetPublishedPosts</c>, whose SQL filters on the published flag. A draft
///     therefore never appears. This is load bearing: a draft URL in the sitemap is an invitation
///     for a crawler to fetch, cache and surface unfinished or embargoed content.</item>
///   <item><b>Excluded — newsletter issues.</b> The newsletter archive is not walked at all, so an
///     unsent issue cannot leak through this surface. (Published issues are reachable through the
///     archive pages, which apply their own sent + public + slugged predicate; adding them here
///     would mean re-implementing that predicate in a second place.)</item>
///   <item><b>Excluded — every authenticated surface.</b> Admin, profile, moderation and account
///     routes are never emitted, because a crawler has no session and would only ever record a
///     redirect to the sign-in page.</item>
/// </list>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The host maps <c>GET /sitemap.xml</c> to <see cref="GenerateSitemap"/> and wraps it in
///     the fifteen-minute <c>Feed</c> output-cache policy, so this method runs at most once per
///     policy window however many crawlers ask.</item>
///   <item>The document is assembled in memory: root, then posts, then categories, then tags, each
///     section guarded by its own try/catch.</item>
///   <item>A section that throws is logged and <i>skipped</i>; the remaining sections still ship.
///     A partial sitemap keeps the site indexable, whereas a 500 teaches the crawler to back
///     off.</item>
/// </list>
///
/// <para><b>Error contract — deliberately not <c>Result</c>.</b> Everything else in the engine
/// returns <c>Result</c> for an expected failure. This class cannot: its caller is a minimal API
/// endpoint that must return a syntactically valid XML body or nothing useful at all. A total
/// failure therefore returns an <i>empty but well-formed</i> <c>urlset</c> after logging the
/// exception, which a crawler treats as "nothing to add today" rather than as a broken site.</para>
///
/// <para><b>Dependencies:</b> <see cref="IBlogPostRepo"/>, <see cref="ICategoryRepo"/> and
/// <see cref="IBlogTagRepo"/> for content, <c>IConfiguration</c> for
/// <c>SiteSettings:BaseUrl</c>, and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>. Requires no
/// authorization — the endpoint is anonymous by design, which is exactly why the inclusion rules
/// above must stay published-only. Set <c>SiteSettings:BaseUrl</c> per deployment: the fallback is
/// <c>https://localhost</c>, and a sitemap full of localhost URLs is silently useless in
/// production.</para>
/// </remarks>
public class SitemapSvc
{
    private readonly IBlogPostRepo postRepo;
    private readonly ICategoryRepo categoryRepo;
    private readonly IBlogTagRepo tagRepo;
    private readonly string baseUrl;
    private readonly ILogger<SitemapSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SitemapSvc"/> class.
    /// </summary>
    /// <remarks>
    /// The base URL is read once and its trailing slash trimmed, so every <c>&lt;loc&gt;</c> is a
    /// simple concatenation of base plus a leading-slash path.
    /// </remarks>
    /// <param name="postRepo">Post data access; supplies the published-only listing.</param>
    /// <param name="categoryRepo">Category data access.</param>
    /// <param name="tagRepo">Tag data access.</param>
    /// <param name="config">Application configuration, read for <c>SiteSettings:BaseUrl</c>.</param>
    /// <param name="logger">Logger for generation failures.</param>
    public SitemapSvc(
        IBlogPostRepo postRepo,
        ICategoryRepo categoryRepo,
        IBlogTagRepo tagRepo,
        IConfiguration config,
        ILogger<SitemapSvc> logger)
    {
        this.postRepo = postRepo;
        this.categoryRepo = categoryRepo;
        this.tagRepo = tagRepo;
        this.baseUrl = config["SiteSettings:BaseUrl"]?.TrimEnd('/') ?? "https://localhost";
        this.logger = logger;
    }

    /// <summary>
    /// Generates the complete sitemap document containing every public URL.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Emits the site root at priority 1.0 with a daily change
    /// frequency, published posts at 0.8/monthly, categories at 0.6/weekly and tags at 0.5/weekly.
    /// The priorities are relative hints within this site only — they carry no meaning across
    /// sites — and are ordered to match how often each surface actually changes.</para>
    /// <para><b>Flow:</b> XML prologue and <c>urlset</c> open → root URL → published posts →
    /// categories → tags → close.</para>
    /// <para><b>Side Effects:</b> Reads from three repositories; writes nothing. On an unexpected
    /// failure it logs the exception and substitutes an empty <c>urlset</c>, so the endpoint never
    /// returns malformed XML.</para>
    /// </remarks>
    /// <returns>
    /// A sitemap-protocol 0.9 document. Never null, and always well formed — an empty
    /// <c>urlset</c> when generation failed outright.
    /// </returns>
    public string GenerateSitemap()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

            // Home page - highest priority
            AddUrl(sb, "/", DateTime.UtcNow, "daily", "1.0");

            // Published posts - high priority
            AddPublishedPosts(sb);

            // Category pages - medium priority
            AddCategories(sb);

            // Tag pages - lower priority
            AddTags(sb);

            sb.AppendLine("</urlset>");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating sitemap");
            // Return minimal valid sitemap on error
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"></urlset>";
        }
    }

    /// <summary>
    /// Appends one entry per published post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reads through the repository's published-only listing, so a
    /// draft or a still-scheduled post is filtered out in SQL rather than here — there is no
    /// second, divergent definition of "public" in this file. <c>lastmod</c> prefers the publish
    /// date and falls back to the creation date, because a crawler uses it to decide whether a
    /// re-fetch is worthwhile.</para>
    /// <para><b>Flow:</b> read up to 10,000 published posts → append a URL per post.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="sb"/>. A repository failure is logged and
    /// swallowed so the rest of the sitemap still ships.</para>
    /// <para><b>Known limit:</b> the page size is capped at 10,000, which is also the practical
    /// ceiling on posts this sitemap can advertise. The protocol allows 50,000 URLs per file; a
    /// site that grows past 10,000 posts needs a paged sitemap index, not a larger number
    /// here.</para>
    /// </remarks>
    /// <param name="sb">The document being assembled.</param>
    private void AddPublishedPosts(StringBuilder sb)
    {
        try
        {
            var posts = postRepo.GetPublishedPosts(10000, 0);
            foreach (var post in posts)
            {
                var lastmod = post.PublishedOn ?? post.CreatedOn;
                AddUrl(sb, $"/post/{post.Slug}", lastmod, "monthly", "0.8");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding posts to sitemap");
        }
    }

    /// <summary>
    /// Appends one entry per category landing page.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every category is listed, including one that currently has no
    /// published posts — the page still renders and an empty category is not sensitive. No
    /// <c>lastmod</c> is emitted, because a category page's freshness is that of its newest post
    /// and the taxonomy row does not track it.</para>
    /// <para><b>Flow:</b> read all categories → append a URL per category.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="sb"/>; a failure is logged and skipped.</para>
    /// </remarks>
    /// <param name="sb">The document being assembled.</param>
    private void AddCategories(StringBuilder sb)
    {
        try
        {
            var categories = categoryRepo.GetAll();
            foreach (var category in categories)
            {
                AddUrl(sb, $"/category/{category.Slug}", null, "weekly", "0.6");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding categories to sitemap");
        }
    }

    /// <summary>
    /// Appends one entry per tag landing page.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Tags rank below categories (0.5 against 0.6) because a tag page
    /// is a narrower slice of the same content and competes with the category page for the same
    /// crawl budget.</para>
    /// <para><b>Flow:</b> read all tags → append a URL per tag.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="sb"/>; a failure is logged and skipped.</para>
    /// </remarks>
    /// <param name="sb">The document being assembled.</param>
    private void AddTags(StringBuilder sb)
    {
        try
        {
            var tags = tagRepo.GetAll();
            foreach (var tag in tags)
            {
                AddUrl(sb, $"/tag/{tag.Slug}", null, "weekly", "0.5");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding tags to sitemap");
        }
    }

    /// <summary>
    /// Appends one <c>&lt;url&gt;</c> element.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>lastmod</c> is emitted only when a date is known, since the
    /// protocol treats a missing element as "unknown" but treats a wrong date as truth. Dates are
    /// written as <c>yyyy-MM-dd</c>, the W3C date subset the protocol requires, using the invariant
    /// pattern so a server running under a non-Gregorian culture cannot emit an unparsable
    /// date.</para>
    /// <para><b>Flow:</b> open element → loc → optional lastmod → changefreq → priority → close.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="sb"/>.</para>
    /// <para><b>Caller contract:</b> slugs reach this method already URL-safe (lower-case,
    /// alphanumerics and hyphens, produced by <c>SlugGenerator</c>), so no XML escaping is applied
    /// here. Do not route arbitrary user text through <paramref name="path"/> without escaping it
    /// first — an unescaped <c>&amp;</c> would break the document for every crawler.</para>
    /// </remarks>
    /// <param name="sb">The document being assembled.</param>
    /// <param name="path">Site-relative path beginning with a slash.</param>
    /// <param name="lastmod">Last modification date, or null to omit the element.</param>
    /// <param name="changefreq">Sitemap change-frequency hint, for example <c>weekly</c>.</param>
    /// <param name="priority">Relative priority between <c>0.0</c> and <c>1.0</c>.</param>
    private void AddUrl(StringBuilder sb, string path, DateTime? lastmod, string changefreq, string priority)
    {
        sb.AppendLine("  <url>");
        sb.AppendLine($"    <loc>{baseUrl}{path}</loc>");
        if (lastmod.HasValue)
        {
            // Invariant culture on purpose: the sitemap protocol mandates the Gregorian W3C date
            // format, and an interpolated date would otherwise be rendered by the server's current
            // culture — a non-Gregorian calendar would emit a date no crawler can parse.
            var lastModified = lastmod.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            sb.AppendLine($"    <lastmod>{lastModified}</lastmod>");
        }
        sb.AppendLine($"    <changefreq>{changefreq}</changefreq>");
        sb.AppendLine($"    <priority>{priority}</priority>");
        sb.AppendLine("  </url>");
    }
}
