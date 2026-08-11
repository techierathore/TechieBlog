using BlogEngine.Common;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Service layer for category operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns the taxonomy rules a category has to satisfy before it is stored: a
/// name is mandatory, and every category ends up with a slug that is unique across the whole
/// taxonomy because that slug is its public URL (<c>/category/{slug}</c>). Callers hand over a
/// <c>Category</c> and never think about slugs; this service derives and de-duplicates them.</para>
///
/// <para><b>Code Flow:</b> a page calls an <c>…Async</c> member → the member validates and generates
/// slugs → <c>ICategoryRepo</c> performs the I/O asynchronously → an expected failure comes back as
/// <c>Result</c>/<c>Result&lt;Category&gt;</c> and an unexpected one is logged and converted to one.</para>
///
/// <para><b>Dependencies:</b> ICategoryRepo for data access, SlugGenerator for URL slugs.</para>
///
/// <para><b>Usage:</b> Call the <c>…Async</c> members. The synchronous twins are retained only while
/// the rest of the call sites migrate (REQ-NFR-026).</para>
///
/// <para><b>Async conversion (REQ-NFR-026):</b> this service is the worked example of how the service
/// layer follows the repository layer. The <c>Result</c> pattern is unchanged by the conversion —
/// <c>Result&lt;T&gt;</c> stays exactly what it is and simply travels inside a task, so a method that
/// returned <c>Result&lt;Category&gt;</c> returns <c>Task&lt;Result&lt;Category&gt;&gt;</c>. There is no
/// <c>AsyncResult</c> type and none is wanted: <c>Result</c> models the expected-failure axis and
/// <c>Task</c> models the completion axis, and they compose without either knowing about the other.
/// The <c>try/catch</c> that turns an unexpected exception into a failed <c>Result</c> keeps working
/// verbatim, because an <c>await</c>ed call throws at the <c>await</c> just as a blocking call throws
/// at the call.</para>
///
/// <para><b>Exception text never reaches the caller (REQ-NFR-031).</b> Every <c>catch</c> logs the
/// exception through <see cref="ILogger{TCategoryName}"/> and then returns one of the curated
/// constants below. The detail stays in the log, where the host's <c>CorrelationIdMiddleware</c> has
/// already attached the request's correlation id to every event (REQ-NFR-015), so an operator can tie
/// a user's report to the exact stack trace without the message disclosing a SQL fragment or a table
/// name. <c>Result.Failure</c>'s own documentation requires this; do not reintroduce
/// <c>ex.Message</c>.</para>
///
/// <para><b>Caching (REQ-NFR-018).</b> The three taxonomy-wide reads — <see cref="GetAllCategories"/>,
/// <see cref="GetAllWithCounts"/> and <see cref="GetCategoryBySlug"/>, with their asynchronous twins —
/// go through <see cref="ICacheService"/> under <c>CacheTags.Taxonomy</c>. Categories are read by the
/// sidebar on virtually every public render and change a handful of times a year, which is the
/// textbook case for a cache. <b>Every mutation here evicts the tag</b> via
/// <see cref="ServiceCache.InvalidateTaxonomy"/>, so a renamed category is visible on the next render
/// rather than when the ten-minute expiry lapses. A new write path added to this class must call it
/// too — that is the whole contract, and a cache without it is worse than no cache.</para>
/// </remarks>
public class CategorySvc
{
    /// <summary>
    /// Prefix used to build an identifier-based slug when a name yields no slug at all.
    /// </summary>
    /// <remarks>
    /// Feeds <c>SlugGenerator.EnsureSlug</c>, which turns it into <c>category-7</c> for a category
    /// that already has an id, or <c>category-{name digest}</c> for one being inserted (REQ-FN-054).
    /// </remarks>
    private const string SlugPrefix = "category";

    /// <summary>Curated message for an insert that could not be persisted (REQ-NFR-031).</summary>
    private const string CreateFailureMessage = "Failed to create category. Please try again later.";

    /// <summary>Curated message for an update that could not be persisted (REQ-NFR-031).</summary>
    private const string UpdateFailureMessage = "Failed to update category. Please try again later.";

    /// <summary>Curated message for a delete that could not be persisted (REQ-NFR-031).</summary>
    private const string DeleteFailureMessage = "Failed to delete category. Please try again later.";

    private readonly ICategoryRepo categoryRepo;
    private readonly ILogger<CategorySvc> logger;
    private readonly ICacheService? cacheService;

    /// <summary>
    /// Initialises the category service.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Pure wiring — the service holds no state beyond these
    /// dependencies.</para>
    /// <para><b>Flow:</b> assign and return.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="categoryRepo">Category data access.</param>
    /// <param name="logger">Logger for query and persistence failures.</param>
    /// <param name="cacheService">
    /// Taxonomy cache (REQ-NFR-018). Optional: omitting it makes every read go to the database, which
    /// is what a unit test that is not exercising caching wants. The host always supplies it — it is
    /// a registered singleton — so the uncached path never runs in the application.
    /// </param>
    public CategorySvc(
        ICategoryRepo categoryRepo,
        ILogger<CategorySvc> logger,
        ICacheService? cacheService = null)
    {
        this.categoryRepo = categoryRepo;
        this.logger = logger;
        this.cacheService = cacheService;
    }

    /// <summary>
    /// Gets all categories ordered by name.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A taxonomy read that fails should not take a page down with it,
    /// so the failure is logged and an empty sequence returned — the sidebar renders without
    /// categories rather than throwing.</para>
    /// <para><b>Flow:</b> cached read (REQ-NFR-018) → on a miss read the repository → log and degrade
    /// on failure.</para>
    /// <para><b>Side Effects:</b> Populates the taxonomy cache on a miss; writes an error log entry
    /// on failure. The repository buffers its own result before the connection is disposed, so what
    /// is cached is a materialised list and not a query that would re-execute — or fail — on a later
    /// enumeration.</para>
    /// <para><b>Staleness:</b> up to ten minutes only if an eviction is missed — every write on this
    /// class evicts the taxonomy tag, so in practice a change is visible on the next render. A failure
    /// is <b>not</b> cached: the empty sequence is returned without being stored, so a transient
    /// database fault does not blank the sidebar for the next ten minutes.</para>
    /// </remarks>
    /// <returns>All categories, or an empty sequence on failure.</returns>
    public IEnumerable<Category> GetAllCategories()
    {
        try
        {
            return ServiceCache.Read<IEnumerable<Category>>(
                cacheService,
                ServiceCache.CategoriesAllKey,
                CacheTags.Taxonomy,
                () => categoryRepo.GetAll());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all categories");
            return Enumerable.Empty<Category>();
        }
    }

    /// <summary>
    /// Gets all categories with their post counts populated.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The count is computed in SQL alongside the category, so the
    /// caller gets both in one round trip instead of a query per category. Same degrade-to-empty
    /// policy as <see cref="GetAllCategories"/>.</para>
    /// <para><b>Flow:</b> cached read (REQ-NFR-018) → on a miss read the repository → log and degrade
    /// on failure.</para>
    /// <para><b>Side Effects:</b> Populates the taxonomy cache on a miss; writes an error log entry
    /// on failure.</para>
    /// <para><b>The counts move when posts move, not when categories do</b>, so this entry is evicted
    /// by <c>BlogSvc</c>'s write paths as well as by this class's — <see cref="ServiceCache.InvalidateContent"/>
    /// drops the taxonomy tag for exactly this reason.</para>
    /// </remarks>
    /// <returns>Categories with <c>PostCount</c> populated, or an empty sequence on failure.</returns>
    public IEnumerable<Category> GetAllWithCounts()
    {
        try
        {
            return ServiceCache.Read<IEnumerable<Category>>(
                cacheService,
                ServiceCache.CategoriesWithCountsKey,
                CacheTags.Taxonomy,
                () => categoryRepo.GetAllWithCounts());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting categories with counts");
            return Enumerable.Empty<Category>();
        }
    }

    /// <summary>
    /// Gets a single category by identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both "no such category" and "the lookup failed" surface as
    /// <c>null</c>; the failure case is distinguished in the log, not in the return value.</para>
    /// <para><b>Flow:</b> read → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="categoryId">Category identifier.</param>
    /// <returns>The category if found, <c>null</c> otherwise.</returns>
    public Category? GetCategory(long categoryId)
    {
        try
        {
            return categoryRepo.GetSingle(categoryId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting category by ID: {CategoryId}", categoryId);
            return null;
        }
    }

    /// <summary>
    /// Gets a category by its URL slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank slug never reaches the database — it can only come from
    /// a malformed route, and the answer is the same <c>null</c> an unknown slug produces.</para>
    /// <para><b>Flow:</b> guard the slug → cached read (REQ-NFR-018) → on a miss read the repository
    /// → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Populates the taxonomy cache on a miss; writes an error log entry
    /// on failure.</para>
    /// <para><b>An unknown slug is never cached</b> — the cache stores no null — so a request for a
    /// category that does not exist costs a round trip every time. That is deliberate: caching the
    /// absence would let anyone mint unbounded cache entries by requesting random slugs.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug.</param>
    /// <returns>The category if found, <c>null</c> otherwise.</returns>
    public Category? GetCategoryBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            return ServiceCache.Read(
                cacheService,
                ServiceCache.CategoryBySlugKey(slug),
                CacheTags.Taxonomy,
                () => categoryRepo.GetBySlug(slug));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting category by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Creates a new category, deriving a unique slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The name is mandatory. A missing slug is derived from the name,
    /// and a slug already in use is suffixed until it is free — capped at 100 attempts so a
    /// pathological collision cannot spin forever. Validation failures are expected outcomes and
    /// come back as a failed <c>Result</c>; only an unexpected persistence error is caught, logged
    /// and converted.</para>
    /// <para><b>Flow:</b> validate → derive slug → resolve collisions → insert → wrap in
    /// <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>Category</c> row and assigns its generated
    /// <c>CategoryId</c> back onto <paramref name="category"/>, which is mutated in place. Writes an
    /// information or error log entry.</para>
    /// <para><b>Slug collision race:</b> the uniqueness check and the insert are separate
    /// statements, so two simultaneous creations of the same name can both pass the check; the
    /// database constraint is the real guard.</para>
    /// <para><b>Authorization:</b> none is applied — taxonomy editing is an administrator
    /// operation and the caller must have established that.</para>
    /// </remarks>
    /// <param name="category">The category to create; mutated with its generated id and slug.</param>
    /// <returns>The created category on success, or a failure carrying the reason.</returns>
    public Result<Category> CreateCategory(Category category)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (string.IsNullOrWhiteSpace(category.CategoryName))
            return Result<Category>.Failure("Category name is required");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        category.Slug = SlugGenerator.EnsureSlug(category.Slug, category.CategoryName, SlugPrefix);
        category.Slug = SlugGenerator.ResolveUniqueSlug(
            category.Slug,
            candidate => categoryRepo.SlugExists(candidate));

        try
        {
            var categoryId = categoryRepo.InsertToGetId(category);
            category.CategoryId = categoryId;
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Created category '{Name}' with ID {CategoryId}", category.CategoryName, categoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create category: {Name}", category.CategoryName);
            return Result<Category>.Failure(CreateFailureMessage);
        }
    }

    /// <summary>
    /// Updates an existing category, keeping its slug unique.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row is confirmed to exist before anything is written, so an
    /// edit of a category deleted in another tab reports "Category not found" instead of a success
    /// that updated nothing. Slug collision resolution excludes the row being edited, so re-saving
    /// an unchanged category does not gratuitously suffix its slug — which matters because the slug
    /// is the public category URL.</para>
    /// <para><b>Flow:</b> validate → confirm existence → derive slug → resolve collisions →
    /// update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row and mutates <paramref name="category"/> in place
    /// with the resolved slug. Writes an information or error log entry.</para>
    /// <para><b>Authorization:</b> none is applied; the caller owns that check.</para>
    /// </remarks>
    /// <param name="category">The category carrying the new values; mutated with its resolved slug.</param>
    /// <returns>The updated category on success, or a failure carrying the reason.</returns>
    public Result<Category> UpdateCategory(Category category)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (category.CategoryId <= 0)
            return Result<Category>.Failure("Invalid category ID");

        if (string.IsNullOrWhiteSpace(category.CategoryName))
            return Result<Category>.Failure("Category name is required");

        // Check if category exists
        var existing = categoryRepo.GetSingle(category.CategoryId);
        if (existing == null)
            return Result<Category>.Failure("Category not found");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        category.Slug = SlugGenerator.EnsureSlug(category.Slug, category.CategoryName, SlugPrefix, category.CategoryId);
        category.Slug = SlugGenerator.ResolveUniqueSlug(
            category.Slug,
            candidate => categoryRepo.SlugExists(candidate, category.CategoryId));

        try
        {
            categoryRepo.Update(category);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Updated category '{Name}' with ID {CategoryId}", category.CategoryName, category.CategoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update category ID {CategoryId}: {Name}", category.CategoryId, category.CategoryName);
            return Result<Category>.Failure(UpdateFailureMessage);
        }
    }

    /// <summary>
    /// Saves a category — inserting or updating according to whether it already has an identifier.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A non-positive <c>CategoryId</c> means the category has never
    /// been persisted, so the editor can bind one method to its save button regardless of mode.</para>
    /// <para><b>Flow:</b> inspect the key → delegate to <see cref="CreateCategory"/> or
    /// <see cref="UpdateCategory"/>.</para>
    /// <para><b>Side Effects:</b> Those of the delegated method — one insert or one update.</para>
    /// </remarks>
    /// <param name="category">The category to save.</param>
    /// <returns>The saved category on success, or a failure carrying the reason.</returns>
    public Result<Category> SaveCategory(Category category)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (category.CategoryId <= 0)
        {
            return CreateCategory(category);
        }
        else
        {
            return UpdateCategory(category);
        }
    }

    /// <summary>
    /// Deletes a category permanently.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Unlike a post, a category is <b>hard-deleted</b> — there is no
    /// soft-delete flag on the taxonomy. Existence is confirmed first so the caller can report "not
    /// found" rather than a success that removed nothing.</para>
    /// <para><b>Flow:</b> validate the id → confirm existence → delete → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Removes one row; writes an information or error log entry. What
    /// happens to posts already filed under the category is decided by the foreign key in the
    /// schema, not here — this method makes no attempt to reassign or count them first, so a
    /// category still in use will either fail on the constraint or orphan its posts depending on how
    /// that key is declared.</para>
    /// <para><b>Authorization:</b> none is applied; the caller owns that check.</para>
    /// </remarks>
    /// <param name="categoryId">Identifier of the category to delete.</param>
    /// <returns>Success, or a failure describing why the delete was refused.</returns>
    public Result DeleteCategory(long categoryId)
    {
        if (categoryId <= 0)
            return Result.Failure("Invalid category ID");

        var existing = categoryRepo.GetSingle(categoryId);
        if (existing == null)
            return Result.Failure("Category not found");

        try
        {
            categoryRepo.Delete(categoryId);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Deleted category ID {CategoryId}", categoryId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category ID {CategoryId}", categoryId);
            return Result.Failure(DeleteFailureMessage);
        }
    }

    // =================================================================================================
    // Async surface — REQ-NFR-026. Preferred over every member above.
    // =================================================================================================

    /// <summary>
    /// Gets all categories ordered by name, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A taxonomy read that fails should not take a page down with it, so
    /// the failure is logged and an empty sequence is returned — the sidebar renders without
    /// categories rather than throwing.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>All categories, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<Category>> GetAllCategoriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync<IEnumerable<Category>>(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.CategoriesAllKey),
                CacheTags.Taxonomy,
                () => categoryRepo.GetAllAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting all categories");
            return Enumerable.Empty<Category>();
        }
    }

    /// <summary>
    /// Gets all categories with their post counts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Same degrade-to-empty policy as
    /// <see cref="GetAllCategoriesAsync"/>.</para>
    /// <para><b>Flow:</b> await the repository → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Categories with PostCount populated, or an empty sequence on failure.</returns>
    public async Task<IEnumerable<Category>> GetAllWithCountsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await ServiceCache.ReadAsync<IEnumerable<Category>>(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.CategoriesWithCountsKey),
                CacheTags.Taxonomy,
                () => categoryRepo.GetAllWithCountsAsync(cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting categories with counts");
            return Enumerable.Empty<Category>();
        }
    }

    /// <summary>
    /// Gets a single category by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both "no such category" and "the lookup failed" surface as
    /// <c>null</c>; the failure case is distinguished in the log, not in the return value.</para>
    /// <para><b>Flow:</b> await the repository → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="categoryId">Category ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Category if found, <c>null</c> otherwise.</returns>
    public async Task<Category?> GetCategoryAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await categoryRepo.GetSingleAsync(categoryId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting category by ID: {CategoryId}", categoryId);
            return null;
        }
    }

    /// <summary>
    /// Gets a category by its URL slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank slug never reaches the database — it can only come from a
    /// malformed route, and the answer is the same <c>null</c> an unknown slug produces.</para>
    /// <para><b>Flow:</b> guard the slug → await the repository → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Category if found, <c>null</c> otherwise.</returns>
    public async Task<Category?> GetCategoryBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            return await ServiceCache.ReadAsync(
                cacheService,
                ServiceCache.AsyncVariant(ServiceCache.CategoryBySlugKey(slug)),
                CacheTags.Taxonomy,
                () => categoryRepo.GetBySlugAsync(slug, cancellationToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting category by slug: {Slug}", slug);
            return null;
        }
    }

    /// <summary>
    /// Creates a new category with validation and slug generation, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A missing slug is derived from the name, and a slug already in use
    /// is suffixed until it is free — capped at 100 attempts so a pathological collision cannot spin
    /// forever. Validation failures are expected outcomes and come back as a failed <c>Result</c>, not
    /// as exceptions.</para>
    /// <para><b>Flow:</b> validate → generate slug → resolve collisions → await insert → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Adds one row; writes an information or error log entry.</para>
    /// </remarks>
    /// <param name="category">The category to create.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result with the created category on success, error message on failure.</returns>
    public async Task<Result<Category>> CreateCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (string.IsNullOrWhiteSpace(category.CategoryName))
            return Result<Category>.Failure("Category name is required");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        category.Slug = SlugGenerator.EnsureSlug(category.Slug, category.CategoryName, SlugPrefix);
        category.Slug = await SlugGenerator.ResolveUniqueSlugAsync(
            category.Slug,
            candidate => categoryRepo.SlugExistsAsync(candidate, 0, cancellationToken)).ConfigureAwait(false);

        try
        {
            var categoryId = await categoryRepo.InsertToGetIdAsync(category, cancellationToken).ConfigureAwait(false);
            category.CategoryId = categoryId;
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Created category '{Name}' with ID {CategoryId}", category.CategoryName, categoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create category: {Name}", category.CategoryName);
            return Result<Category>.Failure(CreateFailureMessage);
        }
    }

    /// <summary>
    /// Updates an existing category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row is confirmed to exist before anything is written, so an
    /// edit of a category deleted in another tab reports "not found" instead of silently updating
    /// nothing. Slug collisions exclude the row being edited.</para>
    /// <para><b>Flow:</b> validate → confirm existence → resolve slug → await update → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Updates one row; writes an information or error log entry.</para>
    /// </remarks>
    /// <param name="category">The category to update.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public async Task<Result<Category>> UpdateCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        if (category == null)
            return Result<Category>.Failure("Category cannot be null");

        if (category.CategoryId <= 0)
            return Result<Category>.Failure("Invalid category ID");

        if (string.IsNullOrWhiteSpace(category.CategoryName))
            return Result<Category>.Failure("Category name is required");

        var existing = await categoryRepo.GetSingleAsync(category.CategoryId, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result<Category>.Failure("Category not found");

        // Derive a guaranteed non-empty base slug, then suffix it until it is free (REQ-FN-054).
        category.Slug = SlugGenerator.EnsureSlug(category.Slug, category.CategoryName, SlugPrefix, category.CategoryId);
        category.Slug = await SlugGenerator.ResolveUniqueSlugAsync(
            category.Slug,
            candidate => categoryRepo.SlugExistsAsync(candidate, category.CategoryId, cancellationToken)).ConfigureAwait(false);

        try
        {
            await categoryRepo.UpdateAsync(category, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Updated category '{Name}' with ID {CategoryId}", category.CategoryName, category.CategoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update category ID {CategoryId}: {Name}", category.CategoryId, category.CategoryName);
            return Result<Category>.Failure(UpdateFailureMessage);
        }
    }

    /// <summary>
    /// Saves a category — insert or update based on CategoryId — without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A non-positive identifier means the category has never been
    /// persisted, so the editor can bind one method to its save button regardless of mode.</para>
    /// <para><b>Flow:</b> inspect the key → delegate to create or update.</para>
    /// <para><b>Side Effects:</b> Those of the delegated method.</para>
    /// </remarks>
    /// <param name="category">The category to save.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public Task<Result<Category>> SaveCategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        if (category == null)
            return Task.FromResult(Result<Category>.Failure("Category cannot be null"));

        return category.CategoryId <= 0
            ? CreateCategoryAsync(category, cancellationToken)
            : UpdateCategoryAsync(category, cancellationToken);
    }

    /// <summary>
    /// Deletes a category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Existence is confirmed first so the caller can report "not found"
    /// rather than a success that removed nothing.</para>
    /// <para><b>Flow:</b> validate → confirm existence → await delete → wrap in <c>Result</c>.</para>
    /// <para><b>Side Effects:</b> Removes one row; writes an information or error log entry.</para>
    /// </remarks>
    /// <param name="categoryId">ID of the category to delete.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Result indicating success or failure.</returns>
    public async Task<Result> DeleteCategoryAsync(long categoryId, CancellationToken cancellationToken = default)
    {
        if (categoryId <= 0)
            return Result.Failure("Invalid category ID");

        var existing = await categoryRepo.GetSingleAsync(categoryId, cancellationToken).ConfigureAwait(false);
        if (existing == null)
            return Result.Failure("Category not found");

        try
        {
            await categoryRepo.DeleteAsync(categoryId, cancellationToken).ConfigureAwait(false);
            ServiceCache.InvalidateTaxonomy(cacheService);
            logger.LogInformation("Deleted category ID {CategoryId}", categoryId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category ID {CategoryId}", categoryId);
            return Result.Failure(DeleteFailureMessage);
        }
    }
}
