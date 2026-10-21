/**
 * vall-authoring.spec.ts — 2026-08-08 `*verify all` run, AUTHORING cluster.
 *
 * Covers REQ-UI-016/017/018/024 and REQ-FN-012…019 against the live host on :5399.
 * Every assertion is cross-checked against PostgreSQL so a green screen with wrong numbers
 * cannot pass. Rows this spec creates are prefixed `VERIFY-0808-` and hard-deleted at the end
 * (`BlogSvc.DeletePost` is a SOFT delete, so cleanup goes through psql).
 *
 * Ordering matters and the file is therefore serial: REQ-UI-017 measures the untouched seed
 * data and must run before anything is created.
 */
import { test, expect, Browser, Page } from '@playwright/test';
import { execSync } from 'node:child_process';
import { visualCheck, renderCheck, BASE } from './_gates';
import { loginHard, goTo, pickSelect, setMarkdown, fillCommitted, rowPairs, texts, SHOTS, MARK } from './vall-authoring-helpers';

// NOT serial: the tests are order-dependent (REQ-UI-017 must measure untouched seed data, and
// REQ-UI-016 creates the row the later CRUD tests edit), and Playwright already runs a file's
// tests in declaration order on one worker. Serial mode would additionally SKIP every remaining
// test after the first failure, which would hide the verdicts this run exists to produce.
test.describe.configure({ mode: 'default' });

/** Runs read-only SQL inside the shared WinPostgre container and returns tab-free rows. */
function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

function sqlOne(query: string): string {
  return sql(query)[0] ?? '';
}

/**
 * Re-reads a scalar until it satisfies `done`, or the deadline passes.
 * Blazor Server writes the row and re-renders asynchronously, so a fixed `waitForTimeout` either
 * flakes on a slow host or wastes seconds on a fast one.
 */
async function pollSql(query: string, done: (value: string) => boolean, timeoutMs = 45000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  let last = '';
  while (Date.now() < deadline) {
    last = sqlOne(query);
    if (done(last)) return last;
    await new Promise((r) => setTimeout(r, 2000));
  }
  return last;
}

/** Id lookups must never silently produce an empty string — that turns into broken SQL later. */
function requireId(query: string, what: string): string {
  const id = sqlOne(query);
  if (!id) throw new Error(`expected ${what} to exist in the database, found nothing`);
  return id;
}

/** Cleanup helper — the app soft-deletes, so verification rows are removed at the source. */
function purgeVerifyRows() {
  sql(`DELETE FROM posttag WHERE postid IN (SELECT postid FROM blogpost WHERE title LIKE '${MARK}%')`);
  sql(`DELETE FROM posttag WHERE tagid IN (SELECT tagid FROM tag WHERE tagname LIKE '${MARK}%')`);
  sql(`DELETE FROM blogpost WHERE title LIKE '${MARK}%'`);
  sql(`DELETE FROM tag WHERE tagname LIKE '${MARK}%'`);
  sql(`DELETE FROM category WHERE categoryname LIKE '${MARK}%'`);
  sql(`UPDATE blogpost SET seriesid = NULL, seriespartnumber = NULL WHERE seriesid IN (SELECT seriesid FROM blogseries WHERE name LIKE '${MARK}%')`);
  sql(`DELETE FROM blogseries WHERE name LIKE '${MARK}%'`);
}

const POST_TITLE = `${MARK}Kitchen Draft`;
const POST_MD = '## Verify heading\n\nSome **bold** text and a `code` span.\n\n- one\n- two\n';

let admin: Page;
const notes: string[] = [];

/**
 * NOTE ON WORKER RECYCLING — this cost a whole run.
 * Playwright tears the worker process down after a failing test and starts a fresh one, which
 * re-runs `beforeAll`. Purging in `beforeAll` therefore deleted the VERIFY draft the moment any
 * earlier test failed, and four later tests reported "the draft does not exist" instead of their
 * real verdict. So: `beforeAll` only signs in, purging happens in the first and last tests, and
 * any test needing the draft calls `ensureVerifyDraft()` which re-creates it through the editor.
 */
test.beforeAll(async ({ browser }: { browser: Browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  admin = await ctx.newPage();
  await loginHard(admin, 'admin');
});

test.afterAll(async () => {
  console.log('NOTES\n' + notes.join('\n'));
});

/** Creates the marked draft through the real editor. Returns its post id. */
async function createVerifyDraft(title: string): Promise<string> {
  // Leave any editor we may already be on: navigateTo the same route is a no-op in Blazor and the
  // component would keep the previously loaded post, turning "create" into "update".
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(admin, '/ManagePost', '[data-testid="post-title-input"]', 120000);
  await fillCommitted(admin, 'post-title-input', title);
  await setMarkdown(admin, POST_MD);
  await fillCommitted(admin, 'post-excerpt-input', 'Verification excerpt.');
  await pickSelect(admin, 'category-select', 'Programming');
  await pickSelect(admin, 'series-select', /Blazor Server in Production/);
  await admin.click('[data-testid="save-draft"]');
  await expect(admin.locator('[data-testid="post-status-message"]')).toContainText(/Draft (created|saved) successfully/i, { timeout: 45000 });
  return requireId(`SELECT postid FROM blogpost WHERE title = '${title}' AND isdeleted IS NOT TRUE`, `the draft "${title}"`);
}

/** The post id of the VERIFY draft, re-created if a recycled worker or a purge lost it. */
async function ensureVerifyDraft(): Promise<string> {
  const existing = sqlOne(
    `SELECT postid FROM blogpost WHERE title LIKE '${POST_TITLE}%' AND isdeleted IS NOT TRUE ORDER BY postid LIMIT 1`,
  );
  if (existing) return existing;
  notes.push('re-created the VERIFY draft (previous worker was recycled after a failure)');
  return createVerifyDraft(POST_TITLE);
}

// ---------------------------------------------------------------------------------------------
// REQ-UI-017 — post list, role scoping, real author names/dates, tab counts that match psql.
// Runs FIRST, against untouched seed data.
// ---------------------------------------------------------------------------------------------
test('REQ-UI-017 post list scopes rows by role and every status tab count matches PostgreSQL', async ({ browser }) => {
  test.setTimeout(300000);
  purgeVerifyRows(); // clear anything a previous run left behind, before measuring the seed

  const dbAll = Number(sqlOne('SELECT count(*) FROM blogpost WHERE isdeleted IS NOT TRUE'));
  const dbPub = Number(sqlOne('SELECT count(*) FROM blogpost WHERE published = TRUE AND isdeleted IS NOT TRUE'));
  const dbSched = Number(sqlOne('SELECT count(*) FROM blogpost WHERE published = FALSE AND scheduledpublishon IS NOT NULL AND isdeleted IS NOT TRUE'));
  const dbDraft = dbAll - dbPub - dbSched;

  // ---- Admin sees everything ----
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await expect(admin.locator('h1').filter({ hasText: 'All Posts' })).toBeVisible();

  const adminTabs = await texts(admin, 'posts-tab-all');
  expect(adminTabs[0]).toBe(`All (${dbAll})`);
  expect((await texts(admin, 'posts-tab-published'))[0]).toBe(`Published (${dbPub})`);
  expect((await texts(admin, 'posts-tab-draft'))[0]).toBe(`Drafts (${dbDraft})`);
  // The known defect this REQ fixed: the Scheduled tab could only ever render 0.
  expect((await texts(admin, 'posts-tab-scheduled'))[0]).toBe(`Scheduled (${dbSched})`);
  expect(dbSched).toBeGreaterThan(0);

  const adminTitles = await texts(admin, 'post-row-title');
  expect(adminTitles.length).toBe(dbAll);

  // §4a — every listed control must render DATA, not just exist.
  const adminAuthors = await texts(admin, 'post-row-author');
  const adminDates = await texts(admin, 'post-row-date');
  expect(adminAuthors.length).toBe(dbAll);
  expect(adminAuthors.filter((a) => a === 'Unknown' || a === '')).toHaveLength(0);
  expect(adminDates.filter((d) => !/^[A-Z][a-z]{2} \d{2}, \d{4}$/.test(d))).toHaveLength(0);
  const adminStatuses = await texts(admin, 'post-row-status');
  expect(adminStatuses.filter((s) => s === 'Published').length).toBe(dbPub);
  expect(adminStatuses.filter((s) => s === 'Draft').length).toBe(dbDraft);
  notes.push(`UI-017 admin: rows=${adminTitles.length} tabs=${dbAll}/${dbPub}/${dbDraft}/${dbSched} authors=${[...new Set(adminAuthors)].join(',')}`);

  // Published rows must show the publish date, not CreatedOn (the other half of the fixed defect).
  const dbPublishedOn = sql(
    "SELECT title, to_char(publishedon,'Mon DD, YYYY') FROM blogpost WHERE published = TRUE AND isdeleted IS NOT TRUE",
  ).map((r) => r.split('|'));
  const rowMap = new Map<string, string>();
  for (let i = 0; i < adminTitles.length; i++) rowMap.set(adminTitles[i], adminDates[i]);
  for (const [title, expected] of dbPublishedOn) {
    expect(rowMap.get(title), `date for "${title}"`).toBe(expected);
  }

  const v1280 = await visualCheck(admin, `${SHOTS}/ui017-blogslist-admin-1280.png`, 1280);
  const v390 = await visualCheck(admin, `${SHOTS}/ui017-blogslist-admin-390.png`, 390);
  notes.push(`UI-017 visual 1280: ${JSON.stringify({ o: v1280.overlaps.length, z: v1280.zeroSized, off: v1280.offViewport, h: v1280.hScroll, e: v1280.consoleErrors })}`);
  notes.push(`UI-017 visual 390: ${JSON.stringify({ o: v390.overlaps.length, z: v390.zeroSized, off: v390.offViewport, h: v390.hScroll, e: v390.consoleErrors })}`);
  await admin.setViewportSize({ width: 1280, height: 900 });

  // ---- Author sees ONLY her own posts ----
  const authorCtx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const author = await authorCtx.newPage();
  await loginHard(author, 'author');
  await goTo(author, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);

  const ownAll = Number(sqlOne("SELECT count(*) FROM blogpost p JOIN bloguser u ON p.userid = u.userid WHERE u.emailid = 'author@techieblog.test' AND p.isdeleted IS NOT TRUE"));
  const ownName = sqlOne("SELECT concat(firstname,' ',lastname) FROM bloguser WHERE emailid = 'author@techieblog.test'");
  const authorTitles = await texts(author, 'post-row-title');
  const authorNames = await texts(author, 'post-row-author');
  expect(ownAll).toBeGreaterThan(0);
  expect(ownAll).toBeLessThan(dbAll); // scoping has to actually remove something
  expect(authorTitles.length).toBe(ownAll);
  expect((await texts(author, 'posts-tab-all'))[0]).toBe(`All (${ownAll})`);
  expect([...new Set(authorNames)]).toEqual([ownName]);
  notes.push(`UI-017 author: rows=${authorTitles.length}/${ownAll} name="${ownName}" titles=${authorTitles.join(' | ')}`);

  const av = await visualCheck(author, `${SHOTS}/ui017-blogslist-author-1280.png`, 1280);
  notes.push(`UI-017 author visual: ${JSON.stringify({ o: av.overlaps.length, z: av.zeroSized, off: av.offViewport, h: av.hScroll, e: av.consoleErrors })}`);
  await authorCtx.close();
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-016 — editor: live preview + metadata sidebar that actually persists.
// Also creates the VERIFY draft used by the later CRUD tests.
// ---------------------------------------------------------------------------------------------
test('REQ-UI-016 post editor renders a live Markdown preview and persists every metadata field', async () => {
  test.setTimeout(300000);
  await goTo(admin, '/ManagePost', '[data-testid="post-title-input"]', 120000);

  // §4a — the DevGuide's control map for this screen.
  const controls = [
    await renderCheck(admin, 'Title input', '[data-testid="post-title-input"]', 'present'),
    await renderCheck(admin, 'Markdown editor', '[data-testid="markdown-editor"]', 'present'),
    await renderCheck(admin, 'Markdown toolbar', '[data-testid="markdown-toolbar"]', 'present'),
    await renderCheck(admin, 'Category dropdown', '[data-testid="category-select"]', 'present'),
    await renderCheck(admin, 'Tag input', '[data-testid="tag-input"]', 'present'),
    // Quick-add is a flex row of buttons, not a table/list, so it gets its own emptiness rule:
    // the control only counts as rendering DATA if it offers at least one real existing tag.
    await (async () => {
      const chips = await texts(admin, 'quick-add-tag');
      const nonBlank = chips.filter((c) => c.replace(/^\+/, '').trim().length > 0);
      return {
        control: 'Quick-add tags (tag autocomplete source)',
        verdict: nonBlank.length > 0 ? ('RENDERS' as const) : ('RENDER-EMPTY' as const),
        detail: `${nonBlank.length} existing tags offered: ${nonBlank.join(',')}`,
      };
    })(),
    await renderCheck(admin, 'Series selector', '[data-testid="series-select"]', 'present'),
    await renderCheck(admin, 'Featured image card', '[data-testid="featured-image-card"]', 'present'),
    await renderCheck(admin, 'Schedule section', '[data-testid="schedule-section"]', 'present'),
    await renderCheck(admin, 'Save draft', '[data-testid="save-draft"]', 'present'),
    await renderCheck(admin, 'Publish', '[data-testid="publish-post"]', 'present'),
  ];
  notes.push('UI-016 controls: ' + controls.map((c) => `${c.control}=${c.verdict}(${c.detail.slice(0, 40)})`).join('; '));
  expect(controls.filter((c) => c.verdict !== 'RENDERS').map((c) => c.control)).toEqual([]);

  // Live preview, part 1 — the acceptance criterion is "preview updates as the author types",
  // so this types real keystrokes and never blurs the textarea. Several cadences are measured
  // because the first run showed characters going missing; a single speed could not tell a
  // Playwright artefact apart from a real editor defect.
  await fillCommitted(admin, 'post-title-input', POST_TITLE);
  const md = admin.locator('[data-testid="markdown-input"]');
  const TYPED = '## Live heading';
  const typingProbe: { delay: number; got: string; preview: string }[] = [];
  for (const delay of [120, 1000]) {
    await setMarkdown(admin, '');
    await md.click();
    await md.pressSequentially(TYPED, { delay });
    await admin.waitForTimeout(3000);
    typingProbe.push({
      delay,
      got: await admin.inputValue('[data-testid="markdown-input"]'),
      preview: (await admin.locator('[data-testid="markdown-preview-content"]').innerHTML().catch(() => '')).replace(/\s+/g, ' ').slice(0, 80),
    });
  }
  for (const t of typingProbe) {
    notes.push(`UI-016 typed "${TYPED}" at ${t.delay}ms/key -> textarea="${t.got}" (${t.got.length}/${TYPED.length} chars) preview=${JSON.stringify(t.preview)}`);
  }
  // The preview does follow the keystrokes (that half of the AC holds) …
  expect(typingProbe.some((t) => /<h2/.test(t.preview)), 'preview updates while typing, before any blur or save').toBeTruthy();
  // … the textarea-fidelity half is asserted at the very END of this test, so that a failure there
  // still leaves the VERIFY draft created for the CRUD tests that follow.
  const bestTyping = typingProbe.reduce((a, b) => (b.got.length > a.got.length ? b : a));

  // Live preview, part 2 — the full exerciser body through every Markdig construct.
  const bodyAccepted = await setMarkdown(admin, POST_MD);
  notes.push(`UI-016 body accepted by the editor: ${bodyAccepted}`);
  expect(bodyAccepted, 'the editor must hold the Markdown body that was written into it').toBeTruthy();
  const preview = admin.locator('[data-testid="markdown-preview-content"]');
  await expect(preview).toBeVisible({ timeout: 20000 });
  const previewHtml = await preview.innerHTML();
  notes.push(`UI-016 preview html: ${previewHtml.replace(/\s+/g, ' ').slice(0, 200)}`);
  expect(previewHtml).toContain('<h2');
  expect(previewHtml).toContain('<strong>');
  expect(previewHtml).toContain('<code>');
  expect(previewHtml).toContain('<li>');

  // Slug auto-generates from the title (REQ-FN-013 surface).
  const slugValue = await admin.inputValue('[data-testid="post-slug-input"]');
  notes.push(`UI-016/FN-013 auto slug from "${POST_TITLE}" = "${slugValue}"`);
  expect(slugValue).toBe('verify-0808-kitchen-draft');

  // Metadata sidebar
  const editorDefects: string[] = [];
  if (bestTyping.got !== TYPED) {
    editorDefects.push(
      `markdown editor loses/reorders keystrokes — typing "${TYPED}" gave "${bestTyping.got}" even at ${bestTyping.delay}ms per key`,
    );
  }

  await fillCommitted(admin, 'post-excerpt-input', 'Verification excerpt.');
  await pickSelect(admin, 'category-select', 'Programming');
  await pickSelect(admin, 'series-select', /Blazor Server in Production/);

  // The tag Input binds on `change`, and the Add button is disabled while the bound value is
  // empty, so the typed name has to be committed (blur) before Add is clickable.
  await fillCommitted(admin, 'tag-input', `${MARK}tag`);
  await admin.waitForTimeout(800);
  await admin.click('[data-testid="add-tag"]');
  await admin.waitForTimeout(1200);
  const quickAdd = admin.locator('[data-testid="quick-add-tag"]').first();
  const quickAddLabel = (await quickAdd.textContent())?.trim() ?? '';
  await quickAdd.click();
  await admin.waitForTimeout(1200);
  const chips = await texts(admin, 'selected-tag');
  notes.push(`UI-016 tag chips after inline-create + quick-add("${quickAddLabel}"): ${chips.join(' , ')}`);
  if (chips.length < 2) {
    editorDefects.push(`tag sidebar kept only ${chips.length} of 2 tags (inline-created + quick-added): ${chips.join(',')}`);
  }

  await admin.click('[data-testid="save-draft"]');
  await expect(admin.locator('[data-testid="post-status-message"]')).toContainText(/Draft (created|saved) successfully/i, { timeout: 45000 });

  // Truth check — every metadata field must be on the row, not just on the screen.
  const row = sql(
    `SELECT postid, slug, published, categoryid, seriesid, seriespartnumber, coalesce(abstract,'') FROM blogpost WHERE title = '${POST_TITLE}'`,
  )[0];
  expect(row, 'saved post row').toBeTruthy();
  const [postId, slug, published, categoryId, seriesId, partNo, abstract] = row.split('|');
  notes.push(`UI-016 persisted row: id=${postId} slug=${slug} published=${published} cat=${categoryId} series=${seriesId} part=${partNo} abstract="${abstract}"`);
  expect(published).toBe('f');
  expect(slug).toBe('verify-0808-kitchen-draft');
  expect(categoryId).toBe(sqlOne("SELECT categoryid FROM category WHERE categoryname = 'Programming'"));
  expect(seriesId).toBe('1');
  expect(Number(partNo)).toBeGreaterThan(0);
  expect(abstract).toBe('Verification excerpt.');
  const tagCount = Number(sqlOne(`SELECT count(*) FROM posttag WHERE postid = ${postId}`));
  notes.push(`UI-016 posttag rows persisted for post ${postId}: ${tagCount}`);
  if (tagCount < 2) editorDefects.push(`only ${tagCount} of 2 chosen tags reached the posttag junction`);

  const v1 = await visualCheck(admin, `${SHOTS}/ui016-managepost-1280.png`, 1280);
  const v2 = await visualCheck(admin, `${SHOTS}/ui016-managepost-390.png`, 390);
  notes.push(`UI-016 visual 1280: ${JSON.stringify({ o: v1.overlaps, z: v1.zeroSized, off: v1.offViewport, h: v1.hScroll, e: v1.consoleErrors })}`);
  notes.push(`UI-016 visual 390: ${JSON.stringify({ o: v2.overlaps, z: v2.zeroSized, off: v2.offViewport, h: v2.hScroll, e: v2.consoleErrors })}`);
  await admin.setViewportSize({ width: 1280, height: 900 });

  // Deferred to last, and collected rather than thrown one at a time, so the draft above still
  // exists for the CRUD tests that follow and every defect on this screen is reported in one go.
  notes.push('UI-016 defects: ' + (editorDefects.length ? editorDefects.join(' || ') : 'none'));
  expect(editorDefects).toEqual([]);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-013 — slug generation, uniqueness, slug-based routing.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-013 slugs are generated from the title, de-duplicated on collision, and route publicly', async ({ browser }) => {
  test.setTimeout(300000);

  // Collision: a second post with the SAME title must not steal the first slug.
  await ensureVerifyDraft(); // the first holder of `verify-0808-kitchen-draft`
  // Blazor treats navigateTo('/ManagePost') from /ManagePost as a no-op, so the editor would keep
  // the row it just saved and the "new" post would silently UPDATE it. Go somewhere else first.
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(admin, '/ManagePost', '[data-testid="post-title-input"]', 120000);
  await fillCommitted(admin, 'post-title-input', POST_TITLE);
  await setMarkdown(admin, 'Collision body.');
  await admin.locator('[data-testid="post-excerpt-input"]').click();
  // A category has to be chosen: the editor defaults the dropdown to "0", and saving with 0
  // hits `blogpost_categoryid_fkey` instead of storing NULL (proved by its own test below).
  await pickSelect(admin, 'category-select', 'Programming');
  await admin.click('[data-testid="save-draft"]');
  await expect(admin.locator('[data-testid="post-status-message"]')).toContainText(/Draft (created|saved) successfully/i, { timeout: 45000 });

  const slugs = sql(`SELECT postid, slug FROM blogpost WHERE title = '${POST_TITLE}' ORDER BY postid`);
  notes.push(`FN-013 collision slugs: ${slugs.join(' ; ')}`);
  expect(slugs.length).toBe(2);
  const slugValues = slugs.map((s) => s.split('|')[1]);
  expect(new Set(slugValues).size).toBe(2);
  expect(slugValues[0]).toBe('verify-0808-kitchen-draft');
  expect(slugValues[1]).toMatch(/^verify-0808-kitchen-draft-\d+$/);
  // Drop the collision post again straight away; the rest of the run only needs the first.
  sql(`DELETE FROM posttag WHERE postid = ${slugs[1].split('|')[0]}`);
  sql(`DELETE FROM blogpost WHERE postid = ${slugs[1].split('|')[0]}`);

  // Public slug routing on a seeded post.
  const anon = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const p = await anon.newPage();
  await p.goto(`${BASE}/post/the-markdown-kitchen-sink`, { waitUntil: 'domcontentloaded' });
  await expect(p.locator('[data-testid="post-title"]')).toContainText('The Markdown Kitchen Sink', { timeout: 60000 });
  notes.push('FN-013 /post/the-markdown-kitchen-sink resolved by slug');
  await anon.close();
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-014 — Markdig rendering.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-014 Markdown bodies are rendered to HTML by Markdig on the public post page', async ({ browser }) => {
  test.setTimeout(240000);
  const anon = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const p = await anon.newPage();
  await p.goto(`${BASE}/post/the-markdown-kitchen-sink`, { waitUntil: 'domcontentloaded' });
  await expect(p.locator('[data-testid="post-content"]')).toBeVisible({ timeout: 60000 });
  const html = await p.locator('[data-testid="post-content"]').innerHTML();
  const raw = sqlOne("SELECT length(postcontent) FROM blogpost WHERE slug = 'the-markdown-kitchen-sink'");

  const found = {
    h2: /<h2/.test(html),
    h3: /<h3/.test(html),
    list: /<ul[\s>]/.test(html) && /<li[\s>]/.test(html),
    ordered: /<ol[\s>]/.test(html),
    code: /<pre/.test(html) && /<code/.test(html),
    quote: /<blockquote/.test(html),
    table: /<table/.test(html),
    link: /<a\s[^>]*href=/.test(html),
    emphasis: /<strong>/.test(html) && /<em>/.test(html),
    noRawMarkdown: !/(^|\n)##\s/.test(await p.locator('[data-testid="post-content"]').innerText()),
  };
  notes.push(`FN-014 rendered (${raw} md chars -> ${html.length} html chars): ${JSON.stringify(found)}`);
  expect(found.h2).toBeTruthy();
  expect(found.list).toBeTruthy();
  expect(found.code).toBeTruthy();
  expect(found.quote).toBeTruthy();
  expect(found.emphasis).toBeTruthy();
  expect(found.link).toBeTruthy();
  expect(found.noRawMarkdown).toBeTruthy();

  const v = await visualCheck(p, `${SHOTS}/fn014-markdown-post-1280.png`, 1280);
  notes.push(`FN-014 visual: ${JSON.stringify({ o: v.overlaps.length, h: v.hScroll, e: v.consoleErrors })}`);
  await anon.close();
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-018 — draft preview page.
// ---------------------------------------------------------------------------------------------
test('REQ-UI-018 draft preview renders an unpublished post in full', async () => {
  test.setTimeout(240000);
  const draftId = sqlOne("SELECT postid FROM blogpost WHERE slug = 'observability-for-blazor-server'");
  await goTo(admin, `/admin/preview/${draftId}`, '[data-testid="preview-article"]', 120000);

  const controls = [
    await renderCheck(admin, 'Preview banner', '[data-testid="preview-banner"]'),
    await renderCheck(admin, 'Title', '[data-testid="preview-title"]'),
    await renderCheck(admin, 'Author', '[data-testid="preview-author"]'),
    await renderCheck(admin, 'Created', '[data-testid="preview-created"]'),
    await renderCheck(admin, 'Reading time', '[data-testid="preview-reading-time"]'),
    await renderCheck(admin, 'Rendered content', '[data-testid="preview-content"]'),
    await renderCheck(admin, 'Metadata (id/slug/status)', '[data-testid="preview-metadata"]'),
    await renderCheck(admin, 'Publish from preview', '[data-testid="preview-publish-post"]', 'present'),
    await renderCheck(admin, 'Edit from preview', '[data-testid="preview-edit-post"]', 'present'),
  ];
  notes.push('UI-018 controls: ' + controls.map((c) => `${c.control}=${c.verdict}(${c.detail.slice(0, 45)})`).join('; '));
  expect(controls.filter((c) => c.verdict !== 'RENDERS').map((c) => c.control)).toEqual([]);

  // It must be the DRAFT's own content, fully rendered.
  const dbTitle = sqlOne(`SELECT title FROM blogpost WHERE postid = ${draftId}`);
  await expect(admin.locator('[data-testid="preview-title"]')).toHaveText(dbTitle);
  await expect(admin.locator('[data-testid="preview-banner"]')).toContainText(/not published/i);
  const contentHtml = await admin.locator('[data-testid="preview-content"]').innerHTML();
  expect(contentHtml.length).toBeGreaterThan(500);
  expect(/<h2|<h3|<p/.test(contentHtml)).toBeTruthy();
  await expect(admin.locator('[data-testid="preview-reading-time"]')).toContainText(/\d+/);
  notes.push(`UI-018 draft ${draftId} "${dbTitle}" content html=${contentHtml.length} chars`);

  const v1 = await visualCheck(admin, `${SHOTS}/ui018-preview-1280.png`, 1280);
  const v2 = await visualCheck(admin, `${SHOTS}/ui018-preview-390.png`, 390);
  notes.push(`UI-018 visual 1280: ${JSON.stringify({ o: v1.overlaps, z: v1.zeroSized, off: v1.offViewport, h: v1.hScroll, e: v1.consoleErrors })}`);
  notes.push(`UI-018 visual 390: ${JSON.stringify({ o: v2.overlaps, z: v2.zeroSized, off: v2.offViewport, h: v2.hScroll, e: v2.consoleErrors })}`);
  await admin.setViewportSize({ width: 1280, height: 900 });
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-015 — draft/published state handling.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-015 drafts are unreachable publicly and publish/unpublish flips the state', async ({ browser }) => {
  test.setTimeout(300000);

  // (a) No draft may be served on a public route — anonymous, and while logged out.
  const anon = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const p = await anon.newPage();
  for (const slug of ['testing-dapper-repositories-without-a-database', 'observability-for-blazor-server', 'verify-0808-kitchen-draft']) {
    await p.goto(`${BASE}/post/${slug}`, { waitUntil: 'domcontentloaded' });
    await expect(p.locator('[data-testid="post-not-found"]'), `draft ${slug} must 404`).toBeVisible({ timeout: 60000 });
    await expect(p.locator('[data-testid="post-title"]')).toHaveCount(0);
  }
  notes.push('FN-015 all three drafts return post-not-found anonymously');

  // The public home listing must not contain them either.
  await p.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  await p.waitForTimeout(3000);
  const homeText = await p.evaluate(() => document.body.innerText);
  expect(homeText).not.toContain('Testing Dapper Repositories');
  expect(homeText).not.toContain(MARK);
  await anon.close();

  // (b) Publish then unpublish OUR OWN post and watch the row follow. The verdict is taken from
  // the database, not from the status banner: publishing redirects to /BlogsList after 500ms, so
  // the banner text on screen belongs to whichever navigation won the race.
  const postId = await ensureVerifyDraft();
  await goTo(admin, `/ManagePost/${postId}`, '[data-testid="post-title-input"]', 120000);
  await expect(admin.locator('[data-testid="post-status-badge"]')).toHaveText('Draft');
  await admin.click('[data-testid="publish-post"]');
  const afterPublish = await pollSql(
    `SELECT published::text || '/' || (publishedon IS NOT NULL)::text FROM blogpost WHERE postid = ${postId}`,
    (v) => v === 'true/true',
  );
  notes.push(`FN-015 after publish: published/publishedOnSet = ${afterPublish}`);
  expect(afterPublish).toBe('true/true');

  await admin.waitForTimeout(3000); // let the post-publish redirect settle before navigating back
  await goTo(admin, `/ManagePost/${postId}`, '[data-testid="post-title-input"]', 120000);
  await expect(admin.locator('[data-testid="post-status-badge"]')).toHaveText('Published');
  await admin.click('[data-testid="unpublish-post"]');
  const afterUnpublish = await pollSql(`SELECT published FROM blogpost WHERE postid = ${postId}`, (v) => v === 'f');
  notes.push(`FN-015 after unpublish: published = ${afterUnpublish}`);
  expect(afterUnpublish).toBe('f');

  // (c) "excluded from EVERY public query" — the series page is a public query too.
  // `BlogPostRepo`'s posts-in-series projection filters only on IsDeleted, never on Published,
  // so an unpublished part is listed by title to anonymous visitors.
  sql(`UPDATE blogpost SET seriesid = 1, seriespartnumber = 9 WHERE postid = ${postId}`);
  const anon2 = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const p2 = await anon2.newPage();
  await p2.goto(`${BASE}/series/blazor-server-in-production`, { waitUntil: 'domcontentloaded' });
  await expect(p2.locator('[data-testid="series-posts"]')).toBeVisible({ timeout: 60000 });
  const listed = await p2.$$eval('[data-testid="series-post-title"]', (n) => n.map((x) => (x.textContent || '').trim()));
  const unpublishedInSeries = sql(
    "SELECT title FROM blogpost WHERE seriesid = 1 AND published = FALSE AND (isdeleted = FALSE OR isdeleted IS NULL)",
  );
  const leaked = unpublishedInSeries.filter((t) => listed.includes(t));
  notes.push(`FN-015 public /series page listed ${listed.length} parts; unpublished parts in db=${unpublishedInSeries.length}; LEAKED=${leaked.join(' | ') || 'none'}`);
  const shot = `${SHOTS}/fn015-series-draft-leak-1280.png`;
  await p2.screenshot({ path: shot, fullPage: false });
  await anon2.close();
  expect(leaked, 'unpublished posts must not be listed on the public series page').toEqual([]);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-016 — scheduling + the hosted background publisher.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-016 a post can be scheduled and the background publisher promotes it when due', async () => {
  test.setTimeout(400000);
  const postId = await ensureVerifyDraft();
  // ARRANGE (not the thing under test): the schedule card only renders for an unpublished post,
  // so put our own row back into the draft state a preceding test may have left it out of.
  sql(`UPDATE blogpost SET published = FALSE, publishedon = NULL, scheduledpublishon = NULL WHERE postid = ${postId}`);
  await goTo(admin, `/ManagePost/${postId}`, '[data-testid="post-title-input"]', 120000);

  // Drive the real schedule UI: DatePicker popover -> a day later this month, then Schedule.
  // NOTE: on TrBlazeUI 2.0.2 `data-testid="publish-date-picker"` never reached the DOM (TR-072);
  // 2.0.3 renders it on the trigger button. The label-based lookup below is kept because it works on
  // both, and `datePickerHookPresent` records which behaviour the run saw.
  await expect(admin.locator('[data-testid="schedule-section"]')).toBeVisible();
  const datePickerHookPresent = (await admin.locator('[data-testid="publish-date-picker"]').count()) > 0;
  notes.push(`FN-016 data-testid="publish-date-picker" present in DOM: ${datePickerHookPresent}`);
  const dateTrigger = admin
    .locator('[data-slot="field"]')
    .filter({ has: admin.locator('label', { hasText: 'Publish Date' }) })
    .locator('button')
    .first();
  await expect(dateTrigger).toBeVisible({ timeout: 20000 });
  await dateTrigger.click();
  await admin.waitForTimeout(1500);
  // Day buttons carry an id of the form `calendar-<guid>-day-YYYYMMDD`. Trailing days from the
  // neighbouring month are rendered but DISABLED, so target a specific future date in the shown
  // month rather than "the last cell" — an earlier run spent its whole budget retrying a disabled
  // 5th-of-next-month button.
  const target = new Date(Date.now() + 10 * 86400000);
  const stamp = `${target.getFullYear()}${String(target.getMonth() + 1).padStart(2, '0')}${String(target.getDate()).padStart(2, '0')}`;
  let dayCell = admin.locator(`button[id$="-day-${stamp}"]:not([disabled])`).first();
  if ((await dayCell.count()) === 0) {
    dayCell = admin.locator('button[id*="-day-"]:not([disabled])').last();
  }
  const cellCount = await admin.locator('button[id*="-day-"]:not([disabled])').count();
  notes.push(`FN-016 enabled calendar day cells: ${cellCount}; targeting ${stamp}`);
  let uiScheduled = false;
  if (cellCount > 0) {
    await dayCell.click();
    await admin.waitForTimeout(1500);
    await admin.click('[data-testid="schedule-post"]');
    await admin.waitForTimeout(4000);
    const msg = await admin.locator('[data-testid="post-status-message"]').textContent().catch(() => '');
    notes.push(`FN-016 schedule status message: ${(msg || '').trim().slice(0, 120)}`);
    const sched = sqlOne(`SELECT coalesce(scheduledpublishon::text,'NULL') || '/' || published FROM blogpost WHERE postid = ${postId}`);
    notes.push(`FN-016 row after UI schedule: ${sched}`);
    uiScheduled = !sched.startsWith('NULL');
  }
  expect(uiScheduled, 'schedule set through the editor UI').toBeTruthy();

  // The Scheduled tab must now count it.
  const dbSched = Number(sqlOne('SELECT count(*) FROM blogpost WHERE published = FALSE AND scheduledpublishon IS NOT NULL AND isdeleted IS NOT TRUE'));
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  expect((await texts(admin, 'posts-tab-scheduled'))[0]).toBe(`Scheduled (${dbSched})`);
  notes.push(`FN-016 Scheduled tab now reads ${dbSched}`);

  // The publisher itself: make our own row due and wait for the hosted service's minute tick.
  sql(`UPDATE blogpost SET scheduledpublishon = now() - interval '2 minutes' WHERE postid = ${postId}`);
  let promoted = 'never';
  const deadline = Date.now() + 150000;
  while (Date.now() < deadline) {
    await admin.waitForTimeout(5000);
    const state = sqlOne(`SELECT published || '/' || coalesce(scheduledpublishon::text,'NULL') FROM blogpost WHERE postid = ${postId}`);
    if (state.startsWith('t')) { promoted = state; break; }
  }
  notes.push(`FN-016 ScheduledPostPublisher promoted the due post: ${promoted}`);
  expect(promoted).not.toBe('never');

  // Put it back to a draft so nothing public is disturbed.
  sql(`UPDATE blogpost SET published = FALSE, publishedon = NULL, scheduledpublishon = NULL WHERE postid = ${postId}`);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-017 — category CRUD + single-category assignment.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-017 categories support full CRUD and a post carries exactly one category', async () => {
  test.setTimeout(300000);
  const seedCats = Number(sqlOne('SELECT count(*) FROM category'));
  await goTo(admin, '/CategoriesList', '[data-testid="categories-grid"]', 120000);
  const names = await texts(admin, 'category-row-name');
  const counts = await texts(admin, 'category-row-postcount');
  const slugsShown = await texts(admin, 'category-row-slug');
  expect(names.length).toBe(seedCats);
  expect(slugsShown.filter((s) => !s.trim())).toHaveLength(0);
  notes.push(`FN-017 list: ${names.map((n, i) => `${n}=${counts[i]}`).join(', ')} (db categories=${seedCats})`);

  // Per-category post counts must match psql. `CategoryRepo.SelectAllWithCountsSql` counts only
  // PUBLISHED, non-deleted posts, so the cross-check uses exactly that predicate — and the pairs
  // are read from the same <tr> so a count can never be matched to another row's name.
  const catPairs = await rowPairs(admin, 'category-row-name', 'category-row-postcount');
  notes.push(`FN-017 name/count pairs: ${catPairs.map(([n, c]) => `${n}=${c}`).join(', ')}`);
  expect(catPairs.length).toBe(seedCats);
  for (const [name, shown] of catPairs) {
    const dbCount = sqlOne(
      `SELECT count(*) FROM blogpost p JOIN category c ON p.categoryid = c.categoryid WHERE c.categoryname = '${name.replace(/'/g, "''")}' AND p.published = TRUE AND (p.isdeleted = FALSE OR p.isdeleted IS NULL)`,
    );
    expect(shown.replace(/\D/g, ''), `published-post count for category ${name}`).toBe(dbCount);
  }

  // Create
  await goTo(admin, '/admin/category', '[data-testid="category-name-input"]', 120000);
  await fillCommitted(admin, 'category-name-input', `${MARK}Category`);
  await fillCommitted(admin, 'category-description-input', 'temporary verification row');
  await admin.click('[data-testid="save-category"]');
  await admin.waitForTimeout(4000);
  const created = sql(`SELECT categoryid, slug FROM category WHERE categoryname = '${MARK}Category'`);
  notes.push(`FN-017 created: ${created.join(';')}`);
  expect(created.length).toBe(1);

  // Update
  const catId = created[0].split('|')[0];
  await goTo(admin, `/admin/category/${catId}`, '[data-testid="category-name-input"]', 120000);
  await expect(admin.locator('[data-testid="category-name-input"]')).toHaveValue(`${MARK}Category`);
  await fillCommitted(admin, 'category-description-input', 'edited by verifier');
  await admin.click('[data-testid="save-category"]');
  await admin.waitForTimeout(4000);
  expect(sqlOne(`SELECT description FROM category WHERE categoryid = ${catId}`)).toBe('edited by verifier');

  // Delete through the real dialog
  await goTo(admin, '/CategoriesList', '[data-testid="categories-grid"]', 120000);
  const myRow = admin.locator('[data-testid="category-row-name"]').filter({ hasText: `${MARK}Category` }).first();
  await expect(myRow).toBeVisible();
  const rowEl = myRow.locator('xpath=ancestor::tr[1]');
  await rowEl.locator('[data-testid="category-delete"]').click();
  await expect(admin.locator('[data-testid="category-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await expect(admin.locator('[data-testid="category-delete-name"]')).toContainText(`${MARK}Category`);
  await admin.click('[data-testid="category-delete-confirm"]');
  await admin.waitForTimeout(4000);
  expect(Number(sqlOne(`SELECT count(*) FROM category WHERE categoryname = '${MARK}Category'`))).toBe(0);
  expect(Number(sqlOne('SELECT count(*) FROM category'))).toBe(seedCats);
  notes.push('FN-017 create/update/delete round trip completed; category count back to ' + seedCats);

  // Single-category assignment — the row holds one scalar FK, not a list.
  const postId = await ensureVerifyDraft();
  const assigned = sql(`SELECT categoryid FROM blogpost WHERE postid = ${postId}`);
  expect(assigned.length).toBe(1);
  expect(Number(assigned[0])).toBeGreaterThan(0);
  notes.push(`FN-017 single-category assignment: post ${postId} categoryid=${assigned[0]}`);

  const v = await visualCheck(admin, `${SHOTS}/fn017-categories-1280.png`, 1280);
  notes.push(`FN-017 visual: ${JSON.stringify({ o: v.overlaps.length, h: v.hScroll, e: v.consoleErrors })}`);
  await admin.setViewportSize({ width: 1280, height: 900 });
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-018 — tags: CRUD, junction, autocomplete/quick-add, accurate counts.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-018 tag CRUD, post-tag junction and per-tag counts all agree with PostgreSQL', async () => {
  test.setTimeout(300000);
  await goTo(admin, '/admin/tags', '[data-testid="tags-grid"]', 120000);
  const names = await texts(admin, 'tag-row-name');
  const dbTags = Number(sqlOne('SELECT count(*) FROM tag'));
  notes.push(`FN-018 tags rendered=${names.length} db=${dbTags}`);
  expect(names.length).toBe(dbTags);
  expect((await texts(admin, 'tag-row-slug')).filter((s) => !s.trim())).toHaveLength(0);

  // Counts must be the real per-tag counts (Story 7.5 fixed a broken COUNT).
  // `BlogTagRepo.SelectAllWithCountsSql` counts PUBLISHED, non-deleted posts, so that is the
  // predicate the badge is held to. Pairs come from the same <tr> — an earlier run compared two
  // independently read columns by index and could not tell misalignment from a wrong number.
  const tagPairs = await rowPairs(admin, 'tag-row-name', 'tag-row-postcount');
  notes.push(`FN-018 name/count pairs: ${tagPairs.map(([n, c]) => `${n}=${c}`).join(', ')}`);
  expect(tagPairs.length).toBe(dbTags);
  const tagMismatches: string[] = [];
  let checked = 0;
  for (const [name, shown] of tagPairs) {
    const safe = name.replace(/'/g, "''");
    const published = sqlOne(
      `SELECT count(*) FROM posttag pt JOIN tag t ON pt.tagid = t.tagid JOIN blogpost p ON p.postid = pt.postid WHERE t.tagname = '${safe}' AND p.published = TRUE AND (p.isdeleted = FALSE OR p.isdeleted IS NULL)`,
    );
    const junction = sqlOne(
      `SELECT count(*) FROM posttag pt JOIN tag t ON pt.tagid = t.tagid WHERE t.tagname = '${safe}'`,
    );
    if (shown.replace(/\D/g, '') !== published) {
      tagMismatches.push(`${name}: badge=${shown}, published=${published}, rawJunctionRows=${junction}`);
    }
    if (Number(published) > 0) checked++;
  }
  notes.push(`FN-018 per-tag counts checked for ${tagPairs.length} tags (${checked} non-zero); mismatches: ${tagMismatches.length ? tagMismatches.join(' | ') : 'none'}`);
  expect(checked).toBeGreaterThan(0);
  expect(tagMismatches, 'every tag badge must equal its published-post count').toEqual([]);

  // Inline creation from the editor: a brand-new tag name typed into the post editor must become
  // a Tag row AND a PostTag junction row when the post is saved. Done here rather than relying on
  // REQ-UI-016 so this REQ gets its own verdict even if the editor test fails.
  const postId = await ensureVerifyDraft();
  await goTo(admin, `/ManagePost/${postId}`, '[data-testid="post-title-input"]', 120000);
  await fillCommitted(admin, 'tag-input', `${MARK}tag`);
  await admin.click('[data-testid="add-tag"]');
  await admin.waitForTimeout(1500);
  await admin.click('[data-testid="save-draft"]');
  await expect(admin.locator('[data-testid="post-status-message"]')).toContainText(/Draft (created|saved) successfully/i, { timeout: 45000 });

  const inline = sql(`SELECT t.tagid, t.tagname, t.slug FROM tag t WHERE t.tagname = '${MARK}tag'`);
  notes.push(`FN-018 inline-created tag: ${inline.join(';')}`);
  expect(inline.length, 'typing a new tag name in the editor creates the Tag row').toBe(1);
  expect(inline[0].split('|')[2], 'the new tag gets a slug').toBeTruthy();
  const junctionRows = await pollSql(
    `SELECT count(*) FROM posttag WHERE postid = ${postId} AND tagid = ${inline[0].split('|')[0]}`,
    (v) => v === '1',
  );
  notes.push(`FN-018 junction rows for the inline tag on post ${postId}: ${junctionRows}`);
  expect(junctionRows, 'the post-tag junction row is written').toBe('1');

  // Tag CRUD through the admin screens.
  const tagId = inline[0].split('|')[0];
  await goTo(admin, `/ManageTag/${tagId}`, '[data-testid="tag-name-input"]', 120000);
  await expect(admin.locator('[data-testid="tag-name-input"]')).toHaveValue(`${MARK}tag`);
  await fillCommitted(admin, 'tag-name-input', `${MARK}tag-edited`);
  await admin.click('[data-testid="save-tag"]');
  await admin.waitForTimeout(4000);
  expect(sqlOne(`SELECT tagname FROM tag WHERE tagid = ${tagId}`)).toBe(`${MARK}tag-edited`);
  notes.push('FN-018 tag update persisted');

  await goTo(admin, '/admin/tags', '[data-testid="tags-grid"]', 120000);
  const myTag = admin.locator('[data-testid="tag-row-name"]').filter({ hasText: `${MARK}tag-edited` }).first();
  await expect(myTag).toBeVisible();
  await myTag.locator('xpath=ancestor::tr[1]').locator('[data-testid="tag-delete"]').click();
  await expect(admin.locator('[data-testid="tag-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await admin.click('[data-testid="tag-delete-confirm"]');
  await admin.waitForTimeout(4000);
  expect(Number(sqlOne(`SELECT count(*) FROM tag WHERE tagid = ${tagId}`))).toBe(0);
  expect(Number(sqlOne('SELECT count(*) FROM tag'))).toBe(dbTags - 1);
  notes.push(`FN-018 tag delete removed it; tag count ${dbTags} -> ${dbTags - 1} (seed restored)`);

  const v = await visualCheck(admin, `${SHOTS}/fn018-tags-1280.png`, 1280);
  notes.push(`FN-018 visual: ${JSON.stringify({ o: v.overlaps.length, h: v.hScroll, e: v.consoleErrors })}`);
  await admin.setViewportSize({ width: 1280, height: 900 });
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-024 — series list + manage series.
// ---------------------------------------------------------------------------------------------
test('REQ-UI-024 series list shows real part counts and the manage form loads its parts', async () => {
  test.setTimeout(300000);
  await goTo(admin, '/admin/series', '[data-testid="series-grid"]', 120000);
  await expect(admin.locator('h1').filter({ hasText: 'Series Management' })).toBeVisible();

  const names = await texts(admin, 'series-row-name');
  const shownCounts = await texts(admin, 'series-row-postcount');
  const authorsShown = await texts(admin, 'series-row-author');
  const statuses = await texts(admin, 'series-row-status');
  const dbSeries = Number(sqlOne('SELECT count(*) FROM blogseries'));
  expect(names.length).toBe(dbSeries);
  expect(authorsShown.filter((a) => !a.trim() || /unknown/i.test(a))).toHaveLength(0);
  notes.push(`UI-024 rows: ${names.map((n, i) => `${n} [${statuses[i]}] count=${shownCounts[i]} by ${authorsShown[i]}`).join(' | ')}`);

  // The count badge is documented as PUBLISHED, non-deleted parts only (BlogSeriesRepo
  // SelectAllWithCountsSql) — assert against exactly that, and record the all-parts number too.
  for (let i = 0; i < names.length; i++) {
    const publishedParts = sqlOne(
      `SELECT count(*) FROM blogpost p JOIN blogseries s ON p.seriesid = s.seriesid WHERE s.name = '${names[i].replace(/'/g, "''")}' AND p.published = TRUE AND (p.isdeleted = FALSE OR p.isdeleted IS NULL)`,
    );
    const allParts = sqlOne(
      `SELECT count(*) FROM blogpost p JOIN blogseries s ON p.seriesid = s.seriesid WHERE s.name = '${names[i].replace(/'/g, "''")}' AND (p.isdeleted = FALSE OR p.isdeleted IS NULL)`,
    );
    notes.push(`UI-024 "${names[i]}": badge=${shownCounts[i]} publishedParts=${publishedParts} allParts=${allParts}`);
    expect(Number(shownCounts[i].replace(/\D/g, ''))).toBeGreaterThan(0);
    expect(shownCounts[i].replace(/\D/g, '')).toBe(publishedParts);
  }

  // Manage form loads the series AND its parts.
  await goTo(admin, '/admin/series/1', '[data-testid="series-name-input"]', 120000);
  const dbRow = sql('SELECT name, slug, status FROM blogseries WHERE seriesid = 1')[0].split('|');
  await expect(admin.locator('[data-testid="series-name-input"]')).toHaveValue(dbRow[0]);
  await expect(admin.locator('[data-testid="series-slug-input"]')).toHaveValue(dbRow[1]);
  const partRows = await texts(admin, 'series-post-title');
  const dbParts = Number(sqlOne('SELECT count(*) FROM blogpost WHERE seriesid = 1 AND (isdeleted = FALSE OR isdeleted IS NULL)'));
  notes.push(`UI-024 ManageSeries(1) "${dbRow[0]}" parts rendered=${partRows.length} db=${dbParts}: ${partRows.join(' | ')}`);
  expect(partRows.length).toBe(dbParts);
  expect(partRows.filter((t) => !t.trim())).toHaveLength(0);

  const v1 = await visualCheck(admin, `${SHOTS}/ui024-serieslist-1280.png`, 1280);
  await goTo(admin, '/admin/series', '[data-testid="series-grid"]', 120000);
  const v2 = await visualCheck(admin, `${SHOTS}/ui024-serieslist-390.png`, 390);
  notes.push(`UI-024 visual manage 1280: ${JSON.stringify({ o: v1.overlaps, z: v1.zeroSized, off: v1.offViewport, h: v1.hScroll, e: v1.consoleErrors })}`);
  notes.push(`UI-024 visual list 390: ${JSON.stringify({ o: v2.overlaps, z: v2.zeroSized, off: v2.offViewport, h: v2.hScroll, e: v2.consoleErrors })}`);
  await admin.setViewportSize({ width: 1280, height: 900 });
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-019 — series CRUD, part ordering, prev/next navigation.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-019 series CRUD works and public series ordering plus prev/next resolve correctly', async ({ browser }) => {
  test.setTimeout(300000);
  const seedSeries = Number(sqlOne('SELECT count(*) FROM blogseries'));

  // Create + update + delete a series of our own.
  await goTo(admin, '/admin/series/new', '[data-testid="series-name-input"]', 120000);
  await fillCommitted(admin, 'series-name-input', `${MARK}Series`);
  await fillCommitted(admin, 'series-description-input', 'temporary verification series');
  await admin.click('[data-testid="save-series"]');
  await admin.waitForTimeout(4000);
  const mine = sql(`SELECT seriesid, slug FROM blogseries WHERE name = '${MARK}Series'`);
  notes.push(`FN-019 created series: ${mine.join(';')}`);
  expect(mine.length).toBe(1);
  const sid = mine[0].split('|')[0];
  expect(mine[0].split('|')[1]).toBeTruthy(); // slug generated

  await goTo(admin, `/admin/series/${sid}`, '[data-testid="series-name-input"]', 120000);
  await fillCommitted(admin, 'series-description-input', 'edited by verifier');
  await admin.click('[data-testid="save-series"]');
  await admin.waitForTimeout(4000);
  expect(sqlOne(`SELECT description FROM blogseries WHERE seriesid = ${sid}`)).toBe('edited by verifier');

  await goTo(admin, '/admin/series', '[data-testid="series-grid"]', 120000);
  const row = admin.locator('[data-testid="series-row-name"]').filter({ hasText: `${MARK}Series` }).first();
  await expect(row).toBeVisible();
  await row.locator('xpath=ancestor::tr[1]').locator('[data-testid="series-delete"]').click();
  await expect(admin.locator('[data-testid="series-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await admin.click('[data-testid="series-delete-confirm"]');
  await admin.waitForTimeout(4000);
  expect(Number(sqlOne('SELECT count(*) FROM blogseries'))).toBe(seedSeries);
  notes.push(`FN-019 series create/update/delete round trip; series count back to ${seedSeries}`);

  // Public ordering + part count + prev/next.
  const anon = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const p = await anon.newPage();
  await p.goto(`${BASE}/series/blazor-server-in-production`, { waitUntil: 'domcontentloaded' });
  await expect(p.locator('[data-testid="series-posts"]')).toBeVisible({ timeout: 60000 });
  const partNumbers = await p.$$eval('[data-testid="series-post-number"]', (n) => n.map((x) => (x.textContent || '').trim()));
  const partTitles = await p.$$eval('[data-testid="series-post-title"]', (n) => n.map((x) => (x.textContent || '').trim()));
  const partCountBadge = (await p.locator('[data-testid="series-part-count"]').first().textContent())?.trim();
  const dbOrder = sql(
    "SELECT seriespartnumber, title FROM blogpost WHERE seriesid = 1 AND (isdeleted = FALSE OR isdeleted IS NULL) ORDER BY seriespartnumber",
  ).map((r) => r.split('|'));
  const dbPublishedParts = sqlOne('SELECT count(*) FROM blogpost WHERE seriesid = 1 AND published = TRUE AND (isdeleted = FALSE OR isdeleted IS NULL)');
  notes.push(`FN-019 /series/blazor-server-in-production badge="${partCountBadge}" dbPublished=${dbPublishedParts} numbers=${partNumbers.join(',')} titles=${partTitles.join(' | ')}`);
  expect(partCountBadge).toContain(dbPublishedParts);
  expect(partCountBadge).not.toMatch(/^0\b/);
  expect(partTitles.length).toBeGreaterThan(0);
  // ordering: the numbers rendered must be ascending and match the DB order
  const nums = partNumbers.map((s) => Number(s.replace(/\D/g, '')));
  expect(nums).toEqual([...nums].sort((a, b) => a - b));
  expect(partTitles).toEqual(dbOrder.slice(0, partTitles.length).map((r) => r[1]));

  // prev/next on the middle part of the series
  await p.goto(`${BASE}/post/blazor-circuits-and-state`, { waitUntil: 'domcontentloaded' });
  await expect(p.locator('[data-testid="series-navigation"]')).toBeVisible({ timeout: 60000 });
  const prev = (await p.locator('[data-testid="series-previous-post"]').first().textContent().catch(() => ''))?.trim();
  const next = (await p.locator('[data-testid="series-next-post"]').first().textContent().catch(() => ''))?.trim();
  const navName = (await p.locator('[data-testid="series-navigation-name"]').first().textContent())?.trim();
  const navPart = (await p.locator('[data-testid="series-navigation-part"]').first().textContent())?.trim();
  notes.push(`FN-019 prev/next on part 2: name="${navName}" part="${navPart}" prev="${prev}" next="${next}"`);
  expect(navName).toContain('Blazor Server in Production');
  expect(prev).toBeTruthy();
  expect(next).toBeTruthy();
  expect(prev).toContain('Blazor Render Modes');
  expect(next).toContain('Scaling SignalR');

  const v = await visualCheck(p, `${SHOTS}/fn019-series-public-1280.png`, 1280);
  notes.push(`FN-019 visual: ${JSON.stringify({ o: v.overlaps.length, h: v.hScroll, e: v.consoleErrors })}`);
  await anon.close();
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-012 — post CRUD end to end (create was proved above; update + delete here).
// ---------------------------------------------------------------------------------------------
test('REQ-FN-012 post create/read/update/delete round-trips through BlogSvc and the repository', async () => {
  test.setTimeout(300000);
  const postId = await ensureVerifyDraft();
  expect(postId, 'create (from REQ-UI-016) produced a row').toBeTruthy();

  // READ — the editor must load the stored values back.
  await goTo(admin, `/ManagePost/${postId}`, '[data-testid="post-title-input"]', 120000);
  await expect(admin.locator('[data-testid="post-title-input"]')).toHaveValue(POST_TITLE);
  await expect(admin.locator('[data-testid="post-excerpt-input"]')).toHaveValue('Verification excerpt.');
  const loadedMd = await admin.inputValue('[data-testid="markdown-input"]');
  expect(loadedMd).toContain('Verify heading');
  notes.push(`FN-012 read back post ${postId}: title/excerpt/body all present (${loadedMd.length} md chars)`);

  // UPDATE
  const newTitle = `${POST_TITLE} v2`;
  await fillCommitted(admin, 'post-title-input', newTitle);
  await fillCommitted(admin, 'post-excerpt-input', 'Edited excerpt.');
  await admin.click('[data-testid="save-draft"]');
  await expect(admin.locator('[data-testid="post-status-message"]')).toContainText(/Draft (saved|created) successfully/i, { timeout: 45000 });
  const updated = sql(`SELECT title, abstract, (updatedon IS NOT NULL) FROM blogpost WHERE postid = ${postId}`)[0];
  notes.push(`FN-012 update: ${updated}`);
  expect(updated.split('|')[0]).toBe(newTitle);
  expect(updated.split('|')[1]).toBe('Edited excerpt.');

  // DELETE through the list's real confirm dialog.
  await goTo(admin, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  const before = await texts(admin, 'post-row-title');
  const mine = admin.locator('[data-testid="post-row-title"]').filter({ hasText: newTitle }).first();
  await expect(mine).toBeVisible();
  await mine.locator('xpath=ancestor::tr[1]').locator('[data-testid="post-delete"]').click();
  await expect(admin.locator('[data-testid="post-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await expect(admin.locator('[data-testid="post-delete-title"]')).toContainText(newTitle);
  await admin.click('[data-testid="post-delete-confirm"]');
  await admin.waitForTimeout(5000);
  const after = await texts(admin, 'post-row-title');
  const dbDeleted = sqlOne(`SELECT isdeleted FROM blogpost WHERE postid = ${postId}`);
  notes.push(`FN-012 delete: rows ${before.length} -> ${after.length}, isdeleted=${dbDeleted}`);
  expect(dbDeleted).toBe('t');
  expect(after).not.toContain(newTitle);
  expect(after.length).toBe(before.length - 1);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-017 (second half) — `blogpost.categoryid` is NULLable, so "no category" must save.
// Kept as its own test, and last, so this defect cannot mask any other verdict.
// ---------------------------------------------------------------------------------------------
test('REQ-FN-017 saving a post with no category selected stores NULL rather than crashing', async () => {
  test.setTimeout(240000);
  const nullable = sqlOne("SELECT is_nullable FROM information_schema.columns WHERE table_name = 'blogpost' AND column_name = 'categoryid'");
  notes.push(`FN-017 blogpost.categoryid is_nullable = ${nullable}`);
  expect(nullable).toBe('YES');

  const title = `${MARK}No Category`;
  await goTo(admin, '/ManagePost', '[data-testid="post-title-input"]', 120000);
  await fillCommitted(admin, 'post-title-input', title);
  await setMarkdown(admin, 'Body without a category.');
  await admin.locator('[data-testid="post-excerpt-input"]').click();
  // deliberately leave the category dropdown on its "-- Select Category --" default
  await admin.click('[data-testid="save-draft"]');
  await admin.waitForTimeout(6000);
  const message = ((await admin.locator('[data-testid="post-status-message"]').textContent().catch(() => '')) || '').replace(/\s+/g, ' ').trim();
  const saved = sql(`SELECT postid, coalesce(categoryid::text,'NULL') FROM blogpost WHERE title = '${title}'`);
  notes.push(`FN-017 no-category save: message="${message.slice(0, 160)}" rows=${saved.join(';')}`);
  expect(message, 'saving without a category must not surface a raw PostgreSQL error').not.toMatch(/violates foreign key constraint|23503/i);
  expect(saved.length, 'the post is saved').toBe(1);
  expect(saved[0].split('|')[1]).toBe('NULL');
});

// ---------------------------------------------------------------------------------------------
// Cleanup verification — the seed must be exactly as it was found.
// ---------------------------------------------------------------------------------------------
test('cleanup restores the seed row counts', async () => {
  test.setTimeout(120000);
  purgeVerifyRows();
  const state = {
    posts: Number(sqlOne('SELECT count(*) FROM blogpost')),
    published: Number(sqlOne('SELECT count(*) FROM blogpost WHERE published = TRUE')),
    drafts: Number(sqlOne('SELECT count(*) FROM blogpost WHERE published = FALSE')),
    softDeleted: Number(sqlOne('SELECT count(*) FROM blogpost WHERE isdeleted = TRUE')),
    categories: Number(sqlOne('SELECT count(*) FROM category')),
    tags: Number(sqlOne('SELECT count(*) FROM tag')),
    series: Number(sqlOne('SELECT count(*) FROM blogseries')),
    posttags: Number(sqlOne('SELECT count(*) FROM posttag')),
    leftovers: sql(`SELECT title FROM blogpost WHERE title LIKE '${MARK}%'`).length
      + sql(`SELECT tagname FROM tag WHERE tagname LIKE '${MARK}%'`).length
      + sql(`SELECT name FROM blogseries WHERE name LIKE '${MARK}%'`).length
      + sql(`SELECT categoryname FROM category WHERE categoryname LIKE '${MARK}%'`).length,
  };
  notes.push('CLEANUP ' + JSON.stringify(state));
  expect(state.leftovers).toBe(0);
  expect(state).toMatchObject({ posts: 10, published: 8, categories: 5, tags: 15, series: 2, posttags: 32, softDeleted: 0 });
});
