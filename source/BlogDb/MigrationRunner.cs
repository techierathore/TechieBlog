namespace BlogDb;

/// <summary>
/// Command-line entry point for applying the PostgreSQL schema migrations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a standalone CLI head for running the DbUp migration set against a
/// PostgreSQL database, for operators who need to upgrade a database without booting the web host.
/// The <c>TechieBlog</c> host runs the very same <see cref="BlogDbSvc.UpgradeDatabase"/> call at
/// startup, so this tool exists for out-of-band and CI use rather than for normal operation.</para>
///
/// <para><b>Code Flow:</b> <see cref="Main"/> parses the command word and the <c>--postgres</c>
/// connection-string option, dispatches to <see cref="RunSchema"/>, and maps success onto process
/// exit code 0 and any failure onto exit code 1.</para>
///
/// <para><b>Dependencies:</b> <see cref="BlogDbSvc"/> (which owns the DbUp engine and the embedded
/// <c>PostgresScripts</c> set).</para>
///
/// <para><b>Usage:</b></para>
/// <code>
/// # Apply all pending PostgreSQL migrations
/// dotnet run -- schema --postgres "Host=localhost;Database=TechieBlog;Username=…;Password=…"
/// </code>
///
/// <para><b>History:</b> This tool previously also carried <c>data</c>, <c>verify</c>, <c>full</c>,
/// <c>counts</c> and <c>discover</c> commands that copied rows out of the retired MySQL database via
/// <c>DataMigrationUtility</c>. That migration completed and PostgreSQL is now the system of record,
/// so the MySQL commands, the utility, the <c>MySqlScripts/</c> folder and the <c>MySql.Data</c>
/// package reference were all removed under REQ-NFR-020.</para>
/// </remarks>
public class MigrationRunner
{
    /// <summary>
    /// Process entry point for the migration CLI.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Treats a missing command, <c>--help</c> or <c>-h</c> as a request
    /// for usage text (exit 0); treats <c>schema</c> as the single supported migration command; and
    /// treats anything else as an operator error (exit 1).</para>
    ///
    /// <para><b>Flow:</b> Print banner → short-circuit on help → read <c>--postgres</c> (accepting
    /// <c>--connection</c> as an alias) → dispatch → convert the boolean outcome to an exit code.
    /// Any escaping exception is caught here so the operator sees a readable message rather than an
    /// unhandled-exception dump.</para>
    ///
    /// <para><b>Side Effects:</b> Writes to standard output and, via <see cref="RunSchema"/>, applies
    /// pending DDL to the target PostgreSQL database.</para>
    /// </remarks>
    /// <param name="args">Raw command-line arguments: a command word followed by option pairs.</param>
    /// <returns>0 when the requested operation succeeded; 1 on any validation or migration failure.</returns>
    public static int Main(string[] args)
    {
        WireLastResortHandlers();

        Console.WriteLine();
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine("  TechieBlog Database Migration Tool");
        Console.WriteLine("  PostgreSQL schema migrations (DbUp)");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();

        if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
        {
            ShowHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var postgresConn = GetArgValue(args, "--postgres") ?? GetArgValue(args, "--connection");

        try
        {
            return command switch
            {
                "schema" => RunSchema(postgresConn),
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
    /// Installs the process-wide handlers that report a crash this CLI's own try/catch cannot see
    /// (REQ-NFR-013).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <see cref="Main"/> already catches everything raised on its own
    /// thread, but a failure on a thread-pool thread or a faulted task nobody awaited bypasses it and
    /// would end the migration run with no explanation at all. Both handlers write to standard error
    /// so a CI job or a redirected console captures them.</para>
    /// <para><b>Diagnostics only, no Serilog:</b> BlogDb is referenced as a library by the
    /// <c>TechieBlog</c> host, and the Coding Standards forbid a library referencing Serilog. Adding
    /// the package here would push the static <c>Log</c> facade into the web host's dependency graph
    /// alongside its own configured logger. The console is therefore this head's sink; the handlers
    /// only ever run when the assembly is launched as the CLI, because <see cref="Main"/> is the only
    /// caller.</para>
    /// <para><b>Flow:</b> subscribe to the AppDomain and TaskScheduler events → observe the task
    /// exception so a legacy <c>ThrowUnobservedTaskExceptions</c> host does not kill the run.</para>
    /// <para><b>Side Effects:</b> Installs handlers that live for the lifetime of the process.</para>
    /// </remarks>
    private static void WireLastResortHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            var exception = eventArgs.ExceptionObject as Exception;
            Console.Error.WriteLine(
                $"\nUNHANDLED EXCEPTION (terminating: {eventArgs.IsTerminating}): {exception?.Message ?? eventArgs.ExceptionObject}");
            Console.Error.WriteLine(exception?.StackTrace);
            Console.Error.Flush();
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Console.Error.WriteLine($"\nUNOBSERVED TASK EXCEPTION: {eventArgs.Exception.Message}");
            Console.Error.WriteLine(eventArgs.Exception.StackTrace);
            Console.Error.Flush();
            eventArgs.SetObserved();
        };
    }

    /// <summary>
    /// Applies all pending PostgreSQL schema migrations through DbUp.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A connection string is mandatory; without one there is no target
    /// to upgrade, so the method reports the omission and fails rather than guessing a default.</para>
    ///
    /// <para><b>Flow:</b> Validate the connection string → construct <see cref="BlogDbSvc"/> →
    /// delegate to <see cref="BlogDbSvc.UpgradeDatabase"/> → map its boolean result to an exit code.</para>
    ///
    /// <para><b>Side Effects:</b> Executes DDL against the target database and appends rows to the
    /// DbUp journal table. The operation is idempotent: already-applied scripts are skipped.</para>
    /// </remarks>
    /// <param name="postgresConn">Npgsql connection string for the database to upgrade.</param>
    /// <returns>0 when every pending script applied cleanly; otherwise 1.</returns>
    private static int RunSchema(string? postgresConn)
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
    /// Writes CLI usage information to standard output.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Documents the single supported command and its required option so
    /// an operator can run a migration without reading the source.</para>
    /// <para><b>Flow:</b> Sequential <c>Console.WriteLine</c> calls; no branching.</para>
    /// <para><b>Side Effects:</b> Writes to standard output only.</para>
    /// </remarks>
    private static void ShowHelp()
    {
        Console.WriteLine("Usage: dotnet run -- <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  schema    Apply pending PostgreSQL schema migrations (DbUp)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --postgres <conn>   PostgreSQL connection string (target)");
        Console.WriteLine("  --connection <conn> Alias for --postgres");
        Console.WriteLine("  --help, -h          Show this help message");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine();
        Console.WriteLine("  dotnet run -- schema --postgres \"Host=localhost;Database=TechieBlog;Username=postgres;Password=secret\"");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  - The PostgreSQL database must exist before running migrations");
        Console.WriteLine("  - Migrations are idempotent; already-applied scripts are skipped");
        Console.WriteLine("  - The TechieBlog web host runs this same upgrade automatically at startup");
    }

    /// <summary>
    /// Reports an unrecognised command word to the operator.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Fails loudly on an unknown command instead of silently doing
    /// nothing, which would otherwise look like a successful no-op migration.</para>
    /// <para><b>Flow:</b> Print the offending command and point at <c>--help</c>.</para>
    /// <para><b>Side Effects:</b> Writes to standard output only.</para>
    /// </remarks>
    /// <param name="command">The unrecognised command word supplied by the operator.</param>
    /// <returns>Always 1, so the caller can return it directly as the process exit code.</returns>
    private static int ShowInvalidCommand(string command)
    {
        Console.WriteLine($"ERROR: Unknown command '{command}'");
        Console.WriteLine("Use --help to see available commands.");
        return 1;
    }

    /// <summary>
    /// Reads the value that follows a named option on the command line.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Supports the <c>--name value</c> option form only. The final
    /// argument is never considered an option name because it cannot be followed by a value, which
    /// also guarantees the lookahead below stays in bounds.</para>
    /// <para><b>Flow:</b> Scan pairwise for a case-insensitive name match and return the next slot;
    /// return <c>null</c> when the option is absent.</para>
    /// <para><b>Side Effects:</b> None — pure read over the supplied array.</para>
    /// </remarks>
    /// <param name="args">The raw command-line argument array.</param>
    /// <param name="argName">Option name to look for, including its leading dashes.</param>
    /// <returns>The option's value, or <c>null</c> when the option was not supplied.</returns>
    private static string? GetArgValue(string[] args, string argName)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(argName, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }
        return null;
    }
}
