/**
 * vall-auth-zz-ratelimit.spec.ts — REQ-NFR-005, run LAST and on its own.
 *
 * Two independent throttles are claimed by the implementation and both are exercised here:
 *   1. the HTTP fixed-window limiter in Program.cs (10 requests / 60 s per client IP, only on the
 *      credential paths) — proved by driving raw HTTP requests until a 429 comes back;
 *   2. BlogEngine.Common.LoginThrottle (5 failures / 15 min per ACCOUNT key, then a 15-minute
 *      lockout) — proved through the real sign-in form against an account THIS SPEC creates
 *      (`fxv8-lock-…`, removed through /users afterwards), so no seeded credential is ever locked out.
 *
 * 2026-10-28 (T2 triage): the lockout used to be proved from the host's Serilog file
 * (`source/TechieBlog/logs`), which this environment does not have. For a real account the audit table
 * itself tells the arms apart (wrong password = the account's userid, throttle refusal = userid NULL),
 * and the lockout is visible on the form: the correct password is refused too.
 */
import { test, expect, request, Page } from '@playwright/test';
import { execFileSync } from 'child_process';
import { BASE } from './_gates';
import { loginHard, goTo, fillCommitted, pickSelect } from './vall-authoring-helpers';

function db(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql],
    { encoding: 'utf8' },
  ).trim();
}

/** Blazor wipes a prerendered input when the circuit attaches; re-type until the value holds. */
async function fillStable(page: Page, selector: string, value: string) {
  for (let attempt = 1; attempt <= 15; attempt++) {
    await page.fill(selector, value).catch(() => {});
    await page.waitForTimeout(600);
    if ((await page.inputValue(selector).catch(() => null)) === value) return true;
  }
  return false;
}

/** Creates an admin-added account through /AddUser (signed in as admin on `admin`). */
async function createLockAccount(admin: Page, email: string, password: string) {
  await goTo(admin, '/AddUser', '[data-testid="add-user-form"]', 120000);
  await fillCommitted(admin, 'user-first-name', 'Fxv8');
  await fillCommitted(admin, 'user-last-name', 'Lockout');
  await fillCommitted(admin, 'user-email', email);
  await fillCommitted(admin, 'user-password', password);
  await fillCommitted(admin, 'user-confirm-password', password);
  await pickSelect(admin, 'user-role', /^Contributor$/);
  await admin.click('[data-testid="add-user-submit"]');
  const end = Date.now() + 60000;
  while (Date.now() < end && db(`SELECT COUNT(*) FROM bloguser WHERE emailid = '${email}'`) !== '1') await admin.waitForTimeout(1500);
  expect(db(`SELECT COUNT(*) FROM bloguser WHERE emailid = '${email}'`), 'the lockout fixture account was created').toBe('1');
}

/** Deletes the account through the /users grid (the app soft-deletes). */
async function deleteLockAccount(admin: Page, email: string) {
  await goTo(admin, '/users', '[data-testid="users-grid"]', 120000);
  await admin.fill('[data-testid="users-search"]', email);
  await admin.dispatchEvent('[data-testid="users-search"]', 'change');
  await admin.waitForTimeout(2000);
  await admin.locator('tbody tr').filter({ hasText: email }).locator('[data-testid="user-delete"]').click();
  await admin.click('[data-testid="user-delete-confirm"]');
  await admin.waitForTimeout(4000);
  expect(db(`SELECT isdeleted FROM bloguser WHERE emailid = '${email}'`), 'the lockout fixture account was removed through the UI').toBe('t');
}

test('REQ-NFR-005 the per-account login throttle locks an address out after repeated failures', async ({ page, browser }) => {
  test.setTimeout(540000);
  // The lockout is proved on an account THIS TEST creates (`fxv8-lock-…`, deleted at the end), so no
  // seeded credential is ever locked for the sibling specs. Observable signals, all black box:
  //   - after the failures a CORRECT password is still refused (the form shows its error, no sign-in);
  //   - the audit trail tells the two arms apart for a real account: a wrong password is written with
  //     the account's userid, a refusal by the throttle is written with userid NULL.
  const email = `fxv8-lock-${Date.now().toString(36)}@example.com`;
  const goodPw = 'Fxv8#Lockout2026';
  const adminCtx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const admin = await adminCtx.newPage();
  try {
    await loginHard(admin, 'admin');
    await createLockAccount(admin, email, goodPw);
    const userId = db(`SELECT userid FROM bloguser WHERE emailid = '${email}'`);
    const before = Number(db('SELECT COALESCE(MAX(logid),0) FROM loginlog'));

    // /login is on the shared 10-requests-per-minute limiter, so wait out a 429 before the first load
    for (let attempt = 1; attempt <= 6; attempt++) {
      const res = await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
      if (res?.status() !== 429) break;
      await page.waitForTimeout(12000);
    }
    await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
    await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 }).catch(() => {});
    await page.waitForTimeout(2500);

    let registered = 0;
    for (let attempt = 1; attempt <= 8; attempt++) {
      await fillStable(page, '[data-testid="login-email"]', email);
      await fillStable(page, '[data-testid="login-password"]', `Wrong#${attempt}`);
      const rowsBefore = Number(db(`SELECT COUNT(*) FROM loginlog WHERE attemptedemail = '${email}'`));
      await page.click('[data-testid="login-submit"]');
      await expect(page.locator('[data-testid="login-error"]')).toBeVisible({ timeout: 20000 });
      await page.waitForTimeout(900);
      const rowsAfter = Number(db(`SELECT COUNT(*) FROM loginlog WHERE attemptedemail = '${email}'`));
      if (rowsAfter > rowsBefore) registered++;
      console.log(`[REQ-NFR-005] attempt ${attempt}: audit rows ${rowsBefore} -> ${rowsAfter}`);
    }
    expect(registered, 'the form actually submitted often enough to trip the throttle').toBeGreaterThanOrEqual(6);

    // the decisive check: the CORRECT password is now refused too
    await fillStable(page, '[data-testid="login-email"]', email);
    await fillStable(page, '[data-testid="login-password"]', goodPw);
    await page.click('[data-testid="login-submit"]');
    await expect(page.locator('[data-testid="login-error"]'), 'a locked address is refused even with the right password').toBeVisible({ timeout: 20000 });
    await page.waitForTimeout(1500);
    expect(new URL(page.url()).pathname.toLowerCase(), 'the locked address did not sign in').toContain('login');

    const rows = db(
      `SELECT logid||'|'||COALESCE(userid::text,'NULL')||'|'||success FROM loginlog WHERE logid > ${before} AND attemptedemail = '${email}' ORDER BY logid`,
    );
    const lines = rows ? rows.split('\n') : [];
    console.log(`[REQ-NFR-005] audit rows for ${email} (${lines.length}):\n${rows}`);
    expect(lines.every((l) => l.endsWith('|false')), 'none of them succeeded, the correct-password attempt included').toBe(true);
    const counted = lines.filter((l) => l.split('|')[1] === userId).length;
    const refused = lines.filter((l) => l.split('|')[1] === 'NULL').length;
    console.log(`[REQ-NFR-005] wrong-password rows (userid ${userId}) = ${counted}; throttle-refused rows (userid NULL) = ${refused}`);
    expect(counted, 'the throttle counted the failures (MaxFailuresPerWindow = 5)').toBeGreaterThanOrEqual(5);
    expect(refused, 'the throttle actually refused further attempts (lockout engaged)').toBeGreaterThanOrEqual(1);
    console.log('[REQ-NFR-005] LoginThrottle: MaxFailuresPerWindow=5, FailureWindowMinutes=15, LockoutMinutes=15');
  } finally {
    await deleteLockAccount(admin, email).catch((e) => console.log(`[REQ-NFR-005] fixture cleanup failed: ${String(e).slice(0, 120)}`));
    await adminCtx.close();
  }
});

test('REQ-NFR-005 the HTTP limiter returns 429 on the credential paths and leaves other paths alone', async () => {
  const ctx = await request.newContext({ baseURL: BASE, ignoreHTTPSErrors: true });

  // an un-throttled path first — it must never 429 no matter how often it is hit
  const controlCodes: number[] = [];
  for (let i = 0; i < 15; i++) controlCodes.push((await ctx.get('/health')).status());
  console.log(`[REQ-NFR-005] /health status codes = ${controlCodes.join(',')}`);
  expect(controlCodes.filter((c) => c === 429).length, 'health probe is exempt').toBe(0);

  // now the credential path: PermitLimit 10 per 60 s per IP
  const codes: number[] = [];
  let retryAfter: string | null = null;
  for (let i = 0; i < 16; i++) {
    const res = await ctx.get('/forgot-password');
    codes.push(res.status());
    if (res.status() === 429 && !retryAfter) retryAfter = res.headers()['retry-after'] ?? null;
    if (codes.filter((c) => c === 429).length >= 2) break;
  }
  console.log(`[REQ-NFR-005] /forgot-password status codes = ${codes.join(',')} (Retry-After: ${retryAfter})`);
  const firstReject = codes.indexOf(429);
  expect(firstReject, 'the limiter rejected a credential-path request').toBeGreaterThan(-1);
  expect(retryAfter, 'a Retry-After header is returned').toBeTruthy();

  await ctx.dispose();
  console.log('[REQ-NFR-005] NOTE: /login,/logout,/register,/forgot-password,/reset-password,/change-password are throttled per client IP for the next 60 s window.');
});
