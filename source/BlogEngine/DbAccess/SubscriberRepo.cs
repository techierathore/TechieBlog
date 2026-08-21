namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing subscriber data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for Subscriber entities using Dapper, plus the
/// address lookups and counters the newsletter surfaces depend on.</para>
///
/// <para><b>Code Flow:</b> <c>SubscriberSvc</c> and <c>EmailVerificationSvc</c> inject this
/// repository, call an <c>…Async</c> member, and the member routes through the protected helpers on
/// <see cref="GenericRepository{TEntity}"/>, which open the connection asynchronously and flow the
/// cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> <see cref="GenericRepository{TEntity}"/>, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage. Both twins execute the
/// same SQL constant, so the two cannot drift apart.</para>
///
/// <para><b>Address matching:</b> every lookup compares on <c>LOWER(Email)</c>, so a reader who
/// retypes their address in a different case is recognised as the same subscriber rather than added
/// a second time.</para>
/// </remarks>
public class SubscriberRepo : GenericRepository<Subscriber>, ISubscriberRepo
{
    /// <summary>
    /// The consent-record and unsubscribe-token columns added by REQ-FN-059, shared by every read.
    /// </summary>
    /// <remarks>
    /// <para>Two reasons every read carries these. First, <c>Subscriber.ConsentState</c> is derived
    /// from <c>ConfirmedOn</c>, <c>UnsubscribedOn</c> and <c>IsConsentUnknown</c>, so a read that
    /// omitted them would report every row as Pending — the exact conflation this requirement
    /// removes.</para>
    /// <para>Second, <c>UnsubscribeToken</c> is projected here even though no read needed it before,
    /// because <c>RecordConsentSql</c> and <c>RotateUnsubscribeTokenSql</c> WRITE that column. A
    /// column that a write path touches but the loading read does not project is this project's
    /// most-repeated defect (REQ-FN-053: eight known instances, two of which shipped) — the entity
    /// comes back holding a default, and the next save writes the default over the real value. On a
    /// consent record that would be silent data loss of exactly the kind REQ-FN-059 exists to
    /// prevent, so the projection is widened rather than the gate narrowed. <c>COALESCE</c> because
    /// the column is nullable and the model property is not.</para>
    /// </remarks>
    private const string ConsentAndTokenColumns = @"
                   ConfirmedOn, UnsubscribedOn, IsConsentUnknown,
                   UnsubscribeTokenIssuedOn, UnsubscribeTokenUsedOn,
                   COALESCE(UnsubscribeToken, '') AS UnsubscribeToken";

    /// <summary>
    /// The projection shared by every subscriber read.
    /// </summary>
    /// <remarks>
    /// <c>IsActive</c> is not a column — it is <c>IsConfirmed</c> under the name the UI binds to.
    /// The <c>COALESCE</c> treats a legacy NULL as active, because rows written before double
    /// opt-in existed were confirmed by definition.
    /// </remarks>
    private const string SubscriberColumns = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   COALESCE(IsConfirmed, TRUE) as IsActive,
" + ConsentAndTokenColumns + @"
            FROM Subscriber";

    private const string SelectAllSql = SubscriberColumns + @"
            ORDER BY SubscribedOn DESC";

    private const string SelectByIdSql = SubscriberColumns + @"
            WHERE SubscriberId = @SubscriberId";

    private const string SelectByEmailSql = SubscriberColumns + @"
            WHERE LOWER(Email) = LOWER(@Email)";

    private const string CountByEmailSql = @"
            SELECT COUNT(1) FROM Subscriber
            WHERE LOWER(Email) = LOWER(@Email)";

    private const string SelectActiveSql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   TRUE as IsActive,
" + ConsentAndTokenColumns + @"
            FROM Subscriber
            WHERE IsConfirmed = TRUE
            ORDER BY SubscribedOn DESC";

    private const string SelectByStatusSql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive,
" + ConsentAndTokenColumns + @"
            FROM Subscriber
            WHERE IsConfirmed = @IsActive
            ORDER BY SubscribedOn DESC";

    private const string SearchByEmailSql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive,
" + ConsentAndTokenColumns + @"
            FROM Subscriber
            WHERE Email ILIKE @Query
            ORDER BY SubscribedOn DESC
            LIMIT 50";

    private const string SelectPagedSql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive,
" + ConsentAndTokenColumns + @"
            FROM Subscriber
            ORDER BY SubscribedOn DESC
            LIMIT @PageSize OFFSET @Offset";

    /// <summary>
    /// Resolves the holder of an unsubscribe token. Exact, case-sensitive comparison — a credential
    /// must not match more values than it was issued as.
    /// </summary>
    private const string SelectByUnsubscribeTokenSql = SubscriberColumns + @"
            WHERE UnsubscribeToken = @UnsubscribeToken";

    /// <summary>
    /// Records a withdrawal: stops the mail, stamps WHEN consent was withdrawn and burns the token,
    /// all in one statement. <c>ConfirmedOn</c> is deliberately untouched — that is the proof of
    /// consent this requirement exists to stop erasing. Guarded on the token still being unburned so
    /// two concurrent redemptions of the same link cannot both report success.
    /// </summary>
    private const string RecordWithdrawalSql = @"
            UPDATE Subscriber SET
                IsConfirmed = FALSE,
                UnsubscribedOn = @UnsubscribedOn,
                UnsubscribeTokenUsedOn = @UnsubscribedOn
            WHERE SubscriberId = @SubscriberId
              AND UnsubscribeTokenUsedOn IS NULL";

    /// <summary>
    /// Records a re-consent and hands the subscriber a fresh, unburned link in the same statement,
    /// so a returning subscriber never holds a token this repository has already refused.
    /// </summary>
    private const string RecordConsentSql = @"
            UPDATE Subscriber SET
                IsConfirmed = TRUE,
                ConfirmedOn = @ConfirmedOn,
                UnsubscribeToken = @UnsubscribeToken,
                UnsubscribeTokenIssuedOn = @ConfirmedOn,
                UnsubscribeTokenUsedOn = NULL
            WHERE SubscriberId = @SubscriberId";

    /// <summary>
    /// Send-time rotation: a new token, a restarted expiry clock and a cleared burn, with the
    /// consent columns untouched.
    /// </summary>
    private const string RotateUnsubscribeTokenSql = @"
            UPDATE Subscriber SET
                UnsubscribeToken = @UnsubscribeToken,
                UnsubscribeTokenIssuedOn = @IssuedOn,
                UnsubscribeTokenUsedOn = NULL
            WHERE SubscriberId = @SubscriberId";

    /// <summary>
    /// Records one per-issue unsubscribe token. [REQ-FN-060]
    /// </summary>
    /// <remarks>
    /// An INSERT, deliberately — never an UPSERT and never an UPDATE of an earlier row. Each send
    /// adds a credential rather than replacing one, which is exactly what keeps the unsubscribe link
    /// in an already-delivered issue working after a newer issue goes out.
    /// </remarks>
    private const string InsertNewsletterTokenSql = @"
            INSERT INTO UnsubscribeToken (SubscriberId, NewsletterId, Token, IssuedOn)
            VALUES (@SubscriberId, @NewsletterId, @Token, @IssuedOn)";

    /// <summary>
    /// Resolves the holder of a PER-ISSUE unsubscribe token. [REQ-FN-060]
    /// </summary>
    /// <remarks>
    /// <para>The three token columns are projected from the matched <c>UnsubscribeToken</c> row, not
    /// from <c>Subscriber</c>, and are aliased onto the model's existing token properties. That is
    /// the whole trick that lets REQ-FN-059's burn and expiry rules — which are written against
    /// <c>Subscriber.UnsubscribeTokenUsedOn</c> and <c>UnsubscribeTokenIssuedOn</c> — govern a
    /// per-issue token with no second implementation to keep in step.</para>
    /// <para>The consent columns are the SUBSCRIBER's own, which is what
    /// <c>SubscriberSvc</c> compares <c>IssuedOn</c> against to detect a token superseded by a later
    /// re-consent.</para>
    /// <para><b>The entity this returns must never be passed to a write path</b> — its
    /// <c>UnsubscribeToken</c> is the per-issue value, and any update that wrote it back would
    /// overwrite the subscriber's row-level token.</para>
    /// </remarks>
    private const string SelectByNewsletterTokenSql = @"
            SELECT s.SubscriberId, s.Email, s.Name, s.SubscribedOn, s.IsConfirmed, s.Preferences,
                   COALESCE(s.IsConfirmed, TRUE) AS IsActive,
                   s.ConfirmedOn, s.UnsubscribedOn, s.IsConsentUnknown,
                   t.IssuedOn AS UnsubscribeTokenIssuedOn,
                   t.UsedOn AS UnsubscribeTokenUsedOn,
                   t.Token AS UnsubscribeToken
            FROM UnsubscribeToken t
            INNER JOIN Subscriber s ON s.SubscriberId = t.SubscriberId
            WHERE t.Token = @Token";

    /// <summary>
    /// Burns a per-issue token and records the withdrawal it authorises, in ONE statement.
    /// [REQ-FN-060]
    /// </summary>
    /// <remarks>
    /// <para>The CTE is what makes it one statement rather than two. Burning the token and taking
    /// the address off the list have to commit together: a crash between two statements would leave
    /// either a spent link that removed nobody, or an address off the list with no record of why —
    /// and the second is precisely the erasure REQ-FN-059 exists to prevent.</para>
    /// <para><c>WHERE UsedOn IS NULL</c> makes the redemption atomic, so two concurrent opens of the
    /// same link cannot both report success. <c>ConfirmedOn</c> is untouched — proof of consent
    /// survives the withdrawal — and the subscriber's OTHER token rows are untouched too, so an
    /// unopened link from a different issue still resolves and reports "already unsubscribed".</para>
    /// </remarks>
    private const string RedeemNewsletterTokenSql = @"
            WITH Burned AS (
                UPDATE UnsubscribeToken SET UsedOn = @UnsubscribedOn
                WHERE Token = @Token AND UsedOn IS NULL
                RETURNING SubscriberId
            )
            UPDATE Subscriber SET
                IsConfirmed = FALSE,
                UnsubscribedOn = @UnsubscribedOn
            WHERE SubscriberId IN (SELECT SubscriberId FROM Burned)";

    private const string InsertSql = @"
            INSERT INTO Subscriber (Email, Name, SubscribedOn, IsConfirmed, Preferences)
            VALUES (@Email, @Name, @SubscribedOn, @IsConfirmed, @Preferences)";

    private const string InsertReturningIdSql = @"
            INSERT INTO Subscriber (Email, Name, SubscribedOn, IsConfirmed, Preferences)
            VALUES (@Email, @Name, @SubscribedOn, @IsConfirmed, @Preferences)
            RETURNING SubscriberId";

    private const string UpdateSql = @"
            UPDATE Subscriber SET
                Email = @Email,
                Name = @Name,
                IsConfirmed = @IsConfirmed,
                Preferences = @Preferences
            WHERE SubscriberId = @SubscriberId";

    private const string UpdateStatusSql = @"
            UPDATE Subscriber SET IsConfirmed = @IsActive
            WHERE SubscriberId = @SubscriberId";

    private const string CountAllSql = "SELECT COUNT(*) FROM Subscriber";

    private const string CountActiveSql = "SELECT COUNT(*) FROM Subscriber WHERE IsConfirmed = TRUE";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public SubscriberRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets all subscribers ordered by subscription date, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Newest first, because the administration grid is read as a feed
    /// of who signed up recently.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All subscribers, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<Subscriber>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Subscriber>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all subscribers for a parent ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A subscriber has no parent entity, so the generic contract
    /// degenerates to "everything"; the member exists only to satisfy the contract.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAllAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="parentId">Ignored; subscribers have no parent.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All subscribers.</returns>
    public override Task<IEnumerable<Subscriber>> GetAllByIdAsync(long parentId, CancellationToken cancellationToken = default)
    {
        return GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a single subscriber by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber, or <c>null</c> when no row carries that key.</returns>
    public override async Task<Subscriber?> GetSingleAsync(long subscriberId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<Subscriber>(
            SelectByIdSql, new { SubscriberId = subscriberId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single subscriber by INT ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber, or <c>null</c> when no row carries that key.</returns>
    public override Task<Subscriber?> GetIntSingleAsync(int subscriberId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(subscriberId, cancellationToken);
    }

    /// <summary>
    /// Gets a subscriber by email address, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The lookup behind "are you already subscribed?". Inline SQL over
    /// <c>Subscriber</c> matching <c>LOWER(Email) = LOWER(@Email)</c>, so a reader who retypes their
    /// address in a different case is recognised rather than added a second time. Note the address is
    /// <b>not</b> trimmed here — a caller that may pass raw form input should trim before calling,
    /// because leading whitespace defeats the comparison.</para>
    /// <para><b>Projection:</b> <c>SubscriberColumns</c> — <c>SubscriberId, Email, Name, SubscribedOn,
    /// IsConfirmed, Preferences</c> plus <c>COALESCE(IsConfirmed, TRUE) AS IsActive</c> and, since
    /// REQ-FN-059, the consent record and the unsubscribe token. The token was previously omitted by
    /// every read here, so the returned entity always carried an empty one; it is projected now
    /// because the consent and rotation statements write it, and a write-back column the loading
    /// read does not project is how this codebase has lost data before.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by lowered address →
    /// first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">The address to look up; compared case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber, or <c>null</c> when the address is not on the list.</returns>
    public async Task<Subscriber?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<Subscriber>(
            SelectByEmailSql, new { Email = email }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether an address is already on the list, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>SELECT COUNT(1)</c> with the same <c>LOWER(Email)</c>
    /// comparison as <see cref="GetByEmailAsync"/>, so the existence check and the fetch can never
    /// disagree. It counts rows in any state: an unconfirmed subscriber still "exists", which is what
    /// stops a second sign-up from creating a duplicate row while the first opt-in email is still
    /// unanswered.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query → compare to
    /// zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="email">The address to test; compared case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when the address is already on the list, confirmed or not.</returns>
    public async Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        var matches = await ExecuteScalarAsync<int>(
            CountByEmailSql, new { Email = email }, cancellationToken).ConfigureAwait(false);

        return matches > 0;
    }

    /// <summary>
    /// Gets every confirmed subscriber, newest first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The recipient list. It filters on <c>IsConfirmed = TRUE</c>, so a
    /// subscriber who never completed double opt-in is excluded and cannot be mailed — that filter is
    /// the compliance boundary, not a UI convenience. Ordered <c>SubscribedOn DESC</c>.</para>
    /// <para><b>Projection:</b> this statement hard-codes <c>TRUE AS IsActive</c> rather than
    /// projecting the column, because the WHERE clause has already established it. Two consequences
    /// worth knowing: a legacy row with a NULL <c>IsConfirmed</c> is <b>excluded</b> here (SQL
    /// three-valued logic), while <see cref="GetAllAsync"/>'s <c>COALESCE(IsConfirmed, TRUE)</c>
    /// reports that same row as active — so the two members can disagree about one legacy row. As
    /// everywhere in this repository, the consent columns and the unsubscribe token are
    /// projected.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered query → materialised
    /// list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Confirmed subscribers, newest first; an empty sequence when none are confirmed.</returns>
    public async Task<IEnumerable<Subscriber>> GetActiveSubscribersAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Subscriber>(SelectActiveSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets subscribers on either side of the confirmation line, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The administration grid's status filter. <c>IsConfirmed</c> is
    /// compared to the bound flag, so passing <c>false</c> yields the pending-opt-in list — the one
    /// an operator uses to decide whether to re-send confirmation mail. Ordered
    /// <c>SubscribedOn DESC</c>.</para>
    /// <para><b>Projection:</b> <c>IsConfirmed AS IsActive</c> with no <c>COALESCE</c>, unlike
    /// <see cref="GetAllAsync"/>. A legacy row with a NULL <c>IsConfirmed</c> matches neither
    /// <c>true</c> nor <c>false</c> and is therefore absent from both halves of this filter. The
    /// consent columns and the unsubscribe token are projected.</para>
    /// <para><b>Flow:</b> bind the flag → helper opens the connection asynchronously → filtered query
    /// → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="isActive">
    /// <c>true</c> for confirmed subscribers, <c>false</c> for those still pending opt-in.
    /// </param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Matching subscribers, newest first; an empty sequence when none match.</returns>
    public async Task<IEnumerable<Subscriber>> GetByStatusAsync(bool isActive, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Subscriber>(
            SelectByStatusSql, new { IsActive = isActive }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds subscribers whose address contains a fragment, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The admin search box. Matching is <c>Email ILIKE @Query</c> —
    /// PostgreSQL's case-insensitive <c>LIKE</c> — with the caller's fragment wrapped in <c>%…%</c>
    /// in C# and bound as a parameter, never concatenated into the statement. Results are capped at
    /// <c>LIMIT 50</c> and ordered <c>SubscribedOn DESC</c>, so a one-character query returns a usable
    /// page instead of the whole list; a search that hits the cap is silently truncated, which is
    /// acceptable for a type-ahead but means this must not be used to enumerate.</para>
    /// <para><b>Caveat:</b> the fragment is not escaped for <c>LIKE</c> wildcards, so a query
    /// containing <c>%</c> or <c>_</c> is treated as a pattern. That is harmless — it is still a bound
    /// parameter and cannot alter the statement — but it means such a query matches more broadly than
    /// the user expects.</para>
    /// <para><b>Projection:</b> <c>IsConfirmed AS IsActive</c>, no <c>COALESCE</c>; the consent
    /// columns and the unsubscribe token are projected.</para>
    /// <para><b>Flow:</b> wrap the fragment in wildcards → helper opens the connection asynchronously
    /// → capped query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">The address fragment to look for; matched case-insensitively.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>At most 50 matching subscribers, newest first; empty when nothing matches.</returns>
    public async Task<IEnumerable<Subscriber>> SearchByEmailAsync(string query, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Subscriber>(
            SearchByEmailSql, new { Query = $"%{query}%" }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a page of subscribers, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a large list never crosses the wire
    /// in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<Subscriber>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Subscriber>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new subscriber, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form. <c>SubscribedOn</c> is normalised through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> so Npgsql sends a <c>timestamp</c> rather than
    /// a <c>timestamptz</c> for a value whose Kind is Utc.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Subscriber</c>.</para>
    /// </remarks>
    /// <param name="subscriber">The subscriber to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(subscriber), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a subscriber and returns the generated ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key. The double opt-in flow needs that key to hang a
    /// verification token off it.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Subscriber</c>.</para>
    /// </remarks>
    /// <param name="subscriber">The subscriber to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>SubscriberId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(subscriber), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing subscriber, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>SubscribedOn</c> is never rewritten — the moment someone
    /// joined is history, not an editable field.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>SubscriberId</c>.</para>
    /// </remarks>
    /// <param name="subscriber">The subscriber carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(Subscriber subscriber, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            UpdateSql,
            new
            {
                subscriber.SubscriberId,
                subscriber.Email,
                subscriber.Name,
                subscriber.IsConfirmed,
                subscriber.Preferences
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Confirms or suspends one subscriber, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A one-column UPDATE of <c>IsConfirmed</c>, keyed on
    /// <c>SubscriberId</c>. It is deliberately narrower than <see cref="UpdateAsync"/>: the double
    /// opt-in confirmation must be able to flip the flag without also rewriting the address and name
    /// from a stale entity the caller happens to be holding. The parameter is named
    /// <c>IsActive</c> to match the model property the UI binds, but the column it writes is
    /// <c>IsConfirmed</c> — the two are the same fact under two names.</para>
    /// <para><b>Flow:</b> bind the key and the flag → helper opens the connection asynchronously →
    /// execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Adds the subscriber to, or removes them from, the recipient list
    /// returned by <see cref="GetActiveSubscribersAsync"/>. An unknown id is a silent no-op.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber to change.</param>
    /// <param name="isActive"><c>true</c> to confirm, <c>false</c> to suspend.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public async Task UpdateStatusAsync(long subscriberId, bool isActive, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            UpdateStatusSql,
            new { SubscriberId = subscriberId, IsActive = isActive },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts every subscriber row, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>SELECT COUNT(*) FROM Subscriber</c> with no filter, so it
    /// includes rows still pending opt-in. It is the paging total for the administration grid and
    /// must match what that grid can page through — subtract
    /// <see cref="GetActiveCountAsync"/> to get the pending backlog.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → scalar count.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The total number of subscriber rows.</returns>
    public async Task<int> GetTotalCountAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(CountAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Counts the confirmed subscribers, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counts <c>IsConfirmed = TRUE</c> — exactly the predicate
    /// <see cref="GetActiveSubscribersAsync"/> uses, so "will reach N people" agrees with the list
    /// that would actually be mailed. A legacy row with a NULL <c>IsConfirmed</c> is excluded from
    /// both, even though <see cref="GetAllAsync"/> would report it as active.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → scalar count.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of confirmed subscribers.</returns>
    public async Task<int> GetActiveCountAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteScalarAsync<int>(CountActiveSql, null, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Consent record and unsubscribe-token lifecycle — REQ-FN-059.
    // =================================================================================================

    /// <summary>
    /// Resolves the subscriber holding an unsubscribe token, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The lookup behind the anonymous <c>/unsubscribe/{token}</c> page.
    /// The comparison is exact and case-SENSITIVE, unlike every email lookup in this repository: the
    /// token is a bearer credential, and a credential that matches case variants of itself is a
    /// larger credential than the one that was issued. The projection is the shared one, so the
    /// consent columns and the token's issuance and burn timestamps come back with the row and the
    /// caller can decide expiry and replay without a second query.</para>
    /// <para><b>Flow:</b> bind the token → helper opens the connection asynchronously → query by
    /// token → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The opaque token taken from the unsubscribe URL.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber carrying that exact token, or <c>null</c> when none does.</returns>
    public async Task<Subscriber?> GetByUnsubscribeTokenAsync(
        string unsubscribeToken, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("UnsubscribeToken", unsubscribeToken);

        return await QueryFirstOrDefaultAsync<Subscriber>(
            SelectByUnsubscribeTokenSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records a withdrawal of consent and burns the token, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One statement does all three things that have to stay in step —
    /// stop the mail, record WHEN the withdrawal happened, and burn the link that caused it — so
    /// there is no window in which the address is off the list with no record of why.
    /// <c>ConfirmedOn</c> is not written, which is precisely what preserves the proof that this
    /// address once opted in (REQ-FN-059).</para>
    /// <para><b>Flow:</b> stamp the instant → guarded UPDATE → report whether a row changed.</para>
    /// <para><b>Side Effects:</b> Updates at most one row. The <c>UnsubscribeTokenUsedOn IS NULL</c>
    /// guard makes the redemption atomic: if two requests carry the same link, exactly one gets
    /// <c>true</c>.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber withdrawing consent.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was changed; <c>false</c> when the identifier is unknown or
    /// the token had already been burned.</returns>
    public async Task<bool> RecordWithdrawalAsync(long subscriberId, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SubscriberId", subscriberId);
        parameters.Add("UnsubscribedOn", DbTimestamp.AsTimestamp(DateTime.UtcNow));

        var affected = await ExecuteAsync(RecordWithdrawalSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Records that consent was given and re-issues the link, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A returning subscriber must not be left holding the token that
    /// was burned when they left, so the consent stamp and the fresh token are written together.
    /// The previous token stops resolving the moment this statement commits.</para>
    /// <para><b>Flow:</b> stamp the instant → UPDATE the flag, the consent column and all three
    /// token columns → report whether a row changed.</para>
    /// <para><b>Side Effects:</b> Updates one row and invalidates the subscriber's previous
    /// unsubscribe link. <c>UnsubscribedOn</c> is left in place, so the earlier withdrawal is still
    /// on the record and only the comparison of the two timestamps changes the derived state.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber giving consent.</param>
    /// <param name="newUnsubscribeToken">The freshly generated token to install.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when a row was changed; <c>false</c> when the identifier is unknown.</returns>
    public async Task<bool> RecordConsentAsync(
        long subscriberId, string newUnsubscribeToken, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SubscriberId", subscriberId);
        parameters.Add("UnsubscribeToken", newUnsubscribeToken);
        parameters.Add("ConfirmedOn", DbTimestamp.AsTimestamp(DateTime.UtcNow));

        var affected = await ExecuteAsync(RecordConsentSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Replaces a subscriber's unsubscribe token, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Send-time rotation, and the mechanism that makes an expiry safe:
    /// every issue can carry a token whose 400-day clock starts when that issue is sent, so a live
    /// subscriber's link can never age out while they are still being mailed. Consent columns are
    /// untouched — rotating a link is not a consent decision.</para>
    /// <para><b>Flow:</b> stamp the issuance → UPDATE the three token columns → report whether a row
    /// changed.</para>
    /// <para><b>Side Effects:</b> The subscriber's previous unsubscribe link stops working. A caller
    /// mailing an issue must therefore use the token it just installed, not one it read earlier.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber whose link is being re-issued.</param>
    /// <param name="newUnsubscribeToken">The freshly generated token to install.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when the token was installed; <c>false</c> when the identifier is
    /// unknown.</returns>
    public async Task<bool> RotateUnsubscribeTokenAsync(
        long subscriberId, string newUnsubscribeToken, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SubscriberId", subscriberId);
        parameters.Add("UnsubscribeToken", newUnsubscribeToken);
        parameters.Add("IssuedOn", DbTimestamp.AsTimestamp(DateTime.UtcNow));

        var affected = await ExecuteAsync(
            RotateUnsubscribeTokenSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    // =================================================================================================
    // Per-issue unsubscribe tokens — REQ-FN-060.
    //
    // These three address the UnsubscribeToken TABLE (migration 027). The three above address the
    // Subscriber.UnsubscribeToken COLUMN. Both are live at once and neither replaces the other —
    // see the header block of 027-PerIssueUnsubscribeToken.sql for why the per-send-rows design was
    // chosen over rotating the single column on every send.
    // =================================================================================================

    /// <summary>
    /// Records one unsubscribe token scoped to one newsletter issue, without blocking the thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Called once per recipient per send, immediately before the
    /// message is composed, so the credential that message carries authorises nothing beyond that
    /// issue. It INSERTS rather than replacing, so the links in issues already delivered keep
    /// working — refusing a genuine opt-out clicked from last week's mail would be a worse defect
    /// than the over-broad credential this narrows.</para>
    /// <para><b>Flow:</b> normalise the issuance timestamp → bind → INSERT → report the row
    /// count.</para>
    /// <para><b>Side Effects:</b> Adds one <c>UnsubscribeToken</c> row. Nothing on
    /// <c>Subscriber</c> changes, so the row-level token and the consent record are untouched.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber the issue is addressed to.</param>
    /// <param name="newsletterId">The issue the token is scoped to.</param>
    /// <param name="unsubscribeToken">The freshly generated token to record.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns><c>true</c> when the token was recorded.</returns>
    public async Task<bool> IssueTokenForNewsletterAsync(
        long subscriberId, long newsletterId, string unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SubscriberId", subscriberId);
        parameters.Add("NewsletterId", newsletterId);
        parameters.Add("Token", unsubscribeToken);
        parameters.Add("IssuedOn", DbTimestamp.AsTimestamp(DateTime.UtcNow));

        var affected = await ExecuteAsync(
            InsertNewsletterTokenSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    /// <summary>
    /// Resolves the holder of a per-issue unsubscribe token, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The first lookup the anonymous <c>/unsubscribe/{token}</c> page
    /// makes; <see cref="GetByUnsubscribeTokenAsync"/> is the fallback for the row-level tokens that
    /// are still sitting in delivered mail. Exact, case-SENSITIVE comparison, for the same reason
    /// that one is.</para>
    /// <para><b>Projection:</b> the three token properties on the returned entity describe the
    /// matched TOKEN ROW, not the subscriber's row-level token — see
    /// <c>SelectByNewsletterTokenSql</c>. The consent columns are the subscriber's own. The entity is
    /// read-only: passing it to an update path would write a per-issue token over the row-level
    /// one.</para>
    /// <para><b>Flow:</b> bind the token → join the token table to its subscriber → first row or
    /// <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The opaque token taken from the unsubscribe URL.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber holding that per-issue token, or <c>null</c> when none does.</returns>
    public async Task<Subscriber?> GetByNewsletterTokenAsync(
        string unsubscribeToken, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Token", unsubscribeToken);

        return await QueryFirstOrDefaultAsync<Subscriber>(
            SelectByNewsletterTokenSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Burns a per-issue token and records the withdrawal, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One statement does both, because a spent link that removed
    /// nobody and an address removed with no record of why are each worse than the operation not
    /// happening at all. The subscriber's other token rows are deliberately left alone: an unopened
    /// link from a different issue still resolves, and reports "already unsubscribed" because the
    /// subscriber is withdrawn by then.</para>
    /// <para><b>Flow:</b> stamp the instant → CTE burns the token row and cascades into the
    /// subscriber row → report whether the withdrawal was recorded.</para>
    /// <para><b>Side Effects:</b> Stamps one token row and updates one subscriber row. The
    /// <c>UsedOn IS NULL</c> guard makes the redemption atomic under a concurrent double open.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">The per-issue token being redeemed.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns><c>true</c> when the withdrawal was recorded; <c>false</c> when the token was
    /// unknown or already burned.</returns>
    public async Task<bool> RedeemNewsletterTokenAsync(
        string unsubscribeToken, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("Token", unsubscribeToken);
        parameters.Add("UnsubscribedOn", DbTimestamp.AsTimestamp(DateTime.UtcNow));

        var affected = await ExecuteAsync(
            RedeemNewsletterTokenSql, parameters, cancellationToken).ConfigureAwait(false);
        return affected > 0;
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets all subscribers ordered by subscription date.
    /// </summary>
    /// <returns>All subscribers.</returns>
    public override IEnumerable<Subscriber> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<Subscriber>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets all subscribers by parent ID (not applicable).
    /// </summary>
    /// <param name="parentId">Ignored; subscribers have no parent.</param>
    /// <returns>All subscribers.</returns>
    public override IEnumerable<Subscriber> GetAllById(long parentId)
    {
        return GetAll();
    }

    /// <summary>
    /// Gets a single subscriber by ID.
    /// </summary>
    /// <param name="subscriberId">The subscriber identifier.</param>
    /// <returns>The subscriber, or <c>null</c> when not found.</returns>
    public override Subscriber? GetSingle(long subscriberId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Subscriber>(SelectByIdSql, new { SubscriberId = subscriberId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single subscriber by INT ID.
    /// </summary>
    /// <param name="subscriberId">The subscriber identifier.</param>
    /// <returns>The subscriber, or <c>null</c> when not found.</returns>
    public override Subscriber? GetIntSingle(int subscriberId)
    {
        return GetSingle(subscriberId);
    }

    /// <summary>
    /// Gets a subscriber by email address.
    /// </summary>
    /// <param name="email">The address to look up.</param>
    /// <returns>The subscriber, or <c>null</c> when the address is not subscribed.</returns>
    public Subscriber? GetByEmail(string email)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Subscriber>(SelectByEmailSql, new { Email = email }).FirstOrDefault();
    }

    /// <summary>
    /// Checks if an email already exists.
    /// </summary>
    /// <param name="email">The address to check.</param>
    /// <returns><c>true</c> when the address is already on the list.</returns>
    public bool EmailExists(string email)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountByEmailSql, new { Email = email }) > 0;
    }

    /// <summary>
    /// Gets all active (confirmed) subscribers.
    /// </summary>
    /// <returns>The confirmed subscribers, newest first.</returns>
    public IEnumerable<Subscriber> GetActiveSubscribers()
    {
        using var connection = GetOpenConnection();
        return connection.Query<Subscriber>(SelectActiveSql).ToList();
    }

    /// <summary>
    /// Gets subscribers by active status.
    /// </summary>
    /// <param name="isActive">Active status filter.</param>
    /// <returns>The matching subscribers, newest first.</returns>
    public IEnumerable<Subscriber> GetByStatus(bool isActive)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Subscriber>(SelectByStatusSql, new { IsActive = isActive }).ToList();
    }

    /// <summary>
    /// Searches subscribers by email.
    /// </summary>
    /// <param name="query">Search query.</param>
    /// <returns>The matching subscribers, newest first.</returns>
    public IEnumerable<Subscriber> SearchByEmail(string query)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Subscriber>(SearchByEmailSql, new { Query = $"%{query}%" }).ToList();
    }

    /// <summary>
    /// Gets paginated subscribers.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<Subscriber> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Subscriber>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new subscriber.
    /// </summary>
    /// <param name="subscriber">The subscriber to persist.</param>
    public override void Insert(Subscriber subscriber)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(subscriber));
    }

    /// <summary>
    /// Inserts a subscriber and returns the generated ID.
    /// </summary>
    /// <param name="subscriber">The subscriber to persist.</param>
    /// <returns>The generated <c>SubscriberId</c>.</returns>
    public override long InsertToGetId(Subscriber subscriber)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(subscriber));
    }

    /// <summary>
    /// Updates an existing subscriber.
    /// </summary>
    /// <param name="subscriber">The subscriber carrying the new values.</param>
    public override void Update(Subscriber subscriber)
    {
        using var connection = GetOpenConnection();
        connection.Execute(
            UpdateSql,
            new
            {
                subscriber.SubscriberId,
                subscriber.Email,
                subscriber.Name,
                subscriber.IsConfirmed,
                subscriber.Preferences
            });
    }

    /// <summary>
    /// Updates subscriber active status.
    /// </summary>
    /// <param name="subscriberId">The subscriber identifier.</param>
    /// <param name="isActive">New active status.</param>
    public void UpdateStatus(long subscriberId, bool isActive)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateStatusSql, new { SubscriberId = subscriberId, IsActive = isActive });
    }

    /// <summary>
    /// Records that consent was given and re-issues the link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The blocking twin of <see cref="RecordConsentAsync"/>, executing
    /// the same <c>RecordConsentSql</c> constant with the same parameters, so the two cannot drift:
    /// a returning subscriber is handed a fresh, unburned token whichever path reactivated them.
    /// <c>UnsubscribedOn</c> is left in place, so the earlier withdrawal stays on the record.</para>
    /// <para><b>Flow:</b> stamp the instant → UPDATE the flag, the consent column and all three
    /// token columns → report whether a row changed.</para>
    /// <para><b>Side Effects:</b> Updates one row and invalidates the subscriber's previous
    /// unsubscribe link.</para>
    /// </remarks>
    /// <param name="subscriberId">The subscriber giving consent.</param>
    /// <param name="newUnsubscribeToken">The freshly generated token to install.</param>
    /// <returns><c>true</c> when a row was changed; <c>false</c> when the identifier is unknown.</returns>
    public bool RecordConsent(long subscriberId, string newUnsubscribeToken)
    {
        var parameters = new DynamicParameters();
        parameters.Add("SubscriberId", subscriberId);
        parameters.Add("UnsubscribeToken", newUnsubscribeToken);
        parameters.Add("ConfirmedOn", DbTimestamp.AsTimestamp(DateTime.UtcNow));

        using var connection = GetOpenConnection();
        return connection.Execute(RecordConsentSql, parameters) > 0;
    }

    /// <summary>
    /// Gets total subscriber count.
    /// </summary>
    /// <returns>The number of subscriber rows.</returns>
    public int GetTotalCount()
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountAllSql);
    }

    /// <summary>
    /// Gets active subscriber count.
    /// </summary>
    /// <returns>The number of confirmed subscribers.</returns>
    public int GetActiveCount()
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountActiveSql);
    }

    /// <summary>
    /// Builds the parameter set shared by both insert paths.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>SubscribedOn</c> is normalised to
    /// <see cref="DateTimeKind.Unspecified"/> because Npgsql picks the wire type from the value's
    /// Kind: a <c>Utc</c> value is sent as <c>timestamptz</c>, which does not match the
    /// <c>TIMESTAMP</c> column this schema declares. Only the Kind label changes; the instant is
    /// untouched.</para>
    /// <para><b>Flow:</b> normalise the timestamp → project the writable columns.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="subscriber">The subscriber being inserted.</param>
    /// <returns>The anonymous parameter object Dapper binds.</returns>
    private static object BuildInsertParameters(Subscriber subscriber)
    {
        return new
        {
            subscriber.Email,
            subscriber.Name,
            SubscribedOn = DbTimestamp.AsTimestamp(subscriber.SubscribedOn),
            subscriber.IsConfirmed,
            subscriber.Preferences
        };
    }
}
