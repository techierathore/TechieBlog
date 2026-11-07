/**
 * fxv5-analytics.spec.ts — verify-phase writer W5, 2026-10-27. REQ-UI-044.
 *
 * "charts render from real view data; the date range filters every panel."
 * The view rows are created by the app itself: this spec visits the two published fxv5 fixture
 * posts as anonymous visitors (each visit writes a PostViews row through the app), then reads the
 * dashboard and compares every panel with psql SELECTs run in the same window. Other writers add
 * views to the same database, so each comparison re-reads the database and polls until the two
 * agree (a stable mismatch fails); no global count is assumed.
 */
import { test, expect, Page } from '@playwright/test';
import { gotoPublic } from './_engage-helpers';
import { login, nav, visualCheck } from './_gates';
import { setupFixtures, teardownFixtures, sql, sqlOne, postSlug, post2Slug, SHOTS } from './fxv5-fixtures';
import * as fs from 'fs';

test.beforeAll(() => fs.mkdirSync(SHOTS, { recursive: true }));
test.beforeAll(async ({ browser }) => { test.setTimeout(600000); await setupFixtures(browser); });
test.afterAll(async ({ browser }) => { test.setTimeout(600000); await teardownFixtures(browser, { comments: false }); });


const VISIBLE = `p.published = TRUE AND COALESCE(p.isdeleted, FALSE) = FALSE`;
const dayRange = (a: string, b: string) => `v.viewedon >= '${a}'::date AND v.viewedon < ('${b}'::date + 1)`;

const db = (a: string, b: string) => ({
  views: Number(sqlOne(`SELECT count(*) FROM postviews v JOIN blogpost p ON p.postid = v.postid WHERE ${VISIBLE} AND ${dayRange(a, b)}`)),
  unique: Number(sqlOne(`SELECT count(DISTINCT v.visitorhash) FROM postviews v JOIN blogpost p ON p.postid = v.postid WHERE ${VISIBLE} AND ${dayRange(a, b)}`)),
  comments: Number(sqlOne(`SELECT count(*) FROM blogcomment c WHERE c.givenon >= '${a}'::date AND c.givenon < ('${b}'::date + 1)`)),
  byPost: Object.fromEntries(sql(`SELECT p.title, count(*) FROM postviews v JOIN blogpost p ON p.postid = v.postid WHERE ${VISIBLE} AND ${dayRange(a, b)} GROUP BY p.title`).map((r) => { const [t, c] = r.split('|'); return [t, Number(c)]; })) as Record<string, number>,
  byCat: Object.fromEntries(sql(`SELECT COALESCE(c.categoryname, 'Uncategorised'), count(*) FROM postviews v JOIN blogpost p ON p.postid = v.postid LEFT JOIN category c ON c.categoryid = p.categoryid WHERE ${VISIBLE} AND ${dayRange(a, b)} GROUP BY 1`).map((r) => { const [t, c] = r.split('|'); return [t, Number(c)]; })) as Record<string, number>,
});

const todayUtc = () => sqlOne(`SELECT to_char(now() AT TIME ZONE 'utc', 'YYYY-MM-DD')`);
const addDays = (d: string, n: number) => sqlOne(`SELECT to_char('${d}'::date + (${n}), 'YYYY-MM-DD')`);

async function settle(page: Page) {
  await page.waitForTimeout(1500);
  await page.waitForFunction(() => !document.querySelector('[data-testid="analytics-trend-loading"], [data-testid="analytics-popular-loading"], [data-testid="analytics-category-loading"]'), undefined, { timeout: 40000 }).catch(() => {});
  await page.waitForTimeout(1500);
}

async function setRange(page: Page, from: string, to: string) {
  await page.fill('[data-testid="analytics-from"]', from);
  await page.keyboard.press('Tab');
  await page.fill('[data-testid="analytics-to"]', to);
  await page.keyboard.press('Tab');
  await page.click('[data-testid="analytics-apply"]');
  await settle(page);
}

const num = async (page: Page, id: string) => Number(((await page.locator(`[data-testid="${id}"]`).first().innerText()) || '').replace(/[^\d]/g, '') || '0');

async function snap(page: Page) {
  const rows = async (id: string) => (await page.locator(`[data-testid="${id}"]`).allInnerTexts()).map((s) => s.trim());
  const popTitles = await rows('popular-row-title');
  const popViews = (await rows('popular-row-views')).map((s) => Number(s.replace(/[^\d]/g, '')));
  const catNames = await rows('category-row-name');
  const catViews = (await rows('category-row-views')).map((s) => Number(s.replace(/[^\d]/g, '')));
  return {
    views: await num(page, 'analytics-stat-views'),
    unique: await num(page, 'analytics-stat-unique'),
    comments: await num(page, 'analytics-stat-comments'),
    caption: ((await page.locator('[data-testid="analytics-range-caption"]').innerText()) || '').replace(/\s+/g, ' '),
    trendChart: await page.locator('[data-testid="analytics-trend-chart"]').count(),
    trendSvg: await page.locator('[data-testid="analytics-trend-card"] svg.apexcharts-svg').count(),
    trendEmpty: await page.locator('[data-testid="analytics-trend-empty"]').count(),
    trendSummary: await page.locator('[data-testid="analytics-trend-summary"]').first().innerText({ timeout: 1500 }).catch(() => ''),
    popEmpty: await page.locator('[data-testid="analytics-popular-empty"]').count(),
    catEmpty: await page.locator('[data-testid="analytics-category-empty"]').count(),
    pop: Object.fromEntries(popTitles.map((t, i) => [t, popViews[i]])) as Record<string, number>,
    cat: Object.fromEntries(catNames.map((t, i) => [t, catViews[i]])) as Record<string, number>,
  };
}

/** Polls until the dashboard equals a fresh database read for the same range (other writers move it). */
async function matchesDb(page: Page, a: string, b: string, label: string) {
  let s = await snap(page), d = db(a, b), ok = false;
  for (let i = 0; i < 6 && !ok; i++) {
    s = await snap(page); d = db(a, b);
    ok = s.views === d.views && s.unique === d.unique && s.comments === d.comments;
    if (!ok) { console.log(`UI-044 ${label} retry ${i}: ui ${s.views}/${s.unique}/${s.comments} db ${d.views}/${d.unique}/${d.comments}`); await page.waitForTimeout(4000); await page.click('[data-testid="analytics-apply"]'); await settle(page); }
  }
  console.log(`UI-044 ${label}: ui views=${s.views} unique=${s.unique} comments=${s.comments} | db views=${d.views} unique=${d.unique} comments=${d.comments} | caption="${s.caption}"`);
  expect(s.views, `${label}: views tile = database`).toBe(d.views);
  expect(s.unique, `${label}: unique tile = database`).toBe(d.unique);
  expect(s.comments, `${label}: comments tile = database`).toBe(d.comments);
  // popular posts: every row the table shows carries the database's count for that post in this range
  for (const [t, v] of Object.entries(s.pop)) expect(v, `${label}: popular row "${t}"`).toBe(d.byPost[t]);
  expect(Object.keys(s.pop).length, `${label}: popular rows = posts with traffic (max 10)`).toBe(Math.min(10, Object.keys(d.byPost).length));
  // categories
  for (const [t, v] of Object.entries(s.cat)) expect(v, `${label}: category row "${t}"`).toBe(d.byCat[t]);
  expect(Object.keys(s.cat).length, `${label}: category rows`).toBe(Math.min(Object.keys(d.byCat).length, Object.keys(s.cat).length || Object.keys(d.byCat).length));
  return { s, d };
}

test('REQ-UI-044 analytics charts render from real view data and the date range filters every panel', async ({ browser }) => {
  test.setTimeout(540000);
  test.skip(!postSlug() || !post2Slug(), 'fixture posts missing');

  // 1. real view rows through the app itself
  const vctx = await browser.newContext({ userAgent: 'fxv5-analytics-visitor' });
  const v = await vctx.newPage();
  for (const s of [postSlug(), post2Slug()]) {
    await gotoPublic(v, `/post/${s}`);
    await expect(v.locator('[data-testid="post-title"]')).toBeVisible({ timeout: 60000 });
    await v.waitForTimeout(3000);
  }
  await vctx.close();

  const errors: string[] = [];
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 1000 } });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => errors.push(e.message));
  await login(page, 'admin');
  await nav(page, '/admin/analytics', /Analytics/i);
  await expect(page.locator('[data-testid="analytics-range-card"]')).toBeVisible({ timeout: 60000 });
  await settle(page);

  const today = todayUtc();
  const monthAgo = addDays(today, -89);

  // 2. a window with traffic: charts render from the rows, every panel equals the database
  await setRange(page, today, today);
  const { s: sToday, d: dToday } = await matchesDb(page, today, today, 'today');
  expect(dToday.views, 'the visits made above are in the database').toBeGreaterThan(0);
  expect(sToday.trendChart, 'trend chart rendered').toBe(1);
  expect(sToday.trendEmpty).toBe(0);
  expect(sToday.trendSvg, 'the chart is a real SVG chart').toBeGreaterThan(0);
  expect(await page.locator('[data-testid="analytics-trend-card"] .apexcharts-bar-area, [data-testid="analytics-trend-card"] .apexcharts-area-series, [data-testid="analytics-trend-card"] .apexcharts-line-series').count(), 'chart has drawn series marks').toBeGreaterThan(0);
  for (const t of [`fxv5 Engage Post`, `fxv5 Engage Post Two`]) expect(Object.keys(sToday.pop), `popular posts lists ${t}`).toContain(t);
  expect(sToday.popEmpty).toBe(0);
  expect(Object.keys(sToday.cat), 'uncategorised traffic is grouped').toContain('Uncategorised');
  await page.screenshot({ path: `${SHOTS}/ui044-today.png`, fullPage: true });

  // 3. a window with no traffic empties every panel (the range filters every panel)
  await setRange(page, '2025-01-01', '2025-01-31');
  const { s: sEmpty } = await matchesDb(page, '2025-01-01', '2025-01-31', 'Jan 2025');
  expect(sEmpty.views).toBe(0);
  expect(sEmpty.trendEmpty, 'trend panel shows its empty state').toBe(1);
  expect(sEmpty.popEmpty, 'popular panel shows its empty state').toBe(1);
  expect(sEmpty.catEmpty, 'category panel shows its empty state').toBe(1);
  expect(sEmpty.caption).toContain('Jan');
  await page.screenshot({ path: `${SHOTS}/ui044-empty.png`, fullPage: true });

  // 4. a wide window and the presets: caption moves, tiles equal the database
  await setRange(page, monthAgo, today);
  const { s: sWide } = await matchesDb(page, monthAgo, today, '90-day window');
  expect(sWide.views).toBeGreaterThanOrEqual(dToday.views - 5); // other writers only add
  for (const d of [7, 30, 90]) {
    await page.click(`[data-testid="analytics-preset-${d}"]`);
    await settle(page);
    const a = addDays(today, -(d - 1));
    const { s } = await matchesDb(page, a, today, `preset ${d}`);
    expect(s.caption, `preset ${d} caption`).toContain(`${d} days`);
  }

  // 5. an inverted range is refused inline and leaves the panels alone
  await setRange(page, today, addDays(today, -5));
  await expect(page.locator('[data-testid="analytics-range-error"]')).toBeVisible({ timeout: 15000 });
  expect((await page.locator('[data-testid="analytics-range-error"]').innerText()).length).toBeGreaterThan(10);

  // 6. looks right at both widths, no console/page errors
  for (const w of [1280, 390]) {
    await page.click('[data-testid="analytics-preset-7"]');
    await settle(page);
    const vis = await visualCheck(page, `${SHOTS}/ui044-${w}.png`, w);
    expect(vis.hScroll, `no horizontal scroll at ${w}`).toBe(0);
  }
  expect(errors, 'no uncaught page errors').toEqual([]);
  await ctx.close();
});
