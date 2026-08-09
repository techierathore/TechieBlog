/**
 * vall-resume.spec.ts — cluster "resume-media-newsletter", part 1 of 3.
 *
 * Grades REQ-UI-036/037/038/039/040 and REQ-FN-027/028/029/053 against the running host on
 * :5399, applying the three gates (acceptance, §4a data-render, §4b visual-truth).
 *
 * The site owner's profile row is mutated ONLY inside the REQ-FN-053 / REQ-FN-028 tests and is
 * captured and restored there; every other test in this file is read-only.
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { BASE, nav, renderCheck, ControlResult } from './_gates';
import { psql, psqlExpectError, report, expectVisualClean, bothWidths, signIn, settle } from './vall-resume-helpers';

const SHOTS = '.verify/shots/resume';
const FIXTURES = path.resolve('.verify/fixtures');

test.beforeAll(() => fs.mkdirSync(SHOTS, { recursive: true }));

// Seven verification agents share this host; a page can take ~10s just to go interactive.
test.beforeEach(({}, testInfo) => testInfo.setTimeout(420000));

// ---------------------------------------------------------------------------------------------
// REQ-UI-036 / REQ-FN-027 — the public resume page
// ---------------------------------------------------------------------------------------------

test('REQ-UI-036 public resume renders hero, about, experience, skills, awards and contact for the site owner', async ({ page }) => {
  const dbSkills = Number(psql('SELECT count(*) FROM userskills WHERE userid=1'));
  const dbAwards = Number(psql('SELECT count(*) FROM userawards WHERE userid=1'));
  const dbExp = Number(psql("SELECT count(*) FROM userevents WHERE userid=1 AND type='Experience'"));
  const dbStats = Number(psql('SELECT count(*) FROM userstats WHERE userid=1'));
  const dbCategories = Number(psql('SELECT count(DISTINCT category) FROM userskills WHERE userid=1'));

  await page.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="resume-page"]')).toBeVisible({ timeout: 45000 });
  await settle(page); // the interactive re-render blanks the prerendered sections while it reloads

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'section nav', '[data-testid="resume-section-nav"]', 'present'));
  controls.push(await renderCheck(page, 'hero name', '[data-testid="resume-name"]'));
  controls.push(await renderCheck(page, 'hero title', '[data-testid="resume-title"]'));
  controls.push(await renderCheck(page, 'hero tagline', '[data-testid="resume-tagline"]'));
  controls.push(await renderCheck(page, 'hero social links', '[data-testid="resume-social-links"]', 'present'));
  controls.push(await renderCheck(page, 'about summary', '[data-testid="about-summary"]'));
  controls.push(await renderCheck(page, 'about stats grid', '[data-testid="about-stats-grid"]', 'present'));
  controls.push(await renderCheck(page, 'experience timeline', '[data-testid="experience-list"]', 'present'));
  controls.push(await renderCheck(page, 'skills grid', '[data-testid="skills-grid"]', 'present'));
  controls.push(await renderCheck(page, 'awards list', '[data-testid="awards-list"]', 'present'));
  controls.push(await renderCheck(page, 'contact grid', '[data-testid="contact-grid"]', 'present'));
  controls.push(await renderCheck(page, 'contact email', '[data-testid="contact-email-value"]'));
  controls.push(await renderCheck(page, 'contact phone', '[data-testid="contact-phone-value"]'));
  controls.push(await renderCheck(page, 'contact location', '[data-testid="contact-location-value"]'));

  const cvPath = psql('SELECT COALESCE(cvfilepath,\'\') FROM bloguser WHERE userid=1');
  const cvButtons = await page.locator('[data-testid="download-cv"]').count();
  controls.push({
    control: 'download CV',
    verdict: cvPath ? (cvButtons > 0 ? 'RENDERS' : 'RENDER-EMPTY') : 'RENDER-EMPTY',
    detail: cvPath
      ? `cvfilepath="${cvPath}", button count ${cvButtons}`
      : `NO-DATA: bloguser.cvfilepath is empty for the site owner, so the button is correctly absent (${cvButtons} found)`,
  });

  const communityRows = Number(psql("SELECT count(*) FROM userstats WHERE userid=1 AND statcategory='Community'"));
  const communitySections = await page.locator('[data-testid="community-section"]').count();
  controls.push({
    control: 'community stats',
    verdict: communityRows > 0 ? (communitySections > 0 ? 'RENDERS' : 'RENDER-EMPTY') : 'RENDER-EMPTY',
    detail: `userstats rows with statcategory='Community' = ${communityRows}; community-section count ${communitySections} (NO-DATA when 0)`,
  });

  // §4a — the counts on screen must match the database exactly.
  await expect(page.locator('[data-testid="experience-item"]')).toHaveCount(dbExp);
  await expect(page.locator('[data-testid="skill-badge"]')).toHaveCount(dbSkills);
  await expect(page.locator('[data-testid="skill-category"]')).toHaveCount(dbCategories);
  await expect(page.locator('[data-testid="award-item"]')).toHaveCount(dbAwards);
  await expect(page.locator('[data-testid="about-stat"]')).toHaveCount(dbStats - communityRows);
  await expect(page.locator('[data-testid="resume-name"]')).not.toHaveText(/^\s*$/);

  // Acceptance — anchor navigation targets exist for every chip in the section nav.
  for (const anchor of ['about', 'experience', 'skills', 'awards', 'contact']) {
    await expect(page.locator(`#${anchor}`), `anchor target #${anchor}`).toHaveCount(1);
  }

  const visuals = await bothWidths(page, 'req-ui-036-resume');
  // /resume is a designed page (docs/mockups/10-resume.html) — keep whole-page captures to eyeball.
  await page.screenshot({ path: `${SHOTS}/req-ui-036-resume-full-1280.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(700);
  await page.screenshot({ path: `${SHOTS}/req-ui-036-resume-full-390.png`, fullPage: true });
  await page.setViewportSize({ width: 1280, height: 900 });
  report('/resume', controls, visuals);
  for (const c of controls.filter((x) => x.control !== 'download CV' && x.control !== 'community stats')) {
    expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  }
  visuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-037 — manage experience
// ---------------------------------------------------------------------------------------------

test('REQ-UI-037 manage experience lists the owner rows with ordering, edit and delete affordances', async ({ page }) => {
  const dbExp = Number(psql("SELECT count(*) FROM userevents WHERE userid=1 AND type='Experience'"));
  await signIn(page, 'admin');
  await nav(page, '/admin/experience', /Manage Experience|Experience/i);
  await settle(page);

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'entry list', '[data-testid="experience-list"]', 'present'));
  controls.push(await renderCheck(page, 'role', '[data-testid="experience-role"]'));
  controls.push(await renderCheck(page, 'company', '[data-testid="experience-company"]'));
  controls.push(await renderCheck(page, 'dates', '[data-testid="experience-dates"]'));
  controls.push(await renderCheck(page, 'display order', '[data-testid="experience-order"]'));
  controls.push(await renderCheck(page, 'add button', '[data-testid="add-experience"]', 'present'));
  controls.push(await renderCheck(page, 'edit button', '[data-testid="edit-experience"]', 'present'));
  controls.push(await renderCheck(page, 'delete button', '[data-testid="delete-experience"]', 'present'));
  controls.push(await renderCheck(page, 'user selector (admin)', '[data-testid="experience-user-select"]', 'present'));

  await expect(page.locator('[data-testid="experience-card"]')).toHaveCount(dbExp);
  await expect(page.locator('[data-testid="experience-current-badge"]')).toHaveCount(
    Number(psql("SELECT count(*) FROM userevents WHERE userid=1 AND type='Experience' AND iscurrent")),
  );

  // Geometry is measured on the page itself; a modal legitimately covers what is behind it, so
  // the dialog is captured for the record but not put through the overlap rule.
  const visuals = await bothWidths(page, 'req-ui-037-experience');

  // The company-logo field lives inside the dialog — open it read-only to prove it renders.
  await page.click('[data-testid="add-experience"]');
  await expect(page.locator('[data-testid="experience-dialog"]')).toBeVisible();
  // NOTE: REQ-UI-037 asks for a "company-logo picker"; what is built is a plain text Input for a
  // path, not the reusable ImagePicker (REQ-UI-035). Recorded as a finding, checked as a control.
  controls.push(await renderCheck(page, 'company logo field (text input, not ImagePicker)', '[data-testid="experience-logo-input"]', 'present'));
  const logoIsPicker = await page.locator('[data-testid="experience-dialog"] [data-testid="image-picker"]').count();
  console.log('REQ-UI-037 ImagePicker instances inside the experience dialog =', logoIsPicker, '(requirement says "company-logo picker")');
  await page.screenshot({ path: '.verify/shots/resume/req-ui-037-experience-dialog-1280.png' });
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(700);
  await page.screenshot({ path: '.verify/shots/resume/req-ui-037-experience-dialog-390.png' });
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.click('[data-testid="cancel-experience"]');
  await page.waitForTimeout(600);

  report('/admin/experience', controls, visuals);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  visuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-038 — manage skills
// ---------------------------------------------------------------------------------------------

test('REQ-UI-038 manage skills groups the owner rows by category with per-category ordering', async ({ page }) => {
  const dbSkills = Number(psql('SELECT count(*) FROM userskills WHERE userid=1'));
  const dbCats = Number(psql('SELECT count(DISTINCT category) FROM userskills WHERE userid=1'));
  await signIn(page, 'admin');
  await nav(page, '/admin/skills', /Manage Skills|Skills/i);
  await settle(page);

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'skills list', '[data-testid="skills-list"]', 'present'));
  controls.push(await renderCheck(page, 'category card', '[data-testid="skill-category-card"]', 'present'));
  controls.push(await renderCheck(page, 'category name', '[data-testid="skill-category-name"]'));
  controls.push(await renderCheck(page, 'category count', '[data-testid="skill-category-count"]'));
  controls.push(await renderCheck(page, 'add skill', '[data-testid="add-skill"]', 'present'));
  controls.push(await renderCheck(page, 'user selector (admin)', '[data-testid="skills-user-select"]', 'present'));

  await expect(page.locator('[data-testid="skill-category-card"]')).toHaveCount(dbCats);

  // Categories render collapsed; expand them all so the rows themselves can be counted.
  const toggles = page.locator('[data-testid="toggle-category"]');
  const toggleCount = await toggles.count();
  for (let i = 0; i < toggleCount; i++) {
    if ((await page.locator('[data-testid="skill-row"]').count()) < dbSkills) {
      await toggles.nth(i).click();
      await page.waitForTimeout(350);
    }
  }
  const rows = await page.locator('[data-testid="skill-row"]').count();
  controls.push({
    control: 'skill rows',
    verdict: rows > 0 ? 'RENDERS' : 'RENDER-EMPTY',
    detail: `${rows} rows rendered after expanding ${toggleCount} categories; userskills userid=1 = ${dbSkills}`,
  });
  expect(rows, 'every seeded skill must be reachable through its category').toBe(dbSkills);
  controls.push(await renderCheck(page, 'skill name', '[data-testid="skill-name"]'));
  controls.push(await renderCheck(page, 'edit skill', '[data-testid="edit-skill"]', 'present'));
  controls.push(await renderCheck(page, 'delete skill', '[data-testid="delete-skill"]', 'present'));
  controls.push(await renderCheck(page, 'move up', '[data-testid="move-skill-up"]', 'present'));
  controls.push(await renderCheck(page, 'move down', '[data-testid="move-skill-down"]', 'present'));

  const visuals = await bothWidths(page, 'req-ui-038-skills');
  report('/admin/skills', controls, visuals);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  visuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-039 — manage awards
// ---------------------------------------------------------------------------------------------

test('REQ-UI-039 manage awards lists the owner rows with badge picker and ordering', async ({ page }) => {
  const dbAwards = Number(psql('SELECT count(*) FROM userawards WHERE userid=1'));
  await signIn(page, 'admin');
  await nav(page, '/admin/awards', /Manage Awards|Awards/i);
  await settle(page);

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'awards list', '[data-testid="awards-list"]', 'present'));
  controls.push(await renderCheck(page, 'award title', '[data-testid="award-title"]'));
  controls.push(await renderCheck(page, 'award year', '[data-testid="award-year"]'));
  controls.push(await renderCheck(page, 'award description', '[data-testid="award-description"]'));
  controls.push(await renderCheck(page, 'add award', '[data-testid="add-award"]', 'present'));
  controls.push(await renderCheck(page, 'edit award', '[data-testid="edit-award"]', 'present'));
  controls.push(await renderCheck(page, 'delete award', '[data-testid="delete-award"]', 'present'));
  controls.push(await renderCheck(page, 'move up', '[data-testid="move-award-up"]', 'present'));
  controls.push(await renderCheck(page, 'move down', '[data-testid="move-award-down"]', 'present'));
  controls.push(await renderCheck(page, 'user selector (admin)', '[data-testid="awards-user-select"]', 'present'));

  await expect(page.locator('[data-testid="award-card"]')).toHaveCount(dbAwards);

  const visuals = await bothWidths(page, 'req-ui-039-awards');

  await page.click('[data-testid="add-award"]');
  await expect(page.locator('[data-testid="award-dialog"]')).toBeVisible();
  // Same finding as REQ-UI-037: the "badge-image picker" is a plain path Input, not ImagePicker.
  controls.push(await renderCheck(page, 'badge image field (text input, not ImagePicker)', '[data-testid="award-badge-input"]', 'present'));
  const badgeIsPicker = await page.locator('[data-testid="award-dialog"] [data-testid="image-picker"]').count();
  console.log('REQ-UI-039 ImagePicker instances inside the award dialog =', badgeIsPicker, '(requirement says "badge-image picker")');
  await page.screenshot({ path: '.verify/shots/resume/req-ui-039-awards-dialog-1280.png' });
  await page.click('[data-testid="cancel-award"]');
  await page.waitForTimeout(600);

  report('/admin/awards', controls, visuals);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  visuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-040 — manage profile renders every stored value
// ---------------------------------------------------------------------------------------------

test('REQ-UI-040 manage profile renders basic info, socials, username and resume settings from the stored row', async ({ page }) => {
  const [firstName, lastName, username, title, tagline, location, phone, linkedin, github, twitter] = psql(
    "SELECT firstname||'|'||lastname||'|'||COALESCE(username,'')||'|'||COALESCE(title,'')||'|'||COALESCE(tagline,'')" +
      "||'|'||COALESCE(location,'')||'|'||COALESCE(phonenumber,'')||'|'||COALESCE(linkedinurl,'')" +
      "||'|'||COALESCE(githuburl,'')||'|'||COALESCE(twitterurl,'') FROM bloguser WHERE userid=1",
  ).split('|');

  await signIn(page, 'admin');
  await nav(page, '/admin/profile', /Profile/i);
  await settle(page);
  await expect(page.locator('[data-testid="manage-profile-page"]')).toBeVisible();

  const controls: ControlResult[] = [];
  const field = async (name: string, testid: string, expected: string) => {
    const value = await page.inputValue(`[data-testid="${testid}"]`);
    controls.push({
      control: name,
      verdict: value.trim() ? 'RENDERS' : 'RENDER-EMPTY',
      detail: `value="${value}" (db="${expected}")`,
    });
    expect(value, `${name} must render its stored value`).toBe(expected);
  };
  await field('first name', 'first-name-input', firstName);
  await field('last name', 'last-name-input', lastName);
  await field('username', 'username-input', username);
  await field('title', 'title-input', title);
  await field('tagline', 'tagline-input', tagline);
  await field('location', 'location-input', location);
  await field('phone', 'phone-input', phone);
  await field('linkedin', 'linkedin-input', linkedin);
  await field('github', 'github-input', github);
  await field('twitter', 'twitter-input', twitter);

  controls.push(await renderCheck(page, 'bio', '[data-testid="bio-input"]', 'present'));
  controls.push(await renderCheck(page, 'resume settings card', '[data-testid="resume-settings-card"]', 'present'));
  controls.push(await renderCheck(page, 'CV picker (ImagePicker)', '[data-testid="image-picker"]', 'present'));
  controls.push(await renderCheck(page, 'quick links', '[data-testid="quick-links-card"]', 'present'));
  for (const link of ['manage-experience-link', 'manage-skills-link', 'manage-awards-link', 'manage-stats-link']) {
    controls.push(await renderCheck(page, link, `[data-testid="${link}"]`, 'present'));
  }
  const resumeEnabled = await page.locator('[data-testid="resume-enabled-checkbox"]').getAttribute('aria-checked');
  controls.push({
    control: 'resume enabled checkbox',
    verdict: resumeEnabled === null ? 'RENDER-EMPTY' : 'RENDERS',
    detail: `aria-checked=${resumeEnabled} (db resumeenabled=${psql('SELECT resumeenabled FROM bloguser WHERE userid=1')})`,
  });
  expect(resumeEnabled).toBe('true');

  const visuals = await bothWidths(page, 'req-ui-040-profile');
  report('/admin/profile', controls, visuals);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  visuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-053 — the data-loss regression. Saving with no edits must preserve the whole resume.
// ---------------------------------------------------------------------------------------------

test('REQ-FN-053 saving Manage Profile with no edits does not erase the site owner resume', async ({ page }) => {
  const AT_RISK =
    "md5(COALESCE(username,'')||'|'||issiteowner::text||'|'||COALESCE(title,'')||'|'||COALESCE(tagline,'')" +
    "||'|'||COALESCE(instagramurl,'')||'|'||COALESCE(phonenumber,'')||'|'||COALESCE(location,'')" +
    "||'|'||COALESCE(cvfilepath,'')||'|'||resumeenabled::text)";

  const before = {
    hash: psql(`SELECT ${AT_RISK} FROM bloguser WHERE userid=1`),
    row: psql(
      "SELECT COALESCE(username,'')||' / '||issiteowner||' / '||COALESCE(title,'')||' / '||COALESCE(location,'')" +
        "||' / '||COALESCE(phonenumber,'')||' / '||resumeenabled FROM bloguser WHERE userid=1",
    ),
    skills: psql('SELECT count(*) FROM userskills WHERE userid=1'),
    awards: psql('SELECT count(*) FROM userawards WHERE userid=1'),
    stats: psql('SELECT count(*) FROM userstats WHERE userid=1'),
    experience: psql("SELECT count(*) FROM userevents WHERE userid=1 AND type='Experience'"),
  };
  console.log('REQ-FN-053 BEFORE', JSON.stringify(before));

  await signIn(page, 'admin');
  await nav(page, '/admin/profile', /Profile/i);
  await settle(page);
  await expect(page.locator('[data-testid="title-input"]')).not.toHaveValue('');

  await page.click('[data-testid="save-profile"]');
  await expect(page.locator('[data-testid="profile-status"]')).toBeVisible({ timeout: 30000 });
  const status = (await page.locator('[data-testid="profile-status"]').textContent())?.trim();
  await page.waitForTimeout(1500);

  const after = {
    hash: psql(`SELECT ${AT_RISK} FROM bloguser WHERE userid=1`),
    row: psql(
      "SELECT COALESCE(username,'')||' / '||issiteowner||' / '||COALESCE(title,'')||' / '||COALESCE(location,'')" +
        "||' / '||COALESCE(phonenumber,'')||' / '||resumeenabled FROM bloguser WHERE userid=1",
    ),
    skills: psql('SELECT count(*) FROM userskills WHERE userid=1'),
    awards: psql('SELECT count(*) FROM userawards WHERE userid=1'),
    stats: psql('SELECT count(*) FROM userstats WHERE userid=1'),
    experience: psql("SELECT count(*) FROM userevents WHERE userid=1 AND type='Experience'"),
  };
  console.log('REQ-FN-053 AFTER ', JSON.stringify(after), 'status =', status);

  expect(after.hash, 'the nine at-risk BlogUser columns must survive a no-edit save').toBe(before.hash);
  expect(after.row).toBe(before.row);
  expect(after.skills).toBe(before.skills);
  expect(after.awards).toBe(before.awards);
  expect(after.stats).toBe(before.stats);
  expect(after.experience).toBe(before.experience);

  // Re-enter the page through the router so persisted (not retained) state is read back.
  await nav(page, '/', /.*/);
  await nav(page, '/admin/profile', /Profile/i);
  await settle(page);
  await expect(page.locator('[data-testid="title-input"]')).not.toHaveValue('');
  await expect(page.locator('[data-testid="username-input"]')).not.toHaveValue('');
  await expect(page.locator('[data-testid="location-input"]')).not.toHaveValue('');

  // And the public resume must still render everything.
  const anon = await page.context().newPage();
  await anon.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
  await expect(anon.locator('[data-testid="resume-page"]')).toBeVisible({ timeout: 45000 });
  await settle(anon);
  await expect(anon.locator('[data-testid="skill-badge"]')).toHaveCount(Number(before.skills));
  await expect(anon.locator('[data-testid="award-item"]')).toHaveCount(Number(before.awards));
  await expect(anon.locator('[data-testid="experience-item"]')).toHaveCount(Number(before.experience));
  await anon.screenshot({ path: `${SHOTS}/req-fn-053-resume-after-save.png` });
  await anon.close();
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-028 — CV upload and public download
// ---------------------------------------------------------------------------------------------

test('REQ-FN-028 a PDF CV uploaded on Manage Profile is downloadable from the public resume', async ({ page }) => {
  const originalCv = psql("SELECT COALESCE(cvfilepath,'') FROM bloguser WHERE userid=1");
  console.log('REQ-FN-028 original cvfilepath =', JSON.stringify(originalCv));
  let uploadedPath = '';

  try {
    await signIn(page, 'admin');
    await nav(page, '/admin/profile', /Profile/i);
  await settle(page);

    // Two pickers live here: [0] the avatar (category "profiles"), [1] the CV (category "cv").
    // Driving the wrong one uploads a PDF into an image category and is rejected on format.
    const picker = page.locator('[data-testid="image-picker"]').nth(1);
    await expect(picker).toBeVisible();
    await picker.locator('[data-testid="upload-new-image"]').click();
    const dialog = page.locator('[data-testid="image-upload-dialog"]:visible');
    await expect(dialog).toBeVisible();
    await dialog.locator('input[type="file"]').setInputFiles(path.join(FIXTURES, 'verify-0808-cv.pdf'));
    await expect(dialog.locator('[data-testid="upload-selected-file"]')).toBeVisible();
    await dialog.locator('[data-testid="upload-confirm"]').click();
    await expect(dialog).toBeHidden({ timeout: 30000 });
    await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible({ timeout: 15000 });

    await page.click('[data-testid="save-profile"]');
    await expect(page.locator('[data-testid="profile-status"]')).toBeVisible({ timeout: 30000 });
    await page.waitForTimeout(1500);

    uploadedPath = psql("SELECT COALESCE(cvfilepath,'') FROM bloguser WHERE userid=1");
    console.log('REQ-FN-028 stored cvfilepath =', uploadedPath);
    expect(uploadedPath, 'the CV path must be persisted on the owner row').toContain('/uploads/cv/');

    const dbRow = psql(
      `SELECT category||' | '||COALESCE(mimetype,'')||' | '||size FROM blogimage WHERE imagepath='${uploadedPath}'`,
    );
    console.log('REQ-FN-028 blogimage row =', dbRow);
    expect(dbRow).toContain('cv');
    expect(dbRow).toContain('application/pdf');

    // Public download: the file must be served and the resume must offer it.
    const res = await page.request.get(`${BASE}${uploadedPath}`);
    expect(res.status(), `GET ${uploadedPath}`).toBe(200);

    const anon = await page.context().newPage();
    await anon.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
    await expect(anon.locator('[data-testid="resume-page"]')).toBeVisible({ timeout: 45000 });
    await settle(anon);
    const cvButton = anon.locator('[data-testid="download-cv"]');
    await expect(cvButton).toBeVisible({ timeout: 15000 });
    expect(await cvButton.getAttribute('href')).toBe(uploadedPath);
    await anon.screenshot({ path: `${SHOTS}/req-fn-028-resume-with-cv.png` });
    await anon.close();

    // REQ-FN-025 negative on the same category: a PNG must never become the CV.
    await picker.locator('[data-testid="upload-new-image"]').click();
    await expect(dialog).toBeVisible();
    await dialog.locator('input[type="file"]').setInputFiles(path.join(FIXTURES, 'verify-0808-small.png'));
    await page.waitForTimeout(4000);
    const cvError = await dialog.locator('[data-testid="upload-error"]').count();
    const cvSelected = await dialog.locator('[data-testid="upload-selected-file"]').count();
    console.log(
      'REQ-FN-028 png-into-cv →',
      cvError ? `error "${(await dialog.locator('[data-testid="upload-error"]').textContent())?.trim()}"` : 'no inline error',
      `| selected-file panels = ${cvSelected}`,
    );
    expect(cvSelected, 'a non-PDF must never become an uploadable CV').toBe(0);
    await expect(dialog.locator('[data-testid="upload-confirm"]')).toBeDisabled();
    await dialog.locator('[data-testid="upload-cancel"]').click();
  } finally {
    // Restore the owner row exactly as found and remove everything created.
    psql(`UPDATE bloguser SET cvfilepath='${originalCv}' WHERE userid=1`);
    if (uploadedPath) {
      psql(`DELETE FROM blogimage WHERE imagepath='${uploadedPath}'`);
      const disk = path.join('source/BlogUI/wwwroot', uploadedPath.replace(/^\//, ''));
      if (fs.existsSync(disk)) fs.unlinkSync(disk);
    }
    psql("DELETE FROM blogimage WHERE imagename LIKE 'verify-0808%'");
    console.log(
      'REQ-FN-028 CLEANUP: cvfilepath =',
      JSON.stringify(psql("SELECT COALESCE(cvfilepath,'') FROM bloguser WHERE userid=1")),
      'blogimage rows =',
      psql('SELECT count(*) FROM blogimage'),
      'skills =',
      psql('SELECT count(*) FROM userskills'),
      'awards =',
      psql('SELECT count(*) FROM userawards'),
    );
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-029 — username uniqueness and the single-site-owner flag
// ---------------------------------------------------------------------------------------------

test('REQ-FN-029 usernames are unique, exactly one site owner exists, and the page reports availability', async ({ page }) => {
  const indexes = psql(
    "SELECT indexname FROM pg_indexes WHERE tablename='bloguser' AND indexname IN ('idxbloguserusername','idxsinglesiteowner') ORDER BY 1",
  );
  console.log('REQ-FN-029 indexes =', JSON.stringify(indexes));
  expect(indexes).toContain('idxbloguserusername');
  expect(indexes).toContain('idxsinglesiteowner');

  const owners = psql('SELECT count(*) FROM bloguser WHERE issiteowner');
  expect(owners, 'exactly one site owner').toBe('1');
  const ownerEmail = psql('SELECT emailid FROM bloguser WHERE issiteowner');
  expect(ownerEmail.toLowerCase()).toBe('ravi@techieblog.com');

  // Proved by attempted violation inside a transaction that is rolled back. psql exits non-zero
  // on the constraint error, so the message is read off the thrown result rather than stdout.
  const dup = psqlExpectError("BEGIN; UPDATE bloguser SET username='ravi' WHERE userid=2; ROLLBACK;");
  console.log('REQ-FN-029 duplicate-username attempt =', JSON.stringify(dup));
  expect(dup).toMatch(/duplicate key value violates unique constraint "idxbloguserusername"/);

  const dupOwner = psqlExpectError('BEGIN; UPDATE bloguser SET issiteowner=true WHERE userid=2; ROLLBACK;');
  console.log('REQ-FN-029 second-owner attempt =', JSON.stringify(dupOwner));
  expect(dupOwner).toMatch(/duplicate key value violates unique constraint "idxsinglesiteowner"/);

  // ON_ERROR_STOP aborts the script before ROLLBACK, so make sure nothing was left committed.
  expect(psql("SELECT COALESCE(username,'') FROM bloguser WHERE userid=2")).not.toBe('ravi');
  expect(psql('SELECT count(*) FROM bloguser WHERE issiteowner')).toBe('1');

  // UI half: the availability hint reacts to a taken username.
  await signIn(page, 'editor');
  await nav(page, '/admin/profile', /Profile/i);
  await settle(page);
  await page.fill('[data-testid="username-input"]', 'ravi');
  await page.locator('[data-testid="username-input"]').blur();
  await page.waitForTimeout(2500);
  const hint = (await page.locator('[data-testid="username-hint"]').first().textContent())?.trim() ?? '';
  console.log('REQ-FN-029 username hint for a taken name =', JSON.stringify(hint));
  expect(hint.length, 'the page must say something about a taken username').toBeGreaterThan(0);
  expect(hint).toMatch(/taken|not available|unavailable|already/i);
  await page.screenshot({ path: `${SHOTS}/req-fn-029-username-taken.png` });

  // Nothing was saved — confirm the editor row is untouched.
  console.log('REQ-FN-029 editor username after test =', JSON.stringify(psql("SELECT COALESCE(username,'') FROM bloguser WHERE userid=2")));
  expect(psql('SELECT count(*) FROM bloguser WHERE issiteowner')).toBe('1');
});
