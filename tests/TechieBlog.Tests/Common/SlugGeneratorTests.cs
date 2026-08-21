using BlogEngine.Common;

namespace TechieBlog.Tests.Common;

/// <summary>
/// Unit tests for <see cref="SlugGenerator"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Locks down the URL-slug transformation rules that every
/// published post URL depends on — lowercasing, special-character stripping,
/// whitespace collapsing and hyphen trimming.</para>
/// <para><b>Dependencies:</b> None. <see cref="SlugGenerator"/> is a pure static helper.</para>
/// </remarks>
public class SlugGeneratorTests
{
    /// <summary>
    /// A plain multi-word title is lowercased and its spaces become single hyphens.
    /// </summary>
    [Fact]
    public void GenerateSlugLowercasesAndHyphenatesWords()
    {
        // Arrange
        var title = "My Blog Post Title";

        // Act
        var slug = SlugGenerator.GenerateSlug(title);

        // Assert
        Assert.Equal("my-blog-post-title", slug);
    }

    /// <summary>
    /// Punctuation and other non alphanumeric characters are dropped entirely
    /// rather than being replaced by hyphens.
    /// </summary>
    [Fact]
    public void GenerateSlugRemovesSpecialCharacters()
    {
        // Arrange
        var title = "C# 13: What's New?!";

        // Act
        var slug = SlugGenerator.GenerateSlug(title);

        // Assert
        Assert.Equal("c-13-whats-new", slug);
    }

    /// <summary>
    /// Runs of whitespace collapse to exactly one hyphen, never a run of hyphens.
    /// </summary>
    [Fact]
    public void GenerateSlugCollapsesRepeatedWhitespace()
    {
        // Arrange
        var title = "Hello    world\t\tagain";

        // Act
        var slug = SlugGenerator.GenerateSlug(title);

        // Assert
        Assert.Equal("hello-world-again", slug);
    }

    /// <summary>
    /// Leading and trailing separators produced by the transformation are trimmed
    /// so the slug never starts or ends with a hyphen.
    /// </summary>
    [Fact]
    public void GenerateSlugTrimsLeadingAndTrailingHyphens()
    {
        // Arrange
        var title = "  --- Edge Case ---  ";

        // Act
        var slug = SlugGenerator.GenerateSlug(title);

        // Assert
        Assert.Equal("edge-case", slug);
    }

    /// <summary>
    /// Null, empty and whitespace-only titles yield an empty slug instead of throwing,
    /// so callers can fall back to an id-based URL.
    /// </summary>
    /// <param name="title">The blank title under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GenerateSlugReturnsEmptyForBlankTitle(string title)
    {
        // Arrange, Act
        var slug = SlugGenerator.GenerateSlug(title);

        // Assert
        Assert.Equal(string.Empty, slug);
    }

    /// <summary>
    /// A title made up only of characters the filter removes collapses to an empty
    /// slug rather than a lone hyphen.
    /// </summary>
    [Fact]
    public void GenerateSlugReturnsEmptyWhenAllCharactersFiltered()
    {
        // Arrange
        var title = "!!! ??? ***";

        // Act
        var slug = SlugGenerator.GenerateSlug(title);

        // Assert
        Assert.Equal(string.Empty, slug);
    }

    /// <summary>
    /// When no duplicates exist the base slug is returned untouched — no "-1" suffix.
    /// </summary>
    [Fact]
    public void GenerateUniqueSlugKeepsBaseWhenNoDuplicates()
    {
        // Arrange
        var baseSlug = "my-post";

        // Act
        var slug = SlugGenerator.GenerateUniqueSlug(baseSlug, 0);

        // Assert
        Assert.Equal("my-post", slug);
    }

    /// <summary>
    /// With one existing post sharing the slug, the next slug is suffixed "-2"
    /// (the count plus one), so numbering reads naturally to a human.
    /// </summary>
    [Fact]
    public void GenerateUniqueSlugAppendsNextOrdinal()
    {
        // Arrange
        var baseSlug = "my-post";

        // Act
        var slug = SlugGenerator.GenerateUniqueSlug(baseSlug, 1);

        // Assert
        Assert.Equal("my-post-2", slug);
    }

    /// <summary>
    /// A negative existing-count is treated as "no duplicates" and returns the base slug.
    /// </summary>
    [Fact]
    public void GenerateUniqueSlugIgnoresNegativeCount()
    {
        // Arrange
        var baseSlug = "my-post";

        // Act
        var slug = SlugGenerator.GenerateUniqueSlug(baseSlug, -3);

        // Assert
        Assert.Equal("my-post", slug);
    }
}
