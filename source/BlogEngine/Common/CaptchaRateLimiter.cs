using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Common;

/// <summary>
/// In-process fixed-window rate limiter for captcha issuance and captcha failures. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements <see cref="ICaptchaRateLimiter"/> with two independent windows
/// per client - one bounding how many challenges may be minted, one bounding how many may be
/// failed. Splitting them matters: a visitor who reloads the image repeatedly because they cannot
/// read it is doing something different from a client submitting wrong answers, and only the
/// second should attract a lockout.</para>
///
/// <para><b>Code Flow:</b> Both windows are <see cref="ConcurrentDictionary{TKey,TValue}"/> maps of
/// client key to an immutable <c>WindowCounter</c>. Advancing a counter whose window has elapsed
/// restarts it at one, which is the window reset. Every trip is logged at warning level so the
/// event is visible in the Serilog file sink with its correlation id (REQ-NFR-015).</para>
///
/// <para><b>Dependencies:</b> <see cref="CaptchaRateLimitOptions"/> for the caps and
/// <see cref="ILogger{TCategoryName}"/> for the security log. No cache, no database - the state is
/// two counters per client and must survive nothing.</para>
///
/// <para><b>Memory — the limiter must not become its own denial of service.</b> Every distinct
/// client key allocates a dictionary entry, and the keys come from the network, so an attacker
/// rotating addresses would otherwise grow these maps without bound until the process died —
/// defeating the limiter by exhausting the host it protects. Two mechanisms bound it: an expired
/// counter is <b>dropped on read</b> (see <see cref="IsFailureBlocked"/>), which reclaims the
/// common case of a client that never returns; and a sweep runs on write once a map passes
/// <see cref="PruneThreshold"/> entries, removing only counters whose window has already
/// elapsed. The sweep is bounded — one pass over the map, no re-entry — and because it can only
/// remove expired entries it can never release a client that is still being throttled.</para>
///
/// <para><b>Scale-out — counters are PER PROCESS, so a multi-instance deployment divides every cap
/// by the instance count.</b> Behind a load balancer with four instances, a client spreading its
/// requests across them gets four independent counters and therefore an effective cap of 80
/// issuances per minute and 20 failures per five minutes, not 20 and 5. Each instance is
/// individually correct and the aggregate is silently four times looser — nothing logs a warning,
/// because no instance can see the others. Scaling out is the moment to replace this with a
/// distributed <see cref="ICaptchaRateLimiter"/> over a shared store; nothing but the registration
/// in <c>EngagementSvcInitializer</c> has to change.</para>
///
/// <para><b>Usage:</b> Registered as a singleton by <c>EngagementSvcInitializer</c> — a shorter
/// lifetime would reset the counters per circuit and the limiter would bound nothing.</para>
/// </remarks>
public class CaptchaRateLimiter : ICaptchaRateLimiter
{
    /// <summary>
    /// Entries a counter map may hold before an expired-entry sweep runs.
    /// </summary>
    public const int PruneThreshold = 5000;

    /// <summary>
    /// Key used when a caller supplies no client identity, so an unattributable request is still
    /// counted rather than silently exempted.
    /// </summary>
    public const string UnknownClientKey = "unknown";

    private readonly ConcurrentDictionary<string, WindowCounter> issueWindows = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, WindowCounter> failureWindows = new(StringComparer.Ordinal);
    private readonly CaptchaRateLimitOptions options;
    private readonly Func<DateTime> clock;
    private readonly ILogger<CaptchaRateLimiter> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptchaRateLimiter"/> class with the system clock.
    /// </summary>
    /// <param name="options">The configured caps.</param>
    /// <param name="logger">Logger for rate-limit trips.</param>
    public CaptchaRateLimiter(CaptchaRateLimitOptions options, ILogger<CaptchaRateLimiter> logger)
        : this(options, null, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptchaRateLimiter"/> class with an injectable
    /// clock, so window expiry is testable without waiting.
    /// </summary>
    /// <param name="options">The configured caps.</param>
    /// <param name="clock">Returns the current UTC time; null selects the system clock.</param>
    /// <param name="logger">Logger for rate-limit trips.</param>
    public CaptchaRateLimiter(CaptchaRateLimitOptions options, Func<DateTime>? clock, ILogger<CaptchaRateLimiter> logger)
    {
        this.options = options ?? new CaptchaRateLimitOptions();
        this.clock = clock ?? (() => DateTime.UtcNow);
        this.logger = logger;
    }

    /// <inheritdoc />
    public bool TryIssue(string clientKey, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var key = Normalise(clientKey);
        var now = clock();
        var window = options.IssueWindow;

        PruneIfNeeded(issueWindows, window, now);

        var counter = issueWindows.AddOrUpdate(
            key,
            _ => new WindowCounter(now, 1),
            (_, existing) => Advance(existing, now, window));

        if (counter.Count <= options.IssuePermitLimit)
            return true;

        retryAfter = RemainingWindow(counter.WindowStart, window, now);
        logger.LogWarning(
            "Captcha issuance rate limit tripped for {ClientKey}: {AttemptCount} requests against a cap of {PermitLimit} per {WindowSeconds}s; retry after {RetryAfterSeconds}s [REQ-NFR-024]",
            key,
            counter.Count,
            options.IssuePermitLimit,
            options.IssueWindowSeconds,
            (int)Math.Ceiling(retryAfter.TotalSeconds));

        return false;
    }

    /// <inheritdoc />
    public bool IsFailureBlocked(string clientKey, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        var key = Normalise(clientKey);
        if (!failureWindows.TryGetValue(key, out var counter))
            return false;

        var now = clock();
        var window = options.FailureWindow;
        if (counter.WindowStart.Add(window) <= now)
        {
            failureWindows.TryRemove(key, out _);
            return false;
        }

        if (counter.Count < options.FailurePermitLimit)
            return false;

        retryAfter = RemainingWindow(counter.WindowStart, window, now);
        return true;
    }

    /// <inheritdoc />
    public void RegisterFailure(string clientKey)
    {
        var key = Normalise(clientKey);
        var now = clock();
        var window = options.FailureWindow;

        PruneIfNeeded(failureWindows, window, now);

        var counter = failureWindows.AddOrUpdate(
            key,
            _ => new WindowCounter(now, 1),
            (_, existing) => Advance(existing, now, window));

        if (counter.Count != options.FailurePermitLimit)
            return;

        logger.LogWarning(
            "Captcha failure rate limit tripped for {ClientKey}: {FailureCount} failed answers against a cap of {PermitLimit} per {WindowSeconds}s [REQ-NFR-024]",
            key,
            counter.Count,
            options.FailurePermitLimit,
            options.FailureWindowSeconds);
    }

    /// <summary>
    /// Produces the next state of a counter after one more attempt.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A counter whose window has fully elapsed restarts at one -
    /// that is the window reset the requirement calls for. Otherwise the count rises inside the
    /// window that is already running.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="existing">The current counter.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="window">The window length.</param>
    /// <returns>The replacement counter.</returns>
    private static WindowCounter Advance(WindowCounter existing, DateTime now, TimeSpan window)
    {
        return existing.WindowStart.Add(window) <= now
            ? new WindowCounter(now, 1)
            : new WindowCounter(existing.WindowStart, existing.Count + 1);
    }

    /// <summary>
    /// Calculates how long is left before a window reopens.
    /// </summary>
    /// <remarks>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="windowStart">When the window began.</param>
    /// <param name="window">The window length.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The remaining time, never negative.</returns>
    private static TimeSpan RemainingWindow(DateTime windowStart, TimeSpan window, DateTime now)
    {
        var remaining = windowStart.Add(window) - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Normalises a client key so a null or blank identity still lands in a bucket.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unattributable caller must never be exempt from the cap -
    /// that would be the easiest evasion of all - so it shares <see cref="UnknownClientKey"/>.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="clientKey">The supplied identity.</param>
    /// <returns>A non-empty key.</returns>
    private static string Normalise(string clientKey)
    {
        return string.IsNullOrWhiteSpace(clientKey) ? UnknownClientKey : clientKey.Trim();
    }

    /// <summary>
    /// Drops counters whose window has elapsed once a map grows past the threshold.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counters are only ever removed when their window has already
    /// expired, so a sweep can never release a client that is still being throttled.</para>
    /// <para><b>Flow:</b> check the size → snapshot the expired keys → remove them.</para>
    /// <para><b>Side Effects:</b> Mutates the supplied map.</para>
    /// </remarks>
    /// <param name="windows">The map to sweep.</param>
    /// <param name="window">The window length that applies to it.</param>
    /// <param name="now">The current UTC time.</param>
    private static void PruneIfNeeded(ConcurrentDictionary<string, WindowCounter> windows, TimeSpan window, DateTime now)
    {
        if (windows.Count < PruneThreshold)
            return;

        foreach (var entry in windows)
        {
            if (entry.Value.WindowStart.Add(window) <= now)
                windows.TryRemove(entry.Key, out _);
        }
    }

    /// <summary>
    /// Immutable per-client window state.
    /// </summary>
    /// <param name="WindowStart">When the current window began.</param>
    /// <param name="Count">Attempts recorded inside the window.</param>
    private sealed record WindowCounter(DateTime WindowStart, int Count);
}
