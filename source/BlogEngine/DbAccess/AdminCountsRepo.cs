using BlogEngine.DaCore;
using BlogModels;
using BlogModels.Interfaces;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Dapper repository supplying every aggregate count on the admin dashboard.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Completes BRD-62. The dashboard previously rendered hardcoded constants
/// because no single query produced all of its tiles; this repository is that query.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>DashboardSvc</c> calls <see cref="GetAdminCountsAsync"/>.</item>
///   <item>One statement of scalar sub-selects reads content, taxonomy, identity, engagement,
///         subscriber, newsletter and view counts in a single round trip.</item>
///   <item>Every count is cast to <c>int</c> in SQL so PostgreSQL's <c>bigint</c> results map onto
///         the model's <c>int</c> properties without a Dapper conversion error.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Dapper, Npgsql (via <c>DbConnectionFactory</c>), and the
/// <c>BlogPost</c>, <c>BlogComment</c>, <c>BlogUser</c>, <c>Tag</c>, <c>Category</c>,
/// <c>BlogImage</c>, <c>Subscriber</c>, <c>Newsletter</c> and <c>PostViews</c> tables.</para>
///
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c> as <c>IAdminCountsRepo</c>.
/// This supersedes the partial <c>IBlogCommentRepo.GetAdminCounts</c>, which remains for backward
/// compatibility.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> <see cref="GetAdminCountsAsync"/> already returned a
/// task but opened its connection with the blocking <c>GetOpenConnection()</c>, so the whole
/// connection handshake ran on a parked thread-pool thread before the sixteen sub-selects started.
/// The generic members were worse: they blocked on the async member with
/// <c>.GetAwaiter().GetResult()</c>, which inside a Blazor Server circuit is a deadlock risk. They now
/// execute the same SQL constant directly, and the async members go through the protected helpers.
/// The counts statement takes no parameters, so the <c>42883</c> timestamp trap does not apply here —
/// the "this month" boundary is computed by PostgreSQL itself via
/// <see cref="StartOfMonthExpression"/> rather than bound from a .NET <c>DateTime</c>, which is what
/// keeps it out of that trap's reach.</para>
/// </remarks>
public class AdminCountsRepo : GenericRepository<AdminCounts>, IAdminCountsRepo
{
    /// <summary>
    /// Restricts post counts to content that has not been soft-deleted.
    /// </summary>
    private const string LivePostPredicate = "(IsDeleted = FALSE OR IsDeleted IS NULL)";

    /// <summary>
    /// First instant of the current calendar month, used by the "this month" sub-labels.
    /// </summary>
    private const string StartOfMonthExpression = "DATE_TRUNC('month', CURRENT_TIMESTAMP)";

    /// <summary>
    /// The single aggregate statement behind the dashboard tiles.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> "Active" subscribers are those whose <c>IsConfirmed</c> flag is
    /// true, matching how <c>SubscriberSvc</c> already treats subscribe and unsubscribe. The month
    /// boundary is evaluated server-side, so the figure cannot disagree with the database's own clock.</para>
    /// <para><b>Flow:</b> one statement, no parameters, one round trip.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    private const string SelectCountsSql = $@"
            SELECT
                (SELECT COUNT(*)::int FROM BlogPost WHERE {LivePostPredicate}) AS BlogCount,
                (SELECT COUNT(*)::int FROM BlogPost WHERE Published = TRUE AND {LivePostPredicate}) AS PublishedPostCount,
                (SELECT COUNT(*)::int FROM BlogPost WHERE Published = FALSE AND {LivePostPredicate}) AS DraftPostCount,
                (SELECT COUNT(*)::int FROM BlogComment) AS CommentCount,
                (SELECT COUNT(*)::int FROM BlogComment WHERE Published = FALSE) AS UnAppComments,
                (SELECT COUNT(*)::int FROM BlogUser) AS UserCount,
                (SELECT COUNT(*)::int FROM Tag) AS TagCount,
                (SELECT COUNT(*)::int FROM Category) AS CategoryCount,
                (SELECT COUNT(*)::int FROM BlogImage) AS ImageCount,
                (SELECT COUNT(*)::int FROM Subscriber) AS SubscriberCount,
                (SELECT COUNT(*)::int FROM Subscriber WHERE COALESCE(IsConfirmed, FALSE) = TRUE) AS ActiveSubscriberCount,
                (SELECT COUNT(*)::int FROM Newsletter) AS NewsletterCount,
                (SELECT COUNT(*)::int FROM Newsletter WHERE Status = 'sent') AS SentNewsletterCount,
                (SELECT COUNT(*)::int FROM PostViews) AS TotalPostViews,
                (SELECT COUNT(*)::int FROM BlogUser WHERE CreatedOn >= {StartOfMonthExpression}) AS NewUsersThisMonth,
                (SELECT COUNT(*)::int FROM Subscriber WHERE SubscribedOn >= {StartOfMonthExpression}) AS NewSubscribersThisMonth";

    /// <summary>
    /// Message shared by every unsupported write member.
    /// </summary>
    private const string ComputedOnlyMessage = "Admin counts are computed from source tables.";

    /// <summary>
    /// Initializes the repository with the PostgreSQL connection string.
    /// </summary>
    /// <param name="connectionString">Connection string supplied by <c>BlogSvcInitializer</c>.</param>
    public AdminCountsRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Reads every aggregate count the admin dashboard displays, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Sixteen figures in one statement, so no two tiles can describe
    /// different instants — the reason this is one query rather than sixteen. An empty result is not
    /// reachable for a statement made only of scalar sub-selects; the null coalesce keeps the "zeroes
    /// on an empty database" contract literal rather than merely likely.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → one round trip → the counts row
    /// or a zeroed value.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A fully populated <c>AdminCounts</c>; zeroes on an empty database.</returns>
    public async Task<AdminCounts> GetAdminCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await QueryFirstOrDefaultAsync<AdminCounts>(SelectCountsSql, null, cancellationToken)
            .ConfigureAwait(false);
        return counts ?? new AdminCounts();
    }

    /// <summary>
    /// Returns the single counts row, since counts are a projection rather than a table.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> There is exactly one counts row by construction, so "all" is a
    /// one-element sequence.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAdminCountsAsync"/> → wrap in a sequence.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A one-element sequence carrying the counts.</returns>
    public override async Task<IEnumerable<AdminCounts>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return new[] { await GetAdminCountsAsync(cancellationToken).ConfigureAwait(false) };
    }

    /// <summary>
    /// Counts have no parent entity; returns the single counts row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The identifier is meaningless here and is ignored deliberately
    /// rather than validated, because the generic contract requires the member to exist.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAllAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Unused; present to satisfy the base contract.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A one-element sequence carrying the counts.</returns>
    public override Task<IEnumerable<AdminCounts>> GetAllByIdAsync(
        long singleId, CancellationToken cancellationToken = default)
    {
        return GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// Counts are not pageable; returns the single counts row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One row cannot be paged, so both paging arguments are ignored.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAllAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Unused.</param>
    /// <param name="offSet">Unused.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>A one-element sequence carrying the counts.</returns>
    public override Task<IEnumerable<AdminCounts>> GetPagedDataAsync(
        int pageSize, int offSet, CancellationToken cancellationToken = default)
    {
        return GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// Returns the counts; there is only ever one row, so the id is ignored.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> See <see cref="GetAllByIdAsync"/>.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAdminCountsAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Unused.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The populated counts.</returns>
    public override async Task<AdminCounts?> GetSingleAsync(
        long singleId, CancellationToken cancellationToken = default)
    {
        return await GetAdminCountsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the counts; there is only ever one row, so the id is ignored.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> See <see cref="GetAllByIdAsync"/>.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="singleId">Unused.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The populated counts.</returns>
    public override Task<AdminCounts?> GetIntSingleAsync(int singleId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(singleId, cancellationToken);
    }

    /// <summary>
    /// Counts are computed, never inserted.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A count is derived; writing one would create a figure no source
    /// table supports.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entity">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override Task InsertAsync(AdminCounts entity, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Counts are computed, never inserted.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> See <see cref="InsertAsync"/>.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entity">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override Task<long> InsertToGetIdAsync(AdminCounts entity, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Counts are computed, never updated.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> See <see cref="InsertAsync"/>.</para>
    /// <para><b>Flow:</b> throw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entityToUpdate">Unused.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override Task UpdateAsync(AdminCounts entityToUpdate, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift, and none of
    // them blocks on a task: doing that inside a Blazor Server circuit risks a deadlock (trap 7).
    // =================================================================================================

    /// <summary>
    /// Returns the single counts row, since counts are a projection rather than a table.
    /// </summary>
    /// <returns>A one-element sequence carrying the counts.</returns>
    public override IEnumerable<AdminCounts> GetAll() => new[] { ReadCounts() };

    /// <summary>
    /// Counts have no parent entity; returns the single counts row.
    /// </summary>
    /// <param name="singleId">Unused; present to satisfy the base contract.</param>
    /// <returns>A one-element sequence carrying the counts.</returns>
    public override IEnumerable<AdminCounts> GetAllById(long singleId) => GetAll();

    /// <summary>
    /// Counts are not pageable; returns the single counts row.
    /// </summary>
    /// <param name="pageSize">Unused.</param>
    /// <param name="offSet">Unused.</param>
    /// <returns>A one-element sequence carrying the counts.</returns>
    public override IEnumerable<AdminCounts> GetPagedData(int pageSize, int offSet) => GetAll();

    /// <summary>
    /// Returns the counts; there is only ever one row, so the id is ignored.
    /// </summary>
    /// <param name="singleId">Unused.</param>
    /// <returns>The populated counts.</returns>
    public override AdminCounts? GetSingle(long singleId) => ReadCounts();

    /// <summary>
    /// Returns the counts; there is only ever one row, so the id is ignored.
    /// </summary>
    /// <param name="singleId">Unused.</param>
    /// <returns>The populated counts.</returns>
    public override AdminCounts? GetIntSingle(int singleId) => GetSingle(singleId);

    /// <summary>
    /// Counts are computed, never inserted.
    /// </summary>
    /// <param name="entity">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Insert(AdminCounts entity) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Counts are computed, never inserted.
    /// </summary>
    /// <param name="entity">Unused.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override long InsertToGetId(AdminCounts entity) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Counts are computed, never updated.
    /// </summary>
    /// <param name="entityToUpdate">Unused.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Update(AdminCounts entityToUpdate) =>
        throw new NotSupportedException(ComputedOnlyMessage);

    /// <summary>
    /// Runs the counts statement synchronously for the legacy generic members.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Shares <see cref="SelectCountsSql"/> with the async member, so the
    /// two twins can never report different figures.</para>
    /// <para><b>Flow:</b> open a connection → one round trip → the counts row or a zeroed value.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <returns>The populated counts, or a zeroed value.</returns>
    private AdminCounts ReadCounts()
    {
        using var connection = GetOpenConnection();
        return connection.QueryFirstOrDefault<AdminCounts>(SelectCountsSql) ?? new AdminCounts();
    }
}
