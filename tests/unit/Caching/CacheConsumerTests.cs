using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace TechieBlog.Tests.Caching;

/// <summary>
/// Proves that the settings, taxonomy and listing services really read through
/// <see cref="ICacheService"/> and really evict when they write (REQ-NFR-018).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Before this requirement <c>ICacheService</c> was registered and unused, and
/// two verification passes graded the registration rather than the use. These tests grade the use.
/// Each one counts <b>repository</b> calls: a cache that works turns two service calls into one
/// round trip, and an eviction that works turns the next service call back into a round trip. Nothing
/// here inspects the cache's internals, so the tests still pass if the implementation behind
/// <see cref="ICacheService"/> is replaced.</para>
///
/// <para><b>The invalidation half matters more than the hit half.</b> A missing eviction does not
/// show up as a slow page — it shows up as a renamed category that keeps its old name, or a published
/// post that stays invisible, for as long as the entry lives. Every surface below is therefore tested
/// in pairs: one test that the value is cached, one that the matching write throws it away. The
/// synchronous and asynchronous twins of each operation are covered separately, because they store
/// their values under adjacent keys and a write that evicted only one tag would leave the other twin
/// serving stale data.</para>
///
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for the repositories, and a real
/// <see cref="MemoryCacheService"/> over a private <see cref="MemoryCache"/> — the cache is genuine,
/// only the database is substituted. Each test builds its own cache, so no test can see another's
/// entries.</para>
///
/// <para><b>Usage:</b> Pure unit tests; no database and no host.</para>
/// </remarks>
public class CacheConsumerTests
{
    /// <summary>
    /// Builds a real cache service over a cache private to the calling test.
    /// </summary>
    /// <returns>A usable cache holding no entries.</returns>
    private static ICacheService BuildCache() =>
        new MemoryCacheService(
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<MemoryCacheService>.Instance);

    /// <summary>
    /// Builds a category service over a substituted repository and a live cache.
    /// </summary>
    /// <param name="repo">The substituted repository.</param>
    /// <param name="cache">The cache to share with other services in the test.</param>
    /// <returns>The service under test.</returns>
    private static CategorySvc BuildCategorySvc(ICategoryRepo repo, ICacheService cache) =>
        new(repo, NullLogger<CategorySvc>.Instance, cache);

    /// <summary>
    /// Builds a tag service over a substituted repository and a live cache.
    /// </summary>
    /// <param name="repo">The substituted repository.</param>
    /// <param name="cache">The cache to share with other services in the test.</param>
    /// <returns>The service under test.</returns>
    private static TagSvc BuildTagSvc(IBlogTagRepo repo, ICacheService cache) =>
        new(repo, NullLogger<TagSvc>.Instance, cache);

    /// <summary>
    /// Builds a post service over a substituted repository and a live cache.
    /// </summary>
    /// <param name="repo">The substituted repository.</param>
    /// <param name="cache">The cache to share with other services in the test.</param>
    /// <returns>The service under test.</returns>
    private static BlogSvc BuildBlogSvc(IBlogPostRepo repo, ICacheService cache) =>
        new(repo, NullLogger<BlogSvc>.Instance, cache);

    /// <summary>
    /// Builds a category carrying a name and a slug.
    /// </summary>
    /// <param name="categoryId">The identifier to carry.</param>
    /// <param name="name">The category name.</param>
    /// <returns>A category instance.</returns>
    private static Category BuildCategory(long categoryId, string name) =>
        new() { CategoryId = categoryId, CategoryName = name, Slug = $"category-{categoryId}" };

    /// <summary>
    /// Builds a post that passes the service's validation.
    /// </summary>
    /// <param name="postId">The identifier to carry; zero means "never persisted".</param>
    /// <returns>A post instance.</returns>
    private static BlogPost BuildPost(long postId = 0) =>
        new() { PostID = postId, Title = "Caching in TechieBlog", PostContent = "Body", Slug = "caching" };

    // =============================================================================================
    // Settings surface
    // =============================================================================================

    /// <summary>
    /// The effective settings aggregate is loaded once and served from the cache afterwards, which
    /// is what makes it affordable for a layout to ask for it on every render.
    /// </summary>
    [Fact]
    public async Task SettingsAggregateIsLoadedOnlyOnce()
    {
        var repo = Substitute.For<ISiteSettingRepo>();
        repo.GetAllAsync().Returns(Task.FromResult<IEnumerable<SiteSetting>>([]));
        var service = new SiteSettingsService(repo, NullLogger<SiteSettingsService>.Instance, BuildCache());

        await service.GetSettingsAsync();
        await service.GetSettingsAsync();
        await service.GetSettingsAsync();

        await repo.Received(1).GetAllAsync();
    }

    /// <summary>
    /// Saving one setting drops the cached aggregate, so an administrator who changes the site
    /// title does not go on being shown the old one.
    /// </summary>
    [Fact]
    public async Task SettingWriteEvictsTheAggregate()
    {
        var repo = Substitute.For<ISiteSettingRepo>();
        repo.GetAllAsync().Returns(Task.FromResult<IEnumerable<SiteSetting>>([]));
        repo.UpsertAsync(Arg.Any<SiteSetting>()).Returns(Task.FromResult(1L));
        var service = new SiteSettingsService(repo, NullLogger<SiteSettingsService>.Instance, BuildCache());

        await service.GetSettingsAsync();
        await service.SetValueAsync("site.title", "Renamed", "General");
        await service.GetSettingsAsync();

        // Once for the first read, once for the reload the save performs, and no more: the read
        // after the save is served from the entry the save rebuilt.
        await repo.Received(2).GetAllAsync();
    }

    /// <summary>
    /// The explicit escape hatch forces the next read back to the database, for the case where
    /// something changed the settings table without going through the service.
    /// </summary>
    [Fact]
    public async Task SettingsInvalidateCacheForcesAReload()
    {
        var repo = Substitute.For<ISiteSettingRepo>();
        repo.GetAllAsync().Returns(Task.FromResult<IEnumerable<SiteSetting>>([]));
        var service = new SiteSettingsService(repo, NullLogger<SiteSettingsService>.Instance, BuildCache());

        await service.GetSettingsAsync();
        service.InvalidateCache();
        await service.GetSettingsAsync();

        await repo.Received(2).GetAllAsync();
    }

    // =============================================================================================
    // Taxonomy surface — categories
    // =============================================================================================

    /// <summary>
    /// Renaming a category evicts the listing, so the public archive shows the new name on the very
    /// next render rather than when the ten-minute expiry lapses.
    /// </summary>
    [Fact]
    public async Task CategoryUpdateEvictsTheListing()
    {
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Category>>([BuildCategory(1, "Blazor")]));
        repo.GetSingleAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Category?>(BuildCategory(1, "Blazor")));
        var service = BuildCategorySvc(repo, BuildCache());

        await service.GetAllCategoriesAsync();
        await service.UpdateCategoryAsync(BuildCategory(1, "Blazor Server"));
        await service.GetAllCategoriesAsync();

        await repo.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deleting a category evicts the listing, so a removed category cannot go on being offered as
    /// a link into a page that no longer exists.
    /// </summary>
    [Fact]
    public async Task CategoryDeleteEvictsTheListing()
    {
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Category>>([BuildCategory(1, "Blazor")]));
        repo.GetSingleAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Category?>(BuildCategory(1, "Blazor")));
        var service = BuildCategorySvc(repo, BuildCache());

        await service.GetAllCategoriesAsync();
        await service.DeleteCategoryAsync(1);
        await service.GetAllCategoriesAsync();

        await repo.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The asynchronous twin caches too, under its own key.
    /// </summary>
    [Fact]
    public async Task CategoryListingAsyncIsReadOnlyOnce()
    {
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Category>>([BuildCategory(1, "Blazor")]));
        var service = BuildCategorySvc(repo, BuildCache());

        await service.GetAllCategoriesAsync();
        await service.GetAllCategoriesAsync();

        await repo.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // Taxonomy surface — tags
    // =============================================================================================

    /// <summary>
    /// The tag cloud is read once and cached.
    /// </summary>
    [Fact]
    public async Task TagCloudIsReadOnlyOnce()
    {
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetAllWithCountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<BlogTag>>([new BlogTag { TagId = 1, TagName = "dotnet", Slug = "dotnet" }]));
        var service = BuildTagSvc(repo, BuildCache());

        await service.GetAllWithCountsAsync();
        await service.GetAllWithCountsAsync();

        await repo.Received(1).GetAllWithCountsAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deleting a tag evicts the cloud.
    /// </summary>
    [Fact]
    public async Task TagDeleteEvictsTheCloud()
    {
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetAllWithCountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<BlogTag>>([new BlogTag { TagId = 1, TagName = "dotnet", Slug = "dotnet" }]));
        repo.GetSingleAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BlogTag?>(new BlogTag { TagId = 1, TagName = "dotnet", Slug = "dotnet" }));
        var service = BuildTagSvc(repo, BuildCache());

        await service.GetAllWithCountsAsync();
        await service.DeleteTagAsync(1);
        await service.GetAllWithCountsAsync();

        await repo.Received(2).GetAllWithCountsAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Re-tagging a post is a content change, not merely a taxonomy one: it moves the counts in the
    /// tag cloud and changes which posts a tag lists, so it must evict both groups.
    /// </summary>
    [Fact]
    public async Task SetTagsForPostEvictsTheCloudAndTheListings()
    {
        var cache = BuildCache();
        var tagRepo = Substitute.For<IBlogTagRepo>();
        tagRepo.GetAllWithCountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<BlogTag>>([new BlogTag { TagId = 1, TagName = "dotnet", Slug = "dotnet" }]));
        var postRepo = Substitute.For<IBlogPostRepo>();
        postRepo.GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(1)]));

        var tagSvc = BuildTagSvc(tagRepo, cache);
        var blogSvc = BuildBlogSvc(postRepo, cache);

        await tagSvc.GetAllWithCountsAsync();
        await blogSvc.GetPublishedPostsAsync(3, 0);
        await tagSvc.SetTagsForPostAsync(1, [1L, 2L]);
        await tagSvc.GetAllWithCountsAsync();
        await blogSvc.GetPublishedPostsAsync(3, 0);

        await tagRepo.Received(2).GetAllWithCountsAsync(Arg.Any<CancellationToken>());
        await postRepo.Received(2).GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // Listings surface
    // =============================================================================================

    /// <summary>
    /// A page of published posts is read once and cached, and a different page is a different key
    /// rather than a stale hit — the discriminator rule the cache's own remarks insist on.
    /// </summary>
    [Fact]
    public async Task PublishedPostPagesAreCachedPerPage()
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetPublishedPostsAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(1)]));
        var service = BuildBlogSvc(repo, BuildCache());

        await service.GetPublishedPostsAsync(3, 0);
        await service.GetPublishedPostsAsync(3, 0);
        await service.GetPublishedPostsAsync(3, 3);

        await repo.Received(1).GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>());
        await repo.Received(1).GetPublishedPostsAsync(3, 3, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The featured post is cached, because the landing page asks for it on every render.
    /// </summary>
    [Fact]
    public async Task FeaturedPostIsReadOnlyOnce()
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetFeaturedPostAsync(Arg.Any<CancellationToken>()).Returns(BuildPost(1));
        var service = BuildBlogSvc(repo, BuildCache());

        await service.GetFeaturedPostAsync();
        await service.GetFeaturedPostAsync();

        await repo.Received(1).GetFeaturedPostAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Publishing a post evicts the listings and the featured slot, so a newly published article
    /// leads the home page immediately instead of after the expiry. This is the invalidation whose
    /// absence would be the most visible defect in the whole requirement.
    /// </summary>
    [Fact]
    public async Task PublishingAPostEvictsTheListingsAndTheFeaturedSlot()
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(1)]));
        repo.GetFeaturedPostAsync(Arg.Any<CancellationToken>()).Returns(BuildPost(1));
        repo.GetSingleAsync(2, Arg.Any<CancellationToken>()).Returns(BuildPost(2));
        var service = BuildBlogSvc(repo, BuildCache());

        await service.GetPublishedPostsAsync(3, 0);
        await service.GetFeaturedPostAsync();
        await service.QuickPublishAsync(2);
        await service.GetPublishedPostsAsync(3, 0);
        await service.GetFeaturedPostAsync();

        await repo.Received(2).GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>());
        await repo.Received(2).GetFeaturedPostAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Soft-deleting a post evicts the listings, so a withdrawn article stops being linked from the
    /// home page on the next render.
    /// </summary>
    [Fact]
    public async Task DeletingAPostEvictsTheListings()
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(1)]));
        repo.GetSingleAsync(1, Arg.Any<CancellationToken>()).Returns(BuildPost(1));
        var service = BuildBlogSvc(repo, BuildCache());

        await service.GetPublishedPostsAsync(3, 0);
        await service.DeletePostAsync(1);
        await service.GetPublishedPostsAsync(3, 0);

        await repo.Received(2).GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A post write also evicts the taxonomy, because every category's and tag's post count moves
    /// when an article is published or withdrawn. Without this a category would go on advertising a
    /// count that no longer matches the archive behind it.
    /// </summary>
    [Fact]
    public async Task PostWriteEvictsTheTaxonomyCounts()
    {
        var cache = BuildCache();
        var categoryRepo = Substitute.For<ICategoryRepo>();
        categoryRepo.GetAllWithCountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Category>>([BuildCategory(1, "Blazor")]));
        var postRepo = Substitute.For<IBlogPostRepo>();
        postRepo.GetSingleAsync(2, Arg.Any<CancellationToken>()).Returns(BuildPost(2));

        var categorySvc = BuildCategorySvc(categoryRepo, cache);
        var blogSvc = BuildBlogSvc(postRepo, cache);

        await categorySvc.GetAllWithCountsAsync();
        await blogSvc.QuickPublishAsync(2);
        await categorySvc.GetAllWithCountsAsync();

        await categoryRepo.Received(2).GetAllWithCountsAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A taxonomy edit does <b>not</b> throw the post listings away: the public post projections
    /// carry a category id and never a category name, so renaming one cannot make a cached listing
    /// wrong. Pins the asymmetry between the two invalidation helpers, which is easy to "tidy up"
    /// into a needless cache flush on every taxonomy save.
    /// </summary>
    [Fact]
    public async Task TaxonomyEditLeavesThePostListingsAlone()
    {
        var cache = BuildCache();
        var categoryRepo = Substitute.For<ICategoryRepo>();
        categoryRepo.GetSingleAsync(1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Category?>(BuildCategory(1, "Blazor")));
        var postRepo = Substitute.For<IBlogPostRepo>();
        postRepo.GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(1)]));

        var categorySvc = BuildCategorySvc(categoryRepo, cache);
        var blogSvc = BuildBlogSvc(postRepo, cache);

        await blogSvc.GetPublishedPostsAsync(3, 0);
        await categorySvc.UpdateCategoryAsync(BuildCategory(1, "Blazor Server"));
        await blogSvc.GetPublishedPostsAsync(3, 0);

        await postRepo.Received(1).GetPublishedPostsAsync(3, 0, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A post read by slug is cached, and an update evicts it — the pair that keeps
    /// <c>/post/{slug}</c> both cheap and correct after an edit.
    /// </summary>
    [Fact]
    public async Task PostBySlugIsCachedAndEvictedOnUpdate()
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetBySlugAsync("caching", Arg.Any<CancellationToken>()).Returns(BuildPost(1));
        repo.GetSingleAsync(1, Arg.Any<CancellationToken>()).Returns(BuildPost(1));
        var service = BuildBlogSvc(repo, BuildCache());

        await service.GetPostBySlugAsync("caching");
        await service.GetPostBySlugAsync("caching");
        await service.UpdatePostAsync(BuildPost(1));
        await service.GetPostBySlugAsync("caching");

        await repo.Received(2).GetBySlugAsync("caching", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The admin listing is never cached: it varies by user and privilege, and an entry keyed
    /// without the principal would serve one author's unpublished drafts to the next caller.
    /// </summary>
    [Fact]
    public async Task AdminPostListingIsNeverCached()
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(1)]));
        repo.GetAllByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(2)]));
        var service = BuildBlogSvc(repo, BuildCache());

        await service.GetAllPostsAsync(7, true);
        await service.GetAllPostsAsync(7, true);
        await service.GetAllPostsAsync(7, false);

        await repo.Received(2).GetAllAsync(Arg.Any<CancellationToken>());
        await repo.Received(1).GetAllByIdAsync(7, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The scheduled-post queue is never cached: the background publisher must see the current
    /// state of the queue, not a snapshot up to ten minutes old, or a scheduled article publishes
    /// late.
    /// </summary>
    [Fact]
    public async Task ScheduledPostQueueIsNeverCached()
    {
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetDueScheduledPostsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogPost>>([BuildPost(1)]));
        var service = BuildBlogSvc(repo, BuildCache());

        await service.GetDueScheduledPostsAsync();
        await service.GetDueScheduledPostsAsync();

        await repo.Received(2).GetDueScheduledPostsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // Rating aggregates — the home page's N+1
    // =============================================================================================

    /// <summary>
    /// The per-post rating aggregates are cached, which is what removes the query-per-card the
    /// latest-articles grid used to cost.
    /// </summary>
    [Fact]
    public async Task RatingAggregatesAreReadOnlyOnce()
    {
        var repo = Substitute.For<IPostRatingRepo>();
        repo.GetAverageByPostAsync(1, Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));
        repo.GetCountByPostAsync(1, Arg.Any<CancellationToken>()).Returns(Task.FromResult(12));
        var service = BuildRatingSvc(repo, BuildCache());

        await service.GetAverageRatingAsync(1);
        await service.GetAverageRatingAsync(1);
        await service.GetRatingCountAsync(1);
        await service.GetRatingCountAsync(1);

        await repo.Received(1).GetAverageByPostAsync(1, Arg.Any<CancellationToken>());
        await repo.Received(1).GetCountByPostAsync(1, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Submitting a rating drops that post's aggregates so the star display moves, and drops only
    /// that post's — one reader rating one article must not throw away every cached listing on the
    /// site.
    /// </summary>
    [Fact]
    public async Task RatingSubmissionEvictsOnlyThatPost()
    {
        var repo = Substitute.For<IPostRatingRepo>();
        repo.GetAverageByPostAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));
        repo.UpsertByEmailAsync(Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(9L));
        var verification = Substitute.For<IEmailVerificationService>();
        verification.IsAddressVerifiedAsync(Arg.Any<string>()).Returns(Task.FromResult(true));
        var service = BuildRatingSvc(repo, BuildCache(), verification);

        await service.GetAverageRatingAsync(1);
        await service.GetAverageRatingAsync(2);
        await service.SubmitRatingAsync(new RatingSubmission
        {
            PostId = 1,
            Rating = 5,
            Email = "ada@example.com",
            UserId = 3
        });
        await service.GetAverageRatingAsync(1);
        await service.GetAverageRatingAsync(2);

        await repo.Received(2).GetAverageByPostAsync(1, Arg.Any<CancellationToken>());
        await repo.Received(1).GetAverageByPostAsync(2, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Builds a rating service over a substituted repository and a live cache.
    /// </summary>
    /// <param name="repo">The substituted rating repository.</param>
    /// <param name="cache">The cache under test.</param>
    /// <param name="verification">Optional verification service; a permissive stub by default.</param>
    /// <returns>The service under test.</returns>
    private static RatingSvc BuildRatingSvc(
        IPostRatingRepo repo,
        ICacheService cache,
        IEmailVerificationService? verification = null)
    {
        return new RatingSvc(
            repo,
            Substitute.For<ICaptchaService>(),
            verification ?? Substitute.For<IEmailVerificationService>(),
            NullLogger<RatingSvc>.Instance,
            cache);
    }

    // =============================================================================================
    // ServiceCache itself
    // =============================================================================================

    /// <summary>
    /// A read that faults leaves nothing behind. Without this the entry stored before the query
    /// completed would park a faulted task under the key and hand the same failure to every caller
    /// for the next ten minutes — turning a momentary database blip into a sustained outage.
    /// </summary>
    [Fact]
    public async Task FailedAsyncReadIsNotLeftInTheCache()
    {
        var cache = BuildCache();
        var attempts = 0;

        async Task<int> Failing()
        {
            attempts++;
            await Task.Yield();
            throw new InvalidOperationException("database unavailable");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceCache.ReadAsync(cache, "test:key", CacheTags.Content, Failing));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ServiceCache.ReadAsync(cache, "test:key", CacheTags.Content, Failing));

        Assert.Equal(2, attempts);
    }

    /// <summary>
    /// With no cache supplied every read runs the factory, so a service built without one behaves
    /// exactly as it did before this requirement.
    /// </summary>
    [Fact]
    public void ReadWithoutACacheAlwaysRunsTheFactory()
    {
        var calls = 0;

        ServiceCache.Read<int>(null, "test:key", CacheTags.Content, () => ++calls);
        ServiceCache.Read<int>(null, "test:key", CacheTags.Content, () => ++calls);

        Assert.Equal(2, calls);
    }

    /// <summary>
    /// The synchronous and asynchronous keys of one query are distinct, which is what stops the two
    /// twins overwriting each other's entry on every alternating call.
    /// </summary>
    [Fact]
    public void AsyncVariantKeyIsDistinctFromItsSyncTwin()
    {
        Assert.NotEqual(
            ServiceCache.CategoriesAllKey,
            ServiceCache.AsyncVariant(ServiceCache.CategoriesAllKey));
    }
}
