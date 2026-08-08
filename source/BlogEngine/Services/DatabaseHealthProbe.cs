using BlogEngine.DaCore;
using BlogModels;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BlogEngine.Services;

/// <summary>
/// Verifies that PostgreSQL is reachable and answering queries (REQ-NFR-014, BRD-74).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the host's <c>/health/ready</c> readiness endpoint. The probe
/// lives in the engine rather than the host so it can be unit-tested without booting ASP.NET
/// Core, and so any future head (CLI, worker) reuses the same definition of "the database is
/// up".</para>
///
/// <para><b>Code Flow:</b> open a connection through <see cref="DbConnectionFactory"/> → run
/// <c>SELECT 1</c> → return a <see cref="Result{T}"/> carrying the round-trip time.</para>
///
/// <para><b>Dependencies:</b> <see cref="DbConnectionFactory"/> (Npgsql) and
/// <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>What a green probe proves:</b> the connection string is present, a socket to
/// PostgreSQL opened, authentication succeeded, and the server executed a statement and returned a
/// row. That is enough to distinguish "the database is down or we cannot log in" from "the
/// application is misbehaving", which is the question a readiness endpoint exists to answer.</para>
///
/// <para><b>What it does NOT prove — read this before treating green as healthy:</b></para>
/// <list type="bullet">
///   <item><b>Nothing about the schema.</b> <c>SELECT 1</c> touches no table. A database whose
///   DbUp migrations failed, or which is empty, probes green while every page 500s.</item>
///   <item><b>Nothing about the right database.</b> The statement would succeed against any
///   PostgreSQL instance the string points at, including the wrong environment's.</item>
///   <item><b>Nothing about capacity.</b> The connection comes from Npgsql's pool, so a warm pool
///   answers without a real handshake; an exhausted pool, on the other hand, makes this probe
///   block on the same contention the application is suffering, which is intended.</item>
///   <item><b>Nothing about write availability.</b> A read-only replica or a full disk answers
///   <c>SELECT 1</c> perfectly.</item>
/// </list>
///
/// <para><b>Usage:</b> Registered in <c>BlogSvcInitializer</c> with the application connection
/// string and wrapped by the host's <c>DatabaseHealthCheck</c>. The returned round-trip time is
/// reported as health-check data; it is a single sample and includes pool acquisition, so treat it
/// as a smoke signal rather than a latency measurement.</para>
///
/// <para><b>Synchronous by design.</b> <see cref="Check"/> blocks. It is called from the health
/// endpoint, not from a Blazor circuit, and it is the one place a blocking database call is
/// acceptable — but that is why it must not be reused from page code.</para>
/// </remarks>
public class DatabaseHealthProbe
{
    private readonly string connectionString;
    private readonly ILogger<DatabaseHealthProbe> logger;

    /// <summary>
    /// Initialises the probe.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    /// <param name="logger">Logger used to record probe failures.</param>
    public DatabaseHealthProbe(string connectionString, ILogger<DatabaseHealthProbe> logger)
    {
        this.connectionString = connectionString;
        this.logger = logger;
    }

    /// <summary>
    /// Runs a lightweight query to confirm the database answers.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A socket that opens but never answers is still unhealthy, so
    /// the probe issues a real <c>SELECT 1</c> rather than only connecting. Every failure mode —
    /// missing configuration, refused connection, authentication error — is converted into a
    /// failed <see cref="Result{T}"/>, because a readiness endpoint must never throw.</para>
    /// <para><b>Flow:</b> guard the connection string → start stopwatch → open connection →
    /// execute scalar → return the elapsed milliseconds, or the failure reason.</para>
    /// <para><b>Side Effects:</b> Opens and closes one pooled connection; logs a warning when the
    /// probe fails.</para>
    /// </remarks>
    /// <returns>A success result carrying the round-trip time in milliseconds, or a failure.</returns>
    public Result<long> Check()
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return Result<long>.Failure("Database connection string 'AppDbConString' is not configured.");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var connection = DbConnectionFactory.GetDbConnection(EDbConnectionTypes.PostgreSql, connectionString);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
            stopwatch.Stop();
            return Result<long>.Success(stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Database health probe failed after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            return Result<long>.Failure($"Database unreachable: {ex.Message}");
        }
    }
}
