using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for the newsletter subscriber list and its double opt-in state.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the <c>Subscriber</c> table for the subscription lifecycle — sign up,
/// confirm, search, count, unsubscribe. The newsletter's own reads of the same table
/// (<c>INewsletterRepo.GetRecipientsAsync</c>, the unsubscribe-token lookups) live on that contract
/// instead, because they belong to a send rather than to list management.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Sign up — <c>SubscriberSvc</c> calls <see cref="EmailExistsAsync"/>, then the inherited
///         <c>InsertAsync</c> with <c>IsConfirmed</c> false.</item>
///   <item>Confirm — <c>EmailVerificationSvc</c> calls <see cref="UpdateStatusAsync"/> when the opt-in
///         link is followed; only then does the address become mailable.</item>
///   <item>Administer — the subscriber grid uses <see cref="GetByStatusAsync"/>,
///         <see cref="SearchByEmailAsync"/>, <see cref="GetTotalCountAsync"/> and
///         <see cref="GetActiveCountAsync"/>.</item>
///   <item>Send — a dispatch resolves its audience through <c>INewsletterRepo</c>, not through
///         <see cref="GetActiveSubscribersAsync"/>.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.SubscriberRepo</c> over Dapper and
/// PostgreSQL. Consumed by <c>SubscriberSvc</c> and <c>EmailVerificationSvc</c>.</para>
///
/// <para><b>Usage.</b> Email matching is case-insensitive everywhere, so an address retyped in a
/// different case resolves to the same subscriber rather than creating a second one. Every sequence
/// comes back newest-<c>SubscribedOn</c> first and callers must not re-sort;
/// <see cref="SearchByEmailAsync"/> is additionally capped in SQL. <c>SubscriberSvc</c> is the layer
/// that converts expected failures into a <c>Result</c>; this contract has none and throws on any
/// data-access failure.</para>
///
/// <para><b>"Active" is a projection, not a column.</b> <c>Subscriber.IsActive</c> is
/// <c>IsConfirmed</c> under the name the UI binds to, and the column is nullable. This contract's
/// implementation resolves a NULL as <i>active</i> (<c>COALESCE(IsConfirmed, TRUE)</c>) on the general
/// reads, on the reasoning that rows written before double opt-in existed were confirmed by definition
/// — but <see cref="GetActiveSubscribersAsync"/> and <see cref="GetByStatusAsync"/> both test
/// <c>IsConfirmed</c> directly, so a NULL row matches neither filter, and <c>NewsletterRepo</c> resolves
/// the same NULL as <i>inactive</i> and excludes it from a send. A caller must therefore not treat
/// "<c>IsActive</c> is true here" as "will receive the next issue". See REQ-NFR-008 defect notes.</para>
///
/// <para><b>Consent surface (REQ-FN-059):</b> <see cref="UpdateStatusAsync"/> is the legacy
/// mailability flip and is no longer the way to record a consent decision. A withdrawal goes
/// through <see cref="RecordWithdrawalAsync"/> and a re-consent through
/// <see cref="RecordConsentAsync"/>, so the <c>UnsubscribedOn</c> / <c>ConfirmedOn</c> record and
/// the token's burn state move together with the flag in one statement. Every read on this contract
/// projects those columns, so <c>Subscriber.ConsentState</c> is populated on every materialised
/// row.</para>
///
/// <para><b>Per-issue token surface (REQ-FN-060):</b> <see cref="IssueTokenForNewsletterAsync"/>,
/// <see cref="GetByNewsletterTokenAsync"/> and <see cref="RedeemNewsletterTokenAsync"/> address the
/// <c>UnsubscribeToken</c> TABLE (migration 027) — one credential per subscriber per issue — while
/// the REQ-FN-059 members above address the <c>Subscriber.UnsubscribeToken</c> COLUMN, one
/// credential per subscriber. Both are live: the column resolves every link already sitting in a
/// delivered mail, the table scopes every link mailed from now on. A redemption tries the table
/// first and falls back to the column.</para>
///
/// <para><b>Async surface (REQ-NFR-026):</b> every member exists twice — a legacy blocking member and
/// an <c>…Async</c> twin carrying a <see cref="CancellationToken"/>. Call the async member; the
/// blocking ones are retained only until the last caller migrates and are deleted in the final stage.
/// Each async member ships with a default implementation that runs its synchronous twin, so an
/// unconverted implementer — including the in-memory test doubles — keeps compiling untouched. A
/// bridged member is still blocking, so a repository that inherits one is unconverted.</para>
///
/// <para>All eight defaults go through <c>RepoSyncBridge</c>, which preserves task semantics
/// faithfully — a pre-cancelled token yields a cancelled task and a thrown exception yields a faulted
/// task, so a caller that only observes failures through <c>await</c> behaves identically either way.
/// <b>It is still not asynchrony</b>: the operation runs inline on the calling thread, parks it for the
/// whole round trip, and a token cancelled <i>after</i> the call starts has no effect.
/// <c>SubscriberRepo</c> overrides all eight with genuine async Dapper, so a caller resolving this
/// contract from the container gets real asynchrony.</para>
/// </remarks>
public interface ISubscriberRepo : IGenericRepository<Subscriber>
{
    /// <summary>
    /// Gets a subscriber by email address.
    /// </summary>
    /// <param name="email">Email address to search; matched case-insensitively.</param>
    /// <returns>The subscriber whatever its confirmation state, or <c>null</c> when the address is not
    /// on the list. "Not subscribed" is a normal answer and is never an exception.</returns>
    Subscriber? GetByEmail(string email);

    /// <summary>
    /// Checks if an email is already subscribed.
    /// </summary>
    /// <param name="email">Email to check; matched case-insensitively.</param>
    /// <returns><c>true</c> when the address is already on the list, confirmed or not. Advisory only —
    /// it is a read, not a reservation, so two concurrent sign-ups can both see <c>false</c>.</returns>
    bool EmailExists(string email);

    /// <summary>
    /// Gets all active subscribers.
    /// </summary>
    /// <returns>Subscribers whose <c>IsConfirmed</c> is explicitly true, newest <c>SubscribedOn</c>
    /// first; an empty sequence — never <c>null</c> — when none are confirmed. Rows with a NULL
    /// <c>IsConfirmed</c> are excluded even though the general reads report them as active.</returns>
    IEnumerable<Subscriber> GetActiveSubscribers();

    /// <summary>
    /// Gets subscribers filtered by active status.
    /// </summary>
    /// <param name="isActive">Active status filter, compared against <c>IsConfirmed</c> directly.</param>
    /// <returns>The matching subscribers, newest <c>SubscribedOn</c> first; an empty sequence when none
    /// match. A row with a NULL <c>IsConfirmed</c> matches <i>neither</i> value, so the two calls do not
    /// partition the list.</returns>
    IEnumerable<Subscriber> GetByStatus(bool isActive);

    /// <summary>
    /// Searches subscribers by email.
    /// </summary>
    /// <param name="query">Search query, matched as a case-insensitive <c>ILIKE</c> pattern — the
    /// caller supplies its own wildcards.</param>
    /// <returns>At most fifty matching subscribers, newest <c>SubscribedOn</c> first; an empty sequence
    /// when nothing matches. The cap is applied in SQL, so a broad query cannot pull the whole
    /// table — and cannot be paged past.</returns>
    IEnumerable<Subscriber> SearchByEmail(string query);

    /// <summary>
    /// Updates subscriber active status.
    /// </summary>
    /// <param name="subscriberId">Subscriber ID. An unknown identifier affects no rows and is a no-op,
    /// not an error.</param>
    /// <param name="isActive">New status, written to <c>IsConfirmed</c>. Idempotent: this is both the
    /// flip that completes a double opt-in and the flip an unsubscribe reverses.</param>
    void UpdateStatus(long subscriberId, bool isActive);

    /// <summary>
    /// Records that consent was given, and re-issues the unsubscribe link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The synchronous twin of <see cref="RecordConsentAsync"/>, and it
    /// exists for one reason: without it the two <c>SubscriberSvc.Subscribe</c> overloads could not
    /// agree. The async path re-consented through <c>RecordConsentAsync</c> — stamping
    /// <c>ConfirmedOn</c> and handing out a fresh token — while the synchronous path did a bare
    /// <see cref="UpdateStatus"/>, so the same reactivation wrote a <b>different consent record</b>
    /// depending on which overload the caller happened to reach. That is precisely the ambiguity
    /// REQ-FN-059 exists to remove, so the fix is to give the blocking path the same write rather
    /// than to block on the asynchronous one (which the conversion contract bans outright).</para>
    /// <para><b>Flow:</b> generate a token in the caller → single UPDATE stamping
    /// <c>ConfirmedOn</c>, the new token, its issuance and a cleared burn.</para>
    /// <para><b>Side Effects:</b> Updates one row and invalidates the previous token.</para>
    /// <para><b>Default implementation:</b> falls back to <see cref="UpdateStatus"/> with
    /// <c>true</c>, discarding the supplied token — exactly what the async member's own default
    /// does, so an unconverted implementer keeps its previous behaviour on both paths. Against the
    /// real database the trigger rotates the token itself, so even the fallback records the
    /// consent.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber giving consent.</param>
    /// <param name="newUnsubscribeToken">The freshly generated token to install.</param>
    /// <returns><c>true</c> when a row was changed; <c>false</c> when the identifier is unknown.</returns>
    bool RecordConsent(long subscriberId, string newUnsubscribeToken)
    {
        UpdateStatus(subscriberId, true);
        return true;
    }

    /// <summary>
    /// Gets total subscriber count.
    /// </summary>
    /// <returns>Every subscriber row, confirmed or not — the "how many people have ever asked" figure,
    /// not the mailable audience.</returns>
    int GetTotalCount();

    /// <summary>
    /// Gets active subscriber count.
    /// </summary>
    /// <returns>The number of rows whose <c>IsConfirmed</c> is explicitly true. NULL rows are not
    /// counted, so this can be lower than the number the admin grid shows as active.</returns>
    int GetActiveCount();

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets a subscriber by email address without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Matching is case-insensitive, so a reader who typed their address
    /// in a different case is recognised as the same subscriber rather than added twice.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query by lowered address → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">Email address to search; matched case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited bridged default observes it
    /// only before the call starts — see the type remarks.</param>
    /// <returns>The subscriber whatever its confirmation state, or <c>null</c> when the address is not
    /// on the list.</returns>
    Task<Subscriber?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetByEmail(email), cancellationToken);

    /// <summary>
    /// Checks whether an email is already subscribed, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A counting query rather than a row read, because the caller only
    /// needs to know whether the address is present.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">Email to check; matched case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited bridged default observes it
    /// only before the call starts.</param>
    /// <returns><c>true</c> when the address is already on the list, confirmed or not. Advisory only —
    /// it is a read, not a reservation.</returns>
    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => EmailExists(email), cancellationToken);

    /// <summary>
    /// Gets all active (confirmed) subscribers without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only confirmed addresses are returned — an unconfirmed
    /// subscription has not completed double opt-in and must never receive an issue.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query. The inherited bridged default observes it
    /// only before the call starts.</param>
    /// <returns>Subscribers whose <c>IsConfirmed</c> is explicitly true, newest <c>SubscribedOn</c>
    /// first; an empty sequence — never <c>null</c> — when none are confirmed. Rows with a NULL
    /// <c>IsConfirmed</c> are excluded.</returns>
    Task<IEnumerable<Subscriber>> GetActiveSubscribersAsync(CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(GetActiveSubscribers, cancellationToken);

    /// <summary>
    /// Gets subscribers filtered by active status, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Filtering happens in SQL so the administration grid never pulls
    /// the whole list across the wire to discard half of it.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="isActive">Active status filter, compared against <c>IsConfirmed</c> directly.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited bridged default observes it
    /// only before the call starts.</param>
    /// <returns>The matching subscribers, newest <c>SubscribedOn</c> first; an empty sequence when none
    /// match. A NULL <c>IsConfirmed</c> matches neither value, so the two calls do not partition the
    /// list.</returns>
    Task<IEnumerable<Subscriber>> GetByStatusAsync(bool isActive, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => GetByStatus(isActive), cancellationToken);

    /// <summary>
    /// Searches subscribers by email without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A capped substring search — the limit is applied in SQL so a
    /// one-character query cannot return the entire list.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → ILIKE query with a LIMIT → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">Search query, matched as a case-insensitive <c>ILIKE</c> pattern — the
    /// caller supplies its own wildcards.</param>
    /// <param name="cancellationToken">Cancels the query. The inherited bridged default observes it
    /// only before the call starts.</param>
    /// <returns>At most fifty matching subscribers, newest <c>SubscribedOn</c> first; an empty sequence
    /// when nothing matches. The cap cannot be paged past.</returns>
    Task<IEnumerable<Subscriber>> SearchByEmailAsync(string query, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => SearchByEmail(query), cancellationToken);

    /// <summary>
    /// Updates a subscriber's active status without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the flip that completes a double opt-in subscription and
    /// the same flip that an unsubscribe reverses, so it is deliberately idempotent.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row; a no-op when the id is unknown.</para>
    /// </remarks>
    /// <param name="subscriberId">Subscriber ID. An unknown identifier is a no-op.</param>
    /// <param name="isActive">New status, written to <c>IsConfirmed</c>.</param>
    /// <param name="cancellationToken">Cancels the update. The inherited bridged default observes it
    /// only before the call starts.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so a caller
    /// cannot tell a successful update from a no-op on an unknown identifier.</returns>
    Task UpdateStatusAsync(long subscriberId, bool isActive, CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(() => UpdateStatus(subscriberId, isActive), cancellationToken);

    /// <summary>
    /// Gets the total subscriber count without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts every row, confirmed or not — this is the "how many people
    /// have ever asked" figure, not the mailable audience.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → COUNT → scalar.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query. The inherited bridged default observes it
    /// only before the call starts.</param>
    /// <returns>Every subscriber row, confirmed or not — not the mailable audience.</returns>
    Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(GetTotalCount, cancellationToken);

    /// <summary>
    /// Gets the active subscriber count without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts confirmed rows only — this is the mailable audience.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered COUNT → scalar.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query. The inherited bridged default observes it
    /// only before the call starts.</param>
    /// <returns>The number of rows whose <c>IsConfirmed</c> is explicitly true. NULL rows are not
    /// counted, so this can be lower than the number the admin grid shows as active.</returns>
    Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
        => RepoSyncBridge.Run(GetActiveCount, cancellationToken);

    // ---------------------------------------------------------------------------------------------
    // Consent record and unsubscribe-token lifecycle — REQ-FN-059.
    //
    // These four members have NO synchronous twin: they were added after the async conversion, so
    // there is no blocking caller to keep compiling. Each carries a DEFAULT implementation that is
    // safe rather than clever, because the in-memory test doubles across tests/unit implement this
    // interface and must keep compiling untouched. Every default either fails closed or reproduces
    // the pre-REQ-FN-059 behaviour exactly; none of them invents a consent record.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves the subscriber holding an unsubscribe token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token is the authorisation for the anonymous
    /// <c>/unsubscribe/{token}</c> page, so the lookup is exact and case-SENSITIVE — unlike the
    /// email lookups, which are deliberately case-insensitive. A credential must not match more
    /// values than it was issued as.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query by token → first row or
    /// <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// <para><b>Default implementation:</b> returns <c>null</c>, i.e. "this implementation has no
    /// token index, so no token resolves". Failing closed means an unconverted implementer reports
    /// an invalid link rather than silently opting the wrong address out.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The opaque token taken from the unsubscribe URL.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber carrying that exact token, or <c>null</c> when none does. The
    /// projection includes the consent columns and the token lifecycle columns, which the general
    /// reads on this contract also carry.</returns>
    Task<Subscriber?> GetByUnsubscribeTokenAsync(
        string unsubscribeToken, CancellationToken cancellationToken = default)
        => Task.FromResult<Subscriber?>(null);

    /// <summary>
    /// Records a withdrawal of consent and burns the unsubscribe token, in one statement.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The write that REQ-FN-059 exists for. It sets <c>IsConfirmed</c>
    /// false (so the address stops being mailed), stamps <c>UnsubscribedOn</c> (so the withdrawal is
    /// RECORDED rather than the consent being erased) and stamps <c>UnsubscribeTokenUsedOn</c> (so
    /// the link cannot be replayed). <c>ConfirmedOn</c> is left alone, which is what preserves the
    /// proof that the address once opted in.</para>
    /// <para><b>Flow:</b> single guarded UPDATE → report whether it changed a row.</para>
    /// <para><b>Side Effects:</b> Updates at most one row. The statement is guarded on the token
    /// still being unburned, so two concurrent redemptions of the same link cannot both succeed.</para>
    /// <para><b>Default implementation:</b> falls back to <see cref="UpdateStatusAsync"/> with
    /// <c>false</c> — exactly the pre-REQ-FN-059 behaviour — and reports success. Against the real
    /// database the trigger <c>TrgSubscriberConsentChange</c> still stamps <c>UnsubscribedOn</c>, so
    /// even the fallback records the withdrawal.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber withdrawing consent.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was changed; <c>false</c> when the identifier is unknown or
    /// the token had already been burned by a concurrent redemption.</returns>
    Task<bool> RecordWithdrawalAsync(long subscriberId, CancellationToken cancellationToken = default)
        => RecordWithdrawalFallbackAsync(subscriberId, cancellationToken);

    /// <summary>
    /// Records that consent was given, and re-issues the unsubscribe link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The mirror of <see cref="RecordWithdrawalAsync"/>. Setting
    /// <c>IsConfirmed</c> true is not enough on its own: a subscriber who comes back must also be
    /// handed a fresh, unburned token, or the link in their next issue would be one this contract
    /// has already refused once.</para>
    /// <para><b>Flow:</b> generate a token in the caller → single UPDATE stamping
    /// <c>ConfirmedOn</c>, the new token, its issuance and a cleared burn.</para>
    /// <para><b>Side Effects:</b> Updates one row and invalidates the previous token.</para>
    /// <para><b>Default implementation:</b> falls back to <see cref="UpdateStatusAsync"/> with
    /// <c>true</c>, discarding the supplied token. Against the real database the trigger rotates the
    /// token itself, so the fallback is still correct — just less explicit.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber giving consent.</param>
    /// <param name="newUnsubscribeToken">The freshly generated token to install.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was changed; <c>false</c> when the identifier is unknown.</returns>
    Task<bool> RecordConsentAsync(
        long subscriberId, string newUnsubscribeToken, CancellationToken cancellationToken = default)
        => RecordConsentFallbackAsync(subscriberId, cancellationToken);

    /// <summary>
    /// Replaces a subscriber's unsubscribe token without touching their consent state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Send-time rotation. Issuing a fresh token per mailing is what
    /// turns the token from a permanent credential into a scoped one: the link in an old issue stops
    /// working as soon as a newer issue goes out, and the 400-day expiry clock restarts, so a live
    /// subscriber's link can never age out.</para>
    /// <para><b>Flow:</b> single UPDATE installing the token, stamping its issuance and clearing any
    /// burn.</para>
    /// <para><b>Side Effects:</b> Invalidates the subscriber's previous unsubscribe link. Consent
    /// columns are untouched.</para>
    /// <para><b>Default implementation:</b> returns <c>false</c> — "rotation is not supported here".
    /// A caller must treat that as "keep using the existing token", never as a failure to mail.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber whose link is being re-issued.</param>
    /// <param name="newUnsubscribeToken">The freshly generated token to install.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when the token was installed; <c>false</c> when the identifier is
    /// unknown or the implementation does not support rotation.</returns>
    Task<bool> RotateUnsubscribeTokenAsync(
        long subscriberId, string newUnsubscribeToken, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    // ---------------------------------------------------------------------------------------------
    // Per-issue unsubscribe tokens — REQ-FN-060.
    //
    // These three members address the UnsubscribeToken TABLE (migration 027), not the
    // Subscriber.UnsubscribeToken COLUMN the four members above address. The column holds one
    // credential per subscriber; the table holds one credential per (subscriber, issue), which is
    // what makes a mailed link scoped to the send it travelled in.
    //
    // The two live side by side on purpose and neither replaces the other: every unsubscribe link
    // mailed before REQ-FN-060 carries a column token that must keep resolving, so the redemption
    // path tries the table first and falls back to the column.
    //
    // Each carries a DEFAULT that fails closed, for the same reason the REQ-FN-059 block above gives
    // — the in-memory doubles across tests/unit implement this interface and must keep compiling.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Issues one unsubscribe token scoped to a single newsletter issue.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-060] Called once per recipient per send, immediately
    /// before the message is composed, so the credential in that message authorises nothing beyond
    /// that one issue. It is an INSERT, never an update: the subscriber's other tokens — the ones in
    /// issues already delivered — are deliberately left working, because refusing a genuine opt-out
    /// clicked from last week's mail is a worse defect than the over-broad credential this
    /// requirement narrows.</para>
    /// <para><b>Flow:</b> generate a token in the caller → single INSERT stamping the issuance.</para>
    /// <para><b>Side Effects:</b> Adds one <c>UnsubscribeToken</c> row. Nothing on
    /// <c>Subscriber</c> is touched — issuing a link is not a consent decision, and the row-level
    /// token stays exactly as it was.</para>
    /// <para><b>Default implementation:</b> returns <c>false</c>, i.e. "per-issue tokens are not
    /// supported here". A caller must read that as "fall back to the row-level token", never as a
    /// reason to mail a message with no unsubscribe link.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber the issue is addressed to.</param>
    /// <param name="newsletterId">The issue the token is scoped to.</param>
    /// <param name="unsubscribeToken">The freshly generated token to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns><c>true</c> when the token was recorded; <c>false</c> when the implementation does
    /// not support per-issue tokens.</returns>
    Task<bool> IssueTokenForNewsletterAsync(
        long subscriberId, long newsletterId, string unsubscribeToken,
        CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <summary>
    /// Resolves the subscriber holding a per-issue unsubscribe token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-060] The first half of the anonymous
    /// <c>/unsubscribe/{token}</c> lookup; <see cref="GetByUnsubscribeTokenAsync"/> is the fallback
    /// half. The comparison is exact and case-SENSITIVE, for the same reason that one is: a
    /// credential must not match more values than it was issued as.</para>
    /// <para><b>The returned <see cref="Subscriber"/> describes the TOKEN's lifecycle, not the
    /// row's.</b> <c>UnsubscribeToken</c>, <c>UnsubscribeTokenIssuedOn</c> and
    /// <c>UnsubscribeTokenUsedOn</c> are projected from the matched <c>UnsubscribeToken</c> row
    /// rather than from the <c>Subscriber</c> row, so every rule REQ-FN-059 already expresses over
    /// those three properties — burned, expired — applies to the per-issue token unchanged and
    /// there is a single implementation of each. The consent columns are the subscriber's own. The
    /// entity is therefore READ-ONLY: never hand it to an update path, or the row-level token will
    /// be overwritten with a per-issue one.</para>
    /// <para><b>Flow:</b> join the token table to its subscriber → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// <para><b>Default implementation:</b> returns <c>null</c> — "no per-issue token resolves
    /// here" — so an unconverted implementer falls through to the row-level lookup rather than
    /// opting the wrong address out.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The opaque token taken from the unsubscribe URL.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber holding that per-issue token, carrying that token's issuance and burn
    /// stamps, or <c>null</c> when no per-issue token matches.</returns>
    Task<Subscriber?> GetByNewsletterTokenAsync(
        string unsubscribeToken, CancellationToken cancellationToken = default)
        => Task.FromResult<Subscriber?>(null);

    /// <summary>
    /// Burns a per-issue token and records the subscriber's withdrawal, in one statement.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-FN-060] The per-issue counterpart of
    /// <see cref="RecordWithdrawalAsync"/>. Burning the token and recording the withdrawal must not
    /// be two statements — a crash between them would leave a spent link that never removed anybody,
    /// or an address off the list with no record of why — so both happen in one guarded statement.
    /// The subscriber's OTHER per-issue tokens are left untouched and stay resolvable; they will
    /// report "already unsubscribed" rather than acting again, because the subscriber is withdrawn
    /// by then.</para>
    /// <para><b>Flow:</b> guarded UPDATE of the token row → cascade into the subscriber row →
    /// report whether the withdrawal was recorded.</para>
    /// <para><b>Side Effects:</b> Stamps one token row's burn and updates one subscriber row. The
    /// guard on the token still being unburned makes the redemption atomic: if two requests carry
    /// the same link, exactly one gets <c>true</c>.</para>
    /// <para><b>Default implementation:</b> returns <c>false</c> — an implementation with no token
    /// table cannot have resolved a per-issue token in the first place, so this is unreachable
    /// there.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The per-issue token being redeemed.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when the withdrawal was recorded; <c>false</c> when the token was
    /// unknown or a concurrent redemption had already burned it.</returns>
    Task<bool> RedeemNewsletterTokenAsync(
        string unsubscribeToken, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    /// <summary>
    /// Runs the legacy status flip that <see cref="RecordWithdrawalAsync"/> falls back to.
    /// </summary>
    /// <remarks>
    /// A separate private helper because a default interface member cannot contain an <c>await</c>
    /// without one — the body has to be a real async method somewhere.
    /// </remarks>
    /// <param name="subscriberId">The subscriber withdrawing consent.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c>, once the legacy update has run.</returns>
    private async Task<bool> RecordWithdrawalFallbackAsync(long subscriberId, CancellationToken cancellationToken)
    {
        await UpdateStatusAsync(subscriberId, false, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Runs the legacy status flip that <see cref="RecordConsentAsync"/> falls back to.
    /// </summary>
    /// <param name="subscriberId">The subscriber giving consent.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c>, once the legacy update has run.</returns>
    private async Task<bool> RecordConsentFallbackAsync(long subscriberId, CancellationToken cancellationToken)
    {
        await UpdateStatusAsync(subscriberId, true, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
