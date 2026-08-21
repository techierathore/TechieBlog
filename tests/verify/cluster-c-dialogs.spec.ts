/**
 * cluster-c-dialogs.spec.ts — REQ-UI-033: the admin dialogs and checkboxes Stories 7.1-7.4 named.
 *
 * A dialog is portalled and only exists once opened, so the broad page sweep never measured one.
 * This opens the category dialog and the comments-list checkboxes in dark mode in all 3 themes and
 * audits what is actually on screen.
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'https://localhost:7373';
const OUT = 'test-results/cluster-c';
const THEMES = ['trblaze-modern', 'developer', 'minimal'] as const;

test.use({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
test.describe.configure({ mode: 'serial' });

/** Contrast audit scoped to a subtree (the open dialog), so page chrome behind it is ignored. */
const AUDIT_IN = (rootSel: string) => {
  const root = document.querySelector(rootSel);
  if (!root) return { missing: true, textFails: [], ctrlFails: [] };
  const cv = document.createElement('canvas');
  cv.width = cv.height = 1;
  const cx = cv.getContext('2d', { willReadFrequently: true })!;
  const px = (c: string): number[] | null => {
    if (!c) return null;
    cx.fillStyle = '#010203';
    cx.fillStyle = c;
    cx.clearRect(0, 0, 1, 1);
    cx.fillRect(0, 0, 1, 1);
    const d = cx.getImageData(0, 0, 1, 1).data;
    return [d[0], d[1], d[2], d[3] / 255];
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
    for (let n: Element | null = el; n; n = n.parentElement) {
      const c = px(getComputedStyle(n).backgroundColor);
      if (c && c[3] > 0) {
        stack.push(c);
        if (c[3] === 1) break;
      }
    }
    const base = px(getComputedStyle(document.documentElement).backgroundColor);
    let acc = base && base[3] === 1 ? base : [255, 255, 255, 1];
    for (let i = stack.length - 1; i >= 0; i--) acc = over(stack[i], acc);
    return acc;
  };
  const vis = (el: Element) => {
    const r = el.getBoundingClientRect();
    const s = getComputedStyle(el);
    return r.width > 1 && r.height > 1 && s.visibility !== 'hidden' && s.display !== 'none';
  };
  const nm = (el: Element) =>
    el.tagName.toLowerCase() + (el.getAttribute('data-testid') ? `[${el.getAttribute('data-testid')}]` : '');

  const textFails: any[] = [];
  const ctrlFails: any[] = [];
  let texts = 0;
  let ctrls = 0;

  root.querySelectorAll('*').forEach((el) => {
    if (!vis(el)) return;
    const s = getComputedStyle(el);
    const own = Array.from(el.childNodes)
      .filter((n) => n.nodeType === 3)
      .map((n) => (n.textContent ?? '').trim())
      .join(' ')
      .trim();
    if (own && !el.closest('[aria-hidden="true"]')) {
      const fg = px(s.color);
      if (fg) {
        texts++;
        const bg = bgOf(el);
        const size = parseFloat(s.fontSize);
        const weight = parseInt(s.fontWeight) || 400;
        const need = size >= 24 || (size >= 18.66 && weight >= 700) ? 3 : 4.5;
        const r = ratio(fg[3] < 1 ? over(fg, bg) : fg, bg);
        if (r < need)
          textFails.push({ sel: nm(el), text: own.slice(0, 50), ratio: +r.toFixed(2), need, fg: s.color });
      }
    }
    const tag = el.tagName.toLowerCase();
    const role = el.getAttribute('role');
    if (!['input', 'textarea', 'select'].includes(tag) && !['checkbox', 'switch', 'radio'].includes(role ?? '')) return;
    for (let n: Element | null = el; n; n = n.parentElement) if (getComputedStyle(n).clipPath !== 'none') return;
    if (el.closest('[aria-hidden="true"]')) return;
    ctrls++;
    let owner: Element = el;
    for (let n: Element | null = el, h = 0; n && h < 4; n = n.parentElement, h++) {
      const ns = getComputedStyle(n);
      const w = parseFloat(ns.borderTopWidth) || 0;
      const c = px(ns.borderTopColor);
      if (w > 0 && c && c[3] > 0) {
        owner = n;
        break;
      }
    }
    const os = getComputedStyle(owner);
    const bw = parseFloat(os.borderTopWidth) || 0;
    const bc = px(os.borderTopColor);
    const bg = bgOf(owner.parentElement ?? owner);
    if (bw > 0 && bc && bc[3] > 0) {
      const r = ratio(over(bc, bg), bg);
      if (r < 3) ctrlFails.push({ sel: nm(el), kind: 'border', color: os.borderTopColor, ratio: +r.toFixed(2) });
    } else {
      const f = px(s.backgroundColor);
      if (f) {
        const r = ratio(f[3] < 1 ? over(f, bg) : f, bg);
        if (r < 3) ctrlFails.push({ sel: nm(el), kind: 'fill', color: s.backgroundColor, ratio: +r.toFixed(2) });
      }
    }
  });
  return { missing: false, textNodes: texts, controls: ctrls, textFails, ctrlFails };
};

async function login(page: Page) {
  const ws = page.waitForEvent('websocket', { timeout: 40000 }).catch(() => null);
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await ws;
  await page.waitForTimeout(3500);
  await page.fill('[data-testid="login-email"]', 'Ravi@techieblog.com');
  await page.fill('[data-testid="login-password"]', 'admin_password');
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2500);
}

for (const theme of THEMES) {
  test(`admin dialog + checkbox dark — ${theme}`, async ({ page }) => {
    test.setTimeout(8 * 60 * 1000);
    fs.mkdirSync(OUT, { recursive: true });
    const results: any[] = [];

    await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    await login(page);
    await page.evaluate(
      (t) => {
        localStorage.setItem('techieblog-theme', JSON.stringify(t));
        localStorage.setItem('techieblog-dark-mode', 'true');
        (window as any).setThemeAttributes(t, true);
      },
      theme
    );

    // ---------- 1. Category dialog ----------
    await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/categories'));
    await expect(page.locator('h1', { hasText: /Categories/i })).toBeVisible({ timeout: 45000 });
    await page.waitForTimeout(1500);
    await page.evaluate(
      (t) => (window as any).setThemeAttributes(t, true),
      theme
    );

    /*
     * The delete-confirmation dialog is the right target: "Add New Category" is a <Button Href>,
     * i.e. an anchor that navigates, not a dialog trigger. This dialog also renders a warning
     * <Alert> inside it, so it exercises --alert-warning on a popover surface in dark mode.
     * It is CANCELLED, never confirmed — the sweep must not delete a seeded category.
     */
    await page.locator('[data-testid="category-delete"]').first().click();
    const dialog = page.locator('[data-testid="category-delete-dialog"]').first();
    await expect(dialog).toBeVisible({ timeout: 20000 });
    await page.waitForTimeout(1200);

    const dlg = await page.evaluate(AUDIT_IN, '[data-testid="category-delete-dialog"]');
    results.push({ what: 'category-delete-dialog', ...dlg });
    await page.screenshot({ path: path.join(OUT, `dialog-${theme}-category-dark.png`) });
    await page.locator('[data-testid="category-delete-cancel"]').first().click();
    await page.waitForTimeout(1200);

    // ---------- 2. Comments list checkboxes ----------
    await page.evaluate(() => (window as any).Blazor.navigateTo('/CommentsList'));
    await expect(page.locator('h1', { hasText: /Comments/i })).toBeVisible({ timeout: 45000 });
    await page.waitForTimeout(1800);
    await page.evaluate((t) => (window as any).setThemeAttributes(t, true), theme);
    await page.waitForTimeout(800);

    const boxes = page.locator('[role="checkbox"], input[type="checkbox"]');
    const boxCount = await boxes.count();
    // Tick the first one: a CHECKED box is the state most likely to vanish in dark mode.
    if (boxCount > 0) {
      await boxes.first().click();
      await page.waitForTimeout(900);
    }
    const cbAudit = await page.evaluate(AUDIT_IN, 'main, [data-testid="main-content"], body');
    const cbState = await page.evaluate(() => {
      const b = document.querySelector('[role="checkbox"], input[type="checkbox"]');
      if (!b) return null;
      const s = getComputedStyle(b);
      const r = b.getBoundingClientRect();
      return {
        checked: b.getAttribute('aria-checked') ?? (b as HTMLInputElement).checked,
        size: [Math.round(r.width), Math.round(r.height)],
        bg: s.backgroundColor,
        border: `${s.borderTopWidth} ${s.borderTopColor}`,
      };
    });
    results.push({ what: 'comments-checkboxes', boxCount, cbState, ...cbAudit });
    await page.screenshot({ path: path.join(OUT, `dialog-${theme}-checkbox-dark.png`) });

    fs.mkdirSync(OUT, { recursive: true });
    fs.writeFileSync(path.join(OUT, `dialogs-${theme}.json`), JSON.stringify(results, null, 2));

    for (const r of results) {
      console.log(
        `[${theme}] ${r.what}: missing=${r.missing} textNodes=${r.textNodes} controls=${r.controls} T=${r.textFails.length} C=${r.ctrlFails.length}` +
          (r.boxCount !== undefined ? ` boxes=${r.boxCount} state=${JSON.stringify(r.cbState)}` : '')
      );
      r.textFails.forEach((f: any) => console.log(`    TEXT ${f.ratio}:1 (need ${f.need}) ${f.sel} "${f.text}" fg=${f.fg}`));
      r.ctrlFails.forEach((f: any) => console.log(`    CTRL ${f.ratio}:1 ${f.sel} ${f.kind}=${f.color}`));
    }
    expect(results[0].missing, 'category dialog should have opened').toBeFalsy();
  });
}
