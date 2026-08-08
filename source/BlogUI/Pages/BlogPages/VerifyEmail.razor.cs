using System;
using System.Threading.Tasks;
using BlogEngine.Services;
using BlogModels;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace BlogUI.Pages.BlogPages;

/// <summary>
/// Code-behind for the double opt-in confirmation landing page at <c>/verify/{token}</c>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Turns the single-use token mailed to an anonymous commenter, rater or
/// newsletter subscriber into one of four visitor-facing outcomes, and says plainly WHAT was
/// confirmed. [REQ-UI-055 / BRD-98]</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The token is read - NOT redeemed - through
///   <see cref="IEmailVerificationTokenRepo.GetByTokenAsync"/>, so an already-used token can be
///   told apart from an expired one and from one that never existed.</item>
///   <item>Only a row that is unused AND inside its window is handed to
///   <see cref="IEmailVerificationService.ConsumeAsync"/>, which redeems it atomically.</item>
///   <item>Promotion of the pending row - comment, rating or subscriber - belongs to
///   <see cref="IEmailVerificationService"/>; this page only chooses the wording.</item>
///   <item>An expired row still carries its address and purpose, so a fresh link can be issued
///   without asking the visitor to type anything again.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="IEmailVerificationService"/>,
/// <see cref="IEmailVerificationTokenRepo"/>, <see cref="IBlogCommentRepo"/>,
/// <see cref="IPostRatingRepo"/> and <see cref="IBlogPostRepo"/>, all registered by
/// <c>BlogSvcInitializer</c> / <c>EngagementSvcInitializer</c>.</para>
///
/// <para><b>Usage:</b> The redemption runs from <see cref="OnAfterRenderAsync"/> on the first
/// INTERACTIVE render, never from <c>OnInitializedAsync</c>. Under global interactive server
/// rendering the initialisation pass runs twice - once while prerendering, once when the circuit
/// opens - and a token consumed on the prerender pass would report "already confirmed" the
/// instant the page became live.</para>
/// </remarks>
public partial class VerifyEmail
{
    /// <summary>Link used when the confirmed row cannot be traced back to a post.</summary>
    private const string BrowseUrl = "/categories";

    /// <summary>
    /// Gets or sets the confirmation token taken from the route.
    /// </summary>
    [Parameter]
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the double opt-in service that redeems tokens.
    /// </summary>
    [Inject]
    public IEmailVerificationService VerificationSvc { get; set; } = default!;

    /// <summary>
    /// Gets or sets the token store, read directly so the page can distinguish
    /// "already used" from "expired" from "unknown".
    /// </summary>
    [Inject]
    public IEmailVerificationTokenRepo TokenRepo { get; set; } = default!;

    /// <summary>
    /// Gets or sets the comment repository, used to trace a comment back to its post.
    /// </summary>
    [Inject]
    public IBlogCommentRepo BlogCommentRepo { get; set; } = default!;

    /// <summary>
    /// Gets or sets the rating repository, used to trace a rating back to its post.
    /// </summary>
    [Inject]
    public IPostRatingRepo PostRatingRepo { get; set; } = default!;

    /// <summary>
    /// Gets or sets the post repository, used to build the "back to the article" link.
    /// </summary>
    [Inject]
    public IBlogPostRepo BlogPostRepo { get; set; } = default!;

    /// <summary>
    /// Gets or sets the logger for confirmation outcomes.
    /// </summary>
    [Inject]
    public ILogger<VerifyEmail> Logger { get; set; } = default!;

    private VerifyState state = VerifyState.Loading;
    private string purpose = string.Empty;
    private string confirmedEmail = string.Empty;
    private string? returnUrl;
    private string? resendMessage;
    private bool canResend;
    private bool isResending;
    private EmailVerificationToken? pendingToken;

    /// <summary>
    /// The outcome of checking a confirmation link.
    /// </summary>
    private enum VerifyState
    {
        /// <summary>The token has not been checked yet.</summary>
        Loading,

        /// <summary>A comment or rating token was redeemed.</summary>
        Confirmed,

        /// <summary>A newsletter subscription token was redeemed.</summary>
        SubscriptionConfirmed,

        /// <summary>The token had already been redeemed; nothing changed.</summary>
        AlreadyVerified,

        /// <summary>The token is unknown or past its 24-hour window.</summary>
        ExpiredOrInvalid
    }

    /// <summary>
    /// Gets the page heading used in the browser title.
    /// </summary>
    private string PageHeading => state switch
    {
        VerifyState.Confirmed => "Email confirmed",
        VerifyState.SubscriptionConfirmed => "You're subscribed",
        VerifyState.AlreadyVerified => "Already confirmed",
        VerifyState.ExpiredOrInvalid => "Link no longer valid",
        _ => "Confirming your email"
    };

    /// <summary>
    /// Gets the one-line statement of what the link was for and what happened to it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The acceptance criterion for [REQ-UI-055] is that the page
    /// names the thing that was confirmed - a comment, a rating or a subscription - so this
    /// string always leads with the purpose, in every state.</para>
    /// </remarks>
    private string ConfirmationSummary => state switch
    {
        VerifyState.Confirmed => $"{PurposeSentence} for {DisplayEmail} is confirmed.",
        VerifyState.SubscriptionConfirmed => $"{PurposeSentence} for {DisplayEmail} is confirmed.",
        VerifyState.AlreadyVerified => $"{PurposeSentence} for {DisplayEmail} was confirmed earlier.",
        VerifyState.ExpiredOrInvalid when purpose != null =>
            $"{PurposeSentence} for {DisplayEmail} has NOT been confirmed.",
        VerifyState.ExpiredOrInvalid => "Nothing has been confirmed.",
        _ => string.Empty
    };

    /// <summary>
    /// Gets the title of the success alert, naming the confirmed item.
    /// </summary>
    private string ConfirmedThingTitle => IsPurpose(EmailVerificationPurpose.Rating)
        ? "Rating confirmed"
        : "Comment confirmed";

    /// <summary>
    /// Gets the success alert body, explaining what happens next.
    /// </summary>
    private string ConfirmationDetail => IsPurpose(EmailVerificationPurpose.Rating)
        ? "Your rating now counts towards this article's average score."
        : "Your comment is queued for moderation and will appear once it is approved.";

    /// <summary>
    /// Gets a human phrase naming what the link confirms.
    /// </summary>
    private string PurposeSentence
    {
        get
        {
            if (IsPurpose(EmailVerificationPurpose.Rating))
                return "Your rating";

            if (IsPurpose(EmailVerificationPurpose.Subscription))
                return "Your newsletter subscription";

            if (IsPurpose(EmailVerificationPurpose.Comment))
                return "Your comment";

            return "Your submission";
        }
    }

    /// <summary>
    /// Gets the address to show, or a neutral placeholder when the token was unknown.
    /// </summary>
    private string DisplayEmail =>
        string.IsNullOrWhiteSpace(confirmedEmail) ? "this address" : confirmedEmail;

    /// <summary>
    /// Redeems the token on the first interactive render.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Deliberately NOT <c>OnInitializedAsync</c>. With global
    /// interactive server rendering the initialisation pass runs once while prerendering and
    /// again when the circuit opens; a single-use token consumed on the first pass would then
    /// render as "already confirmed". <see cref="OnAfterRenderAsync"/> never runs during
    /// prerendering, so the redemption happens exactly once.</para>
    /// <para><b>Flow:</b> guard the first render, check the token, re-render.</para>
    /// <para><b>Side Effects:</b> May redeem a token and promote the row behind it.</para>
    /// </remarks>
    /// <param name="firstRender">True on the component's first render.</param>
    /// <returns>A task that completes when the outcome has been rendered.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        await CheckTokenAsync().ConfigureAwait(false);
        await InvokeAsync(StateHasChanged).ConfigureAwait(false);
    }

    /// <summary>
    /// Classifies the token and, when it is still good, redeems it.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The row is READ before it is redeemed so the four outcomes
    /// can be told apart. A used row is never handed to the service, which guarantees a reused
    /// link confirms nothing. An expired row is kept in <c>pendingToken</c> so a replacement
    /// link can be issued to the same address.</para>
    /// <para><b>Flow:</b> validate, read, classify, consume, resolve the return link.</para>
    /// <para><b>Side Effects:</b> On the valid path, redeems the token and promotes its target.</para>
    /// </remarks>
    /// <returns>A task that completes when <c>state</c> has been decided.</returns>
    private async Task CheckTokenAsync()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            state = VerifyState.ExpiredOrInvalid;
            return;
        }

        try
        {
            var row = await TokenRepo.GetByTokenAsync(Token).ConfigureAwait(false);
            if (row == null)
            {
                Logger.LogWarning("A confirmation link was opened with an unknown token");
                state = VerifyState.ExpiredOrInvalid;
                return;
            }

            purpose = row.Purpose;
            confirmedEmail = row.Email;

            if (row.IsUsed)
            {
                state = VerifyState.AlreadyVerified;
                returnUrl = ResolveReturnUrl(row);
                return;
            }

            if (row.ExpiresOn <= DateTime.UtcNow)
            {
                state = VerifyState.ExpiredOrInvalid;
                pendingToken = row;
                canResend = true;
                return;
            }

            await ConsumeTokenAsync(row).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to check a confirmation token");
            state = VerifyState.ExpiredOrInvalid;
        }
    }

    /// <summary>
    /// Redeems a token that passed the state checks.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A failure here means another request won the race between
    /// the read and the atomic consume, so the honest outcome is "already confirmed" rather than
    /// an error - nothing was lost and nothing was confirmed twice.</para>
    /// <para><b>Flow:</b> consume, promote a subscription, resolve the return link.</para>
    /// <para><b>Side Effects:</b> Flips the token row and promotes the pending target.</para>
    /// </remarks>
    /// <param name="row">The token row as it was read.</param>
    /// <returns>A task that completes when the outcome has been decided.</returns>
    private async Task ConsumeTokenAsync(EmailVerificationToken row)
    {
        var result = await VerificationSvc.ConsumeAsync(Token).ConfigureAwait(false);
        if (result.IsFailure)
        {
            Logger.LogInformation("A confirmation token was refused: {Reason}", result.ErrorMessage);
            state = VerifyState.AlreadyVerified;
            returnUrl = ResolveReturnUrl(row);
            return;
        }

        var consumed = result.Data;
        purpose = consumed.Purpose;
        confirmedEmail = consumed.Email;
        returnUrl = ResolveReturnUrl(consumed);

        // The subscriber row is flipped by EmailVerificationSvc.PromoteTargetAsync, alongside the
        // comment and rating promotions. The page only decides which wording to show.
        state = IsPurpose(EmailVerificationPurpose.Subscription)
            ? VerifyState.SubscriptionConfirmed
            : VerifyState.Confirmed;
    }

    /// <summary>
    /// Issues a replacement link for an expired token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The expired row already holds the address, display name,
    /// purpose and target, so the visitor never has to retype anything. Only offered for a row
    /// that expired unused - a link that was already redeemed has nothing left to confirm.</para>
    /// <para><b>Flow:</b> guard, re-issue, report the outcome in place.</para>
    /// <para><b>Side Effects:</b> Inserts a new token row and sends one email.</para>
    /// </remarks>
    /// <returns>A task that completes when the outcome message has been set.</returns>
    private async Task ResendLinkAsync()
    {
        if (pendingToken == null || isResending)
            return;

        isResending = true;
        resendMessage = null;

        try
        {
            var issued = await VerificationSvc.IssueAsync(
                pendingToken.Email,
                pendingToken.DisplayName,
                pendingToken.Purpose,
                pendingToken.TargetId ?? 0,
                null).ConfigureAwait(false);

            if (issued.IsSuccess)
            {
                canResend = false;
                resendMessage = $"A fresh confirmation link is on its way to {pendingToken.Email}. " +
                                "It is valid for 24 hours.";
            }
            else
            {
                resendMessage = issued.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to re-issue a confirmation link");
            resendMessage = "We could not send a new link. Please try again later.";
        }
        finally
        {
            isResending = false;
        }
    }

    /// <summary>
    /// Builds the "back to the article" link for a comment or rating token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A subscription has no article behind it, and a target row may
    /// have been deleted since the link was mailed, so a null result is a normal outcome - the
    /// page simply drops that button rather than offering a dead link.</para>
    /// <para><b>Side Effects:</b> Two read-only lookups at most.</para>
    /// </remarks>
    /// <param name="token">The token whose target is being traced.</param>
    /// <returns>The post URL, or null when there is no article to return to.</returns>
    private string? ResolveReturnUrl(EmailVerificationToken token)
    {
        if (token?.TargetId is not > 0)
            return null;

        try
        {
            var postId = ResolvePostId(token);
            if (postId <= 0)
                return null;

            var post = BlogPostRepo.GetSingle(postId);
            return string.IsNullOrWhiteSpace(post?.Slug) ? null : $"/post/{post.Slug}";
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not resolve the article behind verification token {TokenId}",
                token.TokenId);
            return null;
        }
    }

    /// <summary>
    /// Finds the post a token's target row belongs to.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A comment target is a comment id and a rating target is a
    /// rating id; both rows carry the post they belong to. Any other purpose has no post.</para>
    /// <para><b>Side Effects:</b> One read-only lookup.</para>
    /// </remarks>
    /// <param name="token">The token whose target is being traced.</param>
    /// <returns>The post id, or zero when there is none.</returns>
    private long ResolvePostId(EmailVerificationToken token)
    {
        var targetId = token.TargetId ?? 0;

        if (IsPurpose(EmailVerificationPurpose.Comment))
            return BlogCommentRepo.GetSingle(targetId)?.PostID ?? 0;

        if (IsPurpose(EmailVerificationPurpose.Rating))
            return PostRatingRepo.GetSingle(targetId)?.PostId ?? 0;

        return 0;
    }

    /// <summary>
    /// Tests the current token's purpose against one of the known values.
    /// </summary>
    /// <param name="candidate">One of the <see cref="EmailVerificationPurpose"/> constants.</param>
    /// <returns>True when the token was issued for that purpose.</returns>
    private bool IsPurpose(string candidate) =>
        string.Equals(purpose, candidate, StringComparison.OrdinalIgnoreCase);
}
