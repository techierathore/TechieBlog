import { test, expect } from '@playwright/test';
import { execFileSync } from 'child_process';
import { BASE, login, nav } from './_gates';

/**
 * Build-phase 2026-10-27 cluster D smoke (admin surfaces): REQ-UI-064, REQ-FN-053, REQ-FN-058.
 * Skills fixtures are `fxd `-prefixed rows seeded by psql: Zeta(1,2) · Mid(3) · Alpha(4,5) — the
 * authored order is deliberately the reverse of alphabetical.
 */
const OUT = 'tests/.artifacts/build-phase-20261027/d';
const AUTHORED = ['fxd Zeta', 'fxd Mid', 'fxd Alpha'];

function psql(sql: string): string {
  return execFileSync('docker', ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-At', '-F', '|', '-c', sql], { encoding: 'utf8' }).trim();
}

async function selectUser(page: import('@playwright/test').Page) {
  await page.click('[data-testid="skills-user-select"]');
  await page.getByRole('option', { name: /author@techieblog\.test/ }).click().catch(async () => {
    await page.getByText(/author@techieblog\.test/).first().click();
  });
  await expect(page.locator('[data-testid="skill-row"]')).toHaveCount(5, { timeout: 20000 });
  await expect(page.locator('[data-testid="skill-row"]').filter({ hasText: 'fxd-u3-' })).toHaveCount(5, { timeout: 20000 });
}

async function categoryNames(page: import('@playwright/test').Page) {
  return (await page.locator('[data-testid="skill-category-name"]').allInnerTexts()).map(s => s.trim());
}

// retired 2026-10-27 (W6): needs psql-seeded fxd fixtures the builder deleted (no DB writes in verify phase); superseded by w6-resume.spec.ts REQ-UI-064
test.skip('retired: admin skills: authored order, order badges, move controls, persistence, public resume', async ({ page }) => {
  test.setTimeout(240000);
  await page.setViewportSize({ width: 1280, height: 1000 });
  await login(page, 'admin');
  await nav(page, '/admin/skills');
  await expect(page.locator('[data-testid="skill-row"]').first()).toBeVisible({ timeout: 45000 });
  // Reordering renumbers the WHOLE set of the selected user, so the moves run against the
  // fixture user (arun, id 3), never the site owner whose rows a sibling builder also uses.
  await selectUser(page);
  await expect(page.locator('[data-testid="skill-row"]')).toHaveCount(5);
  expect(await categoryNames(page)).toEqual(AUTHORED);
  const badges = (await page.locator('[data-testid="skill-order"]').allInnerTexts()).map(t => parseInt(t.replace(/\D+/g, ''), 10));
  expect(badges).toEqual([1, 2, 3, 4, 5]);

  const ups = page.locator('[data-testid="move-category-up"]');
  const downs = page.locator('[data-testid="move-category-down"]');
  expect(await ups.count()).toBe(3);
  await expect(ups.nth(0)).toBeDisabled();
  await expect(ups.nth(1)).toBeEnabled();
  await expect(downs.nth(2)).toBeDisabled();
  const skillUps = page.locator('[data-testid="move-skill-up"]');
  await expect(skillUps.nth(0)).toBeDisabled();
  await page.screenshot({ path: `${OUT}/ui064-admin-before.png` });

  // public resume (site owner's rows, seeded read-only): authored order, NOT alphabetical
  const resume = await (await page.request.get(`${BASE}/resume`)).text();
  const pos = AUTHORED.map(c => resume.indexOf(c));
  console.log('RESUME category positions (Zeta,Mid,Alpha):', pos.join(','));
  expect(pos.every(p => p >= 0), 'seeded categories missing from /resume').toBeTruthy();
  expect(pos[0]).toBeLessThan(pos[1]);
  expect(pos[1]).toBeLessThan(pos[2]);
  await page.goto(`${BASE}/resume`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(3000);
  await page.screenshot({ path: `${OUT}/ui064-resume-authored.png` });
  await nav(page, '/admin/skills');
  await selectUser(page);

  // reorder: Mid up -> Mid, Zeta, Alpha
  await ups.nth(1).click();
  await expect.poll(() => categoryNames(page), { timeout: 20000 }).toEqual(['fxd Mid', 'fxd Zeta', 'fxd Alpha']);
  await page.waitForTimeout(1500);
  console.log('DB order after move:', psql("select skillname||':'||displayorder from userskills where userid=3 and category like 'fxd %' order by displayorder"));
  const renumbered = (await page.locator('[data-testid="skill-order"]').allInnerTexts()).map(t => parseInt(t.replace(/\D+/g, ''), 10));
  expect(renumbered).toEqual([1, 2, 3, 4, 5]);
  await page.screenshot({ path: `${OUT}/ui064-admin-after.png` });

  // persists across a fresh load of the page
  await nav(page, '/admin/skills');
  await expect(page.locator('[data-testid="skill-row"]').first()).toBeVisible({ timeout: 45000 });
  await selectUser(page);
  await expect.poll(() => categoryNames(page), { timeout: 30000 }).toEqual(['fxd Mid', 'fxd Zeta', 'fxd Alpha']);

  // revert (reversible)
  await nav(page, '/admin/skills');
  await expect(page.locator('[data-testid="skill-row"]').first()).toBeVisible({ timeout: 45000 });
  await selectUser(page);
  expect(await categoryNames(page)).toEqual(['fxd Mid', 'fxd Zeta', 'fxd Alpha']);
  await page.waitForTimeout(1500);
  await page.locator('[data-testid="move-category-down"]').nth(0).click();
  await expect.poll(() => categoryNames(page), { timeout: 20000 }).toEqual(AUTHORED);
});

test('REQ-FN-053 opening /admin/profile and saving with no edits leaves every column untouched', async ({ page }) => {
  test.setTimeout(240000);
  const snap = () => psql("select (to_jsonb(b) - 'updatedon')::text from bloguser b where userid = 1");
  const before = snap();
  await page.setViewportSize({ width: 1280, height: 1000 });
  await login(page, 'admin');
  await nav(page, '/admin/profile');
  await expect(page.locator('[data-testid="manage-profile-page"]')).toBeVisible({ timeout: 45000 });
  await expect(page.locator('[data-testid="first-name-input"]')).toHaveValue(/\S/, { timeout: 30000 });
  // the resume inputs render stored values, not empty boxes
  await expect(page.locator('[data-testid="phone-input"]')).toHaveValue('+91 98765 43210');
  await expect(page.locator('[data-testid="location-input"]')).toHaveValue('Hyderabad, India');
  await expect(page.locator('[data-testid="title-input"]')).toHaveValue('Senior .NET Architect');
  await page.screenshot({ path: `${OUT}/fn053-profile-before-save.png` });
  await page.click('[data-testid="save-profile"]');
  await expect(page.locator('[data-testid="profile-status"]')).toBeVisible({ timeout: 30000 });
  console.log('PROFILE STATUS:', (await page.locator('[data-testid="profile-status"]').innerText()).trim());
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${OUT}/fn053-profile-after-save.png` });
  const after = snap();
  expect(after, 'a no-edit save changed a bloguser column').toBe(before);
  // public surfaces still serve the owner's live data
  const home = await (await page.request.get(`${BASE}/`)).text();
  expect(home).toContain('Senior .NET Architect');
  const resume = await (await page.request.get(`${BASE}/resume`)).text();
  expect(resume).toContain('Hyderabad');
});

test('REQ-FN-058 deep link into an admin route keeps the session; sign-out lands on /', async ({ page, context }) => {
  test.setTimeout(240000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page, 'admin');
  // new tab, straight at a deep admin URL (full document load)
  const tab = await context.newPage();
  await tab.setViewportSize({ width: 1280, height: 900 });
  await tab.goto(`${BASE}/admin/speaking`, { waitUntil: 'networkidle' });
  await tab.waitForTimeout(4000);
  expect(new URL(tab.url()).pathname, 'deep link bounced').toBe('/admin/speaking');
  await expect(tab.locator('[data-testid="manage-speaking-page"]')).toBeVisible({ timeout: 30000 });
  await tab.screenshot({ path: `${OUT}/fn058-deeplink-speaking.png` });
  // reload keeps the session
  await tab.reload({ waitUntil: 'networkidle' });
  await tab.waitForTimeout(4000);
  expect(new URL(tab.url()).pathname, 'reload bounced').toBe('/admin/speaking');
  await expect(tab.locator('[data-testid="manage-speaking-page"]')).toBeVisible({ timeout: 30000 });
  await tab.goto(`${BASE}/admin/skills`, { waitUntil: 'networkidle' });
  await tab.waitForTimeout(4000);
  expect(new URL(tab.url()).pathname).toBe('/admin/skills');
  // sign out -> public home
  await tab.click('[data-testid="account-menu-trigger"]');
  await tab.click('[data-testid="account-logout"]');
  await tab.waitForURL((u) => u.pathname === '/', { timeout: 30000 });
  await tab.waitForTimeout(3000);
  expect(new URL(tab.url()).pathname, 'sign-out did not land on /').toBe('/');
  await tab.screenshot({ path: `${OUT}/fn058-after-logout.png` });
  // the session is really gone: a deep link now must not render the admin page
  await tab.goto(`${BASE}/admin/speaking`, { waitUntil: 'networkidle' });
  await tab.waitForTimeout(3000);
  expect(await tab.locator('[data-testid="manage-speaking-page"]').count()).toBe(0);
});
