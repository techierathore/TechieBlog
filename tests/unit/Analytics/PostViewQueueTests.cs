using BlogEngine.Services;
using Microsoft.AspNetCore.Http;
using TechieBlog.Tests.Dashboard;
using TechieBlog.Tests.TestDoubles;
using Xunit;

namespace TechieBlog.Tests.Analytics;

/// <summary>
/// Tests that the post-view write really has left the article render path. [REQ-NFR-034]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The defect this suite exists to stop coming back is a regression that would
/// look like nothing at all — someone "simplifies" the tracker back into awaiting the repository, the
/// site keeps working, every other test still passes, and the only symptom is that every article
/// render blocks on a database write again. These tests assert the negative: after
/// <c>TrackCurrentVisitAsync</c> returns, the repository has NOT been touched.</para>
///
/// <para><b>Code Flow:</b> a real <see cref="PostViewTracker"/> is driven over a real
/// <see cref="PostViewQueue"/> and a <see cref="FakePostViewRepo"/>; the queue is then drained by
/// hand so the test can see both sides of the hand-off.</para>
///
/// <para><b>Dependencies:</b> xUnit, <see cref="FakePostViewRepo"/>, <c>StubConfiguration</c>,
/// <c>RecordingLogger</c>.</para>
///
/// <para><b>Usage:</b> Pure unit tests — no database, no host, no browser.</para>
/// </remarks>
public class PostViewQueueTests
{
    private const long SamplePostId = 42;
    private const string SampleSalt = "unit-test-salt";
    private const string SampleAddress = "203.0.113.7";
    private const string SampleUserAgent = "Mozilla/5.0 (TechieBlog unit test)";

    /// <summary>
    /// A render hands the view to the queue and returns without writing anything, which is the whole
    /// point of REQ-NFR-034 — a read request must not wait on a write.
    /// </summary>
    [Fact]
    public async Task RenderQueuesTheViewInsteadOfWritingIt()
    {
        var repo = new FakePostViewRepo();
        var queue = BuildQueue();
        var tracker = BuildTracker(repo, queue);

        var result = await tracker.TrackCurrentVisitAsync(SamplePostId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data);
        Assert.Empty(repo.Rows);
        Assert.Single(await DrainAsync(queue));
    }

    /// <summary>
    /// The queued item carries the visitor's address and user-agent, because both live on the
    /// HttpContext and are gone by the time the background writer runs.
    /// </summary>
    [Fact]
    public async Task QueuedViewCarriesTheVisitorSnapshot()
    {
        var repo = new FakePostViewRepo();
        var queue = BuildQueue();
        var tracker = BuildTracker(repo, queue);

        await tracker.TrackCurrentVisitAsync(SamplePostId);
        var queued = await DrainAsync(queue);

        Assert.Equal(SamplePostId, queued[0].PostId);
        Assert.Equal(SampleAddress, queued[0].IpAddress);
        Assert.Equal(SampleUserAgent, queued[0].UserAgent);
    }

    /// <summary>
    /// A render with no ambient HTTP request queues nothing, so the interactive pass of a prerendered
    /// Blazor page still cannot count a second view for the same page load.
    /// </summary>
    [Fact]
    public async Task RenderWithoutRequestQueuesNothing()
    {
        var repo = new FakePostViewRepo();
        var queue = BuildQueue();
        var tracker = new PostViewTracker(
            repo,
            new StubConfiguration(new Dictionary<string, string?> { ["Analytics:VisitorSalt"] = SampleSalt }),
            new RecordingLogger<PostViewTracker>(),
            new StubHttpContextAccessor(),
            queue);

        var result = await tracker.TrackCurrentVisitAsync(SamplePostId);

        Assert.True(result.IsSuccess);
        Assert.False(result.Data);
        Assert.Empty(await DrainAsync(queue));
    }

    /// <summary>
    /// A non-positive post id is refused before anything is queued, so a bad id cannot ride the queue
    /// all the way to the database only to be rejected there.
    /// </summary>
    [Fact]
    public async Task InvalidPostIdIsRefusedBeforeQueueing()
    {
        var repo = new FakePostViewRepo();
        var queue = BuildQueue();
        var tracker = BuildTracker(repo, queue);

        var result = await tracker.TrackCurrentVisitAsync(0);

        Assert.True(result.IsFailure);
        Assert.Empty(await DrainAsync(queue));
    }

    /// <summary>
    /// A saturated queue drops the view and says so, rather than blocking the render — losing one
    /// visitor-day of one post is cheaper than making a reader wait for an analytics write.
    /// </summary>
    [Fact]
    public void SaturatedQueueDropsRatherThanBlocking()
    {
        var queue = BuildQueue();
        var sample = new PostViewRequest(SamplePostId, SampleAddress, SampleUserAgent);

        for (var i = 0; i < PostViewQueue.Capacity; i++)
            Assert.True(queue.TryEnqueue(sample));

        Assert.False(queue.TryEnqueue(sample));
    }

    /// <summary>
    /// With no queue registered the tracker still writes inline, so a unit test or a future caller
    /// that constructs it directly keeps the old, fully synchronous behaviour.
    /// </summary>
    [Fact]
    public async Task TrackerWithoutQueueStillWritesInline()
    {
        var repo = new FakePostViewRepo();
        var tracker = BuildTracker(repo, null);

        var result = await tracker.TrackCurrentVisitAsync(SamplePostId);

        Assert.True(result.Data);
        Assert.Single(repo.Rows);
    }

    /// <summary>
    /// Builds a queue with a recording logger attached.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The real implementation is used, not a stand-in — the drop
    /// behaviour under saturation is one of the things under test.</para>
    /// <para><b>Flow:</b> construct with a recording logger.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>An empty queue.</returns>
    private static PostViewQueue BuildQueue() => new(new RecordingLogger<PostViewQueue>());

    /// <summary>
    /// Builds a tracker over the supplied repository and queue, with a request already in flight.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A fixed salt keeps two trackers in one test agreeing on who a
    /// visitor is.</para>
    /// <para><b>Flow:</b> assemble configuration and a request → construct the real service.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="repo">Repository the tracker writes through when it writes at all.</param>
    /// <param name="queue">Queue to hand views to, or null to force the inline fallback.</param>
    /// <returns>A tracker ready to drive.</returns>
    private static PostViewTracker BuildTracker(FakePostViewRepo repo, IPostViewQueue? queue)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(SampleAddress);
        context.Request.Headers.UserAgent = SampleUserAgent;

        return new PostViewTracker(
            repo,
            new StubConfiguration(new Dictionary<string, string?> { ["Analytics:VisitorSalt"] = SampleSalt }),
            new RecordingLogger<PostViewTracker>(),
            new StubHttpContextAccessor(context),
            queue);
    }

    /// <summary>
    /// Reads everything currently sitting in the queue without waiting for more.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The queue's stream only ends on cancellation, so the drain is
    /// bounded by an already-cancelled-on-timeout token and the resulting cancellation is expected
    /// rather than a failure.</para>
    /// <para><b>Flow:</b> stream with a short-lived token → collect → swallow the cancellation.</para>
    /// <para><b>Side Effects:</b> Empties the queue.</para>
    /// </remarks>
    /// <param name="queue">The queue to drain.</param>
    /// <returns>Everything the queue held.</returns>
    private static async Task<List<PostViewRequest>> DrainAsync(IPostViewQueue queue)
    {
        var drained = new List<PostViewRequest>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        try
        {
            await foreach (var item in queue.ReadAllAsync(timeout.Token))
                drained.Add(item);
        }
        catch (OperationCanceledException)
        {
            // Expected: the stream only ends when the token fires.
        }

        return drained;
    }
}
