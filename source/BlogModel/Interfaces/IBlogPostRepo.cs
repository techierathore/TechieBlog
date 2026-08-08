using BlogModels.Models;
using BlogModels.Interfaces;

namespace BlogModels;

/// <summary>
/// Data access for blog posts, including publishing, scheduling and search.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Owns every read and write against the <c>BlogPost</c> table. Public-facing
/// queries consistently exclude drafts and soft-deleted rows.</para>
///
/// <para><b>Code Flow:</b> this is the widest contract in the application because a post is read
/// through five different lenses.</para>
/// <list type="number">
///   <item>Author — <c>BlogSvc</c> generates a slug, proves it free with <see cref="SlugExistsAsync"/>,
///         then uses the inherited insert/update. <see cref="SoftDeleteAsync"/> retires a post by
///         setting a flag; nothing here removes a row.</item>
///   <item>Read one — <see cref="GetBySlugAsync"/> backs <c>/post/{slug}</c>;
///         <see cref="GetFeaturedPostAsync"/> backs the home page hero.</item>
///   <item>Browse — <see cref="GetPublishedPostsAsync"/> and
///         <see cref="GetPostsByCategoryAsync"/> page the listings, each paired with the matching count
///         member so the last page is never empty.</item>
///   <item>Series — <see cref="GetPostsBySeriesAsync"/>, <see cref="GetPostCountBySeriesAsync"/> and
///         <see cref="GetMaxPartNumberInSeriesAsync"/> serve <c>SeriesSvc</c>;
///         <see cref="ClearSeriesFromPostsAsync"/> detaches the parts before a series is deleted, so
///         they survive as standalone articles.</item>
///   <item>Schedule — <c>ScheduledPostPublisher</c> polls <see cref="GetDueScheduledPostsAsync"/> and
///         publishes what it finds; <see cref="GetScheduledPostsAsync"/> shows the author what is
///         still queued.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.BlogPostRepo</c> over Dapper.</para>
///
/// <para><b>Usage:</b> Injected into <c>BlogSvc</c>, <c>SeriesSvc</c> and the sitemap service.
/// <b>Visibility is enforced here, not by the caller</b> — every public-facing member filters drafts
/// and soft-deleted rows in SQL, so an unpublished post cannot leak through a listing even if the
/// caller forgets to check. Deletion is always soft: a "deleted" post still occupies its slug, which is
/// why <see cref="SlugExistsAsync"/> can report a collision against a post no reader can see. Paged
/// reads come back newest first, and each is paired with a count member applying an identical filter.
/// This contract has no <c>Result</c> surface — <c>BlogSvc</c> converts expected failures; here a
/// missing row is <c>null</c>, an empty page is an empty sequence, and any data-access failure is
/// thrown.</para>
///
/// <para><b>Async conversion (REQ-NFR-026).</b> Every member exists twice: a legacy blocking member and
/// an <c>…Async</c> twin carrying a <see cref="CancellationToken"/>. Call the async member. The async
/// members here are <b>abstract</b> — there is no bridged default to inherit, so every implementer
/// genuinely honours the token and every read that reaches this contract is real asynchrony. The
/// blocking members are retained only until the last caller migrates and are deleted in the final
/// stage.</para>
/// </remarks>
public interface IBlogPostRepo : IGenericRepository<BlogPost>
{
    /// <summary>
    /// Gets the dashboard's aggregate post counts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns a <see cref="BlogPost"/> used purely as a counts carrier
    /// rather than as a real post.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <returns>The counts projection, or <c>null</c> when the aggregate query yields no row.</returns>
    BlogPost? GetTheCounts();

    /// <summary>
    /// Gets a blog post by its URL slug.
    /// </summary>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <returns>BlogPost if found, null otherwise.</returns>
    BlogPost? GetBySlug(string slug);

    /// <summary>
    /// Gets published posts for public display with pagination.
    /// </summary>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>List of published, non-deleted posts.</returns>
    IEnumerable<BlogPost> GetPublishedPosts(int pageSize, int offset);

    /// <summary>
    /// Soft deletes a post by setting IsDeleted flag.
    /// </summary>
    /// <param name="postId">The post ID to delete.</param>
    void SoftDelete(long postId);

    /// <summary>
    /// Checks if a slug already exists in the database.
    /// </summary>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludePostId">Post ID to exclude (for updates).</param>
    /// <returns>True if slug exists, false otherwise.</returns>
    bool SlugExists(string slug, long excludePostId = 0);

    /// <summary>
    /// Gets the most recent published post (featured post).
    /// </summary>
    /// <returns>Most recent published post, or null if none.</returns>
    BlogPost? GetFeaturedPost();

    /// <summary>
    /// Gets the total count of published posts.
    /// </summary>
    /// <returns>Count of published, non-deleted posts.</returns>
    int GetPublishedPostCount();

    /// <summary>
    /// Gets published posts filtered by category ID.
    /// </summary>
    /// <param name="categoryId">Category ID to filter by.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <returns>List of published posts in the category.</returns>
    IEnumerable<BlogPost> GetPostsByCategory(long categoryId, int pageSize, int offset);

    /// <summary>
    /// Gets the count of published posts in a category.
    /// </summary>
    /// <param name="categoryId">Category ID.</param>
    /// <returns>Count of posts.</returns>
    int GetPostCountByCategory(long categoryId);

    /// <summary>
    /// Gets all scheduled posts for admin view.
    /// </summary>
    /// <returns>Posts with ScheduledPublishOn set and not yet published.</returns>
    IEnumerable<BlogPost> GetScheduledPosts();

    /// <summary>
    /// Gets posts that are due for publishing (scheduled time has passed).
    /// </summary>
    /// <param name="now">Current UTC time.</param>
    /// <returns>Posts ready to be published.</returns>
    IEnumerable<BlogPost> GetDueScheduledPosts(DateTime now);

    /// <summary>
    /// Gets posts belonging to a series, ordered by part number.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Posts in the series ordered by SeriesPartNumber.</returns>
    IEnumerable<BlogPost> GetPostsBySeries(long seriesId);

    /// <summary>
    /// Gets count of posts in a series.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Number of posts.</returns>
    int GetPostCountBySeries(long seriesId);

    /// <summary>
    /// Gets the highest part number in a series.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    /// <returns>Max part number, or 0 if no posts.</returns>
    int GetMaxPartNumberInSeries(long seriesId);

    /// <summary>
    /// Clears series association from all posts in a series.
    /// </summary>
    /// <param name="seriesId">Series ID.</param>
    void ClearSeriesFromPosts(long seriesId);

    /// <summary>
    /// Searches posts by title, abstract, content, and tags.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <param name="pageSize">Number of results per page.</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <returns>Matching published posts.</returns>
    IEnumerable<BlogPost> SearchPosts(string query, int pageSize = 10, int offset = 0);

    /// <summary>
    /// Gets the count of search results.
    /// </summary>
    /// <param name="query">Search query string.</param>
    /// <returns>Count of matching posts.</returns>
    int GetSearchResultCount(string query);

    // ---------------------------------------------------------------------------------------------
    // Async surface — REQ-NFR-026. Preferred over every member above, which are deleted once every
    // caller has migrated. Implemented by BlogEngine.DbAccess.BlogPostRepo with genuine async Dapper.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the dashboard's aggregate post counts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetTheCounts"/>; the result is a
    /// <see cref="BlogPost"/> used purely as a counts carrier rather than as a real post.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → aggregate query → first row.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The counts projection, or <c>null</c> when the aggregate yields no row.</returns>
    Task<BlogPost?> GetTheCountsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a blog post by its URL slug, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetBySlug"/>; an unknown or soft-deleted
    /// slug is a normal answer and returns <c>null</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → query by slug → first row or <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">URL-friendly slug identifier.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The post if found, <c>null</c> otherwise.</returns>
    Task<BlogPost?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets published posts for public display with pagination, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPublishedPosts"/>. Drafts, scheduled
    /// posts and soft-deleted rows are excluded in SQL so no caller can leak one by forgetting to
    /// filter.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered LIMIT/OFFSET query → materialise.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published, non-deleted posts, newest first.</returns>
    Task<IEnumerable<BlogPost>> GetPublishedPostsAsync(int pageSize, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a post by setting its IsDeleted flag, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SoftDelete"/>. The row is flagged rather
    /// than removed so comments, ratings and view counts that reference it stay valid.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → UPDATE setting the flag and timestamp.</para>
    /// <para><b>Side Effects:</b> Marks one row deleted; it disappears from every public query.</para>
    /// </remarks>
    /// <param name="postId">The post ID to delete.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the row has been flagged.</returns>
    Task SoftDeleteAsync(long postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a post slug is already taken, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SlugExists"/>. The exclusion parameter
    /// lets an update ignore the row being edited, so re-saving a post without renaming it does not
    /// collide with itself.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting query → compare to zero.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="slug">The slug to check.</param>
    /// <param name="excludePostId">Post ID to exclude (for updates).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns><c>true</c> when another post already uses the slug.</returns>
    Task<bool> SlugExistsAsync(string slug, long excludePostId = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent published post, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetFeaturedPost"/>. "Featured" means
    /// simply the newest published post, so the home page stays current without a curated flag.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → ordered query with LIMIT 1.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The newest published post, or <c>null</c> when nothing is published.</returns>
    Task<BlogPost?> GetFeaturedPostAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the total count of published posts, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPublishedPostCount"/>. Applies exactly
    /// the filter <see cref="GetPublishedPostsAsync"/> applies, or the last page comes up empty.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published, non-deleted posts.</returns>
    Task<int> GetPublishedPostCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets published posts filtered by category ID, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostsByCategory"/>; backs
    /// <c>/category/{slug}</c>.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered LIMIT/OFFSET query → materialise.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="categoryId">Category ID to filter by.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Published posts in the category, newest first.</returns>
    Task<IEnumerable<BlogPost>> GetPostsByCategoryAsync(long categoryId, int pageSize, int offset, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of published posts in a category, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostCountByCategory"/>; matches
    /// <see cref="GetPostsByCategoryAsync"/> filter for filter.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="categoryId">Category ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of published posts in the category.</returns>
    Task<int> GetPostCountByCategoryAsync(long categoryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all scheduled posts for the admin view, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetScheduledPosts"/>. A post counts as
    /// scheduled when it is unpublished <i>and</i> carries a schedule time, which is what separates it
    /// from a plain draft.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered query ordered soonest-first.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Posts with ScheduledPublishOn set and not yet published.</returns>
    Task<IEnumerable<BlogPost>> GetScheduledPostsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets posts whose scheduled publish time has passed, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetDueScheduledPosts"/>. The
    /// implementation normalises <paramref name="now"/> to <c>DateTimeKind.Unspecified</c> before
    /// binding it, because the column is <c>TIMESTAMP</c> without time zone and a <c>Kind = Utc</c>
    /// value would be sent as <c>timestamptz</c> and shift the comparison by the host's UTC offset.</para>
    /// <para><b>Flow:</b> normalise the instant → open the connection asynchronously → filtered query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="now">Current UTC time.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Posts ready to be published, soonest first.</returns>
    Task<IEnumerable<BlogPost>> GetDueScheduledPostsAsync(DateTime now, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets posts belonging to a series ordered by part number, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostsBySeries"/>. Drafts are included
    /// because the admin series editor lists unpublished parts too, so public callers filter on
    /// <c>Published</c> themselves.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → filtered query ordered by part number.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">Series ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Posts in the series ordered by SeriesPartNumber.</returns>
    Task<IEnumerable<BlogPost>> GetPostsBySeriesAsync(long seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of published posts in a series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetPostCountBySeries"/>; counts published
    /// parts only, so the "N parts" a reader sees equals the parts they can open.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">Series ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The number of published posts in the series.</returns>
    Task<int> GetPostCountBySeriesAsync(long seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the highest part number in a series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetMaxPartNumberInSeries"/>. Drafts count
    /// here, unlike <see cref="GetPostCountBySeriesAsync"/> — an unpublished part still occupies its
    /// number, so proposing the next one must account for it.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → MAX aggregate with COALESCE.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="seriesId">Series ID.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Max part number, or <c>0</c> if the series has no posts.</returns>
    Task<int> GetMaxPartNumberInSeriesAsync(long seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the series association from all posts in a series, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="ClearSeriesFromPosts"/>. Run before a
    /// series is deleted so its posts survive as standalone articles.</para>
    /// <para><b>Flow:</b> open the connection asynchronously → UPDATE clearing the series columns.</para>
    /// <para><b>Side Effects:</b> Clears SeriesId and SeriesPartNumber on every matching post.</para>
    /// </remarks>
    /// <param name="seriesId">Series ID.</param>
    /// <param name="cancellationToken">Cancels the statement.</param>
    /// <returns>A task that completes when the rows have been written.</returns>
    Task ClearSeriesFromPostsAsync(long seriesId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches posts by title, abstract, content and tags, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="SearchPosts"/>. A blank query is answered
    /// without touching the database — it can only come from an empty search box, and the honest
    /// answer is "no results" rather than "every post".</para>
    /// <para><b>Flow:</b> guard the query → open the connection asynchronously → ILIKE query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">Search query string.</param>
    /// <param name="pageSize">Number of results per page.</param>
    /// <param name="offset">Number of results to skip.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Matching published posts, or an empty sequence for a blank query.</returns>
    Task<IEnumerable<BlogPost>> SearchPostsAsync(string query, int pageSize = 10, int offset = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of search results, without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Async twin of <see cref="GetSearchResultCount"/>; applies the same
    /// blank-query guard and the same filters as <see cref="SearchPostsAsync"/>.</para>
    /// <para><b>Flow:</b> guard the query → open the connection asynchronously → counting query.</para>
    /// <para><b>Side Effects:</b> None — read-only query.</para>
    /// </remarks>
    /// <param name="query">Search query string.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The count of matching posts, or <c>0</c> for a blank query.</returns>
    Task<int> GetSearchResultCountAsync(string query, CancellationToken cancellationToken = default);
}
