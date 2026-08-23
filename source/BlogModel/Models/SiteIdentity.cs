namespace BlogModels.Models;

/// <summary>
/// Minimal, public-safe projection of <see cref="SiteSettings"/> carrying only what a page's
/// chrome needs to render the site's identity — its configured name and logo (UAT-021 / UAT-022).
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <see cref="SiteSettings"/> is not safe to hand to a component that renders
/// on a public page — a populated instance carries <c>Smtp.Password</c>,
/// <c>Storage.CloudAccessKey</c> and <see cref="SiteSettings.AdminEmail"/> (see that type's own
/// exposure warning). This type is the handful of properties a header, footer, admin sidebar or
/// sign-in shell actually needs, and nothing else.</para>
///
/// <para><b>Usage:</b> Obtain it through <c>SiteIdentityExtensions.GetSiteIdentityAsync</c> — never
/// construct it from a raw <see cref="SiteSettings"/> read outside that one projection, or the
/// narrowing this type exists for is bypassed. <see cref="SiteLogoPath"/> is empty when no logo has
/// been configured; that is the signal for a consuming component to render its built-in glyph
/// instead of an <c>&lt;img&gt;</c>, never a broken image icon.</para>
/// </remarks>
/// <param name="SiteTitle">The configured site name; never null or blank — falls back to the
/// built-in default when the stored value is blank.</param>
/// <param name="SiteLogoPath">The configured logo's path or URL, or empty when none is set.</param>
/// <param name="SiteTagline">The configured tagline, or empty when none is set. Public branding
/// text, exactly like the title — it is what the browser tab shows after the site name, and it was
/// added here (2026-08-23) because <c>Home.razor</c> had the tagline HARDCODED, so changing it in
/// Settings changed nothing. Widening this record is deliberate and stays within its rule: only
/// values that are already rendered publicly may live here — never a credential or the admin
/// email.</param>
public sealed record SiteIdentity(string SiteTitle, string SiteLogoPath, string SiteTagline);
