using Blazored.LocalStorage;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.JSInterop;
using System.Security.Claims;

namespace BlogUI;

/// <summary>
/// Resolves the signed-in principal for every circuit and for the static prerender pass.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The product's session is a JWT issued by <c>AuthSvc</c>. The browser keeps
/// it in local storage, and this provider turns it back into a <see cref="ClaimsPrincipal"/> that
/// the router, <c>AuthorizeRouteView</c> and every <c>AuthorizeView</c> can read.</para>
///
/// <para><b>Code Flow:</b> Blazor asks for the state → read the access token from local storage →
/// resolve it through <see cref="IAuthService"/> (renewing it from the refresh token when it has
/// expired) → build the identity. When JS interop is not available — the static prerender pass —
/// the token is unreachable and the request cookie written by <see cref="SessionCookieName"/> is
/// used instead.</para>
///
/// <para><b>REQ-FN-058 — why a cookie exists at all.</b> Local storage is invisible to the server on
/// a plain document GET, so a FULL page load of an admin route used to reach the endpoint with an
/// anonymous principal even when the visitor held a perfectly valid session: the endpoint's
/// <c>[Authorize]</c> metadata challenged, the visitor was redirected to <c>/login</c>, and the login
/// page — seeing an authenticated user once its circuit hydrated — sent them on to the public home
/// page. Bookmarks, shared admin links and F5 were all broken; only client-side navigation, which
/// never leaves the circuit, worked. <see cref="MarkUserAsAuthenticated"/> and
/// <see cref="GetAuthenticationStateAsync"/> now mirror the same token into a request cookie, so the
/// server can authenticate the document request and the prerender pass renders the real page.</para>
///
/// <para><b>Dependencies:</b> <see cref="ILocalStorageService"/>, <see cref="IAuthService"/>, and —
/// optionally, because the desktop head supplies neither — <see cref="IJSRuntime"/> for the cookie
/// and <see cref="IHttpContextAccessor"/> for the prerender fallback.</para>
///
/// <para><b>Usage:</b> Registered scoped as the <see cref="AuthenticationStateProvider"/>. The
/// desktop head subclasses it (<c>DesktopAuthStateProvider</c>) and calls the two-argument
/// constructor, which is why the two additions above are optional parameters rather than a new
/// constructor.</para>
/// </remarks>
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
    /// Authentication type stamped on every identity this provider builds.
    /// </summary>
    /// <remarks>
    /// A <see cref="ClaimsIdentity"/> only reports <c>IsAuthenticated</c> when it carries an
    /// authentication type, so this value is load-bearing rather than decorative. The server-side
    /// handler reuses it through <see cref="BuildIdentity"/> so an HTTP request and a circuit
    /// produce byte-identical principals.
    /// </remarks>
    public const string AuthenticationTypeName = "apiauth_type";

    /// <summary>
    /// Lifetime of the session cookie, in seconds (seven days).
    /// </summary>
    /// <remarks>
    /// The cookie is not a credential in its own right — the token inside it is looked up in the
    /// <c>UserLogin</c> table on every use — so this value bounds how long a dead token keeps being
    /// presented, not how long a session lives. It is deliberately longer than the access-token
    /// lifetime so that a returning visitor's deep link still authenticates on the server.
    /// </remarks>
    public const int SessionCookieMaxAgeSeconds = 7 * 24 * 60 * 60;

    /// <summary>
    /// Path of the JS module that writes and clears the session cookie.
    /// </summary>
    private const string SessionCookieModulePath = "/_content/BlogUI/js/session-cookie.js";

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

    private readonly IJSRuntime? jsRuntime;
    private readonly IHttpContextAccessor? httpContextAccessor;
    private IJSObjectReference? sessionCookieModule;

    /// <summary>
    /// Browser local storage holding the access and refresh tokens.
    /// </summary>
    public ILocalStorageService LocalStorageSvc { get; }

    /// <summary>
    /// Authentication service used to resolve and renew tokens.
    /// </summary>
    public IAuthService AuthSvc { get; set; }

    /// <summary>
    /// Name of the cookie mirroring the access token (REQ-FN-058).
    /// </summary>
    /// <remarks>
    /// Deliberately the same string as <see cref="AppConstants.AccessKey"/>, which embeds
    /// <c>AppSecrets.SessionFingerprint</c>: rotating the JWT signing key renames the cookie, so a
    /// browser holding a token issued under the previous key cannot present it to the server any
    /// more — exactly the invalidation rule the local-storage keys already follow.
    /// </remarks>
    public static string SessionCookieName => AppConstants.AccessKey;

    /// <summary>
    /// Creates the provider.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The last two parameters are optional because the desktop head
    /// derives from this class and calls the two-argument form; a head with no HTTP pipeline simply
    /// gets no cookie mirroring and no prerender fallback, neither of which it needs.</para>
    /// <para><b>Side Effects:</b> None; the JS module is imported lazily on first use.</para>
    /// </remarks>
    /// <param name="localStorageSvc">Browser local storage holding the token pair.</param>
    /// <param name="authSvc">Authentication service resolving and renewing tokens.</param>
    /// <param name="jsRuntime">JS interop used to write the session cookie; omitted by the desktop head.</param>
    /// <param name="httpContextAccessor">Access to the request being prerendered, when there is one.</param>
    public CustomAuthStateProvider(
        ILocalStorageService localStorageSvc,
        IAuthService authSvc,
        IJSRuntime? jsRuntime = null,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        LocalStorageSvc = localStorageSvc;
        AuthSvc = authSvc;
        this.jsRuntime = jsRuntime;
        this.httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Builds the identity for a signed-in user, or an anonymous one when there is no user.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> One definition of "what claims a TechieBlog principal carries",
    /// shared by the circuit and by the server-side session-cookie handler (REQ-FN-058). Two
    /// definitions would drift, and a principal whose role claim differed between the HTTP request
    /// and the circuit would authorize a page at one layer and refuse it at the other.</para>
    /// <para><b>Flow:</b> no user, or a user with no email, yields an anonymous identity; otherwise
    /// the five claims below are stamped with <see cref="AuthenticationTypeName"/>.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="loggedUser">The resolved user, or <c>null</c> when the session is over.</param>
    /// <returns>An authenticated identity, or an anonymous one.</returns>
    public static ClaimsIdentity BuildIdentity(AppUser? loggedUser)
    {
        if (loggedUser?.EmailId == null)
        {
            return new ClaimsIdentity();
        }

        return new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.PrimarySid, Convert.ToString(loggedUser.UserId)),
                new Claim(ClaimTypes.Name, loggedUser.FullName),
                new Claim(ClaimTypes.Email, loggedUser.EmailId),
                new Claim(ClaimTypes.Role, loggedUser.UserRole),
                // REQ-NFR-023: carried on the principal so the router can force the change screen
                // without querying the database per navigation.
                new Claim(MustChangePasswordClaim, loggedUser.MustChangePassword ? "true" : "false")
            },
            AuthenticationTypeName);
    }

    /// <summary>
    /// Resolves the principal for the current render pass.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> On an interactive circuit the access token is read from local
    /// storage and resolved (renewing it when needed). On the STATIC PRERENDER pass local storage is
    /// unreachable, and returning anonymous there is what broke every deep link into a protected
    /// route (REQ-FN-058) — so that branch falls back to the principal the server already
    /// authenticated from the session cookie.</para>
    /// <para><b>Flow:</b> read token → resolve user → build identity → refresh the cookie; on an
    /// interop failure, read <c>HttpContext.User</c> instead.</para>
    /// <para><b>Side Effects:</b> May renew the session and rewrite both local-storage slots, and
    /// rewrites the session cookie whenever a live session is resolved — which is what lets a
    /// browser that signed in before the cookie existed heal itself on its next circuit.</para>
    /// </remarks>
    /// <returns>The authentication state for this render pass.</returns>
    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        ClaimsIdentity identity;

        try
        {
            var accessToken = await LocalStorageSvc.GetItemAsync<string>(AppConstants.AccessKey);

            if (!string.IsNullOrEmpty(accessToken))
            {
                AppUser? user = await ResolveSessionUserAsync(accessToken);
                identity = BuildIdentity(user);

                if (user != null)
                {
                    await SyncSessionCookieAsync();
                }
            }
            else
            {
                identity = new ClaimsIdentity();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or JSException)
        {
            // JS interop is not available: this is the static prerender pass. REQ-FN-058 — the
            // session cookie is the only place the token is visible to the server here, and
            // UseAuthentication has already turned it into HttpContext.User by the time the
            // component tree renders.
            identity = ReadPrerenderIdentity();
        }

        return new AuthenticationState(new ClaimsPrincipal(identity));
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

    /// <summary>
    /// Publishes a freshly signed-in user and persists the session.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Local storage keeps the pair for the circuit; the session cookie
    /// (REQ-FN-058) is what makes the very next FULL page load — a deep link, a bookmark, F5 — arrive
    /// at the server authenticated instead of being bounced to the sign-in page and on to the home
    /// page.</para>
    /// <para><b>Flow:</b> store both tokens → write the cookie → notify the router.</para>
    /// <para><b>Side Effects:</b> Writes two local-storage slots and one cookie; raises
    /// <c>AuthenticationStateChanged</c>.</para>
    /// </remarks>
    /// <param name="loggedUser">The user just authenticated.</param>
    /// <returns>A task that completes once the new state has been published.</returns>
    public async Task MarkUserAsAuthenticated(AppUser loggedUser)
    {
        await LocalStorageSvc.SetItemAsync(AppConstants.AccessKey, loggedUser.AccessToken);
        await LocalStorageSvc.SetItemAsync(AppConstants.RefreshKey, loggedUser.RefreshToken);
        await WriteSessionCookieAsync(loggedUser.AccessToken);

        var claimsPrincipal = new ClaimsPrincipal(BuildIdentity(loggedUser));
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
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

    /// <summary>
    /// Signs the user out of this browser.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Both local-storage slots AND the session cookie have to go. A
    /// surviving cookie would leave the SERVER believing the visitor is signed in on the next
    /// document request even though the circuit reports them anonymous (REQ-FN-058).</para>
    /// <para><b>Flow:</b> remove both tokens → clear the cookie → notify with an anonymous
    /// principal.</para>
    /// <para><b>Side Effects:</b> Clears browser state; raises <c>AuthenticationStateChanged</c>.</para>
    /// </remarks>
    /// <returns>A task that completes once the signed-out state has been published.</returns>
    public async Task MarkUserAsLoggedOut()
    {
        await LocalStorageSvc.RemoveItemAsync(AppConstants.RefreshKey);
        await LocalStorageSvc.RemoveItemAsync(AppConstants.AccessKey);
        await ClearSessionCookieAsync();

        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(anonymousUser)));
    }

    /// <summary>
    /// Reads the principal the server authenticated for the request being prerendered.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Copies <c>HttpContext.User</c> only when it is genuinely
    /// authenticated, so an anonymous request still prerenders anonymous. This runs ONLY from the
    /// interop-failure branch of <see cref="GetAuthenticationStateAsync"/>, which is the static
    /// prerender pass — on an interactive circuit there is no ambient request and this method is
    /// never reached.</para>
    /// <para><b>Flow:</b> read the accessor → check <c>IsAuthenticated</c> → reuse the identity.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <returns>The request's identity, or an anonymous one.</returns>
    private ClaimsIdentity ReadPrerenderIdentity()
    {
        var requestUser = httpContextAccessor?.HttpContext?.User;
        if (requestUser?.Identity?.IsAuthenticated != true)
        {
            return new ClaimsIdentity();
        }

        return new ClaimsIdentity(requestUser.Claims, AuthenticationTypeName);
    }

    /// <summary>
    /// Refreshes the session cookie from whatever token local storage currently holds.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Called on every circuit that resolves a live session, so a
    /// browser signed in before this mechanism existed — or one whose token was just rotated by a
    /// refresh — gets a correct cookie without having to sign in again.</para>
    /// <para><b>Flow:</b> read the stored token → write the cookie.</para>
    /// <para><b>Side Effects:</b> Writes one cookie. Never throws.</para>
    /// </remarks>
    /// <returns>A task that completes once the cookie has been written.</returns>
    private async Task SyncSessionCookieAsync()
    {
        var currentToken = await LocalStorageSvc.GetItemAsync<string>(AppConstants.AccessKey);
        await WriteSessionCookieAsync(currentToken);
    }

    /// <summary>
    /// Writes the session cookie through the shared JS module.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Cookie failures must never cost a user their session — the
    /// circuit works perfectly well without one, it is only the next document load that degrades —
    /// so every failure mode here is swallowed. The desktop head supplies no
    /// <see cref="IJSRuntime"/> at all and simply skips.</para>
    /// <para><b>Flow:</b> guard → import the module once → invoke <c>write</c>.</para>
    /// <para><b>Side Effects:</b> Writes one cookie in the browser.</para>
    /// </remarks>
    /// <param name="accessToken">The token to mirror.</param>
    /// <returns>A task that completes once the cookie has been written or the attempt abandoned.</returns>
    private async Task WriteSessionCookieAsync(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return;
        }

        await InvokeCookieModuleAsync("write", SessionCookieName, accessToken, SessionCookieMaxAgeSeconds);
    }

    /// <summary>
    /// Expires the session cookie.
    /// </summary>
    /// <returns>A task that completes once the cookie has been cleared or the attempt abandoned.</returns>
    private Task ClearSessionCookieAsync()
    {
        return InvokeCookieModuleAsync("clear", SessionCookieName);
    }

    /// <summary>
    /// Calls a function on the session-cookie JS module, importing it on first use.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Everything about the cookie is best-effort. Prerendering, a
    /// disconnected circuit and a head that serves no static assets all fail here, and in every one
    /// of those cases the correct behaviour is to carry on silently.</para>
    /// <para><b>Flow:</b> skip when there is no JS runtime → import once → invoke → swallow.</para>
    /// <para><b>Side Effects:</b> Caches the imported module reference on this scoped instance.</para>
    /// </remarks>
    /// <param name="functionName">Exported function to call.</param>
    /// <param name="arguments">Arguments for the function.</param>
    /// <returns>A task that completes when the call has been made or abandoned.</returns>
    private async Task InvokeCookieModuleAsync(string functionName, params object[] arguments)
    {
        if (jsRuntime == null)
        {
            return;
        }

        try
        {
            sessionCookieModule ??= await jsRuntime.InvokeAsync<IJSObjectReference>(
                "import", SessionCookieModulePath);
            await sessionCookieModule.InvokeVoidAsync(functionName, arguments);
        }
        catch (Exception ex) when (ex is JSException
                                      or InvalidOperationException
                                      or ObjectDisposedException
                                      or TaskCanceledException)
        {
            // Best effort by design — see the remarks above.
        }
    }
}
