using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for post categories — the single-parent taxonomy behind <c>/category/{slug}</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against <c>Category</c>. On top of the inherited
/// generic CRUD surface it adds the three accesses a taxonomy actually needs and the generic surface
/// cannot express: resolve by public slug, prove a slug is free before saving, and list with the
/// published-post counts a navigation menu shows.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Save — <c>CategorySvc</c> generates a slug, calls <see cref="SlugExistsAsync"/> to prove it
///         is free, then the inherited insert/update. The check is advisory, not a lock: two concurrent
///         saves can both see "free" and the unique index, not this member, is what finally rejects the
///         loser.</item>
///   <item>Browse — the category page resolves <see cref="GetBySlugAsync"/>; menus and the sitemap use
///         <see cref="GetAllWithCountsAsync"/>.</item>
///   <item>Remove — <see cref="DeleteAsync"/> drops the row; posts that referenced it become
///         uncategorised rather than being deleted with it.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.CategoryRepo</c> over Dapper and
/// PostgreSQL — the worked reference implementation for the async conversion. Consumed by
/// <c>CategorySvc</c> and <c>SitemapSvc</c>.</para>
///
/// <para><b>Usage:</b> <c>CategorySvc</c> is the layer that converts expected failures (duplicate slug,
/// missing row) into a <c>Result</c>; this contract itself has no <c>Result</c> surface and throws on
/// any data-access failure. Listings come back ordered by <c>CategoryName</c> ascending — the sequence
/// menus render in, so callers must not re-sort — and the counts cover published, non-deleted posts
/// only, so the number beside a category always matches what a reader can actually open.</para>
///
/// <para><b>Async conversion (REQ-NFR-026).</b> Every member exists twice: a legacy blocking member and
/// an <c>…Async</c> twin carrying a <see cref="CancellationToken"/>. Call the async member. Unlike most
/// sibling repositories the async members here are <b>abstract</b> — there is no bridged default to
/// inherit, so every implementer genuinely honours the token. The blocking members are retained only
/// until the last caller migrates and are deleted in the final stage.</para>
/// </remarks>
public interface ICategoryRepo : IGenericRepository<Category>
{
    /// <summary>
    /// Gets a category by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier; matched exactly, so casing matters.</param>
    /// <returns>The category, or <c>null</c> when the slug is unknown. An unknown slug is a normal
    /// answer — it is how the category page decides to render a 404 — and is never an exception.</returns>
    Category? GetBySlug(string slug);

    /// <summary>
    /// Checks if a category slug already exists.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeCategoryId">Category ID to exclude, so an update does not collide with the
    /// row it is editing. Pass 0 (the default) when inserting.</param>
    /// <returns><c>true</c> when another category already uses the slug. Advisory only — it is a read,
    /// not a reservation, so a concurrent save can still take the slug before the caller writes.</returns>
    bool SlugExists(string slug, long excludeCategoryId = 0);

    /// <summary>
    /// Gets all categories with post counts.
    /// </summary>
    /// <returns>Every category ordered by <c>CategoryName</c> ascending, each with <c>PostCount</c>
    /// populated; an empty sequence — never <c>null</c> — on an empty table. An unused category is kept
    /// with a count of zero rather than dropped.</returns>
    IEnumerable<Category> GetAllWithCounts();

    /// <summary>
    /// Deletes a category by ID.
    /// </summary>
    /// <param name="categoryId">Category ID to delete. An unknown identifier affects no rows and is a
    /// no-op, not an error.</param>
    void Delete(long categoryId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over the four members above, which are deleted once
    // every caller has migrated. CategoryRepo is the worked reference implementation.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets a category by its URL slug without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetBySlug"/>; an unknown slug is a normal
    /// answer and returns <c>null</c>.</para>
    /// <para><b>Flow:</b> open connection asynchronously → query by slug → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <param name="cancellationToken">Cancels the query; a cancelled token faults the returned task
    /// with an <c>OperationCanceledException</c> rather than returning <c>null</c>.</param>
    /// <returns>The category, or <c>null</c> when the slug is unknown.</returns>
    Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a category slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SlugExists"/>. The exclusion parameter
    /// lets an update ignore the row being edited, so re-saving a category without renaming it does
    /// not collide with itself.</para>
    /// <para><b>Flow:</b> open connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeCategoryId">Category ID to exclude, so an update does not collide with the
    /// row it is editing. Pass 0 (the default) when inserting.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another category already uses the slug. Advisory only — see the
    /// blocking twin for why this is not a reservation.</returns>
    Task<bool> SlugExistsAsync(string slug, long excludeCategoryId = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all categories with post counts without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllWithCounts"/>. Counts cover
    /// published, non-deleted posts only, so the number matches what a reader can actually browse.</para>
    /// <para><b>Flow:</b> open connection asynchronously → grouped left join → materialise.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Every category ordered by <c>CategoryName</c> ascending, each with <c>PostCount</c>
    /// populated; an empty sequence — never <c>null</c> — on an empty table. Fully buffered before the
    /// connection closes, so it is safe to enumerate twice.</returns>
    Task<IEnumerable<Category>> GetAllWithCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a category by ID without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="Delete"/>. Deleting an unknown identifier
    /// is a no-op rather than an error.</para>
    /// <para><b>Flow:</b> open connection asynchronously → delete by key.</para>
    /// <para><b>Side Effects:</b> Removes one row; posts in the category become uncategorised.</para>
    /// </remarks>
    /// <param name="categoryId">Category ID to delete. An unknown identifier is a no-op.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so a caller
    /// cannot tell a successful delete from a no-op on an unknown identifier.</returns>
    Task DeleteAsync(long categoryId, CancellationToken cancellationToken = default);
}
