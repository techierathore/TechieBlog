using BlogEngine.Services;
using BlogModels;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TechieBlog.Tests.Dashboard;
using Xunit;

namespace TechieBlog.Tests.Content;

/// <summary>
/// Unit tests for <see cref="CategorySvc"/> — the fixed half of the taxonomy.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the taxonomy rules a category must satisfy before it is stored: a name
/// is mandatory, every category ends up with a slug unique across the whole taxonomy because that
/// slug is its public URL, and an edit resolves collisions against every <i>other</i> row so
/// re-saving an unchanged category does not gratuitously renumber its address. The failure convention
/// is pinned too — reads degrade to empty or null, writes report a <c>Result</c>, and a category is
/// hard-deleted rather than flagged.</para>
/// <para><b>Dependencies:</b> xUnit v3, NSubstitute for <see cref="ICategoryRepo"/> and
/// <see cref="ILogger{TCategoryName}"/>. No database. The <c>…Async</c> twins are stubbed explicitly
/// rather than relied on to fall through to a bridged interface default, because a substitute
/// intercepts default implementations too.</para>
/// </remarks>
public class CategorySvcTests
{
    /// <summary>
    /// Builds the service under test over a substituted repository and a silent logger.
    /// </summary>
    /// <param name="repo">The substituted repository the service should use.</param>
    /// <returns>A service wired to <paramref name="repo"/>.</returns>
    private static CategorySvc BuildService(ICategoryRepo repo, ILogger<CategorySvc>? logger = null)
    {
        return new CategorySvc(repo, logger ?? Substitute.For<ILogger<CategorySvc>>());
    }

    /// <summary>
    /// Builds a category that passes every validation rule.
    /// </summary>
    /// <param name="categoryId">Identifier to carry; zero means "never persisted".</param>
    /// <returns>A valid category.</returns>
    private static Category ValidCategory(long categoryId = 0)
    {
        return new Category { CategoryId = categoryId, CategoryName = "Web Development" };
    }

    // =============================================================================================
    // Reads
    // =============================================================================================

    /// <summary>
    /// The taxonomy listing is handed back exactly as the repository ordered it.
    /// </summary>
    [Fact]
    public void GetAllCategoriesForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var categories = new[] { ValidCategory(1), ValidCategory(2) };
        repo.GetAll().Returns(categories);

        // Act
        var result = BuildService(repo).GetAllCategories();

        // Assert
        Assert.Same(categories, result);
    }

    /// <summary>
    /// A failed taxonomy read renders the sidebar without categories rather than taking the page down
    /// with it.
    /// </summary>
    [Fact]
    public void GetAllCategoriesReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.When(r => r.GetAll()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetAllCategories();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The counted listing computes its counts in SQL, so the caller gets both in one round trip and
    /// the rows arrive untouched.
    /// </summary>
    [Fact]
    public void GetAllWithCountsForwardsToRepository()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var categories = new[] { ValidCategory(1) };
        repo.GetAllWithCounts().Returns(categories);

        // Act
        var result = BuildService(repo).GetAllWithCounts();

        // Assert
        Assert.Same(categories, result);
    }

    /// <summary>
    /// A failed counted listing degrades to an empty sequence.
    /// </summary>
    [Fact]
    public void GetAllWithCountsReturnsEmptyOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.When(r => r.GetAllWithCounts()).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetAllWithCounts();

        // Assert
        Assert.Empty(result);
    }

    /// <summary>
    /// The id-keyed lookup forwards the identifier unchanged.
    /// </summary>
    [Fact]
    public void GetCategoryForwardsIdentifier()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var category = ValidCategory(6);
        repo.GetSingle(6).Returns(category);

        // Act
        var result = BuildService(repo).GetCategory(6);

        // Assert
        Assert.Same(category, result);
    }

    /// <summary>
    /// "No such category" and "the lookup failed" both surface as null, distinguished only in the log.
    /// </summary>
    [Fact]
    public void GetCategoryReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.When(r => r.GetSingle(6)).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetCategory(6);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// A blank slug can only come from a malformed route, so it is answered null without a round trip.
    /// </summary>
    /// <param name="slug">The malformed slug taken from the route.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GetCategoryBySlugRejectsBlankSlugWithoutQuerying(string slug)
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();

        // Act
        var result = BuildService(repo).GetCategoryBySlug(slug);

        // Assert
        Assert.Null(result);
        repo.DidNotReceive().GetBySlug(Arg.Any<string>());
    }

    /// <summary>
    /// A real slug reaches the repository verbatim.
    /// </summary>
    [Fact]
    public void GetCategoryBySlugForwardsSlug()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var category = ValidCategory(6);
        repo.GetBySlug("web-development").Returns(category);

        // Act
        var result = BuildService(repo).GetCategoryBySlug("web-development");

        // Assert
        Assert.Same(category, result);
    }

    /// <summary>
    /// A failed slug read renders the category page's not-found state rather than throwing.
    /// </summary>
    [Fact]
    public void GetCategoryBySlugReturnsNullOnRepositoryFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.When(r => r.GetBySlug("web-development")).Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var result = BuildService(repo).GetCategoryBySlug("web-development");

        // Assert
        Assert.Null(result);
    }

    // =============================================================================================
    // CreateCategory
    // =============================================================================================

    /// <summary>
    /// A null category is refused as a failed result rather than a null-reference exception.
    /// </summary>
    [Fact]
    public void CreateCategoryRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();

        // Act
        var result = BuildService(repo).CreateCategory(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// A name is mandatory, and whitespace does not count as one.
    /// </summary>
    /// <param name="categoryName">The unusable name supplied by the administrator.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateCategoryRejectsBlankName(string categoryName)
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();

        // Act
        var result = BuildService(repo).CreateCategory(new Category { CategoryName = categoryName });

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category name is required", result.ErrorMessage);
        repo.DidNotReceive().InsertToGetId(Arg.Any<Category>());
    }

    /// <summary>
    /// A blank slug is derived from the name, so callers hand over a category and never think about
    /// URLs.
    /// </summary>
    [Fact]
    public void CreateCategoryDerivesSlugFromName()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var category = ValidCategory();

        // Act
        var result = BuildService(repo).CreateCategory(category);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("web-development", category.Slug);
    }

    /// <summary>
    /// A slug the administrator typed is kept, because it is the public category address and their
    /// choice is deliberate.
    /// </summary>
    [Fact]
    public void CreateCategoryKeepsSuppliedSlug()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var category = ValidCategory();
        category.Slug = "hand-picked";

        // Act
        BuildService(repo).CreateCategory(category);

        // Assert
        Assert.Equal("hand-picked", category.Slug);
    }

    /// <summary>
    /// A slug already in use gains an ordinal suffix so two categories can never share an address.
    /// </summary>
    [Fact]
    public void CreateCategorySuffixesDuplicateSlug()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.SlugExists("web-development").Returns(true);
        var category = ValidCategory();

        // Act
        BuildService(repo).CreateCategory(category);

        // Assert
        Assert.Equal("web-development-2", category.Slug);
    }

    /// <summary>
    /// When the first suffixed candidate is also taken the loop keeps climbing until it finds a free
    /// slug.
    /// </summary>
    [Fact]
    public void CreateCategoryRetriesUntilSlugIsFree()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.SlugExists("web-development").Returns(true);
        repo.SlugExists("web-development-2").Returns(true);
        var category = ValidCategory();

        // Act
        BuildService(repo).CreateCategory(category);

        // Assert
        Assert.Equal("web-development-3", category.Slug);
    }

    /// <summary>
    /// A pathological collision cannot spin forever: the retry loop is capped and leaves the last
    /// candidate for the database's unique constraint to arbitrate.
    /// </summary>
    [Fact]
    public void CreateCategoryStopsSuffixingAtTheAttemptCap()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.SlugExists(Arg.Any<string>()).Returns(true);
        var category = ValidCategory();

        // Act
        BuildService(repo).CreateCategory(category);

        // Assert
        Assert.Equal("web-development-100", category.Slug);
        repo.Received(100).SlugExists(Arg.Any<string>());
    }

    /// <summary>
    /// The generated key is written back onto the caller's object and travels out on the successful
    /// result.
    /// </summary>
    [Fact]
    public void CreateCategoryAssignsGeneratedIdentifier()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.InsertToGetId(Arg.Any<Category>()).Returns(14L);
        var category = ValidCategory();

        // Act
        var result = BuildService(repo).CreateCategory(category);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(14, category.CategoryId);
        Assert.Same(category, result.Data);
    }

    /// <summary>
    /// An unexpected persistence error is converted into a failed result rather than escaping to the
    /// admin page.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public void CreateCategoryReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var logger = new RecordingLogger<CategorySvc>();
        repo.When(r => r.InsertToGetId(Arg.Any<Category>()))
            .Do(_ => throw new InvalidOperationException("duplicate key"));

        // Act
        var result = BuildService(repo, logger).CreateCategory(ValidCategory());

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create category. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("duplicate key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "duplicate key");
    }

    // =============================================================================================
    // UpdateCategory / SaveCategory / DeleteCategory
    // =============================================================================================

    /// <summary>
    /// A null category is refused before anything is read or written.
    /// </summary>
    [Fact]
    public void UpdateCategoryRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();

        // Act
        var result = BuildService(repo).UpdateCategory(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// An edit needs a key; a non-positive identifier is a caller error rather than an insert.
    /// </summary>
    /// <param name="categoryId">The unusable identifier.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-9)]
    public void UpdateCategoryRejectsNonPositiveIdentifier(long categoryId)
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();

        // Act
        var result = BuildService(repo).UpdateCategory(ValidCategory(categoryId));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid category ID", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<Category>());
    }

    /// <summary>
    /// The name rule applies to an edit exactly as it does to a creation.
    /// </summary>
    [Fact]
    public void UpdateCategoryRejectsBlankName()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var category = ValidCategory(6);
        category.CategoryName = " ";

        // Act
        var result = BuildService(repo).UpdateCategory(category);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category name is required", result.ErrorMessage);
    }

    /// <summary>
    /// Editing a category deleted in another tab reports "not found" instead of a success that
    /// updated nothing.
    /// </summary>
    [Fact]
    public void UpdateCategoryRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetSingle(6).Returns((Category?)null);

        // Act
        var result = BuildService(repo).UpdateCategory(ValidCategory(6));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category not found", result.ErrorMessage);
        repo.DidNotReceive().Update(Arg.Any<Category>());
    }

    /// <summary>
    /// The uniqueness check excludes the row being edited, so re-saving an unchanged category keeps
    /// its slug — and therefore its public URL.
    /// </summary>
    [Fact]
    public void UpdateCategoryExcludesItselfFromSlugCheck()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetSingle(6).Returns(ValidCategory(6));
        var category = ValidCategory(6);
        category.Slug = "web-development";

        // Act
        var result = BuildService(repo).UpdateCategory(category);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("web-development", category.Slug);
        repo.Received(1).SlugExists("web-development", 6);
        repo.Received(1).Update(category);
    }

    /// <summary>
    /// A blank slug on an edit is regenerated from the current name.
    /// </summary>
    [Fact]
    public void UpdateCategoryDerivesSlugFromNameWhenBlank()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetSingle(6).Returns(ValidCategory(6));
        var category = ValidCategory(6);
        category.CategoryName = "Cloud Native";

        // Act
        BuildService(repo).UpdateCategory(category);

        // Assert
        Assert.Equal("cloud-native", category.Slug);
    }

    /// <summary>
    /// A slug that genuinely belongs to another category is suffixed, exactly as on creation.
    /// </summary>
    [Fact]
    public void UpdateCategorySuffixesCollidingSlug()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetSingle(6).Returns(ValidCategory(6));
        repo.SlugExists("web-development", 6).Returns(true);
        var category = ValidCategory(6);

        // Act
        BuildService(repo).UpdateCategory(category);

        // Assert
        Assert.Equal("web-development-2", category.Slug);
    }

    /// <summary>
    /// When the first suffixed candidate is also taken on an edit, the loop keeps climbing, and the
    /// exclusion of the edited row applies to every attempt rather than only the first.
    /// </summary>
    [Fact]
    public void UpdateCategoryRetriesUntilSlugIsFree()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetSingle(6).Returns(ValidCategory(6));
        repo.SlugExists("web-development", 6).Returns(true);
        repo.SlugExists("web-development-2", 6).Returns(true);
        var category = ValidCategory(6);

        // Act
        BuildService(repo).UpdateCategory(category);

        // Assert
        Assert.Equal("web-development-3", category.Slug);
    }

    /// <summary>
    /// An unexpected persistence error on the update is converted into a failed result.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public void UpdateCategoryReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var logger = new RecordingLogger<CategorySvc>();
        repo.GetSingle(6).Returns(ValidCategory(6));
        repo.When(r => r.Update(Arg.Any<Category>())).Do(_ => throw new InvalidOperationException("deadlock"));

        // Act
        var result = BuildService(repo, logger).UpdateCategory(ValidCategory(6));

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update category. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("deadlock", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "deadlock");
    }

    /// <summary>
    /// The save entry point refuses a null category before deciding between insert and update.
    /// </summary>
    [Fact]
    public void SaveCategoryRejectsNull()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();

        // Act
        var result = BuildService(repo).SaveCategory(null!);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category cannot be null", result.ErrorMessage);
    }

    /// <summary>
    /// One editor form serves both add and edit: a non-positive identifier means "new" and inserts,
    /// while a real one updates.
    /// </summary>
    [Fact]
    public void SaveCategoryRoutesOnIdentifier()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetSingle(6).Returns(ValidCategory(6));
        var service = BuildService(repo);

        // Act
        service.SaveCategory(ValidCategory());
        service.SaveCategory(ValidCategory(6));

        // Assert
        repo.Received(1).InsertToGetId(Arg.Any<Category>());
        repo.Received(1).Update(Arg.Any<Category>());
    }

    /// <summary>
    /// A non-positive identifier cannot name a row and is refused before any read.
    /// </summary>
    [Fact]
    public void DeleteCategoryRejectsNonPositiveIdentifier()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();

        // Act
        var result = BuildService(repo).DeleteCategory(0);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Invalid category ID", result.ErrorMessage);
        repo.DidNotReceive().GetSingle(Arg.Any<long>());
    }

    /// <summary>
    /// Deleting a category that is not there reports "not found" rather than a success that removed
    /// nothing.
    /// </summary>
    [Fact]
    public void DeleteCategoryRejectsMissingRow()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        repo.GetSingle(6).Returns((Category?)null);

        // Act
        var result = BuildService(repo).DeleteCategory(6);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Category not found", result.ErrorMessage);
        repo.DidNotReceive().Delete(Arg.Any<long>());
    }

    /// <summary>
    /// Unlike a post, a category is hard-deleted — there is no soft-delete flag on the taxonomy — and
    /// the service makes no attempt to reassign the posts filed under it first.
    /// </summary>
    [Fact]
    public void DeleteCategoryHardDeletesTheRow()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var existing = ValidCategory(6);
        existing.PostCount = 4;
        repo.GetSingle(6).Returns(existing);

        // Act
        var result = BuildService(repo).DeleteCategory(6);

        // Assert
        Assert.True(result.IsSuccess);
        repo.Received(1).Delete(6);
    }

    /// <summary>
    /// A foreign-key violation raised by the schema is converted into a failed result carrying the
    /// reason rather than escaping to the page.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public void DeleteCategoryReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var logger = new RecordingLogger<CategorySvc>();
        repo.GetSingle(6).Returns(ValidCategory(6));
        repo.When(r => r.Delete(6)).Do(_ => throw new InvalidOperationException("foreign key"));

        // Act
        var result = BuildService(repo, logger).DeleteCategory(6);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete category. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("foreign key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "foreign key");
    }

    // =============================================================================================
    // Async surface — REQ-NFR-026
    // =============================================================================================

    /// <summary>
    /// The async listing forwards to the repository and degrades to an empty sequence on failure.
    /// </summary>
    [Fact]
    public async Task GetAllCategoriesAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        var categories = new[] { ValidCategory(1) };
        repo.GetAllAsync(Arg.Any<CancellationToken>()).Returns(categories);

        var failing = Substitute.For<ICategoryRepo>();
        failing.When(r => r.GetAllAsync(Arg.Any<CancellationToken>()))
               .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var good = await BuildService(repo).GetAllCategoriesAsync(token);
        var bad = await BuildService(failing).GetAllCategoriesAsync(token);

        // Assert
        Assert.Same(categories, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// The async counted listing forwards to the repository and degrades to empty on failure.
    /// </summary>
    [Fact]
    public async Task GetAllWithCountsAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        var categories = new[] { ValidCategory(1) };
        repo.GetAllWithCountsAsync(Arg.Any<CancellationToken>()).Returns(categories);

        var failing = Substitute.For<ICategoryRepo>();
        failing.When(r => r.GetAllWithCountsAsync(Arg.Any<CancellationToken>()))
               .Do(_ => throw new InvalidOperationException("boom"));

        // Act
        var good = await BuildService(repo).GetAllWithCountsAsync(token);
        var bad = await BuildService(failing).GetAllWithCountsAsync(token);

        // Assert
        Assert.Same(categories, good);
        Assert.Empty(bad);
    }

    /// <summary>
    /// The async id lookup forwards the identifier and answers null when the read fails.
    /// </summary>
    [Fact]
    public async Task GetCategoryAsyncForwardsAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        var category = ValidCategory(6);
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(category);
        repo.When(r => r.GetSingleAsync(7, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var found = await service.GetCategoryAsync(6, token);
        var failed = await service.GetCategoryAsync(7, token);

        // Assert
        Assert.Same(category, found);
        Assert.Null(failed);
    }

    /// <summary>
    /// The async slug lookup keeps the blank-slug guard, forwards a real slug, and answers null when
    /// the read fails.
    /// </summary>
    [Fact]
    public async Task GetCategoryBySlugAsyncGuardsBlankAndDegradesOnFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        var category = ValidCategory(6);
        repo.GetBySlugAsync("web-development", Arg.Any<CancellationToken>()).Returns(category);
        repo.When(r => r.GetBySlugAsync("broken", Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("boom"));
        var service = BuildService(repo);

        // Act
        var blank = await service.GetCategoryBySlugAsync("   ", token);
        var found = await service.GetCategoryBySlugAsync("web-development", token);
        var failed = await service.GetCategoryBySlugAsync("broken", token);

        // Assert
        Assert.Null(blank);
        Assert.Same(category, found);
        Assert.Null(failed);
        await repo.DidNotReceive().GetBySlugAsync("   ", Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async creation applies the same guards, derives the same slug, resolves a collision with
    /// the same ordinal suffix, and writes the generated key back onto the caller's object.
    /// </summary>
    [Fact]
    public async Task CreateCategoryAsyncValidatesAndResolvesSlug()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.SlugExistsAsync("web-development", 0, Arg.Any<CancellationToken>()).Returns(true);
        repo.InsertToGetIdAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>()).Returns(14L);
        var category = ValidCategory();
        var service = BuildService(repo);

        // Act
        var nullResult = await service.CreateCategoryAsync(null!, token);
        var blankResult = await service.CreateCategoryAsync(new Category { CategoryName = " " }, token);
        var result = await service.CreateCategoryAsync(category, token);

        // Assert
        Assert.Equal("Category cannot be null", nullResult.ErrorMessage);
        Assert.Equal("Category name is required", blankResult.ErrorMessage);
        Assert.True(result.IsSuccess);
        Assert.Equal("web-development-2", category.Slug);
        Assert.Equal(14, category.CategoryId);
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
    public async Task CreateCategoryAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var logger = new RecordingLogger<CategorySvc>();
        repo.When(r => r.InsertToGetIdAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("duplicate key"));

        // Act
        var result = await BuildService(repo, logger).CreateCategoryAsync(ValidCategory(), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to create category. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("duplicate key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "duplicate key");
    }

    /// <summary>
    /// The async update applies its guards in the documented order and excludes the edited row from
    /// the uniqueness check.
    /// </summary>
    [Fact]
    public async Task UpdateCategoryAsyncGuardsAndExcludesItselfFromSlugCheck()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(ValidCategory(6));
        var blankName = ValidCategory(6);
        blankName.CategoryName = "";
        var category = ValidCategory(6);
        category.Slug = "web-development";
        var service = BuildService(repo);

        // Act
        var nullResult = await service.UpdateCategoryAsync(null!, token);
        var idResult = await service.UpdateCategoryAsync(ValidCategory(0), token);
        var nameResult = await service.UpdateCategoryAsync(blankName, token);
        var missingResult = await service.UpdateCategoryAsync(ValidCategory(9), token);
        var okResult = await service.UpdateCategoryAsync(category, token);

        // Assert
        Assert.Equal("Category cannot be null", nullResult.ErrorMessage);
        Assert.Equal("Invalid category ID", idResult.ErrorMessage);
        Assert.Equal("Category name is required", nameResult.ErrorMessage);
        Assert.Equal("Category not found", missingResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        await repo.Received(1).SlugExistsAsync("web-development", 6, Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateAsync(category, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unexpected persistence error on the async update becomes a failed result.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public async Task UpdateCategoryAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var logger = new RecordingLogger<CategorySvc>();
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(ValidCategory(6));
        repo.When(r => r.UpdateAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("deadlock"));

        // Act
        var result = await BuildService(repo, logger).UpdateCategoryAsync(ValidCategory(6), TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to update category. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("deadlock", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "deadlock");
    }

    /// <summary>
    /// The async save refuses a null category and otherwise routes on the identifier — insert without
    /// one, update with one.
    /// </summary>
    [Fact]
    public async Task SaveCategoryAsyncRoutesOnIdentifier()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(ValidCategory(6));
        var service = BuildService(repo);

        // Act
        var nullResult = await service.SaveCategoryAsync(null!, token);
        await service.SaveCategoryAsync(ValidCategory(), token);
        await service.SaveCategoryAsync(ValidCategory(6), token);

        // Assert
        Assert.Equal("Category cannot be null", nullResult.ErrorMessage);
        await repo.Received(1).InsertToGetIdAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>());
        await repo.Received(1).UpdateAsync(Arg.Any<Category>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The async delete refuses a bad identifier and a missing row, and hard-deletes an existing one.
    /// </summary>
    [Fact]
    public async Task DeleteCategoryAsyncAppliesTheSameGuards()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var token = TestContext.Current.CancellationToken;
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(ValidCategory(6));
        var service = BuildService(repo);

        // Act
        var idResult = await service.DeleteCategoryAsync(0, token);
        var missingResult = await service.DeleteCategoryAsync(9, token);
        var okResult = await service.DeleteCategoryAsync(6, token);

        // Assert
        Assert.Equal("Invalid category ID", idResult.ErrorMessage);
        Assert.Equal("Category not found", missingResult.ErrorMessage);
        Assert.True(okResult.IsSuccess);
        await repo.Received(1).DeleteAsync(6, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An unexpected persistence error on the async delete becomes a failed result.
    /// The message the caller sees is the curated sentence and never the exception text, while
    /// the exception itself is written to the log — that split is REQ-NFR-031, and both halves
    /// are asserted here because checking only the new wording would let the disclosure regress
    /// silently.
    /// </summary>
    [Fact]
    public async Task DeleteCategoryAsyncReportsPersistenceFailure()
    {
        // Arrange
        var repo = Substitute.For<ICategoryRepo>();
        var logger = new RecordingLogger<CategorySvc>();
        repo.GetSingleAsync(6, Arg.Any<CancellationToken>()).Returns(ValidCategory(6));
        repo.When(r => r.DeleteAsync(6, Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("foreign key"));

        // Act
        var result = await BuildService(repo, logger).DeleteCategoryAsync(6, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsFailure);
        Assert.Equal("Failed to delete category. Please try again later.", result.ErrorMessage);
        Assert.DoesNotContain("foreign key", result.ErrorMessage);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Error?.Message == "foreign key");
    }
}
