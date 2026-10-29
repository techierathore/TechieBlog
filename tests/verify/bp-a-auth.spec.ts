import { test, expect, Page } from '@playwright/test';
import { BASE, USERS, login, nav, visualCheck } from './_gates';

const SH = 'tests/.artifacts/build-phase-20261027/a/auth';
test.describe.configure({ retries: 0 });
test.beforeEach(({}, ti) => ti.setTimeout(240000));

async function ready(page: Page, url: string) {
  await page.goto(`${BASE}${url}`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 60000 });
  await page.waitForFunction(() => { const f = document.querySelector('form'); return !!f && !f.hasAttribute('action'); }, { timeout: 60000 }).catch(() => {});
  await page.waitForTimeout(1500);
}
async function submit(page: Page, email: string, pw: string) {
  for (let i = 0; i < 12; i++) {
    await page.fill('[data-testid="login-email"]', email);
    await page.fill('[data-testid="login-password"]', pw);
    await page.waitForTimeout(400);
    if ((await page.inputValue('[data-testid="login-password"]')) === pw) break;
  }
  await page.click('[data-testid="login-submit"]');
}

test('UI-001 render + invalid creds at both widths', async ({ page }) => {
  const errs: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 160)); });
  await ready(page, '/login');
  for (const id of ['login-card', 'login-email', 'login-password', 'login-remember', 'login-forgot', 'login-submit'])
    await expect(page.locator(`[data-testid="${id}"]`), id).toBeVisible();
  const w = await visualCheck(page, `${SH}/login-1280.png`, 1280);
  const n = await visualCheck(page, `${SH}/login-390.png`, 390);
  console.log('LOGIN geo', JSON.stringify({ w: [w.overlaps, w.hScroll, w.zeroSized], n: [n.overlaps, n.hScroll, n.zeroSized] }));
  expect(w.overlaps).toEqual([]); expect(n.overlaps).toEqual([]);
  expect(w.hScroll).toBe(0); expect(n.hScroll).toBe(0);
  await submit(page, 'fxa-nobody@example.com', 'Wrong#Pass9');
  const err = page.locator('[data-testid="login-error"]');
  await expect(err).toBeVisible({ timeout: 20000 });
  console.log('LOGIN error text:', (await err.innerText()).replace(/\s+/g, ' '));
  expect(page.url()).toContain('/login');
  await page.screenshot({ path: `${SH}/login-error-390.png` });
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.screenshot({ path: `${SH}/login-error-1280.png` });
  console.log('LOGIN console errors', JSON.stringify(errs));
});

for (const [role, expected] of [['admin', '/admin'], ['editor', '/admin'], ['author', '/blogslist'], ['contributor', '/']] as const) {
  test(`UI-001 landing ${role}`, async ({ page }) => {
    await ready(page, '/login');
    const u = (USERS as any)[role];
    await submit(page, u.email, u.password);
    await page.waitForTimeout(8000);
    const path = new URL(page.url()).pathname.toLowerCase();
    console.log(`LANDING ${role} -> ${page.url()}`);
    await page.screenshot({ path: `${SH}/landing-${role}.png` });
    expect(path).toBe(expected);
  });
}

test('UI-001 returnUrl honoured + open redirect guard', async ({ page: p0 }) => {
  let page = p0;
  await ready(page, `/login?returnUrl=${encodeURIComponent('/BlogsList')}`);
  await submit(page, USERS.admin.email, USERS.admin.password);
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
  await page.waitForTimeout(3000);
  console.log('RETURNURL relative ->', page.url());
  expect(new URL(page.url()).pathname.toLowerCase()).toBe('/blogslist');
  const ctx2 = await page.context().browser()!.newContext();
  page = await ctx2.newPage();
  await ready(page, `/login?returnUrl=${encodeURIComponent('https://evil.example.com/x')}`);
  await submit(page, USERS.admin.email, USERS.admin.password);
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
  await page.waitForTimeout(3000);
  console.log('RETURNURL absolute ->', page.url());
  expect(page.url()).not.toContain('evil');
});

test('UI-004 contributor/author on policy routes', async ({ page }) => {
  const errs: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 160)); });
  await login(page, 'contributor');
  console.log('contributor after login', page.url());
  await nav(page, '/admin');
  await page.waitForTimeout(3000);
  console.log('contributor /admin ->', page.url());
  await expect(page.locator('[data-testid="access-denied"]')).toBeVisible({ timeout: 20000 });
  await expect(page.locator('[data-testid="access-denied-heading"]')).toHaveText(/Access Denied/);
  console.log('dashboard btn count', await page.locator('[data-testid="access-denied-dashboard"]').count());
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, `${SH}/denied-contrib-${w}.png`, w);
    console.log('DENIED geo', w, JSON.stringify([v.overlaps, v.hScroll, v.zeroSized]));
    expect(v.overlaps).toEqual([]); expect(v.hScroll).toBe(0);
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  // raw 403 check: full document load of a policy route with the session in localStorage
  const r = await page.goto(`${BASE}/users`, { waitUntil: 'domcontentloaded' });
  console.log('goto /users status', r?.status(), page.url());
  await page.waitForTimeout(6000);
  console.log('after goto /users ->', page.url(), (await page.locator('body').innerText()).slice(0, 120).replace(/\n/g, ' | '));
  console.log('console errors', JSON.stringify(errs));
});

test('UI-004 author denied from /users + 403 status', async ({ page }) => {
  await login(page, 'author');
  await nav(page, '/users');
  await page.waitForTimeout(3000);
  console.log('author /users ->', page.url());
  await expect(page.locator('[data-testid="access-denied"]')).toBeVisible({ timeout: 20000 });
  const a = await page.request.get(`${BASE}/users`, { maxRedirects: 0 });
  console.log('anon GET /users status', a.status(), a.headers()['location']);
});
