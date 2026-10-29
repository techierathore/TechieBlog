import { test, expect } from '@playwright/test';

/**
 * REQ-UI-065 + REQ-FN-052 smoke against a host booted on a database built ONLY by the migrations
 * (no SiteLogo setting, no svctoken relation). Base URL from TB_BASE.
 */
const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5404';
const OUT = 'tests/.artifacts/build-phase-20261027/d';

function lum(hex: string) {
  const c = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255).map(v => v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4));
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
}
const rgbToHex = (s: string) => '#' + (s.match(/\d+/g) ?? []).slice(0, 3).map(n => Number(n).toString(16).padStart(2, '0')).join('');

for (const theme of ['light', 'dark']) {
  test(`header shows the lockup (${theme})`, async ({ page }) => {
    await page.addInitScript((t) => { try { localStorage.setItem('techieblog-dark-mode', t === 'dark' ? 'true' : 'false'); } catch {} }, theme);
    await page.emulateMedia({ colorScheme: theme as 'light' | 'dark' });
    for (const w of [1280, 390]) {
      await page.setViewportSize({ width: w, height: 800 });
      await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
      await page.waitForTimeout(3000);
      const img = page.locator('[data-testid="brand-link"] img[data-testid="brand-lockup"]');
      await expect(img).toBeVisible();
      await expect.poll(() => img.evaluate((el: HTMLImageElement) => el.naturalWidth), { timeout: 20000 }).toBeGreaterThan(0);
      const info = await img.evaluate((el: HTMLImageElement) => ({ nat: el.naturalWidth, w: el.getBoundingClientRect().width, h: el.getBoundingClientRect().height, src: el.currentSrc, alt: el.alt }));
      console.log(`LOCKUP ${theme}@${w}`, JSON.stringify(info));
      expect(info.nat).toBeGreaterThan(0);
      expect(info.h).toBeGreaterThan(20);
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow).toBeLessThanOrEqual(1);
      expect(await page.locator('[data-testid="site-logo"]').count()).toBe(0);
      const bg = await page.evaluate(() => { const own = getComputedStyle(document.querySelector('[data-testid="brand-lockup"]')!).backgroundColor; const body = getComputedStyle(document.body).backgroundColor; const c = document.createElement('canvas'); c.width = c.height = 1; const x = c.getContext('2d')!; const px = (v: string) => { x.clearRect(0,0,1,1); x.fillStyle = '#000'; x.fillStyle = v; x.fillRect(0,0,1,1); const d = x.getImageData(0,0,1,1).data; return `rgb(${d[0]}, ${d[1]}, ${d[2]})`; }; const ownRgba = own; return (own === 'rgba(0, 0, 0, 0)' ? px(body) : px(own)) + '|' + (own === 'rgba(0, 0, 0, 0)' ? px(body) : px(own)) + '|' + document.documentElement.className; });
      console.log(`HEADER BG ${theme}@${w}`, bg);
      if (w === 1280) {
        const hdrBg = rgbToHex(bg.split('|')[1]);
        const a = lum('#0F4DBC'), b = lum(hdrBg);
        console.log(`CONTRAST wordmark #0F4DBC vs page bg ${hdrBg}: ${((Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)).toFixed(2)}`);
      }
      await page.screenshot({ path: `${OUT}/ui065-${theme}-${w}.png`, clip: { x: 0, y: 0, width: w, height: 120 } });
    }
    const res = await page.request.get(`${BASE}/_content/BlogUI/images/brand/TechieBlog-horizontal.svg`);
    expect(res.status()).toBe(200);
  });
}

test('REQ-FN-052 public pages render on a migrations-only database', async ({ page }) => {
  for (const r of ['/', '/about', '/categories', '/tags', '/series', '/search?q=x', '/rss']) {
    const resp = await page.goto(`${BASE}${r}`, { waitUntil: 'networkidle' });
    expect(resp!.status(), r).toBeLessThan(400);
    await page.waitForTimeout(1500);
    const t = await page.evaluate(() => document.body.innerText);
    expect(t, r).not.toContain('An unhandled error');
    expect(t.toLowerCase(), r).not.toContain('svctoken');
    expect(t.trim().length, r).toBeGreaterThan(100);
  }
});
