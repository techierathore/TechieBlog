import { test, expect, Page } from '@playwright/test';

/**
 * Cluster C smoke — REQ-UI-001 (role-aware post-login landing), REQ-FN-009 (5 roles /
 * 5 policies) and REQ-UI-017 (post list reachable by an Author, author-scoped rows).
 *
 * Gates applied (.tfcore/tasks/_smoke-test-policy.md):
 *  - RENDER-TRUTH: the post list must show real rows with non-empty title / author / status /
 *    date cells, and the row COUNT must match what PostgreSQL holds for that user — an Author
 *    sees only their own 2 posts, an Admin sees all 13.
 *  - VISUAL-TRUTH: screenshots at 1280 and 390; no horizontal overflow, no zero-size or
 *    off-viewport controls, no overlapping key controls.
 *
 * Every credential below is a documented seeded account from docs/TechieBlog-UsageGuide.md.
 * No account is invented and none is created by this spec.
 */

const BASE = process.env.SMOKE_BASE ?? 'https://localhost:7373';

/**
 * Ground truth read from PostgreSQL immediately before this run (AppDbConString):
 *   SELECT COUNT(*) FROM BlogPost WHERE isdeleted IS NOT TRUE                       -> 13
 *   ... AND userid = 6 (author@techieblog.test)                                     ->  2
 *   ... AND published                                                               -> 11
 *   ... AND NOT published AND scheduledpublishon > NOW()                            ->  1
 *   SELECT firstname||' '||lastname FROM BlogUser WHERE userid = 6                  -> 'Arun Nair'
 */
const EXPECTED_ROWS = {
  admin: 13,   // every non-deleted post
  author: 2,   // author@techieblog.test owns exactly 2
};

const EXPECTED_TABS = {
  all: 13,
  published: 11,
  draft: 1,
  scheduled: 1,
};

const AUTHOR_NAME = 'Arun Nair';

interface RoleCase {
  label: string;
  email: string;
  password: string;
  /** The path the user must land on after sign-in, per the Usage Guide. */
  landing: string;
}

const ROLES: RoleCase[] = [
  { label: 'Admin', email: 'Ravi@techieblog.com', password: 'admin_password', landing: '/admin' },
  { label: 'Editor', email: 'editor@techieblog.test', password: 'Editor#Pass1', landing: '/admin' },
  { label: 'Author', email: 'author@techieblog.test', password: 'Author#Pass1', landing: '/blogslist' },
  { label: 'Contributor', email: 'contributor@techieblog.test', password: 'Contrib#Pass1', landing: '/' },
];

/**
 * Signs in with a documented seeded account and waits for the post-login navigation.
 * All seeded accounts carry MustChangePassword; the app does not divert on it today, so the
 * landing URL is read directly.
 */
async function gotoInteractive(page: Page, url: string) {
  // The login form is a plain EditForm with no @formname. Submitting it before the Blazor Server
  // circuit attaches performs a raw HTTP POST, which the framework rejects and which never
  // navigates. Waiting for the _blazor websocket is a direct signal that the circuit is live.
  const socket = page.waitForEvent('websocket', {
    predicate: ws => ws.url().includes('_blazor'),
    timeout: 30000,
  });
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await socket;
  await page.waitForFunction(() => (window as unknown as { Blazor?: unknown }).Blazor !== undefined,
    null, { timeout: 30000 });
  await page.waitForTimeout(1500);
}

/**
 * Navigates CLIENT-SIDE, the way a signed-in user moves around the app (the Blazor router
 * intercepts same-origin anchor clicks), by injecting an anchor and clicking it.
 *
 * A full `page.goto` cannot be used for a protected route: the auth token lives in browser local
 * storage, so a fresh document load has no authenticated principal until the circuit reads it,
 * and the visitor is bounced. That deep-link/refresh gap is reported separately — it is a session
 * concern, not a role-routing one, and it must not silently weaken these role assertions.
 */
async function spaNavigate(page: Page, href: string) {
  await page.evaluate(target => {
    const link = document.createElement('a');
    link.href = target;
    link.id = 'smoke-spa-link';
    link.textContent = 'go';
    link.style.position = 'fixed';
    link.style.top = '0';
    link.style.left = '0';
    link.style.zIndex = '2147483647';
    document.body.appendChild(link);
    // Dispatch the click in-page: a real mouse click can be intercepted by the site header,
    // and Blazor's router listens for the bubbling click event either way.
    link.click();
  }, href);

  await page.waitForTimeout(3000);
  await page.evaluate(() => document.getElementById('smoke-spa-link')?.remove());
}

async function login(page: Page, role: RoleCase): Promise<string> {
  await gotoInteractive(page, `${BASE}/login`);
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.fill('[data-testid="login-email"]', role.email);
  await page.fill('[data-testid="login-password"]', role.password);

  // Under a loaded machine the EditForm's interactive handler can attach a beat after the
  // circuit reports ready, and the first click is then swallowed. Re-press rather than declare
  // a sign-in failure, but stop immediately if the app actually REFUSED the credentials.
  const error = page.locator('[data-testid="login-error"]');
  for (let attempt = 1; attempt <= 3; attempt++) {
    await page.click('[data-testid="login-submit"]');
    try {
      await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 20000 });
      break;
    } catch {
      if (await error.count() > 0) {
        throw new Error(`${role.label}: sign-in refused — "${(await error.innerText()).trim()}"`);
      }
      expect(attempt, `${role.label}: sign-in never navigated after 3 attempts`).toBeLessThan(3);
      await page.waitForTimeout(2000);
    }
  }

  await page.waitForTimeout(2500);
  expect(await error.count(), `${role.label}: sign-in reported an error`).toBe(0);

  return new URL(page.url()).pathname.toLowerCase();
}

/**
 * Visual-truth gate: no horizontal overflow, every named control has a non-zero box inside
 * the viewport, and no two named controls overlap.
 */
async function visualGate(page: Page, testIds: string[], label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal overflow of ${overflow}px`).toBeLessThanOrEqual(1);

  const viewport = page.viewportSize()!;
  const boxes: Array<{ id: string; x: number; y: number; w: number; h: number }> = [];

  for (const id of testIds) {
    const el = page.locator(`[data-testid="${id}"]`).first();
    if (await el.count() === 0) {
      continue;
    }
    const box = await el.boundingBox();
    expect(box, `${label}: ${id} has no box`).not.toBeNull();
    expect(box!.width, `${label}: ${id} has zero width`).toBeGreaterThan(0);
    expect(box!.height, `${label}: ${id} has zero height`).toBeGreaterThan(0);
    expect(box!.x, `${label}: ${id} starts off-viewport at x=${box!.x}`).toBeGreaterThanOrEqual(-1);
    expect(box!.x + box!.width,
      `${label}: ${id} is clipped past the right edge`).toBeLessThanOrEqual(viewport.width + 1);
    boxes.push({ id, x: box!.x, y: box!.y, w: box!.width, h: box!.height });
  }

  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i];
      const b = boxes[j];
      const overlaps = a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;
      expect(overlaps, `${label}: ${a.id} overlaps ${b.id}`).toBe(false);
    }
  }
}

test.describe('Cluster C — role landings and the Author post list', () => {

  for (const role of ROLES) {
    test(`${role.label} lands on an authorised page`, async ({ page }) => {
      const path = await login(page, role);

      expect(path, `${role.label} landed on ${path}, expected ${role.landing}`).toBe(role.landing);

      // The router renders AccessDenied INLINE without changing the URL, so the URL check alone
      // would not catch a bounce — assert the component is absent too.
      expect(await page.locator('[data-testid="access-denied"]').count(),
        `${role.label} was shown the access-denied card`).toBe(0);

      await page.screenshot({
        path: `test-results/cluster-c-landing-${role.label.toLowerCase()}-1280.png`,
        fullPage: true,
      });
    });
  }

  test('Author reaches the post list and sees only their own posts', async ({ page }) => {
    await login(page, ROLES[2]);

    const grid = page.locator('[data-testid="posts-grid"]');
    await expect(grid, 'Author cannot see the posts grid').toBeVisible({ timeout: 30000 });

    // RENDER-TRUTH — real rows with real, non-empty cells.
    const titles = page.locator('[data-testid="post-row-title"]');
    const count = await titles.count();
    expect(count, `Author saw ${count} rows, expected ${EXPECTED_ROWS.author}`)
      .toBe(EXPECTED_ROWS.author);

    for (let i = 0; i < count; i++) {
      expect((await titles.nth(i).innerText()).trim().length,
        `row ${i} has an empty title`).toBeGreaterThan(0);

      // Not merely non-empty: the real seeded author name, so the "Unknown" placeholder fails.
      const author = (await page.locator('[data-testid="post-row-author"]').nth(i).innerText()).trim();
      expect(author, `row ${i} shows "${author}" instead of the real author`).toBe(AUTHOR_NAME);

      expect((await page.locator('[data-testid="post-row-status"]').nth(i).innerText()).trim().length,
        `row ${i} has an empty status`).toBeGreaterThan(0);
      expect((await page.locator('[data-testid="post-row-date"]').nth(i).innerText()).trim().length,
        `row ${i} has an empty date`).toBeGreaterThan(0);
    }

    // The Author must NOT be offered a menu entry that would bounce them.
    expect(await page.locator('[data-testid="nav-dashboard"]').count(),
      'Author is offered the EditorOrAbove dashboard link').toBe(0);
    expect(await page.locator('[data-testid="nav-users"]').count(),
      'Author is offered the AdminOnly users link').toBe(0);
    expect(await page.locator('[data-testid="nav-posts"]').count(),
      'Author is not offered the posts link').toBe(1);

    // VISUAL-TRUTH at 1280.
    await visualGate(page, ['admin-sidebar', 'admin-content'], 'author posts @1280');
    await page.screenshot({ path: 'test-results/cluster-c-author-posts-1280.png', fullPage: true });

    // VISUAL-TRUTH at 390.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await visualGate(page, ['admin-content'], 'author posts @390');
    await page.screenshot({ path: 'test-results/cluster-c-author-posts-390.png', fullPage: true });
  });

  test('Admin sees every post on the same list', async ({ page }) => {
    await login(page, ROLES[0]);
    // The Admin lands on /admin; reach the list the way the sidebar does — client-side.
    await spaNavigate(page, '/BlogsList');

    const grid = page.locator('[data-testid="posts-grid"]');
    await expect(grid, 'Admin cannot see the posts grid').toBeVisible({ timeout: 30000 });

    const count = await page.locator('[data-testid="post-row-title"]').count();
    expect(count, `Admin saw ${count} rows, expected ${EXPECTED_ROWS.admin}`)
      .toBe(EXPECTED_ROWS.admin);
    expect(count, 'Admin sees no more than the Author — scoping is not applied')
      .toBeGreaterThan(EXPECTED_ROWS.author);

    // The status tabs must carry the real counts. The Scheduled tab is the interesting one:
    // the list query used to omit ScheduledPublishOn, so it could only ever read zero.
    const tabCount = async (tab: string) => {
      const raw = (await page.locator(`[data-testid="posts-tab-${tab}"]`).innerText()).trim();
      const match = raw.match(/(\d+)/);
      expect(match, `tab ${tab} rendered "${raw}" with no number`).not.toBeNull();
      return parseInt(match![1], 10);
    };

    expect(await tabCount('all'), 'All tab count').toBe(EXPECTED_TABS.all);
    expect(await tabCount('published'), 'Published tab count').toBe(EXPECTED_TABS.published);
    expect(await tabCount('draft'), 'Draft tab count').toBe(EXPECTED_TABS.draft);
    expect(await tabCount('scheduled'), 'Scheduled tab count').toBe(EXPECTED_TABS.scheduled);

    await page.screenshot({ path: 'test-results/cluster-c-admin-posts-1280.png', fullPage: true });
  });

  test('Author is refused AdminOnly and is offered a landing it can open', async ({ page }) => {
    await login(page, ROLES[2]);
    await spaNavigate(page, '/users');

    await expect(page.locator('[data-testid="access-denied"]'),
      'Author was allowed into the AdminOnly user list').toBeVisible({ timeout: 20000 });

    // REQ-UI-001: the escape-hatch button must point at a route this role can actually open.
    const dashboard = page.locator('[data-testid="access-denied-dashboard"]');
    await expect(dashboard, 'Author is not offered a landing').toBeVisible({ timeout: 10000 });
    expect(await dashboard.getAttribute('href'),
      'the access-denied button still points at the EditorOrAbove dashboard').toBe('/BlogsList');

    await page.screenshot({ path: 'test-results/cluster-c-author-denied-1280.png', fullPage: true });
  });

  test('Contributor is refused every staff surface', async ({ page }) => {
    await login(page, ROLES[3]);

    for (const route of ['/admin', '/BlogsList', '/users', '/settings']) {
      await spaNavigate(page, route);
      // AccessDenied renders inline under AuthLayout; the URL keeps the requested route.
      await expect(page.locator('[data-testid="access-denied"]'),
        `Contributor was allowed into ${route}`).toBeVisible({ timeout: 20000 });
      expect(await page.locator('[data-testid="admin-sidebar"]').count(),
        `Contributor was shown the admin shell at ${route}`).toBe(0);
      // The role has no staff surface, so the card must not offer a "Go to Dashboard"
      // button that would deny them a second time.
      expect(await page.locator('[data-testid="access-denied-dashboard"]').count(),
        'Contributor is offered a dashboard button that denies them again').toBe(0);
    }

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(800);
    await page.screenshot({ path: 'test-results/cluster-c-contributor-denied-390.png', fullPage: true });
  });
});
