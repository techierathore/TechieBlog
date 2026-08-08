using BlogModels.Interfaces;
using System.Collections.Concurrent;

namespace BlogEngine.Common;

/// <summary>
/// In-process sliding-window lockout for repeated failed logins (REQ-NFR-005, BRD-82).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Refuses further authentication attempts for an account once
/// <see cref="MaxFailuresPerWindow"/> failures occur inside <see cref="FailureWindowMinutes"/>,
/// then keeps refusing for <see cref="LockoutMinutes"/>. This complements the ASP.NET Core rate
/// limiter, which cannot see logins that arrive over an established Blazor Server circuit.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>AuthSvc.AppLogin</c> calls <see cref="IsBlocked"/> before any database work.</item>
///   <item>A wrong password calls <see cref="RegisterFailure"/>, which increments the counter and
///     starts a lockout when the limit is hit.</item>
///   <item>A correct password calls <see cref="RegisterSuccess"/>, clearing the counter.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None beyond the BCL; state lives in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>.</para>
///
/// <para><b>Partition key — what is actually being throttled.</b> The key is the <b>login email,
/// lower-cased</b> (the dictionary is built with
/// <see cref="StringComparer.OrdinalIgnoreCase"/>, so casing cannot split a bucket). That means
/// this throttle protects <i>an account</i> against password guessing; it does not limit a single
/// caller who spreads attempts across many accounts. Rate limiting <i>per caller</i> is the ASP.NET
/// Core rate limiter's job at the HTTP boundary, and the two are complementary — this one exists
/// because the HTTP limiter cannot see a login that arrives over an already-established Blazor
/// Server circuit. Keying on the email also means an attacker can deliberately lock a known account
/// out by failing five times; that is the accepted trade, and it is why the lockout is 15 minutes
/// rather than permanent and why <see cref="RegisterSuccess"/> clears the counter outright.</para>
///
/// <para><b>Behind a reverse proxy, caller-based limiting depends on host configuration.</b> The
/// per-caller half of the defence resolves a client address, and an address is only trustworthy if
/// the forwarded headers were vetted. The host configures that in
/// <c>TechieBlog.Middleware.ForwardedHeadersSetup</c> from the <c>ForwardedHeaders</c> section
/// (REQ-NFR-028), and it behaves in a way worth stating because the framework default is a real
/// footgun: <b>an enabled forwarded-headers middleware with EMPTY
/// <c>KnownProxies</c>/<c>KnownNetworks</c> lists trusts every caller</b> — the middleware only
/// consults the allow-list when it has at least one entry, so empty lists mean "accept
/// <c>X-Forwarded-For</c> from anyone", and any client can then mint a fresh rate-limit identity
/// per request by changing a header. Proved at runtime, not reasoned about. The setup code
/// therefore switches the middleware to <c>ForwardedHeaders.None</c> when both lists are
/// empty, so an unconfigured deployment falls back to the transport address rather than to
/// forgeable input. Never configure <c>0.0.0.0/0</c> to "make it work".</para>
///
/// <para><b>Scale-out note:</b> counters are per process — a two-instance deployment behind a load
/// balancer effectively doubles <see cref="MaxFailuresPerWindow"/>, because each instance counts
/// only the attempts it happened to receive. A multi-instance deployment should register a
/// distributed <see cref="ILoginThrottle"/> instead; nothing else has to change. The same caveat
/// applies to <see cref="ICaptchaRateLimiter"/>.</para>
///
/// <para><b>Usage:</b> Registered as a singleton in <c>BlogSvcInitializer</c> — a shorter lifetime
/// would reset the counters on every circuit and the throttle would count nothing.</para>
/// </remarks>
public class LoginThrottle : ILoginThrottle
{
    /// <summary>
    /// Failed attempts tolerated inside one window before the account is locked.
    /// </summary>
    public const int MaxFailuresPerWindow = 5;

    /// <summary>
    /// Length of the counting window, in minutes.
    /// </summary>
    public const int FailureWindowMinutes = 15;

    /// <summary>
    /// Length of the lockout applied once the limit is reached, in minutes.
    /// </summary>
    public const int LockoutMinutes = 15;

    private readonly ConcurrentDictionary<string, ThrottleEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTime> clock;

    /// <summary>
    /// Initialises the throttle with the system clock.
    /// </summary>
    public LoginThrottle()
        : this(() => DateTime.UtcNow)
    {
    }

    /// <summary>
    /// Initialises the throttle with an injectable clock so lockout expiry is testable.
    /// </summary>
    /// <param name="clock">Returns the current UTC time.</param>
    public LoginThrottle(Func<DateTime> clock)
    {
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Indicates whether the key is currently locked out.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A key is blocked while its lockout expiry lies in the future.
    /// Expired entries are dropped on read so the dictionary does not grow without bound.</para>
    /// <para><b>Flow:</b> normalise key → look up → compare expiry to the clock.</para>
    /// <para><b>Side Effects:</b> Removes the entry once its window and lockout have elapsed.</para>
    /// </remarks>
    /// <param name="key">The throttle key, normally the lowercased login email.</param>
    /// <param name="retryAfter">Receives the remaining lockout duration, or <see cref="TimeSpan.Zero"/>.</param>
    /// <returns><c>true</c> when the attempt must be refused.</returns>
    public bool IsBlocked(string key, out TimeSpan retryAfter)
    {
        retryAfter = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(key) || !entries.TryGetValue(key, out var entry))
            return false;

        var now = clock();
        if (entry.LockedUntil > now)
        {
            retryAfter = entry.LockedUntil - now;
            return true;
        }

        if (entry.WindowStart.AddMinutes(FailureWindowMinutes) <= now)
            entries.TryRemove(key, out _);

        return false;
    }

    /// <summary>
    /// Records a failed attempt and starts a lockout once the limit is reached.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Failures are counted inside a sliding window; a window older
    /// than <see cref="FailureWindowMinutes"/> restarts at one. Reaching
    /// <see cref="MaxFailuresPerWindow"/> sets a <see cref="LockoutMinutes"/> lockout.</para>
    /// <para><b>Flow:</b> normalise key → add or update the entry atomically.</para>
    /// <para><b>Side Effects:</b> Mutates the shared failure map.</para>
    /// </remarks>
    /// <param name="key">The throttle key, normally the lowercased login email.</param>
    /// <returns>The failure count inside the current window.</returns>
    public int RegisterFailure(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return 0;

        var now = clock();
        var updated = entries.AddOrUpdate(
            key,
            _ => new ThrottleEntry(now, 1, DateTime.MinValue),
            (_, existing) => Advance(existing, now));

        return updated.FailureCount;
    }

    /// <summary>
    /// Clears the counter for a key after a successful login.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A correct password proves the requester is not an attacker
    /// guessing, so the window is discarded outright.</para>
    /// <para><b>Flow:</b> normalise key → remove entry.</para>
    /// <para><b>Side Effects:</b> Mutates the shared failure map.</para>
    /// </remarks>
    /// <param name="key">The throttle key, normally the lowercased login email.</param>
    public void RegisterSuccess(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            entries.TryRemove(key, out _);
    }

    /// <summary>
    /// Produces the next state of a throttle entry after one more failure.
    /// </summary>
    /// <param name="existing">The current entry.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns>The replacement entry.</returns>
    private static ThrottleEntry Advance(ThrottleEntry existing, DateTime now)
    {
        if (existing.WindowStart.AddMinutes(FailureWindowMinutes) <= now && existing.LockedUntil <= now)
            return new ThrottleEntry(now, 1, DateTime.MinValue);

        var failureCount = existing.FailureCount + 1;
        var lockedUntil = failureCount >= MaxFailuresPerWindow
            ? now.AddMinutes(LockoutMinutes)
            : existing.LockedUntil;

        return new ThrottleEntry(existing.WindowStart, failureCount, lockedUntil);
    }

    /// <summary>
    /// Immutable per-key throttle state.
    /// </summary>
    /// <param name="WindowStart">When the current counting window began.</param>
    /// <param name="FailureCount">Failures recorded inside the window.</param>
    /// <param name="LockedUntil">When the current lockout ends; <see cref="DateTime.MinValue"/> when none.</param>
    private sealed record ThrottleEntry(DateTime WindowStart, int FailureCount, DateTime LockedUntil);
}
