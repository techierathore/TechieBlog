using BlogModels;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BlogApp.Services;

/// <summary>
/// Read-only reachability probe for the site's PostgreSQL server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the connection-setup screen's "Test connection" action (REQ-FN-047).
/// The acceptance criterion is that an invalid connection string is refused with a clear error, so
/// this class translates Npgsql's low-level failures into a sentence an operator can act on.</para>
/// <para><b>Code Flow:</b> setup screen → <see cref="TestAsync"/> → open a connection → confirm the
/// TechieBlog schema is present → report the server version.</para>
/// <para><b>Dependencies:</b> <see cref="NpgsqlConnection"/>,
/// <see cref="ILogger{TCategoryName}"/>.</para>
/// <para><b>Usage:</b> Registered transient; the probe never writes, so it is safe to run against a
/// production blog database.</para>
/// </remarks>
public class ConnectionProbe
{
    /// <summary>Seconds the probe waits for a connection before giving up.</summary>
    private const int ProbeTimeoutSeconds = 10;

    /// <summary>
    /// A table every migrated TechieBlog database has, used to prove the schema is the right one.
    /// </summary>
    private const string SchemaSentinelTable = "bloguser";

    private readonly ILogger<ConnectionProbe> logger;

    /// <summary>
    /// Creates the probe.
    /// </summary>
    /// <param name="logger">Structured logger for probe outcomes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <c>null</c>.</exception>
    public ConnectionProbe(ILogger<ConnectionProbe> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Opens a short-lived connection and verifies that it reaches a TechieBlog database.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Three outcomes matter to the operator and are reported
    /// distinctly: the settings are incomplete, the server is unreachable or refused the
    /// credentials, and the server answered but is not a TechieBlog database. Only the third case
    /// needs a query — the first two fail while opening. BlogApp never runs DbUp migrations
    /// (the web host owns them), so a database missing the schema is an error, not something to
    /// fix in place.</para>
    /// <para><b>Flow:</b> validate → open with a bounded timeout → count the sentinel table →
    /// build the success message from the server version.</para>
    /// <para><b>Side Effects:</b> Opens and closes one PostgreSQL connection. Writes nothing.</para>
    /// </remarks>
    /// <param name="settings">The connection parameters entered on the setup screen.</param>
    /// <returns>
    /// A success result carrying a short description of the server, or a failure result whose
    /// message is safe to render directly in the UI.
    /// </returns>
    public async Task<Result<string>> TestAsync(ConnectionSettings settings)
    {
        if (settings == null || !settings.IsComplete())
        {
            return Result<string>.Failure("Enter a host, port, database and username before testing the connection.");
        }

        var builder = new NpgsqlConnectionStringBuilder(settings.ToConnectionString())
        {
            Timeout = ProbeTimeoutSeconds,
            CommandTimeout = ProbeTimeoutSeconds
        };

        try
        {
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync().ConfigureAwait(false);

            await using var command = new NpgsqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE LOWER(table_name) = @sentinel",
                connection);
            command.Parameters.AddWithValue("sentinel", SchemaSentinelTable);

            var tableCount = Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false));
            if (tableCount == 0)
            {
                logger.LogWarning("Connection probe reached {Server} but found no TechieBlog schema", settings.ToDisplayLabel());
                return Result<string>.Failure(
                    $"Connected to '{settings.Database}', but it does not contain the TechieBlog schema. " +
                    "Point BlogApp at the database the website already migrated.");
            }

            var message = $"Connection OK - TechieBlog schema found on PostgreSQL {connection.PostgreSqlVersion}.";
            logger.LogInformation("Connection probe succeeded against {Server}", settings.ToDisplayLabel());
            return Result<string>.Success(message);
        }
        catch (NpgsqlException ex)
        {
            logger.LogWarning(ex, "Connection probe failed against {Server}", settings.ToDisplayLabel());
            return Result<string>.Failure($"Could not connect: {ex.Message}");
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Connection probe timed out against {Server}", settings.ToDisplayLabel());
            return Result<string>.Failure(
                $"The server did not answer within {ProbeTimeoutSeconds} seconds. Check the host, the port and any firewall.");
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Connection probe rejected the supplied settings");
            return Result<string>.Failure($"The connection settings are not valid: {ex.Message}");
        }
    }
}
