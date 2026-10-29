using System.Text.RegularExpressions;
using Xunit;

namespace TechieBlog.Tests.Ops;

/// <summary>
/// Brand-asset gates: REQ-NFR-042 (README lockup), REQ-NFR-043 (favicon and icons linked from the
/// page head) and REQ-NFR-044 (BlogApp icon, foreground and splash) — BRD-103/104/105, ADR-017.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> brand files are copied byte for byte, never redrawn, so the checks here are
/// about presence, identity between copies, and that the code that consumes them still points at
/// them.</para>
/// <para><b>Code Flow:</b> locate the repository root → assert each consumer (README, host
/// <c>App.razor</c>, <c>BlogApp.csproj</c>) names the expected file and that the file exists with the
/// expected format and size.</para>
/// <para><b>Dependencies:</b> the repository layout; skipped outside a checkout.</para>
/// </remarks>
public class BrandAssetTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// README.md opens with the horizontal lockup, above the title (REQ-NFR-042).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-042 the README opens with the lockup image above the title")]
    public void ReadmeOpensWithTheLockupAboveTheTitle()
    {
        var root = Root();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var image = readme.IndexOf("docs/brand/TechieBlog-horizontal.png", StringComparison.Ordinal);
        var title = Regex.Match(readme, @"^# ", RegexOptions.Multiline);
        Assert.True(image >= 0, "REQ-NFR-042: README does not reference docs/brand/TechieBlog-horizontal.png");
        Assert.True(title.Success && image < title.Index, "REQ-NFR-042: the lockup must come before the title");
        AssertPng(Path.Combine(root, "docs", "brand", "TechieBlog-horizontal.png"));
        AssertPng(Path.Combine(root, "docs", "brand", "TechieBlog.png"));
    }

    /// <summary>
    /// The host head links favicon.ico, icon-192.png and icon-512.png, and each file exists with the
    /// right dimensions (REQ-NFR-043).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-043 the host head links favicon.ico, icon-192.png and icon-512.png")]
    public void HostHeadLinksTheRenderedIcons()
    {
        var root = Root();
        var app = File.ReadAllText(Path.Combine(root, "source", "TechieBlog", "Components", "App.razor"));
        var brand = Path.Combine(root, "source", "BlogUI", "wwwroot", "images", "brand");
        foreach (var name in new[] { "favicon.ico", "icon-192.png", "icon-512.png" })
        {
            Assert.Contains($"_content/BlogUI/images/brand/{name}", app);
            Assert.True(File.Exists(Path.Combine(brand, name)), $"REQ-NFR-043: {name} missing");
        }

        AssertPngSize(Path.Combine(brand, "icon-192.png"), 192);
        AssertPngSize(Path.Combine(brand, "icon-512.png"), 512);

        var ico = File.ReadAllBytes(Path.Combine(brand, "favicon.ico"));
        Assert.True(ico.Length > 22 && ico[0] == 0 && ico[1] == 0 && ico[2] == 1 && ico[3] == 0, "favicon.ico is not an ICO container");
        Assert.Equal(32, ico[6]);
        Assert.Equal(32, ico[7]);
    }

    /// <summary>
    /// The icon script exists under <c>scripts/</c> (the assets are rendered, never hand-made).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-043 the icon render script exists under scripts")]
    public void IconScriptExists()
    {
        Assert.True(File.Exists(Path.Combine(Root(), "scripts", "render-brand-icons.cs")), "REQ-NFR-043: scripts/render-brand-icons.cs missing");
    }

    /// <summary>
    /// BlogApp uses the brand blue for icon and splash, the splash is the glyph-only file, and no
    /// hand-made PNG sits under Resources (REQ-NFR-044).
    /// </summary>
    [Fact(DisplayName = "REQ-NFR-044 the BlogApp icon and splash are the brand files in colour 0F4DBC")]
    public void BlogAppIconAndSplashUseTheBrandFiles()
    {
        var root = Root();
        var csproj = File.ReadAllText(Path.Combine(root, "source", "BlogApp", "BlogApp.csproj"));
        Assert.Matches(new Regex(@"<MauiIcon [^>]*Color=""#0F4DBC"""), csproj);
        Assert.Matches(new Regex(@"<MauiSplashScreen [^>]*Color=""#0F4DBC"""), csproj);
        Assert.DoesNotContain("#512BD4", csproj);

        var resources = Path.Combine(root, "source", "BlogApp", "Resources");
        var foreground = File.ReadAllBytes(Path.Combine(resources, "AppIcon", "appiconfg.svg"));
        var splash = File.ReadAllBytes(Path.Combine(resources, "Splash", "splash.svg"));
        Assert.Equal(foreground, splash);
        Assert.Empty(Directory.EnumerateFiles(resources, "*.png", SearchOption.AllDirectories));

        var siteGlyph = Path.Combine(root, "source", "BlogUI", "wwwroot", "images", "brand", "TechieBlog-appiconfg.svg");
        if (File.Exists(siteGlyph))
        {
            Assert.Equal(foreground, File.ReadAllBytes(siteGlyph));
        }
    }

    private static string Root()
    {
        var root = RepositoryLocator.FindRoot();
        Assert.SkipWhen(root == null, "repository root not found next to the test assembly");
        return root!;
    }

    private static void AssertPng(string path)
    {
        Assert.True(File.Exists(path), $"{path} missing");
        var head = File.ReadAllBytes(path).Take(8).ToArray();
        Assert.Equal(PngSignature, head);
    }

    private static void AssertPngSize(string path, int size)
    {
        AssertPng(path);
        var bytes = File.ReadAllBytes(path);
        var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        Assert.Equal(size, width);
        Assert.Equal(size, height);
    }
}
