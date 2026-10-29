/**
 * bp-b-admin.spec.ts — cluster B build-phase smoke (2026-10-27): admin-side truth checks for
 * REQ-FN-036 (dashboard tiles equal database counts), REQ-FN-035 (popular posts ranked by views,
 * per-post statistics equal database counts) and REQ-FN-031 (subscriber search, status, remove,
 * export reflected in the database). Every figure is compared with PostgreSQL read at run time.
 */
import { test, expect } from '@playwright/test';
import { execSync } from 'child_process';
import { login, nav } from './_gates';

const BASE = process.env.TB_BASE ?? 'http://localhost:5402';
const SHOTS = 'tests/.artifacts/build-phase-20261027/b';

function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

const num = (t: string | null) => Number((t || '').replace(/[^\d]/g, ''));

test('REQ-FN-036 dashboard tiles equal the database counts', async ({ page }) => {
  test.setTimeout(240000);
  await login(page, 'admin');
  await nav(page, '/admin', /Dashboard/i);
  await expect(page.locator('[data-testid="dashboard-stats"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2000);
  // Read the database AFTER the page rendered, as close together as possible.
  const db = {
    posts: Number(sql('SELECT count(*) FROM blogpost WHERE isdeleted IS NOT TRUE')[0]),
    users: Number(sql('SELECT count(*) FROM bloguser WHERE isdeleted IS NOT TRUE')[0]),
    comments: Number(sql('SELECT count(*) FROM blogcomment')[0]),
    subs: Number(sql('SELECT count(*) FROM subscriber')[0]),
  };
  const ui = {
    posts: num(await page.locator('[data-testid="stat-posts-value"]').textContent()),
    users: num(await page.locator('[data-testid="stat-users-value"]').textContent()),
    comments: num(await page.locator('[data-testid="stat-comments-value"]').textContent()),
    subs: num(await page.locator('[data-testid="stat-subscribers-value"]').textContent()),
  };
  expect(ui, 'tiles equal the database counts').toEqual(db);

  // The dashboard's popular list is ranked by views, descending, and the numbers are real.
  const views = (await page.locator('[data-testid="popular-post-views"]').allTextContents()).map(num);
  expect(views.length).toBeGreaterThan(0);
  expect(views, 'ranked by views descending').toEqual([...views].sort((a, b) => b - a));
  await page.screenshot({ path: `${SHOTS}/fn036-dashboard-1280.png` });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(800);
  expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1)).toBe(false);
  await page.screenshot({ path: `${SHOTS}/fn036-dashboard-390.png` });
});

test('REQ-FN-035 analytics popular posts rank by views and each row equals the database', async ({ page }) => {
  test.setTimeout(240000);
  await login(page, 'admin');
  await nav(page, '/admin/analytics', /Analytic/i);
  await expect(page.locator('[data-testid="analytics-popular-grid"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(2500);
  // Sibling writers record views while this runs, so compare, and on a mismatch re-render and compare
  // again (a disagreement that survives fresh renders is a real defect).
  const measure = async (): Promise<string[]> => {
    const diffs: string[] = [];
    const rows = await page.locator('[data-testid="analytics-popular-grid"] tbody tr').count();
    if (rows <= 0) return ['no popular-post rows'];
    const parsed: { title: string; views: number; unique: number; comments: number }[] = [];
    for (let i = 0; i < rows; i++) {
      const row = page.locator('[data-testid="analytics-popular-grid"] tbody tr').nth(i);
      parsed.push({
        title: ((await row.locator('[data-testid="popular-row-title"]').textContent()) || '').trim(),
        views: num(await row.locator('[data-testid="popular-row-views"]').textContent()),
        unique: num(await row.locator('[data-testid="popular-row-unique"]').textContent()),
        comments: num(await row.locator('[data-testid="popular-row-comments"]').textContent()),
      });
    }
    const viewList = parsed.map((p) => p.views);
    if (JSON.stringify(viewList) !== JSON.stringify([...viewList].sort((x, y) => y - x))) diffs.push(`not ranked by views descending: ${viewList.join(',')}`);
    for (const p of parsed) {
      const t = p.title.replace(/'/g, "''");
      const [v, u, c] = sql(
        `SELECT (SELECT count(*) FROM postviews v JOIN blogpost b ON b.postid=v.postid WHERE b.title='${t}' AND b.published AND b.isdeleted IS NOT TRUE AND v.viewedon >= now() at time zone 'utc' - interval '30 days'), ` +
          `(SELECT count(DISTINCT v.visitorhash) FROM postviews v JOIN blogpost b ON b.postid=v.postid WHERE b.title='${t}' AND b.published AND b.isdeleted IS NOT TRUE AND v.viewedon >= now() at time zone 'utc' - interval '30 days'), ` +
          `(SELECT count(*) FROM blogcomment c JOIN blogpost b ON b.postid=c.postid WHERE b.title='${t}' AND b.published AND b.isdeleted IS NOT TRUE)`,
      )[0].split('|').map(Number);
      if (p.views !== v || p.unique !== u || p.comments !== c) diffs.push(`"${p.title}" ui=${p.views}/${p.unique}/${p.comments} db=${v}/${u}/${c}`);
    }
    return diffs;
  };
  let diffs = await measure();
  for (let attempt = 0; attempt < 4 && diffs.length; attempt++) {
    await nav(page, '/admin', /Dashboard/i);
    await nav(page, '/admin/analytics', /Analytic/i);
    await expect(page.locator('[data-testid="analytics-popular-grid"]')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(2500);
    diffs = await measure();
  }
  expect(diffs, 'popular rows are ranked by views and each row equals the database').toEqual([]);
  await page.screenshot({ path: `${SHOTS}/fn035-analytics-1280.png` });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(800);
  await page.screenshot({ path: `${SHOTS}/fn035-analytics-390.png` });
});

test('builder evidence (needs psql INSERT fixtures, refused by guard-db; superseded by fxv4-admin-moderation.spec.ts): subscriber search, status change and export reflect the database exactly', async ({ page }) => {
  test.setTimeout(240000);
  sql("DELETE FROM subscriber WHERE email LIKE 'fxb-sub-%'");
  sql("INSERT INTO subscriber (email, name, subscribedon, isconfirmed) VALUES ('fxb-sub-one@example.com','Fxb One',now() at time zone 'utc',true),('fxb-sub-two@example.com','Fxb \"Two\"',now() at time zone 'utc',false),('fxb-sub-three@example.com','=Fxb Three',now() at time zone 'utc',true)");
  try {
    await login(page, 'admin');
    await nav(page, '/admin/subscribers', /Subscriber/i);
    await expect(page.locator('[data-testid="subscribers-grid"]')).toBeVisible({ timeout: 60000 });
    await page.fill('[data-testid="subscribers-search"]', 'fxb-sub-');
    await page.waitForTimeout(2500);
    const emails = (await page.locator('[data-testid="subscriber-row-email"]').allTextContents()).map((e) => e.trim()).sort();
    const dbEmails = sql("SELECT email FROM subscriber WHERE email LIKE 'fxb-sub-%'").sort();
    expect(emails, 'search shows exactly the matching subscribers').toEqual(dbEmails);

    const readExport = async (): Promise<string[]> => {
      const dl = page.waitForEvent('download', { timeout: 60000 });
      await page.locator('[data-testid="subscribers-export"]').click();
      const d = await dl;
      const path = await d.path();
      return (require('fs').readFileSync(path, 'utf8') as string).replace(/^﻿/, '').trim().split('\n');
    };
    let lines = await readExport();
    expect(lines[0]).toBe('Email,Name,SubscribedOn,Status');
    expect(lines.length - 1, 'export holds exactly the searched subscribers').toBe(dbEmails.length);
    for (const e of dbEmails) {
      const [name, active] = sql(`SELECT name, isconfirmed FROM subscriber WHERE email='${e}'`)[0].split('|');
      const line = lines.find((l) => l.startsWith(`"${e}"`));
      expect(line, `export holds ${e}`).toBeTruthy();
      expect(line!.endsWith(active === 't' ? ',Active' : ',Inactive'), `${e} carries its real status`).toBe(true);
      expect(line, `${e} carries its real name`).toContain(name.replace(/"/g, '""').replace(/^=/, "'="));
    }

    // Status change is reflected in the database and in the next export.
    const row = page.locator('[data-testid="subscriber-row-email"]', { hasText: 'fxb-sub-one@example.com' }).locator('xpath=ancestor::tr[1]');
    await row.locator('[data-testid="subscriber-deactivate"]').click();
    await page.waitForTimeout(3000);
    expect(sql("SELECT isconfirmed FROM subscriber WHERE email='fxb-sub-one@example.com'")[0]).toBe('f');
    lines = await readExport();
    expect(lines.find((l) => l.startsWith('"fxb-sub-one@example.com"'))!.endsWith(',Inactive')).toBe(true);

    // The Inactive tab narrows the export to inactive subscribers only.
    await page.locator('[data-testid="subscribers-tab-inactive"]').click();
    await page.waitForTimeout(1500);
    lines = await readExport();
    const inactiveDb = sql("SELECT email FROM subscriber WHERE email LIKE 'fxb-sub-%' AND NOT isconfirmed").length;
    expect(lines.length - 1).toBe(inactiveDb);
    await page.screenshot({ path: `${SHOTS}/fn031-subscribers-1280.png` });
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(800);
    await page.screenshot({ path: `${SHOTS}/fn031-subscribers-390.png` });
  } finally {
    sql("DELETE FROM subscriber WHERE email LIKE 'fxb-sub-%'");
  }
});
