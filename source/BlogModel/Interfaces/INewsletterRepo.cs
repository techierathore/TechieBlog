namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for newsletter issues, their send history and unsubscribe tokens.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Isolates every SQL statement the newsletter feature needs — composition,
/// dispatch bookkeeping, the public archive and unsubscribe resolution — behind one interface so
/// <c>NewsletterSvc</c> holds business rules only.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Compose — <see cref="InsertToGetIdAsync"/> / <see cref="UpdateAsync"/> persist a draft.</item>
///   <item>Send — <see cref="GetRecipientsAsync"/> resolves the audience,
///         <see cref="InsertRecipientAsync"/> records each attempt,
///         <see cref="MarkSentAsync"/> stamps the issue as published.</item>
///   <item>Archive — <see cref="GetPublishedPageAsync"/>, <see cref="GetPublishedCountAsync"/>,
///         <see cref="GetPublishedBySlugAsync"/> and the neighbour lookups serve readers.</item>
///   <item>Unsubscribe — <see cref="GetSubscriberByUnsubscribeTokenAsync"/> resolves a token and
///         <see cref="DeactivateSubscriberAsync"/> removes the subscriber.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.NewsletterRepo</c> over Dapper
/// and PostgreSQL.</para>
///
/// <para><b>Usage:</b> Every published-issue query must filter on sent + public + slugged, so a
/// draft can never leak through the archive surface.</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> this contract was already task-returning, but the
/// members neither carried a <see cref="CancellationToken"/> nor were genuinely asynchronous —
/// the implementation opened its connection with the blocking factory, which parks a thread-pool
/// thread for the whole TCP, TLS and authentication handshake. The token was therefore added to
/// every member <i>in place</i> rather than as a second overload: a <c>ct</c>-defaulted overload
/// placed beside an existing member is a source-compatibility trap, and the resulting errors
/// surface at the call sites rather than here.</para>
///
/// <para><b>Why <see cref="GetAllAsync"/> returns <c>IEnumerable</c> rather than
/// <c>IReadOnlyList</c>.</b> <c>NewsletterRepo</c> implements this interface <i>and</i>
/// <c>IGenericRepository&lt;Newsletter&gt;</c>, whose own <c>GetAllAsync(CancellationToken)</c>
/// returns <c>Task&lt;IEnumerable&lt;Newsletter&gt;&gt;</c>. Before the token was added the two
/// members differed in arity — <c>GetAllAsync()</c> versus <c>GetAllAsync(CancellationToken)</c> —
/// so they coexisted. Adding the token collapsed them onto one signature that differs only by
/// return type, which C# forbids on a single class (CS0738). Three resolutions were considered:
/// hiding the generic member with <c>new</c>, implementing this one explicitly, or aligning the
/// return type. Aligning wins because the other two both leave the class carrying <i>two</i> "read
/// everything" members that execute the same SQL — a duplicate a caller can pick the wrong half of,
/// and one the final stage of the conversion would have to unpick anyway. The list-ness was never
/// load-bearing here: the repository buffers before the connection closes either way (results are
/// never lazily streamed, see <c>docs/async-conversion-pattern.md</c> trap 3), and the sole caller,
/// <c>NewsletterSvc.GetAllAsync</c>, materialises the sequence itself because <i>its</i> published
/// contract promises <c>IReadOnlyList</c>. The <c>IReadOnlyList</c> returns on the members that have
/// no generic counterpart — the recipient, send-history and archive reads — are deliberately kept,
/// because callers index and count them.</para>
/// </remarks>
public interface INewsletterRepo
{
    /// <summary>
    /// Loads a single newsletter issue in any status, for the admin surface.
    /// </summary>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The issue, or null when no such issue exists.</returns>
    Task<Newsletter?> GetByIdAsync(long newsletterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads every newsletter issue, newest created first, for the admin history list.
    /// </summary>
    /// <remarks>
    /// The sequence is fully materialised before the connection closes, so it is safe to enumerate
    /// more than once. It is typed <c>IEnumerable</c> rather than <c>IReadOnlyList</c> so that one
    /// member on <c>NewsletterRepo</c> satisfies both this contract and
    /// <c>IGenericRepository&lt;Newsletter&gt;</c> — see the type-level remarks for why.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All issues in any status; empty when none exist.</returns>
    Task<IEnumerable<Newsletter>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts a new issue and returns its generated identifier.
    /// </summary>
    /// <param name="newsletter">The issue to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>NewsletterId</c>.</returns>
    Task<long> InsertToGetIdAsync(Newsletter newsletter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the editable fields of an existing issue.
    /// </summary>
    /// <param name="newsletter">The issue carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the update has run.</returns>
    Task UpdateAsync(Newsletter newsletter, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a slug is already taken by another issue.
    /// </summary>
    /// <param name="slug">Candidate slug.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>True when the slug is in use.</returns>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps an issue as sent: send time, recipient count, slug and public flag.
    /// </summary>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <param name="slug">The unique slug the issue is published under.</param>
    /// <param name="sentOn">Dispatch timestamp.</param>
    /// <param name="recipientCount">Number of subscribers successfully reached.</param>
    /// <param name="isPublic">Whether the issue joins the public archive.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the issue has been stamped.</returns>
    Task MarkSentAsync(
        long newsletterId,
        string slug,
        DateTime sentOn,
        int recipientCount,
        bool isPublic,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the subscribers a send should target.
    /// </summary>
    /// <param name="audience">Audience filter; <c>NewsletterAudience.Everyone</c> for a full send.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Matching subscribers, each carrying its unsubscribe token.</returns>
    Task<IReadOnlyList<Subscriber>> GetRecipientsAsync(
        NewsletterAudience audience,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one delivery attempt in the send history.
    /// </summary>
    /// <param name="recipient">The attempt outcome to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    Task InsertRecipientAsync(NewsletterRecipient recipient, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the full send history for one issue.
    /// </summary>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Delivery rows, most recent first; empty when the issue was never sent.</returns>
    Task<IReadOnlyList<NewsletterRecipient>> GetSendHistoryAsync(
        long newsletterId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one page of the public archive, newest send first.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published issues only.</returns>
    Task<IReadOnlyList<Newsletter>> GetPublishedPageAsync(
        int pageSize,
        int offSet,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the issues visible in the public archive.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Number of published issues.</returns>
    Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a published issue from its public slug.
    /// </summary>
    /// <param name="slug">The issue's slug.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The issue, or null when the slug is unknown, a draft or not public.</returns>
    Task<Newsletter?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the published issue sent immediately before the given time.
    /// </summary>
    /// <param name="sentOn">The current issue's send time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The older neighbour, or null at the oldest end of the archive.</returns>
    Task<Newsletter?> GetPreviousPublishedAsync(DateTime sentOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the published issue sent immediately after the given time.
    /// </summary>
    /// <param name="sentOn">The current issue's send time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The newer neighbour, or null at the newest end of the archive.</returns>
    Task<Newsletter?> GetNextPublishedAsync(DateTime sentOn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fills in an unsubscribe token for any subscriber still missing one.
    /// </summary>
    /// <remarks>Defensive repair called before a send, so no message can ever go out without a
    /// working unsubscribe link even if a row was inserted before migration 015 attached the
    /// column default.</remarks>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Number of subscribers repaired.</returns>
    Task<int> EnsureUnsubscribeTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the subscriber owning an unsubscribe token.
    /// </summary>
    /// <param name="unsubscribeToken">Token taken from an unsubscribe link.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber, or null when the token is unknown.</returns>
    Task<Subscriber?> GetSubscriberByUnsubscribeTokenAsync(
        string unsubscribeToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a subscriber as no longer receiving mail.
    /// </summary>
    /// <param name="subscriberId">Subscriber identifier.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the subscriber has been deactivated.</returns>
    Task DeactivateSubscriberAsync(long subscriberId, CancellationToken cancellationToken = default);
}
