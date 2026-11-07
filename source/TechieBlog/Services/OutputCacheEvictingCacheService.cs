using BlogModels.Interfaces;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging;

namespace TechieBlog.Services;

/// <summary>
/// Website-side <see cref="ICacheService"/> decorator: every tag eviction also evicts the matching
/// output-cache tag, so RSS, the sitemap and the public listings never outlive a content change
/// (REQ-FN-015, REQ-FN-037, REQ-FN-038).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>BlogSvc</c> invalidates the in-memory cache the moment a post is saved,
/// published, unpublished or deleted, but the feeds and public listings sit in the ASP.NET Core
/// <i>output</i> cache (15 and 5 minute lifetimes, tagged <see cref="CacheTags.Content"/>), which is a
/// separate store that nothing evicted. An unpublished post therefore stayed in the RSS feed and the
/// sitemap for up to a quarter of an hour, and a newly published one stayed out of both.</para>
/// <para><b>Code Flow:</b> <see cref="EvictTag"/> evicts the inner service first, then the output
/// cache tag; <see cref="Clear"/> evicts every known tag. All other members forward unchanged.</para>
/// <para><b>Dependencies:</b> the wrapped <see cref="ICacheService"/> and
/// <see cref="IOutputCacheStore"/>, both singletons.</para>
/// <para><b>Usage:</b> Registered in <c>Program.cs</c> after the engine graph so it wins resolution.
/// The in-memory output-cache store completes eviction synchronously, so waiting on it here costs
/// nothing and guarantees the next request already sees fresh content.</para>
/// </remarks>
public sealed class OutputCacheEvictingCacheService : ICacheService
{
    private static readonly string[] KnownTags = { CacheTags.Content, CacheTags.Taxonomy, CacheTags.Settings };

    private readonly ICacheService inner;
    private readonly IOutputCacheStore outputCacheStore;
    private readonly ILogger<OutputCacheEvictingCacheService> logger;

    /// <summary>
    /// Creates the decorator.
    /// </summary>
    /// <param name="inner">The in-memory cache service being wrapped.</param>
    /// <param name="outputCacheStore">The output cache to evict alongside it.</param>
    /// <param name="logger">Logger for an eviction failure.</param>
    public OutputCacheEvictingCacheService(
        ICacheService inner,
        IOutputCacheStore outputCacheStore,
        ILogger<OutputCacheEvictingCacheService> logger)
    {
        this.inner = inner;
        this.outputCacheStore = outputCacheStore;
        this.logger = logger;
    }

    /// <inheritdoc />
    public T GetOrCreate<T>(string key, string tag, Func<T> factory, TimeSpan? lifetime = null)
    {
        return inner.GetOrCreate(key, tag, factory, lifetime);
    }

    /// <inheritdoc />
    public void Evict(string key)
    {
        inner.Evict(key);
    }

    /// <inheritdoc />
    public void EvictTag(string tag)
    {
        inner.EvictTag(tag);
        EvictOutputCache(tag);
    }

    /// <inheritdoc />
    public void Clear()
    {
        inner.Clear();
        foreach (var tag in KnownTags)
        {
            EvictOutputCache(tag);
        }
    }

    private void EvictOutputCache(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return;

        try
        {
            outputCacheStore.EvictByTagAsync(tag, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Output cache tag {CacheTag} could not be evicted", tag);
        }
    }
}
