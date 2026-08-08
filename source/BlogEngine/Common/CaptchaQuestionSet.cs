using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BlogEngine.Common;

/// <summary>
/// Builds the accessible alternative to the image captcha: a short question whose answer has to
/// be worked out and is never written down.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The image challenge fails WCAG 2.1 AA 1.1.1 - a blind or low-vision
/// visitor cannot comment, rate or subscribe. This class produces a text/logic challenge that a
/// screen reader can read out. [REQ-UI-057]</para>
///
/// <para><b>Code Flow:</b> <see cref="Build"/> picks one of four question shapes with
/// <see cref="RandomNumberGenerator"/>, then checks the candidate against
/// <see cref="IsAnswerHidden"/> before returning it. <c>CaptchaSvc.GenerateQuestion</c> stores the
/// accepted answers server-side and sends only the prose.</para>
///
/// <para><b>Dependencies:</b> .NET base class library only - no speech engine, no third-party
/// package, no external service. Audio was rejected deliberately: the BCL has no speech
/// synthesiser, so an audio challenge would mean either hand-recorded clips (which are a fixed,
/// fingerprintable corpus) or a new dependency, and the acceptance explicitly permits a
/// text/logic question instead.</para>
///
/// <para><b>Why every shape resolves to a NUMBER — this is the whole design constraint.</b> The
/// obvious accessible captcha is multiple choice ("which of these is a fruit: cherry, tiger,
/// scarlet?"), and it is unusable here: the options have to be rendered, so the answer is printed
/// verbatim in the page source. That is not a hypothetical — it is precisely the defect the image
/// challenge already had to be rebuilt to fix, where the base64 <c>data:</c> URI still contained a
/// literal <c>&lt;text&gt;</c> node and the answer fell out of decoding the image <c>src</c>. Every
/// shape here therefore asks the visitor to <i>compute</i> a number, and
/// <see cref="IsAnswerHidden"/> then asserts that the computed value appears nowhere in the prose.
/// A shape that cannot satisfy that assertion does not belong in this class.</para>
///
/// <para><b>A text question is machine-SOLVABLE, and that is accepted — the rate limiter is the
/// other half of the design.</b> Unlike the image challenge, this one is honestly beatable by a
/// script: "what is seven plus two" is four lines of parsing. Hiding the answer from the markup
/// buys unpredictability, not immunity. What makes solving it uneconomic is
/// <see cref="ICaptchaRateLimiter"/> — 20 issuances per 60 seconds and 5 failures per 300 seconds
/// per client (REQ-NFR-024) — which caps the throughput of a solver to something no spam campaign
/// can use. The two were built together and only work together: <b>weakening or disabling the
/// captcha rate limiter turns this challenge into approximately no challenge at all</b>, whereas
/// weakening it around the image challenge merely makes brute force slow instead of impossible.</para>
///
/// <para><b>Usage:</b> Engine-internal, reached only through <c>CaptchaSvc.GenerateQuestion</c>,
/// which stores the accepted answers server-side and returns only the prose.</para>
/// </remarks>
public static class CaptchaQuestionSet
{
    /// <summary>How many candidates to try before falling back to the provably safe shape.</summary>
    private const int MaxBuildAttempts = 8;

    /// <summary>How many question shapes <see cref="BuildCandidate"/> chooses between.</summary>
    private const int QuestionShapeCount = 4;

    /// <summary>English words for the numbers a question can mention or answer to.</summary>
    private static readonly string[] NumberWords =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen",
        "eighteen", "nineteen", "twenty"
    };

    /// <summary>
    /// Everyday nouns whose letters a visitor is asked to count. None of them contains an English
    /// number word, so counting one can never spell its own answer into the question.
    /// </summary>
    private static readonly string[] CountableWords =
    {
        "lamp", "tree", "book", "river", "cloud", "table", "garden", "window", "planet",
        "bridge", "printer", "monitor", "picture", "keyboard", "notebook", "mountain"
    };

    /// <summary>
    /// Short phrases whose words a visitor is asked to count.
    /// </summary>
    private static readonly string[] CountablePhrases =
    {
        "the sky is blue", "coffee is hot", "a cat sat on the mat", "open the front door",
        "birds sing at dawn", "the river runs east", "rain fell all night long",
        "read a good book today"
    };

    /// <summary>
    /// Builds a question together with every spelling of its answer.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A candidate is only returned once
    /// <see cref="IsAnswerHidden"/> confirms no accepted answer appears inside the question, so
    /// the answer can never be lifted straight out of the rendered page. If eight candidates in a
    /// row somehow fail that check the addition shape is used, which is safe by construction: its
    /// answer is strictly larger than either operand and the prose contains no digits.</para>
    /// <para><b>Flow:</b> pick a shape at random, build it, screen it, return or retry.</para>
    /// <para><b>Side Effects:</b> None. Consumes cryptographic randomness.</para>
    /// </remarks>
    /// <returns>A question whose answer is not recoverable from its own text.</returns>
    public static CaptchaQuestion Build()
    {
        for (var attempt = 0; attempt < MaxBuildAttempts; attempt++)
        {
            var candidate = BuildCandidate();
            if (IsAnswerHidden(candidate))
                return candidate;
        }

        return BuildAdditionQuestion();
    }

    /// <summary>
    /// Checks that no accepted answer appears anywhere inside the question text.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the machine-readability guarantee, asserted rather
    /// than assumed. A substring match is used, not a word match, so "six" hiding inside
    /// "sixteen" would still be caught.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="question">The candidate to screen.</param>
    /// <returns>True when the question gives nothing away.</returns>
    public static bool IsAnswerHidden(CaptchaQuestion question)
    {
        if (question == null)
            return false;

        foreach (var answer in question.AcceptedAnswers)
        {
            if (question.QuestionText.Contains(answer, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Picks one of the four question shapes at random.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Variety matters: a single shape would let an attacker write
    /// one parser and be done. Four shapes with randomised operands and word banks keep the
    /// surface wide without making the question hard for a human.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The candidate question.</returns>
    private static CaptchaQuestion BuildCandidate()
    {
        var shape = RandomNumberGenerator.GetInt32(0, QuestionShapeCount);
        return shape switch
        {
            0 => BuildAdditionQuestion(),
            1 => BuildSubtractionQuestion(),
            2 => BuildLetterCountQuestion(),
            _ => BuildWordCountQuestion()
        };
    }

    /// <summary>
    /// Builds "What is seven plus two?".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both operands are spelled out, so the prose holds no digits
    /// at all, and the sum is strictly larger than either operand, so its word cannot collide
    /// with theirs.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The candidate question.</returns>
    private static CaptchaQuestion BuildAdditionQuestion()
    {
        var first = RandomNumberGenerator.GetInt32(2, 10);
        var second = RandomNumberGenerator.GetInt32(2, 10);
        var text = $"What is {NumberWords[first]} plus {NumberWords[second]}?";
        return new CaptchaQuestion(text, AnswersFor(first + second));
    }

    /// <summary>
    /// Builds "What is eleven minus four?".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The subtrahend is kept small enough that the result is always
    /// positive. A result that happens to equal the subtrahend would spell the answer into the
    /// question; <see cref="Build"/>'s screen catches that case and draws again.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The candidate question.</returns>
    private static CaptchaQuestion BuildSubtractionQuestion()
    {
        var second = RandomNumberGenerator.GetInt32(2, 6);
        var first = RandomNumberGenerator.GetInt32(second + 1, 13);
        var text = $"What is {NumberWords[first]} minus {NumberWords[second]}?";
        return new CaptchaQuestion(text, AnswersFor(first - second));
    }

    /// <summary>
    /// Builds "How many letters are in the word printer?".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counting letters needs no arithmetic vocabulary, which suits
    /// visitors who find sums awkward; the word bank is chosen so no noun spells a number.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The candidate question.</returns>
    private static CaptchaQuestion BuildLetterCountQuestion()
    {
        var word = CountableWords[RandomNumberGenerator.GetInt32(0, CountableWords.Length)];
        var text = $"How many letters are in the word '{word}'?";
        return new CaptchaQuestion(text, AnswersFor(word.Length));
    }

    /// <summary>
    /// Builds "How many words are in this line: the sky is blue?".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Counting words reads well aloud, so it is the friendliest of
    /// the four shapes for a screen-reader user.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The candidate question.</returns>
    private static CaptchaQuestion BuildWordCountQuestion()
    {
        var phrase = CountablePhrases[RandomNumberGenerator.GetInt32(0, CountablePhrases.Length)];
        var wordCount = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var text = $"How many words are in this line: '{phrase}'?";
        return new CaptchaQuestion(text, AnswersFor(wordCount));
    }

    /// <summary>
    /// Lists every spelling of a numeric answer that the server will accept.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Digits and the English word both count, because a visitor
    /// hearing "what is seven plus two" may reasonably type either "9" or "nine". Everything is
    /// lower-cased here so the comparison at validation time is a plain equality check.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="value">The number the question works out to.</param>
    /// <returns>The accepted answers.</returns>
    private static IReadOnlyList<string> AnswersFor(int value)
    {
        var answers = new List<string> { value.ToString() };
        if (value >= 0 && value < NumberWords.Length)
            answers.Add(NumberWords[value]);

        return answers;
    }
}
