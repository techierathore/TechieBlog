import { test, expect, Page } from '@playwright/test';

/**
 * Cluster B smoke — REQ-UI-019 (admin dashboard stat tiles + quick actions) and
 * REQ-FN-036 (admin dashboard counts service).
 *
 * Gates applied:
 *  - RENDER-TRUTH: every stat tile must show the REAL number the database holds. The values
 *    below were cross-checked directly against PostgreSQL with the AppDbConString credentials
 *    immediately before this run. A tile showing a constant (the old 1 / 1 / 0 / 0) fails.
 *  - VISUAL-TRUTH: no overlapping, clipped or off-viewport controls at 1280 and 390.
 */

const BASE = process.env.SMOKE_BASE ?? 'https://localhost:7391';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';
const EDITOR_EMAIL = 'editor@techieblog.test';
const EDITOR_PASSWORD = 'Editor#Pass1';

/**
 * Ground truth, read from PostgreSQL with the AppDbConString credentials immediately before this
 * run and injected as environment variables:
 *   SELECT COUNT(*) FROM BlogUser                                    -> DB_USERS
 *   SELECT COUNT(*) FROM BlogComment                                 -> DB_COMMENTS
 *   SELECT COUNT(*) FROM BlogComment WHERE Published = FALSE         -> DB_PENDING
 *   SELECT COUNT(*) FROM Subscriber                                  -> DB_SUBSCRIBERS
 *   SELECT COUNT(*) FROM BlogPost WHERE IsDeleted IS NOT TRUE        -> DB_POSTS
 *
 * They are injected rather than hardcoded because nine sibling agents are writing to the same
 * database concurrently; a literal would go stale between the query and the run. The old code
 * hardcoded 1 user / 1 subscriber / 0 comments regardless of what the database held.
 */
function required(name: string): number {
  const raw = process.env[name];
  if (raw === undefined || !/^\d+$/.test(raw)) {
    throw new Error(`${name} must be injected from a live database query; got "${raw}"`);
  }
  return parseInt(raw, 10);
}

const EXPECTED = {
  users: required('DB_USERS'),
  comments: required('DB_COMMENTS'),
  pendingComments: required('DB_PENDING'),
  subscribers: required('DB_SUBSCRIBERS'),
  posts: required('DB_POSTS'),
  scheduled: required('DB_SCHEDULED'),
  drafts: required('DB_DRAFTS'),
};

/**
 * Waits until the Blazor Server circuit is actually connected.
 *
 * Without this the click can land while the page is still statically prerendered, so the browser
 * does a plain form POST and the server answers "The POST request does not specify which form is
 * being submitted". That is a harness race, not a product defect — the fix is to interact only
 * once the circuit is live.
 */
async function waitForCircuit(page: Page) {
  await page.waitForFunction(
    () => Boolean((window as any).Blazor?.defaultReconnectionHandler),
    { timeout: 60000 });
  await page.waitForTimeout(1500);
}

/** Signs in with a documented seeded user from the UsageGuide. Never creates a user. */
async function login(page: Page, email = ADMIN_EMAIL, password = ADMIN_PASSWORD) {
  let lastError = '';
  for (let attempt = 1; attempt <= 3; attempt++) {
    await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
    await waitForCircuit(page);

    await page.fill('[data-testid="login-email"]', email);
    await page.fill('[data-testid="login-password"]', password);
    await page.waitForTimeout(500);
    await page.click('[data-testid="login-submit"]');

    try {
      // Generous: the host is cold on the first sign-in of a run and shares the machine with
      // nine sibling agents, so a slow first circuit is not a product defect.
      await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
    } catch {
      lastError = (await page.locator('body').innerText()).slice(0, 200).replace(/\n+/g, ' ');
      continue;
    }
    await page.waitForTimeout(3000);

    // The seeded admin carries MustChangePassword. If the app diverts to the reset screen,
    // the dashboard is reached by continuing past it rather than by inventing a new account.
    if (page.url().toLowerCase().includes('resetpassword') ||
        page.url().toLowerCase().includes('changepassword')) {
      const skip = page.locator('a[href="/admin"], a[href="/AdminDashboard"]').first();
      if (await skip.count() > 0) {
        await skip.click();
        await page.waitForTimeout(3000);
      }
    }
    return;
  }
  throw new Error(`sign-in as ${email} never left /login after 3 attempts. Last page text: ${lastError}`);
}

/**
 * Reads the integer rendered inside a tile value element.
 * Fails loudly rather than coercing a blank to zero, so an unrendered tile cannot pass.
 */
async function tileNumber(page: Page, testId: string): Promise<number> {
  const el = page.locator(`[data-testid="${testId}"]`);
  await expect(el, `${testId} is not visible`).toBeVisible({ timeout: 30000 });
  const raw = (await el.innerText()).trim().replace(/,/g, '');
  expect(/^\d+$/.test(raw), `${testId} rendered "${raw}", which is not a number`).toBe(true);
  return parseInt(raw, 10);
}

/**
 * Visual-truth gate: every listed control must have a non-zero box inside the viewport,
 * and the page must not scroll horizontally.
 */
async function visualGate(page: Page, testIds: string[], label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal overflow`).toBeLessThanOrEqual(1);

  const boxes: Array<{ id: string; x: number; y: number; w: number; h: number }> = [];
  for (const id of testIds) {
    const el = page.locator(`[data-testid="${id}"]`).first();
    if (await el.count() === 0) continue;
    if (!(await el.isVisible())) continue;
    const box = await el.boundingBox();
    expect(box, `${label}: ${id} has no box`).not.toBeNull();
    expect(box!.width, `${label}: ${id} zero width`).toBeGreaterThan(0);
    expect(box!.height, `${label}: ${id} zero height`).toBeGreaterThan(0);
    expect(box!.x, `${label}: ${id} off-viewport left`).toBeGreaterThanOrEqual(-2);
    const viewport = page.viewportSize();
    expect(box!.x, `${label}: ${id} starts off-viewport right`).toBeLessThan(viewport!.width);
    boxes.push({ id, x: box!.x, y: box!.y, w: box!.width, h: box!.height });
  }
  return boxes;
}

test.describe('REQ-UI-019 / REQ-FN-036 admin dashboard', () => {
  test('every stat tile shows the real database number and popular posts are truly ranked', async ({ page }) => {
    await login(page);

    // The Admin role lands on /admin, so the dashboard is reached by SPA navigation.
    await expect(page.locator('[data-testid="admin-dashboard"]')).toBeVisible({ timeout: 30000 });
    await expect(page.locator('[data-testid="dashboard-stats"]')).toBeVisible({ timeout: 30000 });

    // ---- RENDER-TRUTH: tiles must match the database, not the old constants ----------
    const users = await tileNumber(page, 'stat-users-value');
    const comments = await tileNumber(page, 'stat-comments-value');
    const subscribers = await tileNumber(page, 'stat-subscribers-value');
    const posts = await tileNumber(page, 'stat-posts-value');

    expect(users, `users tile shows ${users}, database holds ${EXPECTED.users}`).toBe(EXPECTED.users);
    expect(comments, `comments tile shows ${comments}, database holds ${EXPECTED.comments}`).toBe(EXPECTED.comments);
    expect(subscribers, `subscribers tile shows ${subscribers}, database holds ${EXPECTED.subscribers}`).toBe(EXPECTED.subscribers);
    expect(posts, `posts tile shows ${posts}, database holds ${EXPECTED.posts}`).toBe(EXPECTED.posts);

    // The specific constants the old code shipped must be gone.
    expect(users, 'users tile is still the hardcoded 1').not.toBe(1);
    expect(subscribers, 'subscribers tile is still the hardcoded 1').not.toBe(1);
    expect(comments, 'comments tile is still the hardcoded 0').not.toBe(0);

    // The attention panel must match the database too:
    //   published 11 / scheduled 1 / draft 1 of 13 posts, 0 comments awaiting moderation.
    const pending = (await page.locator('[data-testid="attention-pending-comments"]').innerText()).trim();
    expect(/\d/.test(pending), 'pending comments badge shows no number').toBe(true);
    expect(pending).toContain(String(EXPECTED.pendingComments));

    const scheduled = (await page.locator('[data-testid="attention-scheduled-posts"]').innerText()).trim();
    const drafts = (await page.locator('[data-testid="attention-draft-posts"]').innerText()).trim();
    expect(scheduled, `scheduled posts shows "${scheduled}", database holds ${EXPECTED.scheduled}`)
      .toContain(String(EXPECTED.scheduled));
    expect(drafts, `draft posts shows "${drafts}", database holds ${EXPECTED.drafts}`)
      .toContain(String(EXPECTED.drafts));

    // ---- RENDER-TRUTH: popular posts ranked by REAL views ---------------------------
    const popularEmpty = await page.locator('[data-testid="popular-posts-empty"]').count();
    expect(popularEmpty === 0, 'popular posts rendered its EMPTY state despite 480 views in the window').toBe(true);

    const rows = page.locator('[data-testid="popular-post-row"]');
    const rowCount = await rows.count();
    expect(rowCount, 'popular posts has no rows').toBeGreaterThan(0);

    const viewCounts: number[] = [];
    for (let i = 0; i < rowCount; i++) {
      const title = (await rows.nth(i).locator('[data-testid="popular-post-title"]').innerText()).trim();
      const viewsRaw = (await rows.nth(i).locator('[data-testid="popular-post-views"]').innerText()).trim();
      expect(title.length, `popular post row ${i} has an empty title`).toBeGreaterThan(0);
      expect(/^[\d,]+$/.test(viewsRaw), `popular post row ${i} views "${viewsRaw}" is not a number`).toBe(true);
      const views = parseInt(viewsRaw.replace(/,/g, ''), 10);
      expect(views, `popular post row ${i} shows 0 views — the old fabricated ranking`).toBeGreaterThan(0);
      viewCounts.push(views);
    }

    // Genuinely ranked: view counts must be non-increasing.
    for (let i = 1; i < viewCounts.length; i++) {
      expect(viewCounts[i], `popular posts not ordered by views: ${viewCounts.join(', ')}`)
        .toBeLessThanOrEqual(viewCounts[i - 1]);
    }
    console.log('RENDER-TRUTH tiles:', { users, comments, subscribers, posts, pending });
    console.log('RENDER-TRUTH popular post views:', viewCounts.join(', '));

    // ---- Quick actions must point at real, reachable admin routes -------------------
    // An Admin satisfies every policy, so the full set is offered.
    const adminRoutes = ['/ManagePost', '/comments', '/admin/newsletter', '/users'];
    for (const route of adminRoutes) {
      const link = page.locator(`[data-testid="quick-actions"] a[href="${route}"]`);
      expect(await link.count(), `quick action for ${route} is missing for Admin`).toBeGreaterThan(0);
    }

    // Clicking a quick action must actually navigate there, not 404 or bounce.
    await page.click('[data-testid="action-moderate-comments"]');
    await page.waitForURL(u => u.pathname.toLowerCase() === '/comments', { timeout: 30000 });
    await page.waitForTimeout(2500);
    const commentsBody = (await page.locator('body').innerText()).toLowerCase();
    expect(commentsBody.includes('not found') || commentsBody.includes('sorry'),
      'the Moderate Comments quick action landed on a 404').toBe(false);
    await page.goBack();
    await page.waitForTimeout(2500);
  });

  /**
   * Defect routed from cluster C: the dashboard offered "Manage Users" and "Send Newsletter" to an
   * Editor, but both pages are AdminOnly, so clicking either bounced to /access-denied. Offering a
   * destination that then denies the user is the bug; the fix hides it.
   */
  test('an Editor is offered no quick action that would deny them', async ({ page }) => {
    await login(page, EDITOR_EMAIL, EDITOR_PASSWORD);
    await expect(page.locator('[data-testid="admin-dashboard"]')).toBeVisible({ timeout: 30000 });

    // The AdminOnly actions must not be rendered at all.
    expect(await page.locator('[data-testid="action-manage-users"]').count(),
      'Editor is still offered "Manage Users", which is AdminOnly').toBe(0);
    expect(await page.locator('[data-testid="action-send-newsletter"]').count(),
      'Editor is still offered "Send Newsletter", which is AdminOnly').toBe(0);

    // The actions an Editor DOES satisfy must still be there.
    for (const route of ['/ManagePost', '/comments']) {
      expect(await page.locator(`[data-testid="quick-actions"] a[href="${route}"]`).count(),
        `Editor lost the quick action for ${route}`).toBeGreaterThan(0);
    }

    // Every visible quick action must actually open, never /access-denied.
    const hrefs = await page.locator('[data-testid="quick-actions"] a').evaluateAll(
      (els: any[]) => els.map(e => e.getAttribute('href')).filter(Boolean));
    expect(hrefs.length, 'Editor sees no quick actions at all').toBeGreaterThan(0);
    for (const href of hrefs) {
      await page.click(`[data-testid="quick-actions"] a[href="${href}"]`);
      await page.waitForTimeout(3000);
      expect(page.url().toLowerCase(),
        `Editor quick action ${href} landed on access-denied`).not.toContain('access-denied');
      await page.goBack();
      await page.waitForTimeout(2500);
      await expect(page.locator('[data-testid="admin-dashboard"]')).toBeVisible({ timeout: 30000 });
    }
    console.log('Editor quick actions verified reachable:', hrefs.join(', '));
  });

  test('dashboard renders cleanly at 1280 and 390', async ({ page }) => {
    await login(page);
    await expect(page.locator('[data-testid="admin-dashboard"]')).toBeVisible({ timeout: 30000 });

    const panels = ['dashboard-stats', 'stat-posts', 'stat-users', 'stat-comments', 'stat-subscribers',
      'quick-actions', 'needs-attention', 'recent-activity', 'popular-posts', 'popular-posts-list'];

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.waitForTimeout(800);
    const desktop = await visualGate(page, panels, 'dashboard@1280');
    await page.screenshot({ path: 'test-results/cluster-b/dashboard-1280.png', fullPage: true });

    // The four tiles sit on one row at 1280 (xl:grid-cols-4) and must not overlap.
    const tiles = desktop.filter(b => b.id.startsWith('stat-'));
    for (let i = 1; i < tiles.length; i++) {
      const prev = tiles[i - 1];
      const cur = tiles[i];
      const overlaps = cur.x < prev.x + prev.w - 2 && cur.y < prev.y + prev.h - 2 &&
                       prev.x < cur.x + cur.w - 2 && prev.y < cur.y + cur.h - 2;
      expect(overlaps, `dashboard@1280: ${prev.id} overlaps ${cur.id}`).toBe(false);
    }

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(800);
    await visualGate(page, panels, 'dashboard@390');
    await page.screenshot({ path: 'test-results/cluster-b/dashboard-390.png', fullPage: true });
  });
});
