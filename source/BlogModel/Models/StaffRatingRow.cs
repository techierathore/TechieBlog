namespace BlogModels;

/// <summary>
/// One rating as the staff see it in the ratings dialog: stars, date, confirmation state and a
/// masked rater.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-UI-067 / REQ-UI-068 / REQ-FN-063 (BRD-107…109). The staff can see each
/// rating behind an average, but never the rater's full address.</para>
///
/// <para><b>Code Flow:</b> <c>RatingSvc.GetRatingsForStaffAsync</c> reads <c>PostRating</c> rows
/// and builds one of these per row, masking the email on the way.</para>
///
/// <para><b>Dependencies:</b> None — dependency-leaf contract.</para>
///
/// <para><b>Usage:</b> <b>Deliberately has no email property.</b> The full address therefore cannot
/// reach a Razor component, a render tree or a browser by accident; the only form that exists past
/// the service is <see cref="MaskedEmail"/>.</para>
/// </remarks>
public class StaffRatingRow
{
    /// <summary>
    /// The rating's identifier.
    /// </summary>
    public long RatingId { get; set; }

    /// <summary>
    /// Stars given, 1 to 5.
    /// </summary>
    public int Stars { get; set; }

    /// <summary>
    /// When the rating was last set (UTC): its update time, or its creation time if never changed.
    /// </summary>
    public DateTime RatedOn { get; set; }

    /// <summary>
    /// Whether the rater's address was confirmed. Only confirmed ratings count in any average.
    /// </summary>
    public bool IsConfirmed { get; set; }

    /// <summary>
    /// The rater's address reduced to its first letter and domain, for example <c>r•••@gmail.com</c>.
    /// </summary>
    public string MaskedEmail { get; set; } = string.Empty;
}
