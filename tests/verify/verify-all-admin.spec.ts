/**
 * verify-all-admin.spec.ts — verify-phase §4 / §4a / §4b, ADMIN surface, cluster V2 (2026-08-11).
 *
 * Re-verification run after the TrBlazeUI 2.0.1 → 2.0.2 upgrade. The upgrade DELETED the app-side
 * `SelectFirstPaintLabel` workaround, so every `<Select>` on this surface now depends on the
 * library resolving a pre-selected value to its item text ON FIRST PAINT. That is the single
 * highest-risk regression surface here and every trigger label is read BEFORE any click.
 *
 * Gates encoded here:
 *   §4  ACCEPTANCE — the observable outcome each REQ promises, asserted against psql.
 *   §4a RENDER     — grids need rows > 0 AND non-empty data cells; a count badge over zero rows is
 *                    a FAIL. Verdicts: RENDERS / RENDER-EMPTY / RENDER-ERROR / UNREACHABLE.
 *   §4b VISUAL     — 1280x800 and 390x844: no intersecting sibling controls, every control
 *                    w>0/h>0 in bounds, no page-level horizontal scroll, full-page screenshot at
 *                    each width.
 *
 * READ-ONLY. Three sibling verifier clusters share this host and this database. Nothing here
 * INSERTs, UPDATEs or DELETEs; dialogs are opened and cancelled, no file is ever uploaded.
 * Nothing under source/** is edited.
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { execSync } from 'child_process';
import { renderCheck, visualCheck, ControlResult } from './_gates';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5450';
const OUT = 'tests/.artifacts/verify-admin';
fs.mkdirSync(OUT, { recursive: true });

const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

// =====================================================================================
// psql — SELECT only, read live (the database moves under the run; siblings are writing)
// =====================================================================================
const oneLine = (sql: string) => sql.replace(/\s+/g, ' ').trim();

function psql(sql: string): string {
  const cmd = `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -c ${JSON.stringify(oneLine(sql))}`;
  return execSync(cmd, { encoding: 'utf8' }).split('\n')[0].trim();
}
const psqlInt = (sql: string) => Number(psql(sql));

function psqlRows(sql: string): string[][] {
  const cmd = `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '\t' -c ${JSON.stringify(oneLine(sql))}`;
  return execSync(cmd, { encoding: 'utf8' })
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)
    .map((l) => l.split('\t'));
}

const LIVE = '(IsDeleted = FALSE OR IsDeleted IS NULL)';

// =====================================================================================
// evidence accumulation
// =====================================================================================
interface ReqEvidence {
  req: string;
  controls: ControlResult[];
  visual: any[];
  notes: string[];
}
const evidence: Record<string, ReqEvidence> = {};

function ev(req: string): ReqEvidence {
  evidence[req] ??= { req, controls: [], visual: [], notes: [] };
  return evidence[req];
}

/** §4a — record a control's render verdict and fail the test on anything but RENDERS. */
async function mustRender(
  page: Page,
  req: string,
  control: string,
  selector: string,
  kind: 'table' | 'value' | 'chart' | 'present' = 'value',
) {
  const r = await renderCheck(page, control, selector, kind);
  ev(req).controls.push(r);
  expect(`${control}: ${r.verdict} (${r.detail})`).toContain('RENDERS');
  return r;
}

/**
 * §4b — geometry at both widths + a FULL-PAGE screenshot at each for eyes-on review.
 *
 * An element sitting inside a deliberate `overflow-x:auto` scroller is NOT off-viewport: admin
 * data tables and tab strips legitimately scroll sideways at 390. Those names are subtracted from
 * the failure set and recorded separately so the evidence still shows them.
 */
async function mustLookRight(page: Page, req: string, slug: string) {
  for (const w of [1280, 390]) {
    const shot = `${OUT}/${slug}-${w}.png`;
    const v = await visualCheck(page, shot, w);
    await page.screenshot({ path: `${OUT}/${slug}-${w}-full.png`, fullPage: true });

    const rawNames = v.offViewport.map((s) => s.split('@')[0]);
    const inScroller: string[] = await page.evaluate((names: string[]) => {
      const hasScroller = (e: Element | null) => {
        let n: Element | null = e;
        while (n) {
          const s = getComputedStyle(n);
          if (s.overflowX === 'auto' || s.overflowX === 'scroll') return true;
          n = n.parentElement;
        }
        return false;
      };
      const out: string[] = [];
      for (const nm of names) {
        const el = document.querySelector(`[data-testid="${CSS.escape(nm)}"]`);
        if (el && hasScroller(el.parentElement)) out.push(nm);
      }
      return out;
    }, rawNames);

    const realOff = v.offViewport.filter((s) => !inScroller.includes(s.split('@')[0]));
    ev(req).visual.push({ ...v, offViewportInScroller: inScroller, offViewportReal: realOff, fullPage: `${OUT}/${slug}-${w}-full.png` });
    expect(`${slug}@${w} zeroSized=${JSON.stringify(v.zeroSized)}`).toContain('zeroSized=[]');
    expect(`${slug}@${w} overlaps=${JSON.stringify(v.overlaps)}`).toContain('overlaps=[]');
    expect(`${slug}@${w} offViewport=${JSON.stringify(realOff)}`).toContain('offViewport=[]');
    expect(`${slug}@${w} documentHScroll=${v.hScroll}`).toContain('documentHScroll=0');
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.waitForTimeout(600);
}

/** Reads a Select/combobox trigger's rendered label WITHOUT opening it (first-paint truth). */
async function triggerLabel(page: Page, testid: string): Promise<string> {
  const el = page.locator(`[data-testid="${testid}"]`).first();
  await expect(el).toBeVisible({ timeout: 45000 });
  return ((await el.textContent()) || '').replace(/\s+/g, ' ').trim();
}

// =====================================================================================
// session — one circuit for the whole file; the host is slow under Serilog Debug logging
// =====================================================================================
let page: Page;
let landingUrl = '';

test.beforeAll(async ({ browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  page = await ctx.newPage();
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
  // A prerendered EditForm keeps `action`; typing before the circuit attaches is discarded and the
  // static POST returns HTTP 400. Wait for the attribute to go, then re-type until the value holds.
  await page
    .waitForFunction(
      () => {
        const f = document.querySelector('form');
        return !!f && !f.hasAttribute('action');
      },
      { timeout: 90000 },
    )
    .catch(() => {});
  const fillStable = async (sel: string, v: string) => {
    for (let i = 0; i < 15; i++) {
      await page.fill(sel, v);
      await page.waitForTimeout(500);
      if ((await page.inputValue(sel)) === v) return;
    }
    throw new Error(`${sel} would not hold its value — circuit never attached`);
  };
  await fillStable('[data-testid="login-email"]', ADMIN.email);
  await fillStable('[data-testid="login-password"]', ADMIN.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 90000 });
  await page.waitForTimeout(2500);
  landingUrl = page.url();
});

test.afterAll(async () => {
  fs.writeFileSync(`${OUT}/evidence.json`, JSON.stringify(evidence, null, 2));
});

/**
 * One circuit is shared by the whole file, so a test that ends with a dialog on screen would
 * poison the next test's geometry gate. Every test starts from a clean 1280 viewport with no
 * overlay open. Dialogs are only ever CANCELLED — nothing is saved.
 */
test.beforeEach(async () => {
  if (!page) return;
  await page.setViewportSize({ width: 1280, height: 900 });
  await closeAnyDialog();
});

async function closeAnyDialog() {
  for (let i = 0; i < 6; i++) {
    if ((await page.locator('[role="dialog"]').count()) === 0) return;
    const cancel = page.locator('[role="dialog"] button', { hasText: /^(Cancel|Close)$/i }).first();
    if (await cancel.count()) await cancel.click({ force: true }).catch(() => {});
    else await page.keyboard.press('Escape');
    await page.waitForTimeout(1200);
  }
  expect(`openDialogs=${await page.locator('[role="dialog"]').count()}`).toBe('openDialogs=0');
}

/**
 * Authenticated navigation. A full page load prerenders as anonymous (the JWT lives in
 * localStorage) and bounces to /login, so every hop goes through Blazor.navigateTo; the URL
 * changes before the destination paints, so each hop is gated on the destination's own heading.
 */
async function go(route: string, heading: RegExp | string) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  // A string is a CSS marker for pages with no heading (ManagePost lost its <h1> in the UAT-029 rebuild).
  const marker = typeof heading === 'string'
    ? page.locator(heading).first()
    : page.locator('h1, h2').filter({ hasText: heading }).first();
  await expect(marker).toBeVisible({ timeout: 60000 });
  await page
    .waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), { timeout: 30000 })
    .catch(() => {});
  await page.waitForTimeout(1200);
}

// =====================================================================================
// REQ-UI-034 — Media library page with category tabs
// =====================================================================================
test('REQ-UI-034 media library: category tabs, gallery, per-card actions, user filter label', async () => {
  test.setTimeout(300000);
  const REQ = 'REQ-UI-034';
  await go('/admin/images', /Media Library/i);

  // THE REGRESSION UNDER TEST: read the trigger BEFORE any click. 2.0.1 echoed the raw value "0".
  const filterLabel = await triggerLabel(page, 'user-filter-select');
  ev(REQ).notes.push(`user-filter-select first-paint label = "${filterLabel}"`);
  expect(filterLabel).toBe('All Users');
  expect(filterLabel).not.toBe('0');

  await mustRender(page, REQ, 'media-library-page', '[data-testid="media-library-page"]', 'present');
  await mustRender(page, REQ, 'upload-image', '[data-testid="upload-image"]');
  await mustRender(page, REQ, 'user-filter-select', '[data-testid="user-filter-select"]');
  await mustRender(page, REQ, 'image-count', '[data-testid="image-count"]');

  // Category tabs: 7 of them, each with a label, each a real tab control after the testid move.
  const tabs = page.locator('[data-testid="category-tabs"] [role="tab"]');
  const tabCount = await tabs.count();
  const tabTexts = (await tabs.allTextContents()).map((t) => t.trim());
  ev(REQ).notes.push(`category tabs: ${tabCount} → ${JSON.stringify(tabTexts)}`);
  expect(tabCount).toBe(7);
  expect(tabTexts).toEqual(['Profiles', 'Logos', 'Awards', 'Icons', 'Blog', 'CV', 'General']);
  ev(REQ).controls.push({ control: 'category-tabs', verdict: 'RENDERS', detail: tabTexts.join(',') });

  // §4a gallery: the selected category is Profiles; psql says how many rows must be there.
  const dbProfiles = psqlInt("SELECT COUNT(*) FROM BlogImage WHERE Category = 'profiles'");
  const cards = page.locator('[data-testid="image-card"]');
  await expect(cards).toHaveCount(dbProfiles, { timeout: 45000 });
  const count = ((await page.locator('[data-testid="image-count"]').textContent()) || '').trim();
  ev(REQ).notes.push(`profiles cards=${await cards.count()} psql=${dbProfiles} badge="${count}"`);
  expect(count).toContain(`of ${dbProfiles} image`);

  // Non-empty data cells, not just rows.
  for (let i = 0; i < dbProfiles; i++) {
    const name = ((await cards.nth(i).locator('[data-testid="image-name"]').textContent()) || '').trim();
    const size = ((await cards.nth(i).locator('[data-testid="image-size"]').textContent()) || '').trim();
    expect(name.length).toBeGreaterThan(0);
    expect(size.length).toBeGreaterThan(0);
    await expect(cards.nth(i).locator('[data-testid="copy-image-url"]')).toBeVisible();
    await expect(cards.nth(i).locator('[data-testid="delete-image"]')).toBeVisible();
  }
  ev(REQ).controls.push({
    control: 'image-grid',
    verdict: 'RENDERS',
    detail: `${dbProfiles} cards, every card has a non-empty name+size and copy/delete`,
  });

  // Thumbnail truth: does the <img> each card points at actually load? Recorded, not asserted —
  // the rows currently in blogimage were left behind by another cluster's run, so a 404 here is a
  // stale-DATA condition to report, not proof the page is broken.
  const thumbs = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid="image-card"] img')).map((i) => ({
      src: (i as HTMLImageElement).getAttribute('src'),
      naturalWidth: (i as HTMLImageElement).naturalWidth,
      complete: (i as HTMLImageElement).complete,
    })),
  );
  const broken = thumbs.filter((t) => t.naturalWidth === 0);
  ev(REQ).notes.push(`thumbnails: ${thumbs.length} <img>, ${broken.length} failed to load → ${JSON.stringify(broken)}`);
  fs.writeFileSync(`${OUT}/ui034-thumbnails.json`, JSON.stringify(thumbs, null, 2));

  // Switching to an EMPTY category must show an empty state, not a stale grid or a lying badge.
  await tabs.nth(4).click(); // Blog
  await page.waitForTimeout(2500);
  const dbBlog = psqlInt("SELECT COUNT(*) FROM BlogImage WHERE Category = 'blog'");
  const blogCards = await page.locator('[data-testid="image-card"]').count();
  ev(REQ).notes.push(`blog tab cards=${blogCards} psql=${dbBlog}`);
  expect(blogCards).toBe(dbBlog);
  await tabs.nth(0).click();
  await page.waitForTimeout(2500);

  // The user filter must OPEN and offer real owners (page-level Select, outside any dialog).
  await page.locator('[data-testid="user-filter-select"]').click();
  await page.waitForTimeout(2000);
  const options = await page.locator('[role="option"]').allTextContents();
  ev(REQ).notes.push(`user-filter-select options: ${JSON.stringify(options.map((o) => o.trim()))}`);
  expect(options.length).toBeGreaterThan(1);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1500);
  expect(await triggerLabel(page, 'user-filter-select')).toBe('All Users');

  await mustLookRight(page, REQ, 'ui034-images');
});

// =====================================================================================
// REQ-UI-038 — Manage skills
// =====================================================================================
test('REQ-UI-038 manage skills: 5 categories / 13 skills vs psql, user select label, per-row actions', async () => {
  test.setTimeout(300000);
  const REQ = 'REQ-UI-038';
  await go('/admin/skills', /Manage Skills/i);

  const label = await triggerLabel(page, 'skills-user-select');
  ev(REQ).notes.push(`skills-user-select first-paint label = "${label}"`);
  expect(label).not.toBe('1');
  expect(label).toContain('S Ravi Kumar');

  await mustRender(page, REQ, 'manage-skills-page', '[data-testid="manage-skills-page"]', 'present');
  await mustRender(page, REQ, 'add-skill', '[data-testid="add-skill"]');
  await mustRender(page, REQ, 'skills-user-select', '[data-testid="skills-user-select"]');
  await mustRender(page, REQ, 'skills-list', '[data-testid="skills-list"]', 'present');

  // Ground truth: category grouping and per-category counts, straight from psql.
  const dbGroups = psqlRows('SELECT Category, COUNT(*) FROM UserSkills WHERE UserId = 1 GROUP BY Category ORDER BY Category');
  const dbTotal = psqlInt('SELECT COUNT(*) FROM UserSkills WHERE UserId = 1');

  const cards = page.locator('[data-testid="skill-category-card"]');
  await expect(cards).toHaveCount(dbGroups.length, { timeout: 45000 });

  const uiGroups: string[][] = [];
  for (let i = 0; i < dbGroups.length; i++) {
    const name = ((await cards.nth(i).locator('[data-testid="skill-category-name"]').textContent()) || '').trim();
    const badge = ((await cards.nth(i).locator('[data-testid="skill-category-count"]').textContent()) || '').trim();
    const rows = await cards.nth(i).locator('[data-testid="skill-row"]').count();
    // The badge must not be a lie: the visible row count has to equal it.
    expect(rows).toBe(Number(badge));
    uiGroups.push([name, badge]);
  }
  ev(REQ).notes.push(`ui groups ${JSON.stringify(uiGroups)} vs psql ${JSON.stringify(dbGroups)}`);
  expect(uiGroups).toEqual(dbGroups);

  const allRows = page.locator('[data-testid="skill-row"]');
  expect(await allRows.count()).toBe(dbTotal);
  const names = (await page.locator('[data-testid="skill-name"]').allTextContents()).map((s) => s.trim());
  expect(names.filter((n) => n.length > 0).length).toBe(dbTotal);
  const dbNames = psqlRows('SELECT SkillName FROM UserSkills WHERE UserId = 1 ORDER BY SkillName').map((r) => r[0]);
  expect([...names].sort()).toEqual([...dbNames].sort());
  ev(REQ).controls.push({ control: 'skills-list', verdict: 'RENDERS', detail: `${dbTotal} skills in ${dbGroups.length} categories, names match psql` });

  // Per-row actions must all be present on the first row.
  for (const t of ['edit-skill', 'delete-skill', 'move-skill-up', 'move-skill-down']) {
    await expect(allRows.first().locator(`[data-testid="${t}"]`)).toBeVisible();
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: 'present on skill-row[0]' });
  }

  // Page-level Select opens with real options (contrast with the dialog Select below — TR-067).
  await page.locator('[data-testid="skills-user-select"]').click();
  await page.waitForTimeout(2000);
  const pageOptions = await page.locator('[role="option"]').count();
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1500);
  ev(REQ).notes.push(`page-level skills-user-select opened with ${pageOptions} [role=option]`);
  expect(pageOptions).toBeGreaterThan(0);
  expect(await triggerLabel(page, 'skills-user-select')).toContain('S Ravi Kumar');

  // Geometry is measured on the PAGE, before any overlay is opened.
  await mustLookRight(page, REQ, 'ui038-skills');

  // TR-067 (popover Select inside DialogContent rendered zero options on 2.0.2) is fixed in
  // TrBlazeUI 2.0.3 — the dialog Select must now open with options. Dialog is CANCELLED, nothing written.
  await page.locator('[data-testid="add-skill"]').click();
  await page.waitForTimeout(3000);
  const dlg = page.locator('[role="dialog"]').first();
  await expect(dlg).toBeVisible({ timeout: 30000 });
  const dlgCombo = dlg.locator('[role="combobox"]').first();
  let dlgOptions = -1;
  if (await dlgCombo.count()) {
    await dlgCombo.click();
    await page.waitForTimeout(2500);
    dlgOptions = await page.locator('[role="option"]').count();
    await page.keyboard.press('Escape');
    await page.waitForTimeout(1200);
  }
  ev(REQ).notes.push(`TR-067 check — add-skill dialog Select rendered ${dlgOptions} [role=option] (page-level Select on the same circuit: ${pageOptions})`);
  expect(dlgOptions).toBeGreaterThan(0);
  await closeAnyDialog();
});

// =====================================================================================
// REQ-UI-039 — Manage awards
// =====================================================================================
test('REQ-UI-039 manage awards: 3 cards vs psql with title/year/description, user select label, ordering', async () => {
  test.setTimeout(300000);
  const REQ = 'REQ-UI-039';
  await go('/admin/awards', /Manage Awards/i);

  const label = await triggerLabel(page, 'awards-user-select');
  ev(REQ).notes.push(`awards-user-select first-paint label = "${label}"`);
  expect(label).not.toBe('1');
  expect(label).toContain('S Ravi Kumar');

  await mustRender(page, REQ, 'manage-awards-page', '[data-testid="manage-awards-page"]', 'present');
  await mustRender(page, REQ, 'add-award', '[data-testid="add-award"]');
  await mustRender(page, REQ, 'awards-user-select', '[data-testid="awards-user-select"]');

  const dbAwards = psqlInt('SELECT COUNT(*) FROM UserAwards WHERE UserId = 1');
  const dbTitles = psqlRows('SELECT AwardTitle FROM UserAwards WHERE UserId = 1 ORDER BY DisplayOrder').map((r) => r[0]);
  const cards = page.locator('[data-testid="award-card"]');
  await expect(cards).toHaveCount(dbAwards, { timeout: 45000 });

  const uiTitles: string[] = [];
  for (let i = 0; i < dbAwards; i++) {
    const t = ((await cards.nth(i).locator('[data-testid="award-title"]').textContent()) || '').replace(/\s+/g, ' ').trim();
    const y = ((await cards.nth(i).locator('[data-testid="award-year"]').textContent()) || '').trim();
    const d = ((await cards.nth(i).locator('[data-testid="award-description"]').textContent()) || '').trim();
    expect(t.length).toBeGreaterThan(0);
    expect(y).toMatch(/\d{4}/);
    expect(d.length).toBeGreaterThan(0);
    uiTitles.push(t);
    for (const a of ['edit-award', 'delete-award', 'move-award-up', 'move-award-down']) {
      await expect(cards.nth(i).locator(`[data-testid="${a}"]`)).toBeVisible();
    }
  }
  ev(REQ).notes.push(`ui titles ${JSON.stringify(uiTitles)} vs psql ${JSON.stringify(dbTitles)}`);
  // The card title element also carries the badge/year markup, so match by containment.
  for (let i = 0; i < dbAwards; i++) expect(uiTitles[i]).toContain(dbTitles[i]);
  ev(REQ).controls.push({ control: 'awards-list', verdict: 'RENDERS', detail: `${dbAwards} cards, title/year/description non-empty, order matches psql DisplayOrder` });

  // Ordering controls exist and are enabled; the WRITE half is not exercised — read-only run.
  ev(REQ).notes.push('move-award-up / move-award-down render and are enabled; the reorder WRITE is NOT-OBSERVABLE (read-only verify run)');

  await page.locator('[data-testid="awards-user-select"]').click();
  await page.waitForTimeout(2000);
  const opts = await page.locator('[role="option"]').count();
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1500);
  ev(REQ).notes.push(`awards-user-select opened with ${opts} [role=option]`);
  expect(opts).toBeGreaterThan(0);
  expect(await triggerLabel(page, 'awards-user-select')).toContain('S Ravi Kumar');

  await mustLookRight(page, REQ, 'ui039-awards');
});

// =====================================================================================
// REQ-FN-025 — Image upload service, per-category validation (ADVERTISED limits only, no upload)
// =====================================================================================
test('REQ-FN-025 upload dialog advertises ONE limit per category: caption == dropzone == accept filter', async () => {
  test.setTimeout(300000);
  const REQ = 'REQ-FN-025';
  await go('/admin/images', /Media Library/i);
  await page.locator('[data-testid="upload-image"]').click();
  await page.waitForTimeout(3500);
  const dlg = page.locator('[role="dialog"]').first();
  await expect(dlg).toBeVisible({ timeout: 30000 });

  // TrBlazeUI 2.0.3 fixed TR-067 (a Select inside DialogContent rendered zero options), so the
  // category picker is the styled popover Select again — confirm the NativeSelect workaround is gone.
  const nativeSelects = await dlg.locator('select').count();
  const nativeOuter = await dlg.evaluate((d) => Array.from(d.querySelectorAll('select')).map((s) => s.outerHTML.slice(0, 300)));
  const styledTrigger = dlg.locator('[data-testid="upload-category-select"][role="combobox"]');
  ev(REQ).notes.push(`upload dialog category picker: ${nativeSelects} native <select> ${JSON.stringify(nativeOuter)}, styled trigger present=${await styledTrigger.count()}`);
  expect(nativeSelects).toBe(0);
  await expect(styledTrigger).toBeVisible({ timeout: 30000 });
  const categoryLabels: Record<string, string> = {
    profiles: 'Profiles', logos: 'Logos', awards: 'Awards', icons: 'Icons', blog: 'Blog', cv: 'CV', general: 'General',
  };

  const expected: Record<string, { size: string; accept: string }> = {
    profiles: { size: '2 MB', accept: 'image/jpeg,image/png,image/webp' },
    logos: { size: '500 KB', accept: 'image/jpeg,image/png,image/svg+xml,image/webp' },
    awards: { size: '500 KB', accept: 'image/jpeg,image/png,image/svg+xml,image/webp' },
    icons: { size: '200 KB', accept: 'image/png,image/svg+xml,image/webp' },
    blog: { size: '5 MB', accept: 'image/jpeg,image/png,image/gif,image/webp' },
    cv: { size: '10 MB', accept: 'application/pdf' },
    general: { size: '5 MB', accept: 'image/jpeg,image/png,image/gif,image/webp' },
  };

  const readAdvertised = async () =>
    dlg.evaluate((d) => {
      const text = (d as HTMLElement).innerText.replace(/\s+/g, ' ');
      const caption = text.match(/Max ([\d.]+ ?(?:KB|MB)), formats: ([^|]*?) Drag/i);
      const dropzone = text.match(/Max size: ([\d.]+ ?(?:KB|MB))/i);
      const accepted = text.match(/Accepted: ([^ ]+)/i);
      return {
        captionSize: caption?.[1]?.trim() ?? null,
        captionFormats: caption?.[2]?.trim() ?? null,
        dropzoneSize: dropzone?.[1]?.trim() ?? null,
        advertisedAccept: accepted?.[1]?.trim() ?? null,
        inputAccept: (d.querySelector('input[type=file]') as HTMLInputElement)?.accept ?? null,
        selectLabel: (d.querySelector('[data-testid="upload-category-select"]') as HTMLElement)?.innerText.trim() ?? null,
        raw: text.slice(0, 400),
      };
    });

  const table: any[] = [];
  const seenSizes = new Set<string>();
  for (const cat of Object.keys(expected)) {
    await styledTrigger.click();
    await page.waitForTimeout(1000);
    const option = page.locator('[role="option"]', { hasText: new RegExp(`^${categoryLabels[cat]}$`) });
    await expect(option).toBeVisible({ timeout: 15000 });
    await option.click();
    await page.waitForTimeout(2200);
    const a = await readAdvertised();
    table.push({ category: cat, ...a });
    expect(a.selectLabel).toBe(categoryLabels[cat]);
    // THE GRADED CLAIM: one limit, stated identically in both places, matching the accept filter.
    expect(`${cat} caption=${a.captionSize}`).toBe(`${cat} caption=${expected[cat].size}`);
    expect(`${cat} dropzone=${a.dropzoneSize}`).toBe(`${cat} dropzone=${expected[cat].size}`);
    expect(a.captionSize).toBe(a.dropzoneSize);
    expect(a.inputAccept).toBe(expected[cat].accept);
    expect(a.advertisedAccept).toBe(expected[cat].accept);
    seenSizes.add(a.captionSize!);
  }
  // The limit must actually CHANGE with the category, not be one constant repeated seven times.
  expect(seenSizes.size).toBeGreaterThan(1);
  fs.writeFileSync(`${OUT}/fn025-advertised-limits.json`, JSON.stringify(table, null, 2));
  ev(REQ).notes.push(`7/7 categories: caption == dropzone == accept, ${seenSizes.size} distinct ceilings ${JSON.stringify([...seenSizes])}`);
  ev(REQ).controls.push({ control: 'upload-dialog-limits', verdict: 'RENDERS', detail: JSON.stringify(table.map((t) => `${t.category}:${t.captionSize}`)) });
  ev(REQ).notes.push('The ENFORCEMENT half (server-side rejection of an oversize/wrong-format file) is NOT-OBSERVABLE — this is a read-only run and rule 3 forbids the upload.');

  // Dialog geometry at both widths, then CANCEL — nothing is uploaded, nothing is written.
  for (const w of [1280, 390]) {
    await page.setViewportSize({ width: w, height: w < 500 ? 844 : 900 });
    await page.waitForTimeout(1200);
    const shot = `${OUT}/fn025-upload-dialog-${w}.png`;
    await page.screenshot({ path: shot, fullPage: false });
    const geo = await dlg.evaluate((d) => {
      const boxes = Array.from(d.querySelectorAll('[data-testid], button, select, input'))
        .map((e) => {
          const r = e.getBoundingClientRect();
          return { name: e.getAttribute('data-testid') || e.tagName.toLowerCase(), w: r.width, h: r.height, x: r.left, right: r.right };
        })
        .filter((b) => getComputedStyle(document.querySelector('[role=dialog]')!).display !== 'none');
      const r = d.getBoundingClientRect();
      return {
        dialog: { w: r.width, h: r.height, x: r.left, right: r.right },
        zeroSized: boxes.filter((b) => (b.w <= 0 || b.h <= 0) && b.name !== 'input').map((b) => b.name),
        overflowing: boxes.filter((b) => b.right > document.documentElement.clientWidth + 2).map((b) => b.name),
      };
    });
    ev(REQ).visual.push({ width: w, shot, ...geo });
    expect(`dialog@${w} w=${geo.dialog.w} h=${geo.dialog.h}`).not.toContain('w=0');
    expect(`dialog@${w} zeroSized=${JSON.stringify(geo.zeroSized)}`).toContain('zeroSized=[]');
    expect(`dialog@${w} overflowing=${JSON.stringify(geo.overflowing)}`).toContain('overflowing=[]');
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  // Measured, not assumed: after the styled Select has been used inside the dialog, does Escape
  // still close the dialog? Record each step; the graded claim is the limits above, but a dialog
  // that Escape cannot dismiss is worth a dated remark.
  const focusedBefore = await page.evaluate(() => (document.activeElement as HTMLElement)?.getAttribute('data-testid') ?? document.activeElement?.tagName ?? 'none');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1500);
  const afterFirstEscape = await page.locator('[role="dialog"]').count();
  if (afterFirstEscape) { await page.keyboard.press('Escape'); await page.waitForTimeout(1500); }
  const afterSecondEscape = await page.locator('[role="dialog"]').count();
  if (afterSecondEscape) {
    // Dismiss by coordinates: a locator click stalls in Playwright's actionability wait here even
    // though a real pointer click lands (measured 2026-08-26); the mouse path is what a user has.
    const c = await page.evaluate(() => { const b = Array.from(document.querySelectorAll('[role="dialog"] button')).find((x) => /^\s*Cancel\s*$/.test(x.textContent || '')); const r = b?.getBoundingClientRect(); return r ? { x: r.x + r.width / 2, y: r.y + r.height / 2 } : null; });
    if (c) { await page.mouse.click(c.x, c.y); await page.waitForTimeout(1500); }
  }
  const afterCancel = await page.locator('[role="dialog"]').count();
  ev(REQ).notes.push(`dismiss after Select use: focus=${focusedBefore}, open after Escape#1=${afterFirstEscape}, after Escape#2=${afterSecondEscape}, after Cancel=${afterCancel}`);
  expect(afterCancel).toBe(0);
});

// =====================================================================================
// Fast re-confirm of the currently-`Verified` admin rows — the 2.0.2 upgrade touched every one
// =====================================================================================
test('REQ-UI-047 admin layout: grouped sidebar navigation, topbar, landing URL', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-047';
  expect(landingUrl).toBe(`${BASE}/admin`);
  ev(REQ).notes.push(`login landed on ${landingUrl}`);
  await go('/admin', /Dashboard/i);
  await mustRender(page, REQ, 'admin-sidebar', '[data-testid="admin-sidebar"]', 'present');
  await mustRender(page, REQ, 'admin-topbar', '[data-testid="admin-topbar"]', 'present');
  const navIds = ['nav-dashboard', 'nav-posts', 'nav-series', 'nav-comments', 'nav-categories', 'nav-tags', 'nav-images', 'nav-profile', 'nav-experience', 'nav-skills', 'nav-awards', 'nav-stats', 'nav-users', 'nav-subscribers', 'nav-newsletter', 'nav-analytics', 'nav-settings'];
  for (const n of navIds) await mustRender(page, REQ, n, `[data-testid="${n}"]`);
  ev(REQ).notes.push(`${navIds.length} grouped nav links all render with text`);
  await mustRender(page, REQ, 'theme-toggle', '[data-testid="theme-toggle"]', 'present');
  await mustRender(page, REQ, 'account-menu-trigger', '[data-testid="account-menu-trigger"]');
});

test('REQ-UI-019 admin dashboard: stat tiles vs psql, quick actions, needs-attention, ItemGroup activity', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-019';
  await go('/admin', /Dashboard/i);

  const dbPosts = psqlInt(`SELECT COUNT(*) FROM BlogPost WHERE ${LIVE}`);
  const dbUsers = psqlInt('SELECT COUNT(*) FROM BlogUser');
  const dbComments = psqlInt('SELECT COUNT(*) FROM BlogComment');
  const dbSubs = psqlInt('SELECT COUNT(*) FROM Subscriber');

  const val = async (id: string) => Number(((await page.locator(`[data-testid="${id}"]`).textContent()) || '').trim());
  const ui = { posts: await val('stat-posts-value'), users: await val('stat-users-value'), comments: await val('stat-comments-value'), subs: await val('stat-subscribers-value') };
  ev(REQ).notes.push(`tiles ui=${JSON.stringify(ui)} psql={posts:${dbPosts},users:${dbUsers},comments:${dbComments},subs:${dbSubs}}`);
  expect(ui.posts).toBe(dbPosts);
  expect(ui.users).toBe(dbUsers);
  expect(ui.comments).toBe(dbComments);
  expect(ui.subs).toBe(dbSubs);
  for (const t of ['stat-posts', 'stat-users', 'stat-comments', 'stat-subscribers']) await mustRender(page, REQ, t, `[data-testid="${t}"]`);

  for (const a of ['action-new-post', 'action-moderate-comments', 'action-send-newsletter', 'action-manage-users']) await mustRender(page, REQ, a, `[data-testid="${a}"]`);
  for (const a of ['attention-pending-comments', 'attention-scheduled-posts', 'attention-draft-posts']) await mustRender(page, REQ, a, `[data-testid="${a}"]`);

  // AdminDashboard's hand-rolled <ul>/<li> became ItemGroup/Item in the 2.0.2 sweep; the list must
  // still carry real rows with text, and the 390px overflow that was fixed must stay fixed.
  await mustRender(page, REQ, 'recent-activity-list', '[data-testid="recent-activity-list"]', 'table');
  const items = await page.locator('[data-testid="recent-activity-item"]').count();
  const emptyItems = (await page.locator('[data-testid="recent-activity-item"]').allTextContents()).filter((t) => !t.trim()).length;
  ev(REQ).notes.push(`recent-activity ItemGroup: ${items} items, ${emptyItems} blank`);
  expect(items).toBeGreaterThan(0);
  expect(emptyItems).toBe(0);

  await mustRender(page, REQ, 'popular-posts-list', '[data-testid="popular-posts-list"]', 'present');
  const popRows = await page.locator('[data-testid="popular-post-row"]').count();
  expect(popRows).toBeGreaterThan(0);
  const popTitles = (await page.locator('[data-testid="popular-post-title"]').allTextContents()).map((s) => s.trim());
  expect(popTitles.filter((t) => t.length > 0).length).toBe(popRows);
  ev(REQ).controls.push({ control: 'popular-posts', verdict: 'RENDERS', detail: `${popRows} ranked rows with titles + views` });

  await mustLookRight(page, REQ, 'ui019-dashboard');
});

test('REQ-UI-020 users list: 4 rows vs psql, role tabs, change-role dialog (TR-067)', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-020';
  await go('/users', /Users/i);
  const dbUsers = psqlRows('SELECT FirstName || \' \' || LastName, EmailId FROM BlogUser ORDER BY UserId');
  const rows = page.locator('[data-testid="user-row-name"]');
  await expect(rows).toHaveCount(dbUsers.length, { timeout: 45000 });
  const uiNames = (await rows.allTextContents()).map((s) => s.trim());
  const uiEmails = (await page.locator('[data-testid="user-row-email"]').allTextContents()).map((s) => s.trim());
  ev(REQ).notes.push(`users ui=${JSON.stringify(uiNames)} psql=${JSON.stringify(dbUsers.map((r) => r[0]))}`);
  expect([...uiNames].sort()).toEqual([...dbUsers.map((r) => r[0])].sort());
  expect([...uiEmails].sort()).toEqual([...dbUsers.map((r) => r[1])].sort());
  for (const t of ['user-row-role', 'user-row-status', 'user-row-joined']) {
    const vals = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(vals.filter((v) => v.length > 0).length).toBe(dbUsers.length);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: vals.join('|') });
  }
  await mustRender(page, REQ, 'users-count', '[data-testid="users-count"]');
  await mustRender(page, REQ, 'new-user', '[data-testid="new-user"]');
  await mustRender(page, REQ, 'users-search', '[data-testid="users-search"]', 'present');

  // Role tab strip: the 2.0.2 sweep moved tab testids onto TabsTrigger elsewhere. Here they are
  // still on <span>s — record what actually ships and prove the strip filters.
  const tabIds = ['users-tab-all', 'users-tab-admin', 'users-tab-editor', 'users-tab-reader'];
  const tabInfo: string[] = [];
  for (const t of tabIds) {
    const el = page.locator(`[data-testid="${t}"]`).first();
    await expect(el).toBeVisible();
    tabInfo.push(`${t}=${(await el.evaluate((e) => e.tagName)).toLowerCase()}:"${((await el.textContent()) || '').trim()}"`);
  }
  ev(REQ).notes.push(`role tabs: ${tabInfo.join(', ')}`);
  const dbAdmins = psqlInt("SELECT COUNT(*) FROM BlogUser WHERE UserRole = 'Admin'");
  await page.locator('[data-testid="users-tab-admin"]').click();
  await page.waitForTimeout(2500);
  const adminRows = await page.locator('[data-testid="user-row-name"]').count();
  ev(REQ).notes.push(`Admins tab: ${adminRows} rows, psql Admin=${dbAdmins}`);
  expect(adminRows).toBe(dbAdmins);
  await page.locator('[data-testid="users-tab-all"]').click();
  await page.waitForTimeout(2500);

  // TR-067 fixed in TrBlazeUI 2.0.3 — the edit dialog's role Select must open with options.
  // Dialog is cancelled; no role is changed.
  await page.locator('[data-testid="user-edit"]').first().click();
  await page.waitForTimeout(3000);
  const dlg = page.locator('[role="dialog"]').first();
  let dlgOptions = -1;
  if (await dlg.count()) {
    const combo = dlg.locator('[role="combobox"]').first();
    if (await combo.count()) {
      await combo.click();
      await page.waitForTimeout(2500);
      dlgOptions = await page.locator('[role="option"]').count();
    }
  }
  ev(REQ).notes.push(`TR-067 check — change-role dialog Select rendered ${dlgOptions} [role=option]`);
  expect(dlgOptions).toBeGreaterThan(0);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1200);
  await closeAnyDialog();

  await mustLookRight(page, REQ, 'ui020-users');
});

test('REQ-UI-021 comment moderation: 16 rows vs psql, status tabs are BUTTON role=tab, bulk-action label', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-021';
  await go('/CommentsList', /Comment moderation/i);
  const dbAll = psqlInt('SELECT COUNT(*) FROM BlogComment');
  const dbApproved = psqlInt("SELECT COUNT(*) FROM BlogComment WHERE ModerationStatus = 'Approved'");
  const rows = page.locator('[data-testid="comment-row-text"]');
  await expect(rows).toHaveCount(dbAll, { timeout: 45000 });
  const texts = (await rows.allTextContents()).map((s) => s.trim());
  expect(texts.filter((t) => t.length > 0).length).toBe(dbAll);
  ev(REQ).notes.push(`comments rows=${dbAll} = psql; all non-empty`);
  for (const t of ['comment-row-author', 'comment-row-post', 'comment-row-status', 'comment-row-date']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.filter((x) => x.length > 0).length).toBe(v.length);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: `${v.length} non-empty` });
  }
  await mustRender(page, REQ, 'comments-count', '[data-testid="comments-count"]');

  // Tab strip after the testid move: must be a real BUTTON role=tab now.
  const tabTags: string[] = [];
  for (const t of ['comments-tab-all', 'comments-tab-pending', 'comments-tab-approved', 'comments-tab-spam']) {
    const el = page.locator(`[data-testid="${t}"]`).first();
    await expect(el).toBeVisible();
    tabTags.push(`${t}=${(await el.evaluate((e) => e.tagName)).toLowerCase()}/${await el.getAttribute('role')}:"${((await el.textContent()) || '').trim()}"`);
  }
  ev(REQ).notes.push(`status tabs: ${tabTags.join(', ')}`);
  await page.locator('[data-testid="comments-tab-approved"]').click();
  await page.waitForTimeout(3000);
  const approvedRows = await page.locator('[data-testid="comment-row-text"]').count();
  ev(REQ).notes.push(`Approved tab: ${approvedRows} rows vs psql ${dbApproved}`);
  expect(approvedRows).toBe(dbApproved);
  await page.locator('[data-testid="comments-tab-all"]').click();
  await page.waitForTimeout(3000);

  // Select first-paint sentinel on this screen.
  const bulk = await triggerLabel(page, 'comments-bulk-action');
  ev(REQ).notes.push(`comments-bulk-action first-paint label = "${bulk}"`);
  expect(bulk).toBe('Bulk Actions');
  ev(REQ).notes.push('approve/spam/delete WRITES are NOT-OBSERVABLE (read-only verify run); the controls render on every actionable row');

  await mustLookRight(page, REQ, 'ui021-comments');
});

test('REQ-UI-022 categories list: 5 rows vs psql with slug/description/post count', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-022';
  await go('/admin/categories', /Categories Management/i);
  const db = psqlRows('SELECT CategoryName FROM Category ORDER BY CategoryName').map((r) => r[0]);
  const rows = page.locator('[data-testid="category-row-name"]');
  await expect(rows).toHaveCount(db.length, { timeout: 45000 });
  const ui = (await rows.allTextContents()).map((s) => s.trim());
  ev(REQ).notes.push(`categories ui=${JSON.stringify(ui)} psql=${JSON.stringify(db)}`);
  expect([...ui].sort()).toEqual([...db].sort());
  for (const t of ['category-row-slug', 'category-row-description', 'category-row-postcount']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.filter((x) => x.length > 0).length).toBe(db.length);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: v.join('|').slice(0, 120) });
  }
  await mustRender(page, REQ, 'categories-count', '[data-testid="categories-count"]');
  await mustRender(page, REQ, 'new-category', '[data-testid="new-category"]');
  await mustLookRight(page, REQ, 'ui022-categories');
});

test('REQ-UI-023 tags list: 15 rows vs psql with slug and post count', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-023';
  await go('/admin/tags', /Tags Management/i);
  const db = psqlRows('SELECT TagName FROM Tag ORDER BY TagName').map((r) => r[0]);
  const rows = page.locator('[data-testid="tag-row-name"]');
  await expect(rows).toHaveCount(db.length, { timeout: 45000 });
  const ui = (await rows.allTextContents()).map((s) => s.trim());
  ev(REQ).notes.push(`tags ui count=${ui.length} psql=${db.length}`);
  expect([...ui].sort()).toEqual([...db].sort());
  for (const t of ['tag-row-slug', 'tag-row-postcount']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.filter((x) => x.length > 0).length).toBe(db.length);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: `${v.length} non-empty` });
  }
  await mustRender(page, REQ, 'tags-count', '[data-testid="tags-count"]');
  await mustRender(page, REQ, 'new-tag', '[data-testid="new-tag"]');
  await mustLookRight(page, REQ, 'ui023-tags');
});

test('REQ-UI-025 subscribers admin: 11 rows / 7 active vs psql, status tabs, consent column', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-025';
  await go('/admin/subscribers', /Subscribers/i);
  const dbAll = psqlInt('SELECT COUNT(*) FROM Subscriber');
  const dbActive = psqlInt('SELECT COUNT(*) FROM Subscriber WHERE IsConfirmed = TRUE');
  const rows = page.locator('[data-testid="subscriber-row-email"]');
  await expect(rows).toHaveCount(dbAll, { timeout: 45000 });
  const emails = (await rows.allTextContents()).map((s) => s.trim());
  expect(emails.filter((e) => e.includes('@')).length).toBe(dbAll);
  const summary = ((await page.locator('[data-testid="subscribers-summary"]').textContent()) || '').trim();
  ev(REQ).notes.push(`summary="${summary}" psql all=${dbAll} active=${dbActive}`);
  expect(summary).toContain(`${dbAll} total`);
  expect(summary).toContain(`(${dbActive} active)`);
  for (const t of ['subscriber-row-status', 'subscriber-row-consent', 'subscriber-row-date']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.filter((x) => x.length > 0).length).toBe(dbAll);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: `${v.length} non-empty` });
  }
  await mustRender(page, REQ, 'subscribers-export', '[data-testid="subscribers-export"]');
  await page.locator('[data-testid="subscribers-tab-active"]').click();
  await page.waitForTimeout(3000);
  const activeRows = await page.locator('[data-testid="subscriber-row-email"]').count();
  ev(REQ).notes.push(`Active tab: ${activeRows} rows vs psql ${dbActive}`);
  expect(activeRows).toBe(dbActive);
  await page.locator('[data-testid="subscribers-tab-all"]').click();
  await page.waitForTimeout(3000);
  await mustLookRight(page, REQ, 'ui025-subscribers');
});

test('REQ-UI-026 site settings: six tabs render fields; storage Select label resolves on first paint', async () => {
  test.setTimeout(300000);
  const REQ = 'REQ-UI-026';
  await go('/settings', /Settings/i);
  for (const t of ['tab-general', 'tab-blog', 'tab-theme', 'tab-seo', 'tab-email', 'tab-storage']) await mustRender(page, REQ, t, `[data-testid="${t}"]`);
  await mustRender(page, REQ, 'save-settings', '[data-testid="save-settings"]');

  // General tab values must be bound, not blank.
  const dbTitle = psql("SELECT COALESCE(SettingValue, '') FROM SiteSetting WHERE SettingKey = 'General.SiteTitle'");
  const uiTitle = await page.locator('[data-testid="site-title"]').inputValue();
  ev(REQ).notes.push(`site-title ui="${uiTitle}" psql="${dbTitle}"`);
  expect(uiTitle.length).toBeGreaterThan(0);
  if (dbTitle) expect(uiTitle).toBe(dbTitle);

  // Storage tab — a Select whose value is pre-selected: first-paint label must be the item TEXT.
  await page.locator('[data-testid="tab-storage"]').click();
  await page.waitForTimeout(3000);
  const storage = await triggerLabel(page, 'storage-provider');
  ev(REQ).notes.push(`storage-provider first-paint label = "${storage}"`);
  expect(storage).toBe('Local');
  expect(storage).not.toMatch(/^\d+$/);
  await page.locator('[data-testid="tab-general"]').click();
  await page.waitForTimeout(2500);
  ev(REQ).notes.push('Save WRITE is NOT-OBSERVABLE (read-only verify run) — save-settings renders and is enabled');
  await mustLookRight(page, REQ, 'ui026-settings');
});

test('REQ-UI-032 theme selector in site settings resolves its pre-selected value to a label', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-032';
  await go('/settings', /Settings/i);
  await page.locator('[data-testid="tab-theme"]').click();
  await page.waitForTimeout(3000);
  const theme = await triggerLabel(page, 'site-theme-select');
  ev(REQ).notes.push(`site-theme-select first-paint label = "${theme}"`);
  expect(theme.length).toBeGreaterThan(0);
  expect(theme).not.toMatch(/^\d+$/);
  expect(theme).toBe('TrBlaze Modern');
  await page.locator('[data-testid="site-theme-select"]').click();
  await page.waitForTimeout(2500);
  const opts = (await page.locator('[role="option"]').allTextContents()).map((s) => s.trim());
  ev(REQ).notes.push(`theme options: ${JSON.stringify(opts)}`);
  expect(opts.length).toBeGreaterThan(1);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1500);
  // Must still read the label after the popover closes without a selection.
  expect(await triggerLabel(page, 'site-theme-select')).toBe('TrBlaze Modern');
  await mustLookRight(page, REQ, 'ui032-theme');
});

test('REQ-UI-037 manage experience: 3 cards vs psql and the "-- My Experience --" sentinel', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-037';
  await go('/admin/experience', /Manage Experience/i);
  const sentinel = await triggerLabel(page, 'experience-user-select');
  ev(REQ).notes.push(`experience-user-select first-paint label = "${sentinel}"`);
  expect(sentinel).toBe('-- My Experience --');
  // Experience lives in UserEvents: SessionTitle is the role, EventTitle the company.
  const db = psqlRows("SELECT SessionTitle, EventTitle FROM UserEvents WHERE UserId = 1 AND Type = 'Experience' ORDER BY DisplayOrder");
  const cards = page.locator('[data-testid="experience-card"]');
  await expect(cards).toHaveCount(db.length, { timeout: 45000 });
  const roles = (await page.locator('[data-testid="experience-role"]').allTextContents()).map((s) => s.trim());
  const companies = (await page.locator('[data-testid="experience-company"]').allTextContents()).map((s) => s.trim());
  ev(REQ).notes.push(`experience ui=${JSON.stringify(roles.map((r, i) => `${r} @ ${companies[i]}`))} psql=${JSON.stringify(db.map((r) => `${r[0]} @ ${r[1]}`))}`);
  expect(roles).toEqual(db.map((r) => r[0]));
  expect(companies).toEqual(db.map((r) => r[1]));
  for (const t of ['experience-dates', 'experience-description', 'experience-order']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.filter((x) => x.length > 0).length).toBe(db.length);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: `${v.length} non-empty` });
  }
  await mustRender(page, REQ, 'add-experience', '[data-testid="add-experience"]');
  await mustLookRight(page, REQ, 'ui037-experience');
});

test('REQ-UI-040 manage profile: fields bound to psql; REQ-UI-035 ImagePicker renders with its constraint caption', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-040';
  await go('/admin/profile', /My Profile/i);
  const db = psqlRows("SELECT COALESCE(FirstName,''), COALESCE(LastName,''), COALESCE(UserName,''), COALESCE(Title,''), COALESCE(Location,'') FROM BlogUser WHERE UserId = 1")[0];
  const read = async (id: string) => (await page.locator(`[data-testid="${id}"]`).inputValue()).trim();
  const ui = [await read('first-name-input'), await read('last-name-input'), await read('username-input'), await read('title-input'), await read('location-input')];
  ev(REQ).notes.push(`profile ui=${JSON.stringify(ui)} psql=${JSON.stringify(db)}`);
  expect(ui).toEqual(db);
  for (const t of ['basic-info-card', 'social-links-card', 'resume-settings-card']) await mustRender(page, REQ, t, `[data-testid="${t}"]`, 'present');
  for (const t of ['bio-input', 'tagline-input', 'linkedin-input', 'github-input', 'phone-input']) await mustRender(page, REQ, t, `[data-testid="${t}"]`, 'present');
  await mustRender(page, REQ, 'save-profile', '[data-testid="save-profile"]');
  ev(REQ).notes.push('Save WRITE is NOT-OBSERVABLE (read-only verify run)');

  // REQ-UI-035 — the shared ImagePicker, and the 390px clear-image / upload-new-image overlap that
  // was once a graded defect on this screen.
  const R35 = 'REQ-UI-035';
  const pickers = await page.locator('[data-testid="image-picker"]').count();
  ev(R35).notes.push(`${pickers} ImagePicker instances on /admin/profile`);
  expect(pickers).toBeGreaterThan(0);
  await mustRender(page, R35, 'choose-from-library', '[data-testid="choose-from-library"]');
  await mustRender(page, R35, 'upload-new-image', '[data-testid="upload-new-image"]');
  const caption = ((await page.locator('[data-testid="image-constraints"]').first().textContent()) || '').trim();
  ev(R35).notes.push(`image-constraints caption = "${caption}"`);
  expect(caption).toMatch(/Max 2 MB, formats: jpg, jpeg, png, webp/);
  ev(R35).controls.push({ control: 'image-constraints', verdict: 'RENDERS', detail: caption });

  await mustLookRight(page, REQ, 'ui040-profile');
  ev(R35).visual.push(...ev(REQ).visual.slice(-2));
});

test('REQ-UI-043 newsletter composer: compose card, audience radiogroup with live recipient counts', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-043';
  await go('/admin/newsletter', /Newsletter composer/i);
  for (const t of ['newsletter-compose-card', 'newsletter-recipients-card', 'newsletter-send-card', 'newsletter-history-card']) await mustRender(page, REQ, t, `[data-testid="${t}"]`, 'present');
  for (const t of ['newsletter-subject', 'newsletter-summary', 'newsletter-segment-filter'])
    await mustRender(page, REQ, t, `[data-testid="${t}"]`, 'present');
  for (const t of ['newsletter-save-draft', 'newsletter-new', 'newsletter-send', 'newsletter-tab-write', 'newsletter-tab-preview']) await mustRender(page, REQ, t, `[data-testid="${t}"]`);

  const dbActive = psqlInt('SELECT COUNT(*) FROM Subscriber WHERE IsConfirmed = TRUE');
  const dbAll = psqlInt('SELECT COUNT(*) FROM Subscriber');
  const activeLbl = ((await page.locator('[data-testid="audience-active-label"]').textContent()) || '').trim();
  const everyoneLbl = ((await page.locator('[data-testid="audience-everyone-label"]').textContent()) || '').trim();
  const recipients = ((await page.locator('[data-testid="newsletter-recipient-count"]').textContent()) || '').trim();
  ev(REQ).notes.push(`audience "${activeLbl}" / "${everyoneLbl}" / "${recipients}" vs psql active=${dbActive} all=${dbAll}`);
  expect(activeLbl).toContain(`(${dbActive})`);
  expect(everyoneLbl).toContain(`(${dbAll})`);
  expect(recipients).toContain(`${dbActive} recipient`);

  // History is legitimately empty — psql has no Newsletter rows. NO-DATA, not RENDER-EMPTY.
  const dbNewsletters = psqlInt('SELECT COUNT(*) FROM Newsletter');
  const emptyState = await page.locator('[data-testid="newsletter-history-empty"]').count();
  ev(REQ).notes.push(`history: psql Newsletter=${dbNewsletters}, empty-state elements=${emptyState} (NO-DATA, an explicit empty state is rendered)`);
  if (dbNewsletters === 0) expect(emptyState).toBe(1);
  ev(REQ).notes.push('Send WRITE is NOT-OBSERVABLE (read-only verify run) — a send cannot be undone and siblings share this database');
  await mustLookRight(page, REQ, 'ui043-newsletter');
});

test('REQ-UI-044 analytics dashboard: tiles, trend chart, DataTable popular posts, category list, date range', async () => {
  test.setTimeout(300000);
  const REQ = 'REQ-UI-044';
  await go('/admin/analytics', /Analytics/i);
  for (const t of ['analytics-stat-views', 'analytics-stat-unique', 'analytics-stat-rating', 'analytics-stat-comments']) await mustRender(page, REQ, t, `[data-testid="${t}"]`);
  await mustRender(page, REQ, 'analytics-range-caption', '[data-testid="analytics-range-caption"]');
  await mustRender(page, REQ, 'analytics-trend-chart', '[data-testid="analytics-trend-chart"]', 'chart');
  await mustRender(page, REQ, 'analytics-trend-summary', '[data-testid="analytics-trend-summary"]');
  for (const t of ['analytics-from', 'analytics-to']) await mustRender(page, REQ, t, `[data-testid="${t}"]`, 'present');
  for (const t of ['analytics-apply', 'analytics-preset-7', 'analytics-preset-30', 'analytics-preset-90']) await mustRender(page, REQ, t, `[data-testid="${t}"]`);

  // Popular posts became a DataTable MinWidth="720px" with both hand-rolled wrappers removed —
  // rows must still be there, with non-empty cells in every column.
  const titles = (await page.locator('[data-testid="popular-row-title"]').allTextContents()).map((s) => s.trim());
  expect(titles.length).toBeGreaterThan(0);
  expect(titles.filter((t) => t.length > 0).length).toBe(titles.length);
  for (const t of ['popular-row-views', 'popular-row-unique', 'popular-row-comments', 'popular-row-rating']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.length).toBe(titles.length);
    expect(v.filter((x) => x.length > 0).length).toBe(titles.length);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: v.join(',') });
  }
  ev(REQ).notes.push(`popular DataTable: ${titles.length} rows, all 5 columns non-empty`);

  // The DataTable's 720px min-width must live inside a real horizontal scroller, or 390 spills.
  const scroller = await page.evaluate(() => {
    const grid = document.querySelector('[data-testid="analytics-popular-grid"]');
    let n: HTMLElement | null = grid as HTMLElement;
    while (n) {
      const s = getComputedStyle(n);
      if (s.overflowX === 'auto' || s.overflowX === 'scroll') return { found: true, tag: n.tagName, cls: (n.className || '').toString().slice(0, 60) };
      n = n.parentElement;
    }
    return { found: false };
  });
  ev(REQ).notes.push(`popular-posts overflow-x ancestor: ${JSON.stringify(scroller)}`);
  expect(scroller.found).toBe(true);

  const catNames = (await page.locator('[data-testid="category-row-name"]').allTextContents()).map((s) => s.trim());
  const catViews = (await page.locator('[data-testid="category-row-views"]').allTextContents()).map((s) => s.trim());
  expect(catNames.length).toBeGreaterThan(0);
  expect(catViews.filter((v) => v.length > 0).length).toBe(catNames.length);
  ev(REQ).controls.push({ control: 'analytics-category-list', verdict: 'RENDERS', detail: catNames.map((n, i) => `${n}=${catViews[i]}`).join(', ') });

  // Views total must agree with the rows the table itself shows.
  const uiViews = Number(((await page.locator('[data-testid="analytics-stat-views"]').textContent()) || '0').trim());
  const rowSum = (await page.locator('[data-testid="popular-row-views"]').allTextContents()).reduce((a, b) => a + Number(b.trim() || 0), 0);
  ev(REQ).notes.push(`views tile=${uiViews}, sum of popular rows=${rowSum}`);
  expect(uiViews).toBeGreaterThanOrEqual(rowSum);

  // The preset buttons must actually move the range caption.
  const before = ((await page.locator('[data-testid="analytics-range-caption"]').textContent()) || '').trim();
  await page.locator('[data-testid="analytics-preset-7"]').click();
  await page.waitForTimeout(4000);
  const after = ((await page.locator('[data-testid="analytics-range-caption"]').textContent()) || '').trim();
  ev(REQ).notes.push(`range caption before="${before}" after 7-day preset="${after}"`);
  expect(after).not.toBe(before);
  expect(after).toContain('7 days');
  await page.locator('[data-testid="analytics-preset-30"]').click();
  await page.waitForTimeout(4000);

  await mustLookRight(page, REQ, 'ui044-analytics');
});

test('REQ-UI-017 all-posts list: 10 rows vs psql, status tab strip scrolls rather than spills', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-UI-017';
  await go('/BlogsList', /All Posts/i);
  const dbAll = psqlInt(`SELECT COUNT(*) FROM BlogPost WHERE ${LIVE}`);
  const dbPub = psqlInt(`SELECT COUNT(*) FROM BlogPost WHERE Published = TRUE AND ${LIVE}`);
  const rows = page.locator('[data-testid="post-row-title"]');
  await expect(rows).toHaveCount(dbAll, { timeout: 45000 });
  const titles = (await rows.allTextContents()).map((s) => s.trim());
  expect(titles.filter((t) => t.length > 0).length).toBe(dbAll);
  ev(REQ).notes.push(`posts rows=${dbAll} = psql; all titles non-empty`);
  for (const t of ['post-row-author', 'post-row-status', 'post-row-date', 'post-row-slug']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.filter((x) => x.length > 0).length).toBe(dbAll);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: `${v.length} non-empty` });
  }
  const tabAll = ((await page.locator('[data-testid="posts-tab-all"]').textContent()) || '').trim();
  const tabPub = ((await page.locator('[data-testid="posts-tab-published"]').textContent()) || '').trim();
  ev(REQ).notes.push(`tabs "${tabAll}" / "${tabPub}" vs psql all=${dbAll} published=${dbPub}`);
  expect(tabAll).toContain(`(${dbAll})`);
  expect(tabPub).toContain(`(${dbPub})`);
  await page.locator('[data-testid="posts-tab-published"]').click();
  await page.waitForTimeout(3000);
  expect(await page.locator('[data-testid="post-row-title"]').count()).toBe(dbPub);
  await page.locator('[data-testid="posts-tab-all"]').click();
  await page.waitForTimeout(3000);

  // The tab strip carries its own overflow-x scroller; at 390 that is correct, not a spill.
  const strip = await page.evaluate(() => {
    const s = document.querySelector('[data-testid="posts-status-tabs-scroller"]') as HTMLElement;
    return s ? { overflowX: getComputedStyle(s).overflowX } : { overflowX: null };
  });
  ev(REQ).notes.push(`posts-status-tabs-scroller overflowX=${strip.overflowX}`);
  await mustLookRight(page, REQ, 'ui017-posts');
});

// =====================================================================================
// Cross-cutting: the deleted SelectFirstPaintLabel workaround — every trigger on and around this
// surface, read on FIRST PAINT, before any click.
// =====================================================================================
test('REQ-UI-048 Select first-paint labels resolve library-side across the admin surface (workaround deleted)', async () => {
  test.setTimeout(420000);
  const REQ = 'REQ-UI-048';
  const results: { route: string; testid: string; label: string; expected: string | RegExp; ok: boolean }[] = [];

  const check = async (route: string, heading: RegExp, testid: string, expected: string | RegExp, pre?: () => Promise<void>) => {
    await go(route, heading);
    if (pre) await pre();
    const label = await triggerLabel(page, testid);
    const ok = typeof expected === 'string' ? label === expected : expected.test(label);
    results.push({ route, testid, label, expected: expected.toString(), ok });
  };

  await check('/admin/images', /Media Library/i, 'user-filter-select', 'All Users');
  await check('/admin/skills', /Manage Skills/i, 'skills-user-select', /S Ravi Kumar/);
  await check('/admin/awards', /Manage Awards/i, 'awards-user-select', /S Ravi Kumar/);
  await check('/admin/stats', /Manage Statistics/i, 'stats-user-select', /S Ravi Kumar/);
  await check('/admin/experience', /Manage Experience/i, 'experience-user-select', '-- My Experience --');
  await check('/ManagePost', '[data-testid="post-title-input"]', 'category-select', '-- Select Category --');
  await check('/ManagePost', '[data-testid="post-title-input"]', 'series-select', '-- Not part of a series --');
  // The bulk-action Select lives inside the non-empty branch; with zero comments the documented
  // `comments-empty` state renders instead (conditional by design, not RENDER-EMPTY).
  await go('/CommentsList', /Comment moderation/i);
  if ((await page.locator('[data-testid="comments-empty"]').count()) === 0) {
    await check('/CommentsList', /Comment moderation/i, 'comments-bulk-action', 'Bulk Actions');
  } else {
    ev(REQ).notes.push('/CommentsList: zero comments in this database — comments-bulk-action absent by documented conditional (comments-empty rendered)');
  }
  await check('/BlogsList', /All Posts/i, 'posts-bulk-action', /Bulk Actions/);
  await check('/settings', /Settings/i, 'site-theme-select', 'TrBlaze Modern', async () => {
    await page.locator('[data-testid="tab-theme"]').click();
    await page.waitForTimeout(3000);
  });
  await check('/settings', /Settings/i, 'storage-provider', 'Local', async () => {
    await page.locator('[data-testid="tab-storage"]').click();
    await page.waitForTimeout(3000);
  });
  await check('/search', /Search/i, 'sort-filter', 'Sort by Relevance');
  await check('/search', /Search/i, 'date-filter', 'Any Date');
  await check('/search', /Search/i, 'category-filter', 'All Categories');

  fs.writeFileSync(`${OUT}/select-first-paint.json`, JSON.stringify(results, null, 2));
  ev(REQ).notes.push(`Select first-paint sweep: ${results.filter((r) => r.ok).length}/${results.length} correct`);
  const bad = results.filter((r) => !r.ok);
  // A raw bound value ("0", "1") is the exact 2.0.1 regression this upgrade was meant to close.
  const rawValue = results.filter((r) => /^\d+$/.test(r.label));
  ev(REQ).notes.push(`raw-value echoes: ${JSON.stringify(rawValue.map((r) => `${r.testid}="${r.label}"`))}`);
  expect(`rawValueEchoes=${JSON.stringify(rawValue.map((r) => `${r.route}:${r.testid}="${r.label}"`))}`).toContain('rawValueEchoes=[]');
  expect(`mismatched=${JSON.stringify(bad.map((r) => `${r.route}:${r.testid}="${r.label}" expected ${r.expected}`))}`).toContain('mismatched=[]');
});

// =====================================================================================
// /admin/stats — no dedicated REQ-UI row; graded under REQ-FN-027 (resume data model)
// =====================================================================================
test('REQ-FN-027 manage statistics: 4 cards vs psql with value/label/category and a resolved user select', async () => {
  test.setTimeout(240000);
  const REQ = 'REQ-FN-027';
  await go('/admin/stats', /Manage Statistics/i);
  const label = await triggerLabel(page, 'stats-user-select');
  ev(REQ).notes.push(`stats-user-select first-paint label = "${label}"`);
  expect(label).toContain('S Ravi Kumar');
  const db = psqlInt('SELECT COUNT(*) FROM UserStats WHERE UserId = 1');
  const cards = page.locator('[data-testid="stat-card"]');
  await expect(cards).toHaveCount(db, { timeout: 45000 });
  for (const t of ['stat-value', 'stat-label', 'stat-category']) {
    const v = (await page.locator(`[data-testid="${t}"]`).allTextContents()).map((s) => s.trim());
    expect(v.filter((x) => x.length > 0).length).toBe(db);
    ev(REQ).controls.push({ control: t, verdict: 'RENDERS', detail: v.join('|') });
  }
  ev(REQ).notes.push(`stat cards=${db} = psql UserStats(userid=1)`);
  await mustLookRight(page, REQ, 'fn027-stats');
});
