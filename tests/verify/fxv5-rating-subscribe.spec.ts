/**
 * fxv5-rating-subscribe.spec.ts — verify-phase writer W5, 2026-10-27 (ratings and subscribe).
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






// -------------------------------------------------------------------------------------------
// Email verification
// -------------------------------------------------------------------------------------------



// -------------------------------------------------------------------------------------------
// Moderation (FN-022)
// -------------------------------------------------------------------------------------------


// -------------------------------------------------------------------------------------------
// Ratings
// -------------------------------------------------------------------------------------------

test('REQ-UI-027 an anonymous visitor rates; the widget shows average and count, and a score counts only once the address is confirmed', async ({ page }) => {
  test.skip(!post2Slug(), 'fixture post missing');
  const pid = post2Id();
  const path = `/post/${post2Slug()}`;
  await openPost(page, path);
  const panel = page.locator('[data-testid="post-rating-panel"]');
  await expect(panel).toBeVisible({ timeout: 60000 });
  expect(await panel.locator('[data-testid="post-rating-stars"] button[role="radio"]').count()).toBe(5);
  expect(await panel.locator('a[href*="login" i]').count(), 'no sign-in gate').toBe(0);
  const read = async () => ({
    avg: (await panel.locator('[data-testid="post-rating-average"]').innerText()).trim(),
    cnt: (await panel.locator('[data-testid="post-rating-count"]').innerText()).trim(),
  });
  const dbAgg = () => ({
    avg: sqlOne(`SELECT coalesce(round(avg(rating)::numeric,1)::text,'0') FROM postrating WHERE postid = ${pid} AND isemailverified`),
    cnt: Number(sqlOne(`SELECT count(*) FROM postrating WHERE postid = ${pid} AND isemailverified`)),
  });
  const ui0 = await read();
  console.log('UI-027 empty widget:', JSON.stringify(ui0));
  expect(ui0.cnt).toContain(String(dbAgg().cnt));
  expect(Number(ui0.avg) || 0).toBeCloseTo(Number(dbAgg().avg), 1);

  await chooseStar(page, 4);
  const step = panel.locator('[data-testid="rating-identify-step"]');
  await expect(step).toBeVisible({ timeout: 60000 });
  expect(await step.innerText()).not.toMatch(/sign in|log in|login/i);
  await waitForChallenge(step);
  await step.locator('[data-testid="rating-email"]').fill(EMAIL_R);
  await solveCaptcha(step);
  await step.locator('[data-testid="rating-submit"]').click();
  await expect(panel.locator('[data-testid="rating-form-success"]')).toBeVisible({ timeout: 60000 });

  expect(sqlOne(`SELECT rating, isemailverified FROM postrating WHERE postid = ${pid} AND lower(email) = lower('${EMAIL_R}')`)).toBe('4|f');
  expect(dbAgg().cnt, 'an unconfirmed score is not counted').toBe(0);
  await openPost(page, path);
  const ui1 = await read();
  expect(ui1.cnt, 'widget still shows the confirmed count only').toContain(String(dbAgg().cnt));
  expect(Number(ui1.avg) || 0).toBeCloseTo(Number(dbAgg().avg), 1);

  // confirm the address: the score now counts, and the landing page says it was a rating
  const token = await tokenFor(EMAIL_R, 'Rating');
  expect(token, 'a Rating token was issued').toBeTruthy();
  await redeem(page, token);
  await expect(page.locator('[data-testid="verify-confirmed"]')).toBeVisible();
  expect((await page.locator('[data-testid="verify-purpose"]').innerText()).toLowerCase(), 'states a rating was confirmed').toContain('rating');
  expect(sqlOne(`SELECT isemailverified FROM postrating WHERE postid = ${pid} AND lower(email) = lower('${EMAIL_R}')`)).toBe('t');

  await openPost(page, path);
  const ui2 = await read();
  console.log('UI-027 widget after confirm:', JSON.stringify(ui2), JSON.stringify(dbAgg()));
  expect(ui2.cnt).toContain(String(dbAgg().cnt));
  expect(dbAgg().cnt).toBe(1);
  expect(Number(ui2.avg)).toBeCloseTo(4, 1);
  await page.screenshot({ path: `${SHOTS}/ui027-confirmed-rating.png` });
});

test('REQ-FN-023 one rating row per email per post: a second score replaces the first; average and count match the database', async ({ page }) => {
  test.skip(sqlOne(`SELECT count(*) FROM postrating WHERE lower(email) = lower('${EMAIL_R}')`) !== '1', 'first rating missing');
  const pid = post2Id();
  const path = `/post/${post2Slug()}`;
  const ratingId = sqlOne(`SELECT ratingid FROM postrating WHERE postid = ${pid} AND lower(email) = lower('${EMAIL_R}')`);
  await openPost(page, path);
  const panel = page.locator('[data-testid="post-rating-panel"]');
  await chooseStar(page, 2);
  const step = panel.locator('[data-testid="rating-identify-step"]');
  await expect(step).toBeVisible({ timeout: 60000 });
  await waitForChallenge(step);
  await step.locator('[data-testid="rating-email"]').fill(EMAIL_R);
  await solveCaptcha(step);
  await step.locator('[data-testid="rating-submit"]').click();
  await expect(panel.locator('[data-testid="rating-form-success"]')).toBeVisible({ timeout: 60000 });

  expect(sqlOne(`SELECT count(*) FROM postrating WHERE postid = ${pid} AND lower(email) = lower('${EMAIL_R}')`), 'still exactly one row').toBe('1');
  expect(sqlOne(`SELECT ratingid||'|'||rating FROM postrating WHERE postid = ${pid} AND lower(email) = lower('${EMAIL_R}')`), 'same row, second score').toBe(`${ratingId}|2`);

  await openPost(page, path);
  const avgDb = sqlOne(`SELECT round(avg(rating)::numeric,1)::text FROM postrating WHERE postid = ${pid} AND isemailverified`);
  const cntDb = sqlOne(`SELECT count(*) FROM postrating WHERE postid = ${pid} AND isemailverified`);
  const avgUi = (await page.locator('[data-testid="post-rating-average"]').innerText()).trim();
  const cntUi = (await page.locator('[data-testid="post-rating-count"]').innerText()).trim();
  console.log(`FN-023 ui ${avgUi}/${cntUi} db ${avgDb}/${cntDb}`);
  expect(Number(avgUi)).toBeCloseTo(Number(avgDb), 1);
  expect(cntUi).toContain(cntDb);
  expect(Number(avgDb)).toBeCloseTo(2, 1);
});

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

test('REQ-UI-030 a valid address creates a pending subscriber with a confirmation message; an invalid one shows an inline error', async ({ page }) => {
  const card = await cardSubmit(page, EMAIL_S);
  await expect(card.locator('[data-testid="newsletter-subscribe-status"]')).toBeVisible({ timeout: 60000 });
  const msg = (await card.locator('[data-testid="newsletter-subscribe-status"]').innerText()).replace(/\s+/g, ' ');
  console.log('UI-030 valid submit message:', msg);
  expect(msg).toMatch(/check your inbox|confirm/i);
  expect(sqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${EMAIL_S}')`), 'pending subscriber').toBe('f');
  expect(Number(sqlOne(`SELECT count(*) FROM subscriber WHERE lower(email) = lower('${EMAIL_S}')`))).toBe(1);
  await page.screenshot({ path: `${SHOTS}/ui030-pending.png` });

  // invalid address -> inline error, nothing stored
  await gotoPublic(page, '/newsletters');
  const card2 = page.locator('[data-testid="newsletter-subscribe"]');
  await waitForChallenge(card2);
  const bad = `fxv5-not-an-email-${NONCE}`;
  await card2.locator('[data-testid="newsletter-subscribe-email"]').fill(bad);
  await solveCaptcha(card2);
  await card2.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await page.waitForTimeout(3000);
  const txt = (await card2.innerText()).replace(/\s+/g, ' ');
  console.log('UI-030 invalid submit card text:', txt);
  console.log('UI-030 alerts:', JSON.stringify(await card2.locator('[role="alert"], [data-testid*="status"], [data-testid*="error"]').allInnerTexts()));
  const inline = card2.locator('[role="alert"], [data-testid="newsletter-subscribe-status"], [data-testid="newsletter-subscribe-error"], [aria-invalid="true"]');
  expect(await inline.count(), 'an inline error is shown next to the form').toBeGreaterThan(0);
  const alerts = (await card2.locator('[role="alert"], [data-testid="newsletter-subscribe-status"], [data-testid="newsletter-subscribe-error"]').allInnerTexts()).join(' ');
  expect(alerts, 'the inline error says the address is not valid').toMatch(/valid|invalid/i);
  expect(alerts).not.toMatch(/check your inbox/i);
  expect(sqlOne(`SELECT count(*) FROM subscriber WHERE email = '${bad}'`)).toBe('0');
  await page.screenshot({ path: `${SHOTS}/ui030-invalid.png` });
});

test('REQ-UI-030 REQ-UI-055 a duplicate address creates no second row and is told so politely; the subscription token confirms once', async ({ page }) => {
  test.skip(!sqlOne(`SELECT 1 FROM subscriber WHERE lower(email) = lower('${EMAIL_S}')`), 'subscriber missing');
  const token = await tokenFor(EMAIL_S, 'Subscription');
  expect(token, 'a Subscription token was issued').toBeTruthy();
  await redeem(page, token);
  await expect(page.locator('[data-testid="verify-subscribed"]')).toBeVisible();
  expect((await page.locator('[data-testid="verify-purpose"]').innerText()).toLowerCase(), 'states a subscription was confirmed').toMatch(/subscri/);
  expect(sqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${EMAIL_S}')`)).toBe('t');
  await page.screenshot({ path: `${SHOTS}/ui055-subscription.png` });
  await redeem(page, token);
  expect(await page.locator('[data-testid="verify-subscribed"]').count(), 'replay does not re-confirm').toBe(0);
  await expect(page.locator('[data-testid="verify-already"], [data-testid="verify-expired"]').first()).toBeVisible();

  // duplicate of a confirmed address
  const card = await cardSubmit(page, EMAIL_S);
  await expect(card.locator('[data-testid="newsletter-subscribe-status"]')).toBeVisible({ timeout: 60000 });
  const msg = (await card.locator('[data-testid="newsletter-subscribe-status"]').innerText()).replace(/\s+/g, ' ');
  console.log('UI-030 duplicate message:', msg);
  expect(msg, 'told politely that it is already subscribed').toMatch(/already/i);
  expect(msg).not.toMatch(/error|fail|exception/i);
  expect(sqlOne(`SELECT count(*) FROM subscriber WHERE lower(email) = lower('${EMAIL_S}')`), 'no second row').toBe('1');
});

test('REQ-UI-030 the sidebar subscribe form creates a pending subscriber and refuses a bad address', async ({ page }) => {
  await gotoPublic(page, '/about');
  const sidebar = page.locator('[data-testid="sidebar-subscribe"]');
  await expect(sidebar).toBeVisible({ timeout: 60000 });
  const captcha = page.locator('[data-testid="sidebar-subscribe-captcha"]');
  await waitForChallenge(captcha);
  await sidebar.locator('[data-testid="subscribe-email"]').fill(`fxv5-bad-${NONCE}`);
  await solveCaptcha(captcha);
  await sidebar.locator('[data-testid="subscribe-submit"]').click();
  await page.waitForTimeout(3000);
  const bad = (await sidebar.locator('[data-testid="subscribe-message"]').innerText({ timeout: 2000 }).catch(() => '')).replace(/\s+/g, ' ');
  console.log('UI-030 sidebar invalid message:', bad);
  expect(bad, 'inline error for an invalid address').toMatch(/valid|invalid/i);
  expect(sqlOne(`SELECT count(*) FROM subscriber WHERE email LIKE 'fxv5-bad-%'`)).toBe('0');

  await gotoPublic(page, '/about');
  const sb2 = page.locator('[data-testid="sidebar-subscribe"]');
  const cap2 = page.locator('[data-testid="sidebar-subscribe-captcha"]');
  await waitForChallenge(cap2);
  await sb2.locator('[data-testid="subscribe-email"]').fill(EMAIL_SB);
  await solveCaptcha(cap2);
  await sb2.locator('[data-testid="subscribe-submit"]').click();
  await expect(sb2.locator('[data-testid="subscribe-message"]')).toContainText(/check your inbox|confirmation link|confirm/i, { timeout: 30000 });
  expect(sqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${EMAIL_SB}')`)).toBe('f');
});

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

