using System.Security.Claims;

namespace BlogModels;

/// <summary>
/// Decides who may open and change a given post.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> One place for the ownership rule the posts list already applies
/// (<c>BlogSvc.GetAllPostsAsync(userId, isAdminOrEditor)</c>): an Admin or an Editor may edit any
/// post; anyone else may edit only the posts they own. Until 2026-11-05 the editor and the draft
/// preview loaded any post by id, so an Author could open and save another author's post
/// (REQ-UI-016, DevGuide finding D3).</para>
/// <para><b>Code Flow:</b> Pure functions over the signed-in principal and the post; no I/O.</para>
/// <para><b>Dependencies:</b> <see cref="AppRoles"/> for the role names; the principal's
/// <see cref="ClaimTypes.PrimarySid"/> claim, which the sign-in writes as the user id.</para>
/// <para><b>Usage:</b> <c>PostAccess.CanEdit(authState.User, post)</c> before showing an editable
/// form, a preview, or saving.</para>
/// </remarks>
public static class PostAccess
{
    /// <summary>
    /// Reads the signed-in user's id from the principal.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The id lives in the <see cref="ClaimTypes.PrimarySid"/> claim
    /// the sign-in writes; an anonymous principal or a malformed claim reads as 0, which owns
    /// nothing.</para>
    /// <para><b>Flow:</b> Find the claim, parse it, fall back to 0.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="user">The principal of the current render.</param>
    /// <returns>The user id, or 0 when there is none.</returns>
    public static long UserIdOf(ClaimsPrincipal user)
    {
        var claim = user?.FindFirst(ClaimTypes.PrimarySid)?.Value;
        return long.TryParse(claim, out long userId) ? userId : 0;
    }

    /// <summary>
    /// True when the principal may open, change and save this post.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An Admin or an Editor may edit every post. Any other signed-in
    /// user may edit a post only when its <see cref="BlogPost.UserID"/> is their own id. A post
    /// with no owner recorded (id 0) belongs to nobody, so only an Admin or an Editor may edit it.
    /// A null post is never editable.</para>
    /// <para><b>Flow:</b> Null check, role check, then the owner comparison.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="user">The principal of the current render.</param>
    /// <param name="post">The post that was loaded.</param>
    /// <returns>True when the user may edit the post.</returns>
    public static bool CanEdit(ClaimsPrincipal user, BlogPost post)
    {
        if (user == null || post == null)
        {
            return false;
        }

        if (user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.Editor))
        {
            return true;
        }

        var userId = UserIdOf(user);
        return userId > 0 && post.UserID == userId;
    }
}
