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
/// <para><b>Usage:</b> Call the <c>…Async</c> members; the synchronous twins were removed (REQ-NFR-026).</para>
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
/// <para><b>Caching (REQ-NFR-018).</b> The three taxonomy-wide reads — <see cref="GetAllCategoriesAsync"/>,
/// <see cref="GetAllWithCountsAsync"/> and <see cref="GetCategoryBySlugAsync"/>, with their asynchronous twins —
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
