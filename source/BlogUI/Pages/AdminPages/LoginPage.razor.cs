using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Credential sign-in for staff accounts (REQ-UI-001, BRD-2).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> validates credentials through <see cref="IAuthService"/>, promotes the
/// user into the Blazor authentication state, and routes them to the first surface their role can
/// actually open.</para>
/// <para><b>Landing rules (REQ-UI-001 fix):</b> the page previously sent EVERY role to
/// <c>/admin</c>, which is guarded by <c>EditorOrAbove</c>. An Author or Contributor therefore
/// signed in successfully and was immediately bounced to <c>/access-denied</c>. The destination
/// now comes from <see cref="RoleLandingRoutes"/>, which lives in <c>BlogModels</c> so the rule is
/// unit-testable without booting the host.</para>
/// </remarks>
public partial class LoginPage : ComponentBase
{
    /// <summary>Query-string key carrying the originally requested URL.</summary>
    private const string ReturnUrlKey = "returnUrl";

    private AppUser? validatedUser;
    private ClaimsPrincipal? pageClaimsPrincipal;
    private bool isSubmitting;

    /// <summary>
    /// Login form data (email and password) bound to the sign-in form.
    /// </summary>
    public SvcData LoginDetails { get; set; } = new();

    /// <summary>
    /// Error text shown above the form when a sign-in attempt fails.
    /// </summary>
    public string LoginMesssage { get; set; } = string.Empty;

    /// <summary>
    /// Authentication state provider used to promote the validated user.
    /// </summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; } = default!;

    /// <summary>
    /// Navigation manager used for the post-sign-in redirect.
    /// </summary>
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    /// <summary>
    /// Authentication service that validates the supplied credentials.
    /// </summary>
    [Inject]
    public IAuthService AuthSvc { get; set; } = default!;

    /// <summary>
    /// Cascading authentication state used to short-circuit for signed-in visitors.
    /// </summary>
    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; } = default!;

    /// <summary>
    /// Optional page code supplied by the email-verification link.
    /// </summary>
    [Parameter]
    public string PageCode { get; set; } = default!;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        LoginDetails = new SvcData();
        validatedUser = new AppUser();

        pageClaimsPrincipal = (await AuthStateTask).User;
        if (pageClaimsPrincipal.Identity?.IsAuthenticated == true)
        {
            NavigationManager.NavigateTo(RoleLandingRoutes.PublicHome);
        }
    }

    /// <summary>
    /// Validates the supplied credentials and signs the user in.
    /// </summary>
    /// <remarks>
    /// On success the user is marked authenticated and redirected to the return URL when one was
    /// supplied, otherwise to the landing route chosen by <see cref="RoleLandingRoutes.ResolveFor"/>.
    /// Exception detail is never surfaced to the visitor.
    /// </remarks>
    public async Task ValidateUser()
    {
        if (isSubmitting)
        {
            return;
        }

        isSubmitting = true;
        LoginMesssage = string.Empty;

        try
        {
            validatedUser = await AuthSvc.LoginAsync(new SvcData
            {
                LoginEmail = LoginDetails.LoginEmail,
                LoginPass = LoginDetails.LoginPass
            });

            if (validatedUser == null)
            {
                LoginMesssage = "Invalid email or password. Please try again.";
                LoginDetails.LoginPass = string.Empty;
                return;
            }

            await ((CustomAuthStateProvider)AuthStateProvider).MarkUserAsAuthenticated(validatedUser);
            NavigationManager.NavigateTo(ResolveDestination(validatedUser));
        }
        catch (Exception)
        {
            LoginMesssage = "An error occurred during login. Please try again.";
            LoginDetails.LoginPass = string.Empty;
        }
        finally
        {
            isSubmitting = false;
        }
    }

    /// <summary>
    /// Chooses where to send the user after a successful sign-in.
    /// </summary>
    /// <remarks>
    /// REQ-NFR-023: an account still flagged <c>MustChangePassword</c> goes to the change screen
    /// and nowhere else — ahead of both the requested return URL and the role's landing route. The
    /// return URL is not lost so much as deliberately discarded: the account is still using a
    /// password it did not choose, so it has no business reaching the page it asked for.
    /// <c>ForcePasswordChangeGuard</c> enforces the same rule on every later navigation; deciding
    /// it here as well means the very first hop is already correct rather than a visible bounce.
    /// </remarks>
    /// <param name="signedInUser">The authenticated user.</param>
    /// <returns>The change-password route, the return URL, or the role's landing route.</returns>
    private string ResolveDestination(AppUser signedInUser)
    {
        if (signedInUser.MustChangePassword)
        {
            return RoleLandingRoutes.ChangePassword;
        }

        var returnUrl = ReadReturnUrl();
        return string.IsNullOrWhiteSpace(returnUrl)
            ? RoleLandingRoutes.ResolveFor(signedInUser.UserRole)
            : returnUrl;
    }

    /// <summary>
    /// Reads and validates the <c>returnUrl</c> query-string parameter.
    /// </summary>
    /// <returns>
    /// A site-relative return URL, or an empty string when absent or not site-relative.
    /// Absolute and protocol-relative values are rejected to prevent open-redirect abuse.
    /// </returns>
    private string ReadReturnUrl()
    {
        var query = NavigationManager.ToAbsoluteUri(NavigationManager.Uri).Query;
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }

        var match = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .FirstOrDefault(parts => parts.Length == 2 &&
                string.Equals(Uri.UnescapeDataString(parts[0]), ReturnUrlKey, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return string.Empty;
        }

        var returnUrl = Uri.UnescapeDataString(match[1]);
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//"))
        {
            return string.Empty;
        }

        return returnUrl;
    }
}
