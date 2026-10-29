/**
 * fix1-editor.spec.ts - build-phase FIX cycle 1, cluster F1 (2026-10-28). Row: REQ-UI-016.
 *
 * Two editor defects the W3 verify run found:
 *   (a) a ~17 KB Markdown body failed to save with "Content is required" while ~7 KB saved;
 *   (b) a fresh page load of /ManagePost sometimes left the category dropdown with no options.
 *
 * Fixtures carry the prefix `fxf1-`; the only DB access is read-only psql plus one cleanup delete
 * (build-phase allows it; the rows are ours).
 */
import { test, expect } from '@playwright/test';
import { BASE, psql, pollDb, q, newAdminPage, goTo, fillCommitted } from './w3-helpers';
import { deletePostByTitle } from './fxv1-fixtures';

test.describe.configure({ mode: 'default' });
test.use({ actionTimeout: 30000 });

// Unique per run (T2 triage 2026-10-28): a fixed title collided with rows an earlier run left behind, and the
// poll then read two rows. The posts are deleted through /BlogsList in `finally`.
const RUN = Date.now().toString(36);
const TITLE = `fxf1-long-body-${RUN}`;

/**
 * Puts `text` into the Markdown textarea the way a paste does: ONE input event carrying the whole value.
 * (Playwright's fill() makes Chromium emit one input event per line, each re-sending the whole body -
 * hundreds of full-size circuit messages that bury the Save click; that is a harness artefact, not what
 * a person does, so the editor is fed the way a person feeds it.)
 */
async function pasteBody(page: import('@playwright/test').Page, text: string) {
  await page.evaluate((t) => {
    const el = document.querySelector('[data-testid="markdown-input"]') as HTMLTextAreaElement;
    el.focus();
    const setter = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!;
    setter.call(el, t);
    el.dispatchEvent(new InputEvent('input', { bubbles: true, inputType: 'insertFromPaste', data: null }));
  }, text);
  await page.waitForTimeout(2500);
}

/** Builds a Markdown body of roughly `bytes` characters, one paragraph per line so it stays valid prose. */
function bodyOf(bytes: number): string {
  const para = 'Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt.\n\n';
  let s = '## fxf1 long body\n\n';
  while (s.length < bytes) s += para;
  return s;
}

test('REQ-UI-016 a long Markdown body saves intact (17 KB and 120 KB)', async ({ browser }) => {
  test.setTimeout(300000);
  const page = await newAdminPage(browser, 'admin');
  try {
    for (const [i, size] of [17000, 120000].entries()) {
      const title = `${TITLE}-${i}`;
      const md = bodyOf(size);
      // leave the editor first: a same-route navigate is ignored, and the second pass would edit the first post
      await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
      await goTo(page, '/ManagePost', '[data-testid="post-title-input"]', 120000);
      await expect(page.locator('[data-testid="post-title-input"]')).toHaveValue('');
      await fillCommitted(page, 'post-title-input', title);
      await pasteBody(page, md);
      expect((await page.locator('[data-testid="markdown-input"]').inputValue()).length, `editor holds the ${size}-char body`).toBe(md.length);
      await page.click('[data-testid="save-draft"]');
      const len = await pollDb(
        `SELECT length(postcontent) FROM blogpost WHERE title='${q(title)}' AND isdeleted IS NOT TRUE`,
        (v) => v !== '', 60000);
      expect(Number(len), `${size}-char body persisted whole (no "Content is required")`).toBe(md.length);
    }
  } finally {
    for (const i of [0, 1]) await deletePostByTitle(page, `${TITLE}-${i}`).catch(() => {});
    await page.context().close();
  }
});

test('REQ-UI-016 the category dropdown lists options on every fresh load of /ManagePost, even when clicked the instant the form appears', async ({ browser }) => {
  test.setTimeout(300000);
  const page = await newAdminPage(browser, 'admin');
  const expected = Number(psql('SELECT count(*) FROM category'));
  expect(expected).toBeGreaterThan(1);
  try {
    for (let i = 0; i < 8; i++) {
      await page.goto(`${BASE}/ManagePost`, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('[data-testid="post-title-input"]', { timeout: 60000 });
      if (i % 2 === 1) await page.waitForTimeout(3500); // settled; even loads click the instant the form appears
      const trigger = page.locator('[data-testid="category-select"]');
      await trigger.click();
      const options = page.locator('[role="option"]');
      await expect(options.first(), `load ${i}: dropdown opened with options`).toBeVisible({ timeout: 10000 });
      // placeholder item + one per category
      expect(await options.count(), `load ${i}: every category is offered`).toBeGreaterThanOrEqual(expected + 1);
      await page.keyboard.press('Escape');
    }
  } finally {
    await page.context().close();
  }
});
