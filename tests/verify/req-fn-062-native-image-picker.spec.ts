/**
 * REQ-FN-062 / production report 2026-10-21 — BlogApp crashed natively inside WebView2's file
 * dialog while the owner chose images. The desktop head now picks through the OS picker
 * (INativeImagePicker); the WEBSITE registers none and must keep TrBlazeUI's FileUpload dropzone
 * exactly as before. This spec guards the website half: a real file input, no native button, and a
 * staged file passes the category validation and shows as selected.
 */
import { test, expect } from '@playwright/test';
import { login, nav, USERS } from './_gates';

// _gates.ts still carries the retired admin password; the UsageGuide's test-user table is current.
(USERS.admin as { password: string }).password = process.env.TB_ADMIN_PASSWORD ?? 'VerifyAdmin#2026';

const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64');

test('REQ-FN-062 the website upload dialog keeps its file input and stages a chosen image', async ({ page }) => {
  await login(page, 'admin');
  await nav(page, '/admin/images');

  await page.click('[data-testid="upload-image"]');
  const dropzone = page.locator('[data-testid="upload-dropzone"]');
  await expect(dropzone).toBeVisible();

  await expect(dropzone.locator('input[type="file"]')).toHaveCount(1);
  await expect(page.locator('[data-testid="upload-choose-native"]')).toHaveCount(0);

  // The dropzone's InputFile listener attaches after the dialog renders; a file set before that is
  // dropped silently. Set it again until the dialog reports the staged file.
  const staged = page.locator('[data-testid="upload-selected-file"]');
  for (let attempt = 0; attempt < 4 && !(await staged.isVisible()); attempt++) {
    await page.waitForTimeout(1500);
    await dropzone.locator('input[type="file"]').setInputFiles({
      name: 'probe.png', mimeType: 'image/png', buffer: PNG_1X1,
    });
    // Validation runs server-side over the circuit (ValidateImageAsync), so allow for the round trip.
    await staged.waitFor({ state: 'visible', timeout: 8000 }).catch(() => {});
  }
  await expect(staged).toContainText('probe.png');
});
