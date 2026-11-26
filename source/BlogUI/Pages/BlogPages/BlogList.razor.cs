using BlogModels;
using BlogModels.Interfaces;
using BlogUI.Components;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// Code-behind for BlogList.razor — the Blog page at <c>/blog</c> (REQ-UI-075, BRD-115).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lists every published post, newest first, a page at a time, using the
/// same tiles as the home page's Latest Articles section.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Read the page size from the "Posts per page" site setting.</item>
///   <item>Read the published-post count and work out the number of pages.</item>
///   <item>Read the requested page of published posts (newest first — the repository orders by
///   publication date, script 026) and each post's public rating figures.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="BlogEngine.Services.BlogSvc"/>,
/// <see cref="BlogEngine.Services.RatingSvc"/>, <see cref="ISiteSettingsService"/>,
/// <see cref="NavigationManager"/>.</para>
/// </remarks>
public partial class BlogList
{
    /// <summary>Characters of body text used when a post has no abstract of its own.</summary>
    private const int ExcerptLength = 150;

    private readonly Dictionary<long, int> ratingAverages = new();
    private readonly Dictionary<long, int> ratingCounts = new();

    private List<BlogBreadcrumbItem> breadcrumbItems = new();
    private List<BlogPost> posts = new();
    private bool isLoading = true;
    private int currentPage = 1;
    private int totalPages = 1;
    private int totalPosts;

    /// <summary>
    /// The 1-based page from the address (<c>/blog/{PageNumber}</c>); absent on <c>/blog</c>.
    /// </summary>
    [Parameter]
    public int? PageNumber { get; set; }

    /// <summary>
    /// Service supplying the published posts and their count.
    /// </summary>
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; } = default!;

    /// <summary>
    /// Service supplying each post's public average rating and count.
    /// </summary>
    [Inject]
    public BlogEngine.Services.RatingSvc RatingService { get; set; } = default!;

    /// <summary>
    /// Supplies the "Posts per page" setting.
    /// </summary>
    [Inject]
    public ISiteSettingsService SiteSettingsService { get; set; } = default!;

    /// <summary>
    /// Moves between pages by address, so every page can be linked.
    /// </summary>
    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    /// <summary>
    /// Loads the page named by the address whenever the address changes.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A page number below 1 is treated as page 1. A page past the last
    /// one shows an empty state with a link back to the newest posts rather than an error.</para>
    /// <para><b>Side Effects:</b> Reads from the content cache and the database.</para>
    /// </remarks>
    /// <returns>A task that completes when the page's posts have been read.</returns>
    protected override async Task OnParametersSetAsync()
    {
        currentPage = Math.Max(1, PageNumber ?? 1);
        isLoading = true;

        breadcrumbItems = new List<BlogBreadcrumbItem>
        {
            new BlogBreadcrumbItem { Label = "Home", Url = "/" },
            new BlogBreadcrumbItem { Label = "Blog", Url = "" }
        };

        var pageSize = Math.Max(1, (await SiteSettingsService.GetSettingsAsync()).PostsPerPage);
        totalPosts = await BlogService.GetPublishedPostCountAsync();
        totalPages = Math.Max(1, (int)Math.Ceiling(totalPosts / (double)pageSize));

        var offset = (currentPage - 1) * pageSize;
        posts = (await BlogService.GetPublishedPostsAsync(pageSize, offset)).ToList();
        await LoadRatingsAsync();

        isLoading = false;
    }

    /// <summary>
    /// Opens the chosen page by address.
    /// </summary>
    /// <param name="page">1-based page number.</param>
    private void HandlePageChanged(int page)
    {
        Navigation.NavigateTo(page <= 1 ? "/blog" : $"/blog/{page}");
    }

    /// <summary>
    /// Caches each visible post's public average and count so the render does no database work.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A rating failure leaves that tile without stars rather than
    /// breaking the page, exactly as on the home page.</para>
    /// <para><b>Side Effects:</b> Replaces the two rating dictionaries.</para>
    /// </remarks>
    /// <returns>A task that completes once every visible tile has its figures.</returns>
    private async Task LoadRatingsAsync()
    {
        ratingAverages.Clear();
        ratingCounts.Clear();

        foreach (var post in posts)
        {
            try
            {
                ratingAverages[post.PostID] = (int)Math.Round(await RatingService.GetAverageRatingAsync(post.PostID));
                ratingCounts[post.PostID] = await RatingService.GetRatingCountAsync(post.PostID);
            }
            catch
            {
                ratingAverages[post.PostID] = 0;
                ratingCounts[post.PostID] = 0;
            }
        }
    }

    /// <summary>
    /// Gets the rounded public average rating for a post, or zero when unrated.
    /// </summary>
    /// <param name="postId">Identifier of the post.</param>
    /// <returns>The rounded average.</returns>
    private int GetRating(long postId) => ratingAverages.TryGetValue(postId, out var value) ? value : 0;

    /// <summary>
    /// Gets the number of public ratings for a post, or zero when unrated.
    /// </summary>
    /// <param name="postId">Identifier of the post.</param>
    /// <returns>The rating count.</returns>
    private int GetRatingCount(long postId) => ratingCounts.TryGetValue(postId, out var value) ? value : 0;

    /// <summary>
    /// Gets the first tag as the tile's label, falling back to "General" — the home page's rule.
    /// </summary>
    /// <param name="tags">Comma-separated tag list from the post.</param>
    /// <returns>The label.</returns>
    private static string GetCategory(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return "General";

        var firstTag = tags.Split(',').FirstOrDefault()?.Trim();
        return string.IsNullOrEmpty(firstTag) ? "General" : firstTag;
    }

    /// <summary>
    /// Prefers the post's own abstract and falls back to a trimmed slice of the body.
    /// </summary>
    /// <param name="post">The post being rendered.</param>
    /// <returns>The excerpt.</returns>
    private static string GetExcerpt(BlogPost post)
    {
        if (!string.IsNullOrWhiteSpace(post.Abstract))
            return post.Abstract;

        var text = (post.PostContent ?? string.Empty)
            .Replace("#", string.Empty)
            .Replace("*", string.Empty)
            .Replace("_", string.Empty)
            .Replace("`", string.Empty)
            .Trim();

        return text.Length <= ExcerptLength ? text : text[..ExcerptLength].TrimEnd() + "...";
    }

    /// <summary>
    /// Gets the estimated reading time for a post body.
    /// </summary>
    /// <param name="content">The post body.</param>
    /// <returns>The reading time text.</returns>
    private static string GetReadingTime(string content) =>
        BlogEngine.Common.ReadingTimeCalculator.Calculate(content);
}
