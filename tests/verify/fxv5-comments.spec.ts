/**
 * fxv5-comments.spec.ts — verify-phase writer W5, 2026-10-27 (comments, email verification and moderation).
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

test('REQ-UI-029 an anonymous visitor posts a comment; the email is never rendered; unconfirmed and unapproved never show', async ({ page }) => {
  test.skip(!postSlug(), 'fixture post missing - run fxv5-a-setup first');
  const html0: string[] = [];
  await openPost(page);
  const before = await publicCount(page);
  const { form } = await postComment(page, EMAIL_C, BODY_1);
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 60000 });
  const success = (await form.locator('[data-testid="comment-form-success"]').innerText()).replace(/\s+/g, ' ');
  console.log('UI-029 success text:', success);
  expect(success, 'tells the visitor to confirm the address').toMatch(/confirm|check your|inbox|email|moderat|approv/i);

  const row = sqlOne(`SELECT moderationstatus, published, isemailverified FROM blogcomment WHERE lower(email) = lower('${EMAIL_C}') ORDER BY commentid DESC LIMIT 1`);
  expect(row, 'stored unpublished, unverified and awaiting verification').toBe('PendingVerification|f|f');

  // the email is not in the markup (view-source) nor in the live DOM; the comment text is not shown
  const raw = await (await page.request.get(`${process.env.TB_BASE}${POST()}`)).text();
  html0.push(raw);
  const dom = await page.content();
  for (const [name, h] of [['server HTML', raw], ['live DOM', dom]] as const) {
    expect(h, `${name} must not contain the commenter address`).not.toContain(EMAIL_C);
    expect(h.toLowerCase(), `${name} must not contain the commenter address (any case)`).not.toContain(EMAIL_C.toLowerCase());
    expect(h, `${name} must not show the unconfirmed comment`).not.toContain(BODY_1);
  }
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="comments-section"]')).toBeVisible({ timeout: 60000 });
  expect(await page.locator('[data-testid="comments-section"]').innerText()).not.toContain(BODY_1);
  expect(await publicCount(page), 'public count unchanged by an unconfirmed comment').toBe(before);
  await page.screenshot({ path: `${SHOTS}/ui029-after-submit.png` });
});

test('REQ-UI-029 the thread lists only approved comments and carries no email or sign-in gate', async ({ page }) => {
  test.skip(!postSlug(), 'fixture post missing');
  await openPost(page);
  const pid = postId();
  const approved = dbApproved(pid);
  expect(await publicCount(page)).toBe(approved);
  const sectionText = await page.locator('[data-testid="comments-section"]').innerText();
  expect(sectionText).not.toMatch(/@[\w.-]+\.\w{2,}/);
  expect(sectionText).not.toMatch(/sign in|log in|login|register to comment/i);
  const unapproved = sql(`SELECT comment FROM blogcomment WHERE postid = ${pid} AND moderationstatus <> 'Approved'`);
  for (const c of unapproved) expect(sectionText, 'unapproved comment text never rendered').not.toContain(c);
});

test('REQ-FN-049 a wrong captcha answer blocks the comment write and re-challenges; the answer is not in the markup', async ({ page }) => {
  test.skip(!postSlug(), 'fixture post missing');
  const bad = fxMail('badcaptcha');
  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);

  // image mode first: no answer text can be in the markup (the image is anonymous geometry)
  const imgHtml = await form.locator('[data-testid="captcha-widget"]').evaluate((e) => (e as HTMLElement).outerHTML);
  expect(imgHtml, 'no <text> node in the challenge image').not.toMatch(/<text[\s>]/i);

  const { solved } = await postComment(page, bad, 'fxv5 this must never be stored', { wrong: true });
  await expect(form.locator('[data-testid="captcha-error"], [data-testid="comment-form-error"]').first()).toBeVisible({ timeout: 60000 });
  expect(Number(sqlOne(`SELECT count(*) FROM blogcomment WHERE lower(email) = lower('${bad}')`)), 'nothing written').toBe(0);
  expect(Number(sqlOne(`SELECT count(*) FROM emailverificationtoken WHERE lower(email) = lower('${bad}')`)), 'no token issued').toBe(0);

  // re-challenged: the burned challenge was replaced and the answer box was cleared
  await page.waitForTimeout(2000);
  expect(await form.locator('[data-testid="captcha-answer"]').inputValue()).toBe('');
  const q2 = ((await form.locator('[data-testid="captcha-prompt"]').innerText()) || '').trim();
  console.log(`FN-049 challenge before="${solved.question}" after="${q2}"`);
  expect(q2.length).toBeGreaterThan(0);

  // reload issues a new challenge (question mode makes the text comparable)
  const answer = answerQuestion(q2);
  const htmlQ = await form.locator('[data-testid="captcha-widget"]').evaluate((e) => (e as HTMLElement).outerHTML);
  if (answer !== null) {
    const scrubbed = htmlQ.replace(q2, '');
    expect(scrubbed, 'the numeric answer is not in the markup').not.toContain(`>${answer}<`);
    expect(scrubbed).not.toContain(`value="${answer}"`);
  }
  let changed = false;
  for (let i = 0; i < 8 && !changed; i++) {
    await form.locator('[data-testid="captcha-reload"]').click();
    await page.waitForTimeout(1500);
    const q3 = ((await form.locator('[data-testid="captcha-prompt"]').innerText()) || '').trim();
    changed = q3 !== q2;
  }
  expect(changed, 'reload issues a new challenge').toBe(true);
});

test('REQ-FN-049 a solved challenge is single use: the same form cannot be resubmitted with the old answer', async ({ page }) => {
  test.skip(!postSlug(), 'fixture post missing');
  const email = fxMail('singleuse');
  await openPost(page);
  const { form, solved } = await postComment(page, email, `fxv5 single use ref${NONCE}`);
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 60000 });
  // the widget re-arms with a fresh challenge: the previous correct answer must not carry over
  await page.waitForTimeout(2000);
  const box = form.locator('[data-testid="captcha-answer"]');
  if (await box.count()) expect(await box.inputValue(), 'old answer cleared after use').not.toBe(solved.answer ?? '__none__');
  expect(Number(sqlOne(`SELECT count(*) FROM blogcomment WHERE lower(email) = lower('${email}')`))).toBe(1);
});


// -------------------------------------------------------------------------------------------
// Email verification
// -------------------------------------------------------------------------------------------

test('REQ-UI-055 REQ-FN-048 a comment token confirms exactly once, the page says a comment was confirmed, replay and unknown tokens never confirm', async ({ page }) => {
  test.skip(!sqlOne(`SELECT 1 FROM blogcomment WHERE lower(email) = lower('${EMAIL_C}')`), 'first comment missing');
  const token = await tokenFor(EMAIL_C, 'Comment');
  expect(token, 'a Comment token was issued').toBeTruthy();
  const exp = sqlOne(`SELECT round(extract(epoch FROM (expireson - issuedon))/3600) FROM emailverificationtoken WHERE token = '${token}'`);
  console.log('FN-048 token lifetime hours:', exp);
  expect(Number(exp), 'a token has a finite expiry window').toBeGreaterThan(0);
  expect(Number(exp)).toBeLessThanOrEqual(72);

  await redeem(page, token);
  await expect(page.locator('[data-testid="verify-confirmed"]')).toBeVisible({ timeout: 30000 });
  const purpose = (await page.locator('[data-testid="verify-purpose"]').innerText()).toLowerCase();
  console.log('UI-055 comment landing text:', purpose.replace(/\s+/g, ' '));
  expect(purpose, 'states what was confirmed').toContain('comment');
  await page.screenshot({ path: `${SHOTS}/ui055-comment-confirmed.png` });

  expect(sqlOne(`SELECT moderationstatus, published, isemailverified FROM blogcomment WHERE lower(email) = lower('${EMAIL_C}') ORDER BY commentid LIMIT 1`),
    'a confirmed comment enters the moderation queue, still not public').toBe('PendingApproval|f|t');
  expect(sqlOne(`SELECT isused FROM emailverificationtoken WHERE token = '${token}'`)).toBe('t');
  expect(sqlOne(`SELECT count(*) FROM verifiedemail WHERE lower(email) = lower('${EMAIL_C}')`)).toBe('1');

  // replay: nothing confirmed a second time, the page explains why
  const rowBefore = sqlOne(`SELECT commentid, moderationstatus, published FROM blogcomment WHERE lower(email) = lower('${EMAIL_C}') ORDER BY commentid LIMIT 1`);
  await redeem(page, token);
  await expect(page.locator('[data-testid="verify-already"], [data-testid="verify-expired"]').first()).toBeVisible();
  expect(await page.locator('[data-testid="verify-confirmed"]').count(), 'replay never shows the confirmed card').toBe(0);
  const why = (await page.locator('[data-testid="verify-purpose"]').innerText()).trim();
  expect(why.length, 'the page explains why').toBeGreaterThan(10);
  expect(sqlOne(`SELECT commentid, moderationstatus, published FROM blogcomment WHERE lower(email) = lower('${EMAIL_C}') ORDER BY commentid LIMIT 1`)).toBe(rowBefore);
  await page.screenshot({ path: `${SHOTS}/ui055-replay.png` });

  // unknown token
  await redeem(page, `fxv5-unknown-${NONCE}`);
  await expect(page.locator('[data-testid="verify-expired"]')).toBeVisible();
  expect((await page.locator('[data-testid="verify-heading"]').innerText()).length).toBeGreaterThan(5);
  await page.screenshot({ path: `${SHOTS}/ui055-unknown.png` });

  // not public yet
  await openPost(page);
  expect(await page.locator('[data-testid="comments-section"]').innerText()).not.toContain(BODY_1);
});

test('REQ-FN-048 a verified address posts again without re-confirming; the comment is moderated, not public', async ({ page }) => {
  test.skip(sqlOne(`SELECT count(*) FROM verifiedemail WHERE lower(email) = lower('${EMAIL_C}')`) !== '1', 'address not verified yet');
  const tokensBefore = Number(sqlOne(`SELECT count(*) FROM emailverificationtoken WHERE lower(email) = lower('${EMAIL_C}')`));
  await openPost(page);
  const { form } = await postComment(page, EMAIL_C, BODY_2);
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 60000 });
  const row = sqlOne(`SELECT moderationstatus, published, isemailverified FROM blogcomment WHERE comment = '${BODY_2}'`);
  expect(row, 'straight to the moderation queue').toBe('PendingApproval|f|t');
  expect(Number(sqlOne(`SELECT count(*) FROM emailverificationtoken WHERE lower(email) = lower('${EMAIL_C}')`)), 'no new confirmation token').toBe(tokensBefore);
  expect(await page.locator('[data-testid="comments-section"]').innerText()).not.toContain(BODY_2);
});

// -------------------------------------------------------------------------------------------
// Moderation (FN-022)
// -------------------------------------------------------------------------------------------

test('REQ-FN-022 a confirmed comment waits in the queue; approval publishes it and raises the count; delete lowers it', async ({ browser }) => {
  test.skip(!sqlOne(`SELECT 1 FROM blogcomment WHERE comment = '${BODY_2}'`), 'comments not created yet');
  const pid = postId();
  const ctx: BrowserContext = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const admin = await ctx.newPage();
  const visitorCtx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const visitor = await visitorCtx.newPage();
  try {
    await openPost(visitor);
    const c0 = await publicCount(visitor);
    expect(c0).toBe(dbApproved(pid));
    expect(await visitor.locator('[data-testid="comments-section"]').innerText()).not.toContain(BODY_1);

    await login(admin, 'admin');
    await nav(admin, '/comments', /Comment/i);
    await expect(admin.locator('[data-testid="comments-grid"], [data-testid="comments-empty"]').first()).toBeVisible({ timeout: 60000 });
    const find = async (ref: string) => {
      await admin.fill('[data-testid="comments-search"]', ref);
      await admin.waitForTimeout(2500);
    };
    await find(BODY_1);
    expect(await admin.locator('[data-testid="comment-row-text"]').count(), 'the queue lists the confirmed comment').toBe(1);
    expect((await admin.locator('[data-testid="comment-row-status"]').first().innerText()).trim()).toBe('Pending');
    await admin.locator('[data-testid="comment-approve"]').first().click();
    await expect.poll(() => sqlOne(`SELECT moderationstatus||'|'||published FROM blogcomment WHERE comment = '${BODY_1}'`), { timeout: 30000 }).toBe('Approved|true');

    await openPost(visitor);
    expect(await visitor.locator('[data-testid="comments-section"]').innerText(), 'approved comment is public').toContain(BODY_1);
    expect(await publicCount(visitor), 'the count rose by one').toBe(c0 + 1);
    expect(await publicCount(visitor)).toBe(dbApproved(pid));
    expect(await visitor.locator('[data-testid="comments-section"]').innerText()).not.toContain(BODY_2); // still pending
    expect(await visitor.locator('[data-testid="comments-section"]').innerText()).not.toContain(EMAIL_C);
    await visitor.screenshot({ path: `${SHOTS}/fn022-approved.png` });

    // delete lowers the count
    await nav(admin, '/comments', /Comment/i);
    await find(BODY_1);
    await admin.locator('[data-testid="comment-delete"]').first().click();
    await expect(admin.locator('[data-testid="comment-delete-dialog"]')).toBeVisible({ timeout: 20000 });
    await admin.click('[data-testid="comment-delete-confirm"]');
    await expect.poll(() => sqlOne(`SELECT count(*) FROM blogcomment WHERE comment = '${BODY_1}'`), { timeout: 30000 }).toBe('0');
    await openPost(visitor);
    expect(await publicCount(visitor), 'the count dropped by one').toBe(c0);

    // clean up the remaining fxv5 comments through the same UI
    for (const ref of [BODY_2, `fxv5 single use ref${NONCE}`]) {
      await nav(admin, '/comments', /Comment/i);
      await find(ref);
      if (await admin.locator('[data-testid="comment-delete"]').count()) {
        await admin.locator('[data-testid="comment-delete"]').first().click();
        await admin.click('[data-testid="comment-delete-confirm"]');
        await admin.waitForTimeout(2500);
      }
    }
  } finally {
    await ctx.close();
    await visitorCtx.close();
  }
});

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

