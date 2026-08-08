using BlogModels;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Unit tests for the post-login role → landing-route mapping in
/// <see cref="RoleLandingRoutes"/> (REQ-UI-001, BRD-2).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> the login page used to send every role to <c>/admin</c>, a route guarded
/// by <see cref="AppPolicies.EditorOrAbove"/>, so an Author or Contributor signed in and was
/// bounced straight to <c>/access-denied</c>. These tests pin the corrected mapping and — more
/// importantly — prove the mapping against <see cref="AppPolicies"/>, so a future landing change
/// that lands a role somewhere it cannot open fails here rather than in a browser.</para>
/// <para><b>Dependencies:</b> xUnit; no database, host or rendered component required.</para>
/// </remarks>
public class RoleLandingRoutesTests
{
    /// <summary>
    /// Every role in the model has an explicit landing route, so no role falls through to the
    /// unknown-role fallback by accident.
    /// </summary>
    [Fact]
    public void MapCoversEveryRole()
    {
        Assert.Equal(5, RoleLandingRoutes.RoleRouteMap.Count);
        Assert.Contains(AppRoles.Admin, RoleLandingRoutes.RoleRouteMap.Keys);
        Assert.Contains(AppRoles.Editor, RoleLandingRoutes.RoleRouteMap.Keys);
        Assert.Contains(AppRoles.Author, RoleLandingRoutes.RoleRouteMap.Keys);
        Assert.Contains(AppRoles.Contributor, RoleLandingRoutes.RoleRouteMap.Keys);
        Assert.Contains(AppRoles.Reader, RoleLandingRoutes.RoleRouteMap.Keys);
    }

    /// <summary>
    /// Admin lands on the admin dashboard, the landing documented in the Usage Guide.
    /// </summary>
    [Fact]
    public void AdminLandsOnDashboard()
    {
        Assert.Equal(RoleLandingRoutes.AdminDashboard, RoleLandingRoutes.ResolveFor(AppRoles.Admin));
    }

    /// <summary>
    /// Editor lands on the admin dashboard — the Usage Guide records "lands on /admin after
    /// sign-in" for the seeded editor account.
    /// </summary>
    [Fact]
    public void EditorLandsOnDashboard()
    {
        Assert.Equal(RoleLandingRoutes.AdminDashboard, RoleLandingRoutes.ResolveFor(AppRoles.Editor));
    }

    /// <summary>
    /// Author lands on the post list rather than the dashboard, because the dashboard is
    /// EditorOrAbove and the post list is AuthorOrAbove with author-scoped rows.
    /// </summary>
    [Fact]
    public void AuthorLandsOnPostList()
    {
        Assert.Equal(RoleLandingRoutes.PostList, RoleLandingRoutes.ResolveFor(AppRoles.Author));
        Assert.NotEqual(RoleLandingRoutes.AdminDashboard, RoleLandingRoutes.ResolveFor(AppRoles.Author));
    }

    /// <summary>
    /// Contributor lands on the public home page: the BRD role table gives the role no dedicated
    /// screen, so any admin destination would be an access-denied bounce.
    /// </summary>
    [Fact]
    public void ContributorLandsOnPublicHome()
    {
        Assert.Equal(RoleLandingRoutes.PublicHome, RoleLandingRoutes.ResolveFor(AppRoles.Contributor));
    }

    /// <summary>
    /// Reader lands on the public home page — it has no staff surface at all.
    /// </summary>
    [Fact]
    public void ReaderLandsOnPublicHome()
    {
        Assert.Equal(RoleLandingRoutes.PublicHome, RoleLandingRoutes.ResolveFor(AppRoles.Reader));
    }

    /// <summary>
    /// The regression this mapping exists to prevent: for every role, the landing route's guarding
    /// policy is one the role actually satisfies, so no sign-in can end on /access-denied.
    /// </summary>
    [Fact]
    public void EveryRoleLandsSomewhereItIsAuthorised()
    {
        foreach (var pair in RoleLandingRoutes.RoleRouteMap)
        {
            if (!RoleLandingRoutes.RoutePolicyMap.TryGetValue(pair.Value, out var policyName))
            {
                // An unguarded (anonymous) destination such as the public home page.
                continue;
            }

            Assert.True(
                AppPolicies.IsSatisfiedBy(policyName, pair.Key),
                $"{pair.Key} lands on {pair.Value}, which requires {policyName}.");
        }
    }

    /// <summary>
    /// Both guarded landing routes name the policy their page actually declares, keeping the
    /// authorisation proof above honest.
    /// </summary>
    [Fact]
    public void GuardedRoutesNameTheirPagePolicy()
    {
        Assert.Equal(AppPolicies.EditorOrAbove, RoleLandingRoutes.RoutePolicyMap[RoleLandingRoutes.AdminDashboard]);
        Assert.Equal(AppPolicies.AuthorOrAbove, RoleLandingRoutes.RoutePolicyMap[RoleLandingRoutes.PostList]);
        Assert.DoesNotContain(RoleLandingRoutes.PublicHome, RoleLandingRoutes.RoutePolicyMap.Keys);
    }

    /// <summary>
    /// An unknown, blank or null role falls back to the public home page rather than to a guarded
    /// route, so a corrupt role claim degrades safely instead of bouncing.
    /// </summary>
    [Fact]
    public void UnknownRoleFallsBackToPublicHome()
    {
        Assert.Equal(RoleLandingRoutes.PublicHome, RoleLandingRoutes.ResolveFor("NoSuchRole"));
        Assert.Equal(RoleLandingRoutes.PublicHome, RoleLandingRoutes.ResolveFor(string.Empty));
        Assert.Equal(RoleLandingRoutes.PublicHome, RoleLandingRoutes.ResolveFor("   "));
        Assert.Equal(RoleLandingRoutes.PublicHome, RoleLandingRoutes.ResolveFor(null!));
    }

    /// <summary>
    /// Role matching is ordinal, matching how the authorization policies compare role claims, so a
    /// differently-cased role is not silently given a staff landing.
    /// </summary>
    [Fact]
    public void RoleMatchingIsCaseSensitive()
    {
        Assert.Equal(RoleLandingRoutes.PublicHome, RoleLandingRoutes.ResolveFor("admin"));
    }

    /// <summary>
    /// Every landing route is site-relative, which is what NavigationManager expects and what keeps
    /// the redirect immune to open-redirect abuse.
    /// </summary>
    [Fact]
    public void EveryLandingRouteIsSiteRelative()
    {
        foreach (var route in RoleLandingRoutes.RoleRouteMap.Values)
        {
            Assert.StartsWith("/", route);
            Assert.False(route.StartsWith("//", StringComparison.Ordinal), route);
        }
    }
}
