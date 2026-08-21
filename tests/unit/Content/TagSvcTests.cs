using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechieBlog.Tests.Dashboard;
using Xunit;

namespace TechieBlog.Tests.Content;

/// <summary>
/// Unit tests for <see cref="TagSvc"/> — the free-form half of the taxonomy.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the rules that keep an author-driven vocabulary from producing duplicate
/// or unreachable URLs: the slug is the identity, a taken slug is suffixed rather than merged, and
/// <c>GetOrCreateTag</c> reuses an existing row when a typed name slugs to the same value. The error
/// contract is pinned too — reads swallow and degrade, mutations return <c>Result</c>, and
/// <c>GetOrCreateTag</c> deliberately throws rather than silently attaching a post to no tag.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="IBlogTagRepo"/> and
/// <see cref="ILogger{TCategoryName}"/>. No database. Every repository member the service touches is
/// stubbed explicitly, including the <c>…Async</c> twins, which a substitute intercepts rather than
/// letting them fall through to any interface default implementation.</para>
/// </remarks>
public class TagSvcTests
{
    /// <summary>
    /// Builds the service under test over a substituted repository and a silent logger.
    /// </summary>
    /// <param name="repo">The substituted repository the service should use.</param>
    /// <returns>A service wired to <paramref name="repo"/>.</returns>
    private static TagSvc BuildService(IBlogTagRepo repo, ILogger<TagSvc>? logger = null)
    {
        return new TagSvc(repo, logger ?? Substitute.For<ILogger<TagSvc>>());
    }

    /// <summary>
    /// Builds a tag that passes every validation rule.
    /// </summary>
    /// <param name="tagId">Identifier to carry; zero means "never persisted".</param>
    /// <returns>A valid tag.</returns>
    private static BlogTag ValidTag(long tagId = 0)
    {
        return new BlogTag { TagId = tagId, TagName = "Blazor Server" };
    }

    // =============================================================================================
    // Reads
    // =============================================================================================

    /// <summary>
    /// The admin list is unfiltered — an orphan tag with no posts is still returned so it can be seen
    /// and deleted.
    /// </summary>
    [Fact]
    public void GetAllTagsForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var tags = new[] { ValidTag(1), ValidTag(2) };
        repo.GetAll().Returns(tags);

        // Act
        var result = BuildService(repo).GetAllTags();

        // Assert
        Assert.Same(tags, result);
    }

    /// <summary>
    /// A taxonomy read failure degrades a sidebar rather than breaking the page it sits on.
    /// </summary>
    [Fact]
    public void GetAllTagsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetAll()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetAllTags();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The counted listing is one aggregate query, which is what makes a tag cloud affordable to
    /// render; the rows come back untouched.
    /// </summary>
    [Fact]
    public void GetAllWithCountsForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var tags = new[] { ValidTag(1) };
        repo.GetAllWithCounts().Returns(tags);

        // Act
        var result = BuildService(repo).GetAllWithCounts();

        // Assert
        Assert.Same(tags, result);
    }

    /// <summary>
    /// A failed counted listing degrades to an empty sequence.
    /// </summary>
    [Fact]
    public void GetAllWithCountsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetAllWithCounts()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetAllWithCounts();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The id-keyed lookup is the admin one and forwards the identifier unchanged.
    /// </summary>
    [Fact]
    public void GetSingleTagForwardsIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var tag = ValidTag(4);
        repo.GetSingle(4).Returns(tag);

        // Act
        var result = BuildService(repo).GetSingleTag(4);

        // Assert
        Assert.Same(tag, result);
    }

    /// <summary>
    /// "No such tag" and "the read failed" both surface as null, distinguished only in the log.
    /// </summary>
    [Fact]
    public void GetSingleTagReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetSingle(4)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetSingleTag(4);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// A blank slug from a truncated URL becomes a clean not-found without a database round trip.
    /// </summary>
    /// <param name="slug">The malformed slug taken from the route.</param>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void GetTagBySlugRejectsBlankSlugWithoutQuerying(string slug)
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).GetTagBySlug(slug);

        // Assert
        Assert.Null(result);
        repo.DidNotReceive().GetBySlug(Arg.Any<string>());
    }

    /// <summary>
    /// A real slug is matched exactly as supplied — the service does not normalise casing on the way
    /// in.
    /// </summary>
    [Fact]
    public void GetTagBySlugForwardsSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var tag = ValidTag(4);
        repo.GetBySlug("blazor-server").Returns(tag);

        // Act
        var result = BuildService(repo).GetTagBySlug("blazor-server");

        // Assert
        Assert.Same(tag, result);
    }

    /// <summary>
    /// A failed slug read renders the tag page's not-found state rather than throwing.
    /// </summary>
    [Fact]
    public void GetTagBySlugReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetBySlug("blazor-server")).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetTagBySlug("blazor-server");

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// An empty autocomplete query offers a short sample of the existing vocabulary instead of
    /// nothing, which is how authors are steered towards reusing a tag rather than inventing one.
    /// </summary>
    [Fact]
    public void SearchTagsOffersASampleForABlankQuery()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var all = Enumerable.Range(1, 25).Select(i => ValidTag(i)).ToArray();
        repo.GetAll().Returns(all);

        // Act
        var result = BuildService(repo).SearchTags("   ");

        // Assert
        Assert.Equal(10, result.Count());
        repo.DidNotReceive().SearchTags(Arg.Any<string>());
    }

    /// <summary>
    /// A real fragment goes to the repository's search rather than to the full listing.
    /// </summary>
    [Fact]
    public void SearchTagsForwardsRealQuery()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var hits = new[] { ValidTag(1) };
        repo.SearchTags("bla").Returns(hits);

        // Act
        var result = BuildService(repo).SearchTags("bla");

        // Assert
        Assert.Same(hits, result);
        repo.DidNotReceive().GetAll();
    }

    /// <summary>
    /// A failed autocomplete read degrades to an empty sequence rather than breaking the editor.
    /// </summary>
    [Fact]
    public void SearchTagsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.SearchTags("bla")).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).SearchTags("bla");

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The post's tag chips are whatever the repository reports for that post.
    /// </summary>
    [Fact]
    public void GetTagsForPostForwardsIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var tags = new[] { ValidTag(1) };
        repo.GetTagsForPost(9).Returns(tags);

        // Act
        var result = BuildService(repo).GetTagsForPost(9);

        // Assert
        Assert.Same(tags, result);
    }

    /// <summary>
    /// A failed per-post tag read degrades to an empty sequence.
    /// </summary>
    [Fact]
    public void GetTagsForPostReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetTagsForPost(9)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetTagsForPost(9);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The tag-filtered listing passes its filter and both paging arguments through unclamped.
    /// </summary>
    [Fact]
    public void GetPostsByTagForwardsArguments()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var page = new[] { new BlogPost { PostID = 1 } };
        repo.GetPostsByTag(3, 20, 40).Returns(page);

        // Act
        var result = BuildService(repo).GetPostsByTag(3, 20, 40);

        // Assert
        Assert.Same(page, result);
        repo.Received(1).GetPostsByTag(3, 20, 40);
    }

    /// <summary>
    /// A failed tag listing degrades to an empty page.
    /// </summary>
    [Fact]
    public void GetPostsByTagReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetPostsByTag(3, 20, 40)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetPostsByTag(3, 20, 40);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The tag post count is forwarded unchanged so the pager cannot advertise a page that renders
    /// empty.
    /// </summary>
    [Fact]
    public void GetPostCountByTagForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetPostCountByTag(3).Returns(42);

        // Act
        var result = BuildService(repo).GetPostCountByTag(3);

        // Assert
        Assert.Equal(42, result);
    }

    /// <summary>
    /// A failed count collapses the pager to zero rather than throwing on a public page.
    /// </summary>
    [Fact]
    public void GetPostCountByTagReturnsZeroOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetPostCountByTag(3)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetPostCountByTag(3);

        // Assert
        Assert.Equal(0, result);
    }

    // =============================================================================================
    // GetOrCreateTag
    // =============================================================================================

    /// <summary>
    /// A blank name is not a tag, and is rejected before any lookup so the editor cannot create an
    /// unnamed row by pressing enter on an empty box.
    /// </summary>
    /// <param name="tagName">The unusable name the author typed.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetOrCreateTagRejectsBlankName(string tagName)
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).GetOrCreateTag(tagName);

        // Assert
        Assert.Null(result);
        repo.DidNotReceive().GetBySlug(Arg.Any<string>());
        repo.DidNotReceive().InsertToGetId(Arg.Any<BlogTag>());
    }

    /// <summary>
    /// Matching is by slug rather than by name, so a differently punctuated spelling of an existing
    /// tag resolves to the stored row and the author's spelling does not rename it for every other
    /// post carrying it.
    /// </summary>
    [Fact]
    public void GetOrCreateTagReusesExistingTagMatchedBySlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var stored = new BlogTag { TagId = 12, TagName = "ASP.NET Core", Slug = "aspnet-core" };
        repo.GetBySlug("aspnet-core").Returns(stored);

        // Act
        var result = BuildService(repo).GetOrCreateTag("  ASP.NET Core  ");

        // Assert
        Assert.Same(stored, result);
        Assert.Equal("ASP.NET Core", result!.TagName);
        repo.DidNotReceive().InsertToGetId(Arg.Any<BlogTag>());
    }

    /// <summary>
    /// A genuinely new name is inserted with its derived slug and the trimmed name the author typed,
    /// and comes back carrying the generated identifier.
    /// </summary>
    [Fact]
    public void GetOrCreateTagInsertsGenuinelyNewName()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.InsertToGetId(Arg.Any<BlogTag>()).Returns(31L);

        // Act
        var result = BuildService(repo).GetOrCreateTag("  Minimal APIs ");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(31, result!.TagId);
        Assert.Equal("Minimal APIs", result.TagName);
        Assert.Equal("minimal-apis", result.Slug);
    }

    /// <summary>
    /// A repository failure propagates rather than being swallowed: this runs inside the post-save
    /// path, and returning null would attach the post to no tag while reporting success.
    /// </summary>
    [Fact]
    public void GetOrCreateTagLetsRepositoryFailuresPropagate()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetBySlug("minimal-apis")).Do(_ => throw new InvalidOperationException("boom"));

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => BuildService(repo).GetOrCreateTag("Minimal APIs"));
    }

    // =============================================================================================
    // CreateTag / UpdateTag / SaveTag / DeleteTag
    // =============================================================================================

    /// <summary>
    /// A null tag is refused as a failed result rather than a null-reference exception.
    /// </summary>
    [Fact]
    public void CreateTagRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).CreateTag(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tag cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// A name is mandatory, and whitespace does not count as one.
    /// </summary>
    [Fact]
    public void CreateTagRejectsBlankName()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).CreateTag(new BlogTag { TagName = "  " });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tag name is required", result.ErrorMessage);
        repo.DidNotReceive().InsertToGetId(Arg.Any<BlogTag>());
    }

    /// <summary>
    /// An administrator who leaves the slug blank gets one derived from the name, and the generated
    /// key is written back onto the supplied instance.
    /// </summary>
    [Fact]
    public void CreateTagDerivesSlugAndAssignsIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.InsertToGetId(Arg.Any<BlogTag>()).Returns(8L);
        var tag = ValidTag();

        // Act
        var result = BuildService(repo).CreateTag(tag);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-server", tag.Slug);
        Assert.Equal(8, tag.TagId);
        Assert.Same(tag, result.Data);
    }

    /// <summary>
    /// A slug the administrator typed themselves is kept rather than re-derived from the name,
    /// because it is the tag's public address and their choice is deliberate.
    /// </summary>
    [Fact]
    public void CreateTagKeepsSuppliedSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var tag = ValidTag();
        tag.Slug = "hand-picked";

        // Act
        BuildService(repo).CreateTag(tag);

        // Assert
        Assert.Equal("hand-picked", tag.Slug);
    }

    /// <summary>
    /// A taken slug gains a numeric suffix rather than merging into the existing tag, because two
    /// tags may legitimately share a name-derived slug while meaning different things.
    /// </summary>
    [Fact]
    public void CreateTagSuffixesDuplicateSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.SlugExists("blazor-server").Returns(true);
        var tag = ValidTag();

        // Act
        BuildService(repo).CreateTag(tag);

        // Assert
        Assert.Equal("blazor-server-2", tag.Slug);
    }

    /// <summary>
    /// When the first suffixed candidate is also taken the loop keeps climbing until a free slug is
    /// found.
    /// </summary>
    [Fact]
    public void CreateTagRetriesUntilSlugIsFree()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.SlugExists("blazor-server").Returns(true);
        repo.SlugExists("blazor-server-2").Returns(true);
        var tag = ValidTag();

        // Act
        BuildService(repo).CreateTag(tag);

        // Assert
        Assert.Equal("blazor-server-3", tag.Slug);
    }

    /// <summary>
    /// The retry loop is a bounded safety valve, not a business rule: it stops at the cap and leaves
    /// the last candidate for the database's unique constraint to arbitrate.
    /// </summary>
    [Fact]
    public void CreateTagStopsSuffixingAtTheAttemptCap()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.SlugExists(Arg.Any<string>()).Returns(true);
        var tag = ValidTag();

        // Act
        BuildService(repo).CreateTag(tag);

        // Assert
        Assert.Equal("blazor-server-100", tag.Slug);
        repo.Received(100).SlugExists(Arg.Any<string>());
    }

    /// <summary>
    /// A unique-constraint violation from a concurrent insert is reported as a failed result rather
    /// than crashing the editor.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public void CreateTagReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var logger = new RecordingLogger<TagSvc>();
        repo.When(r => r.InsertToGetId(Arg.Any<BlogTag>()))
            .Do(_ => throw new InvalidOperationException("duplicate key"));

        // Act
        var result = BuildService(repo, logger).CreateTag(ValidTag());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create tag. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("duplicate key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "duplicate key");
    }

    /// <summary>
    /// A null tag is refused before anything is read or written.
    /// </summary>
    [Fact]
    public void UpdateTagRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).UpdateTag(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tag cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// An edit needs a key; a non-positive identifier is a caller error rather than an insert.
    /// </summary>
    /// <param name="tagId">The unusable identifier.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void UpdateTagRejectsNonPositiveIdentifier(long tagId)
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).UpdateTag(ValidTag(tagId));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid tag ID", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogTag>());
    }

    /// <summary>
    /// The name rule applies to an edit exactly as it does to a creation.
    /// </summary>
    [Fact]
    public void UpdateTagRejectsBlankName()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var tag = ValidTag(4);
        tag.TagName = "";

        // Act
        var result = BuildService(repo).UpdateTag(tag);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tag name is required", result.ErrorMessage);
    }

    /// <summary>
    /// Editing a tag someone else deleted reports "not found" rather than a success that updated no
    /// rows.
    /// </summary>
    [Fact]
    public void UpdateTagRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetSingle(4).Returns((BlogTag?)null);

        // Act
        var result = BuildService(repo).UpdateTag(ValidTag(4));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tag not found", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<BlogTag>());
    }

    /// <summary>
    /// The uniqueness check excludes the tag being edited, so saving it without renaming it does not
    /// collide with itself and pointlessly renumber its slug — which would break its published URL.
    /// </summary>
    [Fact]
    public void UpdateTagExcludesItselfFromSlugCheck()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetSingle(4).Returns(ValidTag(4));
        var tag = ValidTag(4);
        tag.Slug = "blazor-server";

        // Act
        var result = BuildService(repo).UpdateTag(tag);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-server", tag.Slug);
        repo.Received(1).SlugExists("blazor-server", 4);
        repo.Received(1).Update(tag);
    }

    /// <summary>
    /// A slug that genuinely belongs to another tag is suffixed, exactly as on creation.
    /// </summary>
    [Fact]
    public void UpdateTagSuffixesCollidingSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetSingle(4).Returns(ValidTag(4));
        repo.SlugExists("blazor-server", 4).Returns(true);
        var tag = ValidTag(4);

        // Act
        BuildService(repo).UpdateTag(tag);

        // Assert
        Assert.Equal("blazor-server-2", tag.Slug);
    }

    /// <summary>
    /// When the first suffixed candidate is also taken on an edit, the loop keeps climbing, and the
    /// exclusion of the edited row applies to every attempt rather than only the first.
    /// </summary>
    [Fact]
    public void UpdateTagRetriesUntilSlugIsFree()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetSingle(4).Returns(ValidTag(4));
        repo.SlugExists("blazor-server", 4).Returns(true);
        repo.SlugExists("blazor-server-2", 4).Returns(true);
        var tag = ValidTag(4);

        // Act
        BuildService(repo).UpdateTag(tag);

        // Assert
        Assert.Equal("blazor-server-3", tag.Slug);
    }

    /// <summary>
    /// An unexpected persistence error on the update is converted into a failed result.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public void UpdateTagReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var logger = new RecordingLogger<TagSvc>();
        repo.GetSingle(4).Returns(ValidTag(4));
        repo.When(r => r.Update(Arg.Any<BlogTag>())).Do(_ => throw new InvalidOperationException("deadlock"));

        // Act
        var result = BuildService(repo, logger).UpdateTag(ValidTag(4));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update tag. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("deadlock", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "deadlock");
    }

    /// <summary>
    /// The save entry point refuses a null tag before deciding between insert and update.
    /// </summary>
    [Fact]
    public void SaveTagRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).SaveTag(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tag cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// One admin form serves both add and edit: a non-positive identifier means "new" and inserts,
    /// while a real one updates.
    /// </summary>
    [Fact]
    public void SaveTagRoutesOnIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetSingle(4).Returns(ValidTag(4));
        var service = BuildService(repo);

        // Act
        service.SaveTag(ValidTag());
        service.SaveTag(ValidTag(4));

        // Assert
        repo.Received(1).InsertToGetId(Arg.Any<BlogTag>());
        repo.Received(1).Update(Arg.Any<BlogTag>());
    }

    /// <summary>
    /// A non-positive identifier cannot name a row and is refused before any read.
    /// </summary>
    [Fact]
    public void DeleteTagRejectsNonPositiveIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();

        // Act
        var result = BuildService(repo).DeleteTag(0);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid tag ID", result.ErrorMessage);
        repo.DidNotReceive().GetSingle(Arg.Any<long>());
    }

    /// <summary>
    /// Deleting an already-deleted tag reports "not found" rather than succeeding silently.
    /// </summary>
    [Fact]
    public void DeleteTagRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.GetSingle(4).Returns((BlogTag?)null);

        // Act
        var result = BuildService(repo).DeleteTag(4);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Tag not found", result.ErrorMessage);
        repo.DidNotReceive().Delete(Arg.Any<long>());
    }

    /// <summary>
    /// A tag is a label rather than a home, so the delete is not blocked when posts still carry it.
    /// </summary>
    [Fact]
    public void DeleteTagRemovesTagEvenWhenPostsCarryIt()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var existing = ValidTag(4);
        existing.PostCount = 12;
        repo.GetSingle(4).Returns(existing);

        // Act
        var result = BuildService(repo).DeleteTag(4);

        // Assert
        Assert.True(result.IsSuccess);
        repo.Received(1).Delete(4);
    }

    /// <summary>
    /// An unexpected persistence error on the delete is converted into a failed result.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public void DeleteTagReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var logger = new RecordingLogger<TagSvc>();
        repo.GetSingle(4).Returns(ValidTag(4));
        repo.When(r => r.Delete(4)).Do(_ => throw new InvalidOperationException("constraint"));

        // Act
        var result = BuildService(repo, logger).DeleteTag(4);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete tag. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("constraint", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "constraint");
    }

    /// <summary>
    /// Setting a post's tags is a replace rather than a merge, and the supplied ids reach the
    /// repository unchanged.
    /// </summary>
    [Fact]
    public void SetTagsForPostForwardsTheCompleteSet()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var ids = new long[] { 3, 5, 8 };

        // Act
        BuildService(repo).SetTagsForPost(9, ids);

        // Assert
        repo.Received(1).SetTagsForPost(9, ids);
    }

    /// <summary>
    /// A repository failure while rewriting the link rows is swallowed — the method reports nothing,
    /// which is why the post save can succeed while the author's tag changes are lost.
    /// </summary>
    [Fact]
    public void SetTagsForPostSwallowsRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.SetTagsForPost(Arg.Any<long>(), Arg.Any<IEnumerable<long>>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var exception = Record.Exception(() => BuildService(repo).SetTagsForPost(9, new long[] { 1 }));

        // Assert
        Assert.Null(exception);
    }

    // =============================================================================================
    // Async surface — REQ-NFR-026
    // =============================================================================================

    /// <summary>
    /// The async listing forwards to the repository and degrades to an empty sequence on failure,
    /// exactly as its blocking twin does.
    /// </summary>
    [Fact]
    public async Task GetAllTagsAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var tags = new[] { ValidTag(1) };
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(tags);

        var failing = Substitute.For<IBlogTagRepo>();
        failing.When(r => r.GetAllAsync(Arg.Any<CancellationToken>()))
               .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var good = await BuildService(repo).GetAllTagsAsync(token);
        var bad = await BuildService(failing).GetAllTagsAsync(token);

        // Assert
        Assert.Same(tags, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// The async counted listing forwards to the repository and degrades to empty on failure.
    /// </summary>
    [Fact]
    public async Task GetAllWithCountsAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var tags = new[] { ValidTag(1) };
        repo.GetAllWithCountsAsync(Arg.Any<CancellationToken>()).Returns(tags);

        var failing = Substitute.For<IBlogTagRepo>();
        failing.When(r => r.GetAllWithCountsAsync(Arg.Any<CancellationToken>()))
               .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var good = await BuildService(repo).GetAllWithCountsAsync(token);
        var bad = await BuildService(failing).GetAllWithCountsAsync(token);

        // Assert
        Assert.Same(tags, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// The async id lookup forwards the identifier and answers null when the read fails.
    /// </summary>
    [Fact]
    public async Task GetSingleTagAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var tag = ValidTag(4);
        repo.GetSingleAsync(4, Arg.Any<CancellationToken>()).Returns(tag);
        repo.When(r => r.GetSingleAsync(5, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var found = await service.GetSingleTagAsync(4, token);
        var failed = await service.GetSingleTagAsync(5, token);

        // Assert
        Assert.Same(tag, found);
        Assert.Null(failed);
    }

    /// <summary>
    /// The async slug lookup keeps the blank-slug guard, forwards a real slug, and answers null when
    /// the read fails.
    /// </summary>
    [Fact]
    public async Task GetTagBySlugAsyncGuardsBlankAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var tag = ValidTag(4);
        repo.GetBySlugAsync("blazor-server", Arg.Any<CancellationToken>()).Returns(tag);
        repo.When(r => r.GetBySlugAsync("broken", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var blank = await service.GetTagBySlugAsync("  ", token);
        var found = await service.GetTagBySlugAsync("blazor-server", token);
        var failed = await service.GetTagBySlugAsync("broken", token);

        // Assert
        Assert.Null(blank);
        Assert.Same(tag, found);
        Assert.Null(failed);
        await repo.DidNotReceive().GetBySlugAsync("  ", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async autocomplete keeps the blank-query branch — a short sample taken in memory over the
    /// full list — and routes a real fragment to the repository's search.
    /// </summary>
    [Fact]
    public async Task SearchTagsAsyncKeepsTheBlankQueryBranch()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var all = Enumerable.Range(1, 25).Select(i => ValidTag(i)).ToArray();
        var hits = new[] { ValidTag(1) };
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(all);
        repo.SearchTagsAsync("bla", Arg.Any<CancellationToken>()).Returns(hits);
        var service = BuildService(repo);

        // Act
        var sample = await service.SearchTagsAsync("", token);
        var matched = await service.SearchTagsAsync("bla", token);

        // Assert
        Assert.Equal(10, sample.Count());
        Assert.Same(hits, matched);
    }

    /// <summary>
    /// A failed async autocomplete read degrades to an empty sequence.
    /// </summary>
    [Fact]
    public async Task SearchTagsAsyncReturnsEmptyOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.SearchTagsAsync("bla", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = await BuildService(repo).SearchTagsAsync("bla", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The async get-or-create keeps every rule of its blocking twin: blank names are rejected, an
    /// existing row is reused when the slug matches, and a new name is inserted.
    /// </summary>
    [Fact]
    public async Task GetOrCreateTagAsyncReusesOrInserts()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var stored = new BlogTag { TagId = 12, TagName = "ASP.NET Core", Slug = "aspnet-core" };
        repo.GetBySlugAsync("aspnet-core", Arg.Any<CancellationToken>()).Returns(stored);
        repo.InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>()).Returns(31L);
        var service = BuildService(repo);

        // Act
        var blank = await service.GetOrCreateTagAsync("   ", token);
        var reused = await service.GetOrCreateTagAsync("  ASP.NET Core  ", token);
        var created = await service.GetOrCreateTagAsync(" Minimal APIs ", token);

        // Assert
        Assert.Null(blank);
        Assert.Same(stored, reused);
        Assert.Equal(31, created!.TagId);
        Assert.Equal("minimal-apis", created.Slug);
        Assert.Equal("Minimal APIs", created.TagName);
    }

    /// <summary>
    /// The async get-or-create faults its task rather than swallowing a repository failure, because
    /// the post-save path must not report success while attaching the post to no tag.
    /// </summary>
    [Fact]
    public async Task GetOrCreateTagAsyncLetsRepositoryFailuresPropagate()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        repo.When(r => r.GetBySlugAsync("minimal-apis", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildService(repo).GetOrCreateTagAsync("Minimal APIs", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The async creation applies the same guards, derives the same slug and resolves a collision
    /// with the same ordinal suffix as its blocking twin.
    /// </summary>
    [Fact]
    public async Task CreateTagAsyncValidatesAndResolvesSlug()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.SlugExistsAsync("blazor-server", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>()).Returns(8L);
        var tag = ValidTag();
        var service = BuildService(repo);

        // Act
        var nullResult = await service.CreateTagAsync(null!, token);
        var blankResult = await service.CreateTagAsync(new BlogTag { TagName = " " }, token);
        var result = await service.CreateTagAsync(tag, token);

        // Assert
        Assert.Equal("Tag cannot be null", nullResult.ErrorMessage);
        Assert.Equal("Tag name is required", blankResult.ErrorMessage);
        Assert.True(result.IsSuccess);
        Assert.Equal("blazor-server-2", tag.Slug);
        Assert.Equal(8, tag.TagId);
    }

    /// <summary>
    /// An unexpected persistence error on the async insert becomes a failed result rather than a
    /// faulted task.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public async Task CreateTagAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var logger = new RecordingLogger<TagSvc>();
        repo.When(r => r.InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("duplicate key"));

        // Act
        var result = await BuildService(repo, logger).CreateTagAsync(ValidTag(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create tag. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("duplicate key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "duplicate key");
    }

    /// <summary>
    /// The async update applies its guards in the documented order and excludes the edited row from
    /// the uniqueness check.
    /// </summary>
    [Fact]
    public async Task UpdateTagAsyncGuardsAndExcludesItselfFromSlugCheck()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(4, Arg.Any<CancellationToken>()).Returns(ValidTag(4));
        var blankName = ValidTag(4);
        blankName.TagName = "";
        var tag = ValidTag(4);
        tag.Slug = "blazor-server";
        var service = BuildService(repo);

        // Act
        var nullResult = await service.UpdateTagAsync(null!, token);
        var idResult = await service.UpdateTagAsync(ValidTag(0), token);
        var nameResult = await service.UpdateTagAsync(blankName, token);
        var missingResult = await service.UpdateTagAsync(ValidTag(9), token);
        var okResult = await service.UpdateTagAsync(tag, token);

        // Assert
        Assert.Equal("Tag cannot be null", nullResult.ErrorMessage);
        Assert.Equal("Invalid tag ID", idResult.ErrorMessage);
        Assert.Equal("Tag name is required", nameResult.ErrorMessage);
        Assert.Equal("Tag not found", missingResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        await repo.Received(1).SlugExistsAsync("blazor-server", 4, Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateAsync(tag, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unexpected persistence error on the async update becomes a failed result.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public async Task UpdateTagAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var logger = new RecordingLogger<TagSvc>();
        repo.GetSingleAsync(4, Arg.Any<CancellationToken>()).Returns(ValidTag(4));
        repo.When(r => r.UpdateAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("deadlock"));

        // Act
        var result = await BuildService(repo, logger).UpdateTagAsync(ValidTag(4), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update tag. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("deadlock", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "deadlock");
    }

    /// <summary>
    /// The async save refuses a null tag and otherwise routes on the identifier — insert without one,
    /// update with one.
    /// </summary>
    [Fact]
    public async Task SaveTagAsyncRoutesOnIdentifier()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(4, Arg.Any<CancellationToken>()).Returns(ValidTag(4));
        var service = BuildService(repo);

        // Act
        var nullResult = await service.SaveTagAsync(null!, token);
        await service.SaveTagAsync(ValidTag(), token);
        await service.SaveTagAsync(ValidTag(4), token);

        // Assert
        Assert.Equal("Tag cannot be null", nullResult.ErrorMessage);
        await repo.Received(1).InsertToGetIdAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateAsync(Arg.Any<BlogTag>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async delete refuses a bad identifier and a missing row, and removes an existing tag.
    /// </summary>
    [Fact]
    public async Task DeleteTagAsyncAppliesTheSameGuards()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(4, Arg.Any<CancellationToken>()).Returns(ValidTag(4));
        var service = BuildService(repo);

        // Act
        var idResult = await service.DeleteTagAsync(0, token);
        var missingResult = await service.DeleteTagAsync(9, token);
        var okResult = await service.DeleteTagAsync(4, token);

        // Assert
        Assert.Equal("Invalid tag ID", idResult.ErrorMessage);
        Assert.Equal("Tag not found", missingResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        await repo.Received(1).DeleteAsync(4, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unexpected persistence error on the async delete becomes a failed result.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public async Task DeleteTagAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var logger = new RecordingLogger<TagSvc>();
        repo.GetSingleAsync(4, Arg.Any<CancellationToken>()).Returns(ValidTag(4));
        repo.When(r => r.DeleteAsync(4, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("constraint"));

        // Act
        var result = await BuildService(repo, logger).DeleteTagAsync(4, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete tag. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("constraint", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "constraint");
    }

    /// <summary>
    /// The async per-post tag read forwards the identifier and degrades to empty on failure.
    /// </summary>
    [Fact]
    public async Task GetTagsForPostAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var tags = new[] { ValidTag(1) };
        repo.GetTagsForPostAsync(9, Arg.Any<CancellationToken>()).Returns(tags);
        repo.When(r => r.GetTagsForPostAsync(10, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.GetTagsForPostAsync(9, token);
        var bad = await service.GetTagsForPostAsync(10, token);

        // Assert
        Assert.Same(tags, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// The async tag-set replacement forwards the complete set and swallows a repository failure, so
    /// a lost tag change is visible only in the log.
    /// </summary>
    [Fact]
    public async Task SetTagsForPostAsyncForwardsAndSwallowsFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var ids = new long[] { 3, 5 };

        var failing = Substitute.For<IBlogTagRepo>();
        failing.When(r => r.SetTagsForPostAsync(Arg.Any<long>(), Arg.Any<IEnumerable<long>>(), Arg.Any<CancellationToken>()))
               .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        await BuildService(repo).SetTagsForPostAsync(9, ids, token);
        var exception = await Record.ExceptionAsync(
            () => BuildService(failing).SetTagsForPostAsync(9, ids, token));

        // Assert
        await repo.Received(1).SetTagsForPostAsync(9, ids, Arg.Any<CancellationToken>());
        Assert.Null(exception);
    }

    /// <summary>
    /// The async tag-filtered listing forwards all three arguments and degrades to empty on failure.
    /// </summary>
    [Fact]
    public async Task GetPostsByTagAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        var page = new[] { new BlogPost { PostID = 1 } };
        repo.GetPostsByTagAsync(3, 20, 40, Arg.Any<CancellationToken>()).Returns(page);
        repo.When(r => r.GetPostsByTagAsync(4, 20, 40, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.GetPostsByTagAsync(3, 20, 40, token);
        var bad = await service.GetPostsByTagAsync(4, 20, 40, token);

        // Assert
        Assert.Same(page, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// The async tag count is forwarded, and a failure collapses the pager to zero.
    /// </summary>
    [Fact]
    public async Task GetPostCountByTagAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<IBlogTagRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetPostCountByTagAsync(3, Arg.Any<CancellationToken>()).Returns(42);
        repo.When(r => r.GetPostCountByTagAsync(4, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var good = await service.GetPostCountByTagAsync(3, token);
        var bad = await service.GetPostCountByTagAsync(4, token);

        // Assert
        Assert.Equal(42, good);
        Assert.Equal(0, bad);
    }
}
