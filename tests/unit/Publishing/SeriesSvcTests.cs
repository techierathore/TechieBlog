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
public async Task GetSeriesReturnsTheHeaderWithoutLoadingItsParts()
    {
        // Arrange
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>())
            .Returns(new BlogSeries { SeriesId = SeriesId, Name = "Alpha" });

        // Act
        var series = await service.GetSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(series);
        Assert.Equal("Alpha", series!.Name);
        Assert.Empty(series.Posts);
        await postRepo.DidNotReceive().GetPostsBySeriesAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unknown identifier is a normal answer and yields null rather than an exception.
    /// </summary>
    [Fact]
public async Task GetSeriesReturnsNullForAnUnknownIdentifier()
    {
        // Arrange
        seriesRepo.GetSingleAsync(SeriesId, Arg.Any<CancellationToken>()).Returns((BlogSeries?)null);

        // Act, Assert
        Assert.Null(await service.GetSeriesAsync(SeriesId, TestContext.Current.CancellationToken));
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
public async Task GetSeriesBySlugReturnsNullAndLogsWhenTheReadFails()
    {
        // Arrange
        seriesRepo.GetBySlugAsync(SeriesSlug, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("slug read exploded"));

        // Act
        var series = await service.GetSeriesBySlugAsync(SeriesSlug, TestContext.Current.CancellationToken);

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
public async Task GetNextPartNumberIsTheHighestExistingPlusOne(int highestExisting, int expected)
    {
        // Arrange
        postRepo.GetMaxPartNumberInSeriesAsync(SeriesId, Arg.Any<CancellationToken>()).Returns(highestExisting);

        // Act, Assert
        Assert.Equal(expected, await service.GetNextPartNumberAsync(SeriesId, TestContext.Current.CancellationToken));
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
    /// A name is mandatory — it is what the slug is derived from and what the public header shows —
    /// so a blank one is rejected before any repository is touched.
    /// </summary>
    /// <param name="name">The blank name under test.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
public async Task CreateSeriesRejectsABlankName(string name)
    {
        // Arrange, Act
        var result = await service.CreateSeriesAsync(new BlogSeries { Name = name }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Series name is required", result.ErrorMessage);
        await seriesRepo.DidNotReceive().InsertToGetIdAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A slug the administrator left blank is derived from the name, so a series is always
    /// addressable without the admin having to invent a URL.
    /// </summary>
    [Fact]
public async Task CreateSeriesDerivesTheSlugFromTheNameWhenItIsBlank()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production!" };

        // Act
        var result = await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-in-production", series.Slug);
    }

    /// <summary>
    /// A slug the administrator supplied is kept verbatim — it is a published address and must not be
    /// silently rewritten from the name.
    /// </summary>
    [Fact]
public async Task CreateSeriesKeepsAnExplicitlySuppliedSlug()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production", Slug = "bzp" };

        // Act
        await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("bzp", series.Slug);
    }

    /// <summary>
    /// A contested slug that the administrator supplied is suffixed from <i>that</i> slug rather than
    /// from the name, so a deliberately short address stays recognisable on its first collision.
    /// </summary>
    [Fact]
public async Task CreateSeriesSuffixesASuppliedSlugFromTheSuppliedBase()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production", Slug = "bzp" };
        seriesRepo.SlugExistsAsync("bzp", 0, Arg.Any<CancellationToken>()).Returns(true);
        seriesRepo.SlugExistsAsync("bzp-2", 0, Arg.Any<CancellationToken>()).Returns(false);

        // Act
        var result = await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("bzp-2", series.Slug);
    }

    /// <summary>
    /// The retry gives up after 99 attempts rather than looping forever against a pathologically busy
    /// name; the candidate is inserted anyway and the database's unique index is the real guard.
    /// </summary>
    [Fact]
public async Task CreateSeriesStopsRetryingTheSlugAfterNinetyNineAttempts()
    {
        // Arrange
        var series = new BlogSeries { Name = "Blazor in Production" };
        seriesRepo.SlugExistsAsync(Arg.Any<string>(), 0, Arg.Any<CancellationToken>()).Returns(true);

        // Act
        var result = await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-in-production-100", series.Slug);
    }

    /// <summary>
    /// An explicitly chosen status survives — a series imported as already complete must not be
    /// relabelled "In Progress".
    /// </summary>
    [Fact]
public async Task CreateSeriesKeepsAnExplicitStatus()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha", Status = "Completed" };

        // Act
        await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Completed", series.Status);
    }

    /// <summary>
    /// Both timestamps are stamped in UTC at creation, so a series created in one time zone sorts
    /// correctly against one created in another.
    /// </summary>
    [Fact]
public async Task CreateSeriesStampsBothTimestampsInUtc()
    {
        // Arrange
        var series = new BlogSeries { Name = "Alpha" };
        var before = DateTime.UtcNow;

        // Act
        await service.CreateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.InRange(series.CreatedOn, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
        Assert.InRange(series.UpdatedOn, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
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
    /// An update needs an identifier; a non-positive one means the caller meant to create, and is
    /// rejected rather than silently updating row zero.
    /// </summary>
    /// <param name="seriesId">The invalid identifier under test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
public async Task UpdateSeriesRejectsANonPositiveIdentifier(long seriesId)
    {
        // Arrange, Act
        var result = await service.UpdateSeriesAsync(new BlogSeries { SeriesId = seriesId, Name = "Alpha" }, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid series ID", result.ErrorMessage);
        await seriesRepo.DidNotReceive().UpdateAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The uniqueness check excludes the series being edited, so re-saving a series unchanged does not
    /// renumber its own slug into <c>-2</c>.
    /// </summary>
    [Fact]
public async Task UpdateSeriesExcludesItselfFromTheUniquenessCheck()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", Slug = "alpha" };

        // Act
        var result = await service.UpdateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("alpha", series.Slug);
        await seriesRepo.Received().SlugExistsAsync("alpha", SeriesId, Arg.Any<CancellationToken>());
        await seriesRepo.DidNotReceive().SlugExistsAsync(Arg.Any<string>(), 0, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A slug cleared on the edit form is re-derived from the name rather than saved empty, which
    /// would make the series unaddressable.
    /// </summary>
    [Fact]
public async Task UpdateSeriesDerivesTheSlugWhenItWasCleared()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Blazor in Production", Slug = string.Empty };

        // Act
        await service.UpdateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("blazor-in-production", series.Slug);
    }

    /// <summary>
    /// Only <c>UpdatedOn</c> is restamped; <c>CreatedOn</c> is left exactly as the caller supplied it,
    /// so an edit cannot rewrite when the series was started.
    /// </summary>
    [Fact]
public async Task UpdateSeriesRestampsUpdatedOnAndLeavesCreatedOnAlone()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        var created = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha", CreatedOn = created };
        var before = DateTime.UtcNow;

        // Act
        await service.UpdateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(created, series.CreatedOn);
        Assert.InRange(series.UpdatedOn, before.AddSeconds(-5), DateTime.UtcNow.AddSeconds(5));
    }

    /// <summary>
    /// A successful update persists through the repository once and returns the saved series.
    /// </summary>
    [Fact]
public async Task UpdateSeriesPersistsThroughTheRepositoryOnce()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        var series = new BlogSeries { SeriesId = SeriesId, Name = "Alpha" };

        // Act
        var result = await service.UpdateSeriesAsync(series, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Same(series, result.Data);
        await seriesRepo.Received(1).UpdateAsync(series, Arg.Any<CancellationToken>());
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
public async Task DeleteSeriesRejectsANonPositiveIdentifier(long seriesId)
    {
        // Arrange, Act
        var result = await service.DeleteSeriesAsync(seriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid series ID", result.ErrorMessage);
        await seriesRepo.DidNotReceive().GetSingleAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Nothing here deletes a post — losing the grouping must never lose the writing.
    /// </summary>
    [Fact]
public async Task DeleteSeriesNeverDeletesItsPosts()
    {
        // Arrange
        ArrangeExistingSeriesAsync();

        // Act
        await service.DeleteSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        await postRepo.DidNotReceive().SoftDeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await postRepo.Received(1).ClearSeriesFromPostsAsync(SeriesId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed detach abandons the delete, so the series survives with its parts still attached
    /// rather than leaving orphans behind.
    /// </summary>
    [Fact]
public async Task DeleteSeriesAbandonsTheDeleteWhenTheDetachFails()
    {
        // Arrange
        ArrangeExistingSeriesAsync();
        postRepo.ClearSeriesFromPostsAsync(SeriesId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("detach exploded"));

        // Act
        var result = await service.DeleteSeriesAsync(SeriesId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete series. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("detach exploded", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "detach exploded");
        await seriesRepo.DidNotReceive().DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
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
    /// The first part has no previous link and the last has no next, so the strip does not offer a
    /// dead end at either end of the series.
    /// </summary>
    [Fact]
public async Task NavigationOmitsPreviousOnTheFirstPartAndNextOnTheLast()
    {
        // Arrange
        var parts = new[]
        {
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = 1, Published = true },
            new BlogPost { PostID = 102, SeriesId = SeriesId, SeriesPartNumber = 2, Published = true },
            new BlogPost { PostID = 103, SeriesId = SeriesId, SeriesPartNumber = 3, Published = true }
        };
        ArrangeNavigationAsync(101, 1, parts);

        // Act
        var first = await service.GetSeriesNavigationAsync(101, TestContext.Current.CancellationToken);
        ArrangeNavigationAsync(103, 3, parts);
        var last = await service.GetSeriesNavigationAsync(103, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(first!.PreviousPost);
        Assert.Equal(102, first.NextPost!.PostID);
        Assert.Equal(102, last!.PreviousPost!.PostID);
        Assert.Null(last.NextPost);
    }

    /// <summary>
    /// A part with no number reports part 0 rather than faulting, which is how the strip degrades for
    /// a post attached to a series without being numbered.
    /// </summary>
    [Fact]
public async Task NavigationReportsPartZeroForAnUnnumberedPart()
    {
        // Arrange
        ArrangeNavigationAsync(
            currentPostId: 101,
            currentPartNumber: null,
            new BlogPost { PostID = 101, SeriesId = SeriesId, SeriesPartNumber = null, Published = true });

        // Act
        var navigation = await service.GetSeriesNavigationAsync(101, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(navigation);
        Assert.Equal(0, navigation!.CurrentPart);
        Assert.Equal(1, navigation.TotalParts);
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
