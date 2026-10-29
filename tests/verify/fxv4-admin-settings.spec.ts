/**
 * fxv4-admin-settings.spec.ts - verify-phase writer W4.
 *
 * Rows: REQ-UI-026 (a saved setting reaches the public site without a restart), REQ-UI-032 (theme chosen
 * in Site Settings reaches an anonymous visitor).
 *
 * Site settings are GLOBAL and other writers read them concurrently, so every test restores the exact
 * original value inside a `finally` and asserts the restore against psql before it ends.
 */
import { test, expect, Page, Browser } from '@playwright/test';
import {
  BASE, psql, psqlInt, sleep, until, fillCommitted, adminSession, openAdmin,
} from './fxv4-helpers';

test.setTimeout(420000);

let browserRef: Browser;
let admin: Page;
let closeAdmin: () => Promise<void>;

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  const s = await adminSession(browser);
  admin = s.page;
  closeAdmin = () => s.ctx.close();
});
test.afterAll(async () => { await closeAdmin?.(); });

const setting = (key: string) => psql(`SELECT COALESCE(SettingValue, '') FROM SiteSetting WHERE SettingKey = '${key}'`);

async function openSettings(tab: string) {
  await openAdmin(admin, '/settings', '[data-testid="site-title"]');
  await sleep(2500);
  await admin.click(`[data-testid="${tab}"]`);
  await sleep(1200);
}

async function save(key: string, expected: string, why: string) {
  await admin.click('[data-testid="save-settings"]');
  expect(await until(() => setting(key) === expected, 30000, 1000), why).toBe(true);
  await sleep(1500);
}

/** A fresh anonymous visitor (no storage) loads the home page. */
async function anonymousHome() {
  const ctx = await browserRef.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, undefined, { timeout: 60000 }).catch(() => {});
  await sleep(3500);
  const info = await page.evaluate(() => ({
    title: document.title,
    theme: document.documentElement.getAttribute('data-site-theme'),
    body: document.body.innerText,
  }));
  await ctx.close();
  return info;
}

test('REQ-UI-026 a saved site title and tagline reach the public site without a restart', async () => {
  const origTitle = setting('General.SiteTitle');
  const origTagline = setting('General.SiteTagline');
  const newTitle = 'fxv4 Site Title';
  const newTagline = 'fxv4 site tagline';
  try {
    await openSettings('tab-general');
    // Every field shows its persisted value.
    expect(await admin.inputValue('[data-testid="site-title"]')).toBe(origTitle);
    expect(await admin.inputValue('[data-testid="site-tagline"]')).toBe(origTagline);

    await fillCommitted(admin, 'site-title', newTitle);
    await fillCommitted(admin, 'site-tagline', newTagline);
    await save('General.SiteTitle', newTitle, 'the title is stored');
    expect(setting('General.SiteTagline')).toBe(newTagline);

    // Public reflects it: poll (the content cache is invalidated by the save, the page is a fresh load).
    let seen = { title: '', theme: '', body: '' };
    await until(async () => {
      seen = await anonymousHome();
      return seen.title.includes(newTagline) || seen.title.includes(newTitle);
    }, 60000, 1000);
    console.log(`[REQ-UI-026] anonymous title = "${seen.title}"`);
    expect(seen.title, 'the anonymous home title carries the new site title').toContain(newTitle);
    expect(seen.title + ' ' + seen.body, 'and the new tagline').toContain(newTagline);

    // Reload the settings screen: it shows what was saved.
    await openSettings('tab-general');
    expect(await admin.inputValue('[data-testid="site-title"]')).toBe(newTitle);
  } finally {
    await openSettings('tab-general').catch(() => {});
    await fillCommitted(admin, 'site-title', origTitle).catch(() => {});
    await fillCommitted(admin, 'site-tagline', origTagline).catch(() => {});
    await admin.click('[data-testid="save-settings"]').catch(() => {});
    await until(() => setting('General.SiteTitle') === origTitle && setting('General.SiteTagline') === origTagline, 30000, 1000);
  }
  expect(setting('General.SiteTitle'), 'original title restored').toBe(origTitle);
  expect(setting('General.SiteTagline'), 'original tagline restored').toBe(origTagline);
});

test('REQ-UI-026 a saved posts-per-page value changes the public listing page size', async () => {
  const orig = setting('Blog.PostsPerPage');
  // A listing with more than 3 results is needed to see a page size of 3.
  const matches = psqlInt(`SELECT COUNT(*) FROM BlogPost WHERE Published = TRUE AND (IsDeleted = FALSE OR IsDeleted IS NULL) AND (Title ILIKE '%fxv%' OR Abstract ILIKE '%fxv%' OR PostContent ILIKE '%fxv%' OR COALESCE(Tags,'') ILIKE '%fxv%')`);
  test.skip(matches < 4, `only ${matches} published posts match the listing query; a page size of 3 cannot be told apart`);
  try {
    await openSettings('tab-blog');
    expect(await admin.inputValue('[data-testid="posts-per-page"]')).toBe(orig);
    await fillCommitted(admin, 'posts-per-page', '3');
    await save('Blog.PostsPerPage', '3', 'posts-per-page is stored');

    const ctx = await browserRef.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await ctx.newPage();
    await page.goto(`${BASE}/search?q=fxv`, { waitUntil: 'domcontentloaded', timeout: 90000 });
    await page.waitForSelector('[data-testid="search-result"]', { timeout: 60000 });
    await sleep(2500);
    const shown = await page.locator('[data-testid="search-result"]').count();
    await page.screenshot({ path: 'tests/.artifacts/verify/W4/ui026-posts-per-page.png' });
    await ctx.close();
    console.log(`[REQ-UI-026] posts-per-page=3, ${matches} matching posts, public listing shows ${shown} on page 1`);
    expect(shown, 'the public listing honours the saved posts-per-page without a restart').toBe(3);
  } finally {
    await openSettings('tab-blog').catch(() => {});
    await fillCommitted(admin, 'posts-per-page', orig).catch(() => {});
    await admin.click('[data-testid="save-settings"]').catch(() => {});
    await until(() => setting('Blog.PostsPerPage') === orig, 30000, 1000);
  }
  expect(setting('Blog.PostsPerPage'), 'original posts-per-page restored').toBe(orig);
});

test('REQ-UI-032 the theme picked in Site Settings persists and the next anonymous page load renders in it', async () => {
  const orig = setting('Theme.SiteTheme') || 'trblaze-modern';
  const target = (['trblaze-modern', 'developer', 'minimal'] as const).find((t) => t !== orig)!;
  try {
    await openSettings('tab-theme');
    await admin.waitForSelector(`[data-testid="theme-swatch-${target}"]`, { state: 'visible', timeout: 30000 });
    await admin.click(`[data-testid="theme-swatch-${target}"]`);
    await sleep(1200);
    await save('Theme.SiteTheme', target, 'the chosen theme is stored');

    let seen = { title: '', theme: '', body: '' };
    await until(async () => { seen = await anonymousHome(); return seen.theme === target; }, 60000, 1000);
    expect(seen.theme, "an anonymous visitor's next page load renders in the new theme").toBe(target);

    // The choice persists across a reload of the settings screen.
    await openSettings('tab-theme');
    expect(setting('Theme.SiteTheme')).toBe(target);
  } finally {
    await openSettings('tab-theme').catch(() => {});
    await admin.click(`[data-testid="theme-swatch-${orig}"]`).catch(() => {});
    await sleep(1000);
    await admin.click('[data-testid="save-settings"]').catch(() => {});
    await until(() => setting('Theme.SiteTheme') === orig, 30000, 1000);
  }
  expect(setting('Theme.SiteTheme'), 'original theme restored').toBe(orig);
});
