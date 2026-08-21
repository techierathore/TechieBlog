import { test, expect, Page } from '@playwright/test';

/**
 * Cluster E — the tail of REQ-NFR-026 stage 3, plus REQ-FN-057 and REQ-FN-059.
 *
 * Seven Blazor call sites were flipped onto newly written async service twins:
 *   CommentSvc.GetCommentsByPostIdAsync / GetAllCommentsAsync / ApproveCommentAsync /
 *   DeleteCommentAsync, and RatingSvc.GetAverageRatingAsync / GetRatingCountAsync /
 *   GetPostRatingStatsAsync.
 *
 * The failure mode a green build cannot see is a twin that quietly reads a DIFFERENT query — a
 * thread that returns every comment on the site, a rating widget zeroed because the async twin
 * dropped the verified filter. Asserting mere PRESENCE passes on a zeroed widget, so every figure
 * below is cross-checked against a number read straight out of PostgreSQL, and every page is walked
 * at 1280 and 390 with a screenshot kept.
 */

const BASE = 'http://172.18.144.1:5407';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';
const SHOTS = 'tests/.artifacts';

/**
 * Ground truth read from PostgreSQL on 2026-08-10 with the repositories' own filters:
 *   approved comments  : WHERE postid=1 AND moderationstatus='Approved'
 *   rating aggregates  : SELECT round(avg(rating),2), count(*) ... WHERE isemailverified
 */
const DB = {
  postSlug: 'blazor-render-modes-explained',
  postApprovedComments: 6,
  postRatingAverage: '3.5',
  postRatingCount: 6,
  /** Home latest grid, newest first, with the star value the renderer rounds each average to. */
  homeCards: [
    { title: 'Writing a Technical Talk That Lands', stars: 4, count: 2 },
    { title: 'Shipping .NET with Docker and GitHub Actions', stars: 4, count: 2 },
    { title: 'The Markdown Kitchen Sink', stars: 5, count: 4 },
  ],
  blazorTagPublished: 3,
  /** REQ-FN-015 / REQ-FN-057: the Contributor's draft must never reach a public listing. */
  hiddenDraftTitle: 'Testing Dapper Repositories Without a Database',
  otherDraftTitle: 'Observability for Blazor Server',
};

let consoleErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  consoleErrors = [];
  page.on('console', m => {
    if (m.type() === 'error') consoleErrors.push(m.text());
  });
  page.on('pageerror', e => consoleErrors.push(`pageerror: ${e.message}`));
});

test.afterEach(() => {
  expect(consoleErrors, `console errors: ${consoleErrors.join(' | ')}`).toEqual([]);
});

/** Fails the check if the page scrolls horizontally or paints a zero-size body. */
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
    await page.waitForTimeout(1500);
    await assertContent();
    await visualTruth(page, `${name}@${tag}`);
    await page.screenshot({ path: `${SHOTS}/e-${name}-${tag}.png`, fullPage: true });
  }
}

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

/** Admin routes are reached by CLICKING the nav link — the JWT lives in localStorage. */
async function gotoAdmin(page: Page, href: string) {
  const link = page.locator(`a[href="${href}"]`).first();
  await link.waitFor({ state: 'attached', timeout: 30000 });
  await link.click();
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

test.describe('REQ-NFR-026 — the seven flipped call sites still render their data', () => {
  test('the post thread renders exactly the approved comments PostgreSQL holds', async ({ page }) => {
    await walk(page, `/post/${DB.postSlug}`, 'postview-comments', async () => {
      // GetCommentsByPostIdAsync must reach GetAllByIdAsync, not GetAllAsync: a twin that read
      // the whole table would render every comment on the site under this one article and still
      // look like a healthy thread in a screenshot.
      await expect(page.locator('[data-testid="comments-count"]'))
        .toHaveText(`(${DB.postApprovedComments})`);
      const items = page.locator('[data-testid="comment-item"], [data-testid="comment-reply"]');
      expect(await items.count(), 'comment thread is EMPTY').toBe(DB.postApprovedComments);
      // Nothing unconfirmed or unapproved may leak: comment 12 is PendingVerification.
      await expect(page.locator('[data-testid="comments-list"]')).not.toContainText('VERIFY-0808 anonymous comment awaiting');
    });
  });

  test('the post rating panel shows the verified average and count, not zeroes', async ({ page }) => {
    await walk(page, `/post/${DB.postSlug}`, 'postview-rating', async () => {
      // GetPostRatingStatsAsync. Asserting presence would pass on a zeroed widget, so both
      // figures are compared to the verified-only aggregate.
      await expect(page.locator('[data-testid="post-rating-average"]'))
        .toHaveText(DB.postRatingAverage);
      await expect(page.locator('[data-testid="post-rating-count"]'))
        .toContainText(`${DB.postRatingCount} ratings`);
    });
  });

  test('the home grid renders each card its own verified star figures', async ({ page }) => {
    await walk(page, '/', 'home-ratings', async () => {
      // GetAverageRatingAsync + GetRatingCountAsync — the two blocking calls that sat on the
      // most-requested route in the application.
      for (const expected of DB.homeCards) {
        const card = page.locator('[data-testid="post-card"]', { hasText: expected.title }).first();
        await expect(card, `home card "${expected.title}" missing`).toBeVisible();
        await expect(card.locator('[data-testid="total-rating-value"]'))
          .toHaveText(`(${expected.count})`);
        await expect(card.locator('[data-testid="star-rating-text"]'))
          .toContainText(`${expected.stars}`);
      }
      await expect(page.locator('body')).not.toContainText(DB.hiddenDraftTitle);
      await expect(page.locator('body')).not.toContainText(DB.otherDraftTitle);
    });
  });

  test('the admin comment grid loads every comment through the async twin', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page);
    await gotoAdmin(page, '/CommentsList');

    // GetAllCommentsAsync: the grid must hold every row in every state, so the total exceeds the
    // six the public thread shows.
    const rows = page.locator('tbody tr');
    expect(await rows.count(), 'admin comment grid is EMPTY').toBeGreaterThan(DB.postApprovedComments);
    await visualTruth(page, 'commentslist@desktop');
    await page.screenshot({ path: `${SHOTS}/e-commentslist-desktop.png`, fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await visualTruth(page, 'commentslist@mobile');
    await page.screenshot({ path: `${SHOTS}/e-commentslist-mobile.png`, fullPage: true });
  });

  test('moderation still works: approve and delete run through the async twins', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page);
    await gotoAdmin(page, '/CommentsList');

    // The grid pages at 20 rows, so the seeded rows are isolated with the search box rather than
    // hunted for; both are removed again by the harness after this spec.
    await page.fill('[data-testid="comments-search"]', 'CLUSTER-E');
    await page.waitForTimeout(2000);

    // ---- Approve, through ApproveCommentAsync. The seeded row is PendingApproval with a
    // confirmed address, so the twin's "address not confirmed" guard must let it through.
    const pendingRow = page.locator('tr', { hasText: 'CLUSTER-E approve me' }).first();
    await expect(pendingRow, 'the seeded PendingApproval row is not in the grid').toBeVisible();
    await pendingRow.locator('[data-testid="comment-approve"]').click();
    await page.waitForTimeout(2500);
    await expect(page.locator('body')).toContainText('Comment approved.');
    await page.screenshot({ path: `${SHOTS}/e-moderation-approved.png`, fullPage: true });

    // The status badge is re-read from the database rather than from the mutated view model:
    // TrBlazeUI's DataTable does not repaint a CellTemplate when a bound item's property changes
    // in place (TR-065), which is pre-existing and identical on the synchronous path. Reloading
    // the grid both dodges that and re-exercises GetAllCommentsAsync.
    await gotoAdmin(page, '/admin');
    await gotoAdmin(page, '/CommentsList');
    await page.fill('[data-testid="comments-search"]', 'CLUSTER-E');
    await page.waitForTimeout(2000);
    await expect(page.locator('tr', { hasText: 'CLUSTER-E approve me' }).first())
      .toContainText('Approved');

    // ---- Delete, through DeleteCommentAsync.
    const deleteRow = page.locator('tr', { hasText: 'CLUSTER-E delete me' }).first();
    await expect(deleteRow, 'the seeded throwaway row is not in the grid').toBeVisible();
    await deleteRow.locator('[data-testid="comment-delete"]').click();
    await page.waitForTimeout(1500);
    await page.click('[data-testid="comment-delete-confirm"]');
    await page.waitForTimeout(2500);
    await expect(page.locator('body')).toContainText('Comment deleted successfully.');
    await expect(page.locator('tr', { hasText: 'CLUSTER-E delete me' })).toHaveCount(0);
    await page.screenshot({ path: `${SHOTS}/e-moderation-deleted.png`, fullPage: true });
  });
});

test.describe('REQ-FN-057 — the detail page and the tag archive date by PublishedOn', () => {
  test('the post detail page shows the publication date, not the creation date', async ({ page }) => {
    const expected = process.env.EXPECTED_POST_DATE;
    test.skip(!expected, 'EXPECTED_POST_DATE not supplied by the harness');

    await walk(page, `/post/${DB.postSlug}`, 'postview-date', async () => {
      await expect(page.locator('[data-testid="post-date"]')).toHaveText(expected!);
    });
  });

  test('the tag archive dates its cards by the publication date', async ({ page }) => {
    const expected = process.env.EXPECTED_TAG_DATE;
    test.skip(!expected, 'EXPECTED_TAG_DATE not supplied by the harness');

    await walk(page, '/tag/blazor', 'tag-archive-date', async () => {
      const grid = page.locator('[data-testid="posts-grid"] [data-testid="post-card"]');
      expect(await grid.count(), 'tag archive grid is EMPTY').toBe(DB.blazorTagPublished);
      const card = grid.filter({ hasText: 'Blazor Render Modes Explained' }).first();
      await expect(card.locator('[data-testid="post-card-date"]')).toHaveText(expected!);
      await expect(page.locator('body')).not.toContainText(DB.hiddenDraftTitle);
    });
  });
});
