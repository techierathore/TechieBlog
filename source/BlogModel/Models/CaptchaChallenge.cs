namespace BlogModels;

/// <summary>
/// A rendered captcha challenge: everything the browser is allowed to see, and nothing more.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published output contract of <c>ICaptchaService.Generate</c>.
/// [REQ-FN-049]</para>
///
/// <para><b>Code Flow:</b> The service generates a random code with
/// <see cref="System.Security.Cryptography.RandomNumberGenerator"/>, keeps the expected answer
/// in a short-lived server-side entry keyed by <see cref="ChallengeId"/>, and returns only the
/// distorted <see cref="SvgMarkup"/> and that opaque id.</para>
///
/// <para><b>Dependencies:</b> None - a plain DTO in the leaf model assembly.</para>
///
/// <para><b>Usage:</b> Render <see cref="SvgMarkup"/> inline and post <see cref="ChallengeId"/>
/// back alongside the visitor's answer. There is deliberately NO property carrying the expected
/// answer: it must never reach the client. That holds for the accessible
/// <see cref="CaptchaChallengeKind.Question"/> form too - <see cref="QuestionText"/> carries the
/// question, never the number it works out to. [REQ-UI-057]</para>
/// </remarks>
public class CaptchaChallenge
{
    /// <summary>
    /// The opaque, single-use identifier of this challenge - the handle the answer is checked
    /// against.
    /// </summary>
    /// <remarks>
    /// 32 cryptographically random bytes, so it cannot be guessed or enumerated, and it encodes
    /// nothing about the expected answer. It is the key of a server-side cache entry, not a value the
    /// server can re-derive: if that entry is gone - expired, already used, or lost because the
    /// process restarted or another instance served the request - the challenge is simply unknown and
    /// validation fails.
    /// <para><b>Single use, right or wrong.</b> The entry is burned before the answer is compared, so
    /// a wrong answer invalidates the challenge too. A UI that re-renders a failed form MUST request a
    /// new challenge; resubmitting this id can only ever fail again.</para>
    /// <para>Safe to place in a hidden form field - it is a lookup key, not a secret answer.</para>
    /// </remarks>
    public string ChallengeId { get; set; } = string.Empty;

    /// <summary>
    /// Which form this challenge takes: an image, or an accessible text question.
    /// </summary>
    /// <remarks>
    /// Decides which of <see cref="SvgMarkup"/> and <see cref="QuestionText"/> is populated - exactly
    /// one of them ever is. Both kinds share one store, one lifetime and one validation call, so this
    /// is a rendering decision only: never branch on it to relax or skip a check. It defaults to
    /// <see cref="CaptchaChallengeKind.Visual"/>, so a default-constructed instance describes an image
    /// challenge with no image.
    /// </remarks>
    public CaptchaChallengeKind Kind { get; set; } = CaptchaChallengeKind.Visual;

    /// <summary>
    /// The self-contained, distorted SVG image of the code, ready to be written inline.
    /// </summary>
    /// <remarks>
    /// Empty on a <see cref="CaptchaChallengeKind.Question"/> challenge, which has no image.
    /// <para>Server-generated markup with no visitor input in it, which is what makes it safe to emit
    /// as raw HTML - the one place in this codebase where that is true of a string on a model. It is
    /// inline rather than a URL so the image cannot be fetched, cached or replayed independently of
    /// the page that issued it.</para>
    /// <para>An SVG is text: the code it draws is readable in the page source, so this defends
    /// against naive bots only. It is one layer beside the honeypot and the timing check, not the
    /// whole defence.</para>
    /// </remarks>
    public string SvgMarkup { get; set; } = string.Empty;

    /// <summary>
    /// The accessible text question a visitor answers instead of reading the image.
    /// </summary>
    /// <remarks>
    /// Empty on a <see cref="CaptchaChallengeKind.Visual"/> challenge. The question is plain
    /// prose that a screen reader can read out; the answer it works out to appears nowhere in it.
    /// <para>Render it as text, not as markup, and label the input with it so assistive technology
    /// reaches it - a question a screen reader cannot announce defeats the entire point of this
    /// alternative (WCAG 2.1 AA 1.1.1). The stored answer set may accept several spellings, so do not
    /// impose a stricter input format than the question implies.</para>
    /// </remarks>
    public string QuestionText { get; set; } = string.Empty;

    /// <summary>
    /// The UTC instant after which the challenge stops being accepted.
    /// </summary>
    /// <remarks>
    /// Informational for the UI - it is the same instant the server-side entry is set to expire at,
    /// so enforcement happens in the cache and not by anyone reading this property. A tampered or
    /// stale value on the client changes nothing.
    /// <para>Expiry is absolute, not sliding: the clock starts when the challenge is issued, so a
    /// visitor who leaves a comment form open past the window gets a failure and a fresh challenge
    /// rather than an extension.</para>
    /// </remarks>
    public DateTime ExpiresOn { get; set; }
}
