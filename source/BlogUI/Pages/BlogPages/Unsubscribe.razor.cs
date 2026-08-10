using System;
using System.Threading.Tasks;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// Code-behind for the anonymous one-click unsubscribe landing page at <c>/unsubscribe/{token}</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Honours the unsubscribe link every newsletter message carries, and reports
/// which of three things happened. [REQ-FN-032 / BRD-59]</para>
///
/// <para><b>Why this page exists at all.</b> <c>NewsletterSvc.BuildUnsubscribeUrl</c> has mailed
/// <c>{BaseUrl}/unsubscribe/{token}</c> since the newsletter feature shipped, but nothing was ever
/// routed at that address: every issue already delivered carried an unsubscribe link that answered
/// HTTP 404 with a zero-byte body. That is a compliance defect — a mailing list with no working way
/// off it — rather than a missing screen.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The route parameter carries the subscriber's opaque token.</item>
///   <item><see cref="OnAfterRenderAsync"/> — never <c>OnInitializedAsync</c>, see below — hands it
///         to <see cref="INewsletterService.UnsubscribeAsync"/>.</item>
///   <item>The returned <see cref="UnsubscribeOutcome"/> chooses one of three final states; a failed
///         result chooses the fourth.</item>
/// </list>
///
/// <para><b>Anonymous by design.</b> The page declares no <c>[Authorize]</c> attribute and never
/// reads the authentication state. The link is followed from a mail client by someone who may have
/// no account at all — an unsubscribe behind a sign-in wall is not an unsubscribe. The unguessable
/// per-subscriber token IS the authorisation, which is also why the URL carries a token rather than
/// an email address: an address in the URL would let anyone unsubscribe a stranger.</para>
///
/// <para><b>It acts on load, deliberately.</b> "One-click" means the click in the mail client is the
/// only action required, so the opt-out is applied as soon as the page opens rather than behind a
/// confirm button. The trade-off is that a link-scanning proxy can trigger it; that is the accepted
/// direction of error for an opt-out — an unwanted unsubscribe is recoverable by resubscribing,
/// an unhonoured one is a compliance breach. The operation is idempotent, so a scanner that opens
/// the link twice changes nothing the second time.</para>
///
/// <para><b>Dependencies:</b> <see cref="INewsletterService"/>, registered transient by
/// <c>BlogSvcInitializer</c>, and <see cref="ILogger{TCategoryName}"/>.</para>
///
/// <para><b>Usage:</b> The work runs from <see cref="OnAfterRenderAsync"/> on the first INTERACTIVE
/// render, for the same reason <c>VerifyEmail</c> does it there: under global interactive server
/// rendering the initialisation pass runs twice — once while prerendering, once when the circuit
/// opens — and a write performed on the prerender pass would report "already unsubscribed" the
/// instant the page became live.</para>
///
/// <para><b>Security:</b> Nothing here reveals whether a token exists. The failure path renders the
/// service's deliberately vague message, and the token is never logged or echoed into the
/// markup.</para>
/// </remarks>
public partial class Unsubscribe
{
    /// <summary>
    /// Fallback wording when the service returns a failure with no message of its own.
    /// </summary>
    private const string DefaultErrorMessage = "This unsubscribe link is not valid.";

    /// <summary>
    /// Gets or sets the subscriber's unsubscribe token, taken from the route.
    /// </summary>
    [Parameter]
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the newsletter service that resolves and consumes the token.
    /// </summary>
    [Inject]
    public INewsletterService NewsletterService { get; set; } = default!;

    /// <summary>
    /// Gets or sets the logger for unsubscribe outcomes.
    /// </summary>
    [Inject]
    public ILogger<Unsubscribe> Logger { get; set; } = default!;

    /// <summary>
    /// Gets the state the page is rendering. Exposed for component tests, which assert the outcome
    /// rather than scraping the markup.
    /// </summary>
    public UnsubscribeState State { get; private set; } = UnsubscribeState.Loading;

    /// <summary>
    /// Gets the message shown on the invalid state, taken from the service so the two cannot drift.
    /// </summary>
    public string ErrorMessage { get; private set; } = DefaultErrorMessage;

    /// <summary>
    /// The outcome of following an unsubscribe link.
    /// </summary>
    public enum UnsubscribeState
    {
        /// <summary>The token has not been applied yet.</summary>
        Loading,

        /// <summary>The subscriber was on the list and has just been opted out.</summary>
        Unsubscribed,

        /// <summary>The token resolved, but the subscriber was already opted out; nothing changed.</summary>
        AlreadyUnsubscribed,

        /// <summary>The token resolved to nobody, or the request could not be processed.</summary>
        Invalid
    }

    /// <summary>
    /// Gets the page heading used in the browser title.
    /// </summary>
    private string PageHeading => State switch
    {
        UnsubscribeState.Unsubscribed => "You have been unsubscribed",
        UnsubscribeState.AlreadyUnsubscribed => "Already unsubscribed",
        UnsubscribeState.Invalid => "Link not valid",
        _ => "Unsubscribing"
    };

    /// <summary>
    /// Applies the unsubscribe token on the first interactive render.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deliberately NOT <c>OnInitializedAsync</c>. With global
    /// interactive server rendering the initialisation pass runs once while prerendering and again
    /// when the circuit opens, so a write performed there would run twice and the live render would
    /// report "already unsubscribed". <see cref="OnAfterRenderAsync"/> never runs during
    /// prerendering, so the opt-out is applied exactly once.</para>
    /// <para><b>Flow:</b> guard the first render → apply the token → re-render.</para>
    /// <para><b>Side Effects:</b> May deactivate a subscriber.</para>
    /// </remarks>
    /// <param name="firstRender">True on the component's first render.</param>
    /// <returns>A task that completes when the outcome has been rendered.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        await ApplyTokenAsync().ConfigureAwait(false);
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);
    }

    /// <summary>
    /// Hands the token to the newsletter service and maps its verdict onto a page state.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every decision belongs to the service — whether the token
    /// resolves, whether the subscriber was still on the list, and what to say when it does not
    /// resolve. The page adds no rule of its own, so the "does not say whether the token existed"
    /// guarantee has exactly one implementation.</para>
    /// <para><b>Flow:</b> guard a blank route value → call the service → branch on the result.</para>
    /// <para><b>Side Effects:</b> On the success path the subscriber row is deactivated. An
    /// unexpected exception is logged WITHOUT the token and rendered as the invalid state.</para>
    /// </remarks>
    /// <returns>A task that completes when <see cref="State"/> has been decided.</returns>
    private async Task ApplyTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            State = UnsubscribeState.Invalid;
            return;
        }

        try
        {
            var result = await NewsletterService.UnsubscribeAsync(Token).ConfigureAwait(false);
            if (result.IsFailure)
            {
                ErrorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                    ? DefaultErrorMessage
                    : result.ErrorMessage;
                State = UnsubscribeState.Invalid;
                return;
            }

            State = result.Data == UnsubscribeOutcome.AlreadyUnsubscribed
                ? UnsubscribeState.AlreadyUnsubscribed
                : UnsubscribeState.Unsubscribed;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to apply an unsubscribe link");
            ErrorMessage = DefaultErrorMessage;
            State = UnsubscribeState.Invalid;
        }
    }
}
