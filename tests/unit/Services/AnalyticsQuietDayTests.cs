using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TechieBlog.Tests.Dashboard;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Unit tests for the quiet-day filling behind <see cref="AnalyticsSvc.GetViewTrendAsync"/> when the
/// repository hands back more than one aggregate row for the same calendar day.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-FN-056. <c>FillQuietDays</c> used to index the recorded points with
/// <c>ToDictionary(point =&gt; point.Day.Date)</c>, which throws <see cref="ArgumentException"/> the
/// moment two rows truncate to the same day. That throw happened inside the read try/catch, so the
/// caller swallowed it and degraded the ENTIRE traffic trend to an empty list — a chart that silently
/// blanks. These tests pin both halves of the fix: a repeated day can no longer empty the trend, and
/// the degraded path is observable in the log.</para>
///
/// <para><b>Dependencies:</b> NSubstitute for <see cref="IAnalyticsRepo"/> and
/// <see cref="IPostViewRepo"/>; <see cref="RecordingLogger{T}"/> — the spy logger the suite already
/// uses — so an emitted diagnostic can be asserted. No database.</para>
///
/// <para><b>Usage:</b> Pure unit tests; the whole class runs in-memory.</para>
/// </remarks>
public class AnalyticsQuietDayTests
{
    private readonly IAnalyticsRepo analyticsRepo = Substitute.For<IAnalyticsRepo>();
    private readonly IPostViewRepo postViewRepo = Substitute.For<IPostViewRepo>();
    private readonly RecordingLogger<AnalyticsSvc> logger = new();
    private readonly AnalyticsSvc service;

    /// <summary>
    /// Wires the service under test to substituted repositories and a recording logger.
    /// </summary>
    public AnalyticsQuietDayTests()
    {
        service = new AnalyticsSvc(analyticsRepo, postViewRepo, logger);
    }

    /// <summary>
    /// Two aggregate rows that truncate to the same calendar day used to make the day index throw,
    /// which the caller's catch swallowed into an empty trend; the trend must instead still carry one
    /// point for every day in the requested range, oldest first, with nothing dropped.
    /// </summary>
    [Fact]
    public async Task GetViewTrendSurvivesADuplicateDayFromTheRepository()
    {
        // Arrange
        ArrangeTrend(
            new ViewTrendPoint { Day = new DateTime(2026, 8, 2, 0, 0, 0), TotalViews = 17, UniqueViews = 9 },
            new ViewTrendPoint { Day = new DateTime(2026, 8, 2, 13, 45, 0), TotalViews = 4, UniqueViews = 2 });

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            new[] { new DateTime(2026, 8, 1), new DateTime(2026, 8, 2), new DateTime(2026, 8, 3) },
            trend.Select(point => point.Day));
    }

    /// <summary>
    /// A duplicate day anywhere in the range must not blank the days around it either — the quiet
    /// bookends still plot as explicit zeroes rather than the whole series collapsing to nothing.
    /// </summary>
    [Fact]
    public async Task GetViewTrendKeepsQuietDaysAroundADuplicateDay()
    {
        // Arrange
        ArrangeTrend(
            new ViewTrendPoint { Day = new DateTime(2026, 8, 2), TotalViews = 17, UniqueViews = 9 },
            new ViewTrendPoint { Day = new DateTime(2026, 8, 2), TotalViews = 4, UniqueViews = 2 });

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, trend[0].TotalViews);
        Assert.Equal(0, trend[2].TotalViews);
    }

    /// <summary>
    /// The projection the repository produced is handed on column-for-column for the repeated day —
    /// the first row for that day wins and keeps its own <c>TotalViews</c> and <c>UniqueViews</c>
    /// rather than being replaced by a zeroed placeholder or an invented aggregate.
    /// </summary>
    [Fact]
    public async Task GetViewTrendPlotsTheFirstRowOfADuplicateDayUnchanged()
    {
        // Arrange
        var first = new ViewTrendPoint { Day = new DateTime(2026, 8, 2, 1, 0, 0), TotalViews = 17, UniqueViews = 9 };
        ArrangeTrend(
            first,
            new ViewTrendPoint { Day = new DateTime(2026, 8, 2, 22, 0, 0), TotalViews = 4, UniqueViews = 2 });

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 2), new DateTime(2026, 8, 2), TestContext.Current.CancellationToken);

        // Assert
        var point = Assert.Single(trend);
        Assert.Same(first, point);
        Assert.Equal(17, point.TotalViews);
        Assert.Equal(9, point.UniqueViews);
    }

    /// <summary>
    /// Collapsing two rows into one loses readership, so the degradation must not be silent: the
    /// service emits a warning naming the range and how many days repeated, which is the diagnostic
    /// that was missing while the duplicate simply blanked the chart.
    /// </summary>
    [Fact]
    public async Task GetViewTrendLogsWhenTheRepositoryRepeatsADay()
    {
        // Arrange
        ArrangeTrend(
            new ViewTrendPoint { Day = new DateTime(2026, 8, 2), TotalViews = 17 },
            new ViewTrendPoint { Day = new DateTime(2026, 8, 2), TotalViews = 4 });

        // Act
        await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("more than one"));
    }

    /// <summary>
    /// A healthy trend must stay quiet in the log, otherwise the duplicate-day warning is noise an
    /// operator learns to ignore rather than a signal that the aggregate query is wrong.
    /// </summary>
    [Fact]
    public async Task GetViewTrendDoesNotLogWhenEveryDayIsDistinct()
    {
        // Arrange
        ArrangeTrend(
            new ViewTrendPoint { Day = new DateTime(2026, 8, 1), TotalViews = 3 },
            new ViewTrendPoint { Day = new DateTime(2026, 8, 3), TotalViews = 8 });

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 3), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, trend.Count);
        Assert.Empty(logger.Entries);
    }

    /// <summary>
    /// The swallow-and-log read contract is unchanged by the duplicate-day fix: a genuinely failed
    /// trend query still degrades to an empty list rather than throwing at the UI, and still writes an
    /// error carrying the exception.
    /// </summary>
    [Fact]
    public async Task GetViewTrendStillDegradesAndLogsWhenTheReadFails()
    {
        // Arrange
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("trend exploded"));

        // Act
        var trend = await service.GetViewTrendAsync(
            new DateTime(2026, 8, 1), new DateTime(2026, 8, 7), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(trend);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error != null);
    }

    /// <summary>
    /// Arranges the substituted repository to return the supplied aggregate rows for any range.
    /// </summary>
    /// <param name="recorded">The rows the repository should hand back.</param>
    private void ArrangeTrend(params ViewTrendPoint[] recorded)
    {
        analyticsRepo.GetViewTrendAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(recorded.ToList());
    }
}
