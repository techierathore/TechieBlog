import { test, expect } from '@playwright/test';
import { loginHard, open, psql, hOverflow, SHOT } from './bp-a-helpers';
import * as fs from 'fs';

const OUT = `${SHOT}/posts`;
test('superseded by w3-authoring.spec.ts (seed-dependent / psql-writing, not a product verdict): REQ-UI-017 post list + 018 preview + 019 dashboard', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await ctx.newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  page.on('pageerror', (e) => errs.push('PAGEERROR ' + String(e).slice(0, 200)));
  const R: any = {};
  R.landing = await loginHard(page, 'admin');

  // ---- 019 dashboard
  await open(page, '/AdminDashboard', '[data-testid="stat-posts-value"]');
  await page.waitForTimeout(4000);
  R.dash = {
    posts: await page.textContent('[data-testid="stat-posts-value"]'),
    users: await page.textContent('[data-testid="stat-users-value"]'),
    comments: await page.textContent('[data-testid="stat-comments-value"]'),
    subs: await page.textContent('[data-testid="stat-subscribers-value"]'),
    tiles: await page.textContent('[data-testid="dashboard-stats"]'),
    attention: await page.textContent('[data-testid="needs-attention"]').catch(() => 'none'),
    popular: await page.textContent('[data-testid="popular-posts"]').catch(() => 'none'),
    recent: await page.textContent('[data-testid="recent-activity"]').catch(() => 'none'),
  };
  R.dashDb = {
    posts: psql('select count(*) from blogpost where isdeleted=false')[0][0],
    published: psql('select count(*) from blogpost where isdeleted=false and published')[0][0],
    users: psql('select count(*) from bloguser where isdeleted=false')[0][0],
    comments: psql('select count(*) from blogcomment')[0][0],
    pendingComments: psql("select count(*) from blogcomment where not published")[0][0],
    subs: psql('select count(*) from subscriber')[0][0],
    confirmedSubs: psql('select count(*) from subscriber where isconfirmed')[0][0],
  };
  await page.screenshot({ path: `${OUT}/dash-1280.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 800 });
  await page.waitForTimeout(800);
  R.dash390 = await hOverflow(page);
  await page.screenshot({ path: `${OUT}/dash-390.png`, fullPage: true });
  await page.setViewportSize({ width: 1280, height: 800 });

  // ---- 017 list
  await open(page, '/BlogsList', '[data-testid="posts-grid"]');
  const tabs: any = {};
  for (const t of ['all', 'published', 'draft', 'scheduled']) tabs[t] = (await page.textContent(`[data-testid="posts-tab-${t}"]`))?.trim();
  R.tabs = tabs;
  R.postDb = {
    all: psql('select count(*) from blogpost where isdeleted=false')[0][0],
    pub: psql('select count(*) from blogpost where isdeleted=false and published')[0][0],
    draft: psql('select count(*) from blogpost where isdeleted=false and not published and scheduledpublishon is null')[0][0],
    sched: psql('select count(*) from blogpost where isdeleted=false and not published and scheduledpublishon is not null')[0][0],
  };
  R.filters = {};
  for (const t of ['published', 'draft']) {
    await page.click(`[data-testid="posts-tab-${t}"]`);
    await page.waitForTimeout(1500);
    const st = await page.$$eval('[data-testid="post-row-status"]', (n) => n.map((x) => x.textContent!.trim()));
    R.filters[t] = { rows: st.length, distinct: [...new Set(st)], footer: (await page.textContent('[data-testid="posts-count"]'))?.trim() };
  }
  await page.click('[data-testid="posts-tab-all"]');
  await page.waitForTimeout(1000);
  // cells populated
  await page.fill('[data-testid="posts-search"]', 'fxa-');
  await page.waitForTimeout(1500);
  R.search = await page.$$eval('tbody tr', (trs) => trs.map((tr) => ({
    title: tr.querySelector('[data-testid="post-row-title"]')?.textContent?.trim(),
    author: tr.querySelector('[data-testid="post-row-author"]')?.textContent?.trim(),
    status: tr.querySelector('[data-testid="post-row-status"]')?.textContent?.trim(),
    date: tr.querySelector('[data-testid="post-row-date"]')?.textContent?.trim(),
    href: tr.querySelector('[data-testid="post-row-title"]')?.getAttribute('href'),
    editHref: tr.querySelector('[data-testid="post-edit"]')?.getAttribute('href'),
  })));
  await page.screenshot({ path: `${OUT}/list-1280.png`, fullPage: true });
  // delete dialog on fxa-draft row (cancel only)
  const draftRow = page.locator('tbody tr').filter({ hasText: 'fxa-draft' });
  const dTitle = psql("select title from blogpost where slug='fxa-draft'")[0][0];
  await draftRow.locator('[data-testid="post-delete"]').click();
  await page.waitForSelector('[data-testid="post-delete-title"]');
  R.deleteDialogTitle = (await page.textContent('[data-testid="post-delete-title"]'))?.trim();
  R.deleteExpected = dTitle;
  await page.click('[data-testid="post-delete-cancel"]');
  await page.waitForTimeout(800);
  R.draftStillInDb = psql("select count(*) from blogpost where slug='fxa-draft'")[0][0];
  // edit link on a row => nav
  await draftRow.locator('[data-testid="post-edit"]').click();
  await page.waitForTimeout(3000);
  R.editNav = new URL(page.url()).pathname;
  // preview on draft row
  await open(page, '/BlogsList', '[data-testid="posts-grid"]');
  await page.fill('[data-testid="posts-search"]', 'fxa-draft');
  await page.waitForTimeout(6000);
  await page.locator('tbody tr').filter({ hasText: 'fxa-draft' }).locator('[data-testid="post-preview"]').click();
  await page.waitForSelector('[data-testid="preview-article"], [data-testid="preview-post"], [data-testid="preview-not-found"]', { timeout: 60000 });
  await page.waitForTimeout(3000);
  R.previewNav = new URL(page.url()).pathname;
  R.previewTexts = {};
  for (const id of ['preview-title', 'preview-status', 'preview-content', 'preview-metadata', 'preview-banner', 'preview-category', 'preview-tags', 'preview-author', 'preview-edit-post', 'preview-publish-post', 'preview-post-id']) {
    R.previewTexts[id] = ((await page.locator(`[data-testid="${id}"]`).first().textContent({ timeout: 2000 }).catch(() => 'ABSENT')) ?? '').trim().slice(0, 160);
  }
  R.previewBodyStart = (await page.innerText('body')).replace(/\s+/g, ' ').slice(0, 900);
  R.previewHtmlHasH2 = await page.locator('[data-testid="preview-content"] h2, [data-testid="preview-content"] strong, [data-testid="preview-content"] li').count().catch(() => -1);
  await page.screenshot({ path: `${OUT}/preview-1280.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 800 });
  await page.waitForTimeout(800);
  R.preview390 = await hOverflow(page);
  await page.screenshot({ path: `${OUT}/preview-390.png`, fullPage: true });

  // ---- 017 at 390
  await open(page, '/BlogsList', '[data-testid="posts-grid"]');
  R.list390 = await hOverflow(page);
  R.scroller390 = await page.evaluate(() => {
    const s = document.querySelector('[data-testid="posts-status-tabs-scroller"]') as HTMLElement;
    const r = s.getBoundingClientRect();
    return { left: r.left, right: r.right, sw: s.scrollWidth, cw: s.clientWidth, vw: innerWidth };
  });
  await page.screenshot({ path: `${OUT}/list-390.png`, fullPage: true });
  R.errs = errs.slice(0, 10);
  fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
});
