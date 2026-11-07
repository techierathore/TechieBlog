/**
 * trblazeui-222-upgrade.spec.ts — consumer re-check of the TrBlazeUI 2.2.1 → 2.2.2 upgrade (2026-11-05).
 *
 * TR-077: on 2.2.1 `SelectValue.GetSelectedText` threw `RuntimeBinderException` on a re-render when the
 * bound value was the empty string, so any re-render of a page holding such a Select fell to the error
 * boundary ("Something went wrong"). The owner named three places to re-check on 2.2.2:
 *
 *   REQ-UI-017  /BlogsList search box          (bulk-action Select bound to "")
 *   REQ-UI-017  /BlogsList row Delete button    (same page, re-render before the confirm dialog)
 *   REQ-UI-016  the post editor after a publish (category / series Selects)
 *
 * On 2.2.1 the first two failed 1 of 1 on 2026-11-05 (tests/.artifacts/triage-20261105/). The tests run in
 * order: the editor test creates one fxv0 post, the search test finds it, the Delete test removes it.
 *
 * Run: TB_BASE=http://localhost:59689 npx playwright test tests/verify/trblazeui-222-upgrade.spec.ts
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';

const BASE = (process.env.TB_BASE ?? process.env.BASE_URL ?? 'http://localhost:5450').replace(/\/$/, '');
const OUT = 'tests/.artifacts/trblazeui-222';
fs.mkdirSync(OUT, { recursive: true });

const ADMIN = { email: 'Ravi@techieblog.com', password: process.env.TB_PW_ADMIN ?? 'VerifyAdmin#2026' };
const STAMP = Date.now().toString(36);
const TITLE = `fxv0 TR-077 publish check ${STAMP}`;
const SLUG = `fxv0-tr077-publish-check-${STAMP}`;
const SEARCH = 'fxv0-tr077';

let page: Page;
const notes: string[] = [];
const errorPage = () => page.locator('[data-testid="error-heading"]');

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  page = await ctx.newPage();
  page.on('pageerror', (e) => notes.push(`PAGEERROR ${e.message}`));
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
  await page
    .waitForFunction(() => {
      const f = document.querySelector('form');
      return !!f && !f.hasAttribute('action');
    }, { timeout: 90000 })
    .catch(() => {});
  const fillStable = async (sel: string, v: string) => {
    for (let i = 0; i < 15; i++) {
      await page.fill(sel, v);
      await page.waitForTimeout(500);
      if ((await page.inputValue(sel)) === v) return;
    }
    throw new Error(`${sel} would not hold its value`);
  };
  await fillStable('[data-testid="login-email"]', ADMIN.email);
  await fillStable('[data-testid="login-password"]', ADMIN.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 90000 });
  await page.waitForTimeout(2000);
  notes.push(`login landed on ${page.url()}`);
  expect(page.url()).not.toMatch(/change-password/i);
});

test.afterAll(async () => {
  fs.writeFileSync(`${OUT}/notes.json`, JSON.stringify(notes, null, 2));
});

async function fillCommitted(testId: string, value: string) {
  const el = page.locator(`[data-testid="${testId}"]`);
  await el.fill(value);
  await el.dispatchEvent('change');
  await page.waitForTimeout(500);
}

async function pickSelect(triggerTestId: string, optionText: string) {
  const option = page.locator('[role="option"]').filter({ hasText: optionText }).first();
  for (let i = 1; i <= 3; i++) {
    await page.click(`[data-testid="${triggerTestId}"]`);
    try {
      await option.waitFor({ state: 'visible', timeout: 8000 });
      await option.click();
      await page.waitForTimeout(800);
      return;
    } catch {
      await page.keyboard.press('Escape').catch(() => {});
      await page.waitForTimeout(1200);
    }
  }
  throw new Error(`could not pick "${optionText}" in "${triggerTestId}"`);
}

/**
 * Opens the posts list and waits until the interactive circuit owns it. A full load prerenders the
 * grid first; typing into that static copy is lost when the circuit re-renders the page a moment
 * later (seen 2026-11-05: the list came back as "All (0) … Loading" with an empty search box). So:
 * a fresh circuit when there is none yet, the app's own router when there is, then a 4 s settle.
 */
async function openList() {
  const live = await page.evaluate(() => !!((window as any).Blazor && (window as any).Blazor.navigateTo)).catch(() => false);
  if (live) {
    await page.evaluate(() => (window as any).Blazor.navigateTo('/admin'));
    await page.waitForTimeout(1500);
    await page.evaluate(() => (window as any).Blazor.navigateTo('/BlogsList'));
  } else {
    await page.goto(`${BASE}/BlogsList`, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => !!((window as any).Blazor && (window as any).Blazor.navigateTo), { timeout: 60000 }).catch(() => {});
  }
  await page.waitForSelector('[data-testid="posts-grid"], [data-testid="posts-empty"]', { timeout: 60000 });
  await page.waitForTimeout(4000);
  await expect(errorPage()).toHaveCount(0);
}

/** Types into the search box and leaves the field, the TR-077 trigger, then waits for the filter. */
async function searchList(term: string) {
  await page.fill('[data-testid="posts-search"]', term);
  await page.locator('[data-testid="posts-search"]').blur();
  await page.waitForTimeout(2500);
}

const rowWithSlug = (slug: string) =>
  page.locator('tbody tr').filter({ has: page.locator('[data-testid="post-row-slug"]', { hasText: slug }) });

// ---------------------------------------------------------------------------------------------
// REQ-UI-016 — the post editor after a publish
// ---------------------------------------------------------------------------------------------
test('REQ-UI-016 TR-077 on 2.2.2: publishing a new post from the editor, then editing it on, never shows the error page', async () => {
  test.setTimeout(300000);
  await page.goto(`${BASE}/ManagePost`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="markdown-input"]', { timeout: 60000 });
  await page.waitForTimeout(2500);
  await pickSelect('category-select', 'Web Development');
  await fillCommitted('post-title-input', TITLE);
  await fillCommitted('post-slug-input', SLUG);
  await fillCommitted('post-excerpt-input', 'fxv0 test post for the TR-077 re-check on TrBlazeUI 2.2.2. Not real content.');
  const md = page.locator('[data-testid="markdown-input"]');
  await md.fill('## fxv0 TR-077 publish check\n\nTest content created by the 2.2.2 re-check. Not real content.\n');
  await md.dispatchEvent('change');
  await page.waitForTimeout(2000);

  await page.locator('[data-testid="publish-post"]').scrollIntoViewIfNeeded();
  await page.click('[data-testid="publish-post"]');
  const settled = await settleSave();
  notes.push(`REQ-UI-016 publish settled: ${settled || 'never'} at ${page.url()}`);
  await page.screenshot({ path: `${OUT}/tr077-editor-after-publish.png`, fullPage: true });
  expect(settled, 'the publish must finish').not.toBe('');
  await expect(errorPage()).toHaveCount(0);

  // the editor after the publish: open the published post again, change it, save it
  await openEditorOf(SLUG);
  await expect(page.locator('[data-testid="post-status-badge"]').first()).toContainText(/Published/i);
  await fillCommitted('post-title-input', `${TITLE} edited`);
  await page.click('[data-testid="save-post"]');
  const saved = await settleSave();
  notes.push(`REQ-UI-016 save after publish: ${saved || 'never'} at ${page.url()}`);
  expect(saved, 'the save must finish').not.toBe('');
  await expect(errorPage()).toHaveCount(0);

  // and once more: the published post's editor re-rendering its Selects (series is bound to "")
  await openEditorOf(SLUG);
  await expect(page.locator('[data-testid="post-title-input"]')).toHaveValue(`${TITLE} edited`);
  await page.click('[data-testid="series-select"]');
  await page.waitForTimeout(1500);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1500);
  await expect(errorPage()).toHaveCount(0);
  await page.screenshot({ path: `${OUT}/tr077-editor-after-publish-edits.png`, fullPage: true });
});

/** Waits for a save or publish to finish: the status message, the redirect to the list, or the error page. */
async function settleSave(): Promise<string> {
  for (let i = 0; i < 40; i++) {
    await page.waitForTimeout(1500);
    if ((await errorPage().count()) > 0) return 'error page';
    const msg = ((await page.locator('[data-testid="post-status-message"]').textContent({ timeout: 1000 }).catch(() => '')) ?? '').trim();
    if (/published|saved|updated/i.test(msg)) return `message "${msg}"`;
    if (/\/BlogsList$/i.test(page.url())) return 'redirected to /BlogsList';
  }
  return '';
}

/** Opens the editor of the post with this slug from the posts list, through the list's own Edit button. */
async function openEditorOf(slug: string) {
  await openList();
  await searchList(SEARCH);
  await expect(errorPage()).toHaveCount(0);
  const row = rowWithSlug(slug);
  await expect(row).toHaveCount(1);
  await row.locator('[data-testid="post-edit"]').click({ timeout: 30000 });
  await page.waitForSelector('[data-testid="post-title-input"]', { timeout: 60000 });
  await page.waitForTimeout(2500);
  await expect(errorPage()).toHaveCount(0);
}

// ---------------------------------------------------------------------------------------------
// REQ-UI-017 — the posts-list search box
// ---------------------------------------------------------------------------------------------
test('REQ-UI-017 TR-077 on 2.2.2: typing in the /BlogsList search box and leaving it filters the list, no error page', async () => {
  test.setTimeout(240000);
  await openList();
  const before = await page.locator('[data-testid="post-row-title"]').count();
  await searchList(SEARCH);
  await expect(errorPage()).toHaveCount(0);
  const rows = page.locator('[data-testid="post-row-slug"]');
  const after = await rows.count();
  notes.push(`REQ-UI-017 search: ${before} rows before, ${after} after "${SEARCH}"`);
  expect(after).toBeGreaterThanOrEqual(1);
  for (const slug of await rows.allTextContents()) expect(slug).toContain(SEARCH);
  await expect(page.locator('[data-testid="posts-count"]')).toContainText(`Showing ${after} of`);
  // the Select whose empty bound value crashed 2.2.1 still shows its placeholder after the re-render
  await expect(page.locator('[data-testid="posts-bulk-action"]')).toContainText('Bulk Actions');
  await page.screenshot({ path: `${OUT}/tr077-posts-search.png`, fullPage: true });
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-017 — the posts-list row Delete button
// ---------------------------------------------------------------------------------------------
test('REQ-UI-017 TR-077 on 2.2.2: a row\'s Delete opens the confirm dialog, and confirming removes that row, no error page', async () => {
  test.setTimeout(300000);
  await openList();
  await searchList(SEARCH);
  const rows = page.locator('tbody tr').filter({ has: page.locator('[data-testid="post-row-slug"]', { hasText: SEARCH }) });
  const n = await rows.count();
  expect(n).toBeGreaterThanOrEqual(1);
  for (let i = 0; i < n; i++) {
    const row = rows.first();
    const slug = ((await row.locator('[data-testid="post-row-slug"]').textContent()) ?? '').trim();
    await row.locator('[data-testid="post-delete"]').click();
    const dialog = page.locator('[data-testid="post-delete-dialog"]');
    await expect(dialog).toBeVisible({ timeout: 30000 });
    await expect(errorPage()).toHaveCount(0);
    await expect(dialog.locator('[data-testid="post-delete-title"]')).toContainText('fxv0 TR-077');
    if (i === 0) await page.screenshot({ path: `${OUT}/tr077-delete-dialog.png` });
    await dialog.locator('[data-testid="post-delete-confirm"]').click();
    await expect(rowWithSlug(slug.replace(/^\//, ''))).toHaveCount(0, { timeout: 30000 });
    await expect(errorPage()).toHaveCount(0);
    notes.push(`REQ-UI-017 delete: removed ${slug}`);
  }
  await expect(rows).toHaveCount(0);
  await page.screenshot({ path: `${OUT}/tr077-after-delete.png`, fullPage: true });
});
