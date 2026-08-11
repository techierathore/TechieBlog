using BlogEngine.Services;
using BlogModels.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using TechieBlog.Tests.Dashboard;
using Xunit;

namespace TechieBlog.Tests.Analytics;

/// <summary>
/// Tests for the background service that drains the post-view queue. [REQ-NFR-034]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Moving the write off the render path is only an improvement if the write
/// still happens. These tests close the loop the queue tests open: a queued view really does reach
/// the repository, a failing write does not take the loop down with it, and the loop keeps running
/// afterwards — the failure mode that would otherwise leave the blog serving articles while silently
/// recording nothing ever again.</para>
///
/// <para><b>Code Flow:</b> a real <see cref="PostViewWriter"/> is started over a real
/// <see cref="PostViewQueue"/> and a container holding a real <see cref="PostViewTracker"/> over a
/// fake repository; the test enqueues, waits for the row to appear, and stops the service.</para>
///
/// <para><b>Dependencies:</b> xUnit, Microsoft.Extensions.DependencyInjection,
/// <see cref="FakePostViewRepo"/>, <see cref="ThrowingPostViewRepo"/>.</para>
///
/// <para><b>Usage:</b> Pure unit tests — no database, no host, no browser.</para>
/// </remarks>
public class PostViewWriterTests
{
    private const long SamplePostId = 42;
    private const string SampleAddress = "203.0.113.7";
    private const string SampleUserAgent = "Mozilla/5.0 (TechieBlog unit test)";

    /// <summary>
    /// A queued view reaches the repository once the background writer has run, so the write really
    /// does still happen after being taken off the render path.
    /// </summary>
    [Fact]
    public async Task QueuedViewReachesTheRepository()
    {
        var repo = new FakePostViewRepo();
        var queue = new PostViewQueue(new RecordingLogger<PostViewQueue>());
        var writer = BuildWriter(queue, repo);

        await writer.StartAsync(CancellationToken.None);
        queue.TryEnqueue(new PostViewRequest(SamplePostId, SampleAddress, SampleUserAgent));
        await WaitForAsync(() => repo.Rows.Count == 1);
        await writer.StopAsync(CancellationToken.None);

        Assert.Single(repo.Rows);
        Assert.Equal(SamplePostId, repo.Rows[0].PostId);
        Assert.Equal(64, repo.Rows[0].VisitorHash.Length);
    }

    /// <summary>
    /// Several queued views are all written, in the order they were observed, because the writer has
    /// exactly one consumer and never runs items beside each other.
    /// </summary>
    [Fact]
    public async Task EveryQueuedViewIsWrittenInOrder()
    {
        var repo = new FakePostViewRepo();
        var queue = new PostViewQueue(new RecordingLogger<PostViewQueue>());
        var writer = BuildWriter(queue, repo);

        await writer.StartAsync(CancellationToken.None);
        for (var offset = 0; offset < 5; offset++)
            queue.TryEnqueue(new PostViewRequest(SamplePostId + offset, SampleAddress, SampleUserAgent));

        await WaitForAsync(() => repo.Rows.Count == 5);
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(
            new[] { SamplePostId, SamplePostId + 1, SamplePostId + 2, SamplePostId + 3, SamplePostId + 4 },
            repo.Rows.Select(row => row.PostId).ToArray());
    }

    /// <summary>
    /// A repository that throws costs exactly one view and nothing else: the writer survives and goes
    /// on to write the next queued view, which is the difference between a bad minute and a process
    /// that never records another view.
    /// </summary>
    [Fact]
    public async Task FailedWriteDoesNotStopTheWriter()
    {
        var repo = new ThrowingPostViewRepo();
        var queue = new PostViewQueue(new RecordingLogger<PostViewQueue>());
        var writer = BuildWriter(queue, repo);

        await writer.StartAsync(CancellationToken.None);
        queue.TryEnqueue(new PostViewRequest(SamplePostId, SampleAddress, SampleUserAgent));
        await WaitForAsync(() => repo.AttemptCount == 1);

        repo.IsFailing = false;
        queue.TryEnqueue(new PostViewRequest(SamplePostId + 1, SampleAddress, SampleUserAgent));
        await WaitForAsync(() => repo.AttemptCount == 2);
        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(2, repo.AttemptCount);
    }

    /// <summary>
    /// Builds a writer over a container that resolves a real tracker from the supplied repository.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A real service provider is used rather than a stubbed scope
    /// factory, because "takes a fresh DI scope per item" is one of the properties under test.</para>
    /// <para><b>Flow:</b> register the repository and the tracker → build the provider → construct
    /// the writer around its scope factory.</para>
    /// <para><b>Side Effects:</b> None until the writer is started.</para>
    /// </remarks>
    /// <param name="queue">Queue the writer drains.</param>
    /// <param name="repo">Repository the resolved tracker writes through.</param>
    /// <returns>A writer ready to start.</returns>
    private static PostViewWriter BuildWriter(IPostViewQueue queue, IPostViewRepo repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new TestDoubles.StubConfiguration(
                new Dictionary<string, string?> { ["Analytics:VisitorSalt"] = "unit-test-salt" }));
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<PostViewTracker>>(
            new RecordingLogger<PostViewTracker>());
        services.AddTransient<IPostViewTracker, PostViewTracker>();

        var provider = services.BuildServiceProvider();

        return new PostViewWriter(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new RecordingLogger<PostViewWriter>());
    }

    /// <summary>
    /// Polls until a condition holds or a short deadline passes.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The writer runs on its own loop, so the test has to wait for it
    /// rather than assert immediately. Polling with a deadline keeps a genuine regression a fast
    /// failure instead of a hung suite.</para>
    /// <para><b>Flow:</b> check → yield → repeat until true or the deadline passes.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="condition">The condition to wait for.</param>
    /// <returns>A task that completes when the condition holds or the deadline passes.</returns>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !condition())
            await Task.Delay(10);
    }
}
