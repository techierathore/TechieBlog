namespace BlogEngine.Services;

/// <summary>
/// Hand-off point between the render that observed a post view and the worker that writes it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [REQ-NFR-034] Gets the <c>PostViews</c> write off the render path. A read
/// request should not wait on a write, and until this existed every article render blocked on an
/// INSERT round trip before its markup could be returned.</para>
///
/// <para><b>Code Flow:</b> <c>PostViewTracker.TrackCurrentVisitAsync</c> snapshots the visitor from
/// the live request and calls <see cref="TryEnqueue"/> → the call returns immediately and the render
/// continues → <c>PostViewWriter</c> reads the item from <see cref="ReadAllAsync"/> on its own
/// background loop, opens its own DI scope and performs the conditional insert.</para>
///
/// <para><b>Why an interface, and why the enqueue side is synchronous.</b> The producer is a Blazor
/// component's render path; giving it an <c>await</c> would reintroduce exactly the latency this
/// exists to remove, and an <c>await</c> that can block on a full queue would be worse than the
/// write it replaced. <see cref="TryEnqueue"/> therefore never waits and never throws — it either
/// accepts the view or reports that it did not. The interface keeps the tracker testable without a
/// channel or a hosted service.</para>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Registered as a <b>singleton</b> by <c>BlogSvcInitializer</c>; it must be, or
/// producer and consumer would hold different queues and nothing would ever be written. The
/// implementation is required to be safe for many concurrent producers and one consumer.</para>
/// </remarks>
public interface IPostViewQueue
{
    /// <summary>
    /// Offers a view for writing without ever blocking the caller.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Analytics must never cost a reader the article, so a queue that
    /// cannot accept the item loses the view rather than delaying, failing or throwing. Dropping a
    /// view is the correct outcome under that constraint: the number on the page is a readership
    /// signal, not an accounting record.</para>
    /// <para><b>Flow:</b> offer the item to the underlying queue → report acceptance.</para>
    /// <para><b>Side Effects:</b> Enqueues at most one item. Never blocks. Never throws.</para>
    /// </remarks>
    /// <param name="request">The captured view.</param>
    /// <returns><c>true</c> when the view was queued; <c>false</c> when it was dropped.</returns>
    bool TryEnqueue(PostViewRequest request);

    /// <summary>
    /// Streams queued views to the background writer until the host stops.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One consumer is assumed. The stream completes only when the
    /// token is cancelled, which is the host shutting down.</para>
    /// <para><b>Flow:</b> yield each queued view as it becomes available.</para>
    /// <para><b>Side Effects:</b> Removes items from the queue as they are yielded.</para>
    /// </remarks>
    /// <param name="cancellationToken">Ends the stream when the host stops.</param>
    /// <returns>An asynchronous stream of queued views.</returns>
    IAsyncEnumerable<PostViewRequest> ReadAllAsync(CancellationToken cancellationToken);
}
