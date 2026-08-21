using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for multi-part series — the ordered grouping behind <c>/series/{slug}</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against <c>BlogSeries</c>. On top of the inherited
/// generic CRUD surface it adds what a series actually needs and the generic surface cannot express:
/// resolve by public slug, prove a slug is free before saving, and list with the part counts a series
/// index shows.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Save — <c>SeriesSvc</c> generates a slug, calls <see cref="SlugExistsAsync"/> to prove it is
///         free, then the inherited insert/update. The check is advisory, not a lock: two concurrent
///         saves can both see "free" and the unique index, not this member, rejects the loser.</item>
///   <item>Browse — the series page resolves <see cref="GetBySlugAsync"/>; the index uses
///         <see cref="GetAllWithCountsAsync"/>.</item>
///   <item>Remove — <c>SeriesSvc</c> detaches the series' posts through <c>IBlogPostRepo</c>
///         <i>first</i>, then calls <see cref="DeleteAsync"/>. That ordering is the contract: this
///         member deletes the series row only, so a caller that skips the detach step leaves posts
///         pointing at a series that no longer exists.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.BlogSeriesRepo</c> over Dapper and
/// PostgreSQL. Consumed by <c>SeriesSvc</c>, which also holds <c>IBlogPostRepo</c> for the part list.</para>
///
/// <para><b>Usage:</b> <c>SeriesSvc</c> is the layer that converts expected failures (duplicate slug,
/// missing row) into a <c>Result</c>; this contract has no <c>Result</c> surface and throws on any
/// data-access failure. Listings come back ordered by series <c>Name</c> ascending and callers must not
/// re-sort. Counts cover published, non-deleted posts only, so the part count a reader sees always
/// matches the parts they can actually open.</para>
///
/// <para><b>Async conversion (REQ-NFR-026).</b> Every member exists twice: a legacy blocking member and
/// an <c>…Async</c> twin carrying a <see cref="CancellationToken"/>. Call the async member. The async
/// members here are <b>abstract</b> — there is no bridged default to inherit, so every implementer
/// genuinely honours the token. The blocking members are retained only until the last caller migrates
/// and are deleted in the final stage.</para>
/// </remarks>
public interface IBlogSeriesRepo : IGenericRepository<BlogSeries>
{
    /// <summary>
    /// Gets a series by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier; matched exactly, so casing matters.</param>
    /// <returns>The series with <c>PostCount</c> populated, or <c>null</c> when the slug is unknown. An
    /// unknown slug is a normal answer — it is how the series page decides to render a 404 — and is
    /// never an exception.</returns>
    BlogSeries? GetBySlug(string slug);

    /// <summary>
    /// Checks if a series slug already exists.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeSeriesId">Series ID to exclude, so an update does not collide with the row
    /// it is editing. Pass 0 (the default) when inserting.</param>
    /// <returns><c>true</c> when another series already uses the slug. Advisory only — it is a read,
    /// not a reservation, so a concurrent save can still take the slug before the caller writes.</returns>
    bool SlugExists(string slug, long excludeSeriesId = 0);

    /// <summary>
    /// Gets all series with post counts.
    /// </summary>
    /// <returns>Every series ordered by <c>Name</c> ascending, each with <c>PostCount</c> populated; an
    /// empty sequence — never <c>null</c> — on an empty table. An empty series is kept with a count of
    /// zero rather than dropped.</returns>
    IEnumerable<BlogSeries> GetAllWithCounts();

    /// <summary>
    /// Deletes a series by ID.
    /// </summary>
    /// <param name="seriesId">Series ID to delete. An unknown identifier affects no rows and is a
    /// no-op, not an error. Detach the series' posts first — this member does not cascade.</param>
    void Delete(long seriesId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over the four members above, which are deleted once every
    // caller has migrated. Implemented by BlogEngine.DbAccess.BlogSeriesRepo with genuine async Dapper.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets a series by its URL slug, including its published-post count, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetBySlug"/>. The projection counts only
    /// published, non-deleted posts, so the part count shown to a reader matches the parts they can
    /// actually open; an unknown slug is a normal answer and returns <c>null</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → grouped left join filtered by slug →
    /// first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <param name="cancellationToken">Cancels the query; a cancelled token faults the returned task
    /// with an <c>OperationCanceledException</c> rather than returning <c>null</c>.</param>
    /// <returns>The series with <c>PostCount</c> populated, or <c>null</c> when the slug is unknown.</returns>
    Task<BlogSeries?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a series slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SlugExists"/>. The exclusion parameter
    /// lets an update ignore the row being edited, so re-saving a series without renaming it does not
    /// collide with itself.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeSeriesId">Series ID to exclude, so an update does not collide with the row
    /// it is editing. Pass 0 (the default) when inserting.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another series already uses the slug. Advisory only — see the blocking
    /// twin for why this is not a reservation.</returns>
    Task<bool> SlugExistsAsync(string slug, long excludeSeriesId = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all series with post counts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllWithCounts"/>. Counts cover
    /// published, non-deleted posts only; the LEFT JOIN keeps an empty series visible with a count of
    /// zero.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → grouped left join → materialise.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Every series ordered by <c>Name</c> ascending, each with <c>PostCount</c> populated; an
    /// empty sequence — never <c>null</c> — on an empty table. Fully buffered before the connection
    /// closes, so it is safe to enumerate twice.</returns>
    Task<IEnumerable<BlogSeries>> GetAllWithCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a series by ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="Delete"/>. Callers detach the series'
    /// posts first so they survive as standalone articles; deleting an unknown identifier is a no-op.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → execute DELETE.</para>
    /// <para><b>Side Effects:</b> Removes one row from <c>BlogSeries</c>.</para>
    /// </remarks>
    /// <param name="seriesId">Series ID to delete. An unknown identifier is a no-op. Detach the
    /// series' posts first — this member does not cascade.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the statement has run. It carries no row count, so a caller
    /// cannot tell a successful delete from a no-op on an unknown identifier.</returns>
    Task DeleteAsync(long seriesId, CancellationToken cancellationToken = default);
}
