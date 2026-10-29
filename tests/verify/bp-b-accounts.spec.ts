/**
 * bp-b-accounts.spec.ts — cluster B build-phase smoke (2026-10-27) for the account rows:
 *   REQ-FN-006  password strength on staff creation + reset (refused with rules shown, compliant
 *               accepted, duplicate email refused)
 *   REQ-FN-011  profile read/update + change password (wrong current refused, right one succeeds)
 *   REQ-FN-029  username uniqueness / URL-safety on the profile page, single site-owner flag
 *
 * The one account this spec creates is `fxb-user@example.com` (Author), through the admin's own
 * /AddUser form; it is deleted in `finally`. Seeded accounts are never modified.
 */
import { test, expect, Page } from '@playwright/test';
import { execSync } from 'child_process';
import { loginHard, goTo, fillCommitted, pickSelect } from './vall-authoring-helpers';

const BASE = process.env.TB_BASE ?? 'http://localhost:5402';
const SHOTS = 'tests/.artifacts/build-phase-20261027/b';
const EMAIL = 'fxb-user@example.com';
const PW_FIRST = 'Fxb#Start2026';
const PW_RESET = 'Fxb#Reset2026';
const PW_FINAL = 'Fxb#Final2026';

function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

async function signInAs(page: Page, email: string, password: string): Promise<string> {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
  await page.waitForFunction(() => { const f = document.querySelector('form'); return !!f && !f.hasAttribute('action'); }, { timeout: 120000 });
  await page.waitForTimeout(1000);
  await page.fill('[data-testid="login-email"]', email);
  await page.fill('[data-testid="login-password"]', password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForTimeout(6000);
  return page.url();
}

async function shots(page: Page, name: string) {
  for (const [w, h] of [[1280, 900], [390, 844]] as const) {
    await page.setViewportSize({ width: w, height: h });
    await page.waitForTimeout(700);
    expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1), `${name} @${w} scrolls horizontally`).toBe(false);
    await page.screenshot({ path: `${SHOTS}/${name}-${w}.png` });
  }
  await page.setViewportSize({ width: 1280, height: 900 });
}

test.describe.configure({ mode: 'serial' });

test.afterAll(() => {
  sql(`DELETE FROM passwordresettoken WHERE userid IN (SELECT userid FROM bloguser WHERE emailid = '${EMAIL}')`);
  sql(`DELETE FROM userlogins WHERE userid IN (SELECT userid FROM bloguser WHERE emailid = '${EMAIL}')`);
  sql(`DELETE FROM loginlog WHERE userid IN (SELECT userid FROM bloguser WHERE emailid = '${EMAIL}')`);
  sql(`DELETE FROM bloguser WHERE emailid = '${EMAIL}'`);
});

test.skip('superseded by w2-accounts (psql writes): REQ-FN-006 admin creation refuses weak and duplicate, accepts a compliant password', async ({ browser }) => {
  test.setTimeout(400000);
  sql(`DELETE FROM bloguser WHERE emailid = '${EMAIL}'`);
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await loginHard(page, 'admin');
  await goTo(page, '/AddUser', '[data-testid="add-user-form"]', 90000);
  const before = Number(sql('SELECT count(*) FROM bloguser')[0]);
  const status = page.locator('[data-testid="add-user-status-message"]');
  const fill = async (pw: string, confirm = pw, email = EMAIL) => {
    await fillCommitted(page, 'user-first-name', 'Fxb');
    await fillCommitted(page, 'user-last-name', 'Tester');
    await fillCommitted(page, 'user-email', email);
    await fillCommitted(page, 'user-password', pw);
    await fillCommitted(page, 'user-confirm-password', confirm);
  };

  // weak: refused, rules shown inline, nothing created
  await fill('abc');
  await pickSelect(page, 'user-role', 'Author');
  await page.click('[data-testid="add-user-submit"]');
  await expect(status).toBeVisible({ timeout: 30000 });
  const weak = ((await status.textContent()) || '').trim();
  expect(weak).toMatch(/at least 8 characters/i);
  expect(weak).toMatch(/uppercase/i);
  expect(weak).toMatch(/number/i);
  expect(Number(sql('SELECT count(*) FROM bloguser')[0])).toBe(before);
  await shots(page, 'fn006-adduser-weak');

  // compliant: accepted; the stored hash is PBKDF2, never the plaintext
  await fill(PW_FIRST);
  await page.click('[data-testid="add-user-submit"]');
  await expect(status).toContainText(/created successfully/i, { timeout: 30000 });
  const row = sql(`SELECT userrole, loginpass, mustchangepassword FROM bloguser WHERE emailid = '${EMAIL}'`)[0].split('|');
  expect(row[0]).toBe('Author');
  expect(row[1]).toMatch(/^PBKDF2-SHA256\$/);
  expect(row[1]).not.toContain(PW_FIRST);
  console.log(`FN-006 created account: role=${row[0]} mustChange=${row[2]}`);

  // duplicate email: refused, count unchanged
  const afterCreate = Number(sql('SELECT count(*) FROM bloguser')[0]);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/users'));
  await page.waitForTimeout(2500);
  await goTo(page, '/AddUser', '[data-testid="add-user-form"]', 90000);
  await fill(PW_FIRST);
  await pickSelect(page, 'user-role', 'Author');
  await page.click('[data-testid="add-user-submit"]');
  await expect(status).toBeVisible({ timeout: 30000 });
  expect(((await status.textContent()) || '').toLowerCase()).toMatch(/already|exists|in use|taken/);
  expect(Number(sql('SELECT count(*) FROM bloguser')[0])).toBe(afterCreate);
  await page.close();
});

test.skip('superseded by w2-accounts (psql writes): REQ-FN-006 reset path refuses a weak password with the rules shown and accepts a compliant one', async ({ browser }) => {
  test.setTimeout(300000);
  const userId = sql(`SELECT userid FROM bloguser WHERE emailid = '${EMAIL}'`)[0];
  const token = `fxbresettoken${Date.now()}`;
  sql(`INSERT INTO passwordresettoken (userid, token, createdat, expiresat, isused) VALUES (${userId}, '${token}', now() at time zone 'utc', now() at time zone 'utc' + interval '1 hour', false)`);
  const hashBefore = sql(`SELECT loginpass FROM bloguser WHERE userid = ${userId}`)[0];
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await page.goto(`${BASE}/reset-password/${token}`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="reset-password-new"]', { timeout: 90000 });
  await page.waitForFunction(() => { const f = document.querySelector('form'); return !!f && !f.hasAttribute('action'); }, { timeout: 120000 });
  await page.waitForTimeout(1500);
  await fillCommitted(page, 'reset-password-new', 'abc');
  await fillCommitted(page, 'reset-password-confirm', 'abc');
  await page.click('[data-testid="reset-submit"]');
  const msg = page.locator('[data-testid="reset-password-message"]');
  await expect(msg).toBeVisible({ timeout: 30000 });
  const text = ((await msg.textContent()) || '').trim();
  expect(text).toMatch(/at least 8 characters/i);
  expect(text).toMatch(/uppercase/i);
  expect(sql(`SELECT loginpass FROM bloguser WHERE userid = ${userId}`)[0], 'hash untouched').toBe(hashBefore);
  await shots(page, 'fn006-reset-weak');

  await fillCommitted(page, 'reset-password-new', PW_RESET);
  await fillCommitted(page, 'reset-password-confirm', PW_RESET);
  await page.click('[data-testid="reset-submit"]');
  await expect(msg).toContainText(/reset successfully/i, { timeout: 30000 });
  expect(sql(`SELECT isused FROM passwordresettoken WHERE token = '${token}'`)[0]).toBe('t');
  expect(sql(`SELECT loginpass FROM bloguser WHERE userid = ${userId}`)[0]).not.toBe(hashBefore);
  await page.close();
});

test.skip('superseded by w2-accounts (psql writes): REQ-FN-011 change password refuses a wrong current password and accepts the right one; the profile persists', async ({ browser }) => {
  test.setTimeout(420000);
  const userId = sql(`SELECT userid FROM bloguser WHERE emailid = '${EMAIL}'`)[0];
  let page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  const landed = await signInAs(page, EMAIL, PW_RESET);
  expect(landed, 'the new account signs in with the password it was reset to').not.toContain('/login');
  console.log(`FN-011 landed on ${landed}`);

  // change password
  await page.evaluate(() => (window as any).Blazor.navigateTo('/change-password'));
  await page.waitForSelector('[data-testid="change-password-card"]', { timeout: 60000 });
  await page.waitForTimeout(1500);
  const msg = page.locator('[data-testid="change-password-message"]');
  const hash0 = sql(`SELECT loginpass FROM bloguser WHERE userid = ${userId}`)[0];
  await fillCommitted(page, 'change-password-current', 'Wrong#Current1');
  await fillCommitted(page, 'change-password-new', PW_FINAL);
  await fillCommitted(page, 'change-password-confirm', PW_FINAL);
  await page.click('[data-testid="change-password-submit"]');
  await expect(msg).toBeVisible({ timeout: 30000 });
  expect(((await msg.textContent()) || '').toLowerCase()).toContain('current password');
  expect(sql(`SELECT loginpass FROM bloguser WHERE userid = ${userId}`)[0]).toBe(hash0);
  await shots(page, 'fn011-change-password-refused');

  await fillCommitted(page, 'change-password-current', PW_RESET);
  await fillCommitted(page, 'change-password-new', PW_FINAL);
  await fillCommitted(page, 'change-password-confirm', PW_FINAL);
  await page.click('[data-testid="change-password-submit"]');
  await page.waitForTimeout(5000);
  expect(sql(`SELECT loginpass FROM bloguser WHERE userid = ${userId}`)[0], 'the right current password changes the hash').not.toBe(hash0);

  // profile: renders stored values; saving persists
  // A signed-in browser keeps a session cookie, so sign in again from a brand-new context.
  await page.close();
  page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await signInAs(page, EMAIL, PW_FINAL).then((u) => expect(u, 'the new password signs in').not.toContain('/login'));
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/profile'));
  await page.waitForSelector('[data-testid="first-name-input"]', { timeout: 60000 });
  await page.waitForTimeout(2500);
  const stored = sql(`SELECT firstname, lastname FROM bloguser WHERE userid = ${userId}`)[0].split('|');
  await expect(page.locator('[data-testid="first-name-input"]')).toHaveValue(stored[0]);
  await expect(page.locator('[data-testid="last-name-input"]')).toHaveValue(stored[1]);
  await fillCommitted(page, 'last-name-input', 'Persisted');
  await fillCommitted(page, 'bio-input', 'fxb bio text');
  await fillCommitted(page, 'github-input', 'https://github.com/fxb-user');
  await page.click('[data-testid="save-profile"]');
  await page.waitForTimeout(5000);
  const saved = sql(`SELECT lastname, coalesce(profiledescription,''), coalesce(githuburl,'') FROM bloguser WHERE userid = ${userId}`)[0].split('|');
  expect(saved).toEqual(['Persisted', 'fxb bio text', 'https://github.com/fxb-user']);
  await shots(page, 'fn011-profile');
  await page.close();
});

test.skip('superseded by w2-accounts (psql writes): REQ-FN-029 username must be free and URL-safe; one site-owner flag; /author is retired', async ({ browser }) => {
  test.setTimeout(420000);
  const userId = sql(`SELECT userid FROM bloguser WHERE emailid = '${EMAIL}'`)[0];
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await signInAs(page, EMAIL, PW_FINAL);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/profile'));
  await page.waitForSelector('[data-testid="username-input"]', { timeout: 60000 });
  await page.waitForTimeout(2500);

  const hint = page.locator('[data-testid="username-hint"]');
  const status = page.locator('[data-testid="profile-status"]');
  const taken = sql("SELECT username FROM bloguser WHERE username IS NOT NULL AND username <> '' AND emailid <> '" + EMAIL + "' LIMIT 1")[0];
  await fillCommitted(page, 'username-input', taken);
  await expect(hint).toContainText(/already taken/i, { timeout: 20000 });
  await page.click('[data-testid="save-profile"]');
  await expect(status).toBeVisible({ timeout: 20000 });
  expect(((await status.textContent()) || '').toLowerCase()).toContain('taken');
  expect(sql(`SELECT coalesce(username,'') FROM bloguser WHERE userid = ${userId}`)[0] ?? '').not.toBe(taken);
  await shots(page, 'fn029-username-taken');

  await fillCommitted(page, 'username-input', 'bad name!');
  await expect(hint).toContainText(/letters, numbers, and hyphens/i, { timeout: 20000 });
  await page.click('[data-testid="save-profile"]');
  await page.waitForTimeout(3000);
  expect(sql(`SELECT coalesce(username,'') FROM bloguser WHERE userid = ${userId}`)[0] ?? '').not.toBe('bad name!');

  await fillCommitted(page, 'username-input', 'fxb-handle');
  await expect(hint).toContainText(/available/i, { timeout: 20000 });
  await page.click('[data-testid="save-profile"]');
  await page.waitForTimeout(5000);
  expect(sql(`SELECT username FROM bloguser WHERE userid = ${userId}`)[0]).toBe('fxb-handle');
  await page.close();

  // Only one account can hold the site-owner flag: the database refuses a second.
  let refused = false;
  try {
    execSync(`docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -v ON_ERROR_STOP=1 -c "UPDATE bloguser SET issiteowner = TRUE WHERE userid = ${userId}"`, { stdio: 'pipe' });
  } catch (e) {
    refused = /IdxSingleSiteOwner|unique/i.test(String((e as { stderr?: Buffer }).stderr ?? e));
  }
  expect(refused, 'a second site-owner is refused by the unique index').toBe(true);
  expect(sql('SELECT count(*) FROM bloguser WHERE issiteowner')[0]).toBe('1');

  // BRD-53/54 retired /author/{username}: the route no longer exists.
  const res = await browser.newContext().then((c) => c.request.get(`${BASE}/author/fxb-handle`));
  expect(res.status()).toBe(404);
});
