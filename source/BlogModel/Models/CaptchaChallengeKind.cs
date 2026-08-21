namespace BlogModels;

/// <summary>
/// The form a captcha challenge takes, so a visitor who cannot use one can switch to the other.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> WCAG 2.1 AA 1.1.1 requires a non-visual alternative to the image
/// challenge. This enum names the two forms the same service can issue. [REQ-UI-057]</para>
///
/// <para><b>Code Flow:</b> <c>ICaptchaService.Generate</c> issues a <see cref="Visual"/>
/// challenge and <c>ICaptchaService.GenerateQuestion</c> issues a <see cref="Question"/> one.
/// Both file their expected answer in the same short-lived server-side store, so both share the
/// single-use and five-minute-expiry lifecycle and both are validated by the same call.</para>
///
/// <para><b>Dependencies:</b> None - a plain enum in the leaf model assembly.</para>
///
/// <para><b>Usage:</b> Read <c>CaptchaChallenge.Kind</c> to decide whether to render
/// <c>SvgMarkup</c> or <c>QuestionText</c>. Never branch on it to relax validation.</para>
/// </remarks>
public enum CaptchaChallengeKind
{
    /// <summary>A distorted image of a random code, answered by typing the characters.</summary>
    Visual = 0,

    /// <summary>
    /// A short text question whose answer must be worked out, answered by typing a number.
    /// </summary>
    Question = 1
}
