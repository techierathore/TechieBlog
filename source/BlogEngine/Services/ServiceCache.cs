using BlogModels.Interfaces;

namespace BlogEngine.Services;

/// <summary>
/// Cache keys, read helper and invalidation rules shared by the caching services (REQ-NFR-018).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="ICacheService"/> is deliberately untyped — it takes whatever
/// string it is handed and compares it ordinally — so the risk it carries is not a slow cache but a
/// <i>wrong</i> one: two services picking the same key, or a write path that forgets which tag its
/// data was filed under. This class removes both risks by making the key vocabulary and the
/// invalidation rules one piece of code that <c>BlogSvc</c>, <c>CategorySvc</c>, <c>TagSvc</c>,
/// <c>SeriesSvc</c>, <c>RatingSvc</c> and <c>SiteSettingsService</c> all share.</para>
///
/// <para><b>Code Flow:</b> a read member calls <see cref="Read{T}"/> with a key built from the
/// members below → a write member calls <see cref="InvalidateContent"/> or
/// <see cref="InvalidateTaxonomy"/> immediately after the row is persisted.</para>
///
/// <para><b>The cache is optional, and that is deliberate.</b> Every helper accepts a
/// <c>null</c> cache and degrades to running the factory — an uncached read — so a service can be
/// constructed in a unit test without a cache and behave exactly as it did before this requirement.
/// In the running application <see cref="ICacheService"/> is always registered (a singleton, from
/// <c>BlogSvcInitializer</c>), so <c>null</c> never occurs there.</para>
///
/// <para><b>Invalidation rules — why the two helpers are not symmetric.</b> A taxonomy edit
/// (renaming a category, deleting a tag) does <i>not</i> change any post listing, because the public
/// post projections carry <c>CategoryId</c> and never a category or tag <i>name</i>; so
/// <see cref="InvalidateTaxonomy"/> evicts the taxonomy tag alone. A post edit is the opposite: it
/// changes the listings <b>and</b> the per-category and per-tag post counts that
/// <c>GetAllWithCounts</c> returns, so <see cref="InvalidateContent"/> must evict both tags. Getting
/// this backwards would leave a category showing "12 posts" after the thirteenth was
/// published.</para>
///
/// <para><b>Dependencies:</b> <see cref="ICacheService"/> and <see cref="CacheTags"/>.</para>
///
/// <para><b>Usage:</b> Static — never instantiated. Add a new key as a member here rather than
/// interpolating a string at the call site, so the key space stays enumerable by reading one file.
/// Keys follow the house convention <c>{area}:{entity}:{discriminator}</c>, lower-case and
/// colon-separated, with every input that changes the value present in the discriminator.</para>
/// </remarks>
public static class ServiceCache
{
    /// <summary>Key holding the effective site-settings aggregate.</summary>
    public const string SettingsEffectiveKey = "settings:effective";

    /// <summary>Key holding every category, ordered by name.</summary>
    public const string CategoriesAllKey = "taxonomy:categories:all";

    /// <summary>Key holding every category with its post count.</summary>
    public const string CategoriesWithCountsKey = "taxonomy:categories:counts";

    /// <summary>Key holding every tag, ordered by name.</summary>
    public const string TagsAllKey = "taxonomy:tags:all";

    /// <summary>Key holding every tag with its post count.</summary>
    public const string TagsWithCountsKey = "taxonomy:tags:counts";

    /// <summary>Key holding every series, ordered for display.</summary>
    public const string SeriesAllKey = "taxonomy:series:all";

    /// <summary>Key holding every series with its part count.</summary>
    public const string SeriesWithCountsKey = "taxonomy:series:counts";

    /// <summary>Key holding the featured (newest published) post.</summary>
    public const string FeaturedPostKey = "content:posts:featured";

    /// <summary>Key holding the count of published, non-deleted posts.</summary>
    public const string PublishedPostCountKey = "content:posts:published:count";

    /// <summary>
    /// Builds the key for a category looked up by its public slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The slug is the only input that changes the value, so it is the
    /// whole discriminator.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="slug">The category slug, exactly as routed.</param>
    /// <returns>The cache key for that category.</returns>
    public static string CategoryBySlugKey(string slug) => $"taxonomy:category:slug:{slug}";

    /// <summary>
    /// Builds the key for a tag looked up by its public slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> As <see cref="CategoryBySlugKey"/> — the slug is the discriminator.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="slug">The tag slug, exactly as routed.</param>
    /// <returns>The cache key for that tag.</returns>
    public static string TagBySlugKey(string slug) => $"taxonomy:tag:slug:{slug}";

    /// <summary>
    /// Builds the key for one page of published posts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both paging arguments change the rows returned, so both appear
    /// in the key; omitting the offset would serve page one to every page.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts skipped.</param>
    /// <returns>The cache key for that page.</returns>
    public static string PublishedPostsKey(int pageSize, int offset) =>
        $"content:posts:published:{pageSize}:{offset}";

    /// <summary>
    /// Builds the key for a published post looked up by its public slug.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The slug is the discriminator. The value does not vary by user —
    /// the repository read is the same row for everyone — so no principal belongs in the key.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="slug">The post slug, exactly as routed.</param>
    /// <returns>The cache key for that post.</returns>
    public static string PostBySlugKey(string slug) => $"content:post:slug:{slug}";

    /// <summary>
    /// Builds the key for one page of published posts inside a category.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Category and both paging arguments all change the rows returned.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="categoryId">The category being listed.</param>
    /// <param name="pageSize">Number of posts per page.</param>
    /// <param name="offset">Number of posts skipped.</param>
    /// <returns>The cache key for that page.</returns>
    public static string PostsByCategoryKey(long categoryId, int pageSize, int offset) =>
        $"content:posts:category:{categoryId}:{pageSize}:{offset}";

    /// <summary>
    /// Builds the key for the published-post count of one category.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The category is the discriminator.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="categoryId">The category being counted.</param>
    /// <returns>The cache key for that count.</returns>
    public static string PostCountByCategoryKey(long categoryId) =>
        $"content:posts:category:{categoryId}:count";

    /// <summary>
    /// Builds the key for a post's mean rating.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The post is the discriminator. Rating aggregates are the same
    /// for every visitor, so nothing about the reader belongs in the key — a per-visitor value such
    /// as "the rating <i>you</i> gave" must never be stored under it.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The rated post.</param>
    /// <returns>The cache key for that average.</returns>
    public static string RatingAverageKey(long postId) => $"content:rating:average:{postId}";

    /// <summary>
    /// Builds the key for a post's rating count.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> As <see cref="RatingAverageKey"/> — the post is the discriminator.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The rated post.</param>
    /// <returns>The cache key for that count.</returns>
    public static string RatingCountKey(long postId) => $"content:rating:count:{postId}";

    /// <summary>
    /// Builds the key for a post's aggregate rating statistics.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The visitor-specific overload of this read
    /// (<c>GetPostRatingStatsForEmailAsync</c>) is <b>not</b> cached and must not reuse this key —
    /// it varies by e-mail address and would leak one visitor's own rating to the next.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The rated post.</param>
    /// <returns>The cache key for those statistics.</returns>
    public static string RatingStatsKey(long postId) => $"content:rating:stats:{postId}";

    /// <summary>
    /// Derives the key an asynchronous twin stores its value under.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Several services expose a synchronous read and an asynchronous
    /// twin of the same query (a REQ-NFR-026 artifact), and both must be cached or the two paths
    /// disagree. They cannot share one entry: the synchronous twin stores a <c>T</c> and the
    /// asynchronous one stores a <c>Task&lt;T&gt;</c>, and <see cref="ICacheService.GetOrCreate{T}"/>
    /// treats a type mismatch as a miss — so sharing a key would have each twin overwrite the
    /// other's entry on every alternating call, turning the cache into a permanent miss. The twins
    /// therefore get adjacent keys carrying the <b>same tag</b>, so one eviction still clears both
    /// and they can never hold different data.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="key">The synchronous twin's key.</param>
    /// <returns>The asynchronous twin's key.</returns>
    public static string AsyncVariant(string key) => $"{key}:task";

    /// <summary>
    /// Returns a cached value, running the factory when the value is absent or caching is disabled.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The single read helper every caching service funnels through, so
    /// "no cache configured" is handled in one place rather than guarded at ninety call sites.</para>
    /// <para><b>Flow:</b> null cache → run the factory and return; otherwise delegate to
    /// <see cref="ICacheService.GetOrCreate{T}"/>, which returns a hit or stores the factory's
    /// result under <paramref name="tag"/>.</para>
    /// <para><b>Side Effects:</b> Populates the shared cache on a miss. The factory must be a pure
    /// read — it can run more than once under concurrent misses, so it must never carry a write.</para>
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="cacheService">The cache, or null when the service was built without one.</param>
    /// <param name="key">Cache key; build it from the members of this class.</param>
    /// <param name="tag">Invalidation tag, one of the <see cref="CacheTags"/> constants.</param>
    /// <param name="factory">
    /// Pure read producing the value on a miss. When <c>T</c> is a sequence the factory must return
    /// an already-materialised one — every repository in this codebase buffers with <c>ToList</c>
    /// before disposing its connection, and a lazy sequence cached here would re-execute against a
    /// closed connection on its second enumeration.
    /// </param>
    /// <returns>The cached or freshly read value.</returns>
    public static T Read<T>(ICacheService? cacheService, string key, string tag, Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return cacheService is null ? factory() : cacheService.GetOrCreate(key, tag, factory);
    }

    /// <summary>
    /// Returns a cached value produced asynchronously, running the factory on a miss.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The asynchronous counterpart to <see cref="Read{T}"/>. The
    /// entry stored is the <see cref="Task{TResult}"/> itself, not its result, which is what makes
    /// concurrent callers share one in-flight query instead of each starting their own.</para>
    ///
    /// <para><b>A failed read is never left in the cache — this is the reason this helper exists.</b>
    /// <see cref="ICacheService.GetOrCreate{T}"/> stores the task the moment the factory returns it,
    /// long before it is known whether the query succeeded. Without the eviction below, one
    /// transient database fault would park a <i>faulted</i> task under the key and every caller for
    /// the next ten minutes would be handed that same failure — a momentary outage turned into a
    /// sustained one. The <c>catch</c> drops the entry and rethrows, so the caller's own error
    /// handling runs unchanged and the next caller retries against the database.</para>
    ///
    /// <para><b>Flow:</b> null cache → await the factory directly; otherwise get-or-create the task
    /// → await it → on failure evict the key and rethrow.</para>
    /// <para><b>Side Effects:</b> Populates the shared cache on a miss and drops the entry again if
    /// the awaited read faults or is cancelled.</para>
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="cacheService">The cache, or null when the service was built without one.</param>
    /// <param name="key">
    /// Cache key. Must be the asynchronous twin's own key — see <see cref="AsyncVariant"/> — never
    /// the key its synchronous twin uses.
    /// </param>
    /// <param name="tag">Invalidation tag, one of the <see cref="CacheTags"/> constants.</param>
    /// <param name="factory">Pure asynchronous read producing the value on a miss.</param>
    /// <returns>The cached or freshly read value.</returns>
    public static async Task<T> ReadAsync<T>(
        ICacheService? cacheService,
        string key,
        string tag,
        Func<Task<T>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (cacheService is null)
            return await factory().ConfigureAwait(false);

        var pending = cacheService.GetOrCreate(key, tag, factory);
        try
        {
            return await pending.ConfigureAwait(false);
        }
        catch
        {
            cacheService.Evict(key);
            throw;
        }
    }

    /// <summary>
    /// Invalidates everything a post write can have changed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Evicts the content tag <b>and</b> the taxonomy tag, because
    /// publishing, unpublishing or deleting a post changes both the listings and the per-category
    /// and per-tag post counts. Deliberately coarse: a listing rebuilt needlessly costs one query,
    /// a listing left stale hides a published article.</para>
    /// <para><b>Flow:</b> evict <c>content</c> → evict <c>taxonomy</c>.</para>
    /// <para><b>Side Effects:</b> Expires every entry under both tags. Safe to call when nothing was
    /// cached, and safe with a null cache — both are no-ops — so a write path may call it
    /// unconditionally.</para>
    /// </remarks>
    /// <param name="cacheService">The cache, or null when the service was built without one.</param>
    public static void InvalidateContent(ICacheService? cacheService)
    {
        if (cacheService is null)
            return;

        cacheService.EvictTag(CacheTags.Content);
        cacheService.EvictTag(CacheTags.Taxonomy);
    }

    /// <summary>
    /// Invalidates everything a taxonomy write can have changed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Evicts the taxonomy tag only. The public post projections select
    /// <c>CategoryId</c> and never a category or tag name, so renaming one cannot make a cached
    /// listing wrong; widening this to the content tag would throw away the listings on every
    /// taxonomy edit for no correctness gain. A write that changes which posts carry a tag is a
    /// <i>content</i> change and must call <see cref="InvalidateContent"/> instead.</para>
    /// <para><b>Flow:</b> evict <c>taxonomy</c>.</para>
    /// <para><b>Side Effects:</b> Expires every entry under the taxonomy tag. No-op with a null cache.</para>
    /// </remarks>
    /// <param name="cacheService">The cache, or null when the service was built without one.</param>
    public static void InvalidateTaxonomy(ICacheService? cacheService)
    {
        cacheService?.EvictTag(CacheTags.Taxonomy);
    }

    /// <summary>
    /// Invalidates the cached rating aggregates of one post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A rating is submitted or withdrawn for a single post, so the
    /// aggregate keys for that post are dropped by name rather than the whole content tag being
    /// evicted — one visitor rating an article must not cost every listing on the site.</para>
    ///
    /// <para><b>Six keys, not three.</b> Each of the three aggregates is read by a synchronous
    /// member and by an asynchronous twin, and <see cref="AsyncVariant"/> gives the twin its own
    /// key because the two store different types. Eviction by name must therefore name both, or
    /// the twin the write path forgot goes on serving the pre-write average for the rest of the
    /// entry's ten-minute life — and, worse, the two twins disagree with each other, so the home
    /// page and the post page show different star counts for the same article. This is the reason
    /// eviction lives here rather than at the call site: adding a cached twin means adding its key
    /// to this one method, not auditing every writer.</para>
    ///
    /// <para><b>Flow:</b> evict the average, count and statistics keys for the post, then their
    /// asynchronous variants.</para>
    /// <para><b>Side Effects:</b> Drops at most six entries. No-op with a null cache or for a post
    /// whose aggregates were never cached.</para>
    /// </remarks>
    /// <param name="cacheService">The cache, or null when the service was built without one.</param>
    /// <param name="postId">The post whose aggregates changed.</param>
    public static void InvalidateRatings(ICacheService? cacheService, long postId)
    {
        if (cacheService is null)
            return;

        foreach (var key in RatingKeys(postId))
        {
            cacheService.Evict(key);
            cacheService.Evict(AsyncVariant(key));
        }
    }

    /// <summary>
    /// The synchronous cache keys holding one post's rating aggregates.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Enumerated in one place so a reader — and a test — can check
    /// that <see cref="InvalidateRatings"/> covers exactly the keys <c>RatingSvc</c> writes. The
    /// asynchronous twins are derived from these with <see cref="AsyncVariant"/> rather than listed
    /// again, so the two sets cannot fall out of step.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The post whose aggregates are keyed.</param>
    /// <returns>The average, count and statistics keys for that post.</returns>
    public static IReadOnlyList<string> RatingKeys(long postId) =>
    [
        RatingAverageKey(postId),
        RatingCountKey(postId),
        RatingStatsKey(postId)
    ];
}
