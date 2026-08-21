/**
 * cluster-a-post-editor-reload.spec.ts — REQ-UI-016 (fix pass, 2026-08-11).
 *
 * The defect this file exists for: `/ManagePost/{id}` loaded the post in OnInitializedAsync, which
 * the Blazor router runs ONCE per visit to the editor. Navigating client-side from /ManagePost/A to
 * /ManagePost/B therefore left post A's title, slug, body and every metadata sidebar field on
 * screen under post B's URL — and a save from that state would have written A's content over B.
 *
 * Three things are measured here, and all three have to hold together:
 *   1. RELOAD — every bound field becomes post B's, checked against psql truth, not against the DOM.
 *   2. SAVE SAFETY — a save taken immediately after the switch writes post B and leaves post A byte
 *      for byte as it was. This is the corruption itself, so it is asserted in the database.
 *   3. KEYSTROKE FIDELITY — the fix releases the markdown editor's "user has typed" latch, which is
 *      exactly the TR-057 keystroke-loss fix. Typing 15 characters after a switch must still yield
 *      all 15 in order, or the reload has been bought by re-opening the older defect.
 *
 * Truth comes from tests/.artifacts/cluster-a/db-before.json, written straight out of psql by the
 * agent before the run (one JSON object per line, one per post).
 *
 * Run with TB_BASE=http://<wsl-gateway>:5411 (cluster A's own port).
 */
import { test, expect, Page } from '@playwright/test';
import { login, nav, visualCheck } from './_gates';
import * as fs from 'fs';

const SHOTS = 'tests/.artifacts/cluster-a';
const TRUTH = 'tests/.artifacts/cluster-a/db-before.json';

/** Post A — the post opened first, whose values must not survive the switch. */
const POST_A = 5;

/** Post B — the post navigated to, whose values every field must show afterwards. */
const POST_B = 7;

/** The exact string the 2026-08-09 verifier used for keystroke fidelity. 15 characters. */
const FIDELITY_TEXT = '## Live heading';

const MD_INPUT = '[data-testid="markdown-input"]';

interface PostTruth {
  postid: number;
  title: string;
  abstract: string;
  postcontent: string;
  featuredimage: string;
  slug: string;
  categoryid: number;
  categoryname: string;
  seriesid: number | null;
  seriesname: string | null;
  seriespartnumber: number | null;
  tagnames: string[];
}

/** Reads the psql snapshot the agent captured before the run. */
function truthFor(postId: number): PostTruth {
  const rows = fs
    .readFileSync(TRUTH, 'utf8')
    .split('\n')
    .filter((line) => line.trim().length > 0)
    .map((line) => JSON.parse(line) as PostTruth);
  const row = rows.find((r) => r.postid === postId);
  if (!row) throw new Error(`no psql truth captured for post ${postId}`);
  return row;
}

/** Everything the editor screen currently shows, read straight out of the DOM. */
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
  featuredImage: string;
}

async function readEditor(page: Page): Promise<EditorState> {
  const textOf = async (selector: string) => {
    const el = page.locator(selector).first();
    return (await el.count()) === 0 ? '' : ((await el.textContent()) || '').trim();
  };
  const image = page.locator('[data-testid="selected-image"]').first();
  return {
    title: await page.inputValue('[data-testid="post-title-input"]'),
    slug: await page.inputValue('[data-testid="post-slug-input"]'),
    slugPreview: await textOf('[data-testid="post-slug-preview"]'),
    excerpt: await page.inputValue('[data-testid="post-excerpt-input"]'),
    body: await page.inputValue(MD_INPUT),
    category: await textOf('[data-testid="category-select"]'),
    series: await textOf('[data-testid="series-select"]'),
    partBadge: await textOf('[data-testid="series-part-badge"]'),
    tags: (await page.locator('[data-testid="selected-tag"]').allTextContents()).map((t) => t.trim()),
    featuredImage: (await image.count()) === 0 ? '' : (await image.getAttribute('src')) || '',
  };
}

/**
 * Navigates client-side to a post and waits for the screen to STOP changing.
 *
 * Deliberately not gated on the expected title — that would make the assertion circular. The gate
 * is stability: the editor is read twice, 2.5s apart, and both reads must agree before anything is
 * asserted. That is also the condition the 2026-08-11 verifier used ("survived a forced re-render"),
 * so a late render that fixed the screen would still be visible as a mismatch here.
 */
async function openPostAndSettle(page: Page, postId: number): Promise<EditorState> {
  await nav(page, postId > 0 ? `/ManagePost/${postId}` : '/ManagePost');
  await expect(page.locator(MD_INPUT)).toBeVisible({ timeout: 45000 });
  await page.waitForTimeout(3000);
  const first = await readEditor(page);
  await page.waitForTimeout(2500);
  const second = await readEditor(page);
  expect(second, 'editor state was still changing 2.5s apart — reads are not trustworthy').toEqual(first);
  return second;
}

/** Asserts every bound field matches the post's row in PostgreSQL. */
function expectMatchesTruth(state: EditorState, truth: PostTruth) {
  expect(state.title).toBe(truth.title);
  expect(state.slug).toBe(truth.slug);
  expect(state.slugPreview).toBe(`/blog/${truth.slug}`);
  expect(state.excerpt).toBe(truth.abstract);
  expect(state.body).toBe(truth.postcontent);
  expect(state.featuredImage).toBe(truth.featuredimage);
  expect(state.category).toBe(truth.categoryname);
  expect(state.series).toBe(
    truth.seriesid === null ? '-- Not part of a series --' : `${truth.seriesname} (2 parts)`,
  );
  expect(state.partBadge).toBe(truth.seriespartnumber === null ? '' : `Part ${truth.seriespartnumber}`);
  expect(state.tags.slice().sort()).toEqual(truth.tagnames.slice().sort());
}

test.describe('REQ-UI-016 — post editor reloads on a route-parameter change', () => {
  test('switching posts client-side replaces every field with the new post', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');

    const truthA = truthFor(POST_A);
    const truthB = truthFor(POST_B);

    const stateA = await openPostAndSettle(page, POST_A);
    expectMatchesTruth(stateA, truthA);

    // The defect: this navigation left every one of these fields showing post A.
    const stateB = await openPostAndSettle(page, POST_B);
    expect(page.url()).toContain(`/ManagePost/${POST_B}`);
    expectMatchesTruth(stateB, truthB);

    // Nothing from post A may be readable anywhere on the screen.
    expect(stateB.title).not.toBe(truthA.title);
    expect(stateB.slug).not.toBe(truthA.slug);
    expect(stateB.body).not.toBe(truthA.postcontent);

    // And back again, so the reload is not a one-way accident.
    const stateBackToA = await openPostAndSettle(page, POST_A);
    expectMatchesTruth(stateBackToA, truthA);

    fs.writeFileSync(
      `${SHOTS}/reload-observed.json`,
      JSON.stringify({ stateA, stateB, stateBackToA }, null, 2),
    );
  });

  test('leaving an existing post for /ManagePost gives an empty new-post form', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');

    // The Author DevGuide called this a "known harness trap": the editor keeping the post it had
    // just saved, so a "new post" silently became an update. It was the same product defect.
    await openPostAndSettle(page, POST_A);
    const fresh = await openPostAndSettle(page, 0);

    expect(await page.locator('[data-testid="content-panel-title"]').textContent()).toContain('New Post');
    expect(fresh.title).toBe('');
    expect(fresh.slug).toBe('');
    expect(fresh.excerpt).toBe('');
    expect(fresh.body).toBe('');
    expect(fresh.tags).toEqual([]);
    expect(fresh.category).toBe('-- Select Category --');
    expect(fresh.series).toBe('-- Not part of a series --');
  });

  test('typing after a post switch keeps every keystroke, in order', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');

    await openPostAndSettle(page, POST_A);
    await openPostAndSettle(page, POST_B);

    // The reset releases the editor's keystroke latch; this is the TR-057 defect it must not re-open.
    const editor = page.locator(MD_INPUT);
    const failures: string[] = [];
    for (const delay of [40, 120]) {
      await editor.click();
      await editor.fill('');
      await page.waitForTimeout(600);
      await editor.pressSequentially(FIDELITY_TEXT, { delay });
      const immediate = await editor.inputValue();
      await page.waitForTimeout(2500);
      const settled = await editor.inputValue();
      if (immediate !== FIDELITY_TEXT) failures.push(`${delay}ms immediate=${JSON.stringify(immediate)}`);
      if (settled !== FIDELITY_TEXT) failures.push(`${delay}ms settled=${JSON.stringify(settled)}`);
    }
    expect(failures, `keystrokes lost or reordered after a post switch:\n${failures.join('\n')}`).toEqual([]);
  });

  test('the editor is visually clean at 1280 and 390 after a switch', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await openPostAndSettle(page, POST_A);
    await openPostAndSettle(page, POST_B);

    const results = [];
    for (const width of [1280, 390]) {
      const result = await visualCheck(page, `${SHOTS}/ui016-managepost-${width}.png`, width);
      results.push(result);
      expect(result.zeroSized, `zero-sized controls at ${width}`).toEqual([]);
      expect(result.offViewport, `off-viewport controls at ${width}`).toEqual([]);
      expect(result.overlaps, `overlapping sibling controls at ${width}`).toEqual([]);
      expect(result.hScroll, `horizontal document scroll at ${width}`).toBe(0);
    }
    fs.writeFileSync(`${SHOTS}/visual.json`, JSON.stringify(results, null, 2));
  });

  test('saving after a post switch writes the new post, not the old one', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');

    const truthB = truthFor(POST_B);

    await openPostAndSettle(page, POST_A);
    const stateB = await openPostAndSettle(page, POST_B);
    expect(stateB.title).toBe(truthB.title);

    // One visible, reversible change so "post B's row changed" is provable, not assumed.
    const marker = `${truthB.title} [ui016 smoke]`;
    await page.fill('[data-testid="post-title-input"]', marker);
    await page.waitForTimeout(800);
    expect(await page.inputValue('[data-testid="post-title-input"]')).toBe(marker);

    await page.click('[data-testid="save-post"]');
    await page.waitForURL((u) => u.pathname.toLowerCase().includes('blogslist'), { timeout: 60000 });
    await page.waitForTimeout(1500);
  });
});
