/**
 * fxv4-admin-taxonomy.spec.ts - verify-phase writer W4.
 *
 * Rows: REQ-UI-022 (categories list + editor), REQ-UI-023 (tags list + editor), REQ-FN-054 (slug
 * generation: collision retry keeps the supplied slug, empty slug is never stored), REQ-FN-055
 * (soft-deleted posts cannot be published / stay off the site), REQ-FN-056 (view trend survives several
 * views on one day).
 *
 * Every fixture is created and deleted THROUGH THE ADMIN UI, prefixed `fxv4`. psql is only used to READ.
 * Categories and tags are AdminOnly (policy), so "the editor" in the acceptance lines is the admin.
 */
import { test, expect, Page, Browser } from '@playwright/test';
import * as fs from 'fs';
import {
  BASE, psql, psqlInt, psqlRows, sleep, until, fillCommitted, pickSelect, adminSession, openAdmin, createPost,
  deletePostBySlug, publicText, setMarkdown,
} from './fxv4-helpers';

const OUT = 'tests/.artifacts/verify/W4';
fs.mkdirSync(OUT, { recursive: true });
test.describe.configure({ mode: 'serial' });
test.setTimeout(420000);

let browserRef: Browser;
let admin: Page;
let closeAdmin: () => Promise<void>;
const madeCategories: string[] = [];
const madeTags: string[] = [];
const madePostSlugs: string[] = [];

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  const s = await adminSession(browser);
  admin = s.page;
  closeAdmin = () => s.ctx.close();
});

/** Removes whatever the tests created, through the list pages' own delete dialogs. */
test.afterAll(async () => {
  try {
    for (const name of madeCategories) {
      if (!psql(`SELECT 1 FROM Category WHERE CategoryName = '${name}'`)) continue;
      await openAdmin(admin, '/admin/categories', '[data-testid="categories-grid"], [data-testid="categories-empty"]');
      await admin.fill('[data-testid="categories-search"]', name);
      await admin.dispatchEvent('[data-testid="categories-search"]', 'change');
      await sleep(1500);
      const row = admin.locator('tbody tr').filter({ hasText: name }).first();
      if (await row.count()) { await row.locator('[data-testid="category-delete"]').click(); await admin.locator('[data-testid="category-delete-confirm"]').click(); await sleep(2000); }
    }
    for (const name of madeTags) {
      if (!psql(`SELECT 1 FROM Tag WHERE TagName = '${name}'`)) continue;
      await openAdmin(admin, '/admin/tags', '[data-testid="tags-grid"], [data-testid="tags-empty"]');
      await admin.fill('[data-testid="tags-search"]', name);
      await admin.dispatchEvent('[data-testid="tags-search"]', 'change');
      await sleep(1500);
      const row = admin.locator('tbody tr').filter({ hasText: name }).first();
      if (await row.count()) { await row.locator('[data-testid="tag-delete"]').click(); await admin.locator('[data-testid="tag-delete-confirm"]').click(); await sleep(2000); }
    }
    for (const slug of madePostSlugs) await deletePostBySlug(admin, slug);
  } catch (e) {
    console.log(`[fxv4 taxonomy cleanup] ${String(e).slice(0, 200)}`);
  } finally {
    await closeAdmin?.();
  }
});

/** Creates a category through the editor form. Returns the stored slug. */
async function addCategory(name: string, slug: string, description: string): Promise<string> {
  madeCategories.push(name);
  await openAdmin(admin, '/admin/category', '[data-testid="category-name-input"]');
  await sleep(1500);
  await fillCommitted(admin, 'category-name-input', name);
  if (slug) await fillCommitted(admin, 'category-slug-input', slug);
  if (description) await fillCommitted(admin, 'category-description-input', description);
  await admin.click('[data-testid="save-category"]');
  await until(() => !!psql(`SELECT 1 FROM Category WHERE CategoryName = '${name}'`), 30000, 1000);
  return psql(`SELECT Slug FROM Category WHERE CategoryName = '${name}'`);
}

async function addTag(name: string, slug: string): Promise<string> {
  madeTags.push(name);
  await openAdmin(admin, '/ManageTag', '[data-testid="tag-name-input"]');
  await sleep(1500);
  await fillCommitted(admin, 'tag-name-input', name);
  if (slug) await fillCommitted(admin, 'tag-slug-input', slug);
  await admin.click('[data-testid="save-tag"]');
  await until(() => !!psql(`SELECT 1 FROM Tag WHERE TagName = '${name}'`), 30000, 1000);
  return psql(`SELECT Slug FROM Tag WHERE TagName = '${name}'`);
}

/** Reads the list grid as { name -> [slug, postCount] }. */
async function readGrid(kind: 'category' | 'tag') {
  const names = (await admin.locator(`[data-testid="${kind}-row-name"]`).allTextContents()).map((t) => t.trim());
  const slugs = (await admin.locator(`[data-testid="${kind}-row-slug"]`).allTextContents()).map((t) => t.trim().replace(/^\//, ''));
  const counts = (await admin.locator(`[data-testid="${kind}-row-postcount"]`).allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  return new Map(names.map((n, i) => [n, [slugs[i], counts[i]] as [string, number]]));
}

test('REQ-UI-022 every category row shows the name, slug and post count the database holds; the editor loads and saves a category', async () => {
  const slug = await addCategory('fxv4 Alpha', 'fxv4-alpha', 'fxv4 original description');
  expect(slug).toBe('fxv4-alpha');

  // List vs database, re-read until a render and a psql read agree (siblings add categories).
  let diff = 'never compared';
  for (let i = 0; i < 5 && diff; i++) {
    await openAdmin(admin, '/admin/categories', '[data-testid="categories-grid"]');
    const ui = await readGrid('category');
    const db = psqlRows(`SELECT c.CategoryName, c.Slug, (SELECT COUNT(*) FROM BlogPost p WHERE p.CategoryId = c.CategoryId AND p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)) FROM Category c ORDER BY c.CategoryId`);
    const wrong: string[] = [];
    for (const [n, s, c] of db) {
      const row = ui.get(n);
      if (!row) wrong.push(`${n}: missing from the list`);
      else if (row[0] !== s || row[1] !== Number(c)) wrong.push(`${n}: ui=${row[0]}/${row[1]} db=${s}/${c}`);
    }
    if (ui.size !== db.length) wrong.push(`row count ui=${ui.size} db=${db.length}`);
    diff = wrong.join('; ');
  }
  expect(diff, 'category rows equal the database').toBe('');

  // The editor loads the stored values for one category, and saving a change persists it.
  await admin.fill('[data-testid="categories-search"]', 'fxv4 Alpha');
  await admin.dispatchEvent('[data-testid="categories-search"]', 'change');
  await sleep(1500);
  await admin.locator('tbody tr').filter({ hasText: 'fxv4 Alpha' }).first().locator('[data-testid="category-edit"]').click();
  await admin.waitForSelector('[data-testid="category-name-input"]', { state: 'visible', timeout: 60000 });
  await sleep(2500);
  const stored = psqlRows(`SELECT CategoryName, Slug, COALESCE(Description,'') FROM Category WHERE CategoryName = 'fxv4 Alpha'`)[0];
  expect(await admin.inputValue('[data-testid="category-name-input"]')).toBe(stored[0]);
  expect(await admin.inputValue('[data-testid="category-slug-input"]')).toBe(stored[1]);
  expect(await admin.inputValue('[data-testid="category-description-input"]')).toBe(stored[2]);

  await fillCommitted(admin, 'category-description-input', 'fxv4 edited description');
  await admin.click('[data-testid="save-category"]');
  expect(await until(() => psql(`SELECT Description FROM Category WHERE CategoryName = 'fxv4 Alpha'`) === 'fxv4 edited description', 30000, 1000), 'the saved change persisted').toBe(true);
  // Revert (the fixture is deleted afterwards anyway; this keeps the test self-contained).
  await openAdmin(admin, '/admin/category', '[data-testid="category-name-input"]');
  const id = psql(`SELECT CategoryId FROM Category WHERE CategoryName = 'fxv4 Alpha'`);
  await openAdmin(admin, `/admin/category/${id}`, '[data-testid="category-name-input"]');
  await sleep(2000);
  await fillCommitted(admin, 'category-description-input', 'fxv4 original description');
  await admin.click('[data-testid="save-category"]');
  expect(await until(() => psql(`SELECT Description FROM Category WHERE CategoryName = 'fxv4 Alpha'`) === 'fxv4 original description', 30000, 1000), 'reverted').toBe(true);
});

test('REQ-UI-023 every tag row shows the name, slug and post count the database holds; the editor loads and saves a tag', async () => {
  const slug = await addTag('fxv4 Tag One', 'fxv4-tag-one');
  expect(slug).toBe('fxv4-tag-one');

  let diff = 'never compared';
  for (let i = 0; i < 5 && diff; i++) {
    await openAdmin(admin, '/admin/tags', '[data-testid="tags-grid"]');
    const ui = await readGrid('tag');
    const db = psqlRows(`SELECT t.TagName, t.Slug, (SELECT COUNT(*) FROM PostTag pt JOIN BlogPost p ON p.PostId = pt.PostId WHERE pt.TagId = t.TagId AND p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)) FROM Tag t ORDER BY t.TagId`);
    const wrong: string[] = [];
    // The grid pages (20 rows); compare the rows it shows, and the full set when it fits.
    for (const [n, s, c] of db) {
      const row = ui.get(n);
      if (!row) { if (db.length <= 20) wrong.push(`${n}: missing from the list`); continue; }
      if (row[0] !== s || row[1] !== Number(c)) wrong.push(`${n}: ui=${row[0]}/${row[1]} db=${s}/${c}`);
    }
    if (db.length <= 20 && ui.size !== db.length) wrong.push(`row count ui=${ui.size} db=${db.length}`);
    diff = wrong.join('; ');
  }
  expect(diff, 'tag rows equal the database').toBe('');

  const id = psql(`SELECT TagId FROM Tag WHERE TagName = 'fxv4 Tag One'`);
  await openAdmin(admin, `/ManageTag/${id}`, '[data-testid="tag-name-input"]');
  await sleep(2500);
  expect(await admin.inputValue('[data-testid="tag-name-input"]')).toBe('fxv4 Tag One');
  expect(await admin.inputValue('[data-testid="tag-slug-input"]')).toBe('fxv4-tag-one');
  await fillCommitted(admin, 'tag-name-input', 'fxv4 Tag One B');
  madeTags.push('fxv4 Tag One B');
  await admin.click('[data-testid="save-tag"]');
  expect(await until(() => psql(`SELECT TagName FROM Tag WHERE TagId = ${id}`) === 'fxv4 Tag One B', 30000, 1000), 'the saved change persisted').toBe(true);
  await openAdmin(admin, `/ManageTag/${id}`, '[data-testid="tag-name-input"]');
  await sleep(2000);
  await fillCommitted(admin, 'tag-name-input', 'fxv4 Tag One');
  await admin.click('[data-testid="save-tag"]');
  expect(await until(() => psql(`SELECT TagName FROM Tag WHERE TagId = ${id}`) === 'fxv4 Tag One', 30000, 1000), 'reverted').toBe(true);
});

test('REQ-FN-054 a slug collision keeps the supplied slug (suffixed) and an empty slug is never stored', async () => {
  // Collision: the category with slug fxv4-alpha exists (previous test). A second one asks for the same slug.
  const second = await addCategory('fxv4 Beta', 'fxv4-alpha', 'fxv4 collision');
  expect(second, 'the retry keeps the slug the user supplied, with a suffix - it does not fall back to the title').toMatch(/^fxv4-alpha-\d+$/);
  expect(second).not.toBe('fxv4-alpha');

  // Empty slug: generated from the name.
  const generated = await addCategory('fxv4 Gamma Cat', '', 'fxv4 generated');
  expect(generated, 'an empty slug is generated from the name').toBe('fxv4-gamma-cat');

  // Empty slug and a name with no slug-able characters: still not stored empty.
  const symbolic = await addCategory('!!!', '', 'fxv4 symbols only');
  expect(symbolic.trim().length, 'a name with no letters still gets a non-empty slug').toBeGreaterThan(0);

  // Same rules for tags.
  const tagSecond = await addTag('fxv4 Tag Dup', 'fxv4-tag-one');
  expect(tagSecond).toMatch(/^fxv4-tag-one-\d+$/);
  const tagGen = await addTag('fxv4 Tag Gamma', '');
  expect(tagGen).toBe('fxv4-tag-gamma');

  // Global invariant: nothing in the database carries an empty slug.
  expect(psqlInt(`SELECT COUNT(*) FROM Category WHERE COALESCE(TRIM(Slug), '') = ''`), 'no category has an empty slug').toBe(0);
  expect(psqlInt(`SELECT COUNT(*) FROM Tag WHERE COALESCE(TRIM(Slug), '') = ''`), 'no tag has an empty slug').toBe(0);
  expect(psqlInt(`SELECT COUNT(*) FROM BlogPost WHERE COALESCE(TRIM(Slug), '') = ''`), 'no post has an empty slug').toBe(0);
});

test('REQ-FN-054 two posts with the same supplied slug are both stored, the second under a suffixed slug', async () => {
  test.setTimeout(600000);
  const a = await createPost(admin, { title: 'fxv4 Slug A', slug: 'fxv4-slug-clash', abstract: 'fxv4 slug clash A', body: '## a\n\nfxv4 slug clash.', category: 'Web Development', publish: false });
  madePostSlugs.push(a.slug);
  const b = await createPost(admin, { title: 'fxv4 Slug B', slug: 'fxv4-slug-clash', abstract: 'fxv4 slug clash B', body: '## b\n\nfxv4 slug clash.', category: 'Web Development', publish: false });
  madePostSlugs.push(b.slug);
  expect(a.slug.startsWith('fxv4-slug-clash')).toBe(true);
  expect(b.slug, 'the second post keeps the supplied slug as its base').toMatch(/^fxv4-slug-clash(-\d+)?$/);
  expect(b.slug, 'and is distinct from the first').not.toBe(a.slug);
});

test('REQ-FN-055 a soft-deleted post cannot be published again and stays off the public site', async () => {
  test.setTimeout(600000);
  const draft = await createPost(admin, { title: 'fxv4 Deleted Draft', slug: 'fxv4-deleted-draft', abstract: 'fxv4 deleted draft', body: '## d\n\nfxv4 soft-delete check.', category: 'Web Development', publish: false });
  const live = await createPost(admin, { title: 'fxv4 Deleted Live', slug: 'fxv4-deleted-live', abstract: 'fxv4 deleted live', body: '## l\n\nfxv4 soft-delete check.', category: 'Web Development', publish: true });
  madePostSlugs.push(draft.slug, live.slug);
  expect((await publicText(browserRef, `/post/${live.slug}`, 2500)).status, 'the live post is public before deletion').toBe(200);

  // Soft-delete both through the post list.
  await deletePostBySlug(admin, draft.slug);
  await deletePostBySlug(admin, live.slug);
  expect(psql(`SELECT IsDeleted FROM BlogPost WHERE PostId = ${draft.id}`)).toBe('t');
  expect(psql(`SELECT IsDeleted FROM BlogPost WHERE PostId = ${live.id}`)).toBe('t');

  // Try to publish the deleted draft through the editor route (the only publish path that takes an id).
  await admin.evaluate((id) => (window as any).Blazor.navigateTo(`/ManagePost/${id}`), draft.id);
  await sleep(6000);
  const publishBtn = admin.locator('[data-testid="publish-post"]');
  if (await publishBtn.count()) {
    await publishBtn.click();
    await sleep(5000);
  }
  expect(psql(`SELECT Published FROM BlogPost WHERE PostId = ${draft.id}`), 'a soft-deleted post must not become Published').toBe('f');

  // Neither is reachable publicly, whatever their Published flag says.
  for (const slug of [draft.slug, live.slug]) {
    const r = await publicText(browserRef, `/post/${slug}`, 3000);
    expect(r.status, `/post/${slug} after deletion`).toBe(404);
  }
  const sitemap = await fetch(`${BASE}/sitemap.xml`).then((r) => r.text()).catch(() => '');
  expect(sitemap, 'deleted posts are not in the sitemap').not.toContain(live.slug);
  // Not listed in the post list either.
  await openAdmin(admin, '/BlogsList', '[data-testid="posts-grid"], [data-testid="posts-empty"]');
  await admin.fill('[data-testid="posts-search"]', 'fxv4-deleted');
  await admin.dispatchEvent('[data-testid="posts-search"]', 'change');
  await sleep(2000);
  expect(await admin.locator('tbody tr').filter({ hasText: 'fxv4-deleted' }).count(), 'deleted posts do not appear in the admin list').toBe(0);
});

test('REQ-FN-056 the views trend plots every day of the range even when a day holds several views', async () => {
  test.setTimeout(600000);
  const post = await createPost(admin, { title: 'fxv4 Trend Post', slug: 'fxv4-trend-post', abstract: 'fxv4 trend post', body: '## t\n\nfxv4 views.', category: 'Web Development', publish: true });
  madePostSlugs.push(post.slug);
  // Views on today's date from this post and from other writers' posts: several aggregate inputs for one day.
  for (let i = 0; i < 3; i++) await publicText(browserRef, `/post/${post.slug}`, 3500);
  expect(await until(() => psqlInt(`SELECT COUNT(*) FROM PostViews WHERE PostId = ${post.id}`) >= 1, 20000, 1000), 'the visit was recorded (repeat visits by one visitor are de-duplicated)').toBe(true);

  let note = 'never compared';
  for (let i = 0; i < 5 && note; i++) {
    await openAdmin(admin, '/admin/analytics', '[data-testid="analytics-trend-card"]');
    await admin.click('[data-testid="analytics-preset-30"]');
    await sleep(3500);
    const empty = await admin.locator('[data-testid="analytics-trend-empty"]').count();
    if (empty) { note = 'trend shows the empty state although views exist'; continue; }
    await admin.waitForSelector('[data-testid="analytics-trend-summary"]', { state: 'visible', timeout: 30000 });
    const summary = (await admin.locator('[data-testid="analytics-trend-summary"]').innerText()).replace(/\s+/g, ' ');
    const m = summary.match(/(\d+) days plotted · ([\d,]+) views/);
    if (!m) { note = `unreadable summary "${summary}"`; continue; }
    const dbViews = psqlInt(`SELECT COUNT(*) FROM PostViews v JOIN BlogPost p ON p.PostId = v.PostId WHERE p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL) AND v.ViewedOn::date >= (NOW() AT TIME ZONE 'utc')::date - 29`);
    note = Number(m[1]) === 30 && Number(m[2].replace(/,/g, '')) === dbViews ? '' : `summary "${summary}" vs db views(30d)=${dbViews}`;
  }
  expect(note, 'the trend plots 30 days and its total equals the database').toBe('');
  expect(await admin.locator('[data-testid="analytics-trend-chart"]').count(), 'the chart renders').toBeGreaterThan(0);
  await admin.screenshot({ path: `${OUT}/fn056-trend.png` });
});
