/**
 * vall-auth-zz-ratelimit.spec.ts — REQ-NFR-005, run LAST and on its own.
 *
 * Two independent throttles are claimed by the implementation and both are exercised here:
 *   1. the HTTP fixed-window limiter in Program.cs (10 requests / 60 s per client IP, only on the
 *      credential paths) — proved by driving raw HTTP requests until a 429 comes back;
 *   2. BlogEngine.Common.LoginThrottle (5 failures / 15 min per ACCOUNT key, then a 15-minute
 *      lockout) — proved through the real sign-in form against an address that owns no account, so
 *      no seeded credential is ever locked out for the sibling clusters.
 *
 * The per-account lockout is invisible in the audit table for an unknown address (both the
 * wrong-password arm and the throttle-refused arm write userid NULL, success false, by design), so
 * it is proved from the application's own Serilog output, which distinguishes them explicitly.
 */
import { test, expect, request, Page } from '@playwright/test';
import { execFileSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { BASE } from './_gates';

const LOG_DIR = 'source/TechieBlog/logs';

function db(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql],
    { encoding: 'utf8' },
  ).trim();
}

/** Reads the tail of the newest Serilog rolling file. */
function recentLog(bytes = 400000): string {
  const files = fs
    .readdirSync(LOG_DIR)
    .filter((f) => f.endsWith('.log'))
    .map((f) => ({ f, m: fs.statSync(path.join(LOG_DIR, f)).mtimeMs }))
    .sort((a, b) => b.m - a.m);
  if (files.length === 0) return '';
  const full = path.join(LOG_DIR, files[0].f);
  const size = fs.statSync(full).size;
  const start = Math.max(0, size - bytes);
  const fd = fs.openSync(full, 'r');
  const buf = Buffer.alloc(size - start);
  fs.readSync(fd, buf, 0, buf.length, start);
  fs.closeSync(fd);
  return buf.toString('utf8');
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

test('REQ-NFR-005 the per-account login throttle locks an address out after repeated failures', async ({ page }) => {
  test.setTimeout(240000);
  // A throwaway ADDRESS (not an account) so only that throttle partition is locked.
  const probe = `vall-auth-rl-${Date.now()}@nowhere.invalid`;
  const before = Number(db('SELECT COALESCE(MAX(logid),0) FROM loginlog'));

  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(2500);

  let registered = 0;
  for (let attempt = 1; attempt <= 8; attempt++) {
    await fillStable(page, '[data-testid="login-email"]', probe);
    await fillStable(page, '[data-testid="login-password"]', `Wrong#${attempt}`);
    const rowsBefore = Number(db(`SELECT COUNT(*) FROM loginlog WHERE attemptedemail = '${probe}'`));
    await page.click('[data-testid="login-submit"]');
    await expect(page.locator('[data-testid="login-error"]')).toBeVisible({ timeout: 20000 });
    await page.waitForTimeout(900);
    const rowsAfter = Number(db(`SELECT COUNT(*) FROM loginlog WHERE attemptedemail = '${probe}'`));
    if (rowsAfter > rowsBefore) registered++;
    console.log(`[REQ-NFR-005] attempt ${attempt}: audit rows ${rowsBefore} -> ${rowsAfter}`);
  }

  const rows = db(
    `SELECT logid||'|'||COALESCE(userid::text,'NULL')||'|'||success FROM loginlog WHERE logid > ${before} AND attemptedemail = '${probe}' ORDER BY logid`,
  );
  const lines = rows ? rows.split('\n') : [];
  console.log(`[REQ-NFR-005] audit rows for ${probe} (${lines.length}):\n${rows}`);
  expect(registered, 'the form actually submitted often enough to trip the throttle').toBeGreaterThanOrEqual(6);
  expect(lines.every((l) => l.endsWith('|false')), 'none of them succeeded').toBe(true);

  // The decisive signal: AuthSvc logs "Failed login attempt N" while the credential is checked and
  // "Login refused ... account locked" once LoginThrottle starts refusing before any DB work.
  const log = recentLog();
  const failures = (log.match(new RegExp(`Failed login attempt \\d+ for ${probe}`, 'g')) ?? []).length;
  const refusals = (log.match(new RegExp(`Login refused for ${probe}`, 'g')) ?? []).length;
  console.log(`[REQ-NFR-005] Serilog: ${failures} "Failed login attempt" lines, ${refusals} "Login refused (locked)" lines`);
  const sample = (log.split('\n').filter((l) => l.includes(probe)).slice(-3) ?? []).join('\n');
  console.log(`[REQ-NFR-005] last log lines:\n${sample}`);

  expect(failures, 'the throttle counted the failures').toBeGreaterThanOrEqual(5);
  expect(refusals, 'the throttle actually refused further attempts (lockout engaged)').toBeGreaterThanOrEqual(1);
  console.log('[REQ-NFR-005] LoginThrottle: MaxFailuresPerWindow=5, FailureWindowMinutes=15, LockoutMinutes=15');
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
