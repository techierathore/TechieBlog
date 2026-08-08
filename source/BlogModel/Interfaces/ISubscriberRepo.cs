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
}
