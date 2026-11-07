/**
 * bp-b-auth.spec.ts — cluster B build-phase smoke (2026-10-27) for REQ-FN-009 (the 5-role model and
 * its authorization policies).
 *
 * `vall-auth.spec.ts` probes three or four routes per role. This spec probes EVERY protected route
 * the app declares: the route -> policy table below is read off the `@attribute [Authorize(...)]`
 * lines of `source/BlogUI/Pages/AdminPages/*.razor` (2026-10-27) and the role -> policy table off
 * `AppPolicies.PolicyRoleMap`. A role must reach exactly the routes its policy allows and land on
 * `/access-denied` for the rest.
 */
import { test, expect, Page } from '@playwright/test';
import { loginHard } from './vall-authoring-helpers';
import { RoleKey } from './_gates';

type Policy = 'AdminOnly' | 'EditorOrAbove' | 'AuthorOrAbove' | 'Authenticated';

const ROUTES: Record<string, Policy> = {
  '/AddUser': 'AdminOnly',
  '/admin/categories': 'AdminOnly',
  '/admin/category': 'AdminOnly',
  '/admin/images': 'AdminOnly',
  '/ManageTag': 'AdminOnly',
  '/admin/newsletter': 'AdminOnly',
  '/settings': 'AdminOnly',
  '/admin/subscribers': 'AdminOnly',
  '/admin/tags': 'AdminOnly',
  '/users': 'AdminOnly',
  '/admin': 'EditorOrAbove',
  '/admin/analytics': 'EditorOrAbove',
  '/comments': 'EditorOrAbove',
  '/admin/speaking': 'EditorOrAbove',
  '/BlogsList': 'AuthorOrAbove',
  '/admin/awards': 'AuthorOrAbove',
  '/admin/experience': 'AuthorOrAbove',
  '/admin/profile': 'AuthorOrAbove',
  '/admin/series': 'AuthorOrAbove',
  '/admin/series/new': 'AuthorOrAbove',
  '/ManagePost': 'AuthorOrAbove',
  '/admin/skills': 'AuthorOrAbove',
  '/admin/stats': 'AuthorOrAbove',
  '/change-password': 'Authenticated',
};

const SATISFIES: Record<RoleKey, Policy[]> = {
  admin: ['AdminOnly', 'EditorOrAbove', 'AuthorOrAbove', 'Authenticated'],
  editor: ['EditorOrAbove', 'AuthorOrAbove', 'Authenticated'],
  author: ['AuthorOrAbove', 'Authenticated'],
  contributor: ['Authenticated'],
};

async function verdict(page: Page, route: string, seconds = 7): Promise<'ALLOWED' | 'DENIED'> {
  // Start from a screen that cannot carry the previous route's verdict.
  await page.evaluate(() => (window as any).Blazor.navigateTo('/'));
  await page.waitForFunction(() => !document.querySelector('[data-testid="access-denied"]') && location.pathname === '/', { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(500);
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  for (let i = 0; i < seconds; i++) {
    await page.waitForTimeout(1000);
    if ((await page.locator('[data-testid="access-denied"]').count()) > 0 || page.url().toLowerCase().includes('access-denied')) return 'DENIED';
    if (page.url().toLowerCase().includes('/login')) return 'DENIED';
  }
  return 'ALLOWED';
}

for (const role of Object.keys(SATISFIES) as RoleKey[]) {
  test(`REQ-FN-009 ${role} reaches exactly the routes its policy allows`, async ({ browser }) => {
    test.setTimeout(1500000);
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    await loginHard(page, role);
    const wrong: string[] = [];
    for (const [route, policy] of Object.entries(ROUTES)) {
      const expected = SATISFIES[role].includes(policy) ? 'ALLOWED' : 'DENIED';
      let actual = await verdict(page, route);
      // A busy shared host can resolve authorisation late; a disagreement is re-measured once
      // with a longer window before it is reported as a defect.
      if (actual !== expected) actual = await verdict(page, route, 20);
      console.log(`[FN-009 ${role}] ${route} ${policy} expected ${expected} got ${actual} at ${new Date().toISOString()}`);
      if (actual !== expected) wrong.push(`${route} (${policy}) expected ${expected} got ${actual}`);
    }
    await page.screenshot({ path: `tests/.artifacts/build-phase-20261027/b/fn009-${role}.png` });
    expect(wrong, wrong.join('; ')).toEqual([]);
    await page.close();
  });
}
