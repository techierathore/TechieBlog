import { test } from '@playwright/test';
import { loginHard, open, SHOT } from './bp-a-helpers';
test.use({ actionTimeout: 25000 });
test('active highlight', async ({ browser }) => {
  test.setTimeout(300000);
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  await loginHard(page, 'admin');
  for (const [route, ready, id] of [['/BlogsList', '[data-testid="posts-grid"]', 'nav-posts'], ['/CommentsList', '[data-testid="comments-grid"]', 'nav-comments'], ['/comments', '[data-testid="comments-grid"]', 'nav-comments'], ['/admin/series', '[data-testid="series-grid"]', 'nav-series']]) {
    await open(page, route, ready); await page.waitForTimeout(3000);
    const r = await page.evaluate((id) => { const n = document.querySelector(`[data-testid="${id}"]`) as HTMLElement; const cs = getComputedStyle(n); const others = Array.from(document.querySelectorAll('[data-testid^="nav-"]')).filter((x) => x.getAttribute('aria-current') === 'page').map((x) => x.getAttribute('data-testid')); return { path: location.pathname, current: others, bg: cs.backgroundColor, color: cs.color, fw: cs.fontWeight, cls: n.className.match(/[^ ]*(current|active)[^ ]*/g) }; }, id);
    console.log('ROW', route, id, JSON.stringify(r));
    await page.screenshot({ path: `${SHOT}/layout/active-${id}-${route.replace(/\W/g, '_')}.png`, clip: { x: 0, y: 60, width: 260, height: 400 } });
  }
});
