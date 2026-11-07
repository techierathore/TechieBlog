using System.Security.Claims;
using BlogModels;
using Xunit;

namespace TechieBlog.Tests.Security;

/// <summary>
/// Unit tests for the post ownership rule in <see cref="PostAccess"/> (REQ-UI-016, 2026-11-05).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The editor and the draft preview refuse another author's post through
/// this one rule, so proving the rule here proves both pages' gate without booting the host. The
/// Author cases are the point: before 2026-11-05 any Author could open and save any post.</para>
/// <para><b>Dependencies:</b> xUnit; no database or host required.</para>
/// </remarks>
public class PostAccessTests
{
    private static ClaimsPrincipal User(long userId, string role)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.PrimarySid, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            },
            "test");
        return new ClaimsPrincipal(identity);
    }

    private static BlogPost Post(long ownerId) => new BlogPost { PostID = 42, UserID = ownerId };

    /// <summary>An Author may edit a post they own.</summary>
    [Fact]
    public void AuthorEditsOwnPost()
    {
        Assert.True(PostAccess.CanEdit(User(3, AppRoles.Author), Post(3)));
    }

    /// <summary>An Author may not edit a post owned by someone else.</summary>
    [Fact]
    public void AuthorCannotEditAnotherAuthorsPost()
    {
        Assert.False(PostAccess.CanEdit(User(3, AppRoles.Author), Post(1)));
    }

    /// <summary>A post with no owner recorded belongs to nobody, so an Author may not edit it.</summary>
    [Fact]
    public void AuthorCannotEditUnownedPost()
    {
        Assert.False(PostAccess.CanEdit(User(3, AppRoles.Author), Post(0)));
    }

    /// <summary>An Admin may edit any post.</summary>
    [Fact]
    public void AdminEditsAnyPost()
    {
        Assert.True(PostAccess.CanEdit(User(1, AppRoles.Admin), Post(3)));
    }

    /// <summary>An Editor may edit any post.</summary>
    [Fact]
    public void EditorEditsAnyPost()
    {
        Assert.True(PostAccess.CanEdit(User(2, AppRoles.Editor), Post(3)));
    }

    /// <summary>An anonymous principal owns nothing and may edit nothing.</summary>
    [Fact]
    public void AnonymousEditsNothing()
    {
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        Assert.Equal(0, PostAccess.UserIdOf(anonymous));
        Assert.False(PostAccess.CanEdit(anonymous, Post(0)));
    }

    /// <summary>A null post is never editable, whoever asks.</summary>
    [Fact]
    public void NullPostIsNeverEditable()
    {
        Assert.False(PostAccess.CanEdit(User(1, AppRoles.Admin), null!));
    }

    /// <summary>The user id comes from the PrimarySid claim the sign-in writes.</summary>
    [Fact]
    public void UserIdReadsThePrimarySidClaim()
    {
        Assert.Equal(7, PostAccess.UserIdOf(User(7, AppRoles.Contributor)));
    }
}
