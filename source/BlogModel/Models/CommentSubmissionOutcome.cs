namespace BlogModels;

/// <summary>
/// What happened to an accepted comment submission, and what the visitor must be told.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published output contract of <c>CommentSvc.SubmitComment</c>.
/// The UI needs to distinguish "check your inbox" from "thanks, awaiting moderation",
/// and both differ from an outright rejection (which is a failed <c>Result</c>). [REQ-FN-022]</para>
///
/// <para><b>Code Flow:</b> The service persists the comment, decides whether the address is
/// already verified, and fills this object accordingly.</para>
///
/// <para><b>Dependencies:</b> <see cref="CommentModerationStatus"/> for <see cref="ModerationStatus"/>.</para>
///
/// <para><b>Usage:</b> Branch on <see cref="IsEmailVerificationRequired"/> for the confirmation message.</para>
///
/// <para><b>This object only ever describes success.</b> Rejections - a failed captcha, a spam
/// verdict, a token that could not be mailed - come back as a failed <c>Result</c> with no outcome
/// at all, and in the spam and captcha cases nothing was written. So the existence of an instance
/// already means "a comment row exists"; it never means "the comment is visible". Exactly two
/// endings are representable:</para>
/// <list type="bullet">
///   <item><c>PendingVerification</c> with <see cref="IsEmailVerificationRequired"/> true - a mail is
///   on its way and the comment is invisible until the link is clicked.</item>
///   <item><c>PendingApproval</c> or <c>Approved</c> with the flag false - the address was already
///   verified on an earlier comment, so the confirmation step was skipped.</item>
/// </list>
///
/// <para><b>Exposure:</b> everything here is safe to show the visitor who just submitted, and
/// nothing here identifies anyone else. It carries no address, no token and no id a visitor could
/// use to reach another comment.</para>
/// </remarks>
public class CommentSubmissionOutcome
{
    /// <summary>
    /// The identifier of the comment that was created.
    /// </summary>
    /// <remarks>
    /// Always a real, persisted <see cref="BlogComment.CommentID"/> - the outcome is only ever
    /// produced after the row is written. It is the token's target id in the verification flow, and
    /// it is useful for correlating logs; it is not an anchor a visitor can navigate to, because the
    /// comment is not yet visible.
    /// </remarks>
    public long CommentId { get; set; }

    /// <summary>
    /// The moderation state the comment landed in - one of the
    /// <see cref="CommentModerationStatus"/> values.
    /// </summary>
    /// <remarks>
    /// In practice only three of the five can appear here: <c>PendingVerification</c>,
    /// <c>PendingApproval</c> or <c>Approved</c>. <c>Rejected</c> and <c>Spam</c> are administrator
    /// and guard verdicts that never produce a successful outcome.
    /// <para>Do not render it raw - these are internal identifiers, not visitor-facing prose. Use
    /// <see cref="Message"/> for what the visitor reads, and this only for branching or logging.</para>
    /// </remarks>
    public string ModerationStatus { get; set; } = string.Empty;

    /// <summary>
    /// Whether a verification email was sent and the comment is waiting for the visitor to click
    /// the link.
    /// </summary>
    /// <remarks>
    /// The single flag the UI branches on: true means "we have emailed you", false means "we have
    /// queued it". It is true exactly when <see cref="ModerationStatus"/> is
    /// <c>PendingVerification</c>, so the two never disagree - prefer this flag, which states the
    /// intent rather than the state.
    /// <para>False does <b>not</b> mean the comment is visible; check
    /// <see cref="ModerationStatus"/> for <c>Approved</c> if that distinction matters.</para>
    /// </remarks>
    public bool IsEmailVerificationRequired { get; set; }

    /// <summary>
    /// The message to show the visitor.
    /// </summary>
    /// <remarks>
    /// Server-authored prose chosen to match the ending that occurred - never assembled from
    /// submitted data, so it is safe to display, though it should still be rendered as text rather
    /// than markup. It is deliberately non-committal about timing, because a queued comment may wait
    /// indefinitely for a moderator.
    /// <para>Not localised. It is written for the visitor, not for the log; log
    /// <see cref="ModerationStatus"/> instead.</para>
    /// </remarks>
    public string Message { get; set; } = string.Empty;
}
