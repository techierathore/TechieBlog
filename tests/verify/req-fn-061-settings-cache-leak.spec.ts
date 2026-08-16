import { test, expect, request } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav, visualCheck } from './_gates';

/**
 * REQ-FN-061 smoke — an unsaved edit on /settings must not reconfigure the live site.
 *
 * The defect: `Settings.razor` bound its form to the aggregate returned by
 * `ISiteSettingsService.GetSettingsAsync()`, which is the singleton's PROCESS-WIDE CACHED instance.
 * Every control on the page therefore wrote straight through to site configuration on change, with
 * no Save — so an admin who previewed a theme and navigated away had re-themed the live site for
 * every visitor, including anonymous ones, until the host restarted. The database stayed correct
 * throughout, which is why nothing surfaced it.
 *
 * The observable is `data-site-theme` on <html>, which App.razor renders server-side from the
 * cached settings (BRD-67). It is read here over a SEPARATE, UNAUTHENTICATED HTTP connection with
 * its own cookie jar — the "second, independent connection" the requirement's acceptance names —
 * so a value that only looked right inside the admin's own circuit cannot pass.
 *
 * Gates: RENDER-TRUTH (the settings form renders its persisted values) and VISUAL-TRUTH (1280+390).
 */

const ARTIFACTS = 'tests/.artifacts/req-fn-061';
const SEED_THEME = 'trblaze-modern';
const PREVIEW_THEME = 'minimal';

/** Reads `data-site-theme` from a page fetched anonymously, on its own connection. */
async function anonymousTheme(): Promise<string> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const response = await api.get('/');
    expect(response.status(), 'anonymous / should be reachable').toBe(200);
    const html = await response.text();
    const match = /data-site-theme="([^"]*)"/.exec(html);
    expect(match, 'the site theme attribute should be present on <html>').not.toBeNull();
    return match![1];
  } finally {
    await api.dispose();
  }
}

test('unsaved theme preview on /settings does not leak to anonymous visitors', async ({ page }) => {
  const evidence: Record<string, unknown> = {};

  // 1. BASELINE — what an anonymous visitor is served before the admin touches anything.
  const themeBefore = await anonymousTheme();
  evidence.themeBefore = themeBefore;
  expect(themeBefore, 'baseline theme should be the seeded one').toBe(SEED_THEME);

  // 2. The admin opens /settings and previews a different theme WITHOUT saving.
  const landing = await login(page, 'admin');
  evidence.landingUrl = landing;
  expect(landing, 'admin must reach an authenticated route, not /change-password').toContain('/admin');

  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });

  // RENDER-TRUTH: the form is populated from the store, not blank.
  const titleValue = await page.inputValue('#site-title');
  evidence.settingsFormSiteTitle = titleValue;
  expect(titleValue.trim().length, 'site title field should render its persisted value').toBeGreaterThan(0);

  // The Theme tab is not the default one, and TabsContent renders nothing until it is selected.
  await page.click('[data-testid="tab-theme"]');
  await expect(page.locator('[data-testid="theme-swatches"]')).toBeVisible({ timeout: 30000 });

  // The swatch is the same handler the Select uses and is reachable without opening a listbox.
  const swatch = page.locator(`[data-testid="theme-swatch-${PREVIEW_THEME}"]`);
  await expect(swatch, 'the preview theme swatch should be present').toBeVisible({ timeout: 30000 });
  await swatch.click();
  await page.waitForTimeout(1500);

  // The preview IS expected to apply for this admin — that is the feature.
  const adminOwnTheme = await page.getAttribute('html', 'data-site-theme');
  evidence.adminOwnThemeAfterPreview = adminOwnTheme;
  expect(adminOwnTheme, 'the previewing admin should see the theme they picked').toBe(PREVIEW_THEME);

  // 3. THE ASSERTION — a second, independent, anonymous connection while the edit is unsaved.
  const themeDuringUnsavedEdit = await anonymousTheme();
  evidence.themeDuringUnsavedEdit = themeDuringUnsavedEdit;
  expect(themeDuringUnsavedEdit, 'an unsaved preview must not reach other users').toBe(themeBefore);

  // 4. The admin abandons the form, exactly as the reported scenario describes.
  await nav(page, '/admin');
  await page.waitForTimeout(1500);

  const themeAfterAbandon = await anonymousTheme();
  evidence.themeAfterAbandon = themeAfterAbandon;
  expect(themeAfterAbandon, 'abandoning the form must leave the site theme untouched').toBe(themeBefore);

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(`${ARTIFACTS}/leak-evidence.json`, JSON.stringify(evidence, null, 2));
});

test('unsaved edits to other fields on /settings do not leak either', async ({ page }) => {
  const api = await request.newContext({ baseURL: BASE });
  const titleBefore = /<title>([^<]*)<\/title>/.exec(await (await api.get('/')).text())?.[1] ?? '';

  await login(page, 'admin');
  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });

  // The theme field was the reported symptom; the whole form shared the same object.
  await page.fill('#site-title', 'LEAKED TITLE — never saved');
  await page.waitForTimeout(1500);

  const titleDuringEdit = /<title>([^<]*)<\/title>/.exec(await (await api.get('/')).text())?.[1] ?? '';
  expect(titleDuringEdit, 'an unsaved site title must not reach other users').toBe(titleBefore);

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(
    `${ARTIFACTS}/scalar-evidence.json`,
    JSON.stringify({ titleBefore, titleDuringEdit }, null, 2));

  await api.dispose();
});

/**
 * The counterweight: detaching the form's model must not detach the Save button.
 *
 * Runs the full round trip against the live site and restores the seeded value afterwards, so the
 * database is left exactly as it was found.
 */
test('saving a theme change on /settings still takes effect site-wide', async ({ page }) => {
  const evidence: Record<string, unknown> = {};

  await login(page, 'admin');
  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });
  await page.click('[data-testid="tab-theme"]');
  await expect(page.locator('[data-testid="theme-swatches"]')).toBeVisible({ timeout: 30000 });

  await page.click('[data-testid="theme-swatch-developer"]');
  await page.waitForTimeout(800);
  await page.click('[data-testid="save-settings"]');
  await page.waitForTimeout(4000);

  evidence.anonymousThemeAfterSave = await anonymousTheme();
  expect(evidence.anonymousThemeAfterSave, 'a SAVED theme must reach every visitor').toBe('developer');

  // Restore the seeded value through the same UI, leaving the database as it was found.
  await page.click('[data-testid="theme-swatch-trblaze-modern"]');
  await page.waitForTimeout(800);
  await page.click('[data-testid="save-settings"]');
  await page.waitForTimeout(4000);

  evidence.anonymousThemeAfterRestore = await anonymousTheme();
  expect(evidence.anonymousThemeAfterRestore, 'the seeded theme should be restored').toBe(SEED_THEME);

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(`${ARTIFACTS}/save-path-evidence.json`, JSON.stringify(evidence, null, 2));
});

test('/settings still looks right at desktop and mobile widths', async ({ page }) => {
  await login(page, 'admin');
  await nav(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 45000 });

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  const results = [];
  for (const width of [1280, 390]) {
    const result = await visualCheck(page, `${ARTIFACTS}/settings-${width}.png`, width);
    results.push(result);
    expect(result.zeroSized, `zero-sized controls at ${width}`).toEqual([]);
    expect(result.offViewport, `off-viewport controls at ${width}`).toEqual([]);
    expect(result.overlaps, `overlapping sibling controls at ${width}`).toEqual([]);
    expect(result.hScroll, `horizontal document scroll at ${width}`).toBe(0);
  }

  fs.writeFileSync(`${ARTIFACTS}/visual.json`, JSON.stringify(results, null, 2));
});
