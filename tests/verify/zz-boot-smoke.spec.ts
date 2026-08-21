/**
 * zz-boot-smoke.spec.ts — verifier pre-flight. Proves the booted host is reachable, the public
 * shell renders, and every seeded role can sign in, BEFORE the cluster fan-out spends time on it.
 */
import { test, expect } from '@playwright/test';
import { BASE, USERS, login, RoleKey } from './_gates';

test('boot: public home renders', async ({ page }) => {
  const res = await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  expect(res?.status()).toBe(200);
  await page.waitForTimeout(2500);
  const text = await page.locator('body').innerText();
  expect(text.length).toBeGreaterThan(200);
  console.log('HOME-TEXT-HEAD:', text.slice(0, 300).replace(/\s+/g, ' '));
});

for (const role of Object.keys(USERS) as RoleKey[]) {
  test(`boot: ${role} can sign in`, async ({ page }) => {
    const landed = await login(page, role);
    console.log(`LANDED ${role} -> ${landed}`);
    expect(landed).not.toContain('/login');
  });
}
