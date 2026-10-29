/**
 * fxv5-captcha-expiry.spec.ts — verify-phase writer W5, 2026-10-27 (captcha challenge expiry).
 * Rows: REQ-UI-027 029 030 055 056 057, REQ-FN-022 023 034 048 049.
 *
 * Black box and database-write-free: the posts are built through the admin UI (fxv5-a-setup), every
 * visitor action goes through the public forms, confirmation tokens are read with psql SELECTs (the
 * dev host has no SMTP, so the mail is not deliverable), moderation is done in /comments. Every
 * address carries the `fxv5-` prefix. Serial: later tests use the addresses earlier ones created.
 */
import { test, expect, Page, Locator, BrowserContext } from '@playwright/test';
import { gotoPublic, solveCaptcha, answerQuestion } from './_engage-helpers';
import { login, nav } from './_gates';
import { setupFixtures, teardownFixtures, sql, sqlOne, fxMail, RUN, postSlug, postId, post2Slug, post2Id, SHOTS } from './fxv5-fixtures';
import * as fs from 'fs';

test.describe.configure({ mode: 'serial' });
test.beforeAll(() => fs.mkdirSync(SHOTS, { recursive: true }));
test.beforeAll(async ({ browser }) => { test.setTimeout(600000); await setupFixtures(browser); });
test.afterAll(async ({ browser }) => { test.setTimeout(600000); await teardownFixtures(browser, { comments: false }); });

test.beforeEach(({}, ti) => ti.setTimeout(420000));

const NONCE = RUN;
const EMAIL_C = fxMail('comment');               // the commenter whose address becomes verified
const BODY_1 = `fxv5 first comment ref${NONCE}`;
const BODY_2 = `fxv5 second comment ref${NONCE}`;
const EMAIL_R = fxMail('rater');                 // rater
const EMAIL_S = fxMail('sub');                   // subscriber, newsletter card
const EMAIL_SB = fxMail('sidebar');              // subscriber, sidebar

const POST = () => `/post/${postSlug()}`;

async function openPost(page: Page, path = POST()) {
  await gotoPublic(page, path);
  await expect(page.locator('[data-testid="comments-section"]')).toBeVisible({ timeout: 60000 });
  await expect(page.locator('[data-testid="comment-form"]')).toBeVisible({ timeout: 60000 });
}

async function waitForChallenge(scope: Locator) {
  // The challenge is issued on the interactive render. The captcha limiter is shared by every
  // visitor on this host (20 challenges / min, 5 failures / 5 min per client) and other verify
  // writers use it concurrently, so a "too many attempts" state is waited out and the widget reloaded.
  for (let round = 0; round < 6; round++) {
    await expect(scope.locator('[data-testid="captcha-prompt"]').first()).toBeVisible({ timeout: 60000 });
    const ready = await expect
      .poll(async () => (await scope.locator('[data-testid="captcha-image-placeholder"]').count()) === 0, { timeout: 25000, intervals: [1000] })
      .toBe(true).then(() => true, () => false);
    if (ready) {
      const err = ((await scope.locator('[data-testid="captcha-error"]').first().innerText({ timeout: 2000 }).catch(() => '')) || '').replace(/\s+/g, ' ');
      if (!/too many/i.test(err)) return;
    }
    const t = ((await scope.locator('[data-testid="captcha-error"]').first().innerText({ timeout: 2000 }).catch(() => '')) || '').replace(/\s+/g, ' ');
    const m = t.match(/wait about (\d+) (second|minute)/i);
    const secs = m ? Number(m[1]) * (/minute/i.test(m[2]) ? 60 : 1) : 20;
    console.log(`captcha limiter active (${t || 'no message'}); waiting ${secs + 3}s`);
    await scope.page().waitForTimeout((secs + 3) * 1000);
    await scope.locator('[data-testid="captcha-reload"]').first().click().catch(() => {});
    await scope.page().waitForTimeout(2500);
  }
  throw new Error('captcha challenge never became available (limiter)');
}

/** Selects a star the way a keyboard visitor does (each star is a real button role=radio). */
async function chooseStar(page: Page, value: number) {
  const options = page.locator('[data-testid="post-rating-stars"] [role="radio"]');
  const step = page.locator('[data-testid="rating-identify-step"]');
  await expect(options).toHaveCount(5, { timeout: 60000 });
  await page.waitForTimeout(2500); // the circuit must own the control before a key press counts
  const option = options.nth(value - 1);
  await option.evaluate((el) => (el as HTMLElement).focus());
  await page.waitForTimeout(400);
  await page.keyboard.press('Enter');
  const shown = await step.waitFor({ state: 'visible', timeout: 6000 }).then(() => true, () => false);
  if (!shown) {
    await option.scrollIntoViewIfNeeded();
    await option.click({ force: true });
  }
}

/** Fills and submits the comment form with a solved captcha. */
async function postComment(page: Page, email: string, body: string, opts: { wrong?: boolean } = {}) {
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);
  await page.waitForTimeout(6000); // the spam guard rejects a form submitted implausibly fast
  await form.locator('[data-testid="comment-name"]').fill('fxv5 Visitor');
  await form.locator('[data-testid="comment-email"]').fill(email);
  await form.locator('[data-testid="comment-input"]').fill(body);
  const solved = await solveCaptcha(form, { wrong: opts.wrong });
  await form.locator('[data-testid="comment-submit"]').click();
  return { form, solved };
}

const publicCount = async (page: Page): Promise<number> =>
  Number(((await page.locator('[data-testid="comments-count"]').first().innerText()) || '').replace(/\D/g, '') || '0');

const dbApproved = (pid: string) => Number(sqlOne(`SELECT count(*) FROM blogcomment WHERE postid = ${pid} AND moderationstatus = 'Approved'`));

async function tokenFor(email: string, purpose: string): Promise<string> {
  let t = '';
  for (let i = 0; i < 20 && !t; i++) {
    t = sqlOne(`SELECT token FROM emailverificationtoken WHERE lower(email) = lower('${email}') AND purpose = '${purpose}' AND isused = false ORDER BY tokenid DESC LIMIT 1`);
    if (!t) await new Promise((r) => setTimeout(r, 1000));
  }
  return t;
}

/** Opens /verify/{token} and waits for the redeemed state card. */
async function redeem(page: Page, token: string) {
  await gotoPublic(page, `/verify/${token}`);
  await expect(page.locator('[data-testid="verify-confirmed"], [data-testid="verify-subscribed"], [data-testid="verify-already"], [data-testid="verify-expired"]').first())
    .toBeVisible({ timeout: 90000 });
  await page.waitForTimeout(800);
}

// -------------------------------------------------------------------------------------------
// Comments
// -------------------------------------------------------------------------------------------





test('REQ-FN-049 a challenge expires after a few minutes and a late correct answer is refused', async ({ page }) => {
  test.setTimeout(900000);
  test.skip(!postSlug(), 'fixture post missing');
  const email = fxMail('expiry');
  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);
  await form.locator('[data-testid="comment-name"]').fill('fxv5 Slow Visitor');
  await form.locator('[data-testid="comment-email"]').fill(email);
  await form.locator('[data-testid="comment-input"]').fill(`fxv5 expiry ref${NONCE}`);
  const solved = await solveCaptcha(form);
  expect(solved.correct).not.toBeNull();
  await page.waitForTimeout(5 * 60 * 1000 + 20000); // documented absolute expiry: 5 minutes
  await form.locator('[data-testid="comment-submit"]').click();
  await expect(form.locator('[data-testid="captcha-error"], [data-testid="comment-form-error"]').first()).toBeVisible({ timeout: 60000 });
  const refusal = ((await form.locator('[data-testid="captcha-error"], [data-testid="comment-form-error"]').allInnerTexts()) || []).join(' | ').replace(/\s+/g, ' ');
  console.log('FN-049 late-answer refusal text:', refusal);
  expect(refusal, 'the refusal says the challenge expired').toMatch(/expired/i);
  expect(Number(sqlOne(`SELECT count(*) FROM blogcomment WHERE lower(email) = lower('${email}')`)), 'the late answer wrote nothing').toBe(0);
});

// -------------------------------------------------------------------------------------------
// Email verification
// -------------------------------------------------------------------------------------------



// -------------------------------------------------------------------------------------------
// Moderation (FN-022)
// -------------------------------------------------------------------------------------------


// -------------------------------------------------------------------------------------------
// Ratings
// -------------------------------------------------------------------------------------------



// -------------------------------------------------------------------------------------------
// Subscribe
// -------------------------------------------------------------------------------------------

async function cardSubmit(page: Page, email: string, opts: { skipCaptcha?: boolean } = {}) {
  await gotoPublic(page, '/newsletters');
  const card = page.locator('[data-testid="newsletter-subscribe"]');
  await expect(card).toBeVisible({ timeout: 60000 });
  await waitForChallenge(card);
  await card.locator('[data-testid="newsletter-subscribe-email"]').fill(email);
  if (!opts.skipCaptcha) await solveCaptcha(card);
  await card.locator('[data-testid="newsletter-subscribe-submit"]').click();
  return card;
}




// -------------------------------------------------------------------------------------------
// Captcha on every public write surface (UI-056) and its accessible alternative (UI-057)
// -------------------------------------------------------------------------------------------

/**
 * Submits with a wrong answer and returns the inline error text. The captcha failure limiter is
 * shared by every visitor on this host (5 failures / 5 min per client) and the other verify writers
 * hit it too, so a "too many attempts" refusal is waited out and the attempt repeated: the
 * acceptance line is about a WRONG ANSWER being refused, not about the limiter.
 */
async function wrongAnswerRefused(attempt: () => Promise<Locator>, page: Page, scope: Locator): Promise<string> {
  const limiterWait = async () => {
    const t = ((await scope.locator('[data-testid="captcha-error"]').first().innerText({ timeout: 2000 }).catch(() => '')) || '').replace(/\s+/g, ' ');
    if (!/too many/i.test(t)) return false;
    const m = t.match(/wait about (\d+) (second|minute)/i);
    const secs = Number(m?.[1] ?? 60) * (/minute/i.test(m?.[2] ?? '') ? 60 : 1);
    console.log(`UI-056 limiter active (${t}); waiting ${secs + 5}s`);
    await page.waitForTimeout((secs + 5) * 1000);
    await scope.locator('[data-testid="captcha-reload"]').first().click().catch(() => {});
    await page.waitForTimeout(3000);
    return true;
  };
  for (let i = 0; i < 4; i++) {
    let err: Locator;
    try {
      err = await attempt();
    } catch (e) {
      if (await limiterWait()) continue;
      throw e;
    }
    await expect(err.first()).toBeVisible({ timeout: 60000 });
    const text = ((await err.first().innerText()) || '').replace(/\s+/g, ' ');
    if (!/too many/i.test(text)) return text;
    await limiterWait();
  }
  throw new Error('captcha limiter never cleared');
}



async function tabTo(page: Page, testid: string, dir: 'Tab' | 'Shift+Tab' = 'Tab', max = 40) {
  for (let i = 0; i < max; i++) {
    if ((await page.evaluate(() => document.activeElement?.getAttribute('data-testid'))) === testid) return true;
    await page.keyboard.press(dir);
  }
  return (await page.evaluate(() => document.activeElement?.getAttribute('data-testid'))) === testid;
}



// -------------------------------------------------------------------------------------------
// View tracking (FN-034) - the acceptance line AS WRITTEN. The shipped design de-duplicates a
// repeat inside 24 h (no new row), which contradicts "raises the total": this test is expected to
// fail on the repeat-visit clause until the owner decides which wins.
// -------------------------------------------------------------------------------------------

