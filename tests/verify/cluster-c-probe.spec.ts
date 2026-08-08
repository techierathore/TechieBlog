/** Probe: explain headers=2, zeroSized at 390px, and the 412px overflower with hScroll=0. */
import { test } from '@playwright/test';

const BASE = process.env.TB_BASE ?? 'https://localhost:7373';
test.use({ ignoreHTTPSErrors: true });
test.setTimeout(180000);

for (const [w, h] of [
  [1280, 900],
  [390, 844],
]) {
  test(`explain geometry @${w}`, async ({ browser }) => {
    const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: w, height: h } });
    const page = await ctx.newPage();
    await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    await page.evaluate(() => {
      localStorage.setItem('techieblog-theme', JSON.stringify('developer'));
      localStorage.setItem('techieblog-dark-mode', 'true');
    });
    await page.goto(`${BASE}/post/theming-with-css-custom-properties`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(9000); // well past the ~3s prerender swap

    const out = await page.evaluate(() => {
      const path = (el: Element) => {
        const parts: string[] = [];
        for (let n: Element | null = el; n && parts.length < 5; n = n.parentElement) {
          parts.push(n.tagName.toLowerCase() + (n.getAttribute('data-testid') ? `[${n.getAttribute('data-testid')}]` : ''));
        }
        return parts.join(' < ');
      };
      const doc = document.documentElement;
      return {
        hScroll: doc.scrollWidth - doc.clientWidth,
        headers: Array.from(document.querySelectorAll('header')).map((e) => {
          const r = e.getBoundingClientRect();
          return { path: path(e), rect: [Math.round(r.width), Math.round(r.height), Math.round(r.top)], cls: String(e.className).slice(0, 60) };
        }),
        zeroSized: Array.from(document.querySelectorAll('[data-testid]'))
          .filter((el) => {
            const r = el.getBoundingClientRect();
            const s = getComputedStyle(el);
            return s.display !== 'none' && s.visibility !== 'hidden' && (r.width === 0 || r.height === 0);
          })
          .map((el) => ({
            id: el.getAttribute('data-testid'),
            path: path(el),
            display: getComputedStyle(el).display,
            parentDisplay: el.parentElement ? getComputedStyle(el.parentElement).display : null,
          })),
        overflowers: Array.from(document.querySelectorAll('*'))
          .filter((el) => {
            const r = el.getBoundingClientRect();
            return r.width > 0 && r.right > doc.clientWidth + 1;
          })
          .slice(0, 6)
          .map((el) => {
            const r = el.getBoundingClientRect();
            // Which ancestor clips it, so the page itself does not scroll?
            let clipper = 'NONE (page would scroll)';
            for (let n: Element | null = el.parentElement; n; n = n.parentElement) {
              const ns = getComputedStyle(n);
              if (ns.overflow !== 'visible' || ns.clipPath !== 'none') {
                const nr = n.getBoundingClientRect();
                clipper = `${n.tagName.toLowerCase()}.${String(n.className).trim().split(/\s+/)[0]} overflow=${ns.overflow} clip=${ns.clipPath} w=${Math.round(nr.width)}`;
                break;
              }
            }
            return { path: path(el), right: Math.round(r.right), width: Math.round(r.width), clipper };
          }),
      };
    });
    console.log(`\n===== viewport ${w}x${h} =====`);
    console.log(JSON.stringify(out, null, 2));
    await ctx.close();
  });
}
