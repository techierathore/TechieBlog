/**
 * cluster-d-resume-admin.spec.ts — build-phase FIX pass, cluster D (2026-08-09).
 *
 * Covers the resume/profile admin surface:
 *   REQ-UI-037  /admin/experience — list + CRUD + display order + COMPANY-LOGO PICKER
 *   REQ-UI-039  /admin/awards     — list + CRUD + ordering    + BADGE-IMAGE PICKER
 *   REQ-UI-040  /admin/profile    — resume fields render, and the ImagePicker clear button
 *                                   no longer overlaps the action row at 390px
 *
 * The two FAIL verdicts were both "the acceptance names a picker and there is only a text path
 * box", so every picker assertion here is measured on the REAL ImagePicker composite inside the
 * dialog (library gallery + upload), not on a text input.
 *
 * REQ-UI-040 additionally carries the REQ-FN-053 data-loss regression guard: a NO-EDIT SAVE on
 * /admin/profile must leave the site owner's resume columns byte-identical. The md5 is taken in
 * psql either side of this file's run by the driving shell.
 */
import { test, expect, Page } from '@playwright/test';
import { BASE, login, nav } from './_gates';

const SHOTS = 'test-results-cluster-d';
const SMOKE_ROLE = 'ClusterD Smoke Architect';
const SMOKE_COMPANY = 'ClusterD Smoke Corp';
const SMOKE_ROLE_EDITED = 'ClusterD Smoke Architect EDITED';
const SMOKE_AWARD = 'ClusterD Smoke Award';
const SMOKE_AWARD_EDITED = 'ClusterD Smoke Award EDITED';

/** Fails the run if the document scrolls sideways — the §4b visual-truth gate. */
async function assertNoHorizontalScroll(page: Page, label: string) {
  const overflow = await page.evaluate(() => ({
    scrollWidth: document.documentElement.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));
  expect(overflow.scrollWidth, `${label}: document scrolls horizontally`).toBeLessThanOrEqual(
    overflow.clientWidth + 1,
  );
}

/** Fails the run if any visible element sticks out past the viewport's right edge. */
async function assertNothingOffViewport(page: Page, label: string) {
  const strays = await page.evaluate(() => {
    const width = document.documentElement.clientWidth;
    const out: string[] = [];
    document.querySelectorAll<HTMLElement>('[data-testid]').forEach((el) => {
      const r = el.getBoundingClientRect();
      if (r.width === 0 && r.height === 0) return;
      if (r.right > width + 1 || r.left < -1) {
        out.push(`${el.getAttribute('data-testid')} left=${Math.round(r.left)} right=${Math.round(r.right)}`);
      }
    });
    return out;
  });
  expect(strays, `${label}: elements outside the viewport`).toEqual([]);
}

/**
 * Fails the run if a text element's own painted width exceeds its layout box.
 *
 * This is the gate the previous pass did not have. `assertNothingOffViewport` measures bounding
 * boxes, and a flex column squeezed to 40px has a perfectly in-viewport box — the TEXT is what
 * escapes it and lands on the badges beside it. Comparing scrollWidth to clientWidth catches
 * exactly that.
 */
async function assertNoTextOverflow(page: Page, testids: string[], label: string) {
  const bad = await page.evaluate((ids) => {
    const out: string[] = [];
    ids.forEach((id) => {
      document.querySelectorAll<HTMLElement>(`[data-testid="${id}"]`).forEach((el) => {
        if (el.scrollWidth > el.clientWidth + 1) {
          out.push(`${id} scrollWidth=${el.scrollWidth} clientWidth=${el.clientWidth}`);
        }
      });
    });
    return out;
  }, testids);
  expect(bad, `${label}: text overflows its box and will paint over neighbours`).toEqual([]);
}

/** Rectangle of the first match of a testid, or null when absent/invisible. */
async function boxOf(page: Page, testid: string) {
  return page.evaluate((id) => {
    const el = document.querySelector<HTMLElement>(`[data-testid="${id}"]`);
    if (!el) return null;
    const r = el.getBoundingClientRect();
    return { x: r.x, y: r.y, w: r.width, h: r.height, right: r.right, bottom: r.bottom };
  }, testid);
}

test.describe.configure({ mode: 'serial' });

// ---------------------------------------------------------------------------
// REQ-UI-037 — Manage experience
// ---------------------------------------------------------------------------
test('ManageExperienceRendersDataAndOffersLogoPicker', async ({ page }) => {
  test.setTimeout(300000);
  await page.setViewportSize({ width: 1280, height: 1000 });
  await login(page, 'admin');
  await nav(page, '/admin/experience', /Manage Experience/i);

  // RENDER-TRUTH: three seeded rows, each with role, company and dates.
  const cards = page.locator('[data-testid="experience-card"]');
  await expect(cards).toHaveCount(3);
  for (let i = 0; i < 3; i++) {
    await expect(cards.nth(i).locator('[data-testid="experience-role"]')).not.toBeEmpty();
    await expect(cards.nth(i).locator('[data-testid="experience-company"]')).not.toBeEmpty();
    await expect(cards.nth(i).locator('[data-testid="experience-dates"]')).not.toBeEmpty();
  }
  await expect(page.locator('[data-testid="experience-current-badge"]')).toHaveCount(1);
  await expect(page.locator('[data-testid="experience-user-select"]')).toBeVisible();

  await page.screenshot({ path: `${SHOTS}/req-ui-037-experience-1280.png`, fullPage: true });
  await assertNoHorizontalScroll(page, 'experience@1280');
  await assertNothingOffViewport(page, 'experience@1280');
  await assertNoTextOverflow(page, ['experience-role', 'experience-company', 'experience-dates'], 'experience@1280');

  // THE DEFECT: the acceptance names a company-logo PICKER.
  await page.click('[data-testid="add-experience"]');
  const dialog = page.locator('[data-testid="experience-dialog"]');
  await expect(dialog).toBeVisible();

  const picker = dialog.locator('[data-testid="experience-logo-picker"] [data-testid="image-picker"]');
  await expect(picker, 'no ImagePicker inside the experience dialog').toHaveCount(1);
  await expect(picker.locator('[data-testid="choose-from-library"]')).toBeVisible();
  await expect(picker.locator('[data-testid="upload-new-image"]')).toBeVisible();
  await expect(picker.locator('[data-testid="image-constraints"]')).toContainText(/500KB/i);

  // The picker must actually pick: open the logos library and click the tile.
  await picker.locator('[data-testid="choose-from-library"]').click();
  const gallery = page.locator('[data-testid="image-gallery-dialog"]');
  await expect(gallery).toBeVisible();
  const tiles = gallery.locator('[data-testid="gallery-image"]');
  await expect(tiles, 'logos gallery is empty — the picker cannot be exercised').toHaveCount(1);
  await tiles.first().click();
  await expect(gallery).toBeHidden();

  // Selection is now bound: the preview renders and the manual path box mirrors it.
  await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible();
  const logoPath = await dialog.locator('[data-testid="experience-logo-input"]').inputValue();
  expect(logoPath, 'picker did not write the chosen path into the bound property').toContain('/uploads/');

  await page.screenshot({ path: `${SHOTS}/req-ui-037-logo-picker.png` });

  // CREATE
  await dialog.locator('[data-testid="experience-role-input"]').fill(SMOKE_ROLE);
  await dialog.locator('[data-testid="experience-company-input"]').fill(SMOKE_COMPANY);
  await dialog.locator('[data-testid="experience-description-input"]').fill('- Cluster D smoke row\n- delete me');
  await dialog.locator('[data-testid="experience-order-input"]').fill('9');
  await dialog.locator('[data-testid="save-experience"]').click();
  await expect(dialog).toBeHidden({ timeout: 45000 });
  await expect(cards).toHaveCount(4);
  await expect(page.locator('[data-testid="experience-role"]', { hasText: SMOKE_ROLE })).toHaveCount(1);

  // EDIT — the row's own edit route reloads the entry into the dialog.
  const smokeCard = page.locator('[data-testid="experience-card"]').filter({ hasText: SMOKE_ROLE });
  await smokeCard.locator('[data-testid="edit-experience"]').click();
  await expect(dialog).toBeVisible({ timeout: 45000 });
  // The saved logo must come back on reload, in BOTH the picker preview and the path box.
  await expect(
    dialog.locator('[data-testid="experience-logo-picker"] [data-testid="selected-image"]'),
  ).toBeVisible();
  await dialog.locator('[data-testid="experience-role-input"]').fill(SMOKE_ROLE_EDITED);
  await dialog.locator('[data-testid="save-experience"]').click();
  await expect(dialog).toBeHidden({ timeout: 45000 });
  await expect(page.locator('[data-testid="experience-role"]', { hasText: SMOKE_ROLE_EDITED })).toHaveCount(1);

  // 390px visual truth on the list.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1200);
  await page.screenshot({ path: `${SHOTS}/req-ui-037-experience-390.png`, fullPage: true });
  await assertNoHorizontalScroll(page, 'experience@390');
  await assertNothingOffViewport(page, 'experience@390');
  await assertNoTextOverflow(page, ['experience-role', 'experience-company', 'experience-dates'], 'experience@390');
  await expect(page.locator('[data-testid="experience-card"]')).toHaveCount(4);

  // The action group must not sit on top of the title once the row wraps.
  const collisions = await page.evaluate(() => {
    const out: string[] = [];
    document.querySelectorAll<HTMLElement>('[data-testid="experience-card"]').forEach((card, i) => {
      const title = card.querySelector<HTMLElement>('[data-testid="experience-role"]');
      const order = card.querySelector<HTMLElement>('[data-testid="experience-order"]');
      if (!title || !order) return;
      const a = title.getBoundingClientRect();
      const b = order.getBoundingClientRect();
      if (a.x < b.right && b.x < a.right && a.y < b.bottom && b.y < a.bottom) {
        out.push(`card ${i}: title overlaps order badge`);
      }
    });
    return out;
  });
  expect(collisions, 'experience@390: card header elements overlap').toEqual([]);
});

test('ManageExperienceDeletesTheSmokeRow', async ({ page }) => {
  test.setTimeout(300000);
  await page.setViewportSize({ width: 1280, height: 1000 });
  await login(page, 'admin');
  await nav(page, '/admin/experience', /Manage Experience/i);

  const smokeCard = page.locator('[data-testid="experience-card"]').filter({ hasText: SMOKE_ROLE_EDITED });
  await expect(smokeCard).toHaveCount(1);
  await smokeCard.locator('[data-testid="delete-experience"]').click();
  await expect(page.locator('[data-testid="experience-delete-dialog"]')).toBeVisible();
  await page.locator('[data-testid="delete-confirm"]').click();
  await expect(page.locator('[data-testid="experience-card"]')).toHaveCount(3, { timeout: 45000 });
});

// ---------------------------------------------------------------------------
// REQ-UI-039 — Manage awards
// ---------------------------------------------------------------------------
test('ManageAwardsRendersDataAndOffersBadgePicker', async ({ page }) => {
  test.setTimeout(300000);
  await page.setViewportSize({ width: 1280, height: 1000 });
  await login(page, 'admin');
  await nav(page, '/admin/awards', /Manage Awards/i);

  const cards = page.locator('[data-testid="award-card"]');
  await expect(cards).toHaveCount(3);
  for (let i = 0; i < 3; i++) {
    await expect(cards.nth(i).locator('[data-testid="award-title"]')).not.toBeEmpty();
    await expect(cards.nth(i).locator('[data-testid="award-year"]')).not.toBeEmpty();
  }
  await expect(page.locator('[data-testid="awards-user-select"]')).toBeVisible();
  await expect(page.locator('[data-testid="move-award-up"]')).toHaveCount(3);
  await expect(page.locator('[data-testid="move-award-down"]')).toHaveCount(3);

  await page.screenshot({ path: `${SHOTS}/req-ui-039-awards-1280.png`, fullPage: true });
  await assertNoHorizontalScroll(page, 'awards@1280');
  await assertNothingOffViewport(page, 'awards@1280');

  // THE DEFECT: the acceptance names a badge-image PICKER.
  await page.click('[data-testid="add-award"]');
  const dialog = page.locator('[data-testid="award-dialog"]');
  await expect(dialog).toBeVisible();

  const picker = dialog.locator('[data-testid="award-badge-picker"] [data-testid="image-picker"]');
  await expect(picker, 'no ImagePicker inside the award dialog').toHaveCount(1);
  await expect(picker.locator('[data-testid="choose-from-library"]')).toBeVisible();
  await expect(picker.locator('[data-testid="upload-new-image"]')).toBeVisible();

  await picker.locator('[data-testid="choose-from-library"]').click();
  const gallery = page.locator('[data-testid="image-gallery-dialog"]');
  await expect(gallery).toBeVisible();
  const tiles = gallery.locator('[data-testid="gallery-image"]');
  await expect(tiles, 'awards gallery is empty — the picker cannot be exercised').toHaveCount(1);
  await tiles.first().click();
  await expect(gallery).toBeHidden();
  await expect(picker.locator('[data-testid="selected-image"]')).toBeVisible();
  const badgePath = await dialog.locator('[data-testid="award-badge-input"]').inputValue();
  expect(badgePath, 'picker did not write the chosen path into the bound field').toContain('/uploads/');

  await page.screenshot({ path: `${SHOTS}/req-ui-039-badge-picker.png` });

  // CREATE
  await dialog.locator('[data-testid="award-title-input"]').fill(SMOKE_AWARD);
  await dialog.locator('[data-testid="award-description-input"]').fill('Cluster D smoke award — delete me');
  await dialog.locator('[data-testid="award-year-input"]').fill('2026');
  await dialog.locator('[data-testid="save-award"]').click();
  await expect(dialog).toBeHidden({ timeout: 45000 });
  await expect(cards).toHaveCount(4);

  // The chosen badge must render on the created card.
  const smokeCard = page.locator('[data-testid="award-card"]').filter({ hasText: SMOKE_AWARD });
  await expect(smokeCard.locator('img')).toHaveCount(1);

  // EDIT — reopening must rehydrate the picker from the stored path.
  await smokeCard.locator('[data-testid="edit-award"]').click();
  await expect(dialog).toBeVisible();
  await expect(
    dialog.locator('[data-testid="award-badge-picker"] [data-testid="selected-image"]'),
  ).toBeVisible();
  await dialog.locator('[data-testid="award-title-input"]').fill(SMOKE_AWARD_EDITED);
  await dialog.locator('[data-testid="save-award"]').click();
  await expect(dialog).toBeHidden({ timeout: 45000 });
  await expect(page.locator('[data-testid="award-card"]').filter({ hasText: SMOKE_AWARD_EDITED })).toHaveCount(1);

  // 390px visual truth.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1200);
  await page.screenshot({ path: `${SHOTS}/req-ui-039-awards-390.png`, fullPage: true });
  await assertNoHorizontalScroll(page, 'awards@390');
  await assertNothingOffViewport(page, 'awards@390');
  await assertNoTextOverflow(page, ['award-title', 'award-year', 'award-description'], 'awards@390');

  const awardCollisions = await page.evaluate(() => {
    const out: string[] = [];
    document.querySelectorAll<HTMLElement>('[data-testid="award-card"]').forEach((card, i) => {
      const title = card.querySelector<HTMLElement>('[data-testid="award-title"]');
      const edit = card.querySelector<HTMLElement>('[data-testid="edit-award"]');
      if (!title || !edit) return;
      const a = title.getBoundingClientRect();
      const b = edit.getBoundingClientRect();
      if (a.x < b.right && b.x < a.right && a.y < b.bottom && b.y < a.bottom) {
        out.push(`card ${i}: title overlaps the edit button`);
      }
    });
    return out;
  });
  expect(awardCollisions, 'awards@390: card header elements overlap').toEqual([]);

  // DELETE the smoke row.
  await page.setViewportSize({ width: 1280, height: 1000 });
  await page.waitForTimeout(800);
  const toDelete = page.locator('[data-testid="award-card"]').filter({ hasText: SMOKE_AWARD_EDITED });
  await toDelete.locator('[data-testid="delete-award"]').click();
  await expect(page.locator('[data-testid="award-delete-dialog"]')).toBeVisible();
  await page.locator('[data-testid="delete-confirm"]').click();
  await expect(page.locator('[data-testid="award-card"]')).toHaveCount(3, { timeout: 45000 });
});

// ---------------------------------------------------------------------------
// REQ-UI-040 — Manage profile: render truth, 390px layout, no-edit-save guard
// ---------------------------------------------------------------------------
test('ManageProfileRendersResumeFieldsAndSurvivesNoEditSave', async ({ page }) => {
  test.setTimeout(300000);
  await page.setViewportSize({ width: 1280, height: 1200 });
  await login(page, 'admin');
  await nav(page, '/admin/profile', /My Profile/i);

  // RENDER-TRUTH on the resume-bearing fields.
  for (const id of ['first-name-input', 'last-name-input', 'username-input', 'title-input', 'location-input']) {
    const value = await page.locator(`[data-testid="${id}"]`).inputValue();
    expect(value.trim(), `${id} rendered blank — the read projection is short again`).not.toBe('');
  }
  await expect(page.locator('[data-testid="resume-settings-card"]')).toBeVisible();
  await expect(page.locator('[data-testid="resume-enabled-checkbox"]')).toHaveAttribute('aria-checked', 'true');
  await expect(page.locator('[data-testid="image-picker"]')).toHaveCount(2); // avatar + CV
  await expect(page.locator('[data-testid="manage-experience-link"]')).toBeVisible();
  await expect(page.locator('[data-testid="manage-awards-link"]')).toBeVisible();

  await page.screenshot({ path: `${SHOTS}/req-ui-040-profile-1280.png`, fullPage: true });
  await assertNoHorizontalScroll(page, 'profile@1280');
  await assertNothingOffViewport(page, 'profile@1280');

  // THE DEFECT at 390px: clear-image sat at the BOTTOM of the avatar preview, on top of the
  // action row, because trblazeui.css ships no `.top-1` rule (TR-059).
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${SHOTS}/req-ui-040-profile-390.png`, fullPage: true });
  await assertNoHorizontalScroll(page, 'profile@390');
  await assertNothingOffViewport(page, 'profile@390');

  const clear = await boxOf(page, 'clear-image');
  const upload = await boxOf(page, 'upload-new-image');
  const preview = await boxOf(page, 'selected-image');
  expect(clear, 'clear-image is not rendered at all').not.toBeNull();
  expect(upload, 'upload-new-image is not rendered').not.toBeNull();
  expect(preview, 'selected-image preview is not rendered').not.toBeNull();

  const overlaps =
    clear!.x < upload!.right && upload!.x < clear!.right &&
    clear!.y < upload!.bottom && upload!.y < clear!.bottom;
  expect(overlaps, `clear-image overlaps upload-new-image: clear=${JSON.stringify(clear)} upload=${JSON.stringify(upload)}`).toBe(false);

  // It must sit in the TOP half of the preview it belongs to, i.e. inside the frame.
  expect(clear!.y, 'clear-image is below the preview frame').toBeLessThan(preview!.y + preview!.h / 2);
  expect(clear!.bottom, 'clear-image spills out of the preview frame').toBeLessThanOrEqual(preview!.bottom + 1);

  // REQ-FN-053 REGRESSION GUARD — save with NO edits at all.
  await page.setViewportSize({ width: 1280, height: 1200 });
  await page.waitForTimeout(800);
  await page.locator('[data-testid="save-profile"]').click();
  await expect(page.locator('[data-testid="profile-status"]')).toContainText(/saved successfully/i, {
    timeout: 45000,
  });

  // Re-enter through the router so the next assertions read PERSISTED, not retained, state.
  await nav(page, '/admin/experience', /Manage Experience/i);
  await nav(page, '/admin/profile', /My Profile/i);
  for (const id of ['username-input', 'title-input', 'location-input']) {
    const value = await page.locator(`[data-testid="${id}"]`).inputValue();
    expect(value.trim(), `${id} was blanked by a no-edit save — REQ-FN-053 has regressed`).not.toBe('');
  }
  await expect(page.locator('[data-testid="resume-enabled-checkbox"]')).toHaveAttribute('aria-checked', 'true');
  await page.screenshot({ path: `${SHOTS}/req-ui-040-profile-after-save.png`, fullPage: true });

  // Downstream: the public resume still renders the owner's sections.
  await page.goto(`${BASE}/resume`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(2500);
  const bodyText = await page.locator('body').innerText();
  expect(bodyText.length, '/resume rendered empty after the no-edit save').toBeGreaterThan(200);
  await page.screenshot({ path: `${SHOTS}/req-ui-040-resume-after-save.png`, fullPage: true });
});
