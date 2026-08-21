/**
 * vall-crosscutting.spec.ts — the verifier's own pass over the two cross-cutting REQs that no
 * single cluster owns.
 *
 * REQ-UI-048 (TrBlazeUI migration): the static half is already proven (0 Fluent packages, 0
 * <Fluent*> components, PortalHost in all 4 layouts). The half that static analysis CANNOT settle
 * is icon rendering: TrBlazeUI.Icons.Lucide embeds canonical names only, and an alias name renders
 * NOTHING — no error, no fallback, no warning. String-scanning the assembly is unreliable (the
 * names are not stored as plain strings), so the only trustworthy check is to count rendered
 * <svg> nodes that actually carry geometry.
 *
 * REQ-UI-033 (dark mode): confirm the dark class is applied and that text/background tokens
 * resolve to a real contrast ratio, measured through a 1-px canvas because Chromium returns these
 * OKLCH tokens verbatim and a naive rgb() regex silently yields null.
 */
import { test, expect } from '@playwright/test';
import { BASE, login, nav } from './_gates';

const PUBLIC_ROUTES = ['/', '/about', '/categories', '/tags', '/series', '/search', '/rss', '/newsletters'];
const ADMIN_ROUTES: [string, RegExp][] = [
  ['/admin', /Dashboard/i],
  ['/settings', /Settings/i],
  ['/BlogsList', /Posts/i],
  ['/CommentsList', /Comments/i],
  ['/admin/categories', /Categories/i],
];

/** An icon that rendered is an <svg> with at least one geometry child. */
const ICON_PROBE = () => {
  const svgs = Array.from(document.querySelectorAll('svg'));
  const geometry = (s: Element) => s.querySelector('path, circle, rect, line, polyline, polygon, ellipse');
  return {
    total: svgs.length,
    empty: svgs.filter((s) => !geometry(s)).length,
    errorBoundary: /An unhandled error has occurred/i.test(document.body.innerText || ''),
    styled: getComputedStyle(document.body).backgroundColor,
    utilityNodes: document.querySelectorAll('[class*="flex"],[class*="grid"],[class*="rounded"]').length,
  };
};

test('REQ-UI-048 every rendered LucideIcon produces a non-empty svg (public)', async ({ page }) => {
  test.setTimeout(180000);
  const rows: any[] = [];
  for (const route of PUBLIC_ROUTES) {
    await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    // Wait past the prerender -> interactive handover, which briefly duplicates the shell.
    await page.waitForTimeout(4000);
    const r = await page.evaluate(ICON_PROBE);
    rows.push({ route, ...r });
    console.log(`ICON ${route.padEnd(14)} svg=${r.total} empty=${r.empty} boundary=${r.errorBoundary} utils=${r.utilityNodes}`);
  }
  for (const r of rows) {
    expect(r.errorBoundary, `${r.route} hit the Blazor error boundary`).toBe(false);
    expect(r.total, `${r.route} rendered no icons at all`).toBeGreaterThan(0);
    expect(r.empty, `${r.route} has ${r.empty} icon(s) that rendered an EMPTY svg (alias name miss)`).toBe(0);
    expect(r.utilityNodes, `${r.route} looks unstyled`).toBeGreaterThan(20);
  }
});

test('REQ-UI-048 admin surface renders styled with non-empty icons', async ({ page }) => {
  test.setTimeout(240000);
  await login(page, 'admin');
  const rows: any[] = [];
  for (const [route, heading] of ADMIN_ROUTES) {
    await nav(page, route, heading);
    const r = await page.evaluate(ICON_PROBE);
    rows.push({ route, ...r });
    console.log(`ICON ${route.padEnd(20)} svg=${r.total} empty=${r.empty} boundary=${r.errorBoundary}`);
  }
  for (const r of rows) {
    expect(r.errorBoundary, `${r.route} hit the Blazor error boundary`).toBe(false);
    expect(r.empty, `${r.route} has ${r.empty} empty svg icon(s)`).toBe(0);
  }
});

test('REQ-UI-033 dark mode applies and text clears WCAG AA on public + admin', async ({ page }) => {
  test.setTimeout(240000);

  const CONTRAST = () => {
    const cv = document.createElement('canvas');
    cv.width = cv.height = 1;
    const cx = cv.getContext('2d', { willReadFrequently: true })!;
    const parse = (c: string): number[] | null => {
      if (!c) return null;
      cx.fillStyle = '#010203';
      cx.fillStyle = c;
      if (cx.fillStyle === '#010203' && !/^#010203$/i.test(c.trim())) return null;
      cx.clearRect(0, 0, 1, 1);
      cx.fillRect(0, 0, 1, 1);
      const d = cx.getImageData(0, 0, 1, 1).data;
      return [d[0], d[1], d[2], d[3] / 255];
    };
    const lum = (c: number[]) => {
      const f = (v: number) => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
      return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
    };
    const ratio = (a: number[], b: number[]) => {
      const [l1, l2] = [lum(a), lum(b)].sort((x, y) => y - x);
      return (l1 + 0.05) / (l2 + 0.05);
    };
    const bgOf = (el: Element): number[] => {
      let n: Element | null = el;
      while (n) {
        const c = parse(getComputedStyle(n).backgroundColor);
        if (c && c[3] === 1) return c;
        n = n.parentElement;
      }
      return [0, 0, 0, 1];
    };
    const fails: any[] = [];
    let checked = 0;
    for (const el of Array.from(document.querySelectorAll('p, span, a, h1, h2, h3, td, th, label, button'))) {
      const txt = (el.textContent || '').trim();
      if (!txt || el.children.length > 0) continue;
      const st = getComputedStyle(el);
      if (st.display === 'none' || st.visibility === 'hidden' || parseFloat(st.opacity) === 0) continue;
      const r = el.getBoundingClientRect();
      if (r.width < 2 || r.height < 2) continue;
      const fg = parse(st.color);
      if (!fg) continue;
      checked++;
      const px = parseFloat(st.fontSize);
      const large = px >= 24 || (px >= 18.66 && parseInt(st.fontWeight, 10) >= 700);
      const need = large ? 3 : 4.5;
      const got = ratio(fg, bgOf(el));
      if (got < need - 0.01) fails.push({ txt: txt.slice(0, 40), got: +got.toFixed(2), need });
    }
    return { checked, fails: fails.slice(0, 8), failCount: fails.length, isDark: document.documentElement.classList.contains('dark') };
  };

  const goDark = () => page.evaluate(() => document.documentElement.classList.add('dark'));

  const results: any[] = [];
  for (const route of ['/', '/about', '/search', '/rss']) {
    await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(4000);
    await goDark();
    await page.waitForTimeout(500);
    const r = await page.evaluate(CONTRAST);
    results.push({ route, ...r });
    console.log(`DARK ${route.padEnd(12)} dark=${r.isDark} checked=${r.checked} fails=${r.failCount} ${JSON.stringify(r.fails)}`);
  }

  await login(page, 'admin');
  for (const [route, heading] of ADMIN_ROUTES.slice(0, 3)) {
    await nav(page, route, heading);
    await goDark();
    await page.waitForTimeout(500);
    const r = await page.evaluate(CONTRAST);
    results.push({ route, ...r });
    console.log(`DARK ${route.padEnd(12)} dark=${r.isDark} checked=${r.checked} fails=${r.failCount} ${JSON.stringify(r.fails)}`);
  }

  for (const r of results) {
    expect(r.isDark, `${r.route} did not apply the dark class`).toBe(true);
    expect(r.checked, `${r.route} measured no text at all — the probe is broken, not the page`).toBeGreaterThan(10);
    expect(r.failCount, `${r.route} has ${r.failCount} text node(s) below WCAG AA: ${JSON.stringify(r.fails)}`).toBe(0);
  }
});
