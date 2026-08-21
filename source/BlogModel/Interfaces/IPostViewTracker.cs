namespace BlogModels.Interfaces;

/// <summary>
/// Records post views for analytics, de-duplicated per visit and free of raw IP storage.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published contract a post page calls once per render to satisfy
/// BRD-60. Tracking must never break page rendering, so every failure is logged and folded into a
/// failed <c>Result</c> rather than thrown at the caller.</para>
///
/// <para><b>Definition of a view and a unique view:</b></para>
/// <list type="bullet">
///   <item>A <b>view</b> is one row in <c>PostViews</c>. The tracker writes at most one row per
///         visitor per post per de-duplication window (24 hours by default), so refreshing or
///         re-opening a post inside one reading session is counted once.</item>
///   <item>A <b>unique view</b> is one distinct visitor hash for the post over all time.</item>
///   <item>A <b>visitor</b> is identified by <c>SHA-256(siteSalt + "|" + ipAddress + "|" +
///         userAgent)</c>. Only that hash is persisted — the raw IP is never written — and the salt
///         makes the hash non-reversible by dictionary attack over the IPv4 space.</item>
/// </list>
///
/// <para><b>Code Flow:</b> caller supplies the raw IP and user-agent → the tracker salts and hashes
/// them into a visitor identity → <c>IPostViewRepo.RecordViewAsync</c> performs a conditional insert
/// that writes a row only if this visitor has not viewed this post inside the window → the outcome
/// comes back as a <c>Result&lt;bool&gt;</c>.</para>
///
/// <para><b>Dependencies:</b> <c>IPostViewRepo</c> for the conditional insert.</para>
///
/// <para><b>Usage:</b> A page normally calls <see cref="TrackCurrentVisitAsync"/>, which reads the
/// visitor's address and user-agent from the ambient HTTP request itself; the three-argument
/// <see cref="TrackViewAsync"/> stays available for callers that already hold those values (imports,
/// tests, a future API head). Ignore the returned flag unless the caller wants to know whether the
/// view was new.</para>
/// </remarks>
public interface IPostViewTracker
{
    /// <summary>
    /// Records a view of a post for the visitor behind the request currently being served.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Identical to <see cref="TrackViewAsync"/> except that the
    /// visitor's address and user-agent are read from the ambient HTTP request instead of being
    /// supplied. When there is no ambient request — a Blazor Server component re-rendering inside an
    /// established circuit, or a background worker — nothing is written and the call reports a
    /// de-duplicated view rather than failing, because "there is no visitor here" is a normal
    /// outcome and analytics must never break a page.</para>
    /// <para><b>Flow:</b> resolve the ambient request → no request means nothing to count → read the
    /// address and user-agent → delegate to <see cref="TrackViewAsync"/>.</para>
    /// <para><b>[REQ-NFR-034] This call performs no I/O and does not wait for the write.</b> It
    /// captures the visitor from the ambient request — which is the only moment that information
    /// exists — and hands the view to a background writer. A page render therefore never blocks on
    /// an analytics INSERT. Two things follow for a caller:</para>
    /// <list type="bullet">
    ///   <item>The returned flag means "accepted for recording", not "written". The row appears
    ///     shortly afterwards; whether it was a new view or a de-duplicated one is decided by the
    ///     writer and reported only to the log.</item>
    ///   <item>View counts read immediately after this call do not include this visit. A byline
    ///     showing the figure as it stood when the reader arrived is the intended behaviour.</item>
    /// </list>
    /// <para>Use <see cref="TrackViewAsync"/> instead when the caller genuinely needs to know the
    /// outcome and already holds the visitor's details.</para>
    /// <para><b>Side Effects:</b> Queues at most one view for writing. Never throws.</para>
    /// </remarks>
    /// <param name="postId">The post being viewed; must be greater than zero.</param>
    /// <returns>Success carrying true when the view was accepted for recording, false when there was
    /// no ambient request to attribute it to or the queue was saturated; failure when the post id is
    /// invalid.</returns>
    Task<Result<bool>> TrackCurrentVisitAsync(long postId);

    /// <summary>
    /// Records a view of a post for the calling visitor.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Derives the visitor hash, then asks the repository for a
    /// conditional insert; a repeat view inside the de-duplication window is not counted again.</para>
    /// <para><b>Flow:</b> validate post id → hash visitor → conditional insert → return whether a
    /// row was written.</para>
    /// <para><b>Side Effects:</b> May write one row to <c>PostViews</c>. Never throws.</para>
    /// </remarks>
    /// <param name="postId">The post being viewed; must be greater than zero.</param>
    /// <param name="ipAddress">Caller IP address, used only as hash input.</param>
    /// <param name="userAgent">Caller user-agent string, used only as hash input.</param>
    /// <returns>Success carrying true when a new view row was written, false when de-duplicated;
    /// failure when the write could not be attempted.</returns>
    Task<Result<bool>> TrackViewAsync(long postId, string ipAddress, string userAgent);
}
