using BlogEngine.Common;
using BlogModels;
using BlogModels.Models;

namespace TechieBlog.Tests.Settings;

/// <summary>
/// Round-trip tests for <see cref="SiteSettingsMapper"/>'s newest key, <c>General.SiteLogo</c>
/// (UAT-022).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Pins the write half (<see cref="SiteSettingsMapper.ToRows"/>) and the
/// read half (<see cref="SiteSettingsMapper.ToSettings"/>) of the site logo setting against each
/// other, the way <c>SiteSettingsServiceTests</c> already does for every other key — a key emitted
/// by one half but not read by the other is silently dropped on the next save.</para>
///
/// <para><b>Dependencies:</b> None — the mapper is pure, so no database or repository double is
/// needed.</para>
/// </remarks>
public class SiteSettingsMapperTests
{
    /// <summary>
    /// A configured logo path is emitted as a <c>General.SiteLogo</c> row, in the General group,
    /// and not marked secret — it is a public asset path, not a credential.
    /// </summary>
    [Fact]
    public void ToRowsEmitsSiteLogoKeyInGeneralGroup()
    {
        var settings = new SiteSettings { SiteLogoPath = "/uploads/logos/logo-1-20260823-abcd1234.png" };

        var rows = SiteSettingsMapper.ToRows(settings);
        var logoRow = rows.Single(row => row.SettingKey == SiteSettingKeys.SiteLogo);

        Assert.Equal("/uploads/logos/logo-1-20260823-abcd1234.png", logoRow.SettingValue);
        Assert.Equal(SiteSettingKeys.Groups.General, logoRow.SettingGroup);
        Assert.False(logoRow.IsSecret);
    }

    /// <summary>
    /// A stored <c>General.SiteLogo</c> value projects onto <see cref="SiteSettings.SiteLogoPath"/>.
    /// </summary>
    [Fact]
    public void ToSettingsProjectsSiteLogoFromStoredValue()
    {
        var values = new Dictionary<string, string>
        {
            [SiteSettingKeys.SiteLogo] = "/uploads/logos/logo-1-20260823-abcd1234.png"
        };

        var settings = SiteSettingsMapper.ToSettings(values, DateTime.UtcNow);

        Assert.Equal("/uploads/logos/logo-1-20260823-abcd1234.png", settings.SiteLogoPath);
    }

    /// <summary>
    /// A database with no <c>General.SiteLogo</c> row — the state of every site before UAT-022 —
    /// projects to the built-in empty default rather than failing, so an existing site still
    /// renders.
    /// </summary>
    [Fact]
    public void ToSettingsFallsBackToEmptyWhenSiteLogoKeyIsAbsent()
    {
        var values = new Dictionary<string, string>();

        var settings = SiteSettingsMapper.ToSettings(values, DateTime.UtcNow);

        Assert.Equal(string.Empty, settings.SiteLogoPath);
    }

    /// <summary>
    /// The full round trip: a configured logo path survives <see cref="SiteSettingsMapper.ToRows"/>
    /// followed by <see cref="SiteSettingsMapper.ToSettings"/> unchanged — the regression guard for
    /// a key that is written but never read back, or read under the wrong name.
    /// </summary>
    [Fact]
    public void SiteLogoRoundTripsThroughRowsAndBack()
    {
        var original = new SiteSettings { SiteTitle = "TechieRathore", SiteLogoPath = "/uploads/logos/mark.svg" };

        var rows = SiteSettingsMapper.ToRows(original);
        var values = rows.ToDictionary(row => row.SettingKey, row => row.SettingValue);
        var reloaded = SiteSettingsMapper.ToSettings(values, DateTime.UtcNow);

        Assert.Equal(original.SiteLogoPath, reloaded.SiteLogoPath);
        Assert.Equal(original.SiteTitle, reloaded.SiteTitle);
    }
}
