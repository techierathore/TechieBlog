import { test, expect } from '@playwright/test';
import { nav } from './_gates';
import { signIn, settle } from './vall-resume-helpers';
test('admin user selectors list the accounts', async ({ page }) => {
  test.setTimeout(300000);
  await signIn(page, 'admin');
  for (const [route, id] of [['/admin/skills', 'skills-user-select'], ['/admin/awards', 'awards-user-select'], ['/admin/experience', 'experience-user-select']]) {
    await nav(page, route); await settle(page);
    await page.click(`[data-testid="${id}"]`);
    await page.waitForTimeout(1500);
    const opts = await page.getByRole('option').allInnerTexts();
    console.log('OPTIONS', route, JSON.stringify(opts));
    await page.screenshot({ path: `tests/.artifacts/build-phase-20261027/a/profile/sel-${id}.png` });
    expect(opts.length).toBeGreaterThanOrEqual(4);
    await page.keyboard.press('Escape');
  }
});
