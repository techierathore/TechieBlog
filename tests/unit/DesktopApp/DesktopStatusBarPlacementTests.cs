using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.DesktopApp;

/// <summary>
/// UAT-020 regression guard: the BlogApp connection chip must render as a real, in-flow top
/// banner rather than a <c>fixed</c> corner overlay that can float on top of the admin sidebar.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> the owner reported the connection chip covering the admin sidebar's
/// Settings entry when the sidebar was expanded, because <c>DesktopStatusBar.razor</c> was
/// anchored <c>fixed bottom-3 left-3</c>. The fix makes it a <c>sticky top-0</c> banner that
/// occupies real layout space, and reorders <c>ConnectionGuard.razor</c> so the banner renders
/// BEFORE the router's output — a `sticky` element only behaves as a top banner when it is the
/// first thing in the DOM. Both properties are easy to silently regress (an unrelated style
/// tweak restoring <c>fixed</c>, or a merge reordering the guard's children), and neither would
/// fail a compile, so this is enforced the same way <see cref="TechieBlog.Tests.Ops.SourceConventionTests"/>
/// enforces naming conventions: by scanning the source text directly rather than trusting a
/// human to notice on review.</para>
///
/// <para><b>Code Flow:</b> locate the repository root from the test assembly's own location →
/// read <c>DesktopStatusBar.razor</c> and check the status chip's own <c>class</c> attribute
/// (isolated by locating the <c>data-testid="desktop-connection-status"</c> anchor and walking
/// back to its owning <c>&lt;div&gt;</c>) never carries <c>fixed</c>, and always carries
/// <c>sticky</c> + <c>top-0</c> + <c>w-full</c> → read <c>ConnectionGuard.razor</c> and assert
/// <c>&lt;DesktopStatusBar</c> appears before <c>@ChildContent</c>.</para>
///
/// <para><b>Dependencies:</b> the repository layout only. Skipped rather than failed when the
/// repository root cannot be located, so a package-restored copy of the test assembly does not
/// report a false violation.</para>
/// </remarks>
public class DesktopStatusBarPlacementTests
{
    private static readonly Regex FixedToken = new(@"\bfixed\b", RegexOptions.Compiled);

    /// <summary>
    /// The connection chip's own class attribute never re-introduces `fixed` positioning — the
    /// exact styling that let it float on top of the admin sidebar's Settings entry.
    /// </summary>
    [Fact]
    public void StatusChipClassHasNoFixedPositioning()
    {
        var classAttribute = ReadStatusChipClassAttribute();
        Assert.SkipWhen(classAttribute == null, "DesktopStatusBar.razor not found next to the test assembly");

        Assert.False(
            FixedToken.IsMatch(classAttribute!),
            "DesktopStatusBar.razor's status chip must not use `fixed` positioning (UAT-020) — " +
            "it floated on top of the admin sidebar's Settings entry. Found: " + classAttribute);
    }

    /// <summary>
    /// The connection chip's own class attribute carries `sticky`, `top-0` and `w-full` — a real,
    /// in-flow top banner that reserves its own space instead of overlaying whatever is beneath it.
    /// </summary>
    [Fact]
    public void StatusChipClassIsAStickyTopBanner()
    {
        var classAttribute = ReadStatusChipClassAttribute();
        Assert.SkipWhen(classAttribute == null, "DesktopStatusBar.razor not found next to the test assembly");

        Assert.Contains("sticky", classAttribute!);
        Assert.Contains("top-0", classAttribute!);
        Assert.Contains("w-full", classAttribute!);
    }

    /// <summary>
    /// ConnectionGuard renders the status bar BEFORE the router's output, not after — a `sticky`
    /// banner only pins to the top of the window when it is the first element in the DOM.
    /// </summary>
    [Fact]
    public void ConnectionGuardRendersStatusBarBeforeChildContent()
    {
        var repositoryRoot = FindRepositoryRoot();
        Assert.SkipWhen(repositoryRoot == null, "repository root not found next to the test assembly");

        var guardPath = Path.Combine(repositoryRoot!, "source", "BlogApp", "Components", "ConnectionGuard.razor");
        Assert.SkipWhen(!File.Exists(guardPath), "ConnectionGuard.razor not found next to the test assembly");

        // Razor comments are stripped before the positions are compared. The comment that explains
        // this very ordering names @ChildContent, so an index taken over the raw text finds that
        // mention FIRST and reports the file as mis-ordered when it is correct — this guard failed
        // on the documentation its own fix added.
        var text = Regex.Replace(File.ReadAllText(guardPath), @"@\*.*?\*@", string.Empty, RegexOptions.Singleline);
        var statusBarIndex = text.IndexOf("<DesktopStatusBar", StringComparison.Ordinal);
        var childContentIndex = text.IndexOf("@ChildContent", StringComparison.Ordinal);

        Assert.True(statusBarIndex >= 0, "ConnectionGuard.razor no longer renders <DesktopStatusBar />.");
        Assert.True(childContentIndex >= 0, "ConnectionGuard.razor no longer renders @ChildContent.");
        Assert.True(
            statusBarIndex < childContentIndex,
            "ConnectionGuard.razor must render <DesktopStatusBar /> BEFORE @ChildContent (UAT-020) " +
            "so the `sticky top-0` banner is the first element in the DOM and pins to the top of " +
            "the window instead of trailing after every shared page's content.");
    }

    /// <summary>
    /// Reads the status chip's own <c>class</c> attribute out of <c>DesktopStatusBar.razor</c>,
    /// isolated by walking back from its <c>data-testid</c> anchor to the owning
    /// <c>&lt;div&gt;</c>'s opening <c>class="..."</c>.
    /// </summary>
    /// <returns>The class attribute's raw text, or <c>null</c> when the file cannot be found.</returns>
    private static string? ReadStatusChipClassAttribute()
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot == null)
        {
            return null;
        }

        var componentPath = Path.Combine(repositoryRoot, "source", "BlogApp", "Components", "DesktopStatusBar.razor");
        if (!File.Exists(componentPath))
        {
            return null;
        }

        var text = File.ReadAllText(componentPath);
        var testIdIndex = text.IndexOf("data-testid=\"desktop-connection-status\"", StringComparison.Ordinal);
        Assert.True(testIdIndex >= 0, "DesktopStatusBar.razor no longer carries data-testid=\"desktop-connection-status\".");

        var divStart = text.LastIndexOf("<div", testIdIndex, StringComparison.Ordinal);
        Assert.True(divStart >= 0, "Could not find the <div> owning desktop-connection-status.");

        var classStart = text.IndexOf("class=\"", divStart, StringComparison.Ordinal) + "class=\"".Length;
        var classEnd = text.IndexOf('"', classStart);

        return text.Substring(classStart, classEnd - classStart);
    }

    /// <summary>
    /// Walks up from the test assembly until the repository root is found.
    /// </summary>
    /// <returns>The absolute path of the repository root, or <c>null</c> when it is not present.</returns>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "source"))
                && File.Exists(Path.Combine(directory.FullName, "TechieBlog.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
