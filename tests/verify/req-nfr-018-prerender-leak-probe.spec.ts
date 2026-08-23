/**
 * REQ-NFR-018 — does settings-save propagation degrade as abandoned PRERENDERS accumulate?
 *
 * Hypothesis under test: `Header`, `Footer`, `AdminLayout` and `SiteBrandTitle` subscribe to the
 * singleton `ISiteSettingsService.SettingsChanged` in `OnInitialized` and unsubscribe in `Dispose`.
 * An anonymous HTTP GET only PRERENDERS the page — it never opens a SignalR circuit — so if those
 * prerendered component instances are not disposed, every such request leaks another subscriber and
 * every later save has to fan out across all of them.
 *
 * This spec is a MEASUREMENT, not an assertion about the fix: it saves a theme and times how long
 * an anonymous connection takes to observe it, then adds a batch of anonymous GETs and re-measures,
 * twice. A propagation time that climbs with the number of abandoned prerenders confirms the leak;
 * a flat one refutes it and sends the investigation elsewhere.
 */
import { test, expect, request } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav } from './_gates';

const ARTIFACTS = 'tests/.artifacts/req-nfr-018-leak';
const SEED_THEME = 'trblaze-modern';

/** Reads `data-site-theme` over a fresh, unauthenticated connection (one prerender). */
async function anonymousTheme(): Promise<string> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const html = await (await api.get('/')).text();
    return /data-site-theme="([^"]*)"/.exec(html)?.[1] ?? '';
  } finally {
    await api.dispose();
  }
}

/** Fires `count` anonymous GETs that prerender the page and never connect a circuit. */
async function abandonPrerenders(count: number): Promise<void> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    for (let i = 0; i < count; i++) {
      await api.get('/');
    }
  } finally {
    await api.dispose();
  }
}

test('settings-save propagation vs. the number of abandoned prerenders', async ({ page }) => {
  test.setTimeout(20 * 60 * 1000);

  await login(page, 'admin');

  /** Saves whichever theme is not live and returns how long visitors took to see it. */
  async function measureSaveMs(): Promise<number> {
    const before = await anonymousTheme();
    const target = before === SEED_THEME ? 'developer' : SEED_THEME;

    await nav(page, '/settings');
    await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });
    await page.click('[data-testid="tab-theme"]');
    await expect(page.locator('[data-testid="theme-swatches"]')).toBeVisible({ timeout: 30000 });
    await page.click(`[data-testid="theme-swatch-${target}"]`);
    await page.waitForTimeout(800);

    const savedAt = Date.now();
    await page.click('[data-testid="save-settings"]');
    for (;;) {
      if ((await anonymousTheme()) === target) break;
      if (Date.now() - savedAt > 5 * 60 * 1000) break;
      await page.waitForTimeout(500);
    }
    return Date.now() - savedAt;
  }

  const readings: Record<string, number> = {};

  readings.baseline = await measureSaveMs();
  await abandonPrerenders(40);
  readings.after40Prerenders = await measureSaveMs();
  await abandonPrerenders(80);
  readings.after120Prerenders = await measureSaveMs();

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(`${ARTIFACTS}/propagation-vs-prerenders.json`, JSON.stringify(readings, null, 2));
  console.log(`[REQ-NFR-018] propagation ms: ${JSON.stringify(readings)}`);

  // Leave the seeded theme behind whatever the numbers said.
  if ((await anonymousTheme()) !== SEED_THEME) {
    await nav(page, '/settings');
    await page.click('[data-testid="tab-theme"]');
    await expect(page.locator('[data-testid="theme-swatches"]')).toBeVisible({ timeout: 30000 });
    await page.click(`[data-testid="theme-swatch-${SEED_THEME}"]`);
    await page.waitForTimeout(800);
    await page.click('[data-testid="save-settings"]');
    await page.waitForTimeout(5000);
  }

  expect(readings.baseline, 'a save must reach visitors at all').toBeGreaterThan(0);
});
