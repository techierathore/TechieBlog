/**
 * cluster-c-authoring.spec.ts — REQ-UI-016 / REQ-UI-017 (fix pass, 2026-08-09).
 *
 * REQ-UI-016 exists because the Markdown textarea LOST AND REORDERED KEYSTROKES: TrBlazeUI's
 * <Textarea> is a controlled input, so under Blazor Server every keystroke round-tripped and the
 * render that came back wrote a stale value into the DOM ('## Live heading' -> '## ivehading').
 * The fidelity test below is therefore the whole point of this file: it types a known string one
 * key at a time at two very different cadences, several runs each, and asserts the DOM value is
 * EXACTLY what was typed — both immediately and again after the circuit has had time to send one
 * more render, which is when the old build clobbered it.
 *
 * REQ-UI-017 exists because the four status tabs measure ~411px and were CLIPPED, not scrollable,
 * in a 390px viewport.
 *
 * Run with TB_BASE=http://localhost:5383 (cluster C's own port).
 */
import { test, expect, Page } from '@playwright/test';
import { BASE, login, nav } from './_gates';

const SHOTS = 'test-results-cluster-c';

/** The exact string the verifier used. 15 characters, two of them significant Markdown. */
const FIDELITY_TEXT = '## Live heading';

const MD_INPUT = '[data-testid="markdown-input"]';

/**
 * Opens the post editor and waits for it to be interactive.
 *
 * The page heading is a TrBlazeUI CardTitle, not an h1/h2, so the shared nav() heading gate
 * cannot see it — the editor's own test hook is the reliable readiness signal.
 */
async function openEditor(page: Page) {
  await nav(page, '/ManagePost');
  await expect(page.locator('[data-testid="content-panel-title"]')).toHaveText(/New Post/i, { timeout: 45000 });
  await expect(page.locator(MD_INPUT)).toBeVisible({ timeout: 45000 });
  await page.waitForTimeout(1200);
}

/**
 * Types `text` one key at a time and returns what the textarea actually holds.
 *
 * The second read after a settle delay is deliberate: the defect was a LATE server render
 * overwriting the DOM, so a value that is correct the instant typing stops can still be wrong a
 * moment later. Both reads have to match.
 */
async function typeAndRead(page: Page, text: string, delay: number) {
  const editor = page.locator(MD_INPUT);
  await editor.click();
  await editor.fill('');
  await page.waitForTimeout(600);
  await editor.pressSequentially(text, { delay });
  const immediate = await editor.inputValue();
  await page.waitForTimeout(2500);
  const settled = await editor.inputValue();
  return { immediate, settled };
}

test.describe('REQ-UI-016 — post editor keystroke fidelity', () => {
  test('markdown textarea keeps every keystroke, in order, at 120ms and 1000ms per key', async ({ page }) => {
    test.setTimeout(600000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await openEditor(page);

    const failures: string[] = [];

    for (const delay of [120, 1000]) {
      for (let run = 1; run <= 3; run++) {
        const { immediate, settled } = await typeAndRead(page, FIDELITY_TEXT, delay);
        if (immediate !== FIDELITY_TEXT) {
          failures.push(`${delay}ms run ${run}: immediate = ${JSON.stringify(immediate)}`);
        }
        if (settled !== FIDELITY_TEXT) {
          failures.push(`${delay}ms run ${run}: settled = ${JSON.stringify(settled)}`);
        }
      }
    }

    expect(failures, `keystrokes lost or reordered:\n${failures.join('\n')}`).toEqual([]);
  });

  /**
   * The teeth of this suite.
   *
   * On a fast local circuit the OLD controlled <Textarea> passed the cadences above, so that test
   * alone would have been vacuous — the verifier only saw the defect because seven agents were
   * hammering the host. This test reproduces that condition deterministically with 400ms of
   * emulated network latency and burst typing, which is when a keystroke is still in flight as
   * the previous render comes back. Measured against the PRE-FIX build these exact settings
   * yielded '#ve he', '## ng', '## Living' and '## Lie edg' in 4 of 9 runs; against the fixed
   * build, 9 of 9 exact. Do not soften the latency or the delays without re-running that
   * counterfactual.
   */
  test('keystrokes survive burst typing on a 400ms-latency circuit', async ({ page }) => {
    test.setTimeout(900000);
    await page.setViewportSize({ width: 1280, height: 900 });

    const client = await page.context().newCDPSession(page);
    await client.send('Network.enable');
    await client.send('Network.emulateNetworkConditions', {
      offline: false, latency: 400, downloadThroughput: -1, uploadThroughput: -1,
    });

    await login(page, 'admin');
    await openEditor(page);

    const failures: string[] = [];
    for (const delay of [0, 15, 40]) {
      for (let run = 1; run <= 3; run++) {
        const { immediate, settled } = await typeAndRead(page, FIDELITY_TEXT, delay);
        if (immediate !== FIDELITY_TEXT || settled !== FIDELITY_TEXT) {
          failures.push(
            `400ms latency, ${delay}ms/key, run ${run}: immediate=${JSON.stringify(immediate)} settled=${JSON.stringify(settled)}`);
        }
      }
    }

    expect(failures, `keystrokes lost or reordered under load:\n${failures.join('\n')}`).toEqual([]);
  });

  test('live preview still renders the typed markdown', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await openEditor(page);

    const editor = page.locator(MD_INPUT);
    await editor.click();
    await editor.pressSequentially('## Live heading\n\n**bold** and `code`\n\n- one\n- two', { delay: 40 });

    const preview = page.locator('[data-testid="markdown-preview-content"]');
    await expect(preview).toBeVisible({ timeout: 20000 });
    await expect(preview.locator('h2')).toHaveText('Live heading', { timeout: 20000 });
    await expect(preview.locator('strong')).toHaveText('bold');
    await expect(preview.locator('code')).toHaveText('code');
    await expect(preview.locator('li')).toHaveCount(2);

    // The textarea must not have been disturbed by the preview re-render.
    expect(await editor.inputValue()).toContain('## Live heading');

    await page.screenshot({ path: `${SHOTS}/ui016-managepost-1280.png`, fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await page.screenshot({ path: `${SHOTS}/ui016-managepost-390.png`, fullPage: true });
  });

  /**
   * The other half of the uncontrolled-input contract: a PROGRAMMATIC change still has to reach
   * the DOM. Toolbar inserts and view-mode switches are the only two writers allowed to move the
   * seed, so if the re-key ever regressed, typed text would survive but the toolbar would appear
   * to do nothing and switching back from Preview would show stale text.
   */
  test('toolbar insert and view-mode switching still write through to the textarea', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await openEditor(page);

    const editor = page.locator(MD_INPUT);
    await editor.click();
    await editor.pressSequentially('typed first', { delay: 40 });
    await page.waitForTimeout(800);

    await page.click('[data-testid="md-heading-2"]');
    await page.waitForTimeout(1200);
    expect(await editor.inputValue()).toBe('typed first\n## Heading 2');

    // Typing after a programmatic insert must append, not replace.
    await editor.click();
    await page.keyboard.press('End');
    await editor.pressSequentially(' more', { delay: 60 });
    await page.waitForTimeout(1200);
    expect(await editor.inputValue()).toBe('typed first\n## Heading 2 more');

    // Preview hides the textarea; coming back must show the CURRENT source, not the old seed.
    await page.click('[data-testid="markdown-view-preview"]');
    await page.waitForTimeout(1200);
    await expect(page.locator(MD_INPUT)).toHaveCount(0);
    await page.click('[data-testid="markdown-view-edit"]');
    await page.waitForTimeout(1200);
    expect(await page.inputValue(MD_INPUT)).toBe('typed first\n## Heading 2 more');
  });

  test('a save persists the body and every metadata field', async ({ page }) => {
    test.setTimeout(600000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await openEditor(page);

    const stamp = Date.now();
    const title = `Cluster C fidelity ${stamp}`;
    const slug = `cluster-c-fidelity-${stamp}`;
    const excerpt = `Excerpt for ${stamp}`;
    const body = `## Live heading\n\nBody written for run ${stamp}.`;

    await page.fill('[data-testid="post-title-input"]', title);
    await page.waitForTimeout(500);
    await page.fill('[data-testid="post-slug-input"]', slug);
    await page.waitForTimeout(500);
    await page.fill('[data-testid="post-excerpt-input"]', excerpt);
    await page.waitForTimeout(500);

    const editor = page.locator(MD_INPUT);
    await editor.click();
    await editor.fill('');
    await editor.pressSequentially(body, { delay: 25 });
    await page.waitForTimeout(800);

    // Category — first real option.
    await page.click('[data-testid="category-select"]');
    await page.waitForTimeout(600);
    await page.locator('[role="option"]').nth(1).click();
    await page.waitForTimeout(600);

    // Series — first real option, so a part number is auto-assigned.
    await page.click('[data-testid="series-select"]');
    await page.waitForTimeout(600);
    await page.locator('[role="option"]').nth(1).click();
    await page.waitForTimeout(600);

    // Two tags.
    for (const tag of [`clusterc${stamp}`, `fidelity${stamp}`]) {
      await page.fill('[data-testid="tag-input"]', tag);
      await page.waitForTimeout(400);
      await page.click('[data-testid="add-tag"]');
      await page.waitForTimeout(600);
    }
    await expect(page.locator('[data-testid="selected-tag"]')).toHaveCount(2);

    await page.click('[data-testid="save-draft"]');
    await expect(page.locator('[data-testid="post-status-message"]')).toContainText(/success/i, { timeout: 45000 });

    // Re-enter through the router so the assertions read PERSISTED state, not retained state.
    await nav(page, '/BlogsList', /All Posts/i);
    await page.fill('[data-testid="posts-search"]', String(stamp));
    await page.waitForTimeout(1500);
    const row = page.locator('[data-testid="post-row-title"]', { hasText: title });
    await expect(row).toHaveCount(1, { timeout: 30000 });
    await row.click();

    await expect(page.locator(MD_INPUT)).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(1500);

    expect(await page.inputValue('[data-testid="post-slug-input"]')).toBe(slug);
    expect(await page.inputValue('[data-testid="post-excerpt-input"]')).toBe(excerpt);
    expect(await page.inputValue(MD_INPUT)).toBe(body);
    await expect(page.locator('[data-testid="selected-tag"]')).toHaveCount(2);
    await expect(page.locator('[data-testid="series-part-badge"]')).toBeVisible();
    await expect(page.locator('[data-testid="category-select"]')).not.toContainText('-- Select Category --');
    await expect(page.locator('[data-testid="series-select"]')).not.toContainText('-- Not part of a series --');

    // Hand the slug to the shell so the run can clean the row up afterwards.
    console.log(`CLUSTER_C_CREATED_SLUG=${slug}`);
  });
});

test.describe('REQ-UI-017 — post list status filters', () => {
  test('status tabs are fully reachable at 390px and clean at 1280px', async ({ page }) => {
    test.setTimeout(300000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await login(page, 'admin');
    await nav(page, '/BlogsList', /All Posts/i);

    // Data-render gate: the grid must actually hold rows, and the tab counts must be non-zero.
    await expect(page.locator('[data-testid="post-row-title"]').first()).toBeVisible({ timeout: 30000 });
    const allTab = await page.locator('[data-testid="posts-tab-all"]').innerText();
    expect(allTab).toMatch(/All \((\d+)\)/);
    expect(Number(allTab.match(/\((\d+)\)/)![1])).toBeGreaterThan(0);

    await page.screenshot({ path: `${SHOTS}/ui017-blogslist-1280.png`, fullPage: true });

    // 390px: the last tab must be reachable — either it already fits, or the row scrolls to it.
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1500);

    const scroller = page.locator('[data-testid="posts-status-tabs-scroller"]');
    await expect(scroller).toBeVisible();

    const metrics = await scroller.evaluate((el) => ({
      clientWidth: el.clientWidth,
      scrollWidth: el.scrollWidth,
      overflowX: getComputedStyle(el).overflowX,
    }));
    // Whatever overflows must be scrollable, never clipped.
    expect(metrics.overflowX).toBe('auto');
    expect(metrics.clientWidth).toBeLessThanOrEqual(390);

    // Scroll to the far end and confirm the last tab is then wholly inside the viewport.
    await scroller.evaluate((el) => { el.scrollLeft = el.scrollWidth; });
    await page.waitForTimeout(600);
    const box = await page.locator('[data-testid="posts-tab-scheduled"]').boundingBox();
    expect(box, 'scheduled tab has no box').not.toBeNull();
    expect(box!.x).toBeGreaterThanOrEqual(-1);
    expect(box!.x + box!.width).toBeLessThanOrEqual(391);

    // And the page itself must still not scroll sideways.
    const bodyOverflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(bodyOverflow).toBeLessThanOrEqual(1);

    // The filter must still work after the layout change.
    await page.click('[data-testid="posts-tab-draft"]');
    await page.waitForTimeout(1500);
    await expect(page.locator('[data-testid="posts-count"]')).toBeVisible();

    await page.screenshot({ path: `${SHOTS}/ui017-blogslist-390.png`, fullPage: true });
  });
});

test.afterAll(async () => {
  // BASE is exported only so the import is not unused when a run is filtered down to one spec.
  void BASE;
});
