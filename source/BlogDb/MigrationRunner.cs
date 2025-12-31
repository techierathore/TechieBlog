namespace BlogDb;

/// <summary>
/// Command-line interface for running database migrations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a CLI entry point for executing schema and data migrations
/// from MySQL to PostgreSQL.</para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// # Run schema migration only
/// dotnet run -- schema --postgres "Host=localhost;Database=techieblog;..."
///
/// # Run data migration only
/// dotnet run -- data --mysql "..." --postgres "..."
///
/// # Run full migration (schema + data + verify)
/// dotnet run -- full --mysql "..." --postgres "..."
///
/// # Verify migration
/// dotnet run -- verify --mysql "..." --postgres "..."
/// </code>
/// </remarks>
public class MigrationRunner
{
    /// <summary>
    /// Main entry point for the migration CLI.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine();
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("  TechieBlog Database Migration Tool");
        Console.WriteLine("  MySQL -> PostgreSQL");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLower();
        var mysqlConn = GetArgValue(args, "--mysql");
        var postgresConn = GetArgValue(args, "--postgres") ?? GetArgValue(args, "--connection");

        try
        {
            return command switch
            {
                "schema" => RunSchemaOnly(postgresConn),
                "data" => await RunDataOnly(mysqlConn, postgresConn),
                "verify" => await RunVerifyOnly(mysqlConn, postgresConn),
                "full" => await RunFullMigration(mysqlConn, postgresConn),
                "counts" => await RunCounts(mysqlConn, postgresConn),
                "discover" => await RunDiscover(mysqlConn),
                _ => ShowInvalidCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nFATAL ERROR: {ex.Message}");
            Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// Runs schema migration only using DbUp.
    /// </summary>
    private static int RunSchemaOnly(string postgresConn)
    {
        if (string.IsNullOrEmpty(postgresConn))
        {
            Console.WriteLine("ERROR: --postgres connection string is required for schema migration");
            return 1;
        }

        Console.WriteLine("Running schema migration (DbUp)...\n");

        var dbSvc = new BlogDbSvc();
        var success = dbSvc.UpgradeDatabase(postgresConn);

        return success ? 0 : 1;
    }

    /// <summary>
    /// Runs data migration only from MySQL to PostgreSQL.
    /// </summary>
    private static async Task<int> RunDataOnly(string mysqlConn, string postgresConn)
    {
        if (string.IsNullOrEmpty(mysqlConn) || string.IsNullOrEmpty(postgresConn))
        {
            Console.WriteLine("ERROR: Both --mysql and --postgres connection strings are required for data migration");
            return 1;
        }

        Console.WriteLine("Running data migration (MySQL -> PostgreSQL)...\n");

        var migrator = new DataMigrationUtility(mysqlConn, postgresConn);

        // Validate connections first
        if (!await migrator.ValidateConnectionsAsync())
        {
            Console.WriteLine("\nConnection validation failed. Aborting migration.");
            return 1;
        }

        Console.WriteLine();

        // Run migration
        var result = await migrator.MigrateAllDataAsync();

        return result.Success ? 0 : 1;
    }

    /// <summary>
    /// Runs verification only, comparing row counts between databases.
    /// </summary>
    private static async Task<int> RunVerifyOnly(string mysqlConn, string postgresConn)
    {
        if (string.IsNullOrEmpty(mysqlConn) || string.IsNullOrEmpty(postgresConn))
        {
            Console.WriteLine("ERROR: Both --mysql and --postgres connection strings are required for verification");
            return 1;
        }

        Console.WriteLine("Running migration verification...\n");

        var migrator = new DataMigrationUtility(mysqlConn, postgresConn);

        // Validate connections first
        if (!await migrator.ValidateConnectionsAsync())
        {
            Console.WriteLine("\nConnection validation failed.");
            return 1;
        }

        // Run verification
        var success = await migrator.VerifyMigrationAsync();

        return success ? 0 : 1;
    }

    /// <summary>
    /// Runs full migration: schema + data + verification.
    /// </summary>
    private static async Task<int> RunFullMigration(string mysqlConn, string postgresConn)
    {
        if (string.IsNullOrEmpty(mysqlConn) || string.IsNullOrEmpty(postgresConn))
        {
            Console.WriteLine("ERROR: Both --mysql and --postgres connection strings are required for full migration");
            return 1;
        }

        Console.WriteLine("Running FULL migration (schema + data + verify)...\n");

        // Step 1: Schema migration
        Console.WriteLine("=" .PadRight(60, '='));
        Console.WriteLine("STEP 1: Schema Migration");
        Console.WriteLine("=".PadRight(60, '='));

        var schemaResult = RunSchemaOnly(postgresConn);
        if (schemaResult != 0)
        {
            Console.WriteLine("\nSchema migration failed. Aborting.");
            return 1;
        }

        Console.WriteLine();

        // Step 2: Data migration
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("STEP 2: Data Migration");
        Console.WriteLine("=".PadRight(60, '='));

        var dataResult = await RunDataOnly(mysqlConn, postgresConn);
        if (dataResult != 0)
        {
            Console.WriteLine("\nData migration failed.");
            // Continue to verification anyway
        }

        Console.WriteLine();

        // Step 3: Verification
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("STEP 3: Verification");
        Console.WriteLine("=".PadRight(60, '='));

        var verifyResult = await RunVerifyOnly(mysqlConn, postgresConn);

        Console.WriteLine();
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("MIGRATION COMPLETE");
        Console.WriteLine("=".PadRight(60, '='));

        if (schemaResult == 0 && dataResult == 0 && verifyResult == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("\nAll steps completed successfully!");
            Console.ResetColor();
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nMigration completed with warnings. Please review the output above.");
            Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// Shows row counts from both databases without migrating.
    /// </summary>
    private static async Task<int> RunCounts(string mysqlConn, string postgresConn)
    {
        Console.WriteLine("Getting row counts from databases...\n");

        var migrator = new DataMigrationUtility(
            mysqlConn ?? "",
            postgresConn ?? ""
        );

        if (!string.IsNullOrEmpty(mysqlConn))
        {
            Console.WriteLine("MySQL Row Counts:");
            Console.WriteLine("-".PadRight(40, '-'));
            try
            {
                var counts = await migrator.GetMySqlRowCountsAsync();
                foreach (var (table, count) in counts.OrderBy(x => x.Key))
                {
                    Console.WriteLine($"  {table,-25} {(count < 0 ? "N/A" : count.ToString()),10}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
            Console.WriteLine();
        }

        if (!string.IsNullOrEmpty(postgresConn))
        {
            Console.WriteLine("PostgreSQL Row Counts:");
            Console.WriteLine("-".PadRight(40, '-'));
            try
            {
                var counts = await migrator.GetPostgresRowCountsAsync();
                foreach (var (table, count) in counts.OrderBy(x => x.Key))
                {
                    Console.WriteLine($"  {table,-25} {(count < 0 ? "N/A" : count.ToString()),10}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error: {ex.Message}");
            }
        }

        return 0;
    }

    /// <summary>
    /// Discovers and displays MySQL database structure.
    /// </summary>
    private static async Task<int> RunDiscover(string mysqlConn)
    {
        if (string.IsNullOrEmpty(mysqlConn))
        {
            Console.WriteLine("ERROR: --mysql connection string is required");
            return 1;
        }

        Console.WriteLine("Discovering MySQL database structure...\n");

        using var conn = new MySql.Data.MySqlClient.MySqlConnection(mysqlConn);
        await conn.OpenAsync();

        // Get all tables
        var tables = new List<string>();
        using (var cmd = new MySql.Data.MySqlClient.MySqlCommand(
            "SELECT TABLE_NAME FROM information_schema.tables WHERE table_schema = DATABASE()", conn))
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));
        }

        Console.WriteLine($"Found {tables.Count} tables:\n");

        foreach (var table in tables.OrderBy(t => t))
        {
            Console.WriteLine($"=== {table} ===");

            // Get columns
            using (var colCmd = new MySql.Data.MySqlClient.MySqlCommand(
                $"SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_KEY FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '{table}' ORDER BY ORDINAL_POSITION", conn))
            using (var colReader = await colCmd.ExecuteReaderAsync())
            {
                while (await colReader.ReadAsync())
                {
                    var name = colReader.GetString(0);
                    var type = colReader.GetString(1);
                    var nullable = colReader.GetString(2) == "YES" ? "NULL" : "NOT NULL";
                    var key = colReader.GetString(3);
                    var keyMarker = key == "PRI" ? " [PK]" : (key == "MUL" ? " [FK]" : "");

                    Console.WriteLine($"  {name,-25} {type,-15} {nullable}{keyMarker}");
                }
            }

            // Get row count (after closing column reader)
            using (var countCmd = new MySql.Data.MySqlClient.MySqlCommand($"SELECT COUNT(*) FROM `{table}`", conn))
            {
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                Console.WriteLine($"  --- Rows: {count}");
            }
            Console.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// Shows help information.
    /// </summary>
    private static void ShowHelp()
    {
        Console.WriteLine("Usage: dotnet run -- <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  discover  Show MySQL database tables and columns");
        Console.WriteLine("  schema    Run PostgreSQL schema migration only (DbUp)");
        Console.WriteLine("  data      Run data migration only (MySQL -> PostgreSQL)");
        Console.WriteLine("  verify    Verify migration by comparing row counts");
        Console.WriteLine("  full      Run full migration (schema + data + verify)");
        Console.WriteLine("  counts    Show row counts from databases (no migration)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --mysql <conn>      MySQL connection string (source)");
        Console.WriteLine("  --postgres <conn>   PostgreSQL connection string (target)");
        Console.WriteLine("  --help, -h          Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine();
        Console.WriteLine("  # Run schema migration only");
        Console.WriteLine("  dotnet run -- schema --postgres \"Host=localhost;Database=techieblog;Username=postgres;Password=secret\"");
        Console.WriteLine();
        Console.WriteLine("  # Run data migration");
        Console.WriteLine("  dotnet run -- data \\");
        Console.WriteLine("    --mysql \"Server=localhost;Database=TechieBlog;Uid=root;Pwd=secret\" \\");
        Console.WriteLine("    --postgres \"Host=localhost;Database=techieblog;Username=postgres;Password=secret\"");
        Console.WriteLine();
        Console.WriteLine("  # Run full migration");
        Console.WriteLine("  dotnet run -- full \\");
        Console.WriteLine("    --mysql \"Server=localhost;Database=TechieBlog;Uid=root;Pwd=secret\" \\");
        Console.WriteLine("    --postgres \"Host=localhost;Database=techieblog;Username=postgres;Password=secret\"");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - Schema migration must be run before data migration");
        Console.WriteLine("  - The 'full' command runs both in the correct order");
        Console.WriteLine("  - PostgreSQL database must exist before running migrations");
        Console.WriteLine("  - MySQL database is only read from, never modified");
    }

    /// <summary>
    /// Shows error for invalid command.
    /// </summary>
    private static int ShowInvalidCommand(string command)
    {
        Console.WriteLine($"ERROR: Unknown command '{command}'");
        Console.WriteLine("Use --help to see available commands.");
        return 1;
    }

    /// <summary>
    /// Gets a command-line argument value.
    /// </summary>
    private static string GetArgValue(string[] args, string argName)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
