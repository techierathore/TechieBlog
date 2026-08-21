namespace BlogModels;

/// <summary>
/// A single recorded view of a blog post, backing the pre-existing <c>PostViews</c> table.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Turns the analytics table — which existed from migration 001 but was never
/// written to — into a real, privacy-conscious record of readership.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A post page load calls <c>IPostViewTracker.TrackViewAsync</c>.</item>
///   <item>The tracker derives <see cref="VisitorHash"/> from the caller's IP and user agent and
///         inserts a row unless the same visitor already viewed the same post inside the
///         de-duplication window.</item>
///   <item>Analytics queries aggregate rows into total views (row count) and unique views
///         (distinct <see cref="VisitorHash"/>).</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> <see cref="ViewerIp"/> is retained only for the legacy column shape and is
/// written as null by the tracker — the raw address is never persisted.</para>
/// </remarks>
public class PostView
{
    /// <summary>
    /// Surrogate key of the view row. Carries no meaning beyond insertion order.
    /// </summary>
    public long ViewId { get; set; }

    /// <summary>
    /// The post that was viewed. Every analytics figure in the application is ultimately a
    /// grouping of these rows by this column.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// When the view occurred, in UTC — unlike the audit and session tables, which record
    /// server-local time. The trend query buckets on this column by UTC day, and the
    /// de-duplication window is measured from it.
    /// </summary>
    public DateTime ViewedOn { get; set; }

    /// <summary>
    /// Legacy raw-IP column. Deliberately left null by the tracker for privacy; kept because the
    /// column pre-dates this feature.
    /// </summary>
    public string ViewerIp { get; set; } = string.Empty;

    /// <summary>
    /// Salted, irreversible SHA-256 hash identifying the visitor. This — not the IP address — is
    /// what "unique" is counted on.
    /// </summary>
    public string VisitorHash { get; set; } = string.Empty;
}
