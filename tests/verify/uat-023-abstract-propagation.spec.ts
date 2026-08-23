/**
 * UAT-023 re-check — "I changed a published post's Abstract and the site still shows the old one."
 *
 * The row was closed 2026-08-23 by adding an authenticated `POST /api/admin/cache/refresh` that
 * BlogApp calls after a publish-affecting save, on the diagnosis that the stale read came from the
 * ten-minute `MemoryCacheService` entry a cross-process write cannot invalidate.
 *
 * That diagnosis was right about the cross-process case but incomplete: the host ALSO carried an
 * untagged one-minute output-cache base policy, so even an edit made through the WEBSITE'S OWN
 * admin — which does invalidate `ICacheService` correctly — kept serving the previous minute's
 * HTML. The refresh endpoint could not have helped there either: it evicts by tag, and the base
 * policy had no tag. That policy was removed on 2026-08-23c.
 *
 * This spec exercises the owner's flow through the website: publish a post, read it as an anonymous
 * visitor, change the Abstract, and MEASURE how long the public page takes to show the new text.
 * The cross-process (BlogApp) half is checked separately against the refresh endpoint.
 */
import { test, expect, request } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav } from './_gates';

const ARTIFACTS = 'tests/.artifacts/uat-023';
const STAMP = Date.now();
const TITLE = `UAT023 Abstract Probe ${STAMP}`;
const SLUG = `uat023-abstract-probe-${STAMP}`;
const ABSTRACT_ONE = `ORIGINAL ABSTRACT ${STAMP}`;
const ABSTRACT_TWO = `CORRECTED ABSTRACT ${STAMP}`;

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

test('a corrected Abstract reaches the public post page promptly', async ({ page }) => {
  test.setTimeout(10 * 60 * 1000);

  const evidence: Record<string, unknown> = { slug: SLUG };

  await login(page, 'admin');

  // ---- publish a post carrying the first abstract -------------------------------------------
  await nav(page, '/ManagePost');
  await page.fill('[data-testid="post-title-input"] input, [data-testid="post-title-input"]', TITLE);
  await page.fill('[data-testid="post-slug-input"] input, [data-testid="post-slug-input"]', SLUG);
  await page.fill('[data-testid="post-excerpt-input"] textarea, [data-testid="post-excerpt-input"] input, [data-testid="post-excerpt-input"]', ABSTRACT_ONE);
  await page.click('[data-testid="category-select"]');
  await page.waitForTimeout(600);
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(400);
  await page.click('[data-testid="publish-post"]');
  await page.waitForTimeout(4000);

  const firstHtml = await publicPostHtml();
  evidence.originalVisible = firstHtml.includes(ABSTRACT_ONE);
  expect(evidence.originalVisible, 'the published post must be publicly visible first').toBe(true);

  // ---- correct the abstract, exactly as the owner did ---------------------------------------
  await nav(page, '/BlogsList');
  await page.waitForTimeout(2000);
  const row = page.locator('tbody tr', { hasText: TITLE }).first();
  await row.locator('[data-testid="post-edit"]').first().click();
  await page.waitForTimeout(3500);
  const excerpt = page.locator('[data-testid="post-excerpt-input"] textarea, [data-testid="post-excerpt-input"] input, [data-testid="post-excerpt-input"]').first();
  await excerpt.fill(ABSTRACT_TWO);
  await page.waitForTimeout(500);
  await page.click('[data-testid="publish-post"]');

  const savedAt = Date.now();
  let corrected = false;
  for (;;) {
    corrected = (await publicPostHtml()).includes(ABSTRACT_TWO);
    if (corrected || Date.now() - savedAt > 5 * 60 * 1000) break;
    await page.waitForTimeout(500);
  }
  evidence.msUntilCorrectionVisible = Date.now() - savedAt;
  evidence.correctionVisible = corrected;

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(`${ARTIFACTS}/website-edit.json`, JSON.stringify(evidence, null, 2));
  console.log(`[UAT-023] corrected abstract visible after ${evidence.msUntilCorrectionVisible}ms`);

  expect(corrected, 'the corrected abstract must reach the public page').toBe(true);
  expect(
    evidence.msUntilCorrectionVisible as number,
    'a correction must not wait out a cache window',
  ).toBeLessThan(15000);
});
