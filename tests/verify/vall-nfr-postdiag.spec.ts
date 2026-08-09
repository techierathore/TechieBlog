/**
 * vall-nfr-postdiag.spec.ts — REQ-FN-020 diagnostic.
 *
 * `curl /post/blazor-render-modes-explained` returns server HTML that DOES contain
 * `data-testid="post-readtime"` ("2 min read") and a populated `data-testid="related-posts"`
 * block, yet a Playwright page on the same URL reports both absent. This spec samples the DOM
 * across the prerender -> interactive handover to find where they go.
 */
import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { BASE } from './_gates';

const SHOTS = path.join(process.cwd(), '.verify', 'shots', 'nfr');
fs.mkdirSync(SHOTS, { recursive: true });

test('FN020 post page prerender to interactive handover', async ({ page }) => {
  test.setTimeout(180000);
  const consoleErrors: string[] = [];
  page.on('console', (m) => {
    if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 200));
  });
  page.on('pageerror', (e) => consoleErrors.push('pageerror: ' + String(e).slice(0, 200)));

  const samples: any[] = [];
  const snap = () =>
    page.evaluate(() => ({
      readtime: (document.querySelector('[data-testid="post-readtime"]')?.textContent || '').trim() || null,
      title: (document.querySelector('[data-testid="post-title"]')?.textContent || '').trim() || null,
      relatedCards: document.querySelectorAll('[data-testid="related-post-card"]').length,
      contentChars: (document.querySelector('[data-testid="post-content"]')?.textContent || '').length,
      bodyChars: (document.body?.innerText || '').length,
      docTitle: document.title,
      blazorReady: !!(window as any).Blazor,
    }));

  await page.goto(BASE + '/post/blazor-render-modes-explained', { waitUntil: 'commit' });
  for (const ms of [0, 500, 1500, 3000, 6000, 10000]) {
    await page.waitForTimeout(ms === 0 ? 200 : ms - (samples.at(-1)?.at ?? 0));
    const s = await snap();
    samples.push({ at: ms, ...s });
    console.log(`FINDING REQ-FN-020 :: t=${ms}ms ${JSON.stringify(s)}`);
  }
  console.log(`FINDING REQ-FN-020 :: console errors (${consoleErrors.length}) ${JSON.stringify(consoleErrors.slice(0, 5))}`);
  await page.screenshot({ path: path.join(SHOTS, 'fn020-postdiag.png'), fullPage: true });
  fs.writeFileSync(path.join(SHOTS, 'fn020-postdiag.json'), JSON.stringify({ samples, consoleErrors }, null, 2));
  expect(samples.length).toBe(6);
});
