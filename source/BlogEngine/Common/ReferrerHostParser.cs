namespace BlogEngine.Common;

/// <summary>
/// Reduces a browser's <c>Referer</c> header to the name of the referring site, or nothing.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> REQ-UI-071 (BRD-111) — "where did readers come from". Only the site's
/// name is kept. The full address can carry a search phrase, a session token or a private
/// document path, so the path and the query are dropped here, before anything is queued or
/// stored, and nothing downstream ever sees them.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>An empty header, a relative or unparseable address, or a scheme other than
///         <c>http</c>/<c>https</c> gives <c>null</c> ("Direct or unknown").</item>
///   <item>The host is lower-cased and a leading <c>www.</c> removed, so
///         <c>https://www.Google.com/search?q=…</c> becomes <c>google.com</c>.</item>
///   <item>A host equal to the site's own host (after the same normalisation) gives <c>null</c>:
///         moving from one post to another is not a referral.</item>
///   <item>A host longer than a legal DNS name (253 characters) gives <c>null</c>, matching the
///         column width in migration 033.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <see cref="System.Uri"/> only.</para>
///
/// <para><b>Usage:</b> Called by <c>PostViewTracker.TrackCurrentVisitAsync</c> while the HTTP request
/// is still in flight.</para>
/// </remarks>
public static class ReferrerHostParser
{
    /// <summary>
    /// The longest host name stored; the width of <c>PostViews.ReferrerHost</c>.
    /// </summary>
    public const int MaxHostLength = 253;

    private const string WwwPrefix = "www.";

    /// <summary>
    /// Returns the referring site's host name, or <c>null</c> for a direct, unknown or same-site visit.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> see the class remarks; the path, query and fragment are never
    /// returned.</para>
    /// <para><b>Flow:</b> parse → check scheme → normalise host → compare with the own host.</para>
    /// <para><b>Side Effects:</b> none.</para>
    /// </remarks>
    /// <param name="refererHeader">The raw <c>Referer</c> header value, possibly empty.</param>
    /// <param name="ownHost">The host the request was addressed to, possibly empty.</param>
    /// <returns>The normalised referring host, or <c>null</c>.</returns>
    public static string? Parse(string? refererHeader, string? ownHost)
    {
        if (string.IsNullOrWhiteSpace(refererHeader))
            return null;

        if (!Uri.TryCreate(refererHeader.Trim(), UriKind.Absolute, out var referer))
            return null;

        if (referer.Scheme != Uri.UriSchemeHttp && referer.Scheme != Uri.UriSchemeHttps)
            return null;

        var host = Normalise(referer.Host);
        if (host.Length == 0 || host.Length > MaxHostLength)
            return null;

        return host == Normalise(ownHost) ? null : host;
    }

    /// <summary>
    /// Lower-cases a host name and removes a leading <c>www.</c>.
    /// </summary>
    /// <param name="host">A host name, possibly <c>null</c>.</param>
    /// <returns>The normalised host; empty for <c>null</c> or blank input.</returns>
    public static string Normalise(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return string.Empty;

        var lowered = host.Trim().ToLowerInvariant();
        return lowered.StartsWith(WwwPrefix, StringComparison.Ordinal) ? lowered[WwwPrefix.Length..] : lowered;
    }
}
