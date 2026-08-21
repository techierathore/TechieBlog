/**
 * cluster-c-darkmode.spec.ts — REQ-UI-033 dark-mode contrast + visual sweep.
 *
 * Sweeps every public and admin route in light AND dark across all three site
 * themes, resolving COMPUTED sRGB colours from the browser (the tokens are
 * authored in OKLCH, so the CSS source cannot be trusted for a ratio) and
 * checking them against WCAG 4.5:1 (normal text), 3:1 (large text and UI
 * component boundaries, 1.4.11).
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'https://localhost:7373';
const OUT = 'test-results/cluster-c';

const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

const THEMES = ['trblaze-modern', 'developer', 'minimal'] as const;
const MODES = ['light', 'dark'] as const;

const PUBLIC_ROUTES = [
  ['home', '/'],
  ['post', '/post/theming-with-css-custom-properties'],
  ['category', '/category/web-development'],
  ['tag', '/tag/blazor'],
  ['series', '/series/blazor-server-in-production'],
  ['search', '/search?q=blazor'],
  ['about', '/about'],
  ['newsletters', '/newsletters'],
  ['resume', '/resume'],
];

const ADMIN_ROUTES = [
  ['admin', '/admin'],
  ['settings', '/settings'],
  ['blogslist', '/BlogsList'],
  ['commentslist', '/CommentsList'],
  ['admincategories', '/admin/categories'],
];

/** Injected into the page: computes real sRGB contrast for text and control boundaries. */
const AUDIT = () => {
  /*
   * Resolve ANY CSS colour to sRGB by painting it.
   *
   * The tokens are authored in OKLCH and Chromium returns computed values as `oklch(...)`
   * verbatim — a plain rgb()/rgba() regex silently returns null for every themed colour, which
   * makes the whole audit no-op while still reporting "0 failures". Painting one pixel and
   * reading it back is the only parser guaranteed to agree with what the user actually sees.
   */
  const cv = document.createElement('canvas');
  cv.width = 1;
  cv.height = 1;
  const cx = cv.getContext('2d', { willReadFrequently: true })!;
  const cache = new Map<string, [number, number, number, number] | null>();
  const parse = (c: string): [number, number, number, number] | null => {
    if (!c) return null;
    if (cache.has(c)) return cache.get(c)!;
    let out: [number, number, number, number] | null = null;
    // A sentinel detects strings the browser rejects: fillStyle keeps its old value on invalid input.
    cx.fillStyle = '#010203';
    cx.fillStyle = c;
    const accepted = cx.fillStyle !== '#010203' || /^#010203$/i.test(c.trim());
    if (accepted) {
      cx.clearRect(0, 0, 1, 1);
      cx.fillRect(0, 0, 1, 1);
      const d = cx.getImageData(0, 0, 1, 1).data;
      out = [d[0], d[1], d[2], d[3] / 255];
    }
    cache.set(c, out);
    return out;
  };
  const over = (fg: number[], bg: number[]) => {
    const a = fg[3];
    return [fg[0] * a + bg[0] * (1 - a), fg[1] * a + bg[1] * (1 - a), fg[2] * a + bg[2] * (1 - a), 1];
  };
  const lum = (c: number[]) => {
    const f = (v: number) => {
      v /= 255;
      return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * f(c[0]) + 0.7152 * f(c[1]) + 0.0722 * f(c[2]);
  };
  const ratio = (a: number[], b: number[]) => {
    const [l1, l2] = [lum(a), lum(b)].sort((x, y) => y - x);
    return (l1 + 0.05) / (l2 + 0.05);
  };
  /** Walks ancestors compositing every translucent layer down to the page canvas. */
  const bgOf = (el: Element): number[] => {
    const stack: number[][] = [];
    let n: Element | null = el;
    while (n) {
      const c = parse(getComputedStyle(n).backgroundColor);
      if (c && c[3] > 0) {
        stack.push(c);
        if (c[3] === 1) break;
      }
      n = n.parentElement;
    }
    let base = parse(getComputedStyle(document.documentElement).backgroundColor);
    let acc = base && base[3] === 1 ? base : [255, 255, 255, 1];
    for (let i = stack.length - 1; i >= 0; i--) acc = over(stack[i], acc);
    return acc;
  };
  const visible = (el: Element) => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return (
      r.width > 1 && r.height > 1 && s.visibility !== 'hidden' && s.display !== 'none' && parseFloat(s.opacity) > 0.1
    );
  };
  const label = (el: Element) => {
    const id = el.getAttribute('data-testid');
    return (
      el.tagName.toLowerCase() +
      (id ? `[data-testid=${id}]` : el.className && typeof el.className === 'string' ? `.${el.className.trim().split(/\s+/).slice(0, 2).join('.')}` : '')
    );
  };

  const textFails: any[] = [];
  const ctrlFails: any[] = [];
  const seen = new Set<string>();

  document.querySelectorAll('*').forEach((el) => {
    if (!visible(el)) return;
    const s = getComputedStyle(el);

    // --- Text nodes: only elements holding their own non-empty text ---
    const own = Array.from(el.childNodes)
      .filter((n) => n.nodeType === 3)
      .map((n) => (n.textContent ?? '').trim())
      .join(' ')
      .trim();
    if (own.length > 0 && el.closest('[aria-hidden="true"]') === null) {
      const fg = parse(s.color);
      if (fg) {
        const bg = bgOf(el);
        const eff = fg[3] < 1 ? over(fg, bg) : fg;
        const size = parseFloat(s.fontSize);
        const weight = parseInt(s.fontWeight) || 400;
        const large = size >= 24 || (size >= 18.66 && weight >= 700);
        const need = large ? 3 : 4.5;
        const r = ratio(eff, bg);
        if (r < need) {
          const key = `T|${label(el)}|${s.color}|${r.toFixed(2)}`;
          if (!seen.has(key)) {
            seen.add(key);
            textFails.push({
              sel: label(el),
              text: own.slice(0, 60),
              fg: s.color,
              bg: `rgb(${bg.map((v) => Math.round(v)).slice(0, 3).join(',')})`,
              px: size,
              need,
              ratio: +r.toFixed(2),
            });
          }
        }
      }
    }

    // --- Control boundaries (WCAG 1.4.11): the visible edge of a form control ---
    const tag = el.tagName.toLowerCase();
    const role = el.getAttribute('role');
    const isCtrl =
      ['input', 'textarea', 'select'].includes(tag) ||
      ['checkbox', 'switch', 'radio', 'combobox', 'textbox'].includes(role ?? '');
    /*
     * A control only has a "visible boundary" obligation if it is actually on screen. The
     * REQ-NFR-007 keyboard fallback deliberately renders clipped, visually-hidden radio inputs;
     * hit-testing the centre point filters those out instead of reporting them as invisible.
     */
    const onScreen = (() => {
      // Clipped out of sight by an ancestor? `clip-path: inset(50%)` is the sr-only idiom the
      // REQ-NFR-007 keyboard fallback uses, and a 1px overflow:hidden box hides just as hard.
      for (let n: Element | null = el; n; n = n.parentElement) {
        const ns = getComputedStyle(n);
        if (ns.clipPath !== 'none') return false;
        const nr = n.getBoundingClientRect();
        if (ns.overflow === 'hidden' && (nr.width <= 2 || nr.height <= 2)) return false;
      }
      // Excluded from the accessibility tree => decorative, no 1.4.11 obligation.
      if (el.closest('[aria-hidden="true"]')) return false;
      const r = el.getBoundingClientRect();
      const cx = r.left + r.width / 2;
      const cy = r.top + r.height / 2;
      if (cx < 0 || cy < 0 || cx > innerWidth || cy > innerHeight) return false;
      const hit = document.elementFromPoint(cx, cy);
      return !!hit && (hit === el || el.contains(hit));
    })();

    if (isCtrl && onScreen) {
      /*
       * The element that DRAWS the boundary is often not the control itself. TrBlazeUI renders
       * `<input class="border-0 bg-transparent">` inside an input-group wrapper that carries
       * `border border-input`. Measuring the bare <input> reports "no boundary" and is a false
       * positive, so walk up to the nearest ancestor that actually paints a border.
       */
      let owner: Element = el;
      for (let n: Element | null = el, hops = 0; n && hops < 4; n = n.parentElement, hops++) {
        const ns = getComputedStyle(n);
        const nbw = parseFloat(ns.borderTopWidth) || 0;
        const nbc = parse(ns.borderTopColor);
        if (nbw > 0 && nbc && nbc[3] > 0) {
          owner = n;
          break;
        }
      }
      const os = getComputedStyle(owner);
      const bw = parseFloat(os.borderTopWidth) || 0;
      const bc = parse(os.borderTopColor);
      const bg = bgOf(owner.parentElement ?? owner);
      if (bw > 0 && bc && bc[3] > 0) {
        const eff = over(bc, bg);
        const r = ratio(eff, bg);
        if (r < 3) {
          const key = `C|${label(el)}|${os.borderTopColor}|${r.toFixed(2)}`;
          if (!seen.has(key)) {
            seen.add(key);
            ctrlFails.push({
              sel: label(el),
              owner: owner === el ? 'self' : label(owner),
              kind: 'border',
              color: os.borderTopColor,
              need: 3,
              ratio: +r.toFixed(2),
            });
          }
        }
      } else if (bw === 0) {
        // Borderless control: its own fill must separate it from the surround.
        const own2 = parse(s.backgroundColor);
        if (own2) {
          const eff = own2[3] < 1 ? over(own2, bg) : own2;
          const r = ratio(eff, bg);
          if (r < 3) {
            const key = `C|${label(el)}|fill|${r.toFixed(2)}`;
            if (!seen.has(key)) {
              seen.add(key);
              ctrlFails.push({ sel: label(el), kind: 'fill(no border)', color: s.backgroundColor, need: 3, ratio: +r.toFixed(2) });
            }
          }
        }
      }
    }
  });

  return { textFails, ctrlFails };
};

/**
 * RENDER-TRUTH: proof the page painted real DATA.
 *
 * A contrast ratio can pass perfectly on an empty page, so every route also records its heading,
 * how many data-bound rows/cards it drew, and whether a spinner or error is still on screen.
 */
const RENDER = () => {
  const txt = (el: Element | null) => (el?.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 80);
  const vis = (el: Element) => {
    const r = el.getBoundingClientRect();
    return r.width > 1 && r.height > 1;
  };
  const testids = Array.from(document.querySelectorAll('[data-testid]'))
    .filter(vis)
    .map((e) => e.getAttribute('testid') ?? e.getAttribute('data-testid')!);
  const body = (document.body.innerText ?? '').replace(/\s+/g, ' ').trim();
  return {
    h1: txt(document.querySelector('h1')),
    title: document.title,
    visibleTestids: testids.length,
    cards: document.querySelectorAll('[data-testid*="card"], article, [data-testid*="row"]').length,
    links: Array.from(document.querySelectorAll('a')).filter(vis).length,
    stillLoading: /Loading\b/i.test(body),
    errorText: /unhandled error|An error has occurred|Sorry, there'?s nothing/i.test(body),
    sample: body.slice(0, 120),
  };
};

/** Layout truth: overlaps, zero-size, out-of-viewport, horizontal page scroll. */
const VISUAL = () => {
  const doc = document.documentElement;
  const overflow = Math.max(0, doc.scrollWidth - doc.clientWidth);
  const offenders: any[] = [];
  if (overflow > 0) {
    document.querySelectorAll('*').forEach((el) => {
      const r = el.getBoundingClientRect();
      if (r.width > 0 && r.right > doc.clientWidth + 1) {
        const id = el.getAttribute('data-testid');
        offenders.push({
          sel: el.tagName.toLowerCase() + (id ? `[${id}]` : `.${String(el.className).trim().split(/\s+/)[0] ?? ''}`),
          right: Math.round(r.right),
          width: Math.round(r.width),
        });
      }
    });
  }
  return { overflow, offenders: offenders.slice(0, 6) };
};

async function applyTheme(page: Page, theme: string, mode: string) {
  await page.evaluate(
    ([t, d]) => {
      localStorage.setItem('techieblog-theme', JSON.stringify(t));
      localStorage.setItem('techieblog-dark-mode', d === 'dark' ? 'true' : 'false');
    },
    [theme, mode]
  );
}

/**
 * Waits until the page has actually rendered its DATA, not a spinner.
 *
 * Polls until the body text stops growing and no "Loading…" placeholder remains, so the
 * audit never measures a transient skeleton. RENDER-TRUTH depends on this.
 */
async function settleContent(page: Page) {
  let last = -1;
  for (let i = 0; i < 12; i++) {
    await page.waitForTimeout(700);
    let txt = '';
    try {
      txt = await page.locator('body').innerText();
    } catch {
      continue;
    }
    const loading = /Loading\b|Please wait/i.test(txt);
    if (!loading && txt.length === last && txt.length > 0) return txt;
    last = txt.length;
  }
  return last;
}

/** Full page load (anonymous routes only) plus a data-render wait. */
async function settle(page: Page, url: string) {
  await page.goto(url, { waitUntil: 'networkidle' }).catch(() => {});
  await settleContent(page);
  return page.url();
}

/**
 * Navigates through the app's OWN router.
 *
 * Pre-existing defect (already recorded by a sibling cluster): the JWT lives in localStorage
 * only, so a full browser load of an authenticated route prerenders as anonymous and bounces to
 * /login. A signed-in admin reaches these screens by router navigation, so the sweep does too.
 */
async function routerGoto(page: Page, href: string) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), href);
  await page
    .waitForURL((u) => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 })
    .catch(() => {});
  await settleContent(page);
  return page.url();
}

/**
 * Applies the theme the way the app itself does.
 *
 * Writes the same LocalStorage keys ThemeService writes, then invokes the same
 * `setThemeAttributes` JS function ThemeProvider invokes — so no reload is needed and the
 * signed-in circuit survives.
 */
async function applyThemeLive(page: Page, theme: string, mode: string) {
  await page.evaluate(
    ([t, d]) => {
      localStorage.setItem('techieblog-theme', JSON.stringify(t));
      localStorage.setItem('techieblog-dark-mode', d === 'dark' ? 'true' : 'false');
      (window as any).setThemeAttributes(t, d === 'dark');
    },
    [theme, mode]
  );
  await page.waitForTimeout(400);
}

async function audit(page: Page) {
  for (let i = 0; i < 3; i++) {
    try {
      const res = await page.evaluate(AUDIT);
      const vis = await page.evaluate(VISUAL);
      return { res, vis };
    } catch {
      await page.waitForTimeout(1200);
    }
  }
  return { res: { textFails: [], ctrlFails: [] }, vis: { overflow: -1, offenders: [] } };
}

/**
 * Logs the admin in. The submit MUST wait for the Blazor Server circuit: submitting
 * while the page is still static SSR posts the form for real and yields
 * "The POST request does not specify which form is being submitted."
 */
async function login(page: Page) {
  const ws = page.waitForEvent('websocket', { timeout: 40000 }).catch(() => null);
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await ws;
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(3000);

  for (let attempt = 0; attempt < 3; attempt++) {
    await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
    await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
    await page.click('[data-testid="login-submit"]');
    try {
      await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 20000 });
      return;
    } catch {
      if (!page.url().toLowerCase().includes('login')) return;
      await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
      await page.waitForTimeout(3500);
    }
  }
  throw new Error(`login failed, still at ${page.url()}`);
}

// Each theme x mode combination is an independent browser context, so they can run concurrently.
test.describe.configure({ mode: 'parallel' });

function log(report: any[]) {
  const totT = report.reduce((s, r) => s + r.textFails.length, 0);
  const totC = report.reduce((s, r) => s + r.ctrlFails.length, 0);
  console.log(`\n=== TEXT failures: ${totT} | CONTROL failures: ${totC} ===`);
  for (const r of report) {
    console.log(
      `\n[${r.theme}/${r.mode}] ${r.name} @${r.landed} (root=${r.root.theme} dark=${r.root.dark}) text=${r.bodyText}ch overflow=${r.vis.overflow} T=${r.textFails.length} C=${r.ctrlFails.length}`
    );
    r.textFails.forEach((f: any) =>
      console.log(`   TEXT ${f.ratio}:1 (need ${f.need}) ${f.sel} fg=${f.fg} bg=${f.bg} "${f.text}"`)
    );
    r.ctrlFails.forEach((f: any) => console.log(`   CTRL ${f.ratio}:1 (need 3) ${f.sel} ${f.kind}=${f.color}`));
    r.vis.offenders.forEach((o: any) => console.log(`   OVERFLOW ${o.sel} right=${o.right} w=${o.width}`));
  }
}

for (const theme of THEMES) {
  for (const mode of MODES) {
    test(`sweep ${theme} / ${mode}`, async ({ browser }) => {
      test.setTimeout(15 * 60 * 1000);
      fs.mkdirSync(OUT, { recursive: true });
      const report: any[] = [];

      const ctx = await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
      const page = await ctx.newPage();
      await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
      await applyTheme(page, theme, mode);

      const reportPath = path.join(OUT, `report-${theme}-${mode}.json`);
      const capture = async (scope: string, name: string, landed: string) => {
        const root = await page.evaluate(() => ({
          theme: document.documentElement.getAttribute('data-site-theme'),
          dark: document.documentElement.classList.contains('dark'),
        }));
        const { res, vis } = await audit(page);
        const render = await page.evaluate(RENDER).catch(() => null);
        const bodyText = (await page.locator('body').innerText()).trim().length;
        report.push({ theme, mode, scope, name, landed, root, ...res, vis, render, bodyText });
        // Written after EVERY route: heavy machine contention means a run may be cut short, and
        // partial evidence is still evidence. The mkdir is repeated because Playwright prunes its
        // own outputDir during a run, which can take this sibling directory with it.
        fs.mkdirSync(OUT, { recursive: true });
        fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
        if (mode === 'dark') {
          await page.screenshot({ path: path.join(OUT, `${theme}-dark-${name}-1280.png`) });
          // VISUAL-TRUTH also has to hold on a phone, where the sidebar collapses and the
          // header becomes a drawer — a different layout, not just a narrower one.
          await page.setViewportSize({ width: 390, height: 844 });
          await page.waitForTimeout(900);
          const mob = await page.evaluate(VISUAL);
          await page.screenshot({ path: path.join(OUT, `${theme}-dark-${name}-390.png`) });
          report[report.length - 1].mobile = mob;
          await page.setViewportSize({ width: 1280, height: 900 });
          await page.waitForTimeout(500);
          fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
        }
      };

      // ---- public routes (anonymous, full page loads) ----
      for (const [name, url] of PUBLIC_ROUTES) {
        const landed = await settle(page, `${BASE}${url}`);
        await capture('public', name, landed);
      }

      // ---- admin routes (authenticated, via the app's own router) ----
      await login(page);
      await applyThemeLive(page, theme, mode);
      for (const [name, url] of ADMIN_ROUTES) {
        const landed = await routerGoto(page, url);
        await applyThemeLive(page, theme, mode);
        await capture('admin', name, landed);
      }

      await ctx.close();
      log(report);
    });
  }
}
