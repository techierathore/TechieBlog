// =============================================================================
// clipboard.js — REQ-UI-070 (BRD-110)
//
// Puts plain text on the clipboard for the Analytics page's "Copy numbers"
// button. Shared by both heads: the website (any modern browser) and BlogApp
// (WebView2), which serve this module from /_content/BlogUI/js/clipboard.js.
//
// The asynchronous Clipboard API needs a secure context and, in some hosts, a
// user gesture; when it refuses, the older hidden-textarea + execCommand('copy')
// path is tried. The function never throws: it answers true or false, and the
// page shows the text in a read-only box to copy by hand when it is false, so a
// refused clipboard is never a silent failure.
// =============================================================================

/**
 * Copies text to the clipboard.
 * @param {string} text The plain text to copy.
 * @returns {Promise<boolean>} true when the text reached the clipboard.
 */
export async function writeText(text) {
    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        }
    } catch {
        // fall through to the legacy path
    }

    try {
        const area = document.createElement("textarea");
        area.value = text;
        area.setAttribute("readonly", "");
        area.style.position = "fixed";
        area.style.top = "-1000px";
        document.body.appendChild(area);
        area.select();
        const copied = document.execCommand("copy");
        document.body.removeChild(area);
        return copied === true;
    } catch {
        return false;
    }
}
