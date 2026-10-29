import { test } from '@playwright/test';
import { loginHard, open, psql, hOverflow, SHOT } from './bp-a-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/comments`;
test.use({ actionTimeout: 25000 });
async function until(page: any, fn: () => Promise<boolean>, ms = 40000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(700); }
  return false;
}
test('builder evidence (psql fixtures retired, no assertions, superseded by fxv4-admin-moderation.spec.ts): comments walkthrough', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = {};
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const pub = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  try {
    const mk = (n: string) => psql(`insert into blogcomment(postid,givenon,givenby,email,comment,published,isemailverified,moderationstatus) values(59,now(),'fxa Mod ${n}','fxa-mod${n}@example.com','fxa moderation body ${n}',false,true,'PendingApproval')`);
    mk('A'); mk('B');
    R.db = psql("select commentid,givenby,left(comment,30),moderationstatus,published from blogcomment order by 1");
    await loginHard(page, 'admin');
    await open(page, '/comments', '[data-testid="comments-grid"]');
    await page.waitForTimeout(4000);
    R.tabs = await page.$$eval('[data-testid="comments-status-tabs"] [role="tab"], [data-testid="comments-status-tabs"] button', (n) => n.map((x) => x.textContent?.trim()));
    await page.click('[data-testid="comments-tab-pending"]');
    await page.waitForTimeout(3000);
    const rows = () => page.$$eval('tbody tr', (trs) => trs.map((tr) => ({
      author: tr.querySelector('[data-testid="comment-row-author"]')?.textContent?.trim(),
      post: tr.querySelector('[data-testid="comment-row-post"]')?.textContent?.trim(),
      text: tr.querySelector('[data-testid="comment-row-text"]')?.textContent?.trim(),
      status: tr.querySelector('[data-testid="comment-row-status"]')?.textContent?.trim(),
      date: tr.querySelector('[data-testid="comment-row-date"]')?.textContent?.trim(),
    })));
    R.pendingRows = await rows();
    R.pendingDb = psql("select givenby,moderationstatus from blogcomment where not published and moderationstatus not in ('Spam','Rejected')");
    await page.screenshot({ path: `${OUT}/pending-1280.png`, fullPage: true });
    // approve A
    const rowA = page.locator('tbody tr').filter({ hasText: 'fxa moderation body A' });
    await rowA.locator('[data-testid="comment-approve"]').click();
    R.approvedA = await until(page, async () => psql("select moderationstatus from blogcomment where email='fxa-modA@example.com'")[0][0] === 'Approved');
    const rowB = page.locator('tbody tr').filter({ hasText: 'fxa moderation body B' });
    await page.waitForTimeout(2500);
    await rowB.locator('[data-testid="comment-spam"]').click();
    R.rejectedB = await until(page, async () => ['Spam', 'Rejected'].includes(psql("select moderationstatus from blogcomment where email='fxa-modB@example.com'")[0][0]));
    R.dbAfter = psql("select email,moderationstatus,published from blogcomment where email like 'fxa-mod%'");
    R.statusMsg = (await page.textContent('[data-testid="comments-status-message"]').catch(() => 'none'))?.trim();
    // public effect
    await pub.goto(`${process.env.TB_BASE}/post/fxa-post-4`, { waitUntil: 'networkidle', timeout: 90000 });
    await pub.waitForTimeout(3000);
    const body = await pub.innerText('body');
    R.publicShowsA = body.includes('fxa moderation body A'); R.publicShowsB = body.includes('fxa moderation body B');
    R.publicShowsEmail = body.includes('fxa-modA@example.com');
    await pub.screenshot({ path: `${OUT}/public-post.png`, fullPage: true });
    // 390
    await page.setViewportSize({ width: 390, height: 800 });
    await page.waitForTimeout(1500);
    R.c390 = await hOverflow(page);
    R.tabsVisible390 = await page.evaluate(() => ['comments-tab-all', 'comments-tab-pending', 'comments-tab-approved', 'comments-tab-spam'].map((id) => { const e = document.querySelector(`[data-testid="${id}"]`) as HTMLElement; if (!e) return id + ':absent'; const r = e.getBoundingClientRect(); return `${id}:${Math.round(r.left)}-${Math.round(r.right)}`; }));
    // make another pending for action buttons at 390
    psql("insert into blogcomment(postid,givenon,givenby,email,comment,published,isemailverified,moderationstatus) values(59,now(),'fxa Mod C','fxa-modC@example.com','fxa moderation body C',false,true,'PendingApproval')");
    await open(page, '/AdminDashboard', '[data-testid="stat-posts-value"]');
    await open(page, '/comments', '[data-testid="comments-grid"]');
    await page.waitForTimeout(4000);
    await page.screenshot({ path: `${OUT}/comments-390.png`, fullPage: true });
    const btn = page.locator('tbody tr').filter({ hasText: 'fxa moderation body C' }).locator('[data-testid="comment-approve"]');
    R.actionBtn390 = await btn.evaluate((e) => { const r = e.getBoundingClientRect(); return { left: r.left, right: r.right, vw: innerWidth }; }).catch((e) => String(e).slice(0, 100));
    R.errs = errs.slice(0, 8);
  } catch (e) { R.error = String(e).slice(0, 400); }
  finally {
    fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
    try { psql("delete from blogcomment where email like 'fxa-mod%@example.com'"); } catch (e) { console.log('cleanup fail', String(e).slice(0, 300)); }
  }
});
