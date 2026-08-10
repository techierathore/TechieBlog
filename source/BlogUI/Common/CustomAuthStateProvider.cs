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

    /// <summary>
    /// Serialises session renewals within one circuit (REQ-FN-008).
    /// </summary>
    /// <remarks>
    /// A single page load can rebuild the authentication state more than once, and a renewal
    /// rotates the token: two resolutions that both read the <i>old</i> value would each try to
    /// redeem it, and the second would be refused because the first already replaced it — signing
    /// the user out at the exact moment the feature was supposed to keep them in. The gate makes
    /// the second one wait and then discover that the session is already renewed. The provider is
    /// scoped to a circuit, so this never contends across users.
    /// </remarks>
    private readonly SemaphoreSlim refreshGate = new(1, 1);

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
                AppUser? user = await ResolveSessionUserAsync(vAccessToken);
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

    /// <summary>
    /// Resolves the signed-in user behind the stored access token, renewing the session when that
    /// token has expired (REQ-FN-008, BRD-6).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This is the one place every circuit passes through to rehydrate
    /// its principal, which is why the refresh belongs here rather than in a page: an expired token
    /// resolved anywhere else would already have become a redirect to the sign-in screen. The stored
    /// refresh token is only reached for when the access token fails, so a live session costs
    /// exactly one lookup, as before.</para>
    /// <para><b>Flow:</b> resolve the access token → on failure read the refresh token → redeem it →
    /// persist the replacement pair → return the user.</para>
    /// <para><b>Side Effects:</b> On a successful renewal, rewrites both browser storage slots and
    /// the session's <c>UserLogin</c> row. Nothing is written when the session cannot be renewed —
    /// the stale values are left for <c>MarkUserAsLoggedOut</c> or the desktop head to clear, so a
    /// transient database failure does not sign a user out permanently.</para>
    /// <para><b>Storing the replacement is not optional.</b> The engine rewrites the session row on
    /// use, so the token that was presented stops working the moment the refresh succeeds. Dropping
    /// the two writes below would renew the session on every single render and leave the browser
    /// holding a value that is already dead.</para>
    /// <para><b>Scope of the guarantee.</b> Blazor Server asks for the authentication state when a
    /// circuit starts, not on a timer, so a session is renewed at the next reconnect, reload or
    /// navigation that rebuilds the state — not at the instant the token expires. Within one
    /// long-lived circuit nothing re-checks, which is the same behaviour the product had before and
    /// is why the access-token lifetime is a policy value rather than something to minimise.</para>
    /// </remarks>
    /// <param name="accessToken">The access token held in browser local storage.</param>
    /// <returns>The signed-in user, or <c>null</c> when the session is over.</returns>
    protected async Task<AppUser?> ResolveSessionUserAsync(string accessToken)
    {
        var user = await AuthSvc.GetUserByAccessTokenAsync(accessToken);
        if (user != null)
        {
            return user;
        }

        await refreshGate.WaitAsync();
        try
        {
            var storedToken = await LocalStorageSvc.GetItemAsync<string>(AppConstants.AccessKey);
            if (!string.IsNullOrEmpty(storedToken) && storedToken != accessToken)
            {
                // Another resolution on this circuit renewed the session while this one waited.
                var renewedElsewhere = await AuthSvc.GetUserByAccessTokenAsync(storedToken);
                if (renewedElsewhere != null)
                {
                    return renewedElsewhere;
                }

                accessToken = storedToken;
            }

            var refreshToken = await LocalStorageSvc.GetItemAsync<string>(AppConstants.RefreshKey);
            if (string.IsNullOrEmpty(refreshToken))
            {
                return null;
            }

            var refreshedUser = await AuthSvc.RefreshTokenAsync(
                new RefreshRequest { AccessToken = accessToken, RefreshToken = refreshToken });
            if (refreshedUser == null)
            {
                return null;
            }

            await LocalStorageSvc.SetItemAsync(AppConstants.AccessKey, refreshedUser.AccessToken);
            await LocalStorageSvc.SetItemAsync(AppConstants.RefreshKey, refreshedUser.RefreshToken);
            return refreshedUser;
        }
        finally
        {
            refreshGate.Release();
        }
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
