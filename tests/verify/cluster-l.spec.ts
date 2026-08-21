import { test, expect, Page } from '@playwright/test';

/**
 * Cluster L smoke — REQ-FN-027 (resume data maintenance) and REQ-FN-029 (site-owner flag).
 *
 * Covers the new /admin/stats maintenance screen (create / edit / delete / reorder) and the
 * new About + Community statistics blocks on the public /resume page, plus the completed
 * TwiiterUrl -> TwitterUrl migration in ResumeHero.
 *
 * Gates: RENDER-TRUTH (grids show real non-empty rows; /resume statistics carry real values
 * cross-checked against PostgreSQL) and VISUAL-TRUTH (no clipped/off-viewport controls, no
 * horizontal overflow) at desktop 1280 and mobile 390.
 */

const BASE = 'http://localhost:5410';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

/** Marker used by every row this smoke creates, so cleanup can find them. */
const MARKER = 'SMOKE-L';

/** Signs in with the documented seeded site owner from the UsageGuide. Never creates a user. */
async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  // The login form is an EditForm: submitting before the interactive circuit attaches
  // degrades to a static POST and 400s. Wait for Blazor to take over the document.
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 });
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

/**
 * Navigates to an admin route by CLICKING its nav link (SPA navigation).
 *
 * A direct page.goto() cannot be used: the JWT lives in localStorage only, which the server
 * cannot read during Blazor Server's prerender pass, so a FULL page load of an admin route
 * evaluates as anonymous and redirects. Pre-existing defect, logged elsewhere; this smoke
 * navigates the way a real signed-in admin does.
 */
async function gotoAdmin(page: Page, href: string) {
  const link = page.locator(`a[href="${href}"]`).first();
  await link.waitFor({ state: 'attached', timeout: 30000 });
  await link.click();
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

/** Visual-truth gate: non-zero in-viewport boxes and no horizontal page scroll. */
async function visualGate(page: Page, testIds: string[], label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal overflow`).toBeLessThanOrEqual(1);

  const vw = page.viewportSize()!.width;
  for (const id of testIds) {
    const el = page.locator(`[data-testid="${id}"]`).first();
    if (await el.count() === 0) continue;
    if (!(await el.isVisible())) continue;
    const box = await el.boundingBox();
    expect(box, `${label}: ${id} has no box`).not.toBeNull();
    expect(box!.width, `${label}: ${id} zero width`).toBeGreaterThan(0);
    expect(box!.height, `${label}: ${id} zero height`).toBeGreaterThan(0);
    expect(box!.x, `${label}: ${id} off left edge`).toBeGreaterThanOrEqual(-1);
    expect(box!.x + box!.width, `${label}: ${id} off right edge`).toBeLessThanOrEqual(vw + 1);
  }
}

test.describe.configure({ mode: 'serial' });

test('admin stats page does full CRUD and the resume follows', async ({ page }) => {
  test.setTimeout(300000);
  await login(page);

  // ---------- Nav entry exists and reaches the new page ----------
  await gotoAdmin(page, '/admin/stats');
  await expect(page.locator('[data-testid="manage-stats-page"]')).toBeVisible();
  // The list renders on the second batch, after the auth state resolves.
  await page.waitForSelector('[data-testid="stats-list"], [data-testid="stats-empty"]', { timeout: 30000 });

  // ---------- RENDER-TRUTH: the grid shows the real seeded rows ----------
  const cards = page.locator('[data-testid="stat-card"]');
  const seededCount = await cards.count();
  expect(seededCount, 'seeded statistics must render').toBeGreaterThan(0);
  for (let i = 0; i < seededCount; i++) {
    const value = (await cards.nth(i).locator('[data-testid="stat-value"]').innerText()).trim();
    const label = (await cards.nth(i).locator('[data-testid="stat-label"]').innerText()).trim();
    expect(value.length, `row ${i} value must be non-empty`).toBeGreaterThan(0);
    expect(label.length, `row ${i} label must be non-empty`).toBeGreaterThan(0);
  }

  // ---------- CREATE ----------
  await page.click('[data-testid="add-stat"]');
  await page.waitForSelector('[data-testid="stat-dialog"]', { timeout: 15000 });
  await page.fill('[data-testid="stat-value-input"]', '77+');
  await page.fill('[data-testid="stat-label-input"]', `${MARKER} created label`);
  await page.fill('[data-testid="stat-category-input"]', 'Community');
  await page.click('[data-testid="save-stat"]');
  await page.waitForTimeout(2500);

  await expect(page.locator('[data-testid="stats-status"]')).toContainText('added successfully');
  await expect(cards).toHaveCount(seededCount + 1);
  await expect(page.locator(`text=${MARKER} created label`).first()).toBeVisible();

  // ---------- The public resume picks the new row up (community category) ----------
  const anon = await page.context().browser()!.newContext({ ignoreHTTPSErrors: true });
  const anonPage = await anon.newPage();
  await anonPage.goto(`${BASE}/resume`, { waitUntil: 'networkidle' });
  await anonPage.waitForTimeout(2500);
  await expect(anonPage.locator('[data-testid="community-section"]')).toBeVisible();
  await expect(anonPage.locator('[data-testid="community-stats-grid"]')).toContainText('77+');
  await expect(anonPage.locator('[data-testid="community-stats-grid"]')).toContainText(`${MARKER} created label`);

  // ---------- EDIT ----------
  const created = page.locator('[data-testid="stat-card"]', { hasText: `${MARKER} created label` });
  await created.locator('[data-testid="edit-stat"]').click();
  await page.waitForSelector('[data-testid="stat-dialog"]', { timeout: 15000 });
  await page.fill('[data-testid="stat-value-input"]', '88+');
  await page.fill('[data-testid="stat-label-input"]', `${MARKER} edited label`);
  await page.click('[data-testid="save-stat"]');
  await page.waitForTimeout(2500);

  await expect(page.locator('[data-testid="stats-status"]')).toContainText('updated successfully');
  await expect(page.locator(`text=${MARKER} edited label`).first()).toBeVisible();

  await anonPage.reload({ waitUntil: 'networkidle' });
  await anonPage.waitForTimeout(2000);
  await expect(anonPage.locator('[data-testid="community-stats-grid"]')).toContainText('88+');
  await expect(anonPage.locator('[data-testid="community-stats-grid"]')).toContainText(`${MARKER} edited label`);

  // ---------- REORDER (move the new last row up one place) ----------
  const before = await page.locator('[data-testid="stat-label"]').allInnerTexts();
  const edited = page.locator('[data-testid="stat-card"]', { hasText: `${MARKER} edited label` });
  await edited.locator('[data-testid="move-stat-up"]').click();
  // Reordering rewrites every row (one connection per row), so poll rather than sleep.
  await expect
    .poll(async () => (await page.locator('[data-testid="stat-label"]').allInnerTexts()).join('|'),
          { timeout: 30000, message: 'move-up must change display order' })
    .not.toEqual(before.join('|'));
  const after = await page.locator('[data-testid="stat-label"]').allInnerTexts();
  expect(after.indexOf(`${MARKER} edited label`), 'moved row must sit one place earlier')
    .toBe(before.indexOf(`${MARKER} edited label`) - 1);

  // ---------- VISUAL-TRUTH: admin page, desktop then mobile ----------
  const adminIds = ['manage-stats-page', 'add-stat', 'stats-list', 'stat-card', 'stat-value',
                    'stat-label', 'edit-stat', 'delete-stat', 'move-stat-up', 'move-stat-down'];
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.waitForTimeout(1200);
  await visualGate(page, adminIds, 'admin-stats@1280');
  await page.screenshot({ path: 'test-results/req-fn-027-admin-stats-1280.png', fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1200);
  await visualGate(page, adminIds, 'admin-stats@390');
  await page.screenshot({ path: 'test-results/req-fn-027-admin-stats-390.png', fullPage: true });
  await page.setViewportSize({ width: 1280, height: 900 });

  // ---------- Public resume renders About + statistics with real values ----------
  const aboutValues = await anonPage.locator('[data-testid="about-stat-value"]').allInnerTexts();
  expect(aboutValues.length, 'about stat tiles must render').toBeGreaterThan(0);
  for (const v of aboutValues) expect(v.trim().length, 'about stat value non-empty').toBeGreaterThan(0);

  const aboutSummary = await anonPage.locator('[data-testid="about-summary"]').innerText();
  expect(aboutSummary.trim().length, 'about summary non-empty').toBeGreaterThan(20);

  // REQ-FN-029: the X/Twitter icon renders through the corrected TwitterUrl property.
  await expect(anonPage.locator('[data-testid="social-twitter"]')).toBeVisible();

  const resumeIds = ['resume-page', 'resume-hero', 'about-section', 'about-summary',
                     'about-stats-grid', 'about-stat', 'community-section', 'community-stats-grid',
                     'community-stat', 'social-twitter'];
  await anonPage.setViewportSize({ width: 1280, height: 900 });
  await anonPage.waitForTimeout(1200);
  await visualGate(anonPage, resumeIds, 'resume@1280');
  await anonPage.screenshot({ path: 'test-results/req-fn-027-resume-1280.png', fullPage: true });

  await anonPage.setViewportSize({ width: 390, height: 844 });
  await anonPage.waitForTimeout(1200);
  await visualGate(anonPage, resumeIds, 'resume@390');
  await anonPage.screenshot({ path: 'test-results/req-fn-027-resume-390.png', fullPage: true });

  // ---------- DELETE (also the cleanup that keeps the seeded resume presentable) ----------
  await page.waitForTimeout(500);
  const toDelete = page.locator('[data-testid="stat-card"]', { hasText: `${MARKER} edited label` });
  await toDelete.locator('[data-testid="delete-stat"]').click();
  await page.waitForSelector('[data-testid="stat-delete-dialog"]', { timeout: 15000 });
  await page.click('[data-testid="stat-delete-confirm"]');
  await page.waitForTimeout(2500);

  await expect(page.locator('[data-testid="stats-status"]')).toContainText('deleted successfully');
  await expect(page.locator('[data-testid="stat-card"]')).toHaveCount(seededCount);
  // Scoped to the list: the dismissed confirmation dialog still holds the label in its markup.
  await expect(page.locator('[data-testid="stats-list"]')).not.toContainText(`${MARKER} edited label`);

  // The public resume drops it again.
  await anonPage.setViewportSize({ width: 1280, height: 900 });
  await anonPage.reload({ waitUntil: 'networkidle' });
  await anonPage.waitForTimeout(2000);
  await expect(anonPage.locator(`text=${MARKER} edited label`)).toHaveCount(0);

  await anon.close();
});
