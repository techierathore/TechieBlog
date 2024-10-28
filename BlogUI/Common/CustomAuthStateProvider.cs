using Blazored.LocalStorage;
using BlogModels.Interfaces;
using BlogModels;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
using BlogModels.Models;

namespace BlogUI;

public class CustomAuthStateProvider : AuthenticationStateProvider
{
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
        var vAccessToken = await LocalStorageSvc.GetItemAsync<string>(AppConstants.AccessKey);

        ClaimsIdentity vIdentity;
        if (vAccessToken != null && vAccessToken != string.Empty)
        {
            AppUser user = await AuthSvc.GetUserByAccessTokenAsync(vAccessToken);
            vIdentity = GetClaimsIdentity(user);
        }
        else
        {
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

    public async Task MarkUserAsLoggedOut()
    {
        await LocalStorageSvc.RemoveItemAsync(AppConstants.RefreshKey);
        await LocalStorageSvc.RemoveItemAsync(AppConstants.AccessKey);

        var vIdentity = new ClaimsIdentity();
        var vUser = new ClaimsPrincipal(vIdentity);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(vUser)));
    }

    private ClaimsIdentity GetClaimsIdentity(AppUser aLoggedUser)
    {
        var vClaimsIdentity = new ClaimsIdentity();

        if (aLoggedUser.EmailId != null)
        {
            vClaimsIdentity = new ClaimsIdentity(new[]
                            {
                                    new Claim(ClaimTypes.PrimarySid,Convert.ToString(aLoggedUser.UserId)),
                                    new Claim(ClaimTypes.Name,aLoggedUser.FullName),
                                    new Claim(ClaimTypes.Email, aLoggedUser.EmailId),
                                    new Claim(ClaimTypes.Role, aLoggedUser.UserRole)
                                }, "apiauth_type");
        }

        return vClaimsIdentity;
    }
}
