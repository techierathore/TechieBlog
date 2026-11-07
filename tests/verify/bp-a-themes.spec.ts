import { test, expect, Page } from '@playwright/test';
import { login, nav } from './_gates';

/** REQ-UI-033 / REQ-UI-048: 3 site themes x light/dark. Themes are chosen via localStorage `techieblog-theme`
 *  (JSON string) and `techieblog-dark-mode`, which App.razor's pre-paint script applies to <html>. */
const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5401';
const OUT = 'tests/.artifacts/build-phase-20261027/a/themes';
const THEMES = ['trblaze-modern', 'developer', 'minimal'];
const PAGES: [string, string, string][] = [
  ['home', '/', '[data-testid="home-articles-grid"] [data-testid="post-card"]'],
  ['post', '/post/fxa-post-1', '[data-testid="post-content"]'],
  ['search', '/search?q=fxa', '[data-testid="search-result"]'],
  ['resume', '/resume', '[data-testid="skills-section"]'],
  ['about', '/about', '[data-testid="about-page"]'],
];

/** WCAG contrast of every visible text node vs its effective opaque background. */
async function contrast(page: Page) {
  return page.evaluate(() => {
    const cv = document.createElement('canvas'); cv.width = cv.height = 1; const g = cv.getContext('2d', { willReadFrequently: true })!;
    // Browser-resolved colour (handles oklch()/color-mix()/color(srgb)) rasterised to sRGB; alpha is carried separately.
    const parse = (c: string) => { if (!c || c === 'transparent') return null; g.clearRect(0, 0, 1, 1); g.fillStyle = '#000'; g.fillStyle = c; g.fillRect(0, 0, 1, 1); const d = g.getImageData(0, 0, 1, 1).data;
      if (d[3] === 0) return { r: 0, g: 0, b: 0, a: 0 };
      return { r: Math.round(d[0] * 255 / d[3]), g: Math.round(d[1] * 255 / d[3]), b: Math.round(d[2] * 255 / d[3]), a: d[3] / 255 }; };
    const lum = (c: any) => { const f = (v: number) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
    const bgOf = (el: Element | null) => { let layers: any[] = []; while (el) { const c = parse(getComputedStyle(el).backgroundColor); if (c && c.a > 0) { layers.push(c); if (c.a >= 1) break; } el = el.parentElement; }
      let base = { r: 255, g: 255, b: 255 }; for (const l of layers.reverse()) base = { r: l.r * l.a + base.r * (1 - l.a), g: l.g * l.a + base.g * (1 - l.a), b: l.b * l.a + base.b * (1 - l.a) }; return base; };
    const bad: string[] = []; let measured = 0;
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    const seen = new Set<Element>();
    while (walker.nextNode()) {
      const t = walker.currentNode as Text; if (!t.textContent?.trim()) continue;
      const el = t.parentElement!; if (seen.has(el)) continue; seen.add(el);
      const cs = getComputedStyle(el); const r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2 || cs.visibility === 'hidden' || cs.display === 'none' || parseFloat(cs.opacity) < 0.05) continue;
      if (el.closest('[aria-hidden="true"], script, style, [disabled]')) continue;
      const fg = parse(cs.color); if (!fg) continue; const bg = bgOf(el);
      const fgC = { r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a), b: fg.b * fg.a + bg.b * (1 - fg.a) };
      const L1 = lum(fgC), L2 = lum(bg); const ratio = (Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05);
      const size = parseFloat(cs.fontSize); const large = size >= 24 || (size >= 18.66 && parseInt(cs.fontWeight) >= 700);
      measured++;
      if (ratio < (large ? 3 : 4.5)) bad.push(`${ratio.toFixed(2)} "${t.textContent!.trim().slice(0, 30)}" ${el.tagName.toLowerCase()}${el.getAttribute('data-testid') ? '[' + el.getAttribute('data-testid') + ']' : ''}`);
    }
    return { measured, bad: bad.slice(0, 15), badCount: bad.length };
  });
}

for (const theme of THEMES) for (const dark of [false, true]) {
  test(`public ${theme} ${dark ? 'dark' : 'light'}`, async ({ browser }) => {
    test.setTimeout(240000);
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    await ctx.addInitScript(([t, d]) => { try { localStorage.setItem('techieblog-theme', JSON.stringify(t)); localStorage.setItem('techieblog-dark-mode', String(d)); } catch {} }, [theme, dark] as const);
    const page = await ctx.newPage();
    const report: any = {};
    for (const [name, path, ready] of PAGES) {
      await page.goto(BASE + path, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector(ready, { timeout: 60000 });
      await page.waitForTimeout(3000);
      await page.waitForSelector(ready, { timeout: 60000 });
      await page.waitForFunction(() => !document.querySelector('[data-testid$="-loading"]') && !/Loading (post|resume)/.test(document.body.innerText), null, { timeout: 60000 });
      await page.waitForTimeout(1000);
      const st = await page.evaluate(() => ({ dark: document.documentElement.classList.contains('dark'), theme: document.documentElement.getAttribute('data-site-theme') }));
      expect(st.dark, `${name} dark class`).toBe(dark); expect(st.theme).toBe(theme);
      report[name] = await contrast(page);
      await page.screenshot({ path: `${OUT}/${theme}-${dark ? 'dark' : 'light'}-${name}.png`, fullPage: false });
    }
    console.log('CONTRAST ' + theme + ' ' + (dark ? 'dark' : 'light') + ' ' + JSON.stringify(report));
    await ctx.close();
  });
}

test('admin dark/light contrast (/admin) trblaze-modern', async ({ browser }) => {
  test.setTimeout(300000);
  for (const dark of [true, false]) {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await ctx.newPage();
    await login(page, 'admin');
    await page.evaluate(([d]) => { localStorage.setItem('techieblog-theme', JSON.stringify('trblaze-modern')); localStorage.setItem('techieblog-dark-mode', String(d)); document.documentElement.classList.toggle('dark', d as boolean); }, [dark] as const);
    await nav(page, '/admin');
    await page.waitForTimeout(3000);
    console.log('ADMIN URL', page.url());
    const r = await contrast(page);
    console.log('ADMIN CONTRAST ' + (dark ? 'dark' : 'light') + ' ' + JSON.stringify(r));
    await page.screenshot({ path: `${OUT}/admin-${dark ? 'dark' : 'light'}.png` });
    expect(r.measured, 'probe measured text').toBeGreaterThan(20);
    await ctx.close();
  }
});
