using System.Text.RegularExpressions;
using BlogEngine.Common;
using BlogEngine.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Tests for the self-hosted captcha service. [REQ-FN-049]
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Proves the four acceptance criteria: the expected answer never reaches
/// the client, a challenge is single-use, a reload issues a new one, and a wrong answer is
/// refused. Also guards the 2026-08-07 security fix - the rendered challenge must contain no
/// SVG <c>&lt;text&gt;</c> node and no literal of any drawn character.</para>
/// <para><b>Code Flow:</b> Each test builds a service over a real <see cref="MemoryCache"/>. The
/// code can no longer be read out of the image (that was the defect), so tests that need to know
/// it use <see cref="PinnedCodeCaptchaSvc"/>, a test double that overrides the random draw.</para>
/// <para><b>Dependencies:</b> xUnit, Microsoft.Extensions.Caching.Memory.</para>
/// <para><b>Usage:</b> Pure unit tests - no database, no network.</para>
/// </remarks>
public class CaptchaSvcTests
{
    /// <summary>The code the pinned test double always draws.</summary>
    private const string PinnedCode = "AB3KW";

    /// <summary>
    /// A challenge answered with exactly the characters that were drawn is accepted.
    /// </summary>
    [Fact]
    public void CaptchaAcceptsCorrectAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.Generate();

        var result = service.Validate(challenge.ChallengeId, PinnedCode);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// The answer is compared case-insensitively and with whitespace stripped, because the
    /// distorted strokes give no reliable case cue.
    /// </summary>
    [Fact]
    public void CaptchaAcceptsLooselyTypedAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.Generate();

        var result = service.Validate(challenge.ChallengeId, " ab 3k w ");

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A challenge answered with the wrong characters is refused, which blocks the write and
    /// forces the caller to render a fresh challenge.
    /// </summary>
    [Fact]
    public void CaptchaRejectsWrongAnswer()
    {
        var service = BuildService();
        var challenge = service.Generate();

        var result = service.Validate(challenge.ChallengeId, "ZZZZZ-not-the-code");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// A correct answer cannot be replayed: the second attempt against the same challenge id
    /// fails because the challenge is consumed by the first validation.
    /// </summary>
    [Fact]
    public void CaptchaChallengeIsSingleUse()
    {
        var service = BuildPinnedService();
        var challenge = service.Generate();

        var first = service.Validate(challenge.ChallengeId, PinnedCode);
        var second = service.Validate(challenge.ChallengeId, PinnedCode);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
    }

    /// <summary>
    /// A wrong answer also burns the challenge, so an attacker cannot brute-force one image.
    /// </summary>
    [Fact]
    public void CaptchaBurnsChallengeOnWrongAnswer()
    {
        var service = BuildPinnedService();
        var challenge = service.Generate();

        service.Validate(challenge.ChallengeId, "wrong");
        var retry = service.Validate(challenge.ChallengeId, PinnedCode);

        Assert.True(retry.IsFailure);
    }

    /// <summary>
    /// A challenge id the server has never issued is refused rather than accepted by default.
    /// </summary>
    [Fact]
    public void CaptchaRejectsUnknownChallenge()
    {
        var service = BuildService();

        var result = service.Validate("a-challenge-that-was-never-issued", "ABCDE");

        Assert.True(result.IsFailure);
    }

    /// <summary>
    /// A challenge expires: once the cache entry is gone the answer that was correct a moment ago
    /// is refused.
    /// </summary>
    [Fact]
    public void CaptchaRejectsExpiredChallenge()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new PinnedCodeCaptchaSvc(cache, NullLogger<CaptchaSvc>.Instance);
        var challenge = service.Generate();

        cache.Remove("Captcha:" + challenge.ChallengeId);
        var result = service.Validate(challenge.ChallengeId, PinnedCode);

        Assert.True(result.IsFailure);
        Assert.True(challenge.ExpiresOn <= DateTime.UtcNow.AddMinutes(5));
    }

    /// <summary>
    /// Reloading the form issues a genuinely different challenge rather than re-serving the
    /// previous one.
    /// </summary>
    [Fact]
    public void CaptchaReloadIssuesNewChallenge()
    {
        var service = BuildService();

        var first = service.Generate();
        var second = service.Generate();

        Assert.NotEqual(first.ChallengeId, second.ChallengeId);
        Assert.NotEqual(first.SvgMarkup, second.SvgMarkup);
    }

    /// <summary>
    /// The expected answer is not carried in the challenge id, so nothing the client receives
    /// besides the picture could reveal the code.
    /// </summary>
    [Fact]
    public void CaptchaAnswerNeverReachesClient()
    {
        var service = BuildPinnedService();

        var challenge = service.Generate();

        Assert.DoesNotContain(PinnedCode, challenge.ChallengeId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Regression guard for the 2026-08-07 defect: the rendered challenge must contain no SVG
    /// <c>&lt;text&gt;</c> element, so an attacker cannot base64-decode the image and read the
    /// answer straight out of the markup.
    /// </summary>
    [Fact]
    public void CaptchaMarkupHasNoTextElement()
    {
        var service = BuildPinnedService();

        var challenge = service.Generate();

        Assert.DoesNotContain("<text", challenge.SvgMarkup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("font", challenge.SvgMarkup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<path", challenge.SvgMarkup, StringComparison.Ordinal);
    }

    /// <summary>
    /// Regression guard for the 2026-08-07 defect: no element in the payload carries a single
    /// alphanumeric character as its text content, which is exactly the shape the old
    /// <c>&lt;text&gt;A&lt;/text&gt;</c> leak had.
    /// </summary>
    [Fact]
    public void CaptchaMarkupHasNoSingleCharacterTextNode()
    {
        var service = BuildPinnedService();

        var challenge = service.Generate();

        Assert.DoesNotMatch(new Regex(@">\s*[A-Za-z0-9]\s*<"), challenge.SvgMarkup);
    }

    /// <summary>
    /// Regression guard for the 2026-08-07 defect: the set of words in the payload is identical
    /// for two completely different codes, so nothing in the markup's vocabulary - no character
    /// literal, no glyph name, no id or class - varies with the answer.
    /// </summary>
    [Fact]
    public void CaptchaMarkupVocabularyIsIndependentOfCode()
    {
        var first = WordsIn(CaptchaSvgRenderer.Render("AAAAA"));
        var second = WordsIn(CaptchaSvgRenderer.Render("39QWX"));

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Two renders of the same code produce different geometry, so path data cannot be used as a
    /// stable per-character fingerprint.
    /// </summary>
    [Fact]
    public void CaptchaGeometryDiffersBetweenRenders()
    {
        var first = CaptchaSvgRenderer.Render(PinnedCode);
        var second = CaptchaSvgRenderer.Render(PinnedCode);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Every character the service can draw has a vector glyph, so a challenge can never contain
    /// a character the renderer refuses or silently skips.
    /// </summary>
    [Fact]
    public void CaptchaRendersEveryAlphabetCharacter()
    {
        foreach (var character in CaptchaSvc.CodeAlphabet)
        {
            var markup = CaptchaSvgRenderer.Render(character.ToString());

            Assert.Contains("<path", markup, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An empty code is a programming error and is refused rather than rendered as a blank plate.
    /// </summary>
    [Fact]
    public void CaptchaRendererRejectsEmptyCode()
    {
        Assert.Throws<ArgumentException>(() => CaptchaSvgRenderer.Render(" "));
    }

    /// <summary>
    /// The rendered challenge keeps its accessible role and label, and the label does not leak
    /// the answer.
    /// </summary>
    [Fact]
    public void CaptchaMarkupKeepsAccessibleLabel()
    {
        var service = BuildPinnedService();

        var challenge = service.Generate();

        Assert.Contains("role=\"img\"", challenge.SvgMarkup, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Verification image. Type the characters you see.\"", challenge.SvgMarkup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rendered challenge is a self-contained SVG document, not a raster image, so it works
    /// on Linux hosts where System.Drawing.Common is unsupported.
    /// </summary>
    [Fact]
    public void CaptchaRendersSelfContainedSvg()
    {
        var service = BuildService();

        var challenge = service.Generate();

        Assert.StartsWith("<svg", challenge.SvgMarkup, StringComparison.Ordinal);
        Assert.EndsWith("</svg>", challenge.SvgMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", challenge.SvgMarkup.Replace("http://www.w3.org/2000/svg", string.Empty));
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
    /// Builds a captcha service whose drawn code is known to the test.
    /// </summary>
    /// <returns>The service under test.</returns>
    private static CaptchaSvc BuildPinnedService()
    {
        return new PinnedCodeCaptchaSvc(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<CaptchaSvc>.Instance);
    }

    /// <summary>
    /// Collects the distinct words of a rendered challenge, ignoring colours and numbers.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Hex colours are picked at random from a fixed palette and
    /// contain letters, so they are stripped first; what remains is element names, attribute
    /// names, keyword values and the path commands. That vocabulary must not depend on the code
    /// being drawn - if it does, something about the answer has leaked into the markup.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="svgMarkup">The rendered challenge.</param>
    /// <returns>The distinct words, sorted.</returns>
    private static string[] WordsIn(string svgMarkup)
    {
        var withoutColours = Regex.Replace(svgMarkup, "#[0-9a-fA-F]{6}", string.Empty);
        return Regex.Matches(withoutColours, "[A-Za-z]+")
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// A captcha service that always draws the same code.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> The fix for REQ-FN-049 makes the drawn code unrecoverable from the
    /// image, which is exactly the point - so a test that needs to answer correctly has to be
    /// told the code instead of reading it.</para>
    /// <para><b>Code Flow:</b> Overrides only the random draw; caching, expiry, single-use and
    /// comparison all run the production path.</para>
    /// <para><b>Dependencies:</b> <see cref="CaptchaSvc"/>.</para>
    /// <para><b>Usage:</b> Tests only. Never registered in the container.</para>
    /// </remarks>
    private sealed class PinnedCodeCaptchaSvc : CaptchaSvc
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PinnedCodeCaptchaSvc"/> class.
        /// </summary>
        /// <param name="memoryCache">Short-lived store for expected answers.</param>
        /// <param name="logger">Logger for security events.</param>
        public PinnedCodeCaptchaSvc(IMemoryCache memoryCache, ILogger<CaptchaSvc> logger)
            : base(memoryCache, logger)
        {
        }

        /// <summary>
        /// Returns the pinned code instead of a random draw.
        /// </summary>
        /// <returns>The pinned code.</returns>
        protected override string BuildRandomCode()
        {
            return PinnedCode;
        }
    }
}
