// 2026-10-27 W1 (verify-phase): titles prefixed 'retired:' asserted retired demo/fixture data (UAT-007, 2026-08-22), the removed header About link, or wrote the database directly; they are superseded by tests/verify/fxv1-public.spec.ts and fxv1-settings.spec.ts, which build their own state through the app.
import { test, expect } from '@playwright/test';
import { loginHard, psql, SHOT } from './bp-a-helpers';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5401';
const OUT = `${SHOT}/fixes`;

test.setTimeout(240000);

test('REQ-UI-036 resume anchors stay on /resume', async ({ page }) => {
  await page.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="nav-contact"]', { timeout: 60000 });
  await page.waitForTimeout(2500);
  await page.click('[data-testid="nav-contact"]');
  await page.waitForTimeout(1500);
  expect(new URL(page.url()).pathname).toBe('/resume');
  expect(page.url()).toContain('#contact');
  await page.screenshot({ path: `${OUT}/resume-anchor.png` });
});

test('retired: REQ-UI-011 search count shows the total, not the page size', async ({ page }) => {
  await page.goto(`${BASE}/search?q=fx`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="search-total-count"]', { timeout: 60000 });
  await page.waitForTimeout(2500);
  const shown = (await page.locator('[data-testid="search-total-count"]').textContent())!.trim();
  const db = psql("select count(*) from blogpost where published and (isdeleted=false or isdeleted is null) and (title ilike '%fx%' or abstract ilike '%fx%' or postcontent ilike '%fx%' or tags ilike '%fx%')")[0][0];
  console.log('search shown', shown, 'db', db);
  expect(parseInt(shown, 10)).toBe(parseInt(db, 10));
});

test('builder evidence (needs deleted psql fixture fxa-series; the active-item check now lives in vall-admin.spec.ts and the spam check in fxv4-admin-moderation.spec.ts): active admin item + REQ-UI-024 reorder + spam persists', async ({ page }) => {
  await loginHard(page, 'admin');
  await page.goto(`${BASE}/admin/series`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="nav-series"]', { timeout: 60000 });
  await page.waitForTimeout(5000);
  const active = await page.locator('[data-testid="nav-series"]').getAttribute('data-active');
  const other = await page.locator('[data-testid="nav-posts"]').getAttribute('data-active');
  console.log('nav-series data-active', active, 'nav-posts', other);
  // TR-076: the library emits data-active="" (present) for the active item; the highlight style itself is a library gap.
  expect(active).not.toBeNull();
  expect(other).toBeNull();
  const bg = await page.locator('[data-testid="nav-series"]').evaluate((e) => getComputedStyle(e).backgroundColor);
  console.log('active bg', bg);
  await page.screenshot({ path: `${OUT}/admin-active.png` });

  // series reorder
  const sid = psql("select seriesid from blogseries where slug='fxa-series'")[0][0];
  const before = psql(`select slug,seriesPartNumber from blogpost where seriesid=${sid} order by seriespartnumber`);
  await page.goto(`${BASE}/admin/series/${sid}`, { waitUntil: 'domcontentloaded' }).catch(() => {});
  await page.waitForSelector('[data-testid="series-post-row"]', { timeout: 60000 }).catch(async () => {
    await page.evaluate((id) => (window as any).Blazor.navigateTo('/admin/series/' + id), sid);
  });
  await page.waitForSelector('[data-testid="series-post-row"]', { timeout: 60000 });
  await page.waitForTimeout(2500);
  await page.locator('[data-testid="series-post-down"]').first().click();
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${OUT}/series-reorder.png` });
  await page.click('[data-testid="save-series"]');
  await page.waitForURL(/\/admin\/series$/, { timeout: 30000 });
  const after = psql(`select slug,seriesPartNumber from blogpost where seriesid=${sid} order by seriespartnumber`);
  console.log('before', JSON.stringify(before), 'after', JSON.stringify(after));
  expect(after[0][0]).toBe(before[1][0]);
  // restore original order through the same UI (fresh page load)
  await page.goto(`${BASE}/admin/series/${sid}`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="series-post-down"]', { timeout: 60000 });
  await page.waitForTimeout(4000);
  await page.locator('[data-testid="series-post-down"]').first().click();
  await page.waitForTimeout(800);
  await page.click('[data-testid="save-series"]');
  await page.waitForURL(/\/admin\/series$/, { timeout: 30000 });
  expect(psql(`select slug,seriesPartNumber from blogpost where seriesid=${sid} order by seriespartnumber`)).toEqual(before);

  // spam persists
  const pid = psql("select postid from blogpost where slug='fxa-post-3'")[0][0];
  psql(`insert into blogcomment(postid,givenon,givenby,email,comment,published,isemailverified,moderationstatus,verifiedon) values (${pid},now(),'fxa Spammer','fxa-spam@example.com','fxa spam candidate',false,true,'PendingApproval',now()) returning commentid`);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/CommentsList'));
  await page.waitForSelector('text=fxa spam candidate', { timeout: 60000 });
  await page.waitForTimeout(1500);
  const row = page.locator('tr', { hasText: 'fxa spam candidate' });
  await row.locator('[data-testid="comment-spam"], button[aria-label*="spam" i]').first().click();
  await page.waitForTimeout(2000);
  const st = psql("select moderationstatus from blogcomment where email='fxa-spam@example.com'")[0][0];
  console.log('spam status', st);
  expect(st).toBe('Spam');
  psql("delete from blogcomment where email='fxa-spam@example.com' returning 1");
});
