/**
 * verify-all-authoring.spec.ts — verification cluster V1 of the 2026-08-11 `*verify all` run.
 *
 * BLACK BOX. This file measures the authoring surface against PostgreSQL truth read at the moment
 * of measurement (never against the UI's own claims) and applies the two verify-phase gates:
 *   §4a DATA-RENDER  — every control the screen owns renders DATA, not an empty shell.
 *   §4b VISUAL-TRUTH — geometry at 1280x800 and 390x844 plus an inspected full-page screenshot.
 *
 * Read-only: the run never INSERTs, UPDATEs or DELETEs, and never presses a save button, so the
 * shared database three clusters are using cannot be disturbed. Where a REQ's acceptance needs a
 * write, only the read-side preconditions are asserted.
 *
 * Run with: TB_BASE=http://172.18.144.1:5450 npx playwright test tests/verify/verify-all-authoring.spec.ts --reporter=line
 */
import { test, expect, Page, Browser, BrowserContext } from '@playwright/test';
import { login, nav, renderCheck, visualCheck, ControlResult, VisualResult } from './_gates';
import { execSync } from 'child_process';
import * as fs from 'fs';

const OUT = 'tests/.artifacts/verify-authoring';
const SHOTS = `${OUT}/shots`;

/** Posts chosen because they differ in EVERY bound field — the route-reload proof needs that. */
const POST_A = 5;
const POST_B = 7;

/** The 2026-08-09 verifier's exact keystroke-loss repro string. 15 characters. */
const FIDELITY_TEXT = '## Live heading';

fs.mkdirSync(SHOTS, { recursive: true });

/** Ground truth straight out of the shared PostgreSQL container. SELECT only. */
function psql(sql: string): string[][] {
  const raw = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -A -t -F'|' -c ${JSON.stringify(sql.replace(/\s+/g, ' ').trim())}`,
    { encoding: 'utf8' },
  );
  return raw
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l.length > 0)
    .map((l) => l.split('|'));
}

function psqlOne(sql: string): string {
  return psql(sql)[0][0];
}

const evidence: Record<string, unknown> = {};
function record(key: string, value: unknown) {
  evidence[key] = value;
  fs.writeFileSync(`${OUT}/evidence.json`, JSON.stringify(evidence, null, 2));
}

/** §4b at both required widths; leaves the page back at 1280 for the next test. */
async function bothWidths(page: Page, slug: string): Promise<VisualResult[]> {
  const results: VisualResult[] = [];
  for (const width of [1280, 390]) {
    results.push(await visualCheck(page, `${SHOTS}/${slug}-${width}.png`, width));
    await page.screenshot({ path: `${SHOTS}/${slug}-${width}-full.png`, fullPage: true });
  }
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.waitForTimeout(600);
  return results;
}

function visualVerdict(results: VisualResult[]): { ok: boolean; detail: string } {
  const problems: string[] = [];
  for (const r of results) {
    if (r.zeroSized.length) problems.push(`${r.width}: zero-sized ${r.zeroSized.join(',')}`);
    if (r.offViewport.length) problems.push(`${r.width}: off-viewport ${r.offViewport.join(',')}`);
    if (r.overlaps.length) problems.push(`${r.width}: overlaps ${r.overlaps.map((o) => `${o.a}~${o.b}`).join(',')}`);
    if (r.hScroll > 0) problems.push(`${r.width}: document hScroll ${r.hScroll}px`);
  }
  return { ok: problems.length === 0, detail: problems.join(' | ') || 'clean at 1280 and 390' };
}

function failedControls(controls: ControlResult[]): ControlResult[] {
  return controls.filter((c) => c.verdict !== 'RENDERS');
}

// ---------------------------------------------------------------------------------------------
// One authenticated circuit for the whole file: the host is running with Serilog Debug render-tree
// logging, so a fresh login per test would triple the run time for no extra signal.
// ---------------------------------------------------------------------------------------------
let browserRef: Browser;
let context: BrowserContext;
let page: Page;

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser }) => {
  browserRef = browser;
  context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  page = await context.newPage();
  const landing = await login(page, 'admin');
  record('loginLandingUrl', landing);
  expect(landing, 'admin login must land on /admin').toContain('/admin');
});

test.afterAll(async () => {
  await context?.close();
});

test.beforeEach(async () => {
  await page.setViewportSize({ width: 1280, height: 800 });
});

// =============================================================================================
// REQ-UI-016 — post editor
// =============================================================================================

interface EditorState {
  title: string;
  slug: string;
  slugPreview: string;
  excerpt: string;
  body: string;
  category: string;
  series: string;
  partBadge: string;
  tags: string[];
  image: string;
  header: string;
}

async function readEditor(p: Page): Promise<EditorState> {
  const textOf = async (sel: string) => {
    const el = p.locator(sel).first();
    return (await el.count()) ? ((await el.textContent()) || '').replace(/\s+/g, ' ').trim() : '';
  };
  const valOf = async (sel: string) => {
    const el = p.locator(sel).first();
    return (await el.count()) ? await el.inputValue() : '';
  };
  return {
    title: await valOf('[data-testid="post-title-input"]'),
    slug: await valOf('[data-testid="post-slug-input"]'),
    slugPreview: await textOf('[data-testid="post-slug-preview"]'),
    excerpt: await valOf('[data-testid="post-excerpt-input"]'),
    body: await valOf('[data-testid="markdown-input"]'),
    category: await textOf('[data-testid="category-select"]'),
    series: await textOf('[data-testid="series-select"]'),
    partBadge: await textOf('[data-testid="series-part-badge"]'),
    tags: (await p.locator('[data-testid="selected-tag"]').allTextContents()).map((t) => t.replace(/\s+/g, ' ').trim()),
    image: await p
      .locator('[data-testid="selected-image"] img, [data-testid="selected-image"]')
      .first()
      .evaluate((n) => (n as HTMLImageElement).src || n.textContent || '')
      .catch(() => ''),
    header: await textOf('h1'),
  };
}

/**
 * Opens a post client-side and waits for the editor to STOP changing.
 *
 * Deliberately NOT gated on the expected value — that would be circular, and the 2026-08-11 defect
 * produced a LATE repairing render. Two reads 2.5s apart must agree before anything is asserted.
 */
async function openPostAndSettle(p: Page, postId: number | null): Promise<EditorState> {
  await nav(p, postId === null ? '/ManagePost' : `/ManagePost/${postId}`);
  await expect(p.locator('[data-testid="markdown-input"]')).toBeVisible({ timeout: 60000 });
  await p.waitForTimeout(2500);
  const first = await readEditor(p);
  await p.waitForTimeout(2500);
  const second = await readEditor(p);
  expect(second, 'editor was still changing 2.5s apart — a late render is repairing the screen').toEqual(first);
  return second;
}

test('REQ-UI-016 — post editor: route-change reload, metadata sidebar, keystroke fidelity, live preview', async () => {
  test.setTimeout(600000);

  const truth = (id: number) => {
    const [r] = psql(
      `SELECT p.title, p.slug, coalesce(p.abstract,''), coalesce(p.postcontent,''), c.categoryname,
              coalesce(s.name,''), coalesce(p.seriespartnumber::text,''), coalesce(p.featuredimage,'')
       FROM blogpost p LEFT JOIN category c ON c.categoryid=p.categoryid
       LEFT JOIN blogseries s ON s.seriesid=p.seriesid WHERE p.postid=${id}`,
    );
    const tags = psql(
      `SELECT t.tagname FROM posttag pt JOIN tag t ON t.tagid=pt.tagid WHERE pt.postid=${id} ORDER BY t.tagname`,
    ).map((x) => x[0]);
    return { title: r[0], slug: r[1], abstract: r[2], content: r[3], category: r[4], series: r[5], part: r[6], image: r[7], tags };
  };
  const tA = truth(POST_A);
  const tB = truth(POST_B);

  // ---- 1. open post A -------------------------------------------------------------------
  const stateA1 = await openPostAndSettle(page, POST_A);

  // ---- 2. client-side route change A -> B ------------------------------------------------
  const stateB = await openPostAndSettle(page, POST_B);

  // ---- 3. and back B -> A ----------------------------------------------------------------
  const stateA2 = await openPostAndSettle(page, POST_A);
  record('ui016.observed', { stateA1, stateB, stateA2, truthA: tA, truthB: tB });

  const mismatches: string[] = [];
  const check = (label: string, actual: string, expected: string) => {
    if (actual !== expected) mismatches.push(`${label}: got ${JSON.stringify(actual)} want ${JSON.stringify(expected)}`);
  };

  check('A1.title', stateA1.title, tA.title);
  check('A1.slug', stateA1.slug, tA.slug);
  check('A1.excerpt', stateA1.excerpt, tA.abstract);
  check('A1.body', stateA1.body, tA.content);
  check('A1.category', stateA1.category, tA.category);
  if (!stateA1.series.includes(tA.series)) mismatches.push(`A1.series: got ${JSON.stringify(stateA1.series)} want to contain ${tA.series}`);
  check('A1.partBadge', stateA1.partBadge, `Part ${tA.part}`);
  expect(stateA1.tags.slice().sort(), 'post A tag chips vs psql').toEqual(tA.tags.slice().sort());

  check('B.title', stateB.title, tB.title);
  check('B.slug', stateB.slug, tB.slug);
  check('B.excerpt', stateB.excerpt, tB.abstract);
  check('B.body', stateB.body, tB.content);
  check('B.category', stateB.category, tB.category);
  if (!/not part of a series/i.test(stateB.series)) mismatches.push(`B.series: got ${JSON.stringify(stateB.series)} want the no-series placeholder`);
  check('B.partBadge', stateB.partBadge, '');
  expect(stateB.tags.slice().sort(), 'post B tag chips vs psql').toEqual(tB.tags.slice().sort());

  check('A2.title', stateA2.title, tA.title);
  check('A2.slug', stateA2.slug, tA.slug);
  check('A2.body', stateA2.body, tA.content);
  check('A2.category', stateA2.category, tA.category);

  // The featured image must move with the post (both rows carry a different file).
  if (tB.image && !stateB.image.includes(tB.image.split('/').pop() as string)) {
    mismatches.push(`B.image: got ${JSON.stringify(stateB.image)} want to contain ${tB.image}`);
  }

  // ---- 4. new-post route clears the form -------------------------------------------------
  const stateNew = await openPostAndSettle(page, null);
  record('ui016.newPost', stateNew);
  if (stateNew.title || stateNew.slug || stateNew.body || stateNew.excerpt || stateNew.tags.length) {
    mismatches.push(`/ManagePost (new) leaked values: ${JSON.stringify(stateNew)}`);
  }

  expect(mismatches, `route-parameter reload mismatches:\n${mismatches.join('\n')}`).toEqual([]);

  // ---- 5. keystroke fidelity after a document switch -------------------------------------
  await openPostAndSettle(page, POST_B);
  const editor = page.locator('[data-testid="markdown-input"]');
  const keystroke: Record<string, { immediate: string; settled: string }> = {};
  const keyFailures: string[] = [];
  for (const delay of [40, 120]) {
    await editor.click();
    await editor.fill('');
    await page.waitForTimeout(1000);
    await editor.pressSequentially(FIDELITY_TEXT, { delay });
    const immediate = await editor.inputValue();
    await page.waitForTimeout(2500);
    const settled = await editor.inputValue();
    keystroke[`${delay}ms`] = { immediate, settled };
    if (immediate !== FIDELITY_TEXT) keyFailures.push(`${delay}ms immediate=${JSON.stringify(immediate)}`);
    if (settled !== FIDELITY_TEXT) keyFailures.push(`${delay}ms settled=${JSON.stringify(settled)}`);
  }
  record('ui016.keystrokes', keystroke);
  expect(keyFailures, `keystrokes lost or reordered:\n${keyFailures.join('\n')}`).toEqual([]);

  // ---- 6. live preview reflects what was typed -------------------------------------------
  await page.click('[data-testid="markdown-view-split"]').catch(() => {});
  await page.waitForTimeout(1500);
  const previewHtml = await page
    .locator('[data-testid="markdown-preview-content"]')
    .first()
    .innerHTML()
    .catch(() => '');
  record('ui016.previewHtml', previewHtml.slice(0, 400));
  expect(previewHtml, 'live preview must render the typed Markdown as an h2').toMatch(/<h2/i);

  // ---- 7. §4a data-render over every control the editor owns ------------------------------
  await openPostAndSettle(page, POST_A);
  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'title input', '[data-testid="post-title-input"]', 'present'));
  controls.push(await renderCheck(page, 'slug input', '[data-testid="post-slug-input"]', 'present'));
  controls.push(await renderCheck(page, 'slug preview', '[data-testid="post-slug-preview"]'));
  controls.push(await renderCheck(page, 'excerpt input', '[data-testid="post-excerpt-input"]', 'present'));
  controls.push(await renderCheck(page, 'markdown editor', '[data-testid="markdown-editor"]', 'present'));
  controls.push(await renderCheck(page, 'markdown toolbar', '[data-testid="markdown-toolbar"]', 'present'));
  controls.push(await renderCheck(page, 'view-mode tabs', '[data-testid="markdown-view-mode"]', 'present'));
  controls.push(await renderCheck(page, 'publish card', '[data-testid="publish-card"]', 'present'));
  controls.push(await renderCheck(page, 'post status badge', '[data-testid="post-status-badge"]'));
  controls.push(await renderCheck(page, 'organise card', '[data-testid="organise-card"]', 'present'));
  controls.push(await renderCheck(page, 'category select', '[data-testid="category-select"]'));
  controls.push(await renderCheck(page, 'series select', '[data-testid="series-select"]'));
  controls.push(await renderCheck(page, 'selected tags', '[data-testid="selected-tags"]'));
  controls.push(await renderCheck(page, 'tag input', '[data-testid="tag-input"]', 'present'));
  controls.push(await renderCheck(page, 'featured image card', '[data-testid="featured-image-card"]', 'present'));
  controls.push(await renderCheck(page, 'image picker', '[data-testid="image-picker"]', 'present'));
  controls.push(await renderCheck(page, 'action bar', '[data-testid="post-action-bar"]', 'present'));
  const toolbarButtons = await page.locator('[data-testid="markdown-toolbar"] button').count();
  controls.push({
    control: 'markdown toolbar buttons',
    verdict: toolbarButtons >= 10 ? 'RENDERS' : 'RENDER-EMPTY',
    detail: `${toolbarButtons} buttons`,
  });
  record('ui016.controls', controls);
  expect(failedControls(controls), 'controls failing the §4a data-render gate').toEqual([]);

  // The upgrade deleted SelectFirstPaintLabel: the trigger must show ITEM TEXT, not the raw value.
  expect(stateA1.category, 'category Select must resolve its pre-selected value to item text on first paint')
    .not.toMatch(/^\d+$/);
  expect(stateA1.series, 'series Select must resolve its pre-selected value to item text on first paint')
    .not.toMatch(/^\d+$/);

  // ---- 8. §4b visual truth ----------------------------------------------------------------
  const vis = await bothWidths(page, 'ui016-managepost');
  record('ui016.visual', vis);
  const v = visualVerdict(vis);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-024 — series list + manage series
// =============================================================================================

test('REQ-UI-024 — series list statuses, per-tab counts vs psql, manage-series form', async () => {
  test.setTimeout(420000);

  const rows = psql('SELECT seriesid, name, slug, status FROM blogseries ORDER BY seriesid');
  const total = Number(psqlOne('SELECT count(*) FROM blogseries'));
  const inProgress = Number(psqlOne("SELECT count(*) FROM blogseries WHERE status='In Progress'"));
  const completed = Number(psqlOne("SELECT count(*) FROM blogseries WHERE status='Completed'"));
  record('ui024.psql', { rows, total, inProgress, completed });

  await nav(page, '/admin/series');
  await expect(page.locator('[data-testid="series-grid"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);

  // ---- grid renders every seeded series with its STORED status ---------------------------
  const grid = await page.evaluate(() => {
    const names = Array.from(document.querySelectorAll('[data-testid="series-row-name"]')).map((n) => (n.textContent || '').trim());
    const statuses = Array.from(document.querySelectorAll('[data-testid="series-row-status"]')).map((n) => (n.textContent || '').trim());
    const slugs = Array.from(document.querySelectorAll('[data-testid="series-row-slug"]')).map((n) => (n.textContent || '').trim());
    const counts = Array.from(document.querySelectorAll('[data-testid="series-row-postcount"]')).map((n) => (n.textContent || '').trim());
    const authors = Array.from(document.querySelectorAll('[data-testid="series-row-author"]')).map((n) => (n.textContent || '').trim());
    return { names, statuses, slugs, counts, authors };
  });
  record('ui024.grid', grid);

  expect(grid.names.length, 'series rows rendered vs psql row count').toBe(total);
  expect(grid.names.every((n) => n.length > 0), 'every series name cell non-empty').toBeTruthy();
  expect(grid.slugs.every((s) => s.replace('/', '').length > 0), 'every slug cell non-empty').toBeTruthy();
  expect(grid.counts.every((c) => c.length > 0), 'every post-count cell non-empty').toBeTruthy();
  expect(grid.authors.every((a) => a.length > 0 && a !== 'Unknown'), 'every author cell resolved').toBeTruthy();

  const statusMismatch: string[] = [];
  for (const [id, name, , status] of rows) {
    const idx = grid.names.indexOf(name);
    if (idx < 0) {
      statusMismatch.push(`seriesid ${id} "${name}" is not in the grid`);
      continue;
    }
    if (grid.statuses[idx] !== status) {
      statusMismatch.push(`seriesid ${id} "${name}" renders "${grid.statuses[idx]}" but psql stores "${status}"`);
    }
  }
  expect(statusMismatch, statusMismatch.join('\n')).toEqual([]);

  // ---- every tab's count must equal the psql count for that status -----------------------
  const tabText = async (id: string) => ((await page.locator(`[data-testid="${id}"]`).first().textContent()) || '').trim();
  const parseCount = (s: string) => {
    const m = s.match(/\((\d+)\)/);
    return m ? Number(m[1]) : NaN;
  };
  const tabs = {
    all: await tabText('series-tab-all'),
    inprogress: await tabText('series-tab-inprogress'),
    complete: await tabText('series-tab-complete'),
  };
  record('ui024.tabs', tabs);
  expect(parseCount(tabs.all), `All tab "${tabs.all}" vs psql ${total}`).toBe(total);
  expect(parseCount(tabs.inprogress), `In Progress tab "${tabs.inprogress}" vs psql ${inProgress}`).toBe(inProgress);
  expect(parseCount(tabs.complete), `Completed tab "${tabs.complete}" vs psql ${completed}`).toBe(completed);

  // ---- and each tab must actually FILTER to that many non-empty rows ----------------------
  const filtered: Record<string, string[]> = {};
  for (const [tab, expected] of [['series-tab-inprogress', inProgress], ['series-tab-complete', completed], ['series-tab-all', total]] as const) {
    await page.click(`[data-testid="${tab}"]`);
    await page.waitForTimeout(2000);
    const names = await page.locator('[data-testid="series-row-name"]').allTextContents();
    filtered[tab] = names.map((n) => n.trim());
    expect(names.length, `${tab} shows ${names.length} rows, psql says ${expected}`).toBe(expected as number);
    expect(names.every((n) => n.trim().length > 0), `${tab} rows must have non-empty name cells`).toBeTruthy();
  }
  record('ui024.filtered', filtered);

  const visList = await bothWidths(page, 'ui024-serieslist');
  record('ui024.visual.list', visList);

  // ---- manage-series form loads the row ---------------------------------------------------
  await nav(page, '/admin/series/1');
  await expect(page.locator('[data-testid="series-name-input"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2500);
  const form = {
    name: await page.inputValue('[data-testid="series-name-input"]'),
    slug: await page.inputValue('[data-testid="series-slug-input"]'),
    description: await page.inputValue('[data-testid="series-description-input"]'),
    status: ((await page.locator('[data-testid="series-status-select"]').first().textContent()) || '').trim(),
    partTitles: (await page.locator('[data-testid="series-post-title"]').allTextContents()).map((t) => t.trim()),
  };
  record('ui024.manageSeries', form);
  const [s1] = psql('SELECT name, slug, status FROM blogseries WHERE seriesid=1');
  const partsTruth = Number(psqlOne('SELECT count(*) FROM blogpost WHERE seriesid=1'));
  expect(form.name, 'manage-series name vs psql').toBe(s1[0]);
  expect(form.slug, 'manage-series slug vs psql').toBe(s1[1]);
  expect(form.description.length, 'manage-series description must not be blank').toBeGreaterThan(0);
  expect(form.status, 'status Select must show item text, not the raw bound value').toBe(s1[2]);
  expect(form.partTitles.length, 'series parts rendered vs psql').toBe(partsTruth);
  expect(form.partTitles.every((t) => t.length > 0), 'every part title non-empty').toBeTruthy();

  const visForm = await bothWidths(page, 'ui024-manageseries');
  record('ui024.visual.form', visForm);

  const v = visualVerdict([...visList, ...visForm]);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-017 — post list with status filters
// =============================================================================================

test('REQ-UI-017 — post list: tab counts vs psql, populated rows, filters after the tab-strip rewrite', async () => {
  test.setTimeout(420000);
  const [[total, pub, draft, sched]] = psql(
    `SELECT count(*), count(*) FILTER (WHERE published),
            count(*) FILTER (WHERE NOT published AND (scheduledpublishon IS NULL OR scheduledpublishon<=now())),
            count(*) FILTER (WHERE NOT published AND scheduledpublishon>now())
     FROM blogpost`,
  );
  record('ui017.psql', { total, pub, draft, sched });

  await nav(page, '/BlogsList');
  await expect(page.locator('[data-testid="posts-grid"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);

  const parseCount = (s: string) => Number((s.match(/\((\d+)\)/) || [])[1]);
  const tabText = async (id: string) => ((await page.locator(`[data-testid="${id}"]`).first().textContent()) || '').trim();
  const tabs = {
    all: await tabText('posts-tab-all'),
    published: await tabText('posts-tab-published'),
    draft: await tabText('posts-tab-draft'),
    scheduled: await tabText('posts-tab-scheduled'),
  };
  record('ui017.tabs', tabs);
  expect(parseCount(tabs.all)).toBe(Number(total));
  expect(parseCount(tabs.published)).toBe(Number(pub));
  expect(parseCount(tabs.draft)).toBe(Number(draft));
  expect(parseCount(tabs.scheduled)).toBe(Number(sched));

  // Rows carry real data: no blank titles, no "Unknown" authors.
  const rows = await page.evaluate(() => ({
    titles: Array.from(document.querySelectorAll('[data-testid="post-row-title"]')).map((n) => (n.textContent || '').trim()),
    authors: Array.from(document.querySelectorAll('[data-testid="post-row-author"]')).map((n) => (n.textContent || '').trim()),
    statuses: Array.from(document.querySelectorAll('[data-testid="post-row-status"]')).map((n) => (n.textContent || '').trim()),
  }));
  record('ui017.rows', rows);
  expect(rows.titles.length).toBe(Number(total));
  expect(rows.titles.every((t) => t.length > 0)).toBeTruthy();
  expect(rows.authors.filter((a) => a === 'Unknown' || a.length === 0).length, 'unresolved author cells').toBe(0);
  expect(rows.statuses.every((s) => s.length > 0)).toBeTruthy();

  // The tab strip is now a real BUTTON role=tab — clicking must still filter.
  const perTab: Record<string, number> = {};
  for (const [tab, expected] of [['posts-tab-draft', draft], ['posts-tab-scheduled', sched], ['posts-tab-published', pub], ['posts-tab-all', total]] as const) {
    await page.click(`[data-testid="${tab}"]`);
    await page.waitForTimeout(2000);
    const n = await page.locator('[data-testid="post-row-title"]').count();
    perTab[tab] = n;
    expect(n, `${tab} rendered ${n} rows, psql says ${expected}`).toBe(Number(expected));
  }
  record('ui017.perTab', perTab);

  // 390px: the Scheduled tab used to be CLIPPED. It must be reachable inside its own scroller.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1200);
  const scroller = await page.locator('[data-testid="posts-status-tabs-scroller"]').evaluate((n) => ({
    clientWidth: n.clientWidth,
    scrollWidth: n.scrollWidth,
    overflowX: getComputedStyle(n).overflowX,
  }));
  await page.locator('[data-testid="posts-status-tabs-scroller"]').evaluate((n) => n.scrollTo({ left: n.scrollWidth }));
  await page.waitForTimeout(800);
  const schedBox = await page.locator('[data-testid="posts-tab-scheduled"]').first().boundingBox();
  record('ui017.mobileTabs', { scroller, schedBox });
  expect(scroller.overflowX, 'tab strip must be a scroller, not clipped').toMatch(/auto|scroll/);
  expect(schedBox!.x + schedBox!.width, 'Scheduled tab must be fully reachable at 390px').toBeLessThanOrEqual(391);

  const vis = await bothWidths(page, 'ui017-blogslist');
  record('ui017.visual', vis);
  const v = visualVerdict(vis);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-018 — draft preview
// =============================================================================================

test('REQ-UI-018 — draft preview renders an unpublished post in full', async () => {
  test.setTimeout(300000);
  const [[id, title, slug]] = psql('SELECT postid, title, slug FROM blogpost WHERE NOT published ORDER BY postid LIMIT 1');
  record('ui018.psql', { id, title, slug });

  await nav(page, `/admin/preview/${id}`);
  await expect(page.locator('[data-testid="preview-article"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);

  const controls: ControlResult[] = [];
  for (const [name, sel, kind] of [
    ['not-published banner', '[data-testid="preview-banner"]', 'value'],
    ['title', '[data-testid="preview-title"]', 'value'],
    ['author', '[data-testid="preview-author"]', 'value'],
    ['created date', '[data-testid="preview-created"]', 'value'],
    ['reading time', '[data-testid="preview-reading-time"]', 'value'],
    ['abstract', '[data-testid="preview-abstract"]', 'value'],
    ['rendered content', '[data-testid="preview-content"]', 'value'],
    ['metadata block', '[data-testid="preview-metadata"]', 'value'],
    ['slug', '[data-testid="preview-slug"]', 'value'],
    ['status', '[data-testid="preview-status"]', 'value'],
    ['actions', '[data-testid="preview-actions"]', 'present'],
  ] as const) {
    controls.push(await renderCheck(page, name, sel, kind as any));
  }
  const shownTitle = ((await page.locator('[data-testid="preview-title"]').textContent()) || '').trim();
  const contentHtml = await page.locator('[data-testid="preview-content"]').innerHTML();
  record('ui018.controls', { controls, shownTitle, contentChars: contentHtml.length });
  expect(failedControls(controls), 'preview controls failing §4a').toEqual([]);
  expect(shownTitle, 'preview title vs psql').toBe(title);
  expect(contentHtml.length, 'rendered Markdown body must not be empty').toBeGreaterThan(200);

  const vis = await bothWidths(page, 'ui018-preview');
  record('ui018.visual', vis);
  const v = visualVerdict(vis);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-019 — admin dashboard
// =============================================================================================

test('REQ-UI-019 — admin dashboard tiles carry live counts', async () => {
  test.setTimeout(300000);
  await nav(page, '/admin');
  await expect(page.locator('[data-testid="dashboard-stats"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2500);

  // Counts are re-queried AT THE INSTANT OF MEASUREMENT — sibling clusters share this database.
  const truth = {
    posts: Number(psqlOne('SELECT count(*) FROM blogpost')),
    users: Number(psqlOne('SELECT count(*) FROM bloguser')),
    comments: Number(psqlOne('SELECT count(*) FROM blogcomment')),
    subscribers: Number(psqlOne('SELECT count(*) FROM subscriber')),
  };
  const shown = {
    posts: ((await page.locator('[data-testid="stat-posts-value"]').textContent()) || '').trim(),
    users: ((await page.locator('[data-testid="stat-users-value"]').textContent()) || '').trim(),
    comments: ((await page.locator('[data-testid="stat-comments-value"]').textContent()) || '').trim(),
    subscribers: ((await page.locator('[data-testid="stat-subscribers-value"]').textContent()) || '').trim(),
  };
  record('ui019', { truth, shown });
  expect(Number(shown.posts)).toBe(truth.posts);
  expect(Number(shown.users)).toBe(truth.users);
  expect(Number(shown.comments)).toBe(truth.comments);
  expect(Number(shown.subscribers)).toBe(truth.subscribers);

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'quick actions', '[data-testid="quick-actions"]', 'present'));
  controls.push(await renderCheck(page, 'needs attention', '[data-testid="needs-attention"]'));
  controls.push(await renderCheck(page, 'recent activity', '[data-testid="recent-activity"]'));
  record('ui019.controls', controls);
  expect(failedControls(controls)).toEqual([]);

  const vis = await bothWidths(page, 'ui019-dashboard');
  record('ui019.visual', vis);
  const v = visualVerdict(vis);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-020 — users list
// =============================================================================================

test('REQ-UI-020 — users list rows and role badges vs psql', async () => {
  test.setTimeout(300000);
  const total = Number(psqlOne('SELECT count(*) FROM bloguser'));
  await nav(page, '/users');
  await expect(page.locator('[data-testid="users-grid"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);

  const rows = await page.evaluate(() => ({
    emails: Array.from(document.querySelectorAll('[data-testid="user-row-email"]')).map((n) => (n.textContent || '').trim()),
    roles: Array.from(document.querySelectorAll('[data-testid="user-row-role"]')).map((n) => (n.textContent || '').trim()),
    names: Array.from(document.querySelectorAll('[data-testid="user-row-name"]')).map((n) => (n.textContent || '').trim()),
  }));
  const countText = ((await page.locator('[data-testid="users-count"]').textContent()) || '').trim();
  record('ui020', { total, rows, countText });
  expect(rows.emails.length, `user rows vs psql ${total}`).toBe(total);
  expect(rows.emails.every((e) => e.includes('@'))).toBeTruthy();
  expect(rows.roles.every((r) => r.length > 0)).toBeTruthy();
  expect(countText).toContain(String(total));

  const vis = await bothWidths(page, 'ui020-users');
  record('ui020.visual', vis);
  const v = visualVerdict(vis);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-021 — comment moderation queue
// =============================================================================================

test('REQ-UI-021 — comment moderation queue rows and status tabs vs psql', async () => {
  test.setTimeout(300000);
  const total = Number(psqlOne('SELECT count(*) FROM blogcomment'));
  await nav(page, '/comments');
  await expect(page.locator('[data-testid="comments-grid"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);

  const rows = await page.evaluate(() => ({
    authors: Array.from(document.querySelectorAll('[data-testid="comment-row-author"]')).map((n) => (n.textContent || '').trim()),
    posts: Array.from(document.querySelectorAll('[data-testid="comment-row-post"]')).map((n) => (n.textContent || '').trim()),
    statuses: Array.from(document.querySelectorAll('[data-testid="comment-row-status"]')).map((n) => (n.textContent || '').trim()),
    texts: Array.from(document.querySelectorAll('[data-testid="comment-row-text"]')).map((n) => (n.textContent || '').trim()),
  }));
  const allTab = ((await page.locator('[data-testid="comments-tab-all"]').first().textContent()) || '').trim();
  record('ui021', { total, rows: { ...rows, authors: rows.authors.slice(0, 5) }, allTab, rowCount: rows.authors.length });
  expect(rows.authors.length, `comment rows vs psql ${total}`).toBe(total);
  expect(rows.authors.every((a) => a.length > 0)).toBeTruthy();
  expect(rows.posts.every((p) => p.length > 0)).toBeTruthy();
  expect(rows.texts.every((t) => t.length > 0)).toBeTruthy();
  expect(allTab).toContain(String(total));

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'bulk action select', '[data-testid="comments-bulk-action"]', 'present'));
  controls.push(await renderCheck(page, 'select all', '[data-testid="comments-select-all"]', 'present'));
  controls.push(await renderCheck(page, 'status tabs', '[data-testid="comments-status-tabs"]', 'present'));
  record('ui021.controls', controls);
  expect(failedControls(controls)).toEqual([]);

  const vis = await bothWidths(page, 'ui021-comments');
  record('ui021.visual', vis);
  const v = visualVerdict(vis);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-022 — categories list + manage category
// =============================================================================================

test('REQ-UI-022 — categories list rows vs psql and the manage-category form loads', async () => {
  test.setTimeout(360000);
  const cats = psql('SELECT categoryid, categoryname, slug FROM category ORDER BY categoryid');
  await nav(page, '/admin/categories');
  await expect(page.locator('[data-testid="categories-grid"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);

  const rows = await page.evaluate(() => ({
    names: Array.from(document.querySelectorAll('[data-testid="category-row-name"]')).map((n) => (n.textContent || '').trim()),
    slugs: Array.from(document.querySelectorAll('[data-testid="category-row-slug"]')).map((n) => (n.textContent || '').trim()),
    counts: Array.from(document.querySelectorAll('[data-testid="category-row-postcount"]')).map((n) => (n.textContent || '').trim()),
  }));
  record('ui022', { psqlCount: cats.length, rows });
  expect(rows.names.length, `category rows vs psql ${cats.length}`).toBe(cats.length);
  expect(rows.names.slice().sort()).toEqual(cats.map((c) => c[1]).sort());
  expect(rows.slugs.every((s) => s.replace('/', '').length > 0)).toBeTruthy();
  expect(rows.counts.every((c) => c.length > 0)).toBeTruthy();

  const visList = await bothWidths(page, 'ui022-categories');

  await nav(page, '/admin/category/3');
  await expect(page.locator('[data-testid="category-name-input"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);
  const form = {
    name: await page.inputValue('[data-testid="category-name-input"]'),
    slug: await page.inputValue('[data-testid="category-slug-input"]'),
  };
  const [c3] = psql('SELECT categoryname, slug FROM category WHERE categoryid=3');
  record('ui022.form', { form, c3 });
  expect(form.name).toBe(c3[0]);
  expect(form.slug).toBe(c3[1]);

  const visForm = await bothWidths(page, 'ui022-managecategory');
  record('ui022.visual', [...visList, ...visForm]);
  const v = visualVerdict([...visList, ...visForm]);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-023 — tags list + manage tag
// =============================================================================================

test('REQ-UI-023 — tags list rows vs psql and the manage-tag form loads', async () => {
  test.setTimeout(360000);
  const tags = psql('SELECT tagid, tagname, slug FROM tag ORDER BY tagid');
  await nav(page, '/admin/tags');
  await expect(page.locator('[data-testid="tags-grid"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);

  const rows = await page.evaluate(() => ({
    names: Array.from(document.querySelectorAll('[data-testid="tag-row-name"]')).map((n) => (n.textContent || '').trim()),
    slugs: Array.from(document.querySelectorAll('[data-testid="tag-row-slug"]')).map((n) => (n.textContent || '').trim()),
    counts: Array.from(document.querySelectorAll('[data-testid="tag-row-postcount"]')).map((n) => (n.textContent || '').trim()),
  }));
  const countText = ((await page.locator('[data-testid="tags-count"]').textContent()) || '').trim();
  record('ui023', { psqlCount: tags.length, rows, countText });
  // The grid pages at 20; the seeded tag set is smaller, so every row must be on screen.
  expect(rows.names.length, `tag rows vs psql ${tags.length}`).toBe(tags.length);
  expect(rows.names.every((n) => n.length > 0)).toBeTruthy();
  expect(rows.slugs.every((s) => s.replace('/', '').length > 0)).toBeTruthy();
  expect(rows.counts.every((c) => c.length > 0)).toBeTruthy();

  const visList = await bothWidths(page, 'ui023-tags');

  const firstTagId = tags[0][0];
  await nav(page, `/ManageTag/${firstTagId}`);
  await expect(page.locator('[data-testid="tag-name-input"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);
  const form = {
    name: await page.inputValue('[data-testid="tag-name-input"]'),
    slug: await page.inputValue('[data-testid="tag-slug-input"]'),
    preview: ((await page.locator('[data-testid="tag-slug-preview"]').first().textContent()) || '').trim(),
  };
  record('ui023.form', { form, truth: tags[0] });
  expect(form.name).toBe(tags[0][1]);
  expect(form.slug).toBe(tags[0][2]);

  const visForm = await bothWidths(page, 'ui023-managetag');
  record('ui023.visual', [...visList, ...visForm]);
  const v = visualVerdict([...visList, ...visForm]);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});

// =============================================================================================
// REQ-UI-025 — subscribers admin page
// =============================================================================================

test('REQ-UI-025 — subscribers list rows, summary and tabs vs psql', async () => {
  test.setTimeout(300000);
  const total = Number(psqlOne('SELECT count(*) FROM subscriber'));
  await nav(page, '/admin/subscribers');
  await page.waitForTimeout(3000);

  const hasGrid = (await page.locator('[data-testid="subscribers-grid"]').count()) > 0;
  const hasEmpty = (await page.locator('[data-testid="subscribers-empty"]').count()) > 0;
  const rows = await page.evaluate(() => ({
    emails: Array.from(document.querySelectorAll('[data-testid="subscriber-row-email"]')).map((n) => (n.textContent || '').trim()),
    statuses: Array.from(document.querySelectorAll('[data-testid="subscriber-row-status"]')).map((n) => (n.textContent || '').trim()),
    dates: Array.from(document.querySelectorAll('[data-testid="subscriber-row-date"]')).map((n) => (n.textContent || '').trim()),
  }));
  const summary = ((await page.locator('[data-testid="subscribers-summary"]').first().textContent()) || '').trim();
  const allTab = ((await page.locator('[data-testid="subscribers-tab-all"]').first().textContent()) || '').trim();
  record('ui025', { total, hasGrid, hasEmpty, rowCount: rows.emails.length, summary, allTab });

  if (total === 0) {
    // A count badge over zero rows is a FAIL; an honest empty state is not.
    expect(hasEmpty, 'zero subscribers must render the explicit empty state').toBeTruthy();
  } else {
    expect(hasGrid, 'grid must render when psql has rows').toBeTruthy();
    expect(rows.emails.length, `subscriber rows vs psql ${total}`).toBe(total);
    expect(rows.emails.every((e) => e.includes('@'))).toBeTruthy();
    expect(rows.statuses.every((s) => s.length > 0)).toBeTruthy();
    expect(rows.dates.every((d) => d.length > 0)).toBeTruthy();
    expect(summary).toContain(String(total));
  }

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'search', '[data-testid="subscribers-search"]', 'present'));
  controls.push(await renderCheck(page, 'status tabs', '[data-testid="subscribers-status-tabs"]', 'present'));
  controls.push(await renderCheck(page, 'export CSV', '[data-testid="subscribers-export"]', 'present'));
  record('ui025.controls', controls);
  expect(failedControls(controls)).toEqual([]);

  const vis = await bothWidths(page, 'ui025-subscribers');
  record('ui025.visual', vis);
  const v = visualVerdict(vis);
  expect(v.ok, `visual-truth gate: ${v.detail}`).toBeTruthy();
});
