using BlogEngine.DaCore;
using BlogModels;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing blog series data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for BlogSeries entities using Dapper.</para>
///
/// <para><b>Code Flow:</b> <c>SeriesSvc</c> injects this repository, calls an <c>…Async</c> member,
/// and the member routes through the protected helpers on <c>GenericRepository</c>, which open the
/// connection asynchronously and flow the cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage.</para>
///
/// <para><b>Async conversion (REQ-NFR-026, Group C).</b> Every SQL statement is hoisted into a
/// <c>private const</c> shared by both twins, so the async version cannot drift from the synchronous
/// one it replaces. <c>CreatedOn</c> and <c>UpdatedOn</c> are bound through
/// <see cref="DbTimestamp.AsTimestamp(DateTime)"/>: the columns are <c>TIMESTAMP</c> without time
/// zone, and the <c>Kind = Utc</c> values <c>SeriesSvc</c> supplies would otherwise be sent as
/// <c>timestamptz</c> and re-interpreted through the session time zone.</para>
/// </remarks>
public class BlogSeriesRepo : GenericRepository<BlogSeries>, IBlogSeriesRepo
{
    private const string SelectAllSql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            ORDER BY s.Name";

    private const string SelectAllWithCountsSql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName,
                   COUNT(p.PostID) as PostCount
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            LEFT JOIN BlogPost p ON s.SeriesId = p.SeriesId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            GROUP BY s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                     s.AuthorId, s.CreatedOn, s.UpdatedOn, u.FirstName, u.LastName
            ORDER BY s.Name";

    private const string SelectByAuthorSql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            WHERE s.AuthorId = @AuthorId
            ORDER BY s.Name";

    private const string SelectByIdSql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            WHERE s.SeriesId = @SeriesId";

    private const string SelectBySlugSql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName,
                   COUNT(p.PostID) as PostCount
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            LEFT JOIN BlogPost p ON s.SeriesId = p.SeriesId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            WHERE s.Slug = @Slug
            GROUP BY s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                     s.AuthorId, s.CreatedOn, s.UpdatedOn, u.FirstName, u.LastName";

    private const string CountBySlugSql = @"
            SELECT COUNT(1) FROM BlogSeries
            WHERE Slug = @Slug AND SeriesId != @ExcludeSeriesId";

    private const string SelectPagedSql = @"
            SELECT s.SeriesId, s.Name, s.Slug, s.Description, s.Status,
                   s.AuthorId, s.CreatedOn, s.UpdatedOn,
                   CONCAT(u.FirstName, ' ', u.LastName) as AuthorName
            FROM BlogSeries s
            LEFT JOIN BlogUser u ON s.AuthorId = u.UserId
            ORDER BY s.Name
            LIMIT @PageSize OFFSET @Offset";

    private const string InsertSql = @"
            INSERT INTO BlogSeries (Name, Slug, Description, Status, AuthorId, CreatedOn, UpdatedOn)
            VALUES (@Name, @Slug, @Description, @Status, @AuthorId, @CreatedOn, @UpdatedOn)";

    private const string InsertReturningIdSql = InsertSql + @"
            RETURNING SeriesId";

    private const string UpdateSql = @"
            UPDATE BlogSeries SET
                Name = @Name,
                Slug = @Slug,
                Description = @Description,
                Status = @Status,
                UpdatedOn = @UpdatedOn
            WHERE SeriesId = @SeriesId";

    private const string DeleteSql = "DELETE FROM BlogSeries WHERE SeriesId = @SeriesId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public BlogSeriesRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets all series ordered by name, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The author is joined in for a display name, as a LEFT JOIN so a
    /// series whose author has been removed still appears rather than vanishing from the admin list.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → left join → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All series, alphabetically.</returns>
    public override async Task<IEnumerable<BlogSeries>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogSeries>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all series with their published-post counts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The count covers published, non-deleted posts only, so the "N
    /// parts" a reader sees equals the parts they can actually open. The LEFT JOIN keeps an empty
    /// series visible, with a count of zero.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → grouped left join → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Series with the computed PostCount field.</returns>
    public async Task<IEnumerable<BlogSeries>> GetAllWithCountsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogSeries>(SelectAllWithCountsSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets every series belonging to one author, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unlike categories and tags, the generic parent key means something
    /// here — a series has an owning author — so this is a real filter rather than a pass-through.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → filter by author → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="authorId">The owning author's user identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The author's series, alphabetically.</returns>
    public override async Task<IEnumerable<BlogSeries>> GetAllByIdAsync(long authorId, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogSeries>(
            SelectByAuthorSql, new { AuthorId = authorId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single series by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.
    /// <c>PostCount</c> is deliberately not computed here — the editor that uses this lookup fetches
    /// the parts themselves, so counting them again would be a wasted aggregate.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The series, or <c>null</c> when no row carries that key.</returns>
    public override async Task<BlogSeries?> GetSingleAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogSeries>(
            SelectByIdSql, new { SeriesId = seriesId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single series by INT ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The series, or <c>null</c> when no row carries that key.</returns>
    public override Task<BlogSeries?> GetIntSingleAsync(int seriesId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(seriesId, cancellationToken);
    }

    /// <summary>
    /// Gets a series by its URL slug, including its published-post count, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Resolves the <c>/series/{slug}</c> route. The projection counts
    /// only <i>published, not-deleted</i> posts, so the part count shown to a reader matches the parts
    /// they can actually open. The left joins keep an authorless or empty series visible rather than
    /// dropping the row.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → grouped left join filtered by
    /// slug → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// <para><b>History:</b> This projection previously omitted <c>PostCount</c>, so the series detail
    /// page always rendered "0 Parts" while the <c>/series</c> listing (which uses
    /// <see cref="GetAllWithCountsAsync"/>) was correct (REQ-FN-019 / REQ-UI-010).</para>
    /// </remarks>
    /// <param name="slug">URL-friendly series identifier taken from the route.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The series with <c>PostCount</c> populated, or <c>null</c> when the slug is unknown.</returns>
    public async Task<BlogSeries?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<BlogSeries>(
            SelectBySlugSql, new { Slug = slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a series slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Slugs must be unique because they address a page. The exclusion
    /// parameter lets an update ignore the row being edited, so re-saving a series without renaming it
    /// does not collide with itself.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeSeriesId">Series ID to exclude, for updates.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another series already uses the slug.</returns>
    public async Task<bool> SlugExistsAsync(string slug, long excludeSeriesId = 0, CancellationToken cancellationToken = default)
    {
        var matches = await ExecuteScalarAsync<int>(
            CountBySlugSql, new { Slug = slug, ExcludeSeriesId = excludeSeriesId }, cancellationToken).ConfigureAwait(false);

        return matches > 0;
    }

    /// <summary>
    /// Gets a page of series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a large catalogue never crosses the
    /// wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<BlogSeries>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<BlogSeries>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form. Both timestamps are normalised so Npgsql sends
    /// <c>timestamp</c> and PostgreSQL does not shift the instant through the session time zone.</para>
    /// <para><b>Flow:</b> normalise timestamps → helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>BlogSeries</c>.</para>
    /// </remarks>
    /// <param name="series">The series to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(BlogSeries series, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(InsertSql, BuildInsertParameters(series), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a series and returns the generated ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key. Shares its parameter set and its statement text
    /// with <see cref="InsertAsync"/>, so the two insert paths cannot drift apart.</para>
    /// <para><b>Flow:</b> normalise timestamps → helper opens the connection asynchronously → INSERT … RETURNING → read the key.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>BlogSeries</c>.</para>
    /// </remarks>
    /// <param name="series">The series to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>SeriesId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(BlogSeries series, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql, BuildInsertParameters(series), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The author and the creation timestamp are absent from the statement
    /// on purpose — an edit never reassigns ownership or rewrites when the series was started.</para>
    /// <para><b>Flow:</b> normalise the timestamp → helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>SeriesId</c>.</para>
    /// </remarks>
    /// <param name="series">The series carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(BlogSeries series, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(UpdateSql, BuildUpdateParameters(series), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a series by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Callers detach the series' posts first
    /// (<c>BlogPostRepo.ClearSeriesFromPostsAsync</c>) so the posts survive as standalone articles.
    /// Deleting an unknown identifier affects no rows and is treated as a no-op.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row from <c>BlogSeries</c>.</para>
    /// </remarks>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    public async Task DeleteAsync(long seriesId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(DeleteSql, new { SeriesId = seriesId }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets all series ordered by name.
    /// </summary>
    /// <returns>All series, alphabetically.</returns>
    public override IEnumerable<BlogSeries> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogSeries>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets all series with their published-post counts.
    /// </summary>
    /// <returns>Series with the computed PostCount field.</returns>
    public IEnumerable<BlogSeries> GetAllWithCounts()
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogSeries>(SelectAllWithCountsSql).ToList();
    }

    /// <summary>
    /// Gets every series belonging to one author.
    /// </summary>
    /// <param name="authorId">The owning author's user identifier.</param>
    /// <returns>The author's series, alphabetically.</returns>
    public override IEnumerable<BlogSeries> GetAllById(long authorId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogSeries>(SelectByAuthorSql, new { AuthorId = authorId }).ToList();
    }

    /// <summary>
    /// Gets a single series by ID.
    /// </summary>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The series, or <c>null</c> when not found.</returns>
    public override BlogSeries? GetSingle(long seriesId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogSeries>(SelectByIdSql, new { SeriesId = seriesId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single series by INT ID.
    /// </summary>
    /// <param name="seriesId">The series identifier.</param>
    /// <returns>The series, or <c>null</c> when not found.</returns>
    public override BlogSeries? GetIntSingle(int seriesId)
    {
        return GetSingle(seriesId);
    }

    /// <summary>
    /// Gets a series by its URL slug, including its published-post count.
    /// </summary>
    /// <param name="slug">URL-friendly series identifier taken from the route.</param>
    /// <returns>The series with <c>PostCount</c> populated, or <c>null</c> when the slug is unknown.</returns>
    public BlogSeries? GetBySlug(string slug)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogSeries>(SelectBySlugSql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Checks whether a series slug is already taken.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeSeriesId">Series ID to exclude, for updates.</param>
    /// <returns><c>true</c> when another series already uses the slug.</returns>
    public bool SlugExists(string slug, long excludeSeriesId = 0)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(CountBySlugSql, new { Slug = slug, ExcludeSeriesId = excludeSeriesId }) > 0;
    }

    /// <summary>
    /// Gets a page of series.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<BlogSeries> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<BlogSeries>(SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new series.
    /// </summary>
    /// <param name="series">The series to persist.</param>
    public override void Insert(BlogSeries series)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, BuildInsertParameters(series));
    }

    /// <summary>
    /// Inserts a series and returns the generated ID.
    /// </summary>
    /// <param name="series">The series to persist.</param>
    /// <returns>The generated <c>SeriesId</c>.</returns>
    public override long InsertToGetId(BlogSeries series)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(InsertReturningIdSql, BuildInsertParameters(series));
    }

    /// <summary>
    /// Updates an existing series.
    /// </summary>
    /// <param name="series">The series carrying the new values.</param>
    public override void Update(BlogSeries series)
    {
        using var connection = GetOpenConnection();
        connection.Execute(UpdateSql, BuildUpdateParameters(series));
    }

    /// <summary>
    /// Deletes a series by ID.
    /// </summary>
    /// <param name="seriesId">The series identifier.</param>
    public void Delete(long seriesId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteSql, new { SeriesId = seriesId });
    }

    // =================================================================================================
    // Parameter builders — shared by both twins so the sync and async write paths cannot diverge.
    // =================================================================================================

    /// <summary>
    /// Builds the parameter set both insert statements bind.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>CreatedOn</c> and <c>UpdatedOn</c> pass through
    /// <see cref="DbTimestamp.AsTimestamp(DateTime)"/> because their columns are <c>TIMESTAMP</c>
    /// without time zone; binding the <c>Kind = Utc</c> value <c>SeriesSvc</c> supplies would make
    /// Npgsql send <c>timestamptz</c> and PostgreSQL convert it through the session time zone.</para>
    /// <para><b>Flow:</b> normalise both timestamps → project the remaining columns unchanged.</para>
    /// <para><b>Side Effects:</b> None — the series itself is not mutated.</para>
    /// </remarks>
    /// <param name="series">The series being persisted.</param>
    /// <returns>An anonymous parameter object matching the insert statements.</returns>
    private static object BuildInsertParameters(BlogSeries series)
    {
        return new
        {
            series.Name,
            series.Slug,
            series.Description,
            series.Status,
            series.AuthorId,
            CreatedOn = DbTimestamp.AsTimestamp(series.CreatedOn),
            UpdatedOn = DbTimestamp.AsTimestamp(series.UpdatedOn)
        };
    }

    /// <summary>
    /// Builds the parameter set the update statement binds.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same timestamp normalisation as
    /// <see cref="BuildInsertParameters"/>; <c>AuthorId</c> and <c>CreatedOn</c> are absent because an
    /// edit never reassigns ownership or rewrites the creation time.</para>
    /// <para><b>Flow:</b> normalise the timestamp → project the editable columns.</para>
    /// <para><b>Side Effects:</b> None — the series itself is not mutated.</para>
    /// </remarks>
    /// <param name="series">The series being updated.</param>
    /// <returns>An anonymous parameter object matching the update statement.</returns>
    private static object BuildUpdateParameters(BlogSeries series)
    {
        return new
        {
            series.SeriesId,
            series.Name,
            series.Slug,
            series.Description,
            series.Status,
            UpdatedOn = DbTimestamp.AsTimestamp(series.UpdatedOn)
        };
    }
}
