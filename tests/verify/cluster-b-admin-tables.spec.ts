import { test, expect, Page } from '@playwright/test';

/**
 * Cluster B smoke — REQ-UI-052, the admin surface shared by the web head and BlogApp.
 *
 * Two things are proven here.
 *
 * 1. THE SPLATTING CRASH IS GONE. TrBlazeUI 2.0.1 ships 132 components that declare no
 *    [Parameter(CaptureUnmatchedValues = true)] property — counted by reflecting over the
 *    shipped assembly, not by reading docs. Handing any of them a `data-testid` throws
 *    InvalidOperationException at render, and Blazor's ErrorBoundary swallows it behind an
 *    HTTP 200, so the route serves a normal response and paints nothing. The repair moves each
 *    hook onto a plain wrapper the page owns.
 *
 * 2. THE ACTION COLUMN IS REACHABLE. The admin grids put their Actions column at the right
 *    edge, and it was reported off-viewport in the BlogApp desktop head, which lays out at only
 *    ~950 CSS px because the unpackaged WinUI process runs DPI-unaware. TrBlazeUI's DataTable
 *    already renders its own `overflow-auto` container, so the column is reachable by scrolling
 *    that container — this suite measures exactly that, and additionally proves the PAGE itself
 *    never scrolls sideways.
 *
 * Gates (.tfcore/tasks/_smoke-test-policy.md):
 *  - RENDER-TRUTH: every grid must show real rows with non-empty cells, cross-checked against
 *    counts read from PostgreSQL immediately before the run and injected as env vars. A page
 *    that loads with an empty table is a FAILURE, not a pass.
 *  - VISUAL-TRUTH: 1280, 950 (the BlogApp width) and 390.
 *
 * Credentials are the documented seeded Admin from docs/TechieBlog-UsageGuide.md. No account is
 * invented and none is created. Every seeded account is flagged MustChangePassword
 * (REQ-NFR-023), so the runner clears that ONE flag for the Admin before the run and re-arms it
 * afterwards; no password hash is ever touched.
 */

const BASE = process.env.SMOKE_BASE ?? 'https://localhost:7481';

const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

/**
 * Where screenshots are written. Playwright wipes `test-results/` at the start of every run, and
 * sibling clusters run concurrently, so evidence masters are written outside it and copied in.
 */
const SHOTS = process.env.SHOT_DIR ?? 'test-results/cluster-b';

/** Text Blazor's default ErrorBoundary renders when a component throws. */
const ERROR_BOUNDARY_TEXT = 'An unhandled error has occurred';

/** The page size every admin grid is constructed with, so expected rows are capped. */
const PAGE_SIZE = 20;

/** Ground truth, read from PostgreSQL immediately before this run and injected, never hardcoded. */
function required(name: string): number {
  const raw = process.env[name];
  if (raw === undefined || !/^\d+$/.test(raw)) {
    throw new Error(`${name} must be injected from a live database query; got "${raw}"`);
  }
  return parseInt(raw, 10);
}

/** Every admin grid route, with the control that sits in its right-most Actions column. */
const ROUTES = [
  { path: '/admin/series', label: 'SeriesList', marker: '[data-testid="series-status-tabs"]', grid: 'series-grid', dbVar: 'DB_SERIES', action: '[data-testid="series-edit"]' },
  { path: '/users', label: 'UsersList', marker: '[data-testid="users-role-tabs"]', grid: 'users-grid', dbVar: 'DB_USERS', action: '[data-testid="user-change-role"]' },
  { path: '/admin/subscribers', label: 'SubscribersList', marker: '[data-testid="subscribers-status-tabs"]', grid: 'subscribers-grid', dbVar: 'DB_SUBSCRIBERS', action: '[data-testid="subscriber-deactivate"], [data-testid="subscriber-activate"]' },
  { path: '/admin/categories', label: 'CategoriesList', marker: '[data-testid="categories-grid"]', grid: 'categories-grid', dbVar: 'DB_CATEGORIES', action: '[data-testid="category-edit"]' },
  { path: '/CommentsList', label: 'CommentsList', marker: '[data-testid="comments-status-tabs"]', grid: 'comments-grid', dbVar: 'DB_COMMENTS', action: '[data-testid="comment-delete"]' },
  { path: '/BlogsList', label: 'BlogsList', marker: '[data-testid="posts-status-tabs"]', grid: 'posts-grid', dbVar: 'DB_POSTS', action: '[data-testid="post-edit"]' },
  { path: '/admin/tags', label: 'TagsList', marker: '[data-testid="tags-grid"]', grid: 'tags-grid', dbVar: 'DB_TAGS', action: '[data-testid="tag-edit"]' },
];

/** ManagePost is an editor form, not a grid, so it gets its own render assertions. */
const MANAGE_POST = { path: '/ManagePost', marker: '[data-testid="publish-time-picker"]' };

async function gotoInteractive(page: Page, url: string) {
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
 * Navigates client-side. A full page load of a protected route bounces to login because the auth
 * token lives in local storage and is unreadable during prerender — a known, separately tracked
 * session defect that must not be allowed to masquerade as a render failure here.
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
    link.click();
  }, href);
  await page.waitForTimeout(3000);
  await page.evaluate(() => document.getElementById('smoke-spa-link')?.remove());
}

async function loginAsAdmin(page: Page) {
  await gotoInteractive(page, `${BASE}/login`);
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.fill('[data-testid="login-email"]', ADMIN.email);
  await page.fill('[data-testid="login-password"]', ADMIN.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForTimeout(5000);

  const landed = new URL(page.url()).pathname;
  expect(landed, 'the admin sign-in did not succeed').not.toContain('/login');
  expect(landed, 'the admin account is pinned to the forced password gate (REQ-NFR-023)')
    .not.toContain('/change-password');
  await expect(page.locator('[data-testid="nav-dashboard"]').first(),
    'the sign-in did not reach the admin shell').toBeVisible({ timeout: 20000 });
}

test.describe('REQ-UI-052 admin surface', () => {
  test('every admin route renders real data, not an empty error boundary', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', e => pageErrors.push(String(e)));

    await page.setViewportSize({ width: 1280, height: 900 });
    await loginAsAdmin(page);

    for (const route of ROUTES) {
      await spaNavigate(page, route.path);

      const bodyText = await page.evaluate(() => document.body.innerText);
      expect(bodyText, `${route.label} (${route.path}) hit the ErrorBoundary`)
        .not.toContain(ERROR_BOUNDARY_TEXT);

      await expect(page.locator(route.marker).first(),
        `${route.label} did not render its marker`).toBeVisible({ timeout: 15000 });

      // RENDER-TRUTH: rows exist, are non-empty, and match what the database holds.
      const grid = `[data-testid="${route.grid}"]`;
      const rows = await page.locator(`${grid} tbody tr`).count();
      const expected = Math.min(required(route.dbVar), PAGE_SIZE);
      expect(rows, `${route.label} row count`).toBe(expected);

      const nonEmptyCells = await page.evaluate(sel => {
        const cells = Array.from(document.querySelectorAll(`${sel} tbody td`));
        return cells.filter(c => (c.textContent ?? '').trim().length > 0).length;
      }, grid);
      expect(nonEmptyCells, `${route.label} rendered a table of blank cells`).toBeGreaterThan(rows);

      await page.screenshot({ path: `${SHOTS}/${route.label}-1280.png`, fullPage: true });
    }

    // ManagePost is a form; assert its own repaired hook and a populated editor.
    await spaNavigate(page, MANAGE_POST.path);
    const managePostText = await page.evaluate(() => document.body.innerText);
    expect(managePostText, 'ManagePost hit the ErrorBoundary').not.toContain(ERROR_BOUNDARY_TEXT);
    await expect(page.locator(MANAGE_POST.marker).first(),
      'ManagePost did not render its repaired TimePicker wrapper').toBeVisible({ timeout: 15000 });
    expect(await page.locator('input, textarea').count(),
      'ManagePost rendered no editor fields').toBeGreaterThan(3);
    await page.screenshot({ path: `${SHOTS}/ManagePost-1280.png`, fullPage: true });

    expect(pageErrors, `page errors: ${pageErrors.join(' | ')}`).toHaveLength(0);
  });

  /**
   * The regression test for the off-viewport action column. 950x574 reproduces the layout the
   * BlogApp desktop head actually gets; 390 is the phone case the same grid has to survive.
   * One sign-in drives all three widths — the box runs many sibling agents and a fresh circuit
   * per viewport was getting the host OOM-killed mid-suite.
   */
  test('the action column is reachable and the page never scrolls sideways', async ({ page }) => {
    await loginAsAdmin(page);

    for (const width of [1280, 950, 390]) {
      await page.setViewportSize({ width, height: width === 950 ? 574 : 900 });

      for (const route of ROUTES) {
        await spaNavigate(page, route.path);
        // Wait for the route to actually paint before measuring — `page.evaluate` does not
        // retry, so querying too early reports a missing control rather than a layout fact.
        await expect(page.locator(route.marker).first(),
          `${route.label} did not render at ${width}px`).toBeVisible({ timeout: 20000 });

        // The page itself must never scroll horizontally — the grid's own container absorbs it.
        const doc = await page.evaluate(() => ({
          scrollWidth: document.documentElement.scrollWidth,
          clientWidth: document.documentElement.clientWidth,
        }));
        expect(doc.scrollWidth, `${route.label} scrolls the PAGE sideways at ${width}px`)
          .toBeLessThanOrEqual(doc.clientWidth + 1);

        // Scroll every scrollable ancestor of the action control fully right. This is what a
        // user does to reach the Actions column, and what the earlier report never tried.
        const scrolled = await page.evaluate(sel => {
          const btn = document.querySelector(sel);
          if (!btn) return null;
          const scrollers: string[] = [];
          for (let el = btn.parentElement; el; el = el.parentElement) {
            if (el.scrollWidth > el.clientWidth + 1) {
              el.scrollLeft = el.scrollWidth;
              scrollers.push(`${el.tagName}.${(el.className || '').toString().split(' ')[0]}`);
            }
          }
          return scrollers;
        }, route.action);
        expect(scrolled, `${route.label} action control missing at ${width}px`).not.toBeNull();
        await page.waitForTimeout(300);

        const action = page.locator(route.action).first();
        await expect(action, `${route.label} action control not visible`).toBeVisible({ timeout: 10000 });

        const rect = await action.boundingBox();
        expect(rect, `${route.label} action control has no box`).not.toBeNull();
        expect(rect!.width, `${route.label} action control has zero width`).toBeGreaterThan(0);
        expect(rect!.height, `${route.label} action control has zero height`).toBeGreaterThan(0);
        expect(rect!.x, `${route.label} action control starts left of the viewport at ${width}px`)
          .toBeGreaterThanOrEqual(-1);
        expect(rect!.x + rect!.width,
          `${route.label} action column cannot be scrolled into view at ${width}px`)
          .toBeLessThanOrEqual(width + 1);

        if (width !== 1280) {
          await page.screenshot({ path: `${SHOTS}/${route.label}-${width}.png`, fullPage: true });
        }
      }
    }
  });
});
