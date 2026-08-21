namespace BlogModels;

/// <summary>
/// The verdict of the comment spam guard on one submission.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Separates "reject outright" from "accept but flag for a human",
/// so borderline submissions are not silently lost. [REQ-FN-022]</para>
///
/// <para><b>Code Flow:</b> <c>CommentSpamGuard.Evaluate</c> runs a series of cheap heuristics
/// and accumulates a score; the service rejects when <see cref="IsSpam"/> is set and otherwise
/// records <see cref="Score"/> and <see cref="Reason"/> in the log.</para>
///
/// <para><b>Dependencies:</b> None - a plain DTO in the leaf model assembly.</para>
///
/// <para><b>Usage:</b> <see cref="Reason"/> is for the operator's log, never for the visitor;
/// telling a bot exactly which rule caught it just helps it evade the rule.</para>
/// </remarks>
public class SpamCheckResult
{
    /// <summary>
    /// Gets or sets whether the submission must be rejected outright. This is the only property
    /// that decides anything — a high <see cref="Score"/> with this left false is an accepted
    /// submission, by design, so that a borderline comment is kept for a human rather than lost.
    /// </summary>
    public bool IsSpam { get; set; }

    /// <summary>
    /// Gets or sets the accumulated suspicion score; higher is worse, zero is clean. The scale is
    /// internal to <c>CommentSpamGuard</c> and has no fixed maximum, so it is comparable between
    /// two submissions but carries no absolute meaning — do not derive a percentage or a
    /// confidence from it, and do not reimplement the threshold at a call site.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Gets or sets the internal explanation of the verdict — which heuristic fired. Null on a
    /// clean result.
    /// </summary>
    /// <remarks>
    /// Operator-facing only. It must never be returned to the submitter or rendered on a page: a
    /// rejection message that names the rule that caught the submission is a tuning guide for the
    /// next attempt. The visitor gets a generic refusal; the log gets this.
    /// </remarks>
    public string? Reason { get; set; }

    /// <summary>
    /// Creates a clean verdict.
    /// </summary>
    /// <returns>A result that allows the submission through.</returns>
    public static SpamCheckResult Clean()
    {
        return new SpamCheckResult { IsSpam = false, Score = 0, Reason = null };
    }

    /// <summary>
    /// Creates a rejecting verdict.
    /// </summary>
    /// <param name="score">The accumulated suspicion score.</param>
    /// <param name="reason">The internal explanation, for logging only.</param>
    /// <returns>A result that blocks the submission.</returns>
    public static SpamCheckResult Blocked(int score, string reason)
    {
        return new SpamCheckResult { IsSpam = true, Score = score, Reason = reason };
    }
}
