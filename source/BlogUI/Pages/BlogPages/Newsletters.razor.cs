using BlogModels;
using BlogModels.Interfaces;
using BlogUI.Components;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// State and behaviour for <c>Newsletters.razor</c> — the public newsletter archive at
/// <c>/newsletters</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-UI-053 (BRD-100): a prominent subscribe card plus the
/// list of past issues, newest send first, with paging.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="OnInitializedAsync"/> reads the confirmed-subscriber count and the first
///         archive page.</item>
///   <item>Every listing read goes through <c>INewsletterService.GetPublishedIssuesAsync</c>,
///         which filters on sent + public + slugged — a draft or unsent issue can never appear
///         here.</item>
///   <item>Paging re-reads the requested page rather than slicing a cached list, so a newly sent
///         issue shows up on the next page change.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="INewsletterService"/> for the archive queries
/// (REQ-FN-050) and <see cref="ISubscriberRepo"/> for the subscriber count shown as social
/// proof.</para>
///
/// <para><b>Usage:</b> Anonymous route; the public shell exposes no login (BRD-93).</para>
/// </remarks>
public partial class Newsletters : ComponentBase
{
    /// <summary>Issues per archive page — matches the six-card grid in mockup 42.</summary>
    private const int PageSize = 6;

    /// <summary>Characters of body text used when an issue carries no summary.</summary>
    private const int ExcerptLength = 200;

    private readonly List<BlogBreadcrumbItem> breadcrumbItems = new()
    {
        new BlogBreadcrumbItem { Label = "Home", Url = "/" },
        new BlogBreadcrumbItem { Label = "Newsletter" }
    };

    private List<Newsletter> issues = new();
    private Dictionary<long, int> issueNumbers = new();
    private int confirmedSubscriberCount;
    private int currentPage = 1;
    private int totalPages;
    private bool isLoading = true;

    /// <summary>
    /// Published-archive queries: list, count and slug resolution (REQ-FN-050).
    /// </summary>
    [Inject]
    public INewsletterService NewsletterService { get; set; } = default!;

    /// <summary>
    /// Subscriber store, read only for the confirmed-subscriber count.
    /// </summary>
    [Inject]
    public ISubscriberRepo SubscriberRepo { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadSubscriberCountAsync().ConfigureAwait(true);
        await LoadPageAsync(1).ConfigureAwait(true);
    }

    /// <summary>
    /// Reads one page of published issues and derives its issue numbers.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Issue numbers are not stored — they are the issue's rank by
    /// send order. Because the service returns the archive newest first together with the total
    /// count, the number of the item at absolute position <c>i</c> is <c>total - i</c>, which
    /// stays stable across pages without a second query.</para>
    /// <para><b>Flow:</b> set the loading flag → read the page → map numbers → clear the flag.</para>
    /// <para><b>Side Effects:</b> None beyond reads.</para>
    /// </remarks>
    /// <param name="pageNumber">One-based page to display.</param>
    /// <returns>A task that completes when the page has been loaded.</returns>
    private async Task LoadPageAsync(int pageNumber)
    {
        isLoading = true;
        try
        {
            var archivePage = await NewsletterService
                .GetPublishedIssuesAsync(pageNumber, PageSize).ConfigureAwait(true);

            issues = archivePage.Items.ToList();
            currentPage = archivePage.PageNumber;
            totalPages = archivePage.TotalPages;
            issueNumbers = BuildIssueNumbers(archivePage);
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// Reads the confirmed-subscriber count, tolerating a data-access failure.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The count is decoration; failing to read it must never stop
    /// the archive — or the subscribe form — from rendering.</para>
    /// <para><b>Flow:</b> count active subscribers → fall back to zero, which hides the line.</para>
    /// <para><b>Side Effects:</b> None beyond a read.</para>
    /// </remarks>
    /// <returns>A task that completes when the count has been read.</returns>
    private async Task LoadSubscriberCountAsync()
    {
        try
        {
            var activeSubscribers = await SubscriberRepo.GetActiveSubscribersAsync().ConfigureAwait(true);
            confirmedSubscriberCount = activeSubscribers.Count();
        }
        catch (Exception)
        {
            confirmedSubscriberCount = 0;
        }
    }

    /// <summary>
    /// Handles a page change from the pagination control.
    /// </summary>
    /// <param name="pageNumber">The page the visitor asked for.</param>
    /// <returns>A task that completes when the new page has been loaded.</returns>
    private async Task GoToPageAsync(int pageNumber)
    {
        await LoadPageAsync(pageNumber).ConfigureAwait(true);
    }

    /// <summary>
    /// Maps each issue on the page to its archive issue number.
    /// </summary>
    /// <param name="archivePage">The page returned by the service.</param>
    /// <returns>Issue id to issue number.</returns>
    private static Dictionary<long, int> BuildIssueNumbers(NewsletterArchivePage archivePage)
    {
        var numbers = new Dictionary<long, int>();
        var firstIndex = (archivePage.PageNumber - 1) * archivePage.PageSize;

        for (var offset = 0; offset < archivePage.Items.Count; offset++)
        {
            numbers[archivePage.Items[offset].NewsletterId] = archivePage.TotalCount - firstIndex - offset;
        }

        return numbers;
    }

    /// <summary>
    /// Gets the display issue number for one issue.
    /// </summary>
    /// <param name="issue">The issue being rendered.</param>
    /// <returns>Its rank by send order, oldest issue being 1.</returns>
    private int GetIssueNumber(Newsletter issue)
    {
        return issueNumbers.TryGetValue(issue.NewsletterId, out var number) ? number : 0;
    }

    /// <summary>
    /// Formats the send date for the issue card.
    /// </summary>
    /// <param name="issue">The issue being rendered.</param>
    /// <returns>A short human date, or an empty string when the issue somehow has no send time.</returns>
    private static string FormatSentOn(Newsletter issue)
    {
        return issue.SentOn.HasValue ? issue.SentOn.Value.ToString("MMM d, yyyy") : string.Empty;
    }

    /// <summary>
    /// Produces the teaser shown under an issue title.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The composer's summary wins when present; otherwise the body
    /// is trimmed. Any HTML the body carries is stripped so the card cannot inherit stray markup
    /// or break the layout.</para>
    /// <para><b>Flow:</b> prefer summary → strip tags → collapse whitespace → truncate on a word.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="issue">The issue being rendered.</param>
    /// <returns>Plain-text excerpt.</returns>
    private static string GetExcerpt(Newsletter issue)
    {
        if (!string.IsNullOrWhiteSpace(issue.Summary))
        {
            return issue.Summary;
        }

        var plain = System.Text.RegularExpressions.Regex.Replace(issue.Content ?? string.Empty, "<[^>]+>", " ");
        plain = System.Text.RegularExpressions.Regex.Replace(plain, @"\s+", " ").Trim();

        if (plain.Length <= ExcerptLength)
        {
            return plain;
        }

        var cut = plain.LastIndexOf(' ', ExcerptLength);
        return string.Concat(plain[..(cut > 0 ? cut : ExcerptLength)], "…");
    }
}
