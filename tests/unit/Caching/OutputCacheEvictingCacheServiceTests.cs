using BlogModels.Interfaces;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TechieBlog.Services;
using Xunit;

namespace TechieBlog.Tests.Caching;

/// <summary>
/// REQ-FN-015 / REQ-FN-037 / REQ-FN-038: a content change must reach the output cache that serves
/// RSS, the sitemap and the public listings, not only the in-memory cache.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins <see cref="OutputCacheEvictingCacheService"/> so an unpublished post
/// cannot stay in the feed for the output cache's lifetime.</para>
/// <para><b>Usage:</b> A failure means a tag eviction no longer propagates to the output cache.</para>
/// </remarks>
public class OutputCacheEvictingCacheServiceTests
{
    private static (OutputCacheEvictingCacheService Service, ICacheService Inner, IOutputCacheStore Store) Build()
    {
        var inner = Substitute.For<ICacheService>();
        var store = Substitute.For<IOutputCacheStore>();
        var service = new OutputCacheEvictingCacheService(inner, store, NullLogger<OutputCacheEvictingCacheService>.Instance);
        return (service, inner, store);
    }

    /// <summary>
    /// Evicting a tag evicts it from the wrapped cache and from the output cache.
    /// </summary>
    [Fact]
    public void EvictTagReachesBothCaches()
    {
        // Arrange
        var (service, inner, store) = Build();

        // Act
        service.EvictTag(CacheTags.Content);

        // Assert
        inner.Received(1).EvictTag(CacheTags.Content);
        store.Received(1).EvictByTagAsync(CacheTags.Content, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Clearing evicts every known tag from the output cache.
    /// </summary>
    [Fact]
    public void ClearEvictsEveryKnownOutputTag()
    {
        // Arrange
        var (service, inner, store) = Build();

        // Act
        service.Clear();

        // Assert
        inner.Received(1).Clear();
        store.Received(1).EvictByTagAsync(CacheTags.Content, Arg.Any<CancellationToken>());
        store.Received(1).EvictByTagAsync(CacheTags.Taxonomy, Arg.Any<CancellationToken>());
        store.Received(1).EvictByTagAsync(CacheTags.Settings, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An output-cache failure never breaks the save that triggered the eviction.
    /// </summary>
    [Fact]
    public void OutputCacheFailureIsSwallowed()
    {
        // Arrange
        var (service, inner, store) = Build();
        store.EvictByTagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(_ => throw new InvalidOperationException("boom"));

        // Act
        var thrown = Record.Exception(() => service.EvictTag(CacheTags.Content));

        // Assert
        Assert.Null(thrown);
        inner.Received(1).EvictTag(CacheTags.Content);
    }

    /// <summary>
    /// Reads and single-key eviction forward to the wrapped cache unchanged.
    /// </summary>
    [Fact]
    public void ReadsAndKeyEvictionForward()
    {
        // Arrange
        var (service, inner, _) = Build();
        inner.GetOrCreate("k", "t", Arg.Any<Func<int>>(), null).Returns(7);

        // Act
        var value = service.GetOrCreate("k", "t", () => 1);
        service.Evict("k");

        // Assert
        Assert.Equal(7, value);
        inner.Received(1).Evict("k");
    }
}
