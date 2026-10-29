import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { nav, visualCheck } from './_gates';
import { signIn, settle } from './vall-resume-helpers';

/** Build-phase 2026-10-27 cluster A verify (group profile): REQ-UI-040. Read-only; nothing is saved. */
const OUT = 'tests/.artifacts/build-phase-20261027/a/profile';
test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

async function values(page: Page) {
  const ids = ['first-name-input', 'last-name-input', 'username-input', 'title-input', 'tagline-input', 'linkedin-input', 'github-input', 'phone-input', 'location-input'];
  const o: Record<string, string> = {};
  for (const id of ids) o[id] = await page.locator(`[data-testid="${id}"]`).first().inputValue().catch(() => '<absent>');
  return o;
}

test('REQ-UI-040 author sees only own profile data; admin gets selectors on the resume admin pages', async ({ page }) => {
  test.setTimeout(500000);
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));

  console.log('AUTHOR LANDING', await signIn(page, 'author'));
  await nav(page, '/admin/profile', /Profile/i);
  await settle(page);
  const a = await values(page);
  console.log('AUTHOR PROFILE', JSON.stringify(a));
  expect(a['first-name-input']).toBe('Arun');
  expect(a['username-input']).toBe('arun');
  expect(await page.locator('[data-testid="manage-profile-page"] [data-testid$="user-select"]').count()).toBe(0);
  for (const id of ['basic-info-card', 'social-links-card', 'resume-settings-card', 'quick-links-card']) await expect(page.locator(`[data-testid="${id}"]`)).toBeVisible();
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, `${OUT}/author-profile-${w}.png`, w);
    console.log(`GEOM author-profile@${w}`, JSON.stringify({ o: v.overlaps, z: v.zeroSized, off: v.offViewport, h: v.hScroll, c: v.consoleErrors }));
    expect(v.hScroll).toBe(0);
    expect(v.overlaps).toEqual([]);
  }
  await page.setViewportSize({ width: 1280, height: 900 });

  for (const route of ['/admin/experience', '/admin/skills', '/admin/awards']) {
    await nav(page, route);
    await settle(page);
    const sel = await page.locator('[data-testid$="-user-select"]').count();
    const txt = (await page.locator('main, body').first().innerText()).replace(/\s+/g, ' ').slice(0, 160);
    console.log('AUTHOR', route, 'user-select count =', sel, '|', txt);
    expect(sel, `${route}: author must have no user selector`).toBe(0);
    expect(txt).not.toMatch(/fxa|fxd|AI for .NET/);
  }
  // sign out by dropping the token: use a fresh context for the admin
  const ctx = await page.context().browser()!.newContext();
  const ap = await ctx.newPage();
  ap.on('console', (m) => m.type() === 'error' && errors.push('admin: ' + m.text()));
  console.log('ADMIN LANDING', await signIn(ap, 'admin'));
  await nav(ap, '/admin/profile', /Profile/i);
  await settle(ap);
  const adm = await values(ap);
  console.log('ADMIN PROFILE', JSON.stringify(adm));
  expect(adm['username-input']).toBe('ravi');
  console.log('ADMIN /admin/profile user-select count', await ap.locator('[data-testid$="user-select"]').count());
  for (const [route, id] of [['/admin/experience', 'experience-user-select'], ['/admin/skills', 'skills-user-select'], ['/admin/awards', 'awards-user-select']]) {
    await nav(ap, route);
    await settle(ap);
    await expect(ap.locator(`[data-testid="${id}"]`)).toBeVisible();
    console.log('ADMIN', route, 'selector present');
  }
  await nav(ap, '/admin/skills');
  await settle(ap);
  await ap.click('[data-testid="skills-user-select"]');
  const opts = await ap.getByRole('option').allInnerTexts();
  console.log('SKILLS SELECTOR OPTIONS', opts);
  await ap.keyboard.press('Escape');
  console.log('CONSOLE ERRORS', errors);
  await ctx.close();
});
