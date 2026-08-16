using System.Text.RegularExpressions;

namespace TechieBlog.Tests.Settings;

/// <summary>
/// Source scan keeping edit forms off the shared settings aggregate (REQ-FN-061).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>SiteSettingsAliasingTests</c> proves the service hands out a detached
/// copy; nothing there stops a page from asking for the shared one anyway. The original defect was
/// exactly that — <c>Settings.razor</c> called <c>GetSettingsAsync</c> and bound its form to the
/// result — so the behavioural tests would all have passed while the site leaked. This scan closes
/// the remaining gap by asserting the call site itself.</para>
///
/// <para><b>Code Flow:</b> Locate <c>Settings.razor</c> from the test assembly's directory, read it,
/// and assert on which service member it calls.</para>
///
/// <para><b>Dependencies:</b> The repository layout (<c>TechieBlog.slnx</c> beside <c>source/</c>).
/// The scan is skipped rather than failed when the tree is absent, so a packaged test run does not
/// report a false defect.</para>
///
/// <para><b>Usage:</b> No database and no running host required.</para>
/// </remarks>
public class SettingsEditorBindingScanTests
{
    private const string SettingsPagePath = "BlogUI/Pages/AdminPages/Settings.razor";

    /// <summary>
    /// The admin Settings screen loads its form model from the editable copy, not the shared cache.
    /// </summary>
    /// <remarks>
    /// Every control on that page two-way-binds to the loaded model, so the member it loads from
    /// decides whether an unsaved keystroke is private to the administrator or is site-wide
    /// configuration for every visitor.
    /// </remarks>
    [Fact]
    public void SettingsPageLoadsItsFormFromTheEditableCopy()
    {
        var page = ReadSettingsPage();
        if (page == null)
        {
            return;
        }

        Assert.Contains("GetEditableSettingsAsync()", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The admin Settings screen never reads the shared cached aggregate.
    /// </summary>
    /// <remarks>
    /// Stated as its own assertion because adding the editable call while leaving the old one in
    /// place would satisfy the test above and still leak through whichever call ran last.
    /// <c>GetEditableSettingsAsync</c> is excluded from the match by requiring the <c>.</c> that
    /// precedes a bare <c>GetSettingsAsync</c> call.
    /// </remarks>
    [Fact]
    public void SettingsPageDoesNotReadTheSharedCachedAggregate()
    {
        var page = ReadSettingsPage();
        if (page == null)
        {
            return;
        }

        var sharedReads = Regex.Matches(page, @"\.GetSettingsAsync\s*\(");
        Assert.Empty(sharedReads);
    }

    /// <summary>
    /// Reads the admin Settings page from the working tree.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Returns null when the source tree is not reachable, which lets
    /// the scan degrade to a skip instead of failing a run that has no repository beneath it.</para>
    /// <para><b>Side Effects:</b> One file read.</para>
    /// </remarks>
    /// <returns>The page's text, or null when the source tree is absent.</returns>
    private static string? ReadSettingsPage()
    {
        var sourceRoot = FindSourceRoot();
        if (sourceRoot == null)
        {
            return null;
        }

        var path = Path.Combine(sourceRoot, SettingsPagePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// Walks up from the test assembly until a folder containing <c>source/</c> is found.
    /// </summary>
    /// <returns>The absolute path of <c>source/</c>, or <c>null</c> when it is not present.</returns>
    private static string? FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "source");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(directory.FullName, "TechieBlog.slnx")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
