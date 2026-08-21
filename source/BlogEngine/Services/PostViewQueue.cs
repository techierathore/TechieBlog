using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Bounded, in-process channel carrying post views from the render path to the background writer.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [REQ-NFR-034] The concrete hand-off that takes the <c>PostViews</c> INSERT
/// off the article render path.</para>
///
/// <para><b>Code Flow:</b> <see cref="TryEnqueue"/> offers the view to a
/// <see cref="Channel{T}"/> and returns at once → <c>PostViewWriter</c> awaits
/// <see cref="ReadAllAsync"/> and drains it.</para>
///
/// <para><b>Why a channel rather than <c>Task.Run</c> or a bare unobserved <c>Task</c>.</b> The
/// obvious "make it not block" fix is to start the write on a background task and not await it.
/// That is wrong here in three separate ways, and the channel answers all three:</para>
/// <list type="bullet">
///   <item><b>Unobserved exceptions.</b> A faulted task nobody awaits surfaces at finalisation
///     through <c>TaskScheduler.UnobservedTaskException</c> — the host wires a handler for exactly
///     that (REQ-NFR-013), and a database blip on a popular article would fire it once per reader.
///     The writer awaits every operation inside a try/catch, so a failure is a log line.</item>
///   <item><b>Disposed scope.</b> The tracker is transient and its repository belongs to the
///     circuit/request scope that is torn down as soon as the render finishes. Work started on that
///     scope and left running reaches for a disposed connection. The writer resolves its own
///     dependencies from a fresh <see cref="IServiceScopeFactory"/> scope per item instead.</item>
///   <item><b>Unbounded fan-out.</b> One background task per view means a traffic spike becomes an
///     unbounded pile of concurrent inserts. A bounded channel with one consumer converts the same
///     spike into a queue of known maximum size and a steady write rate.</item>
/// </list>
///
/// <para><b>What happens when the queue fills, and why that is the right answer.</b> The capacity is
/// <see cref="Capacity"/> items. Once full, a new view is discarded and <see cref="TryEnqueue"/>
/// reports <c>false</c>: a dropped view undercounts, but a blocked render costs a reader the
/// article, and under the de-duplication rule a lost view is at most one visitor-day of one post, so
/// the number stays a faithful readership signal. Drops are logged as a warning once every
/// <see cref="DropLogInterval"/> drops, so a chronically full queue is visible in the log without
/// the log itself becoming the next bottleneck.</para>
///
/// <para><b>The full mode is <c>Wait</c>, and <c>DropWrite</c> here would have been a silent
/// bug.</b> <see cref="BoundedChannelFullMode.DropWrite"/> reads like exactly what is wanted — "drop
/// the write when full" — and it does drop the item, but it also makes <c>TryWrite</c> return
/// <b>true</b> while doing so. Every drop would then be invisible: the counter below would never
/// move, the warning would never be logged, and view tracking could be quietly losing traffic with
/// nothing anywhere to say so. A unit test caught this
/// (<c>PostViewQueueTests.SaturatedQueueDropsRatherThanBlocking</c>), which is the only reason it is
/// not in the shipped code. <see cref="BoundedChannelFullMode.Wait"/> makes only the
/// <i>asynchronous</i> <c>WriteAsync</c> wait — <c>TryWrite</c>, which is all this class ever calls,
/// still returns immediately, and returns <c>false</c> when the channel is full. That is a
/// non-blocking enqueue with an honest answer, which is exactly the contract
/// <see cref="IPostViewQueue.TryEnqueue"/> promises. <b>Never call <c>WriteAsync</c> on this
/// channel</b> — that, and only that, is what would reintroduce blocking.</para>
///
/// <para><b>Capacity, and why it is this number.</b> The writer performs one indexed insert per
/// item — sub-millisecond against a local PostgreSQL — so 2,048 items is several seconds of sustained
/// backlog at any load this blog will see, while costing a few tens of kilobytes at worst. It is a
/// shock absorber, not a durable store.</para>
///
/// <para><b>Durability — deliberately none.</b> The queue is in memory. Views still queued when the
/// process stops are lost, and that is accepted: they are analytics, not orders. <c>PostViewWriter</c>
/// drains what it can during graceful shutdown, which covers the ordinary restart.</para>
///
/// <para><b>Dependencies:</b> <see cref="Channel{T}"/> and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> Registered <b>singleton</b> as <see cref="IPostViewQueue"/> by
/// <c>BlogSvcInitializer</c> — a transient registration would give every producer its own empty
/// channel and silently stop all view tracking. Many producers, one consumer.</para>
/// </remarks>
public class PostViewQueue : IPostViewQueue
{
    /// <summary>
    /// Maximum views held in memory before further views are dropped.
    /// </summary>
    public const int Capacity = 2048;

    /// <summary>
    /// How many drops pass between warning log lines, so a full queue reports itself without flooding.
    /// </summary>
    private const int DropLogInterval = 100;

    private readonly Channel<PostViewRequest> channel;
    private readonly ILogger<PostViewQueue> logger;
    private int dropCount;

    /// <summary>
    /// Creates the bounded channel the render path writes into.
    /// </summary>
    /// <remarks>
    /// A single reader is declared so the channel can take its cheaper single-consumer path; many
    /// writers are expected, one per concurrent article render. See the class remarks for why the
    /// full mode is <c>Wait</c> rather than the deceptively named <c>DropWrite</c>.
    /// </remarks>
    /// <param name="logger">Logger used to report a saturated queue.</param>
    public PostViewQueue(ILogger<PostViewQueue> logger)
    {
        this.logger = logger;
        channel = Channel.CreateBounded<PostViewRequest>(new BoundedChannelOptions(Capacity)
        {
            // Wait, NOT DropWrite: TryWrite must be able to REPORT a drop. See the class remarks.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Accepts the view when there is room and discards it when there is
    /// not, never waiting either way — the caller is a render in progress.</para>
    /// <para><b>Flow:</b> offer to the channel → on rejection count the drop and log every
    /// <see cref="DropLogInterval"/>th one → report the outcome.</para>
    /// <para><b>Side Effects:</b> Enqueues at most one item; may write one warning log line.</para>
    /// </remarks>
    public bool TryEnqueue(PostViewRequest request)
    {
        if (channel.Writer.TryWrite(request))
            return true;

        var drops = Interlocked.Increment(ref dropCount);
        if (drops % DropLogInterval == 1)
        {
            logger.LogWarning(
                "The post-view queue is full; {DropCount} view(s) dropped so far. Capacity is {Capacity}.",
                drops,
                Capacity);
        }

        return false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> Hands the writer every queued view in arrival order.</para>
    /// <para><b>Flow:</b> delegate to the channel reader's asynchronous stream.</para>
    /// <para><b>Side Effects:</b> Consumes items from the queue.</para>
    /// </remarks>
    public IAsyncEnumerable<PostViewRequest> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
