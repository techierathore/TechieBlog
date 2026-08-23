namespace BlogEngine.Services;

/// <summary>
/// Tells the WEBSITE's own process to drop its cached content, from a caller that is not that
/// process (UAT-023 mechanism B).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Public pages read through <c>MemoryCacheService</c>'s ten-minute cache. An
/// edit made through the website evicts it correctly — <c>BlogSvc.UpdatePostAsync</c> calls
/// <c>ServiceCache.InvalidateContent</c> in the same process the reader is served from. An edit
/// made through BlogApp writes straight to the database from a SEPARATE process and never runs a
/// line of the website's code, so nothing evicts the entry and the site keeps serving the stale
/// row for up to ten minutes — exactly what the owner reported in UAT-023. This abstraction is the
/// seam <c>ManagePost.razor</c> (shared by both heads) calls after a save; only one implementation
/// of it actually reaches across a process boundary.</para>
///
/// <para><b>Code Flow:</b> the website registers a no-op — its own save already invalidated the
/// cache locally, so asking itself over HTTP would be redundant work against its own process.
/// BlogApp registers an implementation that calls the website's authenticated
/// <c>POST /api/admin/cache/refresh</c> endpoint. Neither registration happens in
/// <c>BlogSvcInitializer</c>, which defines only the shared engine graph — like
/// <c>BlogUI.IExternalLinkOpener</c> (UAT-024's own abstraction), "notify the OTHER process" is a head-specific notion
/// with no meaning inside the engine itself.</para>
///
/// <para><b>Dependencies:</b> None on this interface. Implementations vary widely (a no-op vs. an
/// authenticated HTTP call).</para>
///
/// <para><b>Usage:</b> Call <see cref="RefreshAsync"/> after a successful post write. NEVER report
/// success the caller cannot prove — see the <see cref="CacheRefreshResult"/> contract: a failed or
/// skipped refresh must say so, not be swallowed into a generic "saved" message.</para>
/// </remarks>
public interface ISiteCacheNotifier
{
    /// <summary>
    /// Asks the website to drop its cached content, taxonomy and settings entries.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors what <c>Settings.razor</c>'s "Clear cached content"
    /// button already does for an admin signed into the website (UAT-001) — that remedy is
    /// unreachable from BlogApp, which has no website admin session, so this is the same fix
    /// reachable from outside the process.</para>
    /// <para><b>Side Effects:</b> Implementation-dependent: none for a no-op, or one authenticated
    /// HTTP request plus a server-side cache eviction for a real one.</para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the refresh attempt.</param>
    /// <returns>
    /// A <see cref="CacheRefreshResult"/> describing what actually happened — never a bare
    /// success/failure boolean, so the caller can tell "not configured" apart from "tried and
    /// failed" apart from "verified working."
    /// </returns>
    Task<CacheRefreshResult> RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// What happened when <see cref="ISiteCacheNotifier.RefreshAsync"/> was asked to run.
/// </summary>
public enum CacheRefreshOutcome
{
    /// <summary>
    /// This head has no need to notify anyone (the website's own no-op), or is not configured to
    /// (BlogApp with no website address on record). Not an error — the caller should not report a
    /// failure, but also should not claim the site was refreshed.
    /// </summary>
    NotApplicable,

    /// <summary>The website confirmed the cache was dropped.</summary>
    Succeeded,

    /// <summary>
    /// An attempt was made and did not succeed — unreachable host, a non-success HTTP status, an
    /// unauthorised token, or any other failure. <see cref="CacheRefreshResult.Detail"/> carries a
    /// curated, non-<c>ex.Message</c> reason.
    /// </summary>
    Failed
}

/// <summary>
/// The honest outcome of one <see cref="ISiteCacheNotifier.RefreshAsync"/> call.
/// </summary>
/// <remarks>
/// A deliberately narrow shape: an earlier round of this feature shipped a probe that answered
/// "OK" for something it had not actually verified. This type exists so that mistake cannot repeat
/// — <see cref="Outcome"/> is never defaulted to <see cref="CacheRefreshOutcome.Succeeded"/>, every
/// caller must set it explicitly, and <see cref="Detail"/> is required whenever it is anything else.
/// </remarks>
public sealed class CacheRefreshResult
{
    /// <summary>What happened.</summary>
    public required CacheRefreshOutcome Outcome { get; init; }

    /// <summary>
    /// A short, user-safe explanation. Required (non-null) for every outcome except
    /// <see cref="CacheRefreshOutcome.Succeeded"/>, where it is optional colour. Never the raw text
    /// of a caught exception — see the Coding Standards' exception-disclosure rule.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// The <see cref="CacheRefreshOutcome.NotApplicable"/> result the website's own no-op
    /// implementation always returns.
    /// </summary>
    public static readonly CacheRefreshResult NotApplicable =
        new() { Outcome = CacheRefreshOutcome.NotApplicable };
}
