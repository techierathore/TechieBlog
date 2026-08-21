using BlogModels;

namespace BlogEngine.Services;

/// <summary>
/// Issues and validates self-hosted captcha challenges.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The published contract the comment and rating forms use to prove a
/// submission came from a human, implemented with the .NET base class library alone - no
/// third-party package and no external service. [REQ-FN-049]</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The form calls <see cref="Generate"/> and renders the returned SVG together with a
///   hidden field holding the challenge id.</item>
///   <item>The visitor types an answer; the form posts the id and the answer.</item>
///   <item>The service calls <see cref="Validate"/>. The challenge is burned by that call
///   whether the answer was right or wrong, so every retry needs a fresh image.</item>
/// </list>
///
/// <para><b>Dependencies:</b> An in-process cache holds the expected answer for a few minutes.
/// The answer is never serialised to the client in any form.</para>
///
/// <para><b>Usage:</b> On a failed validation, re-render with a NEW challenge from
/// <see cref="Generate"/>; re-showing the old one will always fail because it is gone.</para>
/// </remarks>
public interface ICaptchaService
{
    /// <summary>
    /// Creates a new challenge.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Picks a random code with
    /// <see cref="System.Security.Cryptography.RandomNumberGenerator"/>, stores the expected
    /// answer server-side under an opaque id, and renders the code as distorted SVG.</para>
    /// <para><b>Flow:</b> generate code, store answer, render image, return id + image.</para>
    /// <para><b>Side Effects:</b> Adds one short-lived server-side entry.</para>
    /// </remarks>
    /// <returns>The challenge id, the SVG markup and the expiry instant.</returns>
    CaptchaChallenge Generate();

    /// <summary>
    /// Creates a new accessible challenge: a short text question instead of an image.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The image challenge is unusable for a blind or low-vision
    /// visitor, which fails WCAG 2.1 AA 1.1.1 and locks them out of commenting, rating and
    /// subscribing. This issues the same kind of single-use, five-minute challenge in prose that
    /// a screen reader can read out. The number it works out to appears nowhere in the question,
    /// so the accessible path leaks no more than the visual one. [REQ-UI-057]</para>
    /// <para><b>Flow:</b> build a question, store its accepted answers server-side, return the
    /// id + the question text.</para>
    /// <para><b>Side Effects:</b> Adds one short-lived server-side entry.</para>
    /// </remarks>
    /// <returns>The challenge id, the question text and the expiry instant.</returns>
    CaptchaChallenge GenerateQuestion();

    /// <summary>
    /// Checks a visitor's answer and consumes the challenge.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A challenge is single-use: the stored answer is removed
    /// before the comparison, so a wrong guess cannot be retried against the same image and a
    /// correct answer cannot be replayed.</para>
    /// <para><b>Flow:</b> look the id up, remove it, compare case-insensitively ignoring spaces.</para>
    /// <para><b>Side Effects:</b> Removes the server-side entry.</para>
    /// </remarks>
    /// <param name="challengeId">The id returned by <see cref="Generate"/>.</param>
    /// <param name="answer">What the visitor typed.</param>
    /// <returns>Success, or a failure carrying a visitor-safe message.</returns>
    Result Validate(string challengeId, string answer);
}
