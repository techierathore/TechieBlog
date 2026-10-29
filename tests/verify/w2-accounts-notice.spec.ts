/**
 * w2-accounts-notice.spec.ts — triage T3, 2026-10-28. Split out of w2-accounts.spec.ts (that file alone fills the
 * 580 s graded-run budget). One test, one clause of REQ-FN-006: the "created successfully" notice.
 *
 * Genuine defect this test pins: AddUser.razor sets the success StatusMessage and then `await Task.Delay(1500)`
 * before NavigateTo, but a Blazor event handler only re-renders when it finishes, so the notice reaches the DOM in the
 * same batch as the redirect and the admin never sees it. The fixture account `fxv9-notice-…@example.com` is created
 * through /AddUser and removed through /users (soft delete) in `finally`.
 */
import { test, expect, Browser } from '@playwright/test';
import { execSync } from 'child_process';
import * as fs from 'fs';
import { loginHard, goTo, fillCommitted, pickSelect } from './vall-authoring-helpers';

const BASE = process.env.TB_BASE ?? 'http://localhost:5450';
const SHOTS = 'tests/.artifacts/verify/T3/w2-notice';
fs.mkdirSync(SHOTS, { recursive: true });
const RUN = Date.now().toString(36);
const PW = 'Fxv9#Notice2026';

function sql(query: string): string[] {
  const out = execSync(`docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '|' -c "${query.replace(/"/g, '\\"')}"`, { encoding: 'utf8' });
  return out.split('\n').map((r) => r.trim()).filter((r) => r.length > 0);
}

async function deleteUserThroughUi(browser: Browser, email: string) {
  if (!sql(`SELECT 1 FROM bloguser WHERE emailid = '${email}' AND NOT COALESCE(isdeleted,false)`).length) return;
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  try {
    await loginHard(page, 'admin');
    await goTo(page, '/users', '[data-testid="users-grid"]', 90000);
    await page.fill('[data-testid="users-search"]', email);
    await page.dispatchEvent('[data-testid="users-search"]', 'change');
    await page.waitForTimeout(2000);
    await page.locator('tbody tr').filter({ hasText: email }).locator('[data-testid="user-delete"]').click();
    await page.click('[data-testid="user-delete-confirm"]');
    await page.waitForTimeout(4000);
    expect(sql(`SELECT isdeleted FROM bloguser WHERE emailid = '${email}'`)[0], `${email} removed through the UI`).toBe('t');
  } finally {
    await ctx.close();
  }
}

test('REQ-FN-006 the "created successfully" notice is on screen on the add-user page before the redirect', async ({ browser }) => {
  test.setTimeout(600000);
  const email = `fxv9-notice-${RUN}@example.com`;
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  try {
    await loginHard(page, 'admin');
    await goTo(page, '/AddUser', '[data-testid="add-user-form"]', 90000);
    await page.evaluate(() => {
      (window as any).__seen = false;
      new MutationObserver(() => {
        const e = document.querySelector('[data-testid="add-user-status-message"]');
        if (e && /created successfully/i.test(e.textContent || '') && location.pathname.toLowerCase().includes('adduser')) (window as any).__seen = true;
      }).observe(document.body, { subtree: true, childList: true, characterData: true });
    });
    await fillCommitted(page, 'user-first-name', 'Fxv9');
    await fillCommitted(page, 'user-last-name', 'Notice');
    await fillCommitted(page, 'user-email', email);
    await fillCommitted(page, 'user-password', PW);
    await fillCommitted(page, 'user-confirm-password', PW);
    await pickSelect(page, 'user-role', 'Author');
    await page.click('[data-testid="add-user-submit"]');
    await page.waitForURL((u) => u.pathname.toLowerCase() === '/users', { timeout: 90000 });
    expect(sql(`SELECT count(*) FROM bloguser WHERE emailid = '${email}'`)[0], 'the account was created').toBe('1');
    await page.screenshot({ path: `${SHOTS}/fn006-notice-after-redirect.png` });
    expect(await page.evaluate(() => (window as any).__seen), 'the success notice was visible on /AddUser before the redirect').toBe(true);
  } finally {
    await ctx.close();
    await deleteUserThroughUi(browser, email);
    for (const left of sql(`SELECT emailid FROM bloguser WHERE emailid LIKE 'fxv9-%' AND NOT COALESCE(isdeleted,false)`)) await deleteUserThroughUi(browser, left);
  }
});
