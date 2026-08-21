namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing UserEvent data access operations using Dapper ORM.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Handles CRUD operations for user events, which serve two overlapping resume
/// sections — speaking engagements and the work-experience timeline. Which set of columns applies is
/// inferred from <c>Type</c>; there is no discriminator column.</para>
///
/// <para><b>Code Flow:</b> A page injects <see cref="IUserEventRepo"/>, calls an <c>…Async</c>
/// member, and the member routes through the protected helpers on <c>GenericRepository</c>, which
/// open the connection asynchronously and flow the cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL, <see cref="DbTimestamp"/>.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage. Both twins execute the
/// same SQL constant and bind the same parameter object, so they cannot drift apart.</para>
///
/// <para><b>Timestamp binding (REQ-NFR-026, trap 1):</b> both date columns —
/// <c>EventDate</c> and <c>StartDate</c> — are declared <c>TIMESTAMP</c> without time zone, while the
/// experience editor supplies values whose <c>Kind</c> is <c>Local</c> (a date picker, or
/// <c>DateTime.Today</c>) and other callers supply <c>Utc</c>. Npgsql infers the wire type from the
/// Kind, so an un-normalised value is sent as <c>timestamptz</c> and PostgreSQL then shifts the
/// instant into the session time zone before storing it — which on a non-UTC server moves a
/// month-precision resume date across a month boundary. Every bound date therefore passes through
/// <see cref="DbTimestamp.AsTimestamp(DateTime)"/>, which converts a local value to UTC and drops the
/// Kind so the stored instant is the one the caller meant. The mapped column alias
/// <c>type AS eventtype</c> is equally load-bearing: a <c>SELECT *</c> would not bind
/// <c>EventType</c>.</para>
/// </remarks>
public class UserEventRepo : GenericRepository<UserEvent>, IUserEventRepo
{
    private const string EventColumns = @"
                     eventid, logoiconpath, eventtitle, sessiontitle, eventurl,
                     eventdate, type AS eventtype, userid, startdate, description,
                     displayorder, iscurrent";

    private const string SelectAllSql = @"
            SELECT " + EventColumns + @"
            FROM userevents
            ORDER BY displayorder, eventdate DESC";

    private const string SelectByUserIdSql = @"
            SELECT " + EventColumns + @"
            FROM userevents
            WHERE userid = @UserId
            ORDER BY displayorder, eventdate DESC";

    private const string SelectByUserAndTypeSql = @"
            SELECT " + EventColumns + @"
            FROM userevents
            WHERE userid = @UserId AND type = @EventType
            ORDER BY displayorder, eventdate DESC";

    private const string SelectByIdSql = @"
            SELECT " + EventColumns + @"
            FROM userevents
            WHERE eventid = @EventId";

    private const string SelectPagedSql = @"
            SELECT " + EventColumns + @"
            FROM userevents
            ORDER BY displayorder, eventdate DESC
            LIMIT @PageSize OFFSET @Offset";

    private const string InsertSql = @"
            INSERT INTO userevents (logoiconpath, eventtitle, sessiontitle, eventurl,
                                    eventdate, type, userid, startdate, description,
                                    displayorder, iscurrent)
            VALUES (@LogoIconPath, @EventTitle, @SessionTitle, @EventUrl, @EventDate,
                    @EventType, @UserID, @StartDate, @Description, @DisplayOrder, @IsCurrent)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING eventid";

    private const string UpdateSql = @"
            UPDATE userevents
            SET logoiconpath = @LogoIconPath,
                eventtitle = @EventTitle,
                sessiontitle = @SessionTitle,
                eventurl = @EventUrl,
                eventdate = @EventDate,
                type = @EventType,
                userid = @UserID,
                startdate = @StartDate,
                description = @Description,
                displayorder = @DisplayOrder,
                iscurrent = @IsCurrent
            WHERE eventid = @EventID";

    private const string DeleteSql = "DELETE FROM userevents WHERE eventid = @EventId";

    private const string UpdateDisplayOrderSql =
        "UPDATE userevents SET displayorder = @DisplayOrder WHERE eventid = @EventId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public UserEventRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets every event in the table, in timeline order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Display order comes first and the event date breaks ties newest
    /// first, which is the reverse-chronological order the resume timeline renders.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All events, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<UserEvent>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserEvent>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every event belonging to a user, in timeline order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns both talks and experience rows; the caller filters on
    /// <c>EventType</c>, or uses <see cref="GetByUserAndTypeAsync"/> to filter in SQL.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query filtered on user → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The user's events, or an empty sequence when they have none.</returns>
    public override async Task<IEnumerable<UserEvent>> GetAllByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserEvent>(
            SelectByUserIdSql, new { UserId = userId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a user's events of one type, in timeline order, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>Type</c> is free text with no lookup table behind it, so the
    /// match is exact and the caller must spell the type as it was stored — "Experience" for the
    /// work timeline, "Conference" and friends for talks.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query filtered on user and
    /// type → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="eventType">The event type to filter by, for example "Experience".</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The matching events, or an empty sequence when none match.</returns>
    public async Task<IEnumerable<UserEvent>> GetByUserAndTypeAsync(long userId, string eventType, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserEvent>(
            SelectByUserAndTypeSql,
            new { UserId = userId, EventType = eventType },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single event by its identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>,
    /// which is how the experience editor reports "this entry has already been deleted".</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The event, or <c>null</c> when no row carries that key.</returns>
    public override async Task<UserEvent?> GetSingleAsync(long eventId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<UserEvent>(
            SelectByIdSql, new { EventId = eventId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single event by INT identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGSERIAL</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The event, or <c>null</c> when no row carries that key.</returns>
    public override Task<UserEvent?> GetIntSingleAsync(int eventId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(eventId, cancellationToken);
    }

    /// <summary>
    /// Gets a page of events, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a long timeline never crosses the wire
    /// in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<UserEvent>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<UserEvent>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new event, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> normalise both dates → helper opens the connection asynchronously → INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserEvents</c>.</para>
    /// </remarks>
    /// <param name="userEvent">The event to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(UserEvent userEvent, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(userEvent), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts an event and returns the generated identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key.</para>
    /// <para><b>Flow:</b> normalise both dates → INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>UserEvents</c>.</para>
    /// </remarks>
    /// <param name="userEvent">The event to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>EventId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(UserEvent userEvent, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(userEvent), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing event, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every editable column is written together, including both dates,
    /// so an experience row that switches to "current" clears its end date in the same statement.</para>
    /// <para><b>Flow:</b> normalise both dates → helper opens the connection asynchronously → UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>EventId</c>.</para>
    /// </remarks>
    /// <param name="userEvent">The event carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(UserEvent userEvent, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(userEvent), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes an event by identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an unknown identifier affects no rows and is treated as a
    /// no-op rather than an error, so a double submit is harmless.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row from <c>UserEvents</c>.</para>
    /// </remarks>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    public async Task DeleteAsync(long eventId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(DeleteSql, new { EventId = eventId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rewrites display order for several events at once, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A reorder touches every row whose position moved, so the whole set
    /// is written in one call. An empty dictionary is a no-op rather than an error, which keeps a
    /// drag that ended where it started from reaching the database at all.</para>
    ///
    /// <para><b>Flow:</b> project the dictionary to a parameter list → one asynchronous connection →
    /// Dapper's multi-execute runs the statement once per entry. This is the material improvement over
    /// the synchronous twin, which opened a connection and then blocked once per row.</para>
    ///
    /// <para><b>Side Effects:</b> Updates one row per supplied identifier. The statements are not
    /// wrapped in a transaction — display order is presentational, and a partial reorder leaves the
    /// timeline readable rather than broken.</para>
    /// </remarks>
    /// <param name="eventOrders">Map of event identifier to its new display order.</param>
    /// <param name="cancellationToken">Cancels the statements.</param>
    /// <returns>A task that completes when every row has been written.</returns>
    public async Task UpdateDisplayOrdersAsync(Dictionary<long, int> eventOrders, CancellationToken cancellationToken = default)
    {
        if (eventOrders is null || eventOrders.Count == 0)
        {
            return;
        }

        var parameters = eventOrders
            .Select(entry => new { EventId = entry.Key, DisplayOrder = entry.Value })
            .ToList();

        await ExecuteAsync(UpdateDisplayOrderSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets every event in the table, in timeline order.
    /// </summary>
    /// <returns>All events.</returns>
    public override IEnumerable<UserEvent> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserEvent>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets every event belonging to a user, in timeline order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <returns>The user's events.</returns>
    public override IEnumerable<UserEvent> GetAllById(long userId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserEvent>(SelectByUserIdSql, new { UserId = userId }).ToList();
    }

    /// <summary>
    /// Gets a user's events of one type, in timeline order.
    /// </summary>
    /// <param name="userId">The owning user's identifier.</param>
    /// <param name="eventType">The event type to filter by, for example "Experience".</param>
    /// <returns>The matching events.</returns>
    public IEnumerable<UserEvent> GetByUserAndType(long userId, string eventType)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserEvent>(
            SelectByUserAndTypeSql, new { UserId = userId, EventType = eventType }).ToList();
    }

    /// <summary>
    /// Gets a single event by its identifier.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <returns>The event, or <c>null</c> when not found.</returns>
    public override UserEvent? GetSingle(long eventId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<UserEvent>(SelectByIdSql, new { EventId = eventId });
    }

    /// <summary>
    /// Gets a single event by INT identifier.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    /// <returns>The event, or <c>null</c> when not found.</returns>
    public override UserEvent? GetIntSingle(int eventId)
    {
        return GetSingle((long)eventId);
    }

    /// <summary>
    /// Gets a page of events.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<UserEvent> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<UserEvent>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new event.
    /// </summary>
    /// <param name="userEvent">The event to persist.</param>
    public override void Insert(UserEvent userEvent)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(userEvent));
    }

    /// <summary>
    /// Inserts an event and returns the generated identifier.
    /// </summary>
    /// <param name="userEvent">The event to persist.</param>
    /// <returns>The generated <c>EventId</c>.</returns>
    public override long InsertToGetId(UserEvent userEvent)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(userEvent));
    }

    /// <summary>
    /// Updates an existing event.
    /// </summary>
    /// <param name="userEvent">The event carrying the new values.</param>
    public override void Update(UserEvent userEvent)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(userEvent));
    }

    /// <summary>
    /// Deletes an event by identifier.
    /// </summary>
    /// <param name="eventId">The event identifier.</param>
    public void Delete(long eventId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteSql, new { EventId = eventId });
    }

    /// <summary>
    /// Rewrites display order for several events at once.
    /// </summary>
    /// <param name="eventOrders">Map of event identifier to its new display order.</param>
    public void UpdateDisplayOrders(Dictionary<long, int> eventOrders)
    {
        if (eventOrders is null || eventOrders.Count == 0)
        {
            return;
        }

        using var connection = GetOpenConnection();
        foreach (var entry in eventOrders)
        {
            connection.Execute(
                UpdateDisplayOrderSql, new { EventId = entry.Key, DisplayOrder = entry.Value });
        }
    }

    // =================================================================================================
    // Parameter binding shared by both twins.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter object both insert statements bind.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both dates pass through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> because <c>EventDate</c> and <c>StartDate</c>
    /// are <c>TIMESTAMP</c> without time zone. Without that, a value carrying <c>Kind = Local</c> or
    /// <c>Utc</c> is sent as <c>timestamptz</c> and PostgreSQL shifts the instant into the session
    /// time zone — enough to move a month-precision resume date into the previous month.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="userEvent">The event being persisted.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildInsertParameters(UserEvent userEvent)
    {
        return new
        {
            userEvent.LogoIconPath,
            userEvent.EventTitle,
            userEvent.SessionTitle,
            userEvent.EventUrl,
            EventDate = DbTimestamp.AsTimestamp(userEvent.EventDate),
            userEvent.EventType,
            userEvent.UserID,
            StartDate = DbTimestamp.AsTimestamp(userEvent.StartDate),
            userEvent.Description,
            userEvent.DisplayOrder,
            userEvent.IsCurrent
        };
    }

    /// <summary>
    /// Builds the parameter object the update statement binds.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to the insert binding plus the key the statement matches
    /// on; both dates are normalised for the same reason.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="userEvent">The event being updated.</param>
    /// <returns>The bound parameter object.</returns>
    private static object BuildUpdateParameters(UserEvent userEvent)
    {
        return new
        {
            userEvent.EventID,
            userEvent.LogoIconPath,
            userEvent.EventTitle,
            userEvent.SessionTitle,
            userEvent.EventUrl,
            EventDate = DbTimestamp.AsTimestamp(userEvent.EventDate),
            userEvent.EventType,
            userEvent.UserID,
            StartDate = DbTimestamp.AsTimestamp(userEvent.StartDate),
            userEvent.Description,
            userEvent.DisplayOrder,
            userEvent.IsCurrent
        };
    }
}
