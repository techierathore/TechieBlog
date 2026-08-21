/**
 * REQ-UI-017 §4b follow-up: at 390px the "Scheduled (n)" tab is visibly cut off at the viewport
 * edge. This decides whether that is an acceptable scroll-inside-its-own-container pattern or a
 * genuinely unreachable control, by measuring the tab strip's overflow and the tab's own box.
 */
import { test } from '@playwright/test';
import { loginHard, goTo, SHOTS } from './vall-authoring-helpers';

test('REQ-UI-017 mobile tab strip overflow measurement', async ({ page }) => {
  test.setTimeout(240000);
  await loginHard(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1500);

  const measurement = await page.evaluate(() => {
    const tab = document.querySelector('[data-testid="posts-tab-scheduled"]') as HTMLElement;
    const strip = tab?.closest('[role="tablist"]') as HTMLElement | null;
    const r = tab?.getBoundingClientRect();
    const s = strip?.getBoundingClientRect();
    return {
      viewport: document.documentElement.clientWidth,
      tabText: tab?.textContent?.trim(),
      tabRight: r ? Math.round(r.right) : null,
      tabVisibleWidth: r ? Math.round(Math.min(r.right, document.documentElement.clientWidth) - r.left) : null,
      tabFullWidth: r ? Math.round(r.width) : null,
      stripScrollWidth: strip?.scrollWidth ?? null,
      stripClientWidth: strip?.clientWidth ?? null,
      stripOverflowX: strip ? getComputedStyle(strip).overflowX : null,
      stripRight: s ? Math.round(s.right) : null,
      pageScrollWidth: document.documentElement.scrollWidth,
    };
  });
  console.log('TABCLIP ' + JSON.stringify(measurement, null, 1));

  // Can the tab actually be used? Try scrolling the strip and clicking it.
  await page.evaluate(() => {
    const tab = document.querySelector('[data-testid="posts-tab-scheduled"]');
    tab?.scrollIntoView({ inline: 'end', block: 'nearest' });
  });
  await page.waitForTimeout(1000);
  const afterScroll = await page.evaluate(() => {
    const r = document.querySelector('[data-testid="posts-tab-scheduled"]')!.getBoundingClientRect();
    return { right: Math.round(r.right), viewport: document.documentElement.clientWidth };
  });
  console.log('AFTERSCROLL ' + JSON.stringify(afterScroll));
  await page.screenshot({ path: `${SHOTS}/ui017-tabstrip-390.png` });

  await page.click('[data-testid="posts-tab-scheduled"]');
  await page.waitForTimeout(2500);
  const rows = await page.$$eval('[data-testid="post-row-title"]', (n) => n.map((x) => (x.textContent || '').trim()));
  console.log('SCHEDULED TAB ROWS ' + JSON.stringify(rows));
  await page.screenshot({ path: `${SHOTS}/ui017-scheduled-tab-390.png` });
});
