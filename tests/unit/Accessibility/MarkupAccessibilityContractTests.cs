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
    /// <remarks>
    /// <para>A page satisfies this either with a literal <c>&lt;PageTitle&gt;</c> or with
    /// <c>&lt;SiteBrandTitle /&gt;</c>, the shared component UAT-021 introduced so the document title
    /// follows the configured site name instead of a hardcoded one. <c>SiteBrandTitle.razor</c>
    /// renders a real <c>&lt;PageTitle&gt;</c>, so both spellings meet WCAG 2.4.2 — but only the
    /// literal one was recognised when the component landed, which turned this guard's zero into a
    /// 37-page failure. Accepting the component keeps the guard LIVE; deleting the check would have
    /// made it pass while measuring nothing.</para>
    /// </remarks>
    [Fact]
    public void EveryRoutablePageDeclaresAPageTitle()
    {
        var offenders = RoutablePages()
            .Where(p => !DeclaresATitle(File.ReadAllText(p)))
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
    /// <remarks>
    /// <para>Reads BOTH title spellings. After UAT-021 most pages declare their title as
    /// <c>&lt;SiteBrandTitle Page="…" /&gt;</c>, whose distinguishing text is the <c>Page</c>
    /// attribute — the site name that follows it is identical on every page by design. Matching only
    /// the literal <c>&lt;PageTitle&gt;</c> left this guard reading a handful of pages and passing
    /// vacuously, which is worse than failing: a dead guard still reports green.</para>
    /// </remarks>
    [Fact]
    public void PageTitlesAreDistinct()
    {
        var duplicates = RoutablePages()
            .Select(File.ReadAllText)
            .SelectMany(DeclaredTitles)
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
    /// No markup depends on the deleted App.razor accessibility MutationObserver, so a library
    /// upgrade cannot silently leave an aria-hidden subtree holding a live tab stop again
    /// (WCAG 4.1.2, TrBlazeUI TR-052 — fixed in 2.0.2).
    /// </summary>
    /// <remarks>
    /// <para>This replaces the earlier <c>AriaHiddenLibraryControlsAreMarkedDecorative</c> guard,
    /// which required the opposite: every aria-hidden wrapper around a library control had to
    /// carry <c>data-a11y-decorative</c> so the observer would neutralise it. The observer was
    /// deleted on 2026-08-11 after axe measured 0 violations over 9 public + 15 admin routes both
    /// with and without it, so the marker is now dead weight — and a marker that no longer does
    /// anything is worse than none, because the next reader will believe it still protects
    /// something. <c>Rating</c>'s own <c>Focusable="false"</c> and <c>ReadOnly</c> are the
    /// supported mechanism now.</para>
    /// </remarks>
    [Fact]
    public void NoMarkupDependsOnTheDeletedAccessibilityObserver()
    {
        var markers = new[] { "data-a11y-decorative", "data-a11y-controls-removed", "data-a11y-role-removed" };

        var offenders = RazorFiles(SourceRoot())
            .SelectMany(path => markers
                .Where(marker => StripComments(File.ReadAllText(path)).Contains(marker, StringComparison.Ordinal))
                .Select(marker => $"{Path.GetFileName(path)}: {marker}"))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "markup still references the removed App.razor a11y observer: " + string.Join(" | ", offenders));
    }

    /// <summary>
    /// Blanks out Razor (<c>@* … *@</c>) and single-line JavaScript/C# comments.
    /// </summary>
    /// <remarks>
    /// <para>Without this, a guard that forbids a marker cannot describe in a comment WHY the marker
    /// is forbidden — the explanation trips the guard. That happened the first time this test ran:
    /// three files failed on the notes recording the removal, not on any live markup.</para>
    /// </remarks>
    /// <param name="markup">Razor source.</param>
    /// <returns>The source with comment bodies removed.</returns>
    /// <summary>
    /// Whether a page declares a document title by either supported spelling.
    /// </summary>
    /// <param name="markup">The page's raw Razor source.</param>
    /// <returns><c>true</c> when a literal PageTitle or a SiteBrandTitle is present.</returns>
    private static bool DeclaresATitle(string markup)
    {
        var source = StripComments(markup);
        return source.Contains("<PageTitle>", StringComparison.Ordinal)
            || source.Contains("<SiteBrandTitle", StringComparison.Ordinal);
    }

    /// <summary>
    /// The distinguishing title text a page declares, by either supported spelling.
    /// </summary>
    /// <remarks>
    /// For a <c>SiteBrandTitle</c> the distinguishing part is its <c>Page</c> attribute; a
    /// <c>SiteBrandTitle</c> with no <c>Page</c> renders the bare site name, which only the home
    /// page may do, so it contributes nothing to compare.
    /// </remarks>
    /// <param name="markup">The page's raw Razor source.</param>
    /// <returns>Every title string the page declares, trimmed.</returns>
    private static IEnumerable<string> DeclaredTitles(string markup)
    {
        var source = StripComments(markup);

        foreach (Match match in Regex.Matches(source, @"<PageTitle>(?<title>[^<]*)</PageTitle>"))
        {
            yield return match.Groups["title"].Value.Trim();
        }

        foreach (Match match in Regex.Matches(source, @"<SiteBrandTitle[^>]*?\sPage=""(?<title>[^""]*)"""))
        {
            yield return match.Groups["title"].Value.Trim();
        }
    }

    private static string StripComments(string markup)
    {
        var withoutRazorComments = Regex.Replace(markup, @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutRazorComments, @"^\s*//.*$", string.Empty, RegexOptions.Multiline);
    }

    /// <summary>
    /// An interactive TrBlazeUI <c>Rating</c> is never hidden from assistive technology, because
    /// since 2.0.2 it is a real keyboard-operable radio group and hiding it would take the control
    /// away from exactly the users the workaround was written for (WCAG 4.1.2 / 2.1.1, TR-031/045).
    /// </summary>
    /// <remarks>
    /// <para>A <c>ReadOnly</c> rating is exempt: it renders <c>role="img"</c> with no radio
    /// semantics and no tab stop, so hiding it as a decorative duplicate of adjacent text — which
    /// is what <c>StarRating</c> does — is correct rather than a workaround.</para>
    /// </remarks>
    [Fact]
    public void InteractiveRatingsAreNotHiddenFromAssistiveTechnology()
    {
        var offenders = new List<string>();

        foreach (var path in RazorFiles(Path.Combine(SourceRoot(), "BlogUI")))
        {
            var markup = File.ReadAllText(path);

            foreach (Match element in Regex.Matches(markup, @"<Rating\b[^>]*?/>", RegexOptions.Singleline))
            {
                var isReadOnly = element.Value.Contains("ReadOnly=\"true\"", StringComparison.Ordinal);
                var isHidden = element.Value.Contains("aria-hidden=\"true\"", StringComparison.Ordinal);

                if (isHidden && !isReadOnly)
                    offenders.Add($"{Path.GetFileName(path)}: {Regex.Replace(element.Value, @"\s+", " ")}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "interactive <Rating> hidden from assistive technology: " + string.Join(" | ", offenders));
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
