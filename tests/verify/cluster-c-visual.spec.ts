/**
 * cluster-c-visual.spec.ts — VISUAL-TRUTH corroboration for REQ-UI-033.
 *
 * Per the orchestrator's warning, headless Chromium in WSL can composite a STALE surface, and
 * Blazor's prerender swap leaves duplicate chrome in the DOM for ~3s. So every visual claim here
 * is read from live DOM geometry (getBoundingClientRect) at the same instant as the capture, and
 * screenshots are viewport-sized at two scroll offsets rather than one fullPage image.
 */
import { test, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'https://localhost:7373';
const OUT = 'test-results/cluster-c';
const THEMES = ['trblaze-modern', 'developer', 'minimal'] as const;

const ROUTES: [string, string][] = [
  ['home', '/'],
  ['post', '/post/theming-with-css-custom-properties'],
  ['search', '/search?q=blazor'],
  ['about', '/about'],
  ['newsletters', '/newsletters'],
];

test.use({ ignoreHTTPSErrors: true });
test.describe.configure({ mode: 'serial' });

/** Waits out the prerender swap, then confirms the DOM has settled to single chrome. */
async function settleSwap(page: Page) {
  for (let i = 0; i < 20; i++) {
    await page.waitForTimeout(800);
    const g = await page.evaluate(() => {
      const vis = (el: Element) => {
        const r = el.getBoundingClientRect();
        return r.width > 1 && r.height > 1;
      };
      return {
        headers: Array.from(document.querySelectorAll('header')).filter(vis).length,
        footers: Array.from(document.querySelectorAll('footer')).filter(vis).length,
        loading: /Loading\b/i.test(document.body.innerText || ''),
      };
    });
    if (g.headers <= 1 && g.footers <= 1 && !g.loading) return g;
  }
  return page.evaluate(() => ({
    headers: document.querySelectorAll('header').length,
    footers: document.querySelectorAll('footer').length,
    loading: true,
  }));
}

/** Live DOM geometry: the authority. Screenshots only illustrate it. */
const GEOM = () => {
  const doc = document.documentElement;
  const vis = (el: Element) => {
    const s = getComputedStyle(el);
    const r = el.getBoundingClientRect();
    return r.width > 1 && r.height > 1 && s.visibility !== 'hidden' && s.display !== 'none';
  };
  const name = (el: Element) =>
    el.tagName.toLowerCase() + (el.getAttribute('data-testid') ? `[${el.getAttribute('data-testid')}]` : '');

  const overflowers = Array.from(document.querySelectorAll('*'))
    .filter(vis)
    .filter((el) => el.getBoundingClientRect().right > doc.clientWidth + 1)
    .map((el) => ({ sel: name(el), right: Math.round(el.getBoundingClientRect().right) }));

  // Overlap check across the main landmark blocks (not every node — siblings legitimately nest).
  const blocks = Array.from(document.querySelectorAll('header, footer, main, aside, article')).filter(vis);
  const overlaps: string[] = [];
  for (let i = 0; i < blocks.length; i++) {
    for (let j = i + 1; j < blocks.length; j++) {
      const a = blocks[i];
      const b = blocks[j];
      if (a.contains(b) || b.contains(a)) continue;
      const ra = a.getBoundingClientRect();
      const rb = b.getBoundingClientRect();
      const ox = Math.min(ra.right, rb.right) - Math.max(ra.left, rb.left);
      const oy = Math.min(ra.bottom, rb.bottom) - Math.max(ra.top, rb.top);
      if (ox > 4 && oy > 4) overlaps.push(`${name(a)} x ${name(b)} (${Math.round(ox)}x${Math.round(oy)})`);
    }
  }

  return {
    scrollWidth: doc.scrollWidth,
    clientWidth: doc.clientWidth,
    hScroll: Math.max(0, doc.scrollWidth - doc.clientWidth),
    headers: Array.from(document.querySelectorAll('header')).filter(vis).length,
    footers: Array.from(document.querySelectorAll('footer')).filter(vis).length,
    zeroSized: Array.from(document.querySelectorAll('[data-testid]')).filter((el) => {
      const r = el.getBoundingClientRect();
      const s = getComputedStyle(el);
      return s.display !== 'none' && s.visibility !== 'hidden' && (r.width === 0 || r.height === 0);
    }).length,
    overflowers: overflowers.slice(0, 5),
    overlaps: overlaps.slice(0, 5),
    h1: (document.querySelector('h1')?.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 60),
    bodyChars: (document.body.innerText ?? '').trim().length,
  };
};

for (const theme of THEMES) {
  test(`visual-truth ${theme} dark`, async ({ browser }) => {
    test.setTimeout(12 * 60 * 1000);
    fs.mkdirSync(OUT, { recursive: true });
    const out: any[] = [];

    for (const width of [1280, 390]) {
      const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width, height: width === 1280 ? 900 : 844 } });
      const page = await ctx.newPage();
      await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
      await page.evaluate(
        (t) => {
          localStorage.setItem('techieblog-theme', JSON.stringify(t));
          localStorage.setItem('techieblog-dark-mode', 'true');
        },
        theme
      );

      for (const [name, url] of ROUTES) {
        await page.goto(`${BASE}${url}`, { waitUntil: 'networkidle' }).catch(() => {});
        const swap = await settleSwap(page);
        const geom = await page.evaluate(GEOM);
        // Two viewport-sized captures instead of one fullPage, per the stale-surface warning.
        await page.screenshot({ path: path.join(OUT, `vt-${theme}-${name}-${width}-top.png`) });
        await page.evaluate(() => window.scrollTo(0, Math.round(window.innerHeight * 0.9)));
        await page.waitForTimeout(700);
        await page.screenshot({ path: path.join(OUT, `vt-${theme}-${name}-${width}-scrolled.png`) });
        await page.evaluate(() => window.scrollTo(0, 0));
        out.push({ theme, width, name, swap, geom });
        fs.mkdirSync(OUT, { recursive: true });
        fs.writeFileSync(path.join(OUT, `visual-${theme}.json`), JSON.stringify(out, null, 2));
      }
      await ctx.close();
    }

    for (const o of out) {
      console.log(
        `[${o.theme}/${o.width}] ${o.name}: hScroll=${o.geom.hScroll} headers=${o.geom.headers} footers=${o.geom.footers} zeroSized=${o.geom.zeroSized} overlaps=${o.geom.overlaps.length} chars=${o.geom.bodyChars} h1="${o.geom.h1}"`
      );
      o.geom.overflowers.forEach((x: any) => console.log(`     OVERFLOW ${x.sel} right=${x.right}`));
      o.geom.overlaps.forEach((x: string) => console.log(`     OVERLAP ${x}`));
    }
  });
}
