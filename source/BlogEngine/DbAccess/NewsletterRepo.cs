using BlogEngine.DaCore;
using BlogModels;
using BlogModels.Interfaces;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository for newsletter issues, send history and unsubscribe tokens.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every SQL statement behind the newsletter feature — composition,
/// dispatch bookkeeping, the public archive and unsubscribe resolution — so <c>NewsletterSvc</c>
/// carries business rules only.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The connection string is closed over by the DI factory lambda and passed to the base
///         <c>GenericRepository</c>.</item>
///   <item>Every <c>…Async</c> member routes through the protected helpers on
///         <c>GenericRepository</c>, which open the connection with <c>OpenAsync</c>, flow the
///         cancellation token into the Dapper <c>CommandDefinition</c> and buffer the result before
///         the connection closes.</item>
///   <item>Public-archive reads all share <see cref="PublishedPredicate"/>, so listing, counting,
///         slug resolution and navigation can never disagree about what "published" means.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Dapper, Npgsql (via <c>DbConnectionFactory</c>), the
/// <c>Newsletter</c>, <c>SubscriberNewsletter</c> and <c>Subscriber</c> tables as extended by
/// migration 015.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as
/// <c>INewsletterRepo</c>. Call the <c>…Async</c> members; the synchronous twins exist only to
/// satisfy the legacy half of the generic contract and are deleted in the final stage of
/// REQ-NFR-026.</para>
///
/// <para><b>Async conversion (REQ-NFR-026).</b> Four things about the shape below are deliberate:</para>
/// <list type="number">
///   <item><b>Every statement lives in one <c>const</c></b> shared by the async member and its
///   synchronous twin, so the two cannot drift while both exist and deleting the twin later removes
///   only a method, never a query.</item>
///   <item><b>Every async member is a real implementation</b> that awaits Dapper. Inheriting the base
///   class's temporary bridge compiles, passes every test and still parks a thread-pool thread for
///   the whole round trip — the exact stall this requirement removes.</item>
///   <item><b>The synchronous twins execute their own blocking Dapper call.</b> The previous
///   <c>Update</c> blocked on <c>UpdateAsync(...).GetAwaiter().GetResult()</c>, which is a deadlock
///   risk inside a Blazor Server circuit; it is now a plain synchronous statement again.</item>
///   <item><b>Every <see cref="DateTime"/> is bound through <see cref="DbTimestamp.AsTimestamp(DateTime)"/>.</b>
///   Each timestamp column here is <c>TIMESTAMP</c> (no time zone) but <c>DateTime.UtcNow</c> carries
///   <c>Kind = Utc</c>, which Npgsql sends as <c>timestamptz</c>; PostgreSQL then applies an
///   assignment cast through the session time zone, silently shifting the stored instant on any
///   server not running in UTC.</item>
/// </list>
/// </remarks>
public class NewsletterRepo : GenericRepository<Newsletter>, INewsletterRepo
{
    /// <summary>
    /// The single definition of a publicly reachable issue: sent, marked public and slugged.
    /// A draft or unsent issue can never satisfy it.
    /// </summary>
    private const string PublishedPredicate =
        "Status = 'sent' AND IsPublic = TRUE AND Slug IS NOT NULL AND Slug <> ''";

    private const string NewsletterColumns = @"
        NewsletterId, Title, Content, COALESCE(Summary, '') AS Summary, CreatedOn,
        ScheduledFor, SentOn, Status, COALESCE(Slug, '') AS Slug, IsPublic, RecipientCount";

    private const string SubscriberColumns = @"
        SubscriberId, Email, Name, SubscribedOn, COALESCE(IsConfirmed, FALSE) AS IsConfirmed,
        COALESCE(Preferences, '') AS Preferences,
        COALESCE(IsConfirmed, FALSE) AS IsActive,
        COALESCE(UnsubscribeToken, '') AS UnsubscribeToken";

    private const string SelectByIdSql =
        $"SELECT {NewsletterColumns} FROM Newsletter WHERE NewsletterId = @NewsletterId";

    private const string SelectAllSql =
        $"SELECT {NewsletterColumns} FROM Newsletter ORDER BY CreatedOn DESC";

    private const string SelectPagedSql = $@"
        SELECT {NewsletterColumns} FROM Newsletter
        ORDER BY CreatedOn DESC
        LIMIT @PageSize OFFSET @OffSet";

    private const string InsertSql = @"
        INSERT INTO Newsletter (Title, Content, Summary, CreatedOn, ScheduledFor, Status, IsPublic, RecipientCount)
        VALUES (@Title, @Content, @Summary, @CreatedOn, @ScheduledFor, @Status, @IsPublic, 0)";

    private const string InsertReturningIdSql = $@"
        {InsertSql}
        RETURNING NewsletterId";

    private const string UpdateSql = @"
        UPDATE Newsletter SET
            Title = @Title, Content = @Content, Summary = @Summary,
            ScheduledFor = @ScheduledFor, Status = @Status, IsPublic = @IsPublic,
            UpdatedOn = @UpdatedOn
        WHERE NewsletterId = @NewsletterId";

    private const string CountBySlugSql =
        "SELECT COUNT(1) FROM Newsletter WHERE LOWER(Slug) = LOWER(@Slug)";

    private const string MarkSentSql = @"
        UPDATE Newsletter SET
            Status = 'sent', Slug = @Slug, SentOn = @SentOn,
            RecipientCount = @RecipientCount, IsPublic = @IsPublic, UpdatedOn = @SentOn
        WHERE NewsletterId = @NewsletterId";

    private const string SelectRecipientsSql = $@"
        SELECT {SubscriberColumns}
        FROM Subscriber
        WHERE (@IncludeInactive OR COALESCE(IsConfirmed, FALSE) = TRUE)
          AND (@EmailFilter = '' OR Email ILIKE @EmailPattern)
        ORDER BY SubscribedOn DESC
        LIMIT NULLIF(@MaxRecipients, 0)";

    private const string InsertRecipientSql = @"
        INSERT INTO SubscriberNewsletter (NewsletterId, SubscriberId, SentOn, SendStatus, ErrorMessage)
        VALUES (@NewsletterId, @SubscriberId, @SentOn, @SendStatus, @ErrorMessage)";

    private const string SelectSendHistorySql = @"
        SELECT sn.Id, sn.NewsletterId, sn.SubscriberId, s.Email, sn.SentOn, sn.OpenedOn,
               sn.ClickedOn, sn.SendStatus, COALESCE(sn.ErrorMessage, '') AS ErrorMessage
        FROM SubscriberNewsletter sn
        INNER JOIN Subscriber s ON s.SubscriberId = sn.SubscriberId
        WHERE sn.NewsletterId = @NewsletterId
        ORDER BY sn.SentOn DESC, sn.Id DESC";

    private const string SelectPublishedPageSql = $@"
        SELECT {NewsletterColumns} FROM Newsletter
        WHERE {PublishedPredicate}
        ORDER BY SentOn DESC, NewsletterId DESC
        LIMIT @PageSize OFFSET @OffSet";

    private const string CountPublishedSql =
        $"SELECT COUNT(*) FROM Newsletter WHERE {PublishedPredicate}";

    private const string SelectPublishedBySlugSql = $@"
        SELECT {NewsletterColumns} FROM Newsletter
        WHERE {PublishedPredicate} AND LOWER(Slug) = LOWER(@Slug)";

    private const string SelectPreviousPublishedSql = $@"
        SELECT {NewsletterColumns} FROM Newsletter
        WHERE {PublishedPredicate} AND SentOn < @SentOn
        ORDER BY SentOn DESC LIMIT 1";

    private const string SelectNextPublishedSql = $@"
        SELECT {NewsletterColumns} FROM Newsletter
        WHERE {PublishedPredicate} AND SentOn > @SentOn
        ORDER BY SentOn ASC LIMIT 1";

    private const string EnsureUnsubscribeTokensSql = @"
        UPDATE Subscriber
        SET UnsubscribeToken = md5(random()::text || clock_timestamp()::text || SubscriberId::text)
                            || md5(clock_timestamp()::text || random()::text || SubscriberId::text)
        WHERE UnsubscribeToken IS NULL OR UnsubscribeToken = ''";

    private const string SelectSubscriberByTokenSql = $@"
        SELECT {SubscriberColumns}
        FROM Subscriber
        WHERE UnsubscribeToken = @UnsubscribeToken";

    private const string DeactivateSubscriberSql =
        "UPDATE Subscriber SET IsConfirmed = FALSE WHERE SubscriberId = @SubscriberId";

    /// <summary>
    /// Initializes the repository with the PostgreSQL connection string.
    /// </summary>
    /// <param name="connectionString">Connection string supplied by <c>BlogSvcInitializer</c>.</param>
    public NewsletterRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Loads a single issue in any status, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The admin surface reads drafts, scheduled and sent issues alike,
    /// so no publication predicate is applied. An unknown id is a normal answer and yields
    /// <c>null</c>.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetSingleAsync"/>, which runs the one keyed read
    /// this repository needs.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The issue, or <c>null</c> when no such issue exists.</returns>
    public Task<Newsletter?> GetByIdAsync(long newsletterId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(newsletterId, cancellationToken);
    }

    /// <summary>
    /// Loads every issue, newest created first, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Creation order is the admin history order, applied in SQL so
    /// every caller agrees. Drafts and scheduled issues are included — this is the admin list, not
    /// the archive.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All issues in any status; an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<Newsletter>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Newsletter>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads every issue for a parent id, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Newsletter issues have no parent entity, so the filter is ignored
    /// and the whole list is returned. The member exists only to satisfy the generic contract.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAllAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Ignored; issues have no parent.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All issues in any status.</returns>
    public override Task<IEnumerable<Newsletter>> GetAllByIdAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// Loads one page of issues in any status, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a long history never crosses the wire
    /// in full. This is the admin paging shape; the public archive uses
    /// <see cref="GetPublishedPageAsync"/>, which additionally filters on the published predicate.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence past the end.</returns>
    public override async Task<IEnumerable<Newsletter>> GetPagedDataAsync(
        int pageSize,
        int offSet,
        CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Newsletter>(
            SelectPagedSql, new { PageSize = pageSize, OffSet = offSet }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads one issue by its primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> No publication predicate — an unsent draft resolves here, which is
    /// what the composer needs and why the public routes use the published readers instead.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Issue identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The issue, or <c>null</c> when no row carries that key.</returns>
    public override async Task<Newsletter?> GetSingleAsync(long singleId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<Newsletter>(
            SelectByIdSql, new { NewsletterId = singleId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads one issue by an INT primary key, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGSERIAL</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Issue identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The issue, or <c>null</c> when no row carries that key.</returns>
    public override Task<Newsletter?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Inserts an issue without reading back its key, sans blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The plain INSERT is cheaper than the <c>RETURNING</c> form, so it
    /// is kept for the callers that do not need the generated key. Both forms bind the same columns
    /// through <see cref="BuildWriteParameters"/>, so the pair cannot drift.</para>
    /// <para><b>Flow:</b> bind the editable columns → helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Newsletter</c>.</para>
    /// </remarks>
    /// <param name="entity">The issue to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(Newsletter entity, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildWriteParameters(entity), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts an issue and returns its generated identifier, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so the
    /// composer learns the new id without a second round trip. A new issue is always written with a
    /// recipient count of zero — it has reached nobody yet.</para>
    /// <para><b>Flow:</b> bind the editable columns → helper opens the connection asynchronously →
    /// INSERT … RETURNING → read the single value.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Newsletter</c>.</para>
    /// </remarks>
    /// <param name="newsletter">The issue to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>NewsletterId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(Newsletter newsletter, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildWriteParameters(newsletter), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates the editable fields of an existing issue, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only the composable fields are written — the slug, send time and
    /// recipient count belong to the dispatch path (<see cref="MarkSentAsync"/>) and must not be
    /// rewritten by an edit. <c>UpdatedOn</c> is stamped here rather than by the caller so every
    /// write path agrees on the clock.</para>
    /// <para><b>Flow:</b> bind the editable columns plus key and timestamp → helper opens the
    /// connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>NewsletterId</c>.</para>
    /// </remarks>
    /// <param name="newsletter">The issue carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the update has run.</returns>
    public override async Task UpdateAsync(Newsletter newsletter, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(newsletter), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Slugs address a public page, so they must be unique. The
    /// comparison is case-insensitive because <c>/newsletter/{slug}</c> resolves case-insensitively —
    /// treating "August-Issue" as free while "august-issue" exists would produce two rows that
    /// resolve to the same URL.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">Candidate slug.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when the slug is in use.</returns>
    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        var matches = await ExecuteScalarAsync<int>(
            CountBySlugSql, new { Slug = slug }, cancellationToken).ConfigureAwait(false);

        return matches > 0;
    }

    /// <summary>
    /// Stamps an issue as sent, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the single transition that turns a private draft into a
    /// public archive record, so slug, send time, reach and the public flag are written together —
    /// a partially stamped issue would satisfy some halves of the published predicate and not others.
    /// <c>UpdatedOn</c> is set to the same instant as <c>SentOn</c> so the audit trail reads
    /// consistently.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously →
    /// execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row and makes the issue publicly reachable.</para>
    /// </remarks>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <param name="slug">The unique slug the issue is published under.</param>
    /// <param name="sentOn">Dispatch timestamp.</param>
    /// <param name="recipientCount">Number of subscribers successfully reached.</param>
    /// <param name="isPublic">Whether the issue joins the public archive.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the issue has been stamped.</returns>
    public async Task MarkSentAsync(
        long newsletterId,
        string slug,
        DateTime sentOn,
        int recipientCount,
        bool isPublic,
        CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("NewsletterId", newsletterId);
        parameters.Add("Slug", slug);
        parameters.Add("SentOn", DbTimestamp.AsTimestamp(sentOn));
        parameters.Add("RecipientCount", recipientCount);
        parameters.Add("IsPublic", isPublic);

        await ExecuteAsync(MarkSentSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the subscribers a send should target, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unconfirmed subscribers are excluded unless the audience asks for
    /// them, so a routine send never mails an address that has not opted in. An empty email filter
    /// means "no filter" rather than "match the empty string", and a <c>MaxRecipients</c> of zero
    /// means "no cap" — expressed as <c>LIMIT NULLIF(@MaxRecipients, 0)</c> because a literal
    /// <c>LIMIT 0</c> would silently target nobody. Each row carries its unsubscribe token so the
    /// dispatcher can build a personal link without a second read.</para>
    /// <para><b>Flow:</b> read the audience with a null-safe default → helper opens the connection
    /// asynchronously → filtered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="audience">Audience filter; <c>NewsletterAudience.Everyone</c> for a full send.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Matching subscribers, each carrying its unsubscribe token.</returns>
    public async Task<IReadOnlyList<Subscriber>> GetRecipientsAsync(
        NewsletterAudience audience,
        CancellationToken cancellationToken = default)
    {
        var filter = audience?.EmailFilter ?? string.Empty;
        var parameters = new DynamicParameters();
        parameters.Add("IncludeInactive", audience != null && audience.IncludeInactive);
        parameters.Add("EmailFilter", filter);
        parameters.Add("EmailPattern", $"%{filter}%");
        parameters.Add("MaxRecipients", audience == null ? 0 : audience.MaxRecipients);

        var rows = await QueryAsync<Subscriber>(
            SelectRecipientsSql, parameters, cancellationToken).ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>
    /// Records one delivery attempt, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A row is written for a failure as well as a success — the send
    /// history is an audit trail, so an address that bounced must be visible rather than absent.</para>
    /// <para><b>Flow:</b> bind the attempt with a normalised timestamp → helper opens the connection
    /// asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>SubscriberNewsletter</c>.</para>
    /// </remarks>
    /// <param name="recipient">The attempt outcome to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public async Task InsertRecipientAsync(NewsletterRecipient recipient, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipient);

        var parameters = new DynamicParameters();
        parameters.Add("NewsletterId", recipient.NewsletterId);
        parameters.Add("SubscriberId", recipient.SubscriberId);
        parameters.Add("SentOn", DbTimestamp.AsTimestamp(recipient.SentOn));
        parameters.Add("SendStatus", recipient.SendStatus);
        parameters.Add("ErrorMessage", recipient.ErrorMessage);

        await ExecuteAsync(InsertRecipientSql, parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the full send history for one issue, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The subscriber's current address is joined in rather than stored
    /// on the history row, so the audit screen shows who the row belongs to even after the address
    /// has been edited. Most recent attempt first, with the identity as a tiebreaker so a batch sent
    /// within the same clock tick still has a stable order.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → joined query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="newsletterId">Issue identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Delivery rows, most recent first; empty when the issue was never sent.</returns>
    public async Task<IReadOnlyList<NewsletterRecipient>> GetSendHistoryAsync(
        long newsletterId,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync<NewsletterRecipient>(
            SelectSendHistorySql, new { NewsletterId = newsletterId }, cancellationToken).ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>
    /// Reads one page of the public archive, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Filters on <see cref="PublishedPredicate"/>, the same predicate
    /// <see cref="GetPublishedCountAsync"/> counts with, so the archive's total can never disagree
    /// with the rows it lists. Newest send first, with the identity as a tiebreaker so two issues
    /// sent in the same tick page deterministically instead of repeating or skipping rows.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered LIMIT/OFFSET query →
    /// materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published issues only; empty past the end of the archive.</returns>
    public async Task<IReadOnlyList<Newsletter>> GetPublishedPageAsync(
        int pageSize,
        int offSet,
        CancellationToken cancellationToken = default)
    {
        var rows = await QueryAsync<Newsletter>(
            SelectPublishedPageSql,
            new { PageSize = pageSize, OffSet = offSet },
            cancellationToken).ConfigureAwait(false);

        return rows.AsList();
    }

    /// <summary>
    /// Counts the issues visible in the public archive, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Uses the same predicate as the listing so the pager's page count
    /// matches the rows a reader can actually reach.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → <c>COUNT(*)</c> → scalar.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Number of published issues; zero when the archive is empty.</returns>
    public async Task<int> GetPublishedCountAsync(CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<int>(CountPublishedSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves a published issue from its public slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The published predicate is applied here and not left to the
    /// caller, so a draft's slug cannot be guessed into a readable page. An unknown or unpublished
    /// slug returns <c>null</c> for the route to render its 404 rather than throwing.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filtered query by slug →
    /// first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The issue's slug.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The issue, or <c>null</c> when the slug is unknown, a draft or not public.</returns>
    public async Task<Newsletter?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<Newsletter>(
            SelectPublishedBySlugSql, new { Slug = slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the published issue sent immediately before the given time.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Neighbours are resolved among published issues only, so the
    /// "previous issue" link can never walk a reader into a draft. The oldest issue has no previous
    /// and returns <c>null</c> rather than wrapping to the newest.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetNeighbourAsync"/> with the descending query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="sentOn">The current issue's send time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The older neighbour, or <c>null</c> at the oldest end of the archive.</returns>
    public Task<Newsletter?> GetPreviousPublishedAsync(DateTime sentOn, CancellationToken cancellationToken = default)
    {
        return GetNeighbourAsync(SelectPreviousPublishedSql, sentOn, cancellationToken);
    }

    /// <summary>
    /// Finds the published issue sent immediately after the given time.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The ascending mirror of <see cref="GetPreviousPublishedAsync"/>;
    /// the newest issue has no next and returns <c>null</c>.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetNeighbourAsync"/> with the ascending query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="sentOn">The current issue's send time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The newer neighbour, or <c>null</c> at the newest end of the archive.</returns>
    public Task<Newsletter?> GetNextPublishedAsync(DateTime sentOn, CancellationToken cancellationToken = default)
    {
        return GetNeighbourAsync(SelectNextPublishedSql, sentOn, cancellationToken);
    }

    /// <summary>
    /// Fills in an unsubscribe token for any subscriber still missing one.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Defensive repair run before every send, so no message can go out
    /// without a working unsubscribe link even for a row inserted before migration 015 attached the
    /// column default. Rows that already have a token are left untouched, so an existing link in a
    /// previously delivered message keeps working.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → conditional UPDATE → affected count.</para>
    /// <para><b>Side Effects:</b> Writes a token onto every subscriber lacking one.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>Number of subscribers repaired.</returns>
    public async Task<int> EnsureUnsubscribeTokensAsync(CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(EnsureUnsubscribeTokensSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the subscriber owning an unsubscribe token, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The token is matched exactly — it is a generated secret, not a
    /// user-typed value, so a case-insensitive or trimmed match would only widen the guessing
    /// surface. An unknown token yields <c>null</c> so the page can say the link is invalid.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by token → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="unsubscribeToken">Token taken from an unsubscribe link.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The subscriber, or <c>null</c> when the token is unknown.</returns>
    public async Task<Subscriber?> GetSubscriberByUnsubscribeTokenAsync(
        string unsubscribeToken,
        CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<Subscriber>(
            SelectSubscriberByTokenSql,
            new { UnsubscribeToken = unsubscribeToken },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a subscriber as no longer receiving mail, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unsubscribing clears the confirmation flag rather than deleting
    /// the row, so the address stays known and a later re-subscribe is a confirmation rather than a
    /// fresh insert. Running it against an already-inactive subscriber affects nothing, which is what
    /// makes the unsubscribe link idempotent.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Clears <c>IsConfirmed</c> on one subscriber.</para>
    /// </remarks>
    /// <param name="subscriberId">Subscriber identifier.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the subscriber has been deactivated.</returns>
    public async Task DeactivateSubscriberAsync(long subscriberId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            DeactivateSubscriberSql, new { SubscriberId = subscriberId }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Synchronous listing required by <c>GenericRepository</c>; prefer <see cref="GetAllAsync"/>.
    /// </summary>
    /// <returns>All newsletter issues, newest created first.</returns>
    public override IEnumerable<Newsletter> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<Newsletter>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Newsletter issues have no parent entity, so this returns the full list.
    /// </summary>
    /// <param name="singleId">Unused; present to satisfy the base contract.</param>
    /// <returns>All newsletter issues.</returns>
    public override IEnumerable<Newsletter> GetAllById(long singleId) => GetAll();

    /// <summary>
    /// Synchronous paging required by <c>GenericRepository</c>; prefer
    /// <see cref="GetPublishedPageAsync"/> for the public archive.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offSet">Rows to skip.</param>
    /// <returns>One page of issues in any status.</returns>
    public override IEnumerable<Newsletter> GetPagedData(int pageSize, int offSet)
    {
        using var connection = GetOpenConnection();
        return connection
            .Query<Newsletter>(SelectPagedSql, new { PageSize = pageSize, OffSet = offSet })
            .ToList();
    }

    /// <summary>
    /// Synchronous single read required by <c>GenericRepository</c>; prefer
    /// <see cref="GetByIdAsync"/>.
    /// </summary>
    /// <param name="singleId">Issue identifier.</param>
    /// <returns>The issue, or null when it does not exist.</returns>
    public override Newsletter? GetSingle(long singleId)
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<Newsletter>(SelectByIdSql, new { NewsletterId = singleId });
    }

    /// <summary>
    /// Integer-keyed single read required by <c>GenericRepository</c>.
    /// </summary>
    /// <param name="singleId">Issue identifier.</param>
    /// <returns>The issue, or null when it does not exist.</returns>
    public override Newsletter? GetIntSingle(int singleId) => GetSingle(singleId);

    /// <summary>
    /// Synchronous insert required by <c>GenericRepository</c>; prefer <see cref="InsertAsync"/>.
    /// </summary>
    /// <param name="entity">The issue to persist.</param>
    public override void Insert(Newsletter entity)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Synchronous insert-and-return-id required by <c>GenericRepository</c>; prefer
    /// <see cref="InsertToGetIdAsync"/>.
    /// </summary>
    /// <param name="entity">The issue to persist.</param>
    /// <returns>The generated identifier.</returns>
    public override long InsertToGetId(Newsletter entity)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildWriteParameters(entity));
    }

    /// <summary>
    /// Synchronous update required by <c>GenericRepository</c>; prefer <see cref="UpdateAsync"/>.
    /// </summary>
    /// <remarks>
    /// Executes its own blocking statement rather than blocking on <see cref="UpdateAsync"/>: waiting
    /// on a task inside a Blazor Server circuit is a deadlock risk, and it would make this interim
    /// state worse than the one it replaces.
    /// </remarks>
    /// <param name="entityToUpdate">The issue carrying the new values.</param>
    public override void Update(Newsletter entityToUpdate)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(entityToUpdate));
    }

    // =================================================================================================
    // Shared internals.
    // =================================================================================================

    /// <summary>
    /// Runs a previous/next neighbour query against a send timestamp.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both neighbour lookups differ only in comparison direction, so
    /// they share one execution path.</para>
    /// <para><b>Flow:</b> normalise and bind the timestamp → run the caller's SQL → return the single
    /// row or null.</para>
    /// <para><b>Side Effects:</b> None; read-only.</para>
    /// </remarks>
    /// <param name="sql">The neighbour query, already carrying the published predicate.</param>
    /// <param name="sentOn">The current issue's send time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The neighbouring issue, or null at an end of the archive.</returns>
    private async Task<Newsletter?> GetNeighbourAsync(
        string sql,
        DateTime sentOn,
        CancellationToken cancellationToken)
    {
        return await QueryFirstOrDefaultAsync<Newsletter>(
            sql, new { SentOn = DbTimestamp.AsTimestamp(sentOn) }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds the shared parameter set for newsletter inserts and updates.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Insert and update write the same editable columns, so the
    /// binding lives in one place. A missing creation time is stamped here so an issue can never be
    /// stored with the zero date. Timestamps are normalised to <c>Kind = Unspecified</c> because the
    /// columns are <c>TIMESTAMP</c> without a time zone.</para>
    /// <para><b>Flow:</b> copy the editable fields onto a <c>DynamicParameters</c> instance.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="newsletter">The issue being written.</param>
    /// <returns>Parameters for the write statement.</returns>
    private static DynamicParameters BuildWriteParameters(Newsletter newsletter)
    {
        ArgumentNullException.ThrowIfNull(newsletter);

        var parameters = new DynamicParameters();
        parameters.Add("Title", newsletter.Title);
        parameters.Add("Content", newsletter.Content);
        parameters.Add("Summary", newsletter.Summary ?? string.Empty);
        parameters.Add("CreatedOn", DbTimestamp.AsTimestamp(
            newsletter.CreatedOn == default ? DateTime.UtcNow : newsletter.CreatedOn));
        parameters.Add("ScheduledFor", DbTimestamp.AsTimestamp(newsletter.ScheduledFor));
        parameters.Add("Status", newsletter.Status);
        parameters.Add("IsPublic", newsletter.IsPublic);
        return parameters;
    }

    /// <summary>
    /// Extends the shared write parameters with the key and modification stamp an update needs.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>UpdatedOn</c> is stamped by the repository rather than the
    /// caller so both the async and the synchronous update path record the same thing.</para>
    /// <para><b>Flow:</b> build the shared set → add the key and the modification stamp.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="newsletter">The issue being updated.</param>
    /// <returns>Parameters for the update statement.</returns>
    private static DynamicParameters BuildUpdateParameters(Newsletter newsletter)
    {
        var parameters = BuildWriteParameters(newsletter);
        parameters.Add("NewsletterId", newsletter.NewsletterId);
        parameters.Add("UpdatedOn", DbTimestamp.AsTimestamp(DateTime.UtcNow));
        return parameters;
    }
}
