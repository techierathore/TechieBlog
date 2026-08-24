using Bunit;
using BlogUI.Components;
using Xunit;

namespace TechieBlog.Tests.Components.BlogUi;

/// <summary>
/// bUnit component tests for the post detail page's table-of-contents rail (REQ-UI-045 / UAT-027).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the two behavioural rules <c>PostTocRail</c> owns on top of
/// TrBlazeUI's <c>AnchorNav</c>: it renders nothing at all when there are fewer than two headings —
/// a one-entry TOC is noise, not navigation — and a level-3 heading's label carries a leading
/// non-breaking-space indent so the rendered list visually distinguishes h2 from h3 entries, the
/// only lever available since <c>AnchorNavSection</c> has no heading-level parameter of its own
/// (filed as TR-074).</para>
/// <para><b>Dependencies:</b> BlogUI, therefore TrBlazeUI. <c>JSInterop</c> is set to Loose and the
/// rail's own click-interception module is stubbed via <c>SetupModule</c>, since rendering with two
/// or more headings triggers <c>PostTocRail.OnAfterRenderAsync</c>'s JS import — this suite pins
/// markup, not the JS interop plumbing. Compiles only under <c>-p:IncludeBlogUiTests=true</c>.</para>
/// </remarks>
public class PostTocRailTests : BunitContext
{
    /// <summary>
    /// Configures a Loose JSInterop and stubs the rail's own JS module so rendering a rail with
    /// headings does not throw on the click-interceptor import inside OnAfterRenderAsync.
    /// </summary>
    public PostTocRailTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupModule("/_content/BlogUI/js/post-toc-rail.js");
    }

    /// <summary>
    /// A single heading is not a table of contents — the rail renders nothing rather than a
    /// one-item list with nowhere else to go.
    /// </summary>
    [Fact]
    public void RailRendersNothingWithOnlyOneHeading()
    {
        // Arrange, Act
        var cut = Render<PostTocRail>(parameters => parameters
            .Add(rail => rail.Headings, new List<TocHeading> { new("only-one", "Only One", 2) }));

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='post-toc-rail']"));
    }

    /// <summary>
    /// No headings at all renders nothing, the same as the missing-Headings default.
    /// </summary>
    [Fact]
    public void RailRendersNothingWithNoHeadings()
    {
        // Arrange, Act
        var cut = Render<PostTocRail>(parameters => parameters
            .Add(rail => rail.Headings, new List<TocHeading>()));

        // Assert
        Assert.Empty(cut.FindAll("[data-testid='post-toc-rail']"));
    }

    /// <summary>
    /// Two or more headings render one anchor per heading, each pointing at that heading's own id —
    /// the contract PostView relies on to jump the reader to the right place in the article.
    /// </summary>
    [Fact]
    public void RailRendersOneLinkPerHeadingWithTwoOrMore()
    {
        // Arrange
        var headings = new List<TocHeading>
        {
            new("intro", "Introduction", 2),
            new("details", "Details", 2),
        };

        // Act
        var cut = Render<PostTocRail>(parameters => parameters
            .Add(rail => rail.Headings, headings));

        // Assert
        Assert.Single(cut.FindAll("[data-testid='post-toc-rail']"));
        var links = cut.FindAll("[data-testid='post-toc-rail-nav'] a").ToList();
        Assert.Equal(2, links.Count);
        Assert.Equal("#intro", links[0].GetAttribute("href"));
        Assert.Equal("#details", links[1].GetAttribute("href"));
    }

    /// <summary>
    /// A level-3 heading's label is indented relative to its level-2 sibling, so the rail's visual
    /// hierarchy survives even though AnchorNavSection carries no heading-level parameter of its own.
    /// </summary>
    [Fact]
    public void RailIndentsLevelThreeHeadingLabels()
    {
        // Arrange
        var headings = new List<TocHeading>
        {
            new("parent", "Parent Section", 2),
            new("child", "Child Section", 3),
        };

        // Act
        var cut = Render<PostTocRail>(parameters => parameters
            .Add(rail => rail.Headings, headings));

        // Assert
        var links = cut.FindAll("[data-testid='post-toc-rail-nav'] a").ToList();
        Assert.Equal("Parent Section", links[0].TextContent.Trim());
        Assert.StartsWith(" ", links[1].TextContent);
        Assert.Contains("Child Section", links[1].TextContent);
    }
}
