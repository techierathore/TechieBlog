/**
 * fxv4-theming.spec.ts - verify-phase writer W4.
 *
 * Rows: REQ-UI-033 (no unreadable text in dark mode, any page, all three themes), REQ-UI-048 (no Fluent
 * UI left; every page renders in all three themes x light/dark).
 *
 * Read-only. The theme is switched on the VISITOR side (localStorage `techieblog-theme` /
 * `techieblog-dark-mode`, which App.razor's pre-paint script applies), so the global site setting is
 * never touched and sibling writers are not disturbed.
 */
import { test, expect, Page, Browser } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { login, nav } from './_gates';
import { BASE, psql, sleep } from './fxv4-helpers';

test.setTimeout(480000);
const THEMES = ['trblaze-modern', 'developer', 'minimal'] as const;

/** WCAG contrast of every visible text node against its effective opaque background (oklch aware). */
async function contrast(page: Page) {
  return page.evaluate(() => {
    const cv = document.createElement('canvas'); cv.width = cv.height = 1;
    const g = cv.getContext('2d', { willReadFrequently: true })!;
    const parse = (c: string) => {
      if (!c || c === 'transparent') return null;
      g.clearRect(0, 0, 1, 1); g.fillStyle = '#000'; g.fillStyle = c; g.fillRect(0, 0, 1, 1);
      const d = g.getImageData(0, 0, 1, 1).data;
      if (d[3] === 0) return { r: 0, g: 0, b: 0, a: 0 };
      return { r: Math.round(d[0] * 255 / d[3]), g: Math.round(d[1] * 255 / d[3]), b: Math.round(d[2] * 255 / d[3]), a: d[3] / 255 };
    };
    const lum = (c: any) => { const f = (v: number) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); };
    const bgOf = (el: Element | null) => {
      const layers: any[] = [];
      while (el) { const c = parse(getComputedStyle(el).backgroundColor); if (c && c.a > 0) { layers.push(c); if (c.a >= 1) break; } el = el.parentElement; }
      let base = { r: 255, g: 255, b: 255 };
      for (const l of layers.reverse()) base = { r: l.r * l.a + base.r * (1 - l.a), g: l.g * l.a + base.g * (1 - l.a), b: l.b * l.a + base.b * (1 - l.a) };
      return base;
    };
    const bad: string[] = []; let measured = 0;
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    const seen = new Set<Element>();
    while (walker.nextNode()) {
      const t = walker.currentNode as Text; if (!t.textContent?.trim()) continue;
      const el = t.parentElement!; if (seen.has(el)) continue; seen.add(el);
      const cs = getComputedStyle(el); const r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2 || cs.visibility === 'hidden' || cs.display === 'none' || parseFloat(cs.opacity) < 0.05) continue;
      if (el.closest('[aria-hidden="true"], script, style, [disabled], .sr-only')) continue;
      const fg = parse(cs.color); if (!fg) continue; const bg = bgOf(el);
      const fgC = { r: fg.r * fg.a + bg.r * (1 - fg.a), g: fg.g * fg.a + bg.g * (1 - fg.a), b: fg.b * fg.a + bg.b * (1 - fg.a) };
      const L1 = lum(fgC), L2 = lum(bg); const ratio = (Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05);
      const size = parseFloat(cs.fontSize); const large = size >= 24 || (size >= 18.66 && parseInt(cs.fontWeight) >= 700);
      measured++;
      if (ratio < (large ? 3 : 4.5)) bad.push(`${ratio.toFixed(2)} "${t.textContent!.trim().slice(0, 30)}" ${el.tagName.toLowerCase()}${el.getAttribute('data-testid') ? '[' + el.getAttribute('data-testid') + ']' : ''}`);
    }
    return { measured, bad: bad.slice(0, 12), badCount: bad.length };
  });
}

/** State of the page that a theme/render check needs. */
async function renderState(page: Page) {
  return page.evaluate(() => {
    const geometry = (s: Element) => s.querySelector('path, circle, rect, line, polyline, polygon, ellipse');
    const err = document.querySelector('#blazor-error-ui') as HTMLElement | null;
    const body = getComputedStyle(document.body);
    return {
      dark: document.documentElement.classList.contains('dark'),
      theme: document.documentElement.getAttribute('data-site-theme'),
      errorUiVisible: !!err && getComputedStyle(err).display !== 'none',
      sheets: document.styleSheets.length,
      bodyBg: body.backgroundColor,
      emptySvgs: Array.from(document.querySelectorAll('svg')).filter((s) => !geometry(s)).length,
      text: (document.body.innerText || '').slice(0, 4000),
    };
  });
}

async function visitorContext(browser: Browser, theme: string, dark: boolean) {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: dark ? 'dark' : 'light' });
  await ctx.addInitScript(([t, d]) => {
    try { localStorage.setItem('techieblog-theme', JSON.stringify(t)); localStorage.setItem('techieblog-dark-mode', String(d)); } catch { /* ignore */ }
  }, [theme, dark] as const);
  return ctx;
}

async function load(page: Page, route: string, ready: string) {
  await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector(ready, { timeout: 90000 });
  await page.waitForFunction(() => !/Loading (post|resume)/.test(document.body.innerText) && !document.querySelector('[data-testid$="-loading"]'), undefined, { timeout: 60000 }).catch(() => {});
  await sleep(2500);
}

function publicPages(): [string, string, string][] {
  const post = psql("SELECT Slug FROM BlogPost WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL) ORDER BY PostId LIMIT 1");
  const pages: [string, string, string][] = [
    ['home', '/', '[data-testid="public-header"]'],
    ['about', '/about', '[data-testid="about-page"]'],
    ['search', '/search?q=fxv', '[data-testid="search-total-count"], [data-testid="search-empty"], [data-testid="search-results"]'],
    ['resume', '/resume', '[data-testid="contact-section"]'],
    ['newsletters', '/newsletters', '[data-testid="public-header"]'],
    ['login', '/login', '[data-testid="login-email"]'],
  ];
  if (post) pages.splice(1, 0, ['post', `/post/${post}`, '[data-testid="post-content"]']);
  return pages;
}

for (const theme of THEMES) {
  test(`REQ-UI-033 dark mode: every public page is legible in the ${theme} theme`, async ({ browser }) => {
    const ctx = await visitorContext(browser, theme, true);
    const page = await ctx.newPage();
    const report: Record<string, any> = {};
    try {
      for (const [name, route, ready] of publicPages()) {
        await load(page, route, ready);
        const st = await renderState(page);
        expect(st.dark, `${name}: dark class applied`).toBe(true);
        expect(st.theme, `${name}: ${theme} theme applied`).toBe(theme);
        const c = await contrast(page);
        report[name] = { measured: c.measured, bad: c.badCount, worst: c.bad.slice(0, 3) };
        expect(c.measured, `${name}: some text was measured`).toBeGreaterThan(0);
        expect(c.bad, `${name} (${theme}, dark): text below WCAG AA`).toEqual([]);
      }
    } finally {
      console.log(`[REQ-UI-033] ${theme} dark ${JSON.stringify(report)}`);
      await ctx.close();
    }
  });
}

for (const theme of THEMES) {
  test(`REQ-UI-033 dark mode: the admin area is legible in the ${theme} theme`, async ({ browser }) => {
    const ctx = await visitorContext(browser, theme, true);
    const page = await ctx.newPage();
    const report: Record<string, any> = {};
    try {
      await login(page, 'admin');
      const screens: [string, string, RegExp, string][] = [
        ['dashboard', '/admin', /^Dashboard$/, '[data-testid="stat-posts-value"]'],
        ['posts', '/BlogsList', /Posts/i, '[data-testid="posts-grid"], [data-testid="posts-empty"]'],
        ['comments', '/CommentsList', /Comment moderation/, '[data-testid="comments-grid"], [data-testid="comments-empty"]'],
        ['categories', '/admin/categories', /Categories Management/, '[data-testid="categories-grid"], [data-testid="categories-empty"]'],
        ['subscribers', '/admin/subscribers', /^Subscribers$/, '[data-testid="subscribers-grid"], [data-testid="subscribers-empty"]'],
        ['settings', '/settings', /^Settings$/, '[data-testid="save-settings"]'],
      ];
      for (const [name, route, heading, ready] of screens) {
        await nav(page, route, heading);
        await page.waitForSelector(ready, { state: 'visible', timeout: 60000 });
        await sleep(2000);
        expect(await page.evaluate(() => document.documentElement.classList.contains('dark')), `${name}: dark class`).toBe(true);
        const c = await contrast(page);
        report[name] = { measured: c.measured, bad: c.badCount, worst: c.bad.slice(0, 3) };
        expect(c.bad, `admin ${name} (${theme}, dark): text below WCAG AA`).toEqual([]);
      }
    } finally {
      console.log(`[REQ-UI-033] admin ${theme} dark ${JSON.stringify(report)}`);
      await ctx.close();
    }
  });
}

test('REQ-UI-048 no Microsoft.FluentUI package and no <Fluent*> component remains in the solution', async () => {
  const root = path.resolve(__dirname, '..', '..');
  const skip = new Set(['bin', 'obj', 'node_modules', '.git', 'wwwroot']);
  const offenders: string[] = [];
  const walk = (dir: string, code = true) => {
    for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
      if (skip.has(e.name)) continue;
      const full = path.join(dir, e.name);
      if (e.isDirectory()) { walk(full, code); continue; }
      if (/\.(csproj|props|targets|slnx|config)$/i.test(e.name)) {
        if (/Microsoft\.FluentUI/i.test(fs.readFileSync(full, 'utf8'))) offenders.push(`${path.relative(root, full)}: Microsoft.FluentUI package`);
      } else if (code && /\.(razor|cs)$/i.test(e.name)) {
        const text = fs.readFileSync(full, 'utf8');
        // A component tag, not a comment: strip Razor/HTML/C# comments first.
        const code = text.replace(/@\*[\s\S]*?\*@/g, '').replace(/<!--[\s\S]*?-->/g, '').replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
        if (/<Fluent[A-Z]\w*/.test(code)) offenders.push(`${path.relative(root, full)}: <Fluent*> component`);
        if (/using\s+Microsoft\.FluentUI/.test(code)) offenders.push(`${path.relative(root, full)}: using Microsoft.FluentUI`);
      }
    }
  };
  walk(path.join(root, 'source'));
  walk(path.join(root, 'tests'), false); // test projects: package references only
  // Positive control: the scan really reads the files (the solution has more than a handful of razor pages).
  let razor = 0;
  const count = (dir: string) => { for (const e of fs.readdirSync(dir, { withFileTypes: true })) { if (skip.has(e.name)) continue; const f = path.join(dir, e.name); if (e.isDirectory()) count(f); else if (/\.razor$/i.test(e.name)) razor++; } };
  count(path.join(root, 'source'));
  expect(razor, 'positive control: razor files were scanned').toBeGreaterThan(30);
  expect(offenders, 'Fluent UI leftovers').toEqual([]);
});

for (const theme of THEMES) {
  for (const dark of [false, true]) {
    test(`REQ-UI-048 every public page renders correctly in the ${theme} theme, ${dark ? 'dark' : 'light'}`, async ({ browser }) => {
      const ctx = await visitorContext(browser, theme, dark);
      const page = await ctx.newPage();
      try {
        for (const [name, route, ready] of publicPages()) {
          await load(page, route, ready);
          const st = await renderState(page);
          expect(st.errorUiVisible, `${name}: no Blazor error banner`).toBe(false);
          expect(st.sheets, `${name}: stylesheets applied`).toBeGreaterThan(0);
          expect(st.emptySvgs, `${name}: no empty icon`).toBe(0);
          expect(st.dark, `${name}: ${dark ? 'dark' : 'light'} class`).toBe(dark);
          expect(st.theme, `${name}: theme attribute`).toBe(theme);
          expect(st.text.replace(/\s+/g, ' ').trim().length, `${name}: page has content`).toBeGreaterThan(80);
          const hScroll = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
          expect(hScroll, `${name}: no sideways scroll`).toBeLessThanOrEqual(1);
        }
      } finally {
        await ctx.close();
      }
    });
  }
}
