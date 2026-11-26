using BlogEngine;
using BlogEngine.Services;
using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TechieBlog.Tests.TestDoubles;

namespace TechieBlog.Tests.Engagement;

/// <summary>
/// [REQ-FN-048] [REQ-FN-033] The confirmation-link sender the engine registers really mails the link
/// whenever a mail server is set — on the Settings screen first, in configuration as the fallback —
/// and only logs it when none is set. The choice is made at every send, so saving a mail server on the
/// Settings screen takes effect on the next mail with no restart.
/// </summary>
/// <remarks>
/// Written after the 2026-11-26 live report: a reader rated a post and never got the mail, because only
/// the logging sender was ever registered, and the mail values saved on the Settings screen were never
/// read by any sender.
/// </remarks>
public class VerificationSenderRegistrationTests
{
    /// <summary>
    /// A mail server saved on the Settings screen, and nothing in configuration: the link is mailed.
    /// </summary>
    [Fact]
    public async Task SiteSettingsMailServerMailsTheLink()
    {
        var harness = new Harness();
        harness.SiteSettings.Settings.Smtp = MailServer("smtp.site.test");

        await harness.SendAsync();

        Assert.Single(harness.Transport.SentMessages);
    }

    /// <summary>
    /// Nothing on the Settings screen, a mail server in configuration: configuration is the fallback.
    /// </summary>
    [Fact]
    public async Task ConfigurationIsTheFallback()
    {
        var harness = new Harness(new Dictionary<string, string?>
        {
            ["EmailSettings:SmtpHost"] = "smtp.config.test",
            ["EmailSettings:FromAddress"] = "blog@config.test"
        });

        await harness.SendAsync();

        Assert.Single(harness.Transport.SentMessages);
    }

    /// <summary>
    /// No mail server anywhere: nothing reaches the mail transport; the link is only logged.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NoMailServerOnlyLogs(string? host)
    {
        var harness = new Harness(new Dictionary<string, string?> { ["EmailSettings:SmtpHost"] = host });

        await harness.SendAsync();

        Assert.Empty(harness.Transport.SentMessages);
    }

    /// <summary>
    /// The same resolved sender logs while nothing is set, then mails as soon as a mail server is saved
    /// on the Settings screen — the choice is made per send, not per process.
    /// </summary>
    [Fact]
    public async Task SavingAMailServerTakesEffectOnTheNextSend()
    {
        var harness = new Harness();
        await harness.SendAsync();

        harness.SiteSettings.Settings.Smtp = MailServer("smtp.site.test");
        await harness.SendAsync();

        Assert.Single(harness.Transport.SentMessages);
    }

    /// <summary>
    /// Builds Settings-screen mail server values.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>Complete values.</returns>
    internal static SmtpSettings MailServer(string host)
    {
        return new SmtpSettings { Host = host, Port = 587, FromAddress = "blog@site.test" };
    }

    /// <summary>
    /// The engine's registrations over a recording transport.
    /// </summary>
    private sealed class Harness
    {
        private readonly IVerificationEmailSender sender;

        public Harness(Dictionary<string, string?>? configuration = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new FakeConfiguration(configuration ?? new Dictionary<string, string?>()));
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<IEmailService>(Transport);
            services.AddSingleton<ISiteSettingsService>(SiteSettings);
            services.AddTransient<IMailServerSource>(x => new MailServerSource(
                SiteSettings, x.GetService<IConfiguration>(), NullLogger<MailServerSource>.Instance));
            EngagementSvcInitializer.AddEngagementServices(services, "Host=unused");
            sender = services.BuildServiceProvider().GetRequiredService<IVerificationEmailSender>();
        }

        public RecordingEmailService Transport { get; } = new RecordingEmailService();

        public FakeSiteSettingsService SiteSettings { get; } = new FakeSiteSettingsService();

        public Task SendAsync()
        {
            return sender.SendVerificationEmailAsync("reader@example.test", "Reader", "rating", "https://blog.test/verify/x");
        }
    }
}
