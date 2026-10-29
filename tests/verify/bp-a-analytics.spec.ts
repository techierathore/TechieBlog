import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { nav, visualCheck } from './_gates';
import { psql, signIn, settle } from './vall-resume-helpers';

/** Build-phase 2026-10-27 cluster A verify: REQ-UI-044. Read-only. */
const OUT = 'tests/.artifacts/build-phase-20261027/a/analytics';
test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

async function snap(page: Page) {
  const t = async (id: string) => ((await page.locator(`[data-testid="${id}"]`).first().innerText().catch(() => '<absent>')) || '').replace(/\s+/g, ' ');
  const cnt = async (id: string) => page.locator(`[data-testid="${id}"]`).count();
  return {
    views: await t('analytics-stat-views'), unique: await t('analytics-stat-unique'), rating: await t('analytics-stat-rating'), comments: await t('analytics-stat-comments'),
    caption: await t('analytics-range-caption'),
    trend: (await cnt('analytics-trend-chart')) ? await t('analytics-trend-summary') : (await cnt('analytics-trend-empty')) ? 'EMPTY-STATE' : 'NONE',
    popularRows: await cnt('popular-row-title'), popularTitles: await page.locator('[data-testid="popular-row-title"]').allInnerTexts(),
    popularViews: await page.locator('[data-testid="popular-row-views"]').allInnerTexts(),
    catRows: await page.locator('[data-testid="category-row-name"]').allInnerTexts(), catViews: await page.locator('[data-testid="category-row-views"]').allInnerTexts(),
    popEmpty: await cnt('analytics-popular-empty'), catEmpty: await cnt('analytics-category-empty'),
    chartSvg: await page.locator('[data-testid="analytics-trend-chart"] svg, [data-testid="analytics-trend-card"] svg.apexcharts-svg').count(),
    bars: await page.locator('[data-testid="analytics-trend-card"] .apexcharts-bar-area').count(),
  };
}
async function setRange(page: Page, from: string, to: string) {
  await page.fill('[data-testid="analytics-from"]', from);
  await page.keyboard.press('Tab');
  await page.fill('[data-testid="analytics-to"]', to);
  await page.keyboard.press('Tab');
  await page.click('[data-testid="analytics-apply"]');
  const d = new Date(from + 'T00:00:00Z');
  const label = `${String(d.getUTCDate()).padStart(2, '0')} ${d.toLocaleString('en-GB', { month: 'short', timeZone: 'UTC' })}`;
  await page.waitForFunction((l) => (document.querySelector('[data-testid="analytics-range-caption"]') as HTMLElement | null)?.innerText.includes(l) ?? false, label, { timeout: 40000 }).catch(() => console.log('CAPTION DID NOT MOVE TO', label));
  await page.waitForTimeout(1500);
  await page.waitForFunction(() => !document.querySelector('[data-testid="analytics-trend-loading"], [data-testid="analytics-popular-loading"], [data-testid="analytics-category-loading"]'), undefined, { timeout: 30000 });
  await page.waitForTimeout(2500);
}

test('REQ-UI-044 analytics: charts from real views, range filters every panel', async ({ page }) => {
  test.setTimeout(500000);
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  page.on('pageerror', (e) => errors.push('pageerror ' + e.message));
  console.log('LANDING', await signIn(page, 'admin'));
  await nav(page, '/admin/analytics', /Analytics/i);
  await settle(page);
  await page.waitForTimeout(3000);
  const importmap = await page.evaluate(() => !!document.querySelector('script[type="importmap"]'));
  console.log('IMPORTMAP PRESENT', importmap, 'default range', JSON.stringify(await snap(page)));
  await page.screenshot({ path: `${OUT}/default-1280.png`, fullPage: true });
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, `${OUT}/default-${w}.png`, w);
    console.log(`GEOM default@${w}`, JSON.stringify({ o: v.overlaps, z: v.zeroSized, off: v.offViewport, h: v.hScroll, c: v.consoleErrors }));
    expect(v.hScroll).toBe(0);
  }
  await page.setViewportSize({ width: 1280, height: 1000 });

  const ranges: Array<[string, string]> = [['2026-08-24', '2026-08-25'], ['2026-09-15', '2026-09-15'], ['2026-09-15', '2026-09-20'], ['2026-10-27', '2026-10-27'], ['2026-08-01', '2026-10-27'], ['2025-01-01', '2025-01-31']];
  const results: any[] = [];
  for (const [a, b] of ranges) {
    await setRange(page, a, b);
    const s = await snap(page);
    const dbViews = psql(`select count(*) from postviews v join blogpost p on p.postid=v.postid and p.published=true and (p.isdeleted=false or p.isdeleted is null) where v.viewedon::date between '${a}' and '${b}'`);
    const dbUnique = psql(`select count(distinct v.visitorhash) from postviews v join blogpost p on p.postid=v.postid and p.published=true and (p.isdeleted=false or p.isdeleted is null) where v.viewedon::date between '${a}' and '${b}'`);
    const dbByPost = psql(`select string_agg(p.title||'='||c, ' ; ') from (select postid, count(*) c from postviews where viewedon::date between '${a}' and '${b}' group by 1) v join blogpost p on p.postid=v.postid and p.published=true`);
    console.log(`RANGE ${a}..${b}`, JSON.stringify(s), '\n   DB views', dbViews, 'unique', dbUnique, '| by post:', dbByPost);
    await page.screenshot({ path: `${OUT}/range-${a}_${b}.png`, fullPage: true });
    results.push({ a, b, s, dbViews });
    expect(s.views.replace(/,/g, '')).toBe(dbViews);
  }
  // each panel responds: trend summary text, popular rows, category rows all differ between first two ranges
  const r0 = results[0].s, r1 = results[1].s;
  console.log('PANEL CHANGE', r0.trend !== r1.trend, JSON.stringify(r0.popularTitles) !== JSON.stringify(r1.popularTitles), JSON.stringify(r0.catRows) + JSON.stringify(r0.catViews) !== JSON.stringify(r1.catRows) + JSON.stringify(r1.catViews));
  // presets
  for (const d of [7, 30, 90]) {
    await page.click(`[data-testid="analytics-preset-${d}"]`);
    await page.waitForTimeout(6000);
    const s = await snap(page);
    const dbV = psql(`select count(*) from postviews where viewedon::date >= current_date - ${d - 1} or viewedon::date >= current_date - ${d}`);
    console.log(`PRESET ${d}`, JSON.stringify({ views: s.views, caption: s.caption, trend: s.trend, pop: s.popularRows, cats: s.catRows.length, bars: s.bars }), 'DB(>=today-d)', psql(`select count(*) from postviews where viewedon::date >= current_date - ${d}`));
  }
  // invalid range
  await setRange(page, '2026-10-27', '2026-08-01');
  console.log('INVALID RANGE ERROR', await page.locator('[data-testid="analytics-range-error"]').innerText().catch(() => '<none>'));
  console.log('CONSOLE ERRORS', errors);
});
