using BlogEngine.Common;

namespace TechieBlog.Tests.Common;

/// <summary>
/// Unit tests for <see cref="MarkdownRenderer"/>.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Confirms the Markdig pipeline the post editor and the
/// public post page share — core CommonMark rendering, the advanced extensions
/// that were explicitly opted into (pipe tables, auto links), and the plain-text
/// projection used for excerpts and meta descriptions.</para>
/// <para><b>Dependencies:</b> Markdig, via the renderer's own pipeline.</para>
/// </remarks>
public class MarkdownRendererTests
{
    private readonly MarkdownRenderer renderer = new();

    /// <summary>
    /// An ATX heading becomes an h1 element in the rendered HTML.
    /// </summary>
    [Fact]
    public void ToHtmlRendersHeading()
    {
        // Arrange
        var markdown = "# Title";

        // Act
        var html = renderer.ToHtml(markdown);

        // Assert
        Assert.Contains("<h1", html);
    }

    /// <summary>
    /// Double-asterisk emphasis renders as a strong element.
    /// </summary>
    [Fact]
    public void ToHtmlRendersStrongEmphasis()
    {
        // Arrange
        var markdown = "Some **bold** text.";

        // Act
        var html = renderer.ToHtml(markdown);

        // Assert
        Assert.Contains("<strong>bold</strong>", html);
    }

    /// <summary>
    /// A fenced code block renders as a pre/code pair so the syntax highlighter
    /// on the public page has something to attach to.
    /// </summary>
    [Fact]
    public void ToHtmlRendersFencedCodeBlock()
    {
        // Arrange
        var markdown = "```csharp\nvar x = 1;\n```";

        // Act
        var html = renderer.ToHtml(markdown);

        // Assert
        Assert.Contains("<pre>", html);
    }

    /// <summary>
    /// Pipe tables are enabled on the pipeline, so a pipe-delimited block renders
    /// as a real table element rather than a paragraph of pipes.
    /// </summary>
    [Fact]
    public void ToHtmlRendersPipeTable()
    {
        // Arrange
        var markdown = "| A | B |\n| - | - |\n| 1 | 2 |";

        // Act
        var html = renderer.ToHtml(markdown);

        // Assert
        Assert.Contains("<table", html);
    }

    /// <summary>
    /// Auto-links are enabled, so a bare URL in the body becomes an anchor without
    /// the author having to write link syntax.
    /// </summary>
    [Fact]
    public void ToHtmlAutoLinksBareUrl()
    {
        // Arrange
        var markdown = "Visit https://example.com for details.";

        // Act
        var html = renderer.ToHtml(markdown);

        // Assert
        Assert.Contains("<a href=\"https://example.com\"", html);
    }

    /// <summary>
    /// Blank markdown renders as an empty string rather than throwing or emitting
    /// an empty paragraph.
    /// </summary>
    /// <param name="markdown">The blank markdown under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToHtmlReturnsEmptyForBlankMarkdown(string markdown)
    {
        // Arrange, Act
        var html = renderer.ToHtml(markdown);

        // Assert
        Assert.Equal(string.Empty, html);
    }

    /// <summary>
    /// The plain-text projection strips emphasis markers, leaving only the words —
    /// the form used for excerpts and meta descriptions.
    /// </summary>
    [Fact]
    public void ToPlainTextStripsFormatting()
    {
        // Arrange
        var markdown = "# Heading\n\nSome **bold** and _italic_ text.";

        // Act
        var text = renderer.ToPlainText(markdown);

        // Assert
        Assert.DoesNotContain("**", text);
    }

    /// <summary>
    /// The plain-text projection keeps the words themselves intact.
    /// </summary>
    [Fact]
    public void ToPlainTextKeepsWords()
    {
        // Arrange
        var markdown = "Some **bold** text.";

        // Act
        var text = renderer.ToPlainText(markdown);

        // Assert
        Assert.Contains("bold", text);
    }

    /// <summary>
    /// Blank markdown projects to an empty string rather than throwing.
    /// </summary>
    [Fact]
    public void ToPlainTextReturnsEmptyForBlankMarkdown()
    {
        // Arrange, Act
        var text = renderer.ToPlainText("  ");

        // Assert
        Assert.Equal(string.Empty, text);
    }
}
