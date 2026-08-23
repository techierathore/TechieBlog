using BlogModels.Interfaces;
using BlogModels.Models;

namespace BlogEngine.Common;

/// <summary>
/// The one shared way a component obtains the site's public identity, without ever holding a
/// reference to the full <see cref="SiteSettings"/> aggregate (UAT-021 / UAT-022).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="SiteSettings"/> carries two live credentials and a real
/// administrator mailbox; a component that renders on a public page — the header, the footer, the
/// auth shell, a page's document title — has no business holding a reference to any of that. This
/// extension is the single narrowing step every one of those callers goes through, so the
/// projection logic (and the blank-title fallback) exists exactly once instead of being
/// re-implemented, and possibly gotten wrong, in each of them.</para>
///
/// <para><b>Code Flow:</b> Reads the already-cached effective settings through
/// <see cref="ISiteSettingsService.GetSettingsAsync"/> — the same cheap, shared-instance read the
/// layout already relies on for other purposes — and narrows it to <see cref="SiteIdentity"/>.
/// Nothing here mutates the shared instance; only two of its scalar strings are copied out.</para>
///
/// <para><b>Dependencies:</b> <see cref="ISiteSettingsService"/>, <see cref="SiteIdentity"/>.</para>
///
/// <para><b>Usage:</b> An extension method rather than a new injectable service deliberately —
/// every caller already reaches <see cref="ISiteSettingsService"/> through the existing singleton
/// registration, so no new DI wiring is needed for one narrowing call.</para>
/// </remarks>
public static class SiteIdentityExtensions
{
    /// <summary>
    /// The site name shown when no title has been configured. Mirrors
    /// <see cref="SiteSettings.SiteTitle"/>'s own built-in default, kept here too because a row
    /// blanked from outside this application (direct SQL, a partial restore) can reach this
    /// projection with an empty <c>SettingValue</c> that the mapper's "absent key" fallback never
    /// catches.
    /// </summary>
    private const string DefaultSiteTitle = "TechieBlog";

    /// <summary>
    /// Projects the effective settings down to the public site identity.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank stored title falls back to <see cref="DefaultSiteTitle"/>
    /// rather than rendering an empty document title or an empty brand mark — the same "never blank"
    /// guarantee the acceptance criteria ask for.</para>
    /// <para><b>Side Effects:</b> None beyond whatever the first settings load does.</para>
    /// </remarks>
    /// <param name="siteSettingsService">The settings service to read through.</param>
    /// <returns>The site's title and logo path; never null, and the title is never blank.</returns>
    public static async Task<SiteIdentity> GetSiteIdentityAsync(this ISiteSettingsService siteSettingsService)
    {
        ArgumentNullException.ThrowIfNull(siteSettingsService);

        var settings = await siteSettingsService.GetSettingsAsync().ConfigureAwait(false);
        var title = string.IsNullOrWhiteSpace(settings.SiteTitle) ? DefaultSiteTitle : settings.SiteTitle;
        return new SiteIdentity(
            title,
            settings.SiteLogoPath ?? string.Empty,
            settings.SiteTagline ?? string.Empty);
    }
}
