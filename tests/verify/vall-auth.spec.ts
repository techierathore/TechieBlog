/**
 * vall-auth.spec.ts — verification cluster "auth" for the 2026-08-08 `*verify all` run.
 *
 * Scope: authentication, authorization and account security. Every test title is prefixed with the
 * REQ ID it grades. Screenshots go to `.verify/shots/auth/` (NOT test-results/, which Playwright
 * wipes at the start of every concurrent sibling run).
 *
 * Runtime rules baked in (see tests/verify/_gates.ts):
 *   - the JWT lives only in localStorage, so authorised routes must be reached with Blazor.navigateTo.
 *   - the URL flips before the destination renders, so every hop waits on the destination's own marker.
 */
import { test, expect, Page } from '@playwright/test';
import { execFileSync } from 'child_process';
import * as fs from 'fs';
import { BASE, USERS, nav, renderCheck, visualCheck, RoleKey, ControlResult } from './_gates';

const SHOTS = '.verify/shots/auth';
fs.mkdirSync(SHOTS, { recursive: true });

// Eight verification agents share one host, one app and one 10-req/60 s rate-limit budget, so a
// hop that normally takes 3 s can take 90 s. Give each test room to wait its turn, and allow one
// retry so a lost circuit is re-attempted rather than reported as a product failure.
test.describe.configure({ retries: 0 });
test.beforeEach(({}, testInfo) => testInfo.setTimeout(300000));

/** Runs a read-only (or fixture) statement inside the shared WinPostgre container. */
function db(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql],
    { encoding: 'utf8' },
  ).trim();
}

/** psql prints the command tag after a RETURNING value; keep only the value. */
function firstLine(value: string): string {
  return value.split('\n')[0].trim();
}

/** Collected §4a control observations, dumped at the end for the report. */
const observations: Record<string, ControlResult[]> = {};
function record(screen: string, results: ControlResult[]) {
  observations[screen] = (observations[screen] ?? []).concat(results);
  for (const r of results) console.log(`[render] ${screen} :: ${r.control} = ${r.verdict} — ${r.detail}`);
}

/** §4b at both widths; logs the geometry so the report can quote numbers. */
async function bothWidths(page: Page, name: string) {
  const wide = await visualCheck(page, `${SHOTS}/${name}-1280.png`, 1280);
  const narrow = await visualCheck(page, `${SHOTS}/${name}-390.png`, 390);
  for (const v of [wide, narrow]) {
    console.log(
      `[visual] ${name}@${v.width} overlaps=${v.overlaps.length} zero=${v.zeroSized.length} ` +
        `off=${v.offViewport.length} hScroll=${v.hScroll} consoleErrors=${v.consoleErrors.length} ` +
        `${JSON.stringify({ o: v.overlaps.slice(0, 3), z: v.zeroSized.slice(0, 3), f: v.offViewport.slice(0, 3), c: v.consoleErrors.slice(0, 2) })}`,
    );
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  return { wide, narrow };
}

/**
 * Loads a rate-limited credential path, retrying through the 429s.
 *
 * REQ-NFR-005's HTTP limiter partitions on client IP (10 requests / 60 s) and EIGHT verification
 * agents share 127.0.0.1, so a plain page.goto on /login is routinely rejected by a sibling's
 * traffic. The retry is a test-harness concession to that shared budget, not a workaround for a
 * product defect — the 429 itself is the evidence REQ-NFR-005 wants, asserted in the zz spec.
 */
async function gotoThrottled(page: Page, path: string, marker?: string): Promise<boolean> {
  for (let attempt = 1; attempt <= 6; attempt++) {
    // The circuit's websocket must be open before any control is interactive; waiting only for
    // window.Blazor lets a click land on a dead button and silently do nothing.
    const ws = page.waitForEvent('websocket', { timeout: 30000 }).catch(() => null);
    const res = await page.goto(`${BASE}${path}`, { waitUntil: 'domcontentloaded' });
    if (res?.status() === 429) {
      console.log(`[throttle] ${path} returned 429 (attempt ${attempt}) — waiting out the shared window`);
      await page.waitForTimeout(8000);
      continue;
    }
    try {
      if (marker) await page.waitForSelector(marker, { timeout: 25000 });
      await ws;
      await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 20000 }).catch(() => {});
      await page.waitForTimeout(1800);
      if (!marker || (await page.locator(marker).count()) > 0) return true;
    } catch {
      /* fall through to the diagnostic below */
    }
    // A leftover session is the usual reason a credential page bounces: /login sends an already
    // authenticated visitor to the public home page, so the marker disappears. Drop the session
    // before retrying, otherwise the retry bounces exactly the same way.
    const body = (await page.locator('body').innerText().catch(() => ''))?.trim().slice(0, 80) ?? '';
    console.log(`[goto] ${path} attempt ${attempt} ended on ${page.url()} without ${marker}; body="${body}" — backing off`);
    // A throttled response is a 429 with an EMPTY body, which paints a blank page at the right URL.
    // Retrying immediately just spends more of the shared 10-req/60 s budget, so back off first.
    await page.evaluate(() => { try { localStorage.clear(); } catch { /* opaque origin */ } });
    await page.waitForTimeout(8000);
  }
  return false;
}

/**
 * Types into a Blazor Server input and makes sure the value SURVIVES.
 *
 * The page is prerendered as static HTML before the interactive circuit attaches. Anything typed
 * into that static DOM is wiped the moment the circuit renders from the (still empty) server-side
 * model, so a form filled a fraction too early submits blank and the browser's own "Please fill out
 * this field" validation silently blocks the submit — no navigation, no inline error, nothing to
 * wait on. Re-typing until the value sticks is what makes a sign-in deterministic here.
 */
async function fillStable(page: Page, selector: string, value: string) {
  for (let attempt = 1; attempt <= 15; attempt++) {
    await page.fill(selector, value).catch(() => {});
    await page.waitForTimeout(700);
    const actual = await page.inputValue(selector).catch(() => null);
    if (actual === value) return true;
  }
  console.log(`[fill] ${selector} would not hold its value — the circuit keeps resetting it`);
  return false;
}

/** Signs in through the real form, tolerating the shared rate-limit window. */
async function signIn(page: Page, role: RoleKey) {
  const user = USERS[role];
  for (let attempt = 1; attempt <= 2; attempt++) {
    if (!(await gotoThrottled(page, '/login', '[data-testid="login-email"]'))) continue;
    await fillStable(page, '[data-testid="login-email"]', user.email);
    await fillStable(page, '[data-testid="login-password"]', user.password);

    // A click that lands before the interactive circuit has wired the button is swallowed in
    // silence — no navigation, no inline error, nothing to wait for. Under load that window is
    // wide, so press the button again rather than waiting out a long timeout on a dead click.
    for (let press = 1; press <= 12; press++) {
      await page.click('[data-testid="login-submit"]').catch(() => {});
      try {
        await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 8000 });
        await page.waitForTimeout(2000);
        return page.url();
      } catch {
        const err = await page.locator('[data-testid="login-error"]').textContent().catch(() => null);
        if (err && err.trim()) {
          console.log(`[signIn] ${role} refused with an inline error: ${err.trim().replace(/\s+/g, ' ')}`);
          break;
        }
        // Re-type both fields: a swallowed submit clears the bound password on some renders.
        await fillStable(page, '[data-testid="login-email"]', user.email);
        await fillStable(page, '[data-testid="login-password"]', user.password);
      }
    }
    console.log(`[signIn] ${role} attempt ${attempt} never navigated — retrying from a fresh page load`);
    await page.evaluate(() => { try { localStorage.clear(); } catch { /* opaque origin */ } });
  }
  throw new Error(`${role} could not sign in after 2 attempts`);
}

/**
 * One cached signed-in session per role, so a test that merely needs to BE a role does not have to
 * spend another slot of the shared /login budget.
 *
 * This is only used by tests whose subject is something other than signing in — the policy matrix,
 * the admin screens, the profile. Every test that actually grades the sign-in itself (the landing
 * routes, the inline error, the open-redirect guard, the forced password change, the audit trail)
 * still drives the real form through `signIn`.
 */
const sessionCache: Partial<Record<RoleKey, Record<string, string>>> = {};

async function useSession(page: Page, role: RoleKey) {
  const cached = sessionCache[role];
  if (!cached) {
    const url = await signIn(page, role);
    sessionCache[role] = await page.evaluate(() =>
      Object.fromEntries(Object.keys(localStorage).map((k) => [k, localStorage.getItem(k) ?? ''])),
    );
    return url;
  }
  // '/' is not one of the rate-limited credential paths, so this hop always gets through.
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  await page.evaluate((kv) => {
    for (const [k, v] of Object.entries(kv)) localStorage.setItem(k, v as string);
  }, cached);
  const ws = page.waitForEvent('websocket', { timeout: 30000 }).catch(() => null);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await ws;
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 20000 }).catch(() => {});
  await page.waitForTimeout(2500);
  return page.url();
}

/** Fills and submits the sign-in form on an already-loaded /login page. */
async function submitLogin(page: Page, email: string, password: string) {
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(2000);
  await fillStable(page, '[data-testid="login-email"]', email);
  await fillStable(page, '[data-testid="login-password"]', password);
  await page.click('[data-testid="login-submit"]');
}

// ---------------------------------------------------------------------------------------------
// REQ-UI-001 — login page, role-aware landing, inline error, open-redirect guard
// ---------------------------------------------------------------------------------------------

test('REQ-UI-001 login page renders every documented control and survives both widths', async ({ page }) => {
  expect(await gotoThrottled(page, '/login', '[data-testid="login-card"]'), '/login reachable').toBe(true);
  await page.waitForTimeout(1500);

  record('/login', [
    await renderCheck(page, 'login card', '[data-testid="login-card"]', 'present'),
    await renderCheck(page, 'email field', '[data-testid="login-email"]', 'present'),
    await renderCheck(page, 'password field', '[data-testid="login-password"]', 'present'),
    await renderCheck(page, 'remember-me checkbox', '[data-testid="login-remember"]', 'present'),
    await renderCheck(page, 'forgot-password link', '[data-testid="login-forgot"]', 'value'),
    await renderCheck(page, 'submit button', '[data-testid="login-submit"]', 'value'),
  ]);

  const v = await bothWidths(page, 'login');
  expect(v.wide.overlaps, 'desktop overlaps').toEqual([]);
  expect(v.narrow.overlaps, 'mobile overlaps').toEqual([]);
  expect(v.wide.hScroll).toBe(0);
  expect(v.narrow.hScroll).toBe(0);
  expect(v.wide.zeroSized).toEqual([]);
  expect(v.narrow.zeroSized).toEqual([]);
});

test('REQ-UI-001 invalid credentials show an inline error and do not authenticate', async ({ page }) => {
  expect(await gotoThrottled(page, '/login', '[data-testid="login-email"]'), '/login reachable').toBe(true);
  await submitLogin(page, 'vall-auth-nobody@techieblog.test', 'WrongPass#9');
  const err = page.locator('[data-testid="login-error"]');
  await expect(err).toBeVisible({ timeout: 20000 });
  const text = (await err.textContent())?.trim() ?? '';
  console.log(`[REQ-UI-001] inline error = "${text}"`);
  expect(text.length).toBeGreaterThan(5);
  expect(page.url()).toContain('/login');

  record('/login (error state)', [
    await renderCheck(page, 'inline error alert', '[data-testid="login-error"]', 'value'),
  ]);
  const v = await bothWidths(page, 'login-error');
  expect(v.wide.overlaps).toEqual([]);
  expect(v.narrow.overlaps).toEqual([]);
});

for (const [roleKey, expected] of Object.entries({
  admin: '/admin',
  editor: '/admin',
  author: '/blogslist',
  contributor: '/',
})) {
  test(`REQ-UI-001 ${roleKey} lands on its own authorised route after sign-in`, async ({ page }) => {
    const url = await signIn(page, roleKey as RoleKey);
    const path = new URL(url).pathname.toLowerCase();
    console.log(`[REQ-UI-001] ${roleKey} landed on ${path}`);
    expect(path).toBe(expected);
    expect(path).not.toContain('access-denied');
    expect(path).not.toContain('change-password');
  });
}

test('REQ-UI-001 open-redirect guard rejects an absolute returnUrl but honours a site-relative one', async ({ page }) => {
  // 1. absolute off-site returnUrl must be discarded -> role landing route
  expect(await gotoThrottled(page, `/login?returnUrl=${encodeURIComponent('https://evil.example.com/steal')}`, '[data-testid="login-email"]'), '/login reachable').toBe(true);
  await submitLogin(page, USERS.admin.email, USERS.admin.password);
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 90000 });
  await page.waitForTimeout(1500);
  console.log(`[REQ-UI-001] absolute returnUrl landed on ${page.url()}`);
  expect(page.url()).toContain('localhost:5399');
  expect(page.url()).not.toContain('evil.example.com');
  expect(new URL(page.url()).pathname.toLowerCase()).toBe('/admin');

  // 2. protocol-relative //evil must also be discarded
  await page.evaluate(() => localStorage.clear());
  expect(await gotoThrottled(page, `/login?returnUrl=${encodeURIComponent('//evil.example.com/steal')}`, '[data-testid="login-email"]'), '/login reachable').toBe(true);
  await submitLogin(page, USERS.admin.email, USERS.admin.password);
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 90000 });
  await page.waitForTimeout(1500);
  console.log(`[REQ-UI-001] protocol-relative returnUrl landed on ${page.url()}`);
  expect(page.url()).not.toContain('evil.example.com');
  expect(new URL(page.url()).pathname.toLowerCase()).toBe('/admin');

  // 3. a genuine site-relative returnUrl is honoured
  await page.evaluate(() => localStorage.clear());
  expect(await gotoThrottled(page, `/login?returnUrl=${encodeURIComponent('/BlogsList')}`, '[data-testid="login-email"]'), '/login reachable').toBe(true);
  await submitLogin(page, USERS.admin.email, USERS.admin.password);
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 90000 });
  await page.waitForTimeout(1500);
  console.log(`[REQ-UI-001] relative returnUrl landed on ${page.url()}`);
  expect(new URL(page.url()).pathname.toLowerCase()).toBe('/blogslist');
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-005 — JWT issuance and claims
// ---------------------------------------------------------------------------------------------

test('REQ-FN-005 sign-in issues a JWT carrying PrimarySid, Name, Email and Role', async ({ page }) => {
  await signIn(page, 'editor');
  const stored = await page.evaluate(() => {
    const out: Record<string, string> = {};
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i)!;
      if (k.startsWith('AccessToken-') || k.startsWith('RefreshToken-')) out[k] = localStorage.getItem(k)!;
    }
    return out;
  });
  const accessKey = Object.keys(stored).find((k) => k.startsWith('AccessToken-'));
  const refreshKey = Object.keys(stored).find((k) => k.startsWith('RefreshToken-'));
  console.log(`[REQ-FN-005] localStorage keys = ${Object.keys(stored).join(', ')}`);
  expect(accessKey, 'access token key present').toBeTruthy();
  expect(refreshKey, 'refresh token key present').toBeTruthy();

  const raw = stored[accessKey!].replace(/^"|"$/g, '');
  const parts = raw.split('.');
  expect(parts.length, 'JWT has three segments').toBe(3);
  const payload = JSON.parse(Buffer.from(parts[1].replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8'));
  console.log(`[REQ-FN-005] JWT payload claims = ${JSON.stringify(payload)}`);

  const flat = JSON.stringify(payload);
  expect(flat, 'primarysid claim').toContain('primarysid');
  expect(flat.toLowerCase(), 'role claim').toContain('role');
  expect(flat).toContain(USERS.editor.email);
  expect(flat).toContain('Editor');
  expect(payload.exp, 'exp present').toBeTruthy();
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-051 — the login audit trail records a FAILED sign-in
// ---------------------------------------------------------------------------------------------

test('REQ-FN-051 a failed sign-in is written to the login audit trail', async ({ page }) => {
  const before = Number(db('SELECT COALESCE(MAX(logid),0) FROM loginlog'));
  const probe = 'vall-auth-fn051@nowhere.invalid';

  expect(await gotoThrottled(page, '/login', '[data-testid="login-email"]'), '/login reachable').toBe(true);
  await submitLogin(page, probe, 'DefinitelyWrong#1');
  await expect(page.locator('[data-testid="login-error"]')).toBeVisible({ timeout: 20000 });
  await page.waitForTimeout(1200);

  const rows = db(
    `SELECT logid||'|'||COALESCE(userid::text,'NULL')||'|'||attemptedemail||'|'||success FROM loginlog WHERE logid > ${before} ORDER BY logid`,
  );
  console.log(`[REQ-FN-051] rows written after logid ${before}:\n${rows}`);
  expect(rows, 'a new audit row exists').not.toBe('');
  const failRow = rows.split('\n').find((r) => r.includes(probe));
  expect(failRow, 'the failed attempt is recorded against the attempted address').toBeTruthy();
  expect(failRow!.endsWith('|false'), `row records success=false (${failRow})`).toBe(true);
  expect(failRow!.split('|')[1], 'unknown address is attributed to NULL user').toBe('NULL');

  // and a SUCCESSFUL sign-in is distinguishable in the same table
  const beforeOk = Number(db('SELECT COALESCE(MAX(logid),0) FROM loginlog'));
  await page.evaluate(() => localStorage.clear());
  await signIn(page, 'author');
  await page.waitForTimeout(800);
  const okRows = db(
    `SELECT logid||'|'||COALESCE(userid::text,'NULL')||'|'||attemptedemail||'|'||success FROM loginlog WHERE logid > ${beforeOk} ORDER BY logid`,
  );
  console.log(`[REQ-FN-051] success rows:\n${okRows}`);
  expect(okRows).toContain(USERS.author.email);
  expect(okRows.trim().endsWith('|true')).toBe(true);
});

// ---------------------------------------------------------------------------------------------
// REQ-NFR-002 — salted, industry-standard password hashing (proved from the database)
// ---------------------------------------------------------------------------------------------

test('REQ-NFR-002 stored credentials are PBKDF2-HMAC-SHA256 with a per-user salt', async () => {
  const rows = db("SELECT userid||'|'||emailid||'|'||loginpass FROM bloguser ORDER BY userid").split('\n');
  console.log(`[REQ-NFR-002] stored credentials:\n${rows.join('\n')}`);
  expect(rows.length).toBeGreaterThanOrEqual(4);

  const salts = new Set<string>();
  for (const row of rows) {
    const [, email, hash] = row.split('|');
    expect(hash, `${email} is a PBKDF2 record`).toMatch(/^PBKDF2-SHA256\$\d+\$[A-Za-z0-9+/=]+\$[A-Za-z0-9+/=]+$/);
    const [, iters, salt, subkey] = hash.split('$');
    expect(Number(iters), `${email} iteration count`).toBeGreaterThanOrEqual(100000);
    expect(Buffer.from(salt, 'base64').length, `${email} salt bytes`).toBeGreaterThanOrEqual(16);
    expect(Buffer.from(subkey, 'base64').length, `${email} subkey bytes`).toBeGreaterThanOrEqual(32);
    // nothing that looks like plaintext or a bare MD5/SHA digest
    expect(hash).not.toMatch(/^[0-9a-f]{32}$/i);
    expect(hash).not.toMatch(/^[0-9a-f]{64}$/i);
    salts.add(salt);
  }
  expect(salts.size, 'every user has its own salt').toBe(rows.length);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-003 / REQ-FN-007 / REQ-NFR-019 — forgot + reset password
// ---------------------------------------------------------------------------------------------

test('REQ-UI-003 forgot-password page renders its controls and returns the generic message', async ({ page }) => {
  expect(await gotoThrottled(page, '/forgot-password', '[data-testid="forgot-password-card"]'), '/forgot-password reachable').toBe(true);
  await page.waitForTimeout(1200);

  record('/forgot-password', [
    await renderCheck(page, 'card', '[data-testid="forgot-password-card"]', 'present'),
    await renderCheck(page, 'email field', '[data-testid="forgot-email"]', 'present'),
    await renderCheck(page, 'submit button', '[data-testid="forgot-submit"]', 'value'),
    await renderCheck(page, 'sign-in link', '[data-testid="forgot-signin"]', 'value'),
  ]);
  const v = await bothWidths(page, 'forgot-password');
  expect(v.wide.overlaps).toEqual([]);
  expect(v.narrow.overlaps).toEqual([]);
  expect(v.wide.hScroll).toBe(0);
  expect(v.narrow.hScroll).toBe(0);

  await fillStable(page, '[data-testid="forgot-email"]', 'vall-auth-unknown@nowhere.invalid');
  await page.click('[data-testid="forgot-submit"]');
  const msg = page.locator('[data-testid="forgot-password-message"]');
  await expect(msg).toBeVisible({ timeout: 20000 });
  const text = (await msg.textContent())?.trim() ?? '';
  console.log(`[REQ-UI-003] unknown-address message = "${text}"`);
  expect(text.toLowerCase(), 'account-enumeration defence').toContain('if an account exists');

  record('/forgot-password (submitted)', [
    await renderCheck(page, 'result alert', '[data-testid="forgot-password-message"]', 'value'),
  ]);
});

test('REQ-UI-003 an unknown reset token shows the invalid-token card, never the form', async ({ page }) => {
  expect(await gotoThrottled(page, '/reset-password/vall-auth-not-a-real-token', '[data-testid="reset-token-invalid"]'), 'invalid-token card shown').toBe(true);
  await page.waitForTimeout(1000);
  await expect(page.locator('[data-testid="reset-password-card"]')).toHaveCount(0);

  record('/reset-password (invalid token)', [
    await renderCheck(page, 'invalid-token card', '[data-testid="reset-token-invalid"]', 'value'),
    await renderCheck(page, 'request-new-link button', '[data-testid="reset-request-new"]', 'value'),
    await renderCheck(page, 'sign-in link', '[data-testid="reset-signin"]', 'value'),
  ]);
  const v = await bothWidths(page, 'reset-invalid');
  expect(v.wide.overlaps).toEqual([]);
  expect(v.narrow.overlaps).toEqual([]);
  expect(v.wide.hScroll).toBe(0);
  expect(v.narrow.hScroll).toBe(0);
});

test('REQ-FN-007 REQ-NFR-019 a reset token is persisted as a DB row and redeems end to end', async ({ page }) => {
  const target = USERS.contributor;
  const seededHash = db("SELECT loginpass FROM bloguser WHERE userid = 4");
  console.log(`[REQ-FN-007] contributor seeded hash before = ${seededHash}`);
  const beforeMax = Number(db('SELECT COALESCE(MAX(tokenid),0) FROM passwordresettoken'));

  // 1. request the reset through the real page
  expect(await gotoThrottled(page, '/forgot-password', '[data-testid="forgot-email"]'), '/forgot-password reachable').toBe(true);
  await page.waitForTimeout(1500);
  await fillStable(page, '[data-testid="forgot-email"]', target.email);
  await page.click('[data-testid="forgot-submit"]');
  await expect(page.locator('[data-testid="forgot-password-message"]')).toBeVisible({ timeout: 20000 });
  await page.waitForTimeout(1500);

  // 2. REQ-NFR-019 — the token is a DURABLE ROW, not an in-memory entry
  const row = db(
    `SELECT tokenid||'|'||userid||'|'||token||'|'||isused||'|'||createdat||'|'||expiresat FROM passwordresettoken WHERE tokenid > ${beforeMax} ORDER BY tokenid DESC LIMIT 1`,
  );
  console.log(`[REQ-NFR-019] persisted reset token row = ${row}`);
  expect(row, 'a passwordresettoken row was written').not.toBe('');
  const [tokenId, userId, token, isUsed] = row.split('|');
  expect(userId, 'token belongs to the contributor account').toBe('4');
  expect(isUsed, 'a freshly issued token is unused').toBe('false');
  expect(token.length).toBeGreaterThan(10);

  // 3. the emailed link opens the reset form
  expect(await gotoThrottled(page, `/reset-password/${token}`, '[data-testid="reset-password-card"]'), 'reset form shown').toBe(true);
  await page.waitForTimeout(1200);
  record('/reset-password (valid token)', [
    await renderCheck(page, 'reset card', '[data-testid="reset-password-card"]', 'present'),
    await renderCheck(page, 'new-password field', '[data-testid="reset-password-new"]', 'present'),
    await renderCheck(page, 'confirm-password field', '[data-testid="reset-password-confirm"]', 'present'),
    await renderCheck(page, 'submit button', '[data-testid="reset-submit"]', 'value'),
  ]);
  const v = await bothWidths(page, 'reset-valid');
  expect(v.wide.overlaps).toEqual([]);
  expect(v.narrow.overlaps).toEqual([]);
  expect(v.wide.hScroll).toBe(0);
  expect(v.narrow.hScroll).toBe(0);

  // 4. REQ-FN-006 on the reset path — a weak password is refused before anything is written
  await fillStable(page, '[data-testid="reset-password-new"]', 'abc');
  await fillStable(page, '[data-testid="reset-password-confirm"]', 'abc');
  await page.click('[data-testid="reset-submit"]');
  const weakMsg = page.locator('[data-testid="reset-password-message"]');
  await expect(weakMsg).toBeVisible({ timeout: 20000 });
  const weakText = (await weakMsg.textContent())?.trim() ?? '';
  console.log(`[REQ-FN-006] reset-path weak password message = "${weakText}"`);
  expect(weakText.toLowerCase()).toMatch(/8 characters|uppercase|number/);
  expect(db(`SELECT loginpass FROM bloguser WHERE userid = 4`), 'hash untouched by the weak attempt').toBe(seededHash);

  // 5. redeem for real, setting the SAME documented password back (only the salt changes)
  await fillStable(page, '[data-testid="reset-password-new"]', target.password);
  await fillStable(page, '[data-testid="reset-password-confirm"]', target.password);
  await page.click('[data-testid="reset-submit"]');
  await expect(weakMsg).toContainText(/reset successfully/i, { timeout: 25000 });
  await page.waitForTimeout(2500);

  const after = db(`SELECT isused FROM passwordresettoken WHERE tokenid = ${tokenId}`);
  console.log(`[REQ-FN-007] token ${tokenId} after redemption, isused = ${after}`);
  expect(after, 'token marked used exactly once').toBe('t');
  const newHash = db('SELECT loginpass FROM bloguser WHERE userid = 4');
  expect(newHash, 'the stored hash was rotated').not.toBe(seededHash);
  expect(newHash).toMatch(/^PBKDF2-SHA256\$/);

  // 6. the replayed token is refused
  expect(await gotoThrottled(page, `/reset-password/${token}`, '[data-testid="reset-token-invalid"]'), 'replayed token refused').toBe(true);
  console.log('[REQ-FN-007] replayed token -> invalid-token card');

  // 7. the account signs in with the reset password
  await page.evaluate(() => localStorage.clear());
  const landed = await signIn(page, 'contributor');
  console.log(`[REQ-FN-007] contributor signed in after reset -> ${landed}`);
  expect(new URL(landed).pathname).toBe('/');

  // 8. restore the seeded hash byte-for-byte so no sibling sees a changed credential
  db(`UPDATE bloguser SET loginpass = '${seededHash}' WHERE userid = 4`);
  expect(db('SELECT loginpass FROM bloguser WHERE userid = 4')).toBe(seededHash);
  console.log('[REQ-FN-007] contributor seeded hash restored');
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-004 + REQ-FN-009 — access-denied and the policy matrix
// ---------------------------------------------------------------------------------------------

/** Navigates and reports whether the destination rendered or the user was denied. */
async function probeRoute(page: Page, route: string): Promise<'ALLOWED' | 'DENIED'> {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  await page.waitForTimeout(2500);
  const denied =
    (await page.locator('[data-testid="access-denied"]').count()) > 0 ||
    page.url().toLowerCase().includes('access-denied');
  return denied ? 'DENIED' : 'ALLOWED';
}

test('REQ-UI-004 an under-privileged user lands on the access-denied page with a role-aware dashboard link', async ({ page }) => {
  await useSession(page, 'author');
  const verdict = await probeRoute(page, '/users'); // AdminOnly
  console.log(`[REQ-UI-004] author -> /users = ${verdict} (url ${page.url()})`);
  expect(verdict).toBe('DENIED');
  await expect(page.locator('[data-testid="access-denied"]')).toBeVisible({ timeout: 20000 });

  const dash = page.locator('[data-testid="access-denied-dashboard"]');
  await expect(dash).toBeVisible();
  const href = await dash.getAttribute('href');
  console.log(`[REQ-UI-004] author dashboard button href = ${href}`);
  expect((href ?? '').toLowerCase()).toContain('/blogslist');

  record('/access-denied', [
    await renderCheck(page, 'access-denied card', '[data-testid="access-denied"]', 'value'),
    await renderCheck(page, 'Go to Home button', '[data-testid="access-denied-home"]', 'value'),
    await renderCheck(page, 'role-aware Go to Dashboard button', '[data-testid="access-denied-dashboard"]', 'value'),
  ]);
  const v = await bothWidths(page, 'access-denied');
  expect(v.wide.overlaps).toEqual([]);
  expect(v.narrow.overlaps).toEqual([]);
  expect(v.wide.hScroll).toBe(0);
  expect(v.narrow.hScroll).toBe(0);
});

test('REQ-UI-004 a Contributor sees no dashboard button because the role has no staff surface', async ({ page }) => {
  await useSession(page, 'contributor');
  const verdict = await probeRoute(page, '/admin'); // EditorOrAbove
  console.log(`[REQ-UI-004] contributor -> /admin = ${verdict} (url ${page.url()})`);
  expect(verdict).toBe('DENIED');
  await expect(page.locator('[data-testid="access-denied"]')).toBeVisible({ timeout: 20000 });
  await expect(page.locator('[data-testid="access-denied-home"]')).toBeVisible();
  expect(await page.locator('[data-testid="access-denied-dashboard"]').count()).toBe(0);
  await page.screenshot({ path: `${SHOTS}/access-denied-contributor-1280.png` });
});

const POLICY_MATRIX: Record<RoleKey, { route: string; policy: string; expect: 'ALLOWED' | 'DENIED' }[]> = {
  admin: [
    { route: '/users', policy: 'AdminOnly', expect: 'ALLOWED' },
    { route: '/admin', policy: 'EditorOrAbove', expect: 'ALLOWED' },
    { route: '/BlogsList', policy: 'AuthorOrAbove', expect: 'ALLOWED' },
  ],
  editor: [
    { route: '/users', policy: 'AdminOnly', expect: 'DENIED' },
    { route: '/settings', policy: 'AdminOnly', expect: 'DENIED' },
    { route: '/admin', policy: 'EditorOrAbove', expect: 'ALLOWED' },
    { route: '/BlogsList', policy: 'AuthorOrAbove', expect: 'ALLOWED' },
  ],
  author: [
    { route: '/users', policy: 'AdminOnly', expect: 'DENIED' },
    { route: '/admin', policy: 'EditorOrAbove', expect: 'DENIED' },
    { route: '/BlogsList', policy: 'AuthorOrAbove', expect: 'ALLOWED' },
  ],
  contributor: [
    { route: '/users', policy: 'AdminOnly', expect: 'DENIED' },
    { route: '/admin', policy: 'EditorOrAbove', expect: 'DENIED' },
    { route: '/BlogsList', policy: 'AuthorOrAbove', expect: 'DENIED' },
  ],
};

for (const roleKey of Object.keys(POLICY_MATRIX) as RoleKey[]) {
  test(`REQ-FN-009 policy gates hold for ${roleKey}`, async ({ page }) => {
    await useSession(page, roleKey);
    const failures: string[] = [];
    for (const probe of POLICY_MATRIX[roleKey]) {
      const actual = await probeRoute(page, probe.route);
      console.log(`[REQ-FN-009] ${roleKey} -> ${probe.route} (${probe.policy}) expected ${probe.expect}, got ${actual}`);
      if (actual !== probe.expect) failures.push(`${roleKey} ${probe.route} (${probe.policy}) expected ${probe.expect} got ${actual}`);
      // return to a route this role can always open before the next probe
      await page.evaluate(() => (window as any).Blazor.navigateTo('/'));
      await page.waitForTimeout(1200);
    }
    expect(failures, failures.join('; ')).toEqual([]);
  });
}

// ---------------------------------------------------------------------------------------------
// REQ-FN-011 + REQ-FN-006 — change password: current-password verification and strength rules
// ---------------------------------------------------------------------------------------------

test('REQ-FN-011 REQ-FN-006 change-password verifies the current password and enforces strength', async ({ page }) => {
  await useSession(page, 'author');
  const hashBefore = db('SELECT loginpass FROM bloguser WHERE userid = 3');

  await nav(page, '/change-password');
  await page.waitForSelector('[data-testid="change-password-card"]', { timeout: 30000 });
  await page.waitForTimeout(1000);

  record('/change-password', [
    await renderCheck(page, 'card', '[data-testid="change-password-card"]', 'present'),
    await renderCheck(page, 'current-password field', '[data-testid="change-password-current"]', 'present'),
    await renderCheck(page, 'new-password field', '[data-testid="change-password-new"]', 'present'),
    await renderCheck(page, 'confirm-password field', '[data-testid="change-password-confirm"]', 'present'),
    await renderCheck(page, 'submit button', '[data-testid="change-password-submit"]', 'value'),
  ]);
  const v = await bothWidths(page, 'change-password');
  expect(v.wide.overlaps).toEqual([]);
  expect(v.narrow.overlaps).toEqual([]);
  expect(v.wide.hScroll).toBe(0);
  expect(v.narrow.hScroll).toBe(0);

  // (a) wrong CURRENT password is refused even though the new one is compliant — REQ-FN-011
  await fillStable(page, '[data-testid="change-password-current"]', 'NotMyPassword#1');
  await fillStable(page, '[data-testid="change-password-new"]', 'BrandNewPass9');
  await fillStable(page, '[data-testid="change-password-confirm"]', 'BrandNewPass9');
  await page.click('[data-testid="change-password-submit"]');
  const msg = page.locator('[data-testid="change-password-message"]');
  await expect(msg).toBeVisible({ timeout: 20000 });
  const wrongText = (await msg.textContent())?.trim() ?? '';
  console.log(`[REQ-FN-011] wrong current password message = "${wrongText}"`);
  expect(wrongText.toLowerCase()).toContain('current password');
  expect(db('SELECT loginpass FROM bloguser WHERE userid = 3'), 'hash untouched').toBe(hashBefore);

  // (b) weak NEW password with the CORRECT current password — REQ-FN-006
  await fillStable(page, '[data-testid="change-password-current"]', USERS.author.password);
  await fillStable(page, '[data-testid="change-password-new"]', 'abc');
  await fillStable(page, '[data-testid="change-password-confirm"]', 'abc');
  await page.click('[data-testid="change-password-submit"]');
  await page.waitForTimeout(1500);
  const weakText = (await msg.textContent())?.trim() ?? '';
  console.log(`[REQ-FN-006] weak new-password message = "${weakText}"`);
  expect(weakText.toLowerCase()).toMatch(/8 characters|uppercase|number/);
  expect(db('SELECT loginpass FROM bloguser WHERE userid = 3'), 'hash untouched by weak attempt').toBe(hashBefore);

  // (c) mismatched confirmation
  await fillStable(page, '[data-testid="change-password-current"]', USERS.author.password);
  await fillStable(page, '[data-testid="change-password-new"]', 'BrandNewPass9');
  await fillStable(page, '[data-testid="change-password-confirm"]', 'DifferentPass9');
  await page.click('[data-testid="change-password-submit"]');
  await page.waitForTimeout(1200);
  console.log(`[REQ-FN-011] mismatch message = "${(await msg.textContent())?.trim()}"`);
  expect(((await msg.textContent()) ?? '').toLowerCase()).toContain('do not match');
  expect(db('SELECT loginpass FROM bloguser WHERE userid = 3')).toBe(hashBefore);
});

test('REQ-FN-011 the profile screen loads the signed-in account and renders its stored values', async ({ page }) => {
  await useSession(page, 'author');
  await nav(page, '/admin/profile');
  await page.waitForSelector('[data-testid="manage-profile-page"]', { timeout: 30000 });
  await page.waitForTimeout(2000);

  const dbRow = db("SELECT firstname||'|'||lastname||'|'||emailid FROM bloguser WHERE userid = 3");
  console.log(`[REQ-FN-011] DB profile row = ${dbRow}`);
  const [firstName, lastName] = dbRow.split('|');

  const results = [
    await renderCheck(page, 'profile page', '[data-testid="manage-profile-page"]', 'present'),
    await renderCheck(page, 'basic info card', '[data-testid="basic-info-card"]', 'present'),
    await renderCheck(page, 'social links card', '[data-testid="social-links-card"]', 'present'),
    await renderCheck(page, 'save button', '[data-testid="save-profile"]', 'value'),
  ];
  const first = await page.locator('[data-testid="first-name-input"]').inputValue().catch(() => '');
  const last = await page.locator('[data-testid="last-name-input"]').inputValue().catch(() => '');
  console.log(`[REQ-FN-011] rendered first/last = "${first}" / "${last}"`);
  results.push({
    control: 'first-name input (loaded value)',
    verdict: first.trim() ? 'RENDERS' : 'RENDER-EMPTY',
    detail: `value "${first}" vs DB "${firstName}"`,
  });
  results.push({
    control: 'last-name input (loaded value)',
    verdict: last.trim() ? 'RENDERS' : 'RENDER-EMPTY',
    detail: `value "${last}" vs DB "${lastName}"`,
  });
  record('/admin/profile', results);
  await page.screenshot({ path: `${SHOTS}/profile-1280.png` });

  expect(first.trim(), 'profile read populates the first-name field').toBe(firstName);
  expect(last.trim(), 'profile read populates the last-name field').toBe(lastName);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-010 — admin user management
// ---------------------------------------------------------------------------------------------

test('REQ-FN-010 admin user management lists, searches, views and changes a role', async ({ page }) => {
  await useSession(page, 'admin');
  await nav(page, '/users', /Users/i);
  await page.waitForSelector('[data-testid="users-grid"]', { timeout: 30000 });
  await page.waitForTimeout(1500);

  const dbCount = Number(db('SELECT COUNT(*) FROM bloguser'));
  const rowCount = await page.locator('[data-testid="user-row-name"]').count();
  console.log(`[REQ-FN-010] grid rows = ${rowCount}, bloguser rows = ${dbCount}`);
  expect(rowCount, 'the grid lists the accounts in the database').toBe(dbCount);

  record('/users', [
    await renderCheck(page, 'user grid', '[data-testid="users-grid"]', 'present'),
    await renderCheck(page, 'row name', '[data-testid="user-row-name"]', 'value'),
    await renderCheck(page, 'row email', '[data-testid="user-row-email"]', 'value'),
    await renderCheck(page, 'row role badge', '[data-testid="user-row-role"]', 'value'),
    await renderCheck(page, 'row status badge', '[data-testid="user-row-status"]', 'value'),
    await renderCheck(page, 'row joined date', '[data-testid="user-row-joined"]', 'value'),
    await renderCheck(page, 'search box', '[data-testid="users-search"]', 'present'),
    await renderCheck(page, 'role tabs', '[data-testid="users-role-tabs"]', 'present'),
    await renderCheck(page, 'result count', '[data-testid="users-count"]', 'value'),
    await renderCheck(page, 'new-user button', '[data-testid="new-user"]', 'value'),
  ]);
  const v = await bothWidths(page, 'users');
  console.log(`[REQ-FN-010] users overlaps wide=${JSON.stringify(v.wide.overlaps)} narrow=${JSON.stringify(v.narrow.overlaps)}`);

  // search narrows the list
  await fillStable(page, '[data-testid="users-search"]', 'contributor');
  await page.waitForTimeout(1500);
  const filtered = await page.locator('[data-testid="user-row-name"]').count();
  console.log(`[REQ-FN-010] rows after searching "contributor" = ${filtered}`);
  expect(filtered).toBeGreaterThan(0);
  expect(filtered).toBeLessThan(dbCount);
  await fillStable(page, '[data-testid="users-search"]', '');
  await page.waitForTimeout(1200);

  // change role round-trip on user 4 only (restored immediately)
  const roleBefore = db('SELECT userrole FROM bloguser WHERE userid = 4');
  await fillStable(page, '[data-testid="users-search"]', 'contributor');
  await page.waitForTimeout(1500);
  await page.locator('[data-testid="user-change-role"]').first().click();
  await expect(page.locator('[data-testid="user-role-dialog"]')).toBeVisible({ timeout: 15000 });
  const target = (await page.locator('[data-testid="user-role-target"]').textContent())?.trim();
  console.log(`[REQ-FN-010] role dialog target = "${target}"`);
  expect(target && target.length > 0).toBe(true);
  await page.screenshot({ path: `${SHOTS}/users-role-dialog-1280.png` });

  await page.locator('[data-testid="user-role-select"]').click();
  await page.waitForTimeout(600);
  // The Select's options render in a portal OUTSIDE the dialog element, so scope the search to
  // the page and take the visible one.
  await page.locator('[role="option"], [data-slot="select-item"]')
    .filter({ hasText: /^Author$/ })
    .first()
    .click({ timeout: 15000 })
    .catch(async () => {
      await page.getByText('Author', { exact: true }).last().click({ timeout: 15000 });
    });
  await page.waitForTimeout(400);
  await page.locator('[data-testid="user-role-save"]').click();
  await page.waitForTimeout(2500);
  const roleAfter = db('SELECT userrole FROM bloguser WHERE userid = 4');
  console.log(`[REQ-FN-010] role ${roleBefore} -> ${roleAfter}`);
  db(`UPDATE bloguser SET userrole = '${roleBefore}' WHERE userid = 4`);
  console.log(`[REQ-FN-010] role restored to ${db('SELECT userrole FROM bloguser WHERE userid = 4')}`);
  expect(roleAfter, 'the change-role backend persisted the new role').toBe('Author');
});

test('REQ-FN-006 the admin account-creation form refuses a weak password and creates nothing', async ({ page }) => {
  await useSession(page, 'admin');
  const before = Number(db('SELECT COUNT(*) FROM bloguser'));
  await nav(page, '/AddUser');
  await page.waitForSelector('[data-testid="add-user-form"]', { timeout: 30000 });
  await page.waitForTimeout(1200);

  record('/AddUser', [
    await renderCheck(page, 'form', '[data-testid="add-user-form"]', 'present'),
    await renderCheck(page, 'first name', '[data-testid="user-first-name"]', 'present'),
    await renderCheck(page, 'last name', '[data-testid="user-last-name"]', 'present'),
    await renderCheck(page, 'email', '[data-testid="user-email"]', 'present'),
    await renderCheck(page, 'password', '[data-testid="user-password"]', 'present'),
    await renderCheck(page, 'confirm password', '[data-testid="user-confirm-password"]', 'present'),
    await renderCheck(page, 'role select', '[data-testid="user-role"]', 'present'),
    await renderCheck(page, 'submit', '[data-testid="add-user-submit"]', 'value'),
  ]);

  await fillStable(page, '[data-testid="user-first-name"]', 'Weak');
  await fillStable(page, '[data-testid="user-last-name"]', 'Probe');
  await fillStable(page, '[data-testid="user-email"]', 'vall-auth-weak-probe@techieblog.test');
  await fillStable(page, '[data-testid="user-password"]', 'abc');
  await fillStable(page, '[data-testid="user-confirm-password"]', 'abc');
  await page.click('[data-testid="add-user-submit"]');
  const status = page.locator('[data-testid="add-user-status-message"]');
  await expect(status).toBeVisible({ timeout: 20000 });
  const text = (await status.textContent())?.trim() ?? '';
  console.log(`[REQ-FN-006] AddUser weak-password message = "${text}"`);
  expect(text.toLowerCase()).toMatch(/8 characters|uppercase|number/);

  const after = Number(db('SELECT COUNT(*) FROM bloguser'));
  console.log(`[REQ-FN-006] bloguser count ${before} -> ${after}`);
  expect(after, 'no account was created by the refused submission').toBe(before);
  await page.screenshot({ path: `${SHOTS}/adduser-weak-1280.png` });
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-055 + REQ-FN-048 — double opt-in email verification
// ---------------------------------------------------------------------------------------------

const FIXTURE_EMAIL = 'vall-auth-optin@techieblog.test';

test('REQ-UI-055 REQ-FN-048 the verify landing page confirms once, replays as already-confirmed, and refuses expired/unknown tokens', async ({ page }) => {
  // --- fixtures: a pending subscriber plus three tokens (valid, expired, and one already used)
  db(`DELETE FROM emailverificationtoken WHERE email = '${FIXTURE_EMAIL}'`);
  db(`DELETE FROM subscriber WHERE email = '${FIXTURE_EMAIL}'`);
  const subId = firstLine(db(
    `INSERT INTO subscriber (email, name, subscribedon, isconfirmed) VALUES ('${FIXTURE_EMAIL}', 'Vall Auth Probe', NOW(), false) RETURNING subscriberid`,
  ));
  const good = `vallauthgood${Date.now()}`;
  const stale = `vallauthexpired${Date.now()}`;
  db(
    `INSERT INTO emailverificationtoken (token, email, purpose, targetid, displayname, issuedon, expireson, isused) VALUES ('${good}', '${FIXTURE_EMAIL}', 'Subscription', ${subId}, 'Vall Auth Probe', NOW(), NOW() + INTERVAL '24 hours', false)`,
  );
  db(
    `INSERT INTO emailverificationtoken (token, email, purpose, targetid, displayname, issuedon, expireson, isused) VALUES ('${stale}', '${FIXTURE_EMAIL}', 'Subscription', ${subId}, 'Vall Auth Probe', NOW() - INTERVAL '30 hours', NOW() - INTERVAL '6 hours', false)`,
  );
  console.log(`[REQ-FN-048] fixtures: subscriber ${subId}, tokens ${good} (valid) / ${stale} (expired)`);

  try {
    // --- 1. unknown token
    await page.goto(`${BASE}/verify/vall-auth-token-that-never-existed`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="verify-expired"]', { timeout: 30000 });
    console.log('[REQ-UI-055] unknown token -> verify-expired state');
    record('/verify (unknown token)', [
      await renderCheck(page, 'verify card', '[data-testid="verify-card"]', 'present'),
      await renderCheck(page, 'invalid/expired panel', '[data-testid="verify-expired"]', 'value'),
      await renderCheck(page, 'explanatory alert', '[data-testid="verify-expired-alert"]', 'value'),
    ]);
    const vu = await bothWidths(page, 'verify-unknown');
    expect(vu.wide.overlaps).toEqual([]);
    expect(vu.narrow.overlaps).toEqual([]);
    expect(vu.wide.hScroll).toBe(0);
    expect(vu.narrow.hScroll).toBe(0);

    // --- 2. expired token: refused, and the pending row is NOT promoted
    await page.goto(`${BASE}/verify/${stale}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="verify-expired"]', { timeout: 30000 });
    await page.waitForTimeout(1200);
    const expiredState = db(`SELECT isused FROM emailverificationtoken WHERE token = '${stale}'`);
    const subAfterExpired = db(`SELECT isconfirmed FROM subscriber WHERE subscriberid = ${subId}`);
    console.log(`[REQ-FN-048] expired token isused=${expiredState}, subscriber isconfirmed=${subAfterExpired}`);
    expect(expiredState).toBe('f');
    expect(subAfterExpired).toBe('f');
    await page.screenshot({ path: `${SHOTS}/verify-expired-1280.png` });

    // --- 3. valid token: confirms exactly once and says WHAT was confirmed
    await page.goto(`${BASE}/verify/${good}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="verify-subscribed"], [data-testid="verify-confirmed"]', { timeout: 30000 });
    await page.waitForTimeout(1500);
    const purposeText = (await page.locator('[data-testid="verify-purpose"]').first().textContent())?.trim() ?? '';
    console.log(`[REQ-UI-055] confirmed purpose text = "${purposeText}"`);
    expect(purposeText.toLowerCase()).toContain('subscri');

    const tokenAfter = db(`SELECT isused||'|'||COALESCE(consumedon::text,'NULL') FROM emailverificationtoken WHERE token = '${good}'`);
    const subAfter = db(`SELECT isconfirmed FROM subscriber WHERE subscriberid = ${subId}`);
    console.log(`[REQ-FN-048] valid token after redemption = ${tokenAfter}, subscriber isconfirmed = ${subAfter}`);
    expect(tokenAfter.startsWith('true'), 'token is single-use and marked consumed').toBe(true);
    expect(tokenAfter.split('|')[1], 'consumedon stamped').not.toBe('NULL');
    expect(subAfter, 'the pending row was promoted').toBe('t');

    record('/verify (valid token)', [
      await renderCheck(page, 'verify card', '[data-testid="verify-card"]', 'present'),
      await renderCheck(page, 'subscription-confirmed panel', '[data-testid="verify-subscribed"]', 'value'),
      await renderCheck(page, 'purpose statement', '[data-testid="verify-purpose"]', 'value'),
      await renderCheck(page, 'success alert', '[data-testid="verify-success-alert"]', 'value'),
      await renderCheck(page, 'actions row', '[data-testid="verify-actions"]', 'present'),
    ]);
    const vv = await bothWidths(page, 'verify-confirmed');
    expect(vv.wide.overlaps).toEqual([]);
    expect(vv.narrow.overlaps).toEqual([]);
    expect(vv.wide.hScroll).toBe(0);
    expect(vv.narrow.hScroll).toBe(0);

    // --- 4. replay: already-confirmed, nothing consumed twice
    db(`UPDATE subscriber SET isconfirmed = false WHERE subscriberid = ${subId}`);
    await page.goto(`${BASE}/verify/${good}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="verify-already"]', { timeout: 30000 });
    await page.waitForTimeout(1200);
    const subAfterReplay = db(`SELECT isconfirmed FROM subscriber WHERE subscriberid = ${subId}`);
    console.log(`[REQ-FN-048] after replay, subscriber isconfirmed = ${subAfterReplay} (must stay f)`);
    expect(subAfterReplay, 'a replayed token confirms nothing a second time').toBe('f');
    record('/verify (replayed token)', [
      await renderCheck(page, 'already-verified panel', '[data-testid="verify-already"]', 'value'),
      await renderCheck(page, 'already alert', '[data-testid="verify-already-alert"]', 'value'),
    ]);
    await page.screenshot({ path: `${SHOTS}/verify-already-1280.png` });

    // --- 5. persistence shape (REQ-FN-048 / cf. REQ-NFR-019): tokens are DB rows with a 24 h window
    const shape = db(
      `SELECT (expireson - issuedon)::text FROM emailverificationtoken WHERE token = '${good}'`,
    );
    console.log(`[REQ-FN-048] token lifetime column value = ${shape}`);
    // PostgreSQL renders a 24-hour interval as "1 day", not "24:00:00".
    expect(shape === '1 day' || shape.includes('24:00:00'), `24 h window, got "${shape}"`).toBe(true);
  } finally {
    db(`DELETE FROM emailverificationtoken WHERE email = '${FIXTURE_EMAIL}'`);
    db(`DELETE FROM subscriber WHERE email = '${FIXTURE_EMAIL}'`);
    console.log('[REQ-FN-048] fixtures removed');
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-NFR-023 — seeded credential hashed + forced first-login change (user 4 ONLY)
// ---------------------------------------------------------------------------------------------

test('REQ-NFR-023 a seeded account flagged MustChangePassword is forced to the change screen', async ({ page }) => {
  // half 1: the seeded credential is a hash, not plaintext (site owner)
  const ownerHash = db("SELECT loginpass FROM bloguser WHERE issiteowner = true");
  console.log(`[REQ-NFR-023] site-owner stored credential = ${ownerHash}`);
  expect(ownerHash).toMatch(/^PBKDF2-SHA256\$\d+\$/);
  expect(ownerHash).not.toContain('admin_password');

  // half 2: the forced change actually gates — exercised on user 4 only
  db('UPDATE bloguser SET mustchangepassword = true WHERE userid = 4');
  try {
    expect(db('SELECT mustchangepassword FROM bloguser WHERE userid = 4')).toBe('t');
    const landed = await signIn(page, 'contributor');
    console.log(`[REQ-NFR-023] flagged contributor landed on ${landed}`);
    expect(new URL(landed).pathname.toLowerCase()).toBe('/change-password');
    await expect(page.locator('[data-testid="change-password-card"]')).toBeVisible({ timeout: 20000 });
    await expect(page.locator('[data-testid="change-password-forced"]')).toBeVisible({ timeout: 20000 });
    const forcedCopy = (await page.locator('[data-testid="change-password-forced"]').textContent())?.trim();
    console.log(`[REQ-NFR-023] forced copy = "${forcedCopy}"`);
    expect((forcedCopy ?? '').length).toBeGreaterThan(20);

    record('/change-password (forced)', [
      await renderCheck(page, 'forced-change explanation', '[data-testid="change-password-forced"]', 'value'),
      await renderCheck(page, 'card', '[data-testid="change-password-card"]', 'present'),
    ]);
    const v = await bothWidths(page, 'change-password-forced');
    expect(v.wide.overlaps).toEqual([]);
    expect(v.narrow.overlaps).toEqual([]);
    expect(v.wide.hScroll).toBe(0);
    expect(v.narrow.hScroll).toBe(0);

    // the guard bounces every other destination back
    for (const route of ['/', '/BlogsList']) {
      await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
      await page.waitForTimeout(2500);
      console.log(`[REQ-NFR-023] navigating to ${route} while flagged -> ${new URL(page.url()).pathname}`);
      expect(new URL(page.url()).pathname.toLowerCase()).toBe('/change-password');
    }
  } finally {
    db('UPDATE bloguser SET mustchangepassword = false WHERE userid = 4');
    console.log(`[REQ-NFR-023] user 4 flag restored to ${db('SELECT mustchangepassword FROM bloguser WHERE userid = 4')}`);
  }
});

test.afterAll(() => {
  console.log('=== §4a DATA-RENDER SUMMARY ===');
  console.log(JSON.stringify(observations, null, 1));
});
