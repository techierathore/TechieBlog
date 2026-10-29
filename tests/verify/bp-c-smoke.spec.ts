import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

/**
 * build-phase 2026-10-27, cluster C smoke (REQ-NFR-026 data render, REQ-NFR-043 head icons,
 * REQ-NFR-001 pages under load budget). Needs fixtures `fxc-sample-post-1..3` (published) and the
 * public newsletter `fxc-newsletter`, inserted before the host was booted.
 */
const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5403';
const SHOTS = 'tests/.artifacts/build-phase-20261027/C';
const ADMIN = { email: 'Ravi@techieblog.com', password: process.env.ADMIN_PASSWORD ?? 'VerifyAdmin#2026' };
const WIDTHS = [1280, 390];

const PUBLIC = [
  { name: 'home', path: '/', expect: 'FXC Sample Post' },
  { name: 'post', path: '/post/fxc-sample-post-1', expect: 'FXC Sample Post 1' },
  { name: 'category', path: '/category/programming', expect: 'FXC Sample Post' },
  { name: 'newsletters', path: '/newsletters', expect: 'FXC Newsletter' },
  { name: 'newsletter', path: '/newsletter/fxc-newsletter', expect: 'FXC Newsletter' },
];

for (const screen of PUBLIC) {
  for (const width of WIDTHS) {
    test(`public ${screen.name} @${width} renders data, links icons`, async ({ page, request }) => {
      await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
      const response = await page.goto(BASE + screen.path, { waitUntil: 'networkidle' });
      expect(response?.status()).toBe(200);
      await page.waitForTimeout(1500);
      await expect(page.locator('body')).toContainText(screen.expect, { timeout: 20000 });

      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      expect(overflow, 'no horizontal scroll').toBeLessThanOrEqual(1);
      await page.screenshot({ path: `${SHOTS}/${screen.name}-${width}.png`, fullPage: true });

      if (width === 1280) {
        // REQ-NFR-043: head links the three icons and each answers 200.
        const hrefs = await page.locator('head link[rel~="icon"], head link[rel="apple-touch-icon"]').evaluateAll(
          els => els.map(e => (e as HTMLLinkElement).href));
        for (const name of ['favicon.ico', 'icon-192.png', 'icon-512.png']) {
          const href = hrefs.find(h => h.endsWith('/' + name));
          expect(href, `${screen.path} head must link ${name}`).toBeTruthy();
          const r = await request.get(href!);
          expect(r.status(), `${name} must answer 200`).toBe(200);
        }
      }
    });
  }
}

async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 });
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', ADMIN.email);
  await page.fill('[data-testid="login-password"]', ADMIN.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(3000);
  expect(new URL(page.url()).pathname).not.toContain('/change-password');
}

async function routerGoto(page: Page, href: string) {
  await page.evaluate(p => (window as any).Blazor.navigateTo(p), href);
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

for (const width of WIDTHS) {
  test(`admin pages read through converted repositories @${width}`, async ({ page }) => {
    test.setTimeout(180000);
    await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
    await login(page);

    await routerGoto(page, '/admin/newsletter');
    await expect(page.locator('[data-testid="newsletter-history-list"]')).toBeVisible({ timeout: 30000 });
    await expect(page.locator('[data-testid="newsletter-history-list"]')).toContainText('FXC Newsletter');
    await page.screenshot({ path: `${SHOTS}/admin-newsletter-${width}.png`, fullPage: true });

    await routerGoto(page, '/settings');
    await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 30000 });
    await expect(page.locator('[data-testid="site-title"]')).toBeVisible();
    expect((await page.locator('[data-testid="site-title"]').inputValue()).trim().length).toBeGreaterThan(0);
    expect((await page.locator('[data-testid="admin-email"]').inputValue()).trim().length).toBeGreaterThan(0);
    await page.screenshot({ path: `${SHOTS}/settings-${width}.png`, fullPage: true });

    await routerGoto(page, '/BlogsList');
    await expect(page.locator('body')).toContainText('FXC Sample Post', { timeout: 30000 });
    await page.screenshot({ path: `${SHOTS}/blogslist-${width}.png`, fullPage: true });
  });
}
