import { test, expect, Page, Browser } from '@playwright/test';
import * as fs from 'fs';
import { BASE } from './_gates';
import { signIn, settle } from './vall-resume-helpers';

/**
 * Build-phase 2026-11-21 — ratings the owner can see, weekly numbers (BRD-106…111).
 *
 *   REQ-UI-066  dashboard Quick Actions → Analytics
 *   REQ-UI-067  Analytics Rating cell → ratings dialog (masked rater)
 *   REQ-UI-068  Posts list Rating cell → the same dialog
 *   REQ-FN-063  confirmed-only counts; no rater email on a public page
 *   REQ-UI-070  Copy numbers → plain text on the clipboard
 *   REQ-UI-071  Where readers came from (referring site only)
 *
 * Black box against the running website. It creates nothing in the database except one post view
 * (REQ-UI-071 opens a post with a Referer header, exactly as a reader arriving from a link does).
 * The base address is the booted host (_gates.ts BASE); no setting is read from an environment
 * variable here. Sign-in uses the UsageGuide test users through the shared helper.
 */
const OUT = 'tests/.artifacts/verify/req-ui-066-071';
const MASKED = /^[a-z0-9]•••@[a-z0-9.-]+$/;
const FULL_EMAIL = /[A-Za-z0-9._%+-]{2,}@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/;

test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

async function openAnalytics(page: Page) {
  await page.goto(`${BASE}/admin/analytics`, { waitUntil: 'domcontentloaded' });
  await settle(page);
  await page.waitForSelector('[data-testid="analytics-popular-card"]', { timeout: 60000 });
  await page.waitForFunction(
    () => !document.querySelector('[data-testid="analytics-popular-loading"], [data-testid="analytics-referrer-loading"]'),
    undefined, { timeout: 60000 });
  await page.waitForTimeout(1500);
}

async function noSidewaysScroll(page: Page) {
  return page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1);
}

async function shoot(page: Page, name: string) {
  for (const width of [1280, 390]) {
    await page.setViewportSize({ width, height: 900 });
    await page.waitForTimeout(600);
    expect(await noSidewaysScroll(page), `${name} @${width}: sideways scroll`).toBe(true);
    await page.screenshot({ path: `${OUT}/${name}-${width}.png`, fullPage: true });
  }
  await page.setViewportSize({ width: 1280, height: 900 });
}

async function firstRatedButton(page: Page, testId: string) {
  const cells = page.locator(`[data-testid="${testId}"]`);
  const count = await cells.count();
  for (let i = 0; i < count; i++) {
    const text = (await cells.nth(i).innerText()).trim();
    if (/★\s*\(\d+\)/.test(text)) return cells.nth(i);
  }
  return null;
}

async function assertDialogMasked(page: Page) {
  const dialog = page.locator('[data-testid="ratings-dialog"]');
  await expect(dialog).toBeVisible({ timeout: 20000 });
  await page.waitForFunction(() => !document.querySelector('[data-testid="ratings-dialog-loading"]'), undefined, { timeout: 20000 });
  const rows = dialog.locator('[data-testid="ratings-row"]');
  expect(await rows.count(), 'ratings rows').toBeGreaterThan(0);
  for (const rater of await dialog.locator('[data-testid="ratings-row-rater"]').allInnerTexts())
    expect(rater.trim(), 'rater is masked').toMatch(MASKED);
  for (const status of await dialog.locator('[data-testid="ratings-row-status"]').allInnerTexts())
    expect(['Confirmed', 'Not yet confirmed']).toContain(status.trim());
  for (const stars of await dialog.locator('[data-testid="ratings-row-stars"]').allInnerTexts())
    expect(stars.trim()).toMatch(/^[★☆]{5}$/);
  for (const date of await dialog.locator('[data-testid="ratings-row-date"]').allInnerTexts())
    expect(date.trim()).toMatch(/^[A-Z][a-z]{2} \d{1,2}, \d{4}$/);
  const html = await dialog.innerHTML();
  expect(FULL_EMAIL.test(html), 'dialog HTML holds no full email address').toBe(false);
  return { rows: await rows.count(), summary: (await dialog.locator('[data-testid="ratings-dialog-summary"]').innerText()).trim() };
}

test('REQ-UI-066 dashboard Quick Actions offers Analytics and it opens /admin/analytics', async ({ page }) => {
  test.setTimeout(300000);
  await signIn(page, 'editor');
  await page.goto(`${BASE}/admin`, { waitUntil: 'domcontentloaded' });
  await settle(page);
  const button = page.locator('[data-testid="quick-actions"] [data-testid="action-analytics"]');
  await expect(button).toBeVisible({ timeout: 60000 });
  // A signed-in browser state for tf-verify-screens.sh --storage-state, so no password is ever
  // passed on a command line. Git-ignored with the rest of tests/.artifacts/.
  await page.context().storageState({ path: 'tests/.artifacts/verify/editor-state.json' });
  await expect(button).toContainText('Analytics');
  await shoot(page, 'dashboard');
  await button.click();
  await page.waitForURL((u) => u.pathname.toLowerCase() === '/admin/analytics', { timeout: 60000 });
  await expect(page.locator('h1', { hasText: 'Analytics' })).toBeVisible({ timeout: 60000 });
});

test('REQ-UI-044 Analytics counts confirmed ratings only, matching the post page (owner miss 2026-11-21)', async ({ page }) => {
  test.setTimeout(300000);
  // The defect: Analytics averaged a rating whose email was never confirmed, so the owner saw a
  // 5-star rating there and nowhere else. Every Rating cell must now equal the confirmed rows its
  // own ratings dialog lists, and the post page must agree.
  await signIn(page, 'editor');
  await openAnalytics(page);
  const cell = await firstRatedButton(page, 'popular-row-rating');
  expect(cell, 'a rated post is in range').not.toBeNull();
  const label = (await cell!.innerText()).trim();
  const title = (await cell!.getAttribute('aria-label'))!.replace(/^Show ratings for /, '');
  await cell!.click();
  await assertDialogMasked(page);
  const rows = page.locator('[data-testid="ratings-row"]');
  const confirmedStars: number[] = [];
  let pending = 0;
  for (let i = 0; i < await rows.count(); i++) {
    const status = (await rows.nth(i).locator('[data-testid="ratings-row-status"]').innerText()).trim();
    const stars = Number(await rows.nth(i).locator('[data-testid="ratings-row-stars"]').getAttribute('data-stars'));
    if (status === 'Confirmed') confirmedStars.push(stars); else pending++;
  }
  expect(pending, 'the seeded post carries an unconfirmed rating the old query counted').toBeGreaterThan(0);
  const average = confirmedStars.reduce((a, b) => a + b, 0) / confirmedStars.length;
  expect(label).toBe(`${average.toFixed(1)} ★ (${confirmedStars.length})`);

  const anon = await page.context().browser()!.newContext();
  const pub = await anon.newPage();
  await pub.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  const href = await pub.locator('a[href^="/post/"]', { hasText: title }).first().getAttribute('href');
  await pub.goto(`${BASE}${href}`, { waitUntil: 'domcontentloaded' });
  await settle(pub);
  await expect(pub.locator('[data-testid="post-rating-count"]')).toContainText(`${confirmedStars.length} rating`, { timeout: 30000 });
  await expect(pub.locator('[data-testid="post-rating-average"]')).toContainText(average.toFixed(1));
  await anon.close();
});

test('REQ-UI-067 Analytics Rating cell shows confirmed average and count and opens a masked ratings dialog', async ({ page }) => {
  test.setTimeout(300000);
  await signIn(page, 'editor');
  await openAnalytics(page);
  const cell = await firstRatedButton(page, 'popular-row-rating');
  expect(cell, 'a popular post with a confirmed rating is in the range (seed: tests/verify/seed/34-analytics-dashboard.sh)').not.toBeNull();
  const label = (await cell!.innerText()).trim();
  expect(label).toMatch(/^\d\.\d ★ \(\d+\)$/);
  await cell!.click();
  const result = await assertDialogMasked(page);
  expect(result.summary).toMatch(/confirmed rating/);
  await shoot(page, 'analytics-ratings-dialog');
  await page.click('[data-testid="ratings-dialog-close"]');
  await expect(page.locator('[data-testid="ratings-dialog"]')).toBeHidden({ timeout: 10000 });
});

test('REQ-UI-068 Posts list Rating cell shows confirmed average and count and opens the ratings dialog', async ({ page }) => {
  test.setTimeout(300000);
  await signIn(page, 'admin');
  await page.goto(`${BASE}/BlogsList`, { waitUntil: 'domcontentloaded' });
  await settle(page);
  await page.waitForSelector('[data-testid="posts-grid"]', { timeout: 60000 });
  await page.waitForTimeout(4000);
  const cells = page.locator('[data-testid="post-row-rating"]');
  expect(await cells.count(), 'every post row has a Rating cell').toBeGreaterThan(0);
  for (const text of await cells.allInnerTexts())
    expect(text.trim()).toMatch(/^(No ratings|\d\.\d ★ \(\d+\))$/);
  const rated = await firstRatedButton(page, 'post-row-rating');
  expect(rated, 'a listed post has a confirmed rating').not.toBeNull();
  await rated!.click();
  await assertDialogMasked(page);
  await shoot(page, 'posts-list-ratings-dialog');
  await page.click('[data-testid="ratings-dialog-close"]');

  // The Author role reaches the same cell and dialog on its own posts.
  await page.context().clearCookies();
  await page.evaluate(() => localStorage.clear());
  await signIn(page, 'author');
  await page.goto(`${BASE}/BlogsList`, { waitUntil: 'domcontentloaded' });
  await settle(page);
  // An Author with no posts of their own gets the empty state, which has no Rating cell to click.
  await page.waitForSelector('[data-testid="posts-grid"], [data-testid="posts-empty"]', { timeout: 60000 });
  await page.waitForTimeout(4000);
  const authorCells = page.locator('[data-testid="post-row-rating"]');
  if (await authorCells.count() > 0) {
    await authorCells.first().click();
    await expect(page.locator('[data-testid="ratings-dialog"]')).toBeVisible({ timeout: 20000 });
  }
});

test('REQ-FN-063 a post with one confirmed and one unconfirmed rating shows a count of one and no rater email', async ({ page }) => {
  test.setTimeout(300000);
  // The rated post: the one the Analytics dialog reports a pending rating for.
  await signIn(page, 'editor');
  await openAnalytics(page);
  const cell = await firstRatedButton(page, 'popular-row-rating');
  expect(cell).not.toBeNull();
  const label = (await cell!.innerText()).trim();
  const title = (await cell!.getAttribute('aria-label'))!.replace(/^Show ratings for /, '');
  await cell!.click();
  const dialog = await assertDialogMasked(page);
  const statuses = await page.locator('[data-testid="ratings-row-status"]').allInnerTexts();
  const confirmed = statuses.filter((s) => s.trim() === 'Confirmed').length;
  expect(statuses.some((s) => s.trim() === 'Not yet confirmed'), 'the seeded post carries an unconfirmed rating').toBe(true);
  expect(label).toContain(`(${confirmed})`);
  expect(dialog.summary).toContain(`from ${confirmed} confirmed rating`);

  // The public page of that post, read anonymously.
  const anon = await page.context().browser()!.newContext();
  const pub = await anon.newPage();
  await pub.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  const href = await pub.locator('a[href^="/post/"]', { hasText: title }).first().getAttribute('href');
  expect(href, `home page links "${title}"`).not.toBeNull();
  await pub.goto(`${BASE}${href}`, { waitUntil: 'domcontentloaded' });
  await settle(pub);
  await expect(pub.locator('[data-testid="post-rating-count"]')).toContainText(`${confirmed} rating`, { timeout: 30000 });
  const body = await pub.content();
  expect(body.includes('•••@'), 'no masked rater on a public page').toBe(false);
  expect(/@example\.test/i.test(body), 'no rater address on a public page').toBe(false);
  await anon.close();
});

test('REQ-UI-070 Copy numbers puts views, top posts and referring sites on the clipboard as plain text', async ({ page, context }) => {
  test.setTimeout(300000);
  await context.grantPermissions(['clipboard-read', 'clipboard-write'], { origin: BASE });
  await signIn(page, 'editor');
  await openAnalytics(page);
  const today = (await page.locator('[data-testid="analytics-views-today"]').innerText()).trim();
  const seven = (await page.locator('[data-testid="analytics-views-7"]').innerText()).trim();
  const thirty = (await page.locator('[data-testid="analytics-views-30"]').innerText()).trim();
  const titles = await page.locator('[data-testid="popular-row-title"]').allInnerTexts();
  const sites = await page.locator('[data-testid="referrer-row-name"]').allInnerTexts();

  await page.click('[data-testid="analytics-copy-numbers"]');
  await page.waitForTimeout(1500);
  const text: string = await page.evaluate(() => navigator.clipboard.readText());
  fs.writeFileSync(`${OUT}/copied-numbers.txt`, text);

  expect(await page.locator('[data-testid="analytics-copy-fallback"]').count(), 'clipboard accepted the text').toBe(0);
  expect(text).toContain(`- Today (UTC): ${today}`);
  expect(text).toContain(`- Last 7 days: ${seven}`);
  expect(text).toContain(`- Last 30 days: ${thirty}`);
  expect(text).toContain('Ten most read posts');
  for (const t of titles.slice(0, 10)) expect(text).toContain(t.trim());
  expect(text).toMatch(/ views?, (rating \d\.\d from \d+ ratings?|no ratings yet)/);
  expect(text).toContain('Where readers came from');
  for (const s of sites) expect(text).toContain(`- ${s.trim()}:`);
  expect(/<[a-z][^>]*>/i.test(text), 'plain text, no markup').toBe(false);
  await shoot(page, 'analytics-copied');
});

test('REQ-UI-071 a reader arriving from another site is listed by that site name under Where readers came from', async ({ page, request }) => {
  test.setTimeout(300000);
  const home = await request.get(`${BASE}/`);
  const slug = /href="(\/post\/[^"]+)"/.exec(await home.text())?.[1];
  expect(slug, 'a published post is linked from the home page').toBeTruthy();
  const site = 'news.example.org';
  const visit = await request.get(`${BASE}${slug}`, {
    headers: { Referer: `https://www.${site}/thread/42?utm=private`, 'User-Agent': `Req071Reader/${Date.now()}` },
  });
  expect(visit.status()).toBe(200);
  await page.waitForTimeout(3000); // the view is written by the background queue

  await signIn(page, 'editor');
  await openAnalytics(page);
  const card = page.locator('[data-testid="analytics-referrer-card"]');
  await expect(card).toBeVisible();
  const names = (await card.locator('[data-testid="referrer-row-name"]').allInnerTexts()).map((n) => n.trim());
  expect(names, 'the referring site is listed by name only').toContain(site);
  expect(names.some((n) => n.includes('/') || n.includes('?')), 'no path or query is shown').toBe(false);
  const views = await card.locator('[data-testid="referrer-row-views"]').allInnerTexts();
  expect(Number(views[names.indexOf(site)].replace(/,/g, ''))).toBeGreaterThan(0);
  await card.scrollIntoViewIfNeeded();
  await shoot(page, 'analytics-referrers');
});
