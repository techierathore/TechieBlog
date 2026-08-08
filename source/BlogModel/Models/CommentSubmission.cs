using System.ComponentModel.DataAnnotations;

namespace BlogModels;

/// <summary>
/// Everything a visitor sends when posting a comment, including the anti-abuse evidence.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published input contract between the comment form and
/// <c>CommentSvc.SubmitComment</c>. Keeping the raw submission separate from
/// <see cref="BlogComment"/> means the anti-abuse fields (honeypot, captcha answer,
/// render timestamp) never reach the persisted entity. [REQ-FN-022, REQ-FN-049]</para>
///
/// <para><b>Code Flow:</b> The UI binds this object, calls <c>CommentSvc.SubmitComment</c>,
/// which runs the spam guard and captcha check before any row is written.</para>
///
/// <para><b>Dependencies:</b> None - a plain DTO in the leaf model assembly.</para>
///
/// <para><b>Usage:</b> The UI must set <see cref="RenderedOn"/> when the form is first drawn
/// and leave <see cref="HoneypotValue"/> bound to a visually hidden input that a human
/// never fills in.</para>
///
/// <para><b>The state machine this object starts.</b> Every field here is consumed inside one
/// call; nothing about the submission survives except the comment it produces:</para>
/// <list type="number">
///   <item><b>Abuse gates.</b> Anonymous submissions must pass the captcha
///   (<see cref="CaptchaChallengeId"/> + <see cref="CaptchaAnswer"/>); a signed-in visitor
///   (<see cref="UserId"/> greater than zero) is exempt because signing in already proved they are
///   human. Then the spam guard blocks on a filled <see cref="HoneypotValue"/> or a
///   <see cref="RenderedOn"/> less than three seconds old, and scores the body. A failure here writes
///   nothing at all - there is no rejected-comment row.</item>
///   <item><b>Persist.</b> The identity, body and forensic fields are mapped onto a
///   <see cref="BlogComment"/> in status <c>PendingVerification</c>; the anti-abuse fields are
///   dropped and never stored.</item>
///   <item><b>Confirm.</b> An address already verified on an earlier comment skips straight to
///   <c>PendingApproval</c> (or <c>Approved</c>). Otherwise a token is issued and mailed, and the
///   comment waits. If the token cannot be issued the freshly written comment is <i>deleted</i>, so
///   no invisible orphan is left that the visitor could never rescue.</item>
///   <item><b>Report.</b> The result is a <see cref="CommentSubmissionOutcome"/>, which tells the UI
///   which of those two endings occurred.</item>
/// </list>
///
/// <para><b>Exposure.</b> The whole object is inbound and untrusted. Nothing on it may be echoed
/// back to the page except the visitor's own name and body text after encoding; in particular
/// <see cref="CaptchaAnswer"/>, <see cref="IpAddress"/> and <see cref="AuthorEmail"/> must not be
/// rendered.</para>
/// </remarks>
public class CommentSubmission
{
    /// <summary>
    /// The post being commented on - the only field that decides where the comment lands.
    /// </summary>
    /// <remarks>
    /// Client-supplied, so it must be treated as a request rather than a fact: the service is
    /// responsible for checking that the post exists and accepts comments. Nothing here prevents a
    /// crafted submission naming an unpublished or soft-deleted post.
    /// </remarks>
    public long PostId { get; set; }

    /// <summary>
    /// The comment being replied to; null for a top-level comment.
    /// </summary>
    /// <remarks>
    /// Client-supplied and equally unverified - nothing on this type guarantees that the parent
    /// belongs to <see cref="PostId"/>, or that it is itself approved. Zero is not a valid
    /// "top level" value; use null (see <see cref="BlogComment.ParentCommentID"/>).
    /// </remarks>
    public long? ParentCommentId { get; set; }

    /// <summary>
    /// The commenter's display name, published beside the comment.
    /// </summary>
    /// <remarks>
    /// Capped at 150 characters here while the column behind
    /// <see cref="BlogComment.GivenBy"/> allows 350, so the public form is the stricter of the two.
    /// Untrusted and rendered publicly - encode it. It is a label, not an identity: two visitors may
    /// submit the same name, and <see cref="AuthorEmail"/> is what actually identifies them.
    /// </remarks>
    [Required(ErrorMessage = "Name is required")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 150 characters")]
    public string AuthorName { get; set; } = string.Empty;

    /// <summary>
    /// The commenter's email address - the anonymous identity key.
    /// </summary>
    /// <remarks>
    /// Decides the whole flow: an address already verified on an earlier comment skips the
    /// confirmation step, an unknown one triggers a mailed token. Matched case-insensitively, so
    /// changing case does not create a second identity.
    /// <para>The 320-character cap is the RFC maximum for an address; the column behind
    /// <see cref="BlogComment.Email"/> only allows 350, and its own validation regex is <i>narrower</i>
    /// than the <c>[EmailAddress]</c> check used here - see the remarks there.</para>
    /// <para><b>Exposure:</b> personal data. It is never rendered publicly; it reaches only the
    /// moderation queue and the confirmation mail sent to that same address.</para>
    /// </remarks>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    [StringLength(320, ErrorMessage = "Email address is too long")]
    public string AuthorEmail { get; set; } = string.Empty;

    /// <summary>
    /// The comment body.
    /// </summary>
    /// <remarks>
    /// The 850-character cap matches the <c>VARCHAR(850)</c> column exactly, so a valid submission
    /// can never fail on length at the database. Plain text, not Markdown, and nothing sanitises it
    /// on the way out - see <see cref="BlogComment.Comment"/>.
    /// <para>It is also the input to the spam score: more than two links, or embedded markup, each
    /// add points towards a silent rejection.</para>
    /// </remarks>
    [Required(ErrorMessage = "Comment is required")]
    [StringLength(850, MinimumLength = 2, ErrorMessage = "Comment must be between 2 and 850 characters")]
    public string CommentText { get; set; } = string.Empty;

    /// <summary>
    /// The honeypot field. A real visitor never sees it, so any non-empty value means an
    /// automated submission.
    /// </summary>
    /// <remarks>
    /// A hard block with no score and no appeal - the guard treats a filled honeypot as conclusive.
    /// It only works if the input really is invisible to humans and is <b>not</b> marked
    /// <c>required</c>, <c>autocomplete</c>-friendly or reachable by keyboard; hiding it with
    /// <c>display:none</c> also hides it from screen readers, which is the intent here.
    /// <para>Never persisted, never echoed back into the re-rendered form.</para>
    /// </remarks>
    public string HoneypotValue { get; set; } = string.Empty;

    /// <summary>
    /// The id of the captcha challenge the visitor answered.
    /// </summary>
    /// <remarks>
    /// Opaque and single-use: validating it consumes the server-side entry, so a rejected submission
    /// cannot be retried with the same id and the UI <b>must</b> render a fresh challenge after any
    /// failure. Ignored entirely when <see cref="UserId"/> identifies a signed-in visitor.
    /// </remarks>
    public string CaptchaChallengeId { get; set; } = string.Empty;

    /// <summary>
    /// The answer the visitor typed for the captcha challenge.
    /// </summary>
    /// <remarks>
    /// Compared against the expected value held server-side under
    /// <see cref="CaptchaChallengeId"/> - the expected value itself never leaves the server, which is
    /// why <see cref="CaptchaChallenge"/> deliberately has no property for it.
    /// <para><b>Exposure:</b> this field travels inbound only. Do not repopulate it when re-rendering
    /// a failed form: the answer belongs to a challenge that has just been consumed, and echoing
    /// captcha state back into the page is how the answer leaks to the client.</para>
    /// </remarks>
    public string CaptchaAnswer { get; set; } = string.Empty;

    /// <summary>
    /// The UTC instant at which the form was rendered. Used to reject submissions that arrive
    /// impossibly fast.
    /// </summary>
    /// <remarks>
    /// Anything submitted less than three seconds after the form was drawn is blocked outright.
    /// <b>The default value disables the check</b> - the guard skips it when this is
    /// <c>default(DateTime)</c> rather than treating an unset value as suspicious - so a UI that
    /// forgets to set it silently loses this defence.
    /// <para>Must be <see cref="DateTime.UtcNow"/>: the comparison is against UTC, so a local-time
    /// value shifts the window by the server's offset and can block every submission or none.</para>
    /// </remarks>
    public DateTime RenderedOn { get; set; }

    /// <summary>
    /// The originating IP address, supplied by the host.
    /// </summary>
    /// <remarks>
    /// Filled by the server from the request, never bound from the form - a client-supplied value
    /// would make the forensic trail worthless. Copied onto the comment and onto the verification
    /// token for abuse investigation.
    /// <para><b>Exposure:</b> personal data; admin surfaces only. See
    /// <see cref="BlogComment.AuthorIpAddress"/>.</para>
    /// </remarks>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>
    /// The originating user agent, supplied by the host.
    /// </summary>
    /// <remarks>
    /// Attacker-controlled by nature, so it is evidence rather than a control input. Persisted to
    /// <see cref="BlogComment.AuthorUserAgent"/> and subject to the same admin-only exposure rule.
    /// </remarks>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>
    /// The signed-in user id when the visitor is authenticated; null otherwise.
    /// </summary>
    /// <remarks>
    /// <b>Security-bearing:</b> a value greater than zero skips the captcha entirely. It must
    /// therefore be set by the server from the authenticated principal and never bound from the
    /// request - a form-supplied user id would turn this into a one-field captcha bypass. Null and
    /// zero both mean anonymous.
    /// <para>Being signed in skips only the captcha; the double opt-in and moderation steps still
    /// apply, because identity here remains the <see cref="AuthorEmail"/> pair.</para>
    /// </remarks>
    public long? UserId { get; set; }
}
