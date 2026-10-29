// 2026-10-27 W1 (verify-phase): titles prefixed 'retired:' asserted retired demo/fixture data (UAT-007, 2026-08-22), the removed header About link, or wrote the database directly; they are superseded by tests/verify/fxv1-public.spec.ts and fxv1-settings.spec.ts, which build their own state through the app.
/**
 * bp-b-misc.spec.ts — cluster B build-phase smoke (2026-10-27):
 *   REQ-FN-032  the unsubscribe link carried by a newsletter removes the subscriber
 *   REQ-FN-015  publish / unpublish makes a post appear / disappear on search, archive, RSS, sitemap
 *   REQ-FN-034  post views: a row per visit, de-duplicated per visitor per window, no raw IP stored
 * Fixtures: subscriber `fxb-unsub@example.com`, the seeded draft `fxb-draft-post`, views of `fxb-long-post`.
 */
import { test, expect, request } from '@playwright/test';
import { execSync } from 'child_process';
import { loginHard, goTo, openPostById } from './bp-b-helpers';

const BASE = process.env.TB_BASE ?? 'http://localhost:5402';

function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

// retired 2026-10-27 (W5): seeds and deletes subscriber / view rows and back-dates views with psql (blocked in verify phase); superseded by fxv5-newsletter REQ-FN-032 and fxv5-captcha-views REQ-FN-034.
test.skip('retired: the unsubscribe link removes the subscriber and replays as a no-op', async ({ browser }) => {
  test.setTimeout(240000);
  sql("DELETE FROM subscriber WHERE email = 'fxb-unsub@example.com'");
  sql("INSERT INTO subscriber (email, name, subscribedon, isconfirmed, confirmedon) VALUES ('fxb-unsub@example.com','Fxb Unsub',now() at time zone 'utc',true, now() at time zone 'utc')");
  try {
    const token = sql("SELECT unsubscribetoken FROM subscriber WHERE email = 'fxb-unsub@example.com'")[0];
    expect(token.length).toBeGreaterThan(20);
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    await page.goto(`${BASE}/unsubscribe/${token}`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('body')).toContainText(/unsubscrib/i, { timeout: 60000 });
    await expect.poll(() => sql("SELECT isconfirmed FROM subscriber WHERE email = 'fxb-unsub@example.com'")[0], { timeout: 60000 }).toBe('f');
    await page.waitForTimeout(1500);
    await page.screenshot({ path: 'tests/.artifacts/build-phase-20261027/b/fn032-unsubscribed-1280.png' });
    // replay: graceful, nothing breaks
    await page.goto(`${BASE}/unsubscribe/${token}`, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(4000);
    expect(await page.locator('body').innerText()).not.toMatch(/exception|error occurred/i);
    expect(sql("SELECT isconfirmed FROM subscriber WHERE email = 'fxb-unsub@example.com'")[0]).toBe('f');
    await page.close();
  } finally {
    sql("DELETE FROM subscriber WHERE email = 'fxb-unsub@example.com'");
  }
});

async function surfaces(slug: string): Promise<{ search: boolean; rss: boolean; sitemap: boolean; archive: boolean; post: number }> {
  const api = await request.newContext({ baseURL: BASE });
  const get = async (p: string) => api.get(p);
  const rss = await (await get('/rss.xml')).text();
  const sitemap = await (await get('/sitemap.xml')).text();
  const archive = await (await get('/category/fxb-category')).text();
  const search = await (await get('/search?q=zebraquark')).text();
  const post = (await get(`/post/${slug}`)).status();
  await api.dispose();
  return {
    search: search.includes(`/post/${slug}`),
    rss: rss.includes(`/post/${slug}`),
    sitemap: sitemap.includes(`/post/${slug}`),
    archive: archive.includes(`/post/${slug}`),
    post,
  };
}

test('retired: REQ-FN-015 a draft is absent everywhere; publishing shows it; unpublishing hides it again', async ({ browser }) => {
  test.setTimeout(420000);
  const slug = 'fxb-draft-post';
  const id = sql(`SELECT postid FROM blogpost WHERE slug = '${slug}'`)[0];
  sql(`UPDATE blogpost SET published = FALSE, publishedon = NULL WHERE postid = ${id}`);
  try {
    expect(await surfaces(slug), 'draft: 404 and absent from search, RSS, sitemap, archive').toEqual({ search: false, rss: false, sitemap: false, archive: false, post: 404 });
    const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    await loginHard(page, 'admin');
    await openPostById(page, id);
    await page.click('[data-testid="publish-post"]');
    await expect.poll(() => sql(`SELECT published FROM blogpost WHERE postid = ${id}`)[0], { timeout: 60000 }).toBe('t');
    await page.waitForTimeout(3000);
    const live = await surfaces(slug);
    console.log(`FN-015 after publish: ${JSON.stringify(live)}`);
    expect(live).toEqual({ search: true, rss: true, sitemap: true, archive: true, post: 200 });
    await openPostById(page, id);
    await page.click('[data-testid="unpublish-post"]');
    await expect.poll(() => sql(`SELECT published FROM blogpost WHERE postid = ${id}`)[0], { timeout: 60000 }).toBe('f');
    await page.waitForTimeout(3000);
    expect(await surfaces(slug), 'unpublished: gone again').toEqual({ search: false, rss: false, sitemap: false, archive: false, post: 404 });
    await page.close();
  } finally {
    sql(`UPDATE blogpost SET published = FALSE, publishedon = NULL WHERE postid = ${id}`);
  }
});

// retired 2026-10-27 (W5): seeds and deletes subscriber / view rows and back-dates views with psql (blocked in verify phase); superseded by fxv5-newsletter REQ-FN-032 and fxv5-captcha-views REQ-FN-034.
test.skip('retired: views are recorded per visitor with no raw IP; a repeat after the window raises total not unique', async ({ browser }) => {
  test.setTimeout(300000);
  const slug = 'fxb-long-post';
  const id = sql(`SELECT postid FROM blogpost WHERE slug = '${slug}'`)[0];
  sql(`DELETE FROM postviews WHERE postid = ${id}`);
  sql(`DELETE FROM postviewcount WHERE postid = ${id}`);
  try {
    const ctx = await browser.newContext({ userAgent: 'fxb-visitor-agent' });
    const page = await ctx.newPage();
    const visit = async () => {
      await page.goto(`${BASE}/post/${slug}`, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('[data-testid="post-title"]', { timeout: 60000 });
      await page.waitForTimeout(4000);
    };
    await visit();
    await expect.poll(() => sql(`SELECT count(*) FROM postviews WHERE postid = ${id}`)[0], { timeout: 30000 }).toBe('1');
    const row = sql(`SELECT coalesce(viewerip,''), length(visitorhash) FROM postviews WHERE postid = ${id}`)[0].split('|');
    expect(row[0], 'no raw IP address is stored').toBe('');
    expect(Number(row[1])).toBe(64);
    // inside the window the repeat visit is absorbed (documented design): still one row
    await visit();
    expect(sql(`SELECT count(*) FROM postviews WHERE postid = ${id}`)[0]).toBe('1');
    // after the window: total rises, unique does not
    sql(`UPDATE postviews SET viewedon = viewedon - interval '25 hours' WHERE postid = ${id}`);
    await visit();
    await expect.poll(() => sql(`SELECT count(*) FROM postviews WHERE postid = ${id}`)[0], { timeout: 30000 }).toBe('2');
    expect(sql(`SELECT count(DISTINCT visitorhash) FROM postviews WHERE postid = ${id}`)[0]).toBe('1');
    const counts = sql(`SELECT totalviews, uniqueviews FROM postviewcount WHERE postid = ${id}`)[0];
    console.log(`FN-034 postviewcount total|unique = ${counts}`);
    await ctx.close();
  } finally {
    sql(`DELETE FROM postviews WHERE postid = ${id}`);
    sql(`DELETE FROM postviewcount WHERE postid = ${id}`);
  }
});
