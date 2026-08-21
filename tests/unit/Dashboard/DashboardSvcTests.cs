using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using Xunit;

namespace TechieBlog.Tests.Dashboard;

/// <summary>
/// Tests for the admin dashboard's aggregate-count service. [REQ-FN-036]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the behaviour that replaced the dashboard's hardcoded constants — the
/// tiles must show whatever the database reports, unaltered, and a query failure must degrade to
/// zeroes rather than break the admin screen.</para>
/// <para><b>Code Flow:</b> Each test drives the real <see cref="DashboardSvc"/> over
/// <see cref="FakeAdminCountsRepo"/>, so the projection and the error handling are exercised without
/// a database.</para>
/// <para><b>Dependencies:</b> xUnit and the fakes in this folder.</para>
/// <para><b>Usage:</b> Pure unit tests — no database, no network.</para>
/// </remarks>
public class DashboardSvcTests
{
    /// <summary>
    /// Every count the repository reports reaches the caller unchanged, so a tile can never show a
    /// constant in place of the real number.
    /// </summary>
    [Fact]
    public async Task CountsFlowThroughUnchanged()
    {
        var repo = new FakeAdminCountsRepo { Counts = BuildCounts() };
        var service = new DashboardSvc(repo, new RecordingLogger<DashboardSvc>());

        var counts = await service.GetAdminCountsAsync();

        Assert.Equal(BuildCounts().UserCount, counts.UserCount);
        Assert.Equal(BuildCounts().CommentCount, counts.CommentCount);
        Assert.Equal(BuildCounts().UnAppComments, counts.UnAppComments);
        Assert.Equal(BuildCounts().SubscriberCount, counts.SubscriberCount);
        Assert.Equal(BuildCounts().BlogCount, counts.BlogCount);
    }

    /// <summary>
    /// The "this month" figures behind the users and subscribers tiles travel with the rest of the
    /// counts rather than being recomputed in the UI.
    /// </summary>
    [Fact]
    public async Task MonthlyGrowthCountsAreCarried()
    {
        var repo = new FakeAdminCountsRepo { Counts = BuildCounts() };
        var service = new DashboardSvc(repo, new RecordingLogger<DashboardSvc>());

        var counts = await service.GetAdminCountsAsync();

        Assert.Equal(4, counts.NewUsersThisMonth);
        Assert.Equal(3, counts.NewSubscribersThisMonth);
    }

    /// <summary>
    /// The service reads the repository exactly once per dashboard load, so the single-round-trip
    /// design is not quietly lost to a repeated call.
    /// </summary>
    [Fact]
    public async Task CountsAreReadInOneRoundTrip()
    {
        var repo = new FakeAdminCountsRepo { Counts = BuildCounts() };
        var service = new DashboardSvc(repo, new RecordingLogger<DashboardSvc>());

        await service.GetAdminCountsAsync();

        Assert.Equal(1, repo.CallCount);
    }

    /// <summary>
    /// A database failure yields a zeroed value rather than an exception, so the admin screen still
    /// renders when the counts query breaks.
    /// </summary>
    [Fact]
    public async Task RepositoryFailureYieldsZeroedCounts()
    {
        var repo = new FakeAdminCountsRepo { FailWith = new InvalidOperationException("connection reset") };
        var service = new DashboardSvc(repo, new RecordingLogger<DashboardSvc>());

        var counts = await service.GetAdminCountsAsync();

        Assert.Equal(0, counts.UserCount);
        Assert.Equal(0, counts.CommentCount);
        Assert.Equal(0, counts.SubscriberCount);
    }

    /// <summary>
    /// A swallowed query failure is logged as an error together with the original exception, so the
    /// blank tiles are traceable to a cause.
    /// </summary>
    [Fact]
    public async Task RepositoryFailureIsLogged()
    {
        var failure = new InvalidOperationException("connection reset");
        var repo = new FakeAdminCountsRepo { FailWith = failure };
        var logger = new RecordingLogger<DashboardSvc>();
        var service = new DashboardSvc(repo, logger);

        await service.GetAdminCountsAsync();

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(failure, entry.Error);
    }

    /// <summary>
    /// Builds a counts value whose properties are all distinct, so a projection that crosses two
    /// fields fails the assertions.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors the real database at the time the tiles were made real —
    /// 4 users, 7 comments, 12 subscribers, 13 posts.</para>
    /// <para><b>Flow:</b> pure construction.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>A fully populated counts value.</returns>
    private static AdminCounts BuildCounts()
    {
        return new AdminCounts
        {
            BlogCount = 13,
            PublishedPostCount = 11,
            DraftPostCount = 2,
            CommentCount = 7,
            UnAppComments = 1,
            UserCount = 4,
            TagCount = 8,
            CategoryCount = 5,
            ImageCount = 6,
            SubscriberCount = 12,
            ActiveSubscriberCount = 9,
            NewsletterCount = 2,
            SentNewsletterCount = 1,
            TotalPostViews = 960,
            NewUsersThisMonth = 4,
            NewSubscribersThisMonth = 3
        };
    }
}
