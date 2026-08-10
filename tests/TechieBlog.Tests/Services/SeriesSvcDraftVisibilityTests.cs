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

        seriesRepo.GetBySlug(SeriesSlug).Returns(new BlogSeries
        {
            SeriesId = SeriesId,
            Name = "Blazor Server in Production",
            Slug = SeriesSlug,
            PostCount = 1
        });

        postRepo.GetPublishedPostsBySeries(SeriesId).Returns(
        [
            new BlogPost { PostID = 10, Title = "Published Part", SeriesId = SeriesId, SeriesPartNumber = 1, Published = true }
        ]);

        postRepo.GetPostsBySeries(SeriesId).Returns(
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
    public void GetSeriesBySlugAttachesPublishedPartsOnlyByDefault()
    {
        // Arrange, Act
        var series = service.GetSeriesBySlug(SeriesSlug);

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
    public void GetSeriesBySlugUsesThePublishedOnlyRepositoryRead()
    {
        // Arrange, Act
        service.GetSeriesBySlug(SeriesSlug);

        // Assert
        postRepo.Received(1).GetPublishedPostsBySeries(SeriesId);
        postRepo.DidNotReceive().GetPostsBySeries(Arg.Any<long>());
    }

    /// <summary>
    /// The number of parts attached to an anonymous read equals the header's published part count,
    /// so the list and the "N Parts" badge above it can never disagree.
    /// </summary>
    [Fact]
    public void GetSeriesBySlugAttachesAsManyPartsAsTheBadgeCounts()
    {
        // Arrange, Act
        var series = service.GetSeriesBySlug(SeriesSlug);

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
    public void GetSeriesBySlugAttachesDraftPartsWhenDraftsAreRequested()
    {
        // Arrange, Act
        var series = service.GetSeriesBySlug(SeriesSlug, includeDrafts: true);

        // Assert
        Assert.NotNull(series);
        Assert.Contains(series!.Posts, post => post.Title == "Embargoed Draft Part");
        postRepo.Received(1).GetPostsBySeries(SeriesId);
        postRepo.DidNotReceive().GetPublishedPostsBySeries(Arg.Any<long>());
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
    public void GetSeriesBySlugRejectsBlankSlugWithoutReading(string slug)
    {
        // Arrange, Act
        var series = service.GetSeriesBySlug(slug);

        // Assert
        Assert.Null(series);
        postRepo.DidNotReceive().GetPublishedPostsBySeries(Arg.Any<long>());
        postRepo.DidNotReceive().GetPostsBySeries(Arg.Any<long>());
    }

    /// <summary>
    /// An unknown slug yields null and no part read is attempted, so the page renders its
    /// series-not-found state rather than throwing.
    /// </summary>
    [Fact]
    public void GetSeriesBySlugReturnsNullForUnknownSlug()
    {
        // Arrange
        seriesRepo.GetBySlug("no-such-series").Returns((BlogSeries?)null);

        // Act
        var series = service.GetSeriesBySlug("no-such-series");

        // Assert
        Assert.Null(series);
        postRepo.DidNotReceive().GetPublishedPostsBySeries(Arg.Any<long>());
    }
}
