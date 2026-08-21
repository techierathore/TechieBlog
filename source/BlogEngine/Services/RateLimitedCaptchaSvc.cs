using BlogEngine.Common;
using BlogModels;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Wraps the captcha service with per-client caps on challenge issuance and failed answers.
/// [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="CaptchaSvc"/> makes any ONE challenge safe - single use, five
/// minute expiry, answer never sent to the client. It says nothing about volume, so the captcha
/// could still be brute-forced across a stream of fresh challenges, or used to grow the
/// server-side answer cache indefinitely. This decorator adds the missing volume control without
/// touching challenge generation, the SVG renderer or the widget.</para>
///
/// <para><b>Why a decorator — this is the load-bearing design decision.</b> Every caller - the
/// widget, <c>CommentSvc</c>, <c>RatingSvc</c> and the newsletter subscribe card - already talks to
/// <see cref="ICaptchaService"/>. The composition root registers <i>this</i> type as
/// <see cref="ICaptchaService"/> and the unlimited <see cref="CaptchaSvc"/> only as its concrete
/// self, so resolving the interface can only ever yield the limited version. That is what
/// guarantees <b>no path reaches an unlimited captcha</b>: the guarantee is structural, not a rule
/// each caller has to remember. The alternative — putting the counters inside
/// <see cref="CaptchaSvc"/> — would have entangled volume control with challenge generation and
/// made both harder to test; the alternative of asking each caller to check first would have been
/// one forgotten call away from a hole. <b>Do not register <see cref="CaptchaSvc"/> directly as
/// <see cref="ICaptchaService"/>, and do not inject the concrete <see cref="CaptchaSvc"/> into a
/// new caller</b> — either move silently removes the cap.</para>
///
/// <para><b>The two caps, and how they are configured.</b> Two independent per-client fixed
/// windows, defaulting to <b>20 issuances per 60 seconds</b> and <b>5 failures per 300
/// seconds</b>. Both are tunable under the existing <c>RateLimiting</c> configuration section that
/// REQ-NFR-005 established for the authentication endpoints, so a deployment tunes every limiter in
/// one place:
/// <c>RateLimiting:CaptchaIssuePermitLimit</c>, <c>RateLimiting:CaptchaIssueWindowSeconds</c>,
/// <c>RateLimiting:CaptchaFailurePermitLimit</c>, <c>RateLimiting:CaptchaFailureWindowSeconds</c>.
/// A value that is <b>missing, zero, negative or unparsable falls back to the compiled default
/// rather than being taken literally</b> — a security cap must never be switched off by a typo, and
/// "0" must never be read as "unlimited". See <c>CaptchaRateLimitOptions.FromConfiguration</c> for
/// the numbers and the reasoning behind them.</para>
///
/// <para><b>Counters are per process.</b> <c>CaptchaRateLimiter</c> holds its two windows in
/// in-memory dictionaries with no shared backing store. <b>A multi-instance deployment therefore
/// divides every cap by the instance count</b> from an attacker's point of view: with four
/// instances behind a round-robin load balancer, a client can obtain roughly 80 challenges a minute
/// and burn roughly 20 failures per five minutes before any single instance refuses it. State is
/// also lost on restart, which resets an active lockout. Both are acceptable for a single-instance
/// blog and are why the caps are set conservatively; before scaling out, move the counters to a
/// shared store (or pin clients to an instance) rather than assuming the published numbers still
/// hold.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>Both generators refuse outright while the client is in failure lockout, then consume an
///   issuance permit. A refusal is a <see cref="CaptchaRateLimitedException"/>, because
///   <see cref="Generate"/> returns a challenge and has no failure channel.</item>
///   <item><see cref="Validate"/> refuses before consulting the challenge store while the client is
///   locked out, so a locked-out client cannot even burn other people's challenge ids.</item>
///   <item>A rejected answer is recorded, and the answer that crosses the cap gets the wait message
///   instead of the ordinary "that did not match" wording.</item>
/// </list>
///
/// <para><b>Dependencies:</b> the inner <see cref="ICaptchaService"/>,
/// <see cref="ICaptchaRateLimiter"/> for the counters, <see cref="ICaptchaClientKeyProvider"/> for
/// the identity and <see cref="ILogger{TCategoryName}"/> for the security log.</para>
///
/// <para><b>Usage:</b> Registered per scope as <see cref="ICaptchaService"/> over the singleton
/// <see cref="CaptchaSvc"/>; the scope is what makes the circuit fallback key stable.</para>
/// </remarks>
public class RateLimitedCaptchaSvc : ICaptchaService
{
    private readonly ICaptchaService inner;
    private readonly ICaptchaRateLimiter rateLimiter;
    private readonly ICaptchaClientKeyProvider clientKeyProvider;
    private readonly ILogger<RateLimitedCaptchaSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitedCaptchaSvc"/> class.
    /// </summary>
    /// <param name="inner">The captcha service being protected.</param>
    /// <param name="rateLimiter">The per-client counters.</param>
    /// <param name="clientKeyProvider">Supplies the identity the counters are kept against.</param>
    /// <param name="logger">Logger for rate-limit refusals.</param>
    public RateLimitedCaptchaSvc(
        ICaptchaService inner,
        ICaptchaRateLimiter rateLimiter,
        ICaptchaClientKeyProvider clientKeyProvider,
        ILogger<RateLimitedCaptchaSvc> logger)
    {
        this.inner = inner;
        this.rateLimiter = rateLimiter;
        this.clientKeyProvider = clientKeyProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="CaptchaRateLimitedException">
    /// Thrown when the client has exhausted its issuance budget or is in failure lockout.
    /// </exception>
    public CaptchaChallenge Generate()
    {
        return IssueChallenge(inner.Generate, "visual");
    }

    /// <inheritdoc />
    /// <exception cref="CaptchaRateLimitedException">
    /// Thrown when the client has exhausted its issuance budget or is in failure lockout.
    /// </exception>
    public CaptchaChallenge GenerateQuestion()
    {
        return IssueChallenge(inner.GenerateQuestion, "question");
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Business Logic:</b> The lockout is tested <i>before</i> the inner service is
    /// consulted, so a locked-out client cannot burn other people's single-use challenge ids by
    /// submitting guesses against them. A wrong answer is recorded against the client, and the
    /// answer that crosses the cap is reported with the "wait" wording rather than the ordinary
    /// "that did not match", so a legitimate visitor is told why they are stuck. A blank submission
    /// is not counted — see <c>IsIncompleteSubmission</c>.</para>
    /// <para><b>Flow:</b> resolve the client key → refuse while locked out → delegate to the inner
    /// service → on a genuine wrong answer, register the failure and re-test the cap.</para>
    /// <para><b>Side Effects:</b> Increments the client's failure counter on a wrong answer, which
    /// can put the client into lockout; logs a warning on refusal. Delegating to the inner service
    /// <b>consumes the challenge</b> — challenges are single-use, so a challenge validated here
    /// cannot be validated again whatever the outcome.</para>
    /// <para><b>Result contract:</b> a refusal is a returned <c>Result.Failure</c>, not an
    /// exception — unlike the generators, this member has a failure channel and a rate-limited
    /// visitor is an expected outcome. The message is safe to show to the visitor and carries the
    /// remaining wait.</para>
    /// </remarks>
    public Result Validate(string challengeId, string answer)
    {
        var clientKey = clientKeyProvider.GetClientKey();

        if (rateLimiter.IsFailureBlocked(clientKey, out var lockoutRemaining))
        {
            logger.LogWarning(
                "Captcha validation refused for {ClientKey}: failure cap already reached, {RetryAfterSeconds}s remaining [REQ-NFR-024]",
                clientKey,
                (int)Math.Ceiling(lockoutRemaining.TotalSeconds));

            return Result.Failure(CaptchaRateLimitedException.BuildVisitorMessage(lockoutRemaining));
        }

        var result = inner.Validate(challengeId, answer);
        if (result.IsSuccess || IsIncompleteSubmission(challengeId, answer))
            return result;

        rateLimiter.RegisterFailure(clientKey);

        return rateLimiter.IsFailureBlocked(clientKey, out var retryAfter)
            ? Result.Failure(CaptchaRateLimitedException.BuildVisitorMessage(retryAfter))
            : result;
    }

    /// <summary>
    /// Applies both caps and then issues a challenge.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The failure lockout is checked first and on purpose: a client
    /// that has been grinding wrong answers must not be handed fresh challenges either, or the
    /// answer cache keeps growing for exactly the client the limiter has already judged
    /// abusive.</para>
    /// <para><b>Flow:</b> resolve the client key → failure lockout → issuance permit → issue.</para>
    /// <para><b>Side Effects:</b> Consumes one issuance permit; logs a refusal.</para>
    /// </remarks>
    /// <param name="issue">The inner generator to call once the caps allow it.</param>
    /// <param name="challengeKind">Word describing the challenge form, for the log line only.</param>
    /// <returns>The issued challenge.</returns>
    /// <exception cref="CaptchaRateLimitedException">Thrown when either cap refuses the request.</exception>
    private CaptchaChallenge IssueChallenge(Func<CaptchaChallenge> issue, string challengeKind)
    {
        var clientKey = clientKeyProvider.GetClientKey();

        if (rateLimiter.IsFailureBlocked(clientKey, out var lockoutRemaining))
        {
            logger.LogWarning(
                "Captcha {ChallengeKind} challenge refused for {ClientKey}: failure cap already reached, {RetryAfterSeconds}s remaining [REQ-NFR-024]",
                challengeKind,
                clientKey,
                (int)Math.Ceiling(lockoutRemaining.TotalSeconds));

            throw new CaptchaRateLimitedException(lockoutRemaining);
        }

        if (!rateLimiter.TryIssue(clientKey, out var retryAfter))
        {
            logger.LogWarning(
                "Captcha {ChallengeKind} challenge refused for {ClientKey}: issuance cap reached, {RetryAfterSeconds}s remaining [REQ-NFR-024]",
                challengeKind,
                clientKey,
                (int)Math.Ceiling(retryAfter.TotalSeconds));

            throw new CaptchaRateLimitedException(retryAfter);
        }

        return issue();
    }

    /// <summary>
    /// Detects a submission that never carried an answer to judge.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An empty answer box is a form-filling mistake, not a guess at
    /// the code. Counting it would let an ordinary visitor who submits too soon spend their whole
    /// failure budget without ever attempting the captcha.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="challengeId">The challenge id supplied by the caller.</param>
    /// <param name="answer">The answer supplied by the caller.</param>
    /// <returns>True when there was nothing to validate.</returns>
    private static bool IsIncompleteSubmission(string challengeId, string answer)
    {
        return string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(answer);
    }
}
