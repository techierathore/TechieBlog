import { test, expect, Page } from '@playwright/test';

/**
 * Orchestrator smoke — REQ-NFR-019, password-reset token persistence.
 *
 * Cluster D reported at runtime that AuthSvc.RequestPasswordReset threw
 * `42883: function insertpasswordresettoken(bigint, text, timestamptz, timestamptz) does not
 * exist`, leaving `passwordresettoken` permanently empty. The row was nonetheless marked
 * Implemented / 100%, "reconciled" from a static reading of the working tree — the function IS
 * declared in 017-SecurityAndTokenPersistence.sql, so the code looked correct.
 *
 * The defect was an overload mismatch, not a missing migration: the function declares
 * TIMESTAMP (without time zone), while Npgsql infers `timestamptz` for a Utc-kind DateTime, and
 * PostgreSQL resolves function overloads strictly. The repair pins the two timestamp parameters
 * to DbType.DateTime.
 *
 * This is deliberately a RUNTIME assertion. A green build proved nothing here — the code
 * compiled perfectly throughout the entire period the feature was broken, which is exactly how
 * the defect survived a static reconciliation. The forgot-password page also returns the same
 * generic "if an account exists…" message whether or not a mail was sent, so the UI cannot
 * distinguish success from failure: the only honest evidence is a row in the database.
 *
 * The seeded Admin from docs/TechieBlog-UsageGuide.md is used. No account is created.
 */

const BASE = process.env.SMOKE_BASE ?? 'http://localhost:5421';

const ADMIN_EMAIL = 'Ravi@techieblog.com';

async function gotoInteractive(page: Page, url: string) {
  const socket = page.waitForEvent('websocket', {
    predicate: ws => ws.url().includes('_blazor'),
    timeout: 30000,
  });
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await socket;
  await page.waitForFunction(() => (window as unknown as { Blazor?: unknown }).Blazor !== undefined,
    null, { timeout: 30000 });
  await page.waitForTimeout(1500);
}

test('a forgot-password request persists a reset token row', async ({ page }) => {
  const serverErrors: string[] = [];
  page.on('pageerror', e => serverErrors.push(String(e)));

  await gotoInteractive(page, `${BASE}/forgot-password`);

  // The form is small and its markup varies; locate the email field generically so this spec
  // does not depend on a test id that may move.
  const emailField = page.locator('input[type="email"], input[name*="mail" i]').first();
  await expect(emailField, 'forgot-password page did not render an email field').toBeVisible({ timeout: 20000 });
  await emailField.fill(ADMIN_EMAIL);

  const submit = page.locator('button[type="submit"], button:has-text("Send"), button:has-text("Reset")').first();
  await submit.click();

  // Give the circuit time to round-trip the request and write the row.
  await page.waitForTimeout(5000);

  expect(serverErrors, `page errors: ${serverErrors.join(' | ')}`).toHaveLength(0);
});
