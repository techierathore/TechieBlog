using BlogModels.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using System.Collections.Concurrent;

namespace BlogEngine.Services;

/// <summary>
/// Tag-aware in-memory cache used for settings, taxonomy and listings (REQ-NFR-018, BRD-78).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="IMemoryCache"/> on its own cannot enumerate or group its
/// keys, so invalidating "everything that depends on published content" is impossible without
/// bookkeeping. This service keeps a key set per tag and links every entry to a
/// <see cref="CancellationChangeToken"/> for that tag, so one <see cref="EvictTag"/> call
/// expires the whole group atomically.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="GetOrCreate{T}"/> returns a hit, or runs the factory and stores the value
///     with the tag's change token and an absolute expiry.</item>
///   <item>A write path calls <see cref="EvictTag"/> at the defined invalidation event —
///     settings saved, taxonomy edited, or a post published/unpublished/deleted.</item>
///   <item>Cancelling the token expires every entry carrying it in one step.</item>
/// </list>
///
/// <para><b>Cache keys are a security boundary — construct them precisely.</b> This service does
/// not namespace, hash or otherwise transform the key it is given: the string the caller supplies
/// is the key, compared ordinally, in a cache shared by the whole process. Two consequences
/// follow, and the second is the dangerous one:</para>
/// <list type="bullet">
///   <item>Two components that happen to pick the same string share an entry, and the second one
///     will read the first one's value — typed as whatever <c>T</c> it asked for.</item>
///   <item><b>A key that omits a discriminator leaks data across that discriminator.</b> A key of
///     <c>"my-comments"</c> serves the first user's comments to every subsequent user; the correct
///     key is <c>"my-comments:{userId}"</c>. The same applies to any per-role, per-locale or
///     per-audience variation. The general rule: <i>every input that changes the value must appear
///     in the key</i>.</item>
/// </list>
/// <para>House convention for building one: <c>{area}:{entity}:{discriminator}</c>, lower-case,
/// colon-separated, with the discriminator last (for example <c>settings:effective</c>,
/// <c>taxonomy:tags:all</c>, <c>content:post:{slug}</c>). Never cache a value that varies per user
/// under a key that does not name the user. When in doubt, do not cache — a slow page is
/// recoverable, a cross-user disclosure is not.</para>
///
/// <para><b>Lifetime and eviction — three independent mechanisms:</b></para>
/// <list type="number">
///   <item><b>Absolute expiry.</b> Every entry gets one, defaulting to
///     <see cref="DefaultLifetime"/> (10 minutes) when the caller does not specify. It is
///     <i>absolute</i>, not sliding, so a hot key still refreshes on schedule and a stale entry can
///     never outlive its lifetime even if its invalidation event is missed. The default is the
///     safety net that bounds how wrong the cache can be.</item>
///   <item><b>Tag eviction.</b> <see cref="EvictTag"/> cancels the tag's token, expiring every
///     entry that registered it in one step. This is the precise mechanism: a write path calls it
///     at the moment the underlying data changes.</item>
///   <item><b>Memory pressure.</b> The underlying <see cref="IMemoryCache"/> may evict at any time.
///     <b>Never treat a cached value as durable</b> — every consumer must be able to recompute it,
///     which is why the API is get-or-create rather than get and set.</item>
/// </list>
/// <para>Note what is <i>not</i> here: no size limits, no eviction callbacks, and no eviction of
/// entries other components store in the same <see cref="IMemoryCache"/> —
/// <see cref="Clear"/> only clears what this service tagged.</para>
///
/// <para><b>Current consumers.</b> The tag vocabulary (<c>settings</c>, <c>taxonomy</c>,
/// <c>content</c>) is deliberately shared with the host's output-cache tags so one eviction can
/// clear both layers, but at present the only in-tree caller is the readiness health check, which
/// round-trips a sentinel. The invalidation contract is therefore defined <i>ahead</i> of its
/// callers: when a service starts caching through this class, the matching
/// <see cref="EvictTag"/> call must be added to its write path in the same change — a cache with
/// no invalidation is worse than no cache.</para>
///
/// <para><b>Thread safety and the factory.</b> Safe for concurrent use.
/// <see cref="GetOrCreate{T}"/> is <b>not</b> atomic, however: concurrent misses on the same key
/// each run the factory and the last write wins. That is fine for an idempotent read and wrong for
/// anything with a side effect — the factory must be a pure computation, never a write.</para>
///
/// <para><b>Dependencies:</b> <see cref="IMemoryCache"/> (registered by the host with
/// <c>AddMemoryCache</c>) and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered as a singleton (<c>ICacheService</c>) by
/// <c>BlogSvcInitializer</c>; the singleton lifetime is what lets one circuit's eviction be seen by
/// every other. Process-local — a multi-instance deployment gives each instance its own cache, and
/// an eviction on one does not reach the others. Cache <i>through</i> the service that owns the
/// data — for example <c>ISiteSettingsService</c> — rather than caching the same rows in two
/// places.</para>
/// </remarks>
public class MemoryCacheService : ICacheService
{
    /// <summary>
    /// Default absolute lifetime applied when the caller does not supply one.
    /// </summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);

    private readonly IMemoryCache memoryCache;
    private readonly ILogger<MemoryCacheService> logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> tagTokens = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialises the cache service.
    /// </summary>
    /// <param name="memoryCache">The process-wide memory cache.</param>
    /// <param name="logger">Logger used for cache-miss and invalidation diagnostics.</param>
    public MemoryCacheService(IMemoryCache memoryCache, ILogger<MemoryCacheService> logger)
    {
        this.memoryCache = memoryCache;
        this.logger = logger;
    }

    /// <summary>
    /// Returns a cached value, producing and storing it on a miss.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A miss runs the factory once and stores the result under the
    /// supplied tag with an absolute expiry, so a stale entry can never outlive its lifetime even
    /// if the invalidation event is missed.</para>
    /// <para><b>Flow:</b> try get → on miss run factory → attach tag token and expiry → store.</para>
    /// <para><b>Side Effects:</b> Populates the shared cache; logs the miss at debug level.</para>
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    /// <param name="key">
    /// Cache key. Used verbatim and compared ordinally — it must include every input that changes
    /// the value (user id, locale, role), or the entry will be served to callers it does not belong
    /// to. See the type-level remarks on key construction.
    /// </param>
    /// <param name="tag">
    /// Invalidation tag, one of the <see cref="CacheTags"/> constants. A blank or unrecognised tag
    /// is grouped under <c>Content</c> rather than left ungrouped, so no entry escapes eviction.
    /// </param>
    /// <param name="factory">
    /// Produces the value on a miss. Must be a pure computation: it may run more than once under
    /// concurrent misses, and must never carry a side effect.
    /// </param>
    /// <param name="lifetime">Absolute lifetime; <see cref="DefaultLifetime"/> when null.</param>
    /// <returns>The cached or freshly produced value.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    public T GetOrCreate<T>(string key, string tag, Func<T> factory, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);

        if (memoryCache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var value = factory();
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime ?? DefaultLifetime
        };
        options.AddExpirationToken(new CancellationChangeToken(GetTagSource(tag).Token));

        memoryCache.Set(key, value, options);
        logger.LogDebug("Cache miss for {CacheKey} under tag {CacheTag}; entry stored", key, tag);
        return value;
    }

    /// <summary>
    /// Removes a single cache entry.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used when exactly one key is known to be stale, avoiding the
    /// wider cost of a tag eviction.</para>
    /// <para><b>Flow:</b> remove from the underlying cache.</para>
    /// <para><b>Side Effects:</b> Drops one entry. A blank key is ignored rather than throwing, so
    /// an eviction on a path that may not have cached anything is safe to call unconditionally.
    /// Dropping a key that was never cached is also a no-op.</para>
    /// </remarks>
    /// <param name="key">The exact cache key to drop; must match the key used to store it.</param>
    public void Evict(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            memoryCache.Remove(key);
    }

    /// <summary>
    /// Removes every entry stored under an invalidation tag.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Cancels the tag's change token, which expires every entry
    /// that registered it, then installs a fresh token so later entries can be grouped again.</para>
    /// <para><b>Flow:</b> swap the token source → cancel and dispose the old one.</para>
    /// <para><b>Side Effects:</b> Expires all entries carrying the tag; logs at information level
    /// because invalidation events are worth tracing.</para>
    /// </remarks>
    /// <param name="tag">The tag to evict.</param>
    public void EvictTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !tagTokens.TryRemove(tag, out var source))
            return;

        source.Cancel();
        source.Dispose();
        logger.LogInformation("Cache tag {CacheTag} invalidated", tag);
    }

    /// <summary>
    /// Removes every entry this service owns.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Evicts each known tag in turn; entries stored by other
    /// components in the same <see cref="IMemoryCache"/> are left alone.</para>
    /// <para><b>Flow:</b> snapshot tag names → evict each.</para>
    /// <para><b>Side Effects:</b> Expires all tagged entries.</para>
    /// </remarks>
    public void Clear()
    {
        foreach (var tag in tagTokens.Keys.ToList())
        {
            EvictTag(tag);
        }
    }

    /// <summary>
    /// Gets, creating if necessary, the cancellation source backing a tag.
    /// </summary>
    /// <param name="tag">The invalidation tag.</param>
    /// <returns>The tag's cancellation token source.</returns>
    private CancellationTokenSource GetTagSource(string tag)
    {
        var tagKey = string.IsNullOrWhiteSpace(tag) ? CacheTags.Content : tag;
        return tagTokens.GetOrAdd(tagKey, _ => new CancellationTokenSource());
    }
}
