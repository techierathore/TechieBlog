using Bunit;
using BlogUI.Components;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// bUnit component tests for the shared <c>PostCard</c> listing card (REQ-UI-045).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Locks the card's image contract, which is what the 2026-08-09
/// verification round found broken: the home page passed <c>ImageUrl</c> and rendered real
/// cover art while the category and tag archives passed nothing and rendered grey
/// placeholders, so "PostCard renders identically in home, archive and search listings"
/// was false. The card itself was always correct — these tests pin the behaviour the
/// callers must feed, so a caller that forgets the parameter is a visible regression
/// rather than a silent one.</para>
/// <para><b>Dependencies:</b> BlogUI, therefore this suite compiles only under
/// <c>-p:IncludeBlogUiTests=true</c>; the csproj removes this folder otherwise.</para>
/// </remarks>
public class PostCardTests : BunitContext
{
    /// <summary>
    /// A card given a featured-image URL renders a real image element pointing at it,
    /// and no placeholder — the home-page behaviour the archives had to match.
    /// </summary>
    [Fact]
    public void PostCardRendersImageWhenUrlSupplied()
    {
        // Arrange, Act
        var cut = Render<PostCard>(parameters => parameters
            .Add(card => card.Title, "Blazor Render Modes Explained")
            .Add(card => card.ImageUrl, "/_content/BlogUI/images/HomeBg.jpg"));

        // Assert
        var image = cut.Find("[data-testid='post-card-image']");
        Assert.Equal("/_content/BlogUI/images/HomeBg.jpg", image.GetAttribute("src"));
        Assert.Empty(cut.FindAll("[data-testid='post-card-image-placeholder']"));
    }

    /// <summary>
    /// The alt text of the cover image is the post title, so the image is never an
    /// unlabelled graphic in a listing of links.
    /// </summary>
    [Fact]
    public void PostCardImageIsLabelledWithTheTitle()
    {
        // Arrange, Act
        var cut = Render<PostCard>(parameters => parameters
            .Add(card => card.Title, "Reading PostgreSQL Query Plans")
            .Add(card => card.ImageUrl, "/_content/BlogUI/images/Aboutbg.jpg"));

        // Assert
        Assert.Equal("Reading PostgreSQL Query Plans",
            cut.Find("[data-testid='post-card-image']").GetAttribute("alt"));
    }

    /// <summary>
    /// With no URL the card still renders a placeholder rather than a broken image, so a
    /// genuinely image-less post degrades instead of failing.
    /// </summary>
    [Fact]
    public void PostCardFallsBackToPlaceholderWithoutUrl()
    {
        // Arrange, Act
        var cut = Render<PostCard>(parameters => parameters
            .Add(card => card.Title, "A post with no cover")
            .Add(card => card.ImagePlaceholder, "No cover"));

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='post-card-image']"));
        Assert.Equal("No cover", cut.Find("[data-testid='post-card-image-placeholder']").TextContent.Trim());
    }

    /// <summary>
    /// An empty-string URL is treated as "no image" rather than as a valid source, which is
    /// what arrives from the database when FeaturedImage was never set.
    /// </summary>
    [Fact]
    public void PostCardTreatsEmptyUrlAsNoImage()
    {
        // Arrange, Act
        var cut = Render<PostCard>(parameters => parameters
            .Add(card => card.Title, "Empty featured image")
            .Add(card => card.ImageUrl, string.Empty));

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='post-card-image']"));
        Assert.Single(cut.FindAll("[data-testid='post-card-image-placeholder']"));
    }

    /// <summary>
    /// The card surfaces the supplied category name verbatim, which is the contract the
    /// search page broke by hardcoding the literal badge text "Blog" (REQ-UI-011).
    /// </summary>
    [Fact]
    public void PostCardRendersSuppliedCategoryName()
    {
        // Arrange, Act
        var cut = Render<PostCard>(parameters => parameters
            .Add(card => card.Title, "Indexing Basics for .NET Developers")
            .Add(card => card.Category, "Programming"));

        // Assert
        Assert.Equal("Programming", cut.Find("[data-testid='post-card-category']").TextContent.Trim());
    }
}
