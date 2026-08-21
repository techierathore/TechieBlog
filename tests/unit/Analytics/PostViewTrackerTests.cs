using BlogEngine.Services;
using Microsoft.AspNetCore.Http;
using TechieBlog.Tests.Dashboard;
using TechieBlog.Tests.TestDoubles;
using Xunit;

namespace TechieBlog.Tests.Analytics;

/// <summary>
/// Tests for the post-view tracker's request-driven entry point. [REQ-FN-034]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The defect these tests exist to stop coming back is not a wrong number — it
/// is a tracker with no caller. The page now calls <c>TrackCurrentVisitAsync</c> on every render
/// pass, so the two properties that make that safe are pinned here: a render with no ambient request
/// writes nothing (which is what stops a prerendered Blazor page counting every view twice), and a
/// repeat visit inside the de-duplication window writes nothing either.</para>
///
/// <para><b>Code Flow:</b> each test builds a request with a chosen address and user-agent, drives
/// the real <see cref="PostViewTracker"/> over <see cref="FakePostViewRepo"/>, and asserts on the
/// rows the fake accepted.</para>
///
/// <para><b>Dependencies:</b> xUnit, <see cref="FakePostViewRepo"/>, <c>StubConfiguration</c> and
/// <c>RecordingLogger</c>.</para>
///
/// <para><b>Usage:</b> Pure unit tests — no database, no network, no browser.</para>
/// </remarks>
public class PostViewTrackerTests
{
    private const long SamplePostId = 42;
    private const string SampleSalt = "unit-test-salt";
    private const string SampleAddress = "203.0.113.7";
    private const string SampleUserAgent = "Mozilla/5.0 (TechieBlog unit test)";

    /// <summary>
    /// A request-backed render records one view and stores only the salted digest, never the address
    /// that produced it.
    /// </summary>
    [Fact]
    public async Task RequestBackedRenderRecordsOneView()
    {
        var repo = new FakePostViewRepo();
        var tracker = BuildTracker(repo, BuildAccessor(SampleAddress, SampleUserAgent));

        var result = await tracker.TrackCurrentVisitAsync(SamplePostId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        Assert.Single(repo.Rows);
        Assert.Equal(SamplePostId, repo.Rows[0].PostId);
        Assert.Equal(64, repo.Rows[0].VisitorHash.Length);
        Assert.DoesNotContain(SampleAddress, repo.Rows[0].VisitorHash);
    }

    /// <summary>
    /// A render with no ambient HTTP request writes nothing, which is what keeps the interactive
    /// pass of a prerendered Blazor page from counting a second view for the same page load.
    /// </summary>
    [Fact]
    public async Task RenderWithoutRequestRecordsNothing()
    {
        var repo = new FakePostViewRepo();
        var tracker = BuildTracker(repo, new StubHttpContextAccessor());

        var result = await tracker.TrackCurrentVisitAsync(SamplePostId);

        Assert.True(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Empty(repo.Rows);
    }

    /// <summary>
    /// The same visitor reading the same post twice inside the window is counted once, so a refresh
    /// loop cannot inflate the total.
    /// </summary>
    [Fact]
    public async Task RepeatVisitToSamePostIsDeduplicated()
    {
        var repo = new FakePostViewRepo();
        var tracker = BuildTracker(repo, BuildAccessor(SampleAddress, SampleUserAgent));

        await tracker.TrackCurrentVisitAsync(SamplePostId);
        var second = await tracker.TrackCurrentVisitAsync(SamplePostId);

        Assert.False(second.Data);
        Assert.Single(repo.Rows);
    }

    /// <summary>
    /// The same visitor reading a second post raises the total but not the number of distinct
    /// visitors, which is the difference between a total view and a unique one.
    /// </summary>
    [Fact]
    public async Task SameVisitorOnASecondPostRaisesTotalOnly()
    {
        var repo = new FakePostViewRepo();
        var tracker = BuildTracker(repo, BuildAccessor(SampleAddress, SampleUserAgent));

        await tracker.TrackCurrentVisitAsync(SamplePostId);
        await tracker.TrackCurrentVisitAsync(SamplePostId + 1);

        Assert.Equal(2, repo.Rows.Count);
        Assert.Equal(1, repo.UniqueVisitorCount);
    }

    /// <summary>
    /// A different user-agent on the same address is a different visitor, so both the total and the
    /// distinct-visitor count rise.
    /// </summary>
    [Fact]
    public async Task DifferentUserAgentIsADifferentVisitor()
    {
        var repo = new FakePostViewRepo();
        var first = BuildTracker(repo, BuildAccessor(SampleAddress, SampleUserAgent));
        var second = BuildTracker(repo, BuildAccessor(SampleAddress, "Mozilla/5.0 (second reader)"));

        await first.TrackCurrentVisitAsync(SamplePostId);
        await second.TrackCurrentVisitAsync(SamplePostId);

        Assert.Equal(2, repo.Rows.Count);
        Assert.Equal(2, repo.UniqueVisitorCount);
    }

    /// <summary>
    /// A non-positive post id is refused before any write is attempted.
    /// </summary>
    [Fact]
    public async Task InvalidPostIdIsRefused()
    {
        var repo = new FakePostViewRepo();
        var tracker = BuildTracker(repo, BuildAccessor(SampleAddress, SampleUserAgent));

        var result = await tracker.TrackCurrentVisitAsync(0);

        Assert.True(result.IsFailure);
        Assert.Empty(repo.Rows);
    }

    /// <summary>
    /// The configured de-duplication window reaches the repository, so a deployment can widen or
    /// narrow what counts as one visit.
    /// </summary>
    [Fact]
    public async Task ConfiguredDedupeWindowReachesTheRepository()
    {
        var repo = new FakePostViewRepo();
        var tracker = BuildTracker(repo, BuildAccessor(SampleAddress, SampleUserAgent), "6");

        await tracker.TrackCurrentVisitAsync(SamplePostId);

        Assert.Equal(6, repo.LastDedupeWindowHours);
    }

    /// <summary>
    /// Builds a tracker over the supplied repository and request accessor.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every test uses the same salt so two trackers in one test still
    /// agree on who a visitor is.</para>
    /// <para><b>Flow:</b> assemble configuration → construct the real service.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="repo">Repository the tracker writes through.</param>
    /// <param name="accessor">Access to the request being served.</param>
    /// <param name="dedupeWindowHours">Optional window override, as configuration text.</param>
    /// <returns>A tracker ready to drive.</returns>
    private static PostViewTracker BuildTracker(
        FakePostViewRepo repo, IHttpContextAccessor accessor, string? dedupeWindowHours = null)
    {
        var settings = new Dictionary<string, string?> { ["Analytics:VisitorSalt"] = SampleSalt };
        if (dedupeWindowHours != null)
            settings["Analytics:ViewDedupeWindowHours"] = dedupeWindowHours;

        return new PostViewTracker(
            repo,
            new StubConfiguration(settings),
            new RecordingLogger<PostViewTracker>(),
            accessor);
    }

    /// <summary>
    /// Builds a request accessor carrying one visitor's address and user-agent.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The pair is exactly what the tracker hashes, so varying it is how
    /// a test creates a second visitor.</para>
    /// <para><b>Flow:</b> build a context → set the remote address and the header → wrap it.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="ipAddress">Transport address of the caller.</param>
    /// <param name="userAgent">User-agent header of the caller.</param>
    /// <returns>An accessor whose <c>HttpContext</c> describes that visitor.</returns>
    private static IHttpContextAccessor BuildAccessor(string ipAddress, string userAgent)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ipAddress);
        context.Request.Headers.UserAgent = userAgent;

        return new StubHttpContextAccessor(context);
    }
}
