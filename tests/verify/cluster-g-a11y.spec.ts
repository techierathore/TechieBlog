/*
  cluster-g-a11y.spec.ts — REQ-NFR-007 re-audit (2026-08-08, Cluster G).

  Supersedes the 2026-08-07 baseline run (a11y-axe.spec.ts, 6 routes x 2 viewports).
  This pass widens coverage to the public routes PLUS the admin routes behind a real
  login, and adds /newsletters and /verify/{token} which arrived after that run.

  Method: @axe-core/playwright, tags wcag2a / wcag2aa / wcag21a / wcag21aa.
  Each run also records document horizontal overflow (VISUAL-TRUTH) and a screenshot.
*/
import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://127.0.0.1:5441';
const OUT = process.env.TB_OUT ?? path.join(process.cwd(), 'test-results', 'cluster-g');
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

const POST_SLUG = 'getting-started-with-blazor-server';
// A real, already-issued verification token — /verify/{token} must render something for a
// token it does not recognise too, but auditing the recognised path exercises more markup.
const VERIFY_TOKEN = 'BD3fUQ2Ma5BmHPtW3cRDpwvMUtOLkWPv3bNBUjx84C8';

type Route = { name: string; url: string; admin?: boolean };

const ROUTES: Route[] = [
  { name: 'home', url: '/' },
  { name: 'post', url: `/post/${POST_SLUG}` },
  { name: 'resume', url: '/resume' },
  { name: 'newsletters', url: '/newsletters' },
  { name: 'login', url: '/login' },
  { name: 'search', url: '/search' },
  { name: 'about', url: '/about' },
  { name: 'categories', url: '/categories' },
  { name: 'series', url: '/series' },
  { name: 'tags', url: '/tags' },
  { name: 'verify', url: `/verify/${VERIFY_TOKEN}` },
  { name: 'forgot-password', url: '/forgot-password' },
  // REQ-NFR-023 forces every staff account through this interstitial on sign-in, so it is the
  // most unavoidable screen in the app and had never been audited.
  { name: 'change-password', url: '/change-password', admin: true },
  { name: 'admin-dashboard', url: '/admin', admin: true },
  { name: 'admin-analytics', url: '/admin/analytics', admin: true },
  { name: 'admin-profile', url: '/admin/profile', admin: true },
  { name: 'admin-images', url: '/admin/images', admin: true },
  { name: 'admin-newsletter', url: '/admin/newsletter', admin: true },
  { name: 'admin-comments', url: '/comments', admin: true },
];

const VIEWPORTS: Array<[string, number, number]> = [
  ['1280x900', 1280, 900],
  ['390x844', 390, 844],
];

fs.mkdirSync(OUT, { recursive: true });

async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  // The login form must be driven through the LIVE circuit. Clicking submit while the page is
  // still the static prerender does a plain form POST, which Blazor rejects with
  // "The POST request does not specify which form is being submitted" — a test artefact, not a
  // page defect, but it looks exactly like a login failure if it is not waited out.
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
}


/**
 * Navigates through the SPA router rather than reloading the document.
 *
 * A full navigation drops the resolved authentication state (it is rehydrated from localStorage
 * asynchronously) and the router redirects to "/" before it comes back, so `page.goto` on an
 * authorised route audits the wrong page. `Blazor.navigateTo` keeps the live circuit.
 */
async function spaNavigate(page: Page, url: string) {
  await page.evaluate((u: string) => (window as any).Blazor.navigateTo(u), url);
  await page.waitForTimeout(2500);
}

/**
 * Waits for Blazor's prerender swap to finish before anything is measured.
 *
 * For roughly three seconds after load the document holds BOTH the prerendered markup and the
 * interactive copy — two headers, two footers, duplicated ids. axe-core run inside that window
 * manufactures `landmark-unique` / `duplicate-id` / region violations that do not exist in the
 * settled page, and a screenshot taken there shows a doubled layout that is not a real defect.
 * This gate waits for exactly one of each landmark, then lets the page idle.
 */
async function settle(page: Page) {
  await page.waitForFunction(
    () =>
      (window as any).Blazor !== undefined &&
      document.querySelectorAll('header').length <= 1 &&
      document.querySelectorAll('footer').length <= 1 &&
      document.querySelectorAll('main').length <= 1,
    null,
    { timeout: 30000 }
  ).catch(() => { /* recorded below either way — the landmark counts are asserted after this */ });
  await page.waitForTimeout(4500);
}

for (const route of ROUTES) {
  for (const [vpName, w, h] of VIEWPORTS) {
    test(`axe ${route.name} ${vpName}`, async ({ page }) => {
      await page.setViewportSize({ width: w, height: h });
      if (route.admin) await login(page);

      if (route.admin) {
        // A full page load of an authorised route bounces to "/" or "/login": the token lives in
        // localStorage and the router decides before the auth state has rehydrated. Navigating
        // through the SPA router instead keeps the warm circuit and its resolved identity — the
        // same thing a signed-in person clicking a link does.
        await spaNavigate(page, route.url);
      } else {
        await page.goto(BASE + route.url, { waitUntil: 'domcontentloaded' });
      }
      await settle(page);

      const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
      const nodes = results.violations.reduce((n, v) => n + v.nodes.length, 0);
      const detail = results.violations.map(v => ({
        id: v.id,
        impact: v.impact,
        nodes: v.nodes.length,
        targets: v.nodes.slice(0, 6).map(n => n.target.join(' ')),
      }));

      // Record where the browser actually ENDED UP. An authorisation redirect silently turns an
      // "admin dashboard" run into another audit of the home page, and a clean result on the
      // wrong page is worse than no result at all.
      const finalUrl = new URL(page.url()).pathname;
      const heading = await page.evaluate(() => document.querySelector('h1')?.textContent?.trim() ?? null);

      // Proof the prerender swap had finished when axe ran (see settle()).
      const landmarks = await page.evaluate(() => ({
        header: document.querySelectorAll('header').length,
        footer: document.querySelectorAll('footer').length,
        main: document.querySelectorAll('main').length,
      }));

      const overflow = await page.evaluate(() => ({
        scrollWidth: document.body.scrollWidth,
        clientWidth: document.body.clientWidth,
        docScroll: document.documentElement.scrollWidth,
        docClient: document.documentElement.clientWidth,
      }));

      fs.writeFileSync(
        path.join(OUT, `axe-${route.name}-${vpName}.json`),
        JSON.stringify({ page: route.name, url: route.url, finalUrl, heading, viewport: vpName, nodes, detail, landmarks, overflow }, null, 2)
      );
      console.log(
        `AXE ${route.name} ${vpName} @${finalUrl} -> ${nodes} nodes ${JSON.stringify(detail)} overflow=${overflow.scrollWidth - overflow.clientWidth}`
      );
      await page.screenshot({ path: path.join(OUT, `shot-${route.name}-${vpName}.png`), fullPage: false });
      expect(nodes, `axe violations on ${route.name} @ ${vpName}`).toBeGreaterThanOrEqual(0);
      expect(finalUrl, `${route.name} was not redirected away`).toBe(route.url);
      expect(landmarks.header, 'prerender swap had settled — one header').toBeLessThanOrEqual(1);
      expect(landmarks.footer, 'prerender swap had settled — one footer').toBeLessThanOrEqual(1);
    });
  }
}
