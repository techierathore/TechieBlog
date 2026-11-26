namespace BlogModels;

/// <summary>
/// A blog post together with the readership figures that earned it a place in the popular list.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Backs the popular-post ranking of BRD-61. The ranking numbers travel with
/// the post so a listing can show "why" a post is popular without another query.</para>
///
/// <para><b>Ranking rule:</b> posts are ordered by <see cref="TotalViews"/> within the requested
/// window, then by <see cref="UniqueViews"/>, then by <see cref="CommentCount"/>, then by most
/// recent publication — a deterministic order even when several posts tie on views.</para>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> Returned by <c>IAnalyticsService.GetPopularPostsAsync</c>. Only published,
/// non-deleted posts are ever included.</para>
/// </remarks>
public class PopularPost
{
    /// <summary>
    /// The ranked post. Always a published, non-deleted post — the ranking query filters those out
    /// before ordering, so an id appearing here is safe to link to publicly.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// Post title, carried along so a listing renders without a second query. Author-supplied text
    /// bound for a page — escape on render.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Post slug, the public route handle. Use this rather than <see cref="PostId"/> when building
    /// the link; the site addresses posts by slug.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Rows in <c>PostViews</c> for this post <b>inside the requested window</b> — not all time.
    /// The tracker writes at most one row per visitor per post per de-duplication window, so this
    /// is closer to "reading sessions" than to "page loads", and a refresh does not inflate it.
    /// Primary sort key.
    /// </summary>
    public int TotalViews { get; set; }

    /// <summary>
    /// Distinct <c>VisitorHash</c> values inside the same window. Always less than or equal to
    /// <see cref="TotalViews"/>; the gap between them is repeat readership. First tie-break.
    /// </summary>
    public int UniqueViews { get; set; }

    /// <summary>
    /// Comments on the post — <b>all time, and all statuses</b>, so pending and rejected comments
    /// are counted alongside approved ones. It is a popularity signal, not the number a visitor
    /// sees under the post. Second tie-break.
    /// </summary>
    public int CommentCount { get; set; }

    /// <summary>
    /// Confirmed ratings for the post, all time — rows in <c>PostRating</c> whose address
    /// completed double opt-in (REQ-FN-063, 2026-11-21).
    /// </summary>
    /// <remarks>
    /// Until 2026-11-21 this counted every rating, confirmed or not, so an unconfirmed rating showed
    /// on Analytics and nowhere else. It now matches the public star widget
    /// (<see cref="PostRatingStats.RatingCount"/>) exactly.
    /// </remarks>
    public int RatingCount { get; set; }

    /// <summary>
    /// Mean of those same confirmed ratings on the 1-to-5 scale of <see cref="PostRating.Rating"/>.
    /// Zero when the post has no confirmed rating; a genuine zero score is impossible, so test the
    /// count rather than treating 0.0 as "rated badly".
    /// </summary>
    public double AverageRating { get; set; }

    /// <summary>
    /// When the post was published; null on a post with no publication date. The final tie-break,
    /// most recent first, which is what makes the ranking deterministic when several posts match on
    /// every count above.
    /// </summary>
    public DateTime? PublishedOn { get; set; }
}
