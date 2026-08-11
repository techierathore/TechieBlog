using BlogEngine.Services;
using BlogModels;
using BlogModels.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// Proves that the seven async twins added to <see cref="CommentSvc"/> and <see cref="RatingSvc"/>
/// by REQ-NFR-026 stage 3 behave exactly as the blocking members they replace, and that the cached
/// ones are invalidated by the same write paths.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> A twin that quietly differs from its original is worse than no twin. It
/// compiles, it renders, and the divergence only surfaces as two surfaces of the same site
/// disagreeing — a moderation grid that refuses what the detail page allowed, or a home page star
/// count that does not match the post page. Reflection cannot see a method body and the compiler
/// has nothing to check, so agreement has to be asserted behaviourally, one property at a time.</para>
///
/// <para><b>What is checked, per twin:</b> that it reaches the SAME repository query as its
/// original (a twin that reached <c>GetAllAsync</c> where its original reads <c>GetAllById</c>
/// would return every comment on the site to one article's thread and still look right in a
/// screenshot); that it applies the same guards and returns the same VERBATIM <c>Result</c>
/// strings; and that it degrades the same way — empty sequence, zeroed statistics — when the
/// repository throws.</para>
///
/// <para><b>What is checked for the cached twins:</b> that the value really is cached; that the
/// rating write paths evict it; and — the property the whole adjacent-key convention exists for —
/// that a twin and its original do NOT evict each other. Sharing one key would store a
/// <c>T</c> under it from one path and a <c>Task&lt;T&gt;</c> from the other, and
/// <see cref="ICacheService.GetOrCreate{T}"/> reads a type mismatch as a miss, so alternating
/// calls would turn the cache into a permanent miss while looking perfectly healthy.</para>
///
/// <para><b>Dependencies:</b> xUnit, NSubstitute for the repositories, and a real
/// <see cref="MemoryCacheService"/> over a private <see cref="MemoryCache"/>. No database and no
/// host. Note the trap <c>SubstituteBridgeTrapTests</c> documents: a substitute intercepts a
/// default interface implementation rather than falling through to it, so both twins of every
/// repository member a test relies on are stubbed explicitly.</para>
///
/// <para><b>Usage:</b> Run with the rest of the suite. A failure means one path of a pair has
/// drifted from the other.</para>
/// </remarks>
public class AsyncTwinAgreementTests
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
    /// Builds a comment service over a substituted repository.
    /// </summary>
    /// <param name="commentRepo">Receives the substitute the service was built on.</param>
    /// <returns>The service under test.</returns>
    private static CommentSvc BuildCommentSvc(out IBlogCommentRepo commentRepo)
    {
        commentRepo = Substitute.For<IBlogCommentRepo>();
        return new CommentSvc(
            commentRepo,
            Substitute.For<ICaptchaService>(),
            Substitute.For<ICommentSpamGuard>(),
            Substitute.For<IEmailVerificationService>(),
            Substitute.For<ISiteSettingsService>(),
            NullLogger<CommentSvc>.Instance);
    }

    /// <summary>
    /// Builds a rating service over a substituted repository and an optional cache.
    /// </summary>
    /// <param name="ratingRepo">Receives the substitute the service was built on.</param>
    /// <param name="cache">The cache to share, or null for an uncached service.</param>
    /// <param name="verification">Optional verification service; a permissive stub by default.</param>
    /// <returns>The service under test.</returns>
    private static RatingSvc BuildRatingSvc(
        out IPostRatingRepo ratingRepo,
        ICacheService? cache = null,
        IEmailVerificationService? verification = null)
    {
        ratingRepo = Substitute.For<IPostRatingRepo>();
        return new RatingSvc(
            ratingRepo,
            Substitute.For<ICaptchaService>(),
            verification ?? Substitute.For<IEmailVerificationService>(),
            NullLogger<RatingSvc>.Instance,
            cache);
    }

    /// <summary>
    /// Builds an approved, email-confirmed comment.
    /// </summary>
    /// <param name="commentId">The identifier to carry.</param>
    /// <param name="postId">The post the comment belongs to.</param>
    /// <returns>A comment instance.</returns>
    private static BlogComment BuildComment(long commentId, long postId = 7) =>
        new()
        {
            CommentID = commentId,
            PostID = postId,
            GivenBy = "Ada",
            Email = "ada@example.com",
            Comment = "Good article.",
            IsEmailVerified = true,
            ModerationStatus = CommentModerationStatus.Approved,
            Published = true
        };

    // =============================================================================================
    // CommentSvc.GetCommentsByPostIdAsync
    // =============================================================================================

    /// <summary>
    /// The thread twin reads the post-scoped query, not the whole table, and returns the same rows
    /// its blocking original returns.
    /// </summary>
    [Fact]
    public async Task CommentThreadTwinReadsTheSamePostScopedQuery()
    {
        var service = BuildCommentSvc(out var repo);
        var rows = new List<BlogComment> { BuildComment(1), BuildComment(2) };
        repo.GetAllById(7).Returns(rows);
        repo.GetAllByIdAsync(7, Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogComment>>(rows));

        var fromSync = service.GetCommentsByPostId(7).ToList();
        var fromAsync = (await service.GetCommentsByPostIdAsync(7)).ToList();

        Assert.Equal(fromSync.Select(c => c.CommentID), fromAsync.Select(c => c.CommentID));
        await repo.Received(1).GetAllByIdAsync(7, Arg.Any<CancellationToken>());
        await repo.DidNotReceive().GetAllAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Both twins coalesce a null repository answer to an empty thread rather than handing the page
    /// a null to dereference.
    /// </summary>
    [Fact]
    public async Task CommentThreadTwinsBothCoalesceNullToEmpty()
    {
        var service = BuildCommentSvc(out var repo);
        repo.GetAllById(7).Returns((IEnumerable<BlogComment>?)null);
        repo.GetAllByIdAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<BlogComment>>(null!));

        Assert.Empty(service.GetCommentsByPostId(7));
        Assert.Empty(await service.GetCommentsByPostIdAsync(7));
    }

    /// <summary>
    /// A repository failure is swallowed on both paths, so a broken comment table renders an empty
    /// thread rather than taking the article down.
    /// </summary>
    [Fact]
    public async Task CommentThreadTwinsBothSwallowRepositoryFailure()
    {
        var service = BuildCommentSvc(out var repo);
        repo.GetAllById(7).Throws(new InvalidOperationException("database unavailable"));
        repo.GetAllByIdAsync(7, Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<BlogComment>>>(_ => throw new InvalidOperationException("database unavailable"));

        Assert.Empty(service.GetCommentsByPostId(7));
        Assert.Empty(await service.GetCommentsByPostIdAsync(7));
    }

    // =============================================================================================
    // CommentSvc.GetAllCommentsAsync
    // =============================================================================================

    /// <summary>
    /// The administrative grid twin reads every row, in every state, exactly as its original does.
    /// </summary>
    [Fact]
    public async Task AllCommentsTwinReadsTheSameQuery()
    {
        var service = BuildCommentSvc(out var repo);
        var rows = new List<BlogComment> { BuildComment(1), BuildComment(2), BuildComment(3) };
        repo.GetAll().Returns(rows);
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IEnumerable<BlogComment>>(rows));

        var fromSync = service.GetAllComments().ToList();
        var fromAsync = (await service.GetAllCommentsAsync()).ToList();

        Assert.Equal(fromSync.Select(c => c.CommentID), fromAsync.Select(c => c.CommentID));
        await repo.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Both twins degrade to an empty grid when the read fails.
    /// </summary>
    [Fact]
    public async Task AllCommentsTwinsBothSwallowRepositoryFailure()
    {
        var service = BuildCommentSvc(out var repo);
        repo.GetAll().Throws(new InvalidOperationException("database unavailable"));
        repo.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IEnumerable<BlogComment>>>(_ => throw new InvalidOperationException("database unavailable"));

        Assert.Empty(service.GetAllComments());
        Assert.Empty(await service.GetAllCommentsAsync());
    }

    // =============================================================================================
    // CommentSvc.ApproveCommentAsync
    // =============================================================================================

    /// <summary>
    /// The approval twin refuses a comment whose address was never confirmed, with the same wording
    /// its original uses. This is the last line of defence behind "an unconfirmed comment never
    /// appears publicly", and a twin that dropped it would re-open the hole through the async path
    /// alone while the blocking member still guarded it.
    /// </summary>
    [Fact]
    public async Task ApprovalTwinsBothRefuseAnUnconfirmedAddress()
    {
        var service = BuildCommentSvc(out var repo);
        var unconfirmed = BuildComment(5);
        unconfirmed.IsEmailVerified = false;
        repo.GetSingle(5).Returns(unconfirmed);
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BlogComment?>(unconfirmed));

        var fromSync = service.ApproveComment(5);
        var fromAsync = await service.ApproveCommentAsync(5);

        Assert.True(fromSync.IsFailure);
        Assert.True(fromAsync.IsFailure);
        Assert.Equal(fromSync.ErrorMessage, fromAsync.ErrorMessage);
        await repo.DidNotReceive().ApproveBlogCommentAsync(5, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Both approval twins reject a non-positive identifier and an unknown one with the same
    /// messages, before and after the load respectively.
    /// </summary>
    [Fact]
    public async Task ApprovalTwinsShareTheirGuardMessages()
    {
        var service = BuildCommentSvc(out var repo);
        repo.GetSingle(404).Returns((BlogComment?)null);
        repo.GetSingleAsync(404, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BlogComment?>(null));

        Assert.Equal(service.ApproveComment(0).ErrorMessage, (await service.ApproveCommentAsync(0)).ErrorMessage);
        Assert.Equal(service.ApproveComment(404).ErrorMessage, (await service.ApproveCommentAsync(404)).ErrorMessage);
    }

    /// <summary>
    /// A confirmed comment is approved through the repository's async member, and the twin reports
    /// success exactly as its original does.
    /// </summary>
    [Fact]
    public async Task ApprovalTwinApprovesThroughTheAsyncRepositoryMember()
    {
        var service = BuildCommentSvc(out var repo);
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BlogComment?>(BuildComment(5)));

        var result = await service.ApproveCommentAsync(5);

        Assert.True(result.IsSuccess);
        await repo.Received(1).ApproveBlogCommentAsync(5, Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // CommentSvc.DeleteCommentAsync
    // =============================================================================================

    /// <summary>
    /// Both delete twins keep the existence check, so the grid can still tell "removed" from
    /// "was never there", and report it with the same wording.
    /// </summary>
    [Fact]
    public async Task DeleteTwinsShareTheirGuardMessages()
    {
        var service = BuildCommentSvc(out var repo);
        repo.GetSingle(404).Returns((BlogComment?)null);
        repo.GetSingleAsync(404, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BlogComment?>(null));

        Assert.Equal(service.DeleteComment(0).ErrorMessage, (await service.DeleteCommentAsync(0)).ErrorMessage);
        Assert.Equal(service.DeleteComment(404).ErrorMessage, (await service.DeleteCommentAsync(404)).ErrorMessage);
        await repo.DidNotReceive().DeleteAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An existing comment is removed through the repository's async member.
    /// </summary>
    [Fact]
    public async Task DeleteTwinRemovesThroughTheAsyncRepositoryMember()
    {
        var service = BuildCommentSvc(out var repo);
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(Task.FromResult<BlogComment?>(BuildComment(5)));

        var result = await service.DeleteCommentAsync(5);

        Assert.True(result.IsSuccess);
        await repo.Received(1).DeleteAsync(5, Arg.Any<CancellationToken>());
    }

    // =============================================================================================
    // RatingSvc — agreement
    // =============================================================================================

    /// <summary>
    /// The three rating twins return exactly what their blocking originals return, reading through
    /// the async repository members. These two are the home page's figures, so a divergence here
    /// shows up as the article grid and the post page disagreeing about the same article.
    /// </summary>
    [Fact]
    public async Task RatingTwinsReturnTheSameFiguresAsTheirOriginals()
    {
        var service = BuildRatingSvc(out var repo);
        repo.GetAverageByPost(3).Returns(4.5);
        repo.GetCountByPost(3).Returns(12);
        repo.GetStatsByPost(3).Returns(new PostRatingStats { AverageRating = 4.5, RatingCount = 12 });
        repo.GetAverageByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));
        repo.GetCountByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(12));
        repo.GetStatsByPostAsync(3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostRatingStats { AverageRating = 4.5, RatingCount = 12 }));

        Assert.Equal(service.GetAverageRating(3), await service.GetAverageRatingAsync(3));
        Assert.Equal(service.GetRatingCount(3), await service.GetRatingCountAsync(3));
        Assert.Equal(service.GetPostRatingStats(3).AverageRating, (await service.GetPostRatingStatsAsync(3)).AverageRating);
        Assert.Equal(service.GetPostRatingStats(3).RatingCount, (await service.GetPostRatingStatsAsync(3)).RatingCount);
    }

    /// <summary>
    /// A failed rating read degrades to the same neutral figures on both paths — an unrated widget
    /// rather than a broken article.
    /// </summary>
    [Fact]
    public async Task RatingTwinsBothDegradeToZeroOnFailure()
    {
        var service = BuildRatingSvc(out var repo);
        repo.GetAverageByPost(3).Throws(new InvalidOperationException("database unavailable"));
        repo.GetCountByPost(3).Throws(new InvalidOperationException("database unavailable"));
        repo.GetStatsByPost(3).Throws(new InvalidOperationException("database unavailable"));
        repo.GetAverageByPostAsync(3, Arg.Any<CancellationToken>())
            .Returns<Task<double>>(_ => throw new InvalidOperationException("database unavailable"));
        repo.GetCountByPostAsync(3, Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("database unavailable"));
        repo.GetStatsByPostAsync(3, Arg.Any<CancellationToken>())
            .Returns<Task<PostRatingStats>>(_ => throw new InvalidOperationException("database unavailable"));

        Assert.Equal(service.GetAverageRating(3), await service.GetAverageRatingAsync(3));
        Assert.Equal(service.GetRatingCount(3), await service.GetRatingCountAsync(3));
        Assert.Equal(0, (await service.GetPostRatingStatsAsync(3)).RatingCount);
    }

    // =============================================================================================
    // RatingSvc — caching and invalidation
    // =============================================================================================

    /// <summary>
    /// Each rating twin is cached, so the latest-articles grid pays for one query per figure rather
    /// than one per card per render.
    /// </summary>
    [Fact]
    public async Task RatingTwinsAreCached()
    {
        var service = BuildRatingSvc(out var repo, BuildCache());
        repo.GetAverageByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));
        repo.GetCountByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(12));
        repo.GetStatsByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new PostRatingStats()));

        await service.GetAverageRatingAsync(3);
        await service.GetAverageRatingAsync(3);
        await service.GetRatingCountAsync(3);
        await service.GetRatingCountAsync(3);
        await service.GetPostRatingStatsAsync(3);
        await service.GetPostRatingStatsAsync(3);

        await repo.Received(1).GetAverageByPostAsync(3, Arg.Any<CancellationToken>());
        await repo.Received(1).GetCountByPostAsync(3, Arg.Any<CancellationToken>());
        await repo.Received(1).GetStatsByPostAsync(3, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Submitting a rating evicts the cached async figures for that post. A twin that is cached but
    /// not invalidated is the worst of both worlds: the home page would go on showing the old star
    /// count for ten minutes while the post page, reading the evicted synchronous key, showed the
    /// new one.
    /// </summary>
    [Fact]
    public async Task RatingSubmissionEvictsTheAsyncTwins()
    {
        var verification = Substitute.For<IEmailVerificationService>();
        verification.IsAddressVerifiedAsync(Arg.Any<string>()).Returns(Task.FromResult(true));
        var service = BuildRatingSvc(out var repo, BuildCache(), verification);
        repo.GetAverageByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));
        repo.GetCountByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(12));
        repo.GetStatsByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(new PostRatingStats()));
        repo.UpsertByEmailAsync(
                Arg.Any<long>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<long?>(),
                Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(9L));

        await service.GetAverageRatingAsync(3);
        await service.GetRatingCountAsync(3);
        await service.GetPostRatingStatsAsync(3);

        await service.SubmitRatingAsync(new RatingSubmission
        {
            PostId = 3,
            Rating = 5,
            Email = "ada@example.com",
            UserId = 3
        });

        await service.GetAverageRatingAsync(3);
        await service.GetRatingCountAsync(3);
        await service.GetPostRatingStatsAsync(3);

        await repo.Received(2).GetAverageByPostAsync(3, Arg.Any<CancellationToken>());
        await repo.Received(2).GetCountByPostAsync(3, Arg.Any<CancellationToken>());
        await repo.Received(2).GetStatsByPostAsync(3, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Withdrawing a rating evicts the cached async figures too, so a removed score stops counting
    /// on every surface at once.
    /// </summary>
    [Fact]
    public async Task RatingRemovalEvictsTheAsyncTwins()
    {
        var service = BuildRatingSvc(out var repo, BuildCache());
        repo.GetAverageByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));
        repo.DeleteByPostAndEmailAsync(3, "ada@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await service.GetAverageRatingAsync(3);
        await service.RemoveRatingAsync(3, "ada@example.com");
        await service.GetAverageRatingAsync(3);

        await repo.Received(2).GetAverageByPostAsync(3, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An eviction for one post leaves another post's async figures alone, so one reader rating one
    /// article does not cost every cached figure on the site.
    /// </summary>
    [Fact]
    public async Task RatingEvictionIsScopedToOnePost()
    {
        var service = BuildRatingSvc(out var repo, BuildCache());
        repo.GetAverageByPostAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));
        repo.DeleteByPostAndEmailAsync(3, "ada@example.com", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await service.GetAverageRatingAsync(3);
        await service.GetAverageRatingAsync(4);
        await service.RemoveRatingAsync(3, "ada@example.com");
        await service.GetAverageRatingAsync(3);
        await service.GetAverageRatingAsync(4);

        await repo.Received(2).GetAverageByPostAsync(3, Arg.Any<CancellationToken>());
        await repo.Received(1).GetAverageByPostAsync(4, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Alternating between a twin and its original does not turn the cache into a permanent miss.
    /// One stores a value and the other a task, so a shared key would be a type mismatch —
    /// <see cref="ICacheService.GetOrCreate{T}"/> reads that as a miss and overwrites, so each call
    /// would re-query while the cache looked perfectly healthy. The adjacent keys are what stop it.
    /// </summary>
    [Fact]
    public async Task TwinAndOriginalDoNotEvictEachOther()
    {
        var service = BuildRatingSvc(out var repo, BuildCache());
        repo.GetAverageByPost(3).Returns(4.5);
        repo.GetAverageByPostAsync(3, Arg.Any<CancellationToken>()).Returns(Task.FromResult(4.5));

        service.GetAverageRating(3);
        await service.GetAverageRatingAsync(3);
        service.GetAverageRating(3);
        await service.GetAverageRatingAsync(3);

        repo.Received(1).GetAverageByPost(3);
        await repo.Received(1).GetAverageByPostAsync(3, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <see cref="ServiceCache.InvalidateRatings"/> drops both members of every pair. Asserted
    /// against the key vocabulary directly, because this is the one method a future cached twin
    /// has to be added to and a miss here is silent everywhere else.
    /// </summary>
    [Fact]
    public void InvalidateRatingsDropsBothKeysOfEveryPair()
    {
        var cache = BuildCache();
        var keys = ServiceCache.RatingKeys(3)
            .Concat(ServiceCache.RatingKeys(3).Select(ServiceCache.AsyncVariant))
            .ToList();

        foreach (var key in keys)
        {
            cache.GetOrCreate(key, CacheTags.Content, () => "cached");
        }

        ServiceCache.InvalidateRatings(cache, 3);

        foreach (var key in keys)
        {
            var reloads = 0;
            cache.GetOrCreate(key, CacheTags.Content, () => { reloads++; return "fresh"; });
            Assert.Equal(1, reloads);
        }
    }

    /// <summary>
    /// Every rating key has an async variant distinct from it, so no pair can accidentally collide.
    /// </summary>
    [Fact]
    public void EveryRatingKeyHasADistinctAsyncVariant()
    {
        var keys = ServiceCache.RatingKeys(3);

        Assert.Equal(3, keys.Count);
        Assert.Equal(3, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(keys, key => key == ServiceCache.AsyncVariant(key));
    }
}
