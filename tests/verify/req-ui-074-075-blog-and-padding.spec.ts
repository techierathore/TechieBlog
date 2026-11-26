/**
 * Owner UAT 2026-11-26: box top padding (REQ-UI-074) and the Blog page (REQ-UI-075).
 *
 * RUN ONLY AGAINST A THROWAWAY DATABASE: the site under TB_BASE must have been booted by
 * tests/TechieBlog.Tests/Integration/ThrowawaySiteRun.cs, which makes a `tbtest_…` database, adds
 * the 14 published posts "Sample post 01" (newest) … "Sample post 14", and drops it afterwards.
 * Nothing here writes to any database. The spec refuses to start unless TB_SMOKE_DB is a tbtest_ name.
 *
 * Environment: TB_BASE (site), TB_SMOKE_DB (the throwaway database's name), TB_SHOTS (screenshot folder).
 */
import { test, expect, Page } from '@playwright/test';

const BASE = process.env.TB_BASE ?? 'http://localhost:9303';
const DB = process.env.TB_SMOKE_DB ?? '';
const SHOTS = process.env.TB_SHOTS ?? 'tests/.artifacts/uat-20261126/shots';

if (!/^tbtest_/.test(DB)) throw new Error(`REFUSING: TB_SMOKE_DB "${DB}" is not a throwaway tbtest_ database`);

/** Opens a route and waits for the Blazor circuit, so interactive parts are live. */
async function gotoLive(page: Page, route: string) {
  const ws = page.waitForEvent('websocket', { timeout: 60000 }).catch(() => null);
  await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
  await ws;
  await page.waitForTimeout(1500);
}

/** Puts the site in the wanted theme through its own toggle (a click before the circuit is live is lost, so retry). */
async function setTheme(page: Page, dark: boolean) {
  const isDark = async () => page.evaluate(() => document.documentElement.classList.contains('dark'));
  for (let attempt = 0; attempt < 5 && (await isDark()) !== dark; attempt++) {
    await page.locator('[data-testid="theme-toggle"]:visible').first().click();
    await page.waitForTimeout(2000);
  }
  expect(await isDark(), 'theme switched').toBe(dark);
}

/**
 * Every bordered box's direct children: padding-top and padding-bottom in px. A child with side
 * padding but no top padding while it has bottom padding is the reported fault.
 */
async function lopsidedBoxes(page: Page) {
  return page.evaluate(() => {
    const out: string[] = [];
    document.querySelectorAll('.rounded-lg.border, .rounded-xl.border').forEach(box => {
      Array.from(box.children).forEach(child => {
        const s = getComputedStyle(child as Element);
        const top = parseFloat(s.paddingTop), bottom = parseFloat(s.paddingBottom), left = parseFloat(s.paddingLeft);
        const r = (child as HTMLElement).getBoundingClientRect();
        // Content under a box heading is meant to have no top padding: the heading's own bottom
        // padding is the space. Only a first child, or one under an unpadded block (an image), counts.
        const prev = child.previousElementSibling;
        const spacedAbove = prev != null && parseFloat(getComputedStyle(prev).paddingBottom) > 0;
        if (r.height > 0 && left > 0 && bottom > 0 && top === 0 && !spacedAbove) {
          const id = (box as HTMLElement).dataset.testid ?? (child as HTMLElement).className.slice(0, 60);
          out.push(`${id}: top ${top}px, bottom ${bottom}px`);
        }
      });
    });
    return out;
  });
}

/** The padding of a box's content area, by the box's data-testid. */
async function contentPadding(page: Page, testId: string) {
  return page.getByTestId(testId).first().evaluate(box => {
    const s = getComputedStyle(box.firstElementChild as Element);
    return { top: parseFloat(s.paddingTop), bottom: parseFloat(s.paddingBottom) };
  });
}

for (const width of [1280, 390]) {
  for (const dark of [true, false]) {
    const label = `${width}px ${dark ? 'dark' : 'light'}`;

    test(`REQ-UI-074 post page boxes keep their top padding (${label})`, async ({ page }) => {
      await page.setViewportSize({ width, height: 900 });
      await gotoLive(page, '/post/sample-post-02');
      await setTheme(page, dark);
      for (const id of ['post-rating-panel', 'post-author-card', 'related-post-card']) {
        const p = await contentPadding(page, id);
        expect(p.top, `${id} top padding`).toBeGreaterThan(0);
        expect(p.top, `${id} top equals bottom`).toBe(p.bottom);
      }
      await page.getByTestId('post-rating-panel').scrollIntoViewIfNeeded();
      await page.screenshot({ path: `${SHOTS}/post-boxes-${width}-${dark ? 'dark' : 'light'}.png`, fullPage: false });
      expect(await lopsidedBoxes(page)).toEqual([]);
    });
  }
}

const PUBLIC_ROUTES = ['/', '/blog', '/blog/2', '/categories', '/tags', '/search?q=Sample', '/series',
  '/newsletters', '/about', '/resume', '/speaker-profile', '/rss', '/post/sample-post-05'];

for (const width of [1280, 390]) {
  test(`REQ-UI-074 no box on a public page loses its top padding (${width}px)`, async ({ page }) => {
    await page.setViewportSize({ width, height: 900 });
    const found: string[] = [];
    for (const route of PUBLIC_ROUTES) {
      await gotoLive(page, route);
      for (const line of await lopsidedBoxes(page)) found.push(`${route} ${line}`);
    }
    expect(found).toEqual([]);
  });
}

test('REQ-UI-075 Blog is second in the desktop menu, after Home', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await gotoLive(page, '/');
  const labels = (await page.getByTestId('primary-nav').locator('a').allInnerTexts()).map(t => t.trim()).filter(Boolean);
  expect(labels.slice(0, 2)).toEqual(['Home', 'Blog']);
  await page.getByTestId('nav-blog').first().click();
  await expect(page).toHaveURL(/\/blog$/);
  await expect(page.getByTestId('blog-grid')).toBeVisible();
});

test('REQ-UI-075 Blog is second in the phone menu, after Home', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await gotoLive(page, '/');
  await page.getByTestId('mobile-nav-trigger').click();
  const drawer = page.getByTestId('mobile-nav-drawer');
  await expect(drawer).toBeVisible();
  const labels = (await drawer.locator('nav a').allInnerTexts()).map(t => t.trim()).filter(Boolean);
  expect(labels.slice(0, 2)).toEqual(['Home', 'Blog']);
  await page.screenshot({ path: `${SHOTS}/phone-menu-390.png` });
  await page.getByTestId('nav-blog-mobile').click();
  await expect(page).toHaveURL(/\/blog$/);
});

test('REQ-UI-075 browse all articles on the home page opens the Blog page', async ({ page }) => {
  await gotoLive(page, '/');
  const link = page.getByTestId('home-articles-browse-link');
  await expect(link).toHaveAttribute('href', '/blog');
  await link.click();
  await expect(page).toHaveURL(/\/blog$/);
});

test('REQ-UI-075 the Blog page lists every published post newest first, with paging', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await gotoLive(page, '/blog');
  await expect(page.getByTestId('blog-post-count')).toHaveText('14 posts');
  const firstPage = page.getByTestId('blog-grid').getByTestId('post-card');
  await expect(firstPage).toHaveCount(10);
  const titles = await page.getByTestId('blog-grid').locator('h3').allInnerTexts();
  expect(titles.map(t => t.trim())).toEqual(Array.from({ length: 10 }, (_, i) => `Sample post ${String(i + 1).padStart(2, '0')}`));
  await page.screenshot({ path: `${SHOTS}/blog-1280-dark.png`, fullPage: true });

  await page.getByTestId('pagination-page-2').click();
  await expect(page).toHaveURL(/\/blog\/2$/);
  await expect(page.getByTestId('blog-grid').getByTestId('post-card')).toHaveCount(4);
  const second = await page.getByTestId('blog-grid').locator('h3').allInnerTexts();
  expect(second.map(t => t.trim())).toEqual(['Sample post 11', 'Sample post 12', 'Sample post 13', 'Sample post 14']);

  await page.getByTestId('pagination-previous').click();
  await expect(page).toHaveURL(/\/blog$/);
});

test('REQ-UI-075 a Blog page address can be opened directly, and past the end says so', async ({ page }) => {
  await gotoLive(page, '/blog/2');
  await expect(page.getByTestId('blog-grid').getByTestId('post-card')).toHaveCount(4);
  await gotoLive(page, '/blog/9');
  await expect(page.getByTestId('blog-empty')).toBeVisible();
  await expect(page.getByTestId('blog-empty-first-page')).toHaveAttribute('href', '/blog');
});

for (const dark of [true, false]) {
  test(`REQ-UI-075 the Blog page at phone width (${dark ? 'dark' : 'light'})`, async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await gotoLive(page, '/blog');
    await setTheme(page, dark);
    await expect(page.getByTestId('blog-grid').getByTestId('post-card')).toHaveCount(10);
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, 'no sideways scroll').toBeLessThanOrEqual(0);
    await page.screenshot({ path: `${SHOTS}/blog-390-${dark ? 'dark' : 'light'}.png`, fullPage: false });
    if (!dark) await page.setViewportSize({ width: 1280, height: 900 });
    if (!dark) await page.screenshot({ path: `${SHOTS}/blog-1280-light.png`, fullPage: true });
  });
}

test('REQ-UI-075 sitemap.xml lists /blog', async ({ request }) => {
  const res = await request.get(`${BASE}/sitemap.xml`);
  expect(res.status()).toBe(200);
  expect(await res.text()).toMatch(/<loc>[^<]*\/blog<\/loc>/);
});
