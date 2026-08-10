using System.Text.RegularExpressions;

namespace TechieBlog.Tests.Accessibility;

/// <summary>
/// Source-level guards for the accessibility contracts REQ-NFR-007 established, so a later edit
/// cannot silently undo them.
/// </summary>
/// <remarks>
/// <para><b>Why source and not bUnit:</b> <c>TechieBlog.Tests</c> only references <c>BlogUI</c>
/// when <c>IncludeBlogUiTests=true</c>, so a rendering test would not run in the default build —
/// exactly the build a regression would slip through. These read the markup instead, which is
/// where both contracts actually live.</para>
/// </remarks>
public class MarkupAccessibilityContractTests
{
    /// <summary>
    /// Routable pages that still have no <c>PageTitle</c>, with the reason each one is exempt.
    /// </summary>
    /// <remarks>
    /// <para>Every entry is real WCAG 2.4.2 debt, not an accepted exception. They are listed here
    /// rather than fixed because another build cluster owns those files in this pass; the list is
    /// the handover. Deleting an entry after adding the <c>PageTitle</c> is the whole point.</para>
    /// </remarks>
    private static readonly string[] PagesAwaitingTitle =
    {
        "ManagePost.razor",
        "SeriesList.razor",
        "UsersList.razor",
        "SubscribersList.razor",
        "CommentsList.razor",
        "TagsList.razor",
    };

    /// <summary>
    /// Every routable Razor page declares a PageTitle, so no screen announces itself as nothing
    /// but the site name (WCAG 2.4.2 Page Titled, Level A).
    /// </summary>
    [Fact]
    public void EveryRoutablePageDeclaresAPageTitle()
    {
        var offenders = RoutablePages()
            .Where(p => !File.ReadAllText(p).Contains("<PageTitle>", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(name => !PagesAwaitingTitle.Contains(name))
            .OrderBy(name => name)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Routable pages with no <PageTitle> (WCAG 2.4.2): " + string.Join(", ", offenders));
    }

    /// <summary>
    /// No two routable pages share the same title text, because a title that does not distinguish
    /// the page fails WCAG 2.4.2 just as surely as a missing one.
    /// </summary>
    [Fact]
    public void PageTitlesAreDistinct()
    {
        var duplicates = RoutablePages()
            .Select(File.ReadAllText)
            .Select(source => Regex.Match(source, @"<PageTitle>(?<title>[^<]*)</PageTitle>"))
            .Where(match => match.Success)
            .Select(match => match.Groups["title"].Value.Trim())
            .Where(title => !title.Contains('@'))          // templated titles vary at runtime
            .GroupBy(title => title, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} (x{group.Count()})")
            .OrderBy(text => text)
            .ToArray();

        Assert.True(
            duplicates.Length == 0,
            "Routable pages sharing a title (WCAG 2.4.2): " + string.Join(", ", duplicates));
    }

    /// <summary>
    /// Any subtree hidden from assistive technology with aria-hidden that still contains a library
    /// control is marked data-a11y-decorative, so App.razor's observer takes it out of the tab
    /// order and it cannot become a silent, unnamed focus stop (WCAG 4.1.2, TrBlazeUI gap TR-052).
    /// </summary>
    [Fact]
    public void AriaHiddenLibraryControlsAreMarkedDecorative()
    {
        var offenders = new List<string>();

        foreach (var path in RazorFiles(Path.Combine(SourceRoot(), "BlogUI")))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (!line.Contains("aria-hidden=\"true\"", StringComparison.Ordinal))
                    continue;

                // Only the wrappers that exist to hide a control matter; aria-hidden on an icon or
                // a decorative glyph has nothing focusable inside it.
                if (!line.Contains("data-testid=\"post-rating-stars\"", StringComparison.Ordinal)
                    && !line.Contains("class=\"inline-flex\"", StringComparison.Ordinal))
                    continue;

                if (!line.Contains("data-a11y-decorative", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(path)}: {line.Trim()}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "aria-hidden wrappers around a library control without data-a11y-decorative: "
                + string.Join(" | ", offenders));
    }

    /// <summary>Every <c>.razor</c> file under <c>source/BlogUI/Pages</c> that declares a route.</summary>
    private static IEnumerable<string> RoutablePages()
    {
        return RazorFiles(Path.Combine(SourceRoot(), "BlogUI", "Pages"))
            .Where(path => File.ReadAllText(path).Contains("@page ", StringComparison.Ordinal));
    }

    /// <summary>Every <c>.razor</c> file below <paramref name="root"/>, ignoring build output.</summary>
    private static IEnumerable<string> RazorFiles(string root)
    {
        return Directory
            .EnumerateFiles(root, "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));
    }

    /// <summary>
    /// Locates the repository's <c>source</c> folder by walking up from the test binary.
    /// </summary>
    /// <returns>The absolute path of <c>source</c>.</returns>
    /// <exception cref="DirectoryNotFoundException">The folder is not above the test binary.</exception>
    private static string SourceRoot()
    {
        return WalkUpFrom(AppContext.BaseDirectory)
            ?? WalkUpFrom(Path.GetDirectoryName(ThisFilePath()))
            ?? throw new DirectoryNotFoundException("Could not locate the repository's source folder.");
    }

    /// <summary>
    /// Walks up from a starting folder looking for <c>source/BlogUI</c>.
    /// </summary>
    /// <remarks>
    /// The compile-time fallback in <see cref="SourceRoot"/> matters because
    /// <c>dotnet test --artifacts-path …</c> stages the test binary OUTSIDE the repository tree, and
    /// the walk from <c>AppContext.BaseDirectory</c> then finds nothing. Before this, running the
    /// suite with a redirected output folder failed three accessibility tests with
    /// <c>DirectoryNotFoundException</c> — a harness artefact that reads exactly like a real defect.
    /// </remarks>
    /// <param name="startFolder">Folder to start from; may be <c>null</c>.</param>
    /// <returns>The source folder, or <c>null</c> when it is not above the start.</returns>
    private static string? WalkUpFrom(string? startFolder)
    {
        if (string.IsNullOrWhiteSpace(startFolder) || !Directory.Exists(startFolder))
            return null;

        var directory = new DirectoryInfo(startFolder);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "source");
            if (Directory.Exists(Path.Combine(candidate, "BlogUI")))
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
