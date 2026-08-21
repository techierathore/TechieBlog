namespace BlogModels.Interfaces;

/// <summary>
/// In-memory read-through cache for settings, taxonomy and listing data (REQ-NFR-018, BRD-78).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Gives every service one way to cache and — more importantly — one way
/// to invalidate. Entries are grouped by a tag so a single content change can evict every
/// listing that depends on it without the caller knowing the individual keys.</para>
///
/// <para><b>Code Flow:</b> caller asks for a key with <see cref="GetOrCreate{T}"/> → hit is
/// returned directly; miss runs the factory, stores the value under the given tag and returns
/// it → a write path calls <see cref="EvictTag"/> at the defined invalidation event.</para>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.Services.MemoryCacheService</c> over
/// <c>Microsoft.Extensions.Caching.Memory.IMemoryCache</c>.</para>
///
/// <para><b>Usage:</b> Cache <i>through</i> the owning service (for example
/// <c>ISiteSettingsService</c>) — do not cache the same data in two places.</para>
/// </remarks>
public interface ICacheService
{
    /// <summary>
    /// Returns a cached value, producing and storing it on a miss.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="key">Cache key; use the constants on <c>CacheTags</c>-owning services.</param>
    /// <param name="tag">Invalidation tag the entry belongs to.</param>
    /// <param name="factory">Factory invoked on a miss.</param>
    /// <param name="lifetime">Absolute lifetime; a service-specific default is used when null.</param>
    /// <returns>The cached or freshly produced value.</returns>
    T GetOrCreate<T>(string key, string tag, Func<T> factory, TimeSpan? lifetime = null);

    /// <summary>
    /// Removes a single cache entry.
    /// </summary>
    /// <param name="key">The cache key to drop.</param>
    void Evict(string key);

    /// <summary>
    /// Removes every entry stored under an invalidation tag.
    /// </summary>
    /// <param name="tag">The tag to evict, for example <c>CacheTags.Content</c>.</param>
    void EvictTag(string tag);

    /// <summary>
    /// Removes every entry this service owns.
    /// </summary>
    void Clear();
}

/// <summary>
/// The invalidation tags recognised by <see cref="ICacheService"/> and by the output cache.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Names the cache groups so producers and invalidators cannot drift
/// apart. The same strings are used as output-cache tags in the host so one eviction call can
/// clear both layers.</para>
/// <para><b>Usage:</b> <c>cacheService.EvictTag(CacheTags.Content)</c> whenever a post is
/// published, unpublished or deleted.</para>
/// </remarks>
public static class CacheTags
{
    /// <summary>Site-wide settings; invalidated when an administrator saves settings.</summary>
    public const string Settings = "settings";

    /// <summary>Categories, tags and series; invalidated on any taxonomy edit.</summary>
    public const string Taxonomy = "taxonomy";

    /// <summary>Post listings, feeds and the sitemap; invalidated on publish/unpublish/delete.</summary>
    public const string Content = "content";
}
