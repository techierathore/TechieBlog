using System.Text;
using BlogModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service for generating XML sitemaps for SEO.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Generates sitemap.xml content for search engine crawlers.</para>
/// <para><b>Dependencies:</b> IBlogPostRepo, ICategoryRepo, IBlogTagRepo for content data.</para>
/// </remarks>
public class SitemapSvc
{
    private readonly IBlogPostRepo _postRepo;
    private readonly ICategoryRepo _categoryRepo;
    private readonly IBlogTagRepo _tagRepo;
    private readonly string _baseUrl;
    private readonly ILogger<SitemapSvc> _logger;

    public SitemapSvc(
        IBlogPostRepo postRepo,
        ICategoryRepo categoryRepo,
        IBlogTagRepo tagRepo,
        IConfiguration config,
        ILogger<SitemapSvc> logger)
    {
        _postRepo = postRepo;
        _categoryRepo = categoryRepo;
        _tagRepo = tagRepo;
        _baseUrl = config["SiteSettings:BaseUrl"]?.TrimEnd('/') ?? "https://localhost";
        _logger = logger;
    }

    /// <summary>
    /// Generates a complete XML sitemap containing all public URLs.
    /// </summary>
    /// <returns>XML sitemap string following sitemap protocol specification.</returns>
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
            _logger.LogError(ex, "Error generating sitemap");
            // Return minimal valid sitemap on error
            return "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"></urlset>";
        }
    }

    private void AddPublishedPosts(StringBuilder sb)
    {
        try
        {
            var posts = _postRepo.GetPublishedPosts(10000, 0);
            foreach (var post in posts)
            {
                var lastmod = post.PublishedOn ?? post.CreatedOn;
                AddUrl(sb, $"/post/{post.Slug}", lastmod, "monthly", "0.8");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding posts to sitemap");
        }
    }

    private void AddCategories(StringBuilder sb)
    {
        try
        {
            var categories = _categoryRepo.GetAll();
            foreach (var category in categories)
            {
                AddUrl(sb, $"/category/{category.Slug}", null, "weekly", "0.6");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding categories to sitemap");
        }
    }

    private void AddTags(StringBuilder sb)
    {
        try
        {
            var tags = _tagRepo.GetAll();
            foreach (var tag in tags)
            {
                AddUrl(sb, $"/tag/{tag.Slug}", null, "weekly", "0.5");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tags to sitemap");
        }
    }

    private void AddUrl(StringBuilder sb, string path, DateTime? lastmod, string changefreq, string priority)
    {
        sb.AppendLine("  <url>");
        sb.AppendLine($"    <loc>{_baseUrl}{path}</loc>");
        if (lastmod.HasValue)
        {
            sb.AppendLine($"    <lastmod>{lastmod.Value:yyyy-MM-dd}</lastmod>");
        }
        sb.AppendLine($"    <changefreq>{changefreq}</changefreq>");
        sb.AppendLine($"    <priority>{priority}</priority>");
        sb.AppendLine("  </url>");
    }
}
