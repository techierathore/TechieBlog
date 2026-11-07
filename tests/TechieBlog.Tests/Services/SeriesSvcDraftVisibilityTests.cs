using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Unit tests for the draft-visibility rule in <see cref="SeriesSvc"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-015. <c>/series/{slug}</c> is reachable by anonymous visitors, so
/// the part list it is handed must come from the published-only repository read unless the caller
/// has explicitly established that the visitor may see unpublished work. These tests pin which
/// repository member each overload reaches for, which is the decision the leak got wrong: the
/// service called the unfiltered admin read for every visitor and left the filtering to a page that
/// never did it.</para>
/// <para><b>Dependencies:</b> NSubstitute for <c>IBlogSeriesRepo</c> and <c>IBlogPostRepo</c>;
/// <see cref="NullLogger{T}"/> for the logger. No database.</para>
/// </remarks>
public class SeriesSvcDraftVisibilityTests
{
    private const string SeriesSlug = "blazor-server-in-production";
    private const long SeriesId = 1;

    private readonly IBlogSeriesRepo seriesRepo = Substitute.For<IBlogSeriesRepo>();
    private readonly IBlogPostRepo postRepo = Substitute.For<IBlogPostRepo>();
    private readonly SeriesSvc service;

    /// <summary>
    /// Wires the service under test to substituted repositories and gives the series repository a
    /// series to return for <see cref="SeriesSlug"/>.
    /// </summary>
    public SeriesSvcDraftVisibilityTests()
    {
        service = new SeriesSvc(seriesRepo, postRepo, NullLogger<SeriesSvc>.Instance);

        seriesRepo.GetBySlugAsync(SeriesSlug, Arg.Any<CancellationToken>()).Returns(new BlogSeries
        {
            SeriesId = SeriesId,
            Name = "Blazor Server in Production",
            Slug = SeriesSlug,
            PostCount = 1
        });

        postRepo.GetPublishedPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(
        [
            new BlogPost { PostID = 10, Title = "Published Part", SeriesId = SeriesId, SeriesPartNumber = 1, Published = true }
        ]);

        postRepo.GetPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(
        [
            new BlogPost { PostID = 10, Title = "Published Part", SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 11, Title = "Embargoed Draft Part", SeriesId = SeriesId, SeriesPartNumber = 2, Published = false }
        ]);
    }

    /// <summary>
    /// The single-argument overload is the one the anonymous <c>/series/{slug}</c> page calls, so it
    /// must attach published parts only. It previously attached every non-deleted part, putting each
    /// draft's title and abstract in front of anonymous visitors.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugAttachesPublishedPartsOnlyByDefault()
    {
        // Arrange, Act
        var series = await service.GetSeriesBySlugAsync(SeriesSlug, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(series);
        Assert.All(series!.Posts, post => Assert.True(post.Published));
        Assert.DoesNotContain(series.Posts, post => post.Title == "Embargoed Draft Part");
    }

    /// <summary>
    /// The default read reaches for the published-only repository member and never touches the
    /// unfiltered admin one, so the filter is applied in SQL rather than by the caller.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugUsesThePublishedOnlyRepositoryRead()
    {
        // Arrange, Act
        await service.GetSeriesBySlugAsync(SeriesSlug, TestContext.Current.CancellationToken);

        // Assert
        await postRepo.Received(1).GetPublishedPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>());
        await postRepo.DidNotReceive().GetPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The number of parts attached to an anonymous read equals the header's published part count,
    /// so the list and the "N Parts" badge above it can never disagree.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugAttachesAsManyPartsAsTheBadgeCounts()
    {
        // Arrange, Act
        var series = await service.GetSeriesBySlugAsync(SeriesSlug, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(series);
        Assert.Equal(series!.PostCount, series.Posts.Count);
    }

    /// <summary>
    /// An authoring surface that has established the visitor is a content manager asks for drafts
    /// explicitly and gets them — a fix that hid unpublished parts from everyone would break the
    /// series editor.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugAttachesDraftPartsWhenDraftsAreRequested()
    {
        // Arrange, Act
        var series = await service.GetSeriesBySlugAsync(SeriesSlug, includeDrafts: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(series);
        Assert.Contains(series!.Posts, post => post.Title == "Embargoed Draft Part");
        await postRepo.Received(1).GetPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>());
        await postRepo.DidNotReceive().GetPublishedPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A blank slug is answered without consulting either repository, so an empty route segment
    /// cannot turn into a full-table read.
    /// </summary>
    /// <param name="slug">The blank slug under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSeriesBySlugRejectsBlankSlugWithoutReading(string slug)
    {
        // Arrange, Act
        var series = await service.GetSeriesBySlugAsync(slug, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(series);
        await postRepo.DidNotReceive().GetPublishedPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await postRepo.DidNotReceive().GetPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unknown slug yields null and no part read is attempted, so the page renders its
    /// series-not-found state rather than throwing.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugReturnsNullForUnknownSlug()
    {
        // Arrange
        seriesRepo.GetBySlugAsync("no-such-series", Arg.Any<CancellationToken>()).Returns((BlogSeries?)null);

        // Act
        var series = await service.GetSeriesBySlugAsync("no-such-series", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(series);
        await postRepo.DidNotReceive().GetPublishedPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }
}
