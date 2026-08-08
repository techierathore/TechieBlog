using BlogEngine.Common;
using BlogModels;
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
/// </remarks>
public class CategorySvc
{
    private readonly ICategoryRepo categoryRepo;
    private readonly ILogger<CategorySvc> logger;

    /// <summary>
    /// Initialises the category service.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Pure wiring — the service holds no state beyond these two
    /// dependencies.</para>
    /// <para><b>Flow:</b> assign and return.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="categoryRepo">Category data access.</param>
    /// <param name="logger">Logger for query and persistence failures.</param>
    public CategorySvc(ICategoryRepo categoryRepo, ILogger<CategorySvc> logger)
    {
        this.categoryRepo = categoryRepo;
        this.logger = logger;
    }

    /// <summary>
    /// Gets all categories ordered by name.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A taxonomy read that fails should not take a page down with it,
    /// so the failure is logged and an empty sequence returned — the sidebar renders without
    /// categories rather than throwing.</para>
    /// <para><b>Flow:</b> read → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <returns>All categories, or an empty sequence on failure.</returns>
    public IEnumerable<Category> GetAllCategories()
    {
        try
        {
            return categoryRepo.GetAll();
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
    /// <para><b>Flow:</b> read → log and degrade on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <returns>Categories with <c>PostCount</c> populated, or an empty sequence on failure.</returns>
    public IEnumerable<Category> GetAllWithCounts()
    {
        try
        {
            return categoryRepo.GetAllWithCounts();
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
    /// <para><b>Flow:</b> guard the slug → read → log and return <c>null</c> on failure.</para>
    /// <para><b>Side Effects:</b> Writes an error log entry on failure.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug.</param>
    /// <returns>The category if found, <c>null</c> otherwise.</returns>
    public Category? GetCategoryBySlug(string slug)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;
            return categoryRepo.GetBySlug(slug);
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

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateSlug(category.CategoryName);
        }

        // Check for duplicate slug
        if (categoryRepo.SlugExists(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateUniqueSlug(category.Slug, 1);
            int counter = 2;
            while (categoryRepo.SlugExists(category.Slug) && counter < 100)
            {
                category.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(category.CategoryName), counter);
                counter++;
            }
        }

        try
        {
            var categoryId = categoryRepo.InsertToGetId(category);
            category.CategoryId = categoryId;
            logger.LogInformation("Created category '{Name}' with ID {CategoryId}", category.CategoryName, categoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create category: {Name}", category.CategoryName);
            return Result<Category>.Failure($"Failed to create category: {ex.Message}");
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

        // Generate slug if not provided
        if (string.IsNullOrWhiteSpace(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateSlug(category.CategoryName);
        }

        // Check for duplicate slug (exclude current category)
        if (categoryRepo.SlugExists(category.Slug, category.CategoryId))
        {
            category.Slug = SlugGenerator.GenerateUniqueSlug(category.Slug, 1);
            int counter = 2;
            while (categoryRepo.SlugExists(category.Slug, category.CategoryId) && counter < 100)
            {
                category.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(category.CategoryName), counter);
                counter++;
            }
        }

        try
        {
            categoryRepo.Update(category);
            logger.LogInformation("Updated category '{Name}' with ID {CategoryId}", category.CategoryName, category.CategoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update category ID {CategoryId}: {Name}", category.CategoryId, category.CategoryName);
            return Result<Category>.Failure($"Failed to update category: {ex.Message}");
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
            logger.LogInformation("Deleted category ID {CategoryId}", categoryId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category ID {CategoryId}", categoryId);
            return Result.Failure($"Failed to delete category: {ex.Message}");
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
            return await categoryRepo.GetAllAsync(cancellationToken).ConfigureAwait(false);
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
            return await categoryRepo.GetAllWithCountsAsync(cancellationToken).ConfigureAwait(false);
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

            return await categoryRepo.GetBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
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

        if (string.IsNullOrWhiteSpace(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateSlug(category.CategoryName);
        }

        if (await categoryRepo.SlugExistsAsync(category.Slug, 0, cancellationToken).ConfigureAwait(false))
        {
            category.Slug = SlugGenerator.GenerateUniqueSlug(category.Slug, 1);
            int counter = 2;
            while (await categoryRepo.SlugExistsAsync(category.Slug, 0, cancellationToken).ConfigureAwait(false)
                   && counter < 100)
            {
                category.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(category.CategoryName), counter);
                counter++;
            }
        }

        try
        {
            var categoryId = await categoryRepo.InsertToGetIdAsync(category, cancellationToken).ConfigureAwait(false);
            category.CategoryId = categoryId;
            logger.LogInformation("Created category '{Name}' with ID {CategoryId}", category.CategoryName, categoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create category: {Name}", category.CategoryName);
            return Result<Category>.Failure($"Failed to create category: {ex.Message}");
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

        if (string.IsNullOrWhiteSpace(category.Slug))
        {
            category.Slug = SlugGenerator.GenerateSlug(category.CategoryName);
        }

        if (await categoryRepo.SlugExistsAsync(category.Slug, category.CategoryId, cancellationToken).ConfigureAwait(false))
        {
            category.Slug = SlugGenerator.GenerateUniqueSlug(category.Slug, 1);
            int counter = 2;
            while (await categoryRepo.SlugExistsAsync(category.Slug, category.CategoryId, cancellationToken).ConfigureAwait(false)
                   && counter < 100)
            {
                category.Slug = SlugGenerator.GenerateUniqueSlug(
                    SlugGenerator.GenerateSlug(category.CategoryName), counter);
                counter++;
            }
        }

        try
        {
            await categoryRepo.UpdateAsync(category, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Updated category '{Name}' with ID {CategoryId}", category.CategoryName, category.CategoryId);
            return Result<Category>.Success(category);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update category ID {CategoryId}: {Name}", category.CategoryId, category.CategoryName);
            return Result<Category>.Failure($"Failed to update category: {ex.Message}");
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
            logger.LogInformation("Deleted category ID {CategoryId}", categoryId);
            return Result.Success();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete category ID {CategoryId}", categoryId);
            return Result.Failure($"Failed to delete category: {ex.Message}");
        }
    }
}
