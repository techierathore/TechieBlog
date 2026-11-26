using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Build gate for the box-padding fault the owner reported on 2026-11-26 (post page, dark theme:
/// the rating box, the author box and the "More Posts" tiles had their content against the top edge).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> TrBlazeUI's <c>CardContent</c> starts with the classes <c>p-6 pt-0</c>,
/// because it expects a <c>CardHeader</c> above it. When a page passes its own all-sides padding
/// (<c>p-6</c>, <c>p-4</c>, …) the library's class merger drops its <c>p-6</c> but keeps <c>pt-0</c>,
/// so the rendered element reads <c>pt-0 … p-6</c> and the top padding is zero. Measured on the live
/// post page the same day. A box with no header must therefore name its top padding itself
/// (<c>p-6 pt-6</c>); this test fails the build when one does not.</para>
/// <para><b>Code Flow:</b> every <c>*.razor</c> under <c>source/</c> → every <c>&lt;CardContent&gt;</c>
/// → when no <c>&lt;CardHeader</c> sits between it and its <c>&lt;Card</c>, and its classes hold a
/// <c>p-N</c> without a <c>pt-…</c>, it is reported with its file and line.</para>
/// <para><b>Dependencies:</b> the repository layout (<see cref="RepositoryLocator"/>); skipped when
/// the assembly runs outside a checkout.</para>
/// </remarks>
public class CardContentPaddingTests
{
    private static readonly Regex CardContentTag = new(@"<CardContent\b([^>]*)>", RegexOptions.Compiled);

    private static readonly Regex ClassAttribute = new(@"Class=""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex AllSidesPadding = new(@"^p-\d+$", RegexOptions.Compiled);

    private static readonly Regex TopPadding = new(@"^pt-\S+$", RegexOptions.Compiled);

    /// <summary>
    /// Every header-less box that sets its own padding also sets its top padding.
    /// </summary>
    /// <remarks>
    /// Scenario: a public or admin page renders a TrBlazeUI <c>Card</c> whose <c>CardContent</c> has no
    /// <c>CardHeader</c> above it and passes <c>p-4</c>/<c>p-6</c>; without an explicit <c>pt-N</c> the
    /// library's <c>pt-0</c> survives and the content touches the top edge.
    /// </remarks>
    [Fact(DisplayName = "REQ-UI-074 a header-less CardContent with its own padding names its top padding")]
    public void HeaderlessCardContentKeepsTopPadding()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root!, "source"), "*.razor", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(file))
                continue;

            var text = File.ReadAllText(file);
            foreach (var line in FindOffendingLines(text))
                offenders.Add($"{Path.GetRelativePath(root!, file)}:{line}");
        }

        Assert.True(offenders.Count == 0,
            "These boxes lose their top padding (add pt-N beside p-N):" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The scan finds a synthetic violation, and passes the two correct shapes, so a zero is evidence.
    /// </summary>
    /// <remarks>
    /// Scenario: a header-less box with <c>p-6</c> only is reported; the same box with <c>p-6 pt-6</c>,
    /// and a box whose header carries the top spacing, are not.
    /// </remarks>
    [Fact(DisplayName = "REQ-UI-074 the padding scan matches a synthetic violation")]
    public void PaddingScanMatchesPositiveControl()
    {
        const string Bad = "<Card Class=\"my-8\">\n    <CardContent Class=\"flex gap-4 p-6\">x</CardContent>\n</Card>";
        const string Good = "<Card Class=\"my-8\">\n    <CardContent Class=\"flex gap-4 p-6 pt-6\">x</CardContent>\n</Card>";
        const string WithHeader = "<Card>\n    <CardHeader>t</CardHeader>\n    <CardContent Class=\"p-6\">x</CardContent>\n</Card>";

        Assert.Equal(new[] { 2 }, FindOffendingLines(Bad));
        Assert.Empty(FindOffendingLines(Good));
        Assert.Empty(FindOffendingLines(WithHeader));
    }

    /// <summary>
    /// Finds the line numbers of header-less CardContent tags that set p-N without pt-N.
    /// </summary>
    /// <param name="text">The razor file's text.</param>
    /// <returns>The 1-based line numbers of the offending tags.</returns>
    private static List<int> FindOffendingLines(string text)
    {
        var lines = new List<int>();
        foreach (Match match in CardContentTag.Matches(text))
        {
            var before = text[..match.Index];
            var cardStart = Math.Max(before.LastIndexOf("<Card ", StringComparison.Ordinal),
                                     before.LastIndexOf("<Card>", StringComparison.Ordinal));
            if (cardStart >= 0 && before[cardStart..].Contains("<CardHeader", StringComparison.Ordinal))
                continue;

            var classes = ClassAttribute.Match(match.Groups[1].Value);
            if (!classes.Success)
                continue;

            var tokens = classes.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Any(t => AllSidesPadding.IsMatch(t)) && !tokens.Any(t => TopPadding.IsMatch(t)))
                lines.Add(before.Count(c => c == '\n') + 1);
        }

        return lines;
    }

    /// <summary>
    /// Tells whether a path lies under a bin or obj folder.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>True for build output.</returns>
    private static bool IsBuildOutput(string path)
    {
        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return path.Split(separators).Any(p => p is "bin" or "obj");
    }
}
