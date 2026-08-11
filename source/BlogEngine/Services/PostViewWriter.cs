using BlogModels.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Hosted background service that drains the post-view queue and performs the actual writes.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [REQ-NFR-034] The consumer half of the mechanism that took the
/// <c>PostViews</c> INSERT off the article render path. The render observes the view and queues it;
/// this loop is what eventually writes it.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="ExecuteAsync"/> streams <see cref="IPostViewQueue.ReadAllAsync"/> until the
///     host's stopping token is cancelled.</item>
///   <item>Each item gets its <b>own DI scope</b>, from which <see cref="IPostViewTracker"/> is
///     resolved. A hosted service is a singleton and must never capture a scoped or transient
///     dependency in a field; more sharply, the scope that observed this view was torn down when the
///     render finished, so the write cannot borrow it.</item>
///   <item>The tracker's three-argument <c>TrackViewAsync</c> runs the conditional insert. Hashing
///     stays inside the tracker, so a queued view and a directly recorded one produce byte-identical
///     visitor identities.</item>
/// </list>
///
/// <para><b>Failure contract — a failed write is never fatal, and is never silent.</b> The tracker
/// already converts its own failures into a failed <c>Result</c> rather than throwing, so the common
/// case is handled by logging that result. The <c>catch</c> around the loop body exists for
/// everything below it — a disposed provider, a scope that cannot be created, a driver-level fault —
/// because an exception escaping <see cref="ExecuteAsync"/> would stop the service for the lifetime
/// of the process, and the blog would keep serving articles while silently never recording another
/// view. That failure mode is exactly what a fire-and-forget <c>Task</c> would have produced, only
/// with an unobserved-exception handler firing instead of a log line.</para>
///
/// <para><b>Ordering and concurrency.</b> One consumer, strictly sequential. Views are therefore
/// written in the order they were observed and never race each other. Sequential is fast enough by a
/// wide margin — each item is one indexed statement — and it means the de-duplication and rollup
/// logic never contends with itself. If throughput ever demanded parallelism, the correct change is
/// several consumers each with its own scope, not a wider statement.</para>
///
/// <para><b>Shutdown.</b> Cancellation ends the stream and the loop returns, which is what
/// <c>StopAsync</c> waits on. Items still queued at that point are lost by design — see
/// <see cref="PostViewQueue"/> on why analytics durability is not bought here.</para>
///
/// <para><b>Dependencies:</b> <see cref="IPostViewQueue"/> (singleton), <see cref="IServiceScopeFactory"/>
/// for per-item scopes, and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered by <c>BlogSvcInitializer</c> with <c>AddHostedService</c>. It runs
/// with no user context and no authorization policy — recording that an anonymous reader opened an
/// article is the whole of its remit.</para>
/// </remarks>
public class PostViewWriter : BackgroundService
{
    private readonly IPostViewQueue queue;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<PostViewWriter> logger;

    /// <summary>
    /// Initializes the background writer.
    /// </summary>
    /// <remarks>
    /// Only singletons are captured. The tracker and its repository are resolved per item from a
    /// fresh scope, which is the rule that keeps a singleton from pinning a shorter-lived service.
    /// </remarks>
    /// <param name="queue">The shared post-view queue.</param>
    /// <param name="scopeFactory">Factory used to open a DI scope for each queued view.</param>
    /// <param name="logger">Logger for write failures.</param>
    public PostViewWriter(
        IPostViewQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<PostViewWriter> logger)
    {
        this.queue = queue;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
    }

    /// <summary>
    /// Drains the queue for as long as the host is running.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every queued view is written exactly once, in arrival order. A
    /// view that cannot be written is logged and abandoned; retrying would reorder the queue and, on
    /// a persistent database fault, spin.</para>
    /// <para><b>Flow:</b> stream the queue → per item, open a scope, resolve the tracker, record the
    /// view → log a failed result → repeat until cancelled.</para>
    /// <para><b>Side Effects:</b> Writes <c>PostViews</c> rows and moves <c>PostViewCount</c>
    /// counters, through the tracker.</para>
    /// </remarks>
    /// <param name="stoppingToken">Cancelled when the host begins shutting down.</param>
    /// <returns>A task that completes when the host stops.</returns>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Post view writer started");

        try
        {
            await foreach (var request in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await WriteOneAsync(request).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown: the stopping token ended the stream.
        }

        logger.LogInformation("Post view writer stopped");
    }

    /// <summary>
    /// Records one queued view inside its own dependency-injection scope.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The write is best-effort. Analytics must never take down the
    /// host, so nothing here is allowed to escape — a failed write costs one view, an escaping
    /// exception would cost every future view in this process.</para>
    /// <para><b>Flow:</b> open a scope → resolve <see cref="IPostViewTracker"/> → record → log a
    /// failed result → dispose the scope.</para>
    /// <para><b>Side Effects:</b> May write one <c>PostViews</c> row and move one
    /// <c>PostViewCount</c> row. Never throws.</para>
    /// </remarks>
    /// <param name="request">The captured view to persist.</param>
    /// <returns>A task that completes when the view has been written or the failure logged.</returns>
    private async Task WriteOneAsync(PostViewRequest request)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var tracker = scope.ServiceProvider.GetRequiredService<IPostViewTracker>();

            var result = await tracker
                .TrackViewAsync(request.PostId, request.IpAddress, request.UserAgent)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                logger.LogWarning(
                    "Queued view for post {PostId} was not recorded: {Error}",
                    request.PostId,
                    result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write a queued view for post {PostId}", request.PostId);
        }
    }
}
