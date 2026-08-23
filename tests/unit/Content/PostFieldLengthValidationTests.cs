using BlogEngine.Services;
using BlogModels;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Content;

/// <summary>
/// UAT-023 mechanism A regression guard: an over-length post field must be refused with a message
/// naming the field and its limit, never with the generic "Failed to … post" sentence.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The owner edited a post's <c>Abstract</c> past 550 characters (the column
/// width — <c>Abstract VARCHAR(550)</c>, <c>001-CreateTables.sql:214</c>) and the save silently
/// failed: nothing validated the length, so the over-length value reached Npgsql, failed with a raw
/// <c>22001</c>, and <c>BlogSvc.UpdatePostAsync</c> reported it back as the generic "Failed to
/// update post. Please try again later." — a message that named neither the field nor the cause,
/// while the post kept its old abstract. These tests would have caught that: they assert the
/// message names the field and the limit, and they assert the repository's write member is never
/// even called when a field is over length, proving the check runs BEFORE any database round trip
/// (the earlier code let the value travel all the way to Npgsql before failing).</para>
/// <para><b>Scope:</b> Both async write paths — <see cref="BlogSvc.CreatePostAsync"/> and
/// <see cref="BlogSvc.UpdatePostAsync"/> — because <c>ManagePost.razor.cs</c> can reach either
/// depending on whether the post already has an id, and both are shared by BlogApp (REQ-UI-052).
/// Every <c>VARCHAR(550)</c> free-text field the editor can overflow is covered: <c>Title</c>,
/// <c>Abstract</c>, <c>Tags</c> and <c>FeaturedImage</c>.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="IBlogPostRepo"/>. No database.</para>
/// </remarks>
public class PostFieldLengthValidationTests
{
    /// <summary>
    /// Builds the service under test over a substituted repository and a silent logger.
    /// </summary>
    private static BlogSvc BuildService(IBlogPostRepo repo)
    {
        return new BlogSvc(repo, Substitute.For<Microsoft.Extensions.Logging.ILogger<BlogSvc>>());
    }

    /// <summary>
    /// Builds a post that passes every validation rule except the one a test deliberately breaks.
    /// </summary>
    private static BlogPost ValidPost(long postId = 0)
    {
        return new BlogPost
        {
            PostID = postId,
            Title = "My Title",
            PostContent = "Body copy that is long enough to be real.",
            Abstract = "A short, well within limits abstract.",
            Tags = "dotnet,postgres",
            FeaturedImage = "/uploads/blog/hero.png"
        };
    }

    /// <summary>A repository whose <c>GetSingleAsync</c> answers with the post handed in, for update tests.</summary>
    private static IBlogPostRepo BuildRepoReturning(BlogPost existing)
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(existing.PostID, Arg.Any<CancellationToken>()).Returns(existing);
        return repo;
    }

    // =============================================================================================
    // Title (VARCHAR(550))
    // =============================================================================================

    /// <summary>A title of exactly 550 characters is accepted — the limit itself is not a violation.</summary>
    [Fact]
    public async Task CreatePostAsyncAcceptsTitleAtLimit()
    {
        var post = ValidPost();
        post.Title = new string('T', BlogPost.TitleMaxLength);
        var repo = Substitute.For<IBlogPostRepo>();
        repo.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()).Returns(1L);

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// A title one character over the limit is refused with a message naming the field and the
    /// limit, and the repository is never asked to write it.
    /// </summary>
    [Fact]
    public async Task CreatePostAsyncRejectsTitleOneOverLimit()
    {
        var post = ValidPost();
        post.Title = new string('T', BlogPost.TitleMaxLength + 1);
        var repo = Substitute.For<IBlogPostRepo>();

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("title", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BlogPost.TitleMaxLength.ToString(), result.ErrorMessage);
        Assert.Contains((BlogPost.TitleMaxLength + 1).ToString(), result.ErrorMessage);
        await repo.DidNotReceive().InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // Abstract (VARCHAR(550)) — the field the owner actually hit in UAT-023
    // =============================================================================================

    /// <summary>An abstract of exactly 550 characters is accepted.</summary>
    [Fact]
    public async Task CreatePostAsyncAcceptsAbstractAtLimit()
    {
        var post = ValidPost();
        post.Abstract = new string('A', BlogPost.AbstractMaxLength);
        var repo = Substitute.For<IBlogPostRepo>();
        repo.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()).Returns(1L);

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// An abstract one character over the limit is refused, naming the field and the limit, and
    /// nothing is inserted.
    /// </summary>
    [Fact]
    public async Task CreatePostAsyncRejectsAbstractOneOverLimit()
    {
        var post = ValidPost();
        post.Abstract = new string('A', BlogPost.AbstractMaxLength + 1);
        var repo = Substitute.For<IBlogPostRepo>();

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("abstract", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BlogPost.AbstractMaxLength.ToString(), result.ErrorMessage);
        await repo.DidNotReceive().InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The exact reported defect: editing an already-published post's abstract past the column
    /// width is refused with a curated, field-naming message — never the generic "Failed to update
    /// post. Please try again later." that hid the real cause from the owner.
    /// </summary>
    [Fact]
    public async Task UpdatePostAsyncRejectsAbstractOverLimitWithSpecificMessage()
    {
        var existing = ValidPost(34);
        existing.Abstract = new string('A', 468); // the owner's real, pre-edit length
        var repo = BuildRepoReturning(existing);

        var edited = ValidPost(34);
        edited.Abstract = new string('A', 700); // the owner's edit that crossed the ceiling

        var result = await BuildService(repo).UpdatePostAsync(edited, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.NotEqual("Failed to update post. Please try again later.", result.ErrorMessage);
        Assert.Contains("700", result.ErrorMessage);
        Assert.Contains("550", result.ErrorMessage);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // Tags (VARCHAR(550))
    // =============================================================================================

    /// <summary>A tags string of exactly 550 characters is accepted.</summary>
    [Fact]
    public async Task CreatePostAsyncAcceptsTagsAtLimit()
    {
        var post = ValidPost();
        post.Tags = new string('t', BlogPost.TagsMaxLength);
        var repo = Substitute.For<IBlogPostRepo>();
        repo.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()).Returns(1L);

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    /// <summary>A tags string one character over the limit is refused, naming the field and the limit.</summary>
    [Fact]
    public async Task CreatePostAsyncRejectsTagsOneOverLimit()
    {
        var post = ValidPost();
        post.Tags = new string('t', BlogPost.TagsMaxLength + 1);
        var repo = Substitute.For<IBlogPostRepo>();

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("tags", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BlogPost.TagsMaxLength.ToString(), result.ErrorMessage);
        await repo.DidNotReceive().InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // FeaturedImage (VARCHAR(550))
    // =============================================================================================

    /// <summary>A featured-image path of exactly 550 characters is accepted.</summary>
    [Fact]
    public async Task CreatePostAsyncAcceptsFeaturedImageAtLimit()
    {
        var post = ValidPost();
        post.FeaturedImage = "/uploads/blog/" + new string('f', BlogPost.FeaturedImageMaxLength - "/uploads/blog/".Length);
        var repo = Substitute.For<IBlogPostRepo>();
        repo.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()).Returns(1L);

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(BlogPost.FeaturedImageMaxLength, post.FeaturedImage.Length);
    }

    /// <summary>A featured-image path one character over the limit is refused, naming the field and the limit.</summary>
    [Fact]
    public async Task CreatePostAsyncRejectsFeaturedImageOneOverLimit()
    {
        var post = ValidPost();
        post.FeaturedImage = "/uploads/blog/" + new string('f', BlogPost.FeaturedImageMaxLength - "/uploads/blog/".Length + 1);
        var repo = Substitute.For<IBlogPostRepo>();

        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Contains("featured image", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(BlogPost.FeaturedImageMaxLength.ToString(), result.ErrorMessage);
        await repo.DidNotReceive().InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // Validation order — length is checked before any repository write, on both write paths
    // =============================================================================================

    /// <summary>
    /// On update, an over-length field is refused WITHOUT writing — the row lookup that already ran
    /// (to confirm the post exists) is not itself a write, but <c>UpdateAsync</c> must never be
    /// reached once a field is found over length.
    /// </summary>
    [Fact]
    public async Task UpdatePostAsyncNeverWritesWhenAFieldIsOverLength()
    {
        var existing = ValidPost(9);
        var repo = BuildRepoReturning(existing);
        var edited = ValidPost(9);
        edited.Title = new string('T', BlogPost.TitleMaxLength + 1);

        var result = await BuildService(repo).UpdatePostAsync(edited, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }
}
