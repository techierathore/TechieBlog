/**
 * cluster-h-post-editor.spec.ts — REQ-UI-016 (TrBlazeUI 2.0.2 adoption pass, 2026-08-11).
 *
 * TR-057 is recorded as resolved in 2.0.2, so this file exists to decide — by measurement, not by
 * reading the release note — whether `PostMarkdownEditor` may go back to the library `<Textarea>`.
 * The raw uncontrolled `<textarea>` it uses today carries TWO fixes that must both survive:
 *   - TR-057 (2026-08-09): a controlled textarea round-trips every keystroke on a Server circuit
 *     and the returning render writes a stale value into the DOM. '## Live heading' arrived as
 *     '## Li' / '#ve' / '## ivehading'.
 *   - The 2026-08-11 cluster-A fix: `/ManagePost/{id}` reloads on a route-parameter change, and the
 *     editor's `ResetKey` releases the keystroke latch for a CHANGE OF DOCUMENT only. Without it
 *     post A's body sat under post B's URL and a save overwrote the wrong post.
 *
 * Three proofs, all against a running host and PostgreSQL truth:
 *   1. KEYSTROKE INTEGRITY — 15 characters one key at a time at 40ms and at 120ms, read immediately
 *      and again 2.5s later (the original defect produced a LATE bad render).
 *   2. ROUTE-CHANGE RELOAD — /ManagePost/5 -> 7 -> 5 client-side; body, title and slug are the new
 *      post's every time. The editor is read twice 2.5s apart and both reads must AGREE before
 *      anything is asserted, so a late repairing render still registers as a mismatch.
 *   3. SAVE SAFETY — the run edits and saves post 7 after switching from post 5. The column-level
 *      row diff is taken outside this file, straight out of psql (harness/db-snapshot.sh +
 *      harness/db-diff.py): post 7 must differ in exactly the columns changed, post 5 in ZERO.
 *
 * Run with TB_BASE=http://<wsl-gateway>:5424 (cluster H's own port).
 */
import { test, expect, Page } from '@playwright/test';
import { login, nav, visualCheck } from './_gates';
import * as fs from 'fs';

const SHOTS = 'tests/.artifacts/cluster-h';
const TRUTH = 'tests/.artifacts/cluster-h/db-before-rows.json';

/** Post opened first — the post whose values must not survive the switch. */
const POST_A = 5;

/** Post navigated to — every field must be its own afterwards. */
const POST_B = 7;

/** The 2026-08-09 verifier's exact repro string. 15 characters. */
const FIDELITY_TEXT = '## Live heading';

/** Title marker written by the save-safety proof; the agent restores the row afterwards. */
const SAVE_MARKER_SUFFIX = ' [cluster-h ui016 smoke]';

const MD_INPUT = '[data-testid="markdown-input"]';

interface PostRow {
  postid: number;
  title: string;
  slug: string;
  abstract: string;
  postcontent: string;
}

function truthFor(postId: number): PostRow {
  const rows = JSON.parse(fs.readFileSync(TRUTH, 'utf8')) as PostRow[];
  const row = rows.find((r) => r.postid === postId);
  if (!row) throw new Error(`no psql truth captured for post ${postId}`);
  return row;
}

interface EditorState {
  title: string;
  slug: string;
  body: string;
}

async function readEditor(page: Page): Promise<EditorState> {
  return {
    title: await page.inputValue('[data-testid="post-title-input"]'),
    slug: await page.inputValue('[data-testid="post-slug-input"]'),
    body: await page.inputValue(MD_INPUT),
  };
}

/**
 * Navigates client-side to a post and waits for the screen to STOP changing.
 *
 * Not gated on the expected value — that would make the assertion circular. The gate is stability:
 * two reads 2.5s apart must agree, so a late render that repaired the screen is still a mismatch.
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

test.describe('REQ-UI-016 — the markdown editor after the 2.0.2 upgrade', () => {
  test('proof 1 — typing 15 characters keeps every keystroke, in order', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await openPostAndSettle(page, POST_A);

    const editor = page.locator(MD_INPUT);
    const observed: Record<string, { immediate: string; settled: string }> = {};
    const failures: string[] = [];
    for (const delay of [40, 120]) {
      await editor.click();
      await editor.fill('');
      await page.waitForTimeout(800);
      await editor.pressSequentially(FIDELITY_TEXT, { delay });
      const immediate = await editor.inputValue();
      await page.waitForTimeout(2500);
      const settled = await editor.inputValue();
      observed[`${delay}ms`] = { immediate, settled };
      if (immediate !== FIDELITY_TEXT) failures.push(`${delay}ms immediate=${JSON.stringify(immediate)}`);
      if (settled !== FIDELITY_TEXT) failures.push(`${delay}ms settled=${JSON.stringify(settled)}`);
    }
    fs.writeFileSync(`${SHOTS}/keystrokes.json`, JSON.stringify(observed, null, 2));
    expect(failures, `keystrokes lost or reordered:\n${failures.join('\n')}`).toEqual([]);
  });

  test('proof 1b — typing after a post switch also keeps every keystroke', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');

    // The switch is what releases the keystroke latch, so this is the riskiest moment for TR-057.
    await openPostAndSettle(page, POST_A);
    await openPostAndSettle(page, POST_B);

    const editor = page.locator(MD_INPUT);
    const observed: Record<string, { immediate: string; settled: string }> = {};
    const failures: string[] = [];
    for (const delay of [40, 120]) {
      await editor.click();
      await editor.fill('');
      await page.waitForTimeout(800);
      await editor.pressSequentially(FIDELITY_TEXT, { delay });
      const immediate = await editor.inputValue();
      await page.waitForTimeout(2500);
      const settled = await editor.inputValue();
      observed[`${delay}ms`] = { immediate, settled };
      if (immediate !== FIDELITY_TEXT) failures.push(`${delay}ms immediate=${JSON.stringify(immediate)}`);
      if (settled !== FIDELITY_TEXT) failures.push(`${delay}ms settled=${JSON.stringify(settled)}`);
    }
    fs.writeFileSync(`${SHOTS}/keystrokes-after-switch.json`, JSON.stringify(observed, null, 2));
    expect(failures, `keystrokes lost or reordered after a post switch:\n${failures.join('\n')}`).toEqual([]);
  });

  /**
   * The teeth of proof 1, and the reason the two tests above are not enough on their own.
   *
   * On a fast local circuit the PRE-FIX controlled `<Textarea>` ALSO passed those cadences, so a
   * green result there proves nothing about the library's fix. This reproduces the failing
   * condition deterministically — 400ms emulated latency + burst typing, which is when a keystroke
   * is still in flight as the previous render comes back. Cluster C measured the 2.0.1 build
   * failing these exact settings 4 of 9 runs ('#ve he', '## ng', '## Living', '## Lie edg').
   *
   * This variant additionally runs the stress AFTER a post switch, because that is the moment the
   * editor's document latch is released — the one path where this component composes the library's
   * `TextValueSync` with cluster A's `ResetKey`, and therefore the one the library's own tests
   * cannot have covered.
   */
  test('proof 1c — burst typing on a 400ms-latency circuit, after a post switch', async ({ page }) => {
    test.setTimeout(900000);
    await page.setViewportSize({ width: 1280, height: 900 });

    const client = await page.context().newCDPSession(page);
    await client.send('Network.enable');
    await client.send('Network.emulateNetworkConditions', {
      offline: false, latency: 400, downloadThroughput: -1, uploadThroughput: -1,
    });

    await login(page, 'admin');
    await openPostAndSettle(page, POST_A);
    await openPostAndSettle(page, POST_B);

    const editor = page.locator(MD_INPUT);
    const observed: string[] = [];
    const failures: string[] = [];
    for (const delay of [0, 15, 40]) {
      for (let run = 1; run <= 3; run++) {
        await editor.click();
        await editor.fill('');
        await page.waitForTimeout(900);
        await editor.pressSequentially(FIDELITY_TEXT, { delay });
        const immediate = await editor.inputValue();
        await page.waitForTimeout(2500);
        const settled = await editor.inputValue();
        observed.push(`${delay}ms/key run ${run}: immediate=${JSON.stringify(immediate)} settled=${JSON.stringify(settled)}`);
        if (immediate !== FIDELITY_TEXT || settled !== FIDELITY_TEXT) {
          failures.push(observed[observed.length - 1]);
        }
      }
    }
    fs.writeFileSync(`${SHOTS}/keystrokes-latency-after-switch.txt`, observed.join('\n'));
    expect(failures, `keystrokes lost or reordered under 400ms latency:\n${failures.join('\n')}`).toEqual([]);
  });

  test('proof 2 — /ManagePost/5 -> 7 -> 5 shows the new post every time', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');

    const a = truthFor(POST_A);
    const b = truthFor(POST_B);

    const stateA = await openPostAndSettle(page, POST_A);
    expect(stateA.title).toBe(a.title);
    expect(stateA.slug).toBe(a.slug);
    expect(stateA.body).toBe(a.postcontent);

    const stateB = await openPostAndSettle(page, POST_B);
    expect(page.url()).toContain(`/ManagePost/${POST_B}`);
    expect(stateB.title).toBe(b.title);
    expect(stateB.slug).toBe(b.slug);
    expect(stateB.body).toBe(b.postcontent);
    expect(stateB.title).not.toBe(a.title);
    expect(stateB.body).not.toBe(a.postcontent);

    const backToA = await openPostAndSettle(page, POST_A);
    expect(backToA.title).toBe(a.title);
    expect(backToA.slug).toBe(a.slug);
    expect(backToA.body).toBe(a.postcontent);

    fs.writeFileSync(`${SHOTS}/reload-observed.json`, JSON.stringify({ stateA, stateB, backToA }, null, 2));
  });

  test('proof 2b — leaving a post for /ManagePost gives an empty new-post form', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await openPostAndSettle(page, POST_A);
    const fresh = await openPostAndSettle(page, 0);
    expect(await page.locator('[data-testid="content-panel-title"]').textContent()).toContain('New Post');
    expect(fresh.title).toBe('');
    expect(fresh.slug).toBe('');
    expect(fresh.body).toBe('');
  });

  test('proof 3 — editing and saving post 7 after a switch writes post 7', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');

    const b = truthFor(POST_B);
    await openPostAndSettle(page, POST_A);
    const stateB = await openPostAndSettle(page, POST_B);
    expect(stateB.title).toBe(b.title);

    // Two visible, reversible changes — one in the sidebar, one in the editor itself — so the row
    // diff proves the EDITOR round-tripped as well as the metadata form.
    const marker = `${b.title}${SAVE_MARKER_SUFFIX}`;
    await page.fill('[data-testid="post-title-input"]', marker);
    const editor = page.locator(MD_INPUT);
    await editor.click();
    await editor.press('Control+End');
    await editor.pressSequentially('\n\nEdited by the cluster-h smoke.', { delay: 30 });
    await page.waitForTimeout(1200);
    expect(await page.inputValue('[data-testid="post-title-input"]')).toBe(marker);
    expect(await editor.inputValue()).toContain('Edited by the cluster-h smoke.');

    await page.click('[data-testid="save-post"]');
    await page.waitForURL((u) => u.pathname.toLowerCase().includes('blogslist'), { timeout: 60000 });
    await page.waitForTimeout(2000);
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
    fs.writeFileSync(`${SHOTS}/visual-editor.json`, JSON.stringify(results, null, 2));
  });
});
