using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Service-level tests for REQ-FN-054 — an author's supplied slug survives every collision retry, and
/// an empty generated slug never reaches the database.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>SlugAllocationTests</c> pins the primitives; this suite proves the four
/// services actually route through them. Both halves matter separately, because the defect was not in
/// <c>SlugGenerator</c> at all — it was in sixteen copies of a loop the services each wrote out by
/// hand, and a fixed primitive that nobody called would have changed nothing.</para>
/// <para><b>Why these fail against the old code:</b> the supplied-slug tests arrange three collisions,
/// which under the old loop switched from <c>hand-picked-2</c> to the title-derived <c>my-title-3</c>
/// on the second retry; and the empty-slug tests use a title made only of characters the ASCII
/// allow-list rejects, which the old code persisted as <c>""</c>.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for the repositories, <c>NullLogger</c> — logging
/// is not what these tests are about. No database.</para>
/// </remarks>
public class SlugCollisionRetryTests
{
    /// <summary>A title whose every character the ASCII allow-list discards.</summary>
    private const string UnslugabbleTitle = "日本語のタイトル";

    /// <summary>
    /// A post author who typed their own slug keeps it through every retry: three collisions produce
    /// <c>hand-picked-4</c>, never the title-derived <c>my-title-N</c> the old loop fell back to.
    /// </summary>
    [Fact]
    public void CreatePostKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.SlugExists("hand-picked").Returns(true);
        repo.SlugExists("hand-picked-2").Returns(true);
        repo.SlugExists("hand-picked-3").Returns(true);
        repo.InsertToGetId(Arg.Any<BlogPost>()).Returns(11);
        var post = ValidPost();
        post.Slug = "hand-picked";

        // Act
        var result = new BlogSvc(repo, NullLogger<BlogSvc>.Instance).CreatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-4", post.Slug);
        repo.DidNotReceive().SlugExists(Arg.Is<string>(s => s != null && s.StartsWith("my-title")));
    }

    /// <summary>
    /// The async twin allocates the same slug from the same collisions, so the pair cannot drift.
    /// </summary>
    [Fact]
    public async Task CreatePostAsyncKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.SlugExistsAsync("hand-picked", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.SlugExistsAsync("hand-picked-2", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.SlugExistsAsync("hand-picked-3", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()).Returns(11L);
        var post = ValidPost();
        post.Slug = "hand-picked";

        // Act
        var result = await new BlogSvc(repo, NullLogger<BlogSvc>.Instance)
            .CreatePostAsync(post, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-4", post.Slug);
    }

    /// <summary>
    /// An edit keeps the author's slug through its retries too, and the exclusion of the row being
    /// edited is still applied to every candidate rather than only the first.
    /// </summary>
    [Fact]
    public void UpdatePostKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        repo.SlugExists("hand-picked", 5).Returns(true);
        repo.SlugExists("hand-picked-2", 5).Returns(true);
        var post = ValidPost(5);
        post.Slug = "hand-picked";

        // Act
        var result = new BlogSvc(repo, NullLogger<BlogSvc>.Instance).UpdatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", post.Slug);
        repo.Received(1).SlugExists("hand-picked-3", 5);
    }

    /// <summary>
    /// A category administrator's chosen slug survives its retries as well — the same defect was
    /// copied into all four taxonomy services, so all four are pinned.
    /// </summary>
    [Fact]
    public void CreateCategoryKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.SlugExists("hand-picked").Returns(true);
        repo.SlugExists("hand-picked-2").Returns(true);
        repo.InsertToGetId(Arg.Any<Category>()).Returns(3);

        // Act
        var result = new CategorySvc(repo, NullLogger<CategorySvc>.Instance)
            .CreateCategory(new Category { CategoryName = "Web Development", Slug = "hand-picked" });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", result.Data.Slug);
        repo.DidNotReceive().SlugExists(Arg.Is<string>(s => s != null && s.StartsWith("web-development")));
    }

    /// <summary>
    /// Same rule in the tag taxonomy.
    /// </summary>
    [Fact]
    public void CreateTagKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.SlugExists("hand-picked").Returns(true);
        repo.SlugExists("hand-picked-2").Returns(true);
        repo.InsertToGetId(Arg.Any<BlogTag>()).Returns(4);

        // Act
        var result = new TagSvc(repo, NullLogger<TagSvc>.Instance)
            .CreateTag(new BlogTag { TagName = "Blazor Server", Slug = "hand-picked" });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", result.Data.Slug);
        repo.DidNotReceive().SlugExists(Arg.Is<string>(s => s != null && s.StartsWith("blazor-server")));
    }

    /// <summary>
    /// Same rule for a series.
    /// </summary>
    [Fact]
    public void CreateSeriesKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var seriesRepo = Substitute.For<IBlogSeriesRepo>();
        var postRepo = Substitute.For<IBlogPostRepo>();
        seriesRepo.SlugExists("hand-picked").Returns(true);
        seriesRepo.SlugExists("hand-picked-2").Returns(true);
        seriesRepo.InsertToGetId(Arg.Any<BlogSeries>()).Returns(9);

        // Act
        var result = new SeriesSvc(seriesRepo, postRepo, NullLogger<SeriesSvc>.Instance)
            .CreateSeries(new BlogSeries { Name = "Blazor In Production", Slug = "hand-picked" });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", result.Data.Slug);
        seriesRepo.DidNotReceive().SlugExists(Arg.Is<string>(s => s != null && s.StartsWith("blazor-in-production")));
    }

    /// <summary>
    /// A title the allow-list empties out no longer reaches the insert as an empty slug: the row is
    /// stored under an identifier-based address instead, which is what keeps it reachable by URL.
    /// </summary>
    [Fact]
    public void CreatePostNeverPersistsAnEmptySlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.InsertToGetId(Arg.Any<BlogPost>()).Returns(11);
        var post = ValidPost();
        post.Title = UnslugabbleTitle;

        // Act
        var result = new BlogSvc(repo, NullLogger<BlogSvc>.Instance).CreatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(post.Slug));
        Assert.StartsWith("post-", post.Slug);
        repo.DidNotReceive().InsertToGetId(Arg.Is<BlogPost>(p => p != null && string.IsNullOrWhiteSpace(p.Slug)));
    }

    /// <summary>
    /// The async twin substitutes the same address, so an editor gets the same URL whichever surface
    /// saved the post.
    /// </summary>
    [Fact]
    public async Task CreatePostAsyncNeverPersistsAnEmptySlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.InsertToGetIdAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()).Returns(11L);
        var post = ValidPost();
        post.Title = UnslugabbleTitle;

        // Act
        var result = await new BlogSvc(repo, NullLogger<BlogSvc>.Instance)
            .CreatePostAsync(post, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(post.Slug));
        Assert.StartsWith("post-", post.Slug);
    }

    /// <summary>
    /// An edit of a post whose title was replaced with an unslugabble one gets the readable
    /// <c>post-{id}</c> form, because on an update the identifier is already known.
    /// </summary>
    [Fact]
    public void UpdatePostSubstitutesTheIdentifierWhenTheTitleYieldsNoSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));
        var post = ValidPost(5);
        post.Title = UnslugabbleTitle;
        post.Slug = string.Empty;

        // Act
        var result = new BlogSvc(repo, NullLogger<BlogSvc>.Instance).UpdatePost(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("post-5", post.Slug);
    }

    /// <summary>
    /// The taxonomy services never persist an empty slug either.
    /// </summary>
    [Fact]
    public void CreateCategoryAndCreateTagNeverPersistAnEmptySlug()
    {
        // Arrange
        var categoryRepo = Substitute.For<ICategoryRepo>();
        var tagRepo = Substitute.For<IBlogTagRepo>();
        categoryRepo.InsertToGetId(Arg.Any<Category>()).Returns(3);
        tagRepo.InsertToGetId(Arg.Any<BlogTag>()).Returns(4);

        // Act
        var category = new CategorySvc(categoryRepo, NullLogger<CategorySvc>.Instance)
            .CreateCategory(new Category { CategoryName = UnslugabbleTitle });
        var tag = new TagSvc(tagRepo, NullLogger<TagSvc>.Instance)
            .CreateTag(new BlogTag { TagName = UnslugabbleTitle });

        // Assert
        Assert.StartsWith("category-", category.Data.Slug);
        Assert.StartsWith("tag-", tag.Data.Slug);
        categoryRepo.DidNotReceive().InsertToGetId(Arg.Is<Category>(c => c != null && string.IsNullOrWhiteSpace(c.Slug)));
        tagRepo.DidNotReceive().InsertToGetId(Arg.Is<BlogTag>(t => t != null && string.IsNullOrWhiteSpace(t.Slug)));
    }

    /// <summary>
    /// A series created from an unslugabble name is addressable too.
    /// </summary>
    [Fact]
    public void CreateSeriesNeverPersistsAnEmptySlug()
    {
        // Arrange
        var seriesRepo = Substitute.For<IBlogSeriesRepo>();
        var postRepo = Substitute.For<IBlogPostRepo>();
        seriesRepo.InsertToGetId(Arg.Any<BlogSeries>()).Returns(9);

        // Act
        var result = new SeriesSvc(seriesRepo, postRepo, NullLogger<SeriesSvc>.Instance)
            .CreateSeries(new BlogSeries { Name = UnslugabbleTitle });

        // Assert
        Assert.StartsWith("series-", result.Data.Slug);
        seriesRepo.DidNotReceive().InsertToGetId(Arg.Is<BlogSeries>(s => s != null && string.IsNullOrWhiteSpace(s.Slug)));
    }

    /// <summary>
    /// The editor's get-or-create path is the one that runs on every post save, so an unslugabble tag
    /// name has to resolve to a stable address: the first save inserts <c>tag-{digest}</c> and the
    /// second finds that same row rather than inserting a duplicate.
    /// </summary>
    [Fact]
    public void GetOrCreateTagResolvesAnUnslugabbleNameToAStableSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.InsertToGetId(Arg.Any<BlogTag>()).Returns(4);
        var service = new TagSvc(repo, NullLogger<TagSvc>.Instance);

        // Act — first save inserts, then the row exists and the second save must find it.
        var created = service.GetOrCreateTag(UnslugabbleTitle);
        repo.GetBySlug(created!.Slug).Returns(new BlogTag { TagId = 4, TagName = UnslugabbleTitle, Slug = created.Slug });
        var reused = service.GetOrCreateTag(UnslugabbleTitle);

        // Assert
        Assert.StartsWith("tag-", created.Slug);
        Assert.Equal(created.Slug, reused!.Slug);
        repo.Received(1).InsertToGetId(Arg.Any<BlogTag>());
    }

    /// <summary>
    /// Builds a post that passes every validation rule, so a test can isolate the slug rule it varies.
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
}
