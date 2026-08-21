namespace BlogModels;

/// <summary>
/// Per-post engagement statistics: views, comments and ratings in one value.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Satisfies BRD-61's "per-post engagement statistics" with a single
/// round-trip, so an admin post row does not fan out into three separate count queries.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>IAnalyticsService.GetPostEngagementAsync</c> is called with a post id.</item>
///   <item>The analytics repository runs one query with correlated sub-selects over
///         <c>PostViews</c>, <c>BlogComment</c> and <c>PostRating</c>.</item>
///   <item>The populated value is returned; a post with no activity yields zeroes, never null.</item>
/// </list>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> <see cref="AverageRating"/> is zero when <see cref="RatingCount"/> is zero;
/// callers should test the count before rendering stars.</para>
/// </remarks>
public class PostEngagement
{
    /// <summary>
    /// The post these statistics describe.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// Post title, denormalised so an admin list can render without a second query.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Post slug, for linking straight to the public page.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Rows in <c>PostViews</c> for this post, all time. Because the tracker de-duplicates a
    /// visitor within a window, this counts reading sessions rather than raw page loads — it is not
    /// comparable with a web-server hit count.
    /// </summary>
    public int TotalViews { get; set; }

    /// <summary>
    /// Distinct <c>VisitorHash</c> values, all time — never greater than <see cref="TotalViews"/>.
    /// The hash is derived from IP and user agent, so one person on two networks counts twice and
    /// two people behind one NAT with identical browsers count once; treat it as an estimate.
    /// </summary>
    public int UniqueViews { get; set; }

    /// <summary>
    /// Every comment row on the post regardless of moderation state — pending, approved and
    /// rejected together. This is the admin's workload figure, not the public one.
    /// </summary>
    public int CommentCount { get; set; }

    /// <summary>
    /// Comments actually visible on the public page. The difference from
    /// <see cref="CommentCount"/> is the moderation backlog plus everything rejected, so the two
    /// are not interchangeable and only this one may be shown to a visitor.
    /// </summary>
    public int ApprovedCommentCount { get; set; }

    /// <summary>
    /// Rows in <c>PostRating</c> for the post — <b>every</b> rating, verified or not.
    /// </summary>
    /// <remarks>
    /// It therefore does not agree with <see cref="PostRatingStats.RatingCount"/>, which the public
    /// star widget uses and which counts only ratings whose address completed double opt-in. Expect
    /// the admin figure to be the larger of the two, and do not present them side by side as if
    /// they measured the same thing.
    /// </remarks>
    public int RatingCount { get; set; }

    /// <summary>
    /// Mean of those same unfiltered ratings on the 1-to-5 scale, or exactly zero when
    /// <see cref="RatingCount"/> is zero. Zero is not an achievable score, so it always means
    /// "unrated" — branch on the count, not on this value. Because unverified ratings are included,
    /// this average is more easily skewed than the public one.
    /// </summary>
    public double AverageRating { get; set; }

    /// <summary>
    /// When the post was most recently viewed, or null if it never has been. Useful for spotting
    /// content that has gone cold; a null here with a non-zero <see cref="TotalViews"/> would mean
    /// the two halves of the query disagreed and is not a state the analytics repository produces.
    /// </summary>
    public DateTime? LastViewedOn { get; set; }
}
