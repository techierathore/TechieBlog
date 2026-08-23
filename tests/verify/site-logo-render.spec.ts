/**
 * Is the configured site logo actually VISIBLE on the public site?
 *
 * A 200 on the image URL is not enough — it proves the bytes are served, not that the browser
 * decoded them or that the element is on screen. `naturalWidth > 0` is the decode proof; a
 * non-zero bounding box is the visibility proof. Both are checked at desktop and mobile widths,
 * because the header collapses at 390 and the logo lives inside it.
 */
import { test, expect } from '@playwright/test';
import { BASE } from './_gates';

test('the site logo renders on the public site at both widths', async ({ page }) => {
  test.setTimeout(3 * 60 * 1000);

  const findings: Record<string, unknown> = {};

  for (const [label, width, height] of [['desktop', 1280, 800], ['mobile', 390, 844]] as const) {
    await page.setViewportSize({ width, height });
    await page.goto(BASE, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(2500);

    const logo = page.locator('[data-testid="site-logo"]').first();
    const count = await logo.count();

    const state = count === 0 ? null : await logo.evaluate((el) => {
      const img = el as HTMLImageElement;
      const box = img.getBoundingClientRect();
      return {
        src: img.getAttribute('src'),
        alt: img.getAttribute('alt'),
        naturalWidth: img.naturalWidth,
        naturalHeight: img.naturalHeight,
        boxWidth: Math.round(box.width),
        boxHeight: Math.round(box.height),
        visible: getComputedStyle(img).visibility !== 'hidden' && getComputedStyle(img).display !== 'none',
      };
    });

    findings[label] = state ?? '(no [data-testid=site-logo] element)';
    console.log(`  ${label}: ${JSON.stringify(state)}`);

    expect(count, `${label}: the logo element should be present`).toBeGreaterThan(0);
    expect(state, `${label}: logo state should be readable`).not.toBeNull();
    expect((state as { naturalWidth: number }).naturalWidth,
      `${label}: the image must actually DECODE (naturalWidth > 0 — a broken image reads 0)`).toBeGreaterThan(0);
    expect((state as { boxWidth: number }).boxWidth,
      `${label}: the logo must occupy real width on screen`).toBeGreaterThan(0);
    expect((state as { boxHeight: number }).boxHeight,
      `${label}: the logo must occupy real height on screen`).toBeGreaterThan(0);
    expect((state as { visible: boolean }).visible, `${label}: the logo must not be hidden`).toBe(true);

    await page.screenshot({ path: `tests/.artifacts/site-logo/home-${label}.png`, fullPage: false });
  }

  console.log(`  summary: ${JSON.stringify(findings)}`);
});
