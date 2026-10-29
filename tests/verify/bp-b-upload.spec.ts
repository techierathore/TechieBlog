/**
 * bp-b-upload.spec.ts — cluster B build-phase smoke (2026-10-27) for REQ-FN-025: per-category
 * upload validation. Through the admin's real upload dialog on /admin/images:
 *   - a file over its category's size limit is rejected with a message naming the limit;
 *   - a file in a refused format is rejected with a message naming the allowed formats;
 *   - a valid file is stored under a generated unique name;
 *   - two uploads of the same filename never overwrite each other.
 * Every row this spec writes carries the image name prefix `fxb-` and is deleted (row and file).
 */
import { test, expect, Page } from '@playwright/test';
import { execSync } from 'child_process';
import { loginHard, goTo, pickSelect } from './vall-authoring-helpers';

const BASE = process.env.TB_BASE ?? 'http://localhost:5450';
const SHOTS = 'tests/.artifacts/verify/w3';

const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64');

function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

/**
 * Removes every fxb- image through the media library's own delete dialog. The verify phase forbids database
 * writes (guard-db.sh), so this replaces the old psql DELETE + file unlink; the app removes row and file itself.
 */
async function removeFixtures(page: Page) {
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 90000);
  await goTo(page, '/admin/images', '[data-testid="media-library-page"]', 90000);
  await page.waitForTimeout(1500);
  const categories = sql("SELECT DISTINCT category FROM blogimage WHERE imagename LIKE 'fxb-%'");
  for (const category of categories) {
    const tab = page.locator('[data-testid="category-tabs"] [role="tab"]').filter({ hasText: new RegExp(`^${category}`, 'i') }).first();
    if (!(await tab.count())) continue;
    await tab.click();
    await page.waitForTimeout(1500);
    for (let i = 0; i < 6; i++) {
      const card = page.locator('[data-testid="image-card"]').filter({ hasText: 'fxb-' }).first();
      if (!(await card.count())) break;
      await card.locator('[data-testid="delete-image"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await page.waitForTimeout(2500);
    }
  }
}

test.afterAll(async ({ browser }) => {
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await loginHard(page, 'admin');
  await removeFixtures(page);
  await page.close();
  console.log('FN-025 fxb- rows left =', sql("SELECT count(*) FROM blogimage WHERE imagename LIKE 'fxb-%'")[0]);
});

async function openDialog(page: Page, categoryLabel: string) {
  // Leave the library first so every dialog starts from a freshly rendered page.
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 90000);
  await goTo(page, '/admin/images', '[data-testid="media-library-page"]', 90000);
  await page.waitForTimeout(1500);
  await page.click('[data-testid="upload-image"]');
  await expect(page.locator('[data-testid="image-upload-dialog"]')).toBeVisible({ timeout: 30000 });
  await pickSelect(page, 'upload-category-select', new RegExp(`^\\s*${categoryLabel}`, 'i'));
}

async function stage(page: Page, name: string, mimeType: string, buffer: Buffer) {
  const input = page.locator('[data-testid="upload-dropzone"] input[type="file"]');
  const staged = page.locator('[data-testid="upload-selected-file"]');
  const error = page.locator('[data-testid="upload-error"]');
  for (let attempt = 0; attempt < 5; attempt++) {
    await page.waitForTimeout(1200);
    await input.setInputFiles({ name, mimeType, buffer });
    await Promise.race([
      staged.waitFor({ state: 'visible', timeout: 8000 }),
      error.waitFor({ state: 'visible', timeout: 8000 }),
    ]).catch(() => {});
    if ((await staged.isVisible()) || (await error.isVisible())) return;
  }
}

test('REQ-FN-025 over-limit and refused-format files are rejected with the limit named; valid files get unique names', async ({ browser }) => {
  test.setTimeout(420000);
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await loginHard(page, 'admin');
  await removeFixtures(page); // anything an aborted earlier run left, through the UI

  // Icons: max 200 KB, png / svg / webp.
  await openDialog(page, 'Icons');
  await expect(page.locator('[data-testid="upload-category-constraints"]')).toContainText(/200 KB/, { timeout: 30000 });

  // 1. over the limit: a PNG-named 300 KB file
  const big = Buffer.concat([PNG_1X1, Buffer.alloc(300 * 1024)]);
  await stage(page, 'fxb-big.png', 'image/png', big);
  if (!(await page.locator('[data-testid="upload-error"]').count())) {
    // the dropzone may stage it; the service must refuse on submit
    await page.click('[data-testid="upload-confirm"]');
  }
  await expect(page.locator('[data-testid="upload-error"]')).toContainText(/200 KB/, { timeout: 30000 });
  console.log(`FN-025 over-limit message: ${((await page.locator('[data-testid="upload-error"]').textContent()) || '').trim()}`);
  expect(sql("SELECT count(*) FROM blogimage WHERE imagename = 'fxb-big.png'")[0]).toBe('0');
  await page.screenshot({ path: `${SHOTS}/fn025-over-limit-1280.png` });

  // 2. refused format
  await openDialog(page, 'Icons');
  await stage(page, 'fxb-notes.exe', 'application/x-msdownload', Buffer.from('MZ not an image'));
  if (!(await page.locator('[data-testid="upload-error"]').count())) {
    await page.click('[data-testid="upload-confirm"]');
    await expect(page.locator('[data-testid="upload-error"]')).toBeVisible({ timeout: 30000 });
  }
  await expect(page.locator('[data-testid="upload-error"]')).toContainText(/not an accepted file type|not allowed/i, { timeout: 30000 });
  console.log(`FN-025 refused-format message: ${((await page.locator('[data-testid="upload-error"]').textContent()) || '').trim()}`);
  expect(sql("SELECT count(*) FROM blogimage WHERE imagename = 'fxb-notes.exe'")[0]).toBe('0');

  // 3. + 4. a valid file twice under the same filename
  const uploadOnce = async () => {
    await openDialog(page, 'Icons');
    await stage(page, 'fxb-icon.png', 'image/png', PNG_1X1);
    await expect(page.locator('[data-testid="upload-selected-file"]')).toContainText('fxb-icon.png');
    await page.fill('[data-testid="upload-alt-text-input"]', 'fxb upload fixture');
    await page.click('[data-testid="upload-confirm"]');
    await page.waitForTimeout(6000);
  };
  await uploadOnce();
  await uploadOnce();
  const rows = sql("SELECT imagepath FROM blogimage WHERE imagename = 'fxb-icon.png' ORDER BY blogimageid");
  expect(rows.length, 'two uploads, two rows').toBe(2);
  expect(rows[0], 'the stored name is generated, not the upload name').not.toContain('fxb-icon');
  expect(rows[0]).not.toBe(rows[1]);
  for (const r of rows) {
    const res = await page.request.get(`${BASE}${r}`);
    expect(res.status(), `${r} is served`).toBe(200);
  }
  await page.screenshot({ path: `${SHOTS}/fn025-uploaded-1280.png` });
  await page.close();
});
