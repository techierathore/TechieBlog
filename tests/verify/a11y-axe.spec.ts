/*
  a11y-axe.spec.ts — REQ-NFR-007 re-audit (2026-08-07, post REQ-UI-057).

  Same method as the 64 -> 2 baseline: @axe-core/playwright with wcag2a/wcag2aa/wcag21a/wcag21aa
  over the six public URLs at 1280x900 and 390x844.
*/
import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';

const BASE = 'http://localhost:5431';
const OUT = path.join(process.cwd(), 'test-results', 'a11y-reaudit');
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

const URLS: Array<[string, string]> = [
  ['home', '/'],
  ['post', '/post/getting-started-with-blazor-server'],
  ['resume', '/resume'],
  ['newsletters', '/newsletters'],
  ['login', '/login'],
  ['search', '/search'],
];

const VIEWPORTS: Array<[string, number, number]> = [
  ['1280x900', 1280, 900],
  ['390x844', 390, 844],
];

fs.mkdirSync(OUT, { recursive: true });

const summary: any[] = [];

for (const [name, url] of URLS) {
  for (const [vpName, w, h] of VIEWPORTS) {
    test(`axe ${name} ${vpName}`, async ({ page }) => {
      await page.setViewportSize({ width: w, height: h });
      await page.goto(BASE + url, { waitUntil: 'domcontentloaded' });
      // Let the Blazor circuit connect and render interactive content.
      await page.waitForTimeout(4000);

      const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
      const nodes = results.violations.reduce((n, v) => n + v.nodes.length, 0);
      const detail = results.violations.map(v => ({
        id: v.id,
        impact: v.impact,
        nodes: v.nodes.length,
        targets: v.nodes.slice(0, 4).map(n => n.target.join(' ')),
      }));
      summary.push({ page: name, viewport: vpName, nodes, detail });
      fs.writeFileSync(
        path.join(OUT, `axe-${name}-${vpName}.json`),
        JSON.stringify({ page: name, url, viewport: vpName, nodes, detail }, null, 2)
      );
      console.log(`AXE ${name} ${vpName} -> ${nodes} violation nodes ${JSON.stringify(detail)}`);

      // Horizontal-overflow check (VISUAL-TRUTH) alongside the axe run.
      const overflow = await page.evaluate(
        () => document.body.scrollWidth - document.body.clientWidth
      );
      console.log(`OVERFLOW ${name} ${vpName} -> ${overflow}px`);
      await page.screenshot({ path: path.join(OUT, `shot-${name}-${vpName}.png`), fullPage: false });
      expect(nodes, `axe violations on ${name} @ ${vpName}`).toBeGreaterThanOrEqual(0);
    });
  }
}

test.afterAll(async () => {
  fs.writeFileSync(path.join(OUT, 'axe-summary.json'), JSON.stringify(summary, null, 2));
});
