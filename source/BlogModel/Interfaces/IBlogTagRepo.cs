using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access contract for tags and the many-to-many join that attaches them to posts.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns <c>Tag</c> and the <c>PostTag</c> junction together. Tags differ from the
/// other two taxonomies in one way that shapes this whole contract: a post has many tags, so the
/// junction table is part of the aggregate and no caller outside this repository may write to it.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Save a tag — <c>TagSvc</c> generates a slug, calls <see cref="SlugExistsAsync"/> to prove it
///         is free, then the inherited insert/update. The check is advisory, not a lock: two concurrent
///         saves can both see "free" and the unique index, not this member, rejects the loser.</item>
///   <item>Tag a post — the post editor calls <see cref="SetTagsForPostAsync"/>, which
///         <i>replaces</i> the post's whole tag set inside one transaction.</item>
///   <item>Browse — <c>/tag/{slug}</c> resolves <see cref="GetBySlugAsync"/>, then pages with
///         <see cref="GetPostsByTagAsync"/> and <see cref="GetPostCountByTagAsync"/>; the post page's
///         tag strip uses <see cref="GetTagsForPostAsync"/>; the editor's type-ahead uses
///         <see cref="SearchTagsAsync"/>; menus and the sitemap use
///         <see cref="GetAllWithCountsAsync"/>.</item>
///   <item>Remove — <see cref="DeleteAsync"/> clears the junction rows before the tag itself, because
///         <c>PostTag</c> holds the foreign key.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.BlogTagRepo</c> over Dapper and
/// PostgreSQL. Consumed by <c>TagSvc</c>, <c>BlogSvc</c> and <c>SitemapSvc</c>.</para>
///
/// <para><b>Usage:</b> <c>TagSvc</c> is the layer that converts expected failures (duplicate slug,
/// missing row) into a <c>Result</c>; this contract has no <c>Result</c> surface and throws on any
/// data-access failure. Ordering is a contract callers must not undo: tag listings and a post's tag
/// strip come back by <c>TagName</c> ascending, while <see cref="GetPostsByTagAsync"/> is newest-first
/// by <c>CreatedOn</c>. Counts and post pages apply the same published/non-deleted filter, so
/// <see cref="GetPostCountByTagAsync"/> can be paged against <see cref="GetPostsByTagAsync"/> without
/// producing an empty final page.</para>
///
/// <para><b>Async conversion (REQ-NFR-026).</b> Every member exists twice: a legacy blocking member and
/// an <c>…Async</c> twin carrying a <see cref="CancellationToken"/>. Call the async member. The async
/// members here are <b>abstract</b> — there is no bridged default to inherit, so every implementer
/// genuinely honours the token. The blocking members are retained only until the last caller migrates
/// and are deleted in the final stage.</para>
/// </remarks>
public interface IBlogTagRepo : IGenericRepository<BlogTag>
{
    /// <summary>
    /// Gets a tag by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier; matched exactly, so casing matters.</param>
    /// <returns>The tag, or <c>null</c> when the slug is unknown. An unknown slug is a normal answer —
    /// it is how <c>/tag/{slug}</c> decides to render a 404 — and is never an exception.</returns>
    BlogTag? GetBySlug(string slug);

    /// <summary>
    /// Checks if a tag slug already exists.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeTagId">Tag ID to exclude, so an update does not collide with the row it is
    /// editing. Pass 0 (the default) when inserting.</param>
    /// <returns><c>true</c> when another tag already uses the slug. Advisory only — it is a read, not a
    /// reservation, so a concurrent save can still take the slug before the caller writes.</returns>
    bool SlugExists(string slug, long excludeTagId = 0);

    /// <summary>
    /// Gets all tags with post counts.
    /// </summary>
    /// <returns>Every tag ordered by <c>TagName</c> ascending, each with <c>PostCount</c> populated; an
    /// empty sequence — never <c>null</c> — on an empty table. An unused tag is kept with a count of
    /// zero rather than dropped.</returns>
    IEnumerable<BlogTag> GetAllWithCounts();

    /// <summary>
    /// Searches tags by name for autocomplete.
    /// </summary>
    /// <param name="query">Search query string; matched as a case-insensitive substring.</param>
    /// <returns>At most ten matching tags ordered by <c>TagName</c> ascending; an empty sequence when
    /// nothing matches. The cap is applied in SQL, so a one-character query cannot pull the whole
    /// table.</returns>
    IEnumerable<BlogTag> SearchTags(string query);

    /// <summary>
    /// Deletes a tag by ID.
    /// </summary>
    /// <param name="tagId">Tag ID to delete. An unknown identifier affects no rows and is a no-op, not
    /// an error. Every post's association with the tag goes with it; the posts themselves survive.</param>
    void Delete(long tagId);

    /// <summary>
    /// Gets tags for a specific post.
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <returns>The post's tags ordered by <c>TagName</c> ascending; an empty sequence — never
    /// <c>null</c> — for an untagged or unknown post.</returns>
    IEnumerable<BlogTag> GetTagsForPost(long postId);

    /// <summary>
    /// Sets tags for a post (replaces existing).
    /// </summary>
    /// <param name="postId">Post ID.</param>
    /// <param name="tagIds">The complete set of tag IDs the post should carry. This replaces rather than
    /// merges: any tag not listed is detached, and <c>null</c> or an empty sequence detaches every tag.</param>
    void SetTagsForPost(long postId, IEnumerable<long> tagIds);

    /// <summary>
    /// Gets posts by tag ID.
    /// </summary>
    /// <param name="tagId">Tag ID.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>Published, non-deleted posts carrying the tag, newest <c>CreatedOn</c> first; an empty
    /// sequence when the tag is unused or the offset is past the end.</returns>
    IEnumerable<BlogPost> GetPostsByTag(long tagId, int pageSize, int offset);

    /// <summary>
    /// Gets count of posts with a specific tag.
    /// </summary>
    /// <param name="tagId">Tag ID.</param>
    /// <returns>The number of published, non-deleted posts carrying the tag; zero for an unused or
    /// unknown tag. Applies the same filter as <see cref="GetPostsByTag"/>, so the two page
    /// together.</returns>
    int GetPostCountByTag(long tagId);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above, which are deleted once every
    // caller has migrated. Implemented by BlogEngine.DbAccess.BlogTagRepo with genuine async Dapper.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets a tag by its URL slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetBySlug"/>; an unknown slug is a normal
    /// answer and returns <c>null</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query by slug → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The tag if found, <c>null</c> otherwise.</returns>
    Task<BlogTag?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a tag slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SlugExists"/>. The exclusion parameter
    /// lets an update ignore the row being edited.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludeTagId">Tag ID to exclude (for updates).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another tag already uses the slug.</returns>
    Task<bool> SlugExistsAsync(string slug, long excludeTagId = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all tags with post counts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetAllWithCounts"/>. Counts cover
    /// published, non-deleted posts only, so the number matches what a reader can actually browse; the
    /// LEFT JOIN keeps an unused tag visible with a count of zero.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → grouped left join → materialise.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Tags with the computed PostCount field.</returns>
    Task<IEnumerable<BlogTag>> GetAllWithCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches tags by name for autocomplete, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SearchTags"/>; capped at ten rows in SQL
    /// because the result feeds a type-ahead list.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → ILIKE query with LIMIT.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">Search query string.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>At most ten matching tags, alphabetically.</returns>
    Task<IEnumerable<BlogTag>> SearchTagsAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a tag and its post associations, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="Delete"/>. The junction rows go first
    /// because <c>PostTag</c> holds a foreign key to <c>Tag</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → delete junction rows → delete the tag.</para>
    /// <para><b>Side Effects:</b> Removes the tag and every post's association with it.</para>
    /// </remarks>
    /// <param name="tagId">Tag ID to delete.</param>
    /// <param name="cancellationToken">Cancels the statements.</param>
    /// <returns>A task that completes when both statements have run.</returns>
    Task DeleteAsync(long tagId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tags attached to one post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetTagsForPost"/>; a post with no tags
    /// yields an empty sequence, which is what the post page's tag strip expects.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → junction join → materialise.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="postId">Post ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post's tags, alphabetically.</returns>
    Task<IEnumerable<BlogTag>> GetTagsForPostAsync(long postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the set of tags attached to a post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SetTagsForPost"/>. Replace-not-merge, run
    /// inside one transaction so a failure between the delete and the inserts cannot leave the post
    /// with no tags at all.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → begin transaction → delete → insert each
    /// → commit, or roll back and rethrow.</para>
    /// <para><b>Side Effects:</b> Rewrites this post's rows in <c>PostTag</c>.</para>
    /// </remarks>
    /// <param name="postId">Post ID.</param>
    /// <param name="tagIds">The complete set of tag IDs the post should carry. This replaces rather
    /// than merges: any tag not listed is detached, and <c>null</c> or an empty sequence detaches every
    /// tag.</param>
    /// <param name="cancellationToken">Cancels the transaction. Cancellation between the delete and the
    /// inserts rolls the transaction back, so the post is never left with a partial tag set.</param>
    /// <returns>A task that completes when the transaction has committed. An unknown tag id violates the
    /// junction's foreign key and faults the task; the whole set is rolled back.</returns>
    Task SetTagsForPostAsync(long postId, IEnumerable<long> tagIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets published posts carrying one tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostsByTag"/>; backs
    /// <c>/tag/{slug}</c>, with drafts and soft-deleted rows excluded in SQL.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → junction join with LIMIT/OFFSET.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tagId">Tag ID.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published posts with this tag, newest first.</returns>
    Task<IEnumerable<BlogPost>> GetPostsByTagAsync(long tagId, int pageSize, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of posts with a specific tag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostCountByTag"/>; matches
    /// <see cref="GetPostsByTagAsync"/> filter for filter so the last page is never empty.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting join.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="tagId">Tag ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published posts with this tag.</returns>
    Task<int> GetPostCountByTagAsync(long tagId, CancellationToken cancellationToken = default);
}
