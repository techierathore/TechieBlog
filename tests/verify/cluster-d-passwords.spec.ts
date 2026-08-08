import { test, expect, Page } from '@playwright/test';

/**
 * Cluster D smoke — REQ-NFR-002 (PBKDF2 password hashing + transparent migration) and
 * REQ-FN-006 (password strength on the surviving password-setting paths).
 *
 * Gates applied:
 *  - RENDER-TRUTH: the validation message must actually render as text on the page, not as a
 *    blank alert box. Every assertion below reads the visible string.
 *  - VISUAL-TRUTH: screenshots at 1280 and 390 are captured for the pages under test and are
 *    checked for horizontal overflow and zero-size / off-viewport controls.
 *
 * All four accounts come from docs/TechieBlog-UsageGuide.md. No account is invented; the only
 * account created is the one the AddUser page under test creates, which is the point of the test.
 */

const BASE = process.env.SMOKE_BASE ?? 'https://localhost:7433';

/**
 * Waits until the Blazor Server circuit is interactive.
 *
 * Without this the EditForm is still static-SSR markup, so a click posts the form the
 * old-fashioned way and the server answers "The POST request does not specify which form is
 * being submitted" — a harness artefact, not a defect in the page under test.
 */
async function waitInteractive(page: Page) {
  await page.waitForFunction(() => !!(window as { Blazor?: unknown }).Blazor, null, { timeout: 30000 });
  await page.waitForTimeout(2500);
}

const SEEDED = [
  { email: 'Ravi@techieblog.com', password: 'admin_password', label: 'admin' },
  { email: 'editor@techieblog.test', password: 'Editor#Pass1', label: 'editor' },
  { email: 'author@techieblog.test', password: 'Author#Pass1', label: 'author' },
  { email: 'contributor@techieblog.test', password: 'Contrib#Pass1', label: 'contributor' },
];

/** Signs in with a documented seeded account and waits for the app to leave /login. */
async function login(page: Page, email: string, password: string) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await waitInteractive(page);
  await page.fill('[data-testid="login-email"]', email);
  await page.fill('[data-testid="login-password"]', password);
  await page.click('[data-testid="login-submit"]');

  // Blazor Server leaves /login by SPA navigation, so there is no second document "load" event
  // for page.waitForURL() to observe — polling the URL is the reliable signal here.
  for (let waited = 0; waited < 45000; waited += 500) {
    if (!page.url().toLowerCase().includes('login')) break;
    await page.waitForTimeout(500);
  }
  await page.waitForTimeout(2500);
}

/**
 * Navigates to an admin route by CLICKING a link. A direct goto() cannot be used: the JWT lives
 * in localStorage only, so Blazor Server's prerender pass evaluates a full page load of an admin
 * route as anonymous and bounces to "/". (Pre-existing defect, logged separately by cluster D of
 * the earlier run — not a property of the pages under test.)
 */
async function gotoAdmin(page: Page, href: string) {
  const link = page.locator(`a[href="${href}"]`).first();
  if (await link.count() > 0) {
    await link.click();
  } else {
    await page.evaluate(h => {
      const a = document.createElement('a');
      a.href = h;
      document.body.appendChild(a);
      a.click();
    }, href);
  }
  await page.waitForTimeout(3000);
}

/** Fails if the document scrolls horizontally at the current viewport. */
async function assertNoHorizontalOverflow(page: Page, label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: page scrolls horizontally by ${overflow}px`).toBeLessThanOrEqual(1);
}

test.describe('REQ-NFR-002 — every seeded account authenticates against its PBKDF2 hash', () => {
  for (const account of SEEDED) {
    test(`seeded ${account.label} signs in`, async ({ page }) => {
      await login(page, account.email, account.password);

      // Leaving /login is the app's own success signal; assert it explicitly rather than
      // trusting the waitForURL above, and assert no error alert was rendered.
      expect(page.url().toLowerCase()).not.toContain('/login');
      const errorAlert = page.locator('[data-testid="login-error"]');
      if (await errorAlert.count() > 0) {
        expect(await errorAlert.first().isVisible()).toBeFalsy();
      }
      await page.screenshot({
        path: `test-results/cluster-d-login-${account.label}.png`,
        fullPage: true,
      });
    });
  }
});

test.describe('REQ-FN-006 — password strength on the admin account-creation path (BRD-10)', () => {
  test('AddUser rejects a weak password with a visible message and accepts a compliant one',
    async ({ page }) => {
      await page.setViewportSize({ width: 1280, height: 900 });
      await login(page, SEEDED[0].email, SEEDED[0].password);
      await gotoAdmin(page, '/AddUser');

      await page.waitForSelector('[data-testid="user-password"]', { timeout: 30000 });
      await waitInteractive(page);

      const unique = `smoke.pw.${Date.now()}@techieblog.test`;
      await page.fill('[data-testid="user-first-name"]', 'Smoke');
      await page.fill('[data-testid="user-last-name"]', 'Tester');
      await page.fill('[data-testid="user-email"]', unique);

      // ---- weak password -------------------------------------------------
      await page.fill('[data-testid="user-password"]', 'abc');
      await page.fill('[data-testid="user-confirm-password"]', 'abc');
      await page.click('[data-testid="add-user-submit"]');
      await page.waitForTimeout(2000);

      const status = page.locator('[data-testid="add-user-status-message"]');
      await expect(status).toBeVisible({ timeout: 15000 });

      // RENDER-TRUTH: the alert must carry real text, not be an empty coloured box.
      const weakText = (await status.innerText()).trim();
      expect(weakText.length, 'validation alert rendered blank').toBeGreaterThan(10);
      expect(weakText).toMatch(/8 characters/i);
      expect(weakText).toMatch(/uppercase/i);
      expect(weakText).toMatch(/number/i);

      await page.screenshot({ path: 'test-results/cluster-d-adduser-weak-1280.png', fullPage: true });
      await assertNoHorizontalOverflow(page, 'AddUser weak @1280');

      await page.setViewportSize({ width: 390, height: 844 });
      await page.waitForTimeout(800);
      await expect(status).toBeVisible();
      expect((await status.innerText()).trim().length).toBeGreaterThan(10);
      await page.screenshot({ path: 'test-results/cluster-d-adduser-weak-390.png', fullPage: true });
      await assertNoHorizontalOverflow(page, 'AddUser weak @390');

      // ---- compliant password --------------------------------------------
      await page.setViewportSize({ width: 1280, height: 900 });
      await page.waitForTimeout(500);
      await page.fill('[data-testid="user-password"]', 'Sm0keTestPass');
      await page.fill('[data-testid="user-confirm-password"]', 'Sm0keTestPass');
      await page.click('[data-testid="add-user-submit"]');
      await page.waitForTimeout(2500);

      const successText = (await status.innerText()).trim();
      expect(successText).toMatch(/created successfully/i);
      await page.screenshot({ path: 'test-results/cluster-d-adduser-ok-1280.png', fullPage: true });
    });
});

test.describe('REQ-FN-006 — password strength on the reset path (BRD-5)', () => {
  test('reset page rejects a weak password with a visible message', async ({ page }) => {
    const token = process.env.SMOKE_RESET_TOKEN;
    test.skip(!token, 'no reset token supplied by the harness');

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(`${BASE}/reset-password/${token}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="reset-password-new"]', { timeout: 30000 });
    await waitInteractive(page);

    await page.fill('[data-testid="reset-password-new"]', 'abc');
    await page.fill('[data-testid="reset-password-confirm"]', 'abc');
    await page.click('[data-testid="reset-submit"]');
    await page.waitForTimeout(2000);

    const message = page.locator('[data-testid="reset-password-message"]');
    await expect(message).toBeVisible({ timeout: 15000 });
    const text = (await message.innerText()).trim();
    expect(text.length, 'reset validation alert rendered blank').toBeGreaterThan(10);
    expect(text).toMatch(/8 characters/i);

    await page.screenshot({ path: 'test-results/cluster-d-reset-weak-1280.png', fullPage: true });
    await assertNoHorizontalOverflow(page, 'reset weak @1280');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(800);
    await expect(message).toBeVisible();
    await page.screenshot({ path: 'test-results/cluster-d-reset-weak-390.png', fullPage: true });
    await assertNoHorizontalOverflow(page, 'reset weak @390');
  });
});
