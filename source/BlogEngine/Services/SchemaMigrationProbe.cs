using BlogEngine.DaCore;
using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Verifies that DbUp actually applied the schema, not merely that the database answers
/// (REQ-NFR-039).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A green deploy did not prove the migrations ran. DbUp executes at host
/// startup and creates its own <c>schemaversions</c> journal before applying
/// <c>source/BlogDb/PostgresScripts</c>; if the application's database role lacks DDL rights the
/// upgrade throws, <c>Program.cs</c> logs a <i>warning</i> and <b>the host still comes up</b>. The
/// existing <see cref="DatabaseHealthProbe"/> then answers <c>SELECT 1</c> perfectly — it tests the
/// connection, never the schema — so <c>/healthz</c> returned 200, the pipeline's <c>verify</c> job
/// went green, and the deploy was reported successful while the site was empty or 500ing on every
/// page. Until this probe existed the only defence was a manual runbook step
/// (<c>docs/Prod-Deploy-Checklist.md</c> §5a), and a runbook step is not a gate.</para>
///
/// <para><b>Code Flow:</b> confirm <c>schemaversions</c> exists → read every journalled script name
/// → enumerate the migration scripts on disk → report the ones present on disk but absent from the
/// journal.</para>
///
/// <para><b>Dependencies:</b> <see cref="DbConnectionFactory"/> (Npgsql) and
/// <see cref="ILogger{TCategoryName}"/>. No DbUp reference — the engine does not depend on
/// <c>BlogDb</c>, and reading the journal table is a plain query.</para>
///
/// <para><b>The expectation is derived, never hardcoded.</b> The obvious implementation — assert
/// that <c>025-DefaultToDarkMode.sql</c> is journalled — rots the day the next migration lands, and
/// rots silently in the wrong direction: the literal keeps passing while the new script is the one
/// that failed to apply. So the probe reads the <i>same script folder DbUp itself was pointed at</i>
/// (<see cref="scriptsPath"/>, resolved once in <c>Program.cs</c> and handed to both
/// <c>BlogDbSvc.UpgradeDatabase</c> and this probe) and expects every <c>.sql</c> file in it to
/// appear in the journal. Add a script and the expectation moves on its own; there is no constant to
/// forget to bump.</para>
///
/// <para><b>Matching rule.</b> DbUp's <c>WithScriptsFromFileSystem</c> journals the bare file name
/// (verified against the live database: the journal holds <c>001-CreateTables.sql</c>, not a path),
/// so the comparison is on file name, case-insensitively. A script that has been <i>deleted</i> from
/// disk but is still journalled is not a failure — that is a normal consequence of history being
/// immutable while the folder is not, and the journal is the record of what ran.</para>
///
/// <para><b>What this probe still does not prove.</b> It proves the journal says every script ran.
/// It does not verify that the objects those scripts created still exist — a table dropped by hand
/// afterwards leaves the journal intact. That is a deliberate boundary: the journal is the contract
/// DbUp itself honours, and reproducing every script's post-conditions here would be a second,
/// divergent schema definition.</para>
///
/// <para><b>Usage:</b> Registered in <c>Program.cs</c> alongside the migration run so both see the
/// same resolved scripts path, and wrapped by the host's <c>SchemaMigrationHealthCheck</c> under the
/// <c>ready</c> tag — which puts it on <c>/healthz</c>, the URL the deployment pipeline curls. A
/// migration failure now turns the deploy red instead of shipping an empty site.</para>
///
/// <para><b>Synchronous by design</b>, exactly like <see cref="DatabaseHealthProbe"/>: it is called
/// from the health endpoint, not from a Blazor circuit.</para>
/// </remarks>
public class SchemaMigrationProbe
{
    /// <summary>
    /// DbUp's journal table. Lower-case because DbUp creates it unquoted and PostgreSQL folds
    /// unquoted identifiers to lower case.
    /// </summary>
    private const string JournalTable = "schemaversions";

    /// <summary>
    /// Number of outstanding script names named individually in the health response before the rest
    /// are summarised as a count.
    /// </summary>
    /// <remarks>
    /// The operator reading this is mid-deploy and wants to know <i>which</i> migration is missing.
    /// One or two names is the normal case; a wholesale failure produces the entire set, and pasting
    /// thirty file names into a health payload helps nobody, so the tail is counted instead.
    /// </remarks>
    private const int MaxNamedScripts = 5;

    /// <summary>
    /// Message returned when the journal table is absent altogether.
    /// </summary>
    /// <remarks>
    /// Distinguished from "journalled but behind" because the causes and the fixes differ: no table
    /// at all means DbUp never got as far as creating it, which is almost always missing DDL rights
    /// on the database role, whereas a partial journal means DbUp ran and a specific script failed.
    /// Naming the wrong one sends the operator to the wrong place mid-deploy.
    /// </remarks>
    private const string JournalMissingMessage =
        "DbUp's 'schemaversions' journal table does not exist, so NO migration has been applied to " +
        "this database. The application role almost certainly lacks CREATE rights on the schema — " +
        "grant them and restart the host, or run the BlogDb migration tool against this database. " +
        "The site will be empty or broken until this is fixed.";

    private readonly string connectionString;
    private readonly string scriptsPath;
    private readonly ILogger<SchemaMigrationProbe> logger;

    /// <summary>
    /// Initialises the probe.
    /// </summary>
    /// <param name="connectionString">PostgreSQL connection string from <c>AppDbConString</c>.</param>
    /// <param name="scriptsPath">
    /// The very folder handed to <c>BlogDbSvc.UpgradeDatabase</c>. Passing anything else silently
    /// changes what the gate expects.
    /// </param>
    /// <param name="logger">Logger used to record probe failures.</param>
    public SchemaMigrationProbe(
        string connectionString, string scriptsPath, ILogger<SchemaMigrationProbe> logger)
    {
        this.connectionString = connectionString;
        this.scriptsPath = scriptsPath;
        this.logger = logger;
    }

    /// <summary>
    /// Reports whether every migration script on disk is recorded in DbUp's journal.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Three distinguishable outcomes, because the operator's next
    /// action differs for each: the journal is missing (nothing ran), the journal is behind (named
    /// scripts did not apply), or everything on disk is journalled. A fourth case — the scripts
    /// folder cannot be found — is reported as its own failure rather than as success, since a probe
    /// that silently passes when it cannot see the expectation is worse than no probe.</para>
    /// <para><b>Flow:</b> guard the connection string and the scripts folder → open a connection →
    /// test for the journal table with <c>to_regclass</c> → read the journalled names → subtract
    /// them from the file names on disk → build the verdict.</para>
    /// <para><b>Side Effects:</b> Opens and closes one pooled connection. Logs an error naming the
    /// outstanding scripts when the check fails, so the failure is in the log as well as in the
    /// health response.</para>
    /// <para><b>Never throws.</b> A readiness endpoint that throws produces a 500 with no body,
    /// which tells the pipeline nothing. Every failure mode becomes a failed
    /// <see cref="Result{T}"/>.</para>
    /// </remarks>
    /// <returns>
    /// A success result carrying the count of journalled scripts, or a failure whose message names
    /// what is missing.
    /// </returns>
    public Result<int> Check()
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return Result<int>.Failure("Database connection string 'AppDbConString' is not configured.");

        var expected = ReadExpectedScriptNames();
        if (expected == null)
        {
            return Result<int>.Failure(
                "The migration scripts folder could not be read, so the applied schema cannot be " +
                "verified. This check is inconclusive and is reported as a failure deliberately.");
        }

        try
        {
            // GetDbConnection returns an OPEN connection - calling Open() here throws
            // "Connection already open", exactly as DatabaseHealthProbe's identical block shows by
            // not calling it. Found by the REQ-NFR-039 negative-control smoke, which is the point
            // of running one.
            using var connection = DbConnectionFactory.GetDbConnection(EDbConnectionTypes.PostgreSql, connectionString);

            if (!JournalTableExists(connection))
            {
                logger.LogError(
                    "Schema gate FAILED: DbUp journal table '{JournalTable}' is absent; no migration has run",
                    JournalTable);
                return Result<int>.Failure(JournalMissingMessage);
            }

            var applied = ReadJournalledScriptNames(connection);
            var outstanding = expected
                .Where(name => !applied.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (outstanding.Count > 0)
            {
                logger.LogError(
                    "Schema gate FAILED: {OutstandingCount} of {ExpectedCount} migration scripts are " +
                    "not journalled — {Outstanding}",
                    outstanding.Count, expected.Count, string.Join(", ", outstanding));
                return Result<int>.Failure(BuildBehindMessage(outstanding, applied.Count, expected.Count));
            }

            return Result<int>.Success(applied.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Schema migration probe could not read the DbUp journal");
            return Result<int>.Failure(
                "The DbUp journal could not be read, so the applied schema cannot be verified. " +
                "The underlying error is in the application log; match it on the correlationId in " +
                "this response.");
        }
    }

    /// <summary>
    /// Builds the operator-facing message for a journal that exists but is behind the scripts folder.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Names the outstanding scripts, because the person reading this is
    /// mid-deploy and the file name is what they need to look at. The list is truncated so a
    /// wholesale failure does not bury the instruction that follows it.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="outstanding">Script file names present on disk but absent from the journal.</param>
    /// <param name="appliedCount">How many scripts the journal records.</param>
    /// <param name="expectedCount">How many scripts the folder holds.</param>
    /// <returns>A single sentence naming what is missing and what to do about it.</returns>
    private static string BuildBehindMessage(
        IReadOnlyList<string> outstanding, int appliedCount, int expectedCount)
    {
        var named = string.Join(", ", outstanding.Take(MaxNamedScripts));
        var remainder = outstanding.Count - MaxNamedScripts;
        var tail = remainder > 0 ? $" (and {remainder} more)" : string.Empty;

        return $"DbUp's journal is BEHIND the migration set: {appliedCount} of {expectedCount} " +
               $"scripts are recorded, and these have not applied — {named}{tail}. The startup " +
               "migration failed part-way; check the host log for the DbUp error, fix the cause " +
               "(usually a missing grant or a script error) and restart the host. Serving traffic " +
               "against this schema will fail.";
    }

    /// <summary>
    /// Lists the migration script file names DbUp is configured to apply.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the half of the check that keeps itself current. Reading
    /// the folder rather than a constant means a new script becomes part of the expectation the
    /// moment it is added, with nothing to remember to update — which matters because migrations are
    /// added by whoever happens to need one, not by whoever maintains this probe.</para>
    /// <para><b>Flow:</b> guard the folder → enumerate <c>*.sql</c> → project to bare file names.</para>
    /// <para><b>Side Effects:</b> Reads a directory; logs an error when it is absent.</para>
    /// </remarks>
    /// <returns>
    /// The script file names, or <c>null</c> when the folder could not be read — which the caller
    /// converts into a failure rather than a pass.
    /// </returns>
    private IReadOnlyList<string> ReadExpectedScriptNames()
    {
        if (string.IsNullOrWhiteSpace(scriptsPath) || !Directory.Exists(scriptsPath))
        {
            logger.LogError(
                "Schema gate INCONCLUSIVE: migration scripts folder {ScriptsPath} does not exist",
                scriptsPath);
            return null;
        }

        try
        {
            return Directory
                .GetFiles(scriptsPath, "*.sql", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not enumerate migration scripts in {ScriptsPath}", scriptsPath);
            return null;
        }
    }

    /// <summary>
    /// Reports whether DbUp's journal table is present.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>to_regclass</c> answers without raising, so an absent table is
    /// a <c>NULL</c> row rather than an exception — which keeps "no migrations at all" distinct from
    /// "the database is unreachable" instead of collapsing both into one catch.</para>
    /// <para><b>Side Effects:</b> Executes one scalar query.</para>
    /// </remarks>
    /// <param name="connection">An open connection to the application database.</param>
    /// <returns><c>true</c> when the journal table exists.</returns>
    private static bool JournalTableExists(System.Data.IDbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT to_regclass('{JournalTable}') IS NOT NULL";
        var answer = command.ExecuteScalar();
        return answer is bool exists && exists;
    }

    /// <summary>
    /// Reads every script name DbUp has recorded as applied.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The set is case-insensitive because the comparison is against
    /// file names, and a rename that differs only in case is not a new migration.</para>
    /// <para><b>Side Effects:</b> Executes one query and reads it to completion.</para>
    /// </remarks>
    /// <param name="connection">An open connection to the application database.</param>
    /// <returns>The journalled script names.</returns>
    private static HashSet<string> ReadJournalledScriptNames(System.Data.IDbConnection connection)
    {
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT scriptname FROM {JournalTable}";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!reader.IsDBNull(0))
            {
                applied.Add(Path.GetFileName(reader.GetString(0)));
            }
        }

        return applied;
    }
}
