using BlogModels;
using Microsoft.AspNetCore.Components;

namespace BlogUI.Components;

/// <summary>
/// The staff ratings dialog shared by the Analytics page and the Posts list.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-UI-067 / REQ-UI-068 / REQ-FN-063 (BRD-107…109) — lets the staff see
/// every rating behind an average, confirmed or not, without ever seeing a rater's full address.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The parent sets <see cref="PostId"/> and <see cref="PostTitle"/> and opens the dialog.</item>
///   <item><see cref="OnParametersSetAsync"/> loads the rows once per opening through
///         <c>RatingSvc.GetRatingsForStaffAsync</c>, which masks every email.</item>
///   <item>Closing raises <see cref="OpenChanged"/> so the parent's bound flag follows.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>BlogEngine.Services.RatingSvc</c>; TrBlazeUI Dialog, Table, Badge.</para>
///
/// <para><b>Usage:</b> <c>&lt;RatingsDialog @bind-Open="flag" PostId="id" PostTitle="title" /&gt;</c>.</para>
/// </remarks>
public partial class RatingsDialog : ComponentBase
{
    private const int MaxStars = 5;

    private long loadedPostId;
    private bool wasOpen;

    /// <summary>
    /// The rating service; it masks the email before any row reaches this component.
    /// </summary>
    [Inject]
    public BlogEngine.Services.RatingSvc RatingService { get; set; } = default!;

    /// <summary>
    /// Whether the dialog is open.
    /// </summary>
    [Parameter]
    public bool Open { get; set; }

    /// <summary>
    /// Raised when the dialog opens or closes, for <c>@bind-Open</c>.
    /// </summary>
    [Parameter]
    public EventCallback<bool> OpenChanged { get; set; }

    /// <summary>
    /// The post whose ratings are shown.
    /// </summary>
    [Parameter]
    public long PostId { get; set; }

    /// <summary>
    /// The post's title, shown in the dialog's description.
    /// </summary>
    [Parameter]
    public string PostTitle { get; set; } = string.Empty;

    /// <summary>
    /// The loaded rows, newest first.
    /// </summary>
    public IReadOnlyList<StaffRatingRow> Rows { get; private set; } = Array.Empty<StaffRatingRow>();

    /// <summary>
    /// True while the rows are loading.
    /// </summary>
    public bool IsLoading { get; private set; }

    /// <summary>
    /// A message to show when the rows could not be loaded.
    /// </summary>
    public string ErrorMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Title plus the confirmed average and counts, for example
    /// "Post title · 4.5 ★ from 2 confirmed ratings · 1 not yet confirmed".
    /// </summary>
    public string SummaryText => BuildSummary(PostTitle, Rows, IsLoading);

    /// <summary>
    /// Five characters of filled and empty stars for a 1–5 score.
    /// </summary>
    /// <param name="stars">The score.</param>
    /// <returns>For example <c>★★★★☆</c>.</returns>
    public static string StarText(int stars)
    {
        var filled = Math.Clamp(stars, 0, MaxStars);
        return new string('★', filled) + new string('☆', MaxStars - filled);
    }

    /// <summary>
    /// Builds the dialog's one-line summary from the rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> the average counts confirmed ratings only (REQ-FN-063), matching
    /// the public post page; unconfirmed ones are counted separately so the staff can see them.</para>
    /// </remarks>
    /// <param name="title">The post's title.</param>
    /// <param name="rows">The rows.</param>
    /// <param name="isLoading">Whether the rows are still loading.</param>
    /// <returns>The summary line.</returns>
    public static string BuildSummary(string title, IReadOnlyList<StaffRatingRow> rows, bool isLoading)
    {
        if (isLoading)
            return title;

        var confirmed = rows.Where(row => row.IsConfirmed).ToList();
        var pending = rows.Count - confirmed.Count;
        var average = confirmed.Count == 0
            ? "no confirmed ratings"
            : $"{confirmed.Average(row => row.Stars):0.0} ★ from {confirmed.Count} confirmed rating{(confirmed.Count == 1 ? "" : "s")}";
        var pendingText = pending == 0 ? string.Empty : $" · {pending} not yet confirmed";
        return $"{title} · {average}{pendingText}";
    }

    /// <summary>
    /// Loads the rows when the dialog has just opened, or opened for a different post.
    /// </summary>
    /// <returns>A task that completes when the rows are loaded.</returns>
    protected override async Task OnParametersSetAsync()
    {
        var justOpened = Open && (!wasOpen || loadedPostId != PostId);
        wasOpen = Open;
        if (!justOpened)
            return;

        loadedPostId = PostId;
        IsLoading = true;
        ErrorMessage = string.Empty;
        Rows = Array.Empty<StaffRatingRow>();

        var result = await RatingService.GetRatingsForStaffAsync(PostId);
        IsLoading = false;
        if (result.IsSuccess && result.Data != null)
            Rows = result.Data;
        else
            ErrorMessage = result.ErrorMessage ?? "The ratings could not be loaded.";
    }

    private async Task OnOpenChangedAsync(bool isOpen)
    {
        Open = isOpen;
        wasOpen = isOpen;
        await OpenChanged.InvokeAsync(isOpen);
    }

    private Task CloseAsync() => OnOpenChangedAsync(false);
}
