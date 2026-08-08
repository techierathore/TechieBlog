using System.Security.Cryptography;
using System.Text;

namespace BlogEngine.Common;

/// <summary>
/// Derives the irreversible visitor identifier used by post-view analytics.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Lets <c>PostViewTracker</c> distinguish visitors without ever storing a raw
/// IP address (REQ-FN-034). The hash is what "unique view" is counted on.</para>
///
/// <para><b>Code Flow:</b></para>
/// <list type="number">
///   <item>The tracker passes the site salt, the caller's IP address and its user-agent string.</item>
///   <item>The three are joined with a separator that cannot appear in an IP address, so
///         <c>"1.2.3" + "4|UA"</c> and <c>"1.2.34" + "|UA"</c> cannot collide.</item>
///   <item>SHA-256 produces a 64-character lowercase hex digest, which is exactly the width of the
///         <c>PostViews.VisitorHash</c> column.</item>
/// </list>
///
/// <para><b>Privacy — exactly what is hashed, and what the result can and cannot do.</b> The
/// digest is <c>SHA-256(siteSalt | ipAddress | userAgent)</c>. Nothing else about the visitor is
/// consumed and nothing but the digest is retained: the raw IP address and user-agent string are
/// used as hash input and then dropped, so they never reach the <c>PostViews</c> table or the
/// logs.</para>
/// <list type="bullet">
///   <item><b>Salted — yes, and it is load-bearing.</b> The IPv4 space is 2^32 addresses, small
///     enough to enumerate exhaustively in minutes, so an <i>unsalted</i> hash of an IP address is
///     not a pseudonym at all — it is an obfuscated address that anyone can invert with a rainbow
///     table. The site salt (a deployment secret from <c>Analytics:VisitorSalt</c>) is what makes
///     the digest genuinely one-way. <b>A deployment that leaves the salt empty or checks it into
///     source loses that property entirely</b> and turns the column back into recoverable personal
///     data.</item>
///   <item><b>Reversible — no</b>, given a secret salt: SHA-256 is preimage-resistant and the
///     attacker cannot enumerate candidates without the salt. Anyone who <i>holds</i> the salt can
///     of course confirm a guessed IP/user-agent pair by recomputing the digest, so the salt is a
///     secret with the same handling requirements as a database credential.</item>
///   <item><b>Linkable across posts — yes, deliberately.</b> The hash input contains no post
///     identifier, so the same visitor produces the same digest on every post they read. That is
///     what makes "unique view" mean unique <i>person</i> rather than unique <i>page load</i>, and
///     it is the intended behaviour — but it does mean the <c>PostViews</c> table supports building
///     a per-visitor reading history within the retention window. Treat <c>VisitorHash</c> as
///     pseudonymous personal data, not as anonymous data: it is unlinkable to a real identity, not
///     unlinkable to itself. Adding the post id to the hash input would remove cross-post
///     linkability at the cost of the unique-visitor metric; that trade has not been taken.</item>
///   <item><b>Stable over time — only while the inputs are.</b> A visitor on a dynamic IP or a
///     browser that updates its user-agent string becomes a new pseudonym, so unique-view counts
///     drift upward slightly. Rotating <c>Analytics:VisitorSalt</c> resets every pseudonym at once,
///     which is a blunt but effective way to break historical linkability.</item>
/// </list>
///
/// <para><b>Dependencies:</b> <c>System.Security.Cryptography</c> only.</para>
///
/// <para><b>Usage:</b> Static helper; call <see cref="ComputeHash"/> per view.</para>
/// </remarks>
public static class VisitorHasher
{
    /// <summary>
    /// Separator that cannot occur inside an IP address, so concatenated inputs stay unambiguous.
    /// </summary>
    private const string FieldSeparator = "|";

    /// <summary>
    /// Computes the salted visitor hash for one request.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> Missing IP or user-agent values are tolerated — an anonymised
    /// proxy still gets a stable, if coarser, identity rather than being dropped from analytics.</para>
    /// <para><b>Flow:</b> normalise the inputs → join with the separator → SHA-256 → lowercase hex.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// </remarks>
    /// <param name="siteSalt">Deployment-specific salt from <c>Analytics:VisitorSalt</c>.</param>
    /// <param name="ipAddress">The caller's IP address; used only as hash input, never stored.</param>
    /// <param name="userAgent">The caller's user-agent string; used only as hash input.</param>
    /// <returns>A 64-character lowercase hexadecimal SHA-256 digest.</returns>
    public static string ComputeHash(string siteSalt, string ipAddress, string userAgent)
    {
        var material = string.Concat(
            siteSalt ?? string.Empty, FieldSeparator,
            (ipAddress ?? string.Empty).Trim(), FieldSeparator,
            (userAgent ?? string.Empty).Trim());

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
