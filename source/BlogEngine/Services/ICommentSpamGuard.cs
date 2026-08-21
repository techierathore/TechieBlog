using BlogModels;

namespace BlogEngine.Services;

/// <summary>
/// Screens comment submissions for automated abuse before anything is written.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The spam-protection half of [REQ-FN-022]. Opening comments to
/// anonymous visitors removes the sign-in barrier that used to keep bots out, so a cheap,
/// self-hosted screen replaces it.</para>
///
/// <para><b>Code Flow:</b> <c>CommentSvc</c> calls <see cref="EvaluateAsync"/> after the
/// captcha check and before the insert; a blocked verdict aborts the write.</para>
///
/// <para><b>Dependencies:</b> The comment repository, for the per-address and per-origin
/// rate-limit counts.</para>
///
/// <para><b>Usage:</b> The returned <see cref="SpamCheckResult.Reason"/> is for the operator's
/// log only - never show a bot which rule caught it.</para>
/// </remarks>
public interface ICommentSpamGuard
{
    /// <summary>
    /// Scores one submission and decides whether it may proceed.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Runs the cheap deterministic checks first (honeypot, submit
    /// speed) and only then the ones that need a database round trip (rate limits), so an
    /// obvious bot costs nothing.</para>
    /// <para><b>Flow:</b> honeypot, timing, content heuristics, then rate limits.</para>
    /// <para><b>Side Effects:</b> None - the guard reads, it never writes.</para>
    /// </remarks>
    /// <param name="submission">The raw visitor submission.</param>
    /// <returns>The verdict, with a score and an internal reason.</returns>
    Task<SpamCheckResult> EvaluateAsync(CommentSubmission submission);
}
