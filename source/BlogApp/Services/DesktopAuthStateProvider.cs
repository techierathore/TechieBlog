using Blazored.LocalStorage;
using BlogModels;
using BlogModels.Interfaces;
// CustomAuthStateProvider lives in the BlogUI RCL's root namespace. This using is required
// explicitly: BlogApp's `@using BlogUI` in _Imports.razor applies to .razor files only, and the
// project's implicit usings do not cover project references. Without it the MAUI target framework
// fails to compile while the plain net10.0 fallback TFM still builds — which is how a green
// solution build on WSL hid a broken desktop head (REQ-FN-046).
using BlogUI;
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace BlogApp.Services;

/// <summary>
/// Desktop authentication state provider: the shared provider, made safe for a head whose
/// database can be absent or repointed.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> BlogApp reuses <see cref="CustomAuthStateProvider"/> so the shared BlogUI
/// screens behave exactly as they do on the website — <c>LoginPage</c> even casts the provider to
/// that type. Two desktop-only situations break the shared implementation, and both are handled
/// here rather than by forking BlogUI (REQ-UI-051):</para>
/// <list type="number">
///   <item>The app can boot with NO database at all (first run, or after the connection was
///   cleared), while the WebView still holds a token in local storage from a previous
///   connection.</item>
///   <item>The app can be repointed at a different site database, leaving a token that is
///   syntactically valid but identifies nobody there.</item>
/// </list>
/// <para>In both cases <c>IAuthService.GetUserByAccessTokenAsync</c> returns <c>null</c> and the
/// shared provider dereferences it. This subclass resolves the token first, drops it when it no
/// longer identifies anyone, and only then delegates.</para>
/// <para><b>Code Flow:</b> Blazor asks for the authentication state → connection check → token
/// check → user lookup → delegate to the shared implementation or report anonymous.</para>
/// <para><b>Dependencies:</b> <see cref="ConnectionContext"/>, <see cref="ILocalStorageService"/>,
/// <see cref="IAuthService"/>.</para>
/// <para><b>Usage:</b> Registered in <c>MauiProgram</c> as the scoped
/// <see cref="AuthenticationStateProvider"/>; the shared screens never know the difference.</para>
/// </remarks>
public class DesktopAuthStateProvider : CustomAuthStateProvider
{
    private readonly ConnectionContext connectionContext;

    /// <summary>
    /// Creates the desktop provider.
    /// </summary>
    /// <param name="localStorageSvc">Token storage inside the WebView.</param>
    /// <param name="authSvc">Authentication service resolving tokens against the site database.</param>
    /// <param name="connectionContext">The connection the process booted with.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionContext"/> is <c>null</c>.</exception>
    public DesktopAuthStateProvider(
        ILocalStorageService localStorageSvc,
        IAuthService authSvc,
        ConnectionContext connectionContext)
        : base(localStorageSvc, authSvc)
    {
        this.connectionContext = connectionContext ?? throw new ArgumentNullException(nameof(connectionContext));
    }

    /// <summary>
    /// Resolves the current user, treating an unusable token as "signed out".
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An unconfigured head has nobody signed in by definition, and no
    /// server to ask. A configured head resolves the token once: if it no longer identifies a user
    /// the token is deleted, so the operator lands on the sign-in screen instead of a repeating
    /// error. Only a token that resolves cleanly is handed to the shared implementation, which then
    /// builds exactly the same claims the website builds.</para>
    /// <para><b>Flow:</b> connection check → read token → resolve user → discard or delegate.</para>
    /// <para><b>Side Effects:</b> May remove the access and refresh tokens from local storage.</para>
    /// </remarks>
    /// <returns>The authentication state for the current circuit.</returns>
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!connectionContext.IsConfigured)
        {
            return Anonymous();
        }

        try
        {
            var accessToken = await LocalStorageSvc.GetItemAsync<string>(AppConstants.AccessKey);
            if (string.IsNullOrEmpty(accessToken))
            {
                return Anonymous();
            }

            // REQ-FN-008: resolve through the shared helper, not through IAuthService directly, so
            // an expired access token is renewed here too. Calling the service straight would make
            // this head delete a token the website would have refreshed.
            var user = await ResolveSessionUserAsync(accessToken);
            if (user == null || string.IsNullOrEmpty(user.EmailId))
            {
                await ForgetTokensAsync();
                return Anonymous();
            }
        }
        catch (InvalidOperationException)
        {
            // JavaScript interop is not available yet; report anonymous exactly as the shared
            // implementation does and let the next render settle the state.
            return Anonymous();
        }

        return await base.GetAuthenticationStateAsync();
    }

    /// <summary>
    /// Removes the stored access and refresh tokens.
    /// </summary>
    /// <remarks>
    /// Called when a token survives a change of database and no longer identifies anyone, so the
    /// stale value cannot keep re-triggering the lookup on every render.
    /// </remarks>
    /// <returns>A task that completes when both tokens have been removed.</returns>
    private async Task ForgetTokensAsync()
    {
        await LocalStorageSvc.RemoveItemAsync(AppConstants.AccessKey);
        await LocalStorageSvc.RemoveItemAsync(AppConstants.RefreshKey);
    }

    /// <summary>
    /// Builds the signed-out authentication state.
    /// </summary>
    /// <returns>An authentication state carrying an unauthenticated principal.</returns>
    private static AuthenticationState Anonymous()
    {
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
    }
}
