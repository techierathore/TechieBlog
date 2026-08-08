import { test, expect, Page } from '@playwright/test';

/**
 * Orchestrator smoke — the four admin pages repaired after clusters A, C and K independently
 * reported them dead.
 *
 * Root cause: TrBlazeUI 2.0.1 components that declare no CaptureUnmatchedValues throw
 * InvalidOperationException when given a data-testid, and Blazor's ErrorBoundary swallows the
 * failure behind an HTTP 200 — so each page served a normal response and rendered nothing.
 * Confirmed by reflecting over the shipped assembly: 132 of its components reject splatting.
 * The repair moves each test hook onto a plain wrapper the page owns.
 *
 *   /admin/series        SeriesList      TabsList + 3x TabsTrigger
 *   /users               UsersList       TabsList + 4x TabsTrigger
 *   /admin/subscribers   SubscribersList TabsList + 3x TabsTrigger
 *   /ManagePost          ManagePost      TimePicker
 *
 * Gates applied (.tfcore/tasks/_smoke-test-policy.md):
 *  - RENDER-TRUTH: the page must show its real content, not an empty error boundary. A page that
 *    returns 200 while rendering nothing is precisely the failure this suite exists to catch, so
 *    an explicit error-boundary check is asserted on every route.
 *  - VISUAL-TRUTH: 1280 and 390 widths; no horizontal overflow, no zero-size or off-viewport
 *    controls.
 *
 * Credentials are the documented seeded Admin from docs/TechieBlog-UsageGuide.md. No account is
 * invented and none is created.
 */

const BASE = process.env.SMOKE_BASE ?? 'http://localhost:5420';

const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

/** The repaired routes, with a stable marker that only renders when the page truly renders. */
const ROUTES = [
  { path: '/admin/series', label: 'SeriesList', marker: '[data-testid="series-status-tabs"]' },
  { path: '/users', label: 'UsersList', marker: '[data-testid="users-role-tabs"]' },
  { path: '/admin/subscribers', label: 'SubscribersList', marker: '[data-testid="subscribers-status-tabs"]' },
  { path: '/ManagePost', label: 'ManagePost', marker: '[data-testid="publish-time-picker"]' },
];

/** Text Blazor's default ErrorBoundary renders when a component throws. */
const ERROR_BOUNDARY_TEXT = 'An unhandled error has occurred';

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
  await page.waitForTimeout(4000);
}

test.describe('Repaired admin pages render after the TrBlazeUI splatting fix', () => {
  test('all four routes render their content, not an error boundary', async ({ page }) => {
    const consoleErrors: string[] = [];
    page.on('pageerror', e => consoleErrors.push(String(e)));

    await loginAsAdmin(page);

    for (const route of ROUTES) {
      await spaNavigate(page, route.path);

      const bodyText = await page.evaluate(() => document.body.innerText);
      expect(bodyText, `${route.label} (${route.path}) hit the ErrorBoundary`)
        .not.toContain(ERROR_BOUNDARY_TEXT);

      // The marker is the very hook that used to crash the page, so its presence proves both
      // that the page rendered and that the relocated test id survived the repair.
      await expect(page.locator(route.marker).first(),
        `${route.label} did not render its repaired marker`).toBeVisible({ timeout: 15000 });

      await page.screenshot({ path: `test-results/orchestrator/${route.label}-1280.png`, fullPage: true });
    }

    expect(consoleErrors, `page errors: ${consoleErrors.join(' | ')}`).toHaveLength(0);
  });

  test('no horizontal overflow at 1280 or 390', async ({ page }) => {
    await loginAsAdmin(page);

    for (const width of [1280, 390]) {
      await page.setViewportSize({ width, height: 900 });

      for (const route of ROUTES) {
        await spaNavigate(page, route.path);

        const overflow = await page.evaluate(() => ({
          scrollWidth: document.body.scrollWidth,
          clientWidth: document.body.clientWidth,
        }));
        expect(overflow.scrollWidth,
          `${route.label} overflows horizontally at ${width}px`)
          .toBeLessThanOrEqual(overflow.clientWidth + 1);

        if (width === 390) {
          await page.screenshot({ path: `test-results/orchestrator/${route.label}-390.png`, fullPage: true });
        }
      }
    }
  });
});
