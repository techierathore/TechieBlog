/**
 * REQ-FN-061 / REQ-NFR-018 — how long does a SAVED setting take to reach an anonymous visitor?
 *
 * The 2026-08-23 verify run saw `req-fn-061-settings-cache-leak.spec.ts` fail its save-path
 * counterweight: after clicking Save and waiting a fixed 4s, an anonymous request was still served
 * the OLD theme, while psql already held the new one. Minutes later the new value was being served.
 *
 * That leaves exactly two candidate explanations, and they carry opposite verdicts:
 *   (a) the fixed 4s wait is simply too short for the save round trip  -> a TEST timing bug;
 *   (b) `SaveSettingsAsync`'s invalidation does not actually evict what the layout reads, so the
 *       change only surfaces when the 10-minute cache lifetime lapses -> a REAL REQ-NFR-018 defect
 *       ("takes effect immediately across every circuit without a restart" would be false).
 *
 * This spec distinguishes them by MEASURING the propagation delay instead of asserting a guess:
 * it saves through the real admin UI, then polls an anonymous connection until the value flips,
 * recording the elapsed milliseconds. It restores the seeded theme on the way out.
 */
import { test, expect, request } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav } from './_gates';

const ARTIFACTS = 'tests/.artifacts/req-fn-061-timing';
const SEED_THEME = 'trblaze-modern';

/** Reads `data-site-theme` over a FRESH connection, so nothing of the admin's session is reused. */
async function anonymousTheme(): Promise<string> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const html = await (await api.get('/', { headers: { 'Cache-Control': 'no-cache' } })).text();
    return /data-site-theme="([^"]*)"/.exec(html)?.[1] ?? '';
  } finally {
    await api.dispose();
  }
}

/** Polls until the anonymous theme equals `want`, returning the elapsed ms (or -1 on timeout). */
async function msUntilThemeIs(want: string, budgetMs: number): Promise<number> {
  const startedAt = Date.now();
  for (;;) {
    if ((await anonymousTheme()) === want) {
      return Date.now() - startedAt;
    }
    if (Date.now() - startedAt > budgetMs) {
      return -1;
    }
    await new Promise((resolve) => setTimeout(resolve, 2000));
  }
}

test('a saved theme reaches anonymous visitors, and we measure how long it takes', async ({ page }) => {
  test.setTimeout(15 * 60 * 1000);

  const evidence: Record<string, unknown> = {};
  const before = await anonymousTheme();
  evidence.themeBeforeSave = before;

  // Save whichever theme is NOT currently live, so the run always produces an observable flip.
  const target = before === SEED_THEME ? 'developer' : SEED_THEME;
  evidence.themeSaved = target;

  await login(page, 'admin');
  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });
  await page.click('[data-testid="tab-theme"]');
  await expect(page.locator('[data-testid="theme-swatches"]')).toBeVisible({ timeout: 30000 });

  await page.click(`[data-testid="theme-swatch-${target}"]`);
  await page.waitForTimeout(800);
  await page.click('[data-testid="save-settings"]');

  // 11 minutes comfortably outruns the documented 10-minute cache lifetime, so a run that only
  // flips near the end is positive proof that TTL expiry — not invalidation — did the work.
  const elapsed = await msUntilThemeIs(target, 11 * 60 * 1000);
  evidence.msUntilVisitorsSawIt = elapsed;
  evidence.flippedWithinTtl = elapsed >= 0 && elapsed < 30000;

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(`${ARTIFACTS}/propagation.json`, JSON.stringify(evidence, null, 2));

  // Restore the seeded theme through the same UI so the database is left as it was found.
  if (target !== SEED_THEME) {
    await page.click(`[data-testid="theme-swatch-${SEED_THEME}"]`);
    await page.waitForTimeout(800);
    await page.click('[data-testid="save-settings"]');
    await msUntilThemeIs(SEED_THEME, 11 * 60 * 1000);
  }

  expect(elapsed, 'a saved setting must reach visitors at all').toBeGreaterThanOrEqual(0);
  expect(elapsed, 'a saved setting must take effect promptly, not on the 10-minute cache lifetime')
    .toBeLessThan(30000);
});
