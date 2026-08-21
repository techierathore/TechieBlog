using BlogEngine.Services;
using BlogModels;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Captcha stub whose verdict a test controls directly.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Keeps the comment and rating tests focused on their own rules; the
/// captcha's own behaviour is covered by <see cref="CaptchaSvcTests"/>.</para>
/// <para><b>Code Flow:</b> <see cref="Validate"/> returns success or failure according to
/// <see cref="IsAnswerAccepted"/> and records that it was called.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> Set <see cref="IsAnswerAccepted"/> to false to simulate a wrong answer.</para>
/// </remarks>
public class FakeCaptchaService : ICaptchaService
{
    /// <summary>
    /// Gets or sets a value indicating whether <see cref="Validate"/> should succeed.
    /// </summary>
    public bool IsAnswerAccepted { get; set; } = true;

    /// <summary>
    /// Gets the number of times <see cref="Validate"/> was called.
    /// </summary>
    public int ValidateCallCount { get; private set; }

    /// <inheritdoc />
    public CaptchaChallenge Generate()
    {
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
        return new CaptchaChallenge
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            Kind = CaptchaChallengeKind.Question,
            QuestionText = "What is two plus two?",
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
