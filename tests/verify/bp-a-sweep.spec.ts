import { test } from '@playwright/test';
import { loginHard, goTo } from './vall-authoring-helpers';
import * as fs from 'fs';

const OUT = 'tests/.artifacts/build-phase-20261027/a/sweep';
const ROUTES: [string, string][] = [
  ['dashboard', '/AdminDashboard'], ['posts', '/BlogsList'], ['users', '/users'], ['comments', '/comments'],
  ['categories', '/admin/categories'], ['tags', '/admin/tags'], ['series', '/admin/series'],
  ['subscribers', '/admin/subscribers'], ['settings', '/settings'],
];

test('sweep admin pages', async ({ browser }) => {
  test.setTimeout(560000);
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  page.on('pageerror', (e) => errs.push('PAGEERROR ' + String(e).slice(0, 200)));
  const landing = await loginHard(page, 'admin');
  const report: any = { landing, pages: {} };
  for (const w of [1280, 390]) {
    await page.setViewportSize({ width: w, height: 800 });
    for (const [name, route] of ROUTES) {
      errs.length = 0;
      try {
        await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
        await page.waitForTimeout(4000);
        const info = await page.evaluate(() => ({
          url: location.pathname,
          sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth,
          boundary: !!document.querySelector('#blazor-error-ui[style*="block"]'),
          text: document.body.innerText.replace(/\s+/g, ' ').slice(0, 700),
        }));
        report.pages[`${name}@${w}`] = { ...info, errs: [...errs] };
        await page.screenshot({ path: `${OUT}/${name}-${w}.png`, fullPage: true });
      } catch (e) { report.pages[`${name}@${w}`] = { error: String(e).slice(0, 200) }; }
    }
  }
  fs.writeFileSync(`${OUT}/sweep.json`, JSON.stringify(report, null, 1));
});
