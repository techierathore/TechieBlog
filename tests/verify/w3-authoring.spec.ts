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
// REQ-UI-016 — editor: live preview, every metadata field persists, route-parameter switch reloads all
// -----------------------------------------------------------------------------------------------
test('REQ-UI-016 post editor: live preview, saved metadata, and an A to B route switch reloads every field', async () => {
  test.setTimeout(900000);
  const A = 'fxv3 Editor A';
  const B = 'fxv3 Editor B';
  await purgePosts(admin, 'fxv3 Editor'); // leftovers of an aborted run

  // ---- live preview while typing (no blur, no save)
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(admin, '/ManagePost', '[data-testid="post-title-input"]', 120000);
  await admin.click('[data-testid="markdown-view-split"]').catch(() => {});
  const md = admin.locator('[data-testid="markdown-input"]');
  const preview = admin.locator('[data-testid="markdown-preview-content"]');
  await md.click();
  await md.pressSequentially('## Live heading', { delay: 400 });
  await expect(preview, 'preview shows the heading as it is typed, before any blur').toContainText('Live heading', { timeout: 15000 });
  expect(await preview.innerHTML()).toContain('<h2');
  await md.pressSequentially('\n\nthen **bold**', { delay: 300 });
  await expect(preview.locator('strong'), 'the preview follows further keystrokes').toHaveText('bold', { timeout: 15000 });
  await admin.screenshot({ path: `${SHOTS}/ui016-live-preview.png` });

  // ---- create A with every metadata field
  await fillCommitted(admin, 'post-title-input', A);
  expect(await setMarkdown(admin, MARKDOWN_A)).toBe(true);
  await expect(preview.locator('strong')).toHaveText('bold A', { timeout: 15000 });
  await fillCommitted(admin, 'post-slug-input', 'fxv3-editor-a');
  const slugA = () => dbSlug(A);
  await fillCommitted(admin, 'post-excerpt-input', 'fxv3 A abstract text');
  await pickSelect(admin, 'category-select', CAT_RE);
  await fillCommitted(admin, 'tag-input', 'fxv3-tag-a');
  await admin.click('[data-testid="add-tag"]');
  await admin.waitForTimeout(1200);
  await pickSelect(admin, 'series-select', new RegExp(EDSERIES));
  await admin.click('[data-testid="save-draft"]');
  expect(await pollDb(`SELECT count(*) FROM blogpost WHERE title='${A}' AND isdeleted IS NOT TRUE`, (v) => v === '1', 60000)).toBe('1');
  const idA = postId(A);
  const rowA = psqlRows(`SELECT title, slug, abstract, replace(postcontent, chr(10), '~'), categoryid, seriesid, seriespartnumber, published FROM blogpost WHERE postid=${idA}`)[0];
  expect(rowA[0]).toBe(A);
  expect(rowA[1]).toMatch(/^fxv3-editor-a(-\d+)?$/);
  expect(rowA[2]).toBe('fxv3 A abstract text');
  expect(rowA[3].replace(/~/g, '\n').trim()).toBe(MARKDOWN_A.trim());
  expect(rowA[4], 'category persisted').toBe(psql(`SELECT categoryid FROM category WHERE categoryname='${CAT}'`));
  expect(rowA[5], 'series persisted').toBe(psql(`SELECT seriesid FROM blogseries WHERE name='${EDSERIES}'`));
  expect(Number(rowA[6]), 'a part number was assigned').toBeGreaterThan(0);
  expect(rowA[7]).toBe('f');
  expect(psql(`SELECT count(*) FROM posttag pt JOIN tag t ON t.tagid=pt.tagid WHERE pt.postid=${idA} AND t.tagname='fxv3-tag-a'`), 'tag junction row').toBe('1');

  // ---- create B (different everything)
  const idB = await createPost(admin, { title: B, md: MARKDOWN_B, excerpt: 'fxv3 B abstract text', category: CAT2_RE, tags: [TAG] });
  const rowB0 = psql(`SELECT title||'|'||slug||'|'||abstract||'|'||postcontent||'|'||categoryid||'|'||coalesce(seriesid::text,'-') FROM blogpost WHERE postid=${idB}`);

  // ---- open A, then switch client-side to B (route parameter change, no page reload)
  await openPost(admin, idA);
  const stateA = await readEditor(admin);
  expect(stateA.title).toBe(A);
  expect(stateA.body).toBe(MARKDOWN_A);
  expect(stateA.series).toContain(EDSERIES);
  expect(stateA.part).toMatch(/Part \d+/);
  expect(stateA.tags.join(',')).toContain('fxv3-tag-a');

  await admin.evaluate((p) => (window as any).Blazor.navigateTo(p), `/ManagePost/${idB}`);
  await expect(admin.locator('[data-testid="post-title-input"]')).toHaveValue(B, { timeout: 30000 });
  await admin.waitForTimeout(3000);
  const stateB = await readEditor(admin);
  await admin.screenshot({ path: `${SHOTS}/ui016-switched-to-B.png` });
  expect(stateB.title, 'title reloaded').toBe(B);
  expect(stateB.slug, 'slug reloaded').toBe(dbSlug(B));
  expect(stateB.excerpt, 'abstract reloaded').toBe('fxv3 B abstract text');
  expect(stateB.body, 'body reloaded').toBe(MARKDOWN_B);
  expect(stateB.category, 'category reloaded').toContain(CAT2);
  expect(stateB.series, 'series reloaded (B has none)').not.toContain(EDSERIES);
  expect(stateB.part, 'part badge reloaded (B has none)').toBe('');
  expect(stateB.tags.join(','), 'tags reloaded').toContain(TAG);
  expect(stateB.tags.join(','), 'A\'s tag did not leak into B').not.toContain('fxv3-tag-a');

  // ---- saving from the switched-to editor must not write B's form over A (or A's over B)
  const beforeA = psql(`SELECT title||'|'||slug||'|'||abstract||'|'||postcontent||'|'||categoryid||'|'||seriesid||'|'||seriespartnumber FROM blogpost WHERE postid=${idA}`);
  await admin.click('[data-testid="save-draft"]');
  await admin.waitForTimeout(8000);
  expect(psql(`SELECT title||'|'||slug||'|'||abstract||'|'||postcontent||'|'||categoryid||'|'||seriesid||'|'||seriespartnumber FROM blogpost WHERE postid=${idA}`), 'post A untouched by B\'s save').toBe(beforeA);
  expect(psql(`SELECT title||'|'||slug||'|'||abstract||'|'||postcontent||'|'||categoryid||'|'||coalesce(seriesid::text,'-') FROM blogpost WHERE postid=${idB}`), 'post B keeps its own content').toBe(rowB0);

  // ---- and back to A client-side
  await admin.evaluate((p) => (window as any).Blazor.navigateTo(p), `/ManagePost/${idA}`);
  await expect(admin.locator('[data-testid="post-title-input"]')).toHaveValue(A, { timeout: 30000 });
  await admin.waitForTimeout(3000);
  const backA = await readEditor(admin);
  expect(backA.body).toBe(MARKDOWN_A);
  expect(backA.slug).toBe(slugA());
  expect(backA.series).toContain(EDSERIES);
  expect(backA.tags.join(',')).toContain('fxv3-tag-a');
});

// -----------------------------------------------------------------------------------------------
// REQ-UI-017 — post list
// -----------------------------------------------------------------------------------------------
test('REQ-UI-017 post list: real rows, status filters match the database, actions act on their own row, 390px strip', async () => {
  test.setTimeout(600000);
  await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');

  // Tab counts equal the database. Other writers change counts concurrently, so compare against a psql read
  // taken in the same poll, and require the UI to agree with SOME read within the window.
  const dbCounts = () => ({
    all: Number(psql(`SELECT count(*) FROM blogpost WHERE isdeleted IS NOT TRUE`)),
    pub: Number(psql(`SELECT count(*) FROM blogpost WHERE published = TRUE AND isdeleted IS NOT TRUE`)),
    sched: Number(psql(`SELECT count(*) FROM blogpost WHERE published = FALSE AND scheduledpublishon IS NOT NULL AND isdeleted IS NOT TRUE`)),
  });
  let agreed = false;
  let lastSeen = '';
  for (let i = 0; i < 12 && !agreed; i++) {
    await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');
    await expect(admin.locator('[data-testid="posts-tab-scheduled"]').first()).toContainText(/\(\d+\)/, { timeout: 45000 });
    await admin.waitForTimeout(1500);
    const ui = {
      all: await admin.locator('[data-testid="posts-tab-all"]').first().innerText(),
      pub: await admin.locator('[data-testid="posts-tab-published"]').first().innerText(),
      draft: await admin.locator('[data-testid="posts-tab-draft"]').first().innerText(),
      sched: await admin.locator('[data-testid="posts-tab-scheduled"]').first().innerText(),
    };
    const db = dbCounts();
    const dbDraft = db.all - db.pub - db.sched;
    lastSeen = `ui=${JSON.stringify(ui)} db=${JSON.stringify({ ...db, draft: dbDraft })}`;
    agreed = ui.all === `All (${db.all})` && ui.pub === `Published (${db.pub})` && ui.draft === `Drafts (${dbDraft})` && ui.sched === `Scheduled (${db.sched})`;
  }
  expect(agreed, `status tab counts equal the database: ${lastSeen}`).toBe(true);

  // Every post appears with its real title, status and date: search narrows the table to the fixtures.
  await admin.fill('[data-testid="posts-search"]', 'fxv3 ');
  await admin.waitForTimeout(1500);
  const expected = psqlRows(`SELECT title, published::text, to_char(coalesce(case when published then publishedon end, createdon),'Mon DD, YYYY') FROM blogpost WHERE title LIKE 'fxv3 %' AND isdeleted IS NOT TRUE`);
  expect(expected.length).toBeGreaterThanOrEqual(5);
  for (const [title, published, date] of expected) {
    const row = admin.locator('tr').filter({ has: admin.locator('[data-testid="post-row-title"]', { hasText: title }) }).first();
    await expect(row, `row for ${title}`).toBeVisible({ timeout: 20000 });
    const statusText = (await row.locator('[data-testid="post-row-status"]').innerText()).trim();
    if (published === 'true') expect(statusText, `status of ${title}`).toBe('Published'); else expect(statusText, `status of ${title} (published=${published})`).not.toBe('Published');
    expect((await row.locator('[data-testid="post-row-date"]').innerText()).trim(), `date of ${title}`).toBe(date);
    expect((await row.locator('[data-testid="post-row-author"]').innerText()).trim()).not.toMatch(/^(Unknown|)$/);
  }

  // Each status filter shows only that state.
  await admin.fill('[data-testid="posts-search"]', '');
  await admin.click('[data-testid="posts-tab-published"]');
  await admin.waitForTimeout(1500);
  let st = await texts(admin, 'post-row-status');
  expect(st.length).toBeGreaterThan(0);
  expect(st.filter((s) => s !== 'Published'), 'Published tab shows only published rows').toEqual([]);
  await admin.click('[data-testid="posts-tab-draft"]');
  await admin.waitForTimeout(1500);
  st = await texts(admin, 'post-row-status');
  expect(st.length).toBeGreaterThan(0);
  expect(st.filter((s) => s === 'Published'), 'Drafts tab shows no published rows').toEqual([]);
  const draftTitles = await texts(admin, 'post-row-title');
  expect(draftTitles).toContain(DRAFT);
  expect(draftTitles, 'a published fixture is not under Drafts').not.toContain(P1);
  await admin.click('[data-testid="posts-tab-all"]');

  // Row actions act on the row they sit in.
  await admin.fill('[data-testid="posts-search"]', DRAFT);
  await admin.waitForTimeout(1500);
  const row = admin.locator('tr').filter({ has: admin.locator('[data-testid="post-row-title"]', { hasText: DRAFT }) }).first();
  await expect(row).toBeVisible();
  const draftId = postId(DRAFT);
  expect(await row.locator('[data-testid="post-edit"]').getAttribute('href'), 'edit link targets its own row').toBe(`/ManagePost/${draftId}`);
  expect(await row.locator('[data-testid="post-row-title"]').getAttribute('href')).toBe(`/ManagePost/${draftId}`);
  await row.locator('[data-testid="post-delete"]').click();
  await expect(admin.locator('[data-testid="post-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await expect(admin.locator('[data-testid="post-delete-title"]'), 'the dialog names this row').toHaveText(DRAFT);
  await admin.click('[data-testid="post-delete-cancel"]');
  expect(psql(`SELECT isdeleted::text FROM blogpost WHERE postid=${draftId}`), 'cancel deletes nothing').not.toBe('true');
  await row.locator('[data-testid="post-preview"]').click();
  await expect(admin.locator('[data-testid="preview-title"]'), 'preview shows this row\'s post').toHaveText(DRAFT, { timeout: 60000 });
  expect(admin.url()).toContain(`/admin/preview/${draftId}`);

  // 390 px: the status strip scrolls inside its wrapper, the document does not overflow.
  await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');
  const v = await visualCheck(admin, `${SHOTS}/ui017-posts-390.png`, 390);
  expect(v.hScroll, 'no horizontal page scroll at 390').toBe(0);
  const strip = await admin.evaluate(() => {
    const s = document.querySelector('[data-testid="posts-status-tabs-scroller"]') as HTMLElement;
    const last = document.querySelector('[data-testid="posts-tab-scheduled"]') as HTMLElement;
    return { clientWidth: s.clientWidth, scrollWidth: s.scrollWidth, overflowX: getComputedStyle(s).overflowX, docWidth: document.documentElement.clientWidth, lastRight: last.getBoundingClientRect().right };
  });
  expect(strip.overflowX).toMatch(/auto|scroll/);
  expect(strip.clientWidth, 'wrapper fits the viewport').toBeLessThanOrEqual(strip.docWidth);
  await admin.setViewportSize({ width: 1280, height: 900 });
});

// -----------------------------------------------------------------------------------------------
// REQ-UI-018 — draft preview
// -----------------------------------------------------------------------------------------------
test('REQ-UI-018 draft preview renders the draft in full with a draft marker; anonymous cannot reach it', async ({ browser }) => {
  test.setTimeout(300000);
  const id = postId(DRAFT);
  expect(id).not.toBe('');
  await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await goTo(admin, `/admin/preview/${id}`, '[data-testid="preview-article"]', 120000);

  await expect(admin.locator('[data-testid="preview-title"]')).toHaveText(DRAFT);
  await expect(admin.locator('[data-testid="preview-banner"]'), 'preview banner').toContainText(/preview/i);
  await expect(admin.locator('[data-testid="preview-status"]'), 'visible draft marker').toContainText(/draft/i);
  const html = await admin.locator('[data-testid="preview-content"]').innerHTML();
  expect(html, 'Markdown rendered: heading').toContain('<h2');
  expect(html, 'Markdown rendered: bold').toContain('<strong>');
  expect(html, 'Markdown rendered: inline code').toContain('<code>');
  expect(html, 'Markdown rendered: list').toContain('<li>');
  expect(html, 'no raw Markdown left').not.toMatch(/\*\*bold\*\*/);
  await expect(admin.locator('[data-testid="preview-metadata"]')).toContainText(dbSlug(DRAFT));
  await expect(admin.locator('[data-testid="preview-reading-time"]')).toContainText(/\d+/);
  await expect(admin.locator('[data-testid="preview-created"]')).toContainText(/\d{4}/);
  const updated = (await admin.locator('[data-testid="preview-updated"]').innerText()).trim();
  expect(updated, 'the Updated date is real, not year 0001').not.toMatch(/0001/);
  await admin.screenshot({ path: `${SHOTS}/ui018-preview-1280.png` });
  const v = await visualCheck(admin, `${SHOTS}/ui018-preview-390.png`, 390);
  expect(v.hScroll).toBe(0);
  await admin.setViewportSize({ width: 1280, height: 900 });

  // anonymous visitor: no draft content, redirected to login
  const anon = await browser.newContext();
  const r = await anon.request.get(`${BASE}/admin/preview/${id}`, { maxRedirects: 0 });
  expect([301, 302, 303, 307, 401, 403]).toContain(r.status());
  if (r.status() >= 300 && r.status() < 400) expect(r.headers()['location'] ?? '').toMatch(/login/i);
  const body = await r.text();
  expect(body, 'the draft body never reaches an anonymous visitor').not.toContain('Fxv3 heading');
  await anon.close();
});

test('REQ-UI-018 draft preview shows the post\'s tags, as the published page will', async () => {
  test.setTimeout(300000);
  const id = postId(DRAFT);
  const dbTags = psqlRows(`SELECT t.tagname FROM posttag pt JOIN tag t ON t.tagid=pt.tagid WHERE pt.postid=${id}`).map((r) => r[0]);
  expect(dbTags, 'the draft carries tags in the junction table').toContain(TAG);
  await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await goTo(admin, `/admin/preview/${id}`, '[data-testid="preview-article"]', 120000);
  await admin.screenshot({ path: `${SHOTS}/ui018-preview-tags-missing.png`, fullPage: true });
  await expect(admin.locator('[data-testid="preview-tag"]').filter({ hasText: TAG }), 'the preview lists the post\'s tag').toHaveCount(1, { timeout: 15000 });
});

// -----------------------------------------------------------------------------------------------
// REQ-FN-012 — post create / read / update / delete
// -----------------------------------------------------------------------------------------------
test('REQ-FN-012 post CRUD: create saves every field, edit updates the row, delete leaves every listing', async ({ browser }) => {
  test.setTimeout(600000);
  const T = 'fxv3 Crud Post';
  const T2 = 'fxv3 Crud Post v2';
  await purgePosts(admin, 'fxv3 Crud'); // leftovers of an aborted run
  const liveBefore = Number(psql(`SELECT count(*) FROM blogpost WHERE title IN ('${T}','${T2}') AND isdeleted IS NOT TRUE`)); // leftovers of an aborted run
  const id = await createPost(admin, { title: T, md: BODY, excerpt: 'fxv3 crud excerpt', category: CAT_RE, tags: ['fxv3-crud-tag'], mode: 'publish' });
  let row = psqlRows(`SELECT title, slug, abstract, replace(postcontent, chr(10), '~'), categoryid, published::text, (publishedon IS NOT NULL)::text, userid FROM blogpost WHERE postid=${id}`)[0];
  expect(row[0]).toBe(T);
  expect(row[1]).toMatch(new RegExp(`^${slugOf(T)}(-\\d+)?$`));
  expect(row[2]).toBe('fxv3 crud excerpt');
  expect(row[3].replace(/~/g, '\n').trim()).toBe(BODY.trim());
  expect(row[4]).toBe(psql(`SELECT categoryid FROM category WHERE categoryname='${CAT}'`));
  expect(row[5]).toBe('true');
  expect(row[6]).toBe('true');
  expect(psql(`SELECT count(*) FROM posttag pt JOIN tag t ON t.tagid=pt.tagid WHERE pt.postid=${id} AND t.tagname='fxv3-crud-tag'`)).toBe('1');

  // READ: the editor loads the stored values back
  await openPost(admin, id);
  await expect(admin.locator('[data-testid="post-title-input"]')).toHaveValue(T);
  await expect(admin.locator('[data-testid="post-excerpt-input"]')).toHaveValue('fxv3 crud excerpt');
  expect((await admin.inputValue('[data-testid="markdown-input"]')).trim()).toBe(BODY.trim());

  // public while published
  const pub = await browser.newContext();
  expect((await pub.request.get(`${BASE}/post/${row[1]}`)).status(), 'published post is public').toBe(200);

  // UPDATE
  await fillCommitted(admin, 'post-title-input', T2);
  await fillCommitted(admin, 'post-excerpt-input', 'fxv3 crud edited');
  await admin.click('[data-testid="save-post"]');
  expect(await pollDb(`SELECT title FROM blogpost WHERE postid=${id}`, (v) => v === T2, 60000), 'edit updated the row').toBe(T2);
  expect(psql(`SELECT abstract FROM blogpost WHERE postid=${id}`)).toBe('fxv3 crud edited');
  expect(Number(psql(`SELECT count(*) FROM blogpost WHERE title IN ('${T}','${T2}') AND isdeleted IS NOT TRUE`)), 'edit did not create a second row').toBe(liveBefore + 1);

  // DELETE through the list
  const slug = psql(`SELECT slug FROM blogpost WHERE postid=${id}`);
  await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await admin.fill('[data-testid="posts-search"]', T2);
  await admin.waitForTimeout(1500);
  const mine = admin.locator('[data-testid="post-row-title"]').filter({ hasText: T2 }).first();
  await expect(mine).toBeVisible();
  await mine.locator('xpath=ancestor::tr[1]').locator('[data-testid="post-delete"]').click();
  await admin.click('[data-testid="post-delete-confirm"]');
  expect(await pollDb(`SELECT isdeleted::text FROM blogpost WHERE postid=${id}`, (v) => v === 'true', 30000)).toBe('true');
  await admin.fill('[data-testid="posts-search"]', '').catch(() => {});
  await freshList(admin, '/BlogsList', '[data-testid="posts-status-tabs"]');
  expect(await texts(admin, 'post-row-title'), 'gone from the post list').not.toContain(T2);

  // ... and from every public listing
  expect((await pub.request.get(`${BASE}/post/${slug}`)).status(), 'public post page is 404 after delete').toBe(404);
  const home = await (await pub.request.get(`${BASE}/`)).text();
  const rss = await (await pub.request.get(`${BASE}/rss.xml`)).text();
  const cat = await (await pub.request.get(`${BASE}/category/${CAT}`)).text();
  const tag = await (await pub.request.get(`${BASE}/tag/fxv3-crud-tag`)).text();
  for (const [name, text] of [['home', home], ['rss', rss], ['category archive', cat], ['tag archive', tag]] as const) {
    expect(text, `${name} does not list the deleted post`).not.toContain(T2);
    expect(text, `${name} does not list the deleted post`).not.toContain(slug);
  }
  await pub.close();
});

