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
    /// The projection shared by every subscriber read.
    /// </summary>
    /// <remarks>
    /// <c>IsActive</c> is not a column — it is <c>IsConfirmed</c> under the name the UI binds to.
    /// The <c>COALESCE</c> treats a legacy NULL as active, because rows written before double
    /// opt-in existed were confirmed by definition.
    /// </remarks>
    private const string SubscriberColumns = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   COALESCE(IsConfirmed, TRUE) as IsActive
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
                   TRUE as IsActive
            FROM Subscriber
            WHERE IsConfirmed = TRUE
            ORDER BY SubscribedOn DESC";

    private const string SelectByStatusSql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive
            FROM Subscriber
            WHERE IsConfirmed = @IsActive
            ORDER BY SubscribedOn DESC";

    private const string SearchByEmailSql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive
            FROM Subscriber
            WHERE Email ILIKE @Query
            ORDER BY SubscribedOn DESC
            LIMIT 50";

    private const string SelectPagedSql = @"
            SELECT SubscriberId, Email, Name, SubscribedOn, IsConfirmed, Preferences,
                   IsConfirmed as IsActive
            FROM Subscriber
            ORDER BY SubscribedOn DESC
            LIMIT @PageSize OFFSET @Offset";

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
    /// IsConfirmed, Preferences</c> plus <c>COALESCE(IsConfirmed, TRUE) AS IsActive</c>.
    /// <c>UnsubscribeToken</c> is <b>not</b> selected by any read in this repository, so the returned
    /// entity always carries an empty token; the newsletter send path reads its subscribers through
    /// <c>NewsletterRepo</c>, whose projection does include it.</para>
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
    /// everywhere in this repository, <c>UnsubscribeToken</c> is not selected.</para>
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
    /// <c>true</c> nor <c>false</c> and is therefore absent from both halves of this filter.
    /// <c>UnsubscribeToken</c> is not selected.</para>
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
    /// <para><b>Projection:</b> <c>IsConfirmed AS IsActive</c>, no <c>COALESCE</c>;
    /// <c>UnsubscribeToken</c> is not selected.</para>
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
