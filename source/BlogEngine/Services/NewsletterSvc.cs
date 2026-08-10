using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Newsletter composition, SMTP dispatch, send history, unsubscribe and public archive.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements REQ-FN-032 (compose, send, history, unsubscribe link) and
/// REQ-FN-050 (publishing plus public archive queries). It is the only place that decides when a
/// newsletter stops being a private draft and becomes a public record.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="SaveDraftAsync"/> validates and persists a draft; a sent issue is immutable.</item>
///   <item><see cref="SendAsync"/> guards the status, resolves the audience, ensures every
///         subscriber has an unsubscribe token, mails each recipient with a personal unsubscribe
///         link, records the outcome, then stamps the issue sent with a unique slug.</item>
///   <item>Archive reads go through the repository's published-only predicate, so a draft or unsent
///         issue is never publicly reachable.</item>
///   <item><see cref="UnsubscribeAsync"/> consumes the token from an unsubscribe link, and
///         <see cref="BuildUnsubscribeUrl"/> builds that link. The page it addresses is
///         <c>BlogUI.Pages.BlogPages.Unsubscribe</c>, routed at <c>/unsubscribe/{Token}</c> with
///         <b>no authorization attribute</b> — a link that demands a sign-in is not an unsubscribe
///         link, and until 2026-08-09 that page did not exist, so every issue already mailed
///         carried a URL that answered 404.</item>
/// </list>
///
/// <para><b>The publish/archive contract — an unsent draft must never be publicly reachable.</b>
/// An issue becomes public only when all three of these hold, and every archive query enforces all
/// three in SQL rather than trusting the caller:</para>
/// <list type="number">
///   <item><b>Sent.</b> <c>MarkSentAsync</c> has stamped it, which only happens after at least one
///     message was actually delivered — see <c>PublishAsync</c>. A dispatch that failed for every
///     address stays a draft so it can be retried.</item>
///   <item><b>Public.</b> the published flag is set.</item>
///   <item><b>Slugged.</b> a non-empty, unique slug exists. A draft is inserted with <i>no</i>
///     slug, so even a mis-written archive query has nothing to resolve.</item>
/// </list>
/// <para>The corollaries are worth stating outright: <see cref="GetPublishedIssuesAsync"/>,
/// <see cref="GetPublishedBySlugAsync"/>, <see cref="GetPublishedCountAsync"/> and
/// <see cref="GetNavigationAsync"/> are the only members safe to expose anonymously;
/// <see cref="GetByIdAsync"/> and <see cref="GetAllAsync"/> return drafts and are
/// <b>administrative</b>. And once an issue is sent it is immutable — <see cref="SaveDraftAsync"/>
/// refuses to edit it, because subscribers already hold a copy and the archive is the record of
/// what they received.</para>
///
/// <para><b>Sending writes one history row per recipient, and the composer polls it.</b> Every
/// address gets its own <c>SubscriberNewsletter</c> row carrying the outcome (sent or failed) plus
/// the error text, written immediately after that address's send attempt rather than batched at the
/// end. That is what makes live progress possible: the admin composer polls
/// <see cref="GetSendHistoryAsync"/> while a dispatch is running and counts the rows. Two things
/// follow — the row count grows monotonically during a send so a partial read is meaningful rather
/// than misleading, and a large list produces a comparable number of inserts, so this is a write
/// amplification to be aware of before mailing tens of thousands of addresses. Bookkeeping failures
/// are logged and swallowed: the message has already gone by then, and refusing to continue would
/// cost the remaining recipients their copy.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> this service now sits on the fully async
/// <c>INewsletterRepo</c> surface — every repository call is awaited with
/// <c>ConfigureAwait(false)</c>, as a library must. Dispatch remains deliberately
/// <b>sequential</b>: recipients are mailed one at a time rather than in parallel, which keeps the
/// send inside most providers' rate limits and keeps the progress rows in a meaningful order.</para>
///
/// <para><b>Result contract:</b> expected failures are returned — already sent, no matching
/// subscribers, unknown id, invalid token. Unexpected ones are caught, logged with the newsletter
/// id or the recipient address, and converted into a safe, generic message; nothing throws out of
/// this class. Read methods degrade instead of failing: an archive read error yields an empty page
/// so the archive renders empty rather than erroring.</para>
///
/// <para><b>Dependencies:</b> <c>INewsletterRepo</c>, <c>IEmailService</c>, <c>IConfiguration</c>
/// (for <c>SiteSettings:BaseUrl</c>) and <c>ILogger</c>. Slugs come from the shared
/// <c>SlugGenerator</c>, the same helper blog posts use.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as
/// <c>INewsletterService</c>. Composition, dispatch and history are administrative and their pages
/// sit behind <c>AppPolicies.AdminOnly</c>; the archive reads and
/// <see cref="UnsubscribeAsync"/> are reachable anonymously by design — the unsubscribe link in a
/// mail must work without a sign-in, which is why it is authorised by an unguessable token rather
/// than by an identity. This class enforces no policy of its own.</para>
///
/// <para><b>Configure <c>SiteSettings:BaseUrl</c> before sending.</b> It is read once at
/// construction and defaults to an empty string, which yields a relative unsubscribe URL — useless
/// in an email client. A misconfigured base URL is not detected and produces a mailing that nobody
/// can opt out of. The path it is joined to is the <c>UnsubscribePath</c> constant, which must stay
/// in step with the route template on the unsubscribe page.</para>
/// </remarks>
public class NewsletterSvc : INewsletterService
{
    private const int MaxPageSize = 100;
    private const int MaxSlugAttempts = 50;

    /// <summary>
    /// The single wording used for every unresolvable unsubscribe link. Blank, unknown and
    /// malformed tokens must be indistinguishable, or the route becomes a membership oracle.
    /// </summary>
    private const string InvalidLinkMessage = "This unsubscribe link is not valid.";

    /// <summary>
    /// Path segment the unsubscribe page is routed at. It is a constant here and the route template
    /// on <c>BlogUI.Pages.BlogPages.Unsubscribe</c> so the two are edited together — a mismatch
    /// mails a dead link to every subscriber and is invisible until someone tries to opt out.
    /// </summary>
    private const string UnsubscribePath = "/unsubscribe";

    private readonly INewsletterRepo newsletterRepo;
    private readonly IEmailService emailService;
    private readonly MarkdownRenderer markdownRenderer;
    private readonly ILogger<NewsletterSvc> logger;
    private readonly string baseUrl;

    /// <summary>
    /// Initializes the newsletter service.
    /// </summary>
    /// <param name="newsletterRepo">Newsletter data access.</param>
    /// <param name="emailService">Outbound email transport.</param>
    /// <param name="markdownRenderer">Shared sanitising Markdown pipeline used to render the mail body.</param>
    /// <param name="configuration">Application configuration, read for <c>SiteSettings:BaseUrl</c>.</param>
    /// <param name="logger">Logger for send outcomes and failures.</param>
    public NewsletterSvc(
        INewsletterRepo newsletterRepo,
        IEmailService emailService,
        MarkdownRenderer markdownRenderer,
        IConfiguration configuration,
        ILogger<NewsletterSvc> logger)
    {
        this.newsletterRepo = newsletterRepo;
        this.emailService = emailService;
        this.markdownRenderer = markdownRenderer;
        this.logger = logger;
        baseUrl = configuration?["SiteSettings:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Title and content are both mandatory. A new issue is inserted
    /// as a draft (or scheduled, when a date is set) with no slug; an existing one is updated only
    /// while it is still unsent. <b>A sent issue can never be edited</b> — it is an archive record
    /// of what subscribers actually received.</para>
    /// <para><b>Flow:</b> validate → branch on the identifier → insert or update.</para>
    /// <para><b>Side Effects:</b> Inserts or updates one <c>Newsletter</c> row and mutates the
    /// supplied object (status, created date and, on insert, the generated id). Sends nothing.</para>
    /// <para><b>Result contract:</b> validation failures and the already-sent refusal are returned;
    /// an unexpected failure is logged with the newsletter id and converted to a generic
    /// message.</para>
    /// </remarks>
    public async Task<Result<Newsletter>> SaveDraftAsync(Newsletter newsletter)
    {
        if (newsletter == null || string.IsNullOrWhiteSpace(newsletter.Title))
            return Result<Newsletter>.Failure("A newsletter title is required.");

        if (string.IsNullOrWhiteSpace(newsletter.Content))
            return Result<Newsletter>.Failure("Newsletter content is required.");

        try
        {
            return newsletter.NewsletterId <= 0
                ? await InsertDraftAsync(newsletter).ConfigureAwait(false)
                : await UpdateDraftAsync(newsletter).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save newsletter draft {NewsletterId}", newsletter.NewsletterId);
            return Result<Newsletter>.Failure("The newsletter could not be saved.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Loads an issue in <i>any</i> status.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Administrative — returns drafts.</b> This member applies none of the
    /// sent/public/slugged predicate, so it must never back a public route. Use
    /// <see cref="GetPublishedBySlugAsync"/> for anything an anonymous visitor can reach.</para>
    /// </remarks>
    public async Task<Result<Newsletter>> GetByIdAsync(long newsletterId)
    {
        if (newsletterId <= 0)
            return Result<Newsletter>.Failure("A newsletter id is required.");

        try
        {
            var newsletter = await newsletterRepo.GetByIdAsync(newsletterId).ConfigureAwait(false);
            return newsletter == null
                ? Result<Newsletter>.Failure("Newsletter not found.")
                : Result<Newsletter>.Success(newsletter);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load newsletter {NewsletterId}", newsletterId);
            return Result<Newsletter>.Failure("The newsletter could not be loaded.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Every issue in every status, newest created first, for the
    /// admin history list.</para>
    /// <para><b>Side Effects:</b> None beyond logging; a read failure yields an empty list.</para>
    /// <para><b>Administrative — returns drafts.</b> Same warning as
    /// <see cref="GetByIdAsync"/>.</para>
    /// </remarks>
    public async Task<IReadOnlyList<Newsletter>> GetAllAsync()
    {
        try
        {
            // The repository returns IEnumerable so that one member can satisfy both INewsletterRepo
            // and IGenericRepository<Newsletter> (REQ-NFR-026); this contract promises a list, and
            // the rows are already buffered, so materialising here costs one copy of a short list.
            var issues = await newsletterRepo.GetAllAsync().ConfigureAwait(false);
            return issues.ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list newsletters");
            return new List<Newsletter>();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> The irreversible operation in this class. An already-sent issue
    /// is refused up front, which is the guard against a double mailing from a repeated click or a
    /// browser replay. A null audience is treated as everyone.</para>
    /// <para><b>Flow:</b> load → refuse if already sent → dispatch (see <c>DispatchAsync</c>).</para>
    /// <para><b>Side Effects:</b> <b>Sends real email to every matching subscriber</b>, writes one
    /// send-history row per recipient, and on success marks the issue sent and publishes it to the
    /// archive under a unique slug. None of this can be undone. Logs a summary line with the sent,
    /// targeted and failed counts.</para>
    /// <para><b>Not atomic and not resumable.</b> There is no transaction spanning the send: a
    /// crash part-way through leaves the earlier recipients mailed, their history rows written, and
    /// the issue still unsent — so a retry mails those people a second time. Check the send history
    /// before retrying a dispatch that did not report a result.</para>
    /// <para><b>Long-running.</b> One sequential SMTP round trip per recipient, so a large list
    /// takes minutes; the caller is expected to poll <see cref="GetSendHistoryAsync"/> for
    /// progress rather than block on this task.</para>
    /// <para><b>Result contract:</b> an already-sent issue and an empty audience are returned as
    /// failures; per-recipient failures are <i>not</i> — they are counted in the report and the
    /// overall call still succeeds. Inspect <c>FailedCount</c>, never just <c>IsSuccess</c>.</para>
    /// </remarks>
    public async Task<Result<NewsletterSendReport>> SendAsync(long newsletterId, NewsletterAudience audience)
    {
        var loaded = await GetByIdAsync(newsletterId).ConfigureAwait(false);
        if (loaded.IsFailure || loaded.Data == null)
            return Result<NewsletterSendReport>.Failure(loaded.ErrorMessage);

        if (loaded.Data.Status == Newsletter.StatusSent)
            return Result<NewsletterSendReport>.Failure("This newsletter has already been sent.");

        try
        {
            return await DispatchAsync(loaded.Data, audience ?? NewsletterAudience.Everyone).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Newsletter {NewsletterId} dispatch failed", newsletterId);
            return Result<NewsletterSendReport>.Failure("The newsletter could not be sent.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> One row per delivery attempt, carrying the address, the
    /// timestamp, the sent/failed status and the error text. This is both the permanent audit of
    /// who received an issue and the <b>live progress feed</b> the composer polls during a send —
    /// rows appear as each address is attempted, so the count is a meaningful percentage of the
    /// targeted total while the dispatch is still running.</para>
    /// <para><b>Side Effects:</b> None beyond logging; a read failure yields an empty list, which a
    /// polling caller should treat as "no progress information" rather than "nothing sent".</para>
    /// <para><b>Administrative:</b> returns subscriber email addresses. Admin-only surfaces
    /// only.</para>
    /// </remarks>
    public async Task<IReadOnlyList<NewsletterRecipient>> GetSendHistoryAsync(long newsletterId)
    {
        if (newsletterId <= 0)
            return new List<NewsletterRecipient>();

        try
        {
            return await newsletterRepo.GetSendHistoryAsync(newsletterId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read send history for newsletter {NewsletterId}", newsletterId);
            return new List<NewsletterRecipient>();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Honours the one-click unsubscribe link. The <b>token is the
    /// authorisation</b> — the link must work from an email client with no session, so an
    /// unguessable per-subscriber token stands in for an identity. That is also why the URL carries
    /// a token rather than an email address: an address in the URL would let anyone unsubscribe a
    /// stranger by typing theirs.</para>
    /// <para><b>Flow:</b> require a token → resolve the subscriber → report the already-opted-out
    /// case without writing → otherwise deactivate.</para>
    /// <para><b>Side Effects:</b> On the <see cref="UnsubscribeOutcome.Unsubscribed"/> path only,
    /// sets the subscriber inactive — a soft opt-out, so the row and its history survive. Logs the
    /// subscriber id, never the address and never the token. <b>The token is not burned</b>: it
    /// stays valid, so re-opening the link reports
    /// <see cref="UnsubscribeOutcome.AlreadyUnsubscribed"/> rather than failing, and a subscriber
    /// who resubscribes keeps the same link working.</para>
    /// <para><b>Result contract:</b> a blank token, an unknown token and an internal failure are all
    /// returned as failures carrying the <i>same</i> wording, so the route cannot be used to test
    /// whether a guessed token belongs to a real subscriber. Unexpected failures are logged without
    /// the token.</para>
    /// </remarks>
    public async Task<Result<UnsubscribeOutcome>> UnsubscribeAsync(string unsubscribeToken)
    {
        if (string.IsNullOrWhiteSpace(unsubscribeToken))
            return Result<UnsubscribeOutcome>.Failure(InvalidLinkMessage);

        try
        {
            var subscriber = await newsletterRepo
                .GetSubscriberByUnsubscribeTokenAsync(unsubscribeToken.Trim()).ConfigureAwait(false);

            if (subscriber == null)
            {
                logger.LogWarning("An unsubscribe link was opened with a token that matches no subscriber");
                return Result<UnsubscribeOutcome>.Failure(InvalidLinkMessage);
            }

            // IsConfirmed is the single mailability bit — see the remarks on Subscriber. A row that
            // is already off the list must not be written again, so the page can say "already done".
            if (!subscriber.IsConfirmed)
            {
                logger.LogInformation(
                    "Subscriber {SubscriberId} re-opened an unsubscribe link and was already opted out",
                    subscriber.SubscriberId);
                return Result<UnsubscribeOutcome>.Success(UnsubscribeOutcome.AlreadyUnsubscribed);
            }

            await newsletterRepo.DeactivateSubscriberAsync(subscriber.SubscriberId).ConfigureAwait(false);
            logger.LogInformation("Subscriber {SubscriberId} unsubscribed via newsletter link", subscriber.SubscriberId);
            return Result<UnsubscribeOutcome>.Success(UnsubscribeOutcome.Unsubscribed);
        }
        catch (Exception ex)
        {
            // Deliberately NOT InvalidLinkMessage: an outage is not a bad link, and telling a reader
            // their link is invalid when the database is down would send them away for good.
            logger.LogError(ex, "Failed to process an unsubscribe request");
            return Result<UnsubscribeOutcome>.Failure(
                "The unsubscribe request could not be processed just now. Please try the link again shortly.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> One page of the public archive. Both arguments are clamped
    /// here — page below 1 becomes 1, size is clamped into 1..100 — so a hand-edited query string
    /// cannot ask for a negative offset or a page large enough to be a denial of service. The
    /// repository applies the sent + public + slugged predicate, so a draft cannot appear.</para>
    /// <para><b>Flow:</b> clamp → count → read the page → project into the archive model.</para>
    /// <para><b>Side Effects:</b> None beyond logging. A read failure yields an empty page carrying
    /// the requested paging values, so the archive renders "no issues" rather than erroring.</para>
    /// <para><b>Safe for anonymous callers.</b></para>
    /// </remarks>
    public async Task<NewsletterArchivePage> GetPublishedIssuesAsync(int pageNumber, int pageSize)
    {
        var page = pageNumber < 1 ? 1 : pageNumber;
        var size = Math.Clamp(pageSize, 1, MaxPageSize);

        try
        {
            var totalCount = await newsletterRepo.GetPublishedCountAsync().ConfigureAwait(false);
            var items = await newsletterRepo.GetPublishedPageAsync(size, (page - 1) * size).ConfigureAwait(false);
            return new NewsletterArchivePage
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = page,
                PageSize = size
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to read newsletter archive page {PageNumber}", page);
            return new NewsletterArchivePage { PageNumber = page, PageSize = size };
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Resolves a public <c>/newsletter/{slug}</c> URL. The
    /// published-only predicate lives in the repository's SQL, so an unsent issue is unreachable
    /// here even if someone guesses a slug — and since a draft is stored with no slug at all, there
    /// is nothing to guess.</para>
    /// <para><b>Side Effects:</b> None beyond logging.</para>
    /// <para><b>Result contract:</b> "not found" covers both a genuinely unknown slug and an issue
    /// that exists but is not published — the caller cannot tell them apart, which is the intended
    /// behaviour.</para>
    /// <para><b>Safe for anonymous callers.</b></para>
    /// </remarks>
    public async Task<Result<Newsletter>> GetPublishedBySlugAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return Result<Newsletter>.Failure("A newsletter slug is required.");

        try
        {
            var newsletter = await newsletterRepo.GetPublishedBySlugAsync(slug.Trim()).ConfigureAwait(false);
            return newsletter == null
                ? Result<Newsletter>.Failure("Newsletter not found.")
                : Result<Newsletter>.Success(newsletter);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve newsletter slug {Slug}", slug);
            return Result<Newsletter>.Failure("The newsletter could not be loaded.");
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Builds the previous/next strip on an archive page. Navigation
    /// is anchored on <c>SentOn</c>, so the archive reads in dispatch order, and an issue that is
    /// not both published and sent yields an <b>empty</b> navigation rather than neighbours — a
    /// previous/next link must never point at something a reader cannot open.</para>
    /// <para><b>Flow:</b> load the issue → return empty unless published and sent → look up the
    /// neighbours by sent date.</para>
    /// <para><b>Side Effects:</b> None beyond logging; three reads per call.</para>
    /// <para><b>Never null</b> — an empty <c>NewsletterNavigation</c> is returned on every
    /// negative path, so the caller checks the two properties rather than the object.</para>
    /// <para><b>Safe for anonymous callers.</b></para>
    /// </remarks>
    public async Task<NewsletterNavigation> GetNavigationAsync(long newsletterId)
    {
        var navigation = new NewsletterNavigation();

        try
        {
            var current = await newsletterRepo.GetByIdAsync(newsletterId).ConfigureAwait(false);
            if (current == null || !current.IsPublished || !current.SentOn.HasValue)
                return navigation;

            navigation.PreviousIssue = await newsletterRepo
                .GetPreviousPublishedAsync(current.SentOn.Value).ConfigureAwait(false);
            navigation.NextIssue = await newsletterRepo
                .GetNextPublishedAsync(current.SentOn.Value).ConfigureAwait(false);
            return navigation;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve archive navigation for newsletter {NewsletterId}", newsletterId);
            return navigation;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts publicly visible issues only, using the same predicate
    /// as the archive page — the two must agree or the pager will offer a page that renders
    /// empty.</para>
    /// <para><b>Side Effects:</b> None beyond logging; a read failure returns 0, collapsing the
    /// pager rather than throwing.</para>
    /// <para><b>Safe for anonymous callers.</b></para>
    /// </remarks>
    public async Task<int> GetPublishedCountAsync()
    {
        try
        {
            return await newsletterRepo.GetPublishedCountAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to count published newsletters");
            return 0;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Composes the absolute one-click unsubscribe URL from the
    /// configured base URL and the subscriber's token. Absolute by necessity — the link is followed
    /// from a mail client, which has no notion of the site's origin.</para>
    /// <para><b>Flow:</b> blank-token guard → concatenate base and token.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// <para><b>Two silent-failure modes to know about:</b> a blank token yields an empty string,
    /// and an unconfigured <c>SiteSettings:BaseUrl</c> yields a relative path. Either produces a
    /// mailing whose unsubscribe link does not work, and neither raises an error — verify the base
    /// URL before the first send from a new deployment.</para>
    /// </remarks>
    public string BuildUnsubscribeUrl(string unsubscribeToken)
    {
        if (string.IsNullOrWhiteSpace(unsubscribeToken))
            return string.Empty;

        return $"{baseUrl}{UnsubscribePath}/{Uri.EscapeDataString(unsubscribeToken.Trim())}";
    }

    /// <summary>
    /// Persists a brand-new draft.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A new issue always starts as a draft with no slug, so it cannot
    /// be reached publicly before it is sent.</para>
    /// <para><b>Flow:</b> stamp creation time and draft status → insert → return with the new id.</para>
    /// <para><b>Side Effects:</b> Inserts a <c>Newsletter</c> row.</para>
    /// </remarks>
    /// <param name="newsletter">The draft to insert.</param>
    /// <returns>The persisted draft.</returns>
    private async Task<Result<Newsletter>> InsertDraftAsync(Newsletter newsletter)
    {
        newsletter.CreatedOn = newsletter.CreatedOn == default ? DateTime.UtcNow : newsletter.CreatedOn;
        newsletter.Status = newsletter.ScheduledFor.HasValue ? Newsletter.StatusScheduled : Newsletter.StatusDraft;
        newsletter.NewsletterId = await newsletterRepo.InsertToGetIdAsync(newsletter).ConfigureAwait(false);
        logger.LogInformation("Newsletter draft {NewsletterId} created", newsletter.NewsletterId);
        return Result<Newsletter>.Success(newsletter);
    }

    /// <summary>
    /// Updates an existing draft, refusing to alter an issue that has already gone out.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A sent issue is an archive record; editing it would rewrite what
    /// subscribers already received.</para>
    /// <para><b>Flow:</b> load the stored issue → reject when sent → update → return.</para>
    /// <para><b>Side Effects:</b> Updates a <c>Newsletter</c> row.</para>
    /// </remarks>
    /// <param name="newsletter">The draft carrying the new values.</param>
    /// <returns>The persisted draft, or a failure when the issue is already sent.</returns>
    private async Task<Result<Newsletter>> UpdateDraftAsync(Newsletter newsletter)
    {
        var existing = await newsletterRepo.GetByIdAsync(newsletter.NewsletterId).ConfigureAwait(false);
        if (existing == null)
            return Result<Newsletter>.Failure("Newsletter not found.");

        if (existing.Status == Newsletter.StatusSent)
            return Result<Newsletter>.Failure("A newsletter that has been sent can no longer be edited.");

        newsletter.Status = newsletter.ScheduledFor.HasValue ? Newsletter.StatusScheduled : Newsletter.StatusDraft;
        await newsletterRepo.UpdateAsync(newsletter).ConfigureAwait(false);
        logger.LogInformation("Newsletter draft {NewsletterId} updated", newsletter.NewsletterId);
        return Result<Newsletter>.Success(newsletter);
    }

    /// <summary>
    /// Mails one issue to a resolved audience and publishes it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A send with no recipients is refused rather than silently
    /// marking the issue sent. A per-address failure is recorded and logged but does not abort the
    /// run, so one bad address cannot cost the whole list. The issue is published only when at
    /// least one message was delivered.</para>
    /// <para><b>Flow:</b> repair missing tokens → resolve audience → guard emptiness → mail each
    /// recipient → stamp sent → log and return the report.</para>
    /// <para><b>Side Effects:</b> Sends email, writes send-history rows and updates the issue.</para>
    /// </remarks>
    /// <param name="newsletter">The issue to send.</param>
    /// <param name="audience">The resolved audience filter.</param>
    /// <returns>The dispatch report, or a failure when nothing could be sent.</returns>
    private async Task<Result<NewsletterSendReport>> DispatchAsync(Newsletter newsletter, NewsletterAudience audience)
    {
        await newsletterRepo.EnsureUnsubscribeTokensAsync().ConfigureAwait(false);
        var recipients = await newsletterRepo.GetRecipientsAsync(audience).ConfigureAwait(false);
        if (recipients.Count == 0)
            return Result<NewsletterSendReport>.Failure("No subscribers match the selected audience.");

        var report = new NewsletterSendReport
        {
            NewsletterId = newsletter.NewsletterId,
            TargetedCount = recipients.Count,
            SentOn = DateTime.UtcNow
        };

        foreach (var recipient in recipients)
        {
            var isDelivered = await DeliverAsync(newsletter, recipient).ConfigureAwait(false);
            if (isDelivered)
                report.SentCount++;
            else
                report.FailedCount++;
        }

        report.Slug = await PublishAsync(newsletter, report).ConfigureAwait(false);
        logger.LogInformation(
            "Newsletter {NewsletterId} sent to {SentCount} of {TargetedCount} subscribers ({FailedCount} failed), slug {Slug}",
            newsletter.NewsletterId, report.SentCount, report.TargetedCount, report.FailedCount, report.Slug);
        return Result<NewsletterSendReport>.Success(report);
    }

    /// <summary>
    /// Sends one issue to one subscriber and records the outcome.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every message carries the subscriber's personal unsubscribe
    /// link, in the body and in the <c>List-Unsubscribe</c> header.</para>
    /// <para><b>Flow:</b> build the message → send → write a history row with the outcome.</para>
    /// <para><b>Side Effects:</b> Sends email; inserts a <c>SubscriberNewsletter</c> row.</para>
    /// </remarks>
    /// <param name="newsletter">The issue being sent.</param>
    /// <param name="subscriber">The recipient.</param>
    /// <returns>True when the transport accepted the message.</returns>
    private async Task<bool> DeliverAsync(Newsletter newsletter, Subscriber subscriber)
    {
        var unsubscribeUrl = BuildUnsubscribeUrl(subscriber.UnsubscribeToken);
        var message = new EmailMessage
        {
            ToAddress = subscriber.Email,
            ToName = subscriber.Name,
            Subject = newsletter.Title,
            HtmlBody = BuildBody(newsletter, unsubscribeUrl),
            TextBody = $"{newsletter.Content}\n\nUnsubscribe: {unsubscribeUrl}",
            UnsubscribeUrl = unsubscribeUrl
        };

        var sendResult = await emailService.SendAsync(message).ConfigureAwait(false);
        if (sendResult.IsFailure)
            logger.LogError("Newsletter {NewsletterId} could not be delivered to {Email}: {Error}",
                newsletter.NewsletterId, subscriber.Email, sendResult.ErrorMessage);

        await RecordAttemptAsync(newsletter, subscriber, sendResult).ConfigureAwait(false);
        return sendResult.IsSuccess;
    }

    /// <summary>
    /// Writes one send-history row, never letting a bookkeeping error abort the run.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The message has already been sent by this point, so a failure to
    /// record it is logged but must not roll the send back or stop the remaining recipients.</para>
    /// <para><b>Flow:</b> build the row → insert → log any bookkeeping failure.</para>
    /// <para><b>Side Effects:</b> Inserts a <c>SubscriberNewsletter</c> row.</para>
    /// </remarks>
    /// <param name="newsletter">The issue that was sent.</param>
    /// <param name="subscriber">The recipient.</param>
    /// <param name="sendResult">The transport outcome.</param>
    /// <returns>A task that completes when the attempt has been recorded.</returns>
    private async Task RecordAttemptAsync(Newsletter newsletter, Subscriber subscriber, Result sendResult)
    {
        try
        {
            await newsletterRepo.InsertRecipientAsync(new NewsletterRecipient
            {
                NewsletterId = newsletter.NewsletterId,
                SubscriberId = subscriber.SubscriberId,
                Email = subscriber.Email,
                SentOn = DateTime.UtcNow,
                SendStatus = sendResult.IsSuccess ? NewsletterRecipient.StatusSent : NewsletterRecipient.StatusFailed,
                ErrorMessage = sendResult.IsSuccess ? string.Empty : sendResult.ErrorMessage ?? string.Empty
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record newsletter {NewsletterId} delivery to {Email}",
                newsletter.NewsletterId, subscriber.Email);
        }
    }

    /// <summary>
    /// Stamps a delivered issue as sent and publishes it under a unique slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only an issue that actually reached someone becomes a public
    /// archive record; a run that failed for every address stays a draft so it can be retried.</para>
    /// <para><b>Flow:</b> guard delivery → generate a unique slug → mark sent.</para>
    /// <para><b>Side Effects:</b> Updates the <c>Newsletter</c> row.</para>
    /// </remarks>
    /// <param name="newsletter">The issue that was sent.</param>
    /// <param name="report">The dispatch report so far.</param>
    /// <returns>The assigned slug, or an empty string when nothing was delivered.</returns>
    private async Task<string> PublishAsync(Newsletter newsletter, NewsletterSendReport report)
    {
        if (!report.HasReachedAnyone)
        {
            logger.LogError("Newsletter {NewsletterId} reached no subscribers and was not published",
                newsletter.NewsletterId);
            return string.Empty;
        }

        var slug = await ResolveUniqueSlugAsync(newsletter).ConfigureAwait(false);
        await newsletterRepo
            .MarkSentAsync(newsletter.NewsletterId, slug, report.SentOn, report.SentCount, true)
            .ConfigureAwait(false);
        return slug;
    }

    /// <summary>
    /// Produces a slug that no other issue is using.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Reuses the shared <c>SlugGenerator</c> so newsletter URLs read
    /// exactly like post URLs. A title that slugs to nothing (for example one made entirely of
    /// punctuation) falls back to an id-based slug rather than an empty one, which would make the
    /// issue unreachable.</para>
    /// <para><b>Flow:</b> slug the title → fall back if empty → append a counter until free.</para>
    /// <para><b>Side Effects:</b> None beyond existence checks.</para>
    /// </remarks>
    /// <param name="newsletter">The issue being published.</param>
    /// <returns>A slug that is unique across the <c>Newsletter</c> table.</returns>
    private async Task<string> ResolveUniqueSlugAsync(Newsletter newsletter)
    {
        var baseSlug = SlugGenerator.GenerateSlug(newsletter.Title);
        if (string.IsNullOrWhiteSpace(baseSlug))
            baseSlug = $"newsletter-{newsletter.NewsletterId}";

        var candidate = baseSlug;
        for (var attempt = 1; attempt <= MaxSlugAttempts; attempt++)
        {
            if (!await newsletterRepo.SlugExistsAsync(candidate).ConfigureAwait(false))
                return candidate;

            candidate = SlugGenerator.GenerateUniqueSlug(baseSlug, attempt);
        }

        return $"{baseSlug}-{newsletter.NewsletterId}";
    }

    /// <summary>
    /// Renders the newsletter body with its mandatory unsubscribe footer.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The composer writes Markdown and its preview pane promises "this
    /// is exactly what a subscriber receives", so the body is rendered through the same sanitising
    /// <see cref="MarkdownRenderer"/> the preview uses. Interpolating the raw Markdown straight into
    /// the HTML body — which is what this used to do — mailed subscribers literal <c>##</c> headings
    /// and unrendered links, and made the preview a lie.</para>
    /// <para><b>The unsubscribe footer is appended here, not left to the composer</b>, so no issue
    /// can ever go out without one — that is the whole compliance guarantee, and moving it into
    /// author-editable content would let a single forgotten paste mail a list with no way off
    /// it.</para>
    /// <para><b>Flow:</b> render the Markdown → append the footer carrying the recipient's personal
    /// unsubscribe link.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// <para><b>Security:</b> The renderer disables raw HTML and strips unsafe URLs and attributes,
    /// so a body pasted from an untrusted source cannot smuggle markup into the mail. The footer's
    /// styling is inline because mail clients discard a stylesheet.</para>
    /// </remarks>
    /// <param name="newsletter">The issue being rendered.</param>
    /// <param name="unsubscribeUrl">The recipient's personal unsubscribe URL.</param>
    /// <returns>The HTML body.</returns>
    private string BuildBody(Newsletter newsletter, string unsubscribeUrl)
    {
        var contentHtml = markdownRenderer.ToHtml(newsletter.Content ?? string.Empty);

        return $@"{contentHtml}
<hr />
<p style=""font-size:12px"">You are receiving this because you subscribed to the newsletter.
<a href=""{unsubscribeUrl}"">Unsubscribe</a></p>";
    }
}
