using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using BlogEngine.Common;
using BlogModels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Self-hosted captcha: generates a random code, renders it as distorted SVG and validates the
/// answer against a value that never leaves the server.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Blocks automated comment and rating submissions without shipping a
/// third-party script or calling out to an external service. [REQ-FN-049]</para>
///
/// <para><b>Code Flow:</b> <see cref="Generate"/> draws a code from an unambiguous alphabet with
/// <see cref="RandomNumberGenerator"/>, files the expected answer in the in-process cache under
/// a 256-bit random id with a five-minute absolute expiry, and hands back only the id and the
/// image. <see cref="Validate"/> removes the entry first and compares afterwards, which makes
/// every challenge strictly single-use.</para>
///
/// <para><b>Dependencies:</b> <see cref="IMemoryCache"/> for the short-lived answer store,
/// <see cref="CaptchaSvgRenderer"/> for the image, <see cref="ILogger{TCategoryName}"/> for
/// security logging. No <c>System.Drawing.Common</c> - it is Windows-only.</para>
///
/// <para><b>Security note:</b> <see cref="CaptchaSvgRenderer"/> draws the code as anonymous
/// vector strokes, never as SVG <c>&lt;text&gt;</c>. Nothing the browser receives - not the
/// challenge id, not the image - contains the answer in machine-readable form. [REQ-FN-049]</para>
///
/// <para><b>Accessibility:</b> <see cref="GenerateQuestion"/> issues the non-visual alternative
/// required by WCAG 2.1 AA 1.1.1 - a short prose question a screen reader can read out. It shares
/// this class's store, expiry and <see cref="Validate"/> path exactly, so the accessible route is
/// no easier to replay than the visual one, and the number the question works out to appears
/// nowhere in the text that is sent. [REQ-UI-057]</para>
///
/// <para><b>Usage:</b> Registered as a singleton. On a Blazor Server farm, pin sessions or swap
/// the cache for a distributed one; the answer must stay server-side either way.</para>
/// </remarks>
public class CaptchaSvc : ICaptchaService
{
    /// <summary>Cache-key prefix, so captcha entries cannot collide with anything else.</summary>
    private const string CachePrefix = "Captcha:";

    /// <summary>Number of characters in a challenge.</summary>
    private const int CodeLength = 5;

    /// <summary>Bytes of entropy in a challenge id.</summary>
    private const int ChallengeIdByteLength = 32;

    /// <summary>
    /// Characters a challenge may contain. Visually ambiguous glyphs (0/O, 1/I/L, 5/S, 2/Z)
    /// are excluded so a human is not punished for the distortion.
    /// </summary>
    /// <remarks>
    /// Public so tests can prove the vector glyph table in <c>CaptchaGlyphSet</c> covers every
    /// character this service can draw. Knowing the alphabet reveals nothing - the answer is a
    /// random draw from it, held only on the server.
    /// </remarks>
    public const string CodeAlphabet = "ABCDEFGHJKMNPQRTUVWXY346789";

    /// <summary>How long a challenge stays answerable.</summary>
    private static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache memoryCache;
    private readonly ILogger<CaptchaSvc> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CaptchaSvc"/> class.
    /// </summary>
    /// <param name="memoryCache">Short-lived store for expected answers.</param>
    /// <param name="logger">Logger for security events.</param>
    public CaptchaSvc(IMemoryCache memoryCache, ILogger<CaptchaSvc> logger)
    {
        this.memoryCache = memoryCache;
        this.logger = logger;
    }

    /// <inheritdoc />
    public CaptchaChallenge Generate()
    {
        var code = BuildRandomCode();
        var challengeId = StoreAnswers(CaptchaChallengeKind.Visual, new[] { code }, out var expiresOn);

        return new CaptchaChallenge
        {
            ChallengeId = challengeId,
            Kind = CaptchaChallengeKind.Visual,
            SvgMarkup = CaptchaSvgRenderer.Render(code),
            ExpiresOn = expiresOn
        };
    }

    /// <inheritdoc />
    public CaptchaChallenge GenerateQuestion()
    {
        var question = BuildQuestion();
        var challengeId = StoreAnswers(CaptchaChallengeKind.Question, question.AcceptedAnswers, out var expiresOn);

        return new CaptchaChallenge
        {
            ChallengeId = challengeId,
            Kind = CaptchaChallengeKind.Question,
            QuestionText = question.QuestionText,
            ExpiresOn = expiresOn
        };
    }

    /// <inheritdoc />
    public Result Validate(string challengeId, string answer)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(answer))
            return Result.Failure("Please answer the verification challenge.");

        var cacheKey = CachePrefix + challengeId;
        if (!memoryCache.TryGetValue(cacheKey, out CaptchaAnswerEntry entry))
        {
            logger.LogWarning("Captcha challenge {ChallengeId} was unknown, expired or replayed", challengeId);
            return Result.Failure("The verification challenge expired. Please try the new one.");
        }

        // Burn the challenge before comparing: single use, right or wrong.
        memoryCache.Remove(cacheKey);

        if (!IsAnswerCorrect(entry, answer))
        {
            logger.LogWarning(
                "Captcha challenge {ChallengeId} of kind {Kind} was answered incorrectly",
                challengeId,
                entry.Kind);

            return entry.Kind == CaptchaChallengeKind.Question
                ? Result.Failure("That was not the right answer. Please try the new verification question.")
                : Result.Failure("The characters did not match. Please try the new image.");
        }

        return Result.Success();
    }

    /// <summary>
    /// Files the expected answers of a new challenge under a fresh opaque id.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both challenge forms go through this one method, which is what
    /// makes the accessible route share the visual route's five-minute expiry and single-use
    /// lifecycle rather than being a softer parallel path. [REQ-UI-057]</para>
    /// <para><b>Flow:</b> mint an id, compute the expiry, write one absolute-expiry cache entry.</para>
    /// <para><b>Side Effects:</b> Adds one short-lived server-side entry.</para>
    /// </remarks>
    /// <param name="kind">Which form the challenge takes.</param>
    /// <param name="acceptedAnswers">Every answer that counts as correct.</param>
    /// <param name="expiresOn">Receives the instant the challenge stops being answerable.</param>
    /// <returns>The opaque challenge id.</returns>
    private string StoreAnswers(CaptchaChallengeKind kind, IReadOnlyList<string> acceptedAnswers, out DateTime expiresOn)
    {
        var challengeId = BuildChallengeId();
        expiresOn = DateTime.UtcNow.Add(ChallengeLifetime);

        var cacheOptions = new MemoryCacheEntryOptions { AbsoluteExpiration = new DateTimeOffset(expiresOn, TimeSpan.Zero) };
        memoryCache.Set(CachePrefix + challengeId, new CaptchaAnswerEntry(kind, acceptedAnswers), cacheOptions);

        return challengeId;
    }

    /// <summary>
    /// Compares the visitor's answer with the expected value.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Whitespace is ignored and the comparison is
    /// case-insensitive, because neither the distorted glyphs nor a spoken question give a
    /// reliable case cue. A question challenge accepts several spellings ("9" and "nine"), so
    /// the typed answer is matched against the whole accepted set.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="entry">The stored expectation for this challenge.</param>
    /// <param name="answer">What the visitor typed.</param>
    /// <returns>True when they match.</returns>
    private static bool IsAnswerCorrect(CaptchaAnswerEntry entry, string answer)
    {
        var normalisedAnswer = answer.Replace(" ", string.Empty).Trim();
        foreach (var accepted in entry.AcceptedAnswers)
        {
            if (string.Equals(accepted, normalisedAnswer, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the accessible question a <see cref="CaptchaChallengeKind.Question"/> challenge asks.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Delegates to <see cref="CaptchaQuestionSet"/>, which screens
    /// every candidate so the answer never appears inside the question text.</para>
    /// <para><b>Flow:</b> Overridable only so a unit test can pin the question it is about to
    /// answer, mirroring <see cref="BuildRandomCode"/>. Production never overrides it.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The question and its accepted answers.</returns>
    protected virtual CaptchaQuestion BuildQuestion()
    {
        return CaptchaQuestionSet.Build();
    }

    /// <summary>
    /// Draws a code from the unambiguous alphabet.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Each character is chosen with
    /// <see cref="RandomNumberGenerator.GetInt32(int, int)"/>, which is unbiased and
    /// unpredictable - a seeded <see cref="Random"/> would let an attacker replay the sequence.</para>
    /// <para><b>Flow:</b> Overridable only so a unit test can pin the code it is about to
    /// answer; the drawn code is no longer recoverable from the rendered image, so tests have no
    /// other way to exercise the correct-answer path. Production never overrides it.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The generated code.</returns>
    protected virtual string BuildRandomCode()
    {
        var code = new StringBuilder(CodeLength);
        for (var index = 0; index < CodeLength; index++)
        {
            code.Append(CodeAlphabet[RandomNumberGenerator.GetInt32(0, CodeAlphabet.Length)]);
        }

        return code.ToString();
    }

    /// <summary>
    /// Creates an opaque, URL-safe challenge id.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> 256 bits of entropy, base64url encoded, so the id cannot be
    /// guessed and carries no information about the answer.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The challenge id.</returns>
    private static string BuildChallengeId()
    {
        var buffer = RandomNumberGenerator.GetBytes(ChallengeIdByteLength);
        return Convert.ToBase64String(buffer)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// What the server remembers about one outstanding challenge.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Holds the secret half of a challenge - the answers - alongside the
    /// form it took, so <see cref="Validate"/> can word its rejection correctly without the
    /// caller having to tell it which kind was issued. [REQ-UI-057]</para>
    /// <para><b>Code Flow:</b> Written by <see cref="StoreAnswers"/>, read and immediately removed
    /// by <see cref="Validate"/>.</para>
    /// <para><b>Dependencies:</b> None.</para>
    /// <para><b>Usage:</b> Private and never serialised. It must not leave the server.</para>
    /// </remarks>
    private sealed class CaptchaAnswerEntry
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CaptchaAnswerEntry"/> class.
        /// </summary>
        /// <param name="kind">The form the challenge took.</param>
        /// <param name="acceptedAnswers">Every answer that counts as correct.</param>
        public CaptchaAnswerEntry(CaptchaChallengeKind kind, IReadOnlyList<string> acceptedAnswers)
        {
            Kind = kind;
            AcceptedAnswers = acceptedAnswers;
        }

        /// <summary>Gets the form the challenge took.</summary>
        public CaptchaChallengeKind Kind { get; }

        /// <summary>Gets every answer that counts as correct.</summary>
        public IReadOnlyList<string> AcceptedAnswers { get; }
    }
}
