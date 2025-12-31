using Markdig;

namespace BlogEngine.Common;

/// <summary>
/// Service for converting Markdown text to HTML.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides server-side Markdown to HTML conversion using Markdig.</para>
/// <para><b>Usage:</b> Inject as singleton, call ToHtml() with markdown content.</para>
/// </remarks>
public class MarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline;

    public MarkdownRenderer()
    {
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoLinks()
            .UseTaskLists()
            .UsePipeTables()
            .UseAutoIdentifiers()
            .UseEmphasisExtras()
            .UseFootnotes()
            .Build();
    }

    /// <summary>
    /// Converts Markdown text to HTML.
    /// </summary>
    /// <param name="markdown">The markdown content to convert.</param>
    /// <returns>HTML string, or empty string if input is null/empty.</returns>
    public string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, _pipeline);
    }

    /// <summary>
    /// Converts Markdown text to plain text (strips formatting).
    /// </summary>
    /// <param name="markdown">The markdown content to convert.</param>
    /// <returns>Plain text string.</returns>
    public string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToPlainText(markdown, _pipeline);
    }
}
