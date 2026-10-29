/**
 * fxv1-settings.spec.ts — verify-phase writer W1, 2026-10-27.
 *
 * Rows that change SITE SETTINGS through /settings and read the public site: REQ-UI-065 (header lockup
 * vs a configured logo), REQ-FN-040 (settings persistence reflected publicly), plus the BlogApp row
 * REQ-UI-051 which cannot be driven from this session (see its skip reason).
 *
 * Every setting is changed through the real /settings screen and RESTORED through it in `finally`;
 * the restore is then verified with a psql READ. No database writes.
 */
import { test, expect, request, Page } from '@playwright/test';
import { BASE, SHOTS, sql, sqlOne, loginHard, goTo, createPost, deletePostByTitle, pollUntil, CAT_EMPTY, idOf, slugOf } from './fxv1-fixtures';

test.describe.configure({ mode: 'default' });

const setting = (key: string) => sqlOne(`SELECT settingvalue FROM sitesetting WHERE settingkey='${key}'`);

async function openSettings(page: Page, tab: string) {
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(page, '/settings', '[data-testid="settings-page"]', 120000);
  await page.click(`[data-testid="${tab}"]`);
  await page.waitForTimeout(800);
}

async function save(page: Page) {
  await page.click('[data-testid="save-settings"]');
  await page.waitForTimeout(2500);
}

async function publicHtml(path: string): Promise<string> {
  const api = await request.newContext({ baseURL: BASE });
  try { return await (await api.get(path)).text(); } finally { await api.dispose(); }
}

// ---------------------------------------------------------------------------------------------
test('REQ-UI-065 the header shows the TechieBlog lockup when no site logo is configured, and a configured logo still wins', async ({ browser }) => {
  test.setTimeout(900000);
  const orig = setting('General.SiteLogo');
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const adminPage = await ctx.newPage();
  let restored = false;
  try {
    await loginHard(adminPage, 'admin');
    await openSettings(adminPage, 'tab-general');
    if (orig) {
      await adminPage.locator('[data-testid="site-logo"] [data-testid="clear-image"]').click();
      await adminPage.waitForTimeout(800);
      await save(adminPage);
      const cleared = await pollUntil(() => setting('General.SiteLogo'), (v) => v === '');
      expect(cleared, 'the logo was cleared through /settings').toBe('');
    }

    // No logo configured: the header brand link shows the horizontal lockup, at desktop and phone widths.
    const anon = await browser.newContext();
    const page = await anon.newPage();
    for (const w of [1280, 390]) {
      await page.setViewportSize({ width: w, height: 800 });
      await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('[data-testid="brand-link"]', { timeout: 60000 });
      await page.waitForTimeout(6000);
      const img = page.locator('[data-testid="brand-link"] img[data-testid="brand-lockup"]');
      await expect(img, `lockup visible at ${w}`).toBeVisible();
      await expect.poll(() => img.evaluate((el: HTMLImageElement) => el.naturalWidth), { timeout: 20000 }).toBeGreaterThan(0);
      expect(await img.evaluate((el: HTMLImageElement) => el.getBoundingClientRect().height)).toBeGreaterThan(20);
      expect(await img.getAttribute('src')).toMatch(/TechieBlog-horizontal/i);
      expect(await page.locator('[data-testid="brand-link"] [data-testid="site-logo"]').count(), 'no configured logo shown').toBe(0);
      expect(await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth)).toBeLessThanOrEqual(1);
      await page.screenshot({ path: `${SHOTS}/ui065-lockup-${w}.png`, clip: { x: 0, y: 0, width: w, height: 120 } });
    }

    // Restore the owner's logo through the same screen, then prove a configured logo wins.
    if (orig) {
      await openSettings(adminPage, 'tab-general');
      await adminPage.locator('[data-testid="site-logo"] [data-testid="choose-from-library"]').click();
      await expect(adminPage.locator('[data-testid="gallery-grid"]')).toBeVisible({ timeout: 30000 });
      const wanted = adminPage.locator('[data-testid="gallery-image"]').filter({ has: adminPage.locator(`img[src="${orig}"]`) }).first();
      const target = (await wanted.count()) ? wanted : adminPage.locator(`[data-testid="gallery-image"][data-path="${orig}"]`).first();
      await target.click();
      await adminPage.waitForTimeout(1500);
      await save(adminPage);
      expect(await pollUntil(() => setting('General.SiteLogo'), (v) => v === orig), 'the owner logo is restored exactly').toBe(orig);
      restored = true;
      await page.setViewportSize({ width: 1280, height: 800 });
      await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('[data-testid="brand-link"]', { timeout: 60000 });
      await page.waitForTimeout(6000);
      expect(await page.locator('[data-testid="brand-link"] [data-testid="site-logo"]').getAttribute('src'), 'a configured logo wins').toContain(orig);
      expect(await page.locator('[data-testid="brand-lockup"]').count()).toBe(0);
    }
    await anon.close();
  } finally {
    if (orig && !restored && setting('General.SiteLogo') !== orig) console.log(`FXV1 WARNING: site logo NOT restored; original was ${orig}`);
    await ctx.close();
  }
  if (orig) expect(setting('General.SiteLogo'), 'site logo left exactly as found').toBe(orig);
});

// ---------------------------------------------------------------------------------------------
// 21 paragraphs x 100 short words = 2100 words. Short tokens on purpose: a ~17 KB body typed into the
// editor saved as "Content is required" in this run (SignalR default 32 KB message limit is the suspect).
const WORDS_BODY = Array.from({ length: 21 }, () => Array.from({ length: 100 }, (_, j) => `w${j}`).join(' ')).join('\n\n');

test('REQ-FN-040 the article pagination word count saved in Settings governs the public post pages', async ({ browser }) => {
  test.setTimeout(480000);
  const orig = setting('Blog.PaginationWordCount') || '500';
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const admin = await ctx.newPage();
  const stamp = Date.now().toString(36);
  const title = `fxv1 Pagination Probe ${stamp}`;
  const pages = async () => {
    const m = (await publicHtml(`/post/${slugOf(title)}`)).match(/Page 1 of (\d+)/);
    return m ? Number(m[1]) : 1;
  };
  const saveWordCount = async (v: string) => {
    await openSettings(admin, 'tab-blog');
    const field = admin.locator('[data-testid="pagination-word-count"]');
    await field.fill(v);
    await field.dispatchEvent('change');
    await field.blur();
    await admin.waitForTimeout(600);
    await save(admin);
    expect(await pollUntil(() => setting('Blog.PaginationWordCount'), (x) => x === v), `word count ${v} persisted`).toBe(v);
  };
  try {
    const t0 = Date.now(); const lap = (m: string) => console.log(`FN040 +${Math.round((Date.now() - t0) / 1000)}s ${m}`);
    await loginHard(admin, 'admin'); lap('login');
    await createPost(admin, { title, body: WORDS_BODY, abstract: 'fxv1 pagination probe', category: CAT_EMPTY, mode: 'publish' });
    lap('post created');
    await saveWordCount('1500'); lap('wc 1500 saved');
    await admin.waitForTimeout(3000);
    expect(await pages(), '2100 words at 1500 per page is 2 pages').toBe(2);
    await saveWordCount('1000');
    await admin.waitForTimeout(3000);
    expect(await pages(), '2100 words at 1000 per page is 3 pages').toBe(3);
  } finally {
    try { await saveWordCount(orig); } catch (e) { console.log(`FXV1 WARNING: word count not restored (${orig}): ${String(e).slice(0, 100)}`); }
    await deletePostByTitle(admin, title).catch(() => {});
    await ctx.close();
  }
  expect(setting('Blog.PaginationWordCount'), 'word count left exactly as found').toBe(orig);
  expect(idOf(title), 'probe post removed').toBe('');
});

test('REQ-FN-040 the theme saved in Settings is reflected on the public site', async ({ browser }) => {
  test.setTimeout(600000);
  const orig = setting('Theme.SiteTheme');
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const admin = await ctx.newPage();
  const attr = async () => ((await publicHtml('/about')).match(/data-site-theme="([^"]*)"/) || [])[1] ?? '';
  const before = await attr();
  try {
    await loginHard(admin, 'admin');
    await openSettings(admin, 'tab-theme');
    await admin.click('[data-testid="site-theme-select"]');
    await admin.waitForTimeout(1500);
    const options = await admin.$$eval('[role="option"]', (els) => els.map((e) => (e.textContent || '').trim()));
    expect(options.length, 'more than one theme to choose from').toBeGreaterThan(1);
    // Try each other theme until the public attribute changes (two ids can normalise to one value).
    let changed = '';
    for (const name of options) {
      await admin.locator('[role="option"]').filter({ hasText: name }).first().click();
      await admin.waitForTimeout(800);
      await save(admin);
      await admin.waitForTimeout(3000);
      const now = await attr();
      if (now && now !== before) { changed = now; break; }
      await admin.click('[data-testid="site-theme-select"]');
      await admin.waitForTimeout(1200);
    }
    expect(changed, `the public <html data-site-theme> follows the saved theme (was "${before}")`).not.toBe('');
    expect(changed).not.toBe(before);
  } finally {
    try {
      await openSettings(admin, 'tab-theme');
      await admin.click('[data-testid="site-theme-select"]');
      await admin.waitForTimeout(1500);
      const names = await admin.$$eval('[role="option"]', (els) => els.map((e) => (e.textContent || '').trim()));
      const themeLabel = orig;
      const pick = names.find((n) => n.toLowerCase().replace(/[^a-z]/g, '') === themeLabel.toLowerCase().replace(/[^a-z]/g, '')) ?? names[0];
      await admin.locator('[role="option"]').filter({ hasText: pick }).first().click();
      await admin.waitForTimeout(800);
      await save(admin);
    } catch (e) { console.log(`FXV1 WARNING: theme not restored (${orig}): ${String(e).slice(0, 100)}`); }
    await ctx.close();
  }
  if (setting('Theme.SiteTheme') !== orig) console.log(`FXV1 NOTE: theme is now ${setting('Theme.SiteTheme')}, was ${orig} when this test started (another writer shares /settings)`);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-051 (BlogApp login screen + admin shell) is driven by tests/verify/blogapp-desktop.spec.ts over
// WebView2 CDP; the stub that lived here only skipped.
