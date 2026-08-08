using System.ComponentModel.DataAnnotations;

namespace BlogModels;

/// <summary>
/// Everything a visitor sends when rating a post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published input contract of <c>RatingSvc.SubmitRating</c>.
/// Ratings are keyed by <see cref="Email"/>, so an anonymous visitor can rate exactly once
/// per post and change their mind later. [REQ-FN-023]</para>
///
/// <para><b>Code Flow:</b> The star widget binds this object; the service validates the score,
/// checks the captcha for anonymous submissions, upserts the rating and - when the address is
/// not yet verified - issues a double opt-in token.</para>
///
/// <para><b>Dependencies:</b> None - a plain DTO in the leaf model assembly.</para>
///
/// <para><b>Usage:</b> Leave <see cref="CaptchaChallengeId"/> and <see cref="CaptchaAnswer"/>
/// empty for an already-verified address; the service skips the challenge in that case.</para>
///
/// <para><b>Direction — this type only ever travels inbound.</b> Every property here is
/// visitor-supplied and therefore untrusted, including <see cref="UserId"/> and
/// <see cref="IpAddress"/> if a caller lets the browser set them. It is an input contract: never
/// return an instance to the browser and never bind one as the model of a rendered result. The
/// outbound half is <see cref="RatingSubmissionOutcome"/>, which deliberately carries no address
/// and no captcha material.</para>
/// </remarks>
public class RatingSubmission
{
    /// <summary>
    /// Gets or sets the post being rated. Not validated by an attribute — the service must confirm
    /// the post exists and is published, or a visitor can post ratings against draft or deleted ids.
    /// </summary>
    public long PostId { get; set; }

    /// <summary>
    /// Gets or sets the rater's email address - the identity key for the rating.
    /// </summary>
    /// <remarks>
    /// Personal data supplied by an anonymous visitor. It is what the rating is keyed on
    /// (case-insensitively, with <see cref="PostId"/>), and it is the address the double opt-in mail
    /// is sent to — so it must never be rendered back onto the post page. A star widget that echoed
    /// the address of an existing rating would let anyone enumerate who had rated a post. The
    /// <c>320</c>-character bound is the RFC maximum for an address, not a display limit.
    /// </remarks>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(320, ErrorMessage = "Email address is too long")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the score, a whole number of stars from 1 to 5 inclusive. There is no "zero
    /// stars" and no half-star; the <see cref="RangeAttribute"/> is the only thing that rejects 0,
    /// which is also the default for an unset <see cref="int"/> — so a form that never bound this
    /// property fails validation rather than silently recording a rating.
    /// </summary>
    [Range(1, 5, ErrorMessage = "Rating must be between 1 and 5")]
    public int Rating { get; set; }

    /// <summary>
    /// Gets or sets the rater's display name, echoed into the verification email. Optional and
    /// purely cosmetic — it identifies nobody and authorises nothing, and two raters may share one.
    /// Visitor-supplied text that ends up in an outbound message, so encode it into the mail body
    /// rather than concatenating it in.
    /// </summary>
    [StringLength(150, ErrorMessage = "Name is too long")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the signed-in user id when the visitor is authenticated; null otherwise. Stored
    /// alongside the rating for attribution only — the uniqueness key remains
    /// <see cref="PostId"/> + <see cref="Email"/>, so this value never decides whether a rating is
    /// an insert or an update. It must be filled from the server's own principal; a value that
    /// arrived from the browser is an unauthenticated claim of identity.
    /// </summary>
    public long? UserId { get; set; }

    /// <summary>
    /// Gets or sets the opaque id of the captcha challenge the visitor was shown. It is a lookup
    /// handle for the expected answer, which lives server-side keyed by this value; the id itself
    /// reveals nothing and is safe to round-trip through the browser. Single-use and short-lived —
    /// a resubmission needs a freshly generated challenge.
    /// </summary>
    public string CaptchaChallengeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the answer the visitor typed. This is the visitor's <i>attempt</i>, never the
    /// expected value.
    /// </summary>
    /// <remarks>
    /// The expected answer must stay on the server, held against
    /// <see cref="CaptchaChallengeId"/> — see the remarks on <see cref="CaptchaChallenge"/>, which
    /// deliberately has no property for it after a shipped bug sent the answer to the client and
    /// made the challenge decorative. Do not add an "expected answer" property here either: this
    /// object is bound by the browser, so anything on it is visible to whoever is being challenged.
    /// </remarks>
    public string CaptchaAnswer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the originating IP address, used for abuse throttling rather than
    /// identification. It must be filled by the host from the current connection and never accepted
    /// from the request body, or the rate limit is trivially defeated by varying the value. Empty
    /// when the address could not be determined — a Blazor Server circuit has no HTTP request to
    /// read it from — and behind a proxy it is only as trustworthy as the forwarded-headers
    /// configuration. Personal data: not for display.
    /// </summary>
    public string IpAddress { get; set; } = string.Empty;
}
