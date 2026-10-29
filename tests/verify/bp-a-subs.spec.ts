import { test } from '@playwright/test';
import { loginHard, open, psql, hOverflow, SHOT } from './bp-a-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/subs`;
test.use({ actionTimeout: 25000 });
async function until(page: any, fn: () => Promise<boolean>, ms = 40000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(700); }
  return false;
}
test('builder evidence (psql fixtures, no assertions, superseded by fxv4-admin-moderation.spec.ts): subscribers walkthrough', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = {};
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  try {
    psql("insert into subscriber(email,name,subscribedon,isconfirmed,confirmedon) values('fxa-mine@example.com','fxa Mine',now(),true,now())");
    await loginHard(page, 'admin');
    await open(page, '/admin/subscribers', '[data-testid="subscribers-grid"]');
    await page.waitForTimeout(4000);
    const rows = () => page.$$eval('tbody tr', (trs) => trs.map((tr) => ({ email: tr.querySelector('[data-testid="subscriber-row-email"]')?.textContent?.trim(), name: tr.querySelector('[data-testid="subscriber-row-name"]')?.textContent?.trim(), date: tr.querySelector('[data-testid="subscriber-row-date"]')?.textContent?.trim(), status: tr.querySelector('[data-testid="subscriber-row-status"]')?.textContent?.trim(), consent: tr.querySelector('[data-testid="subscriber-row-consent"]')?.textContent?.trim() })));
    R.summary = (await page.textContent('[data-testid="subscribers-summary"]'))?.trim();
    R.tabs = ['all', 'active', 'inactive'].length && await Promise.all(['all', 'active', 'inactive'].map(async (t) => (await page.textContent(`[data-testid="subscribers-tab-${t}"]`))?.trim()));
    const all = await rows();
    R.rowCount = all.length;
    R.dbTotal = psql('select count(*) from subscriber')[0][0];
    R.dbConfirmed = psql('select count(*) from subscriber where isconfirmed')[0][0];
    R.dbEmails = psql('select email from subscriber').map((r) => r[0]).sort();
    R.listedEmails = all.map((r) => r.email!).sort();
    R.sameSet = JSON.stringify(R.dbEmails) === JSON.stringify(R.listedEmails);
    R.emptyCells = all.filter((r) => !r.email || !r.name || !r.date || !r.status).length;
    R.sample = all.slice(0, 3);
    await page.screenshot({ path: `${OUT}/subs-1280.png`, fullPage: true });
    // search
    await page.fill('[data-testid="subscribers-search"]', 'fxa-mine');
    await page.dispatchEvent('[data-testid="subscribers-search"]', 'change');
    R.searchOk = await until(page, async () => (await page.locator('tbody tr').count()) === 1);
    // deactivate mine
    await page.locator('tbody tr').first().locator('[data-testid="subscriber-deactivate"]').click();
    R.deact = await until(page, async () => psql("select isconfirmed from subscriber where email='fxa-mine@example.com'")[0][0] === 'f');
    R.deactDb = psql("select isconfirmed,unsubscribedon from subscriber where email='fxa-mine@example.com'")[0];
    R.afterDeactRow = (await rows())[0];
    await page.fill('[data-testid="subscribers-search"]', '');
    await page.dispatchEvent('[data-testid="subscribers-search"]', 'change');
    await page.waitForTimeout(2500);
    // status filter inactive
    await page.click('[data-testid="subscribers-tab-inactive"]');
    await page.waitForTimeout(2500);
    const inact = await rows();
    R.inactiveTab = { n: inact.length, statuses: [...new Set(inact.map((r) => r.status))], hasMine: inact.some((r) => r.email === 'fxa-mine@example.com') };
    await page.click('[data-testid="subscribers-tab-active"]');
    await page.waitForTimeout(2500);
    const act = await rows();
    R.activeTab = { n: act.length, statuses: [...new Set(act.map((r) => r.status))] };
    // export from the Active tab
    const [dl] = await Promise.all([page.waitForEvent('download', { timeout: 30000 }).catch(() => null), page.click('[data-testid="subscribers-export"]')]);
    if (dl) {
      const p = `${OUT}/export.csv`; await dl.saveAs(p);
      const lines = fs.readFileSync(p, 'utf8').trim().split('\n');
      const emails = lines.slice(1).map((l) => l.split(',')[0].replace(/"/g, '')).sort();
      R.export = { header: lines[0], n: emails.length, equalsActiveTab: JSON.stringify(emails) === JSON.stringify(act.map((r) => r.email!).sort()), equalsAllListed: JSON.stringify(emails) === JSON.stringify(R.listedEmails), notInExport: R.listedEmails.filter((e: string) => !emails.includes(e)).length, statusMsg: (await page.textContent('[data-testid="subscribers-status-message"]').catch(() => ''))?.trim(), containsMine: emails.includes('fxa-mine@example.com') };
    } else R.export = 'NO DOWNLOAD';
    // reactivate mine via inactive tab
    await page.click('[data-testid="subscribers-tab-all"]');
    await page.fill('[data-testid="subscribers-search"]', 'fxa-mine');
    await page.dispatchEvent('[data-testid="subscribers-search"]', 'change');
    await until(page, async () => (await page.locator('tbody tr').count()) === 1);
    await page.locator('tbody tr').first().locator('[data-testid="subscriber-activate"]').click();
    R.react = await until(page, async () => psql("select isconfirmed from subscriber where email='fxa-mine@example.com'")[0][0] === 't');
    await page.setViewportSize({ width: 390, height: 800 });
    await page.waitForTimeout(1200);
    R.s390 = await hOverflow(page);
    await page.screenshot({ path: `${OUT}/subs-390.png`, fullPage: true });
    R.errs = errs.slice(0, 8);
  } catch (e) { R.error = String(e).slice(0, 400); }
  finally {
    fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
    try { psql("delete from subscriber where email='fxa-mine@example.com'"); } catch (e) { console.log('cleanup fail', String(e).slice(0, 300)); }
  }
});
