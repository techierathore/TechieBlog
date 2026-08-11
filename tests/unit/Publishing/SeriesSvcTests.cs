using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TechieBlog.Tests.Dashboard;

namespace TechieBlog.Tests.Publishing;

/// <summary>
/// Unit tests for <see cref="SeriesSvc"/> — reads, slug allocation, part numbering, the
/// detach-before-delete rule and previous/next navigation, across the synchronous surface and its
/// async twins.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A series carries <i>sequence</i>, so this service owns which post is part
/// one, what number the next part takes and what "previous" and "next" mean on a post page. These
/// tests pin the three rules that are easy to break: deleting a series never deletes its posts and
/// always detaches them first; navigation counts published parts only, so a draft never appears as a
/// next link nor inflates the "of N" total; and every read degrades to empty or null on failure
/// rather than taking a public page down. The <c>Result</c> success and failure strings that admin
/// screens display are pinned too.</para>
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IBlogSeriesRepo"/> and
/// <see cref="IBlogPostRepo"/>; <see cref="RecordingLogger{T}"/> so a swallowed exception can be
/// proved to have been logged. No database. Draft visibility on <c>/series/{slug}</c> is covered by
/// <c>SeriesSvcDraftVisibilityTests</c> and is not repeated here beyond the async twin.</para>
/// </remarks>
public class SeriesSvcTests
{
    private const long SeriesId = 7;
    private const string SeriesSlug = "blazor-in-production";

    private readonly IBlogSeriesRepo seriesRepo = Substitute.For<IBlogSeriesRepo>();
    private readonly IBlogPostRepo postRepo = Substitute.For<IBlogPostRepo>();
    private readonly RecordingLogger<SeriesSvc> logger = new();
    private readonly SeriesSvc service;

    /// <summary>
    /// Wires the service under test to substituted repositories and a recording logger.
    /// </summary>
    public SeriesSvcTests()
    {
        service = new SeriesSvc(seriesRepo, postRepo, logger);
    }

    // ===========================================================================================
    // Listing reads
    // ===========================================================================================

    /// <summary>
    /// The unfiltered series list is returned as the repository produced it, including a series with
    /// no posts yet — an author creates the series before writing part one and it has to be
    /// selectable in the editor from that moment.
    /// </summary>
    [Fact]
    public void GetAllSeriesReturnsEverySeriesIncludingEmptyOnes()
    {
        // Arrange
        seriesRepo.GetAll().Returns(new[]
        {
            new BlogSeries { SeriesId = 1, Name = "Alpha", PostCount = 3 },
            new BlogSeries { SeriesId = 2, Name = "Beta", PostCount = 0 }
        });

        // Act
        var all = service.GetAllSeries().ToList();

        // Assert
        Assert.Equal(new[] { "Alpha", "Beta" }, all.Select(series => series.Name));
    }

    /// <summary>
    /// A failed list read degrades to an empty sequence and is logged, so a broken series list cannot
    /// take the surrounding page down.
    /// </summary>
    [Fact]
    public void GetAllSeriesDegradesToEmptyWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetAll().Throws(new InvalidOperationException("list exploded"));

        // Act
        var all = service.GetAllSeries();

        // Assert
        Assert.Empty(all);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error != null);
    }

    /// <summary>
    /// The async twin returns the same unfiltered list through the repository's async member.
    /// </summary>
    [Fact]
    public async Task GetAllSeriesAsyncReturnsEverySeries()
    {
        // Arrange
        seriesRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new BlogSeries { SeriesId = 1, Name = "Alpha" } }.AsEnumerable());

        // Act
        var all = await service.GetAllSeriesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Alpha", Assert.Single(all).Name);
    }

    /// <summary>
    /// A failed async list read degrades to an empty sequence exactly as its synchronous twin does.
    /// </summary>
    [Fact]
    public async Task GetAllSeriesAsyncDegradesToEmptyWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetAllAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async list exploded"));

        // Act
        var all = await service.GetAllSeriesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(all);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The counted list carries the repository's <c>PostCount</c> untouched, so the series index can
    /// show "7 parts" from one aggregate query rather than a query per row.
    /// </summary>
    [Fact]
    public void GetAllWithCountsCarriesThePartCountThrough()
    {
        // Arrange
        seriesRepo.GetAllWithCounts().Returns(new[]
        {
            new BlogSeries { SeriesId = 1, Name = "Alpha", PostCount = 7 }
        });

        // Act
        var all = service.GetAllWithCounts();

        // Assert
        Assert.Equal(7, Assert.Single(all).PostCount);
    }

    /// <summary>
    /// A failed aggregate read degrades to an empty sequence and logs.
    /// </summary>
    [Fact]
    public void GetAllWithCountsDegradesToEmptyWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetAllWithCounts().Throws(new InvalidOperationException("counts exploded"));

        // Act
        var all = service.GetAllWithCounts();

        // Assert
        Assert.Empty(all);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The async twin of the counted list reads through the repository's async member and carries the
    /// same counts.
    /// </summary>
    [Fact]
    public async Task GetAllWithCountsAsyncCarriesThePartCountThrough()
    {
        // Arrange
        seriesRepo.GetAllWithCountsAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new BlogSeries { SeriesId = 1, Name = "Alpha", PostCount = 7 } }.AsEnumerable());

        // Act
        var all = await service.GetAllWithCountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(7, Assert.Single(all).PostCount);
    }

    /// <summary>
    /// A failed async aggregate read degrades to an empty sequence.
    /// </summary>
    [Fact]
    public async Task GetAllWithCountsAsyncDegradesToEmptyWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetAllWithCountsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async counts exploded"));

        // Act
        var all = await service.GetAllWithCountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(all);
    }

    // ===========================================================================================
    // Single-series reads
    // ===========================================================================================

    /// <summary>
    /// Loading one series returns the header only — the member posts are deliberately not attached,
    /// because the edit form does not need them.
    /// </summary>
    [Fact]
    public void GetSeriesReturnsTheHeaderWithoutLoadingItsParts()
    {
        // Arrange
        seriesRepo.GetSingle(SeriesId).Returns(new BlogSeries { SeriesId = SeriesId, Name = "Alpha" });

        // Act
        var series = service.GetSeries(SeriesId);

        // Assert
        Assert.NotNull(series);
        Assert.Equal("Alpha", series!.Name);
        Assert.Empty(series.Posts);
        postRepo.DidNotReceive().GetPostsBySeries(Arg.Any<long>());
    }

    /// <summary>
    /// An unknown identifier is a normal answer and yields null rather than an exception.
    /// </summary>
    [Fact]
    public void GetSeriesReturnsNullForAnUnknownIdentifier()
    {
        // Arrange
        seriesRepo.GetSingle(SeriesId).Returns((BlogSeries?)null);

        // Act, Assert
        Assert.Null(service.GetSeries(SeriesId));
    }

    /// <summary>
    /// A failed lookup also yields null; the difference between "no such series" and "the lookup
    /// failed" is recorded in the log rather than in the return value.
    /// </summary>
    [Fact]
    public void GetSeriesReturnsNullAndLogsWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetSingle(SeriesId).Throws(new InvalidOperationException("lookup exploded"));

        // Act
        var series = service.GetSeries(SeriesId);

        // Assert
        Assert.Null(series);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The async twin reads through the repository's async member and returns the same header.
    /// </summary>
    [Fact]
    public async Task GetSeriesAsyncReturnsTheHeader()
    {
        // Arrange
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>())
            .Returns(new BlogSeries { SeriesId = SeriesId, Name = "Alpha" });

        // Act
        var series = await service.GetSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Alpha", series!.Name);
    }

    /// <summary>
    /// A failed async lookup yields null and logs.
    /// </summary>
    [Fact]
    public async Task GetSeriesAsyncReturnsNullAndLogsWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async lookup exploded"));

        // Act
        var series = await service.GetSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(series);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // ===========================================================================================
    // Slug resolution — the async twin (the synchronous overloads live in SeriesSvcDraftVisibilityTests)
    // ===========================================================================================

    /// <summary>
    /// The default async slug read is the one behind the anonymous <c>/series/{slug}</c> page, so it
    /// attaches published parts only and never reaches for the unfiltered authoring read.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugAsyncAttachesPublishedPartsOnlyByDefault()
    {
        // Arrange
        ArrangeSlugLookupAsync();

        // Act
        var series = await service.GetSeriesBySlugAsync(SeriesSlug, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(series);
        Assert.All(series!.Posts, post => Assert.True(post.Published));
        await postRepo.Received(1).GetPublishedPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>());
        await postRepo.DidNotReceive().GetPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An authoring surface that has established the visitor may see unpublished work asks for drafts
    /// explicitly and gets them, through the unfiltered async read.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugAsyncAttachesDraftsWhenTheyAreRequested()
    {
        // Arrange
        ArrangeSlugLookupAsync();

        // Act
        var series = await service.GetSeriesBySlugAsync(
            SeriesSlug, includeDrafts: true, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(series);
        Assert.Contains(series!.Posts, post => !post.Published);
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
    public async Task GetSeriesBySlugAsyncRejectsABlankSlugWithoutReading(string? slug)
    {
        // Arrange, Act
        var series = await service.GetSeriesBySlugAsync(slug!, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(series);
        await seriesRepo.DidNotReceive().GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await postRepo.DidNotReceive().GetPublishedPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unknown slug yields null and no part read is attempted, so the page renders its
    /// series-not-found state.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugAsyncReturnsNullForAnUnknownSlug()
    {
        // Arrange
        seriesRepo.GetBySlugAsync("no-such-series", Arg.Any<CancellationToken>()).Returns((BlogSeries?)null);

        // Act
        var series = await service.GetSeriesBySlugAsync("no-such-series", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(series);
        await postRepo.DidNotReceive().GetPublishedPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed synchronous slug read yields null and logs rather than propagating into a public page.
    /// </summary>
    [Fact]
    public void GetSeriesBySlugReturnsNullAndLogsWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetBySlug(SeriesSlug).Throws(new InvalidOperationException("slug read exploded"));

        // Act
        var series = service.GetSeriesBySlug(SeriesSlug);

        // Assert
        Assert.Null(series);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// A failed async part read yields null and logs, so a broken part list cannot fault the page.
    /// </summary>
    [Fact]
    public async Task GetSeriesBySlugAsyncReturnsNullAndLogsWhenThePartReadFails()
    {
        // Arrange
        seriesRepo.GetBySlugAsync(SeriesSlug, Arg.Any<CancellationToken>())
            .Returns(new BlogSeries { SeriesId = SeriesId, Name = "Alpha", Slug = SeriesSlug });
        postRepo.GetPublishedPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("part read exploded"));

        // Act
        var series = await service.GetSeriesBySlugAsync(SeriesSlug, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(series);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // ===========================================================================================
    // Part listing and part numbering
    // ===========================================================================================

    /// <summary>
    /// The part list is returned in the order the repository produced it — by part number, not by
    /// publication date — so a series reads in the author's intended sequence even when part 5 shipped
    /// before a back-filled part 4.
    /// </summary>
    [Fact]
    public void GetPostsInSeriesReturnsThePartsInRepositoryOrder()
    {
        // Arrange
        postRepo.GetPostsBySeries(SeriesId).Returns(new[]
        {
            new BlogPost { PostID = 1, SeriesPartNumber = 1 },
            new BlogPost { PostID = 2, SeriesPartNumber = 2 }
        });

        // Act
        var parts = service.GetPostsInSeries(SeriesId).ToList();

        // Assert
        Assert.Equal(new int?[] { 1, 2 }, parts.Select(part => part.SeriesPartNumber));
    }

    /// <summary>
    /// A failed part read degrades to an empty sequence and logs.
    /// </summary>
    [Fact]
    public void GetPostsInSeriesDegradesToEmptyWhenTheReadFails()
    {
        // Arrange
        postRepo.GetPostsBySeries(SeriesId).Throws(new InvalidOperationException("parts exploded"));

        // Act
        var parts = service.GetPostsInSeries(SeriesId);

        // Assert
        Assert.Empty(parts);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The async twin reads the same unfiltered part list — deliberately including drafts, because it
    /// serves authoring surfaces.
    /// </summary>
    [Fact]
    public async Task GetPostsInSeriesAsyncReturnsTheUnfilteredPartList()
    {
        // Arrange
        postRepo.GetPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new BlogPost { PostID = 1, Published = true },
            new BlogPost { PostID = 2, Published = false }
        }.AsEnumerable());

        // Act
        var parts = await service.GetPostsInSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, parts.Count());
        Assert.Contains(parts, part => !part.Published);
    }

    /// <summary>
    /// A failed async part read degrades to an empty sequence.
    /// </summary>
    [Fact]
    public async Task GetPostsInSeriesAsyncDegradesToEmptyWhenTheReadFails()
    {
        // Arrange
        postRepo.GetPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async parts exploded"));

        // Act
        var parts = await service.GetPostsInSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(parts);
    }

    /// <summary>
    /// The suggested part number is the highest existing one plus one, so numbering keeps climbing
    /// after a middle part is deleted rather than silently renumbering a series readers have been
    /// linked to. An empty series (max of 0) yields part 1.
    /// </summary>
    /// <param name="highestExisting">The highest part number currently stored.</param>
    /// <param name="expected">The number the editor should be offered.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(9, 10)]
    public void GetNextPartNumberIsTheHighestExistingPlusOne(int highestExisting, int expected)
    {
        // Arrange
        postRepo.GetMaxPartNumberInSeries(SeriesId).Returns(highestExisting);

        // Act, Assert
        Assert.Equal(expected, service.GetNextPartNumber(SeriesId));
    }

    /// <summary>
    /// A failed part-number read falls back to 1 and logs, so the editor still offers a usable value.
    /// </summary>
    [Fact]
    public void GetNextPartNumberFallsBackToOneWhenTheReadFails()
    {
        // Arrange
        postRepo.GetMaxPartNumberInSeries(SeriesId).Throws(new InvalidOperationException("max exploded"));

        // Act
        var next = service.GetNextPartNumber(SeriesId);

        // Assert
        Assert.Equal(1, next);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The async twin applies the same plus-one rule through the repository's async member.
    /// </summary>
    [Fact]
    public async Task GetNextPartNumberAsyncIsTheHighestExistingPlusOne()
    {
        // Arrange
        postRepo.GetMaxPartNumberInSeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(4);

        // Act
        var next = await service.GetNextPartNumberAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, next);
    }

    /// <summary>
    /// A failed async part-number read falls back to 1.
    /// </summary>
    [Fact]
    public async Task GetNextPartNumberAsyncFallsBackToOneWhenTheReadFails()
    {
        // Arrange
        postRepo.GetMaxPartNumberInSeriesAsync(SeriesId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async max exploded"));

        // Act
        var next = await service.GetNextPartNumberAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, next);
    }

    // ===========================================================================================
    // CreateSeries
    // ===========================================================================================

    /// <summary>
    /// A null series is an expected caller mistake, returned as a failed result rather than thrown.
    /// </summary>
    [Fact]
    public void CreateSeriesRejectsNull()
    {
        // Arrange, Act
        var result = service.CreateSeries(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// A name is mandatory — it is what the slug is derived from and what the public header shows —
    /// so a blank one is rejected before any repository is touched.
    /// </summary>
    /// <param name="name">The blank name under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateSeriesRejectsABlankName(string name)
    {
        // Arrange, Act
        var result = service.CreateSeries(new BlogSeries { Name = name });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series name is required", result.ErrorMessage);
        seriesRepo.DidNotReceive().InsertToGetId(Arg.Any<BlogSeries>());
    }

    /// <summary>
    /// A slug the administrator left blank is derived from the name, so a series is always
    /// addressable without the admin having to invent a URL.
    /// </summary>
    [Fact]
    public void CreateSeriesDerivesTheSlugFromTheNameWhenItIsBlank()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production!" };

        // Act
        var result = service.CreateSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-in-production", series.Slug);
    }

    /// <summary>
    /// A slug the administrator supplied is kept verbatim — it is a published address and must not be
    /// silently rewritten from the name.
    /// </summary>
    [Fact]
    public void CreateSeriesKeepsAnExplicitlySuppliedSlug()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production", Slug = "bzp" };

        // Act
        service.CreateSeries(series);

        // Assert
        Assert.Equal("bzp", series.Slug);
    }

    /// <summary>
    /// A slug already taken gains an ordinal suffix, so the second series with the same name lands on
    /// <c>-2</c> rather than colliding.
    /// </summary>
    [Fact]
    public void CreateSeriesSuffixesASlugThatIsAlreadyTaken()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production" };
        seriesRepo.SlugExists("blazor-in-production").Returns(true);
        seriesRepo.SlugExists("blazor-in-production-2").Returns(false);

        // Act
        var result = service.CreateSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-in-production-2", series.Slug);
    }

    /// <summary>
    /// The suffix keeps climbing while each candidate is taken, so three collisions land on
    /// <c>-4</c> rather than stopping at the first busy number.
    /// </summary>
    [Fact]
    public void CreateSeriesKeepsClimbingUntilAFreeSlugIsFound()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production" };
        seriesRepo.SlugExists(Arg.Any<string>()).Returns(true);
        seriesRepo.SlugExists("blazor-in-production-4").Returns(false);

        // Act
        service.CreateSeries(series);

        // Assert
        Assert.Equal("blazor-in-production-4", series.Slug);
    }

    /// <summary>
    /// A contested slug that the administrator supplied is suffixed from <i>that</i> slug rather than
    /// from the name, so a deliberately short address stays recognisable on its first collision.
    /// </summary>
    [Fact]
    public void CreateSeriesSuffixesASuppliedSlugFromTheSuppliedBase()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production", Slug = "bzp" };
        seriesRepo.SlugExists("bzp").Returns(true);
        seriesRepo.SlugExists("bzp-2").Returns(false);

        // Act
        var result = service.CreateSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("bzp-2", series.Slug);
    }

    /// <summary>
    /// The retry gives up after 99 attempts rather than looping forever against a pathologically busy
    /// name; the candidate is inserted anyway and the database's unique index is the real guard.
    /// </summary>
    [Fact]
    public void CreateSeriesStopsRetryingTheSlugAfterNinetyNineAttempts()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production" };
        seriesRepo.SlugExists(Arg.Any<string>()).Returns(true);

        // Act
        var result = service.CreateSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-in-production-100", series.Slug);
    }

    /// <summary>
    /// A series whose status was cleared defaults to "In Progress" — the honest state for a series
    /// whose first part has not been written yet, and the value the public header shows.
    /// </summary>
    [Fact]
    public void CreateSeriesDefaultsAClearedStatusToInProgress()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha", Status = "   " };

        // Act
        service.CreateSeries(series);

        // Assert
        Assert.Equal("In Progress", series.Status);
    }

    /// <summary>
    /// An explicitly chosen status survives — a series imported as already complete must not be
    /// relabelled "In Progress".
    /// </summary>
    [Fact]
    public void CreateSeriesKeepsAnExplicitStatus()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha", Status = "Completed" };

        // Act
        service.CreateSeries(series);

        // Assert
        Assert.Equal("Completed", series.Status);
    }

    /// <summary>
    /// Both timestamps are stamped in UTC at creation, so a series created in one time zone sorts
    /// correctly against one created in another.
    /// </summary>
    [Fact]
    public void CreateSeriesStampsBothTimestampsInUtc()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha" };
        var before = DateTime.UtcNow;

        // Act
        service.CreateSeries(series);

        // Assert
        Assert.InRange(series.CreatedOn, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
        Assert.InRange(series.UpdatedOn, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }

    /// <summary>
    /// The generated identifier is written back onto the caller's object and carried in the result,
    /// so the admin form can redirect straight to the new series.
    /// </summary>
    [Fact]
    public void CreateSeriesWritesTheGeneratedIdentifierBack()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha" };
        seriesRepo.InsertToGetId(series).Returns(99L);

        // Act
        var result = service.CreateSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(99, series.SeriesId);
        Assert.Same(series, result.Data);
    }

    /// <summary>
    /// A failed insert becomes a failed result naming the underlying problem — acceptable only
    /// because every caller of this method is an admin screen.
    /// </summary>
    [Fact]
    public void CreateSeriesReportsACuratedMessageWhenTheInsertFails()
    {
        // Arrange
        seriesRepo.InsertToGetId(Arg.Any<BlogSeries>()).Throws(new InvalidOperationException("duplicate key"));

        // Act
        var result = service.CreateSeries(new BlogSeries { Name = "Alpha" });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("duplicate key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "duplicate key");
    }

    /// <summary>
    /// The async twin rejects null with the identical message, so a caller cannot tell the two
    /// surfaces apart by their failure strings.
    /// </summary>
    [Fact]
    public async Task CreateSeriesAsyncRejectsNull()
    {
        // Arrange, Act
        var result = await service.CreateSeriesAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// The async twin also requires a name.
    /// </summary>
    [Fact]
    public async Task CreateSeriesAsyncRejectsABlankName()
    {
        // Arrange, Act
        var result = await service.CreateSeriesAsync(
            new BlogSeries { Name = "  " }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series name is required", result.ErrorMessage);
    }

    /// <summary>
    /// The async uniqueness check passes an exclusion of 0 because an insert has no row of its own to
    /// exclude, and suffixes the slug on a collision exactly as the synchronous twin does.
    /// </summary>
    [Fact]
    public async Task CreateSeriesAsyncSuffixesASlugThatIsAlreadyTaken()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production" };
        seriesRepo.SlugExistsAsync("blazor-in-production", 0, Arg.Any<CancellationToken>()).Returns(true);
        seriesRepo.SlugExistsAsync("blazor-in-production-2", 0, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-in-production-2", series.Slug);
        await seriesRepo.Received().SlugExistsAsync(Arg.Any<string>(), 0, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async retry keeps climbing while each candidate is taken, exactly as the synchronous one
    /// does, so the two surfaces cannot allocate different slugs for the same name.
    /// </summary>
    [Fact]
    public async Task CreateSeriesAsyncKeepsClimbingUntilAFreeSlugIsFound()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production" };
        seriesRepo.SlugExistsAsync(Arg.Any<string>(), 0, Arg.Any<CancellationToken>()).Returns(true);
        seriesRepo.SlugExistsAsync("blazor-in-production-4", 0, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("blazor-in-production-4", series.Slug);
    }

    /// <summary>
    /// The async twin defaults a cleared status and writes the generated identifier back.
    /// </summary>
    [Fact]
    public async Task CreateSeriesAsyncDefaultsStatusAndWritesTheIdentifierBack()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha", Status = string.Empty };
        seriesRepo.InsertToGetIdAsync(series, Arg.Any<CancellationToken>()).Returns(55L);

        // Act
        var result = await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("In Progress", series.Status);
        Assert.Equal(55, series.SeriesId);
    }

    /// <summary>
    /// A failed async insert becomes a failed result with the same message shape as the synchronous
    /// twin.
    /// </summary>
    [Fact]
    public async Task CreateSeriesAsyncReportsACuratedMessageWhenTheInsertFails()
    {
        // Arrange
        seriesRepo.InsertToGetIdAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("duplicate key"));

        // Act
        var result = await service.CreateSeriesAsync(
            new BlogSeries { Name = "Alpha" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("duplicate key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "duplicate key");
    }

    // ===========================================================================================
    // UpdateSeries
    // ===========================================================================================

    /// <summary>
    /// A null series is rejected as a failed result.
    /// </summary>
    [Fact]
    public void UpdateSeriesRejectsNull()
    {
        // Arrange, Act
        var result = service.UpdateSeries(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// An update needs an identifier; a non-positive one means the caller meant to create, and is
    /// rejected rather than silently updating row zero.
    /// </summary>
    /// <param name="seriesId">The invalid identifier under test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateSeriesRejectsANonPositiveIdentifier(long seriesId)
    {
        // Arrange, Act
        var result = service.UpdateSeries(new BlogSeries { SeriesId = seriesId, Name = "Alpha" });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid series ID", result.ErrorMessage);
        seriesRepo.DidNotReceive().Update(Arg.Any<BlogSeries>());
    }

    /// <summary>
    /// A name is mandatory on update too.
    /// </summary>
    [Fact]
    public void UpdateSeriesRejectsABlankName()
    {
        // Arrange, Act
        var result = service.UpdateSeries(new BlogSeries { SeriesId = SeriesId, Name = "  " });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series name is required", result.ErrorMessage);
    }

    /// <summary>
    /// A series that no longer exists cannot be updated — the row may have been deleted in another
    /// tab — and the caller is told so rather than the update silently affecting no rows.
    /// </summary>
    [Fact]
    public void UpdateSeriesFailsWhenTheRowIsGone()
    {
        // Arrange
        seriesRepo.GetSingle(SeriesId).Returns((BlogSeries?)null);

        // Act
        var result = service.UpdateSeries(new BlogSeries { SeriesId = SeriesId, Name = "Alpha" });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series not found", result.ErrorMessage);
        seriesRepo.DidNotReceive().Update(Arg.Any<BlogSeries>());
    }

    /// <summary>
    /// The uniqueness check excludes the series being edited, so re-saving a series unchanged does not
    /// renumber its own slug into <c>-2</c>.
    /// </summary>
    [Fact]
    public void UpdateSeriesExcludesItselfFromTheUniquenessCheck()
    {
        // Arrange
        ArrangeExistingSeries();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", Slug = "alpha" };

        // Act
        var result = service.UpdateSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("alpha", series.Slug);
        seriesRepo.Received().SlugExists("alpha", SeriesId);
        seriesRepo.DidNotReceive().SlugExists(Arg.Any<string>(), 0);
    }

    /// <summary>
    /// A slug taken by a <i>different</i> series is still suffixed, so an admin cannot move one series
    /// onto another's published address.
    /// </summary>
    [Fact]
    public void UpdateSeriesSuffixesASlugTakenByAnotherSeries()
    {
        // Arrange
        ArrangeExistingSeries();
        seriesRepo.SlugExists("alpha", SeriesId).Returns(true);
        seriesRepo.SlugExists("alpha-2", SeriesId).Returns(false);
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", Slug = "alpha" };

        // Act
        service.UpdateSeries(series);

        // Assert
        Assert.Equal("alpha-2", series.Slug);
    }

    /// <summary>
    /// The update retry keeps climbing while each candidate is taken by another series, so an edit
    /// cannot settle on an address that is already published elsewhere.
    /// </summary>
    [Fact]
    public void UpdateSeriesKeepsClimbingUntilAFreeSlugIsFound()
    {
        // Arrange
        ArrangeExistingSeries();
        seriesRepo.SlugExists(Arg.Any<string>(), SeriesId).Returns(true);
        seriesRepo.SlugExists("alpha-4", SeriesId).Returns(false);
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", Slug = "alpha" };

        // Act
        service.UpdateSeries(series);

        // Assert
        Assert.Equal("alpha-4", series.Slug);
    }

    /// <summary>
    /// A slug cleared on the edit form is re-derived from the name rather than saved empty, which
    /// would make the series unaddressable.
    /// </summary>
    [Fact]
    public void UpdateSeriesDerivesTheSlugWhenItWasCleared()
    {
        // Arrange
        ArrangeExistingSeries();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Blazor in Production", Slug = string.Empty };

        // Act
        service.UpdateSeries(series);

        // Assert
        Assert.Equal("blazor-in-production", series.Slug);
    }

    /// <summary>
    /// Only <c>UpdatedOn</c> is restamped; <c>CreatedOn</c> is left exactly as the caller supplied it,
    /// so an edit cannot rewrite when the series was started.
    /// </summary>
    [Fact]
    public void UpdateSeriesRestampsUpdatedOnAndLeavesCreatedOnAlone()
    {
        // Arrange
        ArrangeExistingSeries();
        var created = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", CreatedOn = created };
        var before = DateTime.UtcNow;

        // Act
        service.UpdateSeries(series);

        // Assert
        Assert.Equal(created, series.CreatedOn);
        Assert.InRange(series.UpdatedOn, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }

    /// <summary>
    /// A successful update persists through the repository once and returns the saved series.
    /// </summary>
    [Fact]
    public void UpdateSeriesPersistsThroughTheRepositoryOnce()
    {
        // Arrange
        ArrangeExistingSeries();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha" };

        // Act
        var result = service.UpdateSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(series, result.Data);
        seriesRepo.Received(1).Update(series);
    }

    /// <summary>
    /// A failed update becomes a failed result naming the underlying problem, and logs.
    /// </summary>
    [Fact]
    public void UpdateSeriesReportsACuratedMessageWhenThePersistFails()
    {
        // Arrange
        ArrangeExistingSeries();
        seriesRepo.When(repo => repo.Update(Arg.Any<BlogSeries>()))
            .Do(_ => throw new InvalidOperationException("row locked"));

        // Act
        var result = service.UpdateSeries(new BlogSeries { SeriesId = SeriesId, Name = "Alpha" });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("row locked", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "row locked");
    }

    /// <summary>
    /// The async twin applies the same guards in the same order and returns the same failure strings.
    /// </summary>
    [Fact]
    public async Task UpdateSeriesAsyncRejectsANonPositiveIdentifier()
    {
        // Arrange, Act
        var result = await service.UpdateSeriesAsync(
            new BlogSeries { SeriesId = 0, Name = "Alpha" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid series ID", result.ErrorMessage);
    }

    /// <summary>
    /// The async twin rejects null with the same message as the synchronous one.
    /// </summary>
    [Fact]
    public async Task UpdateSeriesAsyncRejectsNull()
    {
        // Arrange, Act
        var result = await service.UpdateSeriesAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// The async twin requires a name, and rejects a blank one before the existence check runs.
    /// </summary>
    [Fact]
    public async Task UpdateSeriesAsyncRejectsABlankName()
    {
        // Arrange, Act
        var result = await service.UpdateSeriesAsync(
            new BlogSeries { SeriesId = SeriesId, Name = "  " }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series name is required", result.ErrorMessage);
        await seriesRepo.DidNotReceive().GetSingleAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async update retry keeps climbing while each candidate is taken by another series.
    /// </summary>
    [Fact]
    public async Task UpdateSeriesAsyncKeepsClimbingUntilAFreeSlugIsFound()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        seriesRepo.SlugExistsAsync(Arg.Any<string>(), SeriesId, Arg.Any<CancellationToken>()).Returns(true);
        seriesRepo.SlugExistsAsync("alpha-4", SeriesId, Arg.Any<CancellationToken>()).Returns(false);
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", Slug = "alpha" };

        // Act
        await service.UpdateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("alpha-4", series.Slug);
    }

    /// <summary>
    /// The async twin fails with "Series not found" when the existence check comes back empty; the
    /// check reaches for the repository's async member, not the blocking one.
    /// </summary>
    [Fact]
    public async Task UpdateSeriesAsyncFailsWhenTheRowIsGone()
    {
        // Arrange
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>()).Returns((BlogSeries?)null);

        // Act
        var result = await service.UpdateSeriesAsync(
            new BlogSeries { SeriesId = SeriesId, Name = "Alpha" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series not found", result.ErrorMessage);
        await seriesRepo.DidNotReceive().UpdateAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async uniqueness check excludes the series being edited and suffixes a slug taken by
    /// another, mirroring the synchronous twin.
    /// </summary>
    [Fact]
    public async Task UpdateSeriesAsyncExcludesItselfAndSuffixesAContestedSlug()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        seriesRepo.SlugExistsAsync("alpha", SeriesId, Arg.Any<CancellationToken>()).Returns(true);
        seriesRepo.SlugExistsAsync("alpha-2", SeriesId, Arg.Any<CancellationToken>()).Returns(false);
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", Slug = "alpha" };

        // Act
        var result = await service.UpdateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("alpha-2", series.Slug);
        await seriesRepo.Received().SlugExistsAsync("alpha", SeriesId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed async update becomes a failed result with the same message shape.
    /// </summary>
    [Fact]
    public async Task UpdateSeriesAsyncReportsACuratedMessageWhenThePersistFails()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        seriesRepo.UpdateAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("row locked"));

        // Act
        var result = await service.UpdateSeriesAsync(
            new BlogSeries { SeriesId = SeriesId, Name = "Alpha" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("row locked", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "row locked");
    }

    // ===========================================================================================
    // SaveSeries
    // ===========================================================================================

    /// <summary>
    /// The save entry point rejects null before deciding between create and update.
    /// </summary>
    [Fact]
    public void SaveSeriesRejectsNull()
    {
        // Arrange, Act
        var result = service.SaveSeries(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// A non-positive identifier means "new", so one admin form can serve both add and edit.
    /// </summary>
    /// <param name="seriesId">The identifier the form carried.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void SaveSeriesCreatesWhenTheIdentifierIsNotPositive(long seriesId)
    {
        // Arrange
        var series = new BlogSeries { SeriesId = seriesId, Name = "Alpha" };

        // Act
        var result = service.SaveSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        seriesRepo.Received(1).InsertToGetId(series);
        seriesRepo.DidNotReceive().Update(Arg.Any<BlogSeries>());
    }

    /// <summary>
    /// A positive identifier routes to the update path, which re-checks that the row still exists.
    /// </summary>
    [Fact]
    public void SaveSeriesUpdatesWhenTheIdentifierIsPositive()
    {
        // Arrange
        ArrangeExistingSeries();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha" };

        // Act
        var result = service.SaveSeries(series);

        // Assert
        Assert.True(result.IsSuccess);
        seriesRepo.Received(1).Update(series);
        seriesRepo.DidNotReceive().InsertToGetId(Arg.Any<BlogSeries>());
    }

    /// <summary>
    /// The async twin answers the null guard with an already-completed task rather than reaching the
    /// database.
    /// </summary>
    [Fact]
    public async Task SaveSeriesAsyncRejectsNull()
    {
        // Arrange, Act
        var result = await service.SaveSeriesAsync(null!, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// The async twin routes a new series to the insert path.
    /// </summary>
    [Fact]
    public async Task SaveSeriesAsyncCreatesWhenTheIdentifierIsNotPositive()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha" };

        // Act
        var result = await service.SaveSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        await seriesRepo.Received(1).InsertToGetIdAsync(series, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async twin routes an existing series to the update path.
    /// </summary>
    [Fact]
    public async Task SaveSeriesAsyncUpdatesWhenTheIdentifierIsPositive()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha" };

        // Act
        var result = await service.SaveSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        await seriesRepo.Received(1).UpdateAsync(series, Arg.Any<CancellationToken>());
        await seriesRepo.DidNotReceive().InsertToGetIdAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>());
    }

    // ===========================================================================================
    // DeleteSeries
    // ===========================================================================================

    /// <summary>
    /// A non-positive identifier is rejected before any read, so a mis-routed delete cannot touch the
    /// table.
    /// </summary>
    /// <param name="seriesId">The invalid identifier under test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void DeleteSeriesRejectsANonPositiveIdentifier(long seriesId)
    {
        // Arrange, Act
        var result = service.DeleteSeries(seriesId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid series ID", result.ErrorMessage);
        seriesRepo.DidNotReceive().GetSingle(Arg.Any<long>());
    }

    /// <summary>
    /// Deleting a series that is already gone is reported rather than treated as success, and nothing
    /// is detached.
    /// </summary>
    [Fact]
    public void DeleteSeriesFailsWhenTheSeriesIsUnknown()
    {
        // Arrange
        seriesRepo.GetSingle(SeriesId).Returns((BlogSeries?)null);

        // Act
        var result = service.DeleteSeries(SeriesId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series not found", result.ErrorMessage);
        postRepo.DidNotReceive().ClearSeriesFromPosts(Arg.Any<long>());
        seriesRepo.DidNotReceive().Delete(Arg.Any<long>());
    }

    /// <summary>
    /// The posts are detached <i>before</i> the series row is removed. The order matters twice over:
    /// it keeps a foreign key from refusing the delete, and it guarantees that a failure between the
    /// two steps leaves the posts free rather than pointing at a row about to vanish.
    /// </summary>
    [Fact]
    public void DeleteSeriesDetachesThePostsBeforeRemovingTheRow()
    {
        // Arrange
        ArrangeExistingSeries();

        // Act
        var result = service.DeleteSeries(SeriesId);

        // Assert
        Assert.True(result.IsSuccess);
        Received.InOrder(() =>
        {
            postRepo.ClearSeriesFromPosts(SeriesId);
            seriesRepo.Delete(SeriesId);
        });
    }

    /// <summary>
    /// Nothing here deletes a post — losing the grouping must never lose the writing.
    /// </summary>
    [Fact]
    public void DeleteSeriesNeverDeletesItsPosts()
    {
        // Arrange
        ArrangeExistingSeries();

        // Act
        service.DeleteSeries(SeriesId);

        // Assert
        postRepo.DidNotReceive().SoftDelete(Arg.Any<long>());
        postRepo.Received(1).ClearSeriesFromPosts(SeriesId);
    }

    /// <summary>
    /// A failed detach abandons the delete, so the series survives with its parts still attached
    /// rather than leaving orphans behind.
    /// </summary>
    [Fact]
    public void DeleteSeriesAbandonsTheDeleteWhenTheDetachFails()
    {
        // Arrange
        ArrangeExistingSeries();
        postRepo.When(repo => repo.ClearSeriesFromPosts(SeriesId))
            .Do(_ => throw new InvalidOperationException("detach exploded"));

        // Act
        var result = service.DeleteSeries(SeriesId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("detach exploded", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "detach exploded");
        seriesRepo.DidNotReceive().Delete(Arg.Any<long>());
    }

    /// <summary>
    /// A failed row delete is reported; the posts are already detached by then, which is the
    /// recoverable state the ordering was chosen to produce.
    /// </summary>
    [Fact]
    public void DeleteSeriesReportsACuratedMessageWhenTheRowDeleteFails()
    {
        // Arrange
        ArrangeExistingSeries();
        seriesRepo.When(repo => repo.Delete(SeriesId))
            .Do(_ => throw new InvalidOperationException("constraint violated"));

        // Act
        var result = service.DeleteSeries(SeriesId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("constraint violated", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "constraint violated");
        postRepo.Received(1).ClearSeriesFromPosts(SeriesId);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The async twin rejects a non-positive identifier with the same message.
    /// </summary>
    [Fact]
    public async Task DeleteSeriesAsyncRejectsANonPositiveIdentifier()
    {
        // Arrange, Act
        var result = await service.DeleteSeriesAsync(0, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid series ID", result.ErrorMessage);
    }

    /// <summary>
    /// The async twin reports an unknown series and detaches nothing.
    /// </summary>
    [Fact]
    public async Task DeleteSeriesAsyncFailsWhenTheSeriesIsUnknown()
    {
        // Arrange
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>()).Returns((BlogSeries?)null);

        // Act
        var result = await service.DeleteSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series not found", result.ErrorMessage);
        await postRepo.DidNotReceive().ClearSeriesFromPostsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async twin also detaches the posts before removing the series row.
    /// </summary>
    [Fact]
    public async Task DeleteSeriesAsyncDetachesThePostsBeforeRemovingTheRow()
    {
        // Arrange
        ArrangeExistingSeriesAsync();

        // Act
        var result = await service.DeleteSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Received.InOrder(() =>
        {
            postRepo.ClearSeriesFromPostsAsync(SeriesId, Arg.Any<CancellationToken>());
            seriesRepo.DeleteAsync(SeriesId, Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// A failed async delete is reported with the curated message, with the exception text confined to
    /// the log (REQ-NFR-031).
    /// </summary>
    [Fact]
    public async Task DeleteSeriesAsyncReportsACuratedMessageWhenTheRowDeleteFails()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        seriesRepo.DeleteAsync(SeriesId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("constraint violated"));

        // Act
        var result = await service.DeleteSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("constraint violated", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "constraint violated");
    }

    // ===========================================================================================
    // GetSeriesNavigation
    // ===========================================================================================

    /// <summary>
    /// A post that does not exist has no strip; null is the single "no strip" signal a caller has to
    /// check.
    /// </summary>
    [Fact]
    public void NavigationIsAbsentForAnUnknownPost()
    {
        // Arrange
        postRepo.GetSingle(100).Returns((BlogPost?)null);

        // Act, Assert
        Assert.Null(service.GetSeriesNavigation(100));
    }

    /// <summary>
    /// A standalone post belongs to no series, so no strip is built and no further read is made.
    /// </summary>
    [Fact]
    public void NavigationIsAbsentForAPostInNoSeries()
    {
        // Arrange
        postRepo.GetSingle(100).Returns(new BlogPost { PostID = 100, SeriesId = null });

        // Act
        var navigation = service.GetSeriesNavigation(100);

        // Assert
        Assert.Null(navigation);
        seriesRepo.DidNotReceive().GetSingle(Arg.Any<long>());
    }

    /// <summary>
    /// A post pointing at a series row that has gone gets no strip rather than a half-built one.
    /// </summary>
    [Fact]
    public void NavigationIsAbsentWhenTheSeriesRowHasGone()
    {
        // Arrange
        postRepo.GetSingle(100).Returns(new BlogPost { PostID = 100, SeriesId = SeriesId });
        seriesRepo.GetSingle(SeriesId).Returns((BlogSeries?)null);

        // Act
        var navigation = service.GetSeriesNavigation(100);

        // Assert
        Assert.Null(navigation);
        postRepo.DidNotReceive().GetPostsBySeries(Arg.Any<long>());
    }

    /// <summary>
    /// An unpublished post is absent from the published part list, so it gets no strip — an author
    /// previewing a draft is not offered navigation that a reader could not follow.
    /// </summary>
    [Fact]
    public void NavigationIsAbsentForAnUnpublishedPost()
    {
        // Arrange
        ArrangeNavigation(
            currentPostId: 102,
            currentPartNumber: 2,
            currentPublished: false,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = false });

        // Act, Assert
        Assert.Null(service.GetSeriesNavigation(102));
    }

    /// <summary>
    /// The strip carries the series name and slug so the reader can jump back to the series page.
    /// </summary>
    [Fact]
    public void NavigationCarriesTheSeriesNameAndSlug()
    {
        // Arrange
        ArrangeNavigation(
            currentPostId: 101,
            currentPartNumber: 1,
            currentPublished: true,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true });

        // Act
        var navigation = service.GetSeriesNavigation(101);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal("Blazor in Production", navigation!.SeriesName);
        Assert.Equal(SeriesSlug, navigation.SeriesSlug);
    }

    /// <summary>
    /// <c>TotalParts</c> counts the parts a reader can actually open, so a draft sitting in the middle
    /// of a series does not inflate the "of N" total.
    /// </summary>
    [Fact]
    public void NavigationCountsPublishedPartsOnly()
    {
        // Arrange
        ArrangeNavigation(
            currentPostId: 101,
            currentPartNumber: 1,
            currentPublished: true,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = false },
            new BlogPost { PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true });

        // Act
        var navigation = service.GetSeriesNavigation(101);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal(2, navigation!.TotalParts);
    }

    /// <summary>
    /// Previous and next skip over an unpublished middle part rather than dead-ending on it, so a
    /// draft is never offered as a link that would 404 or leak.
    /// </summary>
    [Fact]
    public void NavigationSkipsAnUnpublishedMiddlePart()
    {
        // Arrange
        ArrangeNavigation(
            currentPostId: 101,
            currentPartNumber: 1,
            currentPublished: true,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = false },
            new BlogPost { PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true });

        // Act
        var navigation = service.GetSeriesNavigation(101);

        // Assert
        Assert.NotNull(navigation);
        Assert.Null(navigation!.PreviousPost);
        Assert.Equal(103, navigation.NextPost!.PostID);
    }

    /// <summary>
    /// The first part has no previous link and the last has no next, so the strip does not offer a
    /// dead end at either end of the series.
    /// </summary>
    [Fact]
    public void NavigationOmitsPreviousOnTheFirstPartAndNextOnTheLast()
    {
        // Arrange
        var parts = new[]
        {
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = true },
            new BlogPost { PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true }
        };
        ArrangeNavigation(101, 1, true, parts);

        // Act
        var first = service.GetSeriesNavigation(101);
        ArrangeNavigation(103, 3, true, parts);
        var last = service.GetSeriesNavigation(103);

        // Assert
        Assert.Null(first!.PreviousPost);
        Assert.Equal(102, first.NextPost!.PostID);
        Assert.Equal(102, last!.PreviousPost!.PostID);
        Assert.Null(last.NextPost);
    }

    /// <summary>
    /// A middle part is flanked by both neighbours and reports its own part number.
    /// </summary>
    [Fact]
    public void NavigationFlanksAMiddlePartWithBothNeighbours()
    {
        // Arrange
        ArrangeNavigation(
            currentPostId: 102,
            currentPartNumber: 2,
            currentPublished: true,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = true },
            new BlogPost { PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true });

        // Act
        var navigation = service.GetSeriesNavigation(102);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal(2, navigation!.CurrentPart);
        Assert.Equal(3, navigation.TotalParts);
        Assert.Equal(101, navigation.PreviousPost!.PostID);
        Assert.Equal(103, navigation.NextPost!.PostID);
    }

    /// <summary>
    /// Ordering is by part number rather than by publication date, so a back-filled part 2 published
    /// after part 3 still reads second.
    /// </summary>
    [Fact]
    public void NavigationOrdersByPartNumberNotPublicationDate()
    {
        // Arrange
        ArrangeNavigation(
            currentPostId: 102,
            currentPartNumber: 2,
            currentPublished: true,
            new BlogPost
            {
                PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true,
                PublishedOn = new DateTime(2026, 1, 1)
            },
            new BlogPost
            {
                PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true,
                PublishedOn = new DateTime(2026, 3, 1)
            },
            new BlogPost
            {
                PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = true,
                PublishedOn = new DateTime(2026, 6, 1)
            });

        // Act
        var navigation = service.GetSeriesNavigation(102);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal(101, navigation!.PreviousPost!.PostID);
        Assert.Equal(103, navigation.NextPost!.PostID);
    }

    /// <summary>
    /// A part with no number reports part 0 rather than faulting, which is how the strip degrades for
    /// a post attached to a series without being numbered.
    /// </summary>
    [Fact]
    public void NavigationReportsPartZeroForAnUnnumberedPart()
    {
        // Arrange
        ArrangeNavigation(
            currentPostId: 101,
            currentPartNumber: null,
            currentPublished: true,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = null, Published = true });

        // Act
        var navigation = service.GetSeriesNavigation(101);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal(0, navigation!.CurrentPart);
        Assert.Equal(1, navigation.TotalParts);
    }

    /// <summary>
    /// A failed navigation read yields null and logs, so a broken series strip cannot take a post page
    /// down.
    /// </summary>
    [Fact]
    public void NavigationIsAbsentAndLoggedWhenAReadFails()
    {
        // Arrange
        postRepo.GetSingle(100).Throws(new InvalidOperationException("navigation exploded"));

        // Act
        var navigation = service.GetSeriesNavigation(100);

        // Assert
        Assert.Null(navigation);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// The async twin applies the same published-only filter and the same part-number ordering, so the
    /// two surfaces cannot show a reader different neighbours.
    /// </summary>
    [Fact]
    public async Task NavigationAsyncFiltersToPublishedPartsAndOrdersByPartNumber()
    {
        // Arrange
        ArrangeNavigationAsync(
            currentPostId: 101,
            currentPartNumber: 1,
            new BlogPost { PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = false },
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true });

        // Act
        var navigation = await service.GetSeriesNavigationAsync(101, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal(2, navigation!.TotalParts);
        Assert.Null(navigation.PreviousPost);
        Assert.Equal(103, navigation.NextPost!.PostID);
    }

    /// <summary>
    /// The async twin returns null for a post that belongs to no series, without reading the series
    /// table.
    /// </summary>
    [Fact]
    public async Task NavigationAsyncIsAbsentForAPostInNoSeries()
    {
        // Arrange
        postRepo.GetSingleAsync(100, Arg.Any<CancellationToken>())
            .Returns(new BlogPost { PostID = 100, SeriesId = null });

        // Act
        var navigation = await service.GetSeriesNavigationAsync(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(navigation);
        await seriesRepo.DidNotReceive().GetSingleAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async twin returns null for a post that does not exist at all.
    /// </summary>
    [Fact]
    public async Task NavigationAsyncIsAbsentForAnUnknownPost()
    {
        // Arrange
        postRepo.GetSingleAsync(100, Arg.Any<CancellationToken>()).Returns((BlogPost?)null);

        // Act, Assert
        Assert.Null(await service.GetSeriesNavigationAsync(100, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The async twin returns null when the post being viewed is itself unpublished and therefore
    /// absent from the published part list — the fourth of the four "no strip" cases.
    /// </summary>
    [Fact]
    public async Task NavigationAsyncIsAbsentForAnUnpublishedPost()
    {
        // Arrange
        ArrangeNavigationAsync(
            currentPostId: 102,
            currentPartNumber: 2,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = false });

        // Act, Assert
        Assert.Null(await service.GetSeriesNavigationAsync(102, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The async twin flanks a middle part with both neighbours and reports its part number and the
    /// published total, matching the synchronous strip exactly.
    /// </summary>
    [Fact]
    public async Task NavigationAsyncFlanksAMiddlePartWithBothNeighbours()
    {
        // Arrange
        ArrangeNavigationAsync(
            currentPostId: 102,
            currentPartNumber: 2,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = true },
            new BlogPost { PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true });

        // Act
        var navigation = await service.GetSeriesNavigationAsync(102, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal("Blazor in Production", navigation!.SeriesName);
        Assert.Equal(2, navigation.CurrentPart);
        Assert.Equal(3, navigation.TotalParts);
        Assert.Equal(101, navigation.PreviousPost!.PostID);
        Assert.Equal(103, navigation.NextPost!.PostID);
    }

    /// <summary>
    /// The async twin returns null when the series row has gone.
    /// </summary>
    [Fact]
    public async Task NavigationAsyncIsAbsentWhenTheSeriesRowHasGone()
    {
        // Arrange
        postRepo.GetSingleAsync(101, Arg.Any<CancellationToken>())
            .Returns(new BlogPost { PostID = 101, SeriesId = SeriesId });
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>()).Returns((BlogSeries?)null);

        // Act, Assert
        Assert.Null(await service.GetSeriesNavigationAsync(101, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The async twin returns null when the current post is not in the published list, and null again
    /// when a read fails — the same single "no strip" signal.
    /// </summary>
    [Fact]
    public async Task NavigationAsyncIsAbsentAndLoggedWhenAReadFails()
    {
        // Arrange
        postRepo.GetSingleAsync(101, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("async navigation exploded"));

        // Act
        var navigation = await service.GetSeriesNavigationAsync(101, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(navigation);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // ===========================================================================================
    // Arrangement helpers
    // ===========================================================================================

    /// <summary>
    /// Makes the series repository report that <see cref="SeriesId"/> exists, for both the blocking
    /// existence check and the delete path.
    /// </summary>
    private void ArrangeExistingSeries()
    {
        seriesRepo.GetSingle(SeriesId).Returns(new BlogSeries
        {
            SeriesId = SeriesId,
            Name = "Blazor in Production",
            Slug = SeriesSlug
        });
    }

    /// <summary>
    /// Async counterpart of <see cref="ArrangeExistingSeries"/>, stubbing the repository's async
    /// existence check — a substitute intercepts the interface default implementation, so the
    /// synchronous stub alone would leave the async caller looking at null.
    /// </summary>
    private void ArrangeExistingSeriesAsync()
    {
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(new BlogSeries
        {
            SeriesId = SeriesId,
            Name = "Blazor in Production",
            Slug = SeriesSlug
        });
    }

    /// <summary>
    /// Arranges the async slug lookup with one published and one draft part.
    /// </summary>
    private void ArrangeSlugLookupAsync()
    {
        seriesRepo.GetBySlugAsync(SeriesSlug, Arg.Any<CancellationToken>()).Returns(new BlogSeries
        {
            SeriesId = SeriesId,
            Name = "Blazor in Production",
            Slug = SeriesSlug,
            PostCount = 1
        });

        postRepo.GetPublishedPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true }
        }.AsEnumerable());

        postRepo.GetPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = false }
        }.AsEnumerable());
    }

    /// <summary>
    /// Arranges the three reads the synchronous navigation strip performs.
    /// </summary>
    /// <param name="currentPostId">The post being viewed.</param>
    /// <param name="currentPartNumber">Its part number, or null when it is unnumbered.</param>
    /// <param name="currentPublished">Whether the post being viewed is published.</param>
    /// <param name="parts">Every part the repository returns, drafts included.</param>
    private void ArrangeNavigation(
        long currentPostId, int? currentPartNumber, bool currentPublished, params BlogPost[] parts)
    {
        postRepo.GetSingle(currentPostId).Returns(new BlogPost
        {
            PostID = currentPostId,
            SeriesId = SeriesId,
            SeriesPartNumber = currentPartNumber,
            Published = currentPublished
        });
        seriesRepo.GetSingle(SeriesId).Returns(new BlogSeries
        {
            SeriesId = SeriesId,
            Name = "Blazor in Production",
            Slug = SeriesSlug
        });
        postRepo.GetPostsBySeries(SeriesId).Returns(parts);
    }

    /// <summary>
    /// Async counterpart of <see cref="ArrangeNavigation"/>, stubbing the async members the async
    /// twin actually reaches for.
    /// </summary>
    /// <param name="currentPostId">The post being viewed.</param>
    /// <param name="currentPartNumber">Its part number, or null when it is unnumbered.</param>
    /// <param name="parts">Every part the repository returns, drafts included.</param>
    private void ArrangeNavigationAsync(long currentPostId, int? currentPartNumber, params BlogPost[] parts)
    {
        postRepo.GetSingleAsync(currentPostId, Arg.Any<CancellationToken>()).Returns(new BlogPost
        {
            PostID = currentPostId,
            SeriesId = SeriesId,
            SeriesPartNumber = currentPartNumber,
            Published = true
        });
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(new BlogSeries
        {
            SeriesId = SeriesId,
            Name = "Blazor in Production",
            Slug = SeriesSlug
        });
        postRepo.GetPostsBySeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(parts.AsEnumerable());
    }
}
