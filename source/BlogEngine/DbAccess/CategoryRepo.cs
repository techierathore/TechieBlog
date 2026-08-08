using BlogModels;
using Dapper;

namespace BlogEngine.DbAccess;

/// <summary>
/// Repository for managing category data access operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides CRUD operations for Category entities using Dapper.</para>
///
/// <para><b>Code Flow:</b> <c>CategorySvc</c> injects this repository, calls an <c>…Async</c> member,
/// and the member routes through the protected helpers on <c>GenericRepository</c>, which open the
/// connection asynchronously and flow the cancellation token into the Dapper command.</para>
///
/// <para><b>Dependencies:</b> GenericRepository, Dapper, PostgreSQL.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only until
/// the last caller migrates (REQ-NFR-026) and are deleted in the final stage.</para>
///
/// <para><b>Reference implementation (REQ-NFR-026).</b> This repository is the worked example the
/// rest of the conversion follows — see <c>docs/async-conversion-pattern.md</c>. Three things about
/// its shape are deliberate and are the pattern, not incidental style:</para>
/// <list type="number">
///   <item><b>The SQL lives in one <c>const</c> per statement.</b> Both twins execute the same string,
///   so the async version cannot drift away from the synchronous one it is replacing, and deleting
///   the synchronous twin later removes only the method, never the query.</item>
///   <item><b>Every async member is a real override</b> that awaits Dapper. Inheriting the base
///   class's temporary bridge would compile, pass every test and still block a thread-pool thread —
///   the exact failure this requirement exists to remove.</item>
///   <item><b>The synchronous twins were left byte-for-byte equivalent</b> rather than re-expressed on
///   top of the async ones. Blocking on a task (<c>.Result</c>, <c>.GetAwaiter().GetResult()</c>)
///   inside a Blazor Server circuit is a deadlock risk and would have made the interim state worse
///   than the state it replaces.</item>
/// </list>
/// </remarks>
public class CategoryRepo : GenericRepository<Category>, ICategoryRepo
{
    private const string SelectAllSql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            ORDER BY CategoryName";

    private const string SelectAllWithCountsSql = @"
            SELECT c.CategoryId, c.CategoryName, c.Slug, c.Description,
                   COUNT(p.PostID) as PostCount
            FROM Category c
            LEFT JOIN BlogPost p ON c.CategoryId = p.CategoryId
                AND p.Published = TRUE
                AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)
            GROUP BY c.CategoryId, c.CategoryName, c.Slug, c.Description
            ORDER BY c.CategoryName";

    private const string SelectByIdSql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            WHERE CategoryId = @CategoryId";

    private const string SelectBySlugSql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            WHERE Slug = @Slug";

    private const string CountBySlugSql = @"
            SELECT COUNT(1) FROM Category
            WHERE Slug = @Slug AND CategoryId != @ExcludeCategoryId";

    private const string SelectPagedSql = @"
            SELECT CategoryId, CategoryName, Slug, Description
            FROM Category
            ORDER BY CategoryName
            LIMIT @PageSize OFFSET @Offset";

    private const string InsertSql = @"
            INSERT INTO Category (CategoryName, Slug, Description)
            VALUES (@CategoryName, @Slug, @Description)";

    private const string InsertReturningIdSql = @"
            INSERT INTO Category (CategoryName, Slug, Description)
            VALUES (@CategoryName, @Slug, @Description)
            RETURNING CategoryId";

    private const string UpdateSql = @"
            UPDATE Category SET
                CategoryName = @CategoryName,
                Slug = @Slug,
                Description = @Description
            WHERE CategoryId = @CategoryId";

    private const string DeleteSql = "DELETE FROM Category WHERE CategoryId = @CategoryId";

    /// <summary>
    /// Initialises the repository with the application connection string.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    public CategoryRepo(string connectionString) : base(connectionString) { }

    // =================================================================================================
    // Async surface — the members every caller should use.
    // =================================================================================================

    /// <summary>
    /// Gets all categories ordered by name, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Alphabetical order is the browsing order the sidebar and the admin
    /// grid both present, so it is applied in SQL rather than left to each caller.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → buffered query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All categories, or an empty sequence when none exist.</returns>
    public override async Task<IEnumerable<Category>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Category>(SelectAllSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all categories with their post counts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The count covers published, non-deleted posts only — a draft or a
    /// soft-deleted post must not inflate a number a reader can act on. The join is a LEFT JOIN so an
    /// empty category still appears, with a count of zero.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → grouped left join → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Categories with the computed PostCount field.</returns>
    public async Task<IEnumerable<Category>> GetAllWithCountsAsync(CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Category>(SelectAllWithCountsSql, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets all categories for a parent ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Categories are a flat list in this schema — there is no parent
    /// relationship — so the parent filter is ignored and the whole set is returned. The member exists
    /// only to satisfy the generic contract.</para>
    /// <para><b>Flow:</b> delegate to <see cref="GetAllAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="parentId">Ignored; categories have no parent.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All categories.</returns>
    public override Task<IEnumerable<Category>> GetAllByIdAsync(long parentId, CancellationToken cancellationToken = default)
    {
        return GetAllAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a single category by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unknown identifier is a normal answer and yields <c>null</c>.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by key → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The category, or <c>null</c> when no row carries that key.</returns>
    public override async Task<Category?> GetSingleAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<Category>(
            SelectByIdSql, new { CategoryId = categoryId }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets a single category by INT ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Widens the key and reuses the BIGINT lookup; the column is
    /// <c>BIGINT</c>, so there is no second query to write.</para>
    /// <para><b>Flow:</b> widen → delegate to <see cref="GetSingleAsync"/>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The category, or <c>null</c> when no row carries that key.</returns>
    public override Task<Category?> GetIntSingleAsync(int categoryId, CancellationToken cancellationToken = default)
    {
        return GetSingleAsync(categoryId, cancellationToken);
    }

    /// <summary>
    /// Gets a category by its URL slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The slug is the public identifier used by <c>/category/{slug}</c>,
    /// so an unknown slug must return <c>null</c> for the page to render its 404 rather than throw.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → query by slug → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The category, or <c>null</c> when the slug is unknown.</returns>
    public async Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await QueryFirstOrDefaultAsync<Category>(
            SelectBySlugSql, new { Slug = slug }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks whether a slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Slugs must be unique because they address a page. The exclusion
    /// parameter lets an update ignore the row being edited, so re-saving a category without renaming
    /// it does not collide with itself.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeCategoryId">Category ID to exclude, for updates.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another category already uses the slug.</returns>
    public async Task<bool> SlugExistsAsync(string slug, long excludeCategoryId = 0, CancellationToken cancellationToken = default)
    {
        var matches = await ExecuteScalarAsync<int>(
            CountBySlugSql,
            new { Slug = slug, ExcludeCategoryId = excludeCategoryId },
            cancellationToken).ConfigureAwait(false);

        return matches > 0;
    }

    /// <summary>
    /// Gets a page of categories, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paging is applied in SQL so a large taxonomy never crosses the
    /// wire in full.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → LIMIT/OFFSET query → materialised list.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The requested page, or an empty sequence when the offset is past the end.</returns>
    public override async Task<IEnumerable<Category>> GetPagedDataAsync(int pageSize, int offset, CancellationToken cancellationToken = default)
    {
        return await QueryAsync<Category>(
            SelectPagedSql, new { PageSize = pageSize, Offset = offset }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a new category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The caller does not need the generated key here, so the plain
    /// INSERT is used rather than the RETURNING form.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute INSERT.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Category</c>.</para>
    /// </remarks>
    /// <param name="category">The category to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task InsertAsync(Category category, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            InsertSql,
            new { category.CategoryName, category.Slug, category.Description },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Inserts a category and returns the generated ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> PostgreSQL returns the identity from the INSERT itself, so no
    /// second round trip is needed to learn the key.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → INSERT … RETURNING → read scalar.</para>
    /// <para><b>Side Effects:</b> Adds one row to <c>Category</c>.</para>
    /// </remarks>
    /// <param name="category">The category to persist.</param>
    /// <param name="cancellationToken">Cancels the insert.</param>
    /// <returns>The generated <c>CategoryId</c>.</returns>
    public override async Task<long> InsertToGetIdAsync(Category category, CancellationToken cancellationToken = default)
    {
        return await QuerySingleAsync<long>(
            InsertReturningIdSql,
            new { category.CategoryName, category.Slug, category.Description },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates an existing category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> All three editable fields are written together; the key is never
    /// updated.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute UPDATE.</para>
    /// <para><b>Side Effects:</b> Updates one row, matched on <c>CategoryId</c>.</para>
    /// </remarks>
    /// <param name="category">The category carrying the new values.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the row has been written.</returns>
    public override async Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(
            UpdateSql,
            new { category.CategoryId, category.CategoryName, category.Slug, category.Description },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a category by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deleting an unknown identifier affects no rows and is treated as a
    /// no-op rather than an error, so a double submit is harmless.</para>
    /// <para><b>Flow:</b> helper opens the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row; its posts become uncategorised.</para>
    /// </remarks>
    /// <param name="categoryId">The category identifier.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been removed.</returns>
    public async Task DeleteAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(DeleteSql, new { CategoryId = categoryId }, cancellationToken).ConfigureAwait(false);
    }

    // =================================================================================================
    // Legacy blocking surface — REQ-NFR-026 deletes these once every caller has migrated.
    // Each one executes the same SQL constant as its async twin, so the two cannot drift.
    // =================================================================================================

    /// <summary>
    /// Gets all categories ordered by name.
    /// </summary>
    /// <returns>All categories.</returns>
    public override IEnumerable<Category> GetAll()
    {
        using var connection = GetOpenConnection();
        return connection.Query<Category>(SelectAllSql).ToList();
    }

    /// <summary>
    /// Gets all categories with their post counts.
    /// </summary>
    /// <returns>Categories with the computed PostCount field.</returns>
    public IEnumerable<Category> GetAllWithCounts()
    {
        using var connection = GetOpenConnection();
        return connection.Query<Category>(SelectAllWithCountsSql).ToList();
    }

    /// <summary>
    /// Gets all categories by parent ID (not used for flat categories).
    /// </summary>
    /// <param name="parentId">Ignored; categories have no parent.</param>
    /// <returns>All categories.</returns>
    public override IEnumerable<Category> GetAllById(long parentId)
    {
        return GetAll();
    }

    /// <summary>
    /// Gets a single category by ID.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <returns>The category, or <c>null</c> when not found.</returns>
    public override Category? GetSingle(long categoryId)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Category>(SelectByIdSql, new { CategoryId = categoryId }).FirstOrDefault();
    }

    /// <summary>
    /// Gets a single category by INT ID.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    /// <returns>The category, or <c>null</c> when not found.</returns>
    public override Category? GetIntSingle(int categoryId)
    {
        return GetSingle(categoryId);
    }

    /// <summary>
    /// Gets a category by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>The category, or <c>null</c> when the slug is unknown.</returns>
    public Category? GetBySlug(string slug)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Category>(SelectBySlugSql, new { Slug = slug }).FirstOrDefault();
    }

    /// <summary>
    /// Checks if a slug already exists.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeCategoryId">Category ID to exclude, for updates.</param>
    /// <returns><c>true</c> when another category already uses the slug.</returns>
    public bool SlugExists(string slug, long excludeCategoryId = 0)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<int>(
            CountBySlugSql, new { Slug = slug, ExcludeCategoryId = excludeCategoryId }) > 0;
    }

    /// <summary>
    /// Gets paginated categories.
    /// </summary>
    /// <param name="pageSize">Rows per page.</param>
    /// <param name="offset">Rows to skip.</param>
    /// <returns>The requested page.</returns>
    public override IEnumerable<Category> GetPagedData(int pageSize, int offset)
    {
        using var connection = GetOpenConnection();
        return connection.Query<Category>(SelectPagedSql, new { PageSize = pageSize, Offset = offset }).ToList();
    }

    /// <summary>
    /// Inserts a new category.
    /// </summary>
    /// <param name="category">The category to persist.</param>
    public override void Insert(Category category)
    {
        using var connection = GetOpenConnection();
        connection.Execute(InsertSql, new { category.CategoryName, category.Slug, category.Description });
    }

    /// <summary>
    /// Inserts a category and returns the generated ID.
    /// </summary>
    /// <param name="category">The category to persist.</param>
    /// <returns>The generated <c>CategoryId</c>.</returns>
    public override long InsertToGetId(Category category)
    {
        using var connection = GetOpenConnection();
        return connection.ExecuteScalar<long>(
            InsertReturningIdSql, new { category.CategoryName, category.Slug, category.Description });
    }

    /// <summary>
    /// Updates an existing category.
    /// </summary>
    /// <param name="category">The category carrying the new values.</param>
    public override void Update(Category category)
    {
        using var connection = GetOpenConnection();
        connection.Execute(
            UpdateSql,
            new { category.CategoryId, category.CategoryName, category.Slug, category.Description });
    }

    /// <summary>
    /// Deletes a category by ID.
    /// </summary>
    /// <param name="categoryId">The category identifier.</param>
    public void Delete(long categoryId)
    {
        using var connection = GetOpenConnection();
        connection.Execute(DeleteSql, new { CategoryId = categoryId });
    }
}
