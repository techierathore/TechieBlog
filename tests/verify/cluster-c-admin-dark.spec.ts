/**
 * cluster-c-admin-dark.spec.ts — REQ-UI-033 admin-surface dark-mode audit.
 *
 * The broad sweep navigated admin routes with Blazor.navigateTo and sometimes measured the
 * PREVIOUS screen, because the URL changes before the new page's content does. Here each
 * navigation waits for the destination's OWN heading before anything is measured, so a row that
 * says "Categories Management" really is that screen.
 */
import { test, Page, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'https://localhost:7373';
const OUT = 'test-results/cluster-c';
const THEMES = ['trblaze-modern', 'developer', 'minimal'] as const;
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

/** route -> the heading that proves THAT screen is on-screen. */
const ADMIN: [string, string, RegExp][] = [
  ['admin', '/admin', /Dashboard/i],
  ['settings', '/settings', /Settings/i],
  ['blogslist', '/BlogsList', /All Posts|Posts/i],
  ['commentslist', '/CommentsList', /Comments Management|Comments/i],
  ['admincategories', '/admin/categories', /Categories Management|Categories/i],
];

test.use({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
test.describe.configure({ mode: 'serial' });

// Reuses the audit from the main sweep by re-declaring it here; kept in sync deliberately so this
// file can run standalone.
const AUDIT = () => {
  const cv = document.createElement('canvas');
  cv.width = cv.height = 1;
  const cx = cv.getContext('2d', { willReadFrequently: true })!;
  const cache = new Map<string, number[] | null>();
  const parse = (c: string): number[] | null => {
    if (!c) return null;
    if (cache.has(c)) return cache.get(c)!;
    cx.fillStyle = '#010203';
    cx.fillStyle = c;
    const ok = cx.fillStyle !== '#010203' || /^#010203$/i.test(c.trim());
    let out: number[] | null = null;
    if (ok) {
      cx.clearRect(0, 0, 1, 1);
      cx.fillRect(0, 0, 1, 1);
      const d = cx.getImageData(0, 0, 1, 1).data;
      out = [d[0], d[1], d[2], d[3] / 255];
    }
    cache.set(c, out);
    return out;
  };
  const over = (f: number[], b: number[]) => {
    const a = f[3];
    return [f[0] * a + b[0] * (1 - a), f[1] * a + b[1] * (1 - a), f[2] * a + b[2] * (1 - a), 1];
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
    const base = parse(getComputedStyle(document.documentElement).backgroundColor);
    let acc = base && base[3] === 1 ? base : [255, 255, 255, 1];
    for (let i = stack.length - 1; i >= 0; i--) acc = over(stack[i], acc);
    return acc;
  };
  const visible = (el: Element) => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 1 && r.height > 1 && s.visibility !== 'hidden' && s.display !== 'none' && parseFloat(s.opacity) > 0.1;
  };
  const label = (el: Element) => {
    const id = el.getAttribute('data-testid');
    return el.tagName.toLowerCase() + (id ? `[${id}]` : `.${String(el.className).trim().split(/\s+/).slice(0, 2).join('.')}`);
  };

  const textFails: any[] = [];
  const ctrlFails: any[] = [];
  const seen = new Set<string>();

  document.querySelectorAll('*').forEach((el) => {
    if (!visible(el)) return;
    const s = getComputedStyle(el);
    const own = Array.from(el.childNodes)
      .filter((n) => n.nodeType === 3)
      .map((n) => (n.textContent ?? '').trim())
      .join(' ')
      .trim();
    if (own && !el.closest('[aria-hidden="true"]')) {
      const fg = parse(s.color);
      if (fg) {
        const bg = bgOf(el);
        const eff = fg[3] < 1 ? over(fg, bg) : fg;
        const size = parseFloat(s.fontSize);
        const weight = parseInt(s.fontWeight) || 400;
        const need = size >= 24 || (size >= 18.66 && weight >= 700) ? 3 : 4.5;
        const r = ratio(eff, bg);
        if (r < need) {
          const k = `T|${label(el)}|${r.toFixed(2)}`;
          if (!seen.has(k)) {
            seen.add(k);
            textFails.push({
              sel: label(el),
              text: own.slice(0, 60),
              fg: s.color,
              bg: `rgb(${bg.slice(0, 3).map(Math.round).join(',')})`,
              px: size,
              need,
              ratio: +r.toFixed(2),
            });
          }
        }
      }
    }

    const tag = el.tagName.toLowerCase();
    const role = el.getAttribute('role');
    const isCtrl =
      ['input', 'textarea', 'select'].includes(tag) ||
      ['checkbox', 'switch', 'radio', 'combobox', 'textbox'].includes(role ?? '');
    if (!isCtrl) return;
    for (let n: Element | null = el; n; n = n.parentElement) {
      const ns = getComputedStyle(n);
      if (ns.clipPath !== 'none') return;
      const nr = n.getBoundingClientRect();
      if (ns.overflow === 'hidden' && (nr.width <= 2 || nr.height <= 2)) return;
    }
    if (el.closest('[aria-hidden="true"]')) return;

    let owner: Element = el;
    for (let n: Element | null = el, h = 0; n && h < 4; n = n.parentElement, h++) {
      const ns = getComputedStyle(n);
      const w = parseFloat(ns.borderTopWidth) || 0;
      const c = parse(ns.borderTopColor);
      if (w > 0 && c && c[3] > 0) {
        owner = n;
        break;
      }
    }
    const os = getComputedStyle(owner);
    const bw = parseFloat(os.borderTopWidth) || 0;
    const bc = parse(os.borderTopColor);
    const bg = bgOf(owner.parentElement ?? owner);
    if (bw > 0 && bc && bc[3] > 0) {
      const r = ratio(over(bc, bg), bg);
      if (r < 3) {
        const k = `C|${label(el)}|${r.toFixed(2)}`;
        if (!seen.has(k)) {
          seen.add(k);
          ctrlFails.push({ sel: label(el), kind: 'border', color: os.borderTopColor, ratio: +r.toFixed(2) });
        }
      }
    } else {
      const f = parse(s.backgroundColor);
      if (f) {
        const r = ratio(f[3] < 1 ? over(f, bg) : f, bg);
        if (r < 3) {
          const k = `C|${label(el)}|fill|${r.toFixed(2)}`;
          if (!seen.has(k)) {
            seen.add(k);
            ctrlFails.push({ sel: label(el), kind: 'fill(no border)', color: s.backgroundColor, ratio: +r.toFixed(2) });
          }
        }
      }
    }
  });
  return { textFails, ctrlFails };
};

async function login(page: Page) {
  const ws = page.waitForEvent('websocket', { timeout: 40000 }).catch(() => null);
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await ws;
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2500);
}

for (const theme of THEMES) {
  for (const mode of ['dark', 'light'] as const) {
    test(`admin ${theme} / ${mode}`, async ({ page }) => {
      test.setTimeout(10 * 60 * 1000);
      fs.mkdirSync(OUT, { recursive: true });
      const rows: any[] = [];

      await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
      await login(page);

      for (const [name, href, heading] of ADMIN) {
        await page.evaluate((p) => (window as any).Blazor.navigateTo(p), href);
        // The identity gate: wait for THIS screen's heading, not merely for the URL.
        await expect(page.locator('h1, h2').filter({ hasText: heading }).first()).toBeVisible({ timeout: 45000 });
        await page.waitForFunction(() => !/Loading\b/i.test(document.body.innerText || ''), { timeout: 45000 }).catch(() => {});
        // Theme is applied the same way ThemeProvider applies it, so the circuit survives.
        await page.evaluate(
          ([t, d]) => {
            localStorage.setItem('techieblog-theme', JSON.stringify(t));
            localStorage.setItem('techieblog-dark-mode', d === 'dark' ? 'true' : 'false');
            (window as any).setThemeAttributes(t, d === 'dark');
          },
          [theme, mode]
        );
        await page.waitForTimeout(900);

        const res = await page.evaluate(AUDIT);
        const info = await page.evaluate(() => ({
          h1: (document.querySelector('h1')?.textContent ?? '').replace(/\s+/g, ' ').trim().slice(0, 50),
          url: location.pathname,
          dark: document.documentElement.classList.contains('dark'),
          siteTheme: document.documentElement.getAttribute('data-site-theme'),
          chars: (document.body.innerText ?? '').trim().length,
          testids: document.querySelectorAll('[data-testid]').length,
          hScroll: Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth),
        }));
        rows.push({ theme, mode, name, ...info, textFails: res.textFails, ctrlFails: res.ctrlFails });
        if (mode === 'dark') await page.screenshot({ path: path.join(OUT, `admin-${theme}-${name}-dark.png`) });
        fs.mkdirSync(OUT, { recursive: true });
        fs.writeFileSync(path.join(OUT, `admin-${theme}-${mode}.json`), JSON.stringify(rows, null, 2));
      }

      for (const r of rows) {
        console.log(
          `[${r.theme}/${r.mode}] ${r.name} url=${r.url} h1="${r.h1}" dark=${r.dark} theme=${r.siteTheme} chars=${r.chars} ids=${r.testids} T=${r.textFails.length} C=${r.ctrlFails.length} hScroll=${r.hScroll}`
        );
        r.textFails.forEach((f: any) => console.log(`   TEXT ${f.ratio}:1 (need ${f.need}) ${f.sel} fg=${f.fg} bg=${f.bg} "${f.text}"`));
        r.ctrlFails.forEach((f: any) => console.log(`   CTRL ${f.ratio}:1 ${f.sel} ${f.kind}=${f.color}`));
      }
    });
  }
}
