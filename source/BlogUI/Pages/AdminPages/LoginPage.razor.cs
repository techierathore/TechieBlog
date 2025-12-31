/// <summary>
/// Code-behind for LoginPage component.
/// Handles user authentication and login flow.
/// </summary>
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogUI.Pages.AdminPages;

/// <summary>
/// Partial class containing logic for the login page.
/// Validates user credentials and manages authentication state.
/// </summary>
public partial class LoginPage : ComponentBase
{
    /// <summary>
    /// Contains login form data (email and password).
    /// </summary>
    public SvcData LoginDetails { get; set; }

    /// <summary>
    /// Message displayed to user on login errors.
    /// </summary>
    public string LoginMesssage { get; set; }

    /// <summary>
    /// Authentication state provider for managing user login state.
    /// </summary>
    [Inject]
    public AuthenticationStateProvider AuthStateProvider { get; set; }

    /// <summary>
    /// Navigation manager for redirecting after login.
    /// </summary>
    [Inject]
    public NavigationManager NavigationManager { get; set; }

    /// <summary>
    /// Authentication service for validating credentials.
    /// </summary>
    [Inject]
    public IAuthService AuthSvc { get; set; }

    private AppUser vValidatedUser;
    ClaimsPrincipal PageClaimsPrincipal;

    /// <summary>
    /// Cascading authentication state task.
    /// </summary>
    [CascadingParameter]
    private Task<AuthenticationState> AuthStateTask { get; set; }

    /// <summary>
    /// Optional page code parameter for email verification.
    /// </summary>
    [Parameter]
    public string PageCode { get; set; }

    /// <summary>
    /// Initializes the login page and checks existing authentication.
    /// </summary>
    protected async override Task OnInitializedAsync()
    {
        LoginDetails = new SvcData();
        vValidatedUser = new AppUser();

        PageClaimsPrincipal = (await AuthStateTask).User;
        if (PageClaimsPrincipal.Identity.IsAuthenticated)
        {
            NavigationManager.NavigateTo("/");
        }
    }

    /// <summary>
    /// Validates user credentials and authenticates the user.
    /// </summary>
    /// <remarks>
    /// <para><b>Flow:</b></para>
    /// <list type="number">
    ///   <item>Calls AuthSvc.LoginAsync with credentials</item>
    ///   <item>On success, marks user as authenticated and redirects to admin dashboard</item>
    ///   <item>On failure, displays user-friendly error message</item>
    /// </list>
    /// <para><b>Security:</b> Exception details are not exposed to users.</para>
    /// </remarks>
    public async Task ValidateUser()
    {
        try
        {
            LoginMesssage = string.Empty;
            vValidatedUser = await AuthSvc.LoginAsync(new SvcData
            {
                LoginEmail = LoginDetails.LoginEmail,
                LoginPass = LoginDetails.LoginPass
            });
            if (vValidatedUser == null)
            {
                LoginMesssage = "Invalid email or password. Please try again.";
                return;
            }
            await ((CustomAuthStateProvider)AuthStateProvider).MarkUserAsAuthenticated(vValidatedUser);
            NavigationManager.NavigateTo("/admin");
        }
        catch (Exception)
        {
            LoginMesssage = "An error occurred during login. Please try again.";
        }
    }

    /// <summary>
    /// Handler for dialog close events.
    /// </summary>
    public void OnDialogClose()
    {
        StateHasChanged();
    }
}
