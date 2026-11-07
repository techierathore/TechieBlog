using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechieBlog.Tests.Dashboard;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Tests for REQ-FN-055 — a soft-deleted post cannot be moved back into the published state by any
/// transition, and the transitions log a persistence failure.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>DeletePost</c> checked <c>IsDeleted</c>; <c>QuickPublish</c>,
/// <c>UnpublishPost</c> and <c>CancelSchedule</c> did not. Because a soft delete only sets
/// <c>IsDeleted</c> and leaves <c>Published</c> untouched, a deleted post could be pushed live again
/// from the admin grid and would then be served to anonymous visitors. Every test here asserts on the
/// repository call that would have written the resurrection, not merely on the returned message — a
/// refusal that still called <c>Update</c> would be no fix at all.</para>
/// <para><b>Why these fail against the old code:</b> each arranges a stored post with
/// <c>IsDeleted = true</c> and asserts the transition is refused; the old code succeeded.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="IBlogPostRepo"/>,
/// <see cref="RecordingLogger{T}"/> for the logging tests. No database.</para>
/// </remarks>
public class PublishStateDeletedPostTests
{
    /// <summary>
    /// The async twin refuses identically — a divergence here would leave the defect reachable from
    /// whichever surface had migrated.
    /// </summary>
    [Fact]
    public async Task QuickPublishAsyncRefusesADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(DeletedPost(5));

        // Act
        var result = await BuildService(repo).QuickPublishAsync(5, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted and cannot be published", result.ErrorMessage);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A soft-deleted post that was published before it was deleted is refused rather than reported as
    /// "already published" — the deleted state is the honest reason and is checked first.
    /// </summary>
    [Fact]
    public async Task QuickPublishReportsDeletionRatherThanAlreadyPublished()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);
        post.Published = true;
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(post);

        // Act
        var result = await BuildService(repo).QuickPublishAsync(5);

        // Assert
        Assert.Equal("Post is deleted and cannot be published", result.ErrorMessage);
    }

    /// <summary>
    /// The async unpublish twin refuses identically.
    /// </summary>
    [Fact]
    public async Task UnpublishPostAsyncRefusesADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);
        post.Published = true;
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(post);

        // Act
        var result = await BuildService(repo).UnpublishPostAsync(5, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted", result.ErrorMessage);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async cancel twin refuses identically.
    /// </summary>
    [Fact]
    public async Task CancelScheduleAsyncRefusesADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);
        post.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(post);

        // Act
        var result = await BuildService(repo).CancelScheduleAsync(5, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted", result.ErrorMessage);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async object-carrying route refuses identically.
    /// </summary>
    [Fact]
    public async Task PublishPostAsyncRefusesADeletedPostWithoutMutatingIt()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);

        // Act
        var result = await BuildService(repo).PublishPostAsync(post, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.False(post.Published);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The stored row is what decides, not the object handed in. A caller holding a stale post that
    /// still says <c>IsDeleted = false</c> cannot republish a row that has since been deleted, because
    /// <c>UpdatePost</c> re-checks against the database before writing.
    /// </summary>
    [Fact]
    public async Task UpdatePostRefusesToPublishARowDeletedInAnotherTab()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(DeletedPost(5));
        var stale = ValidPost(5);
        stale.IsDeleted = false;
        stale.Published = true;

        // Act
        var result = await BuildService(repo).UpdatePostAsync(stale);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted and cannot be published", result.ErrorMessage);
        await repo.DidNotReceive().UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The guard is aimed at publication, not at every write: a deleted post can still be saved as a
    /// draft, which is what leaves "restore, then republish" possible rather than freezing the row.
    /// </summary>
    [Fact]
    public async Task UpdatePostStillAllowsADraftSaveOfADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(DeletedPost(5));
        var draft = ValidPost(5);
        draft.Published = false;

        // Act
        var result = await BuildService(repo).UpdatePostAsync(draft);

        // Assert
        Assert.True(result.IsSuccess);
        await repo.Received(1).UpdateAsync(draft, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A live post is unaffected by the new guard — the transition that always worked still works, so
    /// the fix is a refusal of one state rather than a general narrowing.
    /// </summary>
    [Fact]
    public async Task QuickPublishStillPublishesALivePost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingleAsync(5, Arg.Any<CancellationToken>()).Returns(ValidPost(5));

        // Act
        var result = await BuildService(repo).QuickPublishAsync(5);

        // Assert
        Assert.True(result.IsSuccess);
        await repo.Received(1).UpdateAsync(Arg.Is<BlogPost>(p => p != null && p.Published), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// LOGGING OF PERSISTENCE FAILURES (REQ-FN-055, second clause). Each of the three transitions
    /// must record the failed write in the log, or a persistence failure leaves no trace at all.
    /// </summary>
    [Fact]
    public async Task TransitionsLogPersistenceFailures()
    {
        // Arrange
        var logger = new RecordingLogger<BlogSvc>();
        var service = new BlogSvc(BuildFailingRepo(), logger);
        var token = TestContext.Current.CancellationToken;

        // Act
        await service.UnpublishPostAsync(1, token);
        await service.QuickPublishAsync(2, token);
        await service.CancelScheduleAsync(3, token);

        // Assert
        Assert.Equal(3, logger.Entries.Count(entry => entry.Level == LogLevel.Error));
        Assert.All(
            logger.Entries.Where(entry => entry.Level == LogLevel.Error),
            entry => Assert.Equal("boom", entry.Error?.Message));
    }

    /// <summary>
    /// Builds the service under test over a substituted repository and a logger that discards.
    /// </summary>
    /// <param name="repo">The substituted repository the service should use.</param>
    /// <returns>A service wired to <paramref name="repo"/>.</returns>
    private static BlogSvc BuildService(IBlogPostRepo repo)
    {
        return new BlogSvc(repo, new RecordingLogger<BlogSvc>());
    }

    /// <summary>
    /// Builds a repository holding one live post per transition under test, whose every write throws.
    /// </summary>
    /// <remarks>
    /// Post 1 is published so it can be unpublished, post 2 is a draft so it can be published, and
    /// post 3 carries a schedule so the cancellation has something to clear. All three reach their
    /// <c>try</c> block, which is the point — a guard rejection would never exercise the log.
    /// </remarks>
    /// <returns>A substituted repository whose <c>Update</c> and <c>UpdateAsync</c> both throw.</returns>
    private static IBlogPostRepo BuildFailingRepo()
    {
        var repo = Substitute.For<IBlogPostRepo>();

        var published = ValidPost(1);
        published.Published = true;
        var draft = ValidPost(2);
        var scheduled = ValidPost(3);
        scheduled.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);

        repo.GetSingleAsync(1, Arg.Any<CancellationToken>()).Returns(published);
        repo.GetSingleAsync(2, Arg.Any<CancellationToken>()).Returns(draft);
        repo.GetSingleAsync(3, Arg.Any<CancellationToken>()).Returns(scheduled);

        repo.When(r => r.UpdateAsync(Arg.Any<BlogPost>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        return repo;
    }

    /// <summary>
    /// Builds a post that passes every validation rule.
    /// </summary>
    /// <param name="postId">Identifier to carry.</param>
    /// <returns>A valid, live post.</returns>
    private static BlogPost ValidPost(long postId)
    {
        return new BlogPost
        {
            PostID = postId,
            Title = "My Title",
            Slug = "my-title",
            PostContent = "Body copy that is long enough to be real."
        };
    }

    /// <summary>
    /// Builds a post in the soft-deleted state the transitions must refuse.
    /// </summary>
    /// <param name="postId">Identifier to carry.</param>
    /// <returns>A valid post whose <c>IsDeleted</c> flag is set.</returns>
    private static BlogPost DeletedPost(long postId)
    {
        var post = ValidPost(postId);
        post.IsDeleted = true;
        return post;
    }
}
