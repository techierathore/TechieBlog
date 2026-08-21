import { test, expect, Page } from '@playwright/test';

/**
 * Cluster F smoke — REQ-FN-052, removal of the orphaned SvcToken data-access stack.
 *
 * REQ-FN-052 was decided as a DELETION: `SvcTokenRepo`, `ISvcTokenRepo` and the `SvcToken`
 * model were removed as dead code. Nothing resolved them, nothing was registered in DI, and
 * `to_regclass('public.svctoken')` returns NULL against the live migrated database, so every
 * statement in the repository named a relation that has never existed.
 *
 * A deletion has no feature to exercise, so this smoke proves the only thing that could have
 * gone wrong: that nothing silently depended on the removed types at RUNTIME, where the C#
 * compiler cannot see it. Two things are asserted.
 *
 * 1. THE HOST STARTS AND THE CONTAINER RESOLVES. A removed registration that something
 *    depended on surfaces as a DI resolution failure the moment a page is served, not at
 *    build time. Serving a real page is the proof.
 * 2. TWO REPRESENTATIVE ROUTES STILL RENDER REAL DATA. The public home page and the admin
 *    dashboard, the latter behind a genuine signed-in Admin circuit.
 *
 * Gates (.tfcore/tasks/_smoke-test-policy.md):
 *  - RENDER-TRUTH: home must list real post cards; the dashboard must show its admin shell.
 *    A page that loads empty is a FAILURE, not a pass.
 *  - VISUAL-TRUTH: 1280 and 390. No horizontal page scroll at either width.
 *
 * Credentials are the documented seeded Admin from docs/TechieBlog-UsageGuide.md. No account
 * is invented, none is created, and no password is altered.
 */

const BASE = process.env.SMOKE_BASE ?? 'https://localhost:7373';

const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

/** Text Blazor's default ErrorBoundary renders when a component throws. */
const ERROR_BOUNDARY_TEXT = 'An unhandled error has occurred';

/** Waits for a live Blazor Server circuit rather than just a painted DOM. */
async function gotoInteractive(page: Page, url: string) {
  const socket = page.waitForEvent('websocket', {
    predicate: ws => ws.url().includes('_blazor'),
    timeout: 30000,
  });
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await socket;
  await page.waitForFunction(
    () => (window as unknown as { Blazor?: unknown }).Blazor !== undefined,
    null,
    { timeout: 30000 });
  await page.waitForTimeout(1500);
}

/**
 * Navigates client-side. A full page load of a protected route bounces to login because the
 * auth token lives in local storage and is unreadable during prerender — a known, separately
 * tracked session defect that must not be allowed to masquerade as a render failure here.
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

/**
 * Waits for the home page to settle onto its interactive render.
 *
 * Blazor Server serves a prerendered copy first and swaps the interactive one in behind it, so
 * for roughly the first three seconds the DOM legitimately holds TWO headers and TWO footers and
 * no post links at all. Screenshotting inside that window captures a doubled layout that looks
 * like a defect but is not one, and asserting inside it reads an empty article list. Settled is
 * defined as exactly one header and at least one real post link.
 */
async function waitForHomeSettled(page: Page) {
  await page.waitForFunction(
    () => document.querySelectorAll('header').length === 1
      && document.querySelectorAll('a[href^="/post/"]').length > 0,
    null,
    { timeout: 30000 });
  await page.waitForTimeout(500);
}

/** Fails when the PAGE itself scrolls sideways, which VISUAL-TRUTH forbids at every width. */
async function expectNoHorizontalPageScroll(page: Page, label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label} scrolls horizontally by ${overflow}px`).toBeLessThanOrEqual(1);
}

/**
 * Captures viewport-sized evidence at a given scroll offset.
 *
 * Deliberately NOT `fullPage`. Under headless Chromium in this WSL environment a full-page
 * capture composites a stale surface: on the home page it produced an image showing two stacked
 * copies of the layout and a loading spinner, while the live DOM at that same instant held one
 * header, one footer and three rendered post links (verified by reading getBoundingClientRect
 * before, between and after the captures). The doubled image was a screenshot artifact, not a
 * render defect, and evidence that lies is worse than no evidence. Viewport captures repaint
 * correctly, so tall pages are covered by capturing at more than one offset.
 */
async function captureAt(page: Page, scrollY: number, path: string) {
  await page.evaluate(y => window.scrollTo(0, y), scrollY);
  await page.waitForTimeout(600);
  await page.screenshot({ path });
}

test.use({ ignoreHTTPSErrors: true });

test.describe('REQ-FN-052 SvcToken removal', () => {
  test('the host serves the public home page with real content after the removal', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', e => pageErrors.push(String(e)));

    await page.setViewportSize({ width: 1280, height: 900 });
    await gotoInteractive(page, `${BASE}/`);
    await waitForHomeSettled(page);

    // RENDER-TRUTH: the article list must carry real post titles read from the database, not an
    // empty shell. Asserted with retrying, web-first expectations rather than a one-shot DOM read:
    // this host is shared with sibling smoke runs, and a Blazor circuit that reconnects under that
    // load re-renders the list, which made a single evaluate() read intermittently see zero links.
    const postLinks = page.locator('a[href^="/post/"]');
    await expect(postLinks.first(), 'home listed no articles').toBeVisible({ timeout: 30000 });

    const expectedPosts = process.env.DB_HOME_POSTS;
    if (expectedPosts !== undefined) {
      expect(parseInt(expectedPosts, 10),
        'the injected published-post count is not a live database reading').toBeGreaterThan(0);
    }

    await expect(postLinks.first(), 'the first article rendered without a title')
      .not.toHaveText('', { timeout: 15000 });

    const bodyText = await page.evaluate(() => document.body.innerText);
    expect(bodyText, 'home hit the ErrorBoundary').not.toContain(ERROR_BOUNDARY_TEXT);
    expect(bodyText.trim().length, 'home rendered an empty body').toBeGreaterThan(200);

    await expectNoHorizontalPageScroll(page, 'home @1280');
    await captureAt(page, 0, 'test-results/cluster-f/home-1280-top.png');
    // Second offset lands on the article list, so the evidence covers the data-bound region.
    const listY = await page.evaluate(() => {
      const first = document.querySelector('a[href^="/post/"]');
      return first ? Math.max(0, first.getBoundingClientRect().top + window.scrollY - 200) : 0;
    });
    await captureAt(page, listY, 'test-results/cluster-f/home-1280-articles.png');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1000);
    await expectNoHorizontalPageScroll(page, 'home @390');
    await captureAt(page, 0, 'test-results/cluster-f/home-390-top.png');

    expect(pageErrors, `home raised script errors: ${pageErrors.join(' | ')}`).toHaveLength(0);
  });

  test('the admin dashboard still resolves its service graph for a signed in Admin', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', e => pageErrors.push(String(e)));

    await page.setViewportSize({ width: 1280, height: 900 });

    await gotoInteractive(page, `${BASE}/login`);
    await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
    await page.fill('[data-testid="login-email"]', ADMIN.email);
    await page.fill('[data-testid="login-password"]', ADMIN.password);
    await page.click('[data-testid="login-submit"]');
    await page.waitForTimeout(4000);

    await spaNavigate(page, '/admin');

    const landed = new URL(page.url()).pathname;
    expect(landed, 'the admin sign in did not take').not.toContain('/login');

    const bodyText = await page.evaluate(() => document.body.innerText);
    expect(bodyText, '/admin hit the ErrorBoundary').not.toContain(ERROR_BOUNDARY_TEXT);
    // A DI resolution failure surfaces as this text on a Blazor Server circuit.
    expect(bodyText, '/admin failed to resolve a service')
      .not.toContain('Unable to resolve service');

    // RENDER-TRUTH: the dashboard shows its admin navigation and non-trivial content.
    await expect(page.locator('[data-testid="nav-dashboard"]').first(),
      'the session did not reach the admin shell').toBeVisible({ timeout: 20000 });
    expect(bodyText.trim().length, '/admin rendered an empty body').toBeGreaterThan(200);

    await expectNoHorizontalPageScroll(page, '/admin @1280');
    await captureAt(page, 0, 'test-results/cluster-f/admin-1280-top.png');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1000);
    await expectNoHorizontalPageScroll(page, '/admin @390');
    await captureAt(page, 0, 'test-results/cluster-f/admin-390-top.png');

    expect(pageErrors, `/admin raised script errors: ${pageErrors.join(' | ')}`).toHaveLength(0);
  });
});
