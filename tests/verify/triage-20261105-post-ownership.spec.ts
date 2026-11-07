/**
 * triage-20261105-post-ownership.spec.ts — the defect the DevGuide flagged on 2026-11-02 (D3) and
 * *triage-and-fix reproduced on 2026-11-05: an Author can open any post by id in the editor and in the
 * draft preview, with no owner check (ManagePost.razor.cs LoadExistingPostAsync, PreviewPost.razor LoadPost),
 * although the posts list already limits an Author to their own posts (REQ-UI-017).
 *
 * The test states the expected behaviour: an Author who opens another author's post gets no editable form
 * and no preview of it. It FAILS until the owner check exists. Evidence of the defect:
 * tests/.artifacts/triage-20261105/04-author-opens-post-521.png.
 *
 * Run: TB_BASE=http://localhost:59689 npx playwright test tests/verify/triage-20261105-post-ownership.spec.ts
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { execFileSync } from 'child_process';

/** The same read-only psql call the seed driver uses (psql lives inside the WinPostgre container). */
function psqlRead(sql: string): string {
  return execFileSync('docker', ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql], { encoding: 'utf8' }).trim();
}

const BASE = (process.env.TB_BASE ?? process.env.BASE_URL ?? 'http://localhost:5450').replace(/\/$/, '');
const OUT = 'tests/.artifacts/triage-20261105';
fs.mkdirSync(OUT, { recursive: true });

const AUTHOR = { email: 'author@techieblog.test', password: process.env.TB_PW_AUTHOR ?? 'TriageYolo2026', userId: 3 };

let page: Page;
const notes: string[] = [];

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
  await fillStable('[data-testid="login-email"]', AUTHOR.email);
  await fillStable('[data-testid="login-password"]', AUTHOR.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 90000 });
  await page.waitForTimeout(2000);
  notes.push(`author login landed on ${page.url()}`);
  expect(page.url()).not.toMatch(/change-password/i);
});

test.afterAll(async () => {
  fs.writeFileSync(`${OUT}/notes-ownership.json`, JSON.stringify(notes, null, 2));
});

/** A live post that belongs to someone other than the Author, read from the database (a read, never a write). */
function someoneElsesPost(): { id: string; title: string } | null {
  const row = psqlRead(
    `select postid || '|' || title from blogpost where userid <> ${AUTHOR.userId} and coalesce(isdeleted,false) = false order by postid desc limit 1`,
  );
  if (!row) return null;
  const [id, ...title] = row.split('|');
  return { id, title: title.join('|') };
}

test('REQ-UI-016 an Author who opens another author\'s post by id gets no editable form and no preview of it', async () => {
  test.setTimeout(240000);
  const other = someoneElsesPost();
  test.skip(!other, 'the database holds no live post owned by another user');
  const { id, title } = other!;
  notes.push(`target post ${id} "${title}"`);

  await page.goto(`${BASE}/ManagePost/${id}`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(6000);
  // a bounded wait: with no title box on the page (the fixed behaviour) an unbounded inputValue never returns
  const titleValue = await page.locator('[data-testid="post-title-input"]').inputValue({ timeout: 3000 }).catch(() => '');
  const saveButtons = await page.locator('[data-testid="save-post"], [data-testid="publish-post"], [data-testid="save-draft"]').count();
  await page.screenshot({ path: `${OUT}/ownership-editor-${id}.png`, fullPage: true });
  notes.push(`editor: url=${page.url()} title="${titleValue}" saveButtons=${saveButtons}`);
  expect(titleValue, 'the editor must not load another author\'s post into the form').not.toBe(title);
  expect(saveButtons, 'no save or publish button for another author\'s post').toBe(0);

  await page.goto(`${BASE}/admin/preview/${id}`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(5000);
  const body = await page.locator('body').innerText();
  await page.screenshot({ path: `${OUT}/ownership-preview-${id}.png`, fullPage: true });
  notes.push(`preview: url=${page.url()} showsTitle=${body.includes(title)}`);
  expect(body, 'the draft preview must not show another author\'s post').not.toContain(title);
});
