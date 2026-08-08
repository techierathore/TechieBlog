namespace BlogModels;

/// <summary>
/// One page of the public newsletter archive: the issues on this page plus the total available.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps the archive listing and its paging control consistent — the
/// acceptance criterion is that "the archive count matches the issues listed", which needs the
/// count and the rows to come from the same call.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>INewsletterService.GetPublishedIssuesAsync</c> asks the repository for one page.</item>
///   <item>The repository runs the listing query and the matching count query against the same
///         published-only predicate.</item>
///   <item>The page is returned with <see cref="TotalCount"/> and <see cref="TotalPages"/>
///         precomputed for the UI.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="Newsletter"/>.</para>
///
/// <para><b>Usage:</b> <see cref="Items"/> is never null — an empty archive yields an empty list.</para>
/// </remarks>
public class NewsletterArchivePage
{
    /// <summary>
    /// The published issues on this page, ordered by send date descending with
    /// <c>NewsletterId</c> as the tie-break. Never null — an empty archive, and a page number past
    /// the end, both yield an empty list rather than null. Only issues that are sent, public and
    /// slugged appear, so nothing here needs a further visibility check before rendering.
    /// </summary>
    public IReadOnlyList<Newsletter> Items { get; set; } = new List<Newsletter>();

    /// <summary>
    /// Published issues across the whole archive, not just this page. Counted with the same
    /// published-only predicate as <see cref="Items"/> in the same call, which is what makes
    /// "the archive count matches the issues listed" hold.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Which page this is, counting from <b>one</b>, not zero — the first page is 1 and that is
    /// also the default. An off-by-one here shifts the whole listing by a page silently.
    /// </summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Issues requested per page. Note there is no default: an instance built without setting it
    /// has a page size of zero, which makes <see cref="TotalPages"/> zero and both navigation flags
    /// false. Always set it from the caller's request.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Pages implied by <see cref="TotalCount"/> and <see cref="PageSize"/>, rounded up so a
    /// partial final page still counts. Guarded against a zero or negative page size, which returns
    /// zero rather than dividing by zero. Computed, never persisted.
    /// </summary>
    public int TotalPages => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;

    /// <summary>
    /// True when a later page exists. Derived from <see cref="TotalPages"/>, so it is false — not
    /// true — when <see cref="PageSize"/> was never set.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>
    /// True when an earlier page exists, i.e. whenever <see cref="PageNumber"/> is above the
    /// one-based first page. Independent of <see cref="TotalCount"/>, so it stays true on an
    /// over-run page number and the reader can navigate back.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;
}
