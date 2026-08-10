/*
  cluster-e.spec.ts — self-smoke for the cluster E FIX pass (2026-08-09).

  REQ-UI-056  every public write surface requires a captcha, and every surface that creates a
              subscriber respects double opt-in (IsConfirmed = false + verification token).
  REQ-UI-057  the accessible question challenge still works — it is also how this suite solves
              the captcha at all, since the image challenge is unreadable to a script by design.
  REQ-UI-004  the access-denied card renders on the bare auth shell, not nested in the blog shell.
  REQ-UI-045  BlogSidebar renders real data (categories, tags, search, subscribe) at 1280 and 390.

  Run: npx playwright test tests/verify/cluster-e.spec.ts --config playwright.config.ts
*/

import { test, expect, Page, Locator } from '@playwright/test';

const BASE = process.env.CLUSTER_E_BASE ?? 'http://172.18.144.1:5385';
const SHOTS = 'test-results-cluster-e';

const NUMBER_WORDS = [
  'zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten',
  'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen',
  'eighteen', 'nineteen', 'twenty',
];

/** Solves the four accessible-question shapes CaptchaQuestionSet can issue. */
function solveQuestion(question: string): string {
  const text = question.trim();

  let m = /What is (\w+) plus (\w+)\?/i.exec(text);
  if (m) return String(NUMBER_WORDS.indexOf(m[1].toLowerCase()) + NUMBER_WORDS.indexOf(m[2].toLowerCase()));

  m = /What is (\w+) minus (\w+)\?/i.exec(text);
  if (m) return String(NUMBER_WORDS.indexOf(m[1].toLowerCase()) - NUMBER_WORDS.indexOf(m[2].toLowerCase()));

  m = /How many letters are in the word '([^']+)'/i.exec(text);
  if (m) return String(m[1].length);

  m = /How many words are in this line: '([^']+)'/i.exec(text);
  if (m) return String(m[1].split(/\s+/).filter(Boolean).length);

  throw new Error(`unrecognised captcha question: "${question}"`);
}

/**
 * Waits until the router has finished authorizing.
 *
 * Routes.razor's <Authorizing> fragment is a <LayoutView Layout="MainLayout">, and
 * AuthorizeRouteView renders that fragment INSIDE the layout of the route being opened — so while
 * the async authentication state resolves, every page transiently paints a second shell (a second
 * header, sidebar and footer nested inside main-content). It settles by itself; asserting before it
 * does produces strict-mode violations that have nothing to do with the component under test.
 * Flagged for the owner — the fix belongs in Routes.razor, which cluster E does not own.
 */
async function waitForShellSettled(page: Page): Promise<void> {
  await expect(page.locator('[data-testid="authorizing"]')).toHaveCount(0, { timeout: 20000 });

  // The second shell appears and disappears again, so a single snapshot can pass while the page is
  // still churning. Require the shell census to hold steady across three consecutive samples.
  const census = () =>
    page.evaluate(() =>
      [
        document.querySelectorAll('[data-testid="main-content"]').length,
        document.querySelectorAll('[data-testid="blog-sidebar"]').length,
        document.querySelectorAll('[data-testid="authorizing"]').length,
      ].join(','));

  const deadline = Date.now() + 25000;
  let stable = 0;
  let previous = '';
  while (Date.now() < deadline) {
    const now = await census();
    stable = now === previous ? stable + 1 : 0;
    previous = now;
    if (stable >= 2 && !now.startsWith('2') && now.split(',')[2] === '0') return;
    await page.waitForTimeout(400);
  }
  throw new Error(`shell never settled — last census (main,sidebar,authorizing) = ${previous}`);
}

/** Waits for the Blazor circuit to be live (the challenge is issued only after that). */
async function waitInteractive(page: Page, captcha: Locator): Promise<void> {
  await expect(captcha.locator('[data-testid="captcha-mode-toggle"]')).toBeVisible({ timeout: 20000 });
  await expect(captcha.locator('[data-testid="captcha-image"]')).toBeVisible({ timeout: 20000 });
}

/** Switches the widget to question mode and answers it correctly. REQ-UI-057. */
async function answerCaptcha(captcha: Locator): Promise<string> {
  await captcha.locator('[data-testid="captcha-mode-toggle"]').click();
  const prompt = captcha.locator('[data-testid="captcha-prompt"]');
  await expect(prompt).toContainText(/\?/, { timeout: 15000 });
  const question = (await prompt.innerText()).trim();
  const answer = solveQuestion(question);
  await captcha.locator('[data-testid="captcha-answer"]').fill(answer);
  return question;
}

const stamp = Date.now().toString(36);
const addr = (tag: string) => `cluster-e-${tag}-${stamp}@techieblog.test`;

test.describe.configure({ mode: 'serial' });

/* -------------------------------------------------------------------------- */
/* REQ-UI-056 — sidebar subscribe: the hole the verifier found                  */
/* -------------------------------------------------------------------------- */

test('REQ-UI-056 sidebar subscribe now carries a captcha and refuses an unsolved one', async ({ page }) => {
  // /about is a MainLayout page — MainLayout is what renders BlogSidebar. (Home, /newsletters
  // and /post use FullWidthLayout and carry no sidebar.)
  await page.goto(`${BASE}/about`, { waitUntil: 'domcontentloaded' });
  await waitForShellSettled(page);

  const sidebar = page.locator('[data-testid="sidebar-subscribe"]');
  const captcha = page.locator('[data-testid="sidebar-subscribe-captcha"]');

  await expect(sidebar).toBeVisible();
  await expect(captcha).toHaveCount(1);
  await waitInteractive(page, captcha);

  // (a) submit with NO captcha answer at all
  await sidebar.locator('[data-testid="subscribe-email"]').fill(addr('nocaptcha'));
  await sidebar.locator('[data-testid="subscribe-submit"]').click();

  await expect(captcha.locator('[data-testid="captcha-error"]')).toBeVisible({ timeout: 10000 });
  // The card-level message must NOT be a success — nothing may have been written.
  await expect(sidebar.locator('[data-testid="subscribe-message"]'))
    .not.toContainText(/check your inbox|confirmation link|Thank you for subscribing/i);

  // (b) submit with a WRONG captcha answer
  await captcha.locator('[data-testid="captcha-answer"]').fill('ZZZZZ');
  await sidebar.locator('[data-testid="subscribe-submit"]').click();
  await expect(sidebar.locator('[data-testid="subscribe-message"]')).toContainText(/did not match|right answer/i, { timeout: 10000 });

  await page.screenshot({ path: `${SHOTS}/req-ui-056-sidebar-blocked-1280.png`, fullPage: false });
});

test('REQ-UI-056 sidebar subscribe with a valid captcha creates a PENDING subscriber', async ({ page }) => {
  // /about is a MainLayout page — MainLayout is what renders BlogSidebar. (Home, /newsletters
  // and /post use FullWidthLayout and carry no sidebar.)
  await page.goto(`${BASE}/about`, { waitUntil: 'domcontentloaded' });
  await waitForShellSettled(page);

  const sidebar = page.locator('[data-testid="sidebar-subscribe"]');
  const captcha = page.locator('[data-testid="sidebar-subscribe-captcha"]');
  await waitInteractive(page, captcha);

  await sidebar.locator('[data-testid="subscribe-email"]').fill(addr('sidebar-ok'));
  await answerCaptcha(captcha);
  await sidebar.locator('[data-testid="subscribe-submit"]').click();

  await expect(sidebar.locator('[data-testid="subscribe-message"]'))
    .toContainText(/check your inbox|confirmation link/i, { timeout: 20000 });

  await page.screenshot({ path: `${SHOTS}/req-ui-056-sidebar-pending-1280.png` });
});

/* -------------------------------------------------------------------------- */
/* REQ-UI-056 — newsletter subscribe card (/newsletters)                       */
/* -------------------------------------------------------------------------- */

test('REQ-UI-056 newsletter card refuses an unsolved captcha and accepts a solved one', async ({ page }) => {
  await page.goto(`${BASE}/newsletters`, { waitUntil: 'domcontentloaded' });
  await waitForShellSettled(page);

  const card = page.locator('[data-testid="newsletter-subscribe"]');
  const captcha = page.locator('[data-testid="newsletter-subscribe-captcha"]');
  await expect(card).toBeVisible();
  await waitInteractive(page, captcha);

  // unsolved
  await card.locator('[data-testid="newsletter-subscribe-email"]').fill(addr('card-nocaptcha'));
  await card.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await expect(captcha.locator('[data-testid="captcha-error"]')).toBeVisible({ timeout: 10000 });

  // solved
  await card.locator('[data-testid="newsletter-subscribe-email"]').fill(addr('card-ok'));
  await answerCaptcha(captcha);
  await card.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await expect(card.locator('[data-testid="newsletter-subscribe-status"]'))
    .toContainText(/check your inbox|confirmation link/i, { timeout: 20000 });

  await page.screenshot({ path: `${SHOTS}/req-ui-056-newsletter-card-1280.png` });
});

/* -------------------------------------------------------------------------- */
/* REQ-UI-056 — comment form and rating panel on a post page                   */
/* -------------------------------------------------------------------------- */

test('REQ-UI-056 comment form requires the captcha and still accepts a solved one', async ({ page }) => {
  await page.goto(`${BASE}/post/blazor-render-modes-explained`, { waitUntil: 'domcontentloaded' });
  await waitForShellSettled(page);

  const form = page.locator('[data-testid="comment-form"]');
  const captcha = form.locator('[data-testid="captcha-widget"]');
  await expect(form).toBeVisible();
  await waitInteractive(page, captcha);
  // CommentSpamGuard rejects a submission that arrives implausibly soon after the form became
  // interactive, so dwell before typing — this is the guard working, not a captcha failure.
  await page.waitForTimeout(6000);

  await form.locator('[data-testid="comment-name"]').fill('Cluster E');
  await form.locator('[data-testid="comment-email"]').fill(addr('comment'));
  await form.locator('[data-testid="comment-input"]').fill('Cluster E smoke — captcha gate check.');

  // wrong captcha -> rejected, nothing written
  await captcha.locator('[data-testid="captcha-answer"]').fill('ZZZZZ');
  await form.locator('[data-testid="comment-submit"]').click();
  await expect(form.locator('[data-testid="comment-form-error"]')).toBeVisible({ timeout: 20000 });

  // correct captcha -> accepted
  await answerCaptcha(captcha);
  await form.locator('[data-testid="comment-submit"]').click();
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 20000 });

  await page.screenshot({ path: `${SHOTS}/req-ui-056-comment-1280.png` });
});

test('REQ-UI-056 rating panel requires the captcha and still accepts a solved one', async ({ page }) => {
  await page.goto(`${BASE}/post/blazor-render-modes-explained`, { waitUntil: 'domcontentloaded' });
  await waitForShellSettled(page);

  const panel = page.locator('[data-testid="post-rating-panel"]');
  await expect(panel).toBeVisible();
  // The native radio group is the WCAG 2.1.1 keyboard fallback (REQ-NFR-007) and is visually
  // hidden until focused, so drive it the way a keyboard user does rather than with check().
  const star = panel.locator('[data-testid="post-rating-star-4"]');
  await star.focus();
  await star.press('Space');

  const step = panel.locator('[data-testid="rating-identify-step"]');
  await expect(step).toBeVisible({ timeout: 20000 });
  const captcha = step.locator('[data-testid="captcha-widget"]');
  await waitInteractive(page, captcha);

  await step.locator('[data-testid="rating-email"]').fill(addr('rating'));

  await captcha.locator('[data-testid="captcha-answer"]').fill('ZZZZZ');
  await panel.locator('[data-testid="rating-submit"]').click();
  await expect(panel.locator('[data-testid="rating-form-error"]')).toBeVisible({ timeout: 20000 });

  await answerCaptcha(captcha);
  await panel.locator('[data-testid="rating-submit"]').click();
  await expect(panel.locator('[data-testid="rating-form-success"]')).toBeVisible({ timeout: 20000 });

  await page.screenshot({ path: `${SHOTS}/req-ui-056-rating-1280.png` });
});

/* -------------------------------------------------------------------------- */
/* REQ-UI-045 — the Sidebar half                                               */
/* -------------------------------------------------------------------------- */

for (const width of [1280, 390]) {
  test(`REQ-UI-045 BlogSidebar renders its data at ${width}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    await page.goto(`${BASE}/about`, { waitUntil: 'domcontentloaded' });
    await waitForShellSettled(page);

    const sidebar = page.locator('[data-testid="blog-sidebar"]');
    await expect(sidebar).toHaveCount(1);
    await expect(sidebar).toBeVisible();

    await expect(sidebar.locator('[data-testid="sidebar-search-input"]')).toBeVisible();
    await expect(sidebar.locator('[data-testid="sidebar-categories-empty"]')).toHaveCount(0);
    await expect(sidebar.locator('[data-testid="sidebar-tags-empty"]')).toHaveCount(0);

    const categories = await sidebar.locator('[data-testid="sidebar-categories"] a').count();
    const tags = await sidebar.locator('[data-testid="sidebar-tags"] a').count();
    expect(categories, 'sidebar category links').toBeGreaterThan(0);
    expect(tags, 'sidebar tag links').toBeGreaterThan(0);

    await expect(sidebar.locator('[data-testid="sidebar-subscribe"]')).toBeVisible();
    await expect(sidebar.locator('[data-testid="subscribe-optin-note"]')).toBeVisible();

    const overflow = await page.evaluate(() => document.body.scrollWidth - document.body.clientWidth);
    expect(overflow, 'horizontal overflow').toBeLessThanOrEqual(0);

    await sidebar.screenshot({ path: `${SHOTS}/req-ui-045-sidebar-${width}.png` });
  });
}

/* -------------------------------------------------------------------------- */
/* REQ-UI-004 — access denied                                                  */
/* -------------------------------------------------------------------------- */

for (const width of [1280, 390]) {
  test(`REQ-UI-004 access-denied renders on the bare auth shell at ${width}`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });

    // Direct load of the route.
    await page.goto(`${BASE}/access-denied`, { waitUntil: 'domcontentloaded' });
    await waitForShellSettled(page);
    await expect(page.locator('[data-testid="access-denied"]')).toBeVisible({ timeout: 20000 });
    await expect(page.locator('[data-testid="authorizing"]')).toHaveCount(0, { timeout: 20000 });
    await expect(page.locator('[data-testid="auth-content"]')).toHaveCount(1);
    await expect(page.locator('[data-testid="blog-sidebar"]')).toHaveCount(0);
    await expect(page.locator('[data-testid="public-footer"]')).toHaveCount(0);
    expect(await page.locator('[data-testid="theme-toggle"]').count(),
      'theme toggles painted').toBeLessThanOrEqual(1);

    await page.screenshot({ path: `${SHOTS}/req-ui-004-direct-${width}.png` });
  });
}

test('REQ-UI-004 a denied role lands on the bare auth shell, not nested in the blog shell', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });

  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await waitForShellSettled(page);
  // The sign-in button only works once the circuit is interactive.
  await page.waitForTimeout(5000);
  await page.locator('[data-testid="login-email"]').fill('author@techieblog.test');
  await page.locator('[data-testid="login-password"]').fill('Author#Pass1');
  await page.locator('[data-testid="login-submit"]').click();
  await page.waitForURL(/BlogsList/, { timeout: 30000 });

  // An Author cannot open /users. This must be a CLIENT-SIDE navigation: a full GET of an
  // authorized URL is answered by the cookie middleware (the app's own token lives in local
  // storage), so it never reaches the router — and the defect this test covers only ever
  // appeared on the router path. Clicking an in-page anchor is what the Blazor router intercepts.
  await page.evaluate(() => {
    const link = document.createElement('a');
    link.href = '/users';
    link.id = 'cluster-e-nav';
    link.textContent = 'users';
    document.body.appendChild(link);
  });
  await page.click('#cluster-e-nav');

  await expect(page.locator('[data-testid="access-denied"]')).toBeVisible({ timeout: 25000 });
  await page.waitForURL(/access-denied/, { timeout: 20000 });
  await expect(page.locator('[data-testid="authorizing"]')).toHaveCount(0, { timeout: 20000 });
  // The bare auth shell has no MainLayout content column at all.
  await expect(page.locator('[data-testid="main-content"]')).toHaveCount(0);

  await expect(page.locator('[data-testid="blog-sidebar"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="public-footer"]')).toHaveCount(0);
  expect(await page.locator('[data-testid="brand-link"]').count(), 'brand marks').toBeLessThanOrEqual(1);
  expect(await page.locator('[data-testid="theme-toggle"]').count(), 'theme toggles').toBeLessThanOrEqual(1);

  await page.screenshot({ path: `${SHOTS}/req-ui-004-denied-1280.png` });
});
