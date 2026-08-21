namespace BlogModels;

/// <summary>
/// Application role constants matching database role values.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The role names are stored as text in <c>BlogUser.UserRole</c> and travel
/// into the principal as a role claim, so the same literal has to be spelled identically in the
/// database, the seed migration, the authorization policies and every page attribute. Holding them
/// as constants turns a typo — which would otherwise fail silently as "this user is not in that
/// role" — into a compile error.</para>
///
/// <para><b>Code Flow:</b> purely declarative. The values reach an authorization decision through
/// <c>CustomAuthStateProvider</c>, which puts the stored role onto the principal as a
/// <c>ClaimTypes.Role</c> claim, and through <see cref="AppPolicies.PolicyRoleMap"/>, from which
/// <c>Program.cs</c> builds the policies.</para>
///
/// <para><b>Dependencies:</b> None — this is the bottom of the graph, and
/// <see cref="AppPolicies"/> depends on it rather than the other way round.</para>
///
/// <para><b>Role Hierarchy:</b> Admin &gt; Editor &gt; Author &gt; Contributor &gt; Reader. The
/// hierarchy is not implied by the constants themselves — it is expressed only by
/// <see cref="AppPolicies.PolicyRoleMap"/>, which lists every role that satisfies each policy.
/// Nothing enforces the ordering, so adding a role means adding it to that map as well; a role that
/// exists here and nowhere else satisfies no policy at all.</para>
/// <para><b>Usage:</b> Apply a role directly with <c>[Authorize(Roles = AppRoles.Admin)]</c>, or a
/// policy with <c>[Authorize(Policy = AppPolicies.AdminOnly)]</c>. Prefer the policy: a role
/// attribute names one role and silently excludes everyone above it in the hierarchy.</para>
/// <para><b>Permission Levels:</b></para>
/// <list type="bullet">
///   <item><b>Admin:</b> Full system access - users, settings, all content, tags, categories</item>
///   <item><b>Editor:</b> Manage all posts, comments, moderate content</item>
///   <item><b>Author:</b> Create/edit own posts, manage own drafts</item>
///   <item><b>Contributor:</b> Submit drafts for review</item>
///   <item><b>Reader:</b> View content, comment, rate (default role)</item>
/// </list>
/// </remarks>
public static class AppRoles
{
    /// <summary>
    /// Full system access - users, settings, all content, tags, categories.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Manage all posts, comments, moderate content.
    /// </summary>
    public const string Editor = "Editor";

    /// <summary>
    /// Create/edit own posts, manage own drafts.
    /// </summary>
    public const string Author = "Author";

    /// <summary>
    /// Submit drafts for review.
    /// </summary>
    public const string Contributor = "Contributor";

    /// <summary>
    /// View content, comment, rate. Default role for new users.
    /// </summary>
    public const string Reader = "Reader";

    /// <summary>
    /// All roles that have content management capabilities: Admin, Editor and Author.
    /// </summary>
    /// <remarks>
    /// <b>Currently unreferenced</b> — the equivalent role list is spelled out inline in
    /// <see cref="AppPolicies.PolicyRoleMap"/> under <see cref="AppPolicies.AuthorOrAbove"/>. The two
    /// therefore have to be kept in step by hand; if you edit one, edit the other. Note also that the
    /// array is mutable (<c>static readonly</c> protects the reference, not the elements), so any
    /// caller could reorder or overwrite its contents process-wide — treat it as read-only.
    /// </remarks>
    public static readonly string[] ContentManagers = { Admin, Editor, Author };

    /// <summary>
    /// All roles that can create any content: Admin, Editor, Author and Contributor.
    /// </summary>
    /// <remarks>
    /// Referenced by <see cref="AppPolicies.PolicyRoleMap"/> as the role list for
    /// <see cref="AppPolicies.ContributorOrAbove"/>, and asserted against it by the unit tests, so
    /// this constant and that policy cannot drift. Carries the same mutable-array caveat as
    /// <see cref="ContentManagers"/>.
    /// </remarks>
    public static readonly string[] ContentCreators = { Admin, Editor, Author, Contributor };
}

/// <summary>
/// Authorization policy names, and the single definition of which roles satisfy each one.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A policy expresses "Editor or anything above it" in one place, so a page can
/// be guarded without listing roles. That matters because <c>[Authorize(Roles = …)]</c> names exactly
/// the roles given and silently excludes everyone senior to them — the classic bug where an Admin is
/// denied a page marked for Editors.</para>
///
/// <para><b>Code Flow:</b> <c>Program.cs</c> calls <c>AddAuthorizationCore</c> and builds every policy
/// by iterating <see cref="PolicyRoleMap"/>; a page declares
/// <c>[Authorize(Policy = AppPolicies.EditorOrAbove)]</c>; at request time the framework matches the
/// principal's role claim against the roles the map recorded.</para>
///
/// <para><b>Dependencies:</b> <see cref="AppRoles"/> for the role constants.</para>
///
/// <para><b>Usage:</b> Guard with a policy, not with a role list. Adding a role to the hierarchy means
/// editing <see cref="PolicyRoleMap"/> — the constants below are only names. A new policy constant
/// that is added here but not to the map is never registered, and referencing an unregistered policy
/// from an <c>[Authorize]</c> attribute throws at request time rather than denying quietly. The one
/// deliberate exception is <see cref="Authenticated"/>, which is not role-based and is registered
/// explicitly in <c>Program.cs</c> with <c>RequireAuthenticatedUser</c>.</para>
/// </remarks>
public static class AppPolicies
{
    /// <summary>
    /// Requires Admin role. Full system access.
    /// </summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>
    /// Requires Editor or Admin role. Content management access.
    /// </summary>
    public const string EditorOrAbove = "EditorOrAbove";

    /// <summary>
    /// Requires Author, Editor, or Admin role. Content creation access.
    /// </summary>
    public const string AuthorOrAbove = "AuthorOrAbove";

    /// <summary>
    /// Requires Contributor, Author, Editor, or Admin role.
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately attached to no page (audited 2026-08-07, REQ-FN-009).</b> The BRD role
    /// table records the Contributor's key screens as "(policy declared; no dedicated screens yet)"
    /// and the BRD policy table repeats "(declared, not yet used by any page)" — the
    /// draft-and-submit workflow that would give the role a surface (a contributor-scoped editor
    /// with no publish action) is not in the current design. The policy is therefore registered and
    /// unit-tested but unused, and a Contributor's post-login landing is the public home page
    /// (see <see cref="RoleLandingRoutes"/>) rather than an admin route that would deny them.</para>
    /// <para>Do NOT attach it to an existing admin page to "make it useful": every one of those
    /// pages carries publish, delete or moderation actions the BRD explicitly withholds from a
    /// Contributor. Attach it when the submit-for-review screen is built.</para>
    /// </remarks>
    public const string ContributorOrAbove = "ContributorOrAbove";

    /// <summary>
    /// Requires any authenticated user regardless of role.
    /// </summary>
    /// <remarks>
    /// The only policy here that is not role-based, so it is absent from <see cref="PolicyRoleMap"/>
    /// and is registered explicitly in <c>Program.cs</c> with <c>RequireAuthenticatedUser</c>.
    /// <b>Consequence:</b> <see cref="IsSatisfiedBy"/> returns <c>false</c> for this policy whatever
    /// role is passed, because the map lookup misses. That helper answers "does this role satisfy this
    /// role-based policy"; it is not a general authorization check and must not be used to decide
    /// whether a signed-in user may proceed.
    /// </remarks>
    public const string Authenticated = "Authenticated";

    /// <summary>
    /// The single source of truth for which roles satisfy which policy (REQ-FN-009, BRD-7/8).
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> <c>Program.cs</c> builds all five policies from this map instead of
    /// repeating role lists, so a role added here is enforced everywhere at once and the
    /// hierarchy can be unit-tested without booting the host.</para>
    /// <para><b>Note:</b> <see cref="Authenticated"/> is intentionally absent — it is not a
    /// role-based policy; it only requires an authenticated principal.</para>
    /// <para><b>Usage:</b> <c>AppPolicies.PolicyRoleMap[AppPolicies.EditorOrAbove]</c>.</para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string[]> PolicyRoleMap =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [AdminOnly] = new[] { AppRoles.Admin },
            [EditorOrAbove] = new[] { AppRoles.Admin, AppRoles.Editor },
            [AuthorOrAbove] = new[] { AppRoles.Admin, AppRoles.Editor, AppRoles.Author },
            [ContributorOrAbove] = AppRoles.ContentCreators
        };

    /// <summary>
    /// Determines whether a role satisfies a role-based policy.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Looks the policy up in <see cref="PolicyRoleMap"/> and tests
    /// membership. Unknown policies and unknown roles deny by default, which is the safe direction:
    /// a typo can only ever refuse access, never grant it.</para>
    /// <para><b>Flow:</b> null-guard → map lookup → ordinal role match.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// <para><b>Scope — this is not the authorization check.</b> Enforcement is the framework's job,
    /// via the <c>[Authorize(Policy = …)]</c> attribute and the policies built from the same map. This
    /// helper exists so the hierarchy can be reasoned about and unit-tested without booting the host,
    /// and for the occasional UI decision such as whether to render a menu item. It answers only for
    /// role-based policies, so it returns <c>false</c> for <see cref="Authenticated"/> regardless of
    /// the role supplied — never use it as the sole gate on a protected operation.</para>
    /// </remarks>
    /// <param name="policyName">One of the policy constants on this class.</param>
    /// <param name="roleName">The role carried by the principal.</param>
    /// <returns><c>true</c> when the role is granted the policy.</returns>
    public static bool IsSatisfiedBy(string policyName, string roleName)
    {
        if (string.IsNullOrWhiteSpace(policyName) || string.IsNullOrWhiteSpace(roleName))
            return false;

        if (!PolicyRoleMap.TryGetValue(policyName, out var allowedRoles))
            return false;

        return allowedRoles.Contains(roleName, StringComparer.Ordinal);
    }
}
