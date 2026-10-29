import { test } from '@playwright/test';
import { loginHard, open, psql, hOverflow, SHOT } from './bp-a-helpers';
import { pickSelect } from './vall-authoring-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/users`;
test.use({ actionTimeout: 25000 });
async function until(page: any, fn: () => Promise<boolean>, ms = 40000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(700); }
  return false;
}
test('probe (no assertions, superseded by w2-accounts REQ-UI-020): users', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = {}; const T0 = Date.now(); const log = (m: string) => console.log(Math.round((Date.now() - T0) / 1000) + 's ' + m);
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  try {
    R.landing = await loginHard(page, 'admin'); log('login');
    await open(page, '/users', '[data-testid="users-grid"]');
    await page.waitForTimeout(4000);
    const rows = async () => page.$$eval('tbody tr', (trs) => trs.map((tr) => ({
      name: tr.querySelector('[data-testid="user-row-name"]')?.textContent?.trim(),
      email: tr.querySelector('[data-testid="user-row-email"]')?.textContent?.trim(),
      role: tr.querySelector('[data-testid="user-row-role"]')?.textContent?.trim(),
      status: tr.querySelector('[data-testid="user-row-status"]')?.textContent?.trim(),
      joined: tr.querySelector('[data-testid="user-row-joined"]')?.textContent?.trim(),
      username: tr.querySelector('[data-testid="user-row-username"]')?.textContent?.trim(),
    })));
    R.rows = await rows();
    R.db = psql("select emailid,userrole,isdeleted from bloguser order by userid");
    R.count = (await page.textContent('[data-testid="users-count"]'))?.trim();
    R.tabs = await page.$$eval('[data-testid="users-role-tabs"] button, [data-testid="users-role-tabs"] [role="tab"]', (n) => n.map((x) => x.textContent?.trim()));
    await page.screenshot({ path: `${OUT}/users-1280.png`, fullPage: true });
    // search
    await page.fill('[data-testid="users-search"]', 'editor@');
    await page.dispatchEvent('[data-testid="users-search"]', 'change');
    R.searchSettled = await until(page, async () => (await page.locator('tbody tr').count()) === 1);
    R.searchRows = (await rows()).map((r) => r.email);
    await page.fill('[data-testid="users-search"]', '');
    await page.dispatchEvent('[data-testid="users-search"]', 'change');
    await page.waitForTimeout(2500);
    // add user
    await open(page, '/AddUser', '[data-testid="user-first-name"]');
    await page.waitForTimeout(3500);
    const f = async (id: string, v: string) => { await page.fill(`[data-testid="${id}"]`, v); await page.dispatchEvent(`[data-testid="${id}"]`, 'change'); await page.waitForTimeout(300); };
    await f('user-first-name', 'fxa'); await f('user-last-name', 'Newuser'); await f('user-email', 'fxa-newuser@example.com');
    await f('user-password', 'FxaStrong#2026'); await f('user-confirm-password', 'FxaStrong#2026');
    await pickSelect(page, 'user-role', 'Reader');
    await page.screenshot({ path: `${OUT}/adduser-filled.png`, fullPage: true });
    await page.click('[data-testid="add-user-submit"]');
    R.created = await until(page, async () => psql("select count(*) from bloguser where emailid='fxa-newuser@example.com'")[0][0] === '1', 40000);
    await page.waitForTimeout(4000);
    R.afterSubmitUrl = new URL(page.url()).pathname;
    R.addMsg = (await page.textContent('[data-testid="add-user-status-message"]').catch(() => 'none'))?.trim();
    R.dbNew = psql("select emailid,userrole,mustchangepassword,isdeleted from bloguser where emailid='fxa-newuser@example.com'")[0];
    await page.screenshot({ path: `${OUT}/adduser-after.png`, fullPage: true });
    await open(page, '/users', '[data-testid="users-grid"]');
    await page.waitForTimeout(3000);
    R.listed = (await rows()).filter((r) => r.email?.includes('fxa-newuser'));
    // 390
    await page.setViewportSize({ width: 390, height: 800 });
    await page.waitForTimeout(1200);
    R.users390 = await hOverflow(page);
    await page.screenshot({ path: `${OUT}/users-390.png`, fullPage: true });
    R.errs = errs.slice(0, 8);
  } catch (e) { R.error = String(e).slice(0, 400); }
  finally {
    fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
    try { psql("delete from loginlog where 1=0"); } catch {}
    try { psql("delete from bloguser where emailid='fxa-newuser@example.com'"); } catch (e) { console.log('cleanup fail', String(e).slice(0, 300)); }
  }
});
