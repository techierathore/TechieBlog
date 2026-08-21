using System.Globalization;
using BlogModels.Models;

namespace BlogEngine.Common;

/// <summary>
/// Translates between persisted key/value settings rows and the typed <see cref="SiteSettings"/>
/// aggregate.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Isolates the one place that knows which key feeds which property, so the
/// settings service stays small and the projection can be unit-tested without a database.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item><c>SiteSettingsService</c> reads rows, decrypts secrets and hands over a plain
///     key/value dictionary.</item>
///   <item><see cref="ToSettings"/> projects that dictionary onto the aggregate, substituting the
///     built-in default for any absent or unparseable key.</item>
///   <item><see cref="ToRows"/> reverses the projection ahead of a save.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>SiteSettingKeys</c>, <see cref="SiteSettings"/>.</para>
///
/// <para><b>Usage:</b> Pure and static — never holds state and never touches the database.</para>
/// </remarks>
public static class SiteSettingsMapper
{
    /// <summary>
    /// The keys whose values are encrypted at rest.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Only credentials qualify. Everything else is site
    /// configuration an administrator would expect to read back in the clear.</para>
    /// </remarks>
    public static readonly IReadOnlyCollection<string> SecretKeys = new[]
    {
        SiteSettingKeys.SmtpPassword,
        SiteSettingKeys.StorageCloudAccessKey
    };

    /// <summary>
    /// Projects a plain key/value dictionary onto the typed settings aggregate.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An absent or malformed value never fails the projection — the
    /// property keeps the built-in default so a partially seeded database still renders a site.</para>
    /// <para><b>Flow:</b> Start from a defaulted aggregate, then overwrite each property whose key
    /// is present and parseable.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="values">Decrypted setting values keyed by <c>SiteSettingKeys</c> constants.</param>
    /// <param name="updatedOn">Timestamp of the most recent write across all rows.</param>
    /// <returns>The effective settings. Never null.</returns>
    public static SiteSettings ToSettings(IReadOnlyDictionary<string, string> values, DateTime updatedOn)
    {
        ArgumentNullException.ThrowIfNull(values);

        var settings = new SiteSettings { UpdatedOn = updatedOn };
        ApplyGeneral(values, settings);
        ApplyBlog(values, settings);
        ApplyPresentation(values, settings);
        ApplySmtp(values, settings.Smtp);
        ApplyStorage(values, settings.Storage);
        return settings;
    }

    /// <summary>
    /// Projects the typed settings aggregate back onto persistable rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every key is always written, so the table is a complete record
    /// of the site's configuration rather than a sparse set of overrides.</para>
    /// <para><b>Flow:</b> Emit one row per key, tagging each with its group and secret flag.</para>
    /// <para><b>Side Effects:</b> None — values are returned in the clear and encrypted by the
    /// caller.</para>
    /// </remarks>
    /// <param name="settings">The settings to persist.</param>
    /// <returns>One row per known setting key.</returns>
    public static IReadOnlyCollection<SiteSetting> ToRows(SiteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var rows = new List<SiteSetting>();
        rows.AddRange(GeneralRows(settings));
        rows.AddRange(BlogRows(settings));
        rows.AddRange(PresentationRows(settings));
        rows.AddRange(SmtpRows(settings.Smtp ?? new SmtpSettings()));
        rows.AddRange(StorageRows(settings.Storage ?? new StorageSettings()));
        return rows;
    }

    /// <summary>
    /// Reports whether a key's value must be encrypted at rest.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Used on both the read and the write path so a value can never
    /// be encrypted on save and read back raw.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settingKey">The key to test.</param>
    /// <returns>True when the value is a credential.</returns>
    public static bool IsSecretKey(string settingKey)
    {
        return SecretKeys.Contains(settingKey, StringComparer.Ordinal);
    }

    /// <summary>
    /// Applies the identity group — title, tagline and administrator address.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Every property is read through <see cref="ReadText"/> with its
    /// own current value as the fallback, so an absent key leaves the built-in default in place.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="settings"/>.</para>
    /// </remarks>
    /// <param name="values">Decrypted setting values keyed by <c>SiteSettingKeys</c> constants.</param>
    /// <param name="settings">The aggregate being populated.</param>
    private static void ApplyGeneral(IReadOnlyDictionary<string, string> values, SiteSettings settings)
    {
        settings.SiteTitle = ReadText(values, SiteSettingKeys.SiteTitle, settings.SiteTitle);
        settings.SiteTagline = ReadText(values, SiteSettingKeys.SiteTagline, settings.SiteTagline);
        settings.AdminEmail = ReadText(values, SiteSettingKeys.AdminEmail, settings.AdminEmail);
    }

    /// <summary>
    /// Applies the blog-behaviour group — pagination sizes and the comment/registration switches.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> These are the flags that gate features at runtime, so an
    /// unparseable value must never be read as "off": <see cref="ReadFlag"/> falls back to the
    /// current value rather than to <c>false</c>, which would silently close comments or
    /// registration site-wide on a typo.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="settings"/>.</para>
    /// </remarks>
    /// <param name="values">Decrypted setting values keyed by <c>SiteSettingKeys</c> constants.</param>
    /// <param name="settings">The aggregate being populated.</param>
    private static void ApplyBlog(IReadOnlyDictionary<string, string> values, SiteSettings settings)
    {
        settings.PostsPerPage = ReadNumber(values, SiteSettingKeys.PostsPerPage, settings.PostsPerPage);
        settings.PaginationWordCount =
            ReadNumber(values, SiteSettingKeys.PaginationWordCount, settings.PaginationWordCount);
        settings.AreCommentsAllowed =
            ReadFlag(values, SiteSettingKeys.AreCommentsAllowed, settings.AreCommentsAllowed);
        settings.AreCommentsModerated =
            ReadFlag(values, SiteSettingKeys.AreCommentsModerated, settings.AreCommentsModerated);
        settings.IsRegistrationAllowed =
            ReadFlag(values, SiteSettingKeys.IsRegistrationAllowed, settings.IsRegistrationAllowed);
    }

    /// <summary>
    /// Applies the presentation groups — theme, SEO metadata and social links.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Three persisted groups (<c>Theme</c>, <c>Seo</c>,
    /// <c>Social</c>) are read together because they are all plain display strings with no
    /// interdependency; the group tag only matters when the values are written back.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="settings"/>.</para>
    /// </remarks>
    /// <param name="values">Decrypted setting values keyed by <c>SiteSettingKeys</c> constants.</param>
    /// <param name="settings">The aggregate being populated.</param>
    private static void ApplyPresentation(IReadOnlyDictionary<string, string> values, SiteSettings settings)
    {
        settings.SiteTheme = ReadText(values, SiteSettingKeys.SiteTheme, settings.SiteTheme);
        settings.IsDarkModeDefault =
            ReadFlag(values, SiteSettingKeys.IsDarkModeDefault, settings.IsDarkModeDefault);
        settings.MetaDescription = ReadText(values, SiteSettingKeys.MetaDescription, settings.MetaDescription);
        settings.MetaKeywords = ReadText(values, SiteSettingKeys.MetaKeywords, settings.MetaKeywords);
        settings.TwitterUrl = ReadText(values, SiteSettingKeys.TwitterUrl, settings.TwitterUrl);
        settings.LinkedInUrl = ReadText(values, SiteSettingKeys.LinkedInUrl, settings.LinkedInUrl);
        settings.GitHubUrl = ReadText(values, SiteSettingKeys.GitHubUrl, settings.GitHubUrl);
    }

    /// <summary>
    /// Applies the outbound-mail group onto the nested SMTP settings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>SmtpPassword</c> is one of the two
    /// <see cref="SecretKeys"/>; by the time it reaches this method the caller has already
    /// decrypted it, so this mapper only ever handles plaintext and never holds a key.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="smtp"/>.</para>
    /// </remarks>
    /// <param name="values">Decrypted setting values keyed by <c>SiteSettingKeys</c> constants.</param>
    /// <param name="smtp">The nested SMTP block being populated.</param>
    private static void ApplySmtp(IReadOnlyDictionary<string, string> values, SmtpSettings smtp)
    {
        smtp.Host = ReadText(values, SiteSettingKeys.SmtpHost, smtp.Host);
        smtp.Port = ReadNumber(values, SiteSettingKeys.SmtpPort, smtp.Port);
        smtp.IsSslEnabled = ReadFlag(values, SiteSettingKeys.SmtpIsSslEnabled, smtp.IsSslEnabled);
        smtp.UserName = ReadText(values, SiteSettingKeys.SmtpUserName, smtp.UserName);
        smtp.Password = ReadText(values, SiteSettingKeys.SmtpPassword, smtp.Password);
        smtp.FromAddress = ReadText(values, SiteSettingKeys.SmtpFromAddress, smtp.FromAddress);
        smtp.FromName = ReadText(values, SiteSettingKeys.SmtpFromName, smtp.FromName);
    }

    /// <summary>
    /// Applies the storage group onto the nested storage settings.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> This block is what <c>FileStorageFactory</c> reads to choose a
    /// provider on the very next upload, so an unrecognised <c>ProviderName</c> is not rejected
    /// here — the factory degrades it to local storage with a logged warning instead.
    /// <c>StorageCloudAccessKey</c> is the second of the two <see cref="SecretKeys"/> and arrives
    /// already decrypted.</para>
    /// <para><b>Side Effects:</b> Mutates <paramref name="storage"/>.</para>
    /// </remarks>
    /// <param name="values">Decrypted setting values keyed by <c>SiteSettingKeys</c> constants.</param>
    /// <param name="storage">The nested storage block being populated.</param>
    private static void ApplyStorage(IReadOnlyDictionary<string, string> values, StorageSettings storage)
    {
        storage.ProviderName = ReadText(values, SiteSettingKeys.StorageProviderName, storage.ProviderName);
        storage.LocalRootPath = ReadText(values, SiteSettingKeys.StorageLocalRootPath, storage.LocalRootPath);
        storage.NetworkRootPath =
            ReadText(values, SiteSettingKeys.StorageNetworkRootPath, storage.NetworkRootPath);
        storage.CloudServiceUrl =
            ReadText(values, SiteSettingKeys.StorageCloudServiceUrl, storage.CloudServiceUrl);
        storage.CloudContainerName =
            ReadText(values, SiteSettingKeys.StorageCloudContainerName, storage.CloudContainerName);
        storage.CloudAccessKey = ReadText(values, SiteSettingKeys.StorageCloudAccessKey, storage.CloudAccessKey);
        storage.PublicBaseUrl = ReadText(values, SiteSettingKeys.StoragePublicBaseUrl, storage.PublicBaseUrl);
    }

    /// <summary>
    /// Emits the identity group as persistable rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The write half of <see cref="ApplyGeneral"/>. Each
    /// <c>…Rows</c> method and its matching <c>Apply…</c> method must cover exactly the same keys —
    /// a key emitted here but not read back would persist and then be ignored, and one read but not
    /// emitted would be silently dropped on the next save.</para>
    /// <para><b>Side Effects:</b> None; lazily enumerated.</para>
    /// </remarks>
    /// <param name="settings">The settings being persisted.</param>
    /// <returns>One row per key in the group.</returns>
    private static IEnumerable<SiteSetting> GeneralRows(SiteSettings settings)
    {
        const string group = SiteSettingKeys.Groups.General;
        yield return Row(SiteSettingKeys.SiteTitle, settings.SiteTitle, group);
        yield return Row(SiteSettingKeys.SiteTagline, settings.SiteTagline, group);
        yield return Row(SiteSettingKeys.AdminEmail, settings.AdminEmail, group);
    }

    /// <summary>
    /// Emits the blog-behaviour group as persistable rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Numbers and flags are formatted through
    /// <see cref="WriteNumber"/> and <see cref="WriteFlag"/> so they round-trip through the
    /// invariant-culture parsers on the read path — a value written under a comma-decimal locale
    /// would otherwise fail to parse and silently revert to its default.</para>
    /// <para><b>Side Effects:</b> None; lazily enumerated.</para>
    /// </remarks>
    /// <param name="settings">The settings being persisted.</param>
    /// <returns>One row per key in the group.</returns>
    private static IEnumerable<SiteSetting> BlogRows(SiteSettings settings)
    {
        const string group = SiteSettingKeys.Groups.Blog;
        yield return Row(SiteSettingKeys.PostsPerPage, WriteNumber(settings.PostsPerPage), group);
        yield return Row(SiteSettingKeys.PaginationWordCount, WriteNumber(settings.PaginationWordCount), group);
        yield return Row(SiteSettingKeys.AreCommentsAllowed, WriteFlag(settings.AreCommentsAllowed), group);
        yield return Row(SiteSettingKeys.AreCommentsModerated, WriteFlag(settings.AreCommentsModerated), group);
        yield return Row(SiteSettingKeys.IsRegistrationAllowed, WriteFlag(settings.IsRegistrationAllowed), group);
    }

    /// <summary>
    /// Emits the theme, SEO and social groups as persistable rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Read together by <see cref="ApplyPresentation"/> but written
    /// under three distinct group tags, because the group is what the Settings screen uses to lay
    /// the values out in sections.</para>
    /// <para><b>Side Effects:</b> None; lazily enumerated.</para>
    /// </remarks>
    /// <param name="settings">The settings being persisted.</param>
    /// <returns>One row per key across the three groups.</returns>
    private static IEnumerable<SiteSetting> PresentationRows(SiteSettings settings)
    {
        yield return Row(SiteSettingKeys.SiteTheme, settings.SiteTheme, SiteSettingKeys.Groups.Theme);
        yield return Row(SiteSettingKeys.IsDarkModeDefault, WriteFlag(settings.IsDarkModeDefault),
            SiteSettingKeys.Groups.Theme);
        yield return Row(SiteSettingKeys.MetaDescription, settings.MetaDescription, SiteSettingKeys.Groups.Seo);
        yield return Row(SiteSettingKeys.MetaKeywords, settings.MetaKeywords, SiteSettingKeys.Groups.Seo);
        yield return Row(SiteSettingKeys.TwitterUrl, settings.TwitterUrl, SiteSettingKeys.Groups.Social);
        yield return Row(SiteSettingKeys.LinkedInUrl, settings.LinkedInUrl, SiteSettingKeys.Groups.Social);
        yield return Row(SiteSettingKeys.GitHubUrl, settings.GitHubUrl, SiteSettingKeys.Groups.Social);
    }

    /// <summary>
    /// Emits the outbound-mail group as persistable rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The password row is emitted in the clear and tagged
    /// <c>IsSecret</c> by <see cref="Row"/>; encrypting it is the caller's step, deliberately kept
    /// out of this pure mapper so the encryption key never has to reach it.</para>
    /// <para><b>Side Effects:</b> None; lazily enumerated.</para>
    /// </remarks>
    /// <param name="smtp">The SMTP block being persisted; never null by the time it arrives.</param>
    /// <returns>One row per key in the group.</returns>
    private static IEnumerable<SiteSetting> SmtpRows(SmtpSettings smtp)
    {
        const string group = SiteSettingKeys.Groups.Smtp;
        yield return Row(SiteSettingKeys.SmtpHost, smtp.Host, group);
        yield return Row(SiteSettingKeys.SmtpPort, WriteNumber(smtp.Port), group);
        yield return Row(SiteSettingKeys.SmtpIsSslEnabled, WriteFlag(smtp.IsSslEnabled), group);
        yield return Row(SiteSettingKeys.SmtpUserName, smtp.UserName, group);
        yield return Row(SiteSettingKeys.SmtpPassword, smtp.Password, group);
        yield return Row(SiteSettingKeys.SmtpFromAddress, smtp.FromAddress, group);
        yield return Row(SiteSettingKeys.SmtpFromName, smtp.FromName, group);
    }

    /// <summary>
    /// Emits the storage group as persistable rows.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The cloud access key is emitted in the clear and tagged
    /// <c>IsSecret</c>, for the same reason as the SMTP password. Every provider's settings are
    /// written whichever provider is currently selected, so switching back to a previously
    /// configured backend does not require re-entering its details.</para>
    /// <para><b>Side Effects:</b> None; lazily enumerated.</para>
    /// </remarks>
    /// <param name="storage">The storage block being persisted; never null by the time it arrives.</param>
    /// <returns>One row per key in the group.</returns>
    private static IEnumerable<SiteSetting> StorageRows(StorageSettings storage)
    {
        const string group = SiteSettingKeys.Groups.Storage;
        yield return Row(SiteSettingKeys.StorageProviderName, storage.ProviderName, group);
        yield return Row(SiteSettingKeys.StorageLocalRootPath, storage.LocalRootPath, group);
        yield return Row(SiteSettingKeys.StorageNetworkRootPath, storage.NetworkRootPath, group);
        yield return Row(SiteSettingKeys.StorageCloudServiceUrl, storage.CloudServiceUrl, group);
        yield return Row(SiteSettingKeys.StorageCloudContainerName, storage.CloudContainerName, group);
        yield return Row(SiteSettingKeys.StorageCloudAccessKey, storage.CloudAccessKey, group);
        yield return Row(SiteSettingKeys.StoragePublicBaseUrl, storage.PublicBaseUrl, group);
    }

    /// <summary>
    /// Builds one persistable row, tagging it with its group and secret flag.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The secret flag is derived from the key by
    /// <see cref="IsSecretKey"/> rather than passed in, so a caller cannot forget it and write a
    /// credential to the table unencrypted. A null value becomes an empty string, because the
    /// column is not nullable and an absent value and a blank one mean the same thing here.</para>
    /// <para><b>Side Effects:</b> None.</para>
    /// </remarks>
    /// <param name="settingKey">The <c>SiteSettingKeys</c> constant this row stores.</param>
    /// <param name="settingValue">The value in the clear; may be null.</param>
    /// <param name="settingGroup">The <c>SiteSettingKeys.Groups</c> tag the Settings screen groups by.</param>
    /// <returns>The populated row.</returns>
    private static SiteSetting Row(string settingKey, string settingValue, string settingGroup)
    {
        return new SiteSetting
        {
            SettingKey = settingKey,
            SettingValue = settingValue ?? string.Empty,
            SettingGroup = settingGroup,
            IsSecret = IsSecretKey(settingKey)
        };
    }

    /// <summary>
    /// Reads one string setting, falling back when the key is absent or its value is null.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> An empty string is a legitimate value — an administrator
    /// clearing the tagline means it — so only a missing key or a null value falls back.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="values">The decrypted values.</param>
    /// <param name="settingKey">The key to read.</param>
    /// <param name="fallback">The value to keep when the key is absent.</param>
    /// <returns>The stored value, or the fallback.</returns>
    private static string ReadText(
        IReadOnlyDictionary<string, string> values, string settingKey, string fallback)
    {
        return values.TryGetValue(settingKey, out var stored) && stored != null ? stored : fallback;
    }

    /// <summary>
    /// Reads one integer setting, falling back when the key is absent or unparseable.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Parsed with <see cref="CultureInfo.InvariantCulture"/> to match
    /// how <see cref="WriteNumber"/> formats it, so a value survives a round trip regardless of the
    /// server's locale. A malformed value degrades to the default rather than failing the whole
    /// projection — one bad row must not take the site down.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="values">The decrypted values.</param>
    /// <param name="settingKey">The key to read.</param>
    /// <param name="fallback">The value to keep when the key is absent or unparseable.</param>
    /// <returns>The parsed value, or the fallback.</returns>
    private static int ReadNumber(
        IReadOnlyDictionary<string, string> values, string settingKey, int fallback)
    {
        if (!values.TryGetValue(settingKey, out var stored))
        {
            return fallback;
        }

        return int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    /// <summary>
    /// Reads one boolean setting, falling back when the key is absent or unparseable.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Falls back to the caller's current value, never to
    /// <c>false</c>. These flags gate features — comments, moderation, registration — so
    /// interpreting a typo as "off" would take a capability away site-wide with no error anywhere.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="values">The decrypted values.</param>
    /// <param name="settingKey">The key to read.</param>
    /// <param name="fallback">The value to keep when the key is absent or unparseable.</param>
    /// <returns>The parsed flag, or the fallback.</returns>
    private static bool ReadFlag(
        IReadOnlyDictionary<string, string> values, string settingKey, bool fallback)
    {
        if (!values.TryGetValue(settingKey, out var stored))
        {
            return fallback;
        }

        return bool.TryParse(stored, out var parsed) ? parsed : fallback;
    }

    /// <summary>
    /// Formats an integer for storage, culture-independently.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Paired with <see cref="ReadNumber"/>; both pin
    /// <see cref="CultureInfo.InvariantCulture"/> so a value written on one host parses on
    /// another.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="value">The value to format.</param>
    /// <returns>The invariant string form.</returns>
    private static string WriteNumber(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a boolean for storage in the spelling <see cref="bool.TryParse"/> accepts.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> <c>bool.TrueString</c>/<c>bool.FalseString</c> rather than
    /// <c>"1"</c>/<c>"0"</c> or lower-case literals, because that is exactly what
    /// <see cref="ReadFlag"/>'s parser round-trips.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="value">The flag to format.</param>
    /// <returns><c>"True"</c> or <c>"False"</c>.</returns>
    private static string WriteFlag(bool value)
    {
        return value ? bool.TrueString : bool.FalseString;
    }
}
