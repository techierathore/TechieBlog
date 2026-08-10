using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// Code-behind for Home.razor — the portfolio-style landing page (REQ-UI-049, BRD-30 revised).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Loads everything the landing page renders from the single
/// <c>IsSiteOwner</c> user: the hero fields, the headline statistics and the contact details,
/// plus the newest published posts for the latest-articles section.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Resolve the site owner; a missing owner short-circuits the page to an empty state.</item>
///   <item>Load that owner's statistics in display order.</item>
///   <item>Load the newest published posts for the article cards.</item>
///   <item>Load the featured post — the newest published article — for the lead block
///   (REQ-FN-020, BRD-31).</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IBlogUserRepo"/>, <see cref="IUserStatsRepo"/>,
/// <see cref="BlogEngine.Services.BlogSvc"/>.</para>
///
/// <para><b>Note:</b> the repository is injected directly for statistics because
/// <c>UserStatsSvc</c> is not registered in <c>BlogSvcInitializer</c>; the repository is.</para>
/// </remarks>
public partial class Home
{
    /// <summary>Number of article cards shown in the latest-articles grid.</summary>
    private const int LatestArticleCount = 3;

    /// <summary>
    /// Number of statistic tiles in the headline row. The mockup's stats band is a single
    /// four-across row; any further statistics the owner keeps stay on /resume.
    /// </summary>
    private const int HeadlineStatCount = 4;

    /// <summary>
    /// Characters of body text used when a featured post has no abstract of its own.
    /// </summary>
    private const int FeaturedExcerptLength = 220;

    private AppUser? siteOwner;
    private IReadOnlyList<UserStat> ownerStats = [];
    private IReadOnlyList<BlogPost> latestPosts = [];
    private BlogPost? featuredPost;
    private bool isLoading = true;

    /// <summary>
    /// Repository used to resolve the single site-owner user.
    /// </summary>
    [Inject]
    public IBlogUserRepo UserRepo { get; set; } = default!;

    /// <summary>
    /// Repository supplying the site owner's headline statistics.
    /// </summary>
    [Inject]
    public IUserStatsRepo UserStatsRepo { get; set; } = default!;

    /// <summary>
    /// Service supplying the recent published posts.
    /// </summary>
    [Inject]
    public BlogEngine.Services.BlogSvc BlogService { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadPageAsync();
    }

    /// <summary>
    /// Loads the site owner, that owner's statistics and the newest published posts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Each block fails independently — a statistics or post
    /// read that throws leaves its own section empty instead of blanking the landing page.</para>
    /// <para><b>Side Effects:</b> Populates the page's backing fields and re-renders.</para>
    /// </remarks>
    private async Task LoadPageAsync()
    {
        isLoading = true;

        siteOwner = LoadSiteOwner();
        ownerStats = siteOwner is null ? [] : LoadStats(siteOwner.UserId);
        latestPosts = LoadLatestPosts();
        featuredPost = LoadFeaturedPost();

        isLoading = false;
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Resolves the user flagged as the site owner.
    /// </summary>
    /// <returns>The site owner, or null when none is configured or the read fails.</returns>
    private AppUser? LoadSiteOwner()
    {
        try
        {
            return UserRepo.GetSiteOwner();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the owner's headline statistics in display order.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The identifier breaks ties so two statistics sharing a display
    /// order still render in a stable sequence; only the first <see cref="HeadlineStatCount"/>
    /// reach the landing page, keeping the stats band to the mockup's single row.</para>
    /// </remarks>
    /// <param name="userId">The site owner's identifier.</param>
    /// <returns>The statistics, or an empty list when none exist or the read fails.</returns>
    private IReadOnlyList<UserStat> LoadStats(long userId)
    {
        try
        {
            return UserStatsRepo.GetByUserId(userId)
                .OrderBy(stat => stat.DisplayOrder)
                .ThenBy(stat => stat.StatId)
                .Take(HeadlineStatCount)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Loads the newest published posts for the latest-articles grid.
    /// </summary>
    /// <returns>The posts newest first, or an empty list when none exist or the read fails.</returns>
    private IReadOnlyList<BlogPost> LoadLatestPosts()
    {
        try
        {
            return BlogService.GetPublishedPosts(LatestArticleCount, 0).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Loads the post that leads the landing page (REQ-FN-020, BRD-31).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "Featured" is a selection rule, not an editorial flag —
    /// <c>BlogSvc.GetFeaturedPost</c> returns the newest published, non-deleted post — so the block
    /// stays current with no one having to remember to move a marker. A site with nothing published
    /// yields <c>null</c> and the section is simply not rendered.</para>
    /// <para><b>Side Effects:</b> None beyond the read; a failure leaves the block absent rather
    /// than blanking the landing page.</para>
    /// </remarks>
    /// <returns>The newest published post, or null when there is none or the read fails.</returns>
    private BlogPost? LoadFeaturedPost()
    {
        try
        {
            return BlogService.GetFeaturedPost();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The blurb shown under the featured post's title.
    /// </summary>
    /// <remarks>
    /// Prefers the post's own abstract and falls back to a trimmed slice of the body with the
    /// commonest Markdown syntax characters removed, matching how the article cards build theirs.
    /// </remarks>
    private string FeaturedExcerpt
    {
        get
        {
            if (featuredPost is null)
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(featuredPost.Abstract))
                return featuredPost.Abstract;

            var text = (featuredPost.PostContent ?? string.Empty)
                .Replace("#", string.Empty)
                .Replace("*", string.Empty)
                .Replace("_", string.Empty)
                .Replace("`", string.Empty)
                .Trim();

            return text.Length <= FeaturedExcerptLength
                ? text
                : text[..FeaturedExcerptLength].TrimEnd() + "...";
        }
    }
}
