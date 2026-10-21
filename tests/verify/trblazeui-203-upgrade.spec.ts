/**
 * trblazeui-203-upgrade.spec.ts — consumer UAT for the TrBlazeUI 2.0.2 → 2.0.3 upgrade (2026-08-25).
 *
 * The 2.0.3 resolution table in docs/TechieBlog-TrBlazeUI-Feedback.md says which workarounds may be
 * removed "only after TechieBlog verifies on 2.0.3". Each test here measures ONE of those claims
 * against the running app with the workaround already removed, so a failure is a signal to put the
 * workaround back, not to soften the assertion.
 *
 *   TR-067  popover Select inside DialogContent renders its options   (/admin/images, /users, /admin/skills)
 *   TR-071  ItemContent ships min-w-0 — /admin has no horizontal scroll at 390px
 *   TR-072  DatePicker / TimePicker render a splatted data-testid       (/ManagePost, /admin/experience)
 *   TR-072b StatTile emits stat-tile-value / stat-tile-label slots      (/)
 *   TR-072c min-h-28 / min-h-36 / hover:opacity-90 / md:-mx-6 resolve   (computed style)
 *   TR-073  bg-gradient-to-br from-muted to-card paints a real gradient (/ post card fallback)
 *
 * Run: TB_BASE=http://172.18.144.1:5473 npx playwright test tests/verify/trblazeui-203-upgrade.spec.ts
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5473';
const OUT = 'tests/.artifacts/trblazeui-203';
fs.mkdirSync(OUT, { recursive: true });

const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

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

test.beforeEach(async () => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await closeAnyDialog();
});

async function closeAnyDialog() {
  for (let i = 0; i < 6; i++) {
    if ((await page.locator('[role="dialog"]').count()) === 0) return;
    const cancel = page.locator('[role="dialog"] button', { hasText: /^(Cancel|Close)$/i }).first();
    if (await cancel.count()) await cancel.click({ force: true }).catch(() => {});
    else await page.keyboard.press('Escape');
    await page.waitForTimeout(1000);
  }
}

async function go(route: string, heading: RegExp | string) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  const marker = typeof heading === 'string'
    ? page.locator(heading).first()
    : page.locator('h1, h2').filter({ hasText: heading }).first();
  await expect(marker).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(1200);
}

/** Opens a Select trigger inside the first open dialog and returns its option labels. */
async function dialogSelectOptions(triggerTestId: string): Promise<string[]> {
  const dlg = page.locator('[role="dialog"]').first();
  await expect(dlg).toBeVisible({ timeout: 30000 });
  const trigger = dlg.locator(`[data-testid="${triggerTestId}"]`).first();
  await expect(trigger).toBeVisible({ timeout: 30000 });
  await trigger.click();
  await page.waitForTimeout(1200);
  const labels = (await page.locator('[role="option"]').allTextContents()).map((t) => t.trim());
  return labels;
}

// ---------------------------------------------------------------------------------------------
// TR-067 — Select inside DialogContent
// ---------------------------------------------------------------------------------------------
test('TR-067 /admin/images upload dialog: styled Select renders 7 categories, mouse + keyboard select', async () => {
  test.setTimeout(240000);
  await go('/admin/images', /Media Library/i);
  await page.locator('[data-testid="upload-image"]').click();
  const labels = await dialogSelectOptions('upload-category-select');
  notes.push(`TR-067 images: ${labels.length} options → ${JSON.stringify(labels)}`);
  expect(labels.length).toBe(7);

  // Mouse: pick "Icons" and confirm the bound value moved (constraint caption follows the category).
  await page.locator('[role="option"]', { hasText: /^Icons$/ }).click();
  await page.waitForTimeout(1200);
  const captionAfterMouse = (await page.locator('[data-testid="upload-category-constraints"]').textContent())?.trim();
  const triggerAfterMouse = (await page.locator('[data-testid="upload-category-select"]').textContent())?.trim();
  notes.push(`TR-067 images mouse → trigger="${triggerAfterMouse}" caption="${captionAfterMouse}"`);
  expect(triggerAfterMouse).toContain('Icons');
  expect(captionAfterMouse).toMatch(/svg/i);

  // Keyboard: focus trigger, open with Enter, ArrowDown, Enter — must land on a different category.
  await page.locator('[data-testid="upload-category-select"]').focus();
  await page.keyboard.press('Enter');
  await page.waitForTimeout(800);
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1200);
  const triggerAfterKeys = (await page.locator('[data-testid="upload-category-select"]').textContent())?.trim();
  notes.push(`TR-067 images keyboard → trigger="${triggerAfterKeys}"`);
  expect(triggerAfterKeys).not.toBe(triggerAfterMouse);
  await page.screenshot({ path: `${OUT}/tr067-images-dialog.png` });
  await closeAnyDialog();
});

test('TR-067 /users change-role dialog Select renders options', async () => {
  test.setTimeout(240000);
  await go('/users', /Users/i);
  await page.locator('[data-testid="user-edit"]').first().click();
  const labels = await dialogSelectOptions('user-role-select');
  notes.push(`TR-067 users: ${labels.length} options → ${JSON.stringify(labels)}`);
  expect(labels.length).toBeGreaterThan(0);
  await page.keyboard.press('Escape');
  await page.screenshot({ path: `${OUT}/tr067-users-dialog.png` });
  await closeAnyDialog();
});

test('TR-067 /admin/skills add-skill dialog Select renders options', async () => {
  test.setTimeout(240000);
  await go('/admin/skills', /Skills/i);
  await page.locator('[data-testid="add-skill"]').click();
  const labels = await dialogSelectOptions('skill-category-select');
  notes.push(`TR-067 skills: ${labels.length} options → ${JSON.stringify(labels)}`);
  expect(labels.length).toBeGreaterThan(0);
  await page.keyboard.press('Escape');
  await page.screenshot({ path: `${OUT}/tr067-skills-dialog.png` });
  await closeAnyDialog();
});

// ---------------------------------------------------------------------------------------------
// TR-071 — ItemContent min-w-0 shipped; /admin at 390px must not overflow
// ---------------------------------------------------------------------------------------------
test('TR-071 /admin dashboard at 390px: ItemContent has min-width 0 and no document h-scroll', async () => {
  test.setTimeout(240000);
  await go('/admin', /Dashboard/i);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1500);
  const m = await page.evaluate(() => {
    const item = document.querySelector('[data-testid="recent-activity-item"] [data-slot="item-content"]')
      ?? document.querySelector('[data-testid="recent-activity-item"] > :nth-child(2)');
    const cs = item ? getComputedStyle(item) : null;
    return {
      itemFound: !!item,
      itemClass: item?.getAttribute('class') ?? '',
      minWidth: cs?.minWidth ?? '',
      hScroll: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    };
  });
  notes.push(`TR-071 /admin@390 → ${JSON.stringify(m)}`);
  await page.screenshot({ path: `${OUT}/tr071-admin-390.png`, fullPage: true });
  expect(m.hScroll).toBe(0);
  if (m.itemFound) expect(m.minWidth).toBe('0px');
});

// ---------------------------------------------------------------------------------------------
// TR-072 — DatePicker / TimePicker splat data-testid onto their trigger button
// ---------------------------------------------------------------------------------------------
test('TR-072 /ManagePost: publish-date-picker + publish-time-picker hooks land on <button>', async () => {
  test.setTimeout(240000);
  await go('/ManagePost', '[data-testid="post-title-input"]');
  const m = await page.evaluate(() => {
    const q = (id: string) => {
      const el = document.querySelector(`[data-testid="${id}"]`);
      return el ? `${el.tagName.toLowerCase()}${el.getAttribute('type') ? '[' + el.getAttribute('type') + ']' : ''}` : 'MISSING';
    };
    return { date: q('publish-date-picker'), time: q('publish-time-picker') };
  });
  notes.push(`TR-072 ManagePost → ${JSON.stringify(m)}`);
  expect(m.date).toBe('button[button]');
  expect(m.time).toBe('button[button]');
});

test('TR-072 /admin/experience dialog: experience-start-date + experience-end-date hooks land on <button>', async () => {
  test.setTimeout(240000);
  await go('/admin/experience', /Experience/i);
  await page.locator('[data-testid="add-experience"]').click();
  const dlg = page.locator('[role="dialog"]').first();
  await expect(dlg).toBeVisible({ timeout: 30000 });
  const m = await page.evaluate(() => {
    const q = (id: string) => {
      const el = document.querySelector(`[data-testid="${id}"]`);
      return el ? el.tagName.toLowerCase() : 'MISSING';
    };
    return { start: q('experience-start-date'), end: q('experience-end-date') };
  });
  notes.push(`TR-072 experience → ${JSON.stringify(m)}`);
  expect(m.start).toBe('button');
  expect(m.end).toBe('button');
  await closeAnyDialog();
});

// ---------------------------------------------------------------------------------------------
// TR-072b / TR-072c / TR-073 — public home page, computed styles
// ---------------------------------------------------------------------------------------------
test('TR-072b/072c/073: StatTile slots (analytics), shipped utilities, gradient stops (home no-banner card)', async () => {
  test.setTimeout(240000);
  // The home page needs one published post without a banner (gradient fallback) and one
  // UserStats row (StatTile band). Both are seeded for this run and reverted afterwards.
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="post-card"]', { timeout: 60000 });
  await page.waitForSelector('[data-testid="home-stat-card"]', { timeout: 60000 });
  await page.waitForTimeout(1500);
  const slots = await page.evaluate(() => ({
    cards: document.querySelectorAll('[data-testid="home-stat-card"]').length,
    value: document.querySelectorAll('[data-slot="stat-tile-value"]').length,
    label: document.querySelectorAll('[data-slot="stat-tile-label"]').length,
    valueText: Array.from(document.querySelectorAll('[data-slot="stat-tile-value"]')).map((e) => e.textContent?.trim()),
  }));
  notes.push(`TR-072b home StatTile slots → ${JSON.stringify(slots)}`);
  expect(slots.value).toBe(slots.cards);
  expect(slots.label).toBe(slots.cards);
  expect(slots.value).toBeGreaterThan(0);
  const m = await page.evaluate(() => {
    const probe = (cls: string, prop: string) => {
      const d = document.createElement('div');
      d.className = cls;
      document.body.appendChild(d);
      const v = getComputedStyle(d).getPropertyValue(prop);
      d.remove();
      return v;
    };
    const fallback = document.querySelector('[data-testid="post-card-image-placeholder"]:not(.hidden)');
    return {
      minH28: probe('min-h-28', 'min-height'),
      minH36: probe('min-h-36', 'min-height'),
      gradient: probe('bg-gradient-to-br from-muted to-card', 'background-image'),
      fallbackPresent: !!fallback,
      fallbackGradient: fallback ? getComputedStyle(fallback).backgroundImage : '',
    };
  });
  notes.push(`TR-072b/c/073 home → ${JSON.stringify(m)}`);
  expect(m.minH28).toBe('112px');
  expect(m.minH36).toBe('144px');
  expect(m.gradient).toMatch(/linear-gradient\(to (bottom right|right bottom)/);
  expect(m.gradient).not.toMatch(/var\(--tw-gradient-stops\)/);
  expect(m.fallbackPresent).toBe(true);
  expect(m.fallbackGradient).toMatch(/linear-gradient\(to (bottom right|right bottom)/);
  expect(m.fallbackGradient).not.toMatch(/var\(--tw-gradient-stops\)/);
  await page.screenshot({ path: `${OUT}/home-1280.png`, fullPage: true });
  await page.locator('[data-testid="post-card-image-placeholder"]:not(.hidden)').first()
    .screenshot({ path: `${OUT}/tr073-post-card-fallback.png` });
});

test('TR-072c md:-mx-6 and hover:opacity-90 resolve', async () => {
  const m = await page.evaluate(() => {
    const d = document.createElement('div');
    d.className = 'md:-mx-6';
    document.body.appendChild(d);
    const mx = getComputedStyle(d).marginLeft;
    d.remove();
    // Tailwind v4 nests its utilities inside `@layer` / `@media` grouping rules, so walk recursively.
    const rules: string[] = [];
    const walk = (list: CSSRuleList, sheet: string) => {
      for (const r of Array.from(list)) {
        const t = (r as CSSStyleRule).selectorText ?? '';
        if (t.includes('opacity-90')) rules.push(`${sheet}: ${t}`);
        const inner = (r as CSSGroupingRule).cssRules;
        if (inner) walk(inner, sheet);
      }
    };
    for (const s of Array.from(document.styleSheets)) {
      try {
        walk(s.cssRules, (s as CSSStyleSheet).href?.split('/').pop()?.split('?')[0] ?? 'inline');
      } catch { /* cross-origin */ }
    }
    return { mdMx: mx, opacity90Rules: rules };
  });
  notes.push(`TR-072c md:-mx-6@1280 → ${JSON.stringify(m)}`);
  expect(m.mdMx).toBe('-24px');
  expect(m.opacity90Rules.some((r) => r.startsWith('trblazeui.css'))).toBe(true);
  expect(m.opacity90Rules.some((r) => r.startsWith('utilities.css'))).toBe(false);
});
