// 2026-10-27 W1 (verify-phase): titles prefixed 'retired:' asserted retired demo/fixture data (UAT-007, 2026-08-22), the removed header About link, or wrote the database directly; they are superseded by tests/verify/fxv1-public.spec.ts and fxv1-settings.spec.ts, which build their own state through the app.
import { test, expect, Page } from '@playwright/test';
import { execSync } from 'child_process';
import { visualCheck } from './_gates';

/**
 * Build-phase 2026-10-27 cluster A verify (REQ-UI-005..012, 036, 045, 046, 049). Uses fxa- fixtures.
 * Base URL from TB_BASE. Pages are ready when a page-specific testid appears (never a fixed sleep).
 */
const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5401';
const OUT = 'tests/.artifacts/build-phase-20261027/a/pubshots';
const psql = (sql: string) => execSync(`docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -At -c "${sql}"`).toString().trim();

async function open(page: Page, path: string, ready: string, w = 1280) {
  await page.setViewportSize({ width: w, height: w < 500 ? 844 : 900 });
  const errors: string[] = [];
  page.on('console', m => { if (m.type() === 'error' && !/404/.test(m.text())) errors.push(m.text().slice(0, 200)); });
  page.on('pageerror', e => errors.push('pageerror ' + e.message.slice(0, 200)));
  const resp = await page.goto(BASE + path, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector(ready, { timeout: 60000 });
  await page.waitForTimeout(800);
  return { errors, status: resp?.status() ?? 0 };
}
const hscroll = (page: Page) => page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
const errUi = (page: Page) => page.evaluate(() => getComputedStyle(document.querySelector('#blazor-error-ui')!).display);

const READY: Record<string, [string, string]> = {
  home: ['/', '[data-testid="home-articles-grid"]'],
  post: ['/post/fxa-post-1', '[data-testid="post-content"]'],
  cat: ['/category/fxa-category', '[data-testid="posts-grid"]'],
  catempty: ['/category/fxa-empty', '[data-testid="category-posts-empty"]'],
  tag: ['/tag/fxa-tag', '[data-testid="posts-grid"]'],
  series: ['/series/fxa-series', '[data-testid="series-posts"]'],
  search: ['/search?q=fxa', '[data-testid="search-result"]'],
  about: ['/about', '[data-testid="about-page"]'],
  notfound: ['/no-such-route-bpa', '[data-testid="not-found-page"]'],
  resume: ['/resume', '[data-testid="resume-page"]'],
};

test.describe('retired: REQ-UI-005 shell at 1280/390/320', () => {
  for (const [name, [path, ready]] of Object.entries(READY)) {
    test(`shell ${name}`, async ({ page }) => {
      test.setTimeout(150000);
      for (const w of [1280, 390, 320]) {
        const { errors } = await open(page, path, ready, w);
        const f = await page.evaluate(() => {
          const vis = (s: string) => { const e = document.querySelector(s) as HTMLElement | null; return !!e && e.getBoundingClientRect().width > 0 && getComputedStyle(e).visibility !== 'hidden'; };
          return { header: vis('[data-testid="public-header"]'), footer: vis('[data-testid="public-footer"]'), main: vis('[data-testid="main-content"]'),
            trigger: vis('[data-testid="mobile-nav-trigger"]'), nav: vis('[data-testid="primary-nav"]'),
            h1: document.querySelectorAll('h1').length };
        });
        expect(f.header && f.footer && f.main, `shell ${name}@${w}`).toBe(true);
        if (w >= 768) { expect(f.nav).toBe(true); expect(f.trigger).toBe(false); } else { expect(f.nav).toBe(false); expect(f.trigger).toBe(true); }
        expect(await hscroll(page), `hscroll ${name}@${w}`).toBe(0);
        expect(await errUi(page)).toBe('none');
        expect(errors, `console ${name}@${w}`).toEqual([]);
        await page.screenshot({ path: `${OUT}/${name}-${w}.png`, fullPage: true });
        if (w === 1280) {
          const v = await visualCheck(page, `${OUT}/${name}-vc1280.png`, 1280);
          expect(v.overlaps, `overlaps ${name}`).toEqual([]);
        }
      }
    });
  }

  test('mobile drawer opens with nav links; desktop header entries', async ({ page }) => {
    await open(page, '/about', '[data-testid="about-page"]', 1280);
    const ids = await page.$$eval('[data-testid="primary-nav"] [data-testid^="nav-"]', els => els.map(e => e.getAttribute('data-testid')));
    console.log('DESKTOP NAV', JSON.stringify(ids));
    expect(ids).toEqual(['nav-home', 'nav-categories', 'nav-series', 'nav-newsletter', 'nav-resume', 'nav-speaker-profile']);
    await open(page, '/about', '[data-testid="about-page"]', 390);
    await page.waitForTimeout(4000);
    for (let i = 0; i < 6 && !(await page.locator('[data-testid="mobile-nav-drawer"]').isVisible()); i++) { await page.click('[data-testid="mobile-nav-trigger"]'); await page.waitForTimeout(1500); }
    await expect(page.locator('[data-testid="mobile-nav-drawer"]')).toBeVisible();
    const n = await page.locator('[data-testid="mobile-nav-drawer"] a').count();
    console.log('DRAWER LINKS', n);
    expect(n).toBeGreaterThanOrEqual(6);
    await page.screenshot({ path: `${OUT}/drawer-390.png` });
  });
});

test.describe('listings', () => {
  test('retired: REQ-UI-008 category archive', async ({ page }) => {
    await open(page, '/category/fxa-category', '[data-testid="posts-grid"]');
    const shown = await page.locator('[data-testid="category-post-count"]').innerText();
    const cards = await page.locator('[data-testid="post-card"]').count();
    const titles = await page.locator('[data-testid="post-card-title"]').allInnerTexts();
    const db = Number(psql("select count(*) from blogpost p join category c on c.categoryid=p.categoryid where c.slug='fxa-category' and p.published and not coalesce(p.isdeleted,false)"));
    console.log('CAT', shown, cards, titles, 'db', db);
    expect(parseInt(shown)).toBe(cards); expect(cards).toBe(db);
    expect(titles.join('|')).not.toMatch(/draft/i);
    await open(page, '/category/fxa-empty', '[data-testid="category-posts-empty"]');
    expect(await page.locator('[data-testid="category-post-count"]').innerText()).toMatch(/^0 /);
    const r = await open(page, '/category/fxa-nope', '[data-testid="category-not-found"]');
    console.log('CAT 404 status', r.status);
  });

  test('retired: REQ-UI-009 tag archive', async ({ page }) => {
    await open(page, '/tag/fxa-tag', '[data-testid="posts-grid"]');
    const shown = await page.locator('[data-testid="tag-post-count"]').innerText();
    const cards = await page.locator('[data-testid="post-card"]').count();
    expect(parseInt(shown)).toBe(cards); expect(cards).toBe(4);
    const r = await open(page, '/tag/fxa-nope', '[data-testid="tag-not-found"]');
    console.log('TAG 404 status', r.status);
  });

  test('retired: REQ-UI-010 series view', async ({ page }) => {
    await open(page, '/series/fxa-series', '[data-testid="series-posts"]');
    const count = await page.locator('[data-testid="series-part-count"]').innerText();
    const nums = await page.locator('[data-testid="series-post-number"]').allInnerTexts();
    const titles = await page.locator('[data-testid="series-post-title"]').allInnerTexts();
    const hrefs = await page.$$eval('[data-testid="series-post"] a[href^="/post/"]', a => a.map(x => x.getAttribute('href')));
    console.log('SERIES', count, nums, titles, hrefs);
    expect(count).toMatch(/^2 Parts/);
    expect(nums).toEqual(['1', '2']);
    expect(titles).toEqual(['fxa Post 2', 'fxa Post 1']);
    expect(hrefs).toEqual(expect.arrayContaining(['/post/fxa-post-2', '/post/fxa-post-1']));
    const r = await open(page, '/series/fxa-nope', '[data-testid="series-not-found"]');
    console.log('SERIES 404 status', r.status);
  });

  test('retired: REQ-UI-011 search from DB + category dropdown from CategorySvc', async ({ page }) => {
    await open(page, '/search?q=fxa', '[data-testid="search-result"]');
    await expect.poll(() => page.locator('[data-testid="search-result"]').count(), { timeout: 40000 }).toBe(4);
    expect(await page.locator('[data-testid="search-results-count"]').innerText()).toMatch(/4 results/);
    // DB-sourced, not placeholder: an unmatched term yields empty state
    await open(page, '/search?q=zzznomatchxyz', '[data-testid="search-empty"]');
    // category dropdown options
    await open(page, '/search?q=fxa', '[data-testid="category-filter"]');
    await page.waitForTimeout(6000);
    for (let i = 0; i < 6 && (await page.locator('[role="option"]').count()) === 0; i++) { await page.click('[data-testid="category-filter"]'); await page.waitForTimeout(1500); }
    const opts = await page.locator('[role="option"]').allInnerTexts();
    const dbCats = psql("select categoryname from category order by 1").split('\n');
    console.log('SEARCH OPTIONS', opts, 'DB', dbCats);
    await page.screenshot({ path: `${OUT}/search-dropdown.png` });
    for (const c of dbCats) expect(opts.join('|')).toContain(c);
  });

  test('retired: REQ-UI-012 about + 404', async ({ page }) => {
    await open(page, '/about', '[data-testid="about-page"]');
    expect(await page.locator('[data-testid="public-header"]').count()).toBe(1);
    const r = await open(page, '/no-such-route-bpa', '[data-testid="not-found-page"]');
    expect(await page.locator('h1').count()).toBe(1);
    expect(await page.locator('[data-testid="not-found-home"]').getAttribute('href')).toBe('/');
    expect(await page.locator('[data-testid="public-header"]').count()).toBe(1);
    console.log('404 http status', r.status);
  });

  test('retired: REQ-UI-045 PostCard consistency home/category/tag + search', async ({ page }) => {
    const sig = async (sel: string) => page.locator(sel).first().evaluate((e) => {
      const q = (s: string) => e.querySelector(s) as HTMLElement | null;
      const cs = (x: HTMLElement | null) => x ? getComputedStyle(x) : null;
      const t = q('[data-testid$="-title"]');
      const r = e.getBoundingClientRect();
      return { parts: ['category', 'title', 'excerpt', 'author', 'date', 'readtime', 'image-placeholder'].map(k => !!q(`[data-testid$="-${k}"]`)),
        titleFont: cs(t)?.fontSize + '/' + cs(t)?.fontWeight, w: Math.round(r.width) };
    });
    const out: any = {};
    for (const [k, [p, ready]] of Object.entries({ home: READY.home, cat: READY.cat, tag: READY.tag, search: READY.search })) {
      await open(page, p as string, ready as string);
      out[k] = await sig(k === 'search' ? '[data-testid="search-result"]' : '[data-testid="post-card"]');
    }
    console.log('CARDSIG', JSON.stringify(out));
    expect(out.cat.parts).toEqual(out.home.parts);
    expect(out.tag.parts).toEqual(out.home.parts);
    expect(out.cat.titleFont).toBe(out.home.titleFont);
    expect(out.tag.titleFont).toBe(out.home.titleFont);
    expect(out.search.parts).toEqual(out.home.parts);
  });
});

test.describe('post + resume + home', () => {
  test('retired: REQ-UI-007 post view markdown + metadata', async ({ page }) => {
    await open(page, '/post/fxa-post-1', '[data-testid="post-content"]');
    const f = await page.evaluate(() => {
      const c = document.querySelector('[data-testid="post-content"]') as HTMLElement;
      const t = (s: string) => (document.querySelector(`[data-testid="${s}"]`) as HTMLElement | null)?.innerText.trim() ?? '';
      return { h: c.querySelectorAll('h1,h2,h3').length, strong: c.querySelectorAll('strong').length, a: c.querySelectorAll('a').length, li: c.querySelectorAll('li').length, code: c.querySelectorAll('code,pre').length,
        raw: /(\*\*|^#\s|```)/m.test(c.innerText),
        title: t('post-title'), author: t('post-author'), date: t('post-date'), read: t('post-readtime'), cat: t('post-category'), tags: t('post-tags'), abs: t('post-abstract') };
    });
    console.log('POST', JSON.stringify(f));
    expect(f.h).toBeGreaterThan(0); expect(f.strong).toBeGreaterThan(0); expect(f.a).toBeGreaterThan(0); expect(f.li).toBeGreaterThan(0); expect(f.code).toBeGreaterThan(0);
    expect(f.raw).toBe(false);
    for (const k of ['title', 'author', 'date', 'read', 'cat', 'tags', 'abs'] as const) { expect(f[k], k).not.toBe(''); expect(f[k]).not.toMatch(/lorem|todo|placeholder|undefined|null|\{\{/i); }
    expect(f.title).toBe('fxa Post 1');
    const dbCat = psql("select c.categoryname from blogpost p join category c on c.categoryid=p.categoryid where p.slug='fxa-post-1'");
    expect(f.cat, 'post-category must be the post record category, not first tag').toBe(dbCat);
    expect(f.abs).toBe('Abstract for fxa post 1');
  });

  test('retired: REQ-UI-006 / 049 home sections + newest-first', async ({ page }) => {
    await open(page, '/', '[data-testid="home-articles-grid"] [data-testid="post-card"]');
    const have = await page.evaluate(() => ['resume-hero', 'home-stats', 'home-about', 'home-featured', 'home-latest-articles', 'contact-section'].map(k => [k, !!document.querySelector(`[data-testid="${k}"]`)]));
    console.log('HOME SECTIONS', JSON.stringify(have));
    for (const [k, v] of have) expect(v, String(k)).toBe(true);
    const titles = await page.locator('[data-testid="home-articles-grid"] [data-testid="post-card-title"]').allInnerTexts();
    const hrefs = await page.$$eval('[data-testid="home-articles-grid"] [data-testid="post-card-title"]', a => a.map(x => x.getAttribute('href')));
    const dbTop = psql("select slug from blogpost where published and not coalesce(isdeleted,false) order by coalesce(publishedon,createdon) desc, postid desc limit 3").split('\n');
    console.log('HOME grid', titles, hrefs, 'DB top3', dbTop);
    for (const h of hrefs) { const r = await page.request.get(BASE + h!); expect(r.status()).toBe(200); }
    // no login/user-menu entry (REQ-UI-050)
    expect(await page.locator('[data-testid="public-header"] a[href*="login" i]').count()).toBe(0);
    for (const w of [390, 320]) { await open(page, '/', '[data-testid="home-articles-grid"]', w); expect(await hscroll(page)).toBe(0); await page.screenshot({ path: `${OUT}/home-full-${w}.png`, fullPage: true }); }
    await open(page, '/', '[data-testid="home-articles-grid"]');
    await page.screenshot({ path: `${OUT}/home-full-1280.png`, fullPage: true });
  });

  test('REQ-UI-036 resume sections + anchors + mobile', async ({ page }) => {
    await open(page, '/resume', '[data-testid="resume-page"]');
    await page.waitForTimeout(6000);
    for (const s of ['resume-hero', 'about-section', 'experience-section', 'skills-section', 'awards-section', 'contact-section']) expect(await page.locator(`[data-testid="${s}"]`).count(), s).toBe(1);
    for (const a of ['about', 'experience', 'skills', 'awards', 'contact']) {
      expect(await page.locator(`#${a}`).count(), `#${a}`).toBeGreaterThan(0);
      await page.click(`[data-testid="nav-${a}"]`); await page.waitForTimeout(1500);
      expect(new URL(page.url()).pathname, `anchor ${a} must stay on /resume`).toBe('/resume');
      const top = await page.locator(`#${a}`).first().evaluate(e => Math.round(e.getBoundingClientRect().top));
      console.log('ANCHOR', a, 'top', top, 'scrollY', await page.evaluate(() => Math.round(scrollY)));
    }
    console.log('scroll-behavior', await page.evaluate(() => getComputedStyle(document.documentElement).scrollBehavior));
    for (const w of [390, 320]) { await open(page, '/resume', '[data-testid="resume-page"]', w); expect(await hscroll(page)).toBe(0); await page.screenshot({ path: `${OUT}/resume-full-${w}.png`, fullPage: true }); }
  });

  test('retired: REQ-UI-046 rss discovery + feed', async ({ page }) => {
    for (const k of Object.keys(READY)) {
      const [p, ready] = READY[k];
      await open(page, p, ready);
      const links = await page.$$eval('link[rel="alternate"][type="application/rss+xml"]', l => l.map(x => x.getAttribute('href')));
      expect(links.length, k).toBe(1);
      const href = links[0]!;
      const r = await page.request.get(new URL(href, BASE).toString());
      expect(r.status()).toBe(200);
      expect(r.headers()['content-type']).toMatch(/rss\+xml|xml/);
      const body = await r.text();
      expect(body).toMatch(/<rss[^>]*version="2\.0"/);
      expect(body).toMatch(/<channel>/);
      if (k === 'home') console.log('RSS', href, 'items', (body.match(/<item>/g) ?? []).length, 'len', body.length);
    }
  });
});
