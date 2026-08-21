// =============================================================================
// session-cookie.js — REQ-FN-058
//
// Mirrors the access token that CustomAuthStateProvider keeps in browser local
// storage into an ordinary request cookie.
//
// WHY THIS EXISTS. The session is a JWT held in local storage, which is reachable
// only through JS interop. A FULL DOCUMENT LOAD therefore arrives at the server
// carrying no evidence of the session at all: the endpoint's [Authorize] metadata
// sees an anonymous principal and challenges, and the static prerender pass builds
// an anonymous ClaimsIdentity. Deep links, bookmarks and F5 on an admin route all
// bounced as a result. A cookie is the only credential a browser sends on a plain
// document GET, so the token has to be in one for the server to see it.
//
// SECURITY NOTE. The cookie cannot be HttpOnly — it is written from script, and the
// same value already lives in local storage, so this adds no exposure that script
// injection did not already have. It is NOT used as a bearer credential on its own:
// the server looks the token up in the UserLogin table exactly as the circuit does,
// so a revoked or expired session is refused whichever way it arrives. SameSite=Lax
// keeps it off cross-site requests; Secure is set only on an https origin so that a
// local http development host still works.
// =============================================================================

/**
 * Writes (or refreshes) the session cookie.
 * @param {string} name Cookie name — the local-storage access-token key, so it rotates with the signing key.
 * @param {string} value The access token.
 * @param {number} maxAgeSeconds Cookie lifetime in seconds.
 */
export function write(name, value, maxAgeSeconds) {
    if (!name || !value) {
        return;
    }

    const secure = window.location.protocol === 'https:' ? '; Secure' : '';
    document.cookie = `${encodeURIComponent(name)}=${encodeURIComponent(value)}`
        + `; Path=/; Max-Age=${maxAgeSeconds}; SameSite=Lax${secure}`;
}

/**
 * Removes the session cookie by expiring it in the past.
 * @param {string} name Cookie name to clear.
 */
export function clear(name) {
    if (!name) {
        return;
    }

    document.cookie = `${encodeURIComponent(name)}=; Path=/; Max-Age=0; SameSite=Lax`;
}
