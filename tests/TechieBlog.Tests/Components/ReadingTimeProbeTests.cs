using Bunit;

namespace TechieBlog.Tests.Components;

/// <summary>
/// bUnit component tests for <see cref="ReadingTimeProbe"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Proves the bUnit harness required by REQ-NFR-016 is wired
/// and working end to end — component rendering, parameter passing, markup
/// assertion via <c>data-testid</c> selectors, and event dispatch with re-render.</para>
/// <para><b>Dependencies:</b> bUnit's <see cref="BunitContext"/>. Deliberately does
/// NOT reference BlogUI, so this suite stays green while the UI layer is rewritten;
/// BlogUI component suites compile once the project is built with
/// <c>-p:IncludeBlogUiTests=true</c>.</para>
/// </remarks>
public class ReadingTimeProbeTests : BunitContext
{
    /// <summary>
    /// A rendered component shows the reading-time label produced by the engine
    /// helper for the content it was given.
    /// </summary>
    [Fact]
    public void ProbeRendersReadingTimeForSuppliedContent()
    {
        // Arrange
        var content = string.Join(' ', Enumerable.Repeat("word", 400));

        // Act
        var cut = Render<ReadingTimeProbe>(p => p.Add(c => c.Content, content));

        // Assert
        Assert.Equal("2 min read", cut.Find("[data-testid='probe-reading-time']").TextContent);
    }

    /// <summary>
    /// The word-count element reflects the same content, confirming parameters
    /// reach the component rather than a default being rendered.
    /// </summary>
    [Fact]
    public void ProbeRendersWordCountForSuppliedContent()
    {
        // Arrange
        var content = "alpha beta gamma";

        // Act
        var cut = Render<ReadingTimeProbe>(p => p.Add(c => c.Content, content));

        // Assert
        Assert.Equal("3", cut.Find("[data-testid='probe-word-count']").TextContent);
    }

    /// <summary>
    /// Rendering with no content supplied falls back to the one-minute floor, so
    /// an empty draft never displays "0 min read".
    /// </summary>
    [Fact]
    public void ProbeFallsBackToOneMinuteWithoutContent()
    {
        // Arrange, Act
        var cut = Render<ReadingTimeProbe>(p => p.Add(c => c.Content, string.Empty));

        // Assert
        Assert.Equal("1 min read", cut.Find("[data-testid='probe-reading-time']").TextContent);
    }

    /// <summary>
    /// Clicking the clear button dispatches the event handler and the component
    /// re-renders with the empty-content word count — proving bUnit event
    /// dispatch and re-render both work in this harness.
    /// </summary>
    [Fact]
    public void ProbeClearButtonResetsWordCount()
    {
        // Arrange
        var cut = Render<ReadingTimeProbe>(p => p.Add(c => c.Content, "alpha beta gamma"));

        // Act
        cut.Find("[data-testid='probe-clear']").Click();

        // Assert
        Assert.Equal("0", cut.Find("[data-testid='probe-word-count']").TextContent);
    }
}
