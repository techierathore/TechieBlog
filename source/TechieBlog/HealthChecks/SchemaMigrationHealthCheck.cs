using BlogEngine.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TechieBlog.HealthChecks;

/// <summary>
/// Readiness check asserting that DbUp's migrations actually applied (REQ-NFR-039).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Turns a failed migration into a red deploy. <see cref="DatabaseHealthCheck"/>
/// answers "can we reach PostgreSQL"; this one answers "is the schema the one this build expects".
/// Those are different questions, and conflating them is what let a deploy report success while
/// shipping an empty site: DbUp throws when the application role lacks DDL rights, <c>Program.cs</c>
/// logs a warning, the host comes up anyway, <c>SELECT 1</c> succeeds, and <c>/healthz</c> returns
/// 200.</para>
///
/// <para><b>Code Flow:</b> the health-check middleware invokes this → it delegates to
/// <see cref="SchemaMigrationProbe"/> in the engine → the probe's <c>Result</c> becomes a healthy or
/// unhealthy verdict.</para>
///
/// <para><b>Dependencies:</b> <see cref="SchemaMigrationProbe"/>, registered in <c>Program.cs</c>
/// with the same connection string and the same resolved scripts path handed to
/// <c>BlogDbSvc.UpgradeDatabase</c>.</para>
///
/// <para><b>Unhealthy, never Degraded.</b> A partly-migrated schema is not a reduced service — the
/// pages that need the missing objects will fail outright — and only <c>Unhealthy</c> makes
/// <c>/healthz</c> answer 503, which is what the pipeline's <c>verify</c> job reads. Reporting
/// <c>Degraded</c> here would keep the endpoint at 200 and preserve the exact defect this
/// requirement exists to close.</para>
///
/// <para><b>Usage:</b> Registered with the <c>ready</c> tag, so it appears on <c>/health/ready</c>
/// and on <c>/healthz</c> — the URL the deployment pipeline curls after every push — while
/// <c>/health</c> (liveness) stays green, since a process with a bad schema is running fine and
/// restarting it will not help.</para>
/// </remarks>
public class SchemaMigrationHealthCheck : IHealthCheck
{
    private readonly SchemaMigrationProbe probe;

    /// <summary>
    /// Initialises the health check.
    /// </summary>
    /// <param name="probe">The engine-side migration journal probe.</param>
    public SchemaMigrationHealthCheck(SchemaMigrationProbe probe)
    {
        this.probe = probe;
    }

    /// <summary>
    /// Runs the migration probe and maps its result to a health status.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The probe's failure message is passed through verbatim as the
    /// check description, because it is written for the operator reading the health payload
    /// mid-deploy and already names whether the journal is absent or behind and which scripts are
    /// outstanding. Migration script file names are not sensitive, so unlike the database
    /// connection failure (REQ-NFR-033) this detail is deliberately published.</para>
    /// <para><b>Flow:</b> probe → map success to Healthy with the journalled count, failure to
    /// Unhealthy with the reason.</para>
    /// <para><b>Side Effects:</b> Opens one pooled database connection.</para>
    /// </remarks>
    /// <param name="context">Health-check registration context.</param>
    /// <param name="cancellationToken">Cancellation token supplied by the middleware.</param>
    /// <returns>The health result for the applied schema.</returns>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = probe.Check();
        if (!result.IsSuccess)
            return Task.FromResult(HealthCheckResult.Unhealthy(result.ErrorMessage));

        var data = new Dictionary<string, object> { ["appliedScripts"] = result.Data };
        return Task.FromResult(HealthCheckResult.Healthy(
            $"DbUp journal records all {result.Data} migration scripts", data));
    }
}
