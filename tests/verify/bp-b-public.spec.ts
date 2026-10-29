// 2026-10-27 W1 (verify-phase): titles prefixed 'retired:' asserted retired demo/fixture data (UAT-007, 2026-08-22), the removed header About link, or wrote the database directly; they are superseded by tests/verify/fxv1-public.spec.ts and fxv1-settings.spec.ts, which build their own state through the app.
/**
 * bp-b-public.spec.ts — cluster B build-phase smoke (2026-10-27) for the PUBLIC read rows:
 * REQ-FN-020 (listings / related / reading time), REQ-FN-021 (search), REQ-FN-037 (RSS),
 * REQ-FN-038 (sitemap + robots) and the public half of REQ-FN-015 (draft invisibility).
 *
 * Fixture: the `fxb` rows seeded by tests/.artifacts/build-phase-20261027/b/seed.sql + seed2.sql
 * (category `fxb-category`, series `fxb-series`, six published posts and one draft `fxb-draft-post`).
 * Every assertion compares the rendered page with PostgreSQL read at run time, never a literal count,
 * because three sibling builders write to the same database while this runs.
 */
import { test, expect, request, Page } from '@playwright/test';
import { execSync } from 'child_process';

const BASE = process.env.TB_BASE ?? 'http://localhost:5402';
const SHOTS = 'tests/.artifacts/build-phase-20261027/b';

function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

async function fetchText(path: string): Promise<{ status: number; body: string }> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const r = await api.get(path);
    return { status: r.status(), body: await r.text() };
  } finally {
    await api.dispose();
  }
}

async function shoot(page: Page, name: string) {
  for (const [w, h] of [[1280, 900], [390, 844]] as const) {
    await page.setViewportSize({ width: w, height: h });
    await page.waitForTimeout(600);
    const hScroll = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
    expect(hScroll, `${name} at ${w}px must not scroll horizontally`).toBe(false);
    await page.screenshot({ path: `${SHOTS}/${name}-${w}.png` });
  }
}

test('retired: REQ-FN-020 category archive lists every published post newest-first and no draft', async ({ page }) => {
  const rows = sql(
    "SELECT slug FROM blogpost WHERE categoryid = (SELECT categoryid FROM category WHERE slug='fxb-category') AND published = TRUE AND isdeleted IS NOT TRUE ORDER BY coalesce(publishedon, createdon) DESC, postid DESC",
  );
  expect(rows.length).toBeGreaterThanOrEqual(6);
  await page.goto(`${BASE}/category/fxb-category`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('a[href^="/post/fxb-"]', { timeout: 60000 });
  const hrefs = await page.$$eval('a[href^="/post/"]', (as) => as.map((a) => (a.getAttribute('href') || '').replace('/post/', '')));
  const listed = [...new Set(hrefs)].filter((s) => s.startsWith('fxb-'));
  expect(listed, 'archive order equals the database order, newest first').toEqual(rows.slice(0, listed.length));
  expect(listed.length, 'every published post is on the archive').toBe(Math.min(rows.length, 10));
  expect(hrefs).not.toContain('fxb-draft-post');
  await shoot(page, 'fn020-category');
});

test('REQ-FN-020 home lists published posts newest-first, features the newest, never a draft', async () => {
  const newest = sql(
    "SELECT slug FROM blogpost WHERE published = TRUE AND isdeleted IS NOT TRUE ORDER BY coalesce(publishedon, createdon) DESC, postid DESC LIMIT 1",
  )[0];
  const { status, body } = await fetchText('/');
  expect(status).toBe(200);
  expect(body, 'the newest published post is on the home page').toContain(`/post/${newest}`);
  expect(body).not.toContain('/post/fxb-draft-post');
  // The home page shows the newest N published posts (N is the page's own latest-articles count),
  // so the posts it lists must be exactly the database's newest N, in the same order.
  const onHome = [...new Set([...body.matchAll(/href="\/post\/([a-z0-9-]+)"/g)].map((m) => m[1]))];
  expect(onHome.length).toBeGreaterThan(1);
  const dbNewest = sql(
    `SELECT slug FROM blogpost WHERE published = TRUE AND isdeleted IS NOT TRUE ORDER BY coalesce(publishedon, createdon) DESC, postid DESC LIMIT ${onHome.length}`,
  );
  expect(onHome, 'home lists the newest published posts, newest first').toEqual(dbNewest);
  expect(/data-testid="featured-post[^"]*"/.test(body) || /Featured/i.test(body)).toBe(true);
});

test('retired: REQ-FN-020 related posts share a category or tag and never include the post itself', async ({ page }) => {
  for (const slug of ['fxb-series-part-2', 'fxb-markdown-post', 'fxb-search-post']) {
    await page.goto(`${BASE}/post/${slug}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="post-title"]', { timeout: 60000 });
    const related = await page.$$eval('[data-testid="related-post-title"]', (as) => as.map((a) => (a.getAttribute('href') || '').replace('/post/', '')));
    for (const r of related) {
      expect(r, 'never the subject itself').not.toBe(slug);
      const shares = sql(
        `SELECT 1 FROM blogpost s, blogpost r WHERE s.slug='${slug}' AND r.slug='${r}' AND r.published = TRUE AND r.isdeleted IS NOT TRUE AND (s.categoryid = r.categoryid OR EXISTS (SELECT 1 FROM posttag a JOIN posttag b ON a.tagid=b.tagid WHERE a.postid=s.postid AND b.postid=r.postid))`,
      );
      expect(shares.length, `${r} shares a category or tag with ${slug}`).toBe(1);
    }
    expect(related.length, `${slug} has related posts`).toBeGreaterThan(0);
  }
  // A post with no category and no tag in common with anything has none: the strip is omitted.
  await page.goto(`${BASE}/post/fxb-series-part-2`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="related-posts"]', { timeout: 60000 });
  await shoot(page, 'fn020-post-related');
});

test('retired: REQ-FN-020 reading time on every surface equals ceil(words / 200)', async ({ page }) => {
  const row = sql("SELECT postcontent FROM blogpost WHERE slug = 'fxb-long-post'")[0];
  const words = row.split(/\s+/).filter(Boolean).length;
  const minutes = Math.max(1, Math.ceil(words / 200));
  expect(minutes).toBe(3);
  await page.goto(`${BASE}/post/fxb-long-post`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="post-title"]', { timeout: 60000 });
  await expect(page.locator('body')).toContainText(`${minutes} min read`);
  await page.goto(`${BASE}/search?q=fxb%20Long%20Post`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="search-result"]', { timeout: 60000 });
  const read = await page.locator('[data-testid="search-result"]', { hasText: 'fxb Long Post' }).locator('[data-testid="search-result-readtime"]').first().textContent();
  expect(read?.trim()).toBe(`${minutes} min read`);
  const cat = await fetchText('/category/fxb-category');
  expect(cat.body).toContain(`${minutes} min read`);
});

test('retired: REQ-FN-021 search finds title, abstract, body and tag words, counts match rows, never a draft', async ({ page }) => {
  const find = async (q: string): Promise<string[]> => {
    await page.goto(`${BASE}/search?q=${encodeURIComponent(q)}`, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('[data-testid="search-result"], [data-testid="search-empty"]', { timeout: 60000 });
    return page.$$eval('[data-testid="search-result-title"]', (as) => as.map((a) => (a.getAttribute('href') || '').replace('/post/', '')));
  };
  for (const [q, field] of [['quokkatitle', 'title'], ['quokkaabstract', 'abstract'], ['quokkabody', 'body'], ['quokkatag', 'tag']] as const) {
    expect(await find(q), `a word in the ${field} finds the post`).toContain('fxb-search-post');
  }
  expect(await find('zebraquark'), 'a draft containing the word is never returned').not.toContain('fxb-draft-post');
  expect((await find('zebraquark')).length).toBe(Number(sql("SELECT count(*) FROM blogpost WHERE published AND isdeleted IS NOT TRUE AND (title ILIKE '%zebraquark%' OR abstract ILIKE '%zebraquark%' OR postcontent ILIKE '%zebraquark%' OR tags ILIKE '%zebraquark%')")[0]));

  // Paging: count shown == DB count == rows listed across every page.
  const dbCount = Number(sql("SELECT count(*) FROM blogpost WHERE published AND isdeleted IS NOT TRUE AND (title ILIKE '%fxb%' OR abstract ILIKE '%fxb%' OR postcontent ILIKE '%fxb%' OR tags ILIKE '%fxb%')")[0]);
  const seen = new Set<string>();
  await page.goto(`${BASE}/search?q=fxb`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="search-total-count"]', { timeout: 60000 });
  const shown = Number(((await page.locator('[data-testid="search-total-count"]').textContent()) || '').replace(/\D/g, ''));
  expect(shown, 'the count shown equals the database count').toBe(dbCount);
  for (let guard = 0; guard < 10; guard++) {
    (await page.$$eval('[data-testid="search-result-title"]', (as) => as.map((a) => a.getAttribute('href') || ''))).forEach((h) => seen.add(h));
    const next = page.getByRole('button', { name: /next/i }).first();
    if (!(await next.count()) || (await next.isDisabled())) break;
    await next.click();
    await page.waitForTimeout(2500);
  }
  expect(seen.size, 'rows listed across the pages equal the count shown').toBe(dbCount);
  await page.goto(`${BASE}/search?q=zebraquark`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="search-result"]', { timeout: 60000 });
  await shoot(page, 'fn021-search');
});

test('REQ-FN-037 RSS is valid 2.0 with every recent published post and no draft', async () => {
  const { status, body } = await fetchText('/rss.xml');
  expect(status).toBe(200);
  expect(body).toContain('<rss version="2.0"');
  const items = [...body.matchAll(/<item>([\s\S]*?)<\/item>/g)].map((m) => m[1]);
  expect(items.length).toBeGreaterThan(0);
  const slugs: string[] = [];
  for (const it of items) {
    for (const tag of ['title', 'link', 'description', 'pubDate', 'dc:creator']) {
      expect(it, `item carries <${tag}>`).toMatch(new RegExp(`<${tag}>[^<]+</${tag}>`));
    }
    const path = (it.match(/<link>https?:\/\/[^/]+(\/[^<]*)<\/link>/) || [])[1];
    expect(path, 'link path').toMatch(/^\/post\//);
    slugs.push(path.replace('/post/', ''));
    const working = await fetchText(path);
    expect(working.status, `item link ${path} resolves`).toBe(200);
  }
  expect(slugs).not.toContain('fxb-draft-post');
  const dbPublished = sql("SELECT slug FROM blogpost WHERE published AND isdeleted IS NOT TRUE ORDER BY coalesce(publishedon, createdon) DESC, postid DESC LIMIT " + slugs.length).map((r) => r);
  expect(slugs, 'items are the newest published posts, in order').toEqual(dbPublished);
});

test('REQ-FN-038 sitemap lists every published post, category and tag and no draft; robots names it', async () => {
  const { status, body } = await fetchText('/sitemap.xml');
  expect(status).toBe(200);
  const locs = [...body.matchAll(/<loc>([^<]+)<\/loc>/g)].map((m) => m[1].replace(/^https?:\/\/[^/]+/, ''));
  const posts = sql("SELECT slug FROM blogpost WHERE published AND isdeleted IS NOT TRUE").map((s) => `/post/${s}`);
  for (const p of posts) expect(locs, `sitemap lists ${p}`).toContain(p);
  const drafts = sql("SELECT slug FROM blogpost WHERE (NOT published OR isdeleted) AND slug IS NOT NULL").map((s) => `/post/${s}`);
  for (const d of drafts) expect(locs, `sitemap omits ${d}`).not.toContain(d);
  const cats = sql("SELECT slug FROM category WHERE slug IS NOT NULL").map((s) => `/category/${s}`);
  const tags = sql("SELECT slug FROM tag WHERE slug IS NOT NULL").map((s) => `/tag/${s}`);
  for (const c of cats.filter((c) => c.includes('fxb'))) expect(locs, `sitemap lists ${c}`).toContain(c);
  for (const t of tags.filter((t) => t.includes('fxb'))) expect(locs, `sitemap lists ${t}`).toContain(t);
  const robots = await fetchText('/robots.txt');
  expect(robots.status).toBe(200);
  expect(robots.body).toMatch(/Sitemap:\s*\S+\/sitemap\.xml/);
});
