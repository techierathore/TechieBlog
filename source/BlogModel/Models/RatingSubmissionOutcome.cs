namespace BlogModels;

/// <summary>
/// What happened to an accepted rating submission.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published output contract of <c>RatingSvc.SubmitRating</c>.
/// The star widget has to know whether the score already counts towards the average or is
/// waiting on an inbox click. [REQ-FN-023, REQ-FN-048]</para>
///
/// <para><b>Code Flow:</b> The service upserts the rating and fills this object from the
/// verification state of the address.</para>
///
/// <para><b>Dependencies:</b> None - a plain DTO in the leaf model assembly.</para>
///
/// <para><b>Usage:</b> Re-read the aggregates after a submission only when
/// <see cref="IsEmailVerificationRequired"/> is false; an unverified rating does not move them.</para>
///
/// <para><b>Exposure:</b> the outbound half of the rating exchange, so everything on it reaches the
/// browser. That is why it carries no address and no captcha material — the inbound
/// <see cref="RatingSubmission"/> holds those and never travels back. Keep any new member on this
/// type safe to show to an anonymous visitor.</para>
/// </remarks>
public class RatingSubmissionOutcome
{
    /// <summary>
    /// Gets or sets the id of the <see cref="PostRating"/> row that was created or updated. Because
    /// the operation is an upsert, the same id comes back when a visitor changes an earlier score —
    /// so it does not distinguish a new rating from an amended one.
    /// </summary>
    public long RatingId { get; set; }

    /// <summary>
    /// Gets or sets whether the rating is parked behind a double opt-in click: <c>true</c> means a
    /// verification email has been sent and the score does <b>not</b> yet contribute to
    /// <see cref="PostRatingStats"/>. This is the property the widget must branch on — the
    /// submission succeeded either way, so treating it as a failure would tell the visitor their
    /// rating was lost when it was in fact stored.
    /// </summary>
    public bool IsEmailVerificationRequired { get; set; }

    /// <summary>
    /// Gets or sets the text to show the visitor. Written for an anonymous audience: it confirms
    /// what happens next and must not repeat the submitted address back onto the page, or leak
    /// whether the address was already known to the site — either would turn the widget into a way
    /// of testing addresses against the database.
    /// </summary>
    public string Message { get; set; } = string.Empty;
}
