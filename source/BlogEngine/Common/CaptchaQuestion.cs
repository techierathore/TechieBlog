using System.Collections.Generic;

namespace BlogEngine.Common;

/// <summary>
/// One accessible captcha question together with every spelling of its answer.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The internal result of <see cref="CaptchaQuestionSet"/>. It pairs the
/// prose a visitor reads (or hears read out) with the answers the server will accept.
/// [REQ-UI-057]</para>
///
/// <para><b>Code Flow:</b> <see cref="CaptchaQuestionSet.Build"/> builds one of these,
/// <c>CaptchaSvc.GenerateQuestion</c> puts <see cref="AcceptedAnswers"/> into the short-lived
/// server-side store and sends only <see cref="QuestionText"/> to the browser.</para>
///
/// <para><b>Dependencies:</b> None.</para>
///
/// <para><b>Usage:</b> Engine-internal. <b>Never serialise this type</b> —
/// <see cref="AcceptedAnswers"/> is the secret, and a type that carries both the question and its
/// answer in one object is exactly the shape that gets accidentally returned from a service method
/// or bound to a component parameter. Only <see cref="QuestionText"/> may ever cross the wire; the
/// answers stay in the server-side challenge store and are compared there.</para>
/// </remarks>
public sealed class CaptchaQuestion
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CaptchaQuestion"/> class.
    /// </summary>
    /// <param name="questionText">The prose the visitor reads.</param>
    /// <param name="acceptedAnswers">Every spelling of the answer that counts as correct.</param>
    public CaptchaQuestion(string questionText, IReadOnlyList<string> acceptedAnswers)
    {
        QuestionText = questionText;
        AcceptedAnswers = acceptedAnswers;
    }

    /// <summary>
    /// Gets the question shown to - and read out to - the visitor.
    /// </summary>
    public string QuestionText { get; }

    /// <summary>
    /// Gets every spelling of the answer that counts as correct, lower-cased.
    /// </summary>
    /// <remarks>
    /// Always holds both the digits and the English word, so "9" and "nine" both pass.
    /// </remarks>
    public IReadOnlyList<string> AcceptedAnswers { get; }
}
