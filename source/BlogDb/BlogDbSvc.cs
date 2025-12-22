using DbUp;

namespace BlogDb;

/// <summary>
/// Database migration service for TechieBlog using DbUp with PostgreSQL.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Manages database schema migrations using DbUp framework.
/// Executes SQL scripts from the PostgresScripts folder in sequence.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Called during application startup from Program.cs</item>
///   <item>Connects to PostgreSQL using provided connection string</item>
///   <item>Scans PostgresScripts folder for SQL migration files</item>
///   <item>Executes new scripts in alphanumeric order</item>
///   <item>Tracks executed scripts in SchemaVersions table</item>
/// </list>
///
/// <para><b>Dependencies:</b></para>
/// <list type="bullet">
///   <item>dbup-postgresql - DbUp PostgreSQL support</item>
///   <item>Npgsql - PostgreSQL .NET driver</item>
/// </list>
///
/// <para><b>Usage:</b> Called once at application startup to ensure database is up to date.</para>
/// </remarks>
public class BlogDbSvc
{
    /// <summary>
    /// Executes pending database migrations against PostgreSQL.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b></para>
    /// <list type="number">
    ///   <item>Creates DbUp upgrader configured for PostgreSQL</item>
    ///   <item>Scans PostgresScripts folder for .sql files</item>
    ///   <item>Executes any scripts not yet recorded in SchemaVersions</item>
    ///   <item>Logs results to console with color-coded status</item>
    /// </list>
    ///
    /// <para><b>Script Naming Convention:</b> Scripts should be named with numeric prefix
    /// for ordering: 001-CreateTables.sql, 002-CreateStoredFunctions.sql, etc.</para>
    /// </remarks>
    /// <param name="connectionString">PostgreSQL connection string from configuration.</param>
    /// <returns>True if all migrations succeeded, false if any errors occurred.</returns>
    public bool UpgradeDatabase(string connectionString)
    {
        var upgrader =
            DeployChanges.To
                .PostgresqlDatabase(connectionString)
                .WithScriptsFromFileSystem("PostgresScripts")
                .LogToConsole()
                .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(result.Error);
            Console.ResetColor();
            return false;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Database migration successful!");
        Console.ResetColor();
        return true;
    }
}
