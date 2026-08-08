import { test, expect } from '@playwright/test';

const BASE = 'https://localhost:7520';

/** Diagnostic: where does login land, and which admin routes are reachable afterwards? */
test('diagnose admin auth', async ({ page }) => {
  const errors: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });

  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]');
  await page.fill('[data-testid="login-email"]', 'Ravi@techieblog.com');
  await page.fill('[data-testid="login-password"]', 'admin_password');
  await page.click('[data-testid="login-submit"]');
  await page.waitForTimeout(6000);
  console.log('AFTER LOGIN URL:', page.url());

  const alert = await page.locator('[role="alert"]').count();
  if (alert > 0) console.log('LOGIN ALERT:', await page.locator('[role="alert"]').first().innerText());

  for (const route of ['/admin', '/admin/newsletter', '/admin/analytics', '/BlogsList', '/users']) {
    await page.goto(`${BASE}${route}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(2500);
    const heading = await page.locator('h1, h2, h3').first().innerText().catch(() => '(none)');
    console.log(`ROUTE ${route} -> ${page.url()} | heading: ${heading.slice(0, 60)}`);
  }

  console.log('CONSOLE ERRORS:', errors.slice(0, 5).join(' || ') || '(none)');
  expect(true).toBe(true);
});
