using BlogEngine.Services;
using BlogModels;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Captcha stub that counts every call, so a test can prove the rate limiter stopped a request
/// before it reached the real service. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="FakeCaptchaService"/> records validations only. The rate-limit
/// tests also need to know whether a challenge was actually minted, because "the cap engaged"
/// means the inner service was never called at all.</para>
/// <para><b>Code Flow:</b> Each member increments its counter and returns a canned value; the
/// verdict of <see cref="Validate"/> is controlled by <see cref="IsAnswerAccepted"/>.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Wrap it in <c>RateLimitedCaptchaSvc</c> and assert on the counters.</para>
/// </remarks>
public class RecordingCaptchaService : ICaptchaService
{
    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Validate"/> should succeed.
    /// </summary>
    public bool IsAnswerAccepted { get; set; }

    /// <summary>
    /// Gets the number of visual challenges minted.
    /// </summary>
    public int GenerateCallCount { get; private set; }

    /// <summary>
    /// Gets the number of accessible question challenges minted.
    /// </summary>
    public int GenerateQuestionCallCount { get; private set; }

    /// <summary>
    /// Gets the number of validations that reached this service.
    /// </summary>
    public int ValidateCallCount { get; private set; }

    /// <inheritdoc />
    public CaptchaChallenge Generate()
    {
        GenerateCallCount++;
        return new CaptchaChallenge
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            Kind = CaptchaChallengeKind.Visual,
            SvgMarkup = "<svg />",
            ExpiresOn = DateTime.UtcNow.AddMinutes(5)
        };
    }

    /// <inheritdoc />
    public CaptchaChallenge GenerateQuestion()
    {
        GenerateQuestionCallCount++;
        return new CaptchaChallenge
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            Kind = CaptchaChallengeKind.Question,
            QuestionText = "What is two plus one?",
            ExpiresOn = DateTime.UtcNow.AddMinutes(5)
        };
    }

    /// <inheritdoc />
    public Result Validate(string challengeId, string answer)
    {
        ValidateCallCount++;
        return IsAnswerAccepted ? Result.Success() : Result.Failure("The characters did not match.");
    }
}
