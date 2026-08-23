using System.Reflection;
using BlogEngine.Common;
using BlogModels.Models;
using TechieBlog.Tests.Engagement;

namespace TechieBlog.Tests.Settings;

/// <summary>
/// Tests for <see cref="SiteIdentityExtensions.GetSiteIdentityAsync"/> — the one shared projection
/// a public-facing component uses to read the site's title and logo (UAT-021 / UAT-022).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Covers the three things the acceptance criteria call out explicitly: the
/// projection carries the configured values, it falls back to the built-in title when the stored
/// value is blank, and — the security assertion — it can never carry <c>Smtp.Password</c>,
/// <c>Storage.CloudAccessKey</c> or <see cref="SiteSettings.AdminEmail"/> out to a caller.</para>
///
/// <para><b>Dependencies:</b> <see cref="FakeSiteSettingsService"/>, the same in-memory double the
/// comment-moderation tests already use for <c>ISiteSettingsService</c>.</para>
/// </remarks>
public class SiteIdentityExtensionsTests
{
    /// <summary>
    /// A configured title and logo project through unchanged.
    /// </summary>
    [Fact]
    public async Task GetSiteIdentityAsyncProjectsConfiguredTitleAndLogo()
    {
        var service = new FakeSiteSettingsService
        {
            Settings = new SiteSettings
            {
                SiteTitle = "TechieRathore",
                SiteLogoPath = "/uploads/logos/mark.svg"
            }
        };

        var identity = await service.GetSiteIdentityAsync();

        Assert.Equal("TechieRathore", identity.SiteTitle);
        Assert.Equal("/uploads/logos/mark.svg", identity.SiteLogoPath);
    }

    /// <summary>
    /// A blank stored title (a row cleared by direct SQL, or a database that predates this
    /// feature) falls back to the built-in "TechieBlog" default rather than rendering an empty
    /// document title or brand mark.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSiteIdentityAsyncFallsBackToDefaultTitleWhenBlank(string blankTitle)
    {
        var service = new FakeSiteSettingsService
        {
            Settings = new SiteSettings { SiteTitle = blankTitle }
        };

        var identity = await service.GetSiteIdentityAsync();

        Assert.Equal("TechieBlog", identity.SiteTitle);
    }

    /// <summary>
    /// An unconfigured logo projects to an empty path — never null — which is the signal
    /// consuming chrome uses to render its built-in glyph instead of a broken <c>&lt;img&gt;</c>.
    /// </summary>
    [Fact]
    public async Task GetSiteIdentityAsyncLogoDefaultsToEmptyWhenUnset()
    {
        var service = new FakeSiteSettingsService { Settings = new SiteSettings() };

        var identity = await service.GetSiteIdentityAsync();

        Assert.Equal(string.Empty, identity.SiteLogoPath);
    }

    /// <summary>
    /// Security assertion (explicit, per the acceptance criteria): the projection can never carry
    /// the SMTP password, the cloud storage access key or the administrator's mailbox — not just
    /// "does not today", but structurally cannot, because <see cref="SiteIdentity"/> declares no
    /// property that could hold them.
    /// </summary>
    [Fact]
    public async Task GetSiteIdentityAsyncNeverExposesCredentialsOrAdminEmail()
    {
        var service = new FakeSiteSettingsService
        {
            Settings = new SiteSettings
            {
                SiteTitle = "TechieRathore",
                AdminEmail = "owner@techieblog.test",
                Smtp = new SmtpSettings { Password = "MailerSecret1" },
                Storage = new StorageSettings { CloudAccessKey = "CloudSecret1" }
            }
        };

        var identity = await service.GetSiteIdentityAsync();

        // Structural guarantee: SiteIdentity exposes exactly these PUBLIC BRANDING properties, so no
        // future edit can quietly widen it to carry a credential through this projection.
        // SiteTagline was added 2026-08-23: Home.razor had the tagline hard-coded, so editing it in
        // Settings changed the database and never the browser tab. It is public text rendered in the
        // page title, which is the same class of value as SiteTitle - widening to it is in the rule,
        // not an exception to it. The credential assertions below are what this test actually
        // defends, and they now cover all three members.
        var propertyNames = typeof(SiteIdentity)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "SiteLogoPath", "SiteTagline", "SiteTitle" }, propertyNames);

        // Belt-and-suspenders: every value that DID make it through carries none of the secrets.
        foreach (var exposed in new[] { identity.SiteTitle, identity.SiteLogoPath, identity.SiteTagline })
        {
            Assert.DoesNotContain("MailerSecret1", exposed);
            Assert.DoesNotContain("CloudSecret1", exposed);
            Assert.DoesNotContain("owner@techieblog.test", exposed);
        }
    }
}
