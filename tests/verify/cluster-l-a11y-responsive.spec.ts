/*
  cluster-l-a11y-responsive.spec.ts — REQ-NFR-007 (WCAG 2.1 AA) and REQ-NFR-010
  (responsive layouts across 4 breakpoints), Cluster L, 2026-08-09.

  Runs the same measurement twice: once against the tree as the verifier left it
  (TB_TAG=before) and once after this cluster's fixes (TB_TAG=after), so the
  violation counts reported in the checklist are a real delta and not a claim.

  What it measures
    1. axe-core (wcag2a / wcag2aa / wcag21a / wcag21aa) over every public route
       and a representative set of admin routes, in BOTH light and dark theme.
    2. A responsive sweep at 320 / 768 / 1024 / 1440 asserting: no horizontal
       document scroll, nothing rendered off-viewport, no overlapping sibling
       controls, no clipped text.
    3. A keyboard traversal recording, for every tab stop, whether it has a
       visible focus indicator.
    4. The two Routes.razor defects: the transient double shell while auth state
       resolves, and the 404 screen swap after hydration.

  Everything is written to test-results-cluster-l/ as JSON plus screenshots.
*/
import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5392';
const TAG = process.env.TB_TAG ?? 'after';
const OUT = path.join(process.cwd(), 'test-results-cluster-l');
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

const POST_SLUG = 'blazor-render-modes-explained';
const CATEGORY_SLUG = 'web-development';
const TAG_SLUG = 'blazor';
const SERIES_SLUG = 'blazor-server-in-production';

/** The four breakpoints REQ-NFR-010 is graded at (mobile / tablet / laptop / wide). */
const BREAKPOINTS: Array<[number, number]> = [
  [320, 720],
  [768, 1024],
  [1024, 800],
  [1440, 900],
];

type Route = { name: string; url: string; admin?: boolean };

const PUBLIC_ROUTES: Route[] = [
  { name: 'home', url: '/' },
  { name: 'about', url: '/about' },
  { name: 'search', url: '/search' },
  { name: 'newsletters', url: '/newsletters' },
  { name: 'resume', url: '/resume' },
  { name: 'rss', url: '/rss' },
  { name: 'categories', url: '/categories' },
  { name: 'series', url: '/series' },
  { name: 'tags', url: '/tags' },
  { name: 'post', url: `/post/${POST_SLUG}` },
  { name: 'category', url: `/category/${CATEGORY_SLUG}` },
  { name: 'tag', url: `/tag/${TAG_SLUG}` },
  { name: 'series-detail', url: `/series/${SERIES_SLUG}` },
  { name: 'login', url: '/login' },
  { name: 'forgot-password', url: '/forgot-password' },
  { name: 'not-found-page', url: '/404' },
];

const ADMIN_ROUTES: Route[] = [
  { name: 'admin-dashboard', url: '/admin', admin: true },
  { name: 'admin-analytics', url: '/admin/analytics', admin: true },
  { name: 'admin-images', url: '/admin/images', admin: true },
  { name: 'admin-newsletter', url: '/admin/newsletter', admin: true },
  { name: 'admin-comments', url: '/comments', admin: true },
  { name: 'admin-posts', url: '/BlogsList', admin: true },
  { name: 'admin-profile', url: '/admin/profile', admin: true },
  { name: 'admin-settings', url: '/settings', admin: true },
];

fs.mkdirSync(OUT, { recursive: true });

/** Appends one JSON record to a per-run results file. */
function record(file: string, data: unknown) {
  const target = path.join(OUT, `${file}-${TAG}.jsonl`);
  fs.appendFileSync(target, JSON.stringify(data) + '\n');
}

/**
 * Forces light or dark mode before the document runs, the same way a returning
 * visitor's stored preference does (App.razor reads this key pre-paint).
 */
async function seedTheme(page: Page, dark: boolean) {
  await page.addInitScript((isDark: boolean) => {
    try {
      localStorage.setItem('techieblog-dark-mode', isDark ? 'true' : 'false');
    } catch { /* private mode — the run still measures the default theme */ }
  }, dark);
}

/** Signs in as the documented seeded site owner. Never creates an account. */
async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  // Submitting before the circuit attaches degrades to a static POST and 400s.
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(2500);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2000);
}

/**
 * Navigates an authorised route through the SPA router.
 *
 * A full document load drops the resolved auth state while it rehydrates from
 * localStorage and the router bounces to "/", so page.goto on an admin route
 * silently audits the home page instead.
 */
async function spaNavigate(page: Page, url: string) {
  await page.evaluate((u: string) => (window as any).Blazor.navigateTo(u), url);
  await page.waitForTimeout(3000);
}

/**
 * Waits until the prerender/interactive handover has settled.
 *
 * Until it does the document can hold two copies of the shell, which manufactures
 * landmark and duplicate-id violations that do not exist in the settled page.
 */
async function settle(page: Page) {
  await page.waitForFunction(
    () =>
      (window as any).Blazor !== undefined &&
      document.querySelectorAll('header').length <= 1 &&
      document.querySelectorAll('footer').length <= 1 &&
      document.querySelectorAll('main').length <= 1,
    null,
    { timeout: 20000 }
  ).catch(() => { /* the landmark counts are recorded below either way */ });
  await page.waitForTimeout(2200);
}

/** Runs axe and returns a compact violation summary. */
async function axeScan(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
  const nodes = results.violations.reduce((sum, v) => sum + v.nodes.length, 0);
  return {
    nodes,
    violations: results.violations.map(v => ({
      id: v.id,
      impact: v.impact,
      count: v.nodes.length,
      targets: v.nodes.slice(0, 3).map(n => n.target.join(' ')),
    })),
  };
}

// ---------------------------------------------------------------------------
// 1 + 2. axe over every public route, light and dark.
// ---------------------------------------------------------------------------
for (const dark of [false, true]) {
  const theme = dark ? 'dark' : 'light';

  test(`axe public ${theme}`, async ({ page }) => {
    test.setTimeout(600000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await seedTheme(page, dark);

    let total = 0;
    for (const route of PUBLIC_ROUTES) {
      await page.goto(`${BASE}${route.url}`, { waitUntil: 'domcontentloaded' });
      await settle(page);
      const scan = await axeScan(page);
      const landmarks = await page.evaluate(() => ({
        header: document.querySelectorAll('header').length,
        main: document.querySelectorAll('main').length,
        footer: document.querySelectorAll('footer').length,
        title: document.title,
      }));
      total += scan.nodes;
      record('axe-public', { theme, route: route.name, url: page.url(), ...scan, landmarks });
      if (scan.nodes > 0) {
        await page.screenshot({ path: path.join(OUT, `axe-${route.name}-${theme}-${TAG}.png`) });
      }
    }
    record('axe-summary', { scope: 'public', theme, totalNodes: total, routes: PUBLIC_ROUTES.length });
    console.log(`[axe public ${theme}] total violation nodes: ${total}`);
  });

  test(`axe admin ${theme}`, async ({ page }) => {
    test.setTimeout(600000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await seedTheme(page, dark);
    await login(page);

    let total = 0;
    for (const route of ADMIN_ROUTES) {
      await spaNavigate(page, route.url);
      await settle(page);
      const landedOn = new URL(page.url()).pathname.toLowerCase();
      const scan = await axeScan(page);
      total += scan.nodes;
      record('axe-admin', { theme, route: route.name, expected: route.url, landedOn, ...scan });
      if (scan.nodes > 0) {
        await page.screenshot({ path: path.join(OUT, `axe-${route.name}-${theme}-${TAG}.png`) });
      }
      // A run that bounced anywhere else is measuring the WRONG PAGE. It bounces two
      // ways: to "/" when the auth state has not rehydrated, and to "/change-password"
      // when REQ-NFR-023's MustChangePassword flag is set on the seeded account — which
      // another agent can re-arm mid-run, and did, silently turning eight admin audits
      // into eight audits of the same interstitial. Assert the landing route, not just
      // "not home".
      expect(landedOn, `admin route ${route.url} bounced to ${landedOn}`).toBe(route.url.toLowerCase());
    }
    record('axe-summary', { scope: 'admin', theme, totalNodes: total, routes: ADMIN_ROUTES.length });
    console.log(`[axe admin ${theme}] total violation nodes: ${total}`);
  });
}

// ---------------------------------------------------------------------------
// 3. Responsive sweep — REQ-NFR-010.
// ---------------------------------------------------------------------------

/**
 * Collects every layout defect visible at the current viewport.
 *
 * Elements inside a scroll container (overflow-x auto/scroll) and fixed-position
 * elements are excluded: a deliberately scrollable table is not a layout break.
 */
async function layoutDefects(page: Page) {
  return page.evaluate(() => {
    const vw = document.documentElement.clientWidth;
    const inScroller = (el: Element) => {
      let node: Element | null = el.parentElement;
      while (node && node !== document.body) {
        const cs = getComputedStyle(node);
        if (cs.overflowX === 'auto' || cs.overflowX === 'scroll') return true;
        node = node.parentElement;
      }
      return false;
    };

    /*
      Screen-reader-only content is not a layout defect, and the naive version of this
      sweep said it was. The `.sr-only` / `.tb-keyboard-fallback` idiom deliberately
      clamps a box to 1x1 with `clip-path: inset(50%)` and `overflow: hidden`, so its
      text always "overflows" and its children always sit outside the viewport — the
      whole point. Counting those produced 1-3 phantom findings on EVERY route (the
      captcha status live region, the star-rating text, the keyboard fallback's legend)
      and buried the question the requirement actually asks. Anything inside a
      visually-hidden subtree is excluded here; genuine overflow shows up as document
      scroll, which is measured separately and unconditionally.
    */
    const isVisuallyHidden = (el: Element) => {
      let node: Element | null = el;
      while (node && node !== document.body) {
        const cs = getComputedStyle(node);
        const clipped = cs.clipPath.includes('inset(50%') || cs.clip === 'rect(0px, 0px, 0px, 0px)';
        const r = node.getBoundingClientRect();
        if (clipped || (r.width <= 1 && r.height <= 1 && cs.overflow === 'hidden')) return true;
        if ((node.className || '').toString().split(/\s+/).includes('sr-only')) return true;
        node = node.parentElement;
      }
      return false;
    };

    const visible = Array.from(document.body.querySelectorAll<HTMLElement>('*')).filter(el => {
      const cs = getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden' || cs.position === 'fixed') return false;
      const r = el.getBoundingClientRect();
      if (r.width <= 0 || r.height <= 0) return false;
      return !isVisuallyHidden(el);
    });

    const describe = (el: HTMLElement) => {
      const r = el.getBoundingClientRect();
      return {
        tag: el.tagName.toLowerCase(),
        testid: el.getAttribute('data-testid') ?? '',
        cls: (el.className || '').toString().slice(0, 90),
        left: Math.round(r.left),
        right: Math.round(r.right),
        width: Math.round(r.width),
      };
    };

    // (a) anything painted past the right edge of the viewport
    const offViewport = visible
      .filter(el => !inScroller(el))
      .filter(el => {
        const r = el.getBoundingClientRect();
        return r.right > vw + 1 || r.left < -1;
      })
      .map(describe);

    // (b) clipped text — a text node whose scrollWidth exceeds its box with no
    //     scroller and no ellipsis to say the truncation was intended
    const clipped = visible
      .filter(el => el.children.length === 0 && (el.textContent ?? '').trim().length > 0)
      .filter(el => {
        const cs = getComputedStyle(el);
        if (cs.textOverflow === 'ellipsis') return false;
        if (cs.overflowX === 'auto' || cs.overflowX === 'scroll') return false;
        return el.scrollWidth > el.clientWidth + 2 && cs.overflow !== 'visible';
      })
      .map(describe);

    // (c) sibling interactive controls whose boxes intersect
    const controls = Array.from(
      document.body.querySelectorAll<HTMLElement>('button, a[href], input, select, textarea')
    ).filter(el => {
      const cs = getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
      const r = el.getBoundingClientRect();
      return r.width > 0 && r.height > 0 && !isVisuallyHidden(el);
    });
    const overlaps: Array<Record<string, unknown>> = [];
    for (let i = 0; i < controls.length; i++) {
      for (let j = i + 1; j < controls.length; j++) {
        const a = controls[i];
        const b = controls[j];
        if (a.contains(b) || b.contains(a)) continue;
        const ra = a.getBoundingClientRect();
        const rb = b.getBoundingClientRect();
        const ox = Math.min(ra.right, rb.right) - Math.max(ra.left, rb.left);
        const oy = Math.min(ra.bottom, rb.bottom) - Math.max(ra.top, rb.top);
        if (ox > 2 && oy > 2) overlaps.push({ a: describe(a), b: describe(b), ox: Math.round(ox), oy: Math.round(oy) });
      }
    }

    return {
      scrollWidth: document.documentElement.scrollWidth,
      clientWidth: vw,
      hScroll: document.documentElement.scrollWidth - vw,
      offViewport: offViewport.slice(0, 12),
      offViewportCount: offViewport.length,
      clipped: clipped.slice(0, 8),
      clippedCount: clipped.length,
      overlaps: overlaps.slice(0, 8),
      overlapCount: overlaps.length,
    };
  });
}

test('responsive public 4 breakpoints', async ({ page }) => {
  test.setTimeout(1800000);
  let bad = 0;
  for (const [w, h] of BREAKPOINTS) {
    await page.setViewportSize({ width: w, height: h });
    for (const route of PUBLIC_ROUTES) {
      await page.goto(`${BASE}${route.url}`, { waitUntil: 'domcontentloaded' });
      await settle(page);
      const defects = await layoutDefects(page);
      const failed =
        defects.hScroll > 0 || defects.offViewportCount > 0 || defects.clippedCount > 0 || defects.overlapCount > 0;
      if (failed) bad++;
      record('responsive-public', { width: w, route: route.name, failed, ...defects });
      if (failed || route.name === 'home' || route.name === 'post') {
        await page.screenshot({ path: path.join(OUT, `resp-${route.name}-${w}-${TAG}.png`) });
      }
    }
  }
  record('responsive-summary', { scope: 'public', badRuns: bad, totalRuns: BREAKPOINTS.length * PUBLIC_ROUTES.length });
  console.log(`[responsive public] failing runs: ${bad} / ${BREAKPOINTS.length * PUBLIC_ROUTES.length}`);
});

test('responsive admin 4 breakpoints', async ({ page }) => {
  test.setTimeout(1800000);
  await page.setViewportSize({ width: 1440, height: 900 });
  await login(page);
  let bad = 0;
  for (const [w, h] of BREAKPOINTS) {
    await page.setViewportSize({ width: w, height: h });
    for (const route of ADMIN_ROUTES) {
      await spaNavigate(page, route.url);
      await settle(page);
      const defects = await layoutDefects(page);
      const failed =
        defects.hScroll > 0 || defects.offViewportCount > 0 || defects.clippedCount > 0 || defects.overlapCount > 0;
      if (failed) bad++;
      const adminLanded = new URL(page.url()).pathname.toLowerCase();
      record('responsive-admin', { width: w, route: route.name, landedOn: adminLanded, failed, ...defects });
      expect(adminLanded, `admin route ${route.url} bounced to ${adminLanded}`).toBe(route.url.toLowerCase());
      if (failed) {
        await page.screenshot({ path: path.join(OUT, `resp-${route.name}-${w}-${TAG}.png`) });
      }
    }
  }
  record('responsive-summary', { scope: 'admin', badRuns: bad, totalRuns: BREAKPOINTS.length * ADMIN_ROUTES.length });
  console.log(`[responsive admin] failing runs: ${bad} / ${BREAKPOINTS.length * ADMIN_ROUTES.length}`);
});

// ---------------------------------------------------------------------------
// 4. Keyboard traversal — every interactive control reachable, with a visible ring.
// ---------------------------------------------------------------------------
test('keyboard traversal', async ({ page }) => {
  test.setTimeout(600000);
  await page.setViewportSize({ width: 1280, height: 900 });

  const pages = ['/', `/post/${POST_SLUG}`, '/rss', '/resume', '/newsletters', '/login'];
  let stopsTotal = 0;
  let noRing = 0;

  for (const url of pages) {
    await page.goto(`${BASE}${url}`, { waitUntil: 'domcontentloaded' });
    await settle(page);
    await page.evaluate(() => (document.activeElement as HTMLElement)?.blur());

    const seen = new Set<string>();
    const stops: Array<Record<string, unknown>> = [];
    for (let i = 0; i < 90; i++) {
      await page.keyboard.press('Tab');
      const info = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        if (!el || el === document.body) return null;
        const r = el.getBoundingClientRect();
        const cs = getComputedStyle(el);
        const hidden = !!el.closest('[aria-hidden="true"]');
        const ringed =
          (cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) >= 1) ||
          cs.boxShadow !== 'none';
        return {
          tag: el.tagName.toLowerCase(),
          testid: el.getAttribute('data-testid') ?? '',
          name: (el.getAttribute('aria-label') || el.textContent || '').trim().slice(0, 40),
          w: Math.round(r.width),
          h: Math.round(r.height),
          ringed,
          insideAriaHidden: hidden,
          key: el.tagName + '|' + (el.getAttribute('data-testid') ?? '') + '|' + Math.round(r.top) + '|' + Math.round(r.left),
        };
      });
      if (!info) break;
      if (seen.has(info.key)) break;
      seen.add(info.key);
      stops.push(info);
      stopsTotal++;
      if (!info.ringed || info.insideAriaHidden || info.w === 0 || info.h === 0) noRing++;
    }
    record('keyboard', {
      url,
      stops: stops.length,
      defects: stops.filter(s => !s.ringed || s.insideAriaHidden || s.w === 0 || s.h === 0),
    });
  }
  record('keyboard-summary', { stopsTotal, defects: noRing });
  console.log(`[keyboard] ${stopsTotal} stops, ${noRing} defective`);
});

// ---------------------------------------------------------------------------
// 5. The two Routes.razor defects.
// ---------------------------------------------------------------------------
test('routes double shell and 404 swap', async ({ page }) => {
  test.setTimeout(600000);
  await page.setViewportSize({ width: 1280, height: 900 });

  // (a) DOUBLE SHELL — sample the landmark counts continuously through the
  //     prerender -> interactive handover. Before the fix, MainLayout nested
  //     inside MainLayout while the auth state resolved, so the page briefly
  //     carried two headers, two sidebars and two footers.
  //
  //     A public route is the WRONG place to look: the authorisation policy for a
  //     page with no [Authorize] resolves without waiting, so <Authorizing> never
  //     renders and the nesting never shows. The repro is a FULL document load of an
  //     authorised route with a token already in localStorage — the auth state has to
  //     come back from JS interop, so the router really does sit in Authorizing.
  const sampleShell = async (label: string, url: string, waitFirst: boolean) => {
    if (waitFirst) await login(page);
    await page.goto(`${BASE}${url}`, { waitUntil: 'commit' });
    let maxHeader = 0;
    let maxFooter = 0;
    let maxMain = 0;
    let emptyTitleSamples = 0;
    let sawAuthorizing = false;
    let maxWhileAuthorizing = 0;
    for (let i = 0; i < 140; i++) {
      const s = await page.evaluate(() => ({
        header: document.querySelectorAll('header').length,
        footer: document.querySelectorAll('footer').length,
        main: document.querySelectorAll('main').length,
        authorizing: !!document.querySelector('[data-testid="authorizing"]'),
        title: document.title,
      })).catch(() => null);
      if (s) {
        maxHeader = Math.max(maxHeader, s.header);
        maxFooter = Math.max(maxFooter, s.footer);
        maxMain = Math.max(maxMain, s.main);
        if (s.authorizing) {
          sawAuthorizing = true;
          maxWhileAuthorizing = Math.max(maxWhileAuthorizing, s.header);
        }
        if (!s.title || s.title.trim() === '') emptyTitleSamples++;
      }
      await page.waitForTimeout(40);
    }
    const result = { label, url, maxHeader, maxFooter, maxMain, emptyTitleSamples, sawAuthorizing, maxWhileAuthorizing };
    record('routes-double-shell', result);
    console.log(`[double shell] ${JSON.stringify(result)}`);
    return result;
  };

  await sampleShell('public-home', '/', false);
  await sampleShell('authed-admin-full-load', '/admin', true);

  // (b) 404 SWAP — the server re-executes to /404 and prerenders `not-found-page`;
  //     after hydration the router matches nothing and fires <NotFound>. If that
  //     fragment paints its own markup, `not-found` appears and the screen visibly
  //     changes. The SERVER's answer is read from the raw response body (a DOM read
  //     races hydration and reported neither marker); the settled DOM is read after.
  const unknown = `/cluster-l-no-such-route-${Date.now()}`;
  const raw = await page.request.get(`${BASE}${unknown}`);
  const rawBody = await raw.text();
  const prerender = {
    page: rawBody.includes('data-testid="not-found-page"'),
    fragment: rawBody.includes('data-testid="not-found"') && !rawBody.includes('data-testid="not-found-page"'),
    bytes: rawBody.length,
    rawStatus: raw.status(),
  };
  const response = await page.goto(`${BASE}${unknown}`, { waitUntil: 'commit' });
  const status = response?.status();
  let sawFragment = false;
  for (let i = 0; i < 100; i++) {
    const s = await page.evaluate(() => !!document.querySelector('[data-testid="not-found"]')).catch(() => false);
    if (s) sawFragment = true;
    await page.waitForTimeout(60);
  }
  const settled = await page.evaluate(() => ({
    page: !!document.querySelector('[data-testid="not-found-page"]'),
    fragment: !!document.querySelector('[data-testid="not-found"]'),
    header: document.querySelectorAll('header').length,
    footer: document.querySelectorAll('footer').length,
    title: document.title,
  }));
  await page.screenshot({ path: path.join(OUT, `routes-404-${TAG}.png`) });
  record('routes-404', { url: unknown, status, prerender, sawFragmentDuringHydration: sawFragment, settled });
  console.log(`[404] status=${status} prerender=${JSON.stringify(prerender)} sawFragment=${sawFragment} settled=${JSON.stringify(settled)}`);

  // A real route must still win.
  const ok = await page.goto(`${BASE}/about`, { waitUntil: 'domcontentloaded' });
  expect(ok?.status()).toBe(200);
});

// ---------------------------------------------------------------------------
// 6. The intermittent desktop rail — cluster E reported `.lg\:flex-row` sometimes
//    missing from the CSSOM at 1280, stacking the sidebar under the article.
// ---------------------------------------------------------------------------
test('desktop rail at 1024 and 1280', async ({ page }) => {
  test.setTimeout(600000);
  const results: Array<Record<string, unknown>> = [];
  for (let attempt = 0; attempt < 8; attempt++) {
    for (const w of [1024, 1280, 1440]) {
      await page.setViewportSize({ width: w, height: 900 });
      await page.goto(`${BASE}/rss`, { waitUntil: 'domcontentloaded' });
      await settle(page);
      const state = await page.evaluate(() => {
        const main = document.querySelector('[data-testid="main-content"]') as HTMLElement | null;
        const rail = main?.parentElement?.querySelector('aside, [data-testid="blog-sidebar"]') as HTMLElement | null
          ?? (main?.nextElementSibling as HTMLElement | null);
        const row = main?.parentElement as HTMLElement | null;
        // Is the .lg\:flex-row rule actually present in the CSSOM?
        let ruleFound = false;
        for (const sheet of Array.from(document.styleSheets)) {
          try {
            for (const rule of Array.from(sheet.cssRules)) {
              const text = (rule as CSSRule).cssText;
              if (text.includes('lg\\:flex-row') || text.includes('lg\\3A flex-row')) ruleFound = true;
            }
          } catch { /* cross-origin sheet — none in this app */ }
        }
        return {
          flexDirection: row ? getComputedStyle(row).flexDirection : 'no-row',
          mainTop: main ? Math.round(main.getBoundingClientRect().top) : -1,
          railTop: rail ? Math.round(rail.getBoundingClientRect().top) : -1,
          railLeft: rail ? Math.round(rail.getBoundingClientRect().left) : -1,
          mainRight: main ? Math.round(main.getBoundingClientRect().right) : -1,
          ruleFound,
          sheets: document.styleSheets.length,
        };
      });
      const sideBySide = state.railLeft >= state.mainRight - 4;
      results.push({ attempt, width: w, sideBySide, ...state });
    }
  }
  record('desktop-rail', { results });
  const stackedAtDesktop = results.filter(r => (r.width as number) >= 1024 && !r.sideBySide);
  const missingRule = results.filter(r => !r.ruleFound);
  console.log(`[rail] runs=${results.length} stacked=${stackedAtDesktop.length} missingRule=${missingRule.length}`);
  record('desktop-rail-summary', { runs: results.length, stacked: stackedAtDesktop.length, missingRule: missingRule.length });
});
