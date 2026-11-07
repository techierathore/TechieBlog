import { test } from '@playwright/test';
import { loginHard, open, psql, hOverflow, SHOT } from './bp-a-helpers';
import { fillCommitted, pickSelect } from './vall-authoring-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/series`;
test.use({ actionTimeout: 25000 });
async function until(page: any, fn: () => Promise<boolean>, ms = 40000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(700); }
  return false;
}
test('superseded by w3-authoring.spec.ts (seed-dependent / psql-writing, not a product verdict): REQ-UI-024 series', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = {};
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  try {
    await loginHard(page, 'admin');
    await open(page, '/admin/series', '[data-testid="series-grid"]');
    await page.waitForTimeout(4000);
    R.rows = await page.$$eval('tbody tr', (trs) => trs.map((tr) => ({ name: tr.querySelector('[data-testid="series-row-name"]')?.textContent?.trim(), slug: tr.querySelector('[data-testid="series-row-slug"]')?.textContent?.trim(), status: tr.querySelector('[data-testid="series-row-status"]')?.textContent?.trim(), count: tr.querySelector('[data-testid="series-row-postcount"]')?.textContent?.trim(), author: tr.querySelector('[data-testid="series-row-author"]')?.textContent?.trim(), edit: tr.querySelector('[data-testid="series-edit"]')?.getAttribute('href') })));
    R.db = psql("select s.name,s.slug,s.status,(select count(*) from blogpost p where p.seriesid=s.seriesid and not p.isdeleted),(select count(*) from blogpost p where p.seriesid=s.seriesid and p.published and not p.isdeleted) from blogseries s order by seriesid");
    await page.screenshot({ path: `${OUT}/series-1280.png`, fullPage: true });
    await open(page, '/admin/series/8', '[data-testid="series-name-input"]');
    await page.waitForTimeout(4000);
    R.editor = { name: await page.inputValue('[data-testid="series-name-input"]'), slug: await page.inputValue('[data-testid="series-slug-input"]'), desc: await page.inputValue('[data-testid="series-description-input"]'), parts: await page.$$eval('[data-testid="series-post-row"]', (n) => n.map((x) => x.textContent!.replace(/\s+/g, ' ').trim())) };
    R.partsDb = psql("select seriespartnumber,slug from blogpost where seriesid=8 order by 1");
    R.reorderControls = await page.locator('[data-testid="series-posts-card"] button, [data-testid="series-posts-card"] input, [data-testid="series-posts-card"] [draggable="true"]').count();
    await page.screenshot({ path: `${OUT}/series-edit-1280.png`, fullPage: true });
    // create new series via UI
    await open(page, '/admin/series/new', '[data-testid="series-name-input"]');
    await page.waitForTimeout(3500);
    await fillCommitted(page, 'series-name-input', 'fxa NewSeries');
    await fillCommitted(page, 'series-description-input', 'fxa created via UI');
    await page.click('[data-testid="save-series"]');
    R.created = await until(page, async () => psql("select count(*) from blogseries where name='fxa NewSeries'")[0][0] === '1');
    R.newDb = psql("select slug,status,authorid from blogseries where name='fxa NewSeries'")[0];
    await page.waitForTimeout(3000);
    R.afterCreateUrl = new URL(page.url()).pathname;
    await open(page, '/admin/series', '[data-testid="series-grid"]');
    await page.waitForTimeout(3500);
    R.listedNew = await page.locator('tbody tr').filter({ hasText: 'fxa NewSeries' }).count();
    await page.setViewportSize({ width: 390, height: 800 });
    await page.waitForTimeout(1200);
    R.s390 = await hOverflow(page);
    await page.screenshot({ path: `${OUT}/series-390.png`, fullPage: true });
    R.errs = errs.slice(0, 8);
  } catch (e) { R.error = String(e).slice(0, 400); }
  finally {
    fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
    try { psql("delete from blogseries where name='fxa NewSeries'"); } catch (e) { console.log('cleanup fail', String(e).slice(0, 300)); }
  }
});
