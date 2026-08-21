namespace BlogModels.Interfaces;

/// <summary>
/// Data access contract for the <c>PostViews</c> analytics table.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Wires the long-dormant <c>PostViews</c> table (BRD-60) to real writes and
/// reads, keeping the de-duplication rule in one parameterised SQL statement rather than in a
/// read-then-write race in C#.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="RecordViewAsync"/> performs a conditional insert: the row is written only
///         when the same visitor has not viewed the same post inside the window.</item>
///   <item><see cref="GetCountsAsync"/> aggregates rows into total and unique counts.</item>
/// </list>
///
/// <para><b>Dependencies:</b> Implemented by <c>BlogEngine.DbAccess.PostViewRepo</c> over Dapper
/// and PostgreSQL.</para>
///
/// <para><b>Usage:</b> Only <c>VisitorHash</c> is persisted for identity — never the raw IP.</para>
///
/// <para><b>Async conversion — REQ-NFR-026.</b> The cancellation token was added to the existing
/// signatures rather than as a second overload, because an <c>Async(args)</c> / <c>Async(args, ct)</c>
/// pair makes every existing call ambiguous at the call site. A view write is fire-and-forget from the
/// page's point of view, so passing a token here matters most for the aggregate reads.</para>
/// </remarks>
public interface IPostViewRepo
{
    /// <summary>
    /// Records a post view unless the visitor already viewed the post inside the window.
    /// </summary>
    /// <param name="postId">The viewed post.</param>
    /// <param name="visitorHash">Salted, irreversible visitor hash.</param>
    /// <param name="viewedOn">UTC timestamp of the view.</param>
    /// <param name="dedupeWindowHours">Hours during which a repeat view is not counted again.</param>
    /// <param name="cancellationToken">Cancels the conditional insert.</param>
    /// <returns>True when a new row was written; false when the view was de-duplicated.</returns>
    Task<bool> RecordViewAsync(
        long postId,
        string visitorHash,
        DateTime viewedOn,
        int dedupeWindowHours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads total and unique view counts for one post.
    /// </summary>
    /// <param name="postId">The post to count.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Counts for the post; zeroes when it has never been viewed.</returns>
    Task<PostViewCounts> GetCountsAsync(long postId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts every recorded view across the site.
    /// </summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>Total rows in <c>PostViews</c>.</returns>
    Task<int> GetSiteTotalViewsAsync(CancellationToken cancellationToken = default);
}
