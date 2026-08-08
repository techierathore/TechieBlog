using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BlogEngine.Common;

/// <summary>
/// Service for converting Markdown text to sanitised HTML.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides server-side Markdown to HTML conversion using Markdig, with
/// XSS neutralisation applied so that author- and visitor-supplied Markdown can be rendered
/// through a <c>MarkupString</c> without executing script (REQ-NFR-006 / BRD-83).</para>
///
/// <para><b>Code Flow:</b> a caller passes Markdown to <see cref="ToHtml"/> → the shared
/// <see cref="MarkdownPipeline"/> parses it into a <see cref="MarkdownDocument"/> → the
/// <see cref="MarkdownSanitizerExtension"/> pass, hooked to
/// <see cref="MarkdownPipelineBuilder.DocumentProcessed"/>, walks that tree and neutralises unsafe
/// URLs and attributes → Markdig's HTML renderer writes the cleaned tree → the caller wraps the
/// string in a <c>MarkupString</c>, which performs <i>no</i> escaping of its own.</para>
///
/// <para><b>Dependencies:</b> Markdig only. The pipeline is immutable once built and the renderer
/// holds no per-caller state, which is why it is registered as a singleton
/// (<see cref="BlogSvcInitializer"/>).</para>
///
/// <para><b>XSS POSTURE — read this before changing the pipeline (REQ-NFR-006).</b> The two
/// questions that matter, answered directly:</para>
/// <list type="bullet">
///   <item><b>Is user-supplied Markdown sanitised? YES</b> — but by <i>this class</i>, not by
///     Markdig. Markdig is a faithful CommonMark implementation and performs no sanitisation
///     whatsoever; a default pipeline will happily emit a <c>&lt;script&gt;</c> tag it was given.
///     Everything that makes this output safe is the three layers listed below. Nothing downstream
///     sanitises again: the output goes to <c>MarkupString</c>, which is Blazor's explicit
///     "trust this HTML" escape hatch and escapes nothing.</item>
///   <item><b>Is raw HTML permitted? NO.</b> <see cref="MarkdownExtensions.DisableHtml"/> removes
///     the raw-HTML block parser and switches inline HTML parsing off, so a literal tag in the
///     source is emitted as escaped text and never as live markup. This is a hard constraint of
///     the feature, not a tunable: re-enabling raw HTML — for a table, an embed, a "just this once"
///     exception — hands stored XSS to every anonymous commenter in the application.</item>
/// </list>
///
/// <para><b>Why the bar is set at comment level.</b> This one renderer serves both post bodies
/// (authored by trusted staff) and <b>comment bodies, which are anonymous visitor input rendered
/// back to every other visitor</b>. That second path makes this the highest-risk renderer in the
/// application: a defect here is stored XSS with site-wide reach, executing in the session of every
/// reader including an administrator. The pipeline is therefore configured for the untrusted case
/// unconditionally, and no caller is given a "trusted" variant that skips sanitisation — the
/// absence of such an overload is intentional.</para>
///
/// <para><b>Usage:</b> Inject as a singleton and call <see cref="ToHtml"/>. Callers should still
/// apply a Content-Security-Policy at the host: defence in depth, because sanitisation is a
/// blocklist-shaped problem and a CSP fails closed if a layer here is ever bypassed.</para>
///
/// <para><b>Security:</b> Markdig performs no sanitisation of its own. Three defences are layered
/// here:</para>
/// <list type="number">
/// <item><description><see cref="MarkdownExtensions.DisableHtml"/> removes the raw-HTML block
/// parser and switches off inline HTML parsing, so <c>&lt;script&gt;</c>,
/// <c>&lt;iframe&gt;</c>, <c>&lt;img onerror&gt;</c> and every other literal tag is emitted as
/// escaped text instead of live markup.</description></item>
/// <item><description>Link and image destinations are checked against a scheme allow-list, so
/// <c>javascript:</c>, <c>vbscript:</c> and <c>data:</c> URLs cannot reach an
/// <c>href</c>/<c>src</c>.</description></item>
/// <item><description>Markdig's generic-attributes extension (part of
/// <see cref="MarkdownExtensions.UseAdvancedExtensions"/>) lets Markdown declare arbitrary HTML
/// attributes such as <c>{onclick=alert(1)}</c>; event-handler, <c>style</c> and URL-bearing
/// attributes are stripped from the syntax tree before rendering.</description></item>
/// </list>
/// </remarks>
public class MarkdownRenderer
{
    private readonly MarkdownPipeline pipeline;

    /// <summary>
    /// Builds the shared, sanitising Markdig pipeline.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Ordering is significant. The convenience extensions are added
    /// first, then <see cref="MarkdownExtensions.DisableHtml"/> strips raw-HTML parsing, and the
    /// sanitiser is added <i>last</i> so its document pass runs over the finished syntax tree.
    /// <see cref="MarkdownExtensions.UseAdvancedExtensions"/> is what pulls in generic attributes —
    /// the feature that lets Markdown declare arbitrary HTML attributes — so the sanitiser is not
    /// optional cleanup, it is the thing that makes that extension safe to enable at all.</para>
    /// <para><b>Flow:</b> add extensions → disable raw HTML → attach the sanitiser → build once.</para>
    /// <para><b>Side Effects:</b> None. The built pipeline is immutable and thread-safe, which is
    /// what allows a single instance to serve every circuit.</para>
    /// </remarks>
    public MarkdownRenderer()
    {
        pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAutoLinks()
            .UseTaskLists()
            .UsePipeTables()
            .UseAutoIdentifiers()
            .UseEmphasisExtras()
            .UseFootnotes()
            .DisableHtml()
            .Use<MarkdownSanitizerExtension>()
            .Build();
    }

    /// <summary>
    /// Converts Markdown text to sanitised HTML that is safe to render as a <c>MarkupString</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Blank input returns an empty string rather than throwing, so a
    /// post or comment with no body renders as nothing rather than failing the page.</para>
    /// <para><b>Flow:</b> guard blank input → parse → sanitise the tree → render HTML.</para>
    /// <para><b>Side Effects:</b> None; pure with respect to the renderer's own state.</para>
    /// <para><b>Security:</b> the returned string is the ONLY sanitisation the content gets — its
    /// destination, <c>MarkupString</c>, escapes nothing. Never concatenate caller-supplied text
    /// onto this result, and never hand <c>MarkupString</c> anything that did not come through
    /// here.</para>
    /// </remarks>
    /// <param name="markdown">The Markdown content to convert. Assumed untrusted.</param>
    /// <returns>Sanitised HTML, or an empty string when the input is null or whitespace.</returns>
    public string ToHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToHtml(markdown, pipeline);
    }

    /// <summary>
    /// Converts Markdown text to plain text, stripping all formatting.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Backs excerpts, meta descriptions and search snippets, where
    /// markup would be noise. The same sanitising pipeline is used, so raw HTML in the source has
    /// already been reduced to text before the plain-text renderer sees it.</para>
    /// <para><b>Flow:</b> guard blank input → parse → sanitise the tree → render text.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// <para><b>Security:</b> the result is plain text, NOT sanitised HTML. It is safe in an
    /// escaping context — a Razor <c>@expression</c>, an attribute value, a meta tag rendered by
    /// Blazor — and must never be passed to <c>MarkupString</c>: any <c>&lt;</c> it contains is a
    /// literal character here and would become a tag there.</para>
    /// </remarks>
    /// <param name="markdown">The Markdown content to convert.</param>
    /// <returns>Plain text, or an empty string when the input is null or whitespace.</returns>
    public string ToPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        return Markdown.ToPlainText(markdown, pipeline);
    }
}

/// <summary>
/// Markdig extension that walks the parsed document and removes the constructs that would
/// otherwise let Markdown emit executable HTML.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Closes the two XSS holes that <see cref="MarkdownExtensions.DisableHtml"/>
/// does not cover — dangerous URL schemes on links/images, and attributes injected through the
/// generic-attributes extension.</para>
/// <para><b>Business Logic:</b> Runs once per document on
/// <see cref="MarkdownPipelineBuilder.DocumentProcessed"/>, i.e. after block and inline parsing
/// but before any renderer runs, so every renderer in the pipeline sees the cleaned tree.</para>
/// <para><b>Side Effects:</b> Mutates the supplied <see cref="MarkdownDocument"/> in place.</para>
/// </remarks>
public sealed class MarkdownSanitizerExtension : IMarkdownExtension
{
    /// <summary>
    /// URL schemes a link or image destination is allowed to use. Everything else — including
    /// <c>javascript:</c>, <c>vbscript:</c>, <c>data:</c> and <c>file:</c> — is rejected.
    /// Scheme-relative and site-relative URLs carry no scheme and are always allowed.
    /// </summary>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        "http", "https", "mailto", "tel", "ftp", "ftps"
    };

    /// <summary>
    /// Attribute names that may never survive into the rendered HTML, regardless of their value.
    /// Any attribute whose name begins with <c>on</c> is dropped as well.
    /// </summary>
    private static readonly HashSet<string> BannedAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "style", "srcdoc", "src", "href", "formaction", "action", "background",
        "dynsrc", "lowsrc", "data", "poster", "xlink:href", "ping"
    };

    /// <summary>
    /// Replacement destination used when a link or image URL fails the scheme allow-list.
    /// </summary>
    private const string BlockedUrl = "#";

    /// <summary>
    /// Registers the document-level sanitisation pass on the pipeline.
    /// </summary>
    /// <param name="pipeline">The pipeline being built.</param>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (pipeline == null)
            return;

        pipeline.DocumentProcessed -= Sanitize;
        pipeline.DocumentProcessed += Sanitize;
    }

    /// <summary>
    /// No renderer-level setup is required; sanitisation happens on the syntax tree.
    /// </summary>
    /// <param name="pipeline">The pipeline being built.</param>
    /// <param name="renderer">The renderer being configured.</param>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    /// <summary>
    /// Walks every node of the document, neutralising unsafe URLs and attributes.
    /// </summary>
    /// <param name="document">The parsed Markdown document.</param>
    private static void Sanitize(MarkdownDocument document)
    {
        if (document == null)
            return;

        foreach (var node in Walk(document))
        {
            SanitizeAttributes(node);

            switch (node)
            {
                case LinkInline link when !IsSafeUrl(link.Url):
                    link.Url = BlockedUrl;
                    link.GetAttributes().Properties?.Clear();
                    break;

                case AutolinkInline autolink when !IsSafeUrl(autolink.Url):
                    autolink.Url = BlockedUrl;
                    break;
            }
        }
    }

    /// <summary>
    /// Enumerates a document node and all of its descendants, blocks and inlines alike.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Markdig splits containment across
    /// <see cref="ContainerBlock"/>, <see cref="LeafBlock"/> (whose inline content hangs off
    /// <see cref="LeafBlock.Inline"/>) and <see cref="ContainerInline"/>, so all three have to be
    /// descended explicitly.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="node">The node to start from.</param>
    /// <returns>The node followed by every descendant.</returns>
    private static IEnumerable<MarkdownObject> Walk(MarkdownObject node)
    {
        if (node == null)
            yield break;

        yield return node;

        switch (node)
        {
            case ContainerBlock container:
                foreach (var child in container)
                {
                    foreach (var descendant in Walk(child))
                        yield return descendant;
                }

                break;

            case LeafBlock leaf when leaf.Inline != null:
                foreach (var descendant in Walk(leaf.Inline))
                    yield return descendant;

                break;
        }

        if (node is ContainerInline containerInline)
        {
            for (var child = containerInline.FirstChild; child != null; child = child.NextSibling)
            {
                foreach (var descendant in Walk(child))
                    yield return descendant;
            }
        }
    }

    /// <summary>
    /// Removes event-handler, style and URL-bearing attributes attached to a node.
    /// </summary>
    /// <param name="node">The node whose attached HTML attributes should be cleaned.</param>
    private static void SanitizeAttributes(MarkdownObject node)
    {
        var attributes = node.TryGetAttributes();
        if (attributes?.Properties == null)
            return;

        attributes.Properties.RemoveAll(property => IsBannedAttribute(property.Key, property.Value));
    }

    /// <summary>
    /// Decides whether a Markdown-declared HTML attribute must be dropped.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The attribute value, which may be null for valueless attributes.</param>
    /// <returns><c>true</c> when the attribute is unsafe to render.</returns>
    private static bool IsBannedAttribute(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return true;

        var key = name.Trim();

        if (key.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            return true;

        if (BannedAttributes.Contains(key))
            return true;

        // A permitted attribute is still unsafe if its value smuggles in a script URL.
        return !string.IsNullOrEmpty(value) && ContainsDangerousScheme(value);
    }

    /// <summary>
    /// Tests a link or image destination against the scheme allow-list.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The destination is HTML-decoded and stripped of whitespace and
    /// control characters first, because <c>java&amp;#09;script:</c> and
    /// <c>&amp;#106;avascript:</c> are both parsed as <c>javascript:</c> by browsers. A
    /// destination with no scheme at all (relative or fragment URL) is allowed.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="url">The raw destination taken from the syntax tree.</param>
    /// <returns><c>true</c> when the destination is safe to emit.</returns>
    private static bool IsSafeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return true;

        var scheme = ExtractScheme(url);

        return scheme == null || AllowedSchemes.Contains(scheme);
    }

    /// <summary>
    /// Reports whether an arbitrary attribute value carries a disallowed URL scheme.
    /// </summary>
    /// <param name="value">The attribute value to inspect.</param>
    /// <returns><c>true</c> when a disallowed scheme is present.</returns>
    private static bool ContainsDangerousScheme(string value)
    {
        var scheme = ExtractScheme(value);

        return scheme != null && !AllowedSchemes.Contains(scheme);
    }

    /// <summary>
    /// Extracts the URL scheme from a destination, defeating entity- and whitespace-obfuscation.
    /// </summary>
    /// <param name="url">The destination to inspect.</param>
    /// <returns>
    /// The lower-cased scheme, or <c>null</c> when the destination carries no scheme (and is
    /// therefore relative).
    /// </returns>
    private static string ExtractScheme(string url)
    {
        var decoded = WebUtility.HtmlDecode(url);

        var cleaned = new StringBuilder(decoded.Length);
        foreach (var character in decoded)
        {
            // Browsers ignore control characters and whitespace inside a scheme, so remove them
            // before looking for the colon.
            if (!char.IsControl(character) && !char.IsWhiteSpace(character))
                cleaned.Append(character);
        }

        var candidate = cleaned.ToString();
        var colon = candidate.IndexOf(':');
        if (colon <= 0)
            return null;

        var scheme = candidate.Substring(0, colon);

        // A colon appearing after a path separator, query or fragment marker is part of the path,
        // not a scheme (e.g. "/posts/a:b" or "?q=a:b").
        if (scheme.IndexOfAny(new[] { '/', '?', '#', '\\' }) >= 0)
            return null;

        // A real scheme is ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) per RFC 3986.
        if (!char.IsLetter(scheme[0]) || !scheme.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '-' || c == '.'))
            return null;

        return scheme.ToLowerInvariant();
    }
}
