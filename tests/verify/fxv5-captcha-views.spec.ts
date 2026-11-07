/**
 * fxv5-captcha-views.spec.ts — verify-phase writer W5, 2026-10-27 (captcha surfaces, accessible alternative and view tracking).
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
test.afterAll(async ({ browser }) => { test.setTimeout(600000); await teardownFixtures(browser, { comments: true }); });

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

test('REQ-UI-056 a wrong answer blocks submission with an inline error and a fresh challenge on rating, comment and subscribe', async ({ page }) => {
  test.setTimeout(900000);
  test.skip(!postSlug(), 'fixture post missing');
  const before = () => Number(sqlOne(`SELECT (SELECT count(*) FROM blogcomment WHERE comment LIKE 'fxv5 wrong answer%') + (SELECT count(*) FROM postrating WHERE email LIKE 'fxv5-w56%') + (SELECT count(*) FROM subscriber WHERE email LIKE 'fxv5-w56%')`));
  const rows0 = before();

  // comment form
  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);
  await page.waitForTimeout(6000);
  const msgC = await wrongAnswerRefused(async () => {
    await form.locator('[data-testid="comment-name"]').fill('fxv5 w56');
    await form.locator('[data-testid="comment-email"]').fill(`fxv5-w56c-${RUN}@example.com`);
    await form.locator('[data-testid="comment-input"]').fill('fxv5 wrong answer must not post');
    await solveCaptcha(form, { wrong: true });
    await form.locator('[data-testid="comment-submit"]').click();
    return form.locator('[data-testid="captcha-error"], [data-testid="comment-form-error"]');
  }, page, form);
  console.log('UI-056 comment refusal:', msgC);
  await page.waitForTimeout(1500);
  expect(await form.locator('[data-testid="captcha-answer"]').inputValue(), 'fresh challenge: answer box cleared').toBe('');
  await expect(form.locator('[data-testid="comment-form-success"]')).toHaveCount(0);

  // rating step
  await chooseStar(page, 3);
  const step = page.locator('[data-testid="rating-identify-step"]');
  await expect(step).toBeVisible({ timeout: 60000 });
  await waitForChallenge(step);
  const msgR = await wrongAnswerRefused(async () => {
    await step.locator('[data-testid="rating-email"]').fill(`fxv5-w56r-${RUN}@example.com`);
    await solveCaptcha(step, { wrong: true });
    await step.locator('[data-testid="rating-submit"]').click();
    return step.locator('[data-testid="captcha-error"]');
  }, page, step);
  console.log('UI-056 rating refusal:', msgR);
  await page.waitForTimeout(1500);
  expect(await step.locator('[data-testid="captcha-answer"]').inputValue()).toBe('');
  await expect(page.locator('[data-testid="rating-form-success"]')).toHaveCount(0);

  // subscribe card
  await gotoPublic(page, '/newsletters');
  const card = page.locator('[data-testid="newsletter-subscribe"]');
  await waitForChallenge(card);
  const msgS = await wrongAnswerRefused(async () => {
    await card.locator('[data-testid="newsletter-subscribe-email"]').fill(`fxv5-w56-card-${RUN}@example.com`);
    await solveCaptcha(card, { wrong: true });
    await card.locator('[data-testid="newsletter-subscribe-submit"]').click();
    return card.locator('[data-testid="captcha-error"]');
  }, page, card);
  console.log('UI-056 subscribe-card refusal:', msgS);
  await page.waitForTimeout(1500);
  expect(await card.locator('[data-testid="captcha-answer"]').inputValue()).toBe('');

  // sidebar subscribe
  await gotoPublic(page, '/about');
  const sb = page.locator('[data-testid="sidebar-subscribe"]');
  const cap = page.locator('[data-testid="sidebar-subscribe-captcha"]');
  await waitForChallenge(cap);
  const msgB = await wrongAnswerRefused(async () => {
    await sb.locator('[data-testid="subscribe-email"]').fill(`fxv5-w56-sb-${RUN}@example.com`);
    await solveCaptcha(cap, { wrong: true });
    await sb.locator('[data-testid="subscribe-submit"]').click();
    return cap.locator('[data-testid="captcha-error"]');
  }, page, cap);
  console.log('UI-056 sidebar refusal:', msgB);
  expect(await sb.locator('[data-testid="subscribe-message"]').innerText({ timeout: 2000 }).catch(() => '')).not.toMatch(/check your inbox|confirmation link/i);

  expect(before(), 'a wrong answer wrote nothing on any surface').toBe(rows0);
});

test('REQ-UI-056 the captcha answer is absent from the page source; the input is labelled; reload is named; the order is keyboard-sane', async ({ page }) => {
  test.skip(!postSlug(), 'fixture post missing');
  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);
  const raw = await (await page.request.get(`${process.env.TB_BASE}${POST()}`)).text();
  expect(raw, 'server HTML carries no challenge at all (generated on the interactive render)').not.toMatch(/captcha-image"[^>]*src="data:/);

  // input has an accessible name via <label for>, reload is named
  const info = await form.locator('[data-testid="captcha-answer"]').evaluate((el) => {
    const id = el.getAttribute('id');
    const label = id ? document.querySelector(`label[for="${id}"]`) : null;
    return { labelText: (label?.textContent || '').trim(), id };
  });
  expect(info.labelText.length, 'input is labelled').toBeGreaterThan(3);
  const reloadName = (await form.locator('[data-testid="captcha-reload"]').getAttribute('aria-label')) ?? '';
  expect(reloadName.length, 'reload button has an aria-label').toBeGreaterThan(3);

  // tab order: reload -> answer -> toggle, each reachable
  await form.locator('[data-testid="comment-input"]').focus();
  const seen: string[] = [];
  for (let i = 0; i < 12 && !(seen.includes('captcha-reload') && seen.includes('captcha-answer') && seen.includes('captcha-mode-toggle')); i++) {
    await page.keyboard.press('Tab');
    seen.push(await page.evaluate(() => document.activeElement?.getAttribute('data-testid') ?? ''));
  }
  console.log('UI-056 tab path:', seen.join(' > '));
  expect(seen.indexOf('captcha-reload')).toBeGreaterThanOrEqual(0);
  expect(seen.indexOf('captcha-answer')).toBeGreaterThan(seen.indexOf('captcha-reload'));
  expect(seen.indexOf('captcha-mode-toggle')).toBeGreaterThan(seen.indexOf('captcha-answer'));

  // question mode: the answer (number or number word) is not in the widget markup
  const solved = await solveCaptcha(form);
  const html = (await form.locator('[data-testid="captcha-widget"]').evaluate((e) => (e as HTMLElement).outerHTML)).replace(solved.question, '');
  expect(html).not.toContain(`>${solved.correct}<`);
  expect(/\d/.test(solved.question), 'question prose carries no digits').toBe(false);
});

async function tabTo(page: Page, testid: string, dir: 'Tab' | 'Shift+Tab' = 'Tab', max = 40) {
  for (let i = 0; i < max; i++) {
    if ((await page.evaluate(() => document.activeElement?.getAttribute('data-testid'))) === testid) return true;
    await page.keyboard.press(dir);
  }
  return (await page.evaluate(() => document.activeElement?.getAttribute('data-testid'))) === testid;
}

test('REQ-UI-057 a keyboard-only visitor switches to the alternative challenge, answers it, hears it announced, and the comment goes through', async ({ page }) => {
  test.skip(!postSlug(), 'fixture post missing');
  const email = fxMail('kbd');
  const body = `fxv5 keyboard ref${NONCE}`;
  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);
  await page.waitForTimeout(6000);
  await form.locator('[data-testid="comment-name"]').fill('fxv5 Keyboard');
  await form.locator('[data-testid="comment-email"]').fill(email);
  await form.locator('[data-testid="comment-input"]').fill(body);

  await form.locator('[data-testid="comment-input"]').focus();
  expect(await tabTo(page, 'captcha-mode-toggle'), 'toggle reachable by Tab').toBe(true);
  const tag = await page.evaluate(() => document.activeElement?.tagName.toLowerCase());
  expect(tag).toBe('button');
  await page.keyboard.press('Enter');
  await expect(form.locator('[data-testid="captcha-image"]')).toHaveCount(0, { timeout: 30000 });
  let question = '';
  for (let i = 0; i < 20; i++) {
    question = ((await form.locator('[data-testid="captcha-prompt"]').innerText()) || '').trim();
    if (question && !/Loading/i.test(question)) break;
    await page.waitForTimeout(700);
  }
  expect(question).toMatch(/\?$/);
  const label = await form.locator('[data-testid="captcha-answer"]').evaluate((el) => document.querySelector(`label[for="${el.id}"]`)?.textContent?.trim());
  expect(label, 'the question is the input label').toBe(question);
  const status = form.locator('[data-testid="captcha-status"]').first();
  expect(await status.getAttribute('role')).toBe('status');
  expect(await status.getAttribute('aria-live')).toBe('polite');
  expect(await status.innerText(), 'live region announces the question').toContain(question);

  const answer = answerQuestion(question);
  expect(answer, `recognised question shape: ${question}`).not.toBeNull();
  expect(await tabTo(page, 'captcha-answer', 'Shift+Tab'), 'answer box reachable by Shift+Tab').toBe(true);
  await page.keyboard.type(answer!);
  expect(await form.locator('[data-testid="captcha-answer"]').inputValue()).toBe(answer);
  await form.locator('[data-testid="comment-submit"]').focus();
  await page.keyboard.press('Enter');
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 60000 });
  expect(Number(sqlOne(`SELECT count(*) FROM blogcomment WHERE comment = '${body}'`)), 'correct answer let the submission through').toBe(1);
});

test('REQ-UI-057 the alternative is offered on the subscribe form and a correct answer lets a subscription through', async ({ page }) => {
  const email = fxMail('kbdsub');
  await gotoPublic(page, '/newsletters');
  const card = page.locator('[data-testid="newsletter-subscribe"]');
  await waitForChallenge(card);
  expect(await card.locator('[data-testid="captcha-mode-toggle"]').count()).toBe(1);
  await card.locator('[data-testid="newsletter-subscribe-email"]').focus();
  expect(await tabTo(page, 'captcha-mode-toggle')).toBe(true);
  await page.keyboard.press('Enter');
  await expect(card.locator('[data-testid="captcha-image"]')).toHaveCount(0, { timeout: 30000 });
  await page.waitForTimeout(1500);
  await card.locator('[data-testid="newsletter-subscribe-email"]').fill(email);
  await solveCaptcha(card);
  await card.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await expect(card.locator('[data-testid="newsletter-subscribe-status"]')).toBeVisible({ timeout: 60000 });
  expect(sqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${email}')`)).toBe('f');
});

// -------------------------------------------------------------------------------------------
// View tracking (FN-034) - acceptance line as corrected 2026-10-28: a repeat by the same visitor
// inside 24 h is de-duplicated (no second row, both counts unchanged).
// -------------------------------------------------------------------------------------------

test('REQ-FN-034 a visit records a view row with no raw IP; a repeat by the same visitor inside 24 h records no second row', async ({ browser }) => {
  test.skip(!postSlug(), 'fixture post missing');
  const pid = postId();
  const ctx = await browser.newContext({ userAgent: `fxv5-visitor-${NONCE}` });
  const page = await ctx.newPage();
  const visit = async () => {
    await gotoPublic(page, POST());
    await expect(page.locator('[data-testid="post-title"]')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(4000);
  };
  // other writers may not touch this post, but our own earlier tests did: take a baseline
  const total = () => Number(sqlOne(`SELECT count(*) FROM postviews WHERE postid = ${pid}`));
  const unique = () => Number(sqlOne(`SELECT count(DISTINCT visitorhash) FROM postviews WHERE postid = ${pid}`));
  const t0 = total(), u0 = unique();
  await visit();
  await expect.poll(total, { timeout: 30000 }).toBeGreaterThan(t0);
  const t1 = total(), u1 = unique();
  console.log(`FN-034 after visit 1: total ${t0}->${t1} unique ${u0}->${u1}`);
  const ips = sql(`SELECT coalesce(viewerip,'') FROM postviews WHERE postid = ${pid}`);
  expect(ips.filter((x) => x.length > 0).length, 'no raw IP address is stored on any view row').toBe(0);
  expect(sql(`SELECT length(visitorhash) FROM postviews WHERE postid = ${pid} ORDER BY viewid DESC LIMIT 1`)[0]).toBe('64');

  await visit();
  await page.waitForTimeout(3000);
  const t2 = total(), u2 = unique();
  console.log(`FN-034 after repeat visit inside 24h: total ${t1}->${t2} unique ${u1}->${u2}`);
  expect(u1, 'the first visit raises the unique count').toBeGreaterThan(u0);
  expect(t2, 'a repeat inside 24 h records no second row').toBe(t1);
  expect(u2, 'and leaves the unique count unchanged').toBe(u1);
  const counts = sqlOne(`SELECT totalviews||'|'||uniqueviews FROM postviewcount WHERE postid = ${pid}`);
  console.log('FN-034 postviewcount total|unique =', counts);
  await ctx.close();
});
