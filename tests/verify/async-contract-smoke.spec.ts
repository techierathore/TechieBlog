import { test, expect, Page } from '@playwright/test';

/**
 * REQ-NFR-026 stage 1 smoke — the async data-access contract.
 *
 * CategoryRepo is the converted reference repository, so /admin/categories is the one screen whose
 * data now travels the whole async path: CategoriesList.OnInitializedAsync ->
 * CategorySvc.GetAllWithCountsAsync -> CategoryRepo.GetAllWithCountsAsync -> async Dapper over a
 * connection opened with OpenAsync. An async refactor breaks things that compile perfectly, so this
 * asserts the rows are really present and really populated, cross-checked against values read
 * straight out of PostgreSQL, and that the page still looks right at desktop and mobile widths.
 *
 * Gates: RENDER-TRUTH (rows present, cells non-empty, values match the database) and VISUAL-TRUTH
 * (no zero-size or off-viewport elements, no horizontal overflow) at 1280 and 390.
 */

const BASE = 'http://localhost:5430';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

/** Ground truth read directly from PostgreSQL, alphabetical by name, with published post counts. */
const EXPECTED = [
  { name: 'Career', slug: 'career', posts: '1' },
  { name: 'DevOps', slug: 'devops', posts: '2' },
  { name: 'Programming', slug: 'programming', posts: '3' },
  { name: 'Technology', slug: 'technology', posts: '1' },
  { name: 'Web Development', slug: 'web-development', posts: '4' },
];

/** Signs in with the documented seeded site owner from the UsageGuide. Never creates a user. */
async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  // The login form is an EditForm: submitting before the interactive circuit attaches degrades to
  // a static POST and 400s. Wait for Blazor to take over the document.
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
 * A direct page.goto() cannot be used: the JWT lives in localStorage only, which the server cannot
 * read during Blazor Server's prerender pass, so a FULL page load of an admin route evaluates as
 * anonymous and redirects. Pre-existing defect, unrelated to REQ-NFR-026; this smoke navigates the
 * way a real signed-in admin does.
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

test('categories render through the async repository path', async ({ page }) => {
  test.setTimeout(300000);

  const consoleErrors: string[] = [];
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });

  await login(page);
  await gotoAdmin(page, '/admin/categories');

  await page.waitForSelector('[data-testid="categories-grid"]', { timeout: 30000 });

  // ---------- RENDER-TRUTH ----------
  const names = await page.locator('[data-testid="category-row-name"]').allTextContents();
  const slugs = await page.locator('[data-testid="category-row-slug"]').allTextContents();
  const counts = await page.locator('[data-testid="category-row-postcount"]').allTextContents();

  expect(names.length, 'row count must match the database').toBe(EXPECTED.length);
  expect(slugs.length).toBe(EXPECTED.length);
  expect(counts.length).toBe(EXPECTED.length);

  for (let i = 0; i < EXPECTED.length; i++) {
    expect(names[i].trim(), `row ${i} name`).toBe(EXPECTED[i].name);
    expect(slugs[i].trim(), `row ${i} slug`).toBe(`/${EXPECTED[i].slug}`);
    expect(counts[i].trim(), `row ${i} post count`).toBe(EXPECTED[i].posts);
  }

  const countLabel = await page.locator('[data-testid="categories-count"]').textContent();
  expect(countLabel?.trim()).toBe(`${EXPECTED.length} categories`);

  // The async path must not have surfaced the page's failure or empty branches.
  expect(await page.locator('[data-testid="categories-status-message"]').count()).toBe(0);
  expect(await page.locator('[data-testid="categories-empty"]').count()).toBe(0);
  expect(await page.locator('[data-testid="categories-loading"]').count()).toBe(0);

  // ---------- VISUAL-TRUTH ----------
  const ids = ['categories-grid', 'categories-count', 'categories-search', 'new-category'];

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.waitForTimeout(800);
  await visualGate(page, ids, '1280');
  await page.screenshot({ path: 'tests/verify/screenshots/async-categories-1280.png', fullPage: true });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(800);
  await visualGate(page, ids, '390');
  await page.screenshot({ path: 'tests/verify/screenshots/async-categories-390.png', fullPage: true });

  const noise = consoleErrors.filter(e => !e.includes('favicon'));
  expect(noise, `console errors: ${noise.join(' || ')}`).toHaveLength(0);
});
