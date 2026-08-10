import { test, expect, Page, Locator } from '@playwright/test';
import { execFileSync } from 'child_process';

/**
 * REQ-FN-032 Cluster H smoke — newsletter send, history, and the unsubscribe link that 404'd.
 *
 * The defect: NewsletterSvc has mailed {BaseUrl}/unsubscribe/{token} since the feature shipped and
 * no Razor page was ever routed there, so every issue already delivered carried a dead unsubscribe
 * link. This run therefore walks the real journey rather than checking the new page in isolation:
 * compose and send an issue as the documented seeded admin, read the RENDERED MAIL BODY back out of
 * the log sink, extract the unsubscribe URL FROM THAT BODY (never hand-built), open it in a fully
 * anonymous browser context, and cross-check the subscriber's flipped status against PostgreSQL.
 *
 * Gates: RENDER-TRUTH (real rows, values matching the database) and VISUAL-TRUTH (no horizontal
 * overflow, no zero-size or off-viewport controls) at 1280 and 390.
 */

const BASE = 'http://172.18.144.1:5388';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

/** The two subscribers this run mails. Seeded by psql, removed by psql afterwards. */
const SEGMENT = 'clusterh';
const SUBSCRIBER_ONE = 'clusterh-one@smoke.test';
const SUBSCRIBER_TWO = 'clusterh-two@smoke.test';

/** Unique per run so the issue, its slug and its log lines cannot collide with a previous run. */
const RUN_ID = process.env.CLUSTER_H_RUN_ID ?? 'h1';
const SUBJECT = `Cluster H unsubscribe smoke ${RUN_ID}`;
const BODY_MARKDOWN = `## Cluster H ${RUN_ID}\n\nThe unsubscribe link below has to work.`;

/** The Serilog file sink the host writes to, inside this agent's private build directory. */
const LOG_GLOB = 'C:\\1MyCode\\TechieBlog\\.build-cluster-h\\logs';

/** Runs a query inside the shared WinPostgre container and returns the raw rows. */
function psql(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql],
    { encoding: 'utf8' },
  ).trim();
}

/**
 * Reads the slice of the host's rolling log that follows a marker line.
 *
 * The Development configuration logs Blazor render-tree activity at Debug, so the file runs to tens
 * of megabytes and cat-ing it overflows the child-process buffer. Only the lines after the marker
 * matter here, so grep does the slicing.
 */
function readHostLogAfter(marker: string, lines = 200): string {
  const dir = LOG_GLOB.replace(/\\/g, '/').replace('C:', '/mnt/c');
  const escaped = marker.replace(/'/g, `'\\''`);
  return execFileSync(
    'bash',
    ['-c', `grep -h -F -A ${lines} '${escaped}' ${dir}/*.log 2>/dev/null || true`],
    { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 },
  );
}

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
}

/**
 * Navigates through the app's own router. The JWT lives in localStorage only, which the server
 * cannot read during the prerender pass, so a full load of an authenticated route evaluates as
 * anonymous. Pre-existing, unrelated to this cluster.
 */
async function routerGoto(page: Page, href: string) {
  await page.evaluate(path => (window as any).Blazor.navigateTo(path), href);
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

/** Asserts an element is present, visible and carries non-blank text. */
async function expectPopulated(locator: Locator, label: string) {
  await expect(locator, `${label} should be visible`).toBeVisible({ timeout: 30000 });
  const text = ((await locator.textContent()) ?? '').trim();
  expect(text.length, `${label} should not be blank`).toBeGreaterThan(0);
}

/**
 * VISUAL-TRUTH: at the supplied width the page must not scroll horizontally and every visible
 * control must have a non-zero box inside the viewport.
 */
async function expectLooksRight(page: Page, width: number, name: string) {
  await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
  await page.waitForTimeout(1200);
  await page.screenshot({ path: `test-results-cluster-h/${name}-${width}.png`, fullPage: true });

  const overflow = await page.evaluate(
    () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
  );
  expect(overflow, `${name} at ${width} should not scroll horizontally`).toBeLessThanOrEqual(1);

  const problems = await page.evaluate(() => {
    const found: string[] = [];
    const viewportWidth = document.documentElement.clientWidth;
    document.querySelectorAll<HTMLElement>('button, a, h1, [data-testid]').forEach(element => {
      const style = getComputedStyle(element);
      if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') return;
      const box = element.getBoundingClientRect();
      if (box.width === 0 && box.height === 0) return;
      if (box.width === 0 || box.height === 0) {
        found.push(`zero-size ${element.tagName}.${element.className}`);
      }
      if (box.right > viewportWidth + 1 || box.left < -1) {
        found.push(`off-viewport ${element.tagName}.${element.className}`);
      }
    });
    return found;
  });
  expect(problems, `${name} at ${width} should have no broken boxes`).toEqual([]);
}

test.describe.configure({ mode: 'serial' });

let mailedUnsubscribeUrl = '';

test('REQ-FN-032 — compose and send an issue, and the mailed body carries a working unsubscribe URL', async ({ page }) => {
  const before = psql(
    `SELECT count(*) FROM subscribernewsletter sn JOIN subscriber s ON s.subscriberid = sn.subscriberid WHERE s.email LIKE '%${SEGMENT}%'`,
  );

  await login(page);
  await routerGoto(page, '/admin/newsletter');

  await page.fill('[data-testid="newsletter-subject"] input, [data-testid="newsletter-subject"]', SUBJECT);
  const bodyBox = page.locator('[data-testid="newsletter-body"] textarea');
  await bodyBox.fill(BODY_MARKDOWN);

  // Segment audience so this run mails only its own subscribers, never a sibling agent's rows.
  await page.click('#audience-segment');
  await page.waitForTimeout(500);
  await page.fill('[data-testid="newsletter-segment-filter"] input, [data-testid="newsletter-segment-filter"]', SEGMENT);
  await page.locator('[data-testid="newsletter-segment-filter"] input, [data-testid="newsletter-segment-filter"]').blur();
  await page.waitForTimeout(1500);

  await expect(page.locator('[data-testid="newsletter-recipient-count"]')).toContainText('2 recipient(s)');

  await page.click('[data-testid="newsletter-send"]');
  await page.waitForSelector('[data-testid="newsletter-send-dialog"]', { timeout: 30000 });
  await page.click('[data-testid="newsletter-send-confirm"]');

  await expect(page.locator('[data-testid="newsletter-send-outcome"]')).toContainText('Sent to 2 of 2', {
    timeout: 60000,
  });

  // RENDER-TRUTH: the delivery log lists the same rows PostgreSQL holds.
  const afterRows = psql(
    `SELECT s.email FROM subscribernewsletter sn JOIN subscriber s ON s.subscriberid = sn.subscriberid JOIN newsletter n ON n.newsletterid = sn.newsletterid WHERE n.title = '${SUBJECT}' ORDER BY s.email`,
  )
    .split('\n')
    .filter(Boolean);
  expect(afterRows, 'psql should hold one send-history row per recipient').toEqual([
    SUBSCRIBER_ONE,
    SUBSCRIBER_TWO,
  ]);
  expect(Number(before)).toBeLessThan(afterRows.length + Number(before));

  await expectPopulated(page.locator('[data-testid="newsletter-delivery-list"]'), 'delivery log');
  const loggedEmails = await page.locator('[data-testid="delivery-row-email"]').allTextContents();
  expect(loggedEmails.map(e => e.trim()).sort()).toEqual([SUBSCRIBER_ONE, SUBSCRIBER_TWO]);

  // The history card must list the issue itself against real counts.
  await expect(page.locator('[data-testid="newsletter-history-list"]')).toContainText(SUBJECT);

  // Extract the unsubscribe URL from the RENDERED MAIL BODY, not from the database and not by
  // rebuilding it: the claim under test is that the link a subscriber actually received works.
  const bodyMarker = `[DEV EMAIL BODY] To ${SUBSCRIBER_ONE} — ${SUBJECT}`;
  const bodySlice = readHostLogAfter(bodyMarker);
  expect(bodySlice, 'the dev transport should have logged the rendered body').toContain(bodyMarker);
  const hrefMatch = bodySlice.match(/href="(https?:\/\/[^"]*\/unsubscribe\/[^"]+)"/);
  expect(hrefMatch, 'the mailed body should carry an unsubscribe anchor').not.toBeNull();
  mailedUnsubscribeUrl = hrefMatch![1];

  // The body is rendered Markdown, matching what the composer preview promised.
  expect(bodySlice.slice(0, 2000)).toContain(`<h2`);
  expect(bodySlice.slice(0, 2000)).not.toContain(`## Cluster H ${RUN_ID}`);

  await expectLooksRight(page, 1280, 'composer-sent');
  await expectLooksRight(page, 390, 'composer-sent');
});

test('REQ-FN-032 — the mailed link unsubscribes anonymously and flips the row in PostgreSQL', async ({ browser }) => {
  expect(mailedUnsubscribeUrl, 'the previous test should have captured a mailed URL').not.toBe('');

  const beforeState = psql(`SELECT isconfirmed FROM subscriber WHERE email = '${SUBSCRIBER_ONE}'`);
  expect(beforeState, 'the subscriber should still be on the list').toBe('t');

  // A brand-new context: no cookies, no localStorage, no JWT. This is a stranger with a mail client.
  const context = await browser.newContext();
  const page = await context.newPage();

  const response = await page.goto(mailedUnsubscribeUrl, { waitUntil: 'networkidle' });
  expect(response?.status(), 'the mailed unsubscribe URL must not 404').toBe(200);

  // Not a login redirect and not a blank body.
  expect(page.url()).not.toContain('/login');
  await expect(page.locator('[data-testid="unsubscribe-page"]')).toBeVisible({ timeout: 30000 });
  await expect(page.locator('[data-testid="unsubscribe-done"]')).toBeVisible({ timeout: 30000 });
  await expectPopulated(page.locator('[data-testid="unsubscribe-summary"]'), 'unsubscribe confirmation');
  await expect(page.locator('h1')).toContainText('unsubscribed');

  const bodyText = ((await page.locator('body').textContent()) ?? '').trim();
  expect(bodyText.length, 'the unsubscribe page must not be a zero-byte body').toBeGreaterThan(100);

  // The write actually happened.
  const afterState = psql(`SELECT isconfirmed FROM subscriber WHERE email = '${SUBSCRIBER_ONE}'`);
  expect(afterState, 'the subscriber must be off the list').toBe('f');

  await expectLooksRight(page, 1280, 'unsubscribe-done');
  await expectLooksRight(page, 390, 'unsubscribe-done');

  await context.close();
});

test('REQ-FN-032 — re-opening the same link is a graceful no-op', async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  const response = await page.goto(mailedUnsubscribeUrl, { waitUntil: 'networkidle' });
  expect(response?.status()).toBe(200);

  await expect(page.locator('[data-testid="unsubscribe-already"]')).toBeVisible({ timeout: 30000 });
  await expect(page.locator('h1')).toContainText('Already unsubscribed');
  await expect(page.locator('[data-testid="unsubscribe-invalid"]')).toHaveCount(0);

  // Still off the list, and nothing else changed.
  expect(psql(`SELECT isconfirmed FROM subscriber WHERE email = '${SUBSCRIBER_ONE}'`)).toBe('f');

  await expectLooksRight(page, 1280, 'unsubscribe-already');
  await context.close();
});

test('REQ-FN-032 — a garbage token fails safely and leaks nothing', async ({ browser }) => {
  const context = await browser.newContext();
  const page = await context.newPage();

  const garbage = 'ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff';
  const response = await page.goto(`${BASE}/unsubscribe/${garbage}`, { waitUntil: 'networkidle' });
  expect(response?.status(), 'an unknown token should still render a real page').toBe(200);

  await expect(page.locator('[data-testid="unsubscribe-invalid"]')).toBeVisible({ timeout: 30000 });
  await expect(page.locator('[data-testid="unsubscribe-done"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="unsubscribe-already"]')).toHaveCount(0);

  // The wording must not disclose whether the token exists, and must never echo an address.
  const text = ((await page.locator('[data-testid="unsubscribe-invalid"]').textContent()) ?? '').toLowerCase();
  expect(text).not.toContain('@');
  expect(text).not.toContain('not found');
  expect(text).not.toContain('no such');
  expect(text).toContain('not valid');

  // The still-subscribed second recipient is untouched by the probe.
  expect(psql(`SELECT isconfirmed FROM subscriber WHERE email = '${SUBSCRIBER_TWO}'`)).toBe('t');

  await expectLooksRight(page, 1280, 'unsubscribe-invalid');
  await expectLooksRight(page, 390, 'unsubscribe-invalid');
  await context.close();
});

test('REQ-FN-032 — the send never mailed an unpublished blog post', async () => {
  // Cluster G fixed a read projection that leaked drafts. The newsletter send path reads only the
  // issue being dispatched, so assert the mailed body carries the composed content and nothing
  // pulled from the post store.
  const draftTitles = psql(
    `SELECT title FROM blogpost WHERE COALESCE(published, FALSE) = FALSE`,
  )
    .split('\n')
    .filter(Boolean);

  const bodyMarker = `[DEV EMAIL BODY] To ${SUBSCRIBER_ONE} — ${SUBJECT}`;
  const bodySlice = readHostLogAfter(bodyMarker, 40);
  expect(draftTitles.length, 'the database should hold at least one unpublished post to test against')
    .toBeGreaterThan(0);

  for (const title of draftTitles) {
    expect(bodySlice, `an unpublished post title leaked into the mail body: ${title}`).not.toContain(title);
  }
  expect(bodySlice).toContain('The unsubscribe link below has to work.');
});
