import { test } from '@playwright/test';
import { loginHard, open, psql, hOverflow, SHOT } from './bp-a-helpers';
import { fillCommitted } from './vall-authoring-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/taxonomy`;
test.use({ actionTimeout: 25000 });
async function until(page: any, fn: () => Promise<boolean>, ms = 40000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(700); }
  return false;
}
test('builder evidence (psql writes, no assertions, superseded by fxv4-admin-taxonomy.spec.ts): categories + tags walkthrough', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = {};
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  const origDesc = psql("select description from category where categoryid=16")[0][0];
  const origTag = psql("select tagname from tag where tagid=27")[0][0];
  try {
    await loginHard(page, 'admin');
    const rows = (pre: string, extra: string[]) => page.$$eval('tbody tr', (trs, a) => trs.map((tr) => Object.fromEntries((a as string[]).map((k) => [k, tr.querySelector(`[data-testid="${(a as string[])[0].split('-')[0]}-row-${k}"]`)?.textContent?.trim()]))), extra);
    // ---- categories
    await open(page, '/admin/categories', '[data-testid="categories-grid"]');
    await page.waitForTimeout(4000);
    R.catRows = await page.$$eval('tbody tr', (trs) => trs.map((tr) => ({ name: tr.querySelector('[data-testid="category-row-name"]')?.textContent?.trim(), slug: tr.querySelector('[data-testid="category-row-slug"]')?.textContent?.trim(), desc: tr.querySelector('[data-testid="category-row-description"]')?.textContent?.trim(), count: tr.querySelector('[data-testid="category-row-postcount"]')?.textContent?.trim(), edit: tr.querySelector('[data-testid="category-edit"]')?.getAttribute('href') })));
    R.catDb = psql("select c.categoryname,c.slug,(select count(*) from blogpost p where p.categoryid=c.categoryid and not p.isdeleted),(select count(*) from blogpost p where p.categoryid=c.categoryid and p.published and not p.isdeleted) from category c order by categoryid");
    R.catCount = (await page.textContent('[data-testid="categories-count"]'))?.trim();
    await page.screenshot({ path: `${OUT}/categories-1280.png`, fullPage: true });
    await open(page, '/admin/category/16', '[data-testid="category-name-input"]');
    await page.waitForTimeout(3500);
    R.catEditor = { name: await page.inputValue('[data-testid="category-name-input"]'), slug: await page.inputValue('[data-testid="category-slug-input"]'), desc: await page.inputValue('[data-testid="category-description-input"]') };
    await fillCommitted(page, 'category-description-input', 'fxa fixture category EDITED');
    await page.click('[data-testid="save-category"]');
    R.catSaved = await until(page, async () => psql("select description from category where categoryid=16")[0][0] === 'fxa fixture category EDITED');
    R.catUrlAfter = new URL(page.url()).pathname;
    await page.waitForTimeout(3000);
    // revert via UI
    await open(page, '/admin/category/16', '[data-testid="category-name-input"]');
    await page.waitForTimeout(3500);
    await fillCommitted(page, 'category-description-input', origDesc);
    await page.click('[data-testid="save-category"]');
    R.catReverted = await until(page, async () => psql("select description from category where categoryid=16")[0][0] === origDesc);
    // ---- tags
    await open(page, '/admin/tags', '[data-testid="tags-grid"]');
    await page.waitForTimeout(4000);
    R.tagRows = await page.$$eval('tbody tr', (trs) => trs.map((tr) => ({ name: tr.querySelector('[data-testid="tag-row-name"]')?.textContent?.trim(), slug: tr.querySelector('[data-testid="tag-row-slug"]')?.textContent?.trim(), count: tr.querySelector('[data-testid="tag-row-postcount"]')?.textContent?.trim(), edit: tr.querySelector('[data-testid="tag-edit"]')?.getAttribute('href') })));
    R.tagDb = psql("select t.tagname,t.slug,(select count(*) from posttag pt join blogpost p using(postid) where pt.tagid=t.tagid and not p.isdeleted),(select count(*) from posttag pt join blogpost p using(postid) where pt.tagid=t.tagid and p.published and not p.isdeleted) from tag t order by tagid");
    R.tagCount = (await page.textContent('[data-testid="tags-count"]'))?.trim();
    await page.screenshot({ path: `${OUT}/tags-1280.png`, fullPage: true });
    await open(page, '/ManageTag/27', '[data-testid="tag-name-input"]');
    await page.waitForTimeout(3500);
    R.tagEditor = { name: await page.inputValue('[data-testid="tag-name-input"]'), slug: await page.inputValue('[data-testid="tag-slug-input"]') };
    await fillCommitted(page, 'tag-name-input', 'fxa Tag EDITED');
    await page.click('[data-testid="save-tag"]');
    R.tagSaved = await until(page, async () => psql("select tagname from tag where tagid=27")[0][0] === 'fxa Tag EDITED');
    await page.waitForTimeout(3000);
    await open(page, '/ManageTag/27', '[data-testid="tag-name-input"]');
    await page.waitForTimeout(3500);
    await fillCommitted(page, 'tag-name-input', origTag);
    await page.click('[data-testid="save-tag"]');
    R.tagReverted = await until(page, async () => psql("select tagname from tag where tagid=27")[0][0] === origTag);
    R.tagSlugAfter = psql("select slug from tag where tagid=27")[0][0];
    // 390
    await page.setViewportSize({ width: 390, height: 800 });
    await open(page, '/admin/categories', '[data-testid="categories-grid"]');
    await page.waitForTimeout(3000);
    R.cat390 = await hOverflow(page);
    await page.screenshot({ path: `${OUT}/categories-390.png`, fullPage: true });
    R.errs = errs.slice(0, 8);
  } catch (e) { R.error = String(e).slice(0, 400); }
  finally {
    fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
    try { psql(`update category set description='${origDesc.replace(/'/g, "''")}' where categoryid=16`); psql(`update tag set tagname='${origTag.replace(/'/g, "''")}' where tagid=27`); } catch (e) { console.log('revert fail', String(e).slice(0, 300)); }
  }
});
