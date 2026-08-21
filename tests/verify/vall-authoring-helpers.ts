/**
 * vall-authoring-helpers.ts — cluster-local helpers for the authoring verification run.
 *
 * Why these exist beside `_gates.ts`:
 *   1. `_gates.login()` waits a fixed 2s for the Blazor Server circuit before clicking submit.
 *      With seven verifier agents hammering the same host that wait is not always enough, and the
 *      click then submits the EditForm as a plain HTML POST — the host answers with
 *      "The POST request does not specify which form is being submitted" and the test dies with a
 *      misleading navigation timeout. `loginHard` proves interactivity before it clicks, and
 *      retries the whole sign-in when the circuit still lost the race.
 *   2. Several authoring screens (ManagePost, ManageSeries, ManageCategory, PreviewPost) render
 *      their title inside a `ContentPanel`/`Card`, i.e. NOT in an `<h1>`/`<h2>`, so `_gates.nav()`'s
 *      heading gate cannot be used. `goTo` gates on an arbitrary destination-owned selector
 *      instead, which is the same discipline expressed differently.
 */
import { Page, expect } from '@playwright/test';
import { USERS, RoleKey, BASE } from './_gates';

export { BASE, USERS };

/**
 * Waits until the prerendered login form has been taken over by the interactive circuit.
 *
 * The precise signal: while a component is still PRERENDERED, `EditForm` sees a non-interactive
 * renderer and emits `<form method="post" action="/login">`. When the circuit attaches and
 * re-renders, the `action` attribute is dropped (the submit now travels over the circuit) while
 * `method="post"` and the antiforgery hidden field are left behind by the diff — so `action` is
 * the only trustworthy marker of the switch. Measured attach time on this host: ~2.8s idle,
 * much longer under the 7-agent load. Clicking before the switch performs a real browser POST,
 * which the host rejects with "The POST request does not specify which form is being submitted"
 * (the EditForm carries no `FormName`) — the exact failure that killed this cluster's first three
 * runs inside a misleading navigation timeout. `window.Blazor` and even
 * `Blazor.defaultReconnectionHandler` exist ~1s after load and are NOT usable readiness signals.
 */
async function formInteractive(page: Page, timeout: number) {
  await page.waitForFunction(
    () => {
      const f = document.querySelector('form');
      return !!f && !f.hasAttribute('action');
    },
    { timeout },
  );
}

/** Signs in through the real login form, proving circuit readiness first. Retries on a lost race. */
export async function loginHard(page: Page, role: RoleKey = 'admin'): Promise<string> {
  const user = USERS[role];
  let lastErr = '';
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 90000 });
      await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
      await formInteractive(page, 120000);
      await page.waitForTimeout(1000);

      await page.fill('[data-testid="login-email"]', user.email);
      await page.fill('[data-testid="login-password"]', user.password);
      await page.click('[data-testid="login-submit"]');
      await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
      await page.waitForTimeout(2500);
      return page.url();
    } catch (e) {
      lastErr = `${String(e).slice(0, 160)} | body=${(await page.evaluate(() => document.body.innerText.slice(0, 160)).catch(() => ''))}`;
    }
  }
  throw new Error(`loginHard(${role}) failed after 3 attempts. Last: ${lastErr}`);
}

/**
 * Authenticated navigation gated on a selector the DESTINATION owns.
 * The URL flips before the destination renders, so gating on the URL measures the previous screen.
 */
export async function goTo(page: Page, route: string, readySelector: string, timeout = 60000) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  await page.waitForSelector(readySelector, { timeout, state: 'visible' });
  await page.waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(700);
}

/**
 * Picks an option in a TrBlazeUI `Select`. The trigger opens a listbox rendered elsewhere in the
 * DOM, so the option is matched globally by its visible text.
 */
export async function pickSelect(page: Page, triggerTestId: string, optionText: string | RegExp) {
  const option = page.locator('[role="option"]').filter({ hasText: optionText }).first();
  // The trigger occasionally swallows the first click while the circuit is busy re-rendering,
  // leaving the listbox closed; reopening is cheap and removes a flake that failed a whole run.
  for (let attempt = 1; attempt <= 3; attempt++) {
    await page.click(`[data-testid="${triggerTestId}"]`);
    try {
      await expect(option).toBeVisible({ timeout: 8000 });
      await option.click();
      await page.waitForTimeout(800);
      return;
    } catch {
      await page.keyboard.press('Escape').catch(() => {});
      await page.waitForTimeout(1200);
    }
  }
  throw new Error(`could not pick "${optionText}" in select "${triggerTestId}" after 3 attempts`);
}

/**
 * Sets the Markdown body deterministically.
 *
 * `PostMarkdownEditor`'s Textarea round-trips every change to the server and the resulting
 * re-render rewrites the DOM value, so a single `fill()` can be silently undone by a re-render
 * still in flight from earlier input. This writes, commits with an explicit `change`, then
 * VERIFIES the value stuck — and retries. It returns whether the editor accepted the text, so a
 * test can report an editor that will not hold content instead of dying on a later assertion.
 */
export async function setMarkdown(page: Page, text: string, attempts = 4): Promise<boolean> {
  const el = page.locator('[data-testid="markdown-input"]');
  for (let i = 0; i < attempts; i++) {
    await el.fill(text);
    await el.dispatchEvent('change');
    await page.waitForTimeout(2500);
    if ((await el.inputValue()) === text) return true;
  }
  return (await el.inputValue()) === text;
}

/** Reads a whole `data-testid` column as trimmed strings. */
export async function texts(page: Page, testId: string): Promise<string[]> {
  return page.$$eval(`[data-testid="${testId}"]`, (ns) => ns.map((n) => (n.textContent || '').trim()));
}

/**
 * Reads two cells from the SAME table row, so a count can never be compared against another
 * row's name. Reading two independent `data-testid` columns and pairing them by index silently
 * misaligns the moment one column renders a different number of nodes.
 */
export async function rowPairs(page: Page, keyTestId: string, valueTestId: string): Promise<[string, string][]> {
  return page.evaluate(
    ([k, v]) =>
      Array.from(document.querySelectorAll('tr'))
        .map((tr) => [tr.querySelector(`[data-testid="${k}"]`), tr.querySelector(`[data-testid="${v}"]`)] as const)
        .filter(([a, b]) => a && b)
        .map(([a, b]) => [(a!.textContent || '').trim(), (b!.textContent || '').trim()] as [string, string]),
    [keyTestId, valueTestId],
  );
}

/**
 * Fills an input/textarea and commits the value to Blazor.
 * TrBlazeUI's `Input`/`Textarea` bind on `change`; Playwright's `fill` only raises `input`, so a
 * plain `fill` leaves the component's bound property untouched and the save writes the old value.
 */
export async function fillCommitted(page: Page, testId: string, value: string) {
  const el = page.locator(`[data-testid="${testId}"]`);
  await el.fill(value);
  await el.dispatchEvent('change');
  await page.waitForTimeout(600);
}

export const SHOTS = '.verify/shots/authoring';
export const MARK = 'VERIFY-0808-';
