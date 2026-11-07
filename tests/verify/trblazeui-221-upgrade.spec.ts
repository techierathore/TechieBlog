/**
 * trblazeui-221-upgrade.spec.ts — consumer UAT for the TrBlazeUI 2.0.3 → 2.2.1 upgrade (2026-10-29).
 *
 * The two findings logged against 2.0.3 that 2.2.1 claims to fix. Neither had a workaround in
 * TechieBlog, so each test measures the library claim on the running app:
 *
 *   TR-075  styled Select inside DialogContent hands focus back to its trigger, so Escape closes
 *           the dialog after a pick                                        (/admin/images)
 *   TR-076  SidebarMenuButton IsActive renders data-active="true"/"false" and the active item
 *           paints the sidebar accent                                      (/admin/series)
 *
 * Run: TB_BASE=http://172.18.144.1:5473 npx playwright test tests/verify/trblazeui-221-upgrade.spec.ts
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5473';
const OUT = 'tests/.artifacts/trblazeui-221';
fs.mkdirSync(OUT, { recursive: true });

const ADMIN = { email: 'Ravi@techieblog.com', password: process.env.TB_PW_ADMIN ?? 'VerifyAdmin#2026' };

let page: Page;
const notes: string[] = [];

test.beforeAll(async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  page = await ctx.newPage();
  page.on('pageerror', (e) => notes.push(`PAGEERROR ${e.message}`));
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
  await page
    .waitForFunction(() => {
      const f = document.querySelector('form');
      return !!f && !f.hasAttribute('action');
    }, { timeout: 90000 })
    .catch(() => {});
  const fillStable = async (sel: string, v: string) => {
    for (let i = 0; i < 15; i++) {
      await page.fill(sel, v);
      await page.waitForTimeout(500);
      if ((await page.inputValue(sel)) === v) return;
    }
    throw new Error(`${sel} would not hold its value`);
  };
  await fillStable('[data-testid="login-email"]', ADMIN.email);
  await fillStable('[data-testid="login-password"]', ADMIN.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 90000 });
  await page.waitForTimeout(2000);
  notes.push(`login landed on ${page.url()}`);
  expect(page.url()).not.toMatch(/change-password/i);
});

test.afterAll(async () => {
  fs.writeFileSync(`${OUT}/notes.json`, JSON.stringify(notes, null, 2));
});

async function go(route: string, heading: RegExp) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  await expect(page.locator('h1, h2').filter({ hasText: heading }).first()).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(1200);
}

// ---------------------------------------------------------------------------------------------
// TR-075 — focus returns to the SelectTrigger after a pick inside a dialog
// ---------------------------------------------------------------------------------------------
test('TR-075 /admin/images upload dialog: after a keyboard pick focus is on the trigger and Escape closes the dialog', async () => {
  test.setTimeout(240000);
  await go('/admin/images', /Media Library/i);
  await page.locator('[data-testid="upload-image"]').click();
  const dlg = page.locator('[role="dialog"]').first();
  await expect(dlg).toBeVisible({ timeout: 30000 });
  const trigger = dlg.locator('[data-testid="upload-category-select"]').first();
  await trigger.focus();
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1200);

  const focused = await page.evaluate(() => {
    const a = document.activeElement as HTMLElement | null;
    return a ? (a.getAttribute('data-testid') ?? a.tagName) : 'none';
  });
  notes.push(`TR-075 activeElement after pick = ${focused}`);
  expect(focused).toBe('upload-category-select');

  await page.keyboard.press('Escape');
  await page.waitForTimeout(1200);
  const open = await page.locator('[role="dialog"]').count();
  notes.push(`TR-075 dialogs open after one Escape = ${open}`);
  expect(open).toBe(0);
});

// ---------------------------------------------------------------------------------------------
// TR-076 — SidebarMenuButton IsActive paints the active item
// ---------------------------------------------------------------------------------------------
test('TR-076 /admin/series: the active sidebar item has data-active="true" and a painted background', async () => {
  test.setTimeout(240000);
  await go('/admin/series', /Series/i);
  const items = await page.locator('[data-sidebar="menu-button"], a[data-active], button[data-active]').evaluateAll((els) =>
    els
      .filter((e) => e.hasAttribute('data-active'))
      .map((e) => ({
        testId: e.getAttribute('data-testid'),
        href: e.getAttribute('href'),
        active: e.getAttribute('data-active'),
        current: e.getAttribute('aria-current'),
        bg: getComputedStyle(e).backgroundColor,
        weight: getComputedStyle(e).fontWeight,
      })));
  notes.push(`TR-076 items = ${JSON.stringify(items)}`);

  const active = items.filter((i) => i.active === 'true');
  expect(active.length).toBe(1);
  expect(active[0].href).toMatch(/\/admin\/series$/i);
  expect(active[0].current).toBe('page');
  expect(active[0].bg).not.toBe('rgba(0, 0, 0, 0)');
  expect(Number(active[0].weight)).toBeGreaterThanOrEqual(500);

  const inactive = items.filter((i) => i.active === 'false');
  expect(inactive.length).toBeGreaterThan(0);
  expect(items.some((i) => i.active === '')).toBe(false);
  for (const i of inactive) expect(i.bg).toBe('rgba(0, 0, 0, 0)');
  await page.screenshot({ path: `${OUT}/tr076-admin-series.png` });
});
