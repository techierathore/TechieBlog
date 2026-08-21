namespace BlogModels;

/// <summary>
/// Maps a signed-in user's role onto the route they land on after a successful sign-in
/// (REQ-UI-001, REQ-FN-009, BRD-2/BRD-7/BRD-8).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> the login page used to send EVERY role to <c>/admin</c>, which is guarded
/// by <see cref="AppPolicies.EditorOrAbove"/>. An Author or Contributor therefore authenticated
/// successfully and was immediately bounced to <c>/access-denied</c>. This class is the single
/// definition of "where does this role go", so the rule can be unit-tested without booting the
/// host and the login page carries no policy knowledge of its own.</para>
/// <para><b>Code Flow:</b> <c>LoginPage.ValidateUser</c> authenticates, then calls
/// <see cref="ResolveFor"/> (unless a site-relative <c>returnUrl</c> was supplied) and navigates
/// to the result. One interstitial can pre-empt that landing: an account flagged
/// <c>MustChangePassword</c> is redirected to <see cref="ChangePassword"/> by
/// <c>ForcePasswordChangeGuard</c> immediately after arriving — see the remarks on that constant,
/// because the redirect happens outside this class rather than inside
/// <see cref="ResolveFor"/>.</para>
/// <para><b>Dependencies:</b> <see cref="AppRoles"/> for the role constants and
/// <see cref="AppPolicies"/> for the policy that guards each destination.</para>
/// <para><b>Usage:</b> <c>NavigationManager.NavigateTo(RoleLandingRoutes.ResolveFor(user.UserRole));</c></para>
/// </remarks>
public static class RoleLandingRoutes
{
    /// <summary>
    /// Admin dashboard. Guarded by <see cref="AppPolicies.EditorOrAbove"/> (BRD §F-ADMIN).
    /// </summary>
    public const string AdminDashboard = "/admin";

    /// <summary>
    /// Post list — "All Posts" for Editor and Admin, "My Posts" for an Author because the service
    /// scopes the rows. Guarded by <see cref="AppPolicies.AuthorOrAbove"/> (REQ-UI-017, BRD-14).
    /// </summary>
    public const string PostList = "/BlogsList";

    /// <summary>
    /// Public home page — the landing for roles that have no staff surface at all.
    /// </summary>
    public const string PublicHome = "/";

    /// <summary>
    /// Forced password change (REQ-NFR-023, BRD-79). Effectively overrides every role's landing route
    /// while the account carries <c>MustChangePassword</c>.
    /// </summary>
    /// <remarks>
    /// <para>A seeded or admin-created account starts life with a password its owner did not choose
    /// and that is written down in the setup documentation. Until it is replaced the account is a
    /// shared secret, so a flagged user is not allowed to reach any other authenticated page.</para>
    /// <para><b>How the override actually works:</b> not inside <see cref="ResolveFor"/>, which is
    /// deliberately unaware of the flag and still returns the role's normal landing route. Enforcement
    /// lives in <c>BlogUI.Components.ForcePasswordChangeGuard</c>, which sits inside
    /// <c>CascadingAuthenticationState</c> in <c>Routes.razor</c>, renders nothing, and re-checks the
    /// <c>MustChangePassword</c> claim on every navigation and every authentication-state change. The
    /// observable sequence is therefore: login resolves the role landing → navigates there → the guard
    /// fires and redirects here. Keeping the two apart is what lets <see cref="ResolveFor"/> stay a
    /// pure, host-free function that the unit tests can exercise directly.</para>
    /// <para>The guard runs only on the interactive render, because the principal is rehydrated from
    /// browser local storage through JS interop and the prerender pass always sees an anonymous user.
    /// It never redirects the change screen to itself, and it leaves anonymous visitors alone.</para>
    /// </remarks>
    public const string ChangePassword = "/change-password";

    /// <summary>
    /// The single source of truth for role → post-login landing route (REQ-UI-001).
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> every entry names a route the role is genuinely authorised to
    /// open, matching the landings documented in <c>docs/TechieBlog-UsageGuide.md</c>: Admin and
    /// Editor satisfy <see cref="AppPolicies.EditorOrAbove"/> and land on the dashboard; Author
    /// satisfies only <see cref="AppPolicies.AuthorOrAbove"/> and lands on the post list;
    /// Contributor and Reader have no staff surface and land on the public site.</para>
    /// <para><b>Note:</b> Contributor is deliberately mapped to <see cref="PublicHome"/> — the BRD
    /// role table records "(policy declared; no dedicated screens yet)" for that role, so sending
    /// it anywhere under <c>/admin</c> would be an access-denied bounce.</para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> RoleRouteMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AppRoles.Admin] = AdminDashboard,
            [AppRoles.Editor] = AdminDashboard,
            [AppRoles.Author] = PostList,
            [AppRoles.Contributor] = PublicHome,
            [AppRoles.Reader] = PublicHome
        };

    /// <summary>
    /// Maps the policy that guards each landing route, so the mapping can be proved authorised.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> mirrors the <c>[Authorize(Policy = …)]</c> attribute on the
    /// page behind each route. <see cref="PublicHome"/> is absent because it is anonymous.</para>
    /// <para><b>Usage:</b> the unit tests assert that every role's landing route is one the role
    /// satisfies, which is the regression that REQ-UI-001 fixes.</para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> RoutePolicyMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AdminDashboard] = AppPolicies.EditorOrAbove,
            [PostList] = AppPolicies.AuthorOrAbove
        };

    /// <summary>
    /// Resolves the post-login landing route for a role.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> looks the role up in <see cref="RoleRouteMap"/>. An unknown,
    /// blank or null role falls back to the public home page — never to a guarded route — so a
    /// bad role string can never produce an access-denied landing.</para>
    /// <para><b>Flow:</b> null/blank guard → ordinal map lookup → fallback.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="roleName">The signed-in user's role, one of the <see cref="AppRoles"/> constants.</param>
    /// <returns>A site-relative route the role is permitted to open.</returns>
    public static string ResolveFor(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return PublicHome;
        }

        return RoleRouteMap.TryGetValue(roleName, out var route) ? route : PublicHome;
    }
}
