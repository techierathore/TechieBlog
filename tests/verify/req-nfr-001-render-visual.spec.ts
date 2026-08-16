import { test, expect, Page } from '@playwright/test';

/**
 * REQ-NFR-001 — §4a data-render sweep and §4b visual-truth gate over the public
 * pages the performance budget is measured against. The perf gate (§4c) proves the
 * pages are fast; these two prove they actually render their data and look right,
 * which `Verified` also requires.
 */

const BASE = process.env.VERIFY_BASE ?? 'http://172.18.144.1:5561';

const SCREENS = [
  { name: 'home', path: '/' },
  { name: 'post', path: '/post/blazor-circuits-and-state' },
  { name: 'category', path: '/category/programming' },
  { name: 'newsletters', path: '/newsletters' },
];

const WIDTHS = [
  { label: '1280', width: 1280, height: 800 },
  { label: '390', width: 390, height: 844 },
];

/** Collects console errors so a Blazor render error becomes RENDER-ERROR, not a silent pass. */
function watchConsole(page: Page, sink: string[]) {
  page.on('console', m => { if (m.type() === 'error') sink.push(m.text()); });
  page.on('pageerror', e => sink.push(String(e)));
}

for (const screen of SCREENS) {
  test(`REQ-NFR-001 render gate — ${screen.name} renders its data`, async ({ page }) => {
    const errors: string[] = [];
    watchConsole(page, errors);

    const response = await page.goto(BASE + screen.path, { waitUntil: 'domcontentloaded' });
    expect(response?.status(), `${screen.path} must return 200`).toBe(200);

    // Blazor Server: wait for the rendered body rather than a fixed timeout.
    await page.waitForSelector('main, .content, body', { timeout: 30000 });
    await page.waitForTimeout(1500);

    // RENDER-EMPTY guard: the page must carry real text, not a shell.
    const text = (await page.locator('body').innerText()).trim();
    expect(text.length, `${screen.path} body must not be blank`).toBeGreaterThan(200);

    // Every screen here is a content page: it must link to at least one real destination.
    const links = await page.locator('a[href]').count();
    expect(links, `${screen.path} must render navigable links`).toBeGreaterThan(3);

    // Screen-specific data assertions (the DevGuide control map).
    if (screen.name === 'home' || screen.name === 'category') {
      // Post list/grid must have rows AND non-empty cells (the count-vs-rows trap).
      const headings = await page.locator('h1, h2, h3').allInnerTexts();
      const nonEmpty = headings.filter(h => h.trim().length > 0);
      expect(nonEmpty.length, `${screen.path} must render post headings with text`).toBeGreaterThan(0);
      const postLinks = await page.locator('a[href*="/post/"]').count();
      expect(postLinks, `${screen.path} must list at least one post`).toBeGreaterThan(0);
    }

    if (screen.name === 'post') {
      // Article body must be rendered markdown, not an empty container.
      const h1 = (await page.locator('h1').first().innerText()).trim();
      expect(h1.length, 'post must render its title').toBeGreaterThan(0);
      const paras = await page.locator('p').allInnerTexts();
      const body = paras.filter(p => p.trim().length > 20);
      expect(body.length, 'post must render article body paragraphs').toBeGreaterThan(0);
    }

    // RENDER-ERROR guard.
    const blazorError = await page.locator('#blazor-error-ui').isVisible().catch(() => false);
    expect(blazorError, `${screen.path} must not show the Blazor error UI`).toBeFalsy();
    const fatal = errors.filter(e => !/favicon|manifest|404 \(Not Found\)/i.test(e));
    expect(fatal, `${screen.path} console errors: ${fatal.join(' | ')}`).toHaveLength(0);
  });

  for (const vp of WIDTHS) {
    test(`REQ-NFR-001 visual gate — ${screen.name} @ ${vp.label}`, async ({ page }) => {
      await page.setViewportSize({ width: vp.width, height: vp.height });
      await page.goto(BASE + screen.path, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('main, .content, body', { timeout: 30000 });
      await page.waitForTimeout(1500);

      // No horizontal overflow — the classic broken-layout signature.
      const overflow = await page.evaluate(() =>
        document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow, `${screen.path} @${vp.label} must not scroll horizontally`).toBeLessThanOrEqual(2);

      // Sized & in-viewport: key controls must have a real box inside the page bounds.
      const bad = await page.evaluate((w) => {
        const out: string[] = [];
        const sel = 'h1, h2, main a[href], nav a[href], button, header, footer';
        document.querySelectorAll(sel).forEach(el => {
          const s = getComputedStyle(el);
          if (s.display === 'none' || s.visibility === 'hidden' || s.opacity === '0') return;
          const r = el.getBoundingClientRect();
          if (r.width === 0 || r.height === 0) return; // collapsed-but-hidden is not a layout fault here
          if (r.left < -4 || r.right > w + 4) {
            out.push(`${el.tagName}.${(el.className || '').toString().slice(0, 30)} x=[${Math.round(r.left)},${Math.round(r.right)}]`);
          }
        });
        return out.slice(0, 12);
      }, vp.width);
      expect(bad, `${screen.path} @${vp.label} controls off-canvas: ${bad.join(' ; ')}`).toHaveLength(0);

      await page.screenshot({
        path: `tests/.artifacts/req-nfr-001/${screen.name}-${vp.label}.png`,
        fullPage: true,
      });
    });
  }
}
