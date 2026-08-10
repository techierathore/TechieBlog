using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TechieBlog.Tests.Dashboard;

namespace TechieBlog.Tests.Publishing;

/// <summary>
/// Unit tests for <see cref="AnalyticsSvc"/> — argument clamping, range ordering, quiet-day filling
/// and the degrade-on-failure contract.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-035. Analytics decorate a page rather than being the page, so every
/// member has to clamp what a caller asks for (no unbounded result sets, no negative windows, no
/// inverted date ranges) and degrade to an empty or zeroed value when the query fails instead of
/// throwing. These tests pin both halves: the arguments that actually reach the repository, and what
/// comes back when the repository refuses.</para>
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IAnalyticsRepo"/> and
/// <see cref="IPostViewRepo"/>; <see cref="RecordingLogger{T}"/> so a swallowed exception can be
/// proved to have been logged. No database.</para>
/// </remarks>
public class AnalyticsSvcTests
{
    private const long PostId = 42;

    private readonly IAnalyticsRepo analyticsRepo = Substitute.For<IAnalyticsRepo>();
    private readonly IPostViewRepo postViewRepo = Substitute.For<IPostViewRepo>();
    private readonly RecordingLogger<AnalyticsSvc> logger = new();
    private readonly AnalyticsSvc service;

    /// <summary>
    /// Wires the service under test to substituted repositories and a recording logger.
    /// </summary>
    public AnalyticsSvcTests()
    {
        service = new AnalyticsSvc(analyticsRepo, postViewRepo, logger);
    }

    // -------------------------------------------------------------------------------------------
    // GetPopularPostsAsync — rolling window
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A caller asking for a zero-day or negative window would otherwise produce a lower bound in the
    /// future and rank nothing; the window is floored at one day so the widget still shows today.
    /// </summary>
    /// <param name="requestedDays">The window the caller asked for.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-365)]
    public async Task GetPopularPostsFloorsWindowAtOneDay(int requestedDays)
    {
        // Arrange
        var captured = CaptureRollingWindowLowerBound();
        var before = DateTime.UtcNow;

        // Act
        await service.GetPopularPostsAsync(requestedDays, 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(captured.Value);
        Assert.InRange(
            captured.Value!.Value,
            before.AddDays(-1).AddSeconds(-30),
            DateTime.UtcNow.AddDays(-1).AddSeconds(30));
    }

    /// <summary>
    /// A sensible window is passed through untouched, so "popular over the last 30 days" really does
    /// look back thirty days rather than a clamped substitute.
    /// </summary>
    [Fact]
    public async Task GetPopularPostsUsesTheRequestedWindowWhenItIsValid()
    {
        // Arrange
        var captured = CaptureRollingWindowLowerBound();
        var before = DateTime.UtcNow;

        // Act
        await service.GetPopularPostsAsync(30, 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(captured.Value);
        Assert.InRange(
            captured.Value!.Value,
            before.AddDays(-30).AddSeconds(-30),
            DateTime.UtcNow.AddDays(-30).AddSeconds(30));
    }

    /// <summary>
    /// The result count is clamped into 1..100 so a caller cannot ask for an unbounded result set nor
    /// for nothing at all — the clamp lives here rather than in SQL by design.
    /// </summary>
    /// <param name="requested">The count the caller asked for.</param>
    /// <param name="expected">The count the repository is allowed to see.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-10, 1)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, 100)]
    public async Task GetPopularPostsClampsResultCount(int requested, int expected)
    {
        // Arrange, Act
        await service.GetPopularPostsAsync(7, requested, TestContext.Current.CancellationToken);

        // Assert
        await analyticsRepo.Received(1).GetPopularPostsAsync(
            Arg.Any<DateTime>(), expected, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ranking the repository produces is returned as-is: the service ranks nothing itself, so a
    /// re-sort here would silently contradict the SQL ORDER BY.
    /// </summary>
    [Fact]
    public async Task GetPopularPostsReturnsTheRepositoryRankingUnchanged()
    {
        // Arrange
        var ranked = new List<PopularPost>
        {
            new() { PostId = 1, Title = "Most read", TotalViews = 900 },
            new() { PostId = 2, Title = "Runner up", TotalViews = 400 }
        };
        analyticsRepo.GetPopularPostsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ranked);

        // Act
        var popular = await service.GetPopularPostsAsync(7, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new long[] { 1, 2 }, popular.Select(post => post.PostId));
    }

    /// <summary>
    /// A failed ranking query degrades to an empty list and is logged — the popular-posts widget
    /// disappearing is acceptable, the page it decorates failing is not.
    /// </summary>
    [Fact]
    public async Task GetPopularPostsDegradesToEmptyWhenTheQueryFails()
    {
        // Arrange
        analyticsRepo.GetPopularPostsAsync(Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ranking exploded"));

        // Act
        var popular = await service.GetPopularPostsAsync(7, 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(popular);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error != null);
    }

    // -------------------------------------------------------------------------------------------
    // GetPostEngagementAsync
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A non-positive post id cannot identify a row, so it is answered with a zeroed instance without
    /// a database round trip at all.
    /// </summary>
    /// <param name="invalidPostId">The identifier under test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetPostEngagementAnswersNonPositiveIdWithoutQuerying(long invalidPostId)
    {
        // Arrange, Act
        var engagement = await service.GetPostEngagementAsync(invalidPostId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(engagement);
        Assert.Equal(0, engagement!.PostId);
        Assert.Equal(0, engagement.TotalViews);
        await analyticsRepo.DidNotReceive().GetPostEngagementAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A post nobody has interacted with has no aggregate row, and the caller is handed a zeroed
    /// instance stamped with the post id rather than a null it would have to guard.
    /// </summary>
    [Fact]
    public async Task GetPostEngagementSubstitutesAZeroedRowForAnUntouchedPost()
    {
        // Arrange
        analyticsRepo.GetPostEngagementAsync(PostId, Arg.Any<CancellationToken>())
            .Returns((PostEngagement?)null);

        // Act
        var engagement = await service.GetPostEngagementAsync(PostId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(engagement);
        Assert.Equal(PostId, engagement!.PostId);
        Assert.Equal(0, engagement.TotalViews);
        Assert.Equal(0, engagement.CommentCount);
    }

    /// <summary>
    /// A real aggregate row is returned untouched, so the page shows the figures SQL computed.
    /// </summary>
    [Fact]
    public async Task GetPostEngagementReturnsTheRepositoryRow()
    {
        // Arrange
        var row = new PostEngagement { PostId = PostId, TotalViews = 12, CommentCount = 3, AverageRating = 4.5 };
        analyticsRepo.GetPostEngagementAsync(PostId, Arg.Any<CancellationToken>()).Returns(row);

        // Act
        var engagement = await service.GetPostEngagementAsync(PostId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(row, engagement);
    }

    /// <summary>
    /// A failed engagement query degrades to a zeroed row carrying the requested post id, and logs.
    /// </summary>
    [Fact]
    public async Task GetPostEngagementDegradesToAZeroedRowWhenTheQueryFails()
    {
        // Arrange
        analyticsRepo.GetPostEngagementAsync(PostId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("engagement exploded"));

        // Act
        var engagement = await service.GetPostEngagementAsync(PostId, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(engagement);
        Assert.Equal(PostId, engagement!.PostId);
        Assert.Equal(0, engagement.TotalViews);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // -------------------------------------------------------------------------------------------
    // GetPostViewCountsAsync
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A non-positive post id is answered with zeroed counts and never reaches the view repository.
    /// </summary>
    /// <param name="invalidPostId">The identifier under test.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-7)]
    public async Task GetPostViewCountsAnswersNonPositiveIdWithoutQuerying(long invalidPostId)
    {
        // Arrange, Act
        var counts = await service.GetPostViewCountsAsync(invalidPostId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, counts.PostId);
        Assert.Equal(0, counts.TotalViews);
        Assert.Equal(0, counts.UniqueViews);
        await postViewRepo.DidNotReceive().GetCountsAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Real counts are passed straight through, total and unique alike.
    /// </summary>
    [Fact]
    public async Task GetPostViewCountsReturnsTheRepositoryCounts()
    {
        // Arrange
        postViewRepo.GetCountsAsync(PostId, Arg.Any<CancellationToken>())
            .Returns(new PostViewCounts { PostId = PostId, TotalViews = 30, UniqueViews = 11 });

        // Act
        var counts = await service.GetPostViewCountsAsync(PostId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PostId, counts.PostId);
        Assert.Equal(30, counts.TotalViews);
        Assert.Equal(11, counts.UniqueViews);
    }

    /// <summary>
    /// A failed count query degrades to zeroed counts stamped with the post id, so the view badge
    /// renders "0" rather than taking the post page down.
    /// </summary>
    [Fact]
    public async Task GetPostViewCountsDegradesToZeroWhenTheQueryFails()
    {
        // Arrange
        postViewRepo.GetCountsAsync(PostId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("counts exploded"));

        // Act
        var counts = await service.GetPostViewCountsAsync(PostId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PostId, counts.PostId);
        Assert.Equal(0, counts.TotalViews);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // -------------------------------------------------------------------------------------------
    // GetTopEngagementAsync
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The admin overview table's row count is clamped into 1..100 exactly as the ranking window is.
    /// </summary>
    /// <param name="requested">The count the caller asked for.</param>
    /// <param name="expected">The count the repository is allowed to see.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(25, 25)]
    [InlineData(5000, 100)]
    public async Task GetTopEngagementClampsResultCount(int requested, int expected)
    {
        // Arrange, Act
        await service.GetTopEngagementAsync(requested, TestContext.Current.CancellationToken);

        // Assert
        await analyticsRepo.Received(1).GetTopEngagementAsync(expected, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The repository's rows are returned in the order SQL produced them.
    /// </summary>
    [Fact]
    public async Task GetTopEngagementReturnsTheRepositoryRows()
    {
        // Arrange
        analyticsRepo.GetTopEngagementAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(
            new List<PostEngagement>
            {
                new() { PostId = 9, TotalViews = 200 },
                new() { PostId = 8, TotalViews = 150 }
            });

        // Act
        var rows = await service.GetTopEngagementAsync(10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new long[] { 9, 8 }, rows.Select(row => row.PostId));
    }

    /// <summary>
    /// A failed overview query degrades to an empty list and logs.
    /// </summary>
    [Fact]
    public async Task GetTopEngagementDegradesToEmptyWhenTheQueryFails()
    {
        // Arrange
        analyticsRepo.GetTopEngagementAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("overview exploded"));

        // Act
        var rows = await service.GetTopEngagementAsync(10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(rows);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // -------------------------------------------------------------------------------------------
    // Range ordering — shared by four members through OrderRange
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// A date picker lets an admin choose an end date before a start date; swapping the pair is
    /// friendlier than returning nothing, so the repository always sees a range the right way round.
    /// </summary>
    [Fact]
    public async Task GetPopularPostsInRangeSwapsAnInvertedRange()
    {
        // Arrange
        var later = new DateTime(2026, 8, 7, 9, 0, 0, DateTimeKind.Utc);
        var earlier = new DateTime(2026, 8, 1, 18, 30, 0, DateTimeKind.Utc);

        // Act
        await service.GetPopularPostsInRangeAsync(later, earlier, 10, TestContext.Current.CancellationToken);

        // Assert
        await analyticsRepo.Received(1).GetPopularPostsInRangeAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 8), 10, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The range is snapped to whole days with an exclusive upper bound landing on the day after the
    /// chosen end date, so "1 Aug to 7 Aug" includes every view recorded on 7 August.
    /// </summary>
    [Fact]
    public async Task GetPopularPostsInRangeSnapsToWholeDaysWithAnExclusiveUpperBound()
    {
        // Arrange
        var from = new DateTime(2026, 8, 1, 23, 59, 59, DateTimeKind.Utc);
        var to = new DateTime(2026, 8, 7, 0, 0, 1, DateTimeKind.Utc);

        // Act
        await service.GetPopularPostsInRangeAsync(from, to, 10, TestContext.Current.CancellationToken);

        // Assert
        await analyticsRepo.Received(1).GetPopularPostsInRangeAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 8), 10, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The ranged ranking clamps its result count exactly as the rolling-window one does.
    /// </summary>
    /// <param name="requested">The count the caller asked for.</param>
    /// <param name="expected">The count the repository is allowed to see.</param>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(400, 100)]
    public async Task GetPopularPostsInRangeClampsResultCount(int requested, int expected)
    {
        // Arrange, Act
        await service.GetPopularPostsInRangeAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), requested, TestContext.Current.CancellationToken);

        // Assert
        await analyticsRepo.Received(1).GetPopularPostsInRangeAsync(
            Arg.Any<DateTime>(), Arg.Any<DateTime>(), expected, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed ranged ranking degrades to an empty list and logs.
    /// </summary>
    [Fact]
    public async Task GetPopularPostsInRangeDegradesToEmptyWhenTheQueryFails()
    {
        // Arrange
        analyticsRepo.GetPopularPostsInRangeAsync(
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("ranged ranking exploded"));

        // Act
        var popular = await service.GetPopularPostsInRangeAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(popular);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // -------------------------------------------------------------------------------------------
    // GetViewTrendAsync — quiet-day filling
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// SQL only returns days that have rows, so a chart plotted from the sparse set would drop quiet
    /// days and overstate the trend; every day in the range gets a point, zeroed where there was no
    /// traffic.
    /// </summary>
    [Fact]
    public async Task GetViewTrendFillsQuietDaysWithZeroedPoints()
    {
        // Arrange
        var from = new DateTime(2026, 8, 1);
        var to = new DateTime(2026, 8, 3);
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<ViewTrendPoint>
            {
                new() { Day = new DateTime(2026, 8, 2), TotalViews = 17, UniqueViews = 9 }
            });

        // Act
        var trend = await service.GetViewTrendAsync(from, to, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, trend.Count);
        Assert.Equal(new DateTime(2026, 8, 1), trend[0].Day);
        Assert.Equal(0, trend[0].TotalViews);
        Assert.Equal(17, trend[1].TotalViews);
        Assert.Equal(9, trend[1].UniqueViews);
        Assert.Equal(new DateTime(2026, 8, 3), trend[2].Day);
        Assert.Equal(0, trend[2].TotalViews);
    }

    /// <summary>
    /// Points come back oldest first regardless of the order the repository produced them, because
    /// the range is walked forwards rather than the recorded set being echoed.
    /// </summary>
    [Fact]
    public async Task GetViewTrendReturnsPointsOldestFirst()
    {
        // Arrange
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<ViewTrendPoint>
            {
                new() { Day = new DateTime(2026, 8, 3), TotalViews = 3 },
                new() { Day = new DateTime(2026, 8, 1), TotalViews = 1 }
            });

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new[] { new DateTime(2026, 8, 1), new DateTime(2026, 8, 2), new DateTime(2026, 8, 3) },
            trend.Select(point => point.Day));
    }

    /// <summary>
    /// A single-day range still produces one point, because the exclusive upper bound is the day
    /// after the chosen end date rather than the end date itself.
    /// </summary>
    [Fact]
    public async Task GetViewTrendCoversASingleDayRange()
    {
        // Arrange
        var day = new DateTime(2026, 8, 9, 14, 22, 0, DateTimeKind.Utc);
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<ViewTrendPoint>());

        // Act
        var trend = await service.GetViewTrendAsync(day, day, TestContext.Current.CancellationToken);

        // Assert
        var point = Assert.Single(trend);
        Assert.Equal(new DateTime(2026, 8, 9), point.Day);
    }

    /// <summary>
    /// An inverted range is swapped before the days are walked, so an admin who picks the end date
    /// first still gets a chart instead of an empty one.
    /// </summary>
    [Fact]
    public async Task GetViewTrendSwapsAnInvertedRange()
    {
        // Arrange
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<ViewTrendPoint>());

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 5), new DateTime(2026, 8, 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, trend.Count);
        Assert.Equal(new DateTime(2026, 8, 3), trend[0].Day);
        Assert.Equal(new DateTime(2026, 8, 5), trend[2].Day);
        await analyticsRepo.Received(1).GetViewTrendAsync(
            new DateTime(2026, 8, 3), new DateTime(2026, 8, 6), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A recorded point is matched on its calendar day, so a timestamp carrying a time-of-day still
    /// lands on the right slot and is emitted rather than replaced by a zeroed placeholder.
    /// </summary>
    [Fact]
    public async Task GetViewTrendMatchesRecordedPointsOnTheirCalendarDay()
    {
        // Arrange
        var recorded = new ViewTrendPoint { Day = new DateTime(2026, 8, 2, 13, 45, 0), TotalViews = 5 };
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<ViewTrendPoint> { recorded });

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 2), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, trend.Count);
        Assert.Same(recorded, trend[1]);
        Assert.Equal(5, trend[1].TotalViews);
    }

    /// <summary>
    /// A failed trend query degrades to an empty list — not to a range of zeroed points, which would
    /// be indistinguishable from a genuinely quiet period — and logs.
    /// </summary>
    [Fact]
    public async Task GetViewTrendDegradesToEmptyWhenTheQueryFails()
    {
        // Arrange
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("trend exploded"));

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(trend);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // -------------------------------------------------------------------------------------------
    // GetCategoryEngagementAsync
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Category engagement orders and day-aligns its range and clamps its result count, sharing the
    /// same two rules as every other ranged member.
    /// </summary>
    [Fact]
    public async Task GetCategoryEngagementOrdersTheRangeAndClampsTheCount()
    {
        // Arrange, Act
        await service.GetCategoryEngagementAsync(
            new DateTime(2026, 8, 7, 6, 0, 0),
            new DateTime(2026, 8, 1, 6, 0, 0),
            999,
            TestContext.Current.CancellationToken);

        // Assert
        await analyticsRepo.Received(1).GetCategoryEngagementAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 8), 100, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The repository's ranked categories are returned unchanged.
    /// </summary>
    [Fact]
    public async Task GetCategoryEngagementReturnsTheRepositoryRows()
    {
        // Arrange
        analyticsRepo.GetCategoryEngagementAsync(
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<CategoryEngagement>
            {
                new() { CategoryId = 3, CategoryName = "Blazor", TotalViews = 80 }
            });

        // Act
        var rows = await service.GetCategoryEngagementAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), 10, TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single(rows);
        Assert.Equal("Blazor", row.CategoryName);
    }

    /// <summary>
    /// A failed category query degrades to an empty list and logs.
    /// </summary>
    [Fact]
    public async Task GetCategoryEngagementDegradesToEmptyWhenTheQueryFails()
    {
        // Arrange
        analyticsRepo.GetCategoryEngagementAsync(
                Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("category engagement exploded"));

        // Act
        var rows = await service.GetCategoryEngagementAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), 10, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(rows);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // -------------------------------------------------------------------------------------------
    // GetSummaryAsync
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The headline summary sees the same ordered, day-aligned range every other ranged member does,
    /// so the tiles above a chart cannot describe a different period from the chart.
    /// </summary>
    [Fact]
    public async Task GetSummaryOrdersAndDayAlignsTheRange()
    {
        // Arrange, Act
        await service.GetSummaryAsync(
            new DateTime(2026, 8, 7, 11, 0, 0),
            new DateTime(2026, 8, 1, 11, 0, 0),
            TestContext.Current.CancellationToken);

        // Assert
        await analyticsRepo.Received(1).GetSummaryAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 8), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The repository's summary is returned unchanged.
    /// </summary>
    [Fact]
    public async Task GetSummaryReturnsTheRepositorySummary()
    {
        // Arrange
        var summary = new AnalyticsSummary { TotalViews = 500, UniqueViews = 320, PostsWithTraffic = 12 };
        analyticsRepo.GetSummaryAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(summary);

        // Act
        var result = await service.GetSummaryAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(summary, result);
    }

    /// <summary>
    /// A failed summary query degrades to a zeroed summary rather than null, so the dashboard tiles
    /// render zeroes instead of the caller having to guard every read.
    /// </summary>
    [Fact]
    public async Task GetSummaryDegradesToAZeroedSummaryWhenTheQueryFails()
    {
        // Arrange
        analyticsRepo.GetSummaryAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("summary exploded"));

        // Act
        var summary = await service.GetSummaryAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(summary);
        Assert.Equal(0, summary.TotalViews);
        Assert.Equal(0, summary.PostsWithTraffic);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    // -------------------------------------------------------------------------------------------
    // Cancellation
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The caller's token reaches the repository, so a query the reader has navigated away from can
    /// actually be abandoned rather than running to completion on a parked thread.
    /// </summary>
    [Fact]
    public async Task AnalyticsFlowsTheCallersTokenToTheRepository()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();

        // Act
        await service.GetSummaryAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), cancellation.Token);
        await service.GetPostViewCountsAsync(PostId, cancellation.Token);

        // Assert
        await analyticsRepo.Received(1).GetSummaryAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), cancellation.Token);
        await postViewRepo.Received(1).GetCountsAsync(PostId, cancellation.Token);
    }

    /// <summary>
    /// Arranges the ranking substitute so the <c>sinceUtc</c> lower bound it is handed is recorded.
    /// </summary>
    /// <returns>A holder whose value is written when the ranking query runs.</returns>
    private CapturedValue<DateTime> CaptureRollingWindowLowerBound()
    {
        var captured = new CapturedValue<DateTime>();
        analyticsRepo
            .GetPopularPostsAsync(
                Arg.Do<DateTime>(sinceUtc => captured.Value = sinceUtc),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(new List<PopularPost>());
        return captured;
    }

    /// <summary>
    /// Mutable holder for an argument captured from a substituted call.
    /// </summary>
    /// <typeparam name="T">The captured argument's type.</typeparam>
    private sealed class CapturedValue<T>
        where T : struct
    {
        /// <summary>
        /// Gets or sets the captured argument, or null when the call never ran.
        /// </summary>
        public T? Value { get; set; }
    }
}
