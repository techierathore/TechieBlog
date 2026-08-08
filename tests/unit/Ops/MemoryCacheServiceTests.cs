using BlogEngine.Services;
using BlogModels.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Unit tests for <see cref="MemoryCacheService"/> (REQ-NFR-018, BRD-78).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The caching requirement is only met if invalidation works, so these
/// tests concentrate on eviction: a tag must clear its own group and leave the others alone.
/// A counting factory proves values are actually served from the cache rather than recomputed.</para>
/// <para><b>Dependencies:</b> xUnit and a real <see cref="MemoryCache"/>; no host required.</para>
/// </remarks>
public class MemoryCacheServiceTests
{
    /// <summary>
    /// A second read of the same key is served from the cache, so the factory runs once.
    /// </summary>
    [Fact]
    public void SecondReadIsServedFromCache()
    {
        var service = BuildService();
        var factoryCalls = 0;

        var first = service.GetOrCreate("key", CacheTags.Settings, () => { factoryCalls++; return "value"; });
        var second = service.GetOrCreate("key", CacheTags.Settings, () => { factoryCalls++; return "other"; });

        Assert.Equal("value", first);
        Assert.Equal("value", second);
        Assert.Equal(1, factoryCalls);
    }

    /// <summary>
    /// Evicting a single key forces the next read to run the factory again, without disturbing
    /// any other entry.
    /// </summary>
    [Fact]
    public void EvictDropsOnlyTheNamedKey()
    {
        var service = BuildService();
        service.GetOrCreate("first", CacheTags.Taxonomy, () => "one");
        service.GetOrCreate("second", CacheTags.Taxonomy, () => "two");

        service.Evict("first");

        Assert.Equal("regenerated", service.GetOrCreate("first", CacheTags.Taxonomy, () => "regenerated"));
        Assert.Equal("two", service.GetOrCreate("second", CacheTags.Taxonomy, () => "regenerated"));
    }

    /// <summary>
    /// Evicting a tag clears every entry in that group in one call — the behaviour a content
    /// publish event relies on.
    /// </summary>
    [Fact]
    public void EvictTagClearsTheWholeGroup()
    {
        var service = BuildService();
        service.GetOrCreate("listing:1", CacheTags.Content, () => "one");
        service.GetOrCreate("listing:2", CacheTags.Content, () => "two");

        service.EvictTag(CacheTags.Content);

        Assert.Equal("fresh1", service.GetOrCreate("listing:1", CacheTags.Content, () => "fresh1"));
        Assert.Equal("fresh2", service.GetOrCreate("listing:2", CacheTags.Content, () => "fresh2"));
    }

    /// <summary>
    /// Evicting one tag leaves the other groups intact, so publishing a post does not throw away
    /// the cached site settings.
    /// </summary>
    [Fact]
    public void EvictTagLeavesOtherTagsIntact()
    {
        var service = BuildService();
        service.GetOrCreate("content", CacheTags.Content, () => "post");
        service.GetOrCreate("settings", CacheTags.Settings, () => "site");

        service.EvictTag(CacheTags.Content);

        Assert.Equal("site", service.GetOrCreate("settings", CacheTags.Settings, () => "regenerated"));
    }

    /// <summary>
    /// Entries cached again after a tag eviction are still grouped, so a second publish event
    /// clears them too rather than leaking a stale generation.
    /// </summary>
    [Fact]
    public void TagRemainsUsableAfterEviction()
    {
        var service = BuildService();
        service.GetOrCreate("listing", CacheTags.Content, () => "first");
        service.EvictTag(CacheTags.Content);
        service.GetOrCreate("listing", CacheTags.Content, () => "second");

        service.EvictTag(CacheTags.Content);

        Assert.Equal("third", service.GetOrCreate("listing", CacheTags.Content, () => "third"));
    }

    /// <summary>
    /// Clear removes every tagged entry the service owns.
    /// </summary>
    [Fact]
    public void ClearRemovesEveryTaggedEntry()
    {
        var service = BuildService();
        service.GetOrCreate("content", CacheTags.Content, () => "post");
        service.GetOrCreate("settings", CacheTags.Settings, () => "site");
        service.GetOrCreate("taxonomy", CacheTags.Taxonomy, () => "tags");

        service.Clear();

        Assert.Equal("a", service.GetOrCreate("content", CacheTags.Content, () => "a"));
        Assert.Equal("b", service.GetOrCreate("settings", CacheTags.Settings, () => "b"));
        Assert.Equal("c", service.GetOrCreate("taxonomy", CacheTags.Taxonomy, () => "c"));
    }

    /// <summary>
    /// Evicting an unknown tag or key is a no-op rather than an error, so invalidation code does
    /// not have to guard every call.
    /// </summary>
    [Fact]
    public void EvictingUnknownEntriesIsHarmless()
    {
        var service = BuildService();

        service.Evict("never-cached");
        service.EvictTag("never-used");
        service.Evict(null!);
        service.EvictTag(null!);

        Assert.Equal("value", service.GetOrCreate("key", CacheTags.Content, () => "value"));
    }

    /// <summary>
    /// A blank key or a null factory is rejected outright, because a cache silently keyed on an
    /// empty string is a correctness bug waiting to happen.
    /// </summary>
    [Fact]
    public void RejectsInvalidArguments()
    {
        var service = BuildService();

        Assert.ThrowsAny<ArgumentException>(() => service.GetOrCreate("  ", CacheTags.Content, () => "value"));
        Assert.Throws<ArgumentNullException>(() => service.GetOrCreate<string>("key", CacheTags.Content, null!));
    }

    /// <summary>
    /// Builds a cache service over a real in-memory cache and a null logger.
    /// </summary>
    /// <returns>The service under test.</returns>
    private static ICacheService BuildService()
    {
        return new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance);
    }
}
