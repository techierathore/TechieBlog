/**
 * REQ-FN-040 / production report 2026-10-21 — "I set 2400 words per page and the pages are way
 * shorter." The post page read the word count from a browser-storage key nothing ever wrote, so
 * every article split at 500 words whatever Settings said. This spec changes the setting through
 * /settings as the owner does and reads the public page's own "Page 1 of N" after each save.
 *
 * Writes to the LOCAL database only (seeded post + the setting); both are restored in afterAll.
 */
import { test, expect, request } from '@playwright/test';
import { BASE, login, nav, USERS } from './_gates';
import { psql } from './_engage-helpers';

// _gates.ts still carries the retired admin password; the UsageGuide's test-user table is current.
(USERS.admin as { password: string }).password = process.env.TB_ADMIN_PASSWORD ?? 'VerifyAdmin#2026';

const SLUG = `fn040-pagination-probe-${Date.now()}`;
// 30 paragraphs x 100 words = 3000 words, built in SQL: a newline inside `psql -c` text would be
// read as a psql backslash command.
const BODY_SQL = "(select string_agg((select string_agg('word' || j, ' ') from generate_series(0, 99) j), chr(10) || chr(10)) from generate_series(1, 30))";

let originalWordCount = '500';

/** Reads the public post page anonymously and returns its "Page 1 of N" total, or 1 when unpaged. */
async function publicPageCount(): Promise<number> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const html = await (await api.get(`/post/${SLUG}`)).text();
    const match = html.match(/Page 1 of (\d+)/);
    return match ? Number(match[1]) : 1;
  } finally {
    await api.dispose();
  }
}

/** Sets the word count on /settings → Blog and saves. */
async function saveWordCount(page: import('@playwright/test').Page, value: string) {
  await nav(page, '/settings');
  await page.click('[data-testid="tab-blog"]');
  const field = page.locator('[data-testid="pagination-word-count"]');
  await field.fill(value);
  await field.blur();
  await page.waitForTimeout(500);
  await page.click('[data-testid="save-settings"]');
  await page.waitForTimeout(2500);
}

test.beforeAll(() => {
  originalWordCount = psql("select settingvalue from sitesetting where settingkey='Blog.PaginationWordCount'") || '500';
  psql(`insert into blogpost (title, abstract, postcontent, createdon, updatedon, userid, published, slug, isdeleted, publishedon, categoryid) `
     + `values ('FN040 Pagination Probe', 'probe', ${BODY_SQL}, now(), now(), 1, true, '${SLUG}', false, now(), (select min(categoryid) from category))`);
});

test.afterAll(() => {
  psql(`delete from postviews where postid in (select postid from blogpost where slug='${SLUG}')`);
  psql(`delete from postviewcount where postid in (select postid from blogpost where slug='${SLUG}')`);
  psql(`delete from blogpost where slug='${SLUG}'`);
  psql(`update sitesetting set settingvalue='${originalWordCount}' where settingkey='Blog.PaginationWordCount'`);
});

test('REQ-FN-040 the article pagination word count saved in Settings governs the public post pages', async ({ page }) => {
  test.setTimeout(5 * 60 * 1000);
  await login(page, 'admin');

  // The probe post was written straight to the database; drop the site's cached listings first.
  await nav(page, '/settings');
  await page.click('[data-testid="clear-cache"]');
  await page.waitForTimeout(1500);

  await saveWordCount(page, '2400');
  expect(await publicPageCount(), '3000 words at 2400 per page is 2 pages').toBe(2);

  await saveWordCount(page, '1000');
  expect(await publicPageCount(), '3000 words at 1000 per page is 3 pages').toBe(3);
});
