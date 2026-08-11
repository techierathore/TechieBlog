using BlogModels;
using BlogModels.Interfaces;

namespace TechieBlog.Tests.Analytics;

/// <summary>
/// Post-view repository that fails on demand, for testing the background writer's failure contract.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> [REQ-NFR-034] Lets a test prove that a database fault costs exactly one
/// view and not the whole background writer. Nothing else in the suite can produce that condition —
/// <see cref="FakePostViewRepo"/> always succeeds.</para>
///
/// <para><b>Code Flow:</b> <see cref="RecordViewAsync"/> counts the attempt, then throws while
/// <see cref="IsFailing"/> is set and returns normally once it is cleared, so one test can drive both
/// the failure and the recovery.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Construct, hand to the writer's container, flip <see cref="IsFailing"/>
/// between the two halves of the test, and assert on <see cref="AttemptCount"/>.</para>
/// </remarks>
public class ThrowingPostViewRepo : IPostViewRepo
{
    private int attemptCount;

    /// <summary>
    /// Gets or sets whether the next write attempt throws. Starts <c>true</c>.
    /// </summary>
    public bool IsFailing { get; set; } = true;

    /// <summary>
    /// Gets how many write attempts have reached this repository, failed ones included.
    /// </summary>
    public int AttemptCount => attemptCount;

    /// <summary>
    /// Counts the attempt, then either throws or reports a recorded view.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The counter moves BEFORE the throw, so a test can wait on the
    /// attempt rather than on a result that never arrives.</para>
    /// <para><b>Flow:</b> increment → throw when failing → otherwise report success.</para>
    /// <para><b>Side Effects:</b> Increments <see cref="AttemptCount"/>.</para>
    /// </remarks>
    /// <param name="postId">The viewed post.</param>
    /// <param name="visitorHash">Salted visitor hash.</param>
    /// <param name="viewedOn">UTC timestamp of the view.</param>
    /// <param name="dedupeWindowHours">De-duplication window in hours.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns><c>true</c> when not failing.</returns>
    /// <exception cref="InvalidOperationException">Thrown while <see cref="IsFailing"/> is set.</exception>
    public Task<bool> RecordViewAsync(
        long postId,
        string visitorHash,
        DateTime viewedOn,
        int dedupeWindowHours,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref attemptCount);

        if (IsFailing)
            throw new InvalidOperationException("Simulated database failure.");

        return Task.FromResult(true);
    }

    /// <summary>
    /// Reads zeroed counts; this fake exists for the write path only.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> No test here reads counts, so the honest answer is zero.</para>
    /// <para><b>Flow:</b> return a zeroed value carrying the post id.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="postId">The post to count.</param>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Zeroed counts.</returns>
    public Task<PostViewCounts> GetCountsAsync(long postId, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PostViewCounts { PostId = postId });

    /// <summary>
    /// Reads a zeroed site total; this fake exists for the write path only.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> No test here reads the site total.</para>
    /// <para><b>Flow:</b> return zero.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>Zero.</returns>
    public Task<int> GetSiteTotalViewsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0);
}
