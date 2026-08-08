namespace BlogEngine.Common;

/// <summary>
/// Raised when a client asks for a captcha challenge it is no longer entitled to. [REQ-NFR-024]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>ICaptchaService.Generate</c> and <c>GenerateQuestion</c> return a
/// challenge, not a <c>Result</c>, so a refusal has no return channel. This exception is that
/// channel, and it carries a message that is safe to show a visitor verbatim.</para>
///
/// <para><b>Code Flow:</b> <c>RateLimitedCaptchaSvc</c> throws it; <c>CaptchaWidget</c> catches it
/// and renders <see cref="VisitorMessage"/> in the same inline, <c>aria-live</c> error slot it
/// already uses for a wrong answer, so a throttled visitor sees a real instruction rather than a
/// blank control or a raw 429 page.</para>
///
/// <para><b>Dependencies:</b> None beyond the BCL.</para>
///
/// <para><b>Usage:</b> Never let this escape to the browser as an unhandled exception - it is a
/// normal, expected outcome of the limiter, not a fault.</para>
/// </remarks>
public class CaptchaRateLimitedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CaptchaRateLimitedException"/> class.
    /// </summary>
    /// <param name="retryAfter">How long the client must wait before trying again.</param>
    public CaptchaRateLimitedException(TimeSpan retryAfter)
        : base($"Captcha rate limit reached; retry after {Math.Ceiling(retryAfter.TotalSeconds)} seconds.")
    {
        RetryAfter = retryAfter;
        VisitorMessage = BuildVisitorMessage(retryAfter);
    }

    /// <summary>
    /// Gets how long the client must wait before the window reopens.
    /// </summary>
    public TimeSpan RetryAfter { get; }

    /// <summary>
    /// Gets the message that may be shown to the visitor unchanged.
    /// </summary>
    /// <remarks>
    /// Deliberately free of any detail about which cap tripped or how many attempts remain -
    /// telling an attacker where the boundary sits hands them the tuning information.
    /// </remarks>
    public string VisitorMessage { get; }

    /// <summary>
    /// Builds the visitor-facing wait message for a retry delay.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The same wording is used whether the refusal arrives through
    /// this exception (issuance) or through a failed <c>Result</c> (validation), so a visitor sees
    /// one consistent instruction. The delay is rounded UP, because rounding down would invite an
    /// immediate retry that fails again.</para>
    /// <para><b>Flow:</b> round the delay up → phrase it in seconds under a minute, minutes above.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="retryAfter">How long the client must wait.</param>
    /// <returns>A plain-language instruction ending in a full stop.</returns>
    public static string BuildVisitorMessage(TimeSpan retryAfter)
    {
        var seconds = (int)Math.Ceiling(Math.Max(retryAfter.TotalSeconds, 1));
        if (seconds < 60)
            return $"Too many verification attempts. Please wait about {seconds} seconds and try again.";

        var minutes = (int)Math.Ceiling(seconds / 60d);
        var unit = minutes == 1 ? "minute" : "minutes";
        return $"Too many verification attempts. Please wait about {minutes} {unit} and try again.";
    }
}
