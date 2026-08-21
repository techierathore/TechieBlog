/**
 * vall-admin-mobile-tabs.spec.ts — follow-up to the admin cluster's §4b gate.
 *
 * Inspecting `comments-list-390.png` showed the fourth status tab ("Spam (0)") cut off at the right
 * edge at 390 px. Geometry alone cannot say whether that is a defect: a clipped control inside a
 * horizontally scrollable strip is reachable, one inside a non-scrollable strip is not. This spec
 * decides it the only honest way — by trying to USE the control on a 390 px viewport.
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { BASE, USERS, nav } from './_gates';

const OUT = '.verify/shots/admin';
fs.mkdirSync(OUT, { recursive: true });

async function signIn(page: Page) {
  for (let i = 0; i < 4; i++) {
    try {
      await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('[data-testid="login-email"]', { timeout: 60000 });
      await page.waitForFunction(() => {
        const b = document.querySelector('[data-testid="login-submit"]');
        return !!b && Array.from(b.attributes).some((a) => a.name.startsWith('_bl'));
      }, { timeout: 90000 });
      await page.waitForTimeout(1000);
      await page.fill('[data-testid="login-email"]', USERS.admin.email);
      await page.fill('[data-testid="login-password"]', USERS.admin.password);
      await page.click('[data-testid="login-submit"]');
      await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 40000 });
      await page.waitForTimeout(2000);
      return;
    } catch { /* retry */ }
  }
  throw new Error('sign-in failed');
}

/** Reports whether a control is fully visible, scrollable-into-view, or genuinely unreachable. */
async function reachability(page: Page, testId: string) {
  return page.evaluate((id) => {
    const el = document.querySelector(`[data-testid="${id}"]`) as HTMLElement | null;
    if (!el) return { id, state: 'absent' as const };
    const r = el.getBoundingClientRect();
    const vw = document.documentElement.clientWidth;
    const inside = r.left >= -1 && r.right <= vw + 1;
    let n: Element | null = el.parentElement;
    let scroller: string | null = null;
    while (n) {
      const s = getComputedStyle(n);
      if ((s.overflowX === 'auto' || s.overflowX === 'scroll') && (n as HTMLElement).scrollWidth > (n as HTMLElement).clientWidth + 2) {
        scroller = `${n.tagName}.${(typeof n.className === 'string' ? n.className : '').split(' ')[0]}`;
        break;
      }
      n = n.parentElement;
    }
    return {
      id,
      state: inside ? ('visible' as const) : scroller ? ('scrollable' as const) : ('unreachable' as const),
      left: Math.round(r.left), right: Math.round(r.right), vw, scroller,
    };
  }, testId);
}

test('REQ-UI-021 the moderation status tabs are all usable on a 390px viewport', async ({ page }) => {
  await signIn(page);
  await nav(page, '/CommentsList', /Comments Management/);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1500);

  const ids = ['comments-tab-all', 'comments-tab-pending', 'comments-tab-approved', 'comments-tab-spam'];
  const states = [];
  for (const id of ids) states.push(await reachability(page, id));
  console.log(`[REQ-UI-021 mobile] tab reachability = ${JSON.stringify(states)}`);
  await page.screenshot({ path: `${OUT}/comments-tabs-390.png` });

  const unreachable = states.filter((s) => s.state === 'unreachable' || s.state === 'absent');

  // Decisive test: actually operate the last tab on the mobile viewport.
  let spamUsable = false;
  let spamRows = -1;
  try {
    await page.locator('[data-testid="comments-tab-spam"]').scrollIntoViewIfNeeded({ timeout: 10000 });
    await page.locator('[data-testid="comments-tab-spam"]').click({ timeout: 10000 });
    await page.waitForTimeout(2500);
    spamRows = await page.locator('[data-testid="comment-row-text"]').count();
    const emptyState = await page.locator('[data-testid="comments-empty"]').count();
    spamUsable = spamRows === 0 ? emptyState > 0 : true;
    console.log(`[REQ-UI-021 mobile] spam tab clicked → rows=${spamRows} emptyState=${emptyState}`);
  } catch (e: any) {
    console.log(`[REQ-UI-021 mobile] spam tab could NOT be operated at 390px: ${`${e.message ?? e}`.split('\n')[0]}`);
  }
  await page.screenshot({ path: `${OUT}/comments-tabs-390-spam.png` });

  console.log(`[REQ-UI-021 mobile] unreachable=${JSON.stringify(unreachable)} spamUsable=${spamUsable}`);
  expect(spamUsable, 'every moderation status tab must be operable at 390px').toBe(true);
  expect(unreachable, 'no status tab may be clipped out of a non-scrollable strip').toEqual([]);
});

test('REQ-UI-020 the user role tabs are all usable on a 390px viewport', async ({ page }) => {
  await signIn(page);
  await nav(page, '/users', /^Users$/);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1500);
  const ids = ['users-tab-all', 'users-tab-admin', 'users-tab-editor', 'users-tab-reader'];
  const states = [];
  for (const id of ids) states.push(await reachability(page, id));
  console.log(`[REQ-UI-020 mobile] tab reachability = ${JSON.stringify(states)}`);
  await page.screenshot({ path: `${OUT}/users-tabs-390.png` });

  await page.locator('[data-testid="users-tab-reader"]').scrollIntoViewIfNeeded({ timeout: 10000 });
  await page.locator('[data-testid="users-tab-reader"]').click({ timeout: 10000 });
  await page.waitForTimeout(2500);
  const emptyOrRows = (await page.locator('[data-testid="user-row-name"]').count()) + (await page.locator('[data-testid="users-empty"]').count());
  console.log(`[REQ-UI-020 mobile] readers tab clicked → rows+emptyState = ${emptyOrRows}`);
  expect(emptyOrRows, 'the Readers tab must render rows or an empty state after a mobile click').toBeGreaterThan(0);
  expect(states.filter((s) => s.state === 'unreachable' || s.state === 'absent'), 'no role tab may be clipped out of a non-scrollable strip').toEqual([]);
});
