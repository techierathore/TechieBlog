using BlogModels.Interfaces;
using BlogModels.Models;
using BlogUI;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace TechieBlog.Authentication;

/// <summary>
/// Authenticates an HTTP request from the session cookie written by the browser (REQ-FN-058).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Makes the product's session visible to the SERVER. Before this handler the
/// <c>BlazorServerAuth</c> scheme was a plain cookie scheme that nothing ever issued a cookie for,
/// so <c>HttpContext.User</c> was anonymous on every single request. Because
/// <c>MapRazorComponents</c> promotes a routable component's <c>[Authorize]</c> attribute to
/// endpoint metadata, the authorization middleware challenged every FULL DOCUMENT LOAD of an admin
/// route — a 302 to <c>/login</c> — no matter how valid the visitor's session was. The login page
/// then hydrated, saw an authenticated principal from local storage, and forwarded to the public
/// home page. That three-step chain is the "deep link bounces to the home page" defect; only
/// client-side navigation, which issues no document request at all, ever reached an admin page.</para>
///
/// <para><b>Code Flow:</b> read the cookie named
/// <see cref="CustomAuthStateProvider.SessionCookieName"/> → resolve it through
/// <see cref="IAuthService"/>, the same lookup the circuit performs → build the principal through
/// <see cref="CustomAuthStateProvider.BuildIdentity"/> so the HTTP request and the circuit agree
/// claim for claim.</para>
///
/// <para><b>Security:</b> the cookie is NOT trusted on its face. Its value is an access token that
/// is looked up in the <c>UserLogin</c> table on every request, so a revoked, replaced or expired
/// session is refused here exactly as it is refused on the circuit. The cookie cannot be HttpOnly
/// because script writes it, but it carries the same token already held in local storage, so it
/// widens no exposure; <c>SameSite=Lax</c> keeps it off cross-site requests.</para>
///
/// <para><b>Dependencies:</b> <see cref="IAuthService"/> (registered transient by the host) and the
/// <c>NotFoundPage.IsInfrastructurePath</c> exclusion list, reused so that framework assets, the
/// SignalR circuit and health probes do not each cost a database lookup.</para>
///
/// <para><b>Usage:</b> Registered in <c>Program.cs</c> as the default authenticate and challenge
/// scheme under the name <see cref="SchemeName"/>.</para>
/// </remarks>
public sealed class SessionCookieAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>
    /// Name of the authentication scheme this handler serves.
    /// </summary>
    /// <remarks>
    /// Kept as the historical <c>BlazorServerAuth</c> so no other registration has to change.
    /// </remarks>
    public const string SchemeName = "BlazorServerAuth";

    /// <summary>
    /// Route a challenged (unauthenticated) request is sent to.
    /// </summary>
    public const string LoginPath = "/login";

    /// <summary>
    /// Route a forbidden (authenticated but unauthorised) request is sent to.
    /// </summary>
    public const string AccessDeniedPath = "/access-denied";

    /// <summary>
    /// Query-string key carrying the originally requested URL.
    /// </summary>
    /// <remarks>
    /// Spelled exactly as <c>LoginPage.ReadReturnUrl</c> expects. The framework's cookie handler used
    /// <c>ReturnUrl</c>, which that page matches case-insensitively, but writing the same casing
    /// keeps the two obviously paired.
    /// </remarks>
    public const string ReturnUrlKey = "returnUrl";

    private readonly IAuthService authService;

    /// <summary>
    /// Creates the handler.
    /// </summary>
    /// <param name="options">Scheme options monitor supplied by the authentication stack.</param>
    /// <param name="loggerFactory">Logger factory supplied by the authentication stack.</param>
    /// <param name="encoder">URL encoder supplied by the authentication stack.</param>
    /// <param name="authService">Service resolving an access token to its owner.</param>
    public SessionCookieAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory loggerFactory,
        UrlEncoder encoder,
        IAuthService authService)
        : base(options, loggerFactory, encoder)
    {
        this.authService = authService;
    }

    /// <summary>
    /// Resolves the request's principal from the session cookie.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> No cookie, an unrecognised token or a lookup failure all produce
    /// <see cref="AuthenticateResult.NoResult"/> rather than a failure, so the request simply
    /// continues as anonymous and the endpoint's own authorization decides what happens next. A
    /// database outage must not turn every page into a 500.</para>
    /// <para><b>Flow:</b> skip infrastructure paths → read cookie → resolve user → build ticket.</para>
    /// <para><b>Side Effects:</b> One indexed session lookup per request that carries a cookie.</para>
    /// </remarks>
    /// <returns>A success ticket for a live session, otherwise no result.</returns>
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Framework assets, the SignalR circuit, health probes and uploaded media never read
        // HttpContext.User, so authenticating them would only buy a database round trip each.
        if (NotFoundPage.IsInfrastructurePath(Request.Path))
        {
            return AuthenticateResult.NoResult();
        }

        var accessToken = Request.Cookies[CustomAuthStateProvider.SessionCookieName];
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return AuthenticateResult.NoResult();
        }

        AppUser? user;
        try
        {
            user = await authService.GetUserByAccessTokenAsync(accessToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Session cookie could not be resolved; continuing anonymously.");
            return AuthenticateResult.NoResult();
        }

        if (user?.EmailId == null)
        {
            return AuthenticateResult.NoResult();
        }

        var principal = new ClaimsPrincipal(CustomAuthStateProvider.BuildIdentity(user));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    /// <summary>
    /// Sends an unauthenticated request to the sign-in page.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Mirrors what the retired cookie handler did, and what
    /// <c>RedirectToLogin</c> does on the client, so an anonymous deep link behaves identically
    /// whichever layer catches it. The requested path is carried as a site-relative
    /// <see cref="ReturnUrlKey"/> value, which the login page's open-redirect guard accepts.</para>
    /// <para><b>Flow:</b> build the return URL → 302.</para>
    /// <para><b>Side Effects:</b> Writes a redirect response.</para>
    /// </remarks>
    /// <param name="properties">Challenge properties supplied by the authorization middleware.</param>
    /// <returns>A completed task.</returns>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var returnUrl = Uri.EscapeDataString(Request.Path + Request.QueryString);
        Response.Redirect($"{LoginPath}?{ReturnUrlKey}={returnUrl}");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends an authenticated but unauthorised request to the access-denied page.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The same destination the router's <c>NotAuthorized</c> fragment
    /// navigates to, so a role that cannot open a page sees one explanation rather than two
    /// different ones depending on how it arrived.</para>
    /// <para><b>Flow:</b> 302 to <see cref="AccessDeniedPath"/>.</para>
    /// <para><b>Side Effects:</b> Writes a redirect response.</para>
    /// </remarks>
    /// <param name="properties">Forbid properties supplied by the authorization middleware.</param>
    /// <returns>A completed task.</returns>
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect(AccessDeniedPath);
        return Task.CompletedTask;
    }
}
