/**
 * w3-media.spec.ts — verify-phase 2026-10-27, group W3 (media).
 *
 * REQ-FN-026 (BlogImage metadata + category schema). The other media rows are covered by bp-a-media.spec.ts
 * (REQ-UI-034/035), vall-resume-media.spec.ts and bp-b-upload.spec.ts (REQ-FN-025). Every upload is named
 * `fxv3-*`, goes through the app's own upload dialog and is removed through the media library's delete dialog
 * (the verify phase forbids database writes). Only psql READS are used.
 */
import { test, expect, Page } from '@playwright/test';
import { loginHard, goTo, pickSelect } from './vall-authoring-helpers';
import { psql, psqlRows, pollDb, BASE, SHOTS } from './w3-helpers';

const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64');

async function deleteFxv3(page: Page) {
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 90000);
  await goTo(page, '/admin/images', '[data-testid="media-library-page"]', 90000);
  await page.waitForTimeout(1500);
  for (const [category] of psqlRows(`SELECT DISTINCT category FROM blogimage WHERE imagename LIKE 'fxv3-%'`)) {
    const tab = page.locator('[data-testid="category-tabs"] [role="tab"]').filter({ hasText: new RegExp(`^${category}`, 'i') }).first();
    if (!(await tab.count())) continue;
    await tab.click();
    await page.waitForTimeout(1500);
    for (let i = 0; i < 6; i++) {
      const card = page.locator('[data-testid="image-card"]').filter({ hasText: 'fxv3-' }).first();
      if (!(await card.count())) break;
      await card.locator('[data-testid="delete-image"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await page.waitForTimeout(2500);
    }
  }
}

test('REQ-FN-026 an uploaded image is stored with its category, type, size, alt text, owner and time; the schema carries those columns', async ({ browser }) => {
  test.setTimeout(420000);
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  try {
    // schema (read-only): the metadata columns and the category default exist
    const cols = psqlRows(`SELECT column_name, data_type, coalesce(column_default,'') FROM information_schema.columns WHERE table_name='blogimage'`);
    const names = cols.map((c) => c[0]);
    for (const c of ['imagename', 'imagepath', 'size', 'createdtime', 'userid', 'category', 'alttext', 'mimetype', 'width', 'height']) {
      expect(names, `blogimage has column ${c}`).toContain(c);
    }
    expect(cols.find((c) => c[0] === 'category')![2], 'category defaults to general').toContain('general');

    await loginHard(page, 'admin');
    await deleteFxv3(page); // leftovers of an aborted earlier run
    await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 90000);
    await goTo(page, '/admin/images', '[data-testid="media-library-page"]', 90000);
    await page.waitForTimeout(1500);

    await page.click('[data-testid="upload-image"]');
    const dialog = page.locator('[data-testid="image-upload-dialog"]');
    await expect(dialog).toBeVisible({ timeout: 30000 });
    await pickSelect(page, 'upload-category-select', /^\s*Logos/i);
    const input = dialog.locator('[data-testid="upload-dropzone"] input[type="file"]');
    const staged = dialog.locator('[data-testid="upload-selected-file"]');
    for (let attempt = 0; attempt < 5 && !(await staged.isVisible()); attempt++) {
      await page.waitForTimeout(1500);
      await input.setInputFiles({ name: 'fxv3-meta.png', mimeType: 'image/png', buffer: PNG_1X1 });
      await staged.waitFor({ state: 'visible', timeout: 8000 }).catch(() => {});
    }
    await expect(staged).toContainText('fxv3-meta.png');
    await dialog.locator('[data-testid="upload-alt-text-input"]').fill('fxv3 alt text');
    await dialog.locator('[data-testid="upload-alt-text-input"]').dispatchEvent('change');
    await dialog.locator('[data-testid="upload-confirm"]').click();
    expect(await pollDb(`SELECT count(*) FROM blogimage WHERE imagename='fxv3-meta.png'`, (v) => v === '1', 60000), 'one metadata row').toBe('1');

    const [id, path, category, mime, size, alt, userId, ageSeconds, width, height] = psqlRows(
      `SELECT blogimageid, imagepath, category, mimetype, size, coalesce(alttext,'<null>'), userid, extract(epoch from (now() at time zone 'utc' - createdtime))::int, coalesce(width::text,'<null>'), coalesce(height::text,'<null>') FROM blogimage WHERE imagename='fxv3-meta.png'`,
    )[0];
    console.log(`FN-026 row: id=${id} path=${path} category=${category} mime=${mime} size=${size} alt=${alt} user=${userId} age=${ageSeconds}s w=${width} h=${height}`);
    expect(category, 'category stored').toBe('logos');
    expect(mime, 'mime type stored').toBe('image/png');
    expect(Number(size), 'size stored (bytes of the file)').toBe(PNG_1X1.length);
    expect(alt, 'alt text stored').toBe('fxv3 alt text');
    expect(userId, 'owner stored').toBe(psql(`SELECT userid FROM bloguser WHERE emailid='Ravi@techieblog.com'`));
    expect(Number(ageSeconds), 'created time stored (UTC, recent)').toBeLessThan(600);
    expect(path).toMatch(/^\/uploads\/logos\/logos-\d+-\d{14}-[0-9a-f]{8}\.png$/);
    expect((await page.request.get(`${BASE}${path}`)).status(), 'the stored file serves').toBe(200);
    await page.screenshot({ path: `${SHOTS}/fn026-uploaded.png` });

    await deleteFxv3(page);
    expect(await pollDb(`SELECT count(*) FROM blogimage WHERE imagename LIKE 'fxv3-%'`, (v) => v === '0', 30000), 'row removed through the library').toBe('0');
    expect((await page.request.get(`${BASE}${path}`)).status(), 'file removed with it').toBe(404);
  } finally {
    await deleteFxv3(page).catch(() => {});
    await page.close();
  }
});
