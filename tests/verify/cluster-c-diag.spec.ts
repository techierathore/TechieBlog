/** Diagnostic: how long do /post and /admin take to actually render data? */
import { test, Page } from '@playwright/test';

const BASE = process.env.TB_BASE ?? 'https://localhost:7373';

test.use({ ignoreHTTPSErrors: true, viewport: { width: 1280, height: 900 } });
test.setTimeout(300000);

async function probe(page: Page, url: string) {
  const errs: string[] = [];
  page.on('pageerror', (e) => errs.push(String(e)));
  page.on('console', (m) => {
    if (m.type() === 'error') errs.push(m.text().slice(0, 200));
  });
  await page.goto(url, { waitUntil: 'networkidle' });
  for (const wait of [1000, 2000, 4000, 8000, 15000]) {
    await page.waitForTimeout(wait);
    const t = (await page.locator('body').innerText()).replace(/\s+/g, ' ').trim();
    console.log(`  after ~${wait}ms extra @${page.url()}: len=${t.length} :: ${t.slice(0, 140)}`);
  }
  if (errs.length) console.log('  ERRORS:', errs.slice(0, 5));
}

test('diag post', async ({ page }) => {
  console.log('--- /post/theming-with-css-custom-properties ---');
  await probe(page, `${BASE}/post/theming-with-css-custom-properties`);
});

test('diag admin', async ({ page }) => {
  const ws = page.waitForEvent('websocket', { timeout: 40000 }).catch(() => null);
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]');
  await ws;
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', 'Ravi@techieblog.com');
  await page.fill('[data-testid="login-password"]', 'admin_password');
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  console.log('landed after login:', page.url());
  for (const r of ['/admin', '/settings', '/BlogsList', '/CommentsList', '/admin/categories']) {
    console.log(`--- ${r} ---`);
    await probe(page, `${BASE}${r}`);
  }
});
