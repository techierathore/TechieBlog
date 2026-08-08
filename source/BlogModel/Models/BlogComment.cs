using System.ComponentModel.DataAnnotations;

namespace BlogModels;

/// <summary>
/// A visitor comment on a blog post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries a comment between the moderation UI, <c>CommentSvc</c>
/// and <c>BlogCommentRepo</c>. Since [REQ-FN-022] the identity of a commenter is the
/// anonymous <see cref="GivenBy"/> / <see cref="Email"/> pair, NOT a signed-in user id;
/// <see cref="UserId"/> is only an optional back-link when the visitor happened to be
/// authenticated.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>A submission arrives as a <see cref="CommentSubmission"/> and is turned into
///   a BlogComment with <see cref="ModerationStatus"/> = <c>PendingVerification</c>.</item>
///   <item>The commenter confirms the address (double opt-in, [REQ-FN-048]); the status
///   moves to <c>PendingApproval</c> - the moderation queue.</item>
///   <item>An administrator approves it; the status moves to <c>Approved</c> and
///   <see cref="Published"/> is set, which is what public queries filter on.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="CommentModerationStatus"/> for the legal status values.
/// The <c>BlogComment</c> table is created by <c>PostgresScripts/001-CreateTables.sql</c> and
/// extended with the whole anonymous-identity and moderation column set by
/// <c>014-AnonymousEngagement.sql</c>.</para>
///
/// <para><b>Usage:</b> Never render a comment whose <see cref="ModerationStatus"/> is not
/// <c>Approved</c> - an unconfirmed comment must never appear publicly.</para>
///
/// <para><b>Exposure.</b> This entity mixes public content with data that must never reach a
/// visitor. Only <see cref="GivenBy"/>, <see cref="Comment"/>, <see cref="GivenOn"/> and the thread
/// structure are publishable; <see cref="Email"/>, <see cref="AuthorIpAddress"/> and
/// <see cref="AuthorUserAgent"/> are not. Bind a public comment list to a projection rather than to
/// this type — serialising the whole entity into a component's render tree publishes the commenter's
/// address to everyone who reads the post.</para>
///
/// <para><b>Validation attributes.</b> The <c>[Required]</c>/<c>[RegularExpression]</c> annotations
/// below are for the admin edit form that binds this entity directly. Public submissions are
/// validated on <see cref="CommentSubmission"/> instead, whose rules are stricter and do not match
/// these — the two sets are independent, and neither is enforced by the database.</para>
/// </remarks>
public class BlogComment
{
    /// <summary>
    /// Surrogate primary key (<c>CommentId</c>, <c>BIGSERIAL</c>).
    /// </summary>
    /// <remarks>
    /// Note the casing difference between this property and the column — Dapper matches
    /// case-insensitively, so it maps, but a hand-written <c>SELECT</c> that aliases columns must not
    /// rely on the C# spelling. Zero until the row is inserted; referenced by
    /// <see cref="ParentCommentID"/> on replies and by the verification token's target id.
    /// </remarks>
    public long CommentID { get; set; }

    /// <summary>
    /// The post being commented on (<c>PostId BIGINT NOT NULL</c>, foreign key to <c>BlogPost</c>).
    /// </summary>
    /// <remarks>
    /// Required by the foreign key, so a comment cannot be orphaned. Because posts are soft-deleted
    /// rather than removed, comments on a deleted post survive and are still returned by any query
    /// that does not filter on the post's <see cref="BlogPost.IsDeleted"/> flag.
    /// </remarks>
    public long PostID { get; set; }

    /// <summary>
    /// When the comment was submitted (<c>GivenOn TIMESTAMP NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Set by the server from the submission, never by the visitor — a client-supplied time would let
    /// a commenter forge their position in a thread. Stored as UTC in a bare <c>TIMESTAMP</c>, so it
    /// materialises with <see cref="DateTimeKind.Unspecified"/>; see the timestamp note on
    /// <see cref="BlogPost"/>. It records submission, not approval, so an approved comment can carry
    /// a date well before it became visible.
    /// </remarks>
    public DateTime GivenOn { get; set; }

    /// <summary>
    /// The commenter's display name (<c>GivenBy VARCHAR(350) NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Supplied by an anonymous visitor and rendered publicly, so it is the most exposed untrusted
    /// string on the type — always HTML-encode it, and never treat it as an identity. Two different
    /// people may use the same name; <see cref="Email"/> is what actually identifies a commenter.
    /// <para>The column allows 350 characters but a public submission is capped at 150 by
    /// <see cref="CommentSubmission.AuthorName"/>, so only an admin edit can produce a longer
    /// value.</para>
    /// </remarks>
    [Required(ErrorMessage = "Name is required")]
    public string GivenBy { get; set; } = string.Empty;

    /// <summary>
    /// The commenter's email address (<c>Email VARCHAR(350) NOT NULL</c>) - the identity key for an
    /// anonymous commenter and the address the verification link is sent to.
    /// </summary>
    /// <remarks>
    /// Indexed case-insensitively (<c>IdxBlogCommentEmail</c> on <c>LOWER(Email)</c>), so
    /// <c>A@b.com</c> and <c>a@b.com</c> are the same commenter as far as the double opt-in flow is
    /// concerned; compare it the same way in C# rather than with a default ordinal equality.
    /// <para><b>Exposure: never render this on a public page.</b> It may be shown to administrators
    /// in the moderation queue and nowhere else. It also reaches the commenter themself, in the
    /// verification mail — and nothing more.</para>
    /// <para>The regular expression below restricts the top-level domain to 2-4 characters, which
    /// rejects addresses at longer modern TLDs (<c>.online</c>, <c>.technology</c>) that
    /// <see cref="CommentSubmission.AuthorEmail"/>'s <c>[EmailAddress]</c> check accepts. A comment
    /// submitted successfully by a visitor can therefore fail validation when an administrator opens
    /// it for editing.</para>
    /// </remarks>
    [Required(ErrorMessage = "Email is required")]
    [RegularExpression(@"^([a-zA-Z0-9_\.\-])+\@(([a-zA-Z0-9\-])+\.)+([a-zA-Z0-9]{2,4})+$", ErrorMessage = "Please Enter Correct Email Address")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The comment body (<c>Comment VARCHAR(850) NOT NULL</c>).
    /// </summary>
    /// <remarks>
    /// Untrusted visitor input published verbatim on a public page - it is plain text, <b>not</b>
    /// Markdown, and is never passed through <c>MarkdownRenderer</c>, so nothing sanitises it on the
    /// way out. It must be HTML-encoded at the render site; emitting it as raw markup is stored XSS.
    /// <para>The 850-character column limit matches the public submission cap on
    /// <see cref="CommentSubmission.CommentText"/>, so an over-long body is rejected before it
    /// reaches the database.</para>
    /// </remarks>
    [Required(ErrorMessage = "Comment is required")]
    public string Comment { get; set; } = string.Empty;

    /// <summary>
    /// Whether the comment is publicly visible (<c>Published BOOLEAN NOT NULL DEFAULT FALSE</c>).
    /// Kept in sync with <see cref="ModerationStatus"/> = <c>Approved</c>.
    /// </summary>
    /// <remarks>
    /// This is the older of the two visibility signals: it predates the moderation state machine
    /// added by migration 014, and the pair is maintained in application code with nothing in the
    /// schema tying them together. They can therefore disagree, and the failure is asymmetric - a
    /// comment with <c>Published = true</c> but a non-approved status is <b>visible</b>, because the
    /// public queries filter on this flag. When they conflict, trust
    /// <see cref="ModerationStatus"/> and correct this one.
    /// <para>Defaults to false, so a comment that skips the workflow stays hidden rather than
    /// leaking.</para>
    /// </remarks>
    public bool Published { get; set; }

    /// <summary>
    /// The comment this one replies to; null for a top-level comment
    /// (<c>ParentCommentId BIGINT</c>, self-referencing foreign key).
    /// </summary>
    /// <remarks>
    /// Zero was the historical sentinel for "top level"; migration 014 rewrote those rows to
    /// <c>NULL</c>, so null is now the only correct representation and a zero here would fail the
    /// foreign key. Nothing prevents a reply to an unapproved parent, so the thread builder must cope
    /// with a reply whose parent is not in the visible set rather than dropping it silently.
    /// </remarks>
    public long? ParentCommentID { get; set; }

    /// <summary>
    /// The signed-in user who left the comment, when there was one; null for anonymous visitors
    /// (<c>UserId BIGINT</c>, added by migration 014).
    /// </summary>
    /// <remarks>
    /// A convenience back-link only - it is <b>not</b> the identity of the commenter and carries no
    /// foreign key, so it can point at a deleted user. Since [REQ-FN-022] identity is the
    /// <see cref="GivenBy"/>/<see cref="Email"/> pair, and being signed in does not by itself skip
    /// the verification or moderation steps.
    /// </remarks>
    public long? UserId { get; set; }

    /// <summary>
    /// Whether the commenter's email address has been confirmed via the double opt-in link
    /// (<c>IsEmailVerified BOOLEAN NOT NULL DEFAULT FALSE</c>, added by migration 014).
    /// </summary>
    /// <remarks>
    /// Migration 014 back-filled every pre-existing comment to <c>true</c>, on the reasoning that
    /// rows predating the double opt-in flow were already moderated by hand; so a <c>true</c> here
    /// does not prove that a link was ever clicked. New rows default to false.
    /// <para>It is a fact about the address, not a visibility decision - a verified comment still
    /// waits in the moderation queue. Visibility is <see cref="ModerationStatus"/>.</para>
    /// </remarks>
    public bool IsEmailVerified { get; set; }

    /// <summary>
    /// The moderation state (<c>ModerationStatus VARCHAR(30) NOT NULL DEFAULT
    /// 'PendingVerification'</c>, added by migration 014). One of the
    /// <see cref="CommentModerationStatus"/> values.
    /// </summary>
    /// <remarks>
    /// The authoritative answer to "may this be shown?" - only <c>Approved</c> may be rendered, and
    /// only <c>PendingApproval</c> belongs in the moderation queue. It is a string rather than an
    /// enum so the value round-trips through the column without a converter, which also means there
    /// is no check constraint: a misspelled status is not an error, it is a comment that quietly
    /// matches no query and disappears from both the site and the queue. Always assign from
    /// <see cref="CommentModerationStatus"/>.
    /// <para>Indexed alone and jointly with the post id, which is why status filtering is cheap
    /// enough to apply on every public read.</para>
    /// </remarks>
    public string ModerationStatus { get; set; } = string.Empty;

    /// <summary>
    /// When the email address was confirmed; null while unconfirmed (<c>VerifiedOn TIMESTAMP</c>,
    /// added by migration 014).
    /// </summary>
    /// <remarks>
    /// Audit information, written at the same moment <see cref="IsEmailVerified"/> is set. Because
    /// migration 014 back-filled the flag but not this timestamp, a verified legacy comment can have
    /// no verification date - do not infer the flag from this being null.
    /// </remarks>
    public DateTime? VerifiedOn { get; set; }

    /// <summary>
    /// The IP address the comment was submitted from, retained for abuse forensics
    /// (<c>AuthorIpAddress VARCHAR(45)</c>, added by migration 014).
    /// </summary>
    /// <remarks>
    /// Captured by the host, not by the visitor. The 45-character width is sized for a full IPv6
    /// literal; behind a proxy it holds whatever the host resolved as the client address, which may
    /// be the proxy's own.
    /// <para><b>Exposure:</b> personal data under GDPR and a spam-evasion aid if leaked. Admin
    /// surfaces only - never render it beside a public comment, and keep it out of anything a visitor
    /// can reach.</para>
    /// </remarks>
    public string AuthorIpAddress { get; set; } = string.Empty;

    /// <summary>
    /// The submitting browser's user-agent string, retained for abuse forensics
    /// (<c>AuthorUserAgent VARCHAR(500)</c>, added by migration 014).
    /// </summary>
    /// <remarks>
    /// Entirely attacker-controlled - a bot sends whatever it likes - so it is evidence, never a
    /// control input, and must be encoded like any other untrusted string if an admin screen displays
    /// it. Same admin-only exposure rule as <see cref="AuthorIpAddress"/>.
    /// </remarks>
    public string AuthorUserAgent { get; set; } = string.Empty;

    /// <summary>
    /// The threaded replies to this comment. Assembled in memory by the repository from
    /// <see cref="ParentCommentID"/>; not a column.
    /// </summary>
    /// <remarks>
    /// Empty unless the caller used a query that builds the tree, so an empty list means "not
    /// assembled" as often as "no replies". Each child is a full <c>BlogComment</c> and carries the
    /// same non-public fields as its parent, so the exposure rules on the type apply to the whole
    /// tree - projecting only the root comment does not protect the replies.
    /// <para>A mutable <c>List</c> with a public setter: mutating it changes what every other holder
    /// of the same instance sees.</para>
    /// </remarks>
    public List<BlogComment> Replies { get; set; } = new();
}
