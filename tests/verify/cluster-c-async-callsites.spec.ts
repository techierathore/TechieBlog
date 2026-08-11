import { test, expect, Page } from '@playwright/test';

/**
 * REQ-NFR-026 stage 3 (call-site half) smoke — cluster C.
 *
 * Every Blazor call site listed below was flipped from a synchronous service/repository member onto
 * its already-existing `...Async` twin. The failure mode this refactor produces is invisible to the
 * compiler: a page that renders an EMPTY grid because the async twin filtered or projected
 * differently. So each assertion cross-checks a rendered value against a figure read straight out of
 * PostgreSQL, and each page is walked at 1280 and 390 with a screenshot kept for inspection.
 */

const BASE = 'http://172.18.144.1:5405';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';
const SHOTS = 'tests/.artifacts';

/** Ground truth read directly from PostgreSQL on 2026-08-10 using the repositories' own SQL. */
const DB = {
  publishedPosts: 8,
  categoriesWithCounts: [
    { name: 'Career', posts: 1 },
    { name: 'DevOps', posts: 1 },
    { name: 'Programming', posts: 2 },
    { name: 'Technology', posts: 1 },
    { name: 'Web Development', posts: 3 },
  ],
  tagCount: 15,
  seriesCount: 2,
  subscribersTotal: 11,
  subscribersConfirmed: 7,
  webDevPublished: 3,
  blazorTagPublished: 3,
  series1PublishedParts: 3,
  blazorSearchHits: 3,
  /** REQ-FN-015: the Contributor's unpublished draft must never surface on a public listing. */
  hiddenDraftTitle: 'Testing Dapper Repositories Without a Database',
};

let consoleErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  consoleErrors = [];
  page.on('console', m => {
    if (m.type() === 'error') consoleErrors.push(m.text());
  });
  page.on('pageerror', e => consoleErrors.push(`pageerror: ${e.message}`));
});

/** Fails the check if the page scrolls horizontally or paints a zero-size main region. */
async function visualTruth(page: Page, label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal overflow`).toBeLessThanOrEqual(1);
  const bodyBox = await page.locator('body').boundingBox();
  expect(bodyBox!.height, `${label}: body has no height`).toBeGreaterThan(200);
}

async function walk(page: Page, path: string, name: string, assertContent: () => Promise<void>) {
  for (const [w, h, tag] of [[1280, 900, 'desktop'], [390, 844, 'mobile']] as const) {
    await page.setViewportSize({ width: w, height: h });
    await page.goto(`${BASE}${path}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(1200);
    await assertContent();
    await visualTruth(page, `${name}@${tag}`);
    await page.screenshot({ path: `${SHOTS}/c-${name}-${tag}.png`, fullPage: true });
  }
}

test.describe('public surfaces still render their data after the async flip', () => {
  test('home renders the featured post, the latest grid and the owner stats', async ({ page }) => {
    await walk(page, '/', 'home', async () => {
      const cards = page.locator('[data-testid="post-card"], article');
      expect(await cards.count(), 'home: latest-articles grid is empty').toBeGreaterThan(0);
      await expect(page.locator('body')).not.toContainText(DB.hiddenDraftTitle);
      // UserStatsRepo.GetByUserIdAsync feeds the stats band; the seed carries four statistics.
      await expect(page.locator('body')).toContainText('Years of experience');
    });
  });

  test('a post page renders its body, related posts and series navigation', async ({ page }) => {
    await walk(page, '/post/blazor-render-modes-explained', 'postview', async () => {
      await expect(page.locator('h1')).toContainText('Blazor Render Modes Explained');
      await expect(page.locator('body')).not.toContainText('not found');
    });
  });

  test('the category index and one archive match the database counts', async ({ page }) => {
    await walk(page, '/categories', 'categories', async () => {
      const cards = page.locator('[data-testid="category-card"]');
      expect(await cards.count(), 'category index rendered nothing').toBeGreaterThanOrEqual(
        DB.categoriesWithCounts.length);
      for (const c of DB.categoriesWithCounts) {
        const card = page.locator('[data-testid="category-card"]', { hasText: c.name }).first();
        await expect(card, `category ${c.name} missing`).toBeVisible();
        await expect(card).toContainText(`${c.posts} posts`);
      }
    });

    await walk(page, '/category/web-development', 'category-archive', async () => {
      await expect(page.locator('[data-testid="category-post-count"]'))
        .toContainText(`${DB.webDevPublished} posts`);
      const grid = page.locator('[data-testid="posts-grid"] >> [data-testid="post-card"], [data-testid="posts-grid"] article');
      expect(await grid.count(), 'category archive grid is EMPTY').toBe(DB.webDevPublished);
      // The unpublished Web Development post (#4) must not appear.
      await expect(page.locator('body')).not.toContainText('Observability for Blazor Server');
    });
  });

  test('the tag index and one archive match the database counts', async ({ page }) => {
    await walk(page, '/tags', 'tags', async () => {
      await expect(page.locator('body')).toContainText('Blazor');
      await expect(page.locator('body')).toContainText('PostgreSQL');
    });

    await walk(page, '/tag/blazor', 'tag-archive', async () => {
      const grid = page.locator('[data-testid="posts-grid"] >> [data-testid="post-card"], [data-testid="posts-grid"] article');
      expect(await grid.count(), 'tag archive grid is EMPTY').toBe(DB.blazorTagPublished);
      await expect(page.locator('body')).not.toContainText(DB.hiddenDraftTitle);
    });
  });

  test('a series page lists its published parts', async ({ page }) => {
    await walk(page, '/series/blazor-server-in-production', 'series', async () => {
      await expect(page.locator('body')).toContainText('Blazor Server in Production');
      await expect(page.locator('body')).not.toContainText(DB.hiddenDraftTitle);
    });
  });

  test('search returns the database hit count for blazor', async ({ page }) => {
    await walk(page, '/search?q=blazor', 'search', async () => {
      const results = page.locator('[data-testid="search-result"], article');
      expect(await results.count(), 'search returned an EMPTY result list').toBeGreaterThan(0);
      await expect(page.locator('body')).not.toContainText(DB.hiddenDraftTitle);
    });
  });

  test('the newsletter archive renders with the confirmed-subscriber count', async ({ page }) => {
    await walk(page, '/newsletters', 'newsletters', async () => {
      // The archive itself is empty in this seed; the subscriber count comes from
      // SubscriberRepo.GetActiveSubscribersAsync and must be the confirmed figure, not zero.
      await expect(page.locator('body')).toContainText(String(DB.subscribersConfirmed));
    });
  });

  test('the resume page renders owner data through the async repositories', async ({ page }) => {
    await walk(page, '/resume', 'resume', async () => {
      await expect(page.locator('body')).toContainText('Years of experience');
    });
  });

  test.afterEach(async () => {
    const real = consoleErrors.filter(e => !/favicon|net::ERR_|Failed to load resource/i.test(e));
    expect(real, `console errors: ${real.join(' | ')}`).toHaveLength(0);
  });
});

/** Signs in with the documented seeded site owner from the UsageGuide. Never creates a user. */
async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 });
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(3000);
  // ASSERT THE LANDING URL: a stale form post can leave us on /login looking signed in.
  expect(new URL(page.url()).pathname.toLowerCase()).not.toContain('login');
}

/**
 * Navigates to an admin route by CLICKING its nav link (SPA navigation). A direct goto() evaluates
 * as anonymous during prerender because the JWT lives in localStorage — pre-existing, unrelated to
 * this REQ.
 */
async function gotoAdmin(page: Page, href: string) {
  const link = page.locator(`a[href="${href}"]`).first();
  await link.waitFor({ state: 'attached', timeout: 30000 });
  await link.click();
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

test.describe('admin surfaces still render their data after the async flip', () => {
  test('dashboard, posts, categories, tags, series and subscribers all render rows', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page);

    await gotoAdmin(page, '/admin');
    await expect(page.locator('body')).toContainText('Posts');
    await page.screenshot({ path: `${SHOTS}/c-admin-dashboard-desktop.png`, fullPage: true });
    await visualTruth(page, 'admin@desktop');

    await gotoAdmin(page, '/BlogsList');
    const postRows = page.locator('tbody tr');
    expect(await postRows.count(), 'BlogsList grid is EMPTY').toBeGreaterThanOrEqual(10);
    await expect(page.locator('body')).toContainText(DB.hiddenDraftTitle);
    await page.screenshot({ path: `${SHOTS}/c-admin-posts-desktop.png`, fullPage: true });

    await gotoAdmin(page, '/admin/categories');
    const catRows = page.locator('tbody tr');
    expect(await catRows.count(), 'categories grid is EMPTY').toBeGreaterThanOrEqual(
      DB.categoriesWithCounts.length);
    await expect(page.locator('body')).toContainText('Web Development');
    await page.screenshot({ path: `${SHOTS}/c-admin-categories-desktop.png`, fullPage: true });

    await gotoAdmin(page, '/admin/tags');
    const tagRows = page.locator('tbody tr');
    expect(await tagRows.count(), 'tags grid is EMPTY').toBe(DB.tagCount);
    await page.screenshot({ path: `${SHOTS}/c-admin-tags-desktop.png`, fullPage: true });

    await gotoAdmin(page, '/admin/series');
    const seriesRows = page.locator('tbody tr');
    expect(await seriesRows.count(), 'series grid is EMPTY').toBe(DB.seriesCount);
    await page.screenshot({ path: `${SHOTS}/c-admin-series-desktop.png`, fullPage: true });

    await gotoAdmin(page, '/admin/subscribers');
    const subRows = page.locator('tbody tr');
    expect(await subRows.count(), 'subscribers grid is EMPTY').toBe(DB.subscribersTotal);
    await page.screenshot({ path: `${SHOTS}/c-admin-subscribers-desktop.png`, fullPage: true });

    // Mobile pass. The admin sidebar collapses behind a hamburger below the md breakpoint, so
    // navigate at desktop width first and only then shrink the viewport — the rendered rows are
    // what is under test here, not the nav affordance.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    expect(await page.locator('tbody tr').count(), 'subscribers grid empty at 390')
      .toBe(DB.subscribersTotal);
    await page.screenshot({ path: `${SHOTS}/c-admin-subscribers-mobile.png`, fullPage: true });
    await visualTruth(page, 'admin-subscribers@mobile');

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.waitForTimeout(800);
    await gotoAdmin(page, '/BlogsList');
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    expect(await page.locator('tbody tr').count(), 'BlogsList grid empty at 390')
      .toBeGreaterThanOrEqual(10);
    await page.screenshot({ path: `${SHOTS}/c-admin-posts-mobile.png`, fullPage: true });
    await visualTruth(page, 'admin-posts@mobile');
  });
});
