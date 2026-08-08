import { test, expect } from '@playwright/test';

/**
 * [REQ-FN-053] Regression guard for the resume data-loss defect.
 *
 * SelectBlogUserById projected only 17 of BlogUser's 26 columns, omitting
 * Username, IsSiteOwner, Title, Tagline, InstagramUrl, PhoneNumber, Location,
 * CVFilePath and ResumeEnabled. ManageProfile loads through that function,
 * binds the result into its form, and writes the form back on Save - so the
 * nine unloaded columns rendered blank and were then PERSISTED as blank.
 * Opening Manage Profile and pressing Save erased the site owner's entire
 * resume and switched ResumeEnabled off.
 *
 * This test opens the page and saves WITHOUT EDITING ANYTHING. Every resume
 * field must survive byte-for-byte. That is the exact user action that
 * destroyed the data, so a regression fails here rather than in production.
 */

const BASE = process.env.TB_BASE_URL ?? 'http://127.0.0.1:5490';

// Populated by the harness from a live psql read before the browser starts.
const EXPECTED = {
  username: process.env.TB_EXP_USERNAME!,
  title: process.env.TB_EXP_TITLE!,
  tagline: process.env.TB_EXP_TAGLINE!,
  location: process.env.TB_EXP_LOCATION!,
  phone: process.env.TB_EXP_PHONE!,
  instagram: process.env.TB_EXP_INSTAGRAM!,
};

test('saving Manage Profile unedited preserves every resume field', async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on('pageerror', e => consoleErrors.push(e.message));

  // Blazor Server renders statically first. Clicking before the interactive
  // circuit attaches submits a plain HTML form, which Blazor rejects with
  // "The POST request does not specify which form is being submitted".
  // Wait for interactivity rather than racing it.
  await page.goto(`${BASE}/login`);
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await expect(page.getByTestId('login-card')).toBeVisible();
  await page.waitForTimeout(1500);

  await page.getByTestId('login-email').fill('Ravi@techieblog.com');
  await page.getByTestId('login-password').fill('admin_password');
  await page.getByTestId('login-submit').click();
  await page.waitForURL(u => !u.pathname.endsWith('/login'), { timeout: 20000 });

  // A hard page.goto to any /admin route logs the session out: the JWT lives in
  // localStorage and the server cannot see it during prerender (a known blocker).
  // Navigate through the app's own router so the circuit and session survive.
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/profile'));
  await expect(page.getByTestId('manage-profile-page')).toBeVisible({ timeout: 20000 });
  await expect(page.getByTestId('profile-loading')).toHaveCount(0);

  // RENDER-TRUTH: the form must show the stored values, not empty boxes.
  // This is the half of the defect a user could actually see.
  await expect(page.getByTestId('title-input')).toHaveValue(EXPECTED.title);
  await expect(page.getByTestId('tagline-input')).toHaveValue(EXPECTED.tagline);
  await expect(page.getByTestId('location-input')).toHaveValue(EXPECTED.location);
  await expect(page.getByTestId('phone-input')).toHaveValue(EXPECTED.phone);
  await expect(page.getByTestId('instagram-input')).toHaveValue(EXPECTED.instagram);
  await expect(page.getByTestId('resume-enabled-checkbox')).toBeChecked();

  // Save with no edits - the exact action that used to destroy the row.
  await page.getByTestId('save-profile').click();
  await expect(page.getByTestId('profile-status')).toContainText(/saved/i, { timeout: 20000 });

  // Re-enter the page through the router to prove we are reading PERSISTED
  // state, not retained form state. A reload would drop the localStorage
  // session before the server could read it, so route away and back instead.
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin'));
  await page.waitForTimeout(1000);
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/profile'));
  await expect(page.getByTestId('manage-profile-page')).toBeVisible({ timeout: 20000 });
  await expect(page.getByTestId('title-input')).toHaveValue(EXPECTED.title);
  await expect(page.getByTestId('tagline-input')).toHaveValue(EXPECTED.tagline);
  await expect(page.getByTestId('location-input')).toHaveValue(EXPECTED.location);
  await expect(page.getByTestId('phone-input')).toHaveValue(EXPECTED.phone);
  await expect(page.getByTestId('instagram-input')).toHaveValue(EXPECTED.instagram);
  await expect(page.getByTestId('resume-enabled-checkbox')).toBeChecked();

  expect(consoleErrors).toEqual([]);
});
