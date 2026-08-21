using BlogModels;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Unit tests for the role/policy hierarchy in <see cref="AppPolicies"/> (REQ-FN-009, BRD-7/8).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>Program.cs</c> builds every role-based policy from
/// <see cref="AppPolicies.PolicyRoleMap"/>, so asserting the map is equivalent to asserting the
/// registered policies — without booting the host. The Contributor cases are the point of the
/// exercise: before REQ-FN-009 the Contributor role granted nothing beyond Reader.</para>
/// <para><b>Dependencies:</b> xUnit; no database or host required.</para>
/// </remarks>
public class AppPoliciesTests
{
    /// <summary>
    /// Four role-based policies are mapped; the fifth policy, Authenticated, is deliberately
    /// absent because it requires a principal rather than a role.
    /// </summary>
    [Fact]
    public void MapCoversEveryRoleBasedPolicy()
    {
        Assert.Equal(4, AppPolicies.PolicyRoleMap.Count);
        Assert.Contains(AppPolicies.AdminOnly, AppPolicies.PolicyRoleMap.Keys);
        Assert.Contains(AppPolicies.EditorOrAbove, AppPolicies.PolicyRoleMap.Keys);
        Assert.Contains(AppPolicies.AuthorOrAbove, AppPolicies.PolicyRoleMap.Keys);
        Assert.Contains(AppPolicies.ContributorOrAbove, AppPolicies.PolicyRoleMap.Keys);
        Assert.DoesNotContain(AppPolicies.Authenticated, AppPolicies.PolicyRoleMap.Keys);
    }

    /// <summary>
    /// Admin satisfies every role-based policy, so an administrator never needs a second role.
    /// </summary>
    [Fact]
    public void AdminSatisfiesEveryPolicy()
    {
        foreach (var policyName in AppPolicies.PolicyRoleMap.Keys)
        {
            Assert.True(AppPolicies.IsSatisfiedBy(policyName, AppRoles.Admin), policyName);
        }
    }

    /// <summary>
    /// Contributor satisfies ContributorOrAbove — the grant that makes the role mean something
    /// beyond Reader — but is refused by every policy above it.
    /// </summary>
    [Fact]
    public void ContributorIsGrantedOnlyContributorOrAbove()
    {
        Assert.True(AppPolicies.IsSatisfiedBy(AppPolicies.ContributorOrAbove, AppRoles.Contributor));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.AuthorOrAbove, AppRoles.Contributor));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.EditorOrAbove, AppRoles.Contributor));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.AdminOnly, AppRoles.Contributor));
    }

    /// <summary>
    /// Author satisfies AuthorOrAbove and ContributorOrAbove but neither of the two policies
    /// above it, so the hierarchy is genuinely ordered.
    /// </summary>
    [Fact]
    public void AuthorIsGrantedAuthorAndContributorPolicies()
    {
        Assert.True(AppPolicies.IsSatisfiedBy(AppPolicies.AuthorOrAbove, AppRoles.Author));
        Assert.True(AppPolicies.IsSatisfiedBy(AppPolicies.ContributorOrAbove, AppRoles.Author));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.EditorOrAbove, AppRoles.Author));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.AdminOnly, AppRoles.Author));
    }

    /// <summary>
    /// Editor satisfies everything except AdminOnly.
    /// </summary>
    [Fact]
    public void EditorIsRefusedOnlyAdminOnly()
    {
        Assert.True(AppPolicies.IsSatisfiedBy(AppPolicies.EditorOrAbove, AppRoles.Editor));
        Assert.True(AppPolicies.IsSatisfiedBy(AppPolicies.AuthorOrAbove, AppRoles.Editor));
        Assert.True(AppPolicies.IsSatisfiedBy(AppPolicies.ContributorOrAbove, AppRoles.Editor));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.AdminOnly, AppRoles.Editor));
    }

    /// <summary>
    /// Reader — the default role — satisfies no role-based policy, so read-only users cannot
    /// reach any administrative surface.
    /// </summary>
    [Fact]
    public void ReaderSatisfiesNoRoleBasedPolicy()
    {
        foreach (var policyName in AppPolicies.PolicyRoleMap.Keys)
        {
            Assert.False(AppPolicies.IsSatisfiedBy(policyName, AppRoles.Reader), policyName);
        }
    }

    /// <summary>
    /// An unknown policy name, an unknown role and blank input all deny rather than falling
    /// through to an accidental grant.
    /// </summary>
    [Fact]
    public void UnknownInputsDenyByDefault()
    {
        Assert.False(AppPolicies.IsSatisfiedBy("NoSuchPolicy", AppRoles.Admin));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.AdminOnly, "NoSuchRole"));
        Assert.False(AppPolicies.IsSatisfiedBy(null!, AppRoles.Admin));
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.AdminOnly, "  "));
    }

    /// <summary>
    /// Role matching is ordinal, so a differently-cased role claim is not silently accepted.
    /// </summary>
    [Fact]
    public void RoleMatchingIsCaseSensitive()
    {
        Assert.False(AppPolicies.IsSatisfiedBy(AppPolicies.AdminOnly, "admin"));
    }

    /// <summary>
    /// ContributorOrAbove maps to exactly the four content-creating roles named by
    /// <see cref="AppRoles.ContentCreators"/>, keeping the constant and the policy in step.
    /// </summary>
    [Fact]
    public void ContributorPolicyMatchesContentCreators()
    {
        Assert.Equal(
            AppRoles.ContentCreators,
            AppPolicies.PolicyRoleMap[AppPolicies.ContributorOrAbove]);
    }
}
