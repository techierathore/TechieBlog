/**
 * fxv5-nfr.spec.ts — verify-phase writer W5, 2026-10-27. REQ-FN-052.
 *
 * "When the host runs against a database built only by the migrations, then no page or service
 * errors on a missing `svctoken` relation, and public pages still render their content; no source
 * file references `svctoken`."
 *
 * The running dev host IS such a host: its database holds no `svctoken` relation (asserted below
 * with a psql SELECT), so serving real pages proves the clause. Content is the fxv5 fixture posts
 * built through the admin UI. The source scan walks source/ (skipping build output) with a positive
 * control so a zero cannot come from an empty or misrooted walk. Read-only throughout.
 */
import { test, expect } from '@playwright/test';
import { execFileSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { gotoPublic } from './_engage-helpers';
import { setupFixtures, teardownFixtures, sqlOne, postSlug, SHOTS } from './fxv5-fixtures';

const BASE = process.env.TB_BASE ?? 'http://localhost:5450';
const SRC = path.resolve(__dirname, '../../source');

function walk(dir: string, out: string[] = []): string[] {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory()) {
      if (['bin', 'obj', 'node_modules', '.vs'].includes(e.name)) continue;
      walk(path.join(dir, e.name), out);
    } else if (/\.(cs|razor|sql|json|csproj|md|js|ts|css|html|xml|slnx|props|targets)$/i.test(e.name)) out.push(path.join(dir, e.name));
  }
  return out;
}

test.beforeAll(() => fs.mkdirSync(SHOTS, { recursive: true }));
test.beforeAll(async ({ browser }) => { test.setTimeout(600000); await setupFixtures(browser); });
test.afterAll(async ({ browser }) => { test.setTimeout(600000); await teardownFixtures(browser, { comments: false }); });


test('REQ-FN-052 the database holds no svctoken relation and no migration or source file references one', async () => {
  expect(sqlOne(`SELECT coalesce(to_regclass('public.svctoken')::text, 'absent')`), 'host database has no svctoken relation').toBe('absent');
  const files = walk(SRC);
  expect(files.length, 'positive control: the walk found the source tree').toBeGreaterThan(100);
  const withBlogPost = files.filter((f) => /BlogPost/.test(fs.readFileSync(f, 'utf8')));
  expect(withBlogPost.length, 'positive control: a known token is found by the same scan').toBeGreaterThan(5);
  const hits = files.filter((f) => /svctoken/i.test(fs.readFileSync(f, 'utf8'))).map((f) => path.relative(SRC, f));
  expect(hits, 'no source file references svctoken').toEqual([]);
});

test('REQ-FN-052 public pages render their content on the migrations-only database with no svctoken error', async ({ page }) => {
  test.setTimeout(300000);
  test.skip(!postSlug(), 'fixture post missing');
  const consoleErrors: string[] = [];
  page.on('pageerror', (e) => consoleErrors.push(e.message));
  const slug = postSlug();
  const pages: Array<[string, RegExp]> = [
    // the home page shows the newest published posts, which other writers also create: expect the database's newest
    ['/', new RegExp(`/post/${sqlOne("SELECT slug FROM blogpost WHERE published AND COALESCE(isdeleted,false) = false ORDER BY publishedon DESC, postid DESC LIMIT 1")}`)],
    ['/about', /./],
    ['/categories', /./],
    ['/tags', /./],
    ['/series', /./],
    ['/search?q=fxv5', /./],
    [`/post/${slug}`, /fxv5 engage body paragraph/],
  ];
  for (const [route, expectText] of pages) {
    const res = await page.request.get(`${BASE}${route}`);
    expect(res.status(), `${route} HTTP status`).toBe(200);
    const html = await res.text();
    expect(html, `${route}: no svctoken text`).not.toMatch(/svctoken/i);
    expect(html, `${route}: no database-error text`).not.toMatch(/Npgsql\.|42P01|relation "[^"]*" does not exist/i);
    expect(html, `${route}: renders its content`).toMatch(expectText);
    await gotoPublic(page, route);
    if (route.startsWith('/search')) await expect(page.locator('body')).toContainText('fxv5 Engage Post', { timeout: 60000 }); // results render on the interactive pass
    const body = await page.locator('body').innerText();
    expect(body, `${route}: rendered body has no error text`).not.toMatch(/An unhandled error has occurred|svctoken|relation .* does not exist/i);
    expect(body.trim().length, `${route}: not blank`).toBeGreaterThan(40);
  }
  const rss = await page.request.get(`${BASE}/rss.xml`);
  expect(rss.status()).toBe(200);
  expect(await rss.text()).toContain(`/post/${slug}`);
  await page.screenshot({ path: `${SHOTS}/fn052-post.png` });
  expect(consoleErrors, 'no uncaught page errors').toEqual([]);
});

test('REQ-FN-052 the host log records no missing-svctoken relation error', async () => {
  const log = path.resolve(__dirname, '../.artifacts/verify/app-5450.log');
  test.skip(!fs.existsSync(log), 'host log not available');
  const count = (needle: string) =>
    Number(execFileSync('bash', ['-c', `grep -c -i -F -- '${needle}' '${log}' || true`], { encoding: 'utf8' }).trim() || '0');
  expect(count('Navigating to component'), 'positive control: the log is the live host log').toBeGreaterThan(0);
  expect(count('svctoken'), 'no svctoken mention (relation error or otherwise) in the host log').toBe(0);
});
