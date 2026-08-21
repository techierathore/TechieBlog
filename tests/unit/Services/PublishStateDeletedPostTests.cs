using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechieBlog.Tests.Dashboard;

namespace TechieBlog.Tests.Services;

/// <summary>
/// Tests for REQ-FN-055 — a soft-deleted post cannot be moved back into the published state by any
/// transition, and the blocking members log a persistence failure exactly as their async twins do.
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
/// <see cref="RecordingLogger{T}"/> for the sync/async logging-parity tests. No database.</para>
/// </remarks>
public class PublishStateDeletedPostTests
{
    /// <summary>
    /// The one-click publish button on the admin grid refuses a soft-deleted post and writes nothing.
    /// </summary>
    [Fact]
    public void QuickPublishRefusesADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(DeletedPost(5));

        // Act
        var result = BuildService(repo).QuickPublish(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted and cannot be published", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

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
    public void QuickPublishReportsDeletionRatherThanAlreadyPublished()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);
        post.Published = true;
        repo.GetSingle(5).Returns(post);

        // Act
        var result = BuildService(repo).QuickPublish(5);

        // Assert
        Assert.Equal("Post is deleted and cannot be published", result.ErrorMessage);
    }

    /// <summary>
    /// Unpublishing a soft-deleted post is refused; the row is already out of every public query and
    /// touching it would only restamp <c>UpdatedOn</c>.
    /// </summary>
    [Fact]
    public void UnpublishPostRefusesADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);
        post.Published = true;
        repo.GetSingle(5).Returns(post);

        // Act
        var result = BuildService(repo).UnpublishPost(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
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
    /// Cancelling the schedule of a soft-deleted post is refused, because clearing the schedule would
    /// quietly move it into the plain-draft state the admin grid lists.
    /// </summary>
    [Fact]
    public void CancelScheduleRefusesADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);
        post.ScheduledPublishOn = DateTime.UtcNow.AddDays(1);
        repo.GetSingle(5).Returns(post);

        // Act
        var result = BuildService(repo).CancelSchedule(5);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
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
    /// The object-carrying publish route is closed too: <c>PublishPost</c> refuses before it mutates
    /// the caller's post, so the caller is not left holding an object that claims to be published.
    /// </summary>
    [Fact]
    public void PublishPostRefusesADeletedPostWithoutMutatingIt()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        var post = DeletedPost(5);

        // Act
        var result = BuildService(repo).PublishPost(post);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted and cannot be published", result.ErrorMessage);
        Assert.False(post.Published);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
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
    public void UpdatePostRefusesToPublishARowDeletedInAnotherTab()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(DeletedPost(5));
        var stale = ValidPost(5);
        stale.IsDeleted = false;
        stale.Published = true;

        // Act
        var result = BuildService(repo).UpdatePost(stale);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Post is deleted and cannot be published", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogPost>());
    }

    /// <summary>
    /// The guard is aimed at publication, not at every write: a deleted post can still be saved as a
    /// draft, which is what leaves "restore, then republish" possible rather than freezing the row.
    /// </summary>
    [Fact]
    public void UpdatePostStillAllowsADraftSaveOfADeletedPost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(DeletedPost(5));
        var draft = ValidPost(5);
        draft.Published = false;

        // Act
        var result = BuildService(repo).UpdatePost(draft);

        // Assert
        Assert.True(result.IsSuccess);
        repo.Received(1).Update(draft);
    }

    /// <summary>
    /// A live post is unaffected by the new guard — the transition that always worked still works, so
    /// the fix is a refusal of one state rather than a general narrowing.
    /// </summary>
    [Fact]
    public void QuickPublishStillPublishesALivePost()
    {
        // Arrange
        var repo = Substitute.For<IBlogPostRepo>();
        repo.GetSingle(5).Returns(ValidPost(5));

        // Act
        var result = BuildService(repo).QuickPublish(5);

        // Assert
        Assert.True(result.IsSuccess);
        repo.Received(1).Update(Arg.Is<BlogPost>(p => p != null && p.Published));
    }

    /// <summary>
    /// SYNC/ASYNC LOGGING PARITY (REQ-FN-055, second clause). The three blocking transitions returned
    /// the exception message without logging it, while their async twins logged. Both halves of each
    /// pair must now record the same error, or a persistence failure on the blocking path leaves no
    /// trace in the log at all.
    /// </summary>
    [Fact]
    public async Task BlockingTransitionsLogPersistenceFailuresLikeTheirAsyncTwins()
    {
        // Arrange
        var syncLogger = new RecordingLogger<BlogSvc>();
        var asyncLogger = new RecordingLogger<BlogSvc>();
        var syncRepo = BuildFailingRepo();
        var asyncRepo = BuildFailingRepo();
        var syncService = new BlogSvc(syncRepo, syncLogger);
        var asyncService = new BlogSvc(asyncRepo, asyncLogger);
        var token = TestContext.Current.CancellationToken;

        // Act
        syncService.UnpublishPost(1);
        syncService.QuickPublish(2);
        syncService.CancelSchedule(3);
        await asyncService.UnpublishPostAsync(1, token);
        await asyncService.QuickPublishAsync(2, token);
        await asyncService.CancelScheduleAsync(3, token);

        // Assert
        Assert.Equal(3, syncLogger.Entries.Count(entry => entry.Level == LogLevel.Error));
        Assert.Equal(
            asyncLogger.Entries.Where(entry => entry.Level == LogLevel.Error).Select(entry => entry.Message),
            syncLogger.Entries.Where(entry => entry.Level == LogLevel.Error).Select(entry => entry.Message));
        Assert.All(
            syncLogger.Entries.Where(entry => entry.Level == LogLevel.Error),
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

        repo.GetSingle(1).Returns(published);
        repo.GetSingle(2).Returns(draft);
        repo.GetSingle(3).Returns(scheduled);
        repo.GetSingleAsync(1, Arg.Any<CancellationToken>()).Returns(published);
        repo.GetSingleAsync(2, Arg.Any<CancellationToken>()).Returns(draft);
        repo.GetSingleAsync(3, Arg.Any<CancellationToken>()).Returns(scheduled);

        repo.When(r => r.Update(Arg.Any<BlogPost>())).Do(_ => throw new InvalidOperationException("boom"));
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
