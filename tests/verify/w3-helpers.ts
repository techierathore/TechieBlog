/**
 * w3-helpers.ts — shared plumbing for the W3 (authoring + media) verify specs, 2026-10-27.
 *
 * Not a spec file. Everything here drives the app through its own admin UI; the only database access
 * is READ-ONLY psql (the verify phase forbids writes, `guard-db.sh` enforces it).
 */
import { expect, Page, Browser } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import { loginHard, goTo, pickSelect, setMarkdown, fillCommitted } from './vall-authoring-helpers';

export { loginHard, goTo, pickSelect, setMarkdown, fillCommitted };
export const BASE = process.env.TB_BASE ?? 'http://localhost:5450';
export const SHOTS = 'tests/.artifacts/verify/w3';

/** Read-only SQL; returns the trimmed scalar / first row. Single quotes in values must be doubled by the caller. */
export function psql(sql: string): string {
  return execFileSync('docker', ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql], { encoding: 'utf8' }).trim();
}

export function psqlRows(sql: string): string[][] {
  const out = psql(sql);
  return out ? out.split('\n').map((r) => r.split('|')) : [];
}

/** Re-reads a scalar until `done(value)`, or the deadline passes; returns the last value. */
export async function pollDb(sql: string, done: (v: string) => boolean, timeoutMs = 45000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  let last = '';
  while (Date.now() < deadline) {
    last = psql(sql);
    if (done(last)) return last;
    await new Promise((r) => setTimeout(r, 1500));
  }
  return last;
}

export const q = (s: string) => s.replace(/'/g, "''");
export const slugOf = (s: string) => s.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');

export async function newAdminPage(browser: Browser, role: 'admin' | 'editor' | 'author' = 'admin', width = 1280): Promise<Page> {
  const ctx = await browser.newContext({ viewport: { width, height: 900 } });
  const page = await ctx.newPage();
  await loginHard(page, role);
  return page;
}

export function postId(title: string): string {
  return psql(`SELECT postid FROM blogpost WHERE title = '${q(title)}' AND isdeleted IS NOT TRUE ORDER BY postid DESC LIMIT 1`);
}

export interface PostSpec {
  title: string;
  md?: string;
  excerpt?: string;
  category?: RegExp | string;
  tags?: string[];
  series?: RegExp | string;
  /** 'draft' (default) saves a draft; 'publish' presses Publish. */
  mode?: 'draft' | 'publish';
}

/** Creates a post through the real editor and returns its id. */
export async function createPost(page: Page, p: PostSpec): Promise<string> {
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(page, '/ManagePost', '[data-testid="post-title-input"]', 120000);
  await fillCommitted(page, 'post-title-input', p.title);
  expect(await setMarkdown(page, p.md ?? `## ${p.title}\n\nBody of **${p.title}**.\n`), 'the editor holds the body').toBe(true);
  await fillCommitted(page, 'post-excerpt-input', p.excerpt ?? `${p.title} excerpt.`);
  if (p.category) await pickSelect(page, 'category-select', p.category);
  for (const t of p.tags ?? []) {
    await fillCommitted(page, 'tag-input', t);
    await page.click('[data-testid="add-tag"]');
    await page.waitForTimeout(1200);
  }
  if (p.series) await pickSelect(page, 'series-select', p.series);
  await page.click(p.mode === 'publish' ? '[data-testid="publish-post"]' : '[data-testid="save-draft"]');
  const want = p.mode === 'publish' ? 't' : 'f';
  const row = await pollDb(
    `SELECT published::text FROM blogpost WHERE title = '${q(p.title)}' AND isdeleted IS NOT TRUE`,
    (v) => v.startsWith(want),
    60000,
  );
  expect(row, `post "${p.title}" saved (${p.mode ?? 'draft'})`).toMatch(new RegExp(`^${want}`));
  await page.waitForTimeout(1500);
  return postId(p.title);
}

/** Opens an existing post in the editor via the list (leaving the editor first, see vall-authoring). */
export async function openPost(page: Page, id: string | number) {
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(page, `/ManagePost/${id}`, '[data-testid="post-title-input"]', 120000);
  await expect(page.locator('[data-testid="post-title-input"]')).not.toHaveValue('', { timeout: 30000 });
}

/** Creates a category through /admin/category. */
export async function createCategory(page: Page, name: string, description = 'fxv3 fixture') {
  if (psql(`SELECT count(*) FROM category WHERE categoryname = '${q(name)}'`) !== '0') return;
  await goTo(page, '/admin/categories', '[data-testid="categories-grid"], [data-testid="categories-empty"]', 120000);
  await goTo(page, '/admin/category', '[data-testid="category-name-input"]', 120000);
  await fillCommitted(page, 'category-name-input', name);
  await fillCommitted(page, 'category-description-input', description);
  await page.click('[data-testid="save-category"]');
  expect(await pollDb(`SELECT count(*) FROM category WHERE categoryname = '${q(name)}'`, (v) => v === '1'), `category ${name} created`).toBe('1');
}

/** Creates a series through /admin/series/new. */
export async function createSeries(page: Page, name: string, description = 'fxv3 fixture') {
  if (psql(`SELECT count(*) FROM blogseries WHERE name = '${q(name)}'`) !== '0') return;
  await goTo(page, '/admin/series', '[data-testid="series-grid"], [data-testid="series-empty"]', 120000);
  await goTo(page, '/admin/series/new', '[data-testid="series-name-input"]', 120000);
  await fillCommitted(page, 'series-name-input', name);
  await fillCommitted(page, 'series-description-input', description);
  await page.click('[data-testid="save-series"]');
  expect(await pollDb(`SELECT count(*) FROM blogseries WHERE name = '${q(name)}'`, (v) => v === '1'), `series ${name} created`).toBe('1');
}

/** Navigates to a list route even when the SPA is already there (navigateTo the same route is a no-op). */
export async function freshList(page: Page, route: string, ready: string) {
  const other = route.toLowerCase().startsWith('/blogslist') ? '/admin/series' : '/BlogsList';
  const otherReady = other === '/BlogsList' ? '[data-testid="posts-status-tabs"]' : '[data-testid="series-grid"], [data-testid="series-empty"], [data-testid="series-loading"]';
  await goTo(page, other, otherReady, 120000);
  await goTo(page, route, ready, 120000);
}

/** Deletes the first list row whose name cell matches, through the list's confirm dialog. Returns false if absent. */
export async function deleteListRow(page: Page, o: { route: string; ready: string; nameTid: string; name: string; deleteTid: string; dialogTid: string; confirmTid: string; searchTid?: string }) {
  await freshList(page, o.route, o.ready);
  if (o.searchTid) { await page.fill(`[data-testid="${o.searchTid}"]`, o.name); await page.waitForTimeout(1500); }
  const cell = page.locator(`[data-testid="${o.nameTid}"]`).filter({ hasText: o.name }).first();
  if ((await cell.count()) === 0) return false;
  await cell.locator('xpath=ancestor::tr[1]').locator(`[data-testid="${o.deleteTid}"]`).click();
  await expect(page.locator(`[data-testid="${o.dialogTid}"]`)).toBeVisible({ timeout: 20000 });
  await page.click(`[data-testid="${o.confirmTid}"]`);
  await page.waitForTimeout(3000);
  return true;
}

/** Anonymous GET status for a public path (own cookie jar, no auth). */
export async function publicStatus(page: Page, path: string): Promise<number> {
  const r = await page.context().browser()!.newContext().then(async (c) => {
    const resp = await c.request.get(`${BASE}${path}`, { maxRedirects: 0 });
    const s = resp.status();
    await c.close();
    return s;
  });
  return r;
}

/** Deletes (through the post list's UI) every live post whose title starts with `prefix`: leftovers of an aborted run. */
export async function purgePosts(page: Page, prefix: string) {
  for (let pass = 0; pass < 15; pass++) {
    const titles = psql(`SELECT title FROM blogpost WHERE title LIKE '${q(prefix)}%' AND isdeleted IS NOT TRUE`).split('\n').filter(Boolean);
    if (titles.length === 0) return;
    // longest title first so a search for a short title cannot hit another post's longer row
    const name = titles.sort((a, b) => b.length - a.length)[0];
    await deleteListRow(page, { route: '/BlogsList', ready: '[data-testid="posts-status-tabs"]', searchTid: 'posts-search', nameTid: 'post-row-title', name, deleteTid: 'post-delete', dialogTid: 'post-delete-dialog', confirmTid: 'post-delete-confirm' }).catch(() => false);
  }
}
