using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using BlogUI.Components;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// State and behaviour for <c>NewsletterView.razor</c> — the public read view of one sent
/// newsletter issue at <c>/newsletter/{slug}</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-UI-054 (BRD-101): issue number, title, sent date,
/// rendered body, previous/next navigation by send order and a compact subscribe CTA.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The slug is resolved through <c>INewsletterService.GetPublishedBySlugAsync</c>, whose
///         predicate is sent + public + slugged. An unknown slug and an unsent draft are
///         therefore indistinguishable from the outside, and both end in
///         <see cref="NavigationManager.NotFound"/>.</item>
///   <item>Previous/next come from <c>GetNavigationAsync</c>, which compares send times, so the
///         first issue has no previous link and the latest has no next link.</item>
///   <item>The issue number is the issue's rank by send order, derived from the archive listing
///         because the schema does not store one.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="INewsletterService"/> (REQ-FN-050),
/// <see cref="MarkdownRenderer"/> for the body and <see cref="NavigationManager"/> for the
/// 404 response.</para>
///
/// <para><b>Usage:</b> Anonymous route; navigating between issues re-runs the load because the
/// route parameter changes (<see cref="OnParametersSetAsync"/>).</para>
/// </remarks>
public partial class NewsletterView : ComponentBase
{
    /// <summary>Issues read per archive page while resolving an issue number.</summary>
    private const int RankScanPageSize = 100;

    /// <summary>Upper bound on archive pages scanned, so a huge archive cannot stall the page.</summary>
    private const int MaxRankScanPages = 50;

    private List<BlogBreadcrumbItem> breadcrumbItems = new();
    private NewsletterNavigation navigation = new();
    private Newsletter? issue;
    private string renderedBody = string.Empty;
    private string loadedSlug = string.Empty;
    private int issueNumber;
    private int totalIssues;
    private bool isLoading = true;

    /// <summary>
    /// URL slug of the issue being read, assigned at send time.
    /// </summary>
    [Parameter]
    public string Slug { get; set; } = default!;

    /// <summary>
    /// Published-archive queries: slug resolution, listing and previous/next (REQ-FN-050).
    /// </summary>
    [Inject]
    public INewsletterService NewsletterService { get; set; } = default!;

    /// <summary>
    /// Renders the stored issue body to HTML, using the same pipeline as blog posts.
    /// </summary>
    [Inject]
    public MarkdownRenderer MarkdownRenderer { get; set; } = default!;

    /// <summary>
    /// Used to answer an unknown or unsent slug with a 404 rather than an empty page.
    /// </summary>
    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(loadedSlug, Slug, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        loadedSlug = Slug;
        await LoadIssueAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Resolves the slug and loads everything the page renders.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Resolution happens before anything else is read, so an
    /// unpublished issue costs exactly one query and never leaks a title, a date or a body.</para>
    /// <para><b>Flow:</b> resolve → 404 on failure → render body → load prev/next → derive the
    /// issue number → build the breadcrumb.</para>
    /// <para><b>Side Effects:</b> May terminate rendering with a not-found response.</para>
    /// </remarks>
    /// <returns>A task that completes when the issue is ready to render.</returns>
    private async Task LoadIssueAsync()
    {
        isLoading = true;
        try
        {
            var resolved = await NewsletterService.GetPublishedBySlugAsync(Slug).ConfigureAwait(true);
            if (resolved.IsFailure || resolved.Data == null)
            {
                issue = null;
                Navigation.NotFound();
                return;
            }

            issue = resolved.Data;
            renderedBody = MarkdownRenderer.ToHtml(issue.Content);
            navigation = await NewsletterService.GetNavigationAsync(issue.NewsletterId).ConfigureAwait(true);
            await ResolveIssueNumberAsync().ConfigureAwait(true);
            breadcrumbItems = BuildBreadcrumb();
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// Derives the issue's rank by send order from the public archive listing.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The schema stores no issue number, so it is defined as the
    /// position in send order with the oldest published issue being number 1. The archive is
    /// returned newest first together with a total, which makes the number
    /// <c>total - absoluteIndex</c>. The scan is bounded so a pathological archive cannot hang
    /// the request; beyond the bound the badge simply shows the total.</para>
    /// <para><b>Flow:</b> page through the archive → stop at the matching id → compute.</para>
    /// <para><b>Side Effects:</b> None beyond reads.</para>
    /// </remarks>
    /// <returns>A task that completes when the number has been resolved.</returns>
    private async Task ResolveIssueNumberAsync()
    {
        if (issue == null)
            return;

        for (var page = 1; page <= MaxRankScanPages; page++)
        {
            var archivePage = await NewsletterService
                .GetPublishedIssuesAsync(page, RankScanPageSize).ConfigureAwait(true);

            totalIssues = archivePage.TotalCount;
            if (archivePage.Items.Count == 0)
            {
                break;
            }

            for (var offset = 0; offset < archivePage.Items.Count; offset++)
            {
                if (archivePage.Items[offset].NewsletterId != issue.NewsletterId)
                {
                    continue;
                }

                issueNumber = archivePage.TotalCount - ((page - 1) * archivePage.PageSize) - offset;
                return;
            }

            if (page >= archivePage.TotalPages)
            {
                break;
            }
        }

        issueNumber = totalIssues;
    }

    /// <summary>
    /// Builds the Home / Newsletter / Issue trail.
    /// </summary>
    /// <returns>The breadcrumb items for this issue.</returns>
    private List<BlogBreadcrumbItem> BuildBreadcrumb()
    {
        return new List<BlogBreadcrumbItem>
        {
            new() { Label = "Home", Url = "/" },
            new() { Label = "Newsletter", Url = "/newsletters" },
            new() { Label = $"Issue #{issueNumber}" }
        };
    }

    /// <summary>
    /// Formats the send date for the issue header.
    /// </summary>
    /// <returns>A long human date, or an empty string when the issue has no send time.</returns>
    private string FormatSentOn()
    {
        return issue?.SentOn.HasValue == true ? issue.SentOn.Value.ToString("MMM d, yyyy") : string.Empty;
    }
}
