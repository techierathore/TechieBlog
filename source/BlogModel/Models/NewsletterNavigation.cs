namespace BlogModels;

/// <summary>
/// Previous/next neighbours of a published newsletter issue, resolved by send order.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Powers the prev/next links on a public archive page. The acceptance
/// criterion requires the ends of the archive to omit their missing neighbour, so both properties
/// are deliberately nullable rather than defaulted to the current issue.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>INewsletterService.GetNavigationAsync</c> loads the current issue.</item>
///   <item>The repository finds the nearest published issue sent before it
///         (<see cref="PreviousIssue"/>) and after it (<see cref="NextIssue"/>).</item>
///   <item>The oldest issue has no previous; the newest has no next.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="Newsletter"/>.</para>
///
/// <para><b>Usage:</b> Check <see cref="HasPrevious"/> / <see cref="HasNext"/> before rendering a
/// link.</para>
/// </remarks>
public class NewsletterNavigation
{
    /// <summary>
    /// The nearest published issue sent <i>before</i> the current one, or null at the oldest end of
    /// the archive. "Nearest" is by <see cref="Newsletter.SentOn"/>, not by
    /// <see cref="Newsletter.NewsletterId"/>, so an issue drafted earlier but sent later is not a
    /// neighbour here — the reader's sense of order is the send order.
    /// </summary>
    public Newsletter? PreviousIssue { get; set; }

    /// <summary>
    /// The nearest published issue sent <i>after</i> the current one, or null at the newest end.
    /// Same ordering rule as <see cref="PreviousIssue"/>. Both are resolved against published
    /// issues only, so a draft or private issue never becomes a neighbour and cannot leak its title
    /// through a navigation link.
    /// </summary>
    public Newsletter? NextIssue { get; set; }

    /// <summary>
    /// True when an older published issue exists. Render the link only when this is set — a null
    /// neighbour must omit the control rather than render a disabled or self-referential one, which
    /// is why the neighbours are nullable instead of defaulting to the current issue.
    /// </summary>
    public bool HasPrevious => PreviousIssue != null;

    /// <summary>
    /// True when a newer published issue exists. Same rule as <see cref="HasPrevious"/>; on a
    /// single-issue archive both are false.
    /// </summary>
    public bool HasNext => NextIssue != null;
}
