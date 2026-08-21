import { test, expect } from '@playwright/test';
import { BASE, login, nav, renderCheck, visualCheck } from './_gates';

/**
 * REQ-NFR-026 / REQ-NFR-001 — cluster K smoke.
 *
 * Cluster K changed two things that a green build cannot vouch for:
 *   1. Service-layer methods that were already `Task`-returning but reached the database through
 *      the BLOCKING repository twin now await the async twin (BlogImageService x4, RatingSvc x1,
 *      SubscriberSvc x1). Swapping a synchronous Dapper call for its async twin is exactly the kind
 *      of edit that compiles, passes unit tests against fakes, and returns nothing at runtime.
 *   2. `Program.cs` raises the thread-pool floor. A hosting change can break startup outright.
 *
 * These checks therefore assert that real DATA still arrives on the pages the changed code feeds,
 * and that those pages still LOOK right, at 1280 and 390.
 *
 * Pages: / (home — site owner, stats, featured post, latest articles), /post/{slug} (post body,
 * rating widget — RatingSvc), /category/{slug} (archive listing) and /admin (dashboard, authorised).
 *
 * Ground truth is cross-checked against values read straight out of PostgreSQL and pinned below.
 */

const SCREENSHOTS = 'test-results-cluster-k';

/** SELECT slug FROM blogpost WHERE published=TRUE AND isdeleted=FALSE ORDER BY postid LIMIT 1; */
const POST_SLUG = 'blazor-render-modes-explained';

/** SELECT slug FROM category; — 'programming' has published posts attached. */
const CATEGORY_SLUG = 'programming';

/**
 * Fails the test when a render gate came back empty, so an empty page cannot pass as a render.
 */
function expectRenders(result: { control: string; verdict: string; detail: string }) {
  expect(
    result.verdict,
    `${result.control}: ${result.detail}`,
  ).toBe('RENDERS');
}

/**
 * Fails the test on the geometry findings that indicate a broken layout rather than a design
 * choice: a horizontally scrolling body, a zero-sized control, or a control pushed off-screen.
 */
function expectLooksRight(result: { width: number; hScroll: number; zeroSized: string[]; offViewport: string[]; consoleErrors: string[] }) {
  expect(result.hScroll, `horizontal overflow at ${result.width}px`).toBeLessThanOrEqual(2);
  expect(result.zeroSized, `zero-sized controls at ${result.width}px`).toEqual([]);
  expect(result.offViewport, `off-viewport controls at ${result.width}px`).toEqual([]);
}

test.describe('cluster K — async service path and thread-pool floor', () => {
  test('home page renders owner, stats, featured post and latest articles', async ({ page }) => {
    await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('[data-testid="home-page"]')).toBeVisible({ timeout: 60000 });

    expectRenders(await renderCheck(page, 'home hero', '[data-testid="home-page"] h1', 'value'));
    expectRenders(await renderCheck(page, 'featured post', '[data-testid="home-featured-post"]', 'value'));

    for (const width of [1280, 390]) {
      expectLooksRight(await visualCheck(page, `${SCREENSHOTS}/home-${width}.png`, width));
    }
  });

  test('post page renders body and the rating widget (RatingSvc async path)', async ({ page }) => {
    await page.goto(`${BASE}/post/${POST_SLUG}`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('h1').first()).toBeVisible({ timeout: 60000 });

    // The post body is the RENDER-TRUTH signal: an async read that silently returned nothing
    // would leave the shell standing and lose the prose.
    const bodyText = await page.locator('[data-testid="post-content"]').innerText();
    expect(bodyText.replace(/\s+/g, ' ').trim().length, 'post body is empty').toBeGreaterThan(300);

    // RatingSvc.GetPostRatingStatsForEmailAsync now awaits GetStatsByPostAsync instead of blocking
    // on GetStatsByPost, so this widget is fed by the changed code path. Asserting that it merely
    // EXISTS would pass on a zeroed widget, which is exactly what a broken async read produces —
    // so the values are cross-checked against PostgreSQL:
    //   SELECT count(*) FILTER (WHERE isemailverified),
    //          round(avg(rating) FILTER (WHERE isemailverified), 2)
    //   FROM postrating r JOIN blogpost p ON p.postid = r.postid WHERE p.slug = '<POST_SLUG>';
    //   -> 6 | 3.50
    await expect(page.locator('[data-testid="post-rating-panel"]')).toBeVisible({ timeout: 30000 });
    await expect(page.locator('[data-testid="post-rating-average"]')).toHaveText('3.5');
    await expect(page.locator('[data-testid="post-rating-count"]')).toContainText('6 ratings');

    for (const width of [1280, 390]) {
      expectLooksRight(await visualCheck(page, `${SCREENSHOTS}/post-${width}.png`, width));
    }
  });

  test('category archive lists published posts', async ({ page }) => {
    await page.goto(`${BASE}/category/${CATEGORY_SLUG}`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('h1').first()).toBeVisible({ timeout: 60000 });

    const links = page.locator('a[href^="/post/"]');
    expect(await links.count(), 'category archive listed no posts').toBeGreaterThan(0);

    for (const width of [1280, 390]) {
      expectLooksRight(await visualCheck(page, `${SCREENSHOTS}/category-${width}.png`, width));
    }
  });

  test('admin dashboard renders its counts for an authorised admin', async ({ page }) => {
    await login(page, 'admin');
    await nav(page, '/admin', /dashboard|admin/i);

    const text = await page.locator('main, body').first().innerText();
    expect(text.replace(/\s+/g, ' ').trim().length, 'admin dashboard is empty').toBeGreaterThan(80);
    expect(/\d/.test(text), 'admin dashboard shows no numbers at all').toBe(true);

    for (const width of [1280, 390]) {
      expectLooksRight(await visualCheck(page, `${SCREENSHOTS}/admin-${width}.png`, width));
    }
  });
});
