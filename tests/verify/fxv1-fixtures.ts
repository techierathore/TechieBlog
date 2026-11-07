/**
 * fxv1-fixtures.ts — verify-phase writer W1 (public reading), 2026-10-27.
 *
 * The dev database holds no published content (demo content retired 2026-08-22, UAT-007), so every
 * public-reading test creates the state it needs THROUGH THE APP (admin UI) and reads back what the
 * app stored with psql SELECTs. Nothing here writes to the database directly.
 *
 * Idempotent: every `ensure*` looks the row up first, so a recycled Playwright worker (which re-runs
 * `beforeAll` after a failing test) never duplicates anything. Cleanup goes through the admin UI.
 *
 * Fixture rows (all prefixed `fxv1`):
 *   category  "fxv1 Category"  (11 published posts) · "fxv1 Empty" (no posts)
 *   series    "fxv1 Series"    (3 published parts + parts created out of title order)
 *   tags      fxv1tag (4 posts), fxv1quokkatag (1 post)
 *   posts     Alpha/Bravo/Charlie (created A,B,C, PUBLISHED C,B,A so PublishedOn order is the reverse of
 *             CreatedOn order = the back-dated case of REQ-UI-059), Markdown, Search, Long, three
 *             series parts, three fillers, one draft.
 */
import { Page, expect } from '@playwright/test';
import { execSync } from 'node:child_process';
import { loginHard, goTo, pickSelect, setMarkdown, fillCommitted } from './vall-authoring-helpers';

export { loginHard, goTo };
export const BASE = process.env.TB_BASE ?? 'http://localhost:5450';
export const SHOTS = 'tests/.artifacts/verify/w1';

/** Read-only psql. Returns trimmed non-empty rows, columns joined by `|`. */
export function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}
export const sqlOne = (q: string) => sql(q)[0] ?? '';

export const CAT = 'fxv1 Category';
export const CAT_EMPTY = 'fxv1 Empty';
export const SERIES = 'fxv1 Series';
export const TAG = 'fxv1tag';
export const TAG_QUOKKA = 'fxv1quokkatag';

/** Markdown covering every element FN-014 names. */
export const MARKDOWN_BODY = [
  '## fxv1 Second Level Heading',
  '',
  '### fxv1 Third Level Heading',
  '',
  'A paragraph with **strong text**, *emphasis* and a [link to the home page](/).',
  '',
  '- bullet one',
  '- bullet two',
  '',
  '1. numbered one',
  '2. numbered two',
  '',
  '```csharp',
  'var fxv1 = 1;',
  '```',
  '',
  '| Col A | Col B |',
  '| ----- | ----- |',
  '| cell1 | cell2 |',
  '',
  '> a block quote',
].join('\n');

/** ~410 words -> ceil(410 / 200) = 3 minutes. */
export const LONG_BODY = Array.from({ length: 41 }, (_, i) => `fxv1 long paragraph ${i} lorem ipsum dolor sit amet consectetur adipiscing elit sed do.`).join('\n\n');

export interface PostSpec {
  title: string;
  body?: string;
  abstract?: string;
  category?: string;
  tags?: string[];
  series?: string;
  /** 'publish' = create and publish in one step; 'draft' = leave as draft. */
  mode: 'publish' | 'draft';
}

/** Specs in creation order. The first three are created as drafts and published later in reverse. */
export const POSTS: PostSpec[] = [
  { title: 'fxv1 Alpha', body: MARKDOWN_BODY, abstract: 'fxv1 alpha abstract', category: CAT, tags: [TAG], mode: 'draft' },
  { title: 'fxv1 Bravo', body: 'fxv1 bravo body.', abstract: 'fxv1 bravo abstract', category: CAT, tags: [TAG], mode: 'draft' },
  { title: 'fxv1 Charlie', body: 'fxv1 charlie body.', abstract: 'fxv1 charlie abstract', category: CAT, tags: [TAG], mode: 'draft' },
  { title: 'fxv1 Markdown Post', body: MARKDOWN_BODY, abstract: 'fxv1 markdown abstract', category: CAT, tags: [TAG], mode: 'publish' },
  { title: 'fxv1 Search Post fxv1quokkatitle', body: 'fxv1quokkabody sits in the body.', abstract: 'fxv1quokkaabstract sits in the abstract.', category: CAT, tags: [TAG_QUOKKA], mode: 'publish' },
  { title: 'fxv1 Long Post', body: LONG_BODY, abstract: 'fxv1 long abstract', category: CAT, tags: [TAG], mode: 'publish' },
  // Created in reverse title order so part order (1,2,3) differs from alphabetical order.
  { title: 'fxv1 Series Part Zulu', body: 'fxv1 series zulu body.', abstract: 'fxv1 series zulu', category: CAT, series: SERIES, mode: 'publish' },
  { title: 'fxv1 Series Part Mike', body: 'fxv1 series mike body.', abstract: 'fxv1 series mike', category: CAT, series: SERIES, mode: 'publish' },
  { title: 'fxv1 Series Part Alpha', body: 'fxv1 series alpha body.', abstract: 'fxv1 series alpha', category: CAT, series: SERIES, mode: 'publish' },
  { title: 'fxv1 Filler One', body: 'fxv1 filler one.', abstract: 'fxv1 filler one', category: CAT, mode: 'publish' },
  { title: 'fxv1 Filler Two', body: 'fxv1 filler two.', abstract: 'fxv1 filler two', category: CAT, mode: 'publish' },
  { title: 'fxv1 Filler Three', body: 'fxv1 filler three.', abstract: 'fxv1 filler three', category: CAT, mode: 'publish' },
  { title: 'fxv1 Draft Post fxv1zebra', body: 'fxv1zebra only in a draft.', abstract: 'fxv1 draft abstract', category: CAT, mode: 'draft' },
];

/** Publish order for the back-dated trio (reverse of creation). */
export const PUBLISH_REVERSED = ['fxv1 Charlie', 'fxv1 Bravo', 'fxv1 Alpha'];

export const slugOf = (title: string): string =>
  sqlOne(`SELECT slug FROM blogpost WHERE title = '${title}' AND isdeleted IS NOT TRUE ORDER BY postid LIMIT 1`);
export const idOf = (title: string): string =>
  sqlOne(`SELECT postid FROM blogpost WHERE title = '${title}' AND isdeleted IS NOT TRUE ORDER BY postid LIMIT 1`);
export const isPublished = (title: string): boolean =>
  sqlOne(`SELECT published FROM blogpost WHERE title = '${title}' AND isdeleted IS NOT TRUE ORDER BY postid LIMIT 1`) === 't';

export async function pollUntil<T>(fn: () => T, ok: (v: T) => boolean, ms = 45000): Promise<T> {
  const end = Date.now() + ms;
  let v = fn();
  while (!ok(v) && Date.now() < end) {
    await new Promise((r) => setTimeout(r, 1500));
    v = fn();
  }
  return v;
}

export async function ensureCategory(admin: Page, name: string) {
  if (sqlOne(`SELECT categoryid FROM category WHERE categoryname = '${name}'`)) return;
  await goTo(admin, '/admin/categories', '[data-testid="categories-count"], [data-testid="categories-empty"], [data-testid="categories-grid"]', 120000);
  await goTo(admin, '/admin/category', '[data-testid="category-name-input"]', 120000);
  await fillCommitted(admin, 'category-name-input', name);
  await admin.click('[data-testid="save-category"]');
  await pollUntil(() => sqlOne(`SELECT categoryid FROM category WHERE categoryname = '${name}'`), (v) => !!v);
  expect(sqlOne(`SELECT categoryid FROM category WHERE categoryname = '${name}'`), `category ${name} saved`).not.toBe('');
}

export async function ensureSeries(admin: Page, name: string) {
  if (sqlOne(`SELECT seriesid FROM blogseries WHERE name = '${name}'`)) return;
  await goTo(admin, '/admin/series', '[data-testid="series-grid"], [data-testid="series-empty"]', 120000);
  await goTo(admin, '/admin/series/new', '[data-testid="series-name-input"]', 120000);
  await fillCommitted(admin, 'series-name-input', name);
  await admin.click('[data-testid="save-series"]');
  await pollUntil(() => sqlOne(`SELECT seriesid FROM blogseries WHERE name = '${name}'`), (v) => !!v);
  expect(sqlOne(`SELECT seriesid FROM blogseries WHERE name = '${name}'`), `series ${name} saved`).not.toBe('');
}

/** Opens the editor on a fresh post (leaves any editor first: Blazor ignores a same-route navigate). */
async function openNew(admin: Page) {
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(admin, '/ManagePost', '[data-testid="post-title-input"]', 120000);
  await expect(admin.locator('[data-testid="post-title-input"]')).toHaveValue('');
}

export async function openPostById(admin: Page, postId: string | number) {
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(admin, `/ManagePost/${postId}`, '[data-testid="post-title-input"]', 120000);
  await expect(admin.locator('[data-testid="post-title-input"]')).not.toHaveValue('', { timeout: 30000 });
}

/** Creates a post through the editor. Returns when the database row exists in the requested state. */
export async function createPost(admin: Page, p: PostSpec, opts: { typedSlug?: string } = {}) {
  await openNew(admin);
  await fillCommitted(admin, 'post-title-input', p.title);
  if (opts.typedSlug) await fillCommitted(admin, 'post-slug-input', opts.typedSlug);
  if (p.abstract) await fillCommitted(admin, 'post-excerpt-input', p.abstract);
  if (p.category) await pickSelect(admin, 'category-select', p.category);
  for (const t of p.tags ?? []) {
    await admin.fill('[data-testid="tag-input"]', t);
    await admin.dispatchEvent('[data-testid="tag-input"]', 'change');
    await admin.waitForTimeout(500);
    await admin.click('[data-testid="add-tag"]');
    await expect(admin.locator('[data-testid="selected-tag"]').filter({ hasText: t })).toBeVisible({ timeout: 15000 });
  }
  if (p.series) await pickSelect(admin, 'series-select', new RegExp(p.series));
  // The Markdown body goes in last: a long body makes the split preview re-render and starves the
  // select popovers above it (measured: the category listbox would not open after a 400-word fill).
  expect(await setMarkdown(admin, p.body ?? 'fxv1 body.'), 'the editor holds the Markdown body').toBe(true);
  await admin.click(p.mode === 'publish' ? '[data-testid="publish-post"]' : '[data-testid="save-draft"]');
  const want = p.mode === 'publish' ? 't' : 'f';
  const state = await pollUntil(
    () => sqlOne(`SELECT published FROM blogpost WHERE title = '${p.title}' AND isdeleted IS NOT TRUE ORDER BY postid LIMIT 1`),
    (v) => v === want,
  );
  expect(state, `post "${p.title}" saved as ${p.mode}`).toBe(want);
  await admin.waitForTimeout(2500); // the editor redirects to /BlogsList after saving
}

export async function publishExisting(admin: Page, title: string) {
  await openPostById(admin, idOf(title));
  await admin.click('[data-testid="publish-post"]');
  const state = await pollUntil(() => (isPublished(title) ? 't' : 'f'), (v) => v === 't');
  expect(state, `"${title}" published`).toBe('t');
  await admin.waitForTimeout(2500);
}

export async function unpublishExisting(admin: Page, title: string) {
  await openPostById(admin, idOf(title));
  await admin.click('[data-testid="unpublish-post"]');
  const state = await pollUntil(() => (isPublished(title) ? 't' : 'f'), (v) => v === 'f');
  expect(state, `"${title}" unpublished`).toBe('f');
  await admin.waitForTimeout(2500);
}

/** Builds every fixture that is missing. Safe to call repeatedly. */
export async function ensureFixtures(admin: Page) {
  await ensureCategory(admin, CAT);
  await ensureCategory(admin, CAT_EMPTY);
  await ensureSeries(admin, SERIES);
  for (const p of POSTS) {
    if (!idOf(p.title)) await createPost(admin, p);
  }
  for (const t of PUBLISH_REVERSED) {
    if (!isPublished(t)) await publishExisting(admin, t);
  }
}

/** Removes one post through the BlogsList delete dialog (the app soft-deletes). */
export async function deletePostByTitle(admin: Page, title: string) {
  for (let guard = 0; guard < 3 && idOf(title); guard++) {
    await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
    await fillSearch(admin, 'posts-search', title);
    const row = admin.locator('tr, [data-testid="post-row"]').filter({ has: admin.locator('[data-testid="post-row-title"]', { hasText: title }) }).first();
    await row.locator('[data-testid="post-delete"]').first().click();
    await admin.click('[data-testid="post-delete-confirm"]');
    await pollUntil(() => idOf(title), (v) => !v, 20000);
  }
}

async function fillSearch(admin: Page, testId: string, text: string) {
  await fillCommitted(admin, testId, text);
  await admin.waitForTimeout(1500);
}

/** Deletes every fxv1 fixture through the admin UI, in dependency order. Returns what is left. */
export async function cleanupFixtures(admin: Page): Promise<string[]> {
  const left: string[] = [];
  const titles = sql("SELECT title FROM blogpost WHERE title LIKE 'fxv1%' AND isdeleted IS NOT TRUE");
  for (const t of titles) {
    try { await deletePostByTitle(admin, t); } catch (e) { left.push(`post ${t}: ${String(e).slice(0, 80)}`); }
  }
  for (const [list, ready, rowName, delBtn, confirm, nameCol, searchId, table] of [
    ['/admin/series', 'series-grid', 'series-row-name', 'series-delete', 'series-delete-confirm', 'name', 'series-search', 'blogseries'],
    ['/admin/categories', 'categories-grid', 'category-row-name', 'category-delete', 'category-delete-confirm', 'categoryname', 'categories-search', 'category'],
    ['/admin/tags', 'tags-grid', 'tag-row-name', 'tag-delete', 'tag-delete-confirm', 'tagname', 'tags-search', 'tag'],
  ] as const) {
    const names = sql(`SELECT ${nameCol} FROM ${table} WHERE ${nameCol} LIKE 'fxv1%'`);
    for (const n of names) {
      try {
        await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
        await goTo(admin, list, `[data-testid="${ready}"], [data-testid="${ready.replace('grid', 'empty')}"]`, 120000);
        await fillSearch(admin, searchId, n);
        const row = admin.locator('tr, [data-testid="' + ready + '"] > *').filter({ has: admin.locator(`[data-testid="${rowName}"]`, { hasText: n }) }).first();
        await row.locator(`[data-testid="${delBtn}"]`).first().click();
        await admin.click(`[data-testid="${confirm}"]`);
        await pollUntil(() => sqlOne(`SELECT ${nameCol} FROM ${table} WHERE ${nameCol} = '${n}'`), (v) => !v, 20000);
      } catch (e) { left.push(`${table} ${n}: ${String(e).slice(0, 80)}`); }
    }
    for (const n of sql(`SELECT ${nameCol} FROM ${table} WHERE ${nameCol} LIKE 'fxv1%'`)) left.push(`${table} ${n} still present`);
  }
  return left;
}
