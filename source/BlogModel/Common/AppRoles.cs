/// <summary>
/// Defines role constants and authorization policy names for TechieBlog.
/// </summary>
/// <remarks>
/// <para><b>Role Hierarchy:</b> Admin > Editor > Author > Contributor > Reader</para>
/// <para><b>Usage:</b> Use role constants with [Authorize(Roles = AppRoles.Admin)] or
/// policy names with [Authorize(Policy = AppPolicies.AdminOnly)]</para>
/// </remarks>
namespace BlogModels;

/// <summary>
/// Application role constants matching database role values.
/// </summary>
/// <remarks>
/// <para><b>Permission Levels:</b></para>
/// <list type="bullet">
///   <item><b>Admin:</b> Full system access - users, settings, all content, tags, categories</item>
///   <item><b>Editor:</b> Manage all posts, comments, moderate content</item>
///   <item><b>Author:</b> Create/edit own posts, manage own drafts</item>
///   <item><b>Contributor:</b> Submit drafts for review</item>
///   <item><b>Reader:</b> View content, comment, rate, favorite (default role)</item>
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
    /// View content, comment, rate, favorite. Default role for new users.
    /// </summary>
    public const string Reader = "Reader";

    /// <summary>
    /// All roles that have content management capabilities.
    /// </summary>
    public static readonly string[] ContentManagers = { Admin, Editor, Author };

    /// <summary>
    /// All roles that can create any content.
    /// </summary>
    public static readonly string[] ContentCreators = { Admin, Editor, Author, Contributor };
}

/// <summary>
/// Authorization policy names for use with [Authorize(Policy = "...")] attribute.
/// </summary>
/// <remarks>
/// Policies are configured in Program.cs using AddAuthorizationCore().
/// Each policy defines which roles can access protected resources.
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
    public const string ContributorOrAbove = "ContributorOrAbove";

    /// <summary>
    /// Requires any authenticated user regardless of role.
    /// </summary>
    public const string Authenticated = "Authenticated";
}
