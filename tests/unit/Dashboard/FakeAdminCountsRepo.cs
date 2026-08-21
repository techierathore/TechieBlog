using BlogModels;
using BlogModels.Interfaces;

namespace TechieBlog.Tests.Dashboard;

/// <summary>
/// In-memory stand-in for <see cref="IAdminCountsRepo"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the dashboard-counts tests prove that real numbers reach the tiles and
/// that a query failure degrades to zeroes, without a database. [REQ-FN-036]</para>
/// <para><b>Code Flow:</b> <see cref="GetAdminCountsAsync"/> either returns the seeded
/// <see cref="Counts"/> or throws the seeded <see cref="FailWith"/> exception, reproducing the two
/// outcomes the real Dapper repository can produce.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Seed <see cref="Counts"/> for the happy path, or set <see cref="FailWith"/>
/// to drive the failure path.</para>
/// </remarks>
public class FakeAdminCountsRepo : IAdminCountsRepo
{
    /// <summary>
    /// Gets or sets the counts this fake returns when it is not configured to fail.
    /// </summary>
    public AdminCounts Counts { get; set; } = new();

    /// <summary>
    /// Gets or sets the exception the fake throws instead of returning counts.
    /// </summary>
    /// <remarks>
    /// Null means the happy path; a non-null value reproduces a database failure.
    /// </remarks>
    public Exception? FailWith { get; set; }

    /// <summary>
    /// Gets the number of times the service asked for counts.
    /// </summary>
    public int CallCount { get; private set; }

    /// <inheritdoc />
    public Task<AdminCounts> GetAdminCountsAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;

        if (FailWith is not null)
            return Task.FromException<AdminCounts>(FailWith);

        return Task.FromResult(Counts);
    }
}
