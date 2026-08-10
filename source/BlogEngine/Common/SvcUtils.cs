using BlogModels;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace BlogSvc;

/// <summary>
/// Reads claims out of an already-issued access token. <b>It decodes; it does not validate.</b>
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> <c>AuthSvc</c> needs the subject id carried by a JWT before it can look
/// the session up in <c>UserLogin</c>. This helper is that one-line extraction, kept out of the
/// service so the service reads as business logic rather than token plumbing.</para>
///
/// <para><b>Code Flow:</b> <c>AuthSvc.GetUserByTokenAsync</c> → <see cref="GetUserIDFromToken"/> →
/// <c>JwtSecurityTokenHandler.ReadJwtToken</c> → the <c>primarysid</c> claim → back to
/// <c>AuthSvc</c>, which immediately re-reads the row from <c>UserLogin</c> and treats <i>that</i>
/// as the authority on whether the session is live.</para>
///
/// <para><b>Dependencies:</b> <c>System.IdentityModel.Tokens.Jwt</c>, and — for
/// <see cref="GetConnectionFromToken"/> only — <c>BlogModels.AppEncrypt</c>.</para>
///
/// <para><b>SECURITY — the signature is NOT verified here, and that is a known gap.</b>
/// <c>ReadJwtToken</c> parses and base64url-decodes a token; it checks neither the HMAC signature,
/// nor <c>exp</c>, nor the issuer or audience. Anyone can hand-craft a JWT with any
/// <c>primarysid</c> they like and this method will return it without complaint. What actually
/// makes a session valid in this application is the database: <c>AuthSvc</c> requires the exact
/// token string to still be recorded against that user id in <c>UserLogin</c>, which is why
/// revocation works and why a forged token does not simply walk in. Three consequences follow, and
/// they are stated plainly because a reader who assumes the signature is checked will write exactly
/// the bug this paragraph exists to prevent:</para>
/// <list type="number">
///   <item><b>No claim from this token may be trusted.</b> A role, an email or a display name read
///     out of the token is unverified attacker-controlled input. Everything <c>AuthSvc</c> returns
///     is deliberately re-read from <c>BlogUser</c> for that reason.</item>
///   <item><b>The <c>UserLogin</c> lookup is load-bearing.</b> It must never be made conditional,
///     cached away or skipped on a "fast path" — it is the only integrity check in the chain.</item>
///   <item><b>Rotating the signing key does not, by itself, invalidate anything</b>, because
///     nothing on the read path ever consults the key. Rotation was made to bite by a separate
///     route: the key is fingerprinted into the storage-key names, so a rotated key no longer
///     resolves the stored material. That is a workaround for this gap, not a replacement for it.</item>
/// </list>
/// <para><b>Outstanding work:</b> closing the gap means calling <c>ValidateToken</c> with the same
/// signing key and asserting the lifetime, issuer and audience, then keeping the <c>UserLogin</c>
/// lookup as the revocation check. Until that lands, treat the JWT as a <i>session handle</i> — an
/// opaque string whose only meaning is "this exact value is in the sessions table" — and never as
/// a bearer credential.</para>
///
/// <para><b>Usage:</b> Static helper called only from <c>AuthSvc</c>. Never call it on a token
/// that has not been, or will not immediately be, matched against <c>UserLogin</c>.</para>
///
/// <para><b>Namespace note:</b> this type lives in <c>BlogSvc</c> rather than
/// <c>BlogEngine.Common</c> like the rest of the folder — a legacy of the original service
/// assembly. Correcting it would ripple into every consumer's <c>using</c> list, so it is recorded
/// here rather than changed under a documentation pass.</para>
/// </remarks>
public static class SvcUtils
{
    /// <summary>
    /// Reads the subject (user) identifier out of an access token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The identifier is carried in the <c>primarysid</c> claim that
    /// <c>AuthSvc.GenerateJWToken</c> writes at sign-in. It is returned as a bare number so the
    /// caller can use it directly as the <c>UserLogin</c> lookup key.</para>
    /// <para><b>Flow:</b> decode the token → take the first <c>primarysid</c> claim → convert to
    /// <see cref="long"/>.</para>
    /// <para><b>Side Effects:</b> None; pure. Nothing is logged, because the token is session
    /// material and must not reach the log sink.</para>
    /// <para><b>Security:</b> the returned id is <b>unauthenticated</b> — see the type-level
    /// remarks. It is a lookup key, not proof of identity; the caller must confirm the token
    /// against <c>UserLogin</c> before acting on it.</para>
    /// </remarks>
    /// <param name="jwToken">The access token issued at sign-in.</param>
    /// <returns>The <c>primarysid</c> claim value as a user identifier.</returns>
    /// <exception cref="ArgumentException">The value is not a readable JWT.</exception>
    /// <exception cref="InvalidOperationException">The token carries no <c>primarysid</c> claim.</exception>
    /// <exception cref="FormatException">The claim value is not a number.</exception>
    public static long GetUserIDFromToken(string jwToken)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.ReadJwtToken(jwToken);
        var idValue = token.Claims.First(claim => claim.Type == "primarysid").Value;
        return Convert.ToInt64(idValue);
    }

    /// <summary>
    /// Reads the moment an access token stops being usable, in UTC.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> The <c>exp</c> claim written by <c>AuthSvc.GenerateJWToken</c>
    /// is what makes a session need refreshing (REQ-FN-008). A token minted before the claim was
    /// treated as meaningful — or by any other issuer that omitted it — carries no expiry at all,
    /// which is reported as <c>null</c> and read by the caller as "does not expire" rather than as
    /// "expired". Failing open here is deliberate: failing closed would sign out every session
    /// issued before this requirement landed.</para>
    /// <para><b>Flow:</b> decode the token → read <c>ValidTo</c> → map the sentinel to
    /// <c>null</c>.</para>
    /// <para><b>Side Effects:</b> None; pure. Nothing is logged — the token is session material.</para>
    /// <para><b>Security:</b> the same unverified-signature caveat as
    /// <see cref="GetUserIDFromToken"/> applies — this expiry is read out of an unvalidated token
    /// and a forged token could name any expiry it liked. It is not a defence on its own, and it is
    /// not used as one: <c>AuthSvc</c> only ever consults it for a token string it has already
    /// matched against the <c>UserLogin</c> row, so the expiry being read is the expiry of a token
    /// this application itself issued.</para>
    /// </remarks>
    /// <param name="jwToken">The access token issued at sign-in.</param>
    /// <returns>The UTC expiry, or <c>null</c> when the token carries no <c>exp</c> claim.</returns>
    /// <exception cref="ArgumentException">The value is not a readable JWT.</exception>
    public static DateTime? GetTokenExpiryUtc(string jwToken)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.ReadJwtToken(jwToken);
        return token.ValidTo == DateTime.MinValue
            ? null
            : DateTime.SpecifyKind(token.ValidTo, DateTimeKind.Utc);
    }

    /// <summary>
    /// Decrypts the database connection string carried by a multi-tenant token.
    /// </summary>
    /// <remarks>
    /// <para><b>Business Logic:</b> A survivor of the multi-tenant design this codebase started
    /// from, where each tenant's connection string travelled inside the token encrypted with
    /// <c>AppEncryptionKey</c>. TechieBlog is single-tenant and resolves its connection string from
    /// <c>AppDbConString</c> at composition time, so <b>this method has no callers today</b>.</para>
    /// <para><b>Flow:</b> decode the token → take the <see cref="ClaimTypes.Hash"/> claim →
    /// decrypt it with the application encryption key.</para>
    /// <para><b>Side Effects:</b> None; pure.</para>
    /// <para><b>Security:</b> the same unverified-signature caveat applies, and it bites harder
    /// here — the decode is unauthenticated, so the only thing preventing an attacker-supplied
    /// claim from being decrypted is that they cannot produce ciphertext under
    /// <c>AppEncryptionKey</c>. If this method is ever revived, verify the token signature first;
    /// a connection string is a far more dangerous thing to take from an unvalidated token than a
    /// user id is.</para>
    /// </remarks>
    /// <param name="jwToken">The token carrying the encrypted connection string.</param>
    /// <returns>The decrypted connection string.</returns>
    /// <exception cref="InvalidOperationException">The value is not a readable JWT, or carries no
    /// <see cref="ClaimTypes.Hash"/> claim.</exception>
    public static string GetConnectionFromToken(string jwToken)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.ReadToken(jwToken) as JwtSecurityToken
            ?? throw new InvalidOperationException("The supplied value is not a readable JWT.");
        var encryptedConnection = token.Claims.First(claim => claim.Type == ClaimTypes.Hash).Value;
        return AppEncrypt.DecryptText(encryptedConnection);
    }
}
