using Dapper;
using Npgsql;

namespace TechieBlog.Tests.Integration;

/// <summary>
/// A throwaway PostgreSQL database, created for one test class and dropped after it.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the mail journey (REQ-NFR-045) run against the real schema without ever
/// touching the owner's database. The database is created on the existing local server (never a new
/// container), given a fresh <c>tbtest_…</c> name, migrated with every script under
/// <c>source/BlogDb/PostgresScripts</c>, and dropped on dispose.</para>
/// <para><b>Dependencies:</b> the environment value <c>TechieBlogTestServer</c>: a connection string to
/// the server only. Its <c>Database</c> part is ignored and replaced; a name that is not a
/// <c>tbtest_</c> name is never connected to except to create and drop the throwaway one. When the value
/// is absent the tests that need a database are skipped with that reason.</para>
/// </remarks>
public sealed class TempDatabase : IAsyncLifetime
{
    /// <summary>The environment value naming the server.</summary>
    public const string ServerSwitchName = "TechieBlogTestServer";

    private const string NamePrefix = "tbtest_";

    private string? adminConnectionString;

    /// <summary>Gets the throwaway database's connection string; null when skipped.</summary>
    public string? ConnectionString { get; private set; }

    /// <summary>Gets the throwaway database's name.</summary>
    public string DatabaseName { get; } = $"{NamePrefix}{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..40];

    /// <summary>Gets why the tests are skipped; null when the database is ready.</summary>
    public string? SkipReason { get; private set; }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable(ServerSwitchName);
        if (string.IsNullOrWhiteSpace(server))
        {
            SkipReason = $"Set {ServerSwitchName} to a server connection string to run the mail journey.";
            return;
        }

        var serverBuilder = new NpgsqlConnectionStringBuilder(server);
        if (string.Equals(serverBuilder.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            serverBuilder.Host = "127.0.0.1";

        server = serverBuilder.ConnectionString;
        adminConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = "postgres", Pooling = false }.ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"CREATE DATABASE \"{DatabaseName}\"");
        }

        ConnectionString = new NpgsqlConnectionStringBuilder(server) { Database = DatabaseName }.ConnectionString;
        await ApplyMigrationsAsync(ConnectionString);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (adminConnectionString == null || !DatabaseName.StartsWith(NamePrefix, StringComparison.Ordinal))
            return;

        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(adminConnectionString);
        await admin.OpenAsync();
        await admin.ExecuteAsync($"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }

    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        var folder = LocateScriptFolder() ?? throw new InvalidOperationException("source/BlogDb/PostgresScripts not found.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var script in Directory.GetFiles(folder, "*.sql").OrderBy(f => f, StringComparer.Ordinal))
        {
            var sql = await File.ReadAllTextAsync(script);
            if (!string.IsNullOrWhiteSpace(sql))
                await connection.ExecuteAsync(sql);
        }
    }

    private static string? LocateScriptFolder()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "source", "BlogDb", "PostgresScripts");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
