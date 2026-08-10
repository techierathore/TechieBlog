namespace BlogModels.Interfaces;

/// <summary>
/// Newsletter composition, SMTP dispatch, send history, unsubscribe and public archive (BRD-59,
/// BRD-100, BRD-101).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published contract the admin composer and the public archive pages
/// both consume. It draws a hard line between the admin surface (any issue, any status) and the
/// public surface (sent + public + slugged only), so a draft can never be reached by a reader.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><b>Compose</b> — <see cref="SaveDraftAsync"/> creates or updates a draft.</item>
///   <item><b>Send</b> — <see cref="SendAsync"/> resolves the audience, mails each subscriber with
///         an unsubscribe link, records every attempt, then stamps the issue sent with a unique
///         slug so it becomes a public archive record.</item>
///   <item><b>Audit</b> — <see cref="GetSendHistoryAsync"/> shows who the issue reached.</item>
///   <item><b>Archive</b> — <see cref="GetPublishedIssuesAsync"/>,
///         <see cref="GetPublishedBySlugAsync"/> and <see cref="GetNavigationAsync"/> serve readers.</item>
///   <item><b>Unsubscribe</b> — <see cref="UnsubscribeAsync"/> consumes the token in the link, and
///         <see cref="BuildUnsubscribeUrl"/> builds the link that carries it. Both must stay
///         reachable without a sign-in: the page behind the link is anonymous by design.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>INewsletterRepo</c>, the engine's <c>IEmailService</c>, and
/// <c>SiteSettings:BaseUrl</c> for building unsubscribe and archive links.</para>
///
/// <para><b>Usage:</b> Implemented by <c>BlogEngine.Services.NewsletterSvc</c>, registered transient.</para>
/// </remarks>
public interface INewsletterService
{
    /// <summary>
    /// Creates or updates a newsletter draft.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Title and content are required. An issue that has already been
    /// sent is immutable and the call is rejected.</para>
    /// <para><b>Flow:</b> validate → insert when the id is zero, otherwise update → return the
    /// persisted issue.</para>
    /// <para><b>Side Effects:</b> Writes to the <c>Newsletter</c> table.</para>
    /// </remarks>
    /// <param name="newsletter">The draft to persist.</param>
    /// <returns>The persisted issue, or a failure carrying the validation message.</returns>
    Task<Result<Newsletter>> SaveDraftAsync(Newsletter newsletter);

    /// <summary>
    /// Loads any newsletter issue for the admin surface, regardless of status.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Admin-only read; performs no publication filtering.</para>
    /// <para><b>Flow:</b> validate id → repository read.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <returns>The issue, or a failure when it does not exist.</returns>
    Task<Result<Newsletter>> GetByIdAsync(long newsletterId);

    /// <summary>
    /// Lists every newsletter issue for the admin history screen.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Includes drafts, scheduled and sent issues, newest first.</para>
    /// <para><b>Flow:</b> repository read → return list.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <returns>All issues; empty when none exist.</returns>
    Task<IReadOnlyList<Newsletter>> GetAllAsync();

    /// <summary>
    /// Sends an issue to the chosen audience and publishes it to the archive.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only an unsent issue can be sent, and only when the audience
    /// resolves to at least one subscriber. Every message carries a personal unsubscribe link. A
    /// per-address failure is logged and recorded but does not abort the run. When at least one
    /// message is delivered the issue is stamped sent, given a unique slug and becomes a public
    /// archive record.</para>
    /// <para><b>Flow:</b> load issue → guard status → resolve audience → mail and record each
    /// recipient → stamp sent → return the report.</para>
    /// <para><b>Side Effects:</b> Sends email; writes <c>SubscriberNewsletter</c> rows; updates the
    /// issue.</para>
    /// </remarks>
    /// <param name="newsletterId">The issue to dispatch.</param>
    /// <param name="audience">Who to send to; <c>NewsletterAudience.Everyone</c> for a full send.</param>
    /// <returns>A report of the dispatch, or a failure when the issue could not be sent at all.</returns>
    Task<Result<NewsletterSendReport>> SendAsync(long newsletterId, NewsletterAudience audience);

    /// <summary>
    /// Reads the delivery history for one issue.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One row per targeted subscriber, carrying the send outcome.</para>
    /// <para><b>Flow:</b> validate id → repository read.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <returns>Delivery rows; empty when the issue was never sent.</returns>
    Task<IReadOnlyList<NewsletterRecipient>> GetSendHistoryAsync(long newsletterId);

    /// <summary>
    /// Removes a subscriber using the token from an unsubscribe link, and reports which of the two
    /// success cases applied.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token is the authorisation — an unsubscribe link is followed
    /// from a mail client with no session, so an unguessable per-subscriber token stands in for an
    /// identity and the call must remain reachable anonymously. A token that resolves to a
    /// subscriber who is already opted out succeeds as
    /// <see cref="UnsubscribeOutcome.AlreadyUnsubscribed"/>, which is what makes re-opening the same
    /// link a harmless no-op instead of an error.</para>
    /// <para><b>Flow:</b> validate token → resolve subscriber → branch on the current state →
    /// deactivate → log.</para>
    /// <para><b>Side Effects:</b> Deactivates the subscriber on the
    /// <see cref="UnsubscribeOutcome.Unsubscribed"/> path only; the other paths write nothing.</para>
    /// <para><b>Does not leak whether a token exists.</b> A blank token, an unknown token and an
    /// internal failure all come back as failures carrying the same wording, so the route cannot be
    /// used to test whether a guessed token belongs to a real subscriber.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">Token taken from the link.</param>
    /// <returns>The outcome when the token resolved; a failure carrying a deliberately vague message
    /// when it did not.</returns>
    Task<Result<UnsubscribeOutcome>> UnsubscribeAsync(string unsubscribeToken);

    /// <summary>
    /// Reads one page of the public newsletter archive.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Published issues only — sent, public and slugged — newest send
    /// first. The returned total is produced from the same predicate as the rows, so the archive
    /// count always matches the issues listed.</para>
    /// <para><b>Flow:</b> clamp paging → count and page in the repository → assemble the page.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="pageNumber">One-based page number; values below one are clamped to one.</param>
    /// <param name="pageSize">Rows per page; clamped to 1..100.</param>
    /// <returns>The requested page; empty items when the archive has no issues.</returns>
    Task<NewsletterArchivePage> GetPublishedIssuesAsync(int pageNumber, int pageSize);

    /// <summary>
    /// Resolves a published issue from its public slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only a sent, public, slugged issue resolves; a draft or unsent
    /// issue returns a failure so the public route can render a 404.</para>
    /// <para><b>Flow:</b> validate slug → repository read with the published predicate.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="slug">The issue's public slug.</param>
    /// <returns>The issue, or a failure when it is not publicly reachable.</returns>
    Task<Result<Newsletter>> GetPublishedBySlugAsync(string slug);

    /// <summary>
    /// Resolves the previous and next published issues by send order.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Neighbours are found by send time among published issues only.
    /// The oldest issue has no previous and the newest has no next; both are returned as null
    /// rather than wrapping around.</para>
    /// <para><b>Flow:</b> load the issue → guard that it is published → look up both neighbours.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="newsletterId">The issue whose neighbours are wanted.</param>
    /// <returns>The navigation value; both neighbours are null for an unpublished issue.</returns>
    Task<NewsletterNavigation> GetNavigationAsync(long newsletterId);

    /// <summary>
    /// Counts the issues visible in the public archive.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Uses the same published predicate as the listing.</para>
    /// <para><b>Flow:</b> repository count.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <returns>Number of published issues; zero when the archive is empty.</returns>
    Task<int> GetPublishedCountAsync();

    /// <summary>
    /// Builds the unsubscribe URL for one subscriber.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The URL is <c>{BaseUrl}/unsubscribe/{token}</c>. Every outbound
    /// newsletter message must carry it, which is why it is part of the published contract rather
    /// than a private detail.</para>
    /// <para><b>Flow:</b> guard the token → concatenate with the configured base URL.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The subscriber's unsubscribe token.</param>
    /// <returns>The absolute unsubscribe URL, or an empty string when the token is missing.</returns>
    string BuildUnsubscribeUrl(string unsubscribeToken);
}
