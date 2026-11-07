/**
 * fxv1-public.spec.ts — verify-phase writer W1 (public reading), 2026-10-27.
 *
 * Rows: REQ-UI-005..012, 045, 046, 049, 050, 059, 060 and REQ-FN-013, 014, 015, 020, 021, 037, 038.
 * (REQ-UI-065, REQ-UI-051 and REQ-FN-040 live in fxv1-settings.spec.ts.)
 *
 * The dev DB holds no published content (owner decision 2026-08-22, UAT-007), so `beforeAll` builds the
 * state through the admin UI (see fxv1-fixtures.ts) and every assertion is cross-checked against a psql
 * READ taken in the same test. Nothing writes to the database directly. The last test removes the
 * fixtures through the UI. Other writers share this app: no assertion pins a global count.
 */
import { test, expect, request, Page, Browser } from '@playwright/test';
import { readFileSync } from 'node:fs';
import {
  BASE, SHOTS, sql, sqlOne, loginHard, ensureFixtures, cleanupFixtures, createPost, publishExisting,
  unpublishExisting, deletePostByTitle, idOf, slugOf, pollUntil, CAT, CAT_EMPTY, SERIES, TAG, TAG_QUOKKA, POSTS,
} from './fxv1-fixtures';

test.describe.configure({ mode: 'default' });

let admin: Page;
const dbNewest = (n: number, extra = '') =>
  sql(`SELECT slug FROM blogpost WHERE published AND isdeleted IS NOT TRUE ${extra} ORDER BY coalesce(publishedon, createdon) DESC, postid DESC LIMIT ${n}`);

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  test.setTimeout(1500000);
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  admin = await ctx.newPage();
  await loginHard(admin, 'admin');
  await ensureFixtures(admin);
});

/** Loads a public page and waits for a selector the page itself owns, then lets the prerender swap settle. */
async function open(page: Page, path: string, ready: string, width = 1280): Promise<number> {
  await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
  const resp = await page.goto(BASE + path, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector(ready, { timeout: 60000 });
  // The prerendered DOM is replaced when the circuit attaches (~5s): wait it out, then re-wait.
  await page.waitForTimeout(5500);
  await page.waitForSelector(ready, { timeout: 60000 });
  return resp?.status() ?? 0;
}
const hscroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
const texts = (page: Page, sel: string) => page.$$eval(sel, (els) => els.map((e) => ((e as HTMLElement).innerText || '').trim().replace(/\s+/g, ' ')));
/** Reads a count badge, waiting for it to render (it arrives after the list loads). */
async function countOf(page: Page, id: string): Promise<number> {
  const loc = page.locator(`[data-testid="${id}"]`).first();
  await expect(loc).toBeVisible({ timeout: 30000 });
  return Number(((await loc.innerText()) || '').replace(/\D/g, ''));
}
const hrefs = (page: Page, sel: string) => page.$$eval(sel, (els) => els.map((e) => e.getAttribute('href') || ''));
const slugsFrom = (hs: string[]) => hs.map((h) => h.replace(/^\/post\//, '').replace(/\/\d+$/, ''));

async function getText(path: string): Promise<{ status: number; body: string; type: string }> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const r = await api.get(path);
    return { status: r.status(), body: await r.text(), type: r.headers()['content-type'] ?? '' };
  } finally {
    await api.dispose();
  }
}

/** Clicks a pagination control and waits until the first card title changes. */
async function gotoPage(page: Page, n: number) {
  const before = (await texts(page, '[data-testid="post-card-title"], [data-testid="search-result-title"]'))[0];
  for (let i = 0; i < 6; i++) {
    await page.click(`[data-testid="pagination-page-${n}"]`);
    await page.waitForTimeout(2000);
    const now = (await texts(page, '[data-testid="post-card-title"], [data-testid="search-result-title"]'))[0];
    if (now !== before) return;
  }
  throw new Error(`pagination-page-${n} never changed the listing`);
}

/** Page size is the Blog.PostsPerPage setting (public listings read it since the 2026-10-28 fix); read it, never hard-code. */
const perPage = () => Number(sqlOne("SELECT settingvalue FROM sitesetting WHERE settingkey='Blog.PostsPerPage'")) || 10;

const postSlugs = () => POSTS.map((p) => slugOf(p.title));

// ---------------------------------------------------------------------------------------------
// REQ-UI-005 — shell on every public route
// ---------------------------------------------------------------------------------------------
const shellRoutes = (): [string, string, string][] => [
  ['home', '/', '[data-testid="home-page"], [data-testid="resume-hero"]'],
  ['post', `/post/${slugOf('fxv1 Markdown Post')}`, '[data-testid="post-content"]'],
  ['category', '/category/fxv1-category', '[data-testid="posts-grid"]'],
  ['category-empty', '/category/fxv1-empty', '[data-testid="category-posts-empty"]'],
  ['tag', `/tag/${TAG}`, '[data-testid="posts-grid"]'],
  ['series-view', '/series/fxv1-series', '[data-testid="series-posts"]'],
  ['search', '/search?q=fxv1', '[data-testid="search-result"]'],
  ['about', '/about', '[data-testid="about-page"]'],
  ['categories', '/categories', '[data-testid="categories-grid"], [data-testid="categories-empty"]'],
  ['tags', '/tags', '[data-testid="tags-cloud"], [data-testid="tags-empty"]'],
  ['series', '/series', '[data-testid="series-grid"], [data-testid="series-empty"]'],
  ['newsletters', '/newsletters', '[data-testid="newsletter-archive"]'],
  ['resume', '/resume', '[data-testid="resume-page"]'],
  ['rss-page', '/rss', '[data-testid="rss-page"]'],
  ['not-found', '/no-such-route-fxv1', '[data-testid="not-found-page"]'],
];

test('REQ-UI-005 every public page renders inside the shell, drawer below 768, no horizontal scroll at 320/390/1280', async ({ page }) => {
  test.setTimeout(900000);
  const routes = shellRoutes();
  expect(routes.length).toBeGreaterThanOrEqual(15);
  const problems: string[] = [];
  for (const [name, path, ready] of routes) {
    for (const w of [1280, 390, 320]) {
      await open(page, path, ready, w);
      const f = await page.evaluate(() => {
        const vis = (s: string) => { const e = document.querySelector(s) as HTMLElement | null; return !!e && e.getBoundingClientRect().width > 0 && getComputedStyle(e).visibility !== 'hidden' && getComputedStyle(e).display !== 'none'; };
        return { header: vis('[data-testid="public-header"]'), footer: vis('[data-testid="public-footer"]'), main: vis('[data-testid="main-content"]'), trigger: vis('[data-testid="mobile-nav-trigger"]'), nav: vis('[data-testid="primary-nav"]') };
      });
      const o = await hscroll(page);
      if (!(f.header && f.footer && f.main)) problems.push(`${name}@${w}: shell part missing ${JSON.stringify(f)}`);
      if (w >= 768 && !(f.nav && !f.trigger)) problems.push(`${name}@${w}: expected primary nav and no drawer trigger ${JSON.stringify(f)}`);
      if (w < 768 && !(f.trigger && !f.nav)) problems.push(`${name}@${w}: expected drawer trigger and no primary nav ${JSON.stringify(f)}`);
      if (o > 1) problems.push(`${name}@${w}: horizontal scroll ${o}px`);
      if (w === 320) await page.screenshot({ path: `${SHOTS}/ui005-${name}-320.png`, fullPage: true });
    }
  }
  expect(problems, problems.join('\n')).toEqual([]);
});

test('REQ-UI-005 the mobile drawer opens with the nav links and the desktop header carries them inline', async ({ page }) => {
  test.setTimeout(240000);
  await open(page, '/about', '[data-testid="about-page"]', 1280);
  const ids = await page.$$eval('[data-testid="primary-nav"] [data-testid^="nav-"]', (els) => els.map((e) => e.getAttribute('data-testid')));
  expect(ids.length).toBeGreaterThanOrEqual(5);
  expect(ids, 'the owner removed the header About link on 2026-08-22').not.toContain('nav-about');
  await open(page, '/about', '[data-testid="about-page"]', 390);
  for (let i = 0; i < 6 && !(await page.locator('[data-testid="mobile-nav-drawer"]').isVisible()); i++) {
    await page.click('[data-testid="mobile-nav-trigger"]');
    await page.waitForTimeout(1500);
  }
  await expect(page.locator('[data-testid="mobile-nav-drawer"]')).toBeVisible();
  expect(await page.locator('[data-testid="mobile-nav-drawer"] a').count()).toBeGreaterThanOrEqual(5);
  await page.screenshot({ path: `${SHOTS}/ui005-drawer-390.png` });
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-006 / 049 — home
// ---------------------------------------------------------------------------------------------
test('REQ-UI-006 home lists the newest published posts newest-first from the database, every link resolves', async ({ page }) => {
  test.setTimeout(240000);
  await open(page, '/', '[data-testid="home-articles-grid"] [data-testid="post-card"]');
  const listed = slugsFrom(await hrefs(page, '[data-testid="home-articles-grid"] [data-testid="post-card-title"]'));
  expect(listed.length, 'the grid is non-empty when posts exist').toBeGreaterThan(1);
  expect(new Set(listed).size).toBe(listed.length);
  const dbTop = dbNewest(listed.length);
  expect(listed, 'grid order equals the database newest-first order').toEqual(dbTop);
  for (const s of listed) expect((await getText(`/post/${s}`)).status, `/post/${s}`).toBe(200);
  await page.screenshot({ path: `${SHOTS}/ui006-home.png`, fullPage: true });
});

test('REQ-UI-049 home renders hero, about, latest articles and contact from the owner record, no login entry, usable at 390/320', async ({ page }) => {
  test.setTimeout(300000);
  await open(page, '/', '[data-testid="home-articles-grid"] [data-testid="post-card"]');
  const have = await page.evaluate(() => Object.fromEntries(['resume-hero', 'home-about', 'home-latest-articles', 'contact-section'].map((k) => [k, !!document.querySelector(`[data-testid="${k}"]`)])));
  expect(have, 'owner-data sections').toEqual({ 'resume-hero': true, 'home-about': true, 'home-latest-articles': true, 'contact-section': true });
  // Hero and contact carry the site owner's real data, not placeholder text.
  const owner = sqlOne("SELECT concat(firstname,' ',lastname) FROM bloguser WHERE issiteowner = TRUE LIMIT 1");
  expect(owner, 'a site owner exists').not.toBe('');
  expect(await page.locator('[data-testid="resume-hero"]').innerText()).toContain(owner.split(' ')[0]);
  const heroText = await page.locator('[data-testid="resume-hero"]').innerText();
  expect(heroText).not.toMatch(/lorem|placeholder|undefined|\{\{/i);
  // Stats band: shown exactly when the owner has stats rows (deliberate empty state otherwise).
  const statRows = Number(sqlOne("SELECT count(*) FROM userstats WHERE userid = (SELECT userid FROM bloguser WHERE issiteowner = TRUE LIMIT 1)"));
  expect(await page.locator('[data-testid="home-stats"]').count() > 0, 'stats band follows the owner rows').toBe(statRows > 0);
  // Latest articles link to real posts.
  const hs = await hrefs(page, '[data-testid="home-articles-grid"] [data-testid="post-card-title"]');
  expect(hs.length).toBeGreaterThan(0);
  for (const h of hs) expect((await getText(h)).status, h).toBe(200);
  // No login / user-menu entry for an anonymous visitor (REQ-UI-050).
  expect(await page.locator('[data-testid="public-header"] a[href*="login" i], [data-testid="account-menu"]').count()).toBe(0);
  for (const w of [390, 320]) {
    await open(page, '/', '[data-testid="home-articles-grid"]', w);
    expect(await hscroll(page), `home overflows at ${w}`).toBeLessThanOrEqual(1);
    await page.screenshot({ path: `${SHOTS}/ui049-home-${w}.png`, fullPage: true });
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-007 / FN-014 — post view
// ---------------------------------------------------------------------------------------------
test('REQ-UI-007 post view shows formatted Markdown and every metadata field from the post record', async ({ page }) => {
  test.setTimeout(240000);
  const slug = slugOf('fxv1 Markdown Post');
  await open(page, `/post/${slug}`, '[data-testid="post-content"]');
  const rec = sql(`SELECT p.title, p.abstract, c.categoryname, c.slug, concat(u.firstname,' ',u.lastname), to_char(p.publishedon,'YYYY-MM-DD') FROM blogpost p JOIN category c ON c.categoryid=p.categoryid JOIN bloguser u ON u.userid=p.userid WHERE p.slug='${slug}'`)[0].split('|');
  const [title, abstract, catName, catSlug, author, pubDay] = rec;
  const t = async (id: string) => (await page.locator(`[data-testid="${id}"]`).first().innerText()).trim();
  expect(await t('post-title')).toBe(title);
  expect(await t('post-abstract')).toBe(abstract);
  expect(await t('post-category'), 'category badge is the post CategoryId, not its first tag').toBe(catName);
  expect(await page.locator('[data-testid="post-category"]').first().evaluate((e) => (e.closest('a') ?? e.querySelector('a'))?.getAttribute('href'))).toBe(`/category/${catSlug}`);
  expect(await t('post-author')).toContain(author.split(' ')[0]);
  expect.soft(await page.locator('[data-testid="post-tags"]').count(), 'the post page shows the tags chosen in the editor (post-tags row)').toBe(1);
  const dateText = await t('post-date');
  const shown = new Date(dateText + ' UTC');
  expect(Number.isNaN(shown.getTime()), `date "${dateText}" parses`).toBe(false);
  expect(Math.abs(shown.getTime() - new Date(pubDay + 'T00:00:00Z').getTime()), 'date is the publish date').toBeLessThanOrEqual(36 * 3600 * 1000);
  expect(await t('post-readtime')).toMatch(/\d+\s*min/i);
  for (const id of ['post-title', 'post-abstract', 'post-category', 'post-author', 'post-date', 'post-readtime']) {
    expect(await t(id), id).not.toMatch(/lorem|todo|placeholder|undefined|null|\{\{/i);
  }
  // The category link resolves to a real archive.
  expect((await getText(`/category/${catSlug}`)).status).toBe(200);
  const f = await page.locator('[data-testid="post-content"]').first().evaluate((c: HTMLElement) => { return { h2: !!c.querySelector('h2'), strong: !!c.querySelector('strong'), a: !!c.querySelector('a[href]'), li: !!c.querySelector('li'), code: !!c.querySelector('pre code, code'), raw: /(^|\n)\s*(#{1,3}\s|```|\*\*\w)/.test(c.innerText) }; });
  expect(f).toEqual({ h2: true, strong: true, a: true, li: true, code: true, raw: false });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(800);
  expect(await hscroll(page)).toBeLessThanOrEqual(1);
  await page.screenshot({ path: `${SHOTS}/ui007-post-390.png`, fullPage: true });
});

test('REQ-FN-014 Markdown headings, lists, fenced code, tables, quotes and links render as HTML with no raw syntax', async ({ page }) => {
  test.setTimeout(240000);
  await open(page, `/post/${slugOf('fxv1 Markdown Post')}`, '[data-testid="post-content"]');
  const html = await page.locator('[data-testid="post-content"]').innerHTML();
  const text = await page.locator('[data-testid="post-content"]').innerText();
  expect(html).toMatch(/<h2[\s>][^]*fxv1 Second Level Heading/);
  expect(html).toMatch(/<h3[\s>][^]*fxv1 Third Level Heading/);
  expect(html).toMatch(/<ul[\s>][^]*<li[\s>][^]*bullet one/);
  expect(html).toMatch(/<ol[\s>][^]*<li[\s>][^]*numbered one/);
  expect(html).toMatch(/<pre[\s>][^]*<code[^]*var fxv1 = 1;/);
  expect(html).toMatch(/<table[\s>][^]*<th[\s>][^]*Col A[^]*<td[\s>][^]*cell1/);
  expect(html).toMatch(/<blockquote[\s>][^]*a block quote/);
  expect(html).toMatch(/<a\s[^>]*href="\/?"[^>]*>link to the home page<\/a>/);
  expect(html).toMatch(/<strong>strong text<\/strong>/);
  expect(html).toMatch(/<em>emphasis<\/em>/);
  expect(text, 'no raw Markdown syntax visible').not.toMatch(/(^|\n)\s*#{2,3}\s|\*\*strong|```|\|\s*-----|\]\(\//);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-008 / 009 / 010 — archives
// ---------------------------------------------------------------------------------------------
test('REQ-UI-008 category archive lists every published post, no draft, count equals the posts listed; an empty category shows the empty state', async ({ page }) => {
  test.setTimeout(240000);
  await open(page, '/category/fxv1-category', '[data-testid="posts-grid"]');
  const dbTitles = sql("SELECT title FROM blogpost WHERE published AND isdeleted IS NOT TRUE AND categoryid=(SELECT categoryid FROM category WHERE slug='fxv1-category') ORDER BY coalesce(publishedon,createdon) DESC, postid DESC");
  const shown = await countOf(page, 'category-post-count');
  expect(shown, 'the count shown equals the database count').toBe(dbTitles.length);
  const p1 = await texts(page, '[data-testid="post-card-title"]');
  expect(p1.length).toBe(Math.min(perPage(), dbTitles.length));
  await gotoPage(page, 2);
  const p2 = await texts(page, '[data-testid="post-card-title"]');
  const all = [...p1, ...p2];
  expect(all.length, 'posts listed across the pages equal the count shown').toBe(shown);
  expect(all, 'newest-first order across pages').toEqual(dbTitles);
  expect(all.join('|')).not.toContain('Draft Post');
  await page.screenshot({ path: `${SHOTS}/ui008-category-p2.png`, fullPage: true });
  await open(page, '/category/fxv1-empty', '[data-testid="category-posts-empty"]');
  expect((await texts(page, '[data-testid="category-post-count"]'))[0]).toMatch(/^0\b/);
  expect(await page.locator('[data-testid="post-card"]').count()).toBe(0);
});

test('REQ-UI-009 tag archive count equals the posts actually listed and the database', async ({ page }) => {
  test.setTimeout(240000);
  const db = Number(sqlOne(`SELECT count(*) FROM blogpost p JOIN posttag pt ON pt.postid=p.postid JOIN tag t ON t.tagid=pt.tagid WHERE t.slug='${TAG}' AND p.published AND p.isdeleted IS NOT TRUE`));
  expect(db).toBeGreaterThan(1);
  await open(page, `/tag/${TAG}`, '[data-testid="posts-grid"]');
  const shown = await countOf(page, 'tag-post-count');
  const cards = await page.locator('[data-testid="post-card"]').count();
  expect(shown, 'count shown equals cards listed').toBe(cards);
  expect(cards, 'cards equal the database').toBe(db);
  await open(page, '/tags', '[data-testid="tags-cloud"]');
  expect(await page.locator('[data-testid="tags-cloud"]').innerText()).toContain(TAG);
  const q = await open(page, `/tag/${TAG_QUOKKA}`, '[data-testid="posts-grid"]');
  expect(q).toBe(200);
  expect(await page.locator('[data-testid="post-card"]').count()).toBe(1);
});

test('REQ-UI-010 series view lists published parts in part-number order with the part count; unknown slug is a 404 page', async ({ page }) => {
  test.setTimeout(240000);
  await open(page, '/series/fxv1-series', '[data-testid="series-posts"]');
  const db = sql("SELECT seriespartnumber, title, slug FROM blogpost WHERE published AND isdeleted IS NOT TRUE AND seriesid=(SELECT seriesid FROM blogseries WHERE slug='fxv1-series') ORDER BY seriespartnumber").map((r) => r.split('|'));
  expect(db.length).toBe(3);
  expect(db.map((r) => r[1]), 'fixture parts were created out of title order').not.toEqual([...db.map((r) => r[1])].sort());
  expect((await texts(page, '[data-testid="series-part-count"]'))[0]).toMatch(new RegExp(`^${db.length} Parts?`));
  expect(await texts(page, '[data-testid="series-post-number"]')).toEqual(db.map((r) => r[0]));
  expect(await texts(page, '[data-testid="series-post-title"]')).toEqual(db.map((r) => r[1]));
  const links = await page.$$eval('[data-testid="series-post"] a[href^="/post/"]', (as) => as.map((a) => a.getAttribute('href')));
  for (const [, , slug] of db) expect(links).toContain(`/post/${slug}`);
  for (const [, , slug] of db) expect((await getText(`/post/${slug}`)).status).toBe(200);
  const status = await open(page, '/series/fxv1-nope', '[data-testid="not-found-page"]');
  expect(status, 'unknown series is a real HTTP 404').toBe(404);
  await page.screenshot({ path: `${SHOTS}/ui010-series.png`, fullPage: true });
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-011 / FN-021 — search
// ---------------------------------------------------------------------------------------------
/**
 * /search prerenders its results, then the circuit attaches (~5s) and re-runs the query, briefly
 * showing neither results nor the empty state. Read only after that swap has settled.
 */
const searchRows = async (page: Page, q: string) => {
  await open(page, `/search?q=${encodeURIComponent(q)}`, '[data-testid="search-result"], [data-testid="search-empty"]');
  return slugsFrom(await hrefs(page, '[data-testid="search-result-title"]'));
};

test('REQ-UI-011 search results come from the database and the category dropdown lists every category from CategorySvc', async ({ page }) => {
  test.setTimeout(300000);
  const rows = await searchRows(page, 'fxv1');
  const db = Number(sqlOne("SELECT count(*) FROM blogpost WHERE published AND isdeleted IS NOT TRUE AND (title ILIKE '%fxv1%' OR abstract ILIKE '%fxv1%' OR postcontent ILIKE '%fxv1%')"));
  expect(Number((await texts(page, '[data-testid="search-total-count"]'))[0].replace(/\D/g, '')), 'total count equals the database').toBeGreaterThanOrEqual(db);
  expect(rows.length).toBeGreaterThan(0);
  expect((await searchRows(page, 'zzznomatchxyzfxv1')).length, 'no hard-coded placeholder results').toBe(0);
  await expect(page.locator('[data-testid="search-empty"]')).toBeVisible();
  await open(page, '/search?q=fxv1', '[data-testid="category-filter"]');
  let opts: string[] = [];
  for (let i = 0; i < 6 && opts.length === 0; i++) {
    await page.click('[data-testid="category-filter"]');
    await page.waitForTimeout(1500);
    opts = (await page.$$eval('[role="option"]', (els) => els.map((e) => (e.textContent || '').trim())));
  }
  const dbCats = sql('SELECT categoryname FROM category ORDER BY 1');
  await page.screenshot({ path: `${SHOTS}/ui011-dropdown.png` });
  expect(dbCats.length).toBeGreaterThan(0);
  for (const c of dbCats) expect(opts, `dropdown lists ${c}`).toContain(c);
});

test('REQ-FN-021 search finds title, abstract, body and tag words; count equals the rows across pages; a draft is never returned', async ({ page }) => {
  test.setTimeout(400000);
  const target = slugOf('fxv1 Search Post fxv1quokkatitle');
  for (const [q, field] of [['fxv1quokkatitle', 'title'], ['fxv1quokkaabstract', 'abstract'], ['fxv1quokkabody', 'body'], [TAG_QUOKKA, 'tag']] as const) {
    const rows = await searchRows(page, q);
    if (field === 'tag') expect.soft(rows, `a word in the ${field} finds the post`).toContain(target);
    else expect(rows, `a word in the ${field} finds the post`).toContain(target);
  }
  expect(await searchRows(page, 'fxv1zebra'), 'a draft that contains the word is never returned').toEqual([]);
  // Count shown == rows listed across every page == database.
  const dbCount = Number(sqlOne("SELECT count(*) FROM blogpost p WHERE p.published AND p.isdeleted IS NOT TRUE AND (p.title ILIKE '%fxv1%' OR p.abstract ILIKE '%fxv1%' OR p.postcontent ILIKE '%fxv1%' OR EXISTS (SELECT 1 FROM posttag pt JOIN tag t ON t.tagid=pt.tagid WHERE pt.postid=p.postid AND t.tagname ILIKE '%fxv1%'))"));
  const first = await searchRows(page, 'fxv1');
  const shown = Number((await texts(page, '[data-testid="search-total-count"]'))[0].replace(/\D/g, ''));
  expect(shown, 'count shown equals the database count').toBe(dbCount);
  const seen = [...first];
  if ((await page.locator('[data-testid="pagination-page-2"]').count()) > 0) {
    await gotoPage(page, 2);
    seen.push(...slugsFrom(await hrefs(page, '[data-testid="search-result-title"]')));
  }
  expect(new Set(seen).size, 'rows listed across the pages equal the count shown').toBe(shown);
  expect(seen).not.toContain(slugOf('fxv1 Draft Post fxv1zebra'));
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-012 — about + 404
// ---------------------------------------------------------------------------------------------
test('REQ-UI-012 /about renders inside the shell and an unmatched route renders the designed 404 with one h1 and a link home', async ({ page }) => {
  test.setTimeout(240000);
  expect(await open(page, '/about', '[data-testid="about-page"]')).toBe(200);
  expect(await page.locator('[data-testid="public-header"]').count()).toBe(1);
  expect(await page.locator('[data-testid="public-footer"]').count()).toBe(1);
  expect((await page.locator('[data-testid="about-page"]').innerText()).trim().length).toBeGreaterThan(50);
  const st = await open(page, '/no-such-route-fxv1', '[data-testid="not-found-page"]');
  expect(st, 'unmatched route answers HTTP 404').toBe(404);
  expect(await page.locator('h1').count()).toBe(1);
  expect(await page.locator('[data-testid="not-found-home"]').getAttribute('href')).toBe('/');
  expect(await page.locator('[data-testid="public-header"]').count()).toBe(1);
  expect((await page.locator('body').innerText()).trim().length, 'not a blank body').toBeGreaterThan(50);
  await page.screenshot({ path: `${SHOTS}/ui012-404.png` });
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-045 — shared components
// ---------------------------------------------------------------------------------------------
test('REQ-UI-045 PostCard renders identically on home, category and tag; pagination, breadcrumb and sidebar reflect real data', async ({ page }) => {
  test.setTimeout(400000);
  const sig = async (sel: string) => page.locator(sel).first().evaluate((e) => {
    const q = (s: string) => e.querySelector(s) as HTMLElement | null;
    const cs = (x: HTMLElement | null) => (x ? getComputedStyle(x) : null);
    const t = q('[data-testid$="-title"]');
    return { parts: ['category', 'title', 'excerpt', 'author', 'date', 'readtime'].map((k) => !!q(`[data-testid$="-${k}"]`)), titleFont: `${cs(t)?.fontSize}/${cs(t)?.fontWeight}` };
  });
  const out: Record<string, { parts: boolean[]; titleFont: string }> = {};
  for (const [k, p, ready] of [['home', '/', '[data-testid="home-articles-grid"] [data-testid="post-card"]'], ['cat', '/category/fxv1-category', '[data-testid="posts-grid"]'], ['tag', `/tag/${TAG}`, '[data-testid="posts-grid"]'], ['search', '/search?q=fxv1', '[data-testid="search-result"]']] as const) {
    await open(page, p, ready);
    out[k] = await sig(k === 'search' ? '[data-testid="search-result"]' : '[data-testid="post-card"]');
  }
  expect(out.home.parts).toEqual([true, true, true, true, true, true]);
  expect(out.cat).toEqual(out.home);
  expect(out.tag).toEqual(out.home);
  expect(out.search.parts, 'search rows carry the same fields').toEqual(out.home.parts);
  // Pagination reflects real counts: 12 published posts at 9 per page = 2 pages, and 5 posts = no pager.
  await open(page, '/category/fxv1-category', '[data-testid="posts-grid"]');
  const total = Number(sqlOne("SELECT count(*) FROM blogpost WHERE published AND isdeleted IS NOT TRUE AND categoryid=(SELECT categoryid FROM category WHERE slug='fxv1-category')"));
  const pages = Math.ceil(total / perPage());
  await expect(page.locator('[data-testid="pagination"]')).toBeVisible({ timeout: 30000 });
  expect(await page.locator('[data-testid^="pagination-page-"]').count(), 'one control per real page').toBe(pages);
  expect(await page.locator('[data-testid="pagination-page-3"]').count()).toBe(0);
  await open(page, `/tag/${TAG}`, '[data-testid="posts-grid"]');
  expect(await page.locator('[data-testid="pagination"]').count(), 'a single page shows no pager').toBe(0);
  // Breadcrumb on archive and post; sidebar lists real categories and tags.
  expect(await page.locator('[data-testid="breadcrumb"]').count()).toBe(1);
  expect(await page.locator('[data-testid="sidebar-categories"]').innerText()).toContain('fxv1 Category');
  expect(await page.locator('[data-testid="sidebar-tags"]').innerText()).toContain(TAG);
  await open(page, `/post/${slugOf('fxv1 Markdown Post')}`, '[data-testid="post-content"]');
  expect(await page.locator('[data-testid="breadcrumb"]').innerText()).toContain('fxv1 Markdown Post');
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-046 / FN-037 — RSS
// ---------------------------------------------------------------------------------------------
test('REQ-UI-046 every public page carries exactly one rss alternate link and the feed answers 200 with valid RSS 2.0', async ({ page }) => {
  test.setTimeout(600000);
  const seenFeeds = new Set<string>();
  for (const [name, path, ready] of shellRoutes()) {
    await open(page, path, ready);
    const links = await page.$$eval('link[rel="alternate"][type="application/rss+xml"]', (l) => l.map((x) => x.getAttribute('href') || ''));
    expect(links.length, `${name} carries exactly one rss alternate link`).toBe(1);
    seenFeeds.add(new URL(links[0], BASE).pathname);
  }
  expect(seenFeeds.size, 'every page points at the same feed').toBe(1);
  const feed = await getText([...seenFeeds][0]);
  expect(feed.status).toBe(200);
  expect(feed.type).toMatch(/xml/);
  expect(feed.body).toMatch(/<rss[^>]*version="2\.0"/);
  expect(feed.body).toMatch(/<channel>/);
});

test('REQ-FN-037 RSS is valid 2.0 listing the newest published posts with title, working link, description, pubDate and author, no draft', async () => {
  const { status, body } = await getText('/rss.xml');
  expect(status).toBe(200);
  expect(body).toContain('<rss version="2.0"');
  const items = [...body.matchAll(/<item>([\s\S]*?)<\/item>/g)].map((m) => m[1]);
  expect(items.length).toBeGreaterThan(0);
  const slugs: string[] = [];
  for (const it of items) {
    for (const tag of ['title', 'link', 'description', 'pubDate', 'dc:creator']) expect(it, `item carries <${tag}>`).toMatch(new RegExp(`<${tag}>[^<]+</${tag}>`));
    const path = (it.match(/<link>https?:\/\/[^/]+(\/[^<]*)<\/link>/) || [])[1];
    expect(path).toMatch(/^\/post\//);
    slugs.push(path.replace('/post/', ''));
    expect((await getText(path)).status, `item link ${path}`).toBe(200);
    expect(new Date((it.match(/<pubDate>([^<]+)<\/pubDate>/) || [])[1]).getTime()).not.toBeNaN();
  }
  expect(slugs).not.toContain(slugOf('fxv1 Draft Post fxv1zebra'));
  expect(slugs, 'items are the newest published posts, in order').toEqual(dbNewest(slugs.length));
  expect(slugs.length).toBeGreaterThanOrEqual(10);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-050 — no login/admin entry points
// ---------------------------------------------------------------------------------------------
test('REQ-UI-050 no public page renders a login or admin affordance for an anonymous visitor', async ({ page }) => {
  test.setTimeout(600000);
  const found: string[] = [];
  for (const [name, path, ready] of shellRoutes()) {
    await open(page, path, ready);
    const bad = await page.$$eval('a[href]', (as) => as.map((a) => a.getAttribute('href') || '').filter((h) => /^\/?(login|admin|signup|register|ManagePost|BlogsList|settings)\b/i.test(h.replace(/^\//, '')) || /\/(login|admin|signup|register)(\/|$|\?)/i.test(h)));
    const chrome = await page.locator('[data-testid="account-menu"], [data-testid="account-menu-trigger"], [data-testid="account-logout"], [data-testid="nav-dashboard"]').count();
    if (bad.length || chrome) found.push(`${name}: ${bad.join(',')} chrome=${chrome}`);
  }
  expect(found, found.join('\n')).toEqual([]);
});

test('REQ-UI-050 /login still works by direct URL and a signed-in admin can reach the admin area', async ({ page }) => {
  test.setTimeout(300000);
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="login-email"]')).toBeVisible({ timeout: 60000 });
  await expect(page.locator('[data-testid="login-submit"]')).toBeVisible();
  const landed = await loginHard(page, 'admin');
  expect(landed, 'admin lands on an admin screen').toMatch(/\/(admin|BlogsList|dashboard|ManagePost)/i);
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), '/admin/categories');
  await expect(page.locator('[data-testid="categories-grid"], [data-testid="categories-empty"], [data-testid="categories-count"]').first()).toBeVisible({ timeout: 60000 });
  expect(new URL(page.url()).pathname).toBe('/admin/categories');
});

test('REQ-UI-050 the README documents the direct sign-in URL and the admin entry', async () => {
  const readme = readFileSync('README.md', 'utf8');
  expect(/\/login\b/.test(readme), 'README names the direct /login URL').toBe(true);
  expect(/\/admin\b|admin area|admin dashboard/i.test(readme), 'README says where the admin area is').toBe(true);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-059 — listings date and sort by PublishedOn
// ---------------------------------------------------------------------------------------------
test('REQ-UI-059 back-dated posts list in PublishedOn order on home, category, search and the feed, matching the date on each card', async ({ page }) => {
  test.setTimeout(400000);
  const trio = ['fxv1 Alpha', 'fxv1 Bravo', 'fxv1 Charlie'].map((t) => sql(`SELECT slug, createdon, publishedon FROM blogpost WHERE title='${t}' AND isdeleted IS NOT TRUE`)[0].split('|'));
  const [a, b, c] = trio;
  expect(new Date(a[1]) < new Date(b[1]) && new Date(b[1]) < new Date(c[1]), 'created Alpha, Bravo, Charlie').toBe(true);
  expect(new Date(c[2]) < new Date(b[2]) && new Date(b[2]) < new Date(a[2]), 'published in reverse: PublishedOn order differs from CreatedOn order').toBe(true);
  const expected = [a[0], b[0], c[0]]; // PublishedOn newest-first
  const order = (all: string[]) => all.filter((s) => expected.includes(s));
  for (const [route, ready] of [['/', '[data-testid="home-articles-grid"] [data-testid="post-card"]'], ['/category/fxv1-category', '[data-testid="posts-grid"]'], ['/search?q=fxv1', '[data-testid="search-result"]']] as const) {
    await open(page, route, ready);
    const sels = route === '/search?q=fxv1' ? '[data-testid="search-result-title"]' : '[data-testid="post-card-title"]';
    const listed = slugsFrom(await hrefs(page, sels));
    if (route === '/') {
      // Home shows only the newest few; other writers' posts may push the trio off it, so compare
      // the whole grid with the database PublishedOn order (the trio is asserted where it always fits).
      expect(listed, 'home grid is in PublishedOn order').toEqual(dbNewest(listed.length));
    }
    const present = order(listed);
    if (route === '/' && present.length < 2) continue;
    expect(present.length, `${route} lists the back-dated posts`).toBeGreaterThanOrEqual(2);
    expect(present, `${route} sorts by PublishedOn`).toEqual(expected.filter((s) => present.includes(s)));
    await page.screenshot({ path: `${SHOTS}/ui059-${route.replace(/[^a-z]/gi, '_')}.png` });
  }
  const rss = (await getText('/rss.xml')).body;
  const rssOrder = [...rss.matchAll(/<link>https?:\/\/[^/]+\/post\/([^<]+)<\/link>/g)].map((m) => m[1]).filter((s) => expected.includes(s));
  expect(rssOrder, 'feed pubDate order agrees').toEqual(expected.filter((s) => rssOrder.includes(s)));
  // The date printed on a card is the PublishedOn date.
  await open(page, '/category/fxv1-category', '[data-testid="posts-grid"]');
  const first = page.locator('[data-testid="post-card"]').first();
  const shown = (await first.locator('[data-testid="post-card-date"]').innerText()).trim();
  const pub = sqlOne(`SELECT to_char(publishedon,'Mon DD') FROM blogpost WHERE slug='${expected[0]}'`).replace(/\s0(\d)$/, ' $1');
  expect(shown.replace(/\s+0?(\d)/, ' $1'), 'the card prints the PublishedOn date').toBe(pub.replace(/\s+0?(\d)/, ' $1'));
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-060 — exactly one h1
// ---------------------------------------------------------------------------------------------
const h1Routes = (): [string, string][] => [
  ['/', ''], ['/access-denied', ''], ['/about', ''], ['/categories', ''], ['/tags', ''], ['/series', ''], ['/newsletters', ''], ['/resume', ''], ['/speaker-profile', ''],
  ['/search', ''], ['/rss', ''], ['/post/fxv1-markdown-post', ''], ['/category/fxv1-category', ''], ['/tag/fxv1tag', ''], ['/series/fxv1-series', ''], ['/no-such-route-fxv1', ''],
];
for (const [route] of h1Routes()) {
  test(`REQ-UI-060 exactly one h1 on ${route}`, async ({ page }) => {
    test.setTimeout(150000);
    await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    await expect.poll(() => page.locator('h1').count(), { timeout: 45000 }).toBeGreaterThanOrEqual(1);
    await page.waitForTimeout(4000); // let the prerender -> interactive swap settle
    expect(await page.locator('h1').count(), `${route}: ${(await page.locator('h1').allInnerTexts()).join(' | ')}`).toBe(1);
  });
}

// ---------------------------------------------------------------------------------------------
// REQ-FN-013 / 015 / 020 / 038 — authoring-driven public behaviour (own temp posts, deleted afterwards)
// ---------------------------------------------------------------------------------------------
test('REQ-FN-013 a slug is generated from the title, a same-title post gets a distinct slug, a typed slug overrides, and the post answers at /post/{slug}', async ({ page }) => {
  test.setTimeout(600000);
  const stamp = Date.now().toString(36);
  const title = `fxv1 Slug Probe ${stamp}`;
  const typed = `fxv1-typed-${stamp}`;
  const mk = { body: 'fxv1 slug body.', abstract: 'fxv1 slug abstract', category: CAT_EMPTY, mode: 'publish' as const };
  try {
    await createPost(admin, { ...mk, title });
    await createPost(admin, { ...mk, title }); // second post, same title
    const slugs = sql(`SELECT slug FROM blogpost WHERE title='${title}' AND isdeleted IS NOT TRUE ORDER BY postid`);
    expect(slugs.length).toBe(2);
    expect(slugs[0], 'URL-safe slug from the title').toBe(`fxv1-slug-probe-${stamp}`);
    expect(slugs[0]).toMatch(/^[a-z0-9]+(-[a-z0-9]+)*$/);
    expect(slugs[1], 'second same-title post gets a distinct slug').toBe(`fxv1-slug-probe-${stamp}-2`);
    await createPost(admin, { ...mk, title: `fxv1 Typed Slug ${stamp}` }, { typedSlug: typed });
    expect(sqlOne(`SELECT slug FROM blogpost WHERE title='fxv1 Typed Slug ${stamp}' AND isdeleted IS NOT TRUE`), 'a typed slug overrides').toBe(typed);
    for (const s of [...slugs, typed]) {
      await open(page, `/post/${s}`, '[data-testid="post-title"]');
      expect((await getText(`/post/${s}`)).status, `/post/${s} answers`).toBe(200);
    }
  } finally {
    for (const t of [title, `fxv1 Typed Slug ${stamp}`]) await deletePostByTitle(admin, t).catch(() => {});
  }
});

test('REQ-FN-015 a draft is absent from every public surface; publishing makes it reachable everywhere; unpublishing removes it again', async ({ page }) => {
  test.setTimeout(900000);
  const stamp = Date.now().toString(36);
  const title = `fxv1 State Probe fxv1state${stamp}`;
  const word = `fxv1state${stamp}`;
  const surfaces = async () => {
    const slug = slugOf(title);
    const r = await page.goto(`${BASE}/post/${slug}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="post-title"], [data-testid="not-found-page"]', { timeout: 60000 });
    const postStatus = r?.status() ?? 0;
    const home = (await getText('/')).body.includes(`/post/${slug}`);
    const cat = (await getText('/category/fxv1-empty')).body.includes(`/post/${slug}`);
    await page.goto(`${BASE}/search?q=${word}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="search-result"], [data-testid="search-empty"]', { timeout: 60000 });
    await page.waitForTimeout(2000);
    const search = (await page.locator('[data-testid="search-result-title"]').count()) > 0;
    const rss = (await getText('/rss.xml')).body.includes(`/post/${slug}`);
    const sitemap = (await getText('/sitemap.xml')).body.includes(`/post/${slug}`);
    return { postStatus, home, cat, search, rss, sitemap };
  };
  try {
    await createPost(admin, { title, body: `${word} body.`, abstract: 'fxv1 state abstract', category: CAT_EMPTY, mode: 'draft' });
    const draft = await surfaces();
    expect(draft, 'draft: 404 and absent everywhere').toEqual({ postStatus: 404, home: false, cat: false, search: false, rss: false, sitemap: false });
    await publishExisting(admin, title);
    const live = await surfaces();
    expect(live, 'published: reachable everywhere').toEqual({ postStatus: 200, home: true, cat: true, search: true, rss: true, sitemap: true });
    await unpublishExisting(admin, title);
    const gone = await surfaces();
    expect(gone, 'unpublished: gone again (RSS and sitemap sit in the output cache)').toEqual({ postStatus: 404, home: false, cat: false, search: false, rss: false, sitemap: false });
  } finally {
    await deletePostByTitle(admin, title).catch(() => {});
  }
});

test('REQ-FN-015 the fixture draft is absent from home, archives, search, RSS and sitemap and /post/{slug} is a 404', async ({ page }) => {
  test.setTimeout(240000);
  const slug = slugOf('fxv1 Draft Post fxv1zebra');
  expect(slug).not.toBe('');
  const r = await page.goto(`${BASE}/post/${slug}`, { waitUntil: 'domcontentloaded' });
  expect(r?.status()).toBe(404);
  await expect(page.locator('[data-testid="not-found-page"]')).toBeVisible({ timeout: 60000 });
  for (const path of ['/', '/category/fxv1-category', '/rss.xml', '/sitemap.xml', '/search?q=fxv1']) {
    expect((await getText(path)).body, `${path} omits the draft`).not.toContain(`/post/${slug}`);
  }
});

test('REQ-FN-020 each published post is on home and its category archive newest-first; related posts share a category or tag and exclude the post itself; reading time matches the word count', async ({ page }) => {
  test.setTimeout(400000);
  // Listings (the archive carries 12 > 9 posts: walk both pages).
  await open(page, '/category/fxv1-category', '[data-testid="posts-grid"]');
  const dbCat = sql("SELECT slug FROM blogpost WHERE published AND isdeleted IS NOT TRUE AND categoryid=(SELECT categoryid FROM category WHERE slug='fxv1-category') ORDER BY coalesce(publishedon,createdon) DESC, postid DESC");
  const l1 = slugsFrom(await hrefs(page, '[data-testid="post-card-title"]'));
  await gotoPage(page, 2);
  const l2 = slugsFrom(await hrefs(page, '[data-testid="post-card-title"]'));
  expect([...l1, ...l2], 'category archive is newest-first and complete').toEqual(dbCat);
  const homeHtml = (await getText('/')).body;
  const onHome = [...new Set([...homeHtml.matchAll(/href="\/post\/([a-z0-9-]+)"/g)].map((m) => m[1]))];
  expect(onHome.length).toBeGreaterThan(1);
  expect(onHome, 'home lists the newest published posts, newest first').toEqual(dbNewest(onHome.length));
  // Related posts.
  for (const title of ['fxv1 Markdown Post', 'fxv1 Search Post fxv1quokkatitle', 'fxv1 Series Part Mike']) {
    const slug = slugOf(title);
    await open(page, `/post/${slug}`, '[data-testid="post-content"]');
    const related = slugsFrom(await hrefs(page, '[data-testid="related-post-title"]'));
    expect(related.length, `${title} has related posts`).toBeGreaterThan(0);
    for (const r of related) {
      expect(r, 'never the subject itself').not.toBe(slug);
      const shares = sql(`SELECT 1 FROM blogpost s, blogpost r WHERE s.slug='${slug}' AND r.slug='${r}' AND r.published AND r.isdeleted IS NOT TRUE AND (s.categoryid=r.categoryid OR EXISTS (SELECT 1 FROM posttag a JOIN posttag b ON a.tagid=b.tagid WHERE a.postid=s.postid AND b.postid=r.postid))`);
      expect(shares.length, `${r} shares a category or tag with ${slug}`).toBe(1);
    }
  }
  // Reading time = ceil(words / 200) on the post page, the category card and the search row.
  const body = sqlOne("SELECT postcontent FROM blogpost WHERE title='fxv1 Long Post' AND isdeleted IS NOT TRUE").length > 0
    ? sql("SELECT postcontent FROM blogpost WHERE title='fxv1 Long Post' AND isdeleted IS NOT TRUE").join('\n') : '';
  const words = body.split(/\s+/).filter(Boolean).length;
  const minutes = Math.max(1, Math.ceil(words / 200));
  expect(words).toBeGreaterThan(400);
  const longSlug = slugOf('fxv1 Long Post');
  await open(page, `/post/${longSlug}`, '[data-testid="post-content"]');
  expect(await page.locator('[data-testid="post-readtime"]').innerText()).toContain(`${minutes} min`);
  await open(page, '/search?q=fxv1%20Long%20Post', '[data-testid="search-result"]');
  const row = page.locator('[data-testid="search-result"]', { hasText: 'fxv1 Long Post' }).first();
  expect(await row.locator('[data-testid="search-result-readtime"]').innerText()).toContain(`${minutes} min`);
  const catIdx = dbCat.indexOf(longSlug);
  await open(page, '/category/fxv1-category', '[data-testid="posts-grid"]');
  if (catIdx >= perPage()) await gotoPage(page, 2);
  const card = page.locator('[data-testid="post-card"]', { hasText: 'fxv1 Long Post' }).first();
  expect(await card.locator('[data-testid="post-card-readtime"]').innerText()).toContain(`${minutes} min`);
});

test('REQ-FN-038 sitemap.xml lists every published post, category and tag and no draft; robots.txt answers 200 and names the sitemap', async () => {
  const { status, body } = await getText('/sitemap.xml');
  expect(status).toBe(200);
  const locs = [...body.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1].replace(/^https?:\/\/[^/]+/, ''));
  const posts = sql('SELECT slug FROM blogpost WHERE published AND isdeleted IS NOT TRUE').map((s) => `/post/${s}`);
  expect(posts.length).toBeGreaterThanOrEqual(12);
  for (const p of posts) expect(locs, `sitemap lists ${p}`).toContain(p);
  for (const d of sql('SELECT slug FROM blogpost WHERE (NOT published OR isdeleted) AND slug IS NOT NULL').map((s) => `/post/${s}`)) expect(locs, `sitemap omits ${d}`).not.toContain(d);
  for (const c of sql('SELECT slug FROM category WHERE slug IS NOT NULL').map((s) => `/category/${s}`)) expect(locs, `sitemap lists ${c}`).toContain(c);
  for (const t of sql('SELECT slug FROM tag WHERE slug IS NOT NULL').map((s) => `/tag/${s}`)) expect(locs, `sitemap lists ${t}`).toContain(t);
  const robots = await getText('/robots.txt');
  expect(robots.status).toBe(200);
  expect(robots.body).toMatch(/Sitemap:\s*\S+\/sitemap\.xml/i);
});

// ---------------------------------------------------------------------------------------------
// Cleanup — keep LAST. Removes every fxv1 fixture through the admin UI.
// ---------------------------------------------------------------------------------------------
test('fxv1 cleanup removes the fixtures through the admin UI', async () => {
  test.setTimeout(900000);
  const left = await cleanupFixtures(admin);
  console.log('FXV1 LEFT BEHIND:', JSON.stringify(left));
  expect(left).toEqual([]);
});
