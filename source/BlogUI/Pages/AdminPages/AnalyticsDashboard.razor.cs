using BlogModels;
using BlogModels.Interfaces;
using BlogUI.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using TrBlazeUI.Components.Toast;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// State and behaviour for the admin analytics dashboard.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-UI-044 (BRD-60, BRD-61) — headline stat tiles, a daily views
/// trend, the popular-post ranking and the share of readership per category, all driven by one
/// date range so the whole page always describes the same period.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="OnInitializedAsync"/> seeds a 30-day range and loads every panel.</item>
///   <item><see cref="ApplyRangeAsync"/> parses and validates the two date inputs; an inverted or
///         unparseable range is refused inline rather than silently corrected.</item>
///   <item><see cref="LoadAsync"/> issues the four range-scoped analytics queries and rebuilds the
///         stat tiles, so a range change moves every panel at once.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IAnalyticsService"/> only — the page owns no data access.</para>
///
/// <para><b>Usage:</b> Routed at <c>/admin/analytics</c> behind the <c>EditorOrAbove</c> policy and
/// rendered inside <c>AdminLayout</c>. A query failure degrades to empty panels rather than an
/// error screen, matching the analytics service's own read-never-fails contract.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> This page is where the cancellation token added to
/// the analytics stack earns its keep. Each load issues four range-scoped aggregate queries; an admin
/// clicking through the presets can start several loads before the first finishes, and without a token
/// every superseded set ran to completion against the database with nobody left to read it. Each load
/// now owns a <see cref="CancellationTokenSource"/> that the next load cancels, and the component
/// cancels the outstanding one when the circuit disposes. A superseded load also returns before
/// assigning, so a slow earlier range can never overwrite the panels of a later one.</para>
/// </remarks>
public partial class AnalyticsDashboard : ComponentBase, IDisposable
{
    /// <summary>
    /// Days covered by the default range when the page first opens.
    /// </summary>
    private const int DefaultRangeDays = 30;

    /// <summary>
    /// Maximum posts listed in the popular-post ranking.
    /// </summary>
    private const int PopularPostLimit = 10;

    /// <summary>
    /// Maximum categories listed in the engagement panel.
    /// </summary>
    private const int CategoryLimit = 8;

    /// <summary>
    /// The most referring sites the card lists (REQ-UI-071).
    /// </summary>
    private const int ReferrerLimit = 10;

    /// <summary>
    /// The clipboard module, served by the BlogUI RCL to both heads (REQ-UI-070).
    /// </summary>
    private const string ClipboardModulePath = "/_content/BlogUI/js/clipboard.js";

    /// <summary>
    /// Date format the HTML date input exchanges with the page.
    /// </summary>
    private const string DateInputFormat = "yyyy-MM-dd";

    /// <summary>
    /// Analytics read service supplying every figure on the page.
    /// </summary>
    [Inject]
    public IAnalyticsService AnalyticsService { get; set; } = default!;

    /// <summary>
    /// JavaScript interop, used only to reach the clipboard module (REQ-UI-070).
    /// </summary>
    [Inject]
    public IJSRuntime JsRuntime { get; set; } = default!;

    /// <summary>
    /// Toast notifications for the "Copy numbers" outcome.
    /// </summary>
    [Inject]
    public ToastService ToastService { get; set; } = default!;

    /// <summary>
    /// Views per referring site in the applied range, busiest first (REQ-UI-071).
    /// </summary>
    public IReadOnlyList<ReferrerEngagement> Referrers { get; private set; } = new List<ReferrerEngagement>();

    /// <summary>
    /// Views recorded today (UTC) — a fixed window, independent of the range (REQ-UI-070).
    /// </summary>
    public int ViewsToday { get; private set; }

    /// <summary>
    /// Views in the last 7 days, today included (REQ-UI-070).
    /// </summary>
    public int ViewsLast7Days { get; private set; }

    /// <summary>
    /// Views in the last 30 days, today included (REQ-UI-070).
    /// </summary>
    public int ViewsLast30Days { get; private set; }

    /// <summary>
    /// Whether the ratings dialog is open (REQ-UI-067).
    /// </summary>
    public bool RatingsOpen { get; set; }

    /// <summary>
    /// The post the ratings dialog shows.
    /// </summary>
    public long RatingsPostId { get; private set; }

    /// <summary>
    /// The title of the post the ratings dialog shows.
    /// </summary>
    public string RatingsPostTitle { get; private set; } = string.Empty;

    /// <summary>
    /// True while a copy is in progress.
    /// </summary>
    public bool IsCopying { get; private set; }

    /// <summary>
    /// The numbers as text, shown only when the clipboard refused them.
    /// </summary>
    public string CopyFallbackText { get; private set; } = string.Empty;

    /// <summary>
    /// Start of the applied range, as typed into the "From" date input.
    /// </summary>
    public string FromDateText { get; set; } = string.Empty;

    /// <summary>
    /// End of the applied range, as typed into the "To" date input.
    /// </summary>
    public string ToDateText { get; set; } = string.Empty;

    /// <summary>
    /// Validation message shown when the typed range cannot be applied.
    /// </summary>
    public string RangeError { get; private set; } = string.Empty;

    /// <summary>
    /// True while the panels are being reloaded.
    /// </summary>
    public bool IsLoading { get; private set; } = true;

    /// <summary>
    /// One point per day in the applied range, quiet days included as zeroes.
    /// </summary>
    public IReadOnlyList<ViewTrendPoint> TrendPoints { get; private set; } = new List<ViewTrendPoint>();

    /// <summary>
    /// Posts ranked by views inside the applied range.
    /// </summary>
    public IReadOnlyList<PopularPost> PopularPosts { get; private set; } = new List<PopularPost>();

    /// <summary>
    /// Categories ranked by views inside the applied range.
    /// </summary>
    public IReadOnlyList<CategoryEngagement> CategoryEngagements { get; private set; } = new List<CategoryEngagement>();

    /// <summary>
    /// Headline figures for the applied range.
    /// </summary>
    public AnalyticsSummary Summary { get; private set; } = new AnalyticsSummary();

    /// <summary>
    /// The stat tiles rendered above the trend, rebuilt on every load.
    /// </summary>
    public IReadOnlyList<StatTile> StatTiles { get; private set; } = new List<StatTile>();

    /// <summary>
    /// Human-readable description of the applied range, reused as every panel's subtitle.
    /// </summary>
    public string RangeCaption { get; private set; } = string.Empty;

    /// <summary>
    /// Total views plotted in the trend panel.
    /// </summary>
    public int TrendTotalViews => TrendPoints.Sum(point => point.TotalViews);

    /// <summary>
    /// True when at least one view was recorded in the applied range.
    /// </summary>
    public bool HasTrafficInRange => TrendTotalViews > 0;

    /// <summary>
    /// Label of the busiest day in the applied range.
    /// </summary>
    public string BusiestDayLabel => BusiestDay?.Label ?? "n/a";

    /// <summary>
    /// Views recorded on the busiest day in the applied range.
    /// </summary>
    public int BusiestDayViews => BusiestDay?.TotalViews ?? 0;

    /// <summary>
    /// Start of the applied range, held separately from the input text so a rejected edit cannot
    /// desynchronise the panels from their caption.
    /// </summary>
    private DateTime appliedFrom;

    /// <summary>
    /// End of the applied range, inclusive of the whole day.
    /// </summary>
    private DateTime appliedTo;

    /// <summary>
    /// Cancels the in-flight load when a newer one starts or the component is disposed.
    /// </summary>
    private CancellationTokenSource? loadCancellation;

    /// <summary>
    /// The busiest day in the applied range, or null when the range is quiet.
    /// </summary>
    private ViewTrendPoint? BusiestDay =>
        TrendPoints.Count == 0 ? null : TrendPoints.OrderByDescending(point => point.TotalViews).First();

    /// <summary>
    /// One headline figure rendered as a card above the trend.
    /// </summary>
    /// <param name="Title">Tile heading.</param>
    /// <param name="Value">Formatted figure.</param>
    /// <param name="Caption">Supporting line beneath the figure.</param>
    /// <param name="Icon">Lucide icon name.</param>
    /// <param name="TestId">Stable <c>data-testid</c> on the value element.</param>
    public sealed record StatTile(string Title, string Value, string Caption, string Icon, string TestId);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        appliedTo = DateTime.UtcNow.Date;
        appliedFrom = appliedTo.AddDays(-(DefaultRangeDays - 1));
        SyncInputsFromAppliedRange();
        await LoadAsync();

        // The fixed windows are secondary to the range panels, so they load after them and never
        // delay the first paint of the page's main figures.
        await LoadFixedWindowsAsync();
    }

    /// <summary>
    /// Opens the ratings dialog for one post (REQ-UI-067).
    /// </summary>
    /// <param name="postId">The post.</param>
    /// <param name="title">The post's title.</param>
    public void OpenRatings(long postId, string title)
    {
        RatingsPostId = postId;
        RatingsPostTitle = title ?? string.Empty;
        RatingsOpen = true;
    }

    /// <summary>
    /// The confirmed average with the confirmed count, for a Rating cell (REQ-UI-067).
    /// </summary>
    /// <param name="averageRating">Confirmed average; 0 when unrated.</param>
    /// <param name="ratingCount">Confirmed count.</param>
    /// <returns>For example <c>4.5 ★ (3)</c>, or <c>No ratings</c>.</returns>
    public static string FormatRatingWithCount(double averageRating, int ratingCount) =>
        ratingCount <= 0 || averageRating <= 0
            ? "No ratings"
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{averageRating:0.0} ★ ({ratingCount:N0})");

    /// <summary>
    /// A referring site's bar length relative to the busiest site (REQ-UI-071).
    /// </summary>
    /// <param name="referrer">The site.</param>
    /// <returns>0–100.</returns>
    public double ReferrerSharePercent(ReferrerEngagement referrer)
    {
        if (referrer == null || Referrers.Count == 0)
            return 0;

        var leader = Referrers.Max(entry => entry.TotalViews);
        return leader <= 0 ? 0 : Math.Round(referrer.TotalViews * 100d / leader, 1);
    }

    /// <summary>
    /// Puts the page's numbers on the clipboard as plain text — REQ-UI-070 (BRD-110).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> the text is built from exactly what the page shows (the fixed
    /// windows, the popular posts and the referring sites of the applied range), so the clipboard and
    /// the screen cannot disagree. A refused clipboard shows the text for copying by hand.</para>
    /// <para><b>Flow:</b> format → clipboard module → toast, or the fallback box.</para>
    /// <para><b>Side Effects:</b> writes the clipboard; shows a toast.</para>
    /// </remarks>
    /// <returns>A task that completes when the copy has been attempted.</returns>
    public async Task CopyNumbersAsync()
    {
        IsCopying = true;
        var text = AnalyticsNumbersFormatter.Format(new AnalyticsNumbers(
            DateTime.UtcNow, RangeCaption, ViewsToday, ViewsLast7Days, ViewsLast30Days, PopularPosts, Referrers));

        bool isCopied;
        try
        {
            await using var module = await JsRuntime.InvokeAsync<IJSObjectReference>("import", ClipboardModulePath);
            isCopied = await module.InvokeAsync<bool>("writeText", text);
        }
        catch (JSDisconnectedException)
        {
            return;
        }
        catch (JSException)
        {
            isCopied = false;
        }

        IsCopying = false;
        CopyFallbackText = isCopied ? string.Empty : text;
        if (isCopied)
            ToastService.Success("The numbers are on the clipboard as plain text.", "Numbers copied");
        else
            ToastService.Warning("The clipboard refused the text. It is shown on the page to copy by hand.", "Not copied");
    }

    /// <summary>
    /// Applies the range currently typed into the two date inputs.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A range that cannot be parsed, or that ends before it starts, is
    /// refused with an inline message; silently swapping the dates would hide a typing mistake and
    /// leave the admin reading a period they did not ask for.</para>
    /// <para><b>Flow:</b> parse both inputs → validate order → store the range → reload.</para>
    /// <para><b>Side Effects:</b> Re-queries every panel.</para>
    /// </remarks>
    /// <returns>A task that completes when the panels have reloaded.</returns>
    public async Task ApplyRangeAsync()
    {
        if (!DateTime.TryParse(FromDateText, out var parsedFrom) ||
            !DateTime.TryParse(ToDateText, out var parsedTo))
        {
            RangeError = "Enter a start and an end date.";
            return;
        }

        if (parsedFrom.Date > parsedTo.Date)
        {
            RangeError = "The start date must fall on or before the end date.";
            return;
        }

        RangeError = string.Empty;
        appliedFrom = parsedFrom.Date;
        appliedTo = parsedTo.Date;
        await LoadAsync();
    }

    /// <summary>
    /// Applies a rolling range that ends today.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The presets are the ranges an admin actually asks for; they also
    /// give the page a one-click way back to a valid range after a rejected edit.</para>
    /// <para><b>Flow:</b> compute the range → mirror it into the inputs → reload.</para>
    /// <para><b>Side Effects:</b> Re-queries every panel.</para>
    /// </remarks>
    /// <param name="days">Length of the rolling window in days, including today.</param>
    /// <returns>A task that completes when the panels have reloaded.</returns>
    public async Task ApplyPresetAsync(int days)
    {
        RangeError = string.Empty;
        appliedTo = DateTime.UtcNow.Date;
        appliedFrom = appliedTo.AddDays(-(Math.Max(days, 1) - 1));
        SyncInputsFromAppliedRange();
        await LoadAsync();
    }

    /// <summary>
    /// Expresses one category's readership as a percentage of the busiest category.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Bars are scaled against the leader rather than against the total,
    /// so the top category always fills its track and smaller ones stay legible.</para>
    /// <para><b>Flow:</b> read the leader → guard a zero leader → scale.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="category">The category being drawn.</param>
    /// <returns>A value between 0 and 100.</returns>
    public double CategorySharePercent(CategoryEngagement category)
    {
        if (category == null || CategoryEngagements.Count == 0)
        {
            return 0;
        }

        var leader = CategoryEngagements.Max(entry => entry.TotalViews);
        return leader <= 0 ? 0 : Math.Round(category.TotalViews * 100d / leader, 1);
    }

    /// <summary>
    /// Formats a mean rating for display, showing a dash when nothing has been rated.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A zero average means "unrated", not "rated zero"; printing
    /// "0.0 ★" would misreport it.</para>
    /// <para><b>Flow:</b> guard zero → format to one decimal with a star.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="averageRating">The mean rating.</param>
    /// <returns>The formatted rating, or an em dash when unrated.</returns>
    public static string FormatRating(double averageRating) =>
        averageRating <= 0 ? "—" : $"{averageRating:0.0} ★";

    /// <summary>
    /// Reloads every panel from the applied range.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> All four queries take the same range, which is what makes "the
    /// date range filters every panel" true rather than merely intended.</para>
    /// <para><b>Flow:</b> raise the loading flag → query summary, trend, ranking and categories →
    /// rebuild the tiles and caption → lower the flag.</para>
    /// <para><b>Side Effects:</b> Four read-only queries; re-renders the page twice.</para>
    /// </remarks>
    /// <returns>A task that completes when every panel holds data for the applied range.</returns>
    private async Task LoadAsync()
    {
        var cancellationToken = StartLoad();

        IsLoading = true;
        StateHasChanged();

        IReadOnlyList<PopularPost> popularPosts;
        IReadOnlyList<CategoryEngagement> categoryEngagements;
        IReadOnlyList<ViewTrendPoint> trendPoints;
        IReadOnlyList<ReferrerEngagement> referrers;
        AnalyticsSummary summary;

        try
        {
            // Five independent reads, each on its own connection, so they run side by side rather
            // than one after another (REQ-UI-071 added the fifth).
            var summaryTask = AnalyticsService.GetSummaryAsync(appliedFrom, appliedTo, cancellationToken);
            var trendTask = AnalyticsService.GetViewTrendAsync(appliedFrom, appliedTo, cancellationToken);
            var popularTask = AnalyticsService
                .GetPopularPostsInRangeAsync(appliedFrom, appliedTo, PopularPostLimit, cancellationToken);
            var categoryTask = AnalyticsService
                .GetCategoryEngagementAsync(appliedFrom, appliedTo, CategoryLimit, cancellationToken);
            var referrerTask = AnalyticsService
                .GetReferrersAsync(appliedFrom, appliedTo, ReferrerLimit, cancellationToken);
            await Task.WhenAll(summaryTask, trendTask, popularTask, categoryTask, referrerTask);

            summary = await summaryTask;
            trendPoints = await trendTask;
            popularPosts = await popularTask;
            categoryEngagements = await categoryTask;
            referrers = await referrerTask;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        Summary = summary;
        TrendPoints = trendPoints;
        PopularPosts = popularPosts;
        CategoryEngagements = categoryEngagements;
        Referrers = referrers;

        RangeCaption = BuildRangeCaption();
        StatTiles = BuildStatTiles();
        IsLoading = false;
    }

    /// <summary>
    /// Loads the three fixed windows — today, last 7 days, last 30 days (UTC) — once per visit.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> REQ-UI-070. These do not follow the date range: "views today"
    /// means today whatever range is being studied.</para>
    /// <para><b>Side Effects:</b> three summary reads.</para>
    /// </remarks>
    /// <returns>A task that completes when the three figures are loaded.</returns>
    private async Task LoadFixedWindowsAsync()
    {
        var today = DateTime.UtcNow.Date;
        var todayTask = AnalyticsService.GetSummaryAsync(today, today);
        var weekTask = AnalyticsService.GetSummaryAsync(today.AddDays(-6), today);
        var monthTask = AnalyticsService.GetSummaryAsync(today.AddDays(-29), today);
        await Task.WhenAll(todayTask, weekTask, monthTask);

        ViewsToday = (await todayTask).TotalViews;
        ViewsLast7Days = (await weekTask).TotalViews;
        ViewsLast30Days = (await monthTask).TotalViews;
    }

    /// <summary>
    /// Retires the previous load and returns the token governing this one.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the most recently requested range may reach the panels. The
    /// previous source is cancelled before it is disposed, so an in-flight query is stopped rather
    /// than merely abandoned — abandoning it would leave the database doing work for a range nobody is
    /// looking at any more, which is the cost REQ-NFR-026 exists to remove.</para>
    /// <para><b>Flow:</b> capture the previous source → install a fresh one → cancel and dispose the
    /// previous → hand back the new token.</para>
    /// <para><b>Side Effects:</b> Cancels the outstanding load and replaces
    /// <see cref="loadCancellation"/>.</para>
    /// </remarks>
    /// <returns>The cancellation token for the load that is starting.</returns>
    private CancellationToken StartLoad()
    {
        var previous = loadCancellation;
        loadCancellation = new CancellationTokenSource();

        if (previous is not null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        return loadCancellation.Token;
    }

    /// <summary>
    /// Cancels any outstanding load when the circuit tears the component down.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An admin who navigates away mid-load leaves four aggregate queries
    /// with no reader; cancelling on dispose returns those connections to the pool immediately.</para>
    /// <para><b>Flow:</b> cancel → dispose → clear the field.</para>
    /// <para><b>Side Effects:</b> Faults the in-flight load's awaits with cancellation, which
    /// <see cref="LoadAsync"/> swallows.</para>
    /// </remarks>
    public void Dispose()
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Mirrors the applied range back into the two date inputs.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A preset must leave the inputs showing the range it applied, so
    /// the admin can edit from there instead of retyping both ends.</para>
    /// <para><b>Flow:</b> format both bounds in the input's own date format.</para>
    /// <para><b>Side Effects:</b> Mutates the bound input text.</para>
    /// </remarks>
    private void SyncInputsFromAppliedRange()
    {
        FromDateText = appliedFrom.ToString(DateInputFormat);
        ToDateText = appliedTo.ToString(DateInputFormat);
    }

    /// <summary>
    /// Describes the applied range in words for the panel subtitles.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every panel repeats the same caption, so a reader can never
    /// mistake one chart for a different period.</para>
    /// <para><b>Flow:</b> format both bounds and the inclusive day count.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <returns>A caption such as "08 Jul – 07 Aug 2026 (31 days)".</returns>
    private string BuildRangeCaption()
    {
        var days = (appliedTo.Date - appliedFrom.Date).Days + 1;
        return $"{appliedFrom:dd MMM} – {appliedTo:dd MMM yyyy} ({days} days)";
    }

    /// <summary>
    /// Builds the four headline tiles from the loaded summary.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Each tile states the period it describes in its caption, so a
    /// figure is never read against the wrong range.</para>
    /// <para><b>Flow:</b> format each figure → pair it with an icon and a stable test id.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <returns>The tiles, in reading order.</returns>
    private IReadOnlyList<StatTile> BuildStatTiles()
    {
        return new List<StatTile>
        {
            new("Views", Summary.TotalViews.ToString("N0"), RangeCaption, "eye", "analytics-stat-views"),
            new("Unique visitors", Summary.UniqueViews.ToString("N0"), RangeCaption, "users", "analytics-stat-unique"),
            new("Avg post rating", FormatRating(Summary.AverageRating),
                $"{Summary.RatingCount:N0} ratings in range", "star", "analytics-stat-rating"),
            new("Comments", Summary.CommentCount.ToString("N0"), RangeCaption, "message-square", "analytics-stat-comments")
        };
    }
}
