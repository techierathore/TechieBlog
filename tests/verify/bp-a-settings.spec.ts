import { test } from '@playwright/test';
import { loginHard, open, psql, hOverflow, SHOT } from './bp-a-helpers';
import { fillCommitted } from './vall-authoring-helpers';
import * as fs from 'fs';
const OUT = `${SHOT}/settings`;
test.use({ actionTimeout: 25000 });
async function until(page: any, fn: () => Promise<boolean>, ms = 40000) {
  const t = Date.now();
  while (Date.now() - t < ms) { if (await fn().catch(() => false)) return true; await page.waitForTimeout(700); }
  return false;
}
const dbv = (k: string) => psql(`select settingvalue from sitesetting where settingkey='${k}'`)[0]?.[0] ?? '';
test('builder evidence (psql writes, no assertions, superseded by fxv4-admin-settings.spec.ts): settings + theme walkthrough', async ({ browser }) => {
  test.setTimeout(560000);
  fs.mkdirSync(OUT, { recursive: true });
  const R: any = {};
  const page = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const anon = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  const origTag = dbv('General.SiteTagline'), origTheme = dbv('Theme.SiteTheme');
  R.orig = { origTag, origTheme };
  try {
    await loginHard(page, 'admin');
    await open(page, '/settings', '[data-testid="site-title"]');
    await page.waitForTimeout(3000);
    R.fields = { title: await page.inputValue('[data-testid="site-title"]'), tagline: await page.inputValue('[data-testid="site-tagline"]'), email: await page.inputValue('[data-testid="admin-email"]') };
    R.dbFields = { title: dbv('General.SiteTitle'), tagline: origTag, email: dbv('General.AdminEmail') };
    await page.click('[data-testid="tab-blog"]'); await page.waitForTimeout(1500);
    R.blog = { ppp: await page.inputValue('[data-testid="posts-per-page"]'), words: await page.inputValue('[data-testid="pagination-word-count"]'), dbPpp: dbv('Blog.PostsPerPage'), dbWords: dbv('Blog.PaginationWordCount') };
    await page.click('[data-testid="tab-seo"]'); await page.waitForTimeout(1500);
    R.seo = { desc: await page.inputValue('[data-testid="meta-description"]'), dbDesc: dbv('Seo.MetaDescription'), tw: await page.inputValue('[data-testid="twitter-url"]'), dbTw: dbv('Social.TwitterUrl') };
    await page.screenshot({ path: `${OUT}/settings-1280.png`, fullPage: true });
    await page.click('[data-testid="tab-general"]'); await page.waitForTimeout(1000);
    // tagline change
    await fillCommitted(page, 'site-tagline', 'fxa tagline temp');
    await page.click('[data-testid="save-settings"]');
    R.tagSaved = await until(page, async () => dbv('General.SiteTagline') === 'fxa tagline temp');
    R.statusAfterSave = await page.locator('[role="alert"], [data-testid*="status"]').first().textContent({ timeout: 3000 }).catch(() => 'none');
    await page.waitForTimeout(2000);
    await anon.goto(`${process.env.TB_BASE}/`, { waitUntil: 'networkidle', timeout: 90000 });
    await anon.waitForTimeout(2500);
    R.anonTitle = await anon.title(); R.anonShowsTagline = R.anonTitle.includes('fxa tagline temp') || (await anon.content()).includes('fxa tagline temp');
    R.anonTaglineWhere = await anon.evaluate(() => { const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); let n; while ((n = w.nextNode())) if (n.textContent!.includes('fxa tagline temp')) return (n.parentElement as HTMLElement).tagName + '.' + (n.parentElement as HTMLElement).className.slice(0, 40); return 'in head/meta?'; });
    // revert tagline
    await fillCommitted(page, 'site-tagline', origTag);
    await page.click('[data-testid="save-settings"]');
    R.tagReverted = await until(page, async () => dbv('General.SiteTagline') === origTag);
    await page.waitForTimeout(2500);
    // theme
    await page.click('[data-testid="tab-theme"]'); await page.waitForSelector('[data-testid="theme-swatch-developer"]', { timeout: 30000 }); await page.waitForTimeout(1000);
    R.themeOptions = await page.$$eval('[data-testid^="theme-swatch-"]', (n) => n.map((x) => x.getAttribute('data-testid')));
    await page.click('[data-testid="theme-swatch-developer"]'); await page.waitForTimeout(1200);
    await page.click('[data-testid="save-settings"]');
    R.themeSaved = await until(page, async () => dbv('Theme.SiteTheme') === 'developer');
    const anon2 = await (await browser.newContext()).newPage();
    await anon2.goto(`${process.env.TB_BASE}/`, { waitUntil: 'networkidle', timeout: 90000 });
    R.anonThemeAttr = await anon2.evaluate(() => document.documentElement.getAttribute('data-site-theme'));
    await anon2.screenshot({ path: `${OUT}/anon-developer.png` });
    // revert immediately
    await page.click('[data-testid="theme-swatch-trblaze-modern"]'); await page.waitForTimeout(1000);
    await page.click('[data-testid="save-settings"]');
    R.themeReverted = await until(page, async () => dbv('Theme.SiteTheme') === origTheme);
    await anon2.goto(`${process.env.TB_BASE}/`, { waitUntil: 'networkidle', timeout: 90000 });
    R.anonThemeAfterRevert = await anon2.evaluate(() => document.documentElement.getAttribute('data-site-theme'));
    await page.setViewportSize({ width: 390, height: 800 }); await page.waitForTimeout(1200);
    R.s390 = await hOverflow(page);
    await page.screenshot({ path: `${OUT}/settings-390.png`, fullPage: true });
    R.errs = errs.slice(0, 8);
  } catch (e) { R.error = String(e).slice(0, 400); }
  finally {
    R.finalDb = { tag: dbv('General.SiteTagline'), theme: dbv('Theme.SiteTheme') };
    if (R.finalDb.tag !== origTag) psql(`update sitesetting set settingvalue='${origTag.replace(/'/g, "''")}' where settingkey='General.SiteTagline'`);
    if (R.finalDb.theme !== origTheme) psql(`update sitesetting set settingvalue='${origTheme}' where settingkey='Theme.SiteTheme'`);
    fs.writeFileSync(`${OUT}/result.json`, JSON.stringify(R, null, 1));
  }
});
