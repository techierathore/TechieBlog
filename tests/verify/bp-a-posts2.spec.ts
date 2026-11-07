import { test } from '@playwright/test';
import { loginHard, open, psql, SHOT } from './bp-a-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/posts`;
async function until(page: any, fn: () => Promise<boolean>, ms = 30000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(500); }
  return false;
}
test.use({ actionTimeout: 20000 });
test('017 filters/search/row actions with settled waits; 018 markdown preview', async ({ browser }) => {
  test.setTimeout(300000);
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 800 } })).newPage();
  const R: any = {}; const T0=Date.now(); const log=(m:string)=>console.log(Math.round((Date.now()-T0)/1000)+'s '+m);
  await loginHard(page, 'admin');
  log('login done'); await open(page, '/BlogsList', '[data-testid="posts-grid"]');
  await page.waitForTimeout(4000);
  const rowsStatus = () => page.$$eval('[data-testid="post-row-status"]', (n) => n.map((x) => x.textContent!.trim()));
  for (const [t, expectN] of [['published', psql('select count(*) from blogpost where published and not isdeleted')[0][0]], ['draft', psql('select count(*) from blogpost where not published and scheduledpublishon is null and not isdeleted')[0][0]], ['scheduled', '0']] as const) {
    await page.click(`[data-testid="posts-tab-${t}"]`);
    const ok = await until(page, async () => (await page.textContent('[data-testid="posts-count"], [data-testid="posts-empty"]'))!.includes(`Showing ${expectN} of`) || (expectN === '0' && (await page.locator('[data-testid="posts-empty"]').count()) > 0), 20000);
    const st = await rowsStatus().catch(() => []);
    log('tab '+t+' ok='+ok);
    R['tab_' + t] = { settled: ok, dbCount: expectN, rows: st.length, distinct: [...new Set(st)], footer: (await page.locator('[data-testid="posts-count"]').textContent().catch(() => 'n/a'))?.trim() };
  }
  log('click all'); await page.click('[data-testid="posts-tab-all"]'); log('clicked');
  await page.waitForTimeout(2000);
  log('fill'); await page.fill('[data-testid="posts-search"]', 'fxa-draft'); log('filled');
  await until(page, async () => (await page.locator('tbody tr').count()) === 1);
  await page.waitForTimeout(2000);
  log('search');
  R.searchRows = await page.locator('tbody tr').count();
  const title = (await page.locator('[data-testid="post-row-title"]').first().textContent())?.trim();
  await page.locator('[data-testid="post-delete"]').first().click();
  await page.waitForSelector('[data-testid="post-delete-title"]');
  R.deleteDialog = { clickedRow: title, dialog: (await page.textContent('[data-testid="post-delete-title"]'))?.trim() };
  await page.click('[data-testid="post-delete-cancel"]');
  await page.waitForTimeout(1000);
  log('dialog');
  R.draftInDb = psql("select count(*) from blogpost where slug='fxa-draft'")[0][0];
  // markdown + published preview via direct nav
  const body = psql("select postcontent from blogpost where slug='fxa-post-1'")[0][0];
  R.postBody = body.slice(0, 200);
  await open(page, '/admin/preview/56', '[data-testid="preview-article"]');
  await page.waitForTimeout(3000);
  R.pubPreview = {
    banner: (await page.textContent('[data-testid="preview-banner"]'))?.replace(/\s+/g, ' ').trim(),
    contentHtml: (await page.innerHTML('[data-testid="preview-content"]')).slice(0, 500),
    updated: (await page.textContent('[data-testid="preview-updated"]').catch(() => 'n/a'))?.replace(/\s+/g, ' '),
    banner_img: await page.locator('[data-testid="preview-featured-image"]').count(),
  };
  await page.screenshot({ path: `${OUT}/preview-published-1280.png`, fullPage: true });
  fs.writeFileSync(`${OUT}/result2.json`, JSON.stringify(R, null, 1));
});
