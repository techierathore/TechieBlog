import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import { BASE, nav, visualCheck } from './_gates';
import { psql, signIn, settle, expectVisualClean } from './vall-resume-helpers';

/**
 * Build-phase 2026-10-27 cluster A verify (group media): REQ-UI-034, REQ-UI-035.
 * Only uploads named fxa-*.png; everything is removed through the UI, then confirmed in psql + disk.
 */
const OUT = 'tests/.artifacts/build-phase-20261027/a/media';
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);
const disk = (p: string) => ['source/TechieBlog/wwwroot', 'source/BlogUI/wwwroot'].map((r) => `${r}${p}`);

test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

test('REQ-UI-034 media library: tabs, empty tab, upload, copy URL, delete', async ({ page, context }) => {
  test.setTimeout(400000);
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  await page.addInitScript(() => {
    (window as any).__copied = null;
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: async (t: string) => { (window as any).__copied = t; } },
    });
  });
  const landing = await signIn(page, 'admin');
  console.log('LANDING', landing);
  await nav(page, '/admin/images', /Media Library/i);
  await settle(page);
  await expect(page.locator('[data-testid="media-library-page"]')).toBeVisible();

  const tabs = page.locator('[data-testid="category-tabs"] [role="tab"]');
  console.log('TABS', await tabs.allTextContents());
  expect(await tabs.count()).toBe(7);

  // empty tab -> empty state
  await tabs.filter({ hasText: /^Icons$/ }).click();
  await expect(page.locator('[data-testid="images-empty"]')).toBeVisible({ timeout: 20000 });
  await page.screenshot({ path: `${OUT}/034-empty-1280.png` });

  // upload from the Icons tab (dialog opens on that category)
  await page.click('[data-testid="upload-image"]');
  const dlg = page.locator('[data-testid="image-upload-dialog"]');
  await expect(dlg).toBeVisible();
  await dlg.locator('input[type="file"]').setInputFiles({ name: 'fxa-media.png', mimeType: 'image/png', buffer: PNG });
  await expect(dlg.locator('[data-testid="upload-selected-file"]')).toBeVisible();
  await dlg.locator('[data-testid="upload-alt-text-input"]').fill('fxa media test');
  await dlg.locator('[data-testid="upload-confirm"]').click();
  await expect(dlg).toBeHidden({ timeout: 30000 });
  await expect(page.locator('[data-testid="image-card"]')).toHaveCount(1, { timeout: 20000 });
  const row = psql("SELECT imagepath||'|'||category||'|'||userid FROM blogimage WHERE imagename='fxa-media.png'");
  console.log('ROW', row);
  const [imagePath, category] = row.split('|');
  expect(category).toBe('icons');
  console.log('DISK', disk(imagePath).map((p) => `${p}:${fs.existsSync(p)}`));
  const card = page.locator('[data-testid="image-card"]');
  await expect(card.locator('[data-testid="image-name"]')).toContainText('fxa-media');
  await expect(card.locator('[data-testid="image-size"]')).not.toBeEmpty();
  const imgOk = await card.locator('img').evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth > 0);
  expect(imgOk, 'grid thumbnail decodes').toBe(true);

  // other tabs must not show it
  await tabs.filter({ hasText: /^Logos$/ }).click();
  await expect(page.locator('[data-testid="category-tabs"] [role="tab"][data-state="active"], [data-testid="category-tabs"] [role="tab"][aria-selected="true"]')).toContainText('Logos');
  await page.waitForTimeout(1500);
  console.log('LOGOS TAB NAMES', await page.locator('[data-testid="image-name"]').allTextContents());
  expect(await page.locator('[data-testid="image-name"]', { hasText: 'fxa-media' }).count()).toBe(0);
  await tabs.filter({ hasText: /^Icons$/ }).click();
  await expect(page.locator('[data-testid="image-card"]')).toHaveCount(1, { timeout: 20000 });

  // copy URL
  await card.locator('[data-testid="copy-image-url"]').click();
  await expect(page.locator('[data-testid="images-status"]')).toContainText(/copied/i);
  const copied: string = await page.evaluate(() => (window as any).__copied);
  console.log('COPIED', copied);
  const u = copied.startsWith('http') ? copied : `${BASE}${copied}`;
  const resp = await page.request.get(u.replace(/^https?:\/\/[^/]+/, BASE));
  console.log('SERVE', resp.status(), resp.headers()['content-type']);
  expect(resp.status()).toBe(200);
  expect(resp.headers()['content-type']).toMatch(/image\/png/);

  // visual @1280 and @390
  const v1 = await visualCheck(page, `${OUT}/034-grid-1280.png`, 1280);
  const v2 = await visualCheck(page, `${OUT}/034-grid-390.png`, 390);
  console.log('V1280', JSON.stringify(v1), '\nV390', JSON.stringify(v2));
  await page.setViewportSize({ width: 1280, height: 900 });
  expect(v1.hScroll).toBe(0);
  expect(v2.hScroll).toBe(0);

  // delete
  await page.locator('[data-testid="delete-image"]').first().click();
  await expect(page.locator('[data-testid="image-delete-dialog"]')).toBeVisible();
  await page.locator('[data-testid="delete-confirm"]').click();
  await expect(page.locator('[data-testid="images-empty"]')).toBeVisible({ timeout: 20000 });
  expect(psql("SELECT count(*) FROM blogimage WHERE imagename='fxa-media.png'")).toBe('0');
  const left = disk(imagePath).filter((p) => fs.existsSync(p));
  console.log('FILES LEFT AFTER DELETE', left);
  expect(left).toEqual([]);
  console.log('CONSOLE ERRORS', errors);
});

test('REQ-UI-035 ImagePicker: upload via picker, choose from library, category scoping', async ({ page }) => {
  test.setTimeout(400000);
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  await signIn(page, 'admin');
  await nav(page, '/admin/profile', /Profile/i);
  await settle(page);
  const pickers = page.locator('[data-testid="image-picker"]');
  console.log('PICKERS', await pickers.count());
  const picker = pickers.first();
  await expect(picker).toBeVisible();
  console.log('CONSTRAINTS', await picker.locator('[data-testid="image-constraints"]').textContent());

  await picker.locator('[data-testid="upload-new-image"]').click();
  const dlg = page.locator('[data-testid="image-upload-dialog"]');
  await expect(dlg).toBeVisible();
  await page.waitForTimeout(2000);
  let staged = false;
  for (let i = 1; i <= 3 && !staged; i++) {
    await dlg.locator('input[type="file"]').setInputFiles({ name: 'fxa-pick.png', mimeType: 'image/png', buffer: PNG });
    staged = await dlg.locator('[data-testid="upload-selected-file"]').waitFor({ timeout: 8000 }).then(() => true, () => false);
    console.log('STAGE ATTEMPT', i, staged, 'error panel:', await dlg.locator('[data-testid="upload-error"]').count());
  }
  expect(staged, 'picker upload dialog stages the chosen file').toBe(true);
  await dlg.locator('[data-testid="upload-confirm"]').click();
  await expect(dlg).toBeHidden({ timeout: 40000 });
  const img = picker.locator('[data-testid="selected-image"]');
  await expect(img).toBeVisible({ timeout: 20000 });
  const row = psql("SELECT imagepath||'|'||category FROM blogimage WHERE imagename='fxa-pick.png'");
  console.log('ROW', row);
  const [path, cat] = row.split('|');
  expect(cat).toBe('profiles');
  expect(await img.getAttribute('src')).toBe(path);
  const decodes = await img.evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth > 0);
  expect(decodes, 'preview decodes').toBe(true);
  await page.screenshot({ path: `${OUT}/035-picker-uploaded-1280.png` });

  // choose from library
  await picker.locator('[data-testid="clear-image"]').click();
  await expect(img).toHaveCount(0);
  await picker.locator('[data-testid="choose-from-library"]').click();
  const gal = page.locator('[data-testid="image-gallery-dialog"]');
  await expect(gal).toBeVisible();
  await expect(gal.locator('[data-testid="gallery-image"]').first()).toBeVisible({ timeout: 30000 });
  const srcs = await gal.locator('[data-testid="gallery-image"] img, img[data-testid="gallery-image"]').evaluateAll(
    (els) => els.map((e) => (e as HTMLImageElement).getAttribute('src')),
  );
  console.log('GALLERY SRCS', srcs);
  const tiles = await gal.locator('[data-testid="gallery-image"]').count();
  console.log('GALLERY TILES', tiles, 'DB profiles rows', psql("SELECT count(*) FROM blogimage WHERE category='profiles'"));
  expect(srcs.filter(Boolean).every((s) => s!.startsWith('/uploads/profiles/'))).toBe(true);
  expect(srcs).toContain(path);
  await page.screenshot({ path: `${OUT}/035-gallery-1280.png` });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${OUT}/035-gallery-390.png` });
  const hs = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  console.log('HSCROLL390', hs);
  await page.setViewportSize({ width: 1280, height: 900 });
  await gal.locator('[data-testid="gallery-image"]').filter({ has: page.locator(`img[src="${path}"]`) }).first().click()
    .catch(async () => { await gal.locator(`img[src="${path}"]`).first().click(); });
  await expect(img).toBeVisible({ timeout: 15000 });
  expect(await img.getAttribute('src')).toBe(path);

  // the cv picker lists only cv images
  await pickers.nth(1).locator('[data-testid="choose-from-library"]').click();
  const gal2 = page.locator('[data-testid="image-gallery-dialog"]');
  await expect(gal2).toBeVisible();
  await page.waitForTimeout(2500);
  const srcs2 = await gal2.locator('img').evaluateAll((els) => els.map((e) => (e as HTMLImageElement).getAttribute('src')));
  console.log('CV GALLERY SRCS', srcs2);
  expect(srcs2.filter((s) => s && s.startsWith('/uploads/')).every((s) => s!.startsWith('/uploads/cv/'))).toBe(true);
  await gal2.locator('[data-testid="gallery-cancel"]').click();
  console.log('CONSOLE ERRORS', errors);

  // cleanup via the media library UI (profiles tab)
  await nav(page, '/admin/images', /Media Library/i);
  await settle(page);
  const card = page.locator('[data-testid="image-card"]').filter({ hasText: 'fxa-pick' });
  await expect(card).toHaveCount(1, { timeout: 20000 });
  await card.locator('[data-testid="delete-image"]').click();
  await page.locator('[data-testid="delete-confirm"]').click();
  await expect(card).toHaveCount(0, { timeout: 20000 });
  expect(psql("SELECT count(*) FROM blogimage WHERE imagename LIKE 'fxa-%'")).toBe('0');
  console.log('FILES LEFT', disk(path).filter((p) => fs.existsSync(p)));
});
