namespace BlogModels.Models;

/// <summary>
/// A role definition as a database row, for an administrative role-management screen.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Would let roles be created and described at runtime instead of being fixed
/// in code.</para>
///
/// <para><b>Code Flow:</b> Unreferenced. Nothing in <c>source/</c> or <c>tests/</c> constructs,
/// queries or renders this type, and no migration creates a matching table. Do not confuse it with
/// <c>AppUser.UserRole</c>, which is a <see cref="string"/> property and is what the application
/// actually authorises against.</para>
///
/// <para><b>Dependencies:</b> None that exist.</para>
///
/// <para><b>Usage:</b> Authorisation in this application is driven by the compile-time constants in
/// <see cref="AppRoles"/> and the policy map in <see cref="AppPolicies.PolicyRoleMap"/>. Adding rows
/// through this type would not grant anything, because no policy consults a table. A deletion
/// candidate unless dynamic roles are actually built.</para>
///
/// <para><b>How authorisation actually works — the role → policy relationship.</b> A user carries
/// exactly one role name, held as the <c>UserRole</c> string on <see cref="AppUser"/> and copied
/// into the principal's claims at sign-in. Five policies are registered in <c>Program.cs</c>, all
/// built from <see cref="AppPolicies.PolicyRoleMap"/>, and every access decision in the application
/// resolves to one of them:</para>
/// <list type="table">
///   <listheader><term>Policy</term><description>Roles that satisfy it</description></listheader>
///   <item>
///     <term><see cref="AppPolicies.AdminOnly"/></term>
///     <description>Admin. Users, settings, and every content type.</description>
///   </item>
///   <item>
///     <term><see cref="AppPolicies.EditorOrAbove"/></term>
///     <description>Admin, Editor. All posts and comment moderation.</description>
///   </item>
///   <item>
///     <term><see cref="AppPolicies.AuthorOrAbove"/></term>
///     <description>Admin, Editor, Author. Creating and editing one's own posts.</description>
///   </item>
///   <item>
///     <term><see cref="AppPolicies.ContributorOrAbove"/></term>
///     <description>Admin, Editor, Author, Contributor. Registered and unit-tested, but
///     deliberately attached to no page — the submit-for-review screen it exists for has not been
///     built (REQ-FN-009).</description>
///   </item>
///   <item>
///     <term><see cref="AppPolicies.Authenticated"/></term>
///     <description>Any signed-in principal. The only policy absent from the map, because it tests
///     authentication rather than a role.</description>
///   </item>
/// </list>
/// <para>The hierarchy Admin &gt; Editor &gt; Author &gt; Contributor &gt; Reader is <i>not</i>
/// implied by the role names; it exists only because each policy enumerates every role above it.
/// That is why <c>[Authorize(Policy = …)]</c> is correct and <c>[Authorize(Roles = …)]</c> is a
/// trap — naming a single role silently locks out everyone senior to it. Reader appears in no
/// policy at all: it is the default role for a new account and grants nothing beyond
/// <see cref="AppPolicies.Authenticated"/>. Role names are compared with
/// <c>StringComparer.Ordinal</c>, so a stored value differing in case authorises nothing, and an
/// unrecognised role denies by default rather than falling back to a lesser one.</para>
/// </remarks>
public class UserRole
{
    /// <summary>
    /// Surrogate key of the role definition. Unused; see the remarks on the type.
    /// </summary>
    public int RoleId { get; set; }

    /// <summary>
    /// The role name. To have any effect it would have to match one of the
    /// <see cref="AppRoles"/> constants exactly, since those are what policies compare against.
    /// </summary>
    public string RoleName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of what the role permits, for display on an administration
    /// screen. Carries no authorisation meaning.
    /// </summary>
    public string RoleDesc { get; set; } = string.Empty;
}
