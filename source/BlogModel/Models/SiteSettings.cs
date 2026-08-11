namespace BlogModels.Models;

/// <summary>
/// Strongly typed aggregate of every site-wide setting (BRD-69).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> The single object application code reads to learn how the site is
/// configured — title, tagline, pagination, comment moderation, theme, SMTP and storage. It
/// replaces the per-browser local-storage preferences that previously stood in for site
/// configuration.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>SiteSettingsService</c> loads all <see cref="SiteSetting"/> rows once and projects
///     them onto this type.</item>
///   <item>The projection is cached, so page renders do not hit the database.</item>
///   <item>Saving writes the rows back and drops the cache, so a change takes effect on the next
///     read with no restart.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="SmtpSettings"/>, <see cref="StorageSettings"/>.</para>
///
/// <para><b>Usage:</b> Never construct this directly outside tests — obtain it from
/// <c>ISiteSettingsService.GetSettingsAsync</c> so defaults and decryption are applied. Every
/// property has a usable built-in default, so a site with an empty <c>SiteSetting</c> table still
/// renders; that also means an absent row and a deliberately blanked one are indistinguishable
/// here.</para>
///
/// <para><b>Exposure:</b> a populated instance contains two live credentials —
/// <c>Smtp.Password</c> and <c>Storage.CloudAccessKey</c>. Do not serialise the whole aggregate to
/// the browser, log it, or hand it to a component that renders on a public page; project the
/// handful of properties a public page actually needs (title, tagline, theme, social URLs) instead.
/// <see cref="AdminEmail"/> is a real address and belongs to the admin surface too.</para>
///
/// <para><b>Encryption at rest:</b> those same two values are the only encrypted settings, under
/// the single <c>AppEncryptionKey</c> configuration value with no key versioning. Rotating that key
/// makes both permanently undecryptable and they must be re-entered — see
/// <see cref="SmtpSettings"/> and <see cref="StorageSettings"/>.</para>
/// </remarks>
public class SiteSettings
{
    /// <summary>
    /// Site title shown in the browser tab, headers and feed metadata.
    /// </summary>
    public string SiteTitle { get; set; } = "TechieBlog";

    /// <summary>
    /// Short strapline displayed under the site title.
    /// </summary>
    public string SiteTagline { get; set; } = string.Empty;

    /// <summary>
    /// Address that receives administrative notifications. A real mailbox, so treat it as personal
    /// data: it belongs on the Settings screen and in outbound envelopes, never rendered into a
    /// public page where it would be harvested.
    /// </summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>
    /// How many posts a public listing page shows. Defaults to 10. Zero or a negative value is not
    /// rejected anywhere, so it reaches the listing query as a <c>LIMIT</c> and yields an empty or
    /// failing page — validate before saving.
    /// </summary>
    public int PostsPerPage { get; set; } = 10;

    /// <summary>
    /// Word count above which a single article is split across pages; zero disables splitting
    /// entirely. Measured in words of rendered body text, not characters.
    /// </summary>
    public int PaginationWordCount { get; set; } = 500;

    /// <summary>
    /// Whether the comment form is offered on posts at all. Turning it off hides the form; it does
    /// not hide or delete comments already approved and published.
    /// </summary>
    public bool AreCommentsAllowed { get; set; } = true;

    /// <summary>
    /// Whether a submitted comment is held for approval before it becomes visible. Defaults to
    /// true, so the safe behaviour survives a missing row; setting it false publishes visitor-
    /// supplied text immediately and makes the spam guard the only thing between a bot and the
    /// public page.
    /// </summary>
    public bool AreCommentsModerated { get; set; } = true;

    /// <summary>
    /// Whether self-service account registration is open. When false the registration route must
    /// refuse, not merely hide its link — this is a server-side policy value, and a hidden link is
    /// not an access control.
    /// </summary>
    public bool IsRegistrationAllowed { get; set; } = true;

    /// <summary>
    /// Identifier of the site-wide theme (<c>trblaze-modern</c>, <c>developer</c> or
    /// <c>minimal</c>). This is the server-side, admin-selected default that REQ-FN-039 /
    /// REQ-UI-032 require — a visitor's own light/dark toggle remains a per-browser preference
    /// layered on top.
    /// </summary>
    /// <remarks>
    /// The default is the shipped TrBlazeUI theme. The pre-REQ-UI-048 identifier
    /// <c>fluent-modern</c> is still accepted on read and mapped onto this value, so a database
    /// written before the migration keeps rendering.
    /// </remarks>
    public string SiteTheme { get; set; } = "trblaze-modern";

    /// <summary>
    /// Whether the site defaults to dark mode for visitors with no stored preference.
    /// </summary>
    /// <remarks>
    /// Ships as <c>true</c> (owner decision, 2026-08-10): the site opens dark. This value is the
    /// fallback used whenever the setting row is missing or unreadable, and it must agree with the
    /// seeded row (<c>025-DefaultToDarkMode.sql</c>) and with
    /// <c>ThemeService.GetSiteDefaultDarkModeAsync</c>'s failure path — a fresh database and an
    /// established one otherwise open in different modes. An administrator can still switch the
    /// site default back to light on /settings, and a visitor's own toggle still wins over both.
    /// </remarks>
    public bool IsDarkModeDefault { get; set; } = true;

    /// <summary>
    /// Default meta description emitted for pages that do not supply their own.
    /// </summary>
    public string MetaDescription { get; set; } = string.Empty;

    /// <summary>
    /// Default meta keywords emitted site-wide.
    /// </summary>
    public string MetaKeywords { get; set; } = string.Empty;

    /// <summary>
    /// Public Twitter/X profile URL.
    /// </summary>
    public string TwitterUrl { get; set; } = string.Empty;

    /// <summary>
    /// Public LinkedIn profile URL.
    /// </summary>
    public string LinkedInUrl { get; set; } = string.Empty;

    /// <summary>
    /// Public GitHub profile URL.
    /// </summary>
    public string GitHubUrl { get; set; } = string.Empty;

    /// <summary>
    /// Outbound e-mail configuration. Never null.
    /// </summary>
    public SmtpSettings Smtp { get; set; } = new SmtpSettings();

    /// <summary>
    /// Uploaded-media storage configuration. Never null.
    /// </summary>
    public StorageSettings Storage { get; set; } = new StorageSettings();

    /// <summary>
    /// Timestamp of the most recent write to any setting, or <c>DateTime.MinValue</c> when the
    /// site is still running on built-in defaults.
    /// </summary>
    public DateTime UpdatedOn { get; set; }
}
