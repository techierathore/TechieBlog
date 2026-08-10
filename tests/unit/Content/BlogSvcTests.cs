using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Content;

/// <summary>
/// Unit tests for <see cref="BlogEngine.Services.BlogSvc"/> — the post lifecycle service.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the rules that govern a post's life: what makes it valid, how a unique
/// slug is derived, how the four states (draft, scheduled, published, soft-deleted) are entered and
/// left, and the failure convention — reads degrade to empty/null/zero while writes report a failed
/// <c>Result</c>. Both the blocking surface and its <c>…Async</c> twins are covered, because
/// REQ-NFR-026 keeps them behaviourally identical and a divergence would be a silent regression.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="IBlogPostRepo"/> and
/// <see cref="ILogger{TCategoryName}"/>. No database and no network — every repository member the
/// service touches is stubbed, including the <c>…Async</c> twins, which Castle DynamicProxy
/// intercepts rather than letting them fall through to their interface default implementation.</para>
/// </remarks>
public class BlogSvcTests
{
    /// <summary>
    /// Builds the service under test over a substituted repository and a silent logger.
    /// </summary>
    /// <param name="repo">The substituted repository the service should use.</param>
    /// <returns>A service wired to <paramref name="repo"/>.</returns>
    private static BlogEngine.Services.BlogSvc BuildService(IBlogPostRepo repo)
    {
        return new BlogEngine.Services.BlogSvc(repo, Substitute.For<ILogger<BlogEngine.Services.BlogSvc>>());
    }

    /// <summary>
    /// Builds a post that passes every validation rule, so a test can isolate the one rule it varies.
    /// </summary>
    /// <param name="postId">Identifier to carry; zero means "never persisted".</param>
    /// <returns>A valid post.</returns>
    private static BlogPost ValidPost(long postId = 0)
    {
        return new BlogPost
        {
            PostID = postId,
            Title = "My Title",
            PostContent = "Body copy that is long enough to be real."
        };
    }

    // =============================================================================================
    // Blocking reads
    // =============================================================================================

    /// <summary>
    /// An admin or editor lists every post, so the unscoped query is the one that runs and the
    /// author-scoped query is never touched.
    /// </summary>
    [Fact]
    public void GetAllPostsReturnsEveryPostForAdmin()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var everything = new[] { ValidPost(1), ValidPost(2) };
        repo.GetAll().Returns(everything);

        // Act
        var result = BuildService(repo).GetAllPosts(42, isAdmin: true);

        // Assert
        Assert.Same(everything, result);
        repo.DidNotReceive().GetAllById(Arg.Any<long>());
    }

    /// <summary>
    /// A non-privileged caller only ever sees their own rows, and the scoping happens in the query
    /// rather than in the page — the signed-in user's id is the one forwarded.
    /// </summary>
    [Fact]
    public void GetAllPostsScopesToAuthorWhenNotAdmin()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var mine = new[] { ValidPost(7) };
        repo.GetAllById(42).Returns(mine);

        // Act
        var result = BuildService(repo).GetAllPosts(42, isAdmin: false);

        // Assert
        Assert.Same(mine, result);
        repo.DidNotReceive().GetAll();
    }

    /// <summary>
    /// A failed listing query blanks the grid rather than taking the admin page down.
    /// </summary>
    [Fact]
    public void GetAllPostsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetAll()).Do(_ => throw new InvalidOperationException("connection reset"));

        // Act
        var result = BuildService(repo).GetAllPosts(42, isAdmin: true);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The admin lookup forwards the identifier untouched and returns whatever row the repository
    /// produced, in any publication state.
    /// </summary>
    [Fact]
    public void GetSinglePostForwardsIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost(9);
        repo.GetSingle(9).Returns(post);

        // Act
        var result = BuildService(repo).GetSinglePost(9);

        // Assert
        Assert.Same(post, result);
    }

    /// <summary>
    /// A failed single-row read is answered with null, exactly as "no such post" is.
    /// </summary>
    [Fact]
    public void GetSinglePostReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetSingle(9)).Do(_ => throw new InvalidOperationException("connection reset"));

        // Act
        var result = BuildService(repo).GetSinglePost(9);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// A blank slug can only come from a malformed route, so it is answered null without a round
    /// trip — and an attacker learns nothing that an unknown slug would not also tell them.
    /// </summary>
    /// <param name="slug">The malformed slug supplied by the route.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetPostBySlugRejectsBlankSlugWithoutQuerying(string slug)
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).GetPostBySlug(slug);

        // Assert
        Assert.Null(result);
        repo.DidNotReceive().GetBySlug(Arg.Any<string>());
    }

    /// <summary>
    /// A real slug reaches the repository verbatim — casing and hyphens are not normalised here.
    /// </summary>
    [Fact]
    public void GetPostBySlugForwardsSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost(3);
        repo.GetBySlug("my-title").Returns(post);

        // Act
        var result = BuildService(repo).GetPostBySlug("my-title");

        // Assert
        Assert.Same(post, result);
    }

    /// <summary>
    /// A failed slug read renders the post page's not-found state rather than throwing on a public
    /// page.
    /// </summary>
    [Fact]
    public void GetPostBySlugReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetBySlug("my-title")).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetPostBySlug("my-title");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Paging arguments are passed through unclamped — the caller owns the page size — and the rows
    /// come back untouched.
    /// </summary>
    [Fact]
    public void GetPublishedPostsForwardsPaging()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var page = new[] { ValidPost(1) };
        repo.GetPublishedPosts(25, 50).Returns(page);

        // Act
        var result = BuildService(repo).GetPublishedPosts(25, 50);

        // Assert
        Assert.Same(page, result);
        repo.Received(1).GetPublishedPosts(25, 50);
    }

    /// <summary>
    /// A failed listing query blanks the home page's article strip rather than erroring the landing
    /// page.
    /// </summary>
    [Fact]
    public void GetPublishedPostsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetPublishedPosts(10, 0)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetPublishedPosts(10, 0);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The dashboard's counts carrier is handed back exactly as the aggregate query produced it.
    /// </summary>
    [Fact]
    public void GetBlogCountsForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var counts = new BlogPost { BlogCount = 17 };
        repo.GetTheCounts().Returns(counts);

        // Act
        var result = BuildService(repo).GetBlogCounts();

        // Assert
        Assert.Same(counts, result);
    }

    /// <summary>
    /// A failed aggregate yields a zeroed carrier rather than null, so the dashboard tile renders
    /// "0" instead of needing a null branch of its own.
    /// </summary>
    [Fact]
    public void GetBlogCountsReturnsZeroCarrierOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetTheCounts()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetBlogCounts();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.BlogCount);
    }

    /// <summary>
    /// The featured item is simply the newest published post the repository reports; the service
    /// applies no editorial rule of its own.
    /// </summary>
    [Fact]
    public void GetFeaturedPostForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost(4);
        repo.GetFeaturedPost().Returns(post);

        // Act
        var result = BuildService(repo).GetFeaturedPost();

        // Assert
        Assert.Same(post, result);
    }

    /// <summary>
    /// A failed featured-post read is answered null, which the home page renders as the absence of a
    /// featured card.
    /// </summary>
    [Fact]
    public void GetFeaturedPostReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetFeaturedPost()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetFeaturedPost();

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// The published count is reported unchanged so the pager and the listing agree.
    /// </summary>
    [Fact]
    public void GetPublishedPostCountForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetPublishedPostCount().Returns(31);

        // Act
        var result = BuildService(repo).GetPublishedPostCount();

        // Assert
        Assert.Equal(31, result);
    }

    /// <summary>
    /// A failed count collapses the pager to zero rather than throwing on a public page.
    /// </summary>
    [Fact]
    public void GetPublishedPostCountReturnsZeroOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetPublishedPostCount()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetPublishedPostCount();

        // Assert
        Assert.Equal(0, result);
    }

    /// <summary>
    /// The category filter and both paging arguments reach the repository in the order the caller
    /// supplied them.
    /// </summary>
    [Fact]
    public void GetPostsByCategoryForwardsArguments()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var page = new[] { ValidPost(1) };
        repo.GetPostsByCategory(5, 20, 40).Returns(page);

        // Act
        var result = BuildService(repo).GetPostsByCategory(5, 20, 40);

        // Assert
        Assert.Same(page, result);
        repo.Received(1).GetPostsByCategory(5, 20, 40);
    }

    /// <summary>
    /// A failed category listing degrades to an empty page.
    /// </summary>
    [Fact]
    public void GetPostsByCategoryReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetPostsByCategory(5, 20, 40)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetPostsByCategory(5, 20, 40);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The category count is the counterpart of the category listing and is forwarded unchanged.
    /// </summary>
    [Fact]
    public void GetPostCountByCategoryForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetPostCountByCategory(5).Returns(12);

        // Act
        var result = BuildService(repo).GetPostCountByCategory(5);

        // Assert
        Assert.Equal(12, result);
    }

    /// <summary>
    /// A failed category count collapses that page's pager to zero.
    /// </summary>
    [Fact]
    public void GetPostCountByCategoryReturnsZeroOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetPostCountByCategory(5)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetPostCountByCategory(5);

        // Assert
        Assert.Equal(0, result);
    }

    /// <summary>
    /// The scheduled list is whatever the repository reports, so a schedule the publisher has failed
    /// to act on stays visible rather than disappearing.
    /// </summary>
    [Fact]
    public void GetScheduledPostsForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var scheduled = new[] { ValidPost(1) };
        repo.GetScheduledPosts().Returns(scheduled);

        // Act
        var result = BuildService(repo).GetScheduledPosts();

        // Assert
        Assert.Same(scheduled, result);
    }

    /// <summary>
    /// A failed scheduled-posts read degrades to an empty sequence.
    /// </summary>
    [Fact]
    public void GetScheduledPostsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetScheduledPosts()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetScheduledPosts();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The due cut-off is the service's own UTC clock rather than a caller-supplied instant, so no
    /// caller can accidentally publish the future by passing a wrong time.
    /// </summary>
    [Fact]
    public void GetDueScheduledPostsUsesCurrentUtcInstant()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var before = DateTime.UtcNow;

        // Act
        BuildService(repo).GetDueScheduledPosts();

        // Assert
        var after = DateTime.UtcNow;
        repo.Received(1).GetDueScheduledPosts(Arg.Is<DateTime>(now => now >= before && now <= after));
    }

    /// <summary>
    /// A failed due-posts read degrades to an empty sequence so one bad publisher cycle skips
    /// publication instead of stopping the hosted service.
    /// </summary>
    [Fact]
    public void GetDueScheduledPostsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetDueScheduledPosts(Arg.Any<DateTime>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetDueScheduledPosts();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The visitor's query text and both paging arguments are passed to the repository untouched —
    /// the match itself is the repository's business.
    /// </summary>
    [Fact]
    public void SearchPostsForwardsQueryAndPaging()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var hits = new[] { ValidPost(1) };
        repo.SearchPosts("blazor", 5, 10).Returns(hits);

        // Act
        var result = BuildService(repo).SearchPosts("blazor", 5, 10);

        // Assert
        Assert.Same(hits, result);
    }

    /// <summary>
    /// A caller that supplies only a query gets the documented default page of ten from offset zero.
    /// </summary>
    [Fact]
    public void SearchPostsAppliesDefaultPaging()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        BuildService(repo).SearchPosts("blazor");

        // Assert
        repo.Received(1).SearchPosts("blazor", 10, 0);
    }

    /// <summary>
    /// A failed search renders "no results" rather than taking the results page down.
    /// </summary>
    [Fact]
    public void SearchPostsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.SearchPosts("blazor", 10, 0)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).SearchPosts("blazor");

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The search count is forwarded unchanged so the pager cannot promise a page the search will
    /// not return.
    /// </summary>
    [Fact]
    public void GetSearchResultCountForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSearchResultCount("blazor").Returns(8);

        // Act
        var result = BuildService(repo).GetSearchResultCount("blazor");

        // Assert
        Assert.Equal(8, result);
    }

    /// <summary>
    /// A failed search count collapses the results pager to zero.
    /// </summary>
    [Fact]
    public void GetSearchResultCountReturnsZeroOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetSearchResultCount("blazor")).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetSearchResultCount("blazor");

        // Assert
        Assert.Equal(0, result);
    }

    // =============================================================================================
    // CreatePost
    // =============================================================================================

    /// <summary>
    /// A null post is an expected caller mistake and comes back as a failed result rather than a
    /// null-reference exception.
    /// </summary>
    [Fact]
    public void CreatePostRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).CreatePost(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post cannot be null", result.ErrorMessage);
        repo.DidNotReceive().InsertToGetId(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// A title made only of whitespace is no title at all — validation refuses it before any I/O.
    /// </summary>
    /// <param name="title">The unusable title supplied by the editor.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePostRejectsBlankTitle(string title)
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost();
        post.Title = title;

        // Act
        var result = BuildService(repo).CreatePost(post);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Title is required", result.ErrorMessage);
        repo.DidNotReceive().InsertToGetId(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// An empty body is refused for the same reason a missing title is, and the title check runs
    /// first so a post missing both is reported by its title.
    /// </summary>
    [Fact]
    public void CreatePostRejectsBlankContent()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost();
        post.PostContent = "   ";

        // Act
        var result = BuildService(repo).CreatePost(post);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Content is required", result.ErrorMessage);
    }

    /// <summary>
    /// An editor that leaves the slug blank gets one derived from the title, so every post is
    /// addressable without the author having to think about URLs.
    /// </summary>
    [Fact]
    public void CreatePostDerivesSlugFromTitle()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost();
        post.Title = "C# — Tips & Tricks!";

        // Act
        var result = BuildService(repo).CreatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("c-tips-tricks", post.Slug);
    }

    /// <summary>
    /// A slug the author typed themselves is kept, because the slug is the public URL and the
    /// author's choice of address is deliberate.
    /// </summary>
    [Fact]
    public void CreatePostKeepsSuppliedSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost();
        post.Slug = "hand-picked";

        // Act
        BuildService(repo).CreatePost(post);

        // Assert
        Assert.Equal("hand-picked", post.Slug);
    }

    /// <summary>
    /// A slug already in use gains an ordinal suffix rather than colliding, so the second post with
    /// a given title is addressable as "-2".
    /// </summary>
    [Fact]
    public void CreatePostSuffixesDuplicateSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.SlugExists("my-title").Returns(true);
        var post = ValidPost();

        // Act
        var result = BuildService(repo).CreatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("my-title-2", post.Slug);
    }

    /// <summary>
    /// When the first suffixed candidate is also taken the loop keeps climbing until it finds a free
    /// slug, so a popular title does not fail to save.
    /// </summary>
    [Fact]
    public void CreatePostRetriesUntilSlugIsFree()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.SlugExists("my-title").Returns(true);
        repo.SlugExists("my-title-2").Returns(true);
        repo.SlugExists("my-title-3").Returns(false);
        var post = ValidPost();

        // Act
        BuildService(repo).CreatePost(post);

        // Assert
        Assert.Equal("my-title-3", post.Slug);
    }

    /// <summary>
    /// A pathological collision cannot spin forever: the retry loop is capped, leaving the last
    /// candidate in place for the database's unique constraint to arbitrate.
    /// </summary>
    [Fact]
    public void CreatePostStopsSuffixingAtTheAttemptCap()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.SlugExists(Arg.Any<string>()).Returns(true);
        var post = ValidPost();

        // Act
        BuildService(repo).CreatePost(post);

        // Assert
        Assert.Equal("my-title-100", post.Slug);
        repo.Received(100).SlugExists(Arg.Any<string>());
    }

    /// <summary>
    /// The creation timestamp is stamped by the service in UTC rather than trusted from the caller,
    /// and the deleted flag is forced false so a recycled object cannot be inserted pre-deleted.
    /// </summary>
    [Fact]
    public void CreatePostStampsCreatedOnAndClearsDeletedFlag()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost();
        post.CreatedOn = new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        post.IsDeleted = true;
        var before = DateTime.UtcNow;

        // Act
        BuildService(repo).CreatePost(post);

        // Assert
        Assert.False(post.IsDeleted);
        Assert.InRange(post.CreatedOn, before, DateTime.UtcNow);
    }

    /// <summary>
    /// The generated key is written back onto the caller's object and travels out on the successful
    /// result, which is how the editor learns the id of the post it just created.
    /// </summary>
    [Fact]
    public void CreatePostAssignsGeneratedIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.InsertToGetId(Arg.Any<BlogPost>()).Returns(77L);
        var post = ValidPost();

        // Act
        var result = BuildService(repo).CreatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(77, post.PostID);
        Assert.Same(post, result.Data);
    }

    /// <summary>
    /// An unexpected persistence error is converted into a failed result carrying the reason rather
    /// than escaping to the page.
    /// </summary>
    [Fact]
    public void CreatePostReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.InsertToGetId(Arg.Any<BlogPost>()))
            .Do(_ => throw new InvalidOperationException("duplicate key"));

        // Act
        var result = BuildService(repo).CreatePost(ValidPost());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create post: duplicate key", result.ErrorMessage);
    }

    // =============================================================================================
    // UpdatePost
    // =============================================================================================

    /// <summary>
    /// A null post is refused before anything is read or written.
    /// </summary>
    [Fact]
    public void UpdatePostRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).UpdatePost(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// An update needs a key; a non-positive identifier means the post was never persisted and is a
    /// caller error rather than an insert.
    /// </summary>
    /// <param name="postId">The unusable identifier.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdatePostRejectsNonPositiveIdentifier(long postId)
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).UpdatePost(ValidPost(postId));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid post ID", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// The title rule applies to an edit exactly as it does to a creation.
    /// </summary>
    [Fact]
    public void UpdatePostRejectsBlankTitle()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost(5);
        post.Title = " ";

        // Act
        var result = BuildService(repo).UpdatePost(post);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Title is required", result.ErrorMessage);
    }

    /// <summary>
    /// The content rule applies to an edit exactly as it does to a creation.
    /// </summary>
    [Fact]
    public void UpdatePostRejectsBlankContent()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost(5);
        post.PostContent = "";

        // Act
        var result = BuildService(repo).UpdatePost(post);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Content is required", result.ErrorMessage);
    }

    /// <summary>
    /// Editing a post that has been deleted in another tab reports "not found" rather than a success
    /// that updated no rows.
    /// </summary>
    [Fact]
    public void UpdatePostRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns((BlogPost?)null);

        // Act
        var result = BuildService(repo).UpdatePost(ValidPost(5));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post not found", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// The uniqueness check excludes the row being edited, so re-saving a post without renaming it
    /// keeps its slug — and therefore every inbound link to it.
    /// </summary>
    [Fact]
    public void UpdatePostExcludesItselfFromSlugCheck()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        var post = ValidPost(5);
        post.Slug = "my-title";

        // Act
        var result = BuildService(repo).UpdatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("my-title", post.Slug);
        repo.Received(1).SlugExists("my-title", 5);
    }

    /// <summary>
    /// A slug that genuinely belongs to another post is suffixed, exactly as it is on creation.
    /// </summary>
    [Fact]
    public void UpdatePostSuffixesCollidingSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        repo.SlugExists("my-title", 5).Returns(true);
        var post = ValidPost(5);

        // Act
        BuildService(repo).UpdatePost(post);

        // Assert
        Assert.Equal("my-title-2", post.Slug);
    }

    /// <summary>
    /// When the first suffixed candidate is also taken on an edit, the loop keeps climbing until it
    /// finds a free slug — the exclusion of the edited row applies to every attempt, not just the
    /// first.
    /// </summary>
    [Fact]
    public void UpdatePostRetriesUntilSlugIsFree()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        repo.SlugExists("my-title", 5).Returns(true);
        repo.SlugExists("my-title-2", 5).Returns(true);
        var post = ValidPost(5);

        // Act
        BuildService(repo).UpdatePost(post);

        // Assert
        Assert.Equal("my-title-3", post.Slug);
    }

    /// <summary>
    /// A blank slug on an edit is regenerated from the current title, so renaming a post that has
    /// had its slug cleared still produces an address.
    /// </summary>
    [Fact]
    public void UpdatePostDerivesSlugFromTitleWhenBlank()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        var post = ValidPost(5);
        post.Title = "Second Edition";

        // Act
        BuildService(repo).UpdatePost(post);

        // Assert
        Assert.Equal("second-edition", post.Slug);
    }

    /// <summary>
    /// The update timestamp is stamped in UTC by the service and the row is written exactly once.
    /// </summary>
    [Fact]
    public void UpdatePostStampsUpdatedOn()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        var post = ValidPost(5);
        var before = DateTime.UtcNow;

        // Act
        var result = BuildService(repo).UpdatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.InRange(post.UpdatedOn, before, DateTime.UtcNow);
        repo.Received(1).Update(post);
    }

    /// <summary>
    /// An unexpected persistence error on the update is converted into a failed result.
    /// </summary>
    [Fact]
    public void UpdatePostReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        repo.When(r => r.Update(Arg.Any<BlogPost>())).Do(_ => throw new InvalidOperationException("deadlock"));

        // Act
        var result = BuildService(repo).UpdatePost(ValidPost(5));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update post: deadlock", result.ErrorMessage);
    }

    // =============================================================================================
    // SavePost and the state-changing helpers
    // =============================================================================================

    /// <summary>
    /// The save entry point refuses a null post before deciding between insert and update.
    /// </summary>
    [Fact]
    public void SavePostRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).SavePost(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// A post that has never been persisted is inserted, which is what lets the editor bind one
    /// method to its save button regardless of mode.
    /// </summary>
    [Fact]
    public void SavePostInsertsWhenIdentifierIsAbsent()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).SavePost(ValidPost());

        // Assert
        Assert.True(result.IsSuccess);
        repo.Received(1).InsertToGetId(Arg.Any<BlogPost>());
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// A post that already carries a key is updated rather than duplicated.
    /// </summary>
    [Fact]
    public void SavePostUpdatesWhenIdentifierIsPresent()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));

        // Act
        var result = BuildService(repo).SavePost(ValidPost(5));

        // Assert
        Assert.True(result.IsSuccess);
        repo.Received(1).Update(Arg.Any<BlogPost>());
        repo.DidNotReceive().InsertToGetId(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// The draft helper refuses a null post rather than dereferencing it to clear the flag.
    /// </summary>
    [Fact]
    public void SaveDraftRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).SaveDraft(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// Saving a live post as a draft unpublishes it — the draft button is a state change, not merely
    /// a save — while the original publication date survives the round trip.
    /// </summary>
    [Fact]
    public void SaveDraftUnpublishesAndKeepsPublicationDate()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        var firstPublished = new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var post = ValidPost(5);
        post.Published = true;
        post.PublishedOn = firstPublished;

        // Act
        var result = BuildService(repo).SaveDraft(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(post.Published);
        Assert.Equal(firstPublished, post.PublishedOn);
    }

    /// <summary>
    /// The publish helper refuses a null post before setting any flag.
    /// </summary>
    [Fact]
    public void PublishPostRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).PublishPost(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// A first publication stamps the publication date and makes the post public.
    /// </summary>
    [Fact]
    public void PublishPostStampsFirstPublicationDate()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost();
        var before = DateTime.UtcNow;

        // Act
        var result = BuildService(repo).PublishPost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(post.Published);
        Assert.NotNull(post.PublishedOn);
        Assert.InRange(post.PublishedOn!.Value, before, DateTime.UtcNow);
    }

    /// <summary>
    /// Re-publishing a post that was temporarily withdrawn restores it with its original publication
    /// date, so "unpublish, fix a typo, publish again" does not jump it to the top of the feed.
    /// </summary>
    [Fact]
    public void PublishPostKeepsExistingPublicationDate()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        var firstPublished = new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var post = ValidPost(5);
        post.PublishedOn = firstPublished;

        // Act
        BuildService(repo).PublishPost(post);

        // Assert
        Assert.Equal(firstPublished, post.PublishedOn);
    }

    // =============================================================================================
    // DeletePost, UnpublishPost, QuickPublish, SchedulePost, CancelSchedule
    // =============================================================================================

    /// <summary>
    /// A non-positive identifier cannot name a row and is refused before any read.
    /// </summary>
    /// <param name="postId">The unusable identifier.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void DeletePostRejectsNonPositiveIdentifier(long postId)
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).DeletePost(postId);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid post ID", result.ErrorMessage);
        repo.DidNotReceive().GetSingle(Arg.Any<long>());
    }

    /// <summary>
    /// Deleting a post that is not there reports "not found" rather than a success that removed
    /// nothing.
    /// </summary>
    [Fact]
    public void DeletePostRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns((BlogPost?)null);

        // Act
        var result = BuildService(repo).DeletePost(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post not found", result.ErrorMessage);
    }

    /// <summary>
    /// A double-submitted delete button reports honestly instead of implying it did something, so an
    /// already-deleted post is refused rather than re-stamped.
    /// </summary>
    [Fact]
    public void DeletePostRejectsAlreadyDeletedRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var existing = ValidPost(5);
        existing.IsDeleted = true;
        repo.GetSingle(5).Returns(existing);

        // Act
        var result = BuildService(repo).DeletePost(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is already deleted", result.ErrorMessage);
        repo.DidNotReceive().SoftDelete(Arg.Any<long>());
    }

    /// <summary>
    /// A live post is retired by flag rather than removed, preserving its comments, ratings and view
    /// history.
    /// </summary>
    [Fact]
    public void DeletePostSoftDeletesTheRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));

        // Act
        var result = BuildService(repo).DeletePost(5);

        // Assert
        Assert.True(result.IsSuccess);
        repo.Received(1).SoftDelete(5);
    }

    /// <summary>
    /// An unexpected persistence error on the delete is converted into a failed result.
    /// </summary>
    [Fact]
    public void DeletePostReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        repo.When(r => r.SoftDelete(5)).Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = BuildService(repo).DeletePost(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete post: timeout", result.ErrorMessage);
    }

    /// <summary>
    /// A non-positive identifier is refused before the row is read.
    /// </summary>
    [Fact]
    public void UnpublishPostRejectsNonPositiveIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).UnpublishPost(0);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid post ID", result.ErrorMessage);
    }

    /// <summary>
    /// Withdrawing a post that does not exist reports "not found".
    /// </summary>
    [Fact]
    public void UnpublishPostRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns((BlogPost?)null);

        // Act
        var result = BuildService(repo).UnpublishPost(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post not found", result.ErrorMessage);
    }

    /// <summary>
    /// Unpublishing something already unpublished is refused rather than reported as a no-op success.
    /// </summary>
    [Fact]
    public void UnpublishPostRejectsAlreadyUnpublishedRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));

        // Act
        var result = BuildService(repo).UnpublishPost(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is already unpublished", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// Withdrawing a live post clears its published flag but deliberately keeps the original
    /// publication date, so re-publishing later restores it rather than presenting an old article as
    /// new.
    /// </summary>
    [Fact]
    public void UnpublishPostClearsFlagAndKeepsPublicationDate()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var firstPublished = new DateTime(2021, 7, 8, 0, 0, 0, DateTimeKind.Utc);
        var existing = ValidPost(5);
        existing.Published = true;
        existing.PublishedOn = firstPublished;
        repo.GetSingle(5).Returns(existing);

        // Act
        var result = BuildService(repo).UnpublishPost(5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(existing.Published);
        Assert.Equal(firstPublished, existing.PublishedOn);
        repo.Received(1).Update(existing);
    }

    /// <summary>
    /// An unexpected persistence error is converted into a failed result carrying the reason.
    /// </summary>
    [Fact]
    public void UnpublishPostReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var existing = ValidPost(5);
        existing.Published = true;
        repo.GetSingle(5).Returns(existing);
        repo.When(r => r.Update(Arg.Any<BlogPost>())).Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = BuildService(repo).UnpublishPost(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to unpublish post: timeout", result.ErrorMessage);
    }

    /// <summary>
    /// A non-positive identifier is refused before the row is read.
    /// </summary>
    [Fact]
    public void QuickPublishRejectsNonPositiveIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).QuickPublish(-2);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid post ID", result.ErrorMessage);
    }

    /// <summary>
    /// One-click publishing a post that does not exist reports "not found".
    /// </summary>
    [Fact]
    public void QuickPublishRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns((BlogPost?)null);

        // Act
        var result = BuildService(repo).QuickPublish(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post not found", result.ErrorMessage);
    }

    /// <summary>
    /// Publishing something already public is refused rather than silently rewriting the row.
    /// </summary>
    [Fact]
    public void QuickPublishRejectsAlreadyPublishedRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var existing = ValidPost(5);
        existing.Published = true;
        repo.GetSingle(5).Returns(existing);

        // Act
        var result = BuildService(repo).QuickPublish(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is already published", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// Publishing now makes a future scheduled publication meaningless, so the pending schedule is
    /// discarded — otherwise the background publisher would act on the post a second time.
    /// </summary>
    [Fact]
    public void QuickPublishClearsPendingSchedule()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var existing = ValidPost(5);
        existing.ScheduledPublishOn = DateTime.UtcNow.AddDays(3);
        repo.GetSingle(5).Returns(existing);

        // Act
        var result = BuildService(repo).QuickPublish(5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(existing.Published);
        Assert.Null(existing.ScheduledPublishOn);
        repo.Received(1).Update(existing);
    }

    /// <summary>
    /// As with the object-based publish, a post that already carries a publication date keeps it.
    /// </summary>
    [Fact]
    public void QuickPublishKeepsExistingPublicationDate()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var firstPublished = new DateTime(2018, 2, 2, 0, 0, 0, DateTimeKind.Utc);
        var existing = ValidPost(5);
        existing.PublishedOn = firstPublished;
        repo.GetSingle(5).Returns(existing);

        // Act
        BuildService(repo).QuickPublish(5);

        // Assert
        Assert.Equal(firstPublished, existing.PublishedOn);
    }

    /// <summary>
    /// An unexpected persistence error is converted into a failed result carrying the reason.
    /// </summary>
    [Fact]
    public void QuickPublishReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        repo.When(r => r.Update(Arg.Any<BlogPost>())).Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = BuildService(repo).QuickPublish(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to publish post: timeout", result.ErrorMessage);
    }

    /// <summary>
    /// Scheduling refuses a null post before touching the clock.
    /// </summary>
    [Fact]
    public void SchedulePostRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).SchedulePost(null!, DateTime.UtcNow.AddDays(1));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// A schedule that is already due is really a request to publish now, and is refused so the
    /// caller reaches for the honest method instead.
    /// </summary>
    [Fact]
    public void SchedulePostRejectsPastInstant()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).SchedulePost(ValidPost(), DateTime.UtcNow.AddMinutes(-1));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Scheduled time must be in the future", result.ErrorMessage);
        repo.DidNotReceive().InsertToGetId(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// Scheduling stores the instant as given and forces the post unpublished, so scheduling a live
    /// post takes it down until its time arrives.
    /// </summary>
    [Fact]
    public void SchedulePostStoresInstantAndUnpublishes()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        var when = DateTime.UtcNow.AddDays(2);
        var post = ValidPost(5);
        post.Published = true;

        // Act
        var result = BuildService(repo).SchedulePost(post, when);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(when, post.ScheduledPublishOn);
        Assert.False(post.Published);
    }

    /// <summary>
    /// A non-positive identifier is refused before the row is read.
    /// </summary>
    [Fact]
    public void CancelScheduleRejectsNonPositiveIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = BuildService(repo).CancelSchedule(0);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid post ID", result.ErrorMessage);
    }

    /// <summary>
    /// Cancelling the schedule of a post that does not exist reports "not found".
    /// </summary>
    [Fact]
    public void CancelScheduleRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns((BlogPost?)null);

        // Act
        var result = BuildService(repo).CancelSchedule(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post not found", result.ErrorMessage);
    }

    /// <summary>
    /// Cancelling a post that was never scheduled is refused rather than silently succeeding.
    /// </summary>
    [Fact]
    public void CancelScheduleRejectsUnscheduledRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));

        // Act
        var result = BuildService(repo).CancelSchedule(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is not scheduled", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// Cancelling clears only the schedule; the post keeps the publication state it had, which for a
    /// scheduled post is unpublished.
    /// </summary>
    [Fact]
    public void CancelScheduleClearsScheduleOnly()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var existing = ValidPost(5);
        existing.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);
        repo.GetSingle(5).Returns(existing);

        // Act
        var result = BuildService(repo).CancelSchedule(5);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(existing.ScheduledPublishOn);
        Assert.False(existing.Published);
        repo.Received(1).Update(existing);
    }

    /// <summary>
    /// An unexpected persistence error is converted into a failed result carrying the reason.
    /// </summary>
    [Fact]
    public void CancelScheduleReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var existing = ValidPost(5);
        existing.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);
        repo.GetSingle(5).Returns(existing);
        repo.When(r => r.Update(Arg.Any<BlogPost>())).Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = BuildService(repo).CancelSchedule(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to cancel schedule: timeout", result.ErrorMessage);
    }

    // =============================================================================================
    // Async surface — REQ-NFR-026
    // =============================================================================================

    /// <summary>
    /// The async listing applies the same privilege branch as its blocking twin: an admin gets the
    /// unscoped query, an author gets their own rows.
    /// </summary>
    [Fact]
    public async Task GetAllPostsAsyncBranchesOnPrivilege()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var everything = new[] { ValidPost(1) };
        var mine = new[] { ValidPost(2) };
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(everything);
        repo.GetAllByIdAsync(42, Arg.Any<CancellationToken>()).Returns(mine);
        var service = BuildService(repo);

        // Act
        var asAdmin = await service.GetAllPostsAsync(42, isAdmin: true, token);
        var asAuthor = await service.GetAllPostsAsync(42, isAdmin: false, token);

        // Assert
        Assert.Same(everything, asAdmin);
        Assert.Same(mine, asAuthor);
    }

    /// <summary>
    /// A failed async listing degrades to an empty sequence exactly as the blocking twin does.
    /// </summary>
    [Fact]
    public async Task GetAllPostsAsyncReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetAllAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = await BuildService(repo).GetAllPostsAsync(42, true, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The async single-row lookup forwards the identifier and returns the row; a failure is answered
    /// null.
    /// </summary>
    [Fact]
    public async Task GetSinglePostAsyncForwardsIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = ValidPost(9);
        repo.GetSingleAsync(9, Arg.Any<CancellationToken>()).Returns(post);

        // Act
        var result = await BuildService(repo).GetSinglePostAsync(9, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(post, result);
    }

    /// <summary>
    /// A failed async single-row read is answered null, exactly as "no such post" is.
    /// </summary>
    [Fact]
    public async Task GetSinglePostAsyncReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetSingleAsync(9, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = await BuildService(repo).GetSinglePostAsync(9, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// A blank slug never reaches the database on the async path either.
    /// </summary>
    [Fact]
    public async Task GetPostBySlugAsyncRejectsBlankSlugWithoutQuerying()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();

        // Act
        var result = await BuildService(repo).GetPostBySlugAsync("  ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
        await repo.DidNotReceive().GetBySlugAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A real slug is forwarded verbatim and a failed read renders the not-found state.
    /// </summary>
    [Fact]
    public async Task GetPostBySlugAsyncForwardsSlugAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var post = ValidPost(3);
        repo.GetBySlugAsync("my-title", Arg.Any<CancellationToken>()).Returns(post);
        repo.When(r => r.GetBySlugAsync("broken", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var found = await service.GetPostBySlugAsync("my-title", token);
        var failed = await service.GetPostBySlugAsync("broken", token);

        // Assert
        Assert.Same(post, found);
        Assert.Null(failed);
    }

    /// <summary>
    /// The async published listing forwards its paging arguments and degrades to empty on failure.
    /// </summary>
    [Fact]
    public async Task GetPublishedPostsAsyncForwardsPagingAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var page = new[] { ValidPost(1) };
        repo.GetPublishedPostsAsync(25, 50, Arg.Any<CancellationToken>()).Returns(page);
        repo.When(r => r.GetPublishedPostsAsync(10, 0, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.GetPublishedPostsAsync(25, 50, token);
        var bad = await service.GetPublishedPostsAsync(10, 0, token);

        // Assert
        Assert.Same(page, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// A failed async aggregate yields a zero-count carrier rather than null.
    /// </summary>
    [Fact]
    public async Task GetBlogCountsAsyncReturnsZeroCarrierOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetTheCountsAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = await BuildService(repo).GetBlogCountsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.BlogCount);
    }

    /// <summary>
    /// The async featured lookup returns the repository's row and answers null on failure.
    /// </summary>
    [Fact]
    public async Task GetFeaturedPostAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var post = ValidPost(4);
        repo.GetFeaturedPostAsync(Arg.Any<CancellationToken>()).Returns(post);

        // Act
        var found = await BuildService(repo).GetFeaturedPostAsync(token);

        var failing = Substitute.For<IBlogPostRepo>();
        failing.When(r => r.GetFeaturedPostAsync(Arg.Any<CancellationToken>()))
               .Do(_ => throw new InvalidOperationException("boom"));
        var degraded = await BuildService(failing).GetFeaturedPostAsync(token);

        // Assert
        Assert.Same(post, found);
        Assert.Null(degraded);
    }

    /// <summary>
    /// A failed async published count collapses the pager to zero.
    /// </summary>
    [Fact]
    public async Task GetPublishedPostCountAsyncReturnsZeroOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetPublishedPostCountAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = await BuildService(repo).GetPublishedPostCountAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result);
    }

    /// <summary>
    /// The async category listing forwards all three arguments and degrades to empty on failure.
    /// </summary>
    [Fact]
    public async Task GetPostsByCategoryAsyncForwardsArgumentsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var page = new[] { ValidPost(1) };
        repo.GetPostsByCategoryAsync(5, 20, 40, Arg.Any<CancellationToken>()).Returns(page);
        repo.When(r => r.GetPostsByCategoryAsync(6, 20, 40, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.GetPostsByCategoryAsync(5, 20, 40, token);
        var bad = await service.GetPostsByCategoryAsync(6, 20, 40, token);

        // Assert
        Assert.Same(page, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// The async category count is forwarded, and a failure collapses it to zero.
    /// </summary>
    [Fact]
    public async Task GetPostCountByCategoryAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetPostCountByCategoryAsync(5, Arg.Any<CancellationToken>()).Returns(12);
        repo.When(r => r.GetPostCountByCategoryAsync(6, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.GetPostCountByCategoryAsync(5, token);
        var bad = await service.GetPostCountByCategoryAsync(6, token);

        // Assert
        Assert.Equal(12, good);
        Assert.Equal(0, bad);
    }

    /// <summary>
    /// A failed async scheduled-posts read degrades to an empty sequence.
    /// </summary>
    [Fact]
    public async Task GetScheduledPostsAsyncReturnsEmptyOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetScheduledPostsAsync(Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = await BuildService(repo).GetScheduledPostsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The async due-posts read takes its own UTC instant, so every publisher cycle compares against
    /// a fresh clock rather than a caller-supplied one.
    /// </summary>
    [Fact]
    public async Task GetDueScheduledPostsAsyncUsesCurrentUtcInstant()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var before = DateTime.UtcNow;

        // Act
        await BuildService(repo).GetDueScheduledPostsAsync(TestContext.Current.CancellationToken);

        // Assert
        var after = DateTime.UtcNow;
        await repo.Received(1).GetDueScheduledPostsAsync(
            Arg.Is<DateTime>(now => now >= before && now <= after),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A failed async due-posts read degrades to an empty sequence.
    /// </summary>
    [Fact]
    public async Task GetDueScheduledPostsAsyncReturnsEmptyOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.GetDueScheduledPostsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = await BuildService(repo).GetDueScheduledPostsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The async search forwards its query and paging, defaults to ten from zero, and degrades to
    /// empty on failure.
    /// </summary>
    [Fact]
    public async Task SearchPostsAsyncForwardsArgumentsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var hits = new[] { ValidPost(1) };
        repo.SearchPostsAsync("blazor", 10, 0, Arg.Any<CancellationToken>()).Returns(hits);
        repo.When(r => r.SearchPostsAsync("broken", 10, 0, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.SearchPostsAsync("blazor", cancellationToken: token);
        var bad = await service.SearchPostsAsync("broken", cancellationToken: token);

        // Assert
        Assert.Same(hits, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// A failed async search count collapses the results pager to zero.
    /// </summary>
    [Fact]
    public async Task GetSearchResultCountAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSearchResultCountAsync("blazor", Arg.Any<CancellationToken>()).Returns(8);
        repo.When(r => r.GetSearchResultCountAsync("broken", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.GetSearchResultCountAsync("blazor", token);
        var bad = await service.GetSearchResultCountAsync("broken", token);

        // Assert
        Assert.Equal(8, good);
        Assert.Equal(0, bad);
    }

    /// <summary>
    /// The async creation applies exactly the validation its blocking twin does, and returns the same
    /// caller-safe messages.
    /// </summary>
    [Fact]
    public async Task CreatePostAsyncAppliesTheSameValidation()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var noTitle = ValidPost();
        noTitle.Title = " ";
        var noContent = ValidPost();
        noContent.PostContent = "";
        var service = BuildService(repo);

        // Act
        var nullResult = await service.CreatePostAsync(null!, token);
        var titleResult = await service.CreatePostAsync(noTitle, token);
        var contentResult = await service.CreatePostAsync(noContent, token);

        // Assert
        Assert.Equal("Post cannot be null", nullResult.ErrorMessage);
        Assert.Equal("Title is required", titleResult.ErrorMessage);
        Assert.Equal("Content is required", contentResult.ErrorMessage);
        await repo.DidNotReceive().InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async creation derives a slug, resolves a collision with the same ordinal suffix, stamps
    /// the creation date and writes the generated key back onto the caller's object.
    /// </summary>
    [Fact]
    public async Task CreatePostAsyncDerivesUniqueSlugAndAssignsIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.SlugExistsAsync("my-title", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()).Returns(77L);
        var post = ValidPost();
        post.IsDeleted = true;
        var before = DateTime.UtcNow;

        // Act
        var result = await BuildService(repo).CreatePostAsync(post, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("my-title-2", post.Slug);
        Assert.Equal(77, post.PostID);
        Assert.False(post.IsDeleted);
        Assert.InRange(post.CreatedOn, before, DateTime.UtcNow);
    }

    /// <summary>
    /// An unexpected persistence error on the async insert becomes a failed result rather than a
    /// faulted task.
    /// </summary>
    [Fact]
    public async Task CreatePostAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.When(r => r.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("duplicate key"));

        // Act
        var result = await BuildService(repo).CreatePostAsync(ValidPost(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create post: duplicate key", result.ErrorMessage);
    }

    /// <summary>
    /// The async update applies its guards in the documented order and never writes when one fails.
    /// </summary>
    [Fact]
    public async Task UpdatePostAsyncAppliesTheSameGuards()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var noTitle = ValidPost(5);
        noTitle.Title = "";
        var service = BuildService(repo);

        // Act
        var nullResult = await service.UpdatePostAsync(null!, token);
        var idResult = await service.UpdatePostAsync(ValidPost(0), token);
        var titleResult = await service.UpdatePostAsync(noTitle, token);
        var missingResult = await service.UpdatePostAsync(ValidPost(5), token);

        // Assert
        Assert.Equal("Post cannot be null", nullResult.ErrorMessage);
        Assert.Equal("Invalid post ID", idResult.ErrorMessage);
        Assert.Equal("Title is required", titleResult.ErrorMessage);
        Assert.Equal("Post not found", missingResult.ErrorMessage);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async update excludes the row being edited from the uniqueness check, so an unchanged
    /// title keeps its public URL.
    /// </summary>
    [Fact]
    public async Task UpdatePostAsyncExcludesItselfFromSlugCheck()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        var post = ValidPost(5);
        post.Slug = "my-title";
        var before = DateTime.UtcNow;

        // Act
        var result = await BuildService(repo).UpdatePostAsync(post, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("my-title", post.Slug);
        Assert.InRange(post.UpdatedOn, before, DateTime.UtcNow);
        await repo.Received(1).SlugExistsAsync("my-title", 5, Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateAsync(post, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unexpected persistence error on the async update becomes a failed result.
    /// </summary>
    [Fact]
    public async Task UpdatePostAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        repo.When(r => r.UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("deadlock"));

        // Act
        var result = await BuildService(repo).UpdatePostAsync(ValidPost(5), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update post: deadlock", result.ErrorMessage);
    }

    /// <summary>
    /// The async save routes on the key exactly as the blocking twin does — insert without one,
    /// update with one — and refuses a null post.
    /// </summary>
    [Fact]
    public async Task SavePostAsyncRoutesOnIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        var service = BuildService(repo);

        // Act
        var nullResult = await service.SavePostAsync(null!, token);
        await service.SavePostAsync(ValidPost(), token);
        await service.SavePostAsync(ValidPost(5), token);

        // Assert
        Assert.Equal("Post cannot be null", nullResult.ErrorMessage);
        await repo.Received(1).InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async delete refuses a bad identifier, a missing row and an already-deleted row, and
    /// soft-deletes a live one.
    /// </summary>
    [Fact]
    public async Task DeletePostAsyncAppliesTheSameGuards()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var deleted = ValidPost(6);
        deleted.IsDeleted = true;
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(deleted);
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(ValidPost(7));
        var service = BuildService(repo);

        // Act
        var idResult = await service.DeletePostAsync(0, token);
        var missingResult = await service.DeletePostAsync(5, token);
        var alreadyResult = await service.DeletePostAsync(6, token);
        var okResult = await service.DeletePostAsync(7, token);

        // Assert
        Assert.Equal("Invalid post ID", idResult.ErrorMessage);
        Assert.Equal("Post not found", missingResult.ErrorMessage);
        Assert.Equal("Post is already deleted", alreadyResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        await repo.Received(1).SoftDeleteAsync(7, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unexpected persistence error on the async soft delete becomes a failed result.
    /// </summary>
    [Fact]
    public async Task DeletePostAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(ValidPost(7));
        repo.When(r => r.SoftDeleteAsync(7, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = await BuildService(repo).DeletePostAsync(7, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete post: timeout", result.ErrorMessage);
    }

    /// <summary>
    /// The async draft save clears the published flag, keeps the original publication date, and
    /// refuses a null post.
    /// </summary>
    [Fact]
    public async Task SaveDraftAsyncUnpublishesAndKeepsPublicationDate()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        var firstPublished = new DateTime(2020, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var post = ValidPost(5);
        post.Published = true;
        post.PublishedOn = firstPublished;
        var service = BuildService(repo);

        // Act
        var nullResult = await service.SaveDraftAsync(null!, token);
        var result = await service.SaveDraftAsync(post, token);

        // Assert
        Assert.Equal("Post cannot be null", nullResult.ErrorMessage);
        Assert.True(result.IsSuccess);
        Assert.False(post.Published);
        Assert.Equal(firstPublished, post.PublishedOn);
    }

    /// <summary>
    /// The async publish stamps a first publication date, preserves an existing one, and refuses a
    /// null post.
    /// </summary>
    [Fact]
    public async Task PublishPostAsyncStampsOnlyTheFirstPublicationDate()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        var firstPublished = new DateTime(2019, 3, 4, 0, 0, 0, DateTimeKind.Utc);
        var republished = ValidPost(5);
        republished.PublishedOn = firstPublished;
        var fresh = ValidPost();
        var service = BuildService(repo);

        // Act
        var nullResult = await service.PublishPostAsync(null!, token);
        await service.PublishPostAsync(fresh, token);
        await service.PublishPostAsync(republished, token);

        // Assert
        Assert.Equal("Post cannot be null", nullResult.ErrorMessage);
        Assert.True(fresh.Published);
        Assert.NotNull(fresh.PublishedOn);
        Assert.Equal(firstPublished, republished.PublishedOn);
    }

    /// <summary>
    /// The async unpublish refuses a bad identifier, a missing row and an already-withdrawn row, and
    /// clears the flag on a live one.
    /// </summary>
    [Fact]
    public async Task UnpublishPostAsyncAppliesTheSameGuards()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var live = ValidPost(7);
        live.Published = true;
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(ValidPost(6));
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(live);
        var service = BuildService(repo);

        // Act
        var idResult = await service.UnpublishPostAsync(0, token);
        var missingResult = await service.UnpublishPostAsync(5, token);
        var alreadyResult = await service.UnpublishPostAsync(6, token);
        var okResult = await service.UnpublishPostAsync(7, token);

        // Assert
        Assert.Equal("Invalid post ID", idResult.ErrorMessage);
        Assert.Equal("Post not found", missingResult.ErrorMessage);
        Assert.Equal("Post is already unpublished", alreadyResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        Assert.False(live.Published);
    }

    /// <summary>
    /// An unexpected persistence error on the async unpublish becomes a failed result.
    /// </summary>
    [Fact]
    public async Task UnpublishPostAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var live = ValidPost(7);
        live.Published = true;
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(live);
        repo.When(r => r.UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = await BuildService(repo).UnpublishPostAsync(7, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to unpublish post: timeout", result.ErrorMessage);
    }

    /// <summary>
    /// The async one-click publish refuses a bad identifier, a missing row and an already-public row,
    /// and clears any pending schedule when it does publish.
    /// </summary>
    [Fact]
    public async Task QuickPublishAsyncAppliesTheSameGuardsAndClearsSchedule()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var live = ValidPost(6);
        live.Published = true;
        var scheduled = ValidPost(7);
        scheduled.ScheduledPublishOn = DateTime.UtcNow.AddDays(3);
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(live);
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(scheduled);
        var service = BuildService(repo);

        // Act
        var idResult = await service.QuickPublishAsync(0, token);
        var missingResult = await service.QuickPublishAsync(5, token);
        var alreadyResult = await service.QuickPublishAsync(6, token);
        var okResult = await service.QuickPublishAsync(7, token);

        // Assert
        Assert.Equal("Invalid post ID", idResult.ErrorMessage);
        Assert.Equal("Post not found", missingResult.ErrorMessage);
        Assert.Equal("Post is already published", alreadyResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        Assert.True(scheduled.Published);
        Assert.Null(scheduled.ScheduledPublishOn);
    }

    /// <summary>
    /// An unexpected persistence error on the async quick publish becomes a failed result.
    /// </summary>
    [Fact]
    public async Task QuickPublishAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(ValidPost(7));
        repo.When(r => r.UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = await BuildService(repo).QuickPublishAsync(7, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to publish post: timeout", result.ErrorMessage);
    }

    /// <summary>
    /// The async schedule refuses a null post and a past instant, and otherwise stores the instant
    /// while forcing the post unpublished.
    /// </summary>
    [Fact]
    public async Task SchedulePostAsyncValidatesInstantAndUnpublishes()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        var when = DateTime.UtcNow.AddDays(2);
        var post = ValidPost(5);
        post.Published = true;
        var service = BuildService(repo);

        // Act
        var nullResult = await service.SchedulePostAsync(null!, when, token);
        var pastResult = await service.SchedulePostAsync(ValidPost(), DateTime.UtcNow.AddMinutes(-1), token);
        var okResult = await service.SchedulePostAsync(post, when, token);

        // Assert
        Assert.Equal("Post cannot be null", nullResult.ErrorMessage);
        Assert.Equal("Scheduled time must be in the future", pastResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        Assert.Equal(when, post.ScheduledPublishOn);
        Assert.False(post.Published);
    }

    /// <summary>
    /// The async cancellation refuses a bad identifier, a missing row and an unscheduled row, and
    /// clears the schedule on a scheduled one.
    /// </summary>
    [Fact]
    public async Task CancelScheduleAsyncAppliesTheSameGuards()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var token = TestContext.Current.CancellationToken;
        var scheduled = ValidPost(7);
        scheduled.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(ValidPost(6));
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(scheduled);
        var service = BuildService(repo);

        // Act
        var idResult = await service.CancelScheduleAsync(0, token);
        var missingResult = await service.CancelScheduleAsync(5, token);
        var unscheduledResult = await service.CancelScheduleAsync(6, token);
        var okResult = await service.CancelScheduleAsync(7, token);

        // Assert
        Assert.Equal("Invalid post ID", idResult.ErrorMessage);
        Assert.Equal("Post not found", missingResult.ErrorMessage);
        Assert.Equal("Post is not scheduled", unscheduledResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        Assert.Null(scheduled.ScheduledPublishOn);
    }

    /// <summary>
    /// An unexpected persistence error on the async cancellation becomes a failed result.
    /// </summary>
    [Fact]
    public async Task CancelScheduleAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var scheduled = ValidPost(7);
        scheduled.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);
        repo.GetSingleAsync(7, Arg.Any<CancellationToken>()).Returns(scheduled);
        repo.When(r => r.UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("timeout"));

        // Act
        var result = await BuildService(repo).CancelScheduleAsync(7, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to cancel schedule: timeout", result.ErrorMessage);
    }
}
