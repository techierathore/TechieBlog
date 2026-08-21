using BlogModels.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TechieBlog.HealthChecks;

/// <summary>
/// Readiness check confirming the critical singleton services resolved and work (REQ-NFR-014,
/// BRD-74).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A database probe alone does not prove the application is ready. If the
/// cache or the login throttle failed to construct, every request would fault at the first use.
/// This check exercises them once so a broken composition surfaces at <c>/health/ready</c> rather
/// than in a user's face.</para>
///
/// <para><b>Code Flow:</b> write and read a sentinel through <see cref="ICacheService"/>, then ask
/// <see cref="ILoginThrottle"/> about a key that is never used for a real account. Both are
/// side-effect free.</para>
///
/// <para><b>Dependencies:</b> <see cref="ICacheService"/> and <see cref="ILoginThrottle"/>,
/// registered by <c>BlogSvcInitializer</c>.</para>
///
/// <para><b>Usage:</b> Registered with the <c>ready</c> tag alongside
/// <see cref="DatabaseHealthCheck"/>.</para>
/// </remarks>
public class CriticalServicesHealthCheck : IHealthCheck
{
    /// <summary>
    /// Cache key used for the round-trip sentinel; never collides with real data.
    /// </summary>
    private const string ProbeCacheKey = "HealthCheck:CacheProbe";

    /// <summary>
    /// Throttle key used for the probe; not a valid email address, so no account can own it.
    /// </summary>
    private const string ProbeThrottleKey = "health-check-probe";

    private readonly ICacheService cacheService;
    private readonly ILoginThrottle loginThrottle;

    /// <summary>
    /// Initialises the health check.
    /// </summary>
    /// <param name="cacheService">The application cache.</param>
    /// <param name="loginThrottle">The failed-login throttle.</param>
    public CriticalServicesHealthCheck(ICacheService cacheService, ILoginThrottle loginThrottle)
    {
        this.cacheService = cacheService;
        this.loginThrottle = loginThrottle;
    }

    /// <summary>
    /// Exercises the critical singletons and reports whether they behave.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The cache must return the value it was given, and the throttle
    /// must answer for an unknown key without blocking. Anything else — including an exception —
    /// is reported as unhealthy so the instance is taken out of rotation.</para>
    /// <para><b>Flow:</b> cache round trip → throttle query → evict the sentinel → verdict.</para>
    /// <para><b>Side Effects:</b> Writes and removes one cache entry.</para>
    /// </remarks>
    /// <param name="context">Health-check registration context.</param>
    /// <param name="cancellationToken">Cancellation token supplied by the middleware.</param>
    /// <returns>The health result for the critical service set.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sentinel = Guid.NewGuid().ToString("N");
            var roundTripped = cacheService.GetOrCreate(
                ProbeCacheKey, CacheTags.Settings, () => sentinel, TimeSpan.FromSeconds(5));
            cacheService.Evict(ProbeCacheKey);

            if (!string.Equals(roundTripped, sentinel, StringComparison.Ordinal))
                return Task.FromResult(HealthCheckResult.Unhealthy("Cache did not return the stored value."));

            if (loginThrottle.IsBlocked(ProbeThrottleKey, out _))
                return Task.FromResult(HealthCheckResult.Degraded("Login throttle reports the probe key as blocked."));

            return Task.FromResult(HealthCheckResult.Healthy("Cache and login throttle are responding."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Critical services check failed.", ex));
        }
    }
}
