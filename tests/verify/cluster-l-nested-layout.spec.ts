/*
  cluster-l-nested-layout.spec.ts — proof for the Routes.razor double-shell defect
  (REQ-NFR-007, Cluster L, 2026-08-09).

  WHY THIS EXISTS SEPARATELY
  AuthorizeRouteView renders BOTH of its fallback fragments through its own
  `LayoutView Layout="DefaultLayout"`, so any <LayoutView> written INSIDE
  <Authorizing> or <NotAuthorized> nests a second complete shell inside the first.
  The <Authorizing> half is the one users complained about ("both shells for
  ~1.5s") but it is not reproducible on demand — the auth state resolves in a few
  milliseconds on a local circuit, so the fragment simply never paints
  (sawAuthorizing: false over 280 samples in the main spec).

  The <NotAuthorized> half of the SAME defect is deterministic: sign in as the
  seeded contributor and open a route that requires a higher role. Before the fix
  that rendered AuthLayout inside MainLayout — a nested <main>, a duplicated brand
  link, and two ToastProvider/PortalHost pairs. That is what this file measures,
  and it is the same code path with the same cause.

  Run with TB_TAG=before against the unmodified Routes.razor and TB_TAG=after
  against the fixed one.
*/
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5392';
const TAG = process.env.TB_TAG ?? 'after';
const OUT = path.join(process.cwd(), 'test-results-cluster-l');

// Seeded contributor from docs/TechieBlog-UsageGuide.md — has no staff surface,
// so every /admin route answers NotAuthorized for this account.
const USER_EMAIL = 'contributor@techieblog.test';
const USER_PASSWORD = 'Contrib#Pass1';

fs.mkdirSync(OUT, { recursive: true });

async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(2500);
  await page.fill('[data-testid="login-email"]', USER_EMAIL);
  await page.fill('[data-testid="login-password"]', USER_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2500);
}

test('not-authorized fallback renders exactly one shell', async ({ page }) => {
  test.setTimeout(240000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  const observed: Array<Record<string, unknown>> = [];
  for (const route of ['/admin', '/settings', '/users']) {
    await page.evaluate((u: string) => (window as any).Blazor.navigateTo(u), route);
    await page.waitForTimeout(4000);
    const shape = await page.evaluate(() => ({
      path: location.pathname,
      accessDenied: document.querySelectorAll('[data-testid="access-denied"], [data-testid="access-denied-page"]').length,
      header: document.querySelectorAll('header').length,
      footer: document.querySelectorAll('footer').length,
      main: document.querySelectorAll('main').length,
      mainTestids: Array.from(document.querySelectorAll('main')).map(m => m.getAttribute('data-testid')),
      brandLinks: document.querySelectorAll('[data-testid="brand-link"]').length,
      portalHosts: document.querySelectorAll('.trblazeui-portal-host, [data-portal-host]').length,
      nestedMain: !!document.querySelector('main main'),
    }));
    observed.push({ route, ...shape });
    await page.screenshot({ path: path.join(OUT, `nested-layout-${route.replace(/\W+/g, '')}-${TAG}.png`) });
  }
  fs.appendFileSync(path.join(OUT, `nested-layout-${TAG}.jsonl`), observed.map(o => JSON.stringify(o)).join('\n') + '\n');
  console.log('NESTED:', JSON.stringify(observed));

  for (const o of observed) {
    expect(o.main, `nested <main> on ${o.route}`).toBeLessThanOrEqual(1);
    expect(o.nestedMain, `main inside main on ${o.route}`).toBe(false);
    expect(o.header, `duplicate header on ${o.route}`).toBeLessThanOrEqual(1);
  }
});
