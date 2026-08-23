/**
 * REQ-FN-061 / REQ-NFR-018 — measures the propagation delay in the ONE configuration that
 * reproduces it: two tests in the same file, in the same worker, the first abandoning a dirty
 * settings form and the second saving a theme.
 *
 * `req-fn-061-save-after-abandoned-edit.spec.ts` does the abandoned edit inside a single test and
 * does NOT reproduce (measured 1231ms). The in-file pair does, deterministically. This spec mirrors
 * that structure exactly — two separate `test()` blocks, so Playwright gives each its own context
 * while the SERVER keeps whatever state the first one left behind — and then polls instead of
 * waiting a fixed 4s, so the output is a number rather than a pass/fail on an arbitrary boundary.
 */
import { test, expect, request } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav } from './_gates';

const ARTIFACTS = 'tests/.artifacts/req-fn-061-timing';
const SEED_THEME = 'trblaze-modern';

/** Reads `data-site-theme` over a fresh, unauthenticated connection. */
async function anonymousTheme(): Promise<string> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const html = await (await api.get('/')).text();
    return /data-site-theme="([^"]*)"/.exec(html)?.[1] ?? '';
  } finally {
    await api.dispose();
  }
}

test('step 1 — an admin abandons a dirty settings form without saving', async ({ page }) => {
  // Mirrors the original spec exactly: ONE long-lived APIRequestContext making an anonymous GET
  // before login and another during the edit. Kept deliberately, as the single variable that
  // differed from this spec's first version, which propagated in <2s.
  const api = await request.newContext({ baseURL: BASE });
  await (await api.get('/')).text();

  await login(page, 'admin');
  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });
  await page.fill('#site-title', 'ABANDONED TITLE — never saved');
  await page.waitForTimeout(1500);
  await (await api.get('/')).text();
  await api.dispose();
  // Test ends here with the form dirty; Playwright disposes the browser context.
});

test('step 2 — the next Save must reach visitors promptly', async ({ page }) => {
  test.setTimeout(5 * 60 * 1000);

  const before = await anonymousTheme();
  const target = before === SEED_THEME ? 'developer' : SEED_THEME;

  await login(page, 'admin');
  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });
  await page.click('[data-testid="tab-theme"]');
  await expect(page.locator('[data-testid="theme-swatches"]')).toBeVisible({ timeout: 30000 });
  await page.click(`[data-testid="theme-swatch-${target}"]`);
  await page.waitForTimeout(800);

  const savedAt = Date.now();
  await page.click('[data-testid="save-settings"]');

  let served = '';
  for (;;) {
    served = await anonymousTheme();
    if (served === target || Date.now() - savedAt > 120000) {
      break;
    }
    await page.waitForTimeout(500);
  }
  const elapsedMs = Date.now() - savedAt;

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(
    `${ARTIFACTS}/pairwise.json`,
    JSON.stringify({ before, target, served, elapsedMs }, null, 2));
  console.log(`[REQ-FN-061] pairwise save propagated in ${elapsedMs}ms`);

  // Restore the seeded theme.
  if (target !== SEED_THEME) {
    await page.click(`[data-testid="theme-swatch-${SEED_THEME}"]`);
    await page.waitForTimeout(800);
    await page.click('[data-testid="save-settings"]');
    await page.waitForTimeout(5000);
  }

  expect(served, 'the save must reach visitors at all').toBe(target);
  expect(elapsedMs, 'a saved setting must reach visitors within a few seconds').toBeLessThan(10000);
});
