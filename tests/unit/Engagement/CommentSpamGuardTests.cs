using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for the anonymous-comment spam screen. [REQ-FN-022]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Opening comments to anonymous visitors removed the sign-in barrier, so
/// these tests pin the replacement: honeypot, submit timing, content heuristics and rate limits.</para>
/// <para><b>Code Flow:</b> Each test builds a submission that trips exactly one rule and asserts
/// the verdict, plus one test that a perfectly ordinary comment passes untouched.</para>
/// <para><b>Dependencies:</b> xUnit, <see cref="FakeBlogCommentRepo"/>.</para>
/// <para><b>Usage:</b> Pure unit tests - no database.</para>
/// </remarks>
public class CommentSpamGuardTests
{
    /// <summary>
    /// A hidden field that only an automated client would fill in blocks the submission.
    /// </summary>
    [Fact]
    public async Task SpamGuardBlocksFilledHoneypot()
    {
        var guard = BuildGuard(out _);
        var submission = BuildCleanSubmission();
        submission.HoneypotValue = "http://buy-things.example";

        var verdict = await guard.EvaluateAsync(submission);

        Assert.True(verdict.IsSpam);
    }

    /// <summary>
    /// A comment posted within a second of the form rendering cannot have been typed by a
    /// human, so it is refused.
    /// </summary>
    [Fact]
    public async Task SpamGuardBlocksInstantSubmission()
    {
        var guard = BuildGuard(out _);
        var submission = BuildCleanSubmission();
        submission.RenderedOn = DateTime.UtcNow;

        var verdict = await guard.EvaluateAsync(submission);

        Assert.True(verdict.IsSpam);
    }

    /// <summary>
    /// A body stuffed with more links than a real reply needs is refused.
    /// </summary>
    [Fact]
    public async Task SpamGuardBlocksLinkFarm()
    {
        var guard = BuildGuard(out _);
        var submission = BuildCleanSubmission();
        submission.CommentText =
            "https://one.example https://two.example https://three.example https://four.example";

        var verdict = await guard.EvaluateAsync(submission);

        Assert.True(verdict.IsSpam);
    }

    /// <summary>
    /// Embedded anchor markup in what should be plain text is refused.
    /// </summary>
    [Fact]
    public async Task SpamGuardBlocksEmbeddedMarkup()
    {
        var guard = BuildGuard(out _);
        var submission = BuildCleanSubmission();
        submission.CommentText = "Great post <a href=\"https://spam.example\">click here</a> thanks";

        var verdict = await guard.EvaluateAsync(submission);

        Assert.True(verdict.IsSpam);
    }

    /// <summary>
    /// Once an address has posted its hourly allowance, further comments from it are refused.
    /// </summary>
    [Fact]
    public async Task SpamGuardBlocksWhenEmailRateLimitReached()
    {
        var guard = BuildGuard(out var commentRepo);
        commentRepo.RecentByEmailCount = 5;

        var verdict = await guard.EvaluateAsync(BuildCleanSubmission());

        Assert.True(verdict.IsSpam);
    }

    /// <summary>
    /// Once an origin has posted its hourly allowance, further comments from it are refused
    /// even when each one uses a fresh address.
    /// </summary>
    [Fact]
    public async Task SpamGuardBlocksWhenIpRateLimitReached()
    {
        var guard = BuildGuard(out var commentRepo);
        commentRepo.RecentByIpCount = 15;

        var verdict = await guard.EvaluateAsync(BuildCleanSubmission());

        Assert.True(verdict.IsSpam);
    }

    /// <summary>
    /// An ordinary reader's comment, typed at human speed with no links, passes every gate.
    /// </summary>
    [Fact]
    public async Task SpamGuardAllowsOrdinaryComment()
    {
        var guard = BuildGuard(out _);

        var verdict = await guard.EvaluateAsync(BuildCleanSubmission());

        Assert.False(verdict.IsSpam);
    }

    /// <summary>
    /// A single link in an otherwise normal comment is tolerated - the guard scores rather than
    /// blocking, so genuine readers are not punished for citing a source.
    /// </summary>
    [Fact]
    public async Task SpamGuardToleratesSingleLink()
    {
        var guard = BuildGuard(out _);
        var submission = BuildCleanSubmission();
        submission.CommentText = "This matches what I read at https://docs.example - thanks for writing it up.";

        var verdict = await guard.EvaluateAsync(submission);

        Assert.False(verdict.IsSpam);
    }

    /// <summary>
    /// Builds a guard over a fake repository.
    /// </summary>
    /// <param name="commentRepo">Receives the fake repository so a test can tune the rate limits.</param>
    /// <returns>The guard under test.</returns>
    private static CommentSpamGuard BuildGuard(out FakeBlogCommentRepo commentRepo)
    {
        commentRepo = new FakeBlogCommentRepo();
        return new CommentSpamGuard(commentRepo, NullLogger<CommentSpamGuard>.Instance);
    }

    /// <summary>
    /// Builds a submission that trips none of the rules.
    /// </summary>
    /// <returns>A clean submission.</returns>
    private static CommentSubmission BuildCleanSubmission()
    {
        return new CommentSubmission
        {
            PostId = 7,
            AuthorName = "Ada Lovelace",
            AuthorEmail = "ada@example.com",
            CommentText = "A thoughtful reply about the analytical engine.",
            RenderedOn = DateTime.UtcNow.AddMinutes(-2),
            IpAddress = "203.0.113.7",
            UserAgent = "Mozilla/5.0"
        };
    }
}
