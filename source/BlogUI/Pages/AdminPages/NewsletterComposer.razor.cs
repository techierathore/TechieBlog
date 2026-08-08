using BlogEngine.Common;
using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components;
using TrBlazeUI.Components.Badge;
using TrBlazeUI.Components.Toast;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// State and behaviour for the admin newsletter composer.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-UI-043 (BRD-59) — compose an issue in Markdown, preview it,
/// pick the audience, dispatch it with visible progress, and read the resulting send history.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="OnInitializedAsync"/> loads the issue list and the subscriber counts that size
///         the audience.</item>
///   <item><see cref="SaveDraftAsync"/> persists the issue; an issue must exist before it can be
///         sent, so the send path saves first and reuses the returned identifier.</item>
///   <item><see cref="ConfirmSendAsync"/> starts the dispatch and polls the send-history row count
///         while it runs, so the progress bar reports database facts rather than a timer.</item>
///   <item>After the dispatch the outcome report, the issue list and the delivery log are all
///         refreshed from the service.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="INewsletterService"/> (draft, send, history),
/// <see cref="SubscriberSvc"/> (audience estimate) and <see cref="ToastService"/>.</para>
///
/// <para><b>Usage:</b> Routed at <c>/admin/newsletter</c> behind the <c>AdminOnly</c> policy and
/// rendered inside <c>AdminLayout</c>. A sent issue is read-only: the composer disables every
/// editing control rather than letting the service reject the edit later.</para>
/// </remarks>
public partial class NewsletterComposer : ComponentBase
{
    /// <summary>
    /// Audience selection covering every confirmed subscriber.
    /// </summary>
    private const string AudienceActive = "active";

    /// <summary>
    /// Audience selection covering unconfirmed subscribers as well.
    /// </summary>
    private const string AudienceEveryone = "everyone";

    /// <summary>
    /// Audience selection restricted by an email substring.
    /// </summary>
    private const string AudienceSegment = "segment";

    /// <summary>
    /// Delay between send-progress polls while a dispatch is running.
    /// </summary>
    private const int ProgressPollMilliseconds = 300;

    /// <summary>
    /// Shared Markdown pipeline for the preview pane; building one per keystroke would be wasteful.
    /// </summary>
    private static readonly MarkdownRenderer PreviewRenderer = new MarkdownRenderer();

    /// <summary>
    /// Newsletter draft, dispatch and history service.
    /// </summary>
    [Inject]
    public INewsletterService NewsletterService { get; set; } = default!;

    /// <summary>
    /// Subscriber service, used only to size the selected audience.
    /// </summary>
    [Inject]
    public SubscriberSvc SubscriberService { get; set; } = default!;

    /// <summary>
    /// Toast notifications for send outcomes.
    /// </summary>
    [Inject]
    public ToastService ToastService { get; set; } = default!;

    /// <summary>
    /// Identifier of the issue being edited; zero until it has been saved once.
    /// </summary>
    public long NewsletterId { get; private set; }

    /// <summary>
    /// Subject line, which also becomes the archive title.
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Optional one-line teaser shown in the public archive.
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Markdown body of the issue.
    /// </summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Selected editor tab: <c>write</c> or <c>preview</c>.
    /// </summary>
    public string EditorTab { get; set; } = "write";

    /// <summary>
    /// Selected audience mode.
    /// </summary>
    public string AudienceMode { get; set; } = AudienceActive;

    /// <summary>
    /// Email substring applied when the audience mode is a segment.
    /// </summary>
    public string EmailFilter { get; set; } = string.Empty;

    /// <summary>
    /// Status of the issue being edited.
    /// </summary>
    public string IssueStatus { get; private set; } = Newsletter.StatusDraft;

    /// <summary>
    /// Feedback banner text.
    /// </summary>
    public string? StatusMessage { get; private set; }

    /// <summary>
    /// True when <see cref="StatusMessage"/> reports a failure.
    /// </summary>
    public bool IsError { get; private set; }

    /// <summary>
    /// True while a dispatch is in flight.
    /// </summary>
    public bool IsSending { get; private set; }

    /// <summary>
    /// True while a draft is being saved or an issue loaded.
    /// </summary>
    public bool IsSaving { get; private set; }

    /// <summary>
    /// True when the send confirmation dialog is open.
    /// </summary>
    public bool ShowSendDialog { get; private set; }

    /// <summary>
    /// Recipients confirmed delivered so far in the running dispatch.
    /// </summary>
    public int SentSoFar { get; private set; }

    /// <summary>
    /// Report from the most recent dispatch, or null when nothing has been sent this session.
    /// </summary>
    public NewsletterSendReport? LastReport { get; private set; }

    /// <summary>
    /// Every issue, newest first.
    /// </summary>
    public IReadOnlyList<Newsletter> Issues { get; private set; } = new List<Newsletter>();

    /// <summary>
    /// Per-recipient delivery log for the issue being edited.
    /// </summary>
    public IReadOnlyList<NewsletterRecipient> SendHistory { get; private set; } = new List<NewsletterRecipient>();

    /// <summary>
    /// Confirmed subscribers on the list.
    /// </summary>
    public int ActiveSubscriberCount { get; private set; }

    /// <summary>
    /// All subscribers on the list, confirmed or not.
    /// </summary>
    public int TotalSubscriberCount { get; private set; }

    /// <summary>
    /// Subscribers the currently selected audience would reach.
    /// </summary>
    public int EstimatedRecipients { get; private set; }

    /// <summary>
    /// True when the composer is busy and its controls must not accept input.
    /// </summary>
    public bool IsBusy => IsSending || IsSaving;

    /// <summary>
    /// True when the loaded issue has already gone out and is therefore read-only.
    /// </summary>
    public bool IsSent => IssueStatus == Newsletter.StatusSent;

    /// <summary>
    /// Placeholder for the segment filter input.
    /// </summary>
    /// <remarks>
    /// Held as a property because the Razor compiler rejects an at-sign inside a literal component
    /// attribute value (it reads as the start of a C# expression).
    /// </remarks>
    public static string SegmentPlaceholder => "e.g. @techieblog.com";

    /// <summary>
    /// Rendered HTML for the preview pane.
    /// </summary>
    public string PreviewHtml => PreviewRenderer.ToHtml(Body);

    /// <summary>
    /// Percentage of the estimated audience already delivered to.
    /// </summary>
    public double SendProgressPercent =>
        EstimatedRecipients <= 0 ? 0 : Math.Min(100, Math.Round(SentSoFar * 100d / EstimatedRecipients, 1));

    /// <summary>
    /// Label for the badge beside the page title.
    /// </summary>
    public string StatusLabel => IsSent ? "Sent" : NewsletterId > 0 ? "Draft saved" : "New draft";

    /// <summary>
    /// Badge variant matching <see cref="StatusLabel"/>.
    /// </summary>
    public BadgeVariant StatusBadgeVariant => IsSent ? BadgeVariant.Default : BadgeVariant.Outline;

    /// <summary>
    /// All subscribers, cached so the audience estimate does not re-query on every keystroke.
    /// </summary>
    private List<Subscriber> subscribers = new List<Subscriber>();

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        LoadSubscribers();
        await RefreshIssuesAsync();
    }

    /// <summary>
    /// Clears the composer so a new issue can be written.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Opening a sent issue leaves the composer read-only; this is the
    /// way back to an editable state without reloading the page.</para>
    /// <para><b>Flow:</b> reset the identity, the fields and the per-issue history.</para>
    /// <para><b>Side Effects:</b> Discards unsaved edits.</para>
    /// </remarks>
    public void StartNewIssue()
    {
        NewsletterId = 0;
        Subject = string.Empty;
        Summary = string.Empty;
        Body = string.Empty;
        IssueStatus = Newsletter.StatusDraft;
        LastReport = null;
        SendHistory = new List<NewsletterRecipient>();
        StatusMessage = string.Empty;
        IsError = false;
        EditorTab = "write";
    }

    /// <summary>
    /// Persists the composer's contents as a draft.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The service refuses an empty subject or body and refuses to edit
    /// a sent issue; the page surfaces that verdict rather than second-guessing it.</para>
    /// <para><b>Flow:</b> build the model → save → adopt the returned identifier → refresh the list.</para>
    /// <para><b>Side Effects:</b> Inserts or updates a <c>Newsletter</c> row.</para>
    /// </remarks>
    /// <returns>True when the draft is stored and carries an identifier.</returns>
    public async Task<bool> SaveDraftAsync()
    {
        IsSaving = true;
        var result = await NewsletterService.SaveDraftAsync(BuildDraft());
        IsSaving = false;

        if (result.IsFailure)
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
            ToastService.Error(result.ErrorMessage ?? "The newsletter could not be saved.", "Newsletter");
            return false;
        }

        NewsletterId = result.Data.NewsletterId;
        IssueStatus = result.Data.Status;
        StatusMessage = $"Draft saved (issue #{NewsletterId}).";
        IsError = false;
        await RefreshIssuesAsync();
        return true;
    }

    /// <summary>
    /// Loads an existing issue into the composer.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A sent issue is opened for reading — its delivery log is the
    /// point of opening it — while a draft is opened for editing.</para>
    /// <para><b>Flow:</b> read the issue → copy it into the fields → read its delivery log.</para>
    /// <para><b>Side Effects:</b> Discards unsaved edits.</para>
    /// </remarks>
    /// <param name="newsletterId">The issue to open.</param>
    /// <returns>A task that completes when the issue and its delivery log are loaded.</returns>
    public async Task LoadIssueAsync(long newsletterId)
    {
        IsSaving = true;
        var result = await NewsletterService.GetByIdAsync(newsletterId);
        IsSaving = false;

        if (result.IsFailure)
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
            return;
        }

        NewsletterId = result.Data.NewsletterId;
        Subject = result.Data.Title;
        Summary = result.Data.Summary;
        Body = result.Data.Content;
        IssueStatus = result.Data.Status;
        LastReport = null;
        StatusMessage = string.Empty;
        IsError = false;
        SendHistory = await NewsletterService.GetSendHistoryAsync(NewsletterId);
    }

    /// <summary>
    /// Opens the send confirmation dialog.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A dispatch cannot be recalled, so it is always confirmed.</para>
    /// <para><b>Flow:</b> refresh the audience estimate → open the dialog.</para>
    /// <para><b>Side Effects:</b> None beyond dialog state.</para>
    /// </remarks>
    public void OpenSendDialog()
    {
        RefreshAudienceEstimate();
        ShowSendDialog = true;
    }

    /// <summary>
    /// Closes the send confirmation dialog without sending.
    /// </summary>
    public void CancelSend()
    {
        ShowSendDialog = false;
    }

    /// <summary>
    /// Keeps the dialog's open state in step with dismissals driven by the component itself.
    /// </summary>
    /// <param name="isOpen">The dialog's new open state.</param>
    public void OnSendDialogOpenChanged(bool isOpen)
    {
        ShowSendDialog = isOpen;
    }

    /// <summary>
    /// Saves the issue if needed, dispatches it, and reports progress while it runs.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Progress is read from the send-history rows the service writes as
    /// it goes, so the bar reflects deliveries that actually happened. An unsaved issue is saved
    /// first, because a dispatch is addressed by identifier.</para>
    /// <para><b>Flow:</b> close the dialog → save → start the dispatch → poll the delivery count →
    /// adopt the report → refresh the list and the delivery log → toast the outcome.</para>
    /// <para><b>Side Effects:</b> Sends email, writes send-history rows and publishes the issue.</para>
    /// </remarks>
    /// <returns>A task that completes when the dispatch has finished and the page is refreshed.</returns>
    public async Task ConfirmSendAsync()
    {
        ShowSendDialog = false;

        if (!await SaveDraftAsync())
        {
            return;
        }

        RefreshAudienceEstimate();
        SentSoFar = 0;
        IsSending = true;
        StateHasChanged();

        var dispatch = NewsletterService.SendAsync(NewsletterId, BuildAudience());
        while (!dispatch.IsCompleted)
        {
            await Task.WhenAny(dispatch, Task.Delay(ProgressPollMilliseconds));
            SentSoFar = (await NewsletterService.GetSendHistoryAsync(NewsletterId)).Count;
            StateHasChanged();
        }

        var result = await dispatch;
        IsSending = false;
        await CompleteSendAsync(result);
    }

    /// <summary>
    /// Adopts a new audience selection and resizes the estimate with it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The recipient count must move the moment the audience does,
    /// otherwise the confirmation dialog would quote a stale number.</para>
    /// <para><b>Flow:</b> store the mode → recompute the estimate.</para>
    /// <para><b>Side Effects:</b> Updates the displayed recipient count.</para>
    /// </remarks>
    /// <param name="audienceMode">The newly selected audience mode.</param>
    public void OnAudienceChanged(string audienceMode)
    {
        AudienceMode = audienceMode;
        RefreshAudienceEstimate();
    }

    /// <summary>
    /// Recomputes how many subscribers the selected audience would reach.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The estimate mirrors the repository's own audience predicate
    /// (confirmed subscribers unless inactive ones are included, optionally narrowed by an email
    /// substring), so the number on screen matches the number that will be mailed.</para>
    /// <para><b>Flow:</b> filter the cached subscribers by the selected mode → count.</para>
    /// <para><b>Side Effects:</b> None beyond the displayed count.</para>
    /// </remarks>
    public void RefreshAudienceEstimate()
    {
        var candidates = AudienceMode == AudienceEveryone
            ? subscribers.AsEnumerable()
            : subscribers.Where(subscriber => subscriber.IsConfirmed);

        if (AudienceMode == AudienceSegment && !string.IsNullOrWhiteSpace(EmailFilter))
        {
            candidates = candidates.Where(subscriber =>
                subscriber.Email != null &&
                subscriber.Email.Contains(EmailFilter.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        EstimatedRecipients = candidates.Count();
    }

    /// <summary>
    /// Formats the date shown against an issue in the history list.
    /// </summary>
    /// <param name="issue">The issue being listed.</param>
    /// <returns>The send date for a sent issue, otherwise its creation date.</returns>
    public static string FormatIssueDate(Newsletter issue)
    {
        if (issue == null)
        {
            return string.Empty;
        }

        var stamp = issue.SentOn ?? issue.CreatedOn;
        return stamp.ToString("dd MMM yyyy");
    }

    /// <summary>
    /// Human-readable status for an issue in the history list.
    /// </summary>
    /// <param name="issue">The issue being listed.</param>
    /// <returns>"Sent", "Scheduled" or "Draft".</returns>
    public static string IssueStatusLabel(Newsletter issue)
    {
        if (issue == null)
        {
            return string.Empty;
        }

        return issue.Status switch
        {
            Newsletter.StatusSent => "Sent",
            Newsletter.StatusScheduled => "Scheduled",
            _ => "Draft"
        };
    }

    /// <summary>
    /// Badge variant matching an issue's status.
    /// </summary>
    /// <param name="issue">The issue being listed.</param>
    /// <returns>A solid badge for a sent issue, an outline badge otherwise.</returns>
    public static BadgeVariant IssueBadgeVariant(Newsletter issue) =>
        issue != null && issue.Status == Newsletter.StatusSent ? BadgeVariant.Default : BadgeVariant.Outline;

    /// <summary>
    /// Applies a finished dispatch to the page.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A failed dispatch leaves the issue a draft so it can be retried;
    /// only a successful one flips the composer into its read-only sent state.</para>
    /// <para><b>Flow:</b> branch on the result → set the banner and toast → refresh list and log.</para>
    /// <para><b>Side Effects:</b> Re-queries the issue list and the delivery log.</para>
    /// </remarks>
    /// <param name="result">The dispatch outcome.</param>
    /// <returns>A task that completes when the page reflects the outcome.</returns>
    private async Task CompleteSendAsync(Result<NewsletterSendReport> result)
    {
        if (result.IsFailure)
        {
            StatusMessage = result.ErrorMessage;
            IsError = true;
            ToastService.Error(result.ErrorMessage ?? "The newsletter could not be sent.", "Newsletter not sent");
        }
        else
        {
            LastReport = result.Data;
            IssueStatus = Newsletter.StatusSent;
            SentSoFar = result.Data.SentCount;
            StatusMessage = $"Newsletter sent to {result.Data.SentCount} subscriber(s).";
            IsError = false;
            ToastService.Success(StatusMessage, "Newsletter");
        }

        SendHistory = await NewsletterService.GetSendHistoryAsync(NewsletterId);
        await RefreshIssuesAsync();
    }

    /// <summary>
    /// Builds the model persisted by a draft save.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The subject doubles as the archive title, so it is trimmed rather
    /// than stored with stray whitespace that would leak into a slug.</para>
    /// <para><b>Flow:</b> copy the composer fields onto a <c>Newsletter</c>.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <returns>The draft to persist.</returns>
    private Newsletter BuildDraft() => new Newsletter
    {
        NewsletterId = NewsletterId,
        Title = Subject?.Trim() ?? string.Empty,
        Summary = Summary?.Trim() ?? string.Empty,
        Content = Body ?? string.Empty,
        Status = IssueStatus
    };

    /// <summary>
    /// Translates the selected audience mode into the service's audience contract.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "Everyone" is the only mode that reaches unconfirmed addresses;
    /// a segment narrows the confirmed list rather than widening it.</para>
    /// <para><b>Flow:</b> map the mode onto <c>NewsletterAudience</c>.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <returns>The audience the dispatch should target.</returns>
    private NewsletterAudience BuildAudience() => new NewsletterAudience
    {
        IncludeInactive = AudienceMode == AudienceEveryone,
        EmailFilter = AudienceMode == AudienceSegment ? EmailFilter?.Trim() ?? string.Empty : string.Empty
    };

    /// <summary>
    /// Caches the subscriber list and the counts the audience card shows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The list is read once per page load; a typing admin should not
    /// cost one query per keystroke.</para>
    /// <para><b>Flow:</b> read all subscribers → count → seed the estimate.</para>
    /// <para><b>Side Effects:</b> None beyond the cached list.</para>
    /// </remarks>
    private void LoadSubscribers()
    {
        subscribers = SubscriberService.GetAllSubscribers().ToList();
        TotalSubscriberCount = subscribers.Count;
        ActiveSubscriberCount = subscribers.Count(subscriber => subscriber.IsConfirmed);
        RefreshAudienceEstimate();
    }

    /// <summary>
    /// Reloads the issue list shown in the history card.
    /// </summary>
    /// <returns>A task that completes when the list has been refreshed.</returns>
    private async Task RefreshIssuesAsync()
    {
        Issues = await NewsletterService.GetAllAsync();
    }
}
