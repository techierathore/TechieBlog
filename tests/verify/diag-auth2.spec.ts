import { test, expect } from '@playwright/test';

const BASE = 'https://localhost:7520';

/**
 * Diagnostic 2: does the admin session survive (a) staying on the landing page,
 * (b) in-app SPA navigation, (c) a hard reload? Distinguishes broken auth from
 * broken auth PERSISTENCE.
 */
test('diagnose auth persistence', async ({ page }) => {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]');
  await page.fill('[data-testid="login-email"]', 'Ravi@techieblog.com');
  await page.fill('[data-testid="login-password"]', 'admin_password');
  await page.click('[data-testid="login-submit"]');
  await page.waitForTimeout(7000);

  console.log('A) landed on:', page.url());
  const body = await page.locator('body').innerText();
  console.log('A) looks like admin?', /dashboard|posts|admin/i.test(body.slice(0, 400)));
  console.log('A) first heading:', await page.locator('h1,h2,h3').first().innerText().catch(() => '(none)'));

  // What auth material is actually stored client-side?
  const storage = await page.evaluate(() => {
    const out: Record<string, string> = {};
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i)!;
      out[k] = (localStorage.getItem(k) || '').slice(0, 40);
    }
    return { local: out, cookies: document.cookie.slice(0, 200) };
  });
  console.log('B) localStorage keys:', JSON.stringify(storage.local));
  console.log('B) cookies:', storage.cookies || '(none)');

  // In-app SPA navigation via a rendered link, if one exists.
  const navLink = page.locator('a[href="/admin/newsletter"], a[href="/BlogsList"]').first();
  if (await navLink.count() > 0) {
    await navLink.click();
    await page.waitForTimeout(4000);
    console.log('C) after SPA link click:', page.url());
  } else {
    console.log('C) no in-app admin link found on the landing page');
  }

  // Hard reload of an admin route.
  await page.goto(`${BASE}/admin`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(4000);
  console.log('D) after hard nav to /admin:', page.url());

  expect(true).toBe(true);
});
