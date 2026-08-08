import { test, expect } from '@playwright/test';
const BASE = 'https://localhost:7520';
test('dump admin nav', async ({ page }) => {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]');
  await page.fill('[data-testid="login-email"]', 'Ravi@techieblog.com');
  await page.fill('[data-testid="login-password"]', 'admin_password');
  await page.click('[data-testid="login-submit"]');
  await page.waitForTimeout(7000);
  const hrefs = await page.evaluate(() =>
    Array.from(document.querySelectorAll('a[href]')).map(a => (a as HTMLAnchorElement).getAttribute('href')));
  console.log('ADMINLINKS:', JSON.stringify([...new Set(hrefs)].filter(h => h && h.startsWith('/'))));
  expect(true).toBe(true);
});
