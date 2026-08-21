namespace BlogEngine.Services;

/// <summary>
/// One post view, captured from a request that is still in flight, waiting to be written.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [REQ-NFR-034] Carries a view across the boundary between the render that
/// observed it and the background worker that persists it.</para>
///
/// <para><b>Why it exists at all — what is gone by the time the write happens.</b> A view can only
/// be attributed to a visitor while an HTTP request is being served: the transport address lives on
/// <c>HttpContext.Connection</c> and the user agent on <c>HttpContext.Request</c>, and both are
/// disposed the moment the response completes. Handing the background worker a post id and letting
/// it look the visitor up later is therefore impossible, not merely inelegant. This type is the
/// snapshot taken while the information still exists — three values copied out of the request, with
/// no reference back into it.</para>
///
/// <para><b>Why the raw address is copied rather than the hash.</b> Hashing needs the configured
/// salt and is the tracker's job; carrying the inputs keeps exactly one place in the codebase where
/// a visitor identity is constructed. The values live only in memory, only until the worker drains
/// the queue, and are never persisted — <c>PostViews.ViewerIp</c> is written as NULL, as it has been
/// since REQ-FN-034.</para>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Created by <c>PostViewTracker.TrackCurrentVisitAsync</c>, queued through
/// <see cref="IPostViewQueue"/>, consumed by <c>PostViewWriter</c>. A readonly record struct because
/// it is allocated once per article render on the hottest path the site has, and it never outlives
/// the queue.</para>
/// </remarks>
/// <param name="PostId">The post that was viewed; always greater than zero when queued.</param>
/// <param name="IpAddress">The visitor's transport address, used only as hash input, never stored.</param>
/// <param name="UserAgent">The visitor's user-agent string, used only as hash input, never stored.</param>
public readonly record struct PostViewRequest(long PostId, string IpAddress, string UserAgent);
