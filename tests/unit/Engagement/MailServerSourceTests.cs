using BlogEngine.Services;
using BlogModels.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// [REQ-FN-033] The mail server is read from Site Settings at every send; configuration is only the
/// fallback.
/// </summary>
public class MailServerSourceTests
{
    /// <summary>
    /// When both the Settings screen and configuration name a mail server, the Settings screen wins.
    /// </summary>
    [Fact]
    public async Task SiteSettingsWinsOverConfiguration()
    {
        var siteSettings = new FakeSiteSettingsService();
        siteSettings.Settings.Smtp = new SmtpSettings { Host = "smtp.site.test", FromAddress = "a@site.test" };
        var source = Build(siteSettings, new() { ["EmailSettings:SmtpHost"] = "smtp.config.test", ["EmailSettings:FromAddress"] = "a@config.test" });

        var choice = await source.GetCurrentAsync();

        Assert.Equal(("smtp.site.test", MailServerOrigin.SiteSettings), (choice.Server.Host, choice.Origin));
    }

    /// <summary>
    /// A blank From address on the Settings screen falls back to the user name, as a Gmail or hosted
    /// mailbox expects, and still counts as a mail server.
    /// </summary>
    [Fact]
    public async Task BlankFromAddressUsesTheUserName()
    {
        var siteSettings = new FakeSiteSettingsService();
        siteSettings.Settings.Smtp = new SmtpSettings { Host = "smtp.gmail.com", UserName = "owner@gmail.com" };

        var choice = await Build(siteSettings, new()).GetCurrentAsync();

        Assert.Equal("owner@gmail.com", choice.Server.FromAddress);
    }

    /// <summary>
    /// A settings store that cannot be read does not stop mail when configuration names a server.
    /// </summary>
    [Fact]
    public async Task UnreadableSiteSettingsFallsBackToConfiguration()
    {
        var siteSettings = new FakeSiteSettingsService { ThrowOnRead = true };
        var choice = await Build(siteSettings, new() { ["EmailSettings:SmtpHost"] = "smtp.config.test", ["EmailSettings:FromAddress"] = "a@config.test" })
            .GetCurrentAsync();

        Assert.Equal(MailServerOrigin.Configuration, choice.Origin);
    }

    /// <summary>
    /// Nothing anywhere means no mail server.
    /// </summary>
    [Fact]
    public async Task NothingSetMeansNotAvailable()
    {
        var choice = await Build(new FakeSiteSettingsService(), new()).GetCurrentAsync();

        Assert.False(choice.IsAvailable);
    }

    private static MailServerSource Build(FakeSiteSettingsService siteSettings, Dictionary<string, string?> configuration)
    {
        return new MailServerSource(siteSettings, new FakeConfiguration(configuration), NullLogger<MailServerSource>.Instance);
    }
}
