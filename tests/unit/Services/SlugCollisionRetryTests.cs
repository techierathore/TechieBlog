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
        await repo.DidNotReceive().SlugExistsAsync(Arg.Is<string>(s => s != null && s.StartsWith("my-title")), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An edit keeps the author's slug through its retries too, and the exclusion of the row being
    /// edited is still applied to every candidate rather than only the first.
    /// </summary>
    [Fact]
    public async Task UpdatePostKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        repo.SlugExistsAsync("hand-picked", 5, Arg.Any<CancellationToken>()).Returns(true);
        repo.SlugExistsAsync("hand-picked-2", 5, Arg.Any<CancellationToken>()).Returns(true);
        var post = ValidPost(5);
        post.Slug = "hand-picked";

        // Act
        var result = await new BlogSvc(repo, NullLogger<BlogSvc>.Instance).UpdatePostAsync(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", post.Slug);
        await repo.Received(1).SlugExistsAsync("hand-picked-3", 5, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A category administrator's chosen slug survives its retries as well — the same defect was
    /// copied into all four taxonomy services, so all four are pinned.
    /// </summary>
    [Fact]
    public async Task CreateCategoryKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.SlugExistsAsync("hand-picked", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.SlugExistsAsync("hand-picked-2", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.InsertToGetIdAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>()).Returns(3);

        // Act
        var result = await new CategorySvc(repo, NullLogger<CategorySvc>.Instance)
            .CreateCategoryAsync(new Category { CategoryName = "Web Development", Slug = "hand-picked" });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", result.Data.Slug);
        await repo.DidNotReceive().SlugExistsAsync(Arg.Is<string>(s => s != null && s.StartsWith("web-development")), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Same rule in the tag taxonomy.
    /// </summary>
    [Fact]
    public async Task CreateTagKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.SlugExistsAsync("hand-picked", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.SlugExistsAsync("hand-picked-2", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>()).Returns(4);

        // Act
        var result = await new TagSvc(repo, NullLogger<TagSvc>.Instance)
            .CreateTagAsync(new BlogTag { TagName = "Blazor Server", Slug = "hand-picked" });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", result.Data.Slug);
        await repo.DidNotReceive().SlugExistsAsync(Arg.Is<string>(s => s != null && s.StartsWith("blazor-server")), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Same rule for a series.
    /// </summary>
    [Fact]
    public async Task CreateSeriesKeepsTheSuppliedSlugAcrossEveryRetry()
    {
        // Arrange
        var seriesRepo = Substitute.For<IBlogSeriesRepo>();
        var postRepo = Substitute.For<IBlogPostRepo>();
        seriesRepo.SlugExistsAsync("hand-picked", 0, Arg.Any<CancellationToken>()).Returns(true);
        seriesRepo.SlugExistsAsync("hand-picked-2", 0, Arg.Any<CancellationToken>()).Returns(true);
        seriesRepo.InsertToGetIdAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>()).Returns(9L);

        // Act
        var result = await new SeriesSvc(seriesRepo, postRepo, NullLogger<SeriesSvc>.Instance)
            .CreateSeriesAsync(new BlogSeries { Name = "Blazor In Production", Slug = "hand-picked" });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("hand-picked-3", result.Data.Slug);
        await seriesRepo.DidNotReceive().SlugExistsAsync(
            Arg.Is<string>(s => s != null && s.StartsWith("blazor-in-production")), Arg.Any<long>(), Arg.Any<CancellationToken>());
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
        await repo.DidNotReceive().InsertToGetIdAsync(Arg.Is<BlogPost>(p => p != null && string.IsNullOrWhiteSpace(p.Slug)), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An edit of a post whose title was replaced with an unslugabble one gets the readable
    /// <c>post-{id}</c> form, because on an update the identifier is already known.
    /// </summary>
    [Fact]
    public async Task UpdatePostSubstitutesTheIdentifierWhenTheTitleYieldsNoSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));
        var post = ValidPost(5);
        post.Title = UnslugabbleTitle;
        post.Slug = string.Empty;

        // Act
        var result = await new BlogSvc(repo, NullLogger<BlogSvc>.Instance).UpdatePostAsync(post);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("post-5", post.Slug);
    }

    /// <summary>
    /// The taxonomy services never persist an empty slug either.
    /// </summary>
    [Fact]
    public async Task CreateCategoryAndCreateTagNeverPersistAnEmptySlug()
    {
        // Arrange
        var categoryRepo = Substitute.For<ICategoryRepo>();
        var tagRepo = Substitute.For<IBlogTagRepo>();
        categoryRepo.InsertToGetIdAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>()).Returns(3);
        tagRepo.InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>()).Returns(4);

        // Act
        var category = await new CategorySvc(categoryRepo, NullLogger<CategorySvc>.Instance)
            .CreateCategoryAsync(new Category { CategoryName = UnslugabbleTitle });
        var tag = await new TagSvc(tagRepo, NullLogger<TagSvc>.Instance)
            .CreateTagAsync(new BlogTag { TagName = UnslugabbleTitle });

        // Assert
        Assert.StartsWith("category-", category.Data.Slug);
        Assert.StartsWith("tag-", tag.Data.Slug);
        await categoryRepo.DidNotReceive().InsertToGetIdAsync(Arg.Is<Category>(c => c != null && string.IsNullOrWhiteSpace(c.Slug)), Arg.Any<CancellationToken>());
        await tagRepo.DidNotReceive().InsertToGetIdAsync(Arg.Is<BlogTag>(t => t != null && string.IsNullOrWhiteSpace(t.Slug)), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A series created from an unslugabble name is addressable too.
    /// </summary>
    [Fact]
    public async Task CreateSeriesNeverPersistsAnEmptySlug()
    {
        // Arrange
        var seriesRepo = Substitute.For<IBlogSeriesRepo>();
        var postRepo = Substitute.For<IBlogPostRepo>();
        seriesRepo.InsertToGetIdAsync(Arg.Any<BlogSeries>(), Arg.Any<CancellationToken>()).Returns(9L);

        // Act
        var result = await new SeriesSvc(seriesRepo, postRepo, NullLogger<SeriesSvc>.Instance)
            .CreateSeriesAsync(new BlogSeries { Name = UnslugabbleTitle });

        // Assert
        Assert.StartsWith("series-", result.Data.Slug);
        await seriesRepo.DidNotReceive().InsertToGetIdAsync(
            Arg.Is<BlogSeries>(s => s != null && string.IsNullOrWhiteSpace(s.Slug)), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The editor's get-or-create path is the one that runs on every post save, so an unslugabble tag
    /// name has to resolve to a stable address: the first save inserts <c>tag-{digest}</c> and the
    /// second finds that same row rather than inserting a duplicate.
    /// </summary>
    [Fact]
    public async Task GetOrCreateTagResolvesAnUnslugabbleNameToAStableSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>()).Returns(4);
        var service = new TagSvc(repo, NullLogger<TagSvc>.Instance);

        // Act — first save inserts, then the row exists and the second save must find it.
        var created = await service.GetOrCreateTagAsync(UnslugabbleTitle);
        repo.GetBySlugAsync(created!.Slug, Arg.Any<CancellationToken>()).Returns(new BlogTag { TagId = 4, TagName = UnslugabbleTitle, Slug = created.Slug });
        var reused = await service.GetOrCreateTagAsync(UnslugabbleTitle);

        // Assert
        Assert.StartsWith("tag-", created.Slug);
        Assert.Equal(created.Slug, reused!.Slug);
        await repo.Received(1).InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>());
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
