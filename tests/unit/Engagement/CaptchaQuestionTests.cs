using BlogEngine.Common;
using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for the accessible alternative captcha challenge. [REQ-UI-057]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Proves the acceptance of REQ-UI-057 at the service level: the
/// alternative is generated from the .NET base class library alone, its answer is never written
/// into the text that is sent to the browser, and it rides the same single-use, five-minute
/// lifecycle as the image challenge rather than being a softer parallel route.</para>
/// <para><b>Code Flow:</b> Each test builds a <see cref="CaptchaSvc"/> over a real
/// <see cref="MemoryCache"/>. Tests that need to answer correctly use
/// <see cref="PinnedQuestionCaptchaSvc"/>, which pins the question and therefore the answer.</para>
/// <para><b>Dependencies:</b> xUnit, Microsoft.Extensions.Caching.Memory.</para>
/// <para><b>Usage:</b> Pure unit tests - no database, no network, no browser.</para>
/// </remarks>
public class CaptchaQuestionTests
{
    /// <summary>How many independently generated questions the sampling tests inspect.</summary>
    private const int SampleSize = 400;

    /// <summary>The question the pinned test double always asks.</summary>
    private const string PinnedQuestionText = "What is seven plus two?";

    /// <summary>
    /// A generated question is real prose with a question mark, so a screen reader has something
    /// meaningful to read out rather than a blank or a placeholder.
    /// </summary>
    [Fact]
    public void QuestionChallengeCarriesReadableProse()
    {
        var service = BuildService();

        var challenge = service.GenerateQuestion();

        Assert.Equal(CaptchaChallengeKind.Question, challenge.Kind);
        Assert.EndsWith("?", challenge.QuestionText, StringComparison.Ordinal);
        Assert.True(challenge.QuestionText.Length > 10);
    }

    /// <summary>
    /// A question challenge carries no image, so nothing about it depends on sight.
    /// </summary>
    [Fact]
    public void QuestionChallengeCarriesNoImage()
    {
        var service = BuildService();

        var challenge = service.GenerateQuestion();

        Assert.Equal(string.Empty, challenge.SvgMarkup);
    }

    /// <summary>
    /// The answer never appears inside the question, so it cannot be lifted out of the rendered
    /// page, out of an aria-label or out of the response payload - the same guarantee the image
    /// challenge had to be hardened to give. Checked over a large sample because the question is
    /// randomly generated.
    /// </summary>
    [Fact]
    public void QuestionNeverContainsItsOwnAnswer()
    {
        for (var sample = 0; sample < SampleSize; sample++)
        {
            var question = CaptchaQuestionSet.Build();

            Assert.True(
                CaptchaQuestionSet.IsAnswerHidden(question),
                $"The answer leaked into the question text: {question.QuestionText}");
        }
    }

    /// <summary>
    /// Every generated question is answerable as a number, and both the digits and the English
    /// word are accepted, so a visitor who hears "what is seven plus two" may type either.
    /// </summary>
    [Fact]
    public void QuestionAcceptsDigitsAndWords()
    {
        for (var sample = 0; sample < SampleSize; sample++)
        {
            var question = CaptchaQuestionSet.Build();

            Assert.Equal(2, question.AcceptedAnswers.Count);
            Assert.True(int.TryParse(question.AcceptedAnswers[0], out _));
            Assert.All(question.AcceptedAnswers, answer => Assert.False(string.IsNullOrWhiteSpace(answer)));
        }
    }

    /// <summary>
    /// The question text is never empty and never contains a digit, because a digit in the prose
    /// would be one parse away from being the answer.
    /// </summary>
    [Fact]
    public void QuestionTextContainsNoDigits()
    {
        for (var sample = 0; sample < SampleSize; sample++)
        {
            var question = CaptchaQuestionSet.Build();

            Assert.False(
                question.QuestionText.Any(char.IsDigit),
                $"A digit reached the browser inside the question: {question.QuestionText}");
        }
    }

    /// <summary>
    /// A candidate whose answer is spelled into its own question is rejected by the screen, which
    /// is what stops a multiple-choice style question ever being issued.
    /// </summary>
    [Fact]
    public void AnswerHiddenScreenCatchesALeak()
    {
        var leaky = new CaptchaQuestion("Which one of these is four: four, six, nine?", new[] { "4", "four" });

        Assert.False(CaptchaQuestionSet.IsAnswerHidden(leaky));
    }

    /// <summary>
    /// The generator produces genuinely varied questions rather than one repeated shape, so an
    /// attacker cannot write a single parser and be done.
    /// </summary>
    [Fact]
    public void QuestionGeneratorProducesVariety()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        for (var sample = 0; sample < SampleSize; sample++)
        {
            texts.Add(CaptchaQuestionSet.Build().QuestionText);
        }

        Assert.True(texts.Count > 20, $"Only {texts.Count} distinct questions in {SampleSize} draws");
    }

    /// <summary>
    /// The digits form of the answer is accepted by the service.
    /// </summary>
    [Fact]
    public void QuestionAcceptsCorrectDigitAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.GenerateQuestion();

        var result = service.Validate(challenge.ChallengeId, "9");

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// The English word for the answer is accepted too, case-insensitively and with stray spaces
    /// ignored, because a visitor working from speech has no case or spacing cue.
    /// </summary>
    [Fact]
    public void QuestionAcceptsCorrectWordAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.GenerateQuestion();

        var result = service.Validate(challenge.ChallengeId, "  NiNe ");

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A wrong answer is refused, so the accessible route is a real gate and not a way past one.
    /// </summary>
    [Fact]
    public void QuestionRejectsWrongAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.GenerateQuestion();

        var result = service.Validate(challenge.ChallengeId, "3");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// An empty answer is refused rather than treated as a pass.
    /// </summary>
    [Fact]
    public void QuestionRejectsEmptyAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.GenerateQuestion();

        var result = service.Validate(challenge.ChallengeId, "   ");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// A correct answer cannot be replayed: the second attempt against the same challenge id
    /// fails, exactly as it does for the image challenge.
    /// </summary>
    [Fact]
    public void QuestionChallengeIsSingleUse()
    {
        var service = BuildPinnedService();
        var challenge = service.GenerateQuestion();

        var first = service.Validate(challenge.ChallengeId, "nine");
        var second = service.Validate(challenge.ChallengeId, "nine");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
    }

    /// <summary>
    /// A wrong answer also burns the question, so a visitor - or a bot - cannot sit on one
    /// question and guess every number.
    /// </summary>
    [Fact]
    public void QuestionBurnsChallengeOnWrongAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.GenerateQuestion();

        service.Validate(challenge.ChallengeId, "3");
        var retry = service.Validate(challenge.ChallengeId, "nine");

        Assert.True(retry.IsFailure);
    }

    /// <summary>
    /// A question challenge carries the same five-minute absolute expiry as the image challenge,
    /// and once the entry is gone the answer that was correct a moment ago is refused.
    /// </summary>
    [Fact]
    public void QuestionChallengeExpires()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PinnedQuestionCaptchaSvc(cache, NullLogger<CaptchaSvc>.Instance);
        var challenge = service.GenerateQuestion();

        cache.Remove("Captcha:" + challenge.ChallengeId);
        var result = service.Validate(challenge.ChallengeId, "nine");

        Assert.True(result.IsFailure);
        Assert.True(challenge.ExpiresOn <= DateTime.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// Asking for a different question issues a genuinely new challenge rather than re-serving
    /// the previous one.
    /// </summary>
    [Fact]
    public void QuestionReloadIssuesNewChallenge()
    {
        var service = BuildService();

        var first = service.GenerateQuestion();
        var second = service.GenerateQuestion();

        Assert.NotEqual(first.ChallengeId, second.ChallengeId);
    }

    /// <summary>
    /// A question challenge id cannot be answered with the code of a visual one and vice versa:
    /// each id resolves to exactly the expectation that was stored with it.
    /// </summary>
    [Fact]
    public void QuestionAndImageChallengesDoNotCrossValidate()
    {
        var service = BuildPinnedService();
        var visual = service.Generate();
        var question = service.GenerateQuestion();

        var visualAnsweredAsQuestion = service.Validate(visual.ChallengeId, "nine");
        var questionAnsweredAsVisual = service.Validate(question.ChallengeId, "AB3KW");

        Assert.True(visualAnsweredAsQuestion.IsFailure);
        Assert.True(questionAnsweredAsVisual.IsFailure);
    }

    /// <summary>
    /// Nothing the browser receives reveals the answer: the DTO that crosses the wire carries the
    /// question, an opaque id and an expiry, and the answer appears in none of them. This is the
    /// property that failed for the image challenge before REQ-FN-049 was hardened.
    /// </summary>
    [Fact]
    public void QuestionAnswerNeverReachesClient()
    {
        var service = BuildPinnedService();

        var challenge = service.GenerateQuestion();
        var payload = $"{challenge.QuestionText}|{challenge.SvgMarkup}|{challenge.Kind}|{challenge.ExpiresOn:O}";

        Assert.DoesNotContain("nine", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("9", payload.Replace(challenge.ExpiresOn.ToString("O"), string.Empty), StringComparison.Ordinal);
        Assert.Equal(PinnedQuestionText, challenge.QuestionText);
    }

    /// <summary>
    /// Builds a captcha service over a real in-memory cache.
    /// </summary>
    /// <returns>The service under test.</returns>
    private static CaptchaSvc BuildService()
    {
        return new CaptchaSvc(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CaptchaSvc>.Instance);
    }

    /// <summary>
    /// Builds a captcha service whose question - and therefore answer - is known to the test.
    /// </summary>
    /// <returns>The service under test.</returns>
    private static CaptchaSvc BuildPinnedService()
    {
        return new PinnedQuestionCaptchaSvc(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CaptchaSvc>.Instance);
    }

    /// <summary>
    /// A captcha service that always asks the same question and always draws the same code.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> The question is generated randomly in production, so a test that
    /// needs to answer it correctly has to be told what it is.</para>
    /// <para><b>Code Flow:</b> Overrides only the two random draws; storage, expiry, single-use
    /// and comparison all run the production path.</para>
    /// <para><b>Dependencies:</b> <see cref="CaptchaSvc"/>.</para>
    /// <para><b>Usage:</b> Tests only. Never registered in the container.</para>
    /// </remarks>
    private sealed class PinnedQuestionCaptchaSvc : CaptchaSvc
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PinnedQuestionCaptchaSvc"/> class.
        /// </summary>
        /// <param name="memoryCache">Short-lived store for expected answers.</param>
        /// <param name="logger">Logger for security events.</param>
        public PinnedQuestionCaptchaSvc(IMemoryCache memoryCache, ILogger<CaptchaSvc> logger)
            : base(memoryCache, logger)
        {
        }

        /// <summary>
        /// Returns the pinned question instead of a random draw.
        /// </summary>
        /// <returns>The pinned question.</returns>
        protected override CaptchaQuestion BuildQuestion()
        {
            return new CaptchaQuestion(PinnedQuestionText, new[] { "9", "nine" });
        }

        /// <summary>
        /// Returns a pinned code instead of a random draw.
        /// </summary>
        /// <returns>The pinned code.</returns>
        protected override string BuildRandomCode()
        {
            return "AB3KW";
        }
    }
}
