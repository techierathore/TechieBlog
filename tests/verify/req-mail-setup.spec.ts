/**
 * Mail set-up and reader confirmation, through the real screens (owner request 2026-11-26).
 *
 * REQ-FN-033  the sender reads the mail server from Site Settings at every send
 * REQ-UI-072  Settings → Email → "Send test mail" shows the mail server's real answer, good or bad
 * REQ-UI-073  the admin dashboard warns until a mail server is set
 * REQ-UI-027  after a rating the reader is told where the link went (masked), or that confirmation is
 *             not available now
 * REQ-FN-048  the mailed link confirms the rating and it counts
 *
 * RUN ONLY AGAINST A THROWAWAY DATABASE. The host must be booted on a `tbsmoke_…` database with one
 * published post with slug `mail-smoke-post` and the seed admin's first-login flag cleared. The spec
 * itself clears the mail server through the Settings screen first, so it can be run again on the same
 * database; every reader address is unique per run. No database is touched from here.
 *
 * Environment: TB_BASE (site), TB_MAIL_PORT + TB_MAIL_DIR (an SMTP catcher listening on 127.0.0.1 that
 * writes each mail as NNN.eml into TB_MAIL_DIR), TB_ADMIN_EMAIL / TB_ADMIN_PASSWORD (default: the
 * public bootstrap seed of 003-SeedData.sql, valid only on a fresh database).
 */
import { test, expect, Page, Locator } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://localhost:5399';
const MAIL_PORT = process.env.TB_MAIL_PORT ?? '2525';
const MAIL_DIR = process.env.TB_MAIL_DIR ?? 'tests/.artifacts/mailjob/mail';
const ADMIN_EMAIL = process.env.TB_ADMIN_EMAIL || 'Ravi@techieblog.com';
const ADMIN_PASSWORD = process.env.TB_ADMIN_PASSWORD || 'admin_password';
const SHOTS = 'tests/.artifacts/mailjob/shots';
const POST = '/post/mail-smoke-post';

test.describe.configure({ mode: 'serial' });

async function gotoLive(page: Page, route: string) {
  const ws = page.waitForEvent('websocket', { timeout: 60000 }).catch(() => null);
  await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
  await ws;
  await page.waitForTimeout(1500);
}

function wordValue(w: string): number {
  const words = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten',
    'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen', 'twenty'];
  return words.indexOf(w.toLowerCase());
}

function answerQuestion(q: string): string {
  let m = q.match(/what is\s+([a-z]+)\s+plus\s+([a-z]+)/i);
  if (m) return String(wordValue(m[1]) + wordValue(m[2]));
  m = q.match(/what is\s+([a-z]+)\s+minus\s+([a-z]+)/i);
  if (m) return String(wordValue(m[1]) - wordValue(m[2]));
  m = q.match(/how many letters are in the word '([^']+)'/i);
  if (m) return String(m[1].length);
  m = q.match(/how many words are in this line: '([^']+)'/i);
  if (m) return String(m[1].split(/\s+/).filter(Boolean).length);
  throw new Error(`Unrecognised captcha question: ${q}`);
}

/** Answers the captcha in its accessible question mode (REQ-UI-057). */
async function solveCaptcha(scope: Locator) {
  const prompt = scope.locator('[data-testid="captcha-prompt"]').first();
  await expect(prompt).toBeVisible({ timeout: 60000 });
  await expect.poll(async () => scope.locator('[data-testid="captcha-image-placeholder"]').count(), { timeout: 60000 }).toBe(0);
  if (await scope.locator('[data-testid="captcha-image"]').count()) {
    await scope.locator('[data-testid="captcha-mode-toggle"]').first().click();
    await expect(scope.locator('[data-testid="captcha-image"]')).toHaveCount(0, { timeout: 30000 });
  }
  let question = '';
  for (let i = 0; i < 20 && (!question || /Loading/i.test(question)); i++) {
    question = ((await prompt.textContent()) || '').trim();
    if (!question || /Loading/i.test(question)) await scope.page().waitForTimeout(700);
  }
  await scope.locator('[data-testid="captcha-answer"]').first().fill(answerQuestion(question));
}

async function rate(page: Page, email: string) {
  const panel = page.locator('[data-testid="post-rating-panel"]');
  await panel.locator('[data-testid="post-rating-stars"] [role="radio"]').nth(4).click();
  await expect(panel.locator('[data-testid="rating-identify-step"]')).toBeVisible();
  await panel.locator('[data-testid="rating-email"]').fill(email);
  await solveCaptcha(panel);
  await panel.locator('[data-testid="rating-submit"]').click();
}

async function signIn(page: Page) {
  await gotoLive(page, '/login');
  // The prerendered form is replaced when the circuit attaches; retry until the typed values stick
  // and the sign-in actually leaves /login.
  await expect(async () => {
    await page.locator('[data-testid="login-email"]').fill(ADMIN_EMAIL);
    await page.locator('[data-testid="login-password"]').fill(ADMIN_PASSWORD);
    await expect(page.locator('[data-testid="login-email"]')).toHaveValue(ADMIN_EMAIL, { timeout: 2000 });
    await page.locator('[data-testid="login-submit"]').click();
    await page.waitForURL(u => !u.pathname.toLowerCase().startsWith('/login'), { timeout: 15000 });
  }).toPass({ timeout: 90000 });
}

async function openEmailTabOnly(page: Page) {
  await gotoLive(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 60000 });
  await expect(async () => {
    await page.locator('[data-testid="tab-email"]').click();
    await expect(page.locator('[data-testid="smtp-host"]')).toBeVisible({ timeout: 3000 });
  }).toPass({ timeout: 60000 });
}

async function openEmailTab(page: Page, port: string) {
  await gotoLive(page, '/settings');
  await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 60000 });
  // The tab only switches once the circuit is live; click until the Email fields show.
  await expect(async () => {
    await page.locator('[data-testid="tab-email"]').click();
    await expect(page.locator('[data-testid="smtp-host"]')).toBeVisible({ timeout: 3000 });
  }).toPass({ timeout: 60000 });
  await page.locator('[data-testid="smtp-host"]').fill('127.0.0.1');
  await page.locator('[data-testid="smtp-port"]').fill(port);
  await page.locator('[data-testid="smtp-from-address"]').fill('blog@smoke.test');
  const ssl = page.locator('[data-testid="smtp-ssl"]');
  if ((await ssl.getAttribute('aria-checked')) === 'true' || (await ssl.getAttribute('data-state')) === 'checked') await ssl.click();
  await page.locator('[data-testid="smtp-test-to"]').fill('owner@smoke.test');
}

function caughtMailTo(address: string): string | null {
  if (!fs.existsSync(MAIL_DIR)) return null;
  for (const f of fs.readdirSync(MAIL_DIR).sort()) {
    const raw = fs.readFileSync(path.join(MAIL_DIR, f), 'utf8');
    if (raw.toLowerCase().includes(`x-catcher-rcpt: ${address.toLowerCase()}`)) return raw;
  }
  return null;
}

function decodedLink(raw: string): string {
  const qp = raw.replace(/=\r?\n/g, '').replace(/=([0-9A-F]{2})/gi, (_, h) => String.fromCharCode(parseInt(h, 16)));
  let m = qp.match(/https?:\/\/[^\s"'<>]+\/verify\/[A-Za-z0-9_-]+/);
  if (!m) {
    for (const chunk of raw.split(/\r?\n\r?\n/)) {
      try {
        const text = Buffer.from(chunk.replace(/\s/g, ''), 'base64').toString('utf8');
        m = text.match(/https?:\/\/[^\s"'<>]+\/verify\/[A-Za-z0-9_-]+/);
        if (m) break;
      } catch { /* not base64 */ }
    }
  }
  if (!m) throw new Error('No confirmation link in the caught mail');
  return m[0];
}

const RUN = Date.now().toString(36);
const RATER = `rater-${RUN}@smoke.test`;
const COMMENTER = `commenter-${RUN}@smoke.test`;
const READER = `reader-${RUN}@smoke.test`;
let ratingMail = '';

async function ratingCount(page: Page): Promise<number> {
  const text = (await page.locator('[data-testid="post-rating-count"]').innerText()).replace(/[^0-9]/g, '');
  return Number(text || '0');
}

async function waitForMail(address: string): Promise<string> {
  await expect.poll(() => caughtMailTo(address) !== null, { timeout: 30000 }).toBe(true);
  return caughtMailTo(address)!;
}

test('REQ-UI-073 the dashboard warns while no mail server is set', async ({ page }) => {
  fs.mkdirSync(SHOTS, { recursive: true });
  await signIn(page);
  await openEmailTabOnly(page);
  await page.locator('[data-testid="smtp-host"]').fill('');
  await page.locator('[data-testid="save-settings"]').click();
  await page.waitForTimeout(2500);
  await gotoLive(page, '/admin');
  await expect(page.locator('[data-testid="dashboard-mail-warning"]')).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/2-dashboard-warning.png`, fullPage: true });
});

test('REQ-UI-027 with no mail server the rating form says confirmation is not available and keeps nothing', async ({ page }) => {
  await gotoLive(page, POST);
  await expect(page.locator('[data-testid="rating-mail-unavailable"]')).toBeVisible();
  const before = await ratingCount(page);
  await rate(page, `early-${RUN}@smoke.test`);
  await expect(page.locator('[data-testid="rating-form-error"]')).toContainText('Email confirmation is not available right now');
  await page.screenshot({ path: `${SHOTS}/1-post-no-mail-server.png`, fullPage: true });
  await gotoLive(page, POST);
  expect(await ratingCount(page)).toBe(before);
});

test('REQ-UI-029 with no mail server the comment form says confirmation is not available', async ({ page }) => {
  await gotoLive(page, POST);
  await expect(page.locator('[data-testid="comment-mail-unavailable"]')).toBeVisible();
});

test('REQ-UI-030 with no mail server the subscribe forms say confirmation is not available', async ({ page }) => {
  await gotoLive(page, '/newsletters');
  await expect(page.locator('[data-testid="newsletter-subscribe-mail-unavailable"]')).toBeVisible();
  await gotoLive(page, POST); // the sidebar subscribe box is on the article pages
  await expect(page.locator('[data-testid="subscribe-mail-unavailable"]')).toBeVisible();
});

test('REQ-UI-072 send test mail shows the refusal when nothing answers on the port', async ({ page }) => {
  await signIn(page);
  await openEmailTab(page, '2599');
  await page.locator('[data-testid="smtp-test-send"]').click();
  await expect(page.locator('[data-testid="smtp-test-refused"]')).toBeVisible({ timeout: 60000 });
  await expect(page.locator('[data-testid="smtp-test-answer"]')).toContainText('127.0.0.1:2599');
  await page.locator('[data-testid="smtp-test-answer"]').scrollIntoViewIfNeeded();
  await page.screenshot({ path: `${SHOTS}/3-test-mail-refused.png` });
});

test('REQ-UI-072 send test mail is accepted by a listening server, then saving makes it the site mail server', async ({ page }) => {
  await signIn(page);
  await openEmailTab(page, MAIL_PORT);
  await page.locator('[data-testid="smtp-test-send"]').click();
  await expect(page.locator('[data-testid="smtp-test-accepted"]')).toBeVisible({ timeout: 60000 });
  await waitForMail('owner@smoke.test');
  await page.locator('[data-testid="smtp-test-answer"]').scrollIntoViewIfNeeded();
  await page.screenshot({ path: `${SHOTS}/4-test-mail-accepted.png` });
  await page.locator('[data-testid="save-settings"]').click();
  await page.waitForTimeout(2500);
  await gotoLive(page, '/admin');
  await expect(page.locator('[data-testid="dashboard-mail-warning"]')).toHaveCount(0);
  await page.screenshot({ path: `${SHOTS}/5-dashboard-no-warning.png`, fullPage: true });
});

test('REQ-FN-033 the mail server saved on the Settings screen carries the next reader mail, with no restart', async ({ page }) => {
  await gotoLive(page, POST);
  await expect(page.locator('[data-testid="rating-mail-unavailable"]')).toHaveCount(0);
  await rate(page, RATER);
  await expect(page.locator('[data-testid="rating-form-success"]')).toContainText(`We sent a link to r•••@smoke.test`);
  await page.screenshot({ path: `${SHOTS}/6-rating-link-sent.png`, fullPage: true });
  ratingMail = await waitForMail(RATER);
});

test('REQ-FN-048 the mailed link confirms the rating and it counts', async ({ page }) => {
  expect(ratingMail, 'the rating mail from the previous test').not.toBe('');
  await gotoLive(page, POST);
  const before = await ratingCount(page);
  await gotoLive(page, new URL(decodedLink(ratingMail)).pathname);
  await expect(page.locator('[data-testid="verify-confirmed"]')).toBeVisible({ timeout: 60000 });
  await page.screenshot({ path: `${SHOTS}/7-link-opened.png`, fullPage: true });
  await gotoLive(page, POST);
  expect(await ratingCount(page)).toBe(before + 1);
  await page.screenshot({ path: `${SHOTS}/8-rating-counted.png`, fullPage: true });
});

test('REQ-UI-027 after a rating the reader is told where the link went, masked', async ({ page }) => {
  await gotoLive(page, POST);
  await rate(page, `second-${RUN}@smoke.test`);
  await expect(page.locator('[data-testid="rating-form-success"]')).toContainText('We sent a link to s•••@smoke.test');
  await expect(page.locator('body')).not.toContainText(`second-${RUN}@smoke.test`);
});

test('REQ-UI-029 after a comment the reader is told where the link went, masked, and the link confirms it', async ({ page }) => {
  await gotoLive(page, POST);
  const form = page.locator('[data-testid="comment-form"]');
  await form.locator('[data-testid="comment-name"]').fill('Smoke Commenter');
  await form.locator('[data-testid="comment-email"]').fill(COMMENTER);
  await form.locator('[data-testid="comment-input"]').fill('A comment sent through the real confirmation mail.');
  await page.waitForTimeout(4000);
  await solveCaptcha(form);
  await form.locator('[data-testid="comment-submit"]').click();
  await expect(page.locator('[data-testid="comment-form-success"]')).toContainText('We sent a link to c•••@smoke.test');
  await page.screenshot({ path: `${SHOTS}/9-comment-link-sent.png`, fullPage: true });
  await gotoLive(page, new URL(decodedLink(await waitForMail(COMMENTER))).pathname);
  await expect(page.locator('[data-testid="verify-confirmed"]')).toBeVisible({ timeout: 60000 });
});

test('REQ-UI-030 after subscribing the reader is told where the link went, masked, and the link confirms it', async ({ page }) => {
  await gotoLive(page, '/newsletters');
  const card = page.locator('[data-testid="newsletter-subscribe"]').first();
  await page.locator('[data-testid="newsletter-subscribe-email"]').fill(READER);
  await solveCaptcha(card);
  await page.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await expect(page.locator('[data-testid="newsletter-subscribe-status"]')).toContainText('We sent a link to r•••@smoke.test');
  await page.screenshot({ path: `${SHOTS}/10-subscribe-link-sent.png`, fullPage: true });
  await gotoLive(page, new URL(decodedLink(await waitForMail(READER))).pathname);
  await expect(page.locator('[data-testid="verify-subscribed"]')).toBeVisible({ timeout: 60000 });
});
