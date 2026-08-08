using Blazored.LocalStorage;
using BlogModels.Interfaces;
using BlogModels;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using BlogModels.Models;

namespace BlogUI;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
    /// <summary>
    /// Claim type carrying the forced-password-change flag (REQ-NFR-023).
    /// </summary>
    /// <remarks>
    /// The flag has to reach the router, and the router only sees a
    /// <see cref="ClaimsPrincipal"/>. Carrying it as a claim means
    /// <c>ForcePasswordChangeGuard</c> can enforce it without another database round trip on every
    /// navigation, and it is refreshed from the database on each circuit start because
    /// <see cref="GetAuthenticationStateAsync"/> rebuilds the identity from the token.
    /// </remarks>
    public const string MustChangePasswordClaim = "MustChangePassword";

    public ILocalStorageService LocalStorageSvc { get; }
    public IAuthService AuthSvc { get; set; }

    public CustomAuthStateProvider(ILocalStorageService aLocalStorageSvc,
        IAuthService aAuthSvc)
    {
        //throw new Exception("CustomAuthenticationStateProviderException");
        LocalStorageSvc = aLocalStorageSvc;
        AuthSvc = aAuthSvc;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsIdentity vIdentity;

        try
        {
            var vAccessToken = await LocalStorageSvc.GetItemAsync<string>(AppConstants.AccessKey);

            if (vAccessToken != null && vAccessToken != string.Empty)
            {
                AppUser? user = await AuthSvc.GetUserByAccessTokenAsync(vAccessToken);
                vIdentity = GetClaimsIdentity(user);
            }
            else
            {
                vIdentity = new ClaimsIdentity();
            }
        }
        catch (InvalidOperationException)
        {
            // JavaScript interop is not available during prerendering
            // Return unauthenticated state during prerender
            vIdentity = new ClaimsIdentity();
        }

        var vClaimsPrincipal = new ClaimsPrincipal(vIdentity);
        return await Task.FromResult(new AuthenticationState(vClaimsPrincipal));
    }

    public async Task MarkUserAsAuthenticated(AppUser aLoggedUser)
    {
        await LocalStorageSvc.SetItemAsync(AppConstants.AccessKey, aLoggedUser.AccessToken);
        await LocalStorageSvc.SetItemAsync(AppConstants.RefreshKey, aLoggedUser.RefreshToken);

        var vIdentity = GetClaimsIdentity(aLoggedUser);
        var vClaimsPrincipal = new ClaimsPrincipal(vIdentity);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(vClaimsPrincipal)));
    }

    /// <summary>
    /// Rebuilds the principal from the database and republishes it (REQ-NFR-023).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> the <c>MustChangePassword</c> claim is a snapshot taken when
    /// the identity was built. Clearing the flag in the database therefore changes nothing the
    /// router can see, and <c>ForcePasswordChangeGuard</c> would bounce the user straight back to
    /// the change screen they just completed. Re-reading the profile behind the stored access token
    /// and notifying is what actually releases them.</para>
    /// <para><b>Flow:</b> re-run <see cref="GetAuthenticationStateAsync"/> → notify.</para>
    /// <para><b>Side Effects:</b> Raises <c>AuthenticationStateChanged</c>.</para>
    /// </remarks>
    /// <returns>A task that completes once the refreshed state has been published.</returns>
    public async Task RefreshAuthenticationStateAsync()
    {
        var refreshed = await GetAuthenticationStateAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(refreshed));
    }

    public async Task MarkUserAsLoggedOut()
    {
        await LocalStorageSvc.RemoveItemAsync(AppConstants.RefreshKey);
        await LocalStorageSvc.RemoveItemAsync(AppConstants.AccessKey);

        var vIdentity = new ClaimsIdentity();
        var vUser = new ClaimsPrincipal(vIdentity);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(vUser)));
    }

    private ClaimsIdentity GetClaimsIdentity(AppUser? aLoggedUser)
    {
        var vClaimsIdentity = new ClaimsIdentity();

        if (aLoggedUser?.EmailId != null)
        {
            vClaimsIdentity = new ClaimsIdentity(new[]
                            {
                                    new Claim(ClaimTypes.PrimarySid,Convert.ToString(aLoggedUser.UserId)),
                                    new Claim(ClaimTypes.Name,aLoggedUser.FullName),
                                    new Claim(ClaimTypes.Email, aLoggedUser.EmailId),
                                    new Claim(ClaimTypes.Role, aLoggedUser.UserRole),
                                    // REQ-NFR-023: carried on the principal so the router can force
                                    // the change screen without querying the database per navigation.
                                    new Claim(MustChangePasswordClaim,
                                        aLoggedUser.MustChangePassword ? "true" : "false")
                                }, "apiauth_type");
        }

        return vClaimsIdentity;
    }
}
