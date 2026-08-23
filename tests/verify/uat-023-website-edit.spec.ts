/**
 * UAT-023, website half — edit a published post's Abstract through the site's own admin and measure
 * how long the public post page takes to show it.
 *
 * This is the path the 2026-08-23c output-cache fix affects. An in-process edit always evicted
 * `ICacheService` correctly (`BlogSvc.UpdatePostAsync` → `ServiceCache.InvalidateContent`), but the
 * rendered page was ALSO held in the untagged one-minute output-cache base policy, which no
 * invalidation could reach — so the corrected text could still be invisible for up to a minute.
 *
 * Operates on a post seeded directly in the database by the surrounding shell run, addressed by the
 * slug in TB_UAT023_SLUG, and leaves the abstract as it found it.
 */
import { test, expect, request } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav } from './_gates';

const ARTIFACTS = 'tests/.artifacts/uat-023';
const SLUG = process.env.TB_UAT023_SLUG ?? '';
const NEW_ABSTRACT = `WEBSITE EDITED ABSTRACT ${Date.now()}`;

/** Fetches the public post page over a fresh, unauthenticated connection. */
async function publicPostHtml(): Promise<string> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const response = await api.get(`/post/${SLUG}`);
    return response.status() === 200 ? await response.text() : '';
  } finally {
    await api.dispose();
  }
}

test('an Abstract corrected in the website admin reaches the public page promptly', async ({ page }) => {
  test.setTimeout(10 * 60 * 1000);
  expect(SLUG, 'TB_UAT023_SLUG must name the seeded post').not.toBe('');

  await login(page, 'admin');
  await nav(page, '/BlogsList');
  await page.waitForTimeout(2500);

  const row = page.locator('tbody tr', { hasText: SLUG.replace(/^uat023-probe-/, 'UAT023 Probe ') }).first();
  const editButton = row.locator('[data-testid="post-edit"]').first();
  await editButton.click({ timeout: 30000 });
  await page.waitForTimeout(4000);

  const excerpt = page
    .locator('[data-testid="post-excerpt-input"] textarea, [data-testid="post-excerpt-input"] input, textarea#post-excerpt')
    .first();
  await excerpt.fill(NEW_ABSTRACT);
  await page.waitForTimeout(500);

  // An ALREADY-PUBLISHED post gets "Save changes" (`save-post`), not "Publish Now"
  // (`publish-post`) — ManagePost.razor gates those on `PageId == 0 || !PageObj.Published`. The
  // save bar also sits below the fold, so scroll it in and wait for it to be enabled
  // (Disabled="@IsSaving") before clicking.
  const publish = page.locator('[data-testid="save-post"], [data-testid="publish-post"]').first();
  await publish.scrollIntoViewIfNeeded({ timeout: 30000 });
  await expect(publish).toBeEnabled({ timeout: 30000 });
  await publish.click({ timeout: 30000 });

  const savedAt = Date.now();
  let visible = false;
  for (;;) {
    visible = (await publicPostHtml()).includes(NEW_ABSTRACT);
    if (visible || Date.now() - savedAt > 3 * 60 * 1000) break;
    await page.waitForTimeout(500);
  }
  const elapsedMs = Date.now() - savedAt;

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(
    `${ARTIFACTS}/website-edit.json`,
    JSON.stringify({ slug: SLUG, newAbstract: NEW_ABSTRACT, visible, elapsedMs }, null, 2));
  console.log(`[UAT-023] website-admin abstract edit visible after ${elapsedMs}ms`);

  expect(visible, 'the corrected abstract must reach the public page').toBe(true);
  expect(elapsedMs, 'a correction must not wait out a cache window').toBeLessThan(15000);
});
