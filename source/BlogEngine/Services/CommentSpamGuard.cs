using System.Text.RegularExpressions;
using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Heuristic, self-hosted spam screen for anonymous comment submissions.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps automated submissions out of the moderation queue now that
/// comments no longer require a signed-in user. [REQ-FN-022]</para>
///
/// <para><b>Code Flow:</b> Four layers, cheapest first - a hidden honeypot field, a minimum
/// time on the form, content heuristics (link count, markup, shouting, repetition, known spam
/// vocabulary) and finally per-address and per-origin rate limits that need the database.
/// Hard signals block outright; soft signals accumulate a score and block once it crosses the
/// threshold.</para>
///
/// <para><b>Dependencies:</b> <see cref="IBlogCommentRepo"/> for the rate-limit counts and
/// <see cref="ILogger{TCategoryName}"/> for the security log.</para>
///
/// <para><b>Usage:</b> Deliberately conservative: it is better to let a borderline comment into
/// the moderation queue, where a human sees it, than to silently discard a real reader.</para>
/// </remarks>
public class CommentSpamGuard : ICommentSpamGuard
{
    /// <summary>A human cannot read a post and write a comment in fewer seconds than this.</summary>
    private const int MinimumFormSeconds = 3;

    /// <summary>Links tolerated in a comment body before it looks like an advert.</summary>
    private const int MaximumLinkCount = 2;

    /// <summary>Score at or above which a submission is refused.</summary>
    private const int SpamScoreThreshold = 5;

    /// <summary>Length above which the shouting and repetition checks start to apply.</summary>
    private const int ShoutingLengthFloor = 20;

    /// <summary>Comments one address may post inside the rate-limit window.</summary>
    private const int MaximumCommentsPerEmail = 5;

    /// <summary>Comments one origin may post inside the rate-limit window.</summary>
    private const int MaximumCommentsPerIp = 15;

    /// <summary>Width of the rate-limit window.</summary>
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

    /// <summary>Matches anything that looks like a hyperlink in the body.</summary>
    private static readonly Regex LinkPattern = new(
        @"(https?://|www\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Matches embedded markup a plain-text comment has no business containing.</summary>
    private static readonly Regex MarkupPattern = new(
        @"(<\s*a\s|<\s*script|\[url|\[link)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Matches a character repeated five or more times, a classic filler signature.</summary>
    private static readonly Regex RepetitionPattern = new(
        @"(.)\1{4,}", RegexOptions.Compiled);

    /// <summary>Vocabulary that overwhelmingly indicates comment spam.</summary>
    private static readonly string[] SpamVocabulary =
    {
        "viagra", "cialis", "casino", "porn", "escort", "payday loan",
        "crypto giveaway", "binary option", "buy followers", "seo services",
        "replica watch", "work from home"
    };

    private readonly IBlogCommentRepo blogCommentRepo;
    private readonly ILogger<CommentSpamGuard> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommentSpamGuard"/> class.
    /// </summary>
    /// <param name="blogCommentRepo">Repository used for the rate-limit counts.</param>
    /// <param name="logger">Logger for security events.</param>
    public CommentSpamGuard(IBlogCommentRepo blogCommentRepo, ILogger<CommentSpamGuard> logger)
    {
        this.blogCommentRepo = blogCommentRepo;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<SpamCheckResult> EvaluateAsync(CommentSubmission submission)
    {
        if (submission == null)
            return SpamCheckResult.Blocked(100, "Null submission");

        var hardVerdict = CheckHardSignals(submission);
        if (hardVerdict != null)
            return LogBlocked(submission, hardVerdict);

        var score = ScoreContent(submission.CommentText);
        if (score >= SpamScoreThreshold)
            return LogBlocked(submission, SpamCheckResult.Blocked(score, "Content heuristics"));

        var rateVerdict = await CheckRateLimitsAsync(submission).ConfigureAwait(false);
        if (rateVerdict != null)
            return LogBlocked(submission, rateVerdict);

        return new SpamCheckResult { IsSpam = false, Score = score, Reason = null };
    }

    /// <summary>
    /// Applies the signals that block on their own, with no scoring.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A filled honeypot means a machine filled every input it
    /// found; an instant submit means the form was never read. Neither has a false positive
    /// worth worrying about.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="submission">The submission under test.</param>
    /// <returns>A blocking verdict, or null when nothing fired.</returns>
    private static SpamCheckResult? CheckHardSignals(CommentSubmission submission)
    {
        if (!string.IsNullOrWhiteSpace(submission.HoneypotValue))
            return SpamCheckResult.Blocked(100, "Honeypot field was filled");

        if (submission.RenderedOn == default)
            return null;

        var elapsed = DateTime.UtcNow - submission.RenderedOn;
        if (elapsed.TotalSeconds < MinimumFormSeconds)
            return SpamCheckResult.Blocked(80, "Form submitted faster than a human can type");

        return null;
    }

    /// <summary>
    /// Accumulates a suspicion score from the comment body.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Each heuristic contributes points rather than blocking, so
    /// one enthusiastic reader quoting a URL is not mistaken for a bot.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="commentText">The comment body.</param>
    /// <returns>The accumulated score.</returns>
    private static int ScoreContent(string commentText)
    {
        if (string.IsNullOrWhiteSpace(commentText))
            return 0;

        var score = 0;
        if (LinkPattern.Matches(commentText).Count > MaximumLinkCount)
            score += 5;

        if (MarkupPattern.IsMatch(commentText))
            score += 5;

        if (RepetitionPattern.IsMatch(commentText))
            score += 2;

        score += ScoreShouting(commentText);
        score += ScoreVocabulary(commentText);
        return score;
    }

    /// <summary>
    /// Scores a body written almost entirely in capitals.
    /// </summary>
    /// <param name="commentText">The comment body.</param>
    /// <returns>Points contributed.</returns>
    private static int ScoreShouting(string commentText)
    {
        if (commentText.Length < ShoutingLengthFloor)
            return 0;

        var letterCount = commentText.Count(char.IsLetter);
        if (letterCount == 0)
            return 0;

        var upperCount = commentText.Count(char.IsUpper);
        return upperCount * 100 / letterCount > 80 ? 2 : 0;
    }

    /// <summary>
    /// Scores the presence of well-known spam vocabulary.
    /// </summary>
    /// <param name="commentText">The comment body.</param>
    /// <returns>Points contributed.</returns>
    private static int ScoreVocabulary(string commentText)
    {
        var hits = SpamVocabulary.Count(term =>
            commentText.Contains(term, StringComparison.OrdinalIgnoreCase));
        return hits * 3;
    }

    /// <summary>
    /// Applies the per-address and per-origin rate limits.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The address limit stops one identity flooding a thread; the
    /// origin limit catches a bot cycling through disposable addresses from one host. The
    /// origin limit is looser because offices and mobile carriers share addresses.</para>
    /// <para><b>Side Effects:</b> Two read-only database round trips.</para>
    /// </remarks>
    /// <param name="submission">The submission under test.</param>
    /// <returns>A blocking verdict, or null when both limits are clear.</returns>
    private async Task<SpamCheckResult?> CheckRateLimitsAsync(CommentSubmission submission)
    {
        var since = DateTime.UtcNow.Subtract(RateLimitWindow);

        var byEmail = await blogCommentRepo
            .CountRecentByEmailAsync(submission.AuthorEmail, since).ConfigureAwait(false);
        if (byEmail >= MaximumCommentsPerEmail)
            return SpamCheckResult.Blocked(60, "Per-address rate limit exceeded");

        if (string.IsNullOrWhiteSpace(submission.IpAddress))
            return null;

        var byIp = await blogCommentRepo
            .CountRecentByIpAsync(submission.IpAddress, since).ConfigureAwait(false);
        if (byIp >= MaximumCommentsPerIp)
            return SpamCheckResult.Blocked(60, "Per-origin rate limit exceeded");

        return null;
    }

    /// <summary>
    /// Records a blocked submission in the security log.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The email address and origin are logged so an operator can
    /// spot a campaign; the comment body is not, to keep the log free of injected payloads.</para>
    /// <para><b>Side Effects:</b> Writes one warning entry.</para>
    /// </remarks>
    /// <param name="submission">The submission that was blocked.</param>
    /// <param name="verdict">The blocking verdict.</param>
    /// <returns>The same verdict, so callers can return it directly.</returns>
    private SpamCheckResult LogBlocked(CommentSubmission submission, SpamCheckResult verdict)
    {
        logger.LogWarning(
            "Comment submission on post {PostId} from {Email} / {IpAddress} blocked: {Reason} (score {Score})",
            submission.PostId, submission.AuthorEmail, submission.IpAddress, verdict.Reason, verdict.Score);
        return verdict;
    }
}
