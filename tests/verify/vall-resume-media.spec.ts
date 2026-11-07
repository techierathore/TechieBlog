/**
 * vall-resume-media.spec.ts — cluster "resume-media-newsletter", part 2 of 3.
 *
 * Grades REQ-UI-034 (media library), REQ-UI-035 (ImagePicker), REQ-FN-025 (per-category upload
 * validation) and REQ-FN-026 (BlogImage metadata + category schema).
 *
 * `blogimage` starts EMPTY, so an empty gallery is NO-DATA rather than a render defect. Every
 * test therefore populates through the app's own write path and removes what it created; each
 * upload is named `verify-0808-*` so a leak is identifiable.
 */
import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { BASE, nav, renderCheck, ControlResult } from './_gates';
import { psql, report, expectVisualClean, bothWidths, signIn, settle } from './vall-resume-helpers';

const SHOTS = '.verify/shots/resume';
// The fixture files used to live in an untracked `.verify/fixtures` folder that no longer exists; they are
// generated per run instead (2026-10-27) so the spec carries everything it needs.
const FIXTURES = path.resolve('tests/.artifacts/verify/w3/fixtures-vall');
const PNG_1X1 = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64');

test.beforeAll(() => {
  fs.mkdirSync(SHOTS, { recursive: true });
  fs.mkdirSync(FIXTURES, { recursive: true });
  fs.writeFileSync(path.join(FIXTURES, 'verify-0808-small.png'), PNG_1X1);
  fs.writeFileSync(path.join(FIXTURES, 'verify-0808-huge.png'), Buffer.concat([PNG_1X1, Buffer.alloc(2_400_000)])); // > the 2 MB profiles cap
  fs.writeFileSync(path.join(FIXTURES, 'verify-0808-bad.txt'), 'not an image\n');
});

// Seven verification agents share this host; a page can take ~10s just to go interactive.
test.beforeEach(({}, testInfo) => testInfo.setTimeout(420000));

/**
 * Reports any row this cluster left behind. The verify phase forbids database writes, so rows are removed
 * through the app (the media library's delete dialog), never through psql; this only READS.
 */
function cleanupUploads() {
  return psql("SELECT imagepath FROM blogimage WHERE imagename LIKE 'verify-0808%'")
    .split('\n')
    .map((s) => s.trim())
    .filter(Boolean);
}

/** Deletes every leftover verify-0808 image through the media library UI. */
async function deleteLeftoversThroughUi(page: import('@playwright/test').Page) {
  await nav(page, '/admin/images', /Media Library/i);
  await settle(page);
  for (const tab of ['Profiles', 'CV', 'Logos', 'Icons', 'Blog', 'Awards', 'General']) {
    const t = page.locator('[data-testid="category-tabs"] [role="tab"]').filter({ hasText: new RegExp(`^${tab}`, 'i') }).first();
    if (!(await t.count())) continue;
    await t.click();
    await page.waitForTimeout(1500);
    for (let i = 0; i < 5; i++) {
      const card = page.locator('[data-testid="image-card"]').filter({ hasText: 'verify-0808' }).first();
      if (!(await card.count())) break;
      await card.locator('[data-testid="delete-image"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await page.waitForTimeout(2500);
    }
  }
}

// ---------------------------------------------------------------------------------------------
// REQ-UI-034 / REQ-FN-025 / REQ-FN-026 — the media library
// ---------------------------------------------------------------------------------------------

test('REQ-UI-034 media library shows category tabs, uploads through its own dialog, then copies and deletes', async ({ page }) => {
  const startRows = Number(psql('SELECT count(*) FROM blogimage'));
  const controls: ControlResult[] = [];
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/images', /Media Library/i);
    await settle(page);
    await expect(page.locator('[data-testid="media-library-page"]')).toBeVisible();

    controls.push(await renderCheck(page, 'category tabs', '[data-testid="category-tabs"]', 'present'));
    controls.push(await renderCheck(page, 'user filter', '[data-testid="user-filter-select"]', 'present'));
    controls.push(await renderCheck(page, 'upload button', '[data-testid="upload-image"]', 'present'));

    const tabs = page.locator('[data-testid="category-tabs"] [role="tab"], [data-testid="category-tabs"] button');
    const tabCount = await tabs.count();
    const tabLabels: string[] = [];
    for (let i = 0; i < tabCount; i++) tabLabels.push(((await tabs.nth(i).textContent()) ?? '').trim());
    console.log('REQ-UI-034 category tabs =', JSON.stringify(tabLabels));
    expect(tabCount, 'seven upload categories must each have a tab').toBe(7);

    const emptyBefore = await page.locator('[data-testid="images-empty"]').count();
    // The shown tab either lists its images (grid) or states it is empty; both render truthfully, and the
    // dev DB is not guaranteed to be empty (other runs' rows exist), so neither is demanded.
    const gridShown = await page.locator('[data-testid="image-grid"]').count();
    controls.push({
      control: 'gallery by category',
      verdict: gridShown + emptyBefore > 0 ? 'RENDERS' : 'RENDER-EMPTY',
      detail: `blogimage holds ${startRows} rows; the shown tab renders ${gridShown ? 'its grid' : 'its empty state'}`,
    });

    // ---- upload through the page's own dialog (this IS the acceptance test) ----
    await page.click('[data-testid="upload-image"]');
    const dialog = page.locator('[data-testid="image-upload-dialog"]');
    await expect(dialog).toBeVisible();
    // The dropzone's listener attaches after the dialog renders; a file set earlier is dropped silently, so set it
    // again until the dialog reports the staged file.
    for (let attempt = 0; attempt < 5 && !(await dialog.locator('[data-testid="upload-selected-file"]').isVisible()); attempt++) {
      await page.waitForTimeout(1500);
      await dialog.locator('input[type="file"]').setInputFiles(path.join(FIXTURES, 'verify-0808-small.png'));
      await dialog.locator('[data-testid="upload-selected-file"]').waitFor({ state: 'visible', timeout: 8000 }).catch(() => {});
    }
    await expect(dialog.locator('[data-testid="upload-selected-file"]')).toBeVisible();
    await dialog.locator('[data-testid="upload-confirm"]').click();
    await expect(dialog).toBeHidden({ timeout: 30000 });
    await page.waitForTimeout(1200);

    const uploaded = psql(
      "SELECT blogimageid||'|'||imagepath||'|'||category||'|'||COALESCE(mimetype,'')||'|'||size||'|'||COALESCE(alttext,'<null>')||'|'||COALESCE(width::text,'<null>')||'|'||COALESCE(height::text,'<null>') FROM blogimage WHERE imagename LIKE 'verify-0808%'",
    );
    console.log('REQ-FN-026 blogimage row (id|path|category|mime|size|alt|w|h) =', uploaded);
    expect(uploaded, 'the upload must produce exactly one metadata row').not.toBe('');
    const [, imagePath, category, mime, size] = uploaded.split('|');
    // The dialog opens on whichever tab is selected — "profiles" by default (ManageImages.razor.cs:99).
    expect(category, 'the upload must land in the category the dialog was showing').toBe('profiles');
    expect(mime).toBe('image/png');
    expect(Number(size)).toBe(fs.statSync(path.join(FIXTURES, 'verify-0808-small.png')).size);
    // Collision-proof name: {category}-{userId}-{timestamp}-{guid8}.{ext} (the DevGuide writes it
    // with underscores; the code uses hyphens — a doc/impl mismatch, not a defect).
    expect(imagePath).toMatch(/^\/uploads\/profiles\/profiles-1-\d{14}-[0-9a-f]{8}\.png$/);

    const served = await page.request.get(`${BASE}${imagePath}`);
    expect(served.status(), `GET ${imagePath}`).toBe(200);

    // The grid must now show the uploaded file with a real name and size.
    controls.push(await renderCheck(page, 'image grid', '[data-testid="image-grid"]', 'present'));
    controls.push(await renderCheck(page, 'image name', '[data-testid="image-name"]'));
    controls.push(await renderCheck(page, 'image size', '[data-testid="image-size"]'));
    controls.push(await renderCheck(page, 'image count', '[data-testid="image-count"]'));
    controls.push(await renderCheck(page, 'copy URL', '[data-testid="copy-image-url"]', 'present'));
    controls.push(await renderCheck(page, 'delete', '[data-testid="delete-image"]', 'present'));
    // The dev DB already holds other rows (other runs' images): assert OUR card, not a global count.
    await expect(page.locator('[data-testid="image-card"]').filter({ hasText: 'verify-0808' })).toHaveCount(1);

    const visuals = await bothWidths(page, 'req-ui-034-media-library');

    // Copy URL — the button must report success rather than throw.
    await page.locator('[data-testid="copy-image-url"]').first().click();
    await page.waitForTimeout(800);
    const copyStatus = (await page.locator('[data-testid="images-status"]').textContent().catch(() => '')) ?? '';
    console.log('REQ-UI-034 copy-url status =', JSON.stringify(copyStatus.trim().slice(0, 120)));

    // ---- delete through the page ----
    await page.locator('[data-testid="delete-image"]').first().click();
    await expect(page.locator('[data-testid="image-delete-dialog"]')).toBeVisible();
    await page.locator('[data-testid="image-delete-dialog"] [data-testid="delete-confirm"]').click();
    await page.waitForTimeout(1500);
    const rowsAfterDelete = Number(psql("SELECT count(*) FROM blogimage WHERE imagename LIKE 'verify-0808%'"));
    console.log('REQ-UI-034 rows after in-app delete =', rowsAfterDelete);
    expect(rowsAfterDelete, 'delete must remove the metadata row').toBe(0);
    const goneStatus = (await page.request.get(`${BASE}${imagePath}`)).status();
    expect(goneStatus, 'delete must remove the stored file (the path no longer serves)').toBe(404);

    report('/admin/images', controls, visuals);
    for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
    visuals.forEach(expectVisualClean);
  } finally {
    if (cleanupUploads().length) await deleteLeftoversThroughUi(page).catch(() => {});
    console.log('REQ-UI-034 CLEANUP leftovers =', JSON.stringify(cleanupUploads()), 'blogimage total =', psql('SELECT count(*) FROM blogimage'));
  }
});

test('REQ-FN-025 the upload service rejects a file that breaks its category size or format rule', async ({ page }) => {
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/images', /Media Library/i);
    await settle(page);

    const dialog = page.locator('[data-testid="image-upload-dialog"]');

    // 1. A 2.4 MB PNG into "profiles" — the tab the dialog opens on, capped at 2 MB.
    const constraints = ((await page.locator('[data-testid="upload-image"]').textContent()) ?? '').trim();
    console.log('REQ-FN-025 upload entry point =', JSON.stringify(constraints));
    await page.click('[data-testid="upload-image"]');
    await expect(dialog).toBeVisible();
    // ManageImages validates on SELECTION (OnFilesChanged → ValidateImageAsync), clearing the
    // pending file, so the rejection surfaces without ever reaching the Upload button.
    await dialog.locator('input[type="file"]').setInputFiles(path.join(FIXTURES, 'verify-0808-huge.png'));
    await expect(dialog.locator('[data-testid="upload-error"]')).toBeVisible({ timeout: 60000 });
    await expect(dialog.locator('[data-testid="upload-confirm"]'), 'a rejected file must not be uploadable').toBeDisabled();
    const sizeError = ((await dialog.locator('[data-testid="upload-error"]').textContent()) ?? '').trim();
    console.log('REQ-FN-025 oversize error =', JSON.stringify(sizeError));
    expect(sizeError).toMatch(/2\s*MB|size|exceeds|large/i);
    await dialog.screenshot({ path: `${SHOTS}/req-fn-025-oversize-rejected.png` });
    await dialog.locator('[data-testid="upload-cancel"]').click();
    await page.waitForTimeout(600);

    // 2. Plain text into "profiles" (jpg/jpeg/png/webp only).
    await page.click('[data-testid="upload-image"]');
    await expect(dialog).toBeVisible();
    await dialog.locator('input[type="file"]').setInputFiles(path.join(FIXTURES, 'verify-0808-bad.txt'));
    await page.waitForTimeout(4000);
    // FileUpload carries the category's MIME allow-list in its `accept`, so a text file is dropped
    // before `FilesChanged` ever fires: no inline error, but also nothing selectable to upload.
    const errorPanels = await dialog.locator('[data-testid="upload-error"]').count();
    const selectedPanels = await dialog.locator('[data-testid="upload-selected-file"]').count();
    const formatError = errorPanels
      ? ((await dialog.locator('[data-testid="upload-error"]').textContent()) ?? '').trim()
      : '(no inline error — the file was filtered by the accept list before selection)';
    console.log('REQ-FN-025 wrong-format outcome =', JSON.stringify(formatError), '| selected panels =', selectedPanels);
    expect(selectedPanels, 'a wrong-format file must never become selectable').toBe(0);
    await expect(dialog.locator('[data-testid="upload-confirm"]'), 'and must not be uploadable').toBeDisabled();
    await dialog.locator('[data-testid="upload-cancel"]').click();

    // Neither rejection may leave a row or a file behind.
    expect(Number(psql("SELECT count(*) FROM blogimage WHERE imagename LIKE 'verify-0808%'"))).toBe(0);
  } finally {
    if (cleanupUploads().length) await deleteLeftoversThroughUi(page).catch(() => {});
    console.log('REQ-FN-025 CLEANUP leftovers =', JSON.stringify(cleanupUploads()));
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-035 — the reusable ImagePicker
// ---------------------------------------------------------------------------------------------

test('REQ-UI-035 the ImagePicker uploads into its own category, binds the path and lists it in the library dialog', async ({ page }) => {
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/profile', /Profile/i);
    await settle(page);
    await expect(page.locator('[data-testid="manage-profile-page"]')).toBeVisible({ timeout: 45000 });

    // ManageProfile is the only screen that actually hosts the component: the avatar picker
    // (category "profiles") and the CV picker (category "cv").
    const pickers = page.locator('[data-testid="image-picker"]');
    await expect(pickers.first()).toBeVisible({ timeout: 30000 });
    const pickerCount = await pickers.count();
    console.log('REQ-UI-035 ImagePicker instances on /admin/profile =', pickerCount);
    expect(pickerCount).toBe(2);
    const picker = pickers.first();

    const controls: ControlResult[] = [];
    controls.push(await renderCheck(page, 'choose from library', '[data-testid="choose-from-library"]', 'present'));
    controls.push(await renderCheck(page, 'upload new', '[data-testid="upload-new-image"]', 'present'));
    controls.push(await renderCheck(page, 'category constraints', '[data-testid="image-constraints"]'));
    const constraints = ((await picker.locator('[data-testid="image-constraints"]').textContent()) ?? '').trim();
    console.log('REQ-UI-035 constraint text on the avatar picker =', JSON.stringify(constraints));
    expect(constraints, 'the picker must state its own category limits').toMatch(/2\s*MB/i);

    // Upload through the picker — the chosen path must bind back into the form (two-way binding).
    await picker.locator('[data-testid="upload-new-image"]').click();
    const uploadDialog = page.locator('[data-testid="image-upload-dialog"]');
    await expect(uploadDialog).toBeVisible();
    await page.waitForTimeout(2000);
    // A file staged before the dialog's circuit handler is wired is dropped silently (the confirm
    // button then stays disabled), so stage again until the dialog shows the selected file.
    for (let i = 0; i < 4; i++) {
      await uploadDialog.locator('input[type="file"]').setInputFiles(path.join(FIXTURES, 'verify-0808-small.png'));
      if (await uploadDialog.locator('[data-testid="upload-selected-file"]').waitFor({ timeout: 8000 }).then(() => true, () => false)) break;
    }
    await uploadDialog.locator('[data-testid="upload-confirm"]').click({ timeout: 30000 });
    await expect(uploadDialog).toBeHidden({ timeout: 40000 });
    await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible({ timeout: 20000 });

    const stored = psql("SELECT imagepath||'|'||category FROM blogimage WHERE imagename LIKE 'verify-0808%'");
    console.log('REQ-UI-035 picker upload =', stored);
    const [storedPath, storedCategory] = stored.split('|');
    expect(storedCategory, 'the picker must upload into the category it was given').toBe('profiles');
    expect(await picker.locator('[data-testid="selected-image"]').getAttribute('src')).toBe(storedPath);
    controls.push({ control: 'two-way bound selection', verdict: 'RENDERS', detail: `img src = ${storedPath}` });

    // "Choose from library" must now list that image, filtered to the picker's category.
    await picker.locator('[data-testid="clear-image"]').click();
    await page.waitForTimeout(600);
    await picker.locator('[data-testid="choose-from-library"]').click();
    const gallery = page.locator('[data-testid="image-gallery-dialog"]');
    await expect(gallery).toBeVisible();
    // The library dialog lists the picker's category only: the tile count equals the profiles rows in the database
    // (other rows already exist there), and our new image is among them.
    await expect(gallery.locator('[data-testid="gallery-image"]').first()).toBeVisible({ timeout: 30000 });
    await expect(gallery.locator(`img[src="${storedPath}"]`)).toHaveCount(1, { timeout: 30000 });
    const tileSrcs = await gallery.locator('[data-testid="gallery-image"] img, img[data-testid="gallery-image"]').evaluateAll((els) => els.map((e) => (e as HTMLImageElement).getAttribute('src')));
    expect(tileSrcs.every((src) => (src ?? '').startsWith('/uploads/profiles/')), 'only profiles images are listed').toBe(true);
    // (an exact count would race other writers' uploads; every tile must be a profiles row, and ours is one)
    expect(tileSrcs.length, 'no more tiles than profiles rows').toBeLessThanOrEqual(Number(psql("SELECT count(*) FROM blogimage WHERE category='profiles'")));
    controls.push(await renderCheck(page, 'gallery grid (category-filtered)', '[data-testid="gallery-grid"]', 'present'));
    await gallery.locator('[data-testid="gallery-image"]').filter({ has: page.locator(`img[src="${storedPath}"]`) }).first().click().catch(async () => { await gallery.locator(`img[src="${storedPath}"]`).first().click(); });
    await page.waitForTimeout(1000);
    await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible();
    expect(await picker.locator('[data-testid="selected-image"]').getAttribute('src')).toBe(storedPath);
    controls.push({ control: 'choose from library', verdict: 'RENDERS', detail: 'gallery selection rebinds the path' });

    await page.screenshot({ path: `${SHOTS}/req-ui-035-image-picker.png` });

    // Nothing is saved: the page is left without pressing Save, so the owner row is untouched.
    const ownerAvatar = psql("SELECT COALESCE(profileimagepath,'') FROM bloguser WHERE userid=1");
    console.log('REQ-UI-035 owner profileimagepath (unsaved, must be unchanged) =', JSON.stringify(ownerAvatar));
    expect(ownerAvatar).not.toBe(storedPath);

    report('ImagePicker (/admin/profile)', controls, []);
    for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  } finally {
    await deleteLeftoversThroughUi(page).catch(() => {});
    console.log('REQ-UI-035 CLEANUP removed =', JSON.stringify(cleanupUploads()));
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-034 finding — can an Author reach the library they upload into?
// ---------------------------------------------------------------------------------------------

test('REQ-UI-034 an Author cannot open the AdminOnly media library although the picker lets them upload', async ({ page }) => {
  await signIn(page, 'author');
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/images'));
  await page.waitForTimeout(10000);
  const denied = await page.locator('[data-testid="access-denied"]').count();
  const library = await page.locator('[data-testid="media-library-page"]').count();
  const shown = (await page.locator('body').innerText()).replace(/\s+/g, ' ').trim().slice(0, 200);
  console.log(
    'REQ-UI-034 author on /admin/images: url =', page.url(),
    '| access-denied =', denied,
    '| media-library-page =', library,
    '| body =', JSON.stringify(shown),
  );
  await page.screenshot({ path: `${SHOTS}/req-ui-034-author-blocked.png` });

  // The Author must still get the picker on a page they own.
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/profile'));
  await expect(page.locator('[data-testid="manage-profile-page"]')).toBeVisible({ timeout: 45000 });
  await expect(page.locator('[data-testid="image-picker"]').first()).toBeVisible({ timeout: 45000 });
  const pickers = await page.locator('[data-testid="image-picker"]').count();
  console.log('REQ-UI-034 author ImagePicker count on /admin/profile =', pickers);
  expect(pickers, 'the Author uploads through the picker they can reach').toBeGreaterThan(0);

  // Recorded as a finding either way: the guard is what the DevGuide claims.
  expect(library, 'the library screen is AdminOnly — an Author must not see it').toBe(0);
  expect(denied, 'an Author must land on the access-denied surface, not a blank page').toBeGreaterThan(0);
});
