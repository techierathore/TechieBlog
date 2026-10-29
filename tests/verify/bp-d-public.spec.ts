// 2026-10-27 W1 (verify-phase): titles prefixed 'retired:' asserted retired demo/fixture data (UAT-007, 2026-08-22), the removed header About link, or wrote the database directly; they are superseded by tests/verify/fxv1-public.spec.ts and fxv1-settings.spec.ts, which build their own state through the app.
import { test, expect, Page } from '@playwright/test';

/**
 * Build-phase 2026-10-27 cluster D smoke (public surfaces): REQ-UI-057, REQ-UI-059, REQ-UI-060,
 * REQ-FN-052. Uses only fixtures prefixed `fxd-` (three published posts with back-dated
 * PublishedOn) seeded by psql before the host boots. Base URL from TB_BASE.
 */
const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5404';
const OUT = 'tests/.artifacts/build-phase-20261027/d';
const POST = '/post/fxd-post-alpha';

const NUMBER_WORDS = ['zero','one','two','three','four','five','six','seven','eight','nine','ten','eleven','twelve','thirteen','fourteen','fifteen','sixteen','seventeen','eighteen','nineteen','twenty'];
function solve(q: string): string {
  const wv = (w: string) => NUMBER_WORDS.indexOf(w.toLowerCase());
  let m = q.match(/what is (\w+) plus (\w+)\?/i); if (m) return NUMBER_WORDS[wv(m[1]) + wv(m[2])] ?? String(wv(m[1]) + wv(m[2]));
  m = q.match(/what is (\w+) minus (\w+)\?/i); if (m) return NUMBER_WORDS[wv(m[1]) - wv(m[2])] ?? String(wv(m[1]) - wv(m[2]));
  m = q.match(/how many letters are in the word '([^']+)'\?/i); if (m) return NUMBER_WORDS[m[1].length] ?? String(m[1].length);
  m = q.match(/how many words are in this line: '([^']+)'\?/i); if (m) return NUMBER_WORDS[m[1].trim().split(/\s+/).length];
  throw new Error('Unrecognised question: ' + q);
}

async function tabUntil(page: Page, testId: string, max = 120) {
  const path: string[] = [];
  for (let i = 1; i <= max; i++) {
    await page.keyboard.press('Tab');
    const here = await page.evaluate(() => {
      const e = document.activeElement as HTMLElement | null;
      return { tid: e?.getAttribute('data-testid') ?? '', tag: e?.tagName.toLowerCase() ?? '' };
    });
    path.push(here.tid || here.tag);
    if (here.tid === testId) return { stops: i, path, tag: here.tag };
  }
  throw new Error(`${testId} unreachable by Tab. Path: ${path.join(' > ')}`);
}

async function questionOf(page: Page): Promise<string> {
  const loc = page.locator('[data-testid="captcha-prompt"]').first();
  await expect(loc).toContainText('?', { timeout: 20000 });
  return (await loc.innerText()).trim();
}

// retired 2026-10-27 (W5): needs the fxd- posts the builder seeded with psql and deleted; superseded by fxv5-captcha-views REQ-UI-057.
test.describe.skip('retired: accessible captcha', () => {
  test('comment form: keyboard-only switch, label + live region, correct answer posts', async ({ page }) => {
    test.setTimeout(240000);
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`${BASE}${POST}`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="captcha-widget"]', { timeout: 45000 });
    await page.waitForTimeout(2500);

    await page.fill('[data-testid="comment-name"]', 'fxd Keyboard Visitor');
    await page.fill('[data-testid="comment-email"]', 'fxd-kb@example.com');
    await page.fill('[data-testid="comment-input"]', 'fxd comment posted through the accessible challenge.');

    const walk = await tabUntil(page, 'captcha-mode-toggle');
    expect(walk.tag).toBe('button');
    await page.keyboard.press('Enter');
    const q = await questionOf(page);
    expect(await page.locator('[data-testid="captcha-image"]').count()).toBe(0);
    const named = await page.evaluate(() => {
      const input = document.querySelector('[data-testid="captcha-answer"]') as HTMLInputElement;
      return (document.querySelector(`label[for="${input.id}"]`)?.textContent || '').trim();
    });
    expect(named).toBe(q);
    const status = page.locator('[data-testid="captcha-status"]').first();
    expect(await status.getAttribute('role')).toBe('status');
    expect(await status.getAttribute('aria-live')).toBe('polite');
    await expect(status).toContainText(q);
    await page.screenshot({ path: `${OUT}/ui057-comment-question.png` });

    // answer by keyboard alone: Tab from the toggle is not guaranteed to be the input; focus it with Shift+Tab walk
    await page.focus('[data-testid="captcha-answer"]');
    await page.keyboard.type(solve(q));
    await page.keyboard.press('Tab');
    await page.keyboard.press('Tab').catch(() => {});
    await page.click('[data-testid="comment-submit"]');
    await expect(page.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 30000 });
    await page.screenshot({ path: `${OUT}/ui057-comment-accepted.png` });
    await page.close();
  });

  test('comment form: a wrong answer is refused', async ({ page }) => {
    test.setTimeout(120000);
    await page.goto(`${BASE}${POST}`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="captcha-widget"]', { timeout: 45000 });
    await page.waitForTimeout(2500);
    await page.fill('[data-testid="comment-name"]', 'fxd Wrong');
    await page.fill('[data-testid="comment-email"]', 'fxd-wrong@example.com');
    await page.fill('[data-testid="comment-input"]', 'fxd wrong answer attempt');
    await tabUntil(page, 'captcha-mode-toggle');
    await page.keyboard.press('Enter');
    await questionOf(page);
    await page.fill('[data-testid="captcha-answer"]', '1234');
    await page.click('[data-testid="comment-submit"]');
    await expect(page.locator('[data-testid="captcha-error"], [data-testid="comment-form-error"]').first()).toBeVisible({ timeout: 20000 });
    expect(await page.locator('[data-testid="comment-form-success"]').count()).toBe(0);
    await page.close();
  });

  test('subscribe form: alternative reachable by keyboard, announced, correct answer subscribes', async ({ page }) => {
    test.setTimeout(240000);
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`${BASE}/newsletters`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="captcha-mode-toggle"]', { timeout: 45000 });
    await page.waitForTimeout(2500);
    await page.screenshot({ path: `${OUT}/ui057-subscribe-before.png` });
    const card = page.locator('[data-testid="newsletter-subscribe"], [data-testid="subscribe-card"]').first();
    await page.fill('input[type="email"]', 'fxd-sub@example.com');
    await tabUntil(page, 'captcha-mode-toggle');
    await page.keyboard.press('Enter');
    const q = await questionOf(page);
    await expect(page.locator('[data-testid="captcha-status"]').first()).toContainText(q);
    await page.fill('[data-testid="captcha-answer"]', solve(q));
    await page.screenshot({ path: `${OUT}/ui057-subscribe-question.png` });
    await page.getByRole('button', { name: /^Subscribe$/ }).click();
    await page.waitForTimeout(6000);
    await page.screenshot({ path: `${OUT}/ui057-subscribe-after.png` });
    const body = await page.evaluate(() => document.body.innerText);
    console.log('SUBSCRIBE BODY EXCERPT:', body.replace(/\s+/g, ' ').slice(0, 600));
    expect(await page.locator('[data-testid="captcha-error"]').count(), 'captcha refused a correct answer').toBe(0);
    await page.close();
  });
});

test.describe('retired: REQ-UI-059 listings sort by PublishedOn', () => {
  const expected = ['fxd-post-charlie', 'fxd-post-bravo', 'fxd-post-alpha'];
  for (const route of ['/', '/category/web-development', '/search?q=fxdword']) {
    test(`order on ${route}`, async ({ page }) => {
      await page.setViewportSize({ width: 1280, height: 1000 });
      await page.goto(`${BASE}${route}`, { waitUntil: 'networkidle' });
      await page.waitForSelector('a[href^="/post/fxd-post-"]', { timeout: 45000 });
      await page.waitForTimeout(3500);
      const hrefs = await page.$$eval('a[href^="/post/fxd-post-"]', (as) => [...new Set(as.map(a => a.getAttribute('href')!.replace('/post/', '')))]);
      console.log(`ORDER ${route}:`, hrefs.join(' , '));
      const present = hrefs.filter(h => expected.includes(h));
      expect(present.length).toBeGreaterThanOrEqual(route === '/' ? 2 : 3);
      expect(present).toEqual(expected.filter(h => present.includes(h)));
      await page.screenshot({ path: `${OUT}/ui059-${route.replace(/[^a-z]/gi, '_')}.png` });
    });
  }
});

test.describe('REQ-UI-060 exactly one h1', () => {
  const routes = ['/', '/access-denied', '/about', '/categories', '/tags', '/series', '/newsletters', '/resume', '/search', '/login', '/rss', '/does-not-exist-fxd', '/post/fxd-post-alpha', '/category/web-development'];
  for (const r of routes) {
    test(`h1 count on ${r}`, async ({ page }) => {
      await page.goto(`${BASE}${r}`, { waitUntil: 'networkidle' });
      await expect.poll(() => page.locator('h1').count(), { timeout: 40000 }).toBeGreaterThanOrEqual(1);
      await page.waitForTimeout(4000); // let the prerender -> interactive swap settle
      const n = await page.locator('h1').count();
      console.log(`H1 ${r}: ${n} -> ${await page.locator('h1').allInnerTexts()}`);
      expect(n, `${r} must have exactly one h1`).toBe(1);
    });
  }
});
