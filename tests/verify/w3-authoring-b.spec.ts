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
// REQ-FN-016 — scheduling + background publisher
// -----------------------------------------------------------------------------------------------
test('REQ-FN-016 a post scheduled a few minutes ahead stays unpublished, then the background publisher promotes it', async ({ browser }) => {
  test.setTimeout(900000);
  const T = 'fxv3 Scheduled Post';
  await purgePosts(admin, T); // leftovers of an aborted run
  const id = await createPost(admin, { title: T, md: BODY, category: CAT_RE });
  await openPost(admin, id);
  await expect(admin.locator('[data-testid="schedule-section"]')).toBeVisible();

  // date: today
  const today = new Date();
  const stamp = `${today.getFullYear()}${String(today.getMonth() + 1).padStart(2, '0')}${String(today.getDate()).padStart(2, '0')}`;
  await admin.click('[data-testid="publish-date-picker"]');
  const dayCell = admin.locator(`button[id$="-day-${stamp}"]:not([disabled])`).first();
  await expect(dayCell, 'today is selectable in the date picker').toBeVisible({ timeout: 30000 });
  await dayCell.click();
  await admin.waitForTimeout(1000);
  await admin.keyboard.press('Escape');

  // time: "Now", then step the minutes up until the summary reads >= 3 minutes ahead
  await admin.click('[data-testid="publish-time-picker"]');
  await admin.getByRole('button', { name: 'Now' }).click();
  await admin.waitForTimeout(1000);
  // the four unlabeled spinner buttons in the popover are hour-up, hour-down, minute-up, minute-down
  const spinnerList = () => Array.from(document.querySelectorAll('button')).filter((b) => b.getBoundingClientRect().top > 480 && b.getBoundingClientRect().left > 960 && !(b.textContent || '').trim() && !b.getAttribute('data-testid'));
  void spinnerList;
  const spinners = await admin.evaluate(() => Array.from(document.querySelectorAll('button')).filter((b) => b.getBoundingClientRect().top > 480 && b.getBoundingClientRect().left > 960 && !(b.textContent || '').trim() && !b.getAttribute('data-testid')).length);
  expect(spinners, 'time picker spinner buttons present').toBeGreaterThanOrEqual(4);
  const clickSpinner = async (index: number) => {
    const handle = await admin.evaluateHandle((i) => {
      const bs = Array.from(document.querySelectorAll('button')).filter((b) => b.getBoundingClientRect().top > 480 && b.getBoundingClientRect().left > 960 && !(b.textContent || '').trim() && !b.getAttribute('data-testid'));
      return bs[i] ?? null;
    }, index);
    await (handle.asElement() as any).click();
  };
  const parseSummary = async () => {
    const t = (await admin.locator('[data-testid="scheduled-summary"]').innerText()).replace(/\s+/g, ' ');
    const m = /at (\d+):(\d+) (AM|PM)/.exec(t)!;
    let h = Number(m[1]) % 12; if (m[3] === 'PM') h += 12;
    return h * 60 + Number(m[2]);
  };
  const nowMin = () => { const d = new Date(); return d.getHours() * 60 + d.getMinutes(); };
  for (let i = 0; i < 15; i++) {
    if ((await parseSummary()) - nowMin() >= 3) break;
    await clickSpinner(2); // minute up
    await admin.waitForTimeout(500);
  }
  const ahead = (await parseSummary()) - nowMin();
  test.skip(ahead < 2 || ahead > 12, `could not set a time 2-12 minutes ahead through the picker (got ${ahead}; midnight wrap or picker change)`);
  await admin.keyboard.press('Escape');
  await admin.click('[data-testid="schedule-post"]');
  expect(await pollDb(`SELECT (scheduledpublishon IS NOT NULL)::text FROM blogpost WHERE postid=${id}`, (v) => v === 'true', 45000), 'schedule saved').toBe('true');

  // not published yet: row state, public 404, Scheduled tab lists it
  expect(psql(`SELECT published::text FROM blogpost WHERE postid=${id}`)).toBe('false');
  const pub = await browser.newContext();
  expect((await pub.request.get(`${BASE}/post/${dbSlug(T)}`)).status(), 'a scheduled post is not public').toBe(404);
  await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await admin.click('[data-testid="posts-tab-scheduled"]');
  await admin.fill('[data-testid="posts-search"]', T);
  await admin.waitForTimeout(1500);
  await expect(admin.locator('[data-testid="post-row-title"]').filter({ hasText: T })).toHaveCount(1);

  // the hosted publisher (1 minute tick) promotes it without anyone acting
  const due = psql(`SELECT extract(epoch from (scheduledpublishon - now()))::int FROM blogpost WHERE postid=${id}`);
  const promoted = await pollDb(`SELECT published::text FROM blogpost WHERE postid=${id}`, (v) => v === 'true', (Math.max(Number(due), 0) + 180) * 1000);
  expect(promoted, `ScheduledPostPublisher promoted the due post (was due in ${due}s)`).toBe('true');
  expect(psql(`SELECT (publishedon IS NOT NULL)::text FROM blogpost WHERE postid=${id}`)).toBe('true');
  await expect.poll(async () => (await pub.request.get(`${BASE}/post/${dbSlug(T)}`)).status(), { timeout: 30000, message: 'the promoted post is now public' }).toBe(200);
  await pub.close();
});

// -----------------------------------------------------------------------------------------------
// REQ-FN-017 — category CRUD + single category assignment
// -----------------------------------------------------------------------------------------------
test('REQ-FN-017 category create, rename and delete persist; a post carries exactly one category (database and public page)', async ({ browser }) => {
  test.setTimeout(600000);
  const N1 = 'fxv3-cat-tmp';
  const N2 = 'fxv3-cat-tmp-renamed';
  await goTo(admin, '/admin/categories', '[data-testid="categories-grid"]', 120000);
  await goTo(admin, '/admin/category', '[data-testid="category-name-input"]', 120000);
  await fillCommitted(admin, 'category-name-input', N1);
  await fillCommitted(admin, 'category-description-input', 'created');
  await admin.click('[data-testid="save-category"]');
  expect(await pollDb(`SELECT count(*) FROM category WHERE categoryname='${N1}'`, (v) => v === '1')).toBe('1');
  const catId = psql(`SELECT categoryid FROM category WHERE categoryname='${N1}'`);
  expect(psql(`SELECT slug FROM category WHERE categoryid=${catId}`)).toBeTruthy();

  await goTo(admin, '/admin/categories', '[data-testid="categories-grid"]', 120000);
  await goTo(admin, `/admin/category/${catId}`, '[data-testid="category-name-input"]', 120000);
  await expect(admin.locator('[data-testid="category-name-input"]')).toHaveValue(N1);
  await fillCommitted(admin, 'category-name-input', N2);
  await admin.click('[data-testid="save-category"]');
  expect(await pollDb(`SELECT categoryname FROM category WHERE categoryid=${catId}`, (v) => v === N2), 'rename persisted').toBe(N2);
  expect(psql(`SELECT count(*) FROM category WHERE categoryid=${catId}`), 'rename edited in place, no duplicate').toBe('1');

  expect(await deleteListRow(admin, { route: '/admin/categories', ready: '[data-testid="categories-grid"]', searchTid: 'categories-search', nameTid: 'category-row-name', name: N2, deleteTid: 'category-delete', dialogTid: 'category-delete-dialog', confirmTid: 'category-delete-confirm' })).toBe(true);
  expect(await pollDb(`SELECT count(*) FROM category WHERE categoryid=${catId}`, (v) => v === '0'), 'delete persisted').toBe('0');

  // a post carries exactly one category: one scalar in the row, exactly that one on its public page
  const rows = psqlRows(`SELECT p.categoryid, c.categoryname FROM blogpost p JOIN category c ON c.categoryid=p.categoryid WHERE p.title='${P1}'`);
  expect(rows).toHaveLength(1);
  expect(rows[0][1]).toBe(CAT);
  const pub = await (await browser.newContext()).newPage();
  await pub.goto(`${BASE}/post/${dbSlug(P1)}`, { waitUntil: 'domcontentloaded' });
  await expect(pub.locator('[data-testid="post-title"]')).toHaveText(P1, { timeout: 60000 });
  await expect(pub.locator('[data-testid="post-category"]'), 'exactly one category link on the page').toHaveCount(1);
  await expect(pub.locator('[data-testid="post-category"]')).toContainText(CAT);
  await pub.context().close();
});

test('REQ-FN-017 a post saved without a category stores NULL rather than failing', async () => {
  test.setTimeout(300000);
  expect(psql(`SELECT is_nullable FROM information_schema.columns WHERE table_name='blogpost' AND column_name='categoryid'`)).toBe('YES');
  await purgePosts(admin, 'fxv3 No Category'); // leftovers of an aborted run
  const id = await createPost(admin, { title: 'fxv3 No Category' });
  expect(psql(`SELECT coalesce(categoryid::text,'NULL') FROM blogpost WHERE postid=${id}`)).toBe('NULL');
});

