using System.Text.RegularExpressions;
using BlogEngine.Services;
using BlogModels;
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
/// (<see cref="Subscriber.IsConfirmed"/> = <c>false</c>) and a confirmation link is mailed;
/// the subscription only becomes real when that link is redeemed on <c>/verify/{token}</c>.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The address is validated locally, then the self-hosted captcha answer is checked
///         (BRD-99) so an unattended script cannot mail-bomb a stranger's inbox.</item>
///   <item>An already-confirmed address is told it is subscribed and no second mail goes out.</item>
///   <item>Otherwise a pending subscriber row is created (or an existing unconfirmed row is
///         reused) and <see cref="IEmailVerificationService.IssueAsync"/> mails the link.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="ISubscriberRepo"/> for the pending row and
/// <see cref="IEmailVerificationService"/> for the token and its email. The card deliberately
/// does NOT use <c>SubscriberSvc.Subscribe</c>, which auto-confirms (a pre-double-opt-in
/// behaviour retained for REQ-FN-030's sidebar form).</para>
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
    /// Pending-row store. A pending subscriber is written directly because the repository is
    /// the only component that can persist <c>IsConfirmed = false</c>.
    /// </summary>
    [Inject]
    public ISubscriberRepo SubscriberRepo { get; set; } = default!;

    /// <summary>
    /// Issues and mails the single-use confirmation link (REQ-FN-048).
    /// </summary>
    [Inject]
    public IEmailVerificationService EmailVerification { get; set; } = default!;

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
    /// Validates the form, creates a pending subscriber and mails the confirmation link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The subscriber row is written BEFORE the token is issued,
    /// because the token has to carry the pending row's id. A failure to mail therefore leaves
    /// an unconfirmed row that receives nothing — the safe direction to fail in. An address that
    /// is already confirmed is never mailed a second link.</para>
    /// <para><b>Flow:</b> validate address → check captcha → resolve or create the pending row →
    /// issue the token → report the outcome and reset the challenge.</para>
    /// <para><b>Side Effects:</b> Inserts a <c>Subscriber</c> row, inserts a verification token
    /// row and sends one email.</para>
    /// </remarks>
    /// <returns>A task that completes when the outcome has been reported to the visitor.</returns>
    private async Task HandleSubscribeAsync()
    {
        if (isSubmitting)
        {
            return;
        }

        var email = (emailAddress ?? string.Empty).Trim().ToLowerInvariant();
        if (!EmailPattern.IsMatch(email))
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
            await SubmitAsync(email).ConfigureAwait(true);
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
    /// <para><b>Business Logic:</b> The widget is supplied by REQ-UI-056. If it has not been
    /// rendered the form still works rather than locking every visitor out, which would be a
    /// worse failure than a missing challenge on a personal blog.</para>
    /// <para><b>Flow:</b> delegate to the widget when present.</para>
    /// <para><b>Side Effects:</b> None; the widget owns its own challenge state.</para>
    /// </remarks>
    /// <returns>True when the visitor passed the challenge.</returns>
    private async Task<bool> IsHumanAsync()
    {
        if (captcha == null)
        {
            return true;
        }

        return await captcha.ValidateAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Creates or reuses the pending subscriber row and mails its confirmation link.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Re-submitting an address that is still pending re-sends the
    /// link against the SAME row, so a visitor who lost the first email is not duplicated in the
    /// subscriber table.</para>
    /// <para><b>Flow:</b> look the address up → short-circuit when confirmed → insert when new →
    /// issue the token → report.</para>
    /// <para><b>Side Effects:</b> May insert a <c>Subscriber</c> row; issues a token and an email.</para>
    /// </remarks>
    /// <param name="email">The normalised address being subscribed.</param>
    /// <returns>A task that completes when the outcome has been reported.</returns>
    private async Task SubmitAsync(string email)
    {
        try
        {
            var existing = SubscriberRepo.GetByEmail(email);
            if (existing != null && existing.IsConfirmed)
            {
                ShowStatus(AlertVariant.Info, "That address is already subscribed.");
                return;
            }

            var subscriberId = existing?.SubscriberId ?? CreatePendingSubscriber(email);
            var issued = await EmailVerification
                .IssueAsync(email, DeriveName(email), EmailVerificationPurpose.Subscription, subscriberId, string.Empty)
                .ConfigureAwait(true);

            if (issued.IsFailure)
            {
                ShowStatus(AlertVariant.Danger, issued.ErrorMessage ?? "We could not start the subscription. Please try again.");
                return;
            }

            emailAddress = string.Empty;
            ShowStatus(AlertVariant.Success,
                "Almost there — check your inbox and click the confirmation link to start your subscription.");
        }
        catch (Exception)
        {
            ShowStatus(AlertVariant.Danger, "We could not complete the subscription. Please try again.");
        }
    }

    /// <summary>
    /// Writes the pending subscriber row.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>IsConfirmed</c> is false and stays false until the mailed
    /// link is redeemed — that is what makes the opt-in double.</para>
    /// <para><b>Flow:</b> build the row → insert → return the generated id.</para>
    /// <para><b>Side Effects:</b> Inserts one <c>Subscriber</c> row.</para>
    /// </remarks>
    /// <param name="email">The normalised address.</param>
    /// <returns>The new subscriber id.</returns>
    private long CreatePendingSubscriber(string email)
    {
        return SubscriberRepo.InsertToGetId(new Subscriber
        {
            Email = email,
            Name = DeriveName(email),
            SubscribedOn = DateTime.UtcNow,
            IsConfirmed = false,
            IsActive = false,
            Preferences = string.Empty
        });
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

    /// <summary>
    /// Derives a display name from an address, since the public form asks only for the email.
    /// </summary>
    /// <param name="email">The normalised address.</param>
    /// <returns>The local part of the address.</returns>
    private static string DeriveName(string email)
    {
        var atIndex = email.IndexOf('@');
        return atIndex > 0 ? email[..atIndex] : email;
    }
}
