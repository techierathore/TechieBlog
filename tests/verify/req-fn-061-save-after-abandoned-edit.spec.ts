/**
 * REQ-FN-061 / REQ-NFR-018 regression — a Save that follows an ABANDONED edit does not propagate.
 *
 * Found by the 2026-08-23 verify run. REQ-FN-061's own suite fails its save-path counterweight
 * ("saving a theme change on /settings still takes effect site-wide") when the whole file runs, but
 * PASSES when that test is run alone. Bisecting the file identified the single predecessor that
 * triggers it: the test that types into `#site-title` and then navigates away WITHOUT saving.
 *
 * Reproduced deterministically (3/3 whole-file runs, plus this pairwise reduction); the same save
 * run in isolation propagates in ~2.6s, measured by `req-fn-061-save-propagation-timing.spec.ts`.
 *
 * SYMPTOM — after an admin abandons an unsaved edit on /settings, the NEXT genuine Save:
 *   - DOES write to the database (psql shows the new value, and the host logs "Persisted 30 site
 *     settings"), but
 *   - does NOT reach visitors: an anonymous connection keeps being served the OLD value, so the
 *     cached aggregate was not evicted/repopulated by that save.
 *
 * The value does eventually appear, consistent with the cache's 10-minute lifetime lapsing rather
 * than with invalidation — i.e. `SaveSettingsAsync`'s "takes effect immediately across every
 * circuit without a restart" contract does not hold in this sequence. Suspected mechanism: the
 * abandoned circuit lingers (the host log shows JSDisconnectedException for disposed circuits) and
 * a settings read that began before the eviction completes after it, writing the STALE aggregate
 * back under a fresh lifetime — the classic evict-then-repopulate race.
 *
 * This spec is written to FAIL while the defect is present. It restores the seeded theme.
 */
import { test, expect, request } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav } from './_gates';

const ARTIFACTS = 'tests/.artifacts/req-fn-061-timing';
const SEED_THEME = 'trblaze-modern';
const OTHER_THEME = 'developer';

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

/** Saves a theme through the real admin UI. */
async function saveTheme(page: import('@playwright/test').Page, theme: string) {
  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });
  await page.click('[data-testid="tab-theme"]');
  await expect(page.locator('[data-testid="theme-swatches"]')).toBeVisible({ timeout: 30000 });
  await page.click(`[data-testid="theme-swatch-${theme}"]`);
  await page.waitForTimeout(800);
  await page.click('[data-testid="save-settings"]');
}

test('a Save that follows an abandoned unsaved edit still reaches visitors', async ({ browser }) => {
  test.setTimeout(5 * 60 * 1000);

  const settled = await anonymousTheme();
  const target = settled === SEED_THEME ? OTHER_THEME : SEED_THEME;

  // 1. An admin opens /settings, types a value, and ABANDONS the form without saving.
  //    (REQ-FN-061 already proves this does not leak; here it is only the trigger condition.)
  const abandoning = await browser.newContext();
  const abandonPage = await abandoning.newPage();
  await login(abandonPage, 'admin');
  await nav(abandonPage, '/settings');
  await expect(abandonPage.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });
  await abandonPage.fill('#site-title', 'ABANDONED TITLE — never saved');
  await abandonPage.waitForTimeout(1500);
  await abandoning.close();   // navigate away / close the tab: the circuit is abandoned dirty

  // 2. A genuine Save now happens in a NEW session.
  const saving = await browser.newContext();
  const savePage = await saving.newPage();
  await login(savePage, 'admin');
  await saveTheme(savePage, target);

  // 3. MEASURE how long the save takes to reach visitors, rather than asserting against a fixed
  //    wait. An isolated save propagates in ~2.6s; the whole-file run fails on a 4s wait; this
  //    records the actual figure for the post-abandoned-edit case.
  const startedAt = Date.now();
  let servedAfterSave = '';
  for (;;) {
    servedAfterSave = await anonymousTheme();
    if (servedAfterSave === target || Date.now() - startedAt > 60000) {
      break;
    }
    await savePage.waitForTimeout(1000);
  }
  const elapsedMs = Date.now() - startedAt;
  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(
    `${ARTIFACTS}/after-abandoned-edit.json`,
    JSON.stringify({ settled, target, servedAfterSave, elapsedMs }, null, 2));
  console.log(`[REQ-FN-061] save-after-abandoned-edit propagated in ${elapsedMs}ms`);

  // Restore the seeded theme regardless of the outcome.
  await saveTheme(savePage, SEED_THEME);
  await savePage.waitForTimeout(5000);
  await saving.close();

  expect(
    servedAfterSave,
    'a saved setting must reach visitors even when a previous edit was abandoned unsaved',
  ).toBe(target);
});
