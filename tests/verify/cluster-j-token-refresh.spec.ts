/**
 * cluster-j-token-refresh.spec.ts — REQ-FN-008, token refresh (BRD-6).
 *
 * WHAT THIS HAS TO PROVE, and why the obvious test does not.
 * The verifier's finding was not "refresh is wrong", it was "refresh is UNREACHABLE": the method
 * existed, nothing called it, and no endpoint exposed it. A unit test that calls the method proves
 * nothing about that, so this spec never calls the refresh path directly. It drives the real login
 * form, lets a real access token pass its real expiry, and then asks only for an ordinary page —
 * exactly what a user does. Three independent witnesses then have to agree that the refresh ran:
 *
 *   1. the session SURVIVES — an [Authorize] route still renders instead of bouncing to /login;
 *   2. the token in browser storage is a DIFFERENT string afterwards (the session was rotated);
 *   3. the host log carries the "expired" line followed by the "Session refreshed" line, and the
 *      `userlogins` row in PostgreSQL holds the new token on the SAME loginid with a slid window.
 *
 * A fourth test is the control: with the session row revoked, the same journey must end at the
 * sign-in screen. Without it, "the page still works" is equally consistent with "nothing ever
 * expires", which is precisely the state this requirement was in before.
 *
 * HOW EXPIRY IS FORCED: the host under test is booted with `Auth__AccessTokenMinutes=0.5`, so an
 * access token lives 30 seconds. Nothing in the product is patched and no clock is faked — the spec
 * waits the token out. Set TB_ACCESS_SECONDS if the host is booted with a different lifetime.
 *
 * Runtime rules inherited from tests/verify/_gates.ts:
 *   - a full page load of an [Authorize] route prerenders as anonymous, so authenticated hops go
 *     through Blazor.navigateTo; a full load is used ONLY to start a fresh circuit on a public page,
 *     which is the moment the authentication state — and therefore the refresh — is rebuilt.
 */
import { test, expect, Page } from '@playwright/test';
import { execFileSync } from 'child_process';
import * as fs from 'fs';
import { BASE, USERS, login, nav } from './_gates';

const SHOTS = 'test-results-cluster-j';
fs.mkdirSync(SHOTS, { recursive: true });

/** Access-token lifetime the host under test was booted with, in seconds. */
const ACCESS_SECONDS = Number(process.env.TB_ACCESS_SECONDS ?? '30');

/** Host console log, used to prove the refresh code actually executed. */
const HOST_LOG =
  process.env.TB_HOST_LOG ??
  '/tmp/claude-1000/-mnt-c-1MyCode-TechieBlog/06426738-4236-446f-af33-97ffbe2dc617/scratchpad/host-5390.log';

/** The seeded Editor from docs/TechieBlog-UsageGuide.md — never an invented account. */
const ROLE = 'editor' as const;

test.describe.configure({ mode: 'serial', retries: 0 });
test.beforeEach(({}, testInfo) => testInfo.setTimeout(300000));

/** Runs a statement inside the shared WinPostgre container. */
function db(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql],
    { encoding: 'utf8' },
  ).trim();
}

/** Reads the whole host log; the lines this spec asserts on are Serilog INF lines. */
function hostLog(): string {
  return fs.existsSync(HOST_LOG) ? fs.readFileSync(HOST_LOG, 'utf8') : '';
}

/**
 * Reads the access token out of browser storage.
 *
 * The storage key is namespaced with a fingerprint of the JWT signing key (REQ-NFR-027), so it is
 * discovered rather than hard-coded — hard-coding it would make this spec fail on any host whose
 * key differs, and report it as a refresh failure.
 */
async function readStoredToken(page: Page): Promise<string> {
  return page.evaluate(() => {
    const key = Object.keys(localStorage).find((k) => k.startsWith('AccessToken-'));
    return key ? (localStorage.getItem(key) ?? '').replace(/^"|"$/g, '') : '';
  });
}

/** The `userlogins` row behind a token, as the product's own three-way match would find it. */
function sessionRow(token: string): { loginId: string; expiry: string } | null {
  const row = db(
    `SELECT loginid || '|' || exiprydate FROM userlogins WHERE logintoken = '${token}' LIMIT 1`,
  );
  if (!row) return null;
  const [loginId, expiry] = row.split('|');
  return { loginId, expiry };
}

/** Signs in and returns the session as the browser and the database each see it. */
async function signIn(page: Page) {
  await login(page, ROLE);
  const token = await readStoredToken(page);
  expect(token, 'sign-in must leave an access token in browser storage').not.toBe('');
  const row = sessionRow(token);
  expect(row, 'the issued token must be recorded in userlogins').not.toBeNull();
  console.log(`[cluster-j] signed in: loginid=${row!.loginId} window ends ${row!.expiry}`);
  return { token, row: row! };
}

/** Waits out the access token's configured lifetime, with a margin for the whole-second `exp`. */
async function waitForAccessTokenToExpire(page: Page) {
  const waitMs = (ACCESS_SECONDS + 8) * 1000;
  console.log(`[cluster-j] waiting ${waitMs / 1000}s for the access token to pass its exp claim`);
  await page.waitForTimeout(waitMs);
}

/**
 * Starts a fresh circuit on a PUBLIC page.
 *
 * This is the whole trigger: Blazor Server rebuilds the authentication state when a circuit starts,
 * which is where CustomAuthStateProvider resolves the stored token and — when it has expired —
 * redeems it. A public route is used so that a failure to refresh shows up later as a bounce from
 * the [Authorize] route rather than being masked by the prerender's own anonymous redirect.
 */
async function startFreshCircuit(page: Page) {
  for (let attempt = 1; attempt <= 6; attempt++) {
    const response = await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    if (response?.status() === 429) {
      console.log(`[cluster-j] home returned 429 (attempt ${attempt}); waiting out the shared budget`);
      await page.waitForTimeout(8000);
      continue;
    }
    break;
  }
  await page.waitForFunction(() => !!(window as any).Blazor, { timeout: 60000 });
  await page.waitForTimeout(3000);
}

test.describe('REQ-FN-008 — an expiring session token is refreshed without forcing re-login', () => {
  /**
   * The whole journey in one test, because it has to be: Playwright gives every test a fresh
   * browser context, and the session under test lives in that context's local storage. Splitting
   * "sign in", "let it expire" and "still signed in?" across tests would measure three different
   * browsers and prove nothing.
   *
   * Sign in → let the real access token pass its real expiry → ask for an ordinary page → the
   * session must survive, the stored token must have been replaced, and the host log and the
   * database must both show the renewal that did it.
   */
  test('REQ-FN-008 an expired access token is renewed and the session survives', async ({ page }) => {
    const logBefore = hostLog().length;
    const { token: originalToken, row: originalRow } = await signIn(page);

    await waitForAccessTokenToExpire(page);
    await startFreshCircuit(page);

    // WITNESS 1 — the token was rotated. The renewal happens inside the circuit as the
    // authentication state is rebuilt, so poll rather than assume a fixed delay.
    await expect
      .poll(async () => (await readStoredToken(page)) !== originalToken, { timeout: 60000 })
      .toBe(true);
    const refreshedToken = await readStoredToken(page);
    expect(refreshedToken, 'the renewed token must be a real token').not.toBe('');
    console.log(
      `[cluster-j] token rotated: …${originalToken.slice(-12)} -> …${refreshedToken.slice(-12)}`,
    );
    await page.screenshot({ path: `${SHOTS}/fn008-after-refresh-home-1280.png` });

    // WITNESS 2 — the session survives: an [Authorize] route renders instead of the sign-in form.
    await nav(page, '/change-password');
    await expect(page.locator('[data-testid="change-password-card"]')).toBeVisible({ timeout: 60000 });
    expect(page.url()).toContain('/change-password');
    expect(page.url()).not.toContain('/login');
    console.log(`[cluster-j] authorized route reached as ${USERS[ROLE].email} after the refresh`);

    await page.setViewportSize({ width: 1280, height: 900 });
    await page.screenshot({ path: `${SHOTS}/fn008-authorized-after-refresh-1280.png` });
    const hScroll = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
    );
    expect(hScroll, 'the authorized screen must not scroll horizontally at 1280').toBe(false);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(800);
    await page.screenshot({ path: `${SHOTS}/fn008-authorized-after-refresh-390.png` });
    const hScrollNarrow = await page.evaluate(
      () => document.documentElement.scrollWidth > document.documentElement.clientWidth,
    );
    expect(hScrollNarrow, 'the authorized screen must not scroll horizontally at 390').toBe(false);
    await page.setViewportSize({ width: 1280, height: 900 });

    // WITNESS 3 — the refresh CODE ran: the host logged the expiry it detected and the renewal it
    // performed, in this run rather than in some earlier one.
    const log = hostLog().slice(logBefore);
    expect(log, 'the host must have logged the expiry it detected').toMatch(
      /Access token for user \d+ has expired/,
    );
    expect(log, 'the host must have logged the renewal itself').toMatch(
      /Session refreshed for user \d+: session \d+ reissued/,
    );
    console.log(
      `[cluster-j] host log carries ${(log.match(/Session refreshed for user/g) ?? []).length} renewal line(s)`,
    );

    // WITNESS 4 — PostgreSQL: the replaced token is gone, the new one occupies the SAME row, and
    // the refresh window slid forward.
    expect(sessionRow(originalToken), 'the replaced token must no longer resolve a session').toBeNull();
    const renewedRow = sessionRow(refreshedToken);
    expect(renewedRow, 'the renewed token must be recorded in userlogins').not.toBeNull();
    expect(renewedRow!.loginId, 'the renewal must rewrite the same session row, not insert another')
      .toBe(originalRow.loginId);
    expect(
      new Date(renewedRow!.expiry).getTime(),
      'the refresh window must have slid forward',
    ).toBeGreaterThan(new Date(originalRow.expiry).getTime());
    console.log(
      `[cluster-j] userlogins ${originalRow.loginId}: window ${originalRow.expiry} -> ${renewedRow!.expiry}`,
    );
    expect(db(`SELECT tokenstatus FROM userlogins WHERE loginid = ${originalRow.loginId}`))
      .toBe('ValidToken');

    // Leave the database as found: this row is the session this test created.
    db(`DELETE FROM userlogins WHERE loginid = ${originalRow.loginId}`);
  });

  /**
   * The control, and the reason the test above is not circular. Revoking the session row must end
   * the session on the next circuit — if it did not, "the page still works" would be equally
   * consistent with a product in which nothing ever expires and nothing is ever checked, which is
   * exactly the state this requirement was in before.
   */
  test('REQ-FN-008 a revoked session is not renewed and lands on the sign-in screen', async ({ page }) => {
    const { row } = await signIn(page);
    db(`UPDATE userlogins SET tokenstatus = 'RevokedToken' WHERE loginid = ${row.loginId}`);

    await startFreshCircuit(page);
    await nav(page, '/change-password');
    await page.waitForTimeout(4000);

    const landing = await page.evaluate(() => ({
      path: location.pathname,
      hasLoginForm: !!document.querySelector('[data-testid="login-email"]'),
      hasCard: !!document.querySelector('[data-testid="change-password-card"]'),
    }));
    console.log(`[cluster-j] revoked session landed on ${landing.path}`);
    expect(landing.hasCard, 'a revoked session must not render an authorized screen').toBe(false);
    expect(
      landing.hasLoginForm || landing.path.toLowerCase().includes('login'),
      'a revoked session must end at the sign-in screen',
    ).toBe(true);

    await page.screenshot({ path: `${SHOTS}/fn008-revoked-session-bounced-1280.png` });

    db(`DELETE FROM userlogins WHERE loginid = ${row.loginId}`);
    expect(db(`SELECT COUNT(*) FROM userlogins WHERE loginid = ${row.loginId}`)).toBe('0');
  });
});
