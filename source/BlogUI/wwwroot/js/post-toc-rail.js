// post-toc-rail.js
// Delegated click handling for the post detail page's table-of-contents rail
// (REQ-UI-045 / UAT-027).
//
// WHY THIS EXISTS: TrBlazeUI's <AnchorNav> renders each entry as a plain
// `<a href="#id">`. This app declares `<base href="/">` (standard for a Blazor
// Web App with client-side routing), and per the HTML spec a bare fragment
// href is resolved AGAINST THE BASE URI, not the current document's path. On
// any post page (e.g. "/post/{slug}") that resolves the link's target to
// "/#id" instead of "/post/{slug}#id" — the path is silently lost. Blazor's
// own client-side click interception performs the SAME base-relative
// resolution to decide whether a click is a same-page fragment jump; since the
// resolved path ("/") differs from the current one ("/post/{slug}"), Blazor
// treats the click as a genuine internal navigation and routes the app to the
// HOME page, discarding the post entirely. Measured live: clicking a TOC entry
// silently navigated away from the article with no error, no reload, and no
// visible change to the address bar's origin — just gone.
//
// FIX: intercept the click on an ANCESTOR of the links — this rail's own
// container — in the bubble phase. Bubble-phase dispatch always visits an
// ancestor element BEFORE the event reaches `document`, regardless of when
// each listener was attached, so a listener here reliably runs ahead of
// Blazor's document-level listener and can call preventDefault() before
// Blazor's own handler (which itself bails out immediately when
// `event.defaultPrevented` is already true) ever sees the click. From there we
// perform the scroll ourselves — compensating for the sticky site header — and
// record the fragment with `history.replaceState` using the CURRENT pathname
// explicitly, sidestepping the same base-href resolution trap for the address
// bar too.
//
// Library gap filed as TR-074 in docs/TechieBlog-TrBlazeUI-Feedback.md.

/**
 * Height (in px) of content the sticky site header would otherwise hide a
 * scrolled-to heading behind. Matches the TopOffset given to <AnchorNav>.
 */
const HEADER_OFFSET = 96;

/**
 * Wires up click handling on the TOC rail container. Idempotent — safe to
 * call again on the same element (e.g. after a Blazor re-render) without
 * double-binding.
 * @param {HTMLElement} railElement - the rail's own root element.
 */
export function init(railElement) {
    if (!railElement || railElement.dataset.tocClickBound === "true") {
        return;
    }
    railElement.dataset.tocClickBound = "true";

    railElement.addEventListener("click", (event) => {
        const link = event.target.closest('a[href^="#"]');
        if (!link || !railElement.contains(link)) {
            return;
        }

        const id = link.getAttribute("href").slice(1);
        const target = id ? document.getElementById(id) : null;
        if (!target) {
            return;
        }

        event.preventDefault();

        const targetTop = target.getBoundingClientRect().top + window.scrollY - HEADER_OFFSET;
        window.scrollTo({ top: Math.max(targetTop, 0), behavior: "smooth" });

        const newUrl = `${window.location.pathname}${window.location.search}#${id}`;
        window.history.replaceState(null, "", newUrl);
    });
}
