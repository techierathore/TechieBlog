using BlogEngine.Common;
using BlogEngine.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for the rate-limiting decorator that sits in front of the captcha service. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The limiter's counters are covered by
/// <see cref="CaptchaRateLimiterTests"/>; these tests prove the decorator applies them at the
/// right moments and produces the visitor-facing outcome the requirement asks for - a clear wait
/// message rather than a silent failure.</para>
/// <para><b>Code Flow:</b> Each test wraps a <see cref="RecordingCaptchaService"/> so it can
/// assert the inner service was never reached once a cap engaged.</para>
/// <para><b>Dependencies:</b> xUnit only.</para>
/// <para><b>Usage:</b> Pure unit tests; no HTTP context and no waiting.</para>
/// </remarks>
public class RateLimitedCaptchaSvcTests
{
    /// <summary>Fixed instant every test starts from.</summary>
    private static readonly DateTime StartTime = new(2026, 8, 7, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A visitor under the cap is served normally - the decorator must be invisible to ordinary
    /// use, or it has broken the feature it was meant to protect.
    /// </summary>
    [Fact]
    public void CaptchaIssuanceUnderTheCapIsUnaffected()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(issuePermitLimit: 5), () => StartTime);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.NotNull(service.Generate());
        }

        Assert.Equal(5, inner.GenerateCallCount);
    }

    /// <summary>
    /// The challenge after the issuance cap is refused with a rate-limit exception carrying a
    /// visitor-safe wait message, and the inner service is never called.
    /// </summary>
    [Fact]
    public void CaptchaIssuancePastTheCapIsRefused()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(issuePermitLimit: 2), () => StartTime);
        service.Generate();
        service.Generate();

        var refusal = Assert.Throws<CaptchaRateLimitedException>(() => service.Generate());

        Assert.Equal(2, inner.GenerateCallCount);
        Assert.Contains("wait", refusal.VisitorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(refusal.RetryAfter > TimeSpan.Zero);
    }

    /// <summary>
    /// The accessible question challenge shares the visual challenge's issuance budget, so the
    /// alternative route is not a way round the cap.
    /// </summary>
    [Fact]
    public void CaptchaQuestionChallengeSharesTheIssuanceCap()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(issuePermitLimit: 2), () => StartTime);
        service.Generate();
        service.GenerateQuestion();

        Assert.Throws<CaptchaRateLimitedException>(() => service.GenerateQuestion());
        Assert.Equal(1, inner.GenerateQuestionCallCount);
    }

    /// <summary>
    /// Once the issuance window elapses the visitor is served again without any operator action.
    /// </summary>
    [Fact]
    public void CaptchaIssuanceResumesAfterTheWindow()
    {
        var inner = new RecordingCaptchaService();
        var now = StartTime;
        var service = BuildService(inner, BuildOptions(issuePermitLimit: 1, issueWindowSeconds: 60), () => now);
        service.Generate();
        Assert.Throws<CaptchaRateLimitedException>(() => service.Generate());

        now = StartTime.AddSeconds(61);

        Assert.NotNull(service.Generate());
    }

    /// <summary>
    /// Wrong answers below the failure cap keep the captcha's own message, so an ordinary visitor
    /// who misreads the image is told what actually happened.
    /// </summary>
    [Fact]
    public void CaptchaWrongAnswerUnderTheCapKeepsItsOwnMessage()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(failurePermitLimit: 3), () => StartTime);

        var result = service.Validate("challenge-id", "WRONG");

        Assert.True(result.IsFailure);
        Assert.Equal("The characters did not match.", result.ErrorMessage);
    }

    /// <summary>
    /// The wrong answer that reaches the failure cap is answered with the wait message instead,
    /// which is what the visitor sees inline under the control.
    /// </summary>
    [Fact]
    public void CaptchaFailurePastTheCapReturnsTheWaitMessage()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(failurePermitLimit: 3), () => StartTime);
        service.Validate("challenge-id", "WRONG");
        service.Validate("challenge-id", "WRONG");

        var result = service.Validate("challenge-id", "WRONG");

        Assert.True(result.IsFailure);
        Assert.Contains("wait", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A client in failure lockout is refused before the challenge store is consulted at all, so
    /// it cannot keep burning challenge ids while it waits.
    /// </summary>
    [Fact]
    public void CaptchaValidationStopsReachingTheServiceOnceLockedOut()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(failurePermitLimit: 2), () => StartTime);
        service.Validate("challenge-id", "WRONG");
        service.Validate("challenge-id", "WRONG");
        var callsBefore = inner.ValidateCallCount;

        service.Validate("challenge-id", "WRONG");

        Assert.Equal(callsBefore, inner.ValidateCallCount);
    }

    /// <summary>
    /// A client in failure lockout is not handed fresh challenges either, so the answer cache
    /// stops growing for exactly the client already judged abusive.
    /// </summary>
    [Fact]
    public void CaptchaLockoutAlsoStopsNewChallenges()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(failurePermitLimit: 2), () => StartTime);
        service.Validate("challenge-id", "WRONG");
        service.Validate("challenge-id", "WRONG");

        Assert.Throws<CaptchaRateLimitedException>(() => service.Generate());
    }

    /// <summary>
    /// The failure lockout lifts once its window elapses, and the visitor is served again.
    /// </summary>
    [Fact]
    public void CaptchaLockoutLiftsAfterTheFailureWindow()
    {
        var inner = new RecordingCaptchaService();
        var now = StartTime;
        var service = BuildService(inner, BuildOptions(failurePermitLimit: 2, failureWindowSeconds: 300), () => now);
        service.Validate("challenge-id", "WRONG");
        service.Validate("challenge-id", "WRONG");
        Assert.Throws<CaptchaRateLimitedException>(() => service.Generate());

        now = StartTime.AddSeconds(301);

        Assert.NotNull(service.Generate());
    }

    /// <summary>
    /// Submitting the form with an empty answer box is a filling-in mistake, not a guess, so it
    /// never spends the visitor's failure budget.
    /// </summary>
    [Fact]
    public void CaptchaBlankAnswerDoesNotSpendTheFailureBudget()
    {
        var inner = new RecordingCaptchaService();
        var service = BuildService(inner, BuildOptions(failurePermitLimit: 2), () => StartTime);

        service.Validate("challenge-id", string.Empty);
        service.Validate("challenge-id", "   ");
        service.Validate(string.Empty, "ANSWER");

        Assert.NotNull(service.Generate());
    }

    /// <summary>
    /// A correct answer clears nothing but costs nothing either - a successful visitor never
    /// accumulates failure credit.
    /// </summary>
    [Fact]
    public void CaptchaCorrectAnswerCostsNoFailureBudget()
    {
        var inner = new RecordingCaptchaService { IsAnswerAccepted = true };
        var service = BuildService(inner, BuildOptions(failurePermitLimit: 1), () => StartTime);

        var result = service.Validate("challenge-id", "RIGHT");

        Assert.True(result.IsSuccess);
        Assert.NotNull(service.Generate());
    }

    /// <summary>
    /// One client hitting the cap leaves another client untouched, end to end through the
    /// decorator rather than only inside the counter.
    /// </summary>
    [Fact]
    public void CaptchaCapsAreScopedToTheClientKey()
    {
        var inner = new RecordingCaptchaService();
        var keyProvider = new StubCaptchaClientKeyProvider { ClientKey = "client-one" };
        var service = BuildService(inner, BuildOptions(issuePermitLimit: 1), () => StartTime, keyProvider);
        service.Generate();
        Assert.Throws<CaptchaRateLimitedException>(() => service.Generate());

        keyProvider.ClientKey = "client-two";

        Assert.NotNull(service.Generate());
    }

    /// <summary>
    /// Builds the decorator over a recording inner service.
    /// </summary>
    /// <param name="inner">The captcha service being protected.</param>
    /// <param name="options">The caps under test.</param>
    /// <param name="clock">The test clock.</param>
    /// <param name="keyProvider">Optional client-key stub; a fixed single client by default.</param>
    /// <returns>The decorator.</returns>
    private static RateLimitedCaptchaSvc BuildService(
        RecordingCaptchaService inner,
        CaptchaRateLimitOptions options,
        Func<DateTime> clock,
        StubCaptchaClientKeyProvider? keyProvider = null)
    {
        var limiter = new CaptchaRateLimiter(options, clock, NullLogger<CaptchaRateLimiter>.Instance);
        return new RateLimitedCaptchaSvc(
            inner,
            limiter,
            keyProvider ?? new StubCaptchaClientKeyProvider(),
            NullLogger<RateLimitedCaptchaSvc>.Instance);
    }

    /// <summary>
    /// Builds a caps object with small, readable numbers.
    /// </summary>
    /// <param name="issuePermitLimit">Challenges permitted per issuance window.</param>
    /// <param name="issueWindowSeconds">Length of the issuance window.</param>
    /// <param name="failurePermitLimit">Failures permitted per failure window.</param>
    /// <param name="failureWindowSeconds">Length of the failure window.</param>
    /// <returns>The caps.</returns>
    private static CaptchaRateLimitOptions BuildOptions(
        int issuePermitLimit = 20,
        int issueWindowSeconds = 60,
        int failurePermitLimit = 5,
        int failureWindowSeconds = 300)
    {
        return new CaptchaRateLimitOptions
        {
            IssuePermitLimit = issuePermitLimit,
            IssueWindowSeconds = issueWindowSeconds,
            FailurePermitLimit = failurePermitLimit,
            FailureWindowSeconds = failureWindowSeconds
        };
    }
}
