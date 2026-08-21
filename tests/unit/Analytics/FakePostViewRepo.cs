using BlogModels;
using BlogModels.Interfaces;

namespace TechieBlog.Tests.Analytics;

/// <summary>
/// In-memory stand-in for <see cref="IPostViewRepo"/> that reproduces the conditional insert.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets the view-tracking tests prove the de-duplication rule and the visitor
/// identity without a database. [REQ-FN-034]</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="RecordViewAsync"/> applies the same test the SQL does — a row is written only
///         when this visitor has not viewed this post since the window opened.</item>
///   <item><see cref="Rows"/> exposes what was written, so a test can assert on the stored visitor
///         hash and confirm no raw address ever reached the table.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Construct, hand to the tracker, then inspect <see cref="Rows"/>.</para>
/// </remarks>
public class FakePostViewRepo : IPostViewRepo
{
    private readonly List<PostView> rows = new();

    /// <summary>
    /// Gets every view row this fake has accepted, in the order they were written.
    /// </summary>
    public IReadOnlyList<PostView> Rows => rows;

    /// <summary>
    /// Gets the number of distinct visitor hashes across every recorded row.
    /// </summary>
    public int UniqueVisitorCount => rows.Select(row => row.VisitorHash).Distinct().Count();

    /// <summary>
    /// Gets the de-duplication window the tracker asked for on the most recent call.
    /// </summary>
    public int LastDedupeWindowHours { get; private set; }

    /// <inheritdoc />
    public Task<bool> RecordViewAsync(
        long postId,
        string visitorHash,
        DateTime viewedOn,
        int dedupeWindowHours,
        CancellationToken cancellationToken = default)
    {
        LastDedupeWindowHours = dedupeWindowHours;

        var windowStart = viewedOn.AddHours(-Math.Abs(dedupeWindowHours));
        var alreadySeen = rows.Any(row =>
            row.PostId == postId && row.VisitorHash == visitorHash && row.ViewedOn > windowStart);

        if (alreadySeen)
            return Task.FromResult(false);

        rows.Add(new PostView
        {
            ViewId = rows.Count + 1,
            PostId = postId,
            VisitorHash = visitorHash,
            ViewedOn = viewedOn
        });

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<PostViewCounts> GetCountsAsync(long postId, CancellationToken cancellationToken = default)
    {
        var forPost = rows.Where(row => row.PostId == postId).ToList();
        return Task.FromResult(new PostViewCounts
        {
            PostId = postId,
            TotalViews = forPost.Count,
            UniqueViews = forPost.Select(row => row.VisitorHash).Distinct().Count()
        });
    }

    /// <inheritdoc />
    public Task<int> GetSiteTotalViewsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(rows.Count);
    }
}
