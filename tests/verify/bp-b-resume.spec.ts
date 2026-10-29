/**
 * bp-b-resume.spec.ts — cluster B build-phase smoke (2026-10-27) for the resume rows:
 *   REQ-FN-027  experience / skills / awards / stats saved through the manage pages persist, and
 *               /resume renders exactly those rows; an admin's user selector loads the chosen
 *               user's rows
 *   REQ-FN-028  a PDF CV under 10 MB uploaded on Manage Profile is offered and served on /resume;
 *               a non-PDF or an oversized file is refused with a message
 *
 * The site owner is user 1 (`ravi`, the Admin). Every row this spec writes is named `fxb-…`, and
 * `finally` blocks delete it and restore `cvfilepath` byte for byte.
 */
import { test, expect, Page } from '@playwright/test';
import { execSync } from 'child_process';
import * as fs from 'fs';
import { loginHard, goTo, fillCommitted, pickSelect } from './vall-authoring-helpers';

const BASE = process.env.TB_BASE ?? 'http://localhost:5402';
const SHOTS = 'tests/.artifacts/build-phase-20261027/b';

function sql(query: string): string[] {
  const out = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`,
    { encoding: 'utf8' },
  );
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

function purge() {
  sql("DELETE FROM userevents WHERE eventtitle LIKE 'fxb-%' OR sessiontitle LIKE 'fxb-%'");
  sql("DELETE FROM userskills WHERE skillname LIKE 'fxb-%'");
  sql("DELETE FROM userawards WHERE awardtitle LIKE 'fxb-%'");
  sql("DELETE FROM userstats WHERE statlabel LIKE 'fxb-%'");
}

test.afterAll(purge);
test.describe.configure({ mode: 'serial' });

async function noOverflow(page: Page, name: string) {
  for (const [w, h] of [[1280, 900], [390, 844]] as const) {
    await page.setViewportSize({ width: w, height: h });
    await page.waitForTimeout(700);
    expect(await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1), `${name} @${w} scrolls horizontally`).toBe(false);
    await page.screenshot({ path: `${SHOTS}/${name}-${w}.png` });
  }
  await page.setViewportSize({ width: 1280, height: 900 });
}

// retired 2026-10-27 (W6): seeds and purges rows with psql INSERT/DELETE (no DB writes in verify phase); superseded by w6-resume.spec.ts REQ-FN-027
test.skip('retired: manage pages persist rows, /resume renders them, the user selector loads another user', async ({ browser }) => {
  test.setTimeout(540000);
  purge();
  const ownerId = sql('SELECT userid FROM bloguser WHERE issiteowner')[0];
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await loginHard(page, 'admin');

  // ---- experience ----
  await goTo(page, '/admin/experience', '[data-testid="manage-experience-page"]', 90000);
  await page.waitForTimeout(2000);
  await page.click('[data-testid="add-experience"]');
  await expect(page.locator('[data-testid="experience-dialog"]')).toBeVisible({ timeout: 20000 });
  await fillCommitted(page, 'experience-role-input', 'fxb-Role');
  await fillCommitted(page, 'experience-company-input', 'fxb-Company');
  await fillCommitted(page, 'experience-description-input', 'fxb experience description');
  await page.click('[data-testid="save-experience"]');
  await page.waitForTimeout(4000);
  const ev = sql(`SELECT userid, eventtitle, coalesce(sessiontitle,'') FROM userevents WHERE eventtitle = 'fxb-Company' OR sessiontitle = 'fxb-Role' OR eventtitle = 'fxb-Role'`);
  console.log(`FN-027 experience row: ${ev.join(' ; ')}`);
  expect(ev.length, 'experience row persisted').toBe(1);
  expect(ev[0].split('|')[0]).toBe(ownerId);

  // ---- skills ----
  await goTo(page, '/admin/skills', '[data-testid="manage-skills-page"]', 90000);
  await page.waitForTimeout(2000);
  await page.click('[data-testid="add-skill"]');
  await expect(page.locator('[data-testid="add-skill-dialog"]')).toBeVisible({ timeout: 20000 });
  await fillCommitted(page, 'skill-name-input', 'fxb-skill');
  await page.click('[data-testid="save-skill"]');
  await page.waitForTimeout(4000);
  const skill = sql(`SELECT userid, category FROM userskills WHERE skillname = 'fxb-skill'`);
  expect(skill.length, 'skill row persisted').toBe(1);
  expect(skill[0].split('|')[0]).toBe(ownerId);

  // ---- awards ----
  await goTo(page, '/admin/awards', '[data-testid="manage-awards-page"]', 90000);
  await page.waitForTimeout(2000);
  await page.click('[data-testid="add-award"]');
  await expect(page.locator('[data-testid="award-dialog"]')).toBeVisible({ timeout: 20000 });
  await fillCommitted(page, 'award-title-input', 'fxb-Award');
  await fillCommitted(page, 'award-description-input', 'fxb award description');
  await fillCommitted(page, 'award-year-input', '2026');
  await page.click('[data-testid="save-award"]');
  await page.waitForTimeout(4000);
  expect(sql(`SELECT userid||'|'||awardyear FROM userawards WHERE awardtitle = 'fxb-Award'`)).toEqual([`${ownerId}|2026`]);

  // ---- stats ----
  await goTo(page, '/admin/stats', '[data-testid="manage-stats-page"]', 90000);
  await page.waitForTimeout(2000);
  await page.click('[data-testid="add-stat"]');
  await expect(page.locator('[data-testid="stat-dialog"]')).toBeVisible({ timeout: 20000 });
  await fillCommitted(page, 'stat-value-input', '99');
  await fillCommitted(page, 'stat-label-input', 'fxb-stat');
  await page.click('[data-testid="save-stat"]');
  await page.waitForTimeout(4000);
  expect(sql(`SELECT userid||'|'||statvalue FROM userstats WHERE statlabel = 'fxb-stat'`)).toEqual([`${ownerId}|99`]);

  // ---- /resume renders exactly the stored rows ----
  const anon = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  await anon.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
  await expect(anon.locator('[data-testid="resume-page"]')).toBeVisible({ timeout: 60000 });
  await anon.waitForTimeout(3000);
  // Experience and skills load after the first paint ("Loading experience..."): wait for the text.
  for (const t of ['fxb-Role', 'fxb-Company', 'fxb-skill', 'fxb-Award', 'fxb-stat']) {
    await expect(anon.locator('[data-testid="resume-page"]'), `/resume shows ${t}`).toContainText(t, { timeout: 45000 });
  }
  const counts = {
    experience: sql(`SELECT count(*) FROM userevents WHERE userid = ${ownerId} AND type <> 'Speaking' AND type <> 'Community'`)[0],
    skills: sql(`SELECT count(*) FROM userskills WHERE userid = ${ownerId}`)[0],
    awards: sql(`SELECT count(*) FROM userawards WHERE userid = ${ownerId}`)[0],
  };
  console.log(`FN-027 db counts for owner: ${JSON.stringify(counts)}`);
  await expect(anon.locator('[data-testid="skill-badge"]')).toHaveCount(Number(counts.skills));
  await expect(anon.locator('[data-testid="award-item"]')).toHaveCount(Number(counts.awards));
  await noOverflow(anon, 'fn027-resume');
  await anon.close();

  // ---- admin user selector loads the chosen user's rows ----
  const arun = sql("SELECT userid FROM bloguser WHERE emailid = 'author@techieblog.test'")[0];
  sql(`INSERT INTO userskills (userid, category, skillname, displayorder) VALUES (${arun}, 'AI/Emerging', 'fxb-arun-skill', 0)`);
  await goTo(page, '/admin/skills', '[data-testid="manage-skills-page"]', 90000);
  await page.waitForTimeout(2500);
  await expect(page.locator('[data-testid="skill-name"]', { hasText: 'fxb-skill' }).first()).toBeVisible({ timeout: 20000 });
  await pickSelect(page, 'skills-user-select', /Arun/i);
  await page.waitForTimeout(3500);
  const names = (await page.locator('[data-testid="skill-name"]').allTextContents()).map((t) => t.trim());
  expect(names, "the selector loads the chosen user's skills").toContain('fxb-arun-skill');
  expect(names, "and not the previous user's").not.toContain('fxb-skill');
  await noOverflow(page, 'fn027-skills');

  // ---- delete through the manage page persists ----
  await pickSelect(page, 'skills-user-select', /Ravi|S Ravi/i);
  await page.waitForTimeout(3500);
  const row = page.locator('[data-testid="skill-row"]', { hasText: 'fxb-skill' }).first();
  await row.locator('[data-testid="delete-skill"]').click();
  await expect(page.locator('[data-testid="skill-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await page.click('[data-testid="delete-confirm"]');
  await page.waitForTimeout(4000);
  expect(sql(`SELECT count(*) FROM userskills WHERE skillname = 'fxb-skill'`)[0]).toBe('0');
  await page.close();
});

// retired 2026-10-27 (W6): restores cvfilepath with a psql UPDATE (no DB writes in verify phase); superseded by w6-resume.spec.ts REQ-FN-028
test.skip('retired: a PDF CV is offered and served on /resume; non-PDF and oversized files are refused', async ({ browser }) => {
  test.setTimeout(420000);
  const ownerId = sql('SELECT userid FROM bloguser WHERE issiteowner')[0];
  const originalCv = sql(`SELECT COALESCE(cvfilepath,'') FROM bloguser WHERE userid = ${ownerId}`)[0] ?? '';
  const pdf = Buffer.from('%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Count 0/Kids[]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n');
  let uploadedPath = '';
  const page = await browser.newPage({ viewport: { width: 1280, height: 900 } });
  try {
    await loginHard(page, 'admin');
    await goTo(page, '/admin/profile', '[data-testid="manage-profile-page"]', 90000);
    await page.waitForTimeout(2500);
    const picker = page.locator('[data-testid="image-picker"]').nth(1);
    const dialog = page.locator('[data-testid="image-upload-dialog"]:visible');
    const stageFile = async (name: string, mimeType: string, buffer: Buffer) => {
      await picker.locator('[data-testid="upload-new-image"]').click();
      await expect(dialog).toBeVisible({ timeout: 20000 });
      for (let i = 0; i < 5; i++) {
        await page.waitForTimeout(1200);
        await dialog.locator('input[type="file"]').setInputFiles({ name, mimeType, buffer });
        await page.waitForTimeout(5000);
        if ((await dialog.locator('[data-testid="upload-selected-file"]').count()) || (await dialog.locator('[data-testid="upload-error"]').count())) break;
      }
    };

    // refused: an image where a PDF is required
    await stageFile('fxb-small.png', 'image/png', Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64'));
    expect(await dialog.locator('[data-testid="upload-selected-file"]').count(), 'a non-PDF is never staged as the CV').toBe(0);
    const pngMsg = (await dialog.locator('[data-testid="upload-error"]').textContent().catch(() => '')) || '';
    console.log(`FN-028 non-PDF message: "${pngMsg.trim()}"`);
    expect(pngMsg.trim().length, 'a refusal message is shown').toBeGreaterThan(0);
    await dialog.locator('[data-testid="upload-cancel"]').click();
    await page.waitForTimeout(1500);

    // refused: over 10 MB
    await stageFile('fxb-big.pdf', 'application/pdf', Buffer.concat([pdf, Buffer.alloc(11 * 1024 * 1024)]));
    const bigMsg = (await dialog.locator('[data-testid="upload-error"]').textContent().catch(() => '')) || '';
    console.log(`FN-028 oversize message: "${bigMsg.trim()}"`);
    expect(bigMsg).toMatch(/10 MB/);
    expect(sql("SELECT count(*) FROM blogimage WHERE imagename = 'fxb-big.pdf'")[0]).toBe('0');
    await dialog.locator('[data-testid="upload-cancel"]').click();
    await page.waitForTimeout(1500);

    // accepted: a small PDF
    await stageFile('fxb-cv.pdf', 'application/pdf', pdf);
    await expect(dialog.locator('[data-testid="upload-selected-file"]')).toContainText('fxb-cv.pdf');
    await dialog.locator('[data-testid="upload-confirm"]').click();
    await expect(dialog).toBeHidden({ timeout: 40000 });
    await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible({ timeout: 20000 });
    await page.click('[data-testid="save-profile"]');
    await expect(page.locator('[data-testid="profile-status"]')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(2000);
    uploadedPath = sql(`SELECT COALESCE(cvfilepath,'') FROM bloguser WHERE userid = ${ownerId}`)[0] ?? '';
    expect(uploadedPath).toContain('/uploads/cv/');
    const served = await page.request.get(`${BASE}${uploadedPath}`);
    expect(served.status()).toBe(200);
    expect(Buffer.from(await served.body()).subarray(0, 5).toString()).toBe('%PDF-');

    const anon = await browser.newPage({ viewport: { width: 1280, height: 900 } });
    await anon.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
    await expect(anon.locator('[data-testid="resume-page"]')).toBeVisible({ timeout: 60000 });
    await anon.waitForTimeout(2500);
    const cv = anon.locator('[data-testid="download-cv"]');
    await expect(cv.first()).toBeVisible({ timeout: 20000 });
    expect(await cv.first().getAttribute('href')).toBe(uploadedPath);
    await noOverflow(anon, 'fn028-resume-cv');
    await anon.close();
  } finally {
    sql(`UPDATE bloguser SET cvfilepath = '${originalCv}' WHERE userid = ${ownerId}`);
    const rows = sql("SELECT imagepath FROM blogimage WHERE imagename LIKE 'fxb-%'");
    sql("DELETE FROM blogimage WHERE imagename LIKE 'fxb-%'");
    for (const p of rows) {
      const file = p.split('/').pop()!;
      try {
        execSync(`find /mnt/c/1MyCode/TechieBlog -name '${file}' -not -path '*/node_modules/*' -delete 2>/dev/null`);
      } catch { /* already gone */ }
    }
    await page.close();
  }
});
