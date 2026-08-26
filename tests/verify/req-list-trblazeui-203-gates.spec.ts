/**
 * req-list-trblazeui-203-gates.spec.ts — verify-phase §4a (data-render) + §4b (visual-truth) gates
 * for scope REQ-UI-048 · REQ-FN-025 · REQ-UI-049, run 2026-08-25 after the TrBlazeUI 2.0.3 upgrade.
 *
 * REQ-UI-048 owns every screen (the migration), so this sweeps the public surface anonymously and
 * the admin surface as the seeded Admin, at 1280 and 390, recording geometry + key-control render
 * verdicts per screen. REQ-UI-049 owns `/`; REQ-FN-025 owns the upload dialog on `/admin/images`.
 *
 * Run: TB_BASE=http://172.18.144.1:5473 npx playwright test tests/verify/req-list-trblazeui-203-gates.spec.ts
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { BASE, login, nav, renderCheck, visualCheck, ControlResult, VisualResult } from './_gates';

const OUT = 'tests/.artifacts/verify-203-gates';
fs.mkdirSync(OUT, { recursive: true });

interface ScreenEvidence { route: string; controls: ControlResult[]; visual: VisualResult[]; }
const evidence: Record<string, ScreenEvidence> = {};
const ev = (route: string) => (evidence[route] ??= { route, controls: [], visual: [] });

async function gates(page: Page, route: string, slug: string) {
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, `${OUT}/${slug}-${w}.png`, w);
    await page.screenshot({ path: `${OUT}/${slug}-${w}-full.png`, fullPage: true });
    // Elements inside a deliberate overflow-x:auto scroller (tab strips, data tables) are not off-viewport.
    const raw = v.offViewport.map((s) => s.split('@')[0]);
    const inScroller: string[] = await page.evaluate((names: string[]) => {
      const has = (e: Element | null) => { let n = e; while (n) { const s = getComputedStyle(n); if (s.overflowX === 'auto' || s.overflowX === 'scroll') return true; n = n.parentElement; } return false; };
      return names.filter((nm) => { const el = document.querySelector(`[data-testid="${CSS.escape(nm)}"]`); return !!el && has(el.parentElement); });
    }, raw);
    const realOff = v.offViewport.filter((s) => !inScroller.includes(s.split('@')[0]));
    ev(route).visual.push({ ...v, offViewport: realOff });
    expect(`${slug}@${w} zeroSized=${JSON.stringify(v.zeroSized)}`).toContain('zeroSized=[]');
    expect(`${slug}@${w} overlaps=${JSON.stringify(v.overlaps)}`).toContain('overlaps=[]');
    expect(`${slug}@${w} offViewport=${JSON.stringify(realOff)}`).toContain('offViewport=[]');
    expect(`${slug}@${w} hScroll=${v.hScroll}`).toContain('hScroll=0');
    expect(`${slug}@${w} consoleErrors=${JSON.stringify(v.consoleErrors)}`).toContain('consoleErrors=[]');
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.waitForTimeout(500);
}

async function mustRender(page: Page, route: string, control: string, selector: string, kind: 'table' | 'value' | 'chart' | 'present' = 'value') {
  const r = await renderCheck(page, control, selector, kind);
  ev(route).controls.push(r);
  expect(`${control}: ${r.verdict} (${r.detail})`).toContain('RENDERS');
}

test.afterAll(() => fs.writeFileSync(`${OUT}/evidence.json`, JSON.stringify(evidence, null, 2)));

// ------------------------------------------------------------------ public, anonymous
test('REQ-UI-049 home: every section renders from site-owner data; no login affordance; gates at 1280/390', async ({ page }) => {
  test.setTimeout(240000);
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="post-card"]', { timeout: 60000 });
  await page.waitForTimeout(1500);
  const R = '/';
  await mustRender(page, R, 'hero name', '[data-testid="hero-name"], [data-testid="resume-hero"] h1, h1');
  await mustRender(page, R, 'home-stats band', '[data-testid="home-stats"]', 'present');
  await mustRender(page, R, 'stat tile value slot', '[data-testid="home-stat-card"] [data-slot="stat-tile-value"]');
  await mustRender(page, R, 'stat tile label slot', '[data-testid="home-stat-card"] [data-slot="stat-tile-label"]');
  const tiles = await page.locator('[data-testid="home-stat-card"]').count();
  expect(tiles).toBe(4);
  await mustRender(page, R, 'latest articles', '[data-testid="home-latest-articles"]', 'present');
  await mustRender(page, R, 'post card title', '[data-testid="post-card-title"]');
  await mustRender(page, R, 'post card no-banner fallback', '[data-testid="post-card-image-placeholder"]:not(.hidden)', 'present');
  const grad = await page.locator('[data-testid="post-card-image-placeholder"]:not(.hidden)').first().evaluate((e) => getComputedStyle(e).backgroundImage);
  ev(R).controls.push({ control: 'fallback gradient', verdict: /linear-gradient\(to (bottom right|right bottom), oklch/.test(grad) ? 'RENDERS' : 'RENDER-EMPTY', detail: grad });
  expect(grad).toMatch(/linear-gradient\(to (bottom right|right bottom), oklch/);
  // latest articles link to real posts
  const href = await page.locator('a[data-testid="post-card-title"]').first().getAttribute('href');
  expect(href).toMatch(/^\/post\//);
  const res = await page.request.get(`${BASE}${href}`);
  expect(res.status()).toBe(200);
  // REQ-UI-050 guard carried by the acceptance: no login affordance for an anonymous visitor
  const loginAffordance = await page.locator('a[href="/login"], [data-testid*="login"], [data-testid*="user-menu"]').count();
  expect(loginAffordance).toBe(0);
  await gates(page, R, 'home');
});

const PUBLIC: [string, RegExp][] = [
  ['/categories', /categor/i], ['/tags', /tag/i], ['/series', /series/i], ['/search', /search/i],
  ['/newsletters', /newsletter/i], ['/speaker-profile', /speak/i], ['/post/fix-test-post-no-banner-20260824', /no banner/i],
];
for (const [route, heading] of PUBLIC) {
  test(`REQ-UI-048 public ${route}: styled, no error boundary, gates at 1280/390`, async ({ page }) => {
    test.setTimeout(240000);
    await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('h1, h2').filter({ hasText: heading }).first()).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(1200);
    const probe = await page.evaluate(() => ({
      errorBoundary: !!document.querySelector('.blazor-error-boundary, [data-testid="error-boundary"]') || /Something went wrong/i.test(document.body.innerText),
      svgIcons: document.querySelectorAll('svg').length,
      svgEmpty: Array.from(document.querySelectorAll('svg')).filter((s) => s.children.length === 0).length,
      styled: document.querySelectorAll('[class*="bg-"], [class*="text-"]').length,
    }));
    ev(route).controls.push({ control: 'page shell', verdict: probe.errorBoundary ? 'RENDER-ERROR' : 'RENDERS', detail: JSON.stringify(probe) });
    expect(probe.errorBoundary).toBe(false);
    expect(probe.svgEmpty).toBe(0);
    expect(probe.styled).toBeGreaterThan(20);
    await gates(page, route, route.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'root');
  });
}

// ------------------------------------------------------------------ admin, seeded Admin, one circuit
test.describe.serial('admin surface', () => {
  let page: Page;
  test.beforeAll(async ({ browser }) => {
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    page = await ctx.newPage();
    const landed = await login(page, 'admin');
    expect(landed).not.toMatch(/change-password|login/i);
  });
  test.beforeEach(async () => {
    await page.setViewportSize({ width: 1280, height: 900 });
    for (let i = 0; i < 5 && (await page.locator('[role="dialog"]').count()); i++) {
      await page.keyboard.press('Escape'); await page.waitForTimeout(800);
    }
  });

  test('REQ-FN-025 /admin/images: upload dialog — styled Select in dialog, one limit per category, gates', async () => {
    test.setTimeout(300000);
    const R = '/admin/images';
    await nav(page, R, /Media Library/i);
    await mustRender(page, R, 'category tabs', '[data-testid="category-tabs"] [role="tab"]', 'present');
    await mustRender(page, R, 'user filter label', '[data-testid="user-filter-select"]');
    // Gallery or its documented empty state — the local DB holds no uploads by design.
    const gallery = await page.locator('[data-testid="image-grid"], [data-testid="images-empty"]').first();
    await expect(gallery).toBeVisible({ timeout: 30000 });
    ev(R).controls.push({ control: 'gallery/empty-state', verdict: 'RENDERS', detail: (await page.locator('[data-testid="image-grid"]').count()) ? 'grid' : 'documented empty state' });
    await gates(page, R, 'admin-images');

    await page.locator('[data-testid="upload-image"]').click();
    const dlg = page.locator('[role="dialog"]').first();
    await expect(dlg).toBeVisible({ timeout: 30000 });
    expect(await dlg.locator('select').count()).toBe(0);
    const trigger = dlg.locator('[data-testid="upload-category-select"]');
    await expect(trigger).toBeVisible();
    const expected: Record<string, string> = { Profiles: '2 MB', Logos: '500 KB', Awards: '500 KB', Icons: '200 KB', Blog: '5 MB', CV: '10 MB', General: '5 MB' };
    const seen: Record<string, { caption: string | null; dropzone: string | null }> = {};
    for (const label of Object.keys(expected)) {
      await trigger.click();
      await page.waitForTimeout(700);
      const opt = page.locator('[role="option"]', { hasText: new RegExp(`^${label}$`) });
      await expect(opt).toBeVisible({ timeout: 15000 });
      await opt.click();
      await page.waitForTimeout(1500);
      const text = (await dlg.innerText()).replace(/\s+/g, ' ');
      const caption = text.match(/Max ([\d.]+ ?(?:KB|MB))/i)?.[1] ?? null;
      const dropzone = text.match(/Max size: ([\d.]+ ?(?:KB|MB))/i)?.[1] ?? null;
      seen[label] = { caption, dropzone };
      expect(`${label} caption=${caption}`).toBe(`${label} caption=${expected[label]}`);
      expect(`${label} dropzone=${dropzone}`).toBe(`${label} dropzone=${expected[label]}`);
    }
    ev(R).controls.push({ control: 'upload dialog category Select (7 categories)', verdict: 'RENDERS', detail: JSON.stringify(seen) });
    // dialog geometry at both widths
    for (const w of [1280, 390]) {
      await page.setViewportSize({ width: w, height: w < 500 ? 844 : 900 });
      await page.waitForTimeout(900);
      const g = await dlg.evaluate((d) => { const r = d.getBoundingClientRect(); return { x: r.left, w: r.width, vw: document.documentElement.clientWidth, h: r.height }; });
      await page.screenshot({ path: `${OUT}/admin-images-upload-dialog-${w}.png` });
      expect(g.h).toBeGreaterThan(100);
      expect(g.x).toBeGreaterThanOrEqual(-2);
      expect(g.x + g.w).toBeLessThanOrEqual(g.vw + 2);
    }
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.keyboard.press('Escape');
    await page.waitForTimeout(800);
  });

  const ADMIN: [string, RegExp | string, string][] = [
    ['/admin', /Dashboard/i, 'admin-dashboard'],
    ['/users', /Users/i, 'admin-users'],
    ['/admin/skills', /Skills/i, 'admin-skills'],
    ['/admin/experience', /Experience/i, 'admin-experience'],
    ['/admin/analytics', '[data-testid="analytics-stat-tiles"]', 'admin-analytics'],
    ['/ManagePost', '[data-testid="post-title-input"]', 'admin-managepost'],
  ];
  for (const [route, marker, slug] of ADMIN) {
    test(`REQ-UI-048 admin ${route}: styled, no error boundary, gates at 1280/390`, async () => {
      test.setTimeout(240000);
      if (typeof marker === 'string') {
        await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
        await expect(page.locator(marker).first()).toBeVisible({ timeout: 60000 });
        await page.waitForTimeout(1200);
      } else {
        await nav(page, route, marker);
      }
      const probe = await page.evaluate(() => ({
        errorBoundary: /Something went wrong/i.test(document.body.innerText),
        svgEmpty: Array.from(document.querySelectorAll('svg')).filter((s) => s.children.length === 0).length,
        styled: document.querySelectorAll('[class*="bg-"], [class*="text-"]').length,
      }));
      ev(route).controls.push({ control: 'page shell', verdict: probe.errorBoundary ? 'RENDER-ERROR' : 'RENDERS', detail: JSON.stringify(probe) });
      expect(probe.errorBoundary).toBe(false);
      expect(probe.svgEmpty).toBe(0);
      if (route === '/admin') await mustRender(page, route, 'recent activity item', '[data-testid="recent-activity-item"]', 'present');
      if (route === '/ManagePost') {
        await mustRender(page, route, 'publish-date-picker on trigger', 'button[data-testid="publish-date-picker"]', 'present');
        await mustRender(page, route, 'publish-time-picker on trigger', 'button[data-testid="publish-time-picker"]', 'present');
      }
      await gates(page, route, slug);
    });
  }
});
