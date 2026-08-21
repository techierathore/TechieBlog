using BlogEngine.Services;
using Microsoft.Extensions.Logging;
using TechieBlog.Tests.Dashboard;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Unit tests for <see cref="SchemaMigrationProbe"/> (REQ-NFR-039).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The gate exists because a green deploy did not prove the migrations
/// applied: DbUp can throw at startup while the host still comes up, and the old readiness check
/// only ran <c>SELECT 1</c>, so <c>/healthz</c> returned 200 over an unmigrated database. A gate
/// nobody has watched fail is not a gate, so these tests are weighted towards the failure modes —
/// the journal absent, the journal behind, the expectation unreadable — and each asserts that the
/// message names <i>which</i> of those it is, because the operator reading it is mid-deploy.</para>
///
/// <para><b>Dependencies:</b> xUnit. Deliberately no database: every case here is decided before or
/// without a successful connection, which keeps the suite runnable on any machine. The
/// journal-content cases are proved against the real PostgreSQL instance by the negative-control
/// smoke described in the requirement's verification notes.</para>
/// </remarks>
public class SchemaMigrationProbeTests
{
    /// <summary>
    /// A missing connection string is reported as a configuration failure naming the setting, so an
    /// unconfigured host does not look like a migration problem.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingConnectionStringIsReportedAsConfigurationFailure(string connectionString)
    {
        var probe = new SchemaMigrationProbe(
            connectionString, CreateScriptsFolder("001-CreateTables.sql"), new RecordingLogger<SchemaMigrationProbe>());

        var result = probe.Check();

        Assert.True(result.IsFailure);
        Assert.Contains("AppDbConString", result.ErrorMessage);
    }

    /// <summary>
    /// A scripts folder that cannot be read makes the check inconclusive, and an inconclusive check
    /// FAILS rather than passes — a gate that goes green when it cannot see its own expectation is
    /// the exact defect this requirement closes, one level up.
    /// </summary>
    [Fact]
    public void UnreadableScriptsFolderFailsRatherThanPasses()
    {
        var recorder = new RecordingLogger<SchemaMigrationProbe>();
        var probe = new SchemaMigrationProbe(
            "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1",
            Path.Combine(Path.GetTempPath(), "techieblog-no-such-scripts-folder"),
            recorder);

        var result = probe.Check();

        Assert.True(result.IsFailure);
        Assert.Contains("inconclusive", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(recorder.Entries, entry => entry.Level == LogLevel.Error);
    }

    /// <summary>
    /// An unreachable database produces a failed result rather than an exception, so the readiness
    /// endpoint still answers with a parsable body that the deployment pipeline can read.
    /// </summary>
    [Fact]
    public void UnreachableDatabaseFailsWithoutThrowing()
    {
        var probe = new SchemaMigrationProbe(
            "Host=127.0.0.1;Port=1;Database=NoSuchDb;Username=none;Password=none;Timeout=1",
            CreateScriptsFolder("001-CreateTables.sql"),
            new RecordingLogger<SchemaMigrationProbe>());

        var result = probe.Check();

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    /// <summary>
    /// The database-unreachable message carries no exception text, because the same message is
    /// rendered into the anonymously reachable <c>/healthz</c> body (REQ-NFR-033); it points at the
    /// log and at the correlation id instead.
    /// </summary>
    [Fact]
    public void UnreachableDatabaseMessageDisclosesNoExceptionText()
    {
        var probe = new SchemaMigrationProbe(
            "Host=127.0.0.1;Port=1;Database=NoSuchDb;Username=secretaccount;Password=none;Timeout=1",
            CreateScriptsFolder("001-CreateTables.sql"),
            new RecordingLogger<SchemaMigrationProbe>());

        var result = probe.Check();

        Assert.DoesNotContain("secretaccount", result.ErrorMessage);
        Assert.DoesNotContain("127.0.0.1", result.ErrorMessage);
        Assert.Contains("correlationId", result.ErrorMessage);
    }

    /// <summary>
    /// The expectation is derived from the scripts folder rather than from a constant, so a script
    /// added by anyone becomes part of what the gate demands with no code change here — which is
    /// what stops the check rotting into a literal that passes while the newest migration is the one
    /// that failed.
    /// </summary>
    [Fact]
    public void ExpectationIsDerivedFromTheScriptsFolder()
    {
        var folder = CreateScriptsFolder("001-CreateTables.sql", "029-SomethingNew.sql", "notes.txt");
        var probe = new SchemaMigrationProbe(
            "Host=127.0.0.1;Port=1;Database=NoSuchDb;Username=none;Password=none;Timeout=1",
            folder,
            new RecordingLogger<SchemaMigrationProbe>());

        // Reaching the connection attempt at all proves the folder was enumerated successfully;
        // a folder it could not read short-circuits with the "inconclusive" message instead.
        var result = probe.Check();

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("inconclusive", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Creates a throwaway folder holding the named files, standing in for
    /// <c>source/BlogDb/PostgresScripts</c>.
    /// </summary>
    /// <param name="fileNames">Files to create inside it.</param>
    /// <returns>The folder's absolute path.</returns>
    private static string CreateScriptsFolder(params string[] fileNames)
    {
        var folder = Path.Combine(Path.GetTempPath(), "techieblog-scripts-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        foreach (var fileName in fileNames)
        {
            File.WriteAllText(Path.Combine(folder, fileName), "-- test script");
        }

        return folder;
    }
}
