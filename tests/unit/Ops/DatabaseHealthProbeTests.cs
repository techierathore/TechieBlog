using BlogEngine.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Unit tests for <see cref="DatabaseHealthProbe"/> (REQ-NFR-014, BRD-74).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A readiness endpoint that throws is worse than no endpoint at all — the
/// probe would return a 500 that says nothing about which dependency is down. These tests drive
/// the probe against connection strings that cannot possibly work and assert it degrades into a
/// failed result carrying a diagnosable message.</para>
/// <para><b>Dependencies:</b> xUnit; deliberately no reachable database.</para>
/// </remarks>
public class DatabaseHealthProbeTests
{
    /// <summary>
    /// A missing connection string is reported as a configuration failure naming the setting,
    /// rather than as a connection error that sends the operator hunting the wrong problem.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingConnectionStringIsReportedAsConfigurationFailure(string connectionString)
    {
        var probe = BuildProbe(connectionString);

        var result = probe.Check();

        Assert.True(result.IsFailure);
        Assert.Contains("AppDbConString", result.ErrorMessage);
    }

    /// <summary>
    /// An unreachable database produces a failed result rather than an exception, so the
    /// readiness endpoint can still answer with a useful body.
    /// </summary>
    [Fact]
    public void UnreachableDatabaseFailsWithoutThrowing()
    {
        var probe = BuildProbe("Host=127.0.0.1;Port=1;Database=NoSuchDb;Username=none;Password=none;Timeout=1");

        var result = probe.Check();

        Assert.True(result.IsFailure);
        Assert.Contains("Database unreachable", result.ErrorMessage);
    }

    /// <summary>
    /// A malformed connection string is also absorbed into a failed result, covering the case
    /// where configuration is present but wrong.
    /// </summary>
    [Fact]
    public void MalformedConnectionStringFailsWithoutThrowing()
    {
        var probe = BuildProbe("this is not a connection string");

        var result = probe.Check();

        Assert.True(result.IsFailure);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    /// <summary>
    /// Builds a probe over the supplied connection string and a null logger.
    /// </summary>
    /// <param name="connectionString">The connection string under test.</param>
    /// <returns>The probe under test.</returns>
    private static DatabaseHealthProbe BuildProbe(string connectionString)
    {
        return new DatabaseHealthProbe(connectionString, NullLogger<DatabaseHealthProbe>.Instance);
    }
}
