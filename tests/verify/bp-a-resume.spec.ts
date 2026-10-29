import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { BASE, nav, visualCheck } from './_gates';
import { psql, signIn, settle } from './vall-resume-helpers';

/**
 * Build-phase 2026-10-27 cluster A verify (group resume): REQ-UI-037, -038, -039.
 * Rows created carry the fxa- prefix (owner = userid 1) and are removed through the UI.
 */
const OUT = 'tests/.artifacts/build-phase-20261027/a/resume';
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
  console.log('OWNER EXPERIENCE BEFORE', before, psql("select md5(string_agg(t::text,'' order by eventid)) from userevents t where userid=1 and sessiontitle not like 'fxa-%'"));
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
      await d.locator('[data-testid="experience-description-input"]').fill('- fxa bullet');
      await d.locator('[data-testid="experience-order-input"]').fill(order);
      await d.locator('[data-testid="save-experience"]').click();
      await expect(d).toBeHidden({ timeout: 45000 });
    };
    await add('fxa-exp-late', 'fxa Co Late', '91');
    await add('fxa-exp-early', 'fxa Co Early', '90');
    const roles = await page.locator('[data-testid="experience-role"]').allInnerTexts();
    console.log('ADMIN LIST ORDER', roles);
    expect(roles.indexOf('fxa-exp-early')).toBeGreaterThanOrEqual(0);
    expect(roles.indexOf('fxa-exp-early')).toBeLessThan(roles.indexOf('fxa-exp-late'));
    console.log('DB', psql("select sessiontitle||':'||displayorder from userevents where sessiontitle like 'fxa-exp%' order by displayorder"));
    await geom(page, '037-list');

    let rs = await resumeTexts(page, '[data-testid="experience-item"]', '[data-testid="experience-item"]');
    console.log('RESUME ITEMS', rs);
    const iE = rs.findIndex((s) => s.includes('fxa-exp-early')), iL = rs.findIndex((s) => s.includes('fxa-exp-late'));
    expect(iE).toBeGreaterThanOrEqual(0);
    expect(iE).toBeLessThan(iL);

    // edit
    const card = page.locator('[data-testid="experience-card"]').filter({ hasText: 'fxa-exp-late' });
    await card.locator('[data-testid="edit-experience"]').click();
    const d = page.locator('[data-testid="experience-dialog"]');
    await expect(d).toBeVisible({ timeout: 30000 });
    await expect(d.locator('[data-testid="experience-role-input"]')).toHaveValue('fxa-exp-late');
    await d.locator('[data-testid="experience-role-input"]').fill('fxa-exp-late-EDITED');
    await d.locator('[data-testid="experience-order-input"]').fill('89');
    await d.locator('[data-testid="save-experience"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });
    const roles2 = await page.locator('[data-testid="experience-role"]').allInnerTexts();
    console.log('AFTER EDIT', roles2);
    expect(roles2.indexOf('fxa-exp-late-EDITED')).toBeLessThan(roles2.indexOf('fxa-exp-early'));
    rs = await resumeTexts(page, '[data-testid="experience-item"]', '[data-testid="experience-item"]');
    console.log('RESUME AFTER EDIT', rs);
    expect(rs.findIndex((s) => s.includes('fxa-exp-late-EDITED'))).toBeLessThan(rs.findIndex((s) => s.includes('fxa-exp-early')));

    // delete both
    for (const name of ['fxa-exp-late-EDITED', 'fxa-exp-early']) {
      await page.locator('[data-testid="experience-card"]').filter({ hasText: name }).locator('[data-testid="delete-experience"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await expect(page.locator('[data-testid="experience-card"]').filter({ hasText: name })).toHaveCount(0, { timeout: 30000 });
      rs = await resumeTexts(page, '[data-testid="experience-item"]', '[data-testid="experience-item"]');
      console.log('RESUME AFTER DELETE of', name, rs);
      expect(rs.some((s) => s.includes(name))).toBe(false);
      if (name === 'fxa-exp-late-EDITED') expect(rs.some((s) => s.includes('fxa-exp-early')), 'positive control: sibling row still renders').toBe(true);
    }
  } finally {
    const left = psql("select count(*) from userevents where sessiontitle like 'fxa-exp%'");
    console.log('LEFTOVERS', left);
    if (left !== '0') psql("delete from userevents where sessiontitle like 'fxa-exp%'");
    console.log('OWNER EXPERIENCE AFTER', psql("select count(*) from userevents where type='Experience' and userid=1"), psql("select md5(string_agg(t::text,'' order by eventid)) from userevents t where userid=1 and sessiontitle not like 'fxa-%'"));
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
    await addSkill('fxa-skill-one', null, 'fxa-cat-a');       // new category
    await addSkill('fxa-skill-two', 'fxa-cat-a');             // existing category
    await addSkill('fxa-skill-three', null, 'fxa-cat-b');     // second new category
    const catCard = (c: string) => page.locator('[data-testid="skill-category-card"]').filter({ has: page.locator('[data-testid="skill-category-name"]', { hasText: c }) });
    const names = async (c: string) => (await catCard(c).locator('[data-testid="skill-name"]').allInnerTexts());
    console.log('CAT A', await names('fxa-cat-a'), 'CAT B', await names('fxa-cat-b'));
    expect(await names('fxa-cat-a')).toEqual(['fxa-skill-one', 'fxa-skill-two']);
    console.log('DB', psql("select skillname||'|'||category||'|'||displayorder from userskills where skillname like 'fxa-%' order by 1").replace(/\n/g, ' ; '));
    await geom(page, '038-list');

    let rs = await resumeTexts(page, '[data-testid="skill-category"]', '[data-testid="skill-category"]');
    console.log('RESUME SKILLS', rs);
    expect(rs.some((s) => s.includes('fxa-cat-a') && s.includes('fxa-skill-one') && s.includes('fxa-skill-two'))).toBe(true);

    // reorder within category
    await catCard('fxa-cat-a').locator('[data-testid="skill-row"]').nth(1).locator('[data-testid="move-skill-up"]').click();
    await expect.poll(async () => (await names('fxa-cat-a')).join(','), { timeout: 20000 }).toBe('fxa-skill-two,fxa-skill-one');
    rs = await resumeTexts(page, '[data-testid="skill-category"]', '[data-testid="skill-category"]');
    console.log('RESUME AFTER MOVE', rs);
    const a = rs.find((s) => s.includes('fxa-cat-a'))!;
    expect(a.indexOf('fxa-skill-two')).toBeLessThan(a.indexOf('fxa-skill-one'));

    // reorder categories
    const catNames = async () => page.locator('[data-testid="skill-category-name"]').allInnerTexts();
    const preCats = await catNames();
    await catCard('fxa-cat-b').locator('[data-testid="move-category-up"]').click();
    await expect.poll(async () => { const c = await catNames(); return c.indexOf('fxa-cat-b') < c.indexOf('fxa-cat-a'); }, { timeout: 20000 }).toBe(true);
    console.log('CATS', preCats, '->', await catNames());

    // edit (rename) a skill, and persistence across reload
    const row = catCard('fxa-cat-a').locator('[data-testid="skill-row"]').filter({ hasText: 'fxa-skill-one' });
    await row.locator('[data-testid="edit-skill"]').click();
    await page.locator('[data-testid="skill-name-edit-input"]').fill('fxa-skill-one-EDITED');
    await page.locator('[data-testid="save-skill-edit"]').click();
    await expect(page.locator('[data-testid="skill-name"]', { hasText: 'fxa-skill-one-EDITED' })).toHaveCount(1, { timeout: 20000 });
    console.log('DB AFTER EDIT', psql("select skillname||'|'||displayorder from userskills where skillname like 'fxa-%' order by 1").replace(/\n/g, ' ; '));
    await page.reload({ waitUntil: 'domcontentloaded' }).catch(() => {});
    await settle(page).catch(() => {});

    // delete all
    await nav(page, '/admin/skills', /Manage Skills/i);
    await settle(page);
    for (const n of ['fxa-skill-one-EDITED', 'fxa-skill-two', 'fxa-skill-three']) {
      await page.locator('[data-testid="skill-row"]').filter({ hasText: n }).locator('[data-testid="delete-skill"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await expect(page.locator('[data-testid="skill-name"]', { hasText: n })).toHaveCount(0, { timeout: 30000 });
      rs = await resumeTexts(page, '[data-testid="skill-category"]', '[data-testid="skills-section"]');
      console.log('RESUME AFTER DELETE of', n, rs);
      expect(rs.some((s) => s.includes(n))).toBe(false);
      if (n === 'fxa-skill-two') expect(rs.some((s) => s.includes('fxa-skill-three')), 'positive control: sibling skill still renders').toBe(true);
    }
  } finally {
    const left = psql("select count(*) from userskills where skillname like 'fxa-%'");
    console.log('LEFTOVERS', left);
    if (left !== '0') psql("delete from userskills where skillname like 'fxa-%'");
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
    await d.locator('[data-testid="award-title-input"]').fill('fxa-award-one');
    await d.locator('[data-testid="award-description-input"]').fill('fxa award one description');
    await d.locator('[data-testid="award-year-input"]').fill('2024');
    const picker = d.locator('[data-testid="image-picker"]');
    await picker.locator('[data-testid="upload-new-image"]').click();
    const up = page.locator('[data-testid="image-upload-dialog"]');
    await expect(up).toBeVisible();
    await page.waitForTimeout(2000);
    for (let i = 0; i < 3; i++) {
      await up.locator('input[type="file"]').setInputFiles({ name: 'fxa-badge.png', mimeType: 'image/png', buffer: PNG });
      if (await up.locator('[data-testid="upload-selected-file"]').waitFor({ timeout: 8000 }).then(() => true, () => false)) break;
    }
    await up.locator('[data-testid="upload-confirm"]').click();
    await expect(up).toBeHidden({ timeout: 40000 });
    await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible({ timeout: 20000 });
    const badgePath = await picker.locator('[data-testid="selected-image"]').getAttribute('src');
    console.log('BADGE PATH', badgePath, psql("select category from blogimage where imagename='fxa-badge.png'"));
    await d.locator('[data-testid="save-award"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });

    // award 2 (no badge), year range
    await page.click('[data-testid="add-award"]');
    await d.locator('[data-testid="award-title-input"]').fill('fxa-award-two');
    await d.locator('[data-testid="award-year-input"]').fill('2023');
    await d.locator('[data-testid="save-award"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });

    const titles = async () => (await page.locator('[data-testid="award-card"] [data-testid="award-title"]').allInnerTexts()).map((s) => s.replace(/\s+/g, ' ').trim());
    console.log('ADMIN AWARDS', await titles());
    console.log('DB', psql("select awardtitle||'|'||displayorder||'|'||coalesce(badgeimagepath,'') from userawards where awardtitle like 'fxa-%' order by displayorder").replace(/\n/g, ' ; '));
    const card1 = page.locator('[data-testid="award-card"]').filter({ hasText: 'fxa-award-one' });
    expect(await card1.locator('img').getAttribute('src')).toBe(badgePath);
    await geom(page, '039-list');

    let rs = await resumeTexts(page, '[data-testid="award-item"]', '[data-testid="award-item"]');
    console.log('RESUME AWARDS', rs);
    const anon = await page.context().browser()!.newContext();
    const p = await anon.newPage();
    await p.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
    await p.waitForSelector('[data-testid="award-item"]', { timeout: 60000 });
    const badgeImg = p.locator('[data-testid="award-item"]').filter({ hasText: 'fxa-award-one' }).locator('img');
    const bsrc = await badgeImg.first().getAttribute('src').catch(() => null);
    const bok = bsrc ? await badgeImg.first().evaluate((i: HTMLImageElement) => i.complete && i.naturalWidth > 0) : false;
    console.log('RESUME BADGE SRC', bsrc, 'decodes', bok);
    await anon.close();
    expect(bsrc).toBe(badgePath);
    const i1 = rs.findIndex((s) => s.includes('fxa-award-one')), i2 = rs.findIndex((s) => s.includes('fxa-award-two'));
    expect(i1).toBeGreaterThanOrEqual(0);
    expect(i2).toBeGreaterThanOrEqual(0);
    expect(i1).toBeLessThan(i2);

    // reorder: move two up
    await page.locator('[data-testid="award-card"]').filter({ hasText: 'fxa-award-two' }).locator('[data-testid="move-award-up"]').click();
    await expect.poll(async () => { const t = await titles(); return t.findIndex((x) => x.includes('fxa-award-two')) < t.findIndex((x) => x.includes('fxa-award-one')); }, { timeout: 20000 }).toBe(true);
    rs = await resumeTexts(page, '[data-testid="award-item"]', '[data-testid="award-item"]');
    console.log('RESUME AFTER MOVE', rs);
    expect(rs.findIndex((s) => s.includes('fxa-award-two'))).toBeLessThan(rs.findIndex((s) => s.includes('fxa-award-one')));

    // edit
    await page.locator('[data-testid="award-card"]').filter({ hasText: 'fxa-award-two' }).locator('[data-testid="edit-award"]').click();
    await expect(d).toBeVisible({ timeout: 30000 });
    await d.locator('[data-testid="award-title-input"]').fill('fxa-award-two-EDITED');
    await d.locator('[data-testid="save-award"]').click();
    await expect(d).toBeHidden({ timeout: 45000 });
    await expect(page.locator('[data-testid="award-title"]', { hasText: 'fxa-award-two-EDITED' })).toHaveCount(1);

    // delete
    for (const n of ['fxa-award-two-EDITED', 'fxa-award-one']) {
      await page.locator('[data-testid="award-card"]').filter({ hasText: n }).locator('[data-testid="delete-award"]').click();
      await page.locator('[data-testid="delete-confirm"]').click();
      await expect(page.locator('[data-testid="award-card"]').filter({ hasText: n })).toHaveCount(0, { timeout: 30000 });
    }

    // remove the badge image via the media library UI
    await nav(page, '/admin/images', /Media Library/i);
    await settle(page);
    await page.locator('[data-testid="category-tabs"] [role="tab"]').filter({ hasText: /^Awards$/ }).click();
    const bc = page.locator('[data-testid="image-card"]').filter({ hasText: 'fxa-badge' });
    await expect(bc).toHaveCount(1, { timeout: 20000 });
    await bc.locator('[data-testid="delete-image"]').click();
    await page.locator('[data-testid="delete-confirm"]').click();
    await expect(bc).toHaveCount(0, { timeout: 20000 });
  } finally {
    const left = psql("select count(*) from userawards where awardtitle like 'fxa-%'") + '/' + psql("select count(*) from blogimage where imagename like 'fxa-%'");
    console.log('LEFTOVERS awards/images', left);
    if (!left.startsWith('0')) psql("delete from userawards where awardtitle like 'fxa-%'");
    console.log('OWNER AWARDS AFTER', psql('select count(*) from userawards where userid=1'));
    console.log('CONSOLE ERRORS', errors);
  }
});
