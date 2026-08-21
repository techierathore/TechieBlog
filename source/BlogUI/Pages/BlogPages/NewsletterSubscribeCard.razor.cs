using System.Text.RegularExpressions;
using BlogEngine.Services;
using BlogUI.Components;
using Microsoft.AspNetCore.Components;
using TrBlazeUI.Components.Alert;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// State and behaviour for <c>NewsletterSubscribeCard.razor</c> — the public newsletter
/// sign-up card shared by the archive page and the issue view.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Implements the reader-facing half of double opt-in (BRD-98) for
/// REQ-UI-053 and REQ-UI-054. A submitted address becomes a <b>pending</b> subscriber
/// (<c>IsConfirmed = false</c>) and a confirmation link is mailed; the subscription only becomes
/// real when that link is redeemed on <c>/verify/{token}</c>.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The self-hosted captcha answer is checked (BRD-99) so an unattended script cannot
///         mail-bomb a stranger's inbox.</item>
///   <item><see cref="SubscriberSvc.SubscribePendingAsync"/> validates the address, resolves or
///         creates the pending row and mails the confirmation link.</item>
///   <item>An already-confirmed address is told it is subscribed and no second mail goes out.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="SubscriberSvc"/> for the whole write, and
/// <see cref="CaptchaWidget"/> for the human check. [REQ-UI-056] 2026-08-09: the card used to
/// drive <c>ISubscriberRepo</c> and <c>IEmailVerificationService</c> itself. That flow now lives in
/// <see cref="SubscriberSvc.SubscribePendingAsync"/> so the sidebar form and this card share ONE
/// double opt-in implementation — a second copy is how the sidebar drifted onto the auto-confirming
/// path in the first place. The card deliberately does NOT use <c>SubscriberSvc.Subscribe</c>,
/// which auto-confirms and is administrative only.</para>
///
/// <para><b>Usage:</b> <c>&lt;NewsletterSubscribeCard Compact="true" /&gt;</c> renders the
/// condensed call-to-action used at the foot of an issue page.</para>
/// </remarks>
public partial class NewsletterSubscribeCard : ComponentBase
{
    /// <summary>Accepts anything with a single @ and a dotted domain; the confirmation mail is the real test.</summary>
    private static readonly Regex EmailPattern =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private string emailAddress = string.Empty;
    private string? statusMessage;
    private AlertVariant statusVariant = AlertVariant.Info;
    private bool isSubmitting;
    private CaptchaWidget? captcha;

    /// <summary>
    /// Owns the double opt-in write: pending row, verification token and confirmation email.
    /// </summary>
    [Inject]
    public SubscriberSvc SubscriberService { get; set; } = default!;

    /// <summary>
    /// When true the card renders the condensed variant used on an issue page — no header,
    /// no subscriber count, plus a link back to the archive.
    /// </summary>
    [Parameter]
    public bool Compact { get; set; }

    /// <summary>
    /// Number of confirmed subscribers shown as social proof on the full-size card.
    /// Zero hides the line.
    /// </summary>
    [Parameter]
    public int ConfirmedSubscriberCount { get; set; }

    /// <summary>
    /// Prefix for the card's <c>data-testid</c> attributes, so two cards on one page stay
    /// individually addressable.
    /// </summary>
    [Parameter]
    public string TestId { get; set; } = "newsletter-subscribe";

    /// <summary>Element id tying the label to the email input.</summary>
    private string emailInputId => $"{TestId}-email-input";

    /// <summary>
    /// Screens the visitor with the captcha and starts a double opt-in subscription.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The captcha is the first gate — an unanswered or wrong
    /// challenge stops the submission before any row is written. The service then owns the rest:
    /// it validates the address, writes an UNCONFIRMED row and mails the link, so a solved captcha
    /// still cannot put a stranger's address on the list.</para>
    /// <para><b>Flow:</b> check the captcha → submit → report the outcome → re-arm the challenge.</para>
    /// <para><b>Side Effects:</b> May insert a <c>Subscriber</c> row, a verification token row and
    /// send one email; always consumes and replaces the captcha challenge.</para>
    /// </remarks>
    /// <returns>A task that completes when the outcome has been reported to the visitor.</returns>
    private async Task HandleSubscribeAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        // Shape-checked here FIRST so a typo does not burn the single-use captcha challenge.
        // The service re-validates; this check exists only for the message the visitor sees.
        if (!EmailPattern.IsMatch((emailAddress ?? string.Empty).Trim()))
        {
            ShowStatus(AlertVariant.Danger, "Please enter a valid email address.");
            return;
        }

        if (!await IsHumanAsync().ConfigureAwait(true))
        {
            ShowStatus(
                AlertVariant.Danger,
                captcha?.IsQuestionMode == true
                    ? "That was not the right answer. Please answer the new verification question and try again."
                    : "That answer did not match the image. Please try again.");
            return;
        }

        isSubmitting = true;
        try
        {
            await SubmitAsync().ConfigureAwait(true);
        }
        finally
        {
            isSubmitting = false;
            captcha?.Reset();
        }
    }

    /// <summary>
    /// Checks the self-hosted captcha answer.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> [REQ-UI-056] A missing widget is a hard refusal, not a pass.
    /// This card is reachable anonymously, so failing open would reopen exactly the hole the
    /// requirement closes. An empty answer box is refused without a service round trip so an
    /// unanswered challenge is not burned.</para>
    /// <para><b>Side Effects:</b> Consumes the current challenge and issues a new one.</para>
    /// </remarks>
    /// <returns>True only when a rendered widget accepted the typed answer.</returns>
    private async Task<bool> IsHumanAsync()
    {
        if (captcha == null)
        {
            return false;
        }

        if (!captcha.HasAnswer)
        {
            captcha.ShowError(captcha.IsQuestionMode
                ? "Please answer the verification question."
                : "Please type the characters from the verification image.");
            return false;
        }

        return await captcha.ValidateAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Hands the address to the shared double opt-in path and reports what came back.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Re-submitting an address that is still pending re-sends the
    /// link against the SAME row, so a visitor who lost the first email is not duplicated in the
    /// subscriber table. An address that has already confirmed is never mailed a second link.</para>
    /// <para><b>Flow:</b> submit → branch on the outcome → clear the box on success.</para>
    /// <para><b>Side Effects:</b> Delegated to <see cref="SubscriberSvc.SubscribePendingAsync"/>.</para>
    /// </remarks>
    /// <returns>A task that completes when the outcome has been reported.</returns>
    private async Task SubmitAsync()
    {
        var result = await SubscriberService
            .SubscribePendingAsync(emailAddress ?? string.Empty)
            .ConfigureAwait(true);

        if (result.IsFailure)
        {
            ShowStatus(AlertVariant.Danger, result.ErrorMessage ?? "We could not complete the subscription. Please try again.");
            return;
        }

        if (!result.Data)
        {
            ShowStatus(AlertVariant.Info, "That address is already subscribed.");
            return;
        }

        emailAddress = string.Empty;
        ShowStatus(AlertVariant.Success,
            "Almost there — check your inbox and click the confirmation link to start your subscription.");
    }

    /// <summary>
    /// Records the message shown under the form.
    /// </summary>
    /// <param name="variant">Severity of the message.</param>
    /// <param name="message">Visitor-facing text.</param>
    private void ShowStatus(AlertVariant variant, string message)
    {
        statusVariant = variant;
        statusMessage = message;
    }
}
