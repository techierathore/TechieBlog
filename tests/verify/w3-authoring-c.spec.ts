/**
 * w3-authoring.spec.ts — verify-phase 2026-10-27, group W3 (authoring).
 *
 * Rows: REQ-UI-016/017/018/024, REQ-FN-012/016/017/018/019, REQ-FN-057 (live RSS half).
 *
 * The dev DB holds no demo content (owner decision 2026-08-22), and the verify phase forbids database
 * writes, so every fixture is created through the admin UI in `beforeAll` and removed through the UI in
 * `afterAll`. Fixture prefix `fxv3`. Other writers share this app: no test asserts a global count without
 * re-reading psql in the same poll, and rows are looked up by their fxv3 names.
 *
 * SPLIT 2026-10-28 (triage T3): the file as one ran past the 580 s graded-run budget, so it is cut into three
 * spec files that share this header, fixture prefix (fxv3) and teardown: w3-authoring (UI-016/017/018, FN-012),
 * w3-authoring-b (FN-016, FN-017), w3-authoring-c (FN-018, FN-019, UI-024, FN-057, UI-017 author).
 *
 * Known residue: `BlogSvc.DeletePost` is a SOFT delete, so deleted fixture posts stay in `blogpost`
 * with isdeleted = true (reported in the W3 return).
 */
import { test, expect, Page } from '@playwright/test';
import {
  BASE, SHOTS, psql, psqlRows, pollDb, q, slugOf, newAdminPage, createPost, createCategory, createSeries,
  openPost, freshList, deleteListRow, purgePosts, goTo, pickSelect, setMarkdown, fillCommitted, postId, loginHard,
} from './w3-helpers';
import { visualCheck } from './_gates';
import { texts } from './vall-authoring-helpers';

test.describe.configure({ mode: 'default' });
test.use({ actionTimeout: 30000 });

const CAT = 'fxv3-cat';
const CAT2 = 'fxv3-cat2';
const CAT_RE = /fxv3-cat(?!\d)/;
const CAT2_RE = /fxv3-cat2/;
const SERIES = 'fxv3-series';
const EDSERIES = 'fxv3-edseries';
const TAG = 'fxv3-tag';
const P1 = 'fxv3 Part One';
const P2 = 'fxv3 Part Two';
const P3 = 'fxv3 Part Three';
const DRAFT = 'fxv3 Draft Post';
const BODY = '## Fxv3 heading\n\nSome **bold** text and a `code` span.\n\n- one\n- two\n';

let admin: Page;

/** The slug the app actually generated (a soft-deleted earlier run keeps its slug, so a new post may get a -2 suffix). */
const dbSlug = (title: string) => psql(`SELECT slug FROM blogpost WHERE title='${q(title)}' AND isdeleted IS NOT TRUE ORDER BY postid DESC LIMIT 1`);

async function ensureFixtures(page: Page) {
  await createCategory(page, CAT);
  await createCategory(page, CAT2);
  await createSeries(page, SERIES);
  await createSeries(page, EDSERIES);
  if (postId(P1) === '') await createPost(page, { title: P1, md: BODY, category: CAT_RE, tags: [TAG], series: new RegExp(SERIES + '(?!\\w)'), mode: 'publish' });
  if (postId(P2) === '') await createPost(page, { title: P2, md: BODY, category: CAT_RE, tags: [TAG], series: new RegExp(SERIES + '(?!\\w)'), mode: 'publish' });
  if (postId(P3) === '') await createPost(page, { title: P3, md: BODY, category: CAT_RE, series: new RegExp(SERIES + '(?!\\w)'), mode: 'publish' });
  if (postId(DRAFT) === '') await createPost(page, { title: DRAFT, md: BODY, category: CAT_RE, tags: [TAG] });
}

test.beforeAll(async ({ browser }) => {
  test.setTimeout(900000);
  admin = await newAdminPage(browser, 'admin');
  await ensureFixtures(admin);
});

test.afterAll(async ({ browser }) => {
  test.setTimeout(600000);
  if (process.env.W3_KEEP) return; // development: keep the fixtures between partial runs
  const page = await newAdminPage(browser, 'admin');
  // posts first (a category / series cannot go while posts reference them)
  for (let pass = 0; pass < 25; pass++) {
    const titles = psqlRows(`SELECT title FROM blogpost WHERE title LIKE 'fxv3%' AND isdeleted IS NOT TRUE ORDER BY title DESC`).map((r) => r[0]);
    if (titles.length === 0) break;
    // longest title first, so a search for 'fxv3 Crud Post' cannot hit the 'v2' row of a different post
    await deleteListRow(page, { route: '/BlogsList', ready: '[data-testid="posts-status-tabs"]', searchTid: 'posts-search', nameTid: 'post-row-title', name: titles.sort((x, y) => y.length - x.length)[0], deleteTid: 'post-delete', dialogTid: 'post-delete-dialog', confirmTid: 'post-delete-confirm' }).catch(() => false);
  }
  const series = psqlRows(`SELECT name FROM blogseries WHERE name LIKE 'fxv3%'`).map((r) => r[0]);
  for (const s of series) {
    await deleteListRow(page, { route: '/admin/series', ready: '[data-testid="series-grid"]', nameTid: 'series-row-name', name: s, deleteTid: 'series-delete', dialogTid: 'series-delete-dialog', confirmTid: 'series-delete-confirm' }).catch(() => false);
  }
  const tags = psqlRows(`SELECT tagname FROM tag WHERE tagname LIKE 'fxv3%'`).map((r) => r[0]);
  for (const t of tags) {
    await deleteListRow(page, { route: '/admin/tags', ready: '[data-testid="tags-grid"]', searchTid: 'tags-search', nameTid: 'tag-row-name', name: t, deleteTid: 'tag-delete', dialogTid: 'tag-delete-dialog', confirmTid: 'tag-delete-confirm' }).catch(() => false);
  }
  const cats = psqlRows(`SELECT categoryname FROM category WHERE categoryname LIKE 'fxv3%'`).map((r) => r[0]);
  for (const c of cats) {
    await deleteListRow(page, { route: '/admin/categories', ready: '[data-testid="categories-grid"]', searchTid: 'categories-search', nameTid: 'category-row-name', name: c, deleteTid: 'category-delete', dialogTid: 'category-delete-dialog', confirmTid: 'category-delete-confirm' }).catch(() => false);
  }
  console.log('W3 leftovers: ' + JSON.stringify({
    postsNotSoftDeleted: psql(`SELECT count(*) FROM blogpost WHERE title LIKE 'fxv3%' AND isdeleted IS NOT TRUE`),
    postsSoftDeleted: psql(`SELECT count(*) FROM blogpost WHERE title LIKE 'fxv3%' AND isdeleted = TRUE`),
    series: psql(`SELECT count(*) FROM blogseries WHERE name LIKE 'fxv3%'`),
    tags: psql(`SELECT count(*) FROM tag WHERE tagname LIKE 'fxv3%'`),
    categories: psql(`SELECT count(*) FROM category WHERE categoryname LIKE 'fxv3%'`),
  }));
});

/** Reads what the editor currently shows for the post it has loaded. */
async function readEditor(page: Page) {
  const textOf = async (s: string) => {
    const e = page.locator(s).first();
    return (await e.count()) === 0 ? '' : ((await e.textContent()) || '').trim();
  };
  return {
    title: await page.inputValue('[data-testid="post-title-input"]'),
    slug: await page.inputValue('[data-testid="post-slug-input"]'),
    excerpt: await page.inputValue('[data-testid="post-excerpt-input"]'),
    body: await page.inputValue('[data-testid="markdown-input"]'),
    category: await textOf('[data-testid="category-select"]'),
    series: await textOf('[data-testid="series-select"]'),
    part: await textOf('[data-testid="series-part-badge"]'),
    tags: (await page.locator('[data-testid="selected-tag"]').allTextContents()).map((t) => t.replace(/[×✕x]\s*$/i, '').trim()),
  };
}

const MARKDOWN_A = '# Alpha heading\n\nThis is **bold A** and a list:\n\n- one\n- two\n';
const MARKDOWN_B = '## Beta heading\n\nBody of B only, with *emphasis*.\n';

// -----------------------------------------------------------------------------------------------
// REQ-FN-018 — tags
// -----------------------------------------------------------------------------------------------
test('REQ-FN-018 tags: existing-tag choice and inline creation write junction rows; admin and archive counts equal the database', async ({ browser }) => {
  test.setTimeout(600000);
  const inline = 'fxv3-tag-inline';
  // The no-category post is a draft that carries no tags yet.
  const NOCAT = 'fxv3 No Category';
  const id = postId(NOCAT) || (await createPost(admin, { title: NOCAT }));
  await openPost(admin, id);
  // (a) choose an existing tag from the quick-add suggestions
  await expect(admin.locator('[data-testid="quick-add-tags"]'), 'existing tags are offered').toBeVisible();
  const firstOffer = (await admin.locator('[data-testid="quick-add-tag"]').first().innerText()).replace(/^\+/, '').trim();
  await admin.locator('[data-testid="quick-add-tag"]').first().click();
  await admin.waitForTimeout(800);
  // (b) typing the name of an existing tag reuses it instead of creating a duplicate
  const tagRowsBefore = psql(`SELECT count(*) FROM tag WHERE lower(tagname)=lower('${TAG}')`);
  expect(tagRowsBefore).toBe('1');
  await fillCommitted(admin, 'tag-input', TAG);
  await admin.click('[data-testid="add-tag"]');
  await admin.waitForTimeout(1200);
  // (c) a brand-new name is created inline
  await fillCommitted(admin, 'tag-input', inline);
  await admin.click('[data-testid="add-tag"]');
  await admin.waitForTimeout(1200);
  const chips = await texts(admin, 'selected-tag');
  for (const want of [firstOffer, TAG, inline]) expect(chips.join(' | '), `chip for ${want}`).toContain(want);
  await admin.click('[data-testid="save-draft"]');
  expect(await pollDb(`SELECT count(*) FROM tag WHERE tagname='${inline}'`, (v) => v === '1', 60000), 'inline tag created').toBe('1');
  const tagId = psql(`SELECT tagid FROM tag WHERE tagname='${inline}'`);
  expect(await pollDb(`SELECT count(*) FROM posttag WHERE postid=${id} AND tagid=${tagId}`, (v) => v === '1'), 'junction row for the inline tag').toBe('1');
  expect(psql(`SELECT count(*) FROM posttag pt JOIN tag t ON t.tagid=pt.tagid WHERE pt.postid=${id} AND t.tagname='${TAG}'`), 'junction row for the typed existing tag').toBe('1');
  expect(psql(`SELECT count(*) FROM posttag pt JOIN tag t ON t.tagid=pt.tagid WHERE pt.postid=${id} AND t.tagname='${q(firstOffer)}'`), 'junction row for the quick-added tag').toBe('1');
  expect(psql(`SELECT count(*) FROM tag WHERE lower(tagname)=lower('${TAG}')`), 'no duplicate of the existing tag').toBe('1');
  expect(psql(`SELECT slug FROM tag WHERE tagid=${tagId}`)).toBeTruthy();

  // counts: published, non-deleted posts only
  const dbCount = () => psql(`SELECT count(*) FROM posttag pt JOIN tag t ON t.tagid=pt.tagid JOIN blogpost p ON p.postid=pt.postid WHERE t.tagname='${TAG}' AND p.published=TRUE AND p.isdeleted IS NOT TRUE`);
  expect(Number(dbCount()), 'two published fixtures carry the tag').toBe(2);
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(admin, '/admin/tags', '[data-testid="tags-grid"]', 120000);
  // The tags grid is paged (Blog.PostsPerPage rows) since the 2026-10-28 build, so find the row by searching for it.
  await admin.fill('[data-testid="tags-search"]', TAG);
  await admin.dispatchEvent('[data-testid="tags-search"]', 'change');
  await admin.waitForTimeout(1500);
  const adminRow = admin.locator('tr').filter({ has: admin.locator('[data-testid="tag-row-name"]', { hasText: new RegExp(`^${TAG}$`) }) }).first();
  await expect(adminRow.locator('[data-testid="tag-row-postcount"]')).toHaveText(dbCount(), { timeout: 20000 });

  const pub = await (await browser.newContext()).newPage();
  await pub.goto(`${BASE}/tag/${TAG}`, { waitUntil: 'domcontentloaded' });
  await expect(pub.locator('[data-testid="tag-post-count"]')).toContainText(new RegExp(`^\\s*${dbCount()} posts?`), { timeout: 60000 });
  const archiveTitles = await texts(pub, 'post-card-title').catch(() => []);
  void archiveTitles;
  expect(await pub.locator('main, body').first().innerText()).toContain(P1);
  expect(await pub.locator('main, body').first().innerText(), 'the draft carrying the tag is not listed publicly').not.toContain(DRAFT);

  // sidebar: it lists tags by name only (no count) — record what it shows for the fixture category instead
  await pub.goto(`${BASE}/post/${dbSlug(P1)}`, { waitUntil: 'domcontentloaded' });
  const sidebarCat = pub.locator('[data-testid="sidebar-categories"] a').filter({ hasText: CAT }).first();
  if (await sidebarCat.count()) {
    const shown = (await sidebarCat.locator('span').last().innerText()).trim();
    const dbCat = psql(`SELECT count(*) FROM blogpost p JOIN category c ON c.categoryid=p.categoryid WHERE c.categoryname='${CAT}' AND p.published=TRUE AND p.isdeleted IS NOT TRUE`);
    expect(shown, 'sidebar category count equals published posts in the database').toBe(dbCat);
  }
  await pub.context().close();

  // Tag CRUD: rename then delete the inline tag through the admin screens
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(admin, `/ManageTag/${tagId}`, '[data-testid="tag-name-input"]', 120000);
  await expect(admin.locator('[data-testid="tag-name-input"]')).toHaveValue(inline);
  await fillCommitted(admin, 'tag-name-input', `${inline}-renamed`);
  await admin.click('[data-testid="save-tag"]');
  expect(await pollDb(`SELECT tagname FROM tag WHERE tagid=${tagId}`, (v) => v === `${inline}-renamed`), 'tag rename persisted').toBe(`${inline}-renamed`);
  expect(await deleteListRow(admin, { route: '/admin/tags', ready: '[data-testid="tags-grid"]', searchTid: 'tags-search', nameTid: 'tag-row-name', name: `${inline}-renamed`, deleteTid: 'tag-delete', dialogTid: 'tag-delete-dialog', confirmTid: 'tag-delete-confirm' })).toBe(true);
  expect(await pollDb(`SELECT count(*) FROM tag WHERE tagid=${tagId}`, (v) => v === '0'), 'tag delete persisted').toBe('0');
  expect(psql(`SELECT count(*) FROM posttag WHERE tagid=${tagId}`), 'junction rows go with the tag').toBe('0');
});

// -----------------------------------------------------------------------------------------------
// REQ-FN-019 — series prev/next
// -----------------------------------------------------------------------------------------------
test('REQ-FN-019 series: parts in part-number order; each part links its previous and next part; ends omit the missing neighbour', async ({ browser }) => {
  test.setTimeout(300000);
  const pub = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const parts = psqlRows(`SELECT title, seriespartnumber FROM blogpost p JOIN blogseries s ON s.seriesid=p.seriesid WHERE s.name='${SERIES}' AND p.published=TRUE AND p.isdeleted IS NOT TRUE ORDER BY seriespartnumber`);
  expect(parts.map((r) => r[0])).toEqual([P1, P2, P3]);

  await pub.goto(`${BASE}/series/${SERIES}`, { waitUntil: 'domcontentloaded' });
  await expect(pub.locator('[data-testid="series-posts"]')).toBeVisible({ timeout: 60000 });
  expect(await texts(pub, 'series-post-title')).toEqual([P1, P2, P3]);
  const nums = (await texts(pub, 'series-post-number')).map((s) => Number(s.replace(/\D/g, '')));
  expect(nums).toEqual([...nums].sort((a, b) => a - b));
  await expect(pub.locator('[data-testid="series-part-count"]').first()).toContainText('3');

  const nav = async (title: string) => {
    await pub.goto(`${BASE}/post/${dbSlug(title)}`, { waitUntil: 'domcontentloaded' });
    await expect(pub.locator('[data-testid="series-navigation"]')).toBeVisible({ timeout: 60000 });
  };
  await nav(P2);
  await expect(pub.locator('[data-testid="series-previous-post"]')).toHaveCount(1);
  await expect(pub.locator('[data-testid="series-next-post"]')).toHaveCount(1);
  expect(await pub.locator('[data-testid="series-previous-post"]').getAttribute('href')).toBe(`/post/${dbSlug(P1)}`);
  expect(await pub.locator('[data-testid="series-next-post"]').getAttribute('href')).toBe(`/post/${dbSlug(P3)}`);
  await expect(pub.locator('[data-testid="series-navigation-name"]')).toContainText(SERIES);
  await nav(P1);
  await expect(pub.locator('[data-testid="series-previous-post"]'), 'first part has no previous').toHaveCount(0);
  expect(await pub.locator('[data-testid="series-next-post"]').getAttribute('href')).toBe(`/post/${dbSlug(P2)}`);
  await nav(P3);
  await expect(pub.locator('[data-testid="series-next-post"]'), 'last part has no next').toHaveCount(0);
  expect(await pub.locator('[data-testid="series-previous-post"]').getAttribute('href')).toBe(`/post/${dbSlug(P2)}`);
  await pub.screenshot({ path: `${SHOTS}/fn019-last-part.png` });
  await pub.context().close();
});

// -----------------------------------------------------------------------------------------------
// REQ-UI-024 — series list + manage series (incl. part reorder)
// -----------------------------------------------------------------------------------------------
test('REQ-UI-024 series list shows title and part count; a series loads its parts in order; reorder persists; a new series can be created', async () => {
  test.setTimeout(600000);
  await freshList(admin, '/admin/series', '[data-testid="series-grid"]');
  const row = admin.locator('tr').filter({ has: admin.locator('[data-testid="series-row-name"]', { hasText: new RegExp(`^${SERIES}$`) }) }).first();
  await expect(row).toBeVisible({ timeout: 30000 });
  const published = psql(`SELECT count(*) FROM blogpost p JOIN blogseries s ON s.seriesid=p.seriesid WHERE s.name='${SERIES}' AND p.published=TRUE AND p.isdeleted IS NOT TRUE`);
  await expect(row.locator('[data-testid="series-row-postcount"]'), 'part count equals the published parts').toHaveText(new RegExp(`^\\s*${published}\\b`));
  expect(published).toBe('3');
  await expect(row.locator('[data-testid="series-row-author"]')).not.toHaveText(/^(\s*|Unknown)$/);

  // open it: parts in part-number order
  const seriesId = psql(`SELECT seriesid FROM blogseries WHERE name='${SERIES}'`);
  await row.locator('[data-testid="series-edit"]').click();
  await expect(admin.locator('[data-testid="series-name-input"]')).toHaveValue(SERIES, { timeout: 30000 });
  await admin.waitForTimeout(2000);
  expect(await texts(admin, 'series-post-title')).toEqual([P1, P2, P3]);

  // reorder: move Part Two up (swap with Part One), save, read back
  const rows = admin.locator('[data-testid="series-post-row"]');
  await rows.nth(1).locator('[data-testid="series-post-up"]').click();
  await expect.poll(() => texts(admin, 'series-post-title'), { timeout: 15000, message: 'the list reflects the move before saving' }).toEqual([P2, P1, P3]);
  await admin.click('[data-testid="save-series"]');
  const order = async () => psqlRows(`SELECT title FROM blogpost WHERE seriesid=${seriesId} AND isdeleted IS NOT TRUE ORDER BY seriespartnumber`).map((r) => r[0]);
  await expect.poll(order, { timeout: 45000, message: 'the new order persisted' }).toEqual([P2, P1, P3]);
  expect(psqlRows(`SELECT seriespartnumber FROM blogpost WHERE seriesid=${seriesId} AND isdeleted IS NOT TRUE ORDER BY seriespartnumber`).map((r) => Number(r[0])), 'part numbers stay distinct').toEqual([1, 2, 3]);

  // reload shows the saved order
  await freshList(admin, '/admin/series', '[data-testid="series-grid"]');
  await goTo(admin, `/admin/series/${seriesId}`, '[data-testid="series-name-input"]', 120000);
  await admin.waitForTimeout(2000);
  expect(await texts(admin, 'series-post-title')).toEqual([P2, P1, P3]);

  // restore the original order (P1 first) and save, so the other tests see the fixture as built
  await admin.locator('[data-testid="series-post-row"]').nth(1).locator('[data-testid="series-post-up"]').click();
  await admin.waitForTimeout(800);
  await admin.click('[data-testid="save-series"]');
  await expect.poll(order, { timeout: 45000 }).toEqual([P1, P2, P3]);

  // a new series can be created and is listed
  const NEW = 'fxv3-newseries';
  for (let i = 0; i < 5 && psql(`SELECT count(*) FROM blogseries WHERE name='${NEW}'`) !== '0'; i++) {
    // leftovers of an aborted run, removed through the UI
    await deleteListRow(admin, { route: '/admin/series', ready: '[data-testid="series-grid"]', nameTid: 'series-row-name', name: NEW, deleteTid: 'series-delete', dialogTid: 'series-delete-dialog', confirmTid: 'series-delete-confirm' }).catch(() => false);
  }
  await goTo(admin, '/admin/series', '[data-testid="series-grid"]', 120000);
  await goTo(admin, '/admin/series/new', '[data-testid="series-name-input"]', 120000);
  await fillCommitted(admin, 'series-name-input', NEW);
  await fillCommitted(admin, 'series-description-input', 'created by W3');
  await pickSelect(admin, 'series-status-select', /Complete/);
  await admin.click('[data-testid="save-series"]');
  expect(await pollDb(`SELECT count(*) FROM blogseries WHERE name='${NEW}'`, (v) => v === '1')).toBe('1');
  expect(psql(`SELECT slug FROM blogseries WHERE name='${NEW}'`)).toBe(NEW);
  await freshList(admin, '/admin/series', '[data-testid="series-grid"]');
  await expect(admin.locator('[data-testid="series-row-name"]').filter({ hasText: NEW })).toHaveCount(1, { timeout: 20000 });
  // the status it was saved with renders as itself, and the Complete tab counts the database's completed series
  expect(psql(`SELECT status FROM blogseries WHERE name='${NEW}'`)).toBe('Completed');
  await expect(admin.locator('tr').filter({ has: admin.locator('[data-testid="series-row-name"]', { hasText: NEW }) }).locator('[data-testid="series-row-status"]')).toHaveText(/Completed/);
  await expect.poll(async () => (await admin.locator('[data-testid="series-tab-complete"]').first().innerText()).replace(/\D/g, ''), { timeout: 20000, message: 'Complete tab count equals completed series in the database' }).toBe(psql(`SELECT count(*) FROM blogseries WHERE status='Completed'`));
  await admin.screenshot({ path: `${SHOTS}/ui024-series-list-1280.png` });
  const v = await visualCheck(admin, `${SHOTS}/ui024-series-list-390.png`, 390);
  expect(v.hScroll).toBe(0);
  await admin.setViewportSize({ width: 1280, height: 900 });
});

// -----------------------------------------------------------------------------------------------
// REQ-FN-057 — the live half of the projection-completeness guard
// -----------------------------------------------------------------------------------------------
test('REQ-FN-057 read projections carry every mapped column: the RSS feed dates a post by its PublishedOn', async ({ browser }) => {
  test.setTimeout(300000);
  const pub = await browser.newContext();
  const xml = await (await pub.request.get(`${BASE}/rss.xml`)).text();
  const publishedOn = psql(`SELECT to_char(publishedon AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI') FROM blogpost WHERE title='${P1}' AND isdeleted IS NOT TRUE ORDER BY postid DESC LIMIT 1`);
  const item = xml.split('<item>').find((i) => i.includes(P1));
  expect(item, 'the published fixture is in the feed').toBeTruthy();
  const pubDate = /<pubDate>([^<]+)<\/pubDate>/.exec(item!)?.[1];
  expect(pubDate, 'the item carries a pubDate').toBeTruthy();
  expect(new Date(pubDate!).toISOString().slice(0, 16), 'pubDate follows PublishedOn').toBe(publishedOn);
  await pub.close();
});

// -----------------------------------------------------------------------------------------------
// Authorisation spot check: an Author sees only their own posts in the list (REQ-UI-017 role scoping)
// -----------------------------------------------------------------------------------------------
test('REQ-UI-017 an Author sees only their own posts in the post list', async ({ browser }) => {
  test.setTimeout(300000);
  const page = await newAdminPage(browser, 'author');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await page.waitForTimeout(2000);
  const titles = await texts(page, 'post-row-title');
  const names = await texts(page, 'post-row-author');
  const own = psql(`SELECT concat(firstname,' ',lastname) FROM bloguser WHERE emailid='author@techieblog.test'`);
  expect([...new Set(names)].filter((n) => n !== own), 'only the author\'s own name appears').toEqual([]);
  expect(titles.filter((t) => t.startsWith('fxv3 ')), 'admin fixtures are not listed for the author').toEqual([]);
  await page.context().close();
});

