namespace BlogModels.Common;

/// <summary>
/// Joins a configured site base address with a site-relative path.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Two BlogApp seams need the exact same arithmetic — "the operator's
/// configured website address" plus "a path this process only knows as site-relative" — and
/// getting it wrong produces either a double slash (<c>https://site.com//post/slug</c>, which some
/// servers 404 on) or a missing one (<c>https://site.compost/slug</c>). <c>DesktopLinkOpener</c>
/// (UAT-024) combines <c>SiteBaseUrl</c> with a post's public path before handing it to the OS
/// browser; <c>RemoteSiteCacheNotifier</c> (UAT-023 mechanism B) combines it with the website's
/// cache-refresh endpoint path. Both used to duplicate this arithmetic inline; this is the one
/// place it is written and tested.</para>
/// <para><b>Code Flow:</b> pure and static — no I/O, no platform dependency, callable from any
/// project (including BlogApp, which has no reference back to BlogEngine or BlogUI's test
/// infrastructure) and testable with no MAUI runtime.</para>
/// <para><b>Dependencies:</b> None.</para>
/// </remarks>
public static class SiteUrlResolver
{
    /// <summary>
    /// Combines a base URL with a relative path, tolerating either side's slashes.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A blank base means "no site configured" — callers use that to
    /// distinguish "not configured" from "configured but unreachable," so this returns <c>null</c>
    /// rather than fabricating a partial URL. Exactly one slash always separates the two halves,
    /// however many either side was supplied with.</para>
    /// <para><b>Flow:</b> blank guard → trim one trailing slash off the base → trim one leading
    /// slash off the path → join with a single slash.</para>
    /// </remarks>
    /// <param name="baseUrl">The configured site address, e.g. <c>https://example.com</c> or
    /// <c>https://example.com/</c>. <c>null</c> or blank means "not configured."</param>
    /// <param name="relativePath">A site-relative path, e.g. <c>/post/my-slug</c> or
    /// <c>post/my-slug</c>.</param>
    /// <returns>The combined absolute URL, or <c>null</c> when <paramref name="baseUrl"/> is blank.</returns>
    public static string? Combine(string? baseUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        var trimmedRelative = relativePath?.Trim() ?? string.Empty;
        return baseUrl.TrimEnd('/') + "/" + trimmedRelative.TrimStart('/');
    }
}
