using BlogEngine.Common;

namespace TechieBlog.Tests.Common;

/// <summary>
/// Unit tests for the slug-allocation primitives added under REQ-FN-054 —
/// <see cref="SlugGenerator.EnsureSlug"/>, <see cref="SlugGenerator.BuildFallbackSlug"/> and the two
/// <c>ResolveUniqueSlug</c> members.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Two defects lived in the sixteen hand-written collision loops these members
/// replace. The first threw away an author's supplied slug from the second retry onwards, because
/// only the first candidate was derived from it and every later one was re-derived from the title.
/// The second persisted an empty slug whenever a title was punctuation-only or written in a
/// non-Latin script, leaving the row with no URL at all — even though <c>GenerateSlug</c>'s own
/// documentation told callers to substitute an id-based address. Each test below fails against the
/// old behaviour, which is the only way it proves anything.</para>
/// <para><b>Dependencies:</b> xUnit v3. Pure functions — no repository, no database, no clock.</para>
/// </remarks>
public class SlugAllocationTests
{
    /// <summary>
    /// The base slug is offered before any suffix, so the uncontended case keeps the clean URL that
    /// the author or the title asked for.
    /// </summary>
    [Fact]
    public void ResolveUniqueSlugKeepsTheBaseWhenItIsFree()
    {
        // Arrange
        var probed = new List<string>();

        // Act
        var slug = SlugGenerator.ResolveUniqueSlug("my-title", candidate =>
        {
            probed.Add(candidate);
            return false;
        });

        // Assert
        Assert.Equal("my-title", slug);
        Assert.Equal(new[] { "my-title" }, probed);
    }

    /// <summary>
    /// THE REQ-FN-054 REGRESSION TEST. Every candidate after the first must still be built from the
    /// author's supplied slug. Under the old loop the first retry produced <c>hand-picked-2</c> and
    /// then every later attempt silently switched to the title-derived <c>my-title-N</c>, so an author
    /// who chose their own URL lost it the moment two of them collided.
    /// </summary>
    [Fact]
    public void ResolveUniqueSlugSuffixesTheSuppliedBaseOnEveryAttempt()
    {
        // Arrange — the base and its first two suffixes are all taken.
        var taken = new HashSet<string> { "hand-picked", "hand-picked-2", "hand-picked-3" };
        var probed = new List<string>();

        // Act
        var slug = SlugGenerator.ResolveUniqueSlug("hand-picked", candidate =>
        {
            probed.Add(candidate);
            return taken.Contains(candidate);
        });

        // Assert
        Assert.Equal("hand-picked-4", slug);
        Assert.Equal(new[] { "hand-picked", "hand-picked-2", "hand-picked-3", "hand-picked-4" }, probed);
        Assert.All(probed, candidate => Assert.StartsWith("hand-picked", candidate));
    }

    /// <summary>
    /// The attempt budget is the base plus ninety-nine suffixes, and the last candidate is returned
    /// even though the probe reported it taken — the unique index is the real guard, and returning
    /// rather than throwing preserves the behaviour of the loops this replaced.
    /// </summary>
    [Fact]
    public void ResolveUniqueSlugStopsAtTheAttemptBudget()
    {
        // Arrange
        var probes = 0;

        // Act
        var slug = SlugGenerator.ResolveUniqueSlug("my-title", _ =>
        {
            probes++;
            return true;
        });

        // Assert
        Assert.Equal(SlugGenerator.MaxSlugAttempts, probes);
        Assert.Equal("my-title-100", slug);
    }

    /// <summary>
    /// The asynchronous twin allocates exactly the same slug from the same inputs, so a service's
    /// blocking and awaiting members cannot drift apart (REQ-NFR-026 keeps them behaviourally
    /// identical).
    /// </summary>
    [Fact]
    public async Task ResolveUniqueSlugAsyncMatchesTheSynchronousTwin()
    {
        // Arrange
        var taken = new HashSet<string> { "hand-picked", "hand-picked-2" };

        // Act
        var blocking = SlugGenerator.ResolveUniqueSlug("hand-picked", taken.Contains);
        var awaiting = await SlugGenerator.ResolveUniqueSlugAsync(
            "hand-picked",
            candidate => Task.FromResult(taken.Contains(candidate)));

        // Assert
        Assert.Equal("hand-picked-3", blocking);
        Assert.Equal(blocking, awaiting);
    }

    /// <summary>
    /// A supplied slug wins over the title, and surrounding whitespace is the only thing trimmed from
    /// it — the author chose the URL and nothing here is entitled to rewrite it.
    /// </summary>
    [Fact]
    public void EnsureSlugPrefersTheSuppliedSlug()
    {
        // Act
        var slug = SlugGenerator.EnsureSlug("  hand-picked  ", "A Completely Different Title", "post");

        // Assert
        Assert.Equal("hand-picked", slug);
    }

    /// <summary>
    /// With no supplied slug the title is used, exactly as before.
    /// </summary>
    [Fact]
    public void EnsureSlugFallsBackToTheTitle()
    {
        // Act
        var slug = SlugGenerator.EnsureSlug(null, "C# — Tips & Tricks!", "post");

        // Assert
        Assert.Equal("c-tips-tricks", slug);
    }

    /// <summary>
    /// THE SECOND REQ-FN-054 REGRESSION TEST. A title written entirely in a non-Latin script produces
    /// an empty string from <c>GenerateSlug</c>; the old services persisted that empty string and the
    /// row became unaddressable. An identifier-based address is substituted instead.
    /// </summary>
    /// <param name="title">A title the ASCII allow-list rejects in its entirety.</param>
    [Theory]
    [InlineData("日本語のタイトル")]
    [InlineData("Заголовок")]
    [InlineData("!!! ??? ...")]
    [InlineData("###")]
    public void EnsureSlugNeverReturnsEmptyForAnUnslugabbleTitle(string title)
    {
        // Act — first as an insert with no identifier, then as an update that has one.
        var onInsert = SlugGenerator.EnsureSlug(null, title, "post");
        var onUpdate = SlugGenerator.EnsureSlug(null, title, "post", 42);

        // Assert
        Assert.Empty(SlugGenerator.GenerateSlug(title));
        Assert.NotEmpty(onInsert);
        Assert.StartsWith("post-", onInsert);
        Assert.Equal("post-42", onUpdate);
    }

    /// <summary>
    /// The insert-time fallback is derived from the title rather than being random, so the same
    /// unslugabble name always resolves to the same address. <c>TagSvc.GetOrCreateTag</c> depends on
    /// this: it matches an existing tag by slug, and a random fallback would mint a duplicate row on
    /// every save.
    /// </summary>
    [Fact]
    public void EnsureSlugFallbackIsDeterministicForTheSameTitle()
    {
        // Act
        var first = SlugGenerator.EnsureSlug(null, "日本語のタイトル", "tag");
        var second = SlugGenerator.EnsureSlug(null, "日本語のタイトル", "tag");
        var different = SlugGenerator.EnsureSlug(null, "Другой", "tag");

        // Assert
        Assert.Equal(first, second);
        Assert.NotEqual(first, different);
        Assert.StartsWith("tag-", first);
    }

    /// <summary>
    /// The fallback is itself URL-safe: whatever the prefix and the seed, the result matches the same
    /// allow-list <c>GenerateSlug</c> enforces, so substituting it cannot introduce a character that
    /// would need percent-encoding.
    /// </summary>
    [Fact]
    public void BuildFallbackSlugIsUrlSafe()
    {
        // Act
        var withId = SlugGenerator.BuildFallbackSlug("post", "日本語", 42);
        var withoutId = SlugGenerator.BuildFallbackSlug("post", "日本語", 0);
        var blankPrefix = SlugGenerator.BuildFallbackSlug("   ", "日本語", 0);

        // Assert
        Assert.Equal("post-42", withId);
        Assert.Equal(withoutId, SlugGenerator.GenerateSlug(withoutId));
        Assert.StartsWith("item-", blankPrefix);
    }
}
