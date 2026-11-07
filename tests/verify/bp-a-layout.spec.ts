import { test } from '@playwright/test';
import { loginHard, open, hOverflow, SHOT } from './bp-a-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/layout`;
test.use({ actionTimeout: 25000 });
const navState = (page: any) => page.evaluate(() => {
  const items = Array.from(document.querySelectorAll('[data-testid^="nav-"]')) as HTMLElement[];
  const labels = Array.from(document.querySelectorAll('[data-sidebar="group-label"], [data-testid="admin-sidebar"] [class*="GroupLabel"]')).map((n) => n.textContent?.trim());
  return {
    ids: items.map((n) => n.getAttribute('data-testid')),
    active: items.filter((n) => n.getAttribute('aria-current') === 'page' || n.getAttribute('data-active') === 'true' || /\bactive\b/.test(n.className)).map((n) => n.getAttribute('data-testid')),
    sample: items.slice(0, 2).map((n) => n.outerHTML.slice(0, 260)),
    groupText: Array.from(document.querySelectorAll('[data-testid="admin-sidebar"] *')).filter((n) => n.children.length === 0 && ['Content', 'Taxonomy', 'Media', 'Resume', 'Audience', 'System'].includes(n.textContent?.trim() || '')).map((n) => n.textContent!.trim()),
    path: location.pathname, shell: !!document.querySelector('[data-testid="admin-sidebar"]'), topbar: !!document.querySelector('[data-testid="admin-topbar"]'),
  };
});
test('builder evidence (no assertions, superseded by vall-admin.spec.ts): layout walkthrough', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = { roles: {} };
  const errs: string[] = [];
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  R.adminLanding = await loginHard(page, 'admin');
  R.activeByRoute = {};
  for (const [route, ready] of [['/AdminDashboard', '[data-testid="stat-posts-value"]'], ['/BlogsList', '[data-testid="posts-grid"], [data-testid="posts-empty"]'], ['/admin/series', '[data-testid="series-grid"]'], ['/users', '[data-testid="users-grid"]'], ['/admin/subscribers', '[data-testid="subscribers-grid"]'], ['/settings', '[data-testid="site-title"]'], ['/admin/categories', '[data-testid="categories-grid"]'], ['/CommentsList', '[data-testid="comments-grid"]']]) {
    await open(page, route, ready);
    await page.waitForTimeout(2500);
    const s = await navState(page);
    R.activeByRoute[route] = { path: s.path, active: s.active, shell: s.shell, topbar: s.topbar };
    if (route === '/BlogsList') { R.sampleHtml = s.sample; R.adminIds = s.ids; R.adminGroups = s.groupText; }
  }
  const stateAdmin = await page.evaluate(() => { const a = document.querySelector('[data-testid="nav-comments"]') as HTMLElement; return a ? { href: a.getAttribute('href'), cls: a.className.slice(0, 200) } : null; });
  R.navComments = stateAdmin;
  await page.screenshot({ path: `${OUT}/admin-active.png` });
  await ctx.close();
  for (const role of ['editor', 'author', 'contributor'] as const) {
    const c = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const p = await c.newPage();
    try {
      const landing = await loginHard(p, role);
      await p.waitForTimeout(3000);
      const s = await navState(p);
      R.roles[role] = { landing, path: s.path, shell: s.shell, ids: s.ids, groups: s.groupText };
      await p.screenshot({ path: `${OUT}/${role}-landing.png` });
    } catch (e) { R.roles[role] = { error: String(e).slice(0, 300) }; }
    await c.close();
  }
  R.errs = errs.slice(0, 5);
  fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
});
