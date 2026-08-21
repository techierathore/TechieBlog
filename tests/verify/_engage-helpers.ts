/**
 * _engage-helpers.ts — shared plumbing for the engagement / search / analytics verification cluster.
 *
 * Only the verifier uses this; no application code imports it. Two things live here that every
 * engagement test needs:
 *   1. `solveCaptcha` — the captcha's ACCESSIBLE question mode (REQ-UI-057) is the only form a
 *      headless browser can answer honestly. The image mode is deliberately unreadable by machine
 *      (REQ-FN-049), so a test that "solved" it would be proving the captcha broken.
 *   2. `psql` — read-only cross-checks against the shared WinPostgre container.
 */
import { execSync } from 'child_process';
import { Locator, Page, expect } from '@playwright/test';

export const SHOTS = '.verify/shots/engage';

/** Marker every row this cluster writes carries, so the owner can find and drop them. */
export const MARK = 'VERIFY-0808';

/** Email prefix reserved for this cluster. */
export const mail = (tag: string) => `verify0808+${tag}@techieblog.test`;

/** Runs a read-only (or evidence-gathering) statement inside the shared WinPostgre container. */
export function psql(sql: string): string {
  const cmd = `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F'|' -c ${JSON.stringify(sql)}`;
  return execSync(cmd, { encoding: 'utf8' }).trim();
}

/** Single-scalar convenience over {@link psql}. */
export function psqlOne(sql: string): string {
  return psql(sql).split('\n')[0]?.trim() ?? '';
}

/**
 * Loads a PUBLIC page and waits until the Blazor circuit is actually live.
 *
 * The prerendered HTML arrives long before the websocket does on a loaded box, and a click sent in
 * that window is silently dropped — which is exactly how a working control looks broken.
 */
export async function gotoPublic(page: Page, path: string) {
  const ws = page.waitForEvent('websocket', { timeout: 60000 }).catch(() => null);
  await page.goto(`http://localhost:5399${path}`, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 60000 }).catch(() => {});
  const socket = await ws;
  if (socket) {
    await page
      .waitForFunction(() => !document.querySelector('#components-reconnect-modal.components-reconnect-show'), { timeout: 30000 })
      .catch(() => {});
  }
  await page.waitForTimeout(2500);
}

const NUMBER_WORDS = [
  'zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten',
  'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen',
  'eighteen', 'nineteen', 'twenty',
];

/** Turns an English number word into its value; -1 when it is not a number word. */
function wordValue(word: string): number {
  return NUMBER_WORDS.indexOf(word.trim().toLowerCase());
}

/**
 * Works out the answer to one of the four accessible-challenge shapes.
 * Returns null when the prose does not match any known shape — which is itself a finding.
 */
export function answerQuestion(question: string): string | null {
  let m = question.match(/what is\s+([a-z]+)\s+plus\s+([a-z]+)/i);
  if (m) {
    const a = wordValue(m[1]), b = wordValue(m[2]);
    return a >= 0 && b >= 0 ? String(a + b) : null;
  }
  m = question.match(/what is\s+([a-z]+)\s+minus\s+([a-z]+)/i);
  if (m) {
    const a = wordValue(m[1]), b = wordValue(m[2]);
    return a >= 0 && b >= 0 ? String(a - b) : null;
  }
  m = question.match(/how many letters are in the word '([^']+)'/i);
  if (m) return String(m[1].length);
  m = question.match(/how many words are in this line: '([^']+)'/i);
  if (m) return String(m[1].split(/\s+/).filter(Boolean).length);
  return null;
}

/**
 * Switches a captcha widget to its accessible question mode, reads the question and types the
 * computed answer. `scope` is the container that holds exactly ONE captcha widget.
 *
 * Returns the question text and the answer typed, so callers can assert the answer never appeared
 * in the markup.
 */
export async function solveCaptcha(scope: Locator, opts: { wrong?: boolean } = {}) {
  const prompt = scope.locator('[data-testid="captcha-prompt"]').first();
  const toggle = scope.locator('[data-testid="captcha-mode-toggle"]').first();
  const answerBox = scope.locator('[data-testid="captcha-answer"]').first();

  await expect(prompt).toBeVisible({ timeout: 60000 });

  // The challenge is issued in OnAfterRenderAsync, so wait for it before touching anything.
  await expect
    .poll(async () => (await scope.locator('[data-testid="captcha-image-placeholder"]').count()) === 0, {
      timeout: 90000,
      intervals: [1000],
    })
    .toBe(true);

  // Enter question mode when the image is showing.
  if (await scope.locator('[data-testid="captcha-image"]').count()) {
    await toggle.click();
    await expect(scope.locator('[data-testid="captcha-image"]')).toHaveCount(0, { timeout: 30000 });
  }

  let question = '';
  for (let i = 0; i < 20; i++) {
    question = ((await prompt.textContent()) || '').trim();
    if (question && !/Loading the verification question/i.test(question)) break;
    await scope.page().waitForTimeout(700);
  }

  const correct = answerQuestion(question);
  const typed = opts.wrong ? 'zzzz' : correct;
  if (typed === null) throw new Error(`Unrecognised captcha question shape: "${question}"`);

  await answerBox.fill(typed);
  return { question, answer: typed, correct };
}

/** True when a visible error anywhere on the page is the captcha rate limiter talking. */
export async function isRateLimited(page: Page): Promise<boolean> {
  const t = (await page.locator('body').innerText().catch(() => '')) || '';
  return /too many|try again in|rate limit/i.test(t);
}
