/*
  cluster-l-visual.spec.ts — VISUAL-TRUTH captures for the three markup changes
  Cluster L made (2026-08-09): the rebuilt Recent Activity list on /admin, the
  labelled feed-URL row on /rss, and the 404 screen an unmatched URL now shows.

  Each capture also asserts the block actually rendered rows/controls, so a blank
  region cannot pass as "looks fine".
*/
import { test, expect, Page } from '@playwright/test';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5392';
const OUT = path.join(process.cwd(), 'test-results-cluster-l');
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]');
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(2500);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2500);
}

test('admin recent activity list renders as a real list', async ({ page }) => {
  test.setTimeout(240000);
  for (const [w, h] of [[1280, 900], [390, 844]] as Array<[number, number]>) {
    await page.setViewportSize({ width: w, height: h });
    if (w === 1280) await login(page);
    await page.evaluate(() => (window as any).Blazor.navigateTo('/admin'));
    await page.waitForTimeout(4000);
    const state = await page.evaluate(() => {
      const list = document.querySelector('[data-testid="recent-activity-list"]') as HTMLElement | null;
      const items = Array.from(document.querySelectorAll('[data-testid="recent-activity-item"]')) as HTMLElement[];
      const card = document.querySelector('[data-testid="recent-activity"]') as HTMLElement | null;
      const cardRect = card?.getBoundingClientRect();
      return {
        listTag: list?.tagName,
        listRole: list ? getComputedStyle(list).display : null,
        itemCount: items.length,
        itemTags: items.map(i => i.tagName),
        firstItemText: items[0]?.innerText.replace(/\s+/g, ' ').trim().slice(0, 80) ?? '',
        rowHeights: items.map(i => Math.round(i.getBoundingClientRect().height)),
        rowsInsideCard: items.every(i => {
          const r = i.getBoundingClientRect();
          return !!cardRect && r.left >= cardRect.left - 1 && r.right <= cardRect.right + 1;
        }),
        markerBullets: getComputedStyle(items[0] ?? document.body).listStyleType,
      };
    });
    console.log(`ACTIVITY @${w}:`, JSON.stringify(state));
    await page.locator('[data-testid="recent-activity"]').screenshot({ path: path.join(OUT, `visual-recent-activity-${w}.png`) }).catch(() => {});
    await page.screenshot({ path: path.join(OUT, `visual-admin-${w}.png`) });
    expect(state.listTag, 'activity feed must be a real <ul>').toBe('UL');
    expect(state.itemCount, 'activity feed must have rows').toBeGreaterThan(0);
    expect(state.itemTags.every(t => t === 'LI'), 'rows must be <li>').toBe(true);
    expect(state.firstItemText.length, 'first row must carry text').toBeGreaterThan(0);
    expect(state.rowsInsideCard, 'rows must stay inside the card').toBe(true);
    expect(state.markerBullets, 'no bullet markers').toBe('none');
  }
});

test('rss feed url row', async ({ page }) => {
  test.setTimeout(180000);
  for (const [w, h] of [[1280, 900], [390, 844]] as Array<[number, number]>) {
    await page.setViewportSize({ width: w, height: h });
    await page.goto(`${BASE}/rss`, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(4000);
    const state = await page.evaluate(() => {
      const input = document.querySelector('[data-testid="rss-url"]') as HTMLInputElement | null;
      const label = document.querySelector('label[for="rss-url-input"]') as HTMLElement | null;
      const labelRect = label?.getBoundingClientRect();
      const inputRect = input?.getBoundingClientRect();
      return {
        value: input?.value ?? '',
        inputId: input?.id,
        labelText: label?.textContent ?? '',
        labelIsVisuallyHidden: !!labelRect && labelRect.width <= 1 && labelRect.height <= 1,
        inputWidth: Math.round(inputRect?.width ?? 0),
        inputRight: Math.round(inputRect?.right ?? 0),
        viewport: document.documentElement.clientWidth,
        hScroll: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      };
    });
    console.log(`RSS @${w}:`, JSON.stringify(state));
    await page.screenshot({ path: path.join(OUT, `visual-rss-${w}.png`) });
    expect(state.inputId).toBe('rss-url-input');
    expect(state.labelText).toBe('RSS feed URL');
    expect(state.labelIsVisuallyHidden, 'label must not take layout space').toBe(true);
    expect(state.value).toContain('feed.xml');
    expect(state.hScroll).toBe(0);
    expect(state.inputRight).toBeLessThanOrEqual(state.viewport);
  }
});

test('unmatched url 404 screen', async ({ page }) => {
  test.setTimeout(180000);
  for (const [w, h] of [[1280, 900], [390, 844]] as Array<[number, number]>) {
    await page.setViewportSize({ width: w, height: h });
    const res = await page.goto(`${BASE}/cluster-l-visual-no-such-${Date.now()}`, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(6000);
    const state = await page.evaluate(() => ({
      page: !!document.querySelector('[data-testid="not-found-page"]'),
      fragment: !!document.querySelector('[data-testid="not-found"]'),
      heading: document.querySelector('h1')?.textContent?.trim(),
      title: document.title,
      header: document.querySelectorAll('header').length,
      footer: document.querySelectorAll('footer').length,
      buttons: Array.from(document.querySelectorAll('[data-testid^="not-found-"]')).map(b => b.getAttribute('data-testid')),
      hScroll: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    }));
    console.log(`404 @${w}: status=${res?.status()}`, JSON.stringify(state));
    await page.screenshot({ path: path.join(OUT, `visual-404-${w}.png`) });
    expect(res?.status()).toBe(404);
    expect(state.page, 'server-rendered 404 page must survive hydration').toBe(true);
    expect(state.fragment, 'router NotFound must not paint a second, different screen').toBe(false);
    expect(state.hScroll).toBe(0);
  }
});
