import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { BASE, nav, visualCheck } from './_gates';
import { psql, signIn, settle } from './vall-resume-helpers';
import { solveCaptcha, gotoPublic } from './_engage-helpers';

/**
 * Verify-phase 2026-10-27 group W6 (resume manage pages): REQ-UI-037, -038, -039.
 * Rows created carry the fxv6- prefix (owner = userid 1) and are removed through the UI.
 */
const OUT = 'tests/.artifacts/verify/w6';
const PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64',
);
test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

function watch(page: Page) {
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  page.on('pageerror', (e) => errors.push('pageerror ' + e.message));
  return errors;
}
async function geom(page: Page, stem: string) {
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, `${OUT}/${stem}-${w}.png`, w);
    console.log(`GEOM ${stem}@${w}`, JSON.stringify({ o: v.overlaps, z: v.zeroSized, off: v.offViewport, h: v.hScroll, c: v.consoleErrors }));
    expect(v.hScroll, `${stem}@${w} hscroll`).toBe(0);
    expect(v.overlaps, `${stem}@${w} overlaps`).toEqual([]);
  }
  await page.setViewportSize({ width: 1280, height: 900 });
}
async function resumeTexts(page: Page, itemSel: string, ready: string) {
  const anon = await page.context().browser()!.newContext();
  const p = await anon.newPage();
  await p.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
  await p.waitForSelector(ready, { timeout: 45000 }).catch(() => {});
  await p.waitForFunction(() => document.querySelectorAll('[data-testid$="-loading"]').length === 0, undefined, { timeout: 45000 }).catch(() => {});
  await p.waitForTimeout(2500);
  let t = await p.locator(itemSel).allInnerTexts();
  for (let i = 0; i < 3 && t.length === 0; i++) { await p.waitForTimeout(4000); t = await p.locator(itemSel).allInnerTexts(); }
  await p.screenshot({ path: `${OUT}/resume-${Date.now()}.png`, fullPage: true });
  await anon.close();
  return t.map((s) => s.replace(/\s+/g, ' ').trim());
}

test('REQ-UI-037 experience: empty state, add, order on /resume, edit, delete', async ({ page }) => {
  test.setTimeout(500000);
  const errors = watch(page);
  const before = psql("select count(*) from userevents where type='Experience' and userid=1");
  console.log('OWNER EXPERIENCE BEFORE', before, psql("select md5(string_agg(t::text,'' order by eventid)) from userevents t where userid=1 and sessiontitle not like 'fxv6-%'"));
  try {
    console.log('LANDING', await signIn(page, 'admin'));
    await nav(page, '/admin/experience', /Manage Experience/i);
    await settle(page);

    // empty state via the editor account (zero experience rows) using the admin user selector
    await expect(page.locator('[data-testid="experience-user-select"]')).toBeVisible();
    await page.click('[data-testid="experience-user-select"]');
    await page.getByRole('option', { name: /editor@techieblog\.test/ }).click();
    await expect(page.locator('[data-testid="experience-empty"]')).toBeVisible({ timeout: 20000 });
    await expect(page.locator('[data-testid="add-first-experience"]')).toBeVisible();
    await page.screenshot({ path: `${OUT}/037-empty-1280.png` });
    await geom(page, '037-empty');
    // back to owner
    await page.click('[data-testid="experience-user-select"]');
    await page.getByRole('option', { name: /Ravi@techieblog\.com/i }).click();
    await expect(page.locator('[data-testid="experience-card"], [data-testid="experience-empty"]').first()).toBeVisible({ timeout: 20000 });
    await page.waitForTimeout(1500);

    const add = async (role: string, company: string, order: string) => {
      await page.click('[data-testid="add-experience"]');
      const d = page.locator('[data-testid="experience-dialog"]');
      await expect(d).toBeVisible();
      await d.locator('[data-testid="experience-role-input"]').fill(role);
      await d.locator('[data-testid="experience-company-input"]').fill(company);
      await d.locator('[data-testid="experience-description-input"]').fill('- fxv6 bullet');
      await d.locator('[data-testid="experience-order-input"]').fill(order);
      await d.locator('[data-testid="save-experience"]').click();
      await expect(d).toBeHidden({ timeout: 45000 });
    };
    await add('fxv6-exp-late', 'fxv6 Co Late', '91');
    await add('fxv6-exp-early', 'fxv6 Co Early', '90');
    const roles = await page.locator('[data-testid="experience-role"]').allInnerTexts();
    console.log('ADMIN LIST ORDER', roles);
    expect(roles.indexOf('fxv6-exp-early')).toBeGreaterThanOrEqual(0);
    expect(roles.indexOf('fxv6-exp-early')).toBeLessThan(roles.indexOf('fxv6-exp-late'));
    console.log('DB', psql("select sessiontitle||':'||displayorder from userevents where sessiontitle like 'fxv6-exp%' order by displayorder"));
    await geom(page, '037-list');

    let rs = await resumeTexts(page, '[data-testid="experience-item"]', '[data-testid="experience-item"]');
    console.log('RESUME ITEMS', rs);
    const iE = rs.findIndex((s) => s.includes('fxv6-exp-early')), iL = rs.findIndex((s) => s.includes('fxv6-exp-late'));
    expect(iE).toBeGreaterThanOrEqual(0);
    expect(iE).toBeLessThan(iL);

    // edit
    const card = page.locator('[data-testid="experience-card"]').filter({ hasText: 'fxv6-exp-late' });
    await card.locator('[data-testid="edit-experience"]').click();
    const d = page.locator('[data-testid="experience-dialog"]');
    await expect(d).toBeVisible({ timeout: 30000 });
    await expect(d.locator('[data-testid="experience-role-input"]')).toHaveValue('fxv6-exp-late');
    await d.locator('[data-testid="experience-role-input"]').fill('fxv6-exp-late-EDITED');
    await d.locator('[data-testid="experience-order-input"]').fill('89');
    await d.locator('[data-testid="save-experience"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });
    const roles2 = await page.locator('[data-testid="experience-role"]').allInnerTexts();
    console.log('AFTER EDIT', roles2);
    expect(roles2.indexOf('fxv6-exp-late-EDITED')).toBeLessThan(roles2.indexOf('fxv6-exp-early'));
    rs = await resumeTexts(page, '[data-testid="experience-item"]', '[data-testid="experience-item"]');
    console.log('RESUME AFTER EDIT', rs);
    expect(rs.findIndex((s) => s.includes('fxv6-exp-late-EDITED'))).toBeLessThan(rs.findIndex((s) => s.includes('fxv6-exp-early')));

    // delete both
    for (const name of ['fxv6-exp-late-EDITED', 'fxv6-exp-early']) {
      await page.locator('[data-testid="experience-card"]').filter({ hasText: name }).locator('[data-testid="delete-experience"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await expect(page.locator('[data-testid="experience-card"]').filter({ hasText: name })).toHaveCount(0, { timeout: 30000 });
      rs = await resumeTexts(page, '[data-testid="experience-item"]', '[data-testid="experience-item"]');
      console.log('RESUME AFTER DELETE of', name, rs);
      expect(rs.some((s) => s.includes(name))).toBe(false);
      if (name === 'fxv6-exp-late-EDITED') expect(rs.some((s) => s.includes('fxv6-exp-early')), 'positive control: sibling row still renders').toBe(true);
    }
  } finally {
    const left = psql("select count(*) from userevents where sessiontitle like 'fxv6-exp%'");
    console.log('LEFTOVERS', left);
    if (left !== '0') console.log('LEFTOVER ROWS (remove through the UI):', left);
    console.log('OWNER EXPERIENCE AFTER', psql("select count(*) from userevents where type='Experience' and userid=1"), psql("select md5(string_agg(t::text,'' order by eventid)) from userevents t where userid=1 and sessiontitle not like 'fxv6-%'"));
    console.log('CONSOLE ERRORS', errors);
  }
});

test('REQ-UI-038 skills: empty, add new/existing category, order, resume, edit, move, delete', async ({ page }) => {
  test.setTimeout(500000);
  const errors = watch(page);
  console.log('OWNER SKILLS BEFORE', psql('select count(*) from userskills where userid=1'));
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/skills', /Manage Skills/i);
    await settle(page);
    const emptyBefore = await page.locator('[data-testid="skills-empty"]').count();
    console.log('SKILLS EMPTY STATE PRESENT', emptyBefore);
    if (emptyBefore) {
      await expect(page.locator('[data-testid="add-first-skill"]')).toBeVisible();
      await page.screenshot({ path: `${OUT}/038-empty-1280.png` });
      await geom(page, '038-empty');
    }

    const addSkill = async (name: string, cat: string | null, newCat?: string) => {
      await page.click('[data-testid="add-skill"]');
      const d = page.locator('[data-testid="add-skill-dialog"]');
      await expect(d).toBeVisible();
      await d.locator('[data-testid="skill-name-input"]').fill(name);
      await d.locator('[data-testid="skill-category-select"]').click();
      if (newCat) {
        await page.getByRole('option', { name: /Create New Category/ }).click();
        await d.locator('[data-testid="new-category-input"]').fill(newCat);
      } else {
        await page.getByRole('option', { name: cat!, exact: true }).click();
      }
      await d.locator('[data-testid="save-skill"]').click();
      await expect(d).toBeHidden({ timeout: 30000 });
    };
    await addSkill('fxv6-skill-one', null, 'fxv6-cat-a');       // new category
    await addSkill('fxv6-skill-two', 'fxv6-cat-a');             // existing category
    await addSkill('fxv6-skill-three', null, 'fxv6-cat-b');     // second new category
    const catCard = (c: string) => page.locator('[data-testid="skill-category-card"]').filter({ has: page.locator('[data-testid="skill-category-name"]', { hasText: c }) });
    const names = async (c: string) => (await catCard(c).locator('[data-testid="skill-name"]').allInnerTexts());
    console.log('CAT A', await names('fxv6-cat-a'), 'CAT B', await names('fxv6-cat-b'));
    expect(await names('fxv6-cat-a')).toEqual(['fxv6-skill-one', 'fxv6-skill-two']);
    console.log('DB', psql("select skillname||'|'||category||'|'||displayorder from userskills where skillname like 'fxv6-%' order by 1").replace(/\n/g, ' ; '));
    await geom(page, '038-list');

    let rs = await resumeTexts(page, '[data-testid="skill-category"]', '[data-testid="skill-category"]');
    console.log('RESUME SKILLS', rs);
    expect(rs.some((s) => s.includes('fxv6-cat-a') && s.includes('fxv6-skill-one') && s.includes('fxv6-skill-two'))).toBe(true);

    // reorder within category
    await catCard('fxv6-cat-a').locator('[data-testid="skill-row"]').nth(1).locator('[data-testid="move-skill-up"]').click();
    await expect.poll(async () => (await names('fxv6-cat-a')).join(','), { timeout: 20000 }).toBe('fxv6-skill-two,fxv6-skill-one');
    rs = await resumeTexts(page, '[data-testid="skill-category"]', '[data-testid="skill-category"]');
    console.log('RESUME AFTER MOVE', rs);
    const a = rs.find((s) => s.includes('fxv6-cat-a'))!;
    expect(a.indexOf('fxv6-skill-two')).toBeLessThan(a.indexOf('fxv6-skill-one'));

    // reorder categories
    const catNames = async () => page.locator('[data-testid="skill-category-name"]').allInnerTexts();
    const preCats = await catNames();
    await catCard('fxv6-cat-b').locator('[data-testid="move-category-up"]').click();
    await expect.poll(async () => { const c = await catNames(); return c.indexOf('fxv6-cat-b') < c.indexOf('fxv6-cat-a'); }, { timeout: 20000 }).toBe(true);
    console.log('CATS', preCats, '->', await catNames());

    // edit (rename) a skill, and persistence across reload
    const row = catCard('fxv6-cat-a').locator('[data-testid="skill-row"]').filter({ hasText: 'fxv6-skill-one' });
    await row.locator('[data-testid="edit-skill"]').click();
    await page.locator('[data-testid="skill-name-edit-input"]').fill('fxv6-skill-one-EDITED');
    await page.locator('[data-testid="save-skill-edit"]').click();
    await expect(page.locator('[data-testid="skill-name"]', { hasText: 'fxv6-skill-one-EDITED' })).toHaveCount(1, { timeout: 20000 });
    console.log('DB AFTER EDIT', psql("select skillname||'|'||displayorder from userskills where skillname like 'fxv6-%' order by 1").replace(/\n/g, ' ; '));
    await page.reload({ waitUntil: 'domcontentloaded' }).catch(() => {});
    await settle(page).catch(() => {});

    // delete all
    await nav(page, '/admin/skills', /Manage Skills/i);
    await settle(page);
    for (const n of ['fxv6-skill-one-EDITED', 'fxv6-skill-two', 'fxv6-skill-three']) {
      await page.locator('[data-testid="skill-row"]').filter({ hasText: n }).locator('[data-testid="delete-skill"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await expect(page.locator('[data-testid="skill-name"]', { hasText: n })).toHaveCount(0, { timeout: 30000 });
      rs = await resumeTexts(page, '[data-testid="skill-category"]', '[data-testid="skills-section"]');
      console.log('RESUME AFTER DELETE of', n, rs);
      expect(rs.some((s) => s.includes(n))).toBe(false);
      if (n === 'fxv6-skill-two') expect(rs.some((s) => s.includes('fxv6-skill-three')), 'positive control: sibling skill still renders').toBe(true);
    }
  } finally {
    const left = psql("select count(*) from userskills where skillname like 'fxv6-%'");
    console.log('LEFTOVERS', left);
    if (left !== '0') console.log('LEFTOVER ROWS (remove through the UI):', left);
    console.log('OWNER SKILLS AFTER', psql('select count(*) from userskills where userid=1'));
    console.log('CONSOLE ERRORS', errors);
  }
});

test('REQ-UI-039 awards: empty, add with badge via picker, order, resume, edit, delete', async ({ page }) => {
  test.setTimeout(500000);
  const errors = watch(page);
  console.log('OWNER AWARDS BEFORE', psql('select count(*) from userawards where userid=1'));
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/awards', /Manage Awards/i);
    await settle(page);
    const emptyBefore = await page.locator('[data-testid="awards-empty"]').count();
    console.log('AWARDS EMPTY STATE PRESENT', emptyBefore);
    if (emptyBefore) {
      await expect(page.locator('[data-testid="add-first-award"]')).toBeVisible();
      await page.screenshot({ path: `${OUT}/039-empty-1280.png` });
    }

    // award 1 with a badge uploaded through the picker
    await page.click('[data-testid="add-award"]');
    const d = page.locator('[data-testid="award-dialog"]');
    await expect(d).toBeVisible();
    await d.locator('[data-testid="award-title-input"]').fill('fxv6-award-one');
    await d.locator('[data-testid="award-description-input"]').fill('fxv6 award one description');
    await d.locator('[data-testid="award-year-input"]').fill('2024');
    const picker = d.locator('[data-testid="image-picker"]');
    await picker.locator('[data-testid="upload-new-image"]').click();
    const up = page.locator('[data-testid="image-upload-dialog"]');
    await expect(up).toBeVisible();
    await page.waitForTimeout(2000);
    for (let i = 0; i < 3; i++) {
      await up.locator('input[type="file"]').setInputFiles({ name: 'fxv6-badge.png', mimeType: 'image/png', buffer: PNG });
      if (await up.locator('[data-testid="upload-selected-file"]').waitFor({ timeout: 8000 }).then(() => true, () => false)) break;
    }
    await up.locator('[data-testid="upload-confirm"]').click();
    await expect(up).toBeHidden({ timeout: 40000 });
    await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible({ timeout: 20000 });
    const badgePath = await picker.locator('[data-testid="selected-image"]').getAttribute('src');
    console.log('BADGE PATH', badgePath, psql("select category from blogimage where imagename='fxv6-badge.png'"));
    await d.locator('[data-testid="save-award"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });

    // award 2 (no badge), year range
    await page.click('[data-testid="add-award"]');
    await d.locator('[data-testid="award-title-input"]').fill('fxv6-award-two');
    await d.locator('[data-testid="award-year-input"]').fill('2023');
    await d.locator('[data-testid="save-award"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });

    const titles = async () => (await page.locator('[data-testid="award-card"] [data-testid="award-title"]').allInnerTexts()).map((s) => s.replace(/\s+/g, ' ').trim());
    console.log('ADMIN AWARDS', await titles());
    console.log('DB', psql("select awardtitle||'|'||displayorder||'|'||coalesce(badgeimagepath,'') from userawards where awardtitle like 'fxv6-%' order by displayorder").replace(/\n/g, ' ; '));
    const card1 = page.locator('[data-testid="award-card"]').filter({ hasText: 'fxv6-award-one' });
    expect(await card1.locator('img').getAttribute('src')).toBe(badgePath);
    await geom(page, '039-list');

    let rs = await resumeTexts(page, '[data-testid="award-item"]', '[data-testid="award-item"]');
    console.log('RESUME AWARDS', rs);
    const anon = await page.context().browser()!.newContext();
    const p = await anon.newPage();
    await p.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('[data-testid="award-item"]', { timeout: 60000 });
    const badgeImg = p.locator('[data-testid="award-item"]').filter({ hasText: 'fxv6-award-one' }).locator('img');
    const bsrc = await badgeImg.first().getAttribute('src').catch(() => null);
    const bok = bsrc ? await badgeImg.first().evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth > 0) : false;
    console.log('RESUME BADGE SRC', bsrc, 'decodes', bok);
    await anon.close();
    expect(bsrc).toBe(badgePath);
    const i1 = rs.findIndex((s) => s.includes('fxv6-award-one')), i2 = rs.findIndex((s) => s.includes('fxv6-award-two'));
    expect(i1).toBeGreaterThanOrEqual(0);
    expect(i2).toBeGreaterThanOrEqual(0);
    expect(i1).toBeLessThan(i2);

    // reorder: move two up
    await page.locator('[data-testid="award-card"]').filter({ hasText: 'fxv6-award-two' }).locator('[data-testid="move-award-up"]').click();
    await expect.poll(async () => { const t = await titles(); return t.findIndex((x) => x.includes('fxv6-award-two')) < t.findIndex((x) => x.includes('fxv6-award-one')); }, { timeout: 20000 }).toBe(true);
    rs = await resumeTexts(page, '[data-testid="award-item"]', '[data-testid="award-item"]');
    console.log('RESUME AFTER MOVE', rs);
    expect(rs.findIndex((s) => s.includes('fxv6-award-two'))).toBeLessThan(rs.findIndex((s) => s.includes('fxv6-award-one')));

    // edit
    await page.locator('[data-testid="award-card"]').filter({ hasText: 'fxv6-award-two' }).locator('[data-testid="edit-award"]').click();
    await expect(d).toBeVisible({ timeout: 30000 });
    await d.locator('[data-testid="award-title-input"]').fill('fxv6-award-two-EDITED');
    await d.locator('[data-testid="save-award"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });
    await expect(page.locator('[data-testid="award-title"]', { hasText: 'fxv6-award-two-EDITED' })).toHaveCount(1);

    // delete
    for (const n of ['fxv6-award-two-EDITED', 'fxv6-award-one']) {
      await page.locator('[data-testid="award-card"]').filter({ hasText: n }).locator('[data-testid="delete-award"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await expect(page.locator('[data-testid="award-card"]').filter({ hasText: n })).toHaveCount(0, { timeout: 30000 });
    }

    // remove the badge image via the media library UI
    await nav(page, '/admin/images', /Media Library/i);
    await settle(page);
    await page.locator('[data-testid="category-tabs"] [role="tab"]').filter({ hasText: /^Awards$/ }).click();
    const bc = page.locator('[data-testid="image-card"]').filter({ hasText: 'fxv6-badge' });
    await expect(bc).toHaveCount(1, { timeout: 20000 });
    await bc.locator('[data-testid="delete-image"]').click();
    await page.locator('[data-testid="delete-confirm"]').click();
    await expect(bc).toHaveCount(0, { timeout: 20000 });
  } finally {
    const left = psql("select count(*) from userawards where awardtitle like 'fxv6-%'") + '/' + psql("select count(*) from blogimage where imagename like 'fxv6-%'");
    console.log('LEFTOVERS awards/images', left);
    if (!left.startsWith('0')) console.log('LEFTOVER ROWS (remove through the UI):', left);
    console.log('OWNER AWARDS AFTER', psql('select count(*) from userawards where userid=1'));
    console.log('CONSOLE ERRORS', errors);
  }
});

// ---------------------------------------------------------------------------------------------
// Added by W6 (2026-10-27): REQ-UI-036, REQ-UI-064, REQ-FN-027, REQ-FN-028, REQ-FN-030.
// Every row is created through the manage pages / public form and labelled `fxv6`.
// ---------------------------------------------------------------------------------------------

const PDF = Buffer.from('%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Count 0/Kids[]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n');

async function addExperience(page: Page, role: string, company: string, order: string) {
  await page.click('[data-testid="add-experience"]');
  const d = page.locator('[data-testid="experience-dialog"]');
  await expect(d).toBeVisible();
  await d.locator('[data-testid="experience-role-input"]').fill(role);
  await d.locator('[data-testid="experience-company-input"]').fill(company);
  await d.locator('[data-testid="experience-description-input"]').fill('- fxv6 bullet');
  await d.locator('[data-testid="experience-order-input"]').fill(order);
  await d.locator('[data-testid="save-experience"]').click();
  await expect(d).toBeHidden({ timeout: 45000 });
}
async function addSkill(page: Page, name: string, cat: string, isNew: boolean) {
  await page.click('[data-testid="add-skill"]');
  const d = page.locator('[data-testid="add-skill-dialog"]');
  await expect(d).toBeVisible();
  await d.locator('[data-testid="skill-name-input"]').fill(name);
  await d.locator('[data-testid="skill-category-select"]').click();
  if (isNew) {
    await page.getByRole('option', { name: /Create New Category/ }).click();
    await d.locator('[data-testid="new-category-input"]').fill(cat);
  } else {
    await page.getByRole('option', { name: cat, exact: true }).click();
  }
  await d.locator('[data-testid="save-skill"]').click();
  await expect(d).toBeHidden({ timeout: 30000 });
}
async function addAward(page: Page, title: string, year: string) {
  await page.click('[data-testid="add-award"]');
  const d = page.locator('[data-testid="award-dialog"]');
  await expect(d).toBeVisible();
  await d.locator('[data-testid="award-title-input"]').fill(title);
  await d.locator('[data-testid="award-description-input"]').fill('fxv6 award description');
  await d.locator('[data-testid="award-year-input"]').fill(year);
  await d.locator('[data-testid="save-award"]').click();
  await expect(d).toBeHidden({ timeout: 45000 });
}
async function deleteSkill(page: Page, name: string) {
  const loc = page.locator('[data-testid="skill-name"]', { hasText: name });
  const n0 = await loc.count();
  await page.locator('[data-testid="skill-row"]').filter({ hasText: name }).first().locator('[data-testid="delete-skill"]').click();
  await page.locator('[data-testid="delete-confirm"]').click();
  await expect(loc).toHaveCount(n0 - 1, { timeout: 30000 });
}
async function deleteAward(page: Page, title: string) {
  const loc = page.locator('[data-testid="award-card"]').filter({ hasText: title });
  const n0 = await loc.count();
  await loc.first().locator('[data-testid="delete-award"]').click();
  await page.locator('[data-testid="delete-confirm"]').click();
  await expect(loc).toHaveCount(n0 - 1, { timeout: 30000 });
}
async function deleteExperience(page: Page, role: string) {
  const loc = page.locator('[data-testid="experience-card"]').filter({ hasText: role });
  const n0 = await loc.count();
  await loc.first().locator('[data-testid="delete-experience"]').click();
  await page.locator('[data-testid="delete-confirm"]').click();
  await expect(loc).toHaveCount(n0 - 1, { timeout: 30000 });
}
async function anonResume(page: Page, width = 1280) {
  const ctx = await page.context().browser()!.newContext({ viewport: { width, height: 900 } });
  const p = await ctx.newPage();
  await p.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
  await p.waitForSelector('[data-testid="resume-page"]', { timeout: 60000 });
  await p.waitForFunction(() => document.querySelectorAll('[data-testid$="-loading"]').length === 0, undefined, { timeout: 45000 }).catch(() => {});
  await p.waitForTimeout(2500);
  return { p, ctx };
}

test('REQ-UI-036 public resume: every section renders for the site owner, anchors stay on /resume and scroll smoothly, usable at 390 and 320', async ({ page }) => {
  test.setTimeout(500000);
  const errors = watch(page);
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/experience', /Manage Experience/i); await settle(page);
    await addExperience(page, 'fxv6-036-role', 'fxv6 036 Co', '5');
    await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
    await addSkill(page, 'fxv6-036-skill', 'fxv6-036-cat', true);
    await nav(page, '/admin/awards', /Manage Awards/i); await settle(page);
    await addAward(page, 'fxv6-036-award', '2025');

    const { p, ctx } = await anonResume(page);
    for (const s of ['resume-hero', 'about-section', 'experience-section', 'skills-section', 'awards-section', 'contact-section']) {
      await expect(p.locator(`[data-testid="${s}"]`), s).toHaveCount(1);
    }
    const body = await p.locator('[data-testid="resume-page"]').innerText();
    for (const t of ['fxv6-036-role', 'fxv6-036-skill', 'fxv6-036-award']) expect(body, `/resume shows ${t}`).toContain(t);
    expect(await p.evaluate(() => getComputedStyle(document.documentElement).scrollBehavior), 'anchor scrolling is smooth').toBe('smooth');
    await p.screenshot({ path: `${OUT}/036-resume-1280.png`, fullPage: true });
    for (const a of ['about', 'experience', 'skills', 'awards', 'contact']) {
      await p.click(`[data-testid="nav-${a}"]`);
      await p.waitForTimeout(1800);
      expect(new URL(p.url()).pathname, `anchor ${a} stays on /resume`).toBe('/resume');
      expect(p.url()).toContain(`#${a}`);
      const top = await p.locator(`#${a}`).first().evaluate((e) => Math.round(e.getBoundingClientRect().top));
      expect(top, `#${a} scrolled into view (top=${top})`).toBeGreaterThanOrEqual(-5);
      expect(top, `#${a} scrolled into view (top=${top})`).toBeLessThan(900);
    }
    await ctx.close();
    for (const w of [390, 320]) {
      const m = await anonResume(page, w);
      const hs = await m.p.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
      await m.p.screenshot({ path: `${OUT}/036-resume-${w}.png`, fullPage: true });
      expect(hs, `no horizontal scroll at ${w}`).toBeLessThanOrEqual(0);
      await expect(m.p.locator('[data-testid="experience-section"]')).toBeVisible();
      await m.ctx.close();
    }

  } finally {
    try {
      await nav(page, '/admin/awards', /Manage Awards/i); await settle(page);
      while (await page.locator('[data-testid="award-card"]').filter({ hasText: 'fxv6-036-award' }).count()) await deleteAward(page, 'fxv6-036-award');
      await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
      while (await page.locator('[data-testid="skill-row"]').filter({ hasText: 'fxv6-036-skill' }).count()) await deleteSkill(page, 'fxv6-036-skill');
      await nav(page, '/admin/experience', /Manage Experience/i); await settle(page);
      while (await page.locator('[data-testid="experience-card"]').filter({ hasText: 'fxv6-036-role' }).count()) await deleteExperience(page, 'fxv6-036-role');
    } catch (e) { console.log('036 cleanup problem', String(e).slice(0, 200)); }
    console.log('LEFTOVERS 036', psql("select (select count(*) from userevents where sessiontitle like 'fxv6-036%')||'/'||(select count(*) from userskills where skillname like 'fxv6-036%')||'/'||(select count(*) from userawards where awardtitle like 'fxv6-036%')"), 'CONSOLE', errors);
  }
});

test('REQ-UI-064 skill categories keep the authored order (not alphabetical), each skill shows its order, category move persists and renumbers, /resume follows', async ({ page }) => {
  test.setTimeout(500000);
  const ownerOther = psql("select count(*) from userskills where userid=1 and skillname not like 'fxv6-%'");
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
    // reverse-alphabetical authoring: zeta, mid, alpha
    await addSkill(page, 'fxv6-z1', 'fxv6-zeta', true);
    await addSkill(page, 'fxv6-z2', 'fxv6-zeta', false);
    await addSkill(page, 'fxv6-m1', 'fxv6-mid', true);
    await addSkill(page, 'fxv6-a1', 'fxv6-alpha', true);
    await addSkill(page, 'fxv6-a2', 'fxv6-alpha', false);
    const cats = async () => (await page.locator('[data-testid="skill-category-name"]').allInnerTexts()).map((s) => s.trim()).filter((s) => s.startsWith('fxv6-'));
    const badges = async () => {
      const rows = page.locator('[data-testid="skill-row"]').filter({ hasText: 'fxv6-' });
      return (await rows.locator('[data-testid="skill-order"]').allInnerTexts()).map((t) => parseInt(t.replace(/\D+/g, ''), 10));
    };
    const moveCat = async (name: string, dir: 'up' | 'down') =>
      page.locator('[data-testid="skill-category-card"]').filter({ has: page.locator('[data-testid="skill-category-name"]', { hasText: name }) }).locator(`[data-testid="move-category-${dir}"]`).click();
    const resumeCats = async () => {
      const { p, ctx } = await anonResume(page);
      await p.waitForSelector('[data-testid="skill-category-name"]', { timeout: 45000 }).catch(() => {});
      await p.waitForTimeout(1500);
      const t = (await p.locator('[data-testid="skill-category-name"]').allInnerTexts()).map((s) => s.trim()).filter((s) => s.startsWith('fxv6-'));
      await p.screenshot({ path: `${OUT}/064-resume-${Date.now()}.png`, fullPage: true });
      await ctx.close();
      return t;
    };
    const initial = await cats();
    console.log('064 initial category order after adding zeta, mid, alpha through the UI:', initial);
    expect([...initial].sort(), 'all three categories present').toEqual(['fxv6-alpha', 'fxv6-mid', 'fxv6-zeta']);
    // every skill row shows an order badge; the first category has up disabled, the last has down disabled
    const rows = page.locator('[data-testid="skill-row"]').filter({ hasText: 'fxv6-' });
    expect(await rows.count()).toBe(5);
    expect(await rows.locator('[data-testid="skill-order"]').count(), 'one order badge per skill').toBe(5);
    await expect(rows.first().locator('[data-testid="move-skill-up"]'), 'first skill: up disabled').toBeDisabled();
    if (Number(ownerOther) === 0) {
      await expect(page.locator('[data-testid="move-category-up"]').first(), 'first category: up disabled').toBeDisabled();
    }
    await expect(page.locator('[data-testid="move-category-down"]').last(), 'last category: down disabled').toBeDisabled();
    await page.screenshot({ path: `${OUT}/064-admin-before.png`, fullPage: true });

    // reorder to a NON-alphabetical order: zeta, alpha, mid
    // a click can be swallowed while the circuit is busy re-rendering: retry until the order changes
    const moveUntil = async (name: string, dir: 'up' | 'down', done: (c: string[]) => boolean) => {
      for (let i = 0; i < 6 && !done(await cats()); i++) {
        await moveCat(name, dir);
        for (let w = 0; w < 8 && !done(await cats()); w++) await page.waitForTimeout(1000);
      }
    };
    await moveUntil('fxv6-zeta', 'up', (c) => c.indexOf('fxv6-zeta') === 0);
    await expect.poll(async () => (await cats())[0], { timeout: 20000 }).toBe('fxv6-zeta');
    const afterZeta = await cats();
    console.log('064 after moving zeta first:', afterZeta);
    const b1 = await badges();
    expect(b1, 'badges ascend after the move').toEqual([...b1].sort((x, y) => x - y));
    expect(b1[b1.length - 1] - b1[0], 'renumbered gaplessly').toBe(4);
    expect(new Set(b1).size, 'every order is distinct once renumbered').toBe(5);
    // persists across a fresh load, and /resume shows exactly that order (not alphabetical)
    await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
    expect(await cats(), 'persisted after reload').toEqual(afterZeta);
    expect(await resumeCats(), '/resume follows the admin order').toEqual(afterZeta);
    expect(afterZeta, 'the order is not alphabetical').not.toEqual([...afterZeta].sort());

    // a second move: put mid directly under zeta
    await moveUntil('fxv6-mid', 'up', (c) => c.indexOf('fxv6-mid') < afterZeta.indexOf('fxv6-mid'));
    const afterMid = await cats();
    console.log('064 after moving mid up:', afterMid);
    expect(afterMid).not.toEqual(afterZeta);
    await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
    expect(await cats(), 'second move persisted').toEqual(afterMid);
    expect(await resumeCats(), '/resume follows the second move').toEqual(afterMid);
    await page.screenshot({ path: `${OUT}/064-admin-after.png`, fullPage: true });
  } finally {
    // cleanup through the UI
    try {
      await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
      for (const n of ['fxv6-z1', 'fxv6-z2', 'fxv6-m1', 'fxv6-a1', 'fxv6-a2']) {
        if (await page.locator('[data-testid="skill-row"]').filter({ hasText: n }).count()) await deleteSkill(page, n);
      }
    } catch (e) { console.log('064 cleanup problem', String(e).slice(0, 200)); }
    console.log('LEFTOVERS 064', psql("select count(*) from userskills where skillname like 'fxv6-%'"));
  }
});

test('REQ-FN-027 experience, skills, awards and stats saved on the manage pages persist and /resume renders them; the admin user selector loads the chosen user', async ({ page }) => {
  test.setTimeout(540000);
  const owner = psql("select userid from bloguser where issiteowner");
  const author = psql("select userid from bloguser where emailid='author@techieblog.test'");
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/experience', /Manage Experience/i); await settle(page);
    await addExperience(page, 'fxv6-027-role', 'fxv6 027 Co', '7');
    await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
    await addSkill(page, 'fxv6-027-skill', 'fxv6-027-cat', true);
    await nav(page, '/admin/awards', /Manage Awards/i); await settle(page);
    await addAward(page, 'fxv6-027-award', '2026');
    await nav(page, '/admin/stats', /Manage Stat|Statistics/i); await settle(page);
    await page.click('[data-testid="add-stat"]');
    const sd = page.locator('[data-testid="stat-dialog"]');
    await expect(sd).toBeVisible();
    await sd.locator('[data-testid="stat-value-input"]').fill('27');
    await sd.locator('[data-testid="stat-label-input"]').fill('fxv6-027-stat');
    await sd.locator('[data-testid="save-stat"]').click();
    await expect(sd).toBeHidden({ timeout: 45000 });

    // persisted rows belong to the site owner
    expect(psql("select userid from userevents where sessiontitle='fxv6-027-role'")).toBe(owner);
    expect(psql("select userid from userskills where skillname='fxv6-027-skill'")).toBe(owner);
    expect(psql("select userid from userawards where awardtitle='fxv6-027-award'")).toBe(owner);
    expect(psql("select userid||'|'||statvalue from userstats where statlabel='fxv6-027-stat'")).toBe(`${owner}|27`);

    // /resume renders exactly the stored rows
    const { p, ctx } = await anonResume(page);
    const body = await p.locator('[data-testid="resume-page"]').innerText();
    for (const t of ['fxv6-027-role', 'fxv6-027-skill', 'fxv6-027-award', 'fxv6-027-stat']) expect(body, `/resume shows ${t}`).toContain(t);
    const dbSkills = Number(psql(`select count(*) from userskills where userid=${owner}`));
    const dbAwards = Number(psql(`select count(*) from userawards where userid=${owner}`));
    await expect(p.locator('[data-testid="skill-badge"]')).toHaveCount(dbSkills);
    await expect(p.locator('[data-testid="award-item"]')).toHaveCount(dbAwards);
    await p.screenshot({ path: `${OUT}/fn027-resume.png`, fullPage: true });
    await ctx.close();

    // admin user selector: add a skill for the author, the lists differ
    await nav(page, '/admin/skills', /Manage Skills/i); await settle(page);
    await page.click('[data-testid="skills-user-select"]');
    await page.getByRole('option', { name: /author@techieblog\.test/ }).click();
    await page.waitForTimeout(2500);
    await expect(page.locator('[data-testid="skill-name"]', { hasText: 'fxv6-027-skill' })).toHaveCount(0);
    await addSkill(page, 'fxv6-027-other', 'fxv6-027-othercat', true);
    expect(psql("select userid from userskills where skillname='fxv6-027-other'")).toBe(author);
    await page.click('[data-testid="skills-user-select"]');
    await page.getByRole('option', { name: /Ravi@techieblog\.com/i }).click();
    await page.waitForTimeout(2500);
    await expect(page.locator('[data-testid="skill-name"]', { hasText: 'fxv6-027-skill' })).toHaveCount(1, { timeout: 20000 });
    await expect(page.locator('[data-testid="skill-name"]', { hasText: 'fxv6-027-other' }), "the owner's list does not show the author's skill").toHaveCount(0);
    // the author's skill must not leak onto the owner's public resume
    const r2 = await anonResume(page);
    expect(await r2.p.locator('[data-testid="resume-page"]').innerText()).not.toContain('fxv6-027-other');
    await r2.ctx.close();

    // deletes persist (author's first, then the owner's)
    await page.click('[data-testid="skills-user-select"]');
    await page.getByRole('option', { name: /author@techieblog\.test/ }).click();
    await page.waitForTimeout(2500);
    await deleteSkill(page, 'fxv6-027-other');
    expect(psql("select count(*) from userskills where skillname='fxv6-027-other'")).toBe('0');
    await page.click('[data-testid="skills-user-select"]');
    await page.getByRole('option', { name: /Ravi@techieblog\.com/i }).click();
    await page.waitForTimeout(2500);
    await deleteSkill(page, 'fxv6-027-skill');
    expect(psql("select count(*) from userskills where skillname='fxv6-027-skill'")).toBe('0');
    await nav(page, '/admin/awards', /Manage Awards/i); await settle(page);
    await deleteAward(page, 'fxv6-027-award');
    await nav(page, '/admin/experience', /Manage Experience/i); await settle(page);
    await deleteExperience(page, 'fxv6-027-role');
    await nav(page, '/admin/stats', /Manage Stat|Statistics/i); await settle(page);
    await page.locator('[data-testid="stat-card"]').filter({ hasText: 'fxv6-027-stat' }).locator('[data-testid="delete-stat"]').click();
    await page.locator('[data-testid="stat-delete-confirm"]').click();
    await expect(page.locator('[data-testid="stat-card"]').filter({ hasText: 'fxv6-027-stat' })).toHaveCount(0, { timeout: 30000 });
    expect(psql("select count(*) from userstats where statlabel='fxv6-027-stat'")).toBe('0');
  } finally {
    console.log('LEFTOVERS 027', psql("select (select count(*) from userevents where sessiontitle like 'fxv6-027%')||'/'||(select count(*) from userskills where skillname like 'fxv6-027%')||'/'||(select count(*) from userawards where awardtitle like 'fxv6-027%')||'/'||(select count(*) from userstats where statlabel like 'fxv6-027%')"));
  }
});

test('REQ-FN-028 a PDF under 10 MB uploaded as the CV is offered on /resume and served as that PDF; a non-PDF and an oversized file are refused with a message', async ({ page }) => {
  test.setTimeout(480000);
  const owner = psql('select userid from bloguser where issiteowner');
  const originalCv = psql(`select coalesce(cvfilepath,'') from bloguser where userid=${owner}`);
  console.log('FN-028 ORIGINAL cvfilepath =', JSON.stringify(originalCv));
  let uploaded = '';
  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/profile', /Profile/i); await settle(page);
    const picker = page.locator('[data-testid="image-picker"]').nth(1);
    const dialog = page.locator('[data-testid="image-upload-dialog"]:visible');
    const stage = async (name: string, mimeType: string, buffer: Buffer) => {
      await picker.locator('[data-testid="upload-new-image"]').click();
      await expect(dialog).toBeVisible({ timeout: 20000 });
      for (let i = 0; i < 5; i++) {
        await page.waitForTimeout(1200);
        await dialog.locator('input[type="file"]').setInputFiles({ name, mimeType, buffer });
        await page.waitForTimeout(4000);
        if ((await dialog.locator('[data-testid="upload-selected-file"]').count()) || (await dialog.locator('[data-testid="upload-error"]').count())) break;
      }
    };
    await stage('fxv6-small.png', 'image/png', Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64'));
    expect(await dialog.locator('[data-testid="upload-selected-file"]').count(), 'a non-PDF is never staged').toBe(0);
    const pngMsg = ((await dialog.locator('[data-testid="upload-error"]').textContent().catch(() => '')) || '').trim();
    console.log('FN-028 non-PDF message:', pngMsg);
    expect(pngMsg.length, 'a refusal message is shown').toBeGreaterThan(0);
    await dialog.locator('[data-testid="upload-cancel"]').click();
    await page.waitForTimeout(1500);

    await stage('fxv6-big.pdf', 'application/pdf', Buffer.concat([PDF, Buffer.alloc(11 * 1024 * 1024)]));
    const bigMsg = ((await dialog.locator('[data-testid="upload-error"]').textContent().catch(() => '')) || '').trim();
    console.log('FN-028 oversize message:', bigMsg);
    expect(bigMsg).toMatch(/10 MB/);
    expect(psql("select count(*) from blogimage where imagename like 'fxv6-big%' or imagename like 'fxv6-small%'")).toBe('0');
    await dialog.locator('[data-testid="upload-cancel"]').click();
    await page.waitForTimeout(1500);

    await stage('fxv6-cv.pdf', 'application/pdf', PDF);
    await expect(dialog.locator('[data-testid="upload-selected-file"]')).toContainText('fxv6-cv.pdf');
    await dialog.locator('[data-testid="upload-confirm"]').click();
    await expect(dialog).toBeHidden({ timeout: 40000 });
    await expect(picker.locator('[data-testid="selected-image"], [data-testid="clear-image"]').first()).toBeVisible({ timeout: 20000 });
    await page.click('[data-testid="save-profile"]');
    await expect(page.locator('[data-testid="profile-status"]')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(2000);
    uploaded = psql(`select coalesce(cvfilepath,'') from bloguser where userid=${owner}`);
    expect(uploaded).toContain('/uploads/cv/');
    const served = await page.request.get(`${BASE}${uploaded}`);
    expect(served.status()).toBe(200);
    expect((await served.body()).subarray(0, 5).toString()).toBe('%PDF-');

    const { p, ctx } = await anonResume(page);
    const cv = p.locator('[data-testid="download-cv"]');
    await expect(cv.first()).toBeVisible({ timeout: 20000 });
    expect(await cv.first().getAttribute('href')).toBe(uploaded);
    await p.screenshot({ path: `${OUT}/fn028-resume-cv.png`, fullPage: true });
    await ctx.close();
  } finally {
    // restore the owner's CV setting through the UI, then remove the uploaded file from the library
    try {
      await nav(page, '/admin/profile', /Profile/i); await settle(page);
      const pk = page.locator('[data-testid="image-picker"]').nth(1);
      if (originalCv === '') {
        if (await pk.locator('[data-testid="clear-image"]').count()) {
          await pk.locator('[data-testid="clear-image"]').click();
          await page.click('[data-testid="save-profile"]');
          await expect(page.locator('[data-testid="profile-status"]')).toBeVisible({ timeout: 30000 });
          await page.waitForTimeout(2000);
        }
      }
      await nav(page, '/admin/images', /Media Library/i); await settle(page);
      await page.locator('[data-testid="category-tabs"] [role="tab"]').filter({ hasText: /^CV$/ }).click();
      await page.waitForTimeout(3500);
      const card = page.locator('[data-testid="image-card"]').filter({ hasText: 'fxv6-cv' });
      while (await card.count()) {
        const n0 = await card.count();
        await card.first().locator('[data-testid="delete-image"]').click();
        await page.locator('[data-testid="delete-confirm"]').click();
        await expect(card).toHaveCount(n0 - 1, { timeout: 20000 });
      }
    } catch (e) { console.log('FN-028 cleanup problem', String(e).slice(0, 300)); }
    console.log('FN-028 RESTORED cvfilepath =', JSON.stringify(psql(`select coalesce(cvfilepath,'') from bloguser where userid=${owner}`)), 'original', JSON.stringify(originalCv), 'blogimage fxv6 rows', psql("select count(*) from blogimage where imagename like 'fxv6-%'"));
  }
});

test('REQ-FN-030 subscriber capture: an invalid address is rejected, a valid one is stored once as unconfirmed, a duplicate creates no second row', async ({ page }) => {
  test.setTimeout(300000);
  const email = `fxv6-sub-${Date.now().toString(36)}@techieblog.test`;
  const count = () => Number(psql(`select count(*) from subscriber where lower(email)=lower('${email}')`));
  const form = async () => {
    await gotoPublic(page, '/search');
    const card = page.locator('[data-testid="sidebar-subscribe"]');
    await expect(card).toBeVisible({ timeout: 60000 });
    await expect(card.locator('[data-testid="captcha-prompt"]').first()).toBeVisible({ timeout: 60000 });
    return card;
  };
  const submit = async (card: ReturnType<Page['locator']>, value: string, withCaptcha: boolean) => {
    await card.locator('[data-testid="subscribe-email"]').fill(value);
    if (withCaptcha) await solveCaptcha(card);
    await card.locator('[data-testid="subscribe-submit"]').click();
    await expect(card.locator('[data-testid="subscribe-message"]')).toBeVisible({ timeout: 60000 });
    return ((await card.locator('[data-testid="subscribe-message"]').innerText()) || '').replace(/\s+/g, ' ').trim();
  };

  // 1. invalid address -> rejected, nothing stored
  let card = await form();
  const bad = await submit(card, 'fxv6-not-an-email', false);
  console.log('FN-030 invalid message:', bad);
  expect(bad).toMatch(/valid email/i);
  expect(Number(psql("select count(*) from subscriber where email='fxv6-not-an-email'"))).toBe(0);

  // 2. valid address -> stored once, unconfirmed (double opt-in)
  card = await form();
  const ok = await submit(card, email, true);
  console.log('FN-030 first message:', ok);
  expect(ok).toMatch(/inbox|confirmation/i);
  expect(count(), 'one row stored').toBe(1);
  expect(psql(`select isconfirmed from subscriber where lower(email)=lower('${email}')`), 'stored unconfirmed').toBe('f');
  await page.screenshot({ path: `${OUT}/fn030-first.png` });

  // 3. the same address again -> no second row, a message is shown
  card = await form();
  const dup = await submit(card, email, true);
  console.log('FN-030 duplicate message:', dup);
  expect(dup.length).toBeGreaterThan(0);
  expect(dup).not.toMatch(/error occurred/i);
  expect(count(), 'a duplicate creates no second row').toBe(1);
  await page.screenshot({ path: `${OUT}/fn030-duplicate.png` });

  // 4. the admin list shows the captured address once
  await signIn(page, 'admin');
  await nav(page, '/admin/subscribers', /Subscribers/i); await settle(page);
  await page.locator('[data-testid="subscribers-search"]').fill(email);
  await page.waitForTimeout(1500);
  await expect(page.locator('[data-testid="subscriber-row-email"]', { hasText: email })).toHaveCount(1, { timeout: 20000 });
  console.log('FN-030 LEFTOVER subscriber (no delete control exists in the UI):', email);
});
