namespace BlogUI.Components;

/// <summary>
/// One entry in the post detail page's table-of-contents rail — an in-page heading's stable anchor
/// id, its display text and its markdown level.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Carries the data <c>PostView.razor</c> extracts from the rendered article
/// HTML (Markdig's own auto-generated heading ids, from <c>MarkdownRenderer</c>'s
/// <c>UseAutoIdentifiers()</c> pipeline step) through to <c>PostTocRail.razor</c>, which turns it
/// into a TrBlazeUI <c>AnchorNav</c> scrollspy list. Kept as its own type/file — one class per file,
/// per the Coding Standards — rather than a record nested inside either component, because both
/// <c>PostView</c> and <c>PostTocRail</c> reference it.</para>
/// <para><b>Code Flow:</b> Built by <c>PostView.BuildTocHeadings</c> from the current page's
/// rendered HTML; read by <c>PostTocRail</c> to build the <c>AnchorNav</c> sections list.</para>
/// <para><b>Dependencies:</b> None.</para>
/// <para><b>Usage:</b> <c>new TocHeading("getting-started", "Getting Started", 2)</c></para>
/// </remarks>
/// <param name="Id">The heading's stable id attribute, as rendered by Markdig's auto-identifiers.</param>
/// <param name="Text">Plain-text heading content, HTML-decoded and stripped of inline markup.</param>
/// <param name="Level">Markdown heading level — 2 or 3.</param>
public sealed record TocHeading(string Id, string Text, int Level);
