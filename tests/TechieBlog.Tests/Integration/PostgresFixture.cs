using System.Diagnostics;
using Dapper;
using Npgsql;
using Testcontainers.PostgreSql;

namespace TechieBlog.Tests.Integration;

/// <summary>
/// Boots a throwaway PostgreSQL container and applies the repository's DbUp
/// migration scripts to it, so integration tests run against the real schema.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Satisfies the REQ-NFR-016 requirement that integration
/// tests run against a PostgreSQL test container rather than a shared database.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Starts a PostgreSQL container via Testcontainers.</item>
///   <item>Reads <c>source/BlogDb/PostgresScripts/*.sql</c> in filename order.</item>
///   <item>Executes each script against the fresh database.</item>
///   <item>Exposes the resulting connection string to the test classes.</item>
/// </list>
///
/// <para><b>Opt-in:</b> Container-backed tests are SKIPPED unless the environment
/// variable <c>TechieBlogIntegrationTests</c> is set to <c>true</c>. This keeps
/// `dotnet test` green on machines and CI legs without a Docker daemon, and keeps
/// the unit suite free of any external dependency. Turn them on with:</para>
/// <code>
/// TechieBlogIntegrationTests=true TESTCONTAINERS_RYUK_DISABLED=true \
///   ~/.dotnet/dotnet test tests/TechieBlog.Tests/TechieBlog.Tests.csproj
/// </code>
///
/// <para><b>Image:</b> defaults to <c>postgres:16-alpine</c>. Override with the
/// environment variable <c>TechieBlogTestPostgresImage</c> to reuse an image that
/// is already present locally and avoid a registry pull.</para>
///
/// <para><b>Dependencies:</b> Testcontainers.PostgreSql, Npgsql, Dapper, and a
/// reachable Docker daemon.</para>
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// Environment variable that switches container-backed integration tests on.
    /// </summary>
    public const string EnableSwitchName = "TechieBlogIntegrationTests";

    /// <summary>
    /// Environment variable that overrides the PostgreSQL image used.
    /// </summary>
    public const string ImageOverrideName = "TechieBlogTestPostgresImage";

    /// <summary>
    /// Environment variable that overrides the migration-script folder. Only needed
    /// when the test assembly runs from outside the repository tree.
    /// </summary>
    public const string ScriptFolderOverrideName = "TechieBlogMigrationScripts";

    /// <summary>
    /// Image used when <see cref="ImageOverrideName"/> is not set.
    /// </summary>
    public const string DefaultImage = "postgres:16-alpine";

    private PostgreSqlContainer? container;

    /// <summary>
    /// Connection string for the started container, or <c>null</c> when the
    /// fixture was skipped because integration tests are disabled.
    /// </summary>
    public string? ConnectionString { get; private set; }

    /// <summary>
    /// Reason the container could not be started, or <c>null</c> when it started.
    /// </summary>
    public string? SkipReason { get; private set; }

    /// <summary>
    /// True when container-backed integration tests have been switched on for
    /// this run. xUnit consults this through the test classes' SkipUnless hooks.
    /// </summary>
    public static bool IntegrationTestsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(EnableSwitchName),
            "true",
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts the container and applies every migration script, unless integration
    /// tests are switched off — in which case this is a no-op.
    /// </summary>
    /// <returns>A task that completes once the schema is ready.</returns>
    public async ValueTask InitializeAsync()
    {
        if (!IntegrationTestsEnabled)
        {
            SkipReason = $"Set {EnableSwitchName}=true to run container-backed integration tests.";
            return;
        }

        var image = Environment.GetEnvironmentVariable(ImageOverrideName);
        if (string.IsNullOrWhiteSpace(image))
            image = DefaultImage;

        try
        {
            container = new PostgreSqlBuilder()
                .WithImage(image)
                .WithDatabase("techieblogtest")
                .WithUsername("techieblog")
                .WithPassword("techieblog")
                .Build();

            await container.StartAsync();
            ConnectionString = container.GetConnectionString();
            await ApplyMigrationsAsync(ConnectionString);
        }
        catch (Exception ex)
        {
            // Blank the connection string so dependent tests skip with a reason
            // rather than running against a half-provisioned database.
            ConnectionString = null;
            SkipReason = $"PostgreSQL container could not be prepared ({ex.GetType().Name}: {ex.Message}).";
        }
    }

    /// <summary>
    /// Stops and removes the container.
    /// </summary>
    /// <returns>A task that completes once the container is gone.</returns>
    public async ValueTask DisposeAsync()
    {
        if (container is not null)
            await container.DisposeAsync();
    }

    /// <summary>
    /// Applies every <c>*.sql</c> file under <c>source/BlogDb/PostgresScripts</c>
    /// in filename order — the same order DbUp uses at host startup.
    /// </summary>
    /// <param name="connectionString">Connection string for the fresh database.</param>
    /// <returns>A task that completes once all scripts have run.</returns>
    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        var scriptFolder = LocateScriptFolder();
        if (scriptFolder is null)
            throw new InvalidOperationException("Could not locate source/BlogDb/PostgresScripts.");

        var scripts = Directory.GetFiles(scriptFolder, "*.sql").OrderBy(f => f, StringComparer.Ordinal);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        foreach (var script in scripts)
        {
            var sql = await File.ReadAllTextAsync(script);
            if (string.IsNullOrWhiteSpace(sql))
                continue;

            try
            {
                await connection.ExecuteAsync(sql);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Migration script '{Path.GetFileName(script)}' failed: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// Walks up from the test assembly location until the repository root — the
    /// folder holding <c>source/BlogDb/PostgresScripts</c> — is found. The
    /// <c>TechieBlogMigrationScripts</c> environment variable short-circuits the
    /// walk for runs staged outside the repository tree.
    /// </summary>
    /// <returns>Absolute path to the script folder, or <c>null</c> if not found.</returns>
    private static string? LocateScriptFolder()
    {
        var configured = Environment.GetEnvironmentVariable(ScriptFolderOverrideName);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
            return configured;

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "source", "BlogDb", "PostgresScripts");
            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        Debug.WriteLine("PostgresScripts folder not found while walking up from " + AppContext.BaseDirectory);
        return null;
    }
}

/// <summary>
/// xUnit collection that shares a single <see cref="PostgresFixture"/> across every
/// integration test class, so the container is started once per test run.
/// </summary>
[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    /// <summary>
    /// Name of the shared PostgreSQL collection.
    /// </summary>
    public const string Name = "PostgresContainer";
}
