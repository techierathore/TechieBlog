using BlogModels.Interfaces;
using BlogModels.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Services;

/// <summary>
/// Reads the mail server from Site Settings at every send, falling back to configuration.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Closes the gap where the mail values an administrator saved on the Settings
/// screen were encrypted, stored and never used (REQ-FN-033, "configured from Site Settings").</para>
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><see cref="ISiteSettingsService.GetSmtpSettingsAsync"/> — a copy of the cached, already
///         decrypted values, so this is a field read on the hot path, not a query.</item>
///   <item>When that has no host, the <c>EmailSettings</c> section of configuration.</item>
///   <item>Otherwise <see cref="MailServerChoice.NotSet"/>.</item>
/// </list>
/// <para><b>Dependencies:</b> <see cref="ISiteSettingsService"/> (singleton), <see cref="IConfiguration"/>,
/// <see cref="ILogger{TCategoryName}"/>.</para>
/// <para><b>Usage:</b> Registered transient by <c>BlogSvcInitializer</c>. No deploy secret and no
/// environment value is needed for mail: the Settings screen is the place it is set.</para>
/// </remarks>
public class MailServerSource : IMailServerSource
{
    /// <summary>Configuration section read when Site Settings holds no mail server.</summary>
    public const string ConfigurationSection = "EmailSettings";

    private const int DefaultSmtpPort = 587;

    private readonly ISiteSettingsService siteSettingsService;
    private readonly IConfiguration? configuration;
    private readonly ILogger<MailServerSource> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailServerSource"/> class.
    /// </summary>
    /// <param name="siteSettingsService">The site settings store.</param>
    /// <param name="configuration">Application configuration; may be null in a test host.</param>
    /// <param name="logger">Logger for a settings store that cannot be read.</param>
    public MailServerSource(
        ISiteSettingsService siteSettingsService,
        IConfiguration? configuration,
        ILogger<MailServerSource> logger)
    {
        this.siteSettingsService = siteSettingsService;
        this.configuration = configuration;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<MailServerChoice> GetCurrentAsync()
    {
        var fromSite = Complete(await ReadSiteSettingsAsync().ConfigureAwait(false));
        if (fromSite.IsConfigured)
            return new MailServerChoice { Server = fromSite, Origin = MailServerOrigin.SiteSettings };

        var fromConfiguration = Complete(ReadConfiguration());
        if (fromConfiguration.IsConfigured)
            return new MailServerChoice { Server = fromConfiguration, Origin = MailServerOrigin.Configuration };

        return MailServerChoice.NotSet();
    }

    /// <summary>
    /// Reads the Settings-screen values, treating an unreadable store as "nothing set there".
    /// </summary>
    /// <returns>A copy of the saved values, or empty values.</returns>
    private async Task<SmtpSettings> ReadSiteSettingsAsync()
    {
        try
        {
            return await siteSettingsService.GetSmtpSettingsAsync().ConfigureAwait(false) ?? new SmtpSettings();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the mail server from Site Settings; trying configuration");
            return new SmtpSettings();
        }
    }

    /// <summary>
    /// Reads the <c>EmailSettings</c> configuration section.
    /// </summary>
    /// <returns>The configured values, or empty values.</returns>
    private SmtpSettings ReadConfiguration()
    {
        if (configuration == null)
            return new SmtpSettings();

        return new SmtpSettings
        {
            Host = Read("SmtpHost"),
            Port = int.TryParse(Read("SmtpPort"), out var port) ? port : DefaultSmtpPort,
            IsSslEnabled = !bool.TryParse(Read("EnableSsl"), out var isSsl) || isSsl,
            UserName = Read("UserName"),
            Password = Read("Password"),
            FromAddress = Read("FromAddress"),
            FromName = Read("FromName")
        };
    }

    /// <summary>
    /// Reads one key of the <c>EmailSettings</c> section.
    /// </summary>
    /// <param name="key">The key inside the section.</param>
    /// <returns>The value, or an empty string.</returns>
    private string Read(string key)
    {
        return configuration?[$"{ConfigurationSection}:{key}"] ?? string.Empty;
    }

    /// <summary>
    /// Trims the values and lets a blank sender address fall back to the user name, which is what a
    /// Gmail or hosted-mailbox account expects.
    /// </summary>
    /// <param name="server">The values read.</param>
    /// <returns>The same instance, completed.</returns>
    private static SmtpSettings Complete(SmtpSettings server)
    {
        server.Host = (server.Host ?? string.Empty).Trim();
        server.UserName = (server.UserName ?? string.Empty).Trim();
        server.FromAddress = (server.FromAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(server.FromAddress))
            server.FromAddress = server.UserName;

        if (string.IsNullOrWhiteSpace(server.FromName))
            server.FromName = "TechieBlog";

        return server;
    }
}
