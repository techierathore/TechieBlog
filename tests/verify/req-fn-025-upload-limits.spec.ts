import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import { login, nav } from './_gates';

/**
 * REQ-FN-025 acceptance — the upload surface advertises ONE size limit, and it is the real one.
 *
 * The 2026-08-11 verify demoted this row because the dialog stated two contradictory limits for the
 * same upload: the category caption read `Max 2MB` while the dropzone advertised `Max size: 10 MB`
 * — so an admin dropping a 5 MB avatar was accepted client-side and then rejected by the service.
 * The fix moved the limits into `BlogModels.ImageCategoryRules` and derives caption, `accept`
 * filter and the dropzone's `MaxFileSize` from that one rule.
 *
 * This asserts the acceptance directly: whatever size the page states for the selected category,
 * it must appear exactly once — no second, larger number anywhere on the upload surface.
 */

const ARTIFACTS = 'tests/.artifacts/req-fn-025';

/** The single source of truth: source/BlogModel/Common/ImageCategoryRules.cs. */
const EXPECTED_DEFAULT_LIMITS = ['2 MB', '500 KB', '200 KB', '5 MB', '10 MB'];

test('REQ-FN-025 — the upload surface states one size limit, matching ImageCategoryRules', async ({ page }) => {
  await login(page, 'admin');
  await nav(page, '/admin/images');
  await expect(page.locator('body')).toContainText(/image/i, { timeout: 45000 });

  // The limits live INSIDE the upload dialog. Asserting against the closed page would find no
  // size figures at all and pass vacuously — which is precisely the shape of a false clean run.
  await page.click('[data-testid="upload-image"]');
  const dialog = page.locator('[data-testid="image-upload-dialog"]');
  await expect(dialog, 'the upload dialog should open').toBeVisible({ timeout: 30000 });
  await expect(page.locator('[data-testid="upload-category-constraints"]'),
    'the category caption should render').toBeVisible({ timeout: 30000 });
  await expect(page.locator('[data-testid="upload-dropzone"]'),
    'the dropzone should render').toBeVisible({ timeout: 30000 });

  const surface = (await dialog.innerText()) ?? '';

  // Every size figure the upload surface shows, normalised.
  const sizes = Array.from(surface.matchAll(/(\d+(?:\.\d+)?)\s*(MB|KB)/gi))
    .map((m) => `${m[1]} ${m[2].toUpperCase()}`);
  const distinct = Array.from(new Set(sizes));

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(`${ARTIFACTS}/limits.json`,
    JSON.stringify({ sizes, distinct }, null, 2));
  await page.screenshot({ path: `${ARTIFACTS}/images-upload-surface.png`, fullPage: true });

  // The defect was TWO different limits visible for one upload. One distinct figure is the fix;
  // zero would mean the caption stopped rendering at all, which is also a failure.
  // Exactly one — not "at most one". Zero would mean the caption stopped rendering, and passing on
  // zero is how a check like this goes quietly vacuous.
  expect(distinct.length, `upload surface should state exactly one size limit, saw: ${distinct.join(' vs ') || 'none'}`)
    .toBe(1);
  expect(EXPECTED_DEFAULT_LIMITS, `stated limit ${distinct[0]} is not one declared in ImageCategoryRules`)
    .toContain(distinct[0]);
});
