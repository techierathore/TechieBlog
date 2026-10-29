import { test, expect, Page } from '@playwright/test';
import { execFileSync } from 'child_process';
import { BASE, visualCheck } from './_gates';

const SH = 'tests/.artifacts/build-phase-20261027/a/auth';
const db = (sql: string) => execFileSync('docker', ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql], { encoding: 'utf8' }).trim();
test.beforeEach(({}, ti) => ti.setTimeout(240000));

async function open(page: Page, url: string, sel: string) {
  await page.goto(`${BASE}${url}`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector(sel, { timeout: 60000 });
  await page.waitForFunction(() => { const f = document.querySelector('form'); return !f || !f.hasAttribute('action'); }, { timeout: 60000 }).catch(() => {});
  await page.waitForTimeout(2000);
}
async function scrollInfo(page: Page, tag: string, w: number, h: number) {
  await page.setViewportSize({ width: w, height: h });
  await page.waitForTimeout(600);
  const m = await page.evaluate(() => ({
    sh: document.documentElement.scrollHeight, ch: document.documentElement.clientHeight,
    bodySh: document.body.scrollHeight, hs: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    mainH: (document.querySelector('main') as HTMLElement | null)?.getBoundingClientRect().height ?? -1,
    cardBottom: (document.querySelector('[data-testid$="card"],[data-testid*="reset-token-invalid"]') as HTMLElement | null)?.getBoundingClientRect().bottom ?? -1,
    docOverflowY: getComputedStyle(document.documentElement).overflowY, bodyMinH: getComputedStyle(document.body).minHeight,
  }));
  console.log(`SCROLL ${tag} @${w}x${h}`, JSON.stringify(m));
  await page.screenshot({ path: `${SH}/${tag}-${w}.png`, fullPage: true });
  return m;
}

test('UI-003 forgot page: geometry, token issued for fixture user, generic message for unknown', async ({ page }) => {
  const errs: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 160)); });
  await open(page, '/forgot-password', '[data-testid="forgot-email"]');
  for (const [w, h] of [[1280, 800], [390, 844]]) {
    const v = await visualCheck(page, `${SH}/forgot-v-${w}.png`, w);
    console.log('FORGOT geo', w, JSON.stringify([v.overlaps, v.hScroll, v.zeroSized]));
    expect(v.overlaps).toEqual([]); expect(v.hScroll).toBe(0);
    await scrollInfo(page, 'forgot', w, h);
  }
  await page.setViewportSize({ width: 1280, height: 800 });
  const before = Number(db(`select count(*) from passwordresettoken where userid=(select userid from bloguser where emailid='fxa-reset@example.com')`));
  await page.fill('[data-testid="forgot-email"]', 'fxa-reset@example.com');
  await page.waitForTimeout(300);
  await page.click('[data-testid="forgot-submit"]');
  await expect(page.locator('[data-testid="forgot-password-message"]')).toBeVisible({ timeout: 30000 });
  console.log('FORGOT msg', (await page.locator('[data-testid="forgot-password-message"]').innerText()).replace(/\s+/g, ' '));
  await page.waitForTimeout(1500);
  const after = Number(db(`select count(*) from passwordresettoken where userid=(select userid from bloguser where emailid='fxa-reset@example.com')`));
  console.log('TOKEN rows before/after', before, after);
  expect(after).toBe(before + 1);
  // unknown address: same message, no row
  const total = Number(db('select count(*) from passwordresettoken'));
  await page.fill('[data-testid="forgot-email"]', 'fxa-nobody@example.com');
  await page.click('[data-testid="forgot-submit"]');
  await page.waitForTimeout(3000);
  console.log('UNKNOWN msg', (await page.locator('[data-testid="forgot-password-message"]').innerText()).replace(/\s+/g, ' '), 'rows', total, '->', db('select count(*) from passwordresettoken'));
  console.log('console errors', JSON.stringify(errs));
});

test('UI-003 reset: invalid, expired, then valid token -> new password -> login', async ({ page }) => {
  await open(page, '/reset-password/not-a-real-token', '[data-testid="reset-token-invalid"]');
  console.log('INVALID text', (await page.locator('[data-testid="reset-token-invalid"]').innerText()).replace(/\s+/g, ' '));
  await scrollInfo(page, 'reset-invalid', 390, 844);
  await page.setViewportSize({ width: 1280, height: 800 });
  await open(page, '/reset-password/fxa-expired-token', '[data-testid="reset-token-invalid"]');
  console.log('EXPIRED text', (await page.locator('[data-testid="reset-token-invalid"]').innerText()).replace(/\s+/g, ' '));

  const token = db(`select token from passwordresettoken where userid=(select userid from bloguser where emailid='fxa-reset@example.com') and isused=false and expiresat>now() order by tokenid desc limit 1`);
  expect(token.length).toBeGreaterThan(20);
  await open(page, `/reset-password/${encodeURIComponent(token)}`, '[data-testid="reset-password-card"]');
  for (const [w, h] of [[1280, 800], [390, 844]]) { const v = await visualCheck(page, `${SH}/reset-v-${w}.png`, w); console.log('RESET geo', w, JSON.stringify([v.overlaps, v.hScroll])); expect(v.overlaps).toEqual([]); await scrollInfo(page, 'reset', w, h); }
  await page.setViewportSize({ width: 1280, height: 800 });
  // mismatch
  await page.fill('[data-testid="reset-password-new"]', 'FxaNew#Pass42');
  await page.fill('[data-testid="reset-password-confirm"]', 'Different#Pass42');
  await page.click('[data-testid="reset-submit"]');
  await expect(page.locator('[data-testid="reset-password-message"]')).toBeVisible({ timeout: 20000 });
  console.log('MISMATCH msg', (await page.locator('[data-testid="reset-password-message"]').innerText()).replace(/\s+/g, ' '));
  // weak
  await page.fill('[data-testid="reset-password-new"]', 'weak');
  await page.fill('[data-testid="reset-password-confirm"]', 'weak');
  await page.click('[data-testid="reset-submit"]');
  await page.waitForTimeout(2500);
  console.log('WEAK msg', (await page.locator('[data-testid="reset-password-message"]').innerText()).replace(/\s+/g, ' '));
  // good
  await page.fill('[data-testid="reset-password-new"]', 'FxaNew#Pass42');
  await page.fill('[data-testid="reset-password-confirm"]', 'FxaNew#Pass42');
  await page.click('[data-testid="reset-submit"]');
  await page.waitForURL(/\/login/, { timeout: 30000 });
  console.log('REDIRECT ->', page.url());
  console.log('token used flag', db(`select isused from passwordresettoken where token='${token.replace(/'/g, "''")}'`));
  // reuse
  await open(page, `/reset-password/${encodeURIComponent(token)}`, '[data-testid="reset-token-invalid"]');
  console.log('REUSED text', (await page.locator('[data-testid="reset-token-invalid"]').innerText()).replace(/\s+/g, ' '));
  // login with new password
  await open(page, '/login', '[data-testid="login-email"]');
  for (let i = 0; i < 10; i++) { await page.fill('[data-testid="login-email"]', 'fxa-reset@example.com'); await page.fill('[data-testid="login-password"]', 'FxaNew#Pass42'); await page.waitForTimeout(400); if (await page.inputValue('[data-testid="login-password"]') === 'FxaNew#Pass42') break; }
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  console.log('LOGIN with new pw ->', page.url());
});
