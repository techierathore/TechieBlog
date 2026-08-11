using System.Text.RegularExpressions;

namespace TechieBlog.Tests.Accessibility;

/// <summary>
/// Guards the document-heading contract REQ-UI-060 established for <c>/access-denied</c>, so the
/// page cannot silently lose its <c>h1</c> again.
/// </summary>
/// <remarks>
/// <para><b>Why source and not bUnit:</b> the same reason
/// <see cref="MarkupAccessibilityContractTests"/> gives, plus two specific to this page. Rendering
/// <c>AccessDenied</c> in bUnit exercises <c>OnAfterRender</c>, which reads
/// <c>RendererInfo.IsInteractive</c> and calls <c>NavigationManager</c> — machinery that has nothing
/// to do with the invariant and everything to do with making the test flaky. And the acceptance is
/// about the WHOLE rendered page, which means the layout too; bUnit renders the component without
/// its <c>@layout</c>, so it could not see a heading the shell contributed even if one existed.</para>
/// <para>The rendered outline is proved separately and for real, by Playwright against the running
/// host. These tests are the cheap always-on guard underneath that: they run in every default build,
/// including the build a careless edit would otherwise sail through.</para>
/// </remarks>
public class AccessDeniedHeadingTests
{
    /// <summary>Heading tags in the order their level implies.</summary>
    private const string HeadingPattern = @"<(?<tag>h[1-6])[\s>]";

    /// <summary>
    /// The access-denied page emits exactly one h1, so the screen a denied visitor lands on has a
    /// top-level document heading rather than the lone h3 CardTitle used to produce
    /// (WCAG 2.4.6 / 1.3.1, TrBlazeUI gap TR-066).
    /// </summary>
    [Fact]
    public void AccessDeniedDeclaresExactlyOneTopLevelHeading()
    {
        var headings = HeadingsIn(AccessDeniedMarkup());
        var topLevel = headings.Where(tag => tag == "h1").ToArray();

        Assert.True(
            topLevel.Length == 1,
            $"AccessDenied.razor must emit exactly one <h1>; found {topLevel.Length}. "
                + "Outline was: " + Describe(headings));
    }

    /// <summary>
    /// The heading levels on the access-denied page start at h1 and never skip a level, so the
    /// outline a screen reader builds is contiguous.
    /// </summary>
    [Fact]
    public void AccessDeniedHeadingOrderIsContiguous()
    {
        var levels = HeadingsIn(AccessDeniedMarkup())
            .Select(tag => tag[1] - '0')
            .ToArray();

        Assert.True(levels.Length > 0, "AccessDenied.razor emits no heading at all.");
        Assert.True(levels[0] == 1, "The first heading on AccessDenied.razor must be an h1.");

        for (var index = 1; index < levels.Length; index++)
        {
            Assert.True(
                levels[index] <= levels[index - 1] + 1,
                $"Heading order jumps from h{levels[index - 1]} to h{levels[index]} "
                    + "on AccessDenied.razor.");
        }
    }

    /// <summary>
    /// The access-denied page does not use CardTitle for its heading, because CardTitle hardcodes
    /// h3 with no way to re-level it — reintroducing it is exactly how the missing h1 returns.
    /// </summary>
    [Fact]
    public void AccessDeniedDoesNotUseCardTitleForItsHeading()
    {
        Assert.DoesNotContain("<CardTitle", AccessDeniedMarkup(), StringComparison.Ordinal);
    }

    /// <summary>
    /// AuthLayout contributes no heading of its own, which is what makes the page's h1 the
    /// document's only top-level heading rather than a second one competing with the shell.
    /// </summary>
    [Fact]
    public void AuthLayoutContributesNoHeading()
    {
        var headings = HeadingsIn(MarkupOf(Path.Combine("Layouts", "AuthLayout.razor")));

        Assert.True(
            headings.Length == 0,
            "AuthLayout must contribute no heading, else /access-denied gains a competing "
                + "top-level heading. Found: " + Describe(headings));
    }

    /// <summary>Heading tag names appearing in <paramref name="markup"/>, in document order.</summary>
    /// <param name="markup">Razor markup with comments already stripped.</param>
    /// <returns>Lower-case tag names such as <c>h1</c>.</returns>
    private static string[] HeadingsIn(string markup)
    {
        return Regex.Matches(markup, HeadingPattern, RegexOptions.IgnoreCase)
            .Select(match => match.Groups["tag"].Value.ToLowerInvariant())
            .ToArray();
    }

    /// <summary>Renders a heading list for an assertion message.</summary>
    /// <param name="headings">Tag names in document order.</param>
    /// <returns>A readable outline, or a note that there were none.</returns>
    private static string Describe(string[] headings)
    {
        return headings.Length == 0 ? "(no headings)" : string.Join(" -> ", headings);
    }

    /// <summary>The access-denied page's markup, with Razor comments removed.</summary>
    /// <returns>The markup to assert against.</returns>
    private static string AccessDeniedMarkup()
    {
        return MarkupOf(Path.Combine("Pages", "AccessDenied.razor"));
    }

    /// <summary>
    /// Reads a BlogUI Razor file and strips its <c>@* … *@</c> comments.
    /// </summary>
    /// <remarks>
    /// Stripping is not cosmetic: both files here carry comments that DISCUSS the heading tags, so
    /// a raw scan would count documentation as markup and the tests would pass on prose alone.
    /// </remarks>
    /// <param name="relativePath">Path below <c>source/BlogUI</c>.</param>
    /// <returns>The file's markup without Razor comments.</returns>
    private static string MarkupOf(string relativePath)
    {
        var fullPath = Path.Combine(BlogUiRoot(), relativePath);
        var source = File.ReadAllText(fullPath);

        return Regex.Replace(source, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
    }

    /// <summary>Locates <c>source/BlogUI</c> by walking up from the test binary.</summary>
    /// <returns>The absolute path of the BlogUI project folder.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder is not above the test binary.</exception>
    private static string BlogUiRoot()
    {
        var found = WalkUpFrom(AppContext.BaseDirectory)
            ?? WalkUpFrom(Path.GetDirectoryName(ThisFilePath()))
            ?? throw new DirectoryNotFoundException("Could not locate source/BlogUI.");

        return found;
    }

    /// <summary>
    /// Walks up from a starting folder looking for <c>source/BlogUI</c>.
    /// </summary>
    /// <remarks>
    /// The compile-time fallback matters because <c>dotnet test --artifacts-path …</c> stages the
    /// test binary outside the repository tree, where the walk from
    /// <c>AppContext.BaseDirectory</c> finds nothing.
    /// </remarks>
    /// <param name="startFolder">Folder to start from; may be <c>null</c>.</param>
    /// <returns>The BlogUI folder, or <c>null</c> when it is not above the start.</returns>
    private static string? WalkUpFrom(string? startFolder)
    {
        if (string.IsNullOrWhiteSpace(startFolder) || !Directory.Exists(startFolder))
            return null;

        var directory = new DirectoryInfo(startFolder);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "source", "BlogUI");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// This source file's path, captured by the compiler.
    /// </summary>
    /// <param name="filePath">Supplied by the compiler; never pass a value.</param>
    /// <returns>The absolute path of this file on the machine that compiled it.</returns>
    private static string ThisFilePath([System.Runtime.CompilerServices.CallerFilePath] string filePath = "")
    {
        return filePath;
    }
}
