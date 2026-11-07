import { test } from '@playwright/test';
import { loginHard, open, SHOT } from './bp-a-helpers';
import * as fs from 'fs';
test.use({ actionTimeout: 25000 });
test('layout active + author', async ({ browser }) => {
  test.setTimeout(500000);
  const R: any = {};
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  await loginHard(page, 'admin');
  for (const [route, ready, id] of [['/BlogsList', '[data-testid="posts-grid"], [data-testid="posts-empty"]', 'nav-posts'], ['/admin/series', '[data-testid="series-grid"]', 'nav-series'], ['/users', '[data-testid="users-grid"]', 'nav-users']]) {
    await open(page, route, ready); await page.waitForTimeout(2500);
    R[route] = await page.evaluate((expected) => Array.from(document.querySelectorAll('[data-testid^="nav-"]')).map((n) => { const a = Array.from(n.attributes).filter((x) => /^(data-(?!testid)|aria-current|class)/.test(x.name) && x.name !== 'class').map((x) => `${x.name}=${x.value}`); const cs = getComputedStyle(n); return { id: n.getAttribute('data-testid'), attrs: a, bg: cs.backgroundColor, fw: cs.fontWeight, hasActiveTokens: /data-\[active=true\]/.test(n.className) }; }).filter((x) => x.id === expected || x.attrs.some((a) => a.includes('active') || a.includes('current')) || true).slice(0, 18), id);
  }
  // author: one attempt each candidate
  const cands = ['TriageYolo2026'];
  R.author = [];
  for (const pw of cands) {
    const c = await browser.newContext(); const p = await c.newPage();
    await p.goto(`${process.env.TB_BASE}/login`, { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('[data-testid="login-email"]');
    await p.waitForFunction(() => { const f = document.querySelector('form'); return !!f && !f.hasAttribute('action'); }, { timeout: 90000 });
    await p.waitForTimeout(1500);
    await p.fill('[data-testid="login-email"]', 'author@techieblog.test'); await p.fill('[data-testid="login-password"]', pw);
    await p.click('[data-testid="login-submit"]');
    await p.waitForTimeout(8000);
    R.author.push({ pw: pw.slice(0, 4) + '...', url: p.url() });
    if (!p.url().includes('login')) {
      await p.waitForTimeout(3000);
      R.authorNav = await p.evaluate(() => ({ path: location.pathname, ids: Array.from(document.querySelectorAll('[data-testid^="nav-"]')).map((n) => n.getAttribute('data-testid')), groups: Array.from(document.querySelectorAll('[data-testid="admin-sidebar"] *')).filter((n) => n.children.length === 0 && ['Content', 'Taxonomy', 'Media', 'Resume', 'Audience', 'System'].includes(n.textContent?.trim() || '')).map((n) => n.textContent!.trim()) }));
    }
    await c.close();
  }
  fs.writeFileSync(`${SHOT}/layout/result2.json`, JSON.stringify(R, null, 1));
});
