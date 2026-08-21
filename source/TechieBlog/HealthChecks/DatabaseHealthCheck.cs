using BlogEngine.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TechieBlog.HealthChecks;

/// <summary>
/// Readiness check confirming PostgreSQL is reachable and answering (REQ-NFR-014, BRD-74).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The application is useless without its database — every page reads from
/// it — so <c>/health/ready</c> must fail while PostgreSQL is unavailable and a load balancer must
/// stop sending traffic. This check is the readiness half of that contract.</para>
///
/// <para><b>Code Flow:</b> the health-check middleware invokes this → it delegates to
/// <see cref="DatabaseHealthProbe"/> in the engine → the probe's <c>Result</c> is translated into
/// a healthy or unhealthy verdict carrying the round-trip time.</para>
///
/// <para><b>Dependencies:</b> <see cref="DatabaseHealthProbe"/>, registered by
/// <c>BlogSvcInitializer</c> with the <c>AppDbConString</c> connection string.</para>
///
/// <para><b>Usage:</b> Registered with the <c>ready</c> tag so <c>/health</c> (liveness) stays
/// green while <c>/health/ready</c> reflects dependency state.</para>
/// </remarks>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly DatabaseHealthProbe probe;

    /// <summary>
    /// Initialises the health check.
    /// </summary>
    /// <param name="probe">The engine-side database probe.</param>
    public DatabaseHealthCheck(DatabaseHealthProbe probe)
    {
        this.probe = probe;
    }

    /// <summary>
    /// Runs the database probe and maps its result to a health status.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A successful probe reports the round-trip time as check data
    /// so a slow-but-alive database is visible before it becomes an outage. The probe never
    /// throws, so no exception handling is needed here.</para>
    /// <para><b>Flow:</b> probe → map success to Healthy with timing, failure to Unhealthy with
    /// the reason.</para>
    /// <para><b>Side Effects:</b> Opens one pooled database connection.</para>
    /// </remarks>
    /// <param name="context">Health-check registration context.</param>
    /// <param name="cancellationToken">Cancellation token supplied by the middleware.</param>
    /// <returns>The health result for the database dependency.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = probe.Check();
        if (!result.IsSuccess)
            return Task.FromResult(HealthCheckResult.Unhealthy(result.ErrorMessage));

        var data = new Dictionary<string, object> { ["roundTripMs"] = result.Data };
        return Task.FromResult(HealthCheckResult.Healthy("PostgreSQL responded to SELECT 1", data));
    }
}
