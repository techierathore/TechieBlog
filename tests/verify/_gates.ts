/**
 * _gates.ts — shared verifier helpers for the 2026-08-08 `*verify all` run.
 *
 * Every cluster spec imports these so the §4a data-render gate and the §4b visual-truth gate are
 * measured identically everywhere. Two hard-won rules are baked in:
 *   1. A full page load of an authorised route prerenders as anonymous (the JWT lives only in
 *      localStorage), so it bounces to /login. Authenticated navigation MUST go through
 *      Blazor.navigateTo — never page.goto.
 *   2. The URL changes before the destination renders, so every navigation is gated on the
 *      destination's OWN heading, otherwise the previous screen gets measured.
 */
import { Page, expect } from '@playwright/test';

export const BASE = process.env.TB_BASE ?? 'http://localhost:5399';

export const USERS = {
  admin: { email: 'Ravi@techieblog.com', password: 'admin_password', role: 'Admin' },
  editor: { email: 'editor@techieblog.test', password: 'Editor#Pass1', role: 'Editor' },
  author: { email: 'author@techieblog.test', password: 'Author#Pass1', role: 'Author' },
  contributor: { email: 'contributor@techieblog.test', password: 'Contrib#Pass1', role: 'Contributor' },
} as const;

export type RoleKey = keyof typeof USERS;

/**
 * Signs in through the real login form.
 *
 * A fixed wait is NOT safe here, and three separate clusters lost runs to it before this was
 * understood. `/login` is an `EditForm` under InteractiveServer: Blazor prerenders it as static
 * HTML with `<form method="post" action="/login">`, and the interactive re-render both drops the
 * `action` attribute and WIPES anything already typed. Click too early and the browser submits the
 * static form, the host answers HTTP 400 "The POST request does not specify which form is being
 * submitted", and the failure surfaces as a misleading `waitForURL` timeout that looks exactly
 * like a product defect. Under 7-way concurrency the handover was measured between 9.6s and ~18s.
 *
 * The two reliable signals are therefore: the form losing its `action`, and the input actually
 * holding the value it was given. `window.Blazor` exists ~1s after load and is NOT usable.
 */
export async function login(page: Page, role: RoleKey = 'admin') {
  const user = USERS[role];
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 60000 });

  // The circuit has attached once the prerendered form's `action` is gone.
  await page
    .waitForFunction(() => {
      const f = document.querySelector('form');
      return !!f && !f.hasAttribute('action');
    }, { timeout: 60000 })
    .catch(() => {});

  // Re-type until the value sticks: an early fill is discarded by the interactive re-render.
  const fillStable = async (selector: string, value: string) => {
    for (let i = 0; i < 12; i++) {
      await page.fill(selector, value);
      await page.waitForTimeout(500);
      if ((await page.inputValue(selector)) === value) return;
    }
    throw new Error(`field ${selector} would not hold its value — circuit never attached`);
  };
  await fillStable('[data-testid="login-email"]', user.email);
  await fillStable('[data-testid="login-password"]', user.password);

  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
  await page.waitForTimeout(2000);
  return page.url();
}

/** Authenticated navigation: keeps the circuit (and therefore the session) alive. */
export async function nav(page: Page, route: string, heading?: RegExp) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  if (heading) {
    await expect(page.locator('h1, h2').filter({ hasText: heading }).first())
      .toBeVisible({ timeout: 45000 });
  }
  await page.waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), { timeout: 30000 })
    .catch(() => {});
  await page.waitForTimeout(800);
}

export type RenderVerdict = 'RENDERS' | 'RENDER-EMPTY' | 'RENDER-ERROR' | 'UNREACHABLE';

export interface ControlResult {
  control: string;
  verdict: RenderVerdict;
  detail: string;
}

/**
 * §4a — does the control actually render DATA, not just exist?
 * `kind` picks the emptiness rule: a table needs rows with non-blank cells, a value needs
 * non-placeholder text, a chart needs a non-empty series node.
 */
export async function renderCheck(
  page: Page,
  control: string,
  selector: string,
  kind: 'table' | 'value' | 'chart' | 'present' = 'value',
): Promise<ControlResult> {
  const el = page.locator(selector).first();
  if ((await el.count()) === 0) {
    return { control, verdict: 'RENDER-EMPTY', detail: `absent: ${selector}` };
  }
  if (!(await el.isVisible().catch(() => false))) {
    return { control, verdict: 'RENDER-EMPTY', detail: `present but not visible: ${selector}` };
  }
  if (kind === 'present') return { control, verdict: 'RENDERS', detail: 'present and visible' };

  if (kind === 'table') {
    const stats = await el.evaluate((n) => {
      const rows = Array.from(n.querySelectorAll('tbody tr, [role="row"], li'))
        .filter((r) => !r.querySelector('th'));
      const nonEmpty = rows.filter((r) => (r.textContent || '').trim().length > 0);
      return { rows: rows.length, nonEmpty: nonEmpty.length, text: (n.textContent || '').trim().slice(0, 120) };
    });
    if (stats.rows === 0) return { control, verdict: 'RENDER-EMPTY', detail: `zero rows (${stats.text})` };
    if (stats.nonEmpty === 0) return { control, verdict: 'RENDER-EMPTY', detail: `${stats.rows} rows, all cells blank` };
    return { control, verdict: 'RENDERS', detail: `${stats.nonEmpty}/${stats.rows} rows with data` };
  }

  if (kind === 'chart') {
    const n = await el.evaluate((e) => e.querySelectorAll('svg *, canvas, [class*="series"], [class*="bar"], [class*="slice"]').length);
    return n > 0
      ? { control, verdict: 'RENDERS', detail: `${n} chart nodes` }
      : { control, verdict: 'RENDER-EMPTY', detail: 'chart container has no series nodes' };
  }

  const txt = ((await el.textContent()) || '').trim();
  if (!txt || /^(-|—|n\/a|null|undefined|loading\.*)$/i.test(txt)) {
    return { control, verdict: 'RENDER-EMPTY', detail: `blank/placeholder value "${txt}"` };
  }
  return { control, verdict: 'RENDERS', detail: txt.slice(0, 80) };
}

export interface VisualResult {
  width: number;
  overlaps: { a: string; b: string }[];
  zeroSized: string[];
  offViewport: string[];
  hScroll: number;
  consoleErrors: string[];
  screenshot: string;
}

/**
 * §4b — geometry truth. Skips deliberately-clipped / aria-hidden / hidden-breakpoint subtrees,
 * which produced false "invisible control" findings in the previous pass.
 */
export async function visualCheck(page: Page, screenshotPath: string, width: number): Promise<VisualResult> {
  const consoleErrors: string[] = [];
  const onErr = (m: any) => { if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 200)); };
  page.on('console', onErr);
  await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
  await page.waitForTimeout(900);

  const geo = await page.evaluate(() => {
    const named = (e: Element) =>
      e.getAttribute('data-testid') || `${e.tagName.toLowerCase()}${e.className && typeof e.className === 'string' ? '.' + e.className.split(' ')[0] : ''}`;
    const clipped = (e: Element) => {
      let n: Element | null = e;
      while (n) {
        const s = getComputedStyle(n);
        if (s.clipPath && s.clipPath !== 'none' && /inset\(50%|inset\(100%/.test(s.clipPath)) return true;
        if (n.getAttribute('aria-hidden') === 'true') return true;
        if (s.display === 'none' || s.visibility === 'hidden') return true;
        n = n.parentElement;
      }
      return false;
    };
    const sel = 'a[data-testid], button[data-testid], input[data-testid], [data-testid][role], table[data-testid], h1, h2';
    const els = Array.from(document.querySelectorAll(sel)).filter((e) => !clipped(e));
    const boxes = els.map((e) => {
      const r = e.getBoundingClientRect();
      return { name: named(e), x: r.left, y: r.top, w: r.width, h: r.height };
    });
    const zeroSized = boxes.filter((b) => b.w <= 0 || b.h <= 0).map((b) => b.name);
    const vw = document.documentElement.clientWidth;
    const offViewport = boxes
      .filter((b) => b.w > 0 && b.h > 0 && (b.x + b.w > vw + 2 || b.x < -2))
      .map((b) => `${b.name}@x=${Math.round(b.x)},w=${Math.round(b.w)}`);
    const solid = boxes.filter((b) => b.w > 4 && b.h > 4);
    const overlaps: { a: string; b: string }[] = [];
    for (let i = 0; i < solid.length; i++) {
      for (let j = i + 1; j < solid.length; j++) {
        const a = solid[i], b = solid[j];
        const ox = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
        const oy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
        // Only siblings-level overlap matters; nesting (one box inside another) is normal.
        const nested =
          (a.x <= b.x && a.y <= b.y && a.x + a.w >= b.x + b.w && a.y + a.h >= b.y + b.h) ||
          (b.x <= a.x && b.y <= a.y && b.x + b.w >= a.x + a.w && b.y + b.h >= a.y + a.h);
        if (ox > 4 && oy > 4 && !nested) overlaps.push({ a: a.name, b: b.name });
      }
    }
    return {
      zeroSized,
      offViewport,
      overlaps: overlaps.slice(0, 20),
      hScroll: Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth),
    };
  });

  await page.screenshot({ path: screenshotPath, fullPage: false });
  page.off('console', onErr);
  return { width, ...geo, consoleErrors, screenshot: screenshotPath };
}
