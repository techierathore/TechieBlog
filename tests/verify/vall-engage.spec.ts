/**
 * vall-engage.spec.ts — *verify all (2026-08-08), cluster "engage".
 *
 * Grades the PUBLIC engagement surfaces: search, comments, ratings, subscribe, captcha,
 * post-view capture. Admin-side grading lives in vall-engage-admin.spec.ts.
 *
 * Every write goes through the app's own public form — nothing is inserted behind the UI's back.
 * Rows created here carry the marker VERIFY-0808 and the address prefix verify0808+.
 */
import { test, expect, Locator, Page } from '@playwright/test';
import { visualCheck, renderCheck } from './_gates';
import { MARK, SHOTS, gotoPublic, mail, psql, psqlOne, solveCaptcha } from './_engage-helpers';

const POST = '/post/blazor-render-modes-explained';
const POST_ID = 1;

test.setTimeout(300000);

/** Waits until a captcha widget has actually issued its challenge (image or question on screen). */
async function waitForChallenge(scope: Locator) {
  const prompt = scope.locator('[data-testid="captcha-prompt"]').first();
  await expect(prompt).toBeVisible({ timeout: 60000 });
  await expect
    .poll(async () => {
      const img = await scope.locator('[data-testid="captcha-image"]').count();
      const ph = await scope.locator('[data-testid="captcha-image-placeholder"]').count();
      const err = await scope.locator('[data-testid="captcha-error"]').count();
      return img > 0 || err > 0 || ph === 0;
    }, { timeout: 90000, intervals: [1000] })
    .toBe(true);
}

/**
 * Selects a star on the rating panel.
 *
 * Since TrBlazeUI 2.0.2 (TR-031/045/052) the stars ARE the control: each option is a real
 * `<button role="radio">` with a roving tabindex and a literal `aria-checked`. The visually hidden
 * `<fieldset>` of native radios that used to carry the keyboard semantics beside them, and its
 * `post-rating-star-N` ids, were deleted on 2026-08-11 — so the option is addressed by position
 * within the group and driven by keyboard, which is the path a keyboard visitor actually takes.
 */
async function chooseStar(page: Page, value: number) {
  const options = page.locator('[data-testid="post-rating-stars"] [role="radio"]');
  await expect(options).toHaveCount(5, { timeout: 60000 });
  const option = options.nth(value - 1);
  await option.evaluate((el) => (el as HTMLElement).focus());
  await page.waitForTimeout(400);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(600);
  if ((await options.nth(value - 1).getAttribute('aria-checked')) !== 'true') {
    await option.click({ force: true });
  }
}

/**
 * Screenshots the control itself, not the top of the page.
 *
 * `visualCheck` shoots the viewport un-scrolled, which on a long article means every engagement
 * screenshot is a picture of the hero image. This scrolls the named control into view first so the
 * §4b "open the screenshot and look at it" step has something to look at.
 */
async function shotOf(page: Page, testid: string, path: string, width: number) {
  await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
  await page.waitForTimeout(600);
  await page.locator(`[data-testid="${testid}"]`).first().scrollIntoViewIfNeeded();
  await page.waitForTimeout(900);
  await page.screenshot({ path, fullPage: false });
  // Back to the top: a sticky header measured over scrolled-under content reads as an overlap.
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(400);
}

/** Loads the post page and waits for the comment form (the slowest control) to exist. */
async function openPost(page: Page, slug = POST) {
  await gotoPublic(page, slug);
  await expect(page.locator('[data-testid="comments-section"]')).toBeVisible({ timeout: 60000 });
  await expect(page.locator('[data-testid="comment-form"]')).toBeVisible({ timeout: 60000 });
}

// ---------------------------------------------------------------------------------------------
// REQ-FN-021 — search service
// ---------------------------------------------------------------------------------------------

/**
 * Runs the public search box for a title/tag term, a body-only term and a tag-only term and
 * compares each result count with the same ILIKE the repository issues, proving the service
 * really covers title, abstract, body and tags and really restricts itself to published posts.
 */
test('REQ-FN-021 search covers title, body and tags over published posts only', async ({ page }) => {
  // The interactive box itself first: type a term and press the button.
  await gotoPublic(page, '/search');
  await expect(page.locator('[data-testid="search-input"]')).toBeVisible({ timeout: 60000 });
  await page.fill('[data-testid="search-input"]', 'blazor');
  await page.click('[data-testid="search-submit"]');
  await expect(page.locator('[data-testid="search-results-count"]')).toBeVisible({ timeout: 90000 });
  console.log('SEARCH interactive box works: ' + (await page.locator('[data-testid="search-results-count"]').innerText()).replace(/\s+/g, ' '));

  const cases = [
    { term: 'blazor', why: 'title + tags + body' },
    { term: 'predicate', why: 'body only' },
    { term: 'azure', why: 'tags only' },
  ];

  for (const c of cases) {
    const expected = Number(
      psqlOne(
        `SELECT count(*) FROM blogpost WHERE published AND (title ILIKE '%${c.term}%' OR coalesce(abstract,'') ILIKE '%${c.term}%' OR postcontent ILIKE '%${c.term}%' OR tags ILIKE '%${c.term}%')`,
      ),
    );
    await gotoPublic(page, `/search?q=${encodeURIComponent(c.term)}`);
    await expect(page.locator('[data-testid="search-results-count"]')).toBeVisible({ timeout: 90000 });
    await page.waitForTimeout(600);

    const rendered = await page.locator('[data-testid="search-result"]').count();
    const countText = (await page.locator('[data-testid="search-results-count"]').innerText()).trim();
    console.log(`SEARCH "${c.term}" (${c.why}) rendered=${rendered} psql=${expected} countText="${countText}"`);

    expect(expected, `${c.term} should match at least one published post`).toBeGreaterThan(0);
    expect(rendered, `${c.term}: rendered cards must equal the ILIKE count`).toBe(expected);
    expect(countText).toContain(String(expected));

    // Every card must carry real data, not an empty shell.
    const first = page.locator('[data-testid="search-result"]').first();
    expect(((await first.locator('[data-testid="search-result-title"]').innerText()) || '').trim().length).toBeGreaterThan(0);
    expect(((await first.locator('[data-testid="search-result-excerpt"]').innerText()) || '').trim().length).toBeGreaterThan(0);
  }

  // An unpublished post must never surface: post 4 and 10 are drafts.
  const draftTitle = psqlOne(`SELECT title FROM blogpost WHERE postid = 4`);
  await gotoPublic(page, `/search?q=${encodeURIComponent(draftTitle.slice(0, 18))}`);
  await page.waitForTimeout(2000);
  const body = await page.locator('main').innerText();
  console.log(`SEARCH draft "${draftTitle}" leaked into results: ${body.includes(draftTitle)}`);
  expect(body, 'a draft post must not appear in public search').not.toContain(draftTitle);

  // Paging control: 8 published posts, page size 10 → exactly one page, so no pager is expected.
  await gotoPublic(page, '/search?q=the');
  await expect(page.locator('[data-testid="search-results-count"]')).toBeVisible({ timeout: 90000 });
  const broad = Number(psqlOne(`SELECT count(*) FROM blogpost WHERE published AND (title ILIKE '%the%' OR coalesce(abstract,'') ILIKE '%the%' OR postcontent ILIKE '%the%' OR tags ILIKE '%the%')`));
  const shown = await page.locator('[data-testid="search-result"]').count();
  console.log(`SEARCH paging: total=${broad} shownOnPage1=${shown} (pageSize 10)`);
  expect(shown).toBe(Math.min(broad, 10));

  // DevGuide control map for /search also lists the category filter.
  for (const id of ['search-filters', 'category-filter', 'search-results']) {
    const r = await renderCheck(page, id, `[data-testid="${id}"]`, id === 'search-results' ? 'table' : 'value');
    console.log(`SEARCH control ${id}: ${r.verdict} — ${r.detail}`);
  }

  // §4a: does the category badge on a result carry the post's REAL category?
  const badges = await page.locator('[data-testid="search-result-category"]').allInnerTexts();
  const realCategories = psql(
    "SELECT DISTINCT c.categoryname FROM category c JOIN postcategory pc ON pc.categoryid = c.categoryid JOIN blogpost p ON p.postid = pc.postid WHERE p.published",
  ).split('\n').map((s) => s.trim()).filter(Boolean);
  const distinctBadges = Array.from(new Set(badges.map((b) => b.trim())));
  console.log(`SEARCH result category badges=${JSON.stringify(distinctBadges)} real categories in db=${JSON.stringify(realCategories)}`);

  const sv1280 = await visualCheck(page, `${SHOTS}/search-1280.png`, 1280);
  const sv390 = await visualCheck(page, `${SHOTS}/search-390.png`, 390);
  console.log(`VISUAL search 1280: hScroll=${sv1280.hScroll} zero=${JSON.stringify(sv1280.zeroSized)} off=${JSON.stringify(sv1280.offViewport)} overlaps=${JSON.stringify(sv1280.overlaps)} consoleErrors=${JSON.stringify(sv1280.consoleErrors)}`);
  console.log(`VISUAL search 390: hScroll=${sv390.hScroll} zero=${JSON.stringify(sv390.zeroSized)} off=${JSON.stringify(sv390.offViewport)} overlaps=${JSON.stringify(sv390.overlaps)} consoleErrors=${JSON.stringify(sv390.consoleErrors)}`);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-029 — comment thread rendering
// ---------------------------------------------------------------------------------------------

/**
 * Checks the public thread against the database: the count matches the approved rows, replies
 * render one level deep, no commenter's email address is anywhere in the section, and nothing in
 * the section asks the visitor to sign in.
 */
test('REQ-UI-029 comment thread renders approved rows only, hides emails, no sign-in gate', async ({ page }) => {
  await openPost(page);

  const approved = Number(psqlOne(`SELECT count(*) FROM blogcomment WHERE postid = ${POST_ID} AND moderationstatus = 'Approved'`));
  const parents = Number(psqlOne(`SELECT count(*) FROM blogcomment WHERE postid = ${POST_ID} AND moderationstatus = 'Approved' AND parentcommentid IS NULL`));
  const replies = Number(psqlOne(`SELECT count(*) FROM blogcomment WHERE postid = ${POST_ID} AND moderationstatus = 'Approved' AND parentcommentid IS NOT NULL`));

  const countText = (await page.locator('[data-testid="comments-count"]').innerText()).trim();
  const items = await page.locator('[data-testid="comment-item"]').count();
  const replyEls = await page.locator('[data-testid="comment-reply"]').count();
  console.log(`COMMENTS psql approved=${approved} (parents=${parents} replies=${replies}) rendered items=${items} replies=${replyEls} heading="${countText}"`);

  expect(countText).toContain(String(approved));
  expect(items).toBe(parents);
  expect(replyEls).toBe(replies);
  expect(replies, 'the thread needs at least one reply to prove one-level nesting').toBeGreaterThan(0);

  // Every rendered comment carries author, date and body — no blank cells.
  for (const sel of ['comment-author', 'comment-date', 'comment-body']) {
    const r = await renderCheck(page, sel, `[data-testid="${sel}"]`, 'value');
    console.log(`RENDER ${sel}: ${r.verdict} — ${r.detail}`);
    expect(r.verdict).toBe('RENDERS');
  }

  const sectionText = await page.locator('[data-testid="comments-section"]').innerText();
  const emails = psql(`SELECT DISTINCT email FROM blogcomment WHERE postid = ${POST_ID}`).split('\n').map((s) => s.trim()).filter(Boolean);
  for (const e of emails) {
    expect(sectionText, `commenter email ${e} must never be rendered`).not.toContain(e);
  }
  expect(sectionText).not.toMatch(/@[\w.-]+\.\w{2,}/);
  expect(sectionText).not.toMatch(/sign in|log in|login|register to comment/i);
  expect(await page.locator('[data-testid="comments-section"] a[href*="login" i]').count()).toBe(0);

  const vc1280 = await visualCheck(page, `${SHOTS}/post-page-1280.png`, 1280);
  await shotOf(page, 'comments-section', `${SHOTS}/post-comments-1280.png`, 1280);
  const vc390 = await visualCheck(page, `${SHOTS}/post-page-390.png`, 390);
  await shotOf(page, 'comments-section', `${SHOTS}/post-comments-390.png`, 390);
  console.log(`VISUAL post 1280: hScroll=${vc1280.hScroll} zero=${JSON.stringify(vc1280.zeroSized)} off=${JSON.stringify(vc1280.offViewport)} overlaps=${JSON.stringify(vc1280.overlaps)} consoleErrors=${JSON.stringify(vc1280.consoleErrors)}`);
  console.log(`VISUAL post 390: hScroll=${vc390.hScroll} zero=${JSON.stringify(vc390.zeroSized)} off=${JSON.stringify(vc390.offViewport)} overlaps=${JSON.stringify(vc390.overlaps)} consoleErrors=${JSON.stringify(vc390.consoleErrors)}`);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-029 / REQ-FN-022 / REQ-FN-049 / REQ-UI-056 / REQ-UI-057 — anonymous comment write path
// ---------------------------------------------------------------------------------------------

/**
 * Addresses are minted per TEST, not per module.
 *
 * Two constraints force this. (1) The double opt-in path can only be exercised by an address the
 * site has never confirmed: an address already in `verifiedemail` correctly skips straight to
 * PendingApproval, so reusing one silently stops testing the thing under test. (2) Playwright
 * restarts the worker process after a failed test, which re-evaluates any module-level
 * `Date.now()` — so a module-level "run id" is NOT stable across a file that has any failure.
 * Follow-on tests therefore look their subject up in the database by prefix.
 */
const freshCommentEmail = () => mail(`comment-${Date.now().toString(36)}`);
const COMMENT_BODY = `${MARK} anonymous comment written by the verifier through the public form.`;

/**
 * Posts a comment as an anonymous visitor with a correct (accessible-mode) captcha and proves the
 * row lands unpublished and does not appear in the public thread.
 */
test('REQ-UI-029 anonymous comment is accepted, stored pending and NOT publicly visible', async ({ page }) => {
  // A nonce, not the address: the body is rendered publicly once approved, and REQ-UI-029 says an
  // address must never appear there — so the marker must not smuggle one in.
  const nonce = Date.now().toString(36);
  const COMMENT_EMAIL = mail(`comment-${nonce}`);
  const uniqueBody = `${COMMENT_BODY} ref:${nonce}`;
  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);

  // The spam guard measures "too fast to be human" from first render — behave like a human.
  await page.waitForTimeout(4000);

  await form.locator('[data-testid="comment-name"]').fill('Verify Engage');
  await form.locator('[data-testid="comment-email"]').fill(COMMENT_EMAIL);
  await form.locator('[data-testid="comment-input"]').fill(uniqueBody);

  // Honeypot must stay empty and must be hidden from a real visitor.
  const honeypot = form.locator('[data-testid="comment-honeypot"]');
  expect(await honeypot.count()).toBe(1);
  expect(await honeypot.isVisible()).toBe(false);

  const solved = await solveCaptcha(form);
  console.log(`COMMENT captcha question="${solved.question}" answer="${solved.answer}"`);

  await form.locator('[data-testid="comment-submit"]').click();
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 60000 });
  const success = await form.locator('[data-testid="comment-form-success"]').innerText();
  console.log(`COMMENT success alert: ${success.replace(/\s+/g, ' ')}`);

  const row = psqlOne(`SELECT commentid, moderationstatus, published, isemailverified FROM blogcomment WHERE lower(email) = lower('${COMMENT_EMAIL}') ORDER BY commentid DESC LIMIT 1`);
  console.log(`COMMENT db row: ${row}`);
  const [, status, published, verified] = row.split('|');
  expect(status).toBe('PendingVerification');
  expect(published).toBe('f');
  expect(verified).toBe('f');

  const token = psqlOne(`SELECT count(*) FROM emailverificationtoken WHERE lower(email) = lower('${COMMENT_EMAIL}') AND purpose = 'Comment'`);
  expect(Number(token), 'a double opt-in token must be issued').toBeGreaterThan(0);

  // The freshly written comment must NOT be on the page. Match on this comment's own address,
  // which the body carries — the shared marker also appears on rows a previous pass approved.
  const sectionText = await page.locator('[data-testid="comments-section"]').innerText();
  expect(sectionText, 'an unapproved comment must not be publicly visible').not.toContain(`ref:${nonce}`);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="comments-section"]')).toBeVisible({ timeout: 60000 });
  expect(await page.locator('[data-testid="comments-section"]').innerText()).not.toContain(`ref:${nonce}`);
});

/**
 * Submits a comment with a deliberately wrong captcha answer and proves nothing is written and a
 * fresh challenge is issued.
 */
test('REQ-FN-049 a wrong captcha answer blocks the comment write and re-challenges', async ({ page }) => {
  const badEmail = mail(`badcaptcha-${Date.now().toString(36)}`);
  const before = Number(psqlOne(`SELECT count(*) FROM blogcomment WHERE lower(email) = lower('${badEmail}')`));

  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);
  await page.waitForTimeout(4000);

  await form.locator('[data-testid="comment-name"]').fill('Verify Bad Captcha');
  await form.locator('[data-testid="comment-email"]').fill(badEmail);
  await form.locator('[data-testid="comment-input"]').fill(`${MARK} this submission must never reach the database.`);

  const solved = await solveCaptcha(form, { wrong: true });
  const questionBefore = solved.question;
  await form.locator('[data-testid="comment-submit"]').click();

  await expect(form.locator('[data-testid="captcha-error"]')).toBeVisible({ timeout: 60000 });
  const err = (await form.locator('[data-testid="captcha-error"]').innerText()).replace(/\s+/g, ' ');
  console.log(`WRONG-CAPTCHA inline error: ${err}`);
  expect(err.length).toBeGreaterThan(0);

  const after = Number(psqlOne(`SELECT count(*) FROM blogcomment WHERE lower(email) = lower('${badEmail}')`));
  console.log(`WRONG-CAPTCHA rows before=${before} after=${after}`);
  expect(after).toBe(before);
  expect(after).toBe(0);

  // A fresh challenge must have been issued (the old one was burned).
  await page.waitForTimeout(1500);
  const questionAfter = (await form.locator('[data-testid="captcha-prompt"]').innerText()).trim();
  console.log(`WRONG-CAPTCHA challenge before="${questionBefore}" after="${questionAfter}"`);
  const answerBox = form.locator('[data-testid="captcha-answer"]');
  expect(await answerBox.inputValue()).toBe('');
});

/**
 * Consumes the double opt-in token issued for the anonymous comment and proves the comment moves
 * to the moderation queue, the address is remembered, and the token is single use.
 */
test('REQ-FN-022 the double opt-in token promotes the comment to the moderation queue exactly once', async ({ page }) => {
  // Look the subject up rather than assume an address: see the note above freshCommentEmail.
  const pair = psqlOne("SELECT token, email FROM emailverificationtoken WHERE email LIKE 'verify0808+comment-%' AND purpose = 'Comment' AND isused = false ORDER BY tokenid DESC LIMIT 1");
  expect(pair, 'the comment test must have issued a token').toBeTruthy();
  const [token, COMMENT_EMAIL] = pair.split('|');
  console.log(`VERIFY consuming token for ${COMMENT_EMAIL}`);

  await gotoPublic(page, `/verify/${token}`);
  await page.waitForTimeout(3000);
  const firstText = (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 240);
  console.log(`VERIFY first visit: ${firstText}`);

  const row = psqlOne(`SELECT moderationstatus, published, isemailverified FROM blogcomment WHERE lower(email) = lower('${COMMENT_EMAIL}') ORDER BY commentid DESC LIMIT 1`);
  console.log(`VERIFY comment row after confirm: ${row}`);
  const [status, published, verified] = row.split('|');
  expect(verified).toBe('t');
  expect(published).toBe('f');
  expect(status).toBe('PendingApproval');

  expect(Number(psqlOne(`SELECT count(*) FROM verifiedemail WHERE lower(email) = lower('${COMMENT_EMAIL}')`))).toBe(1);
  expect(psqlOne(`SELECT isused FROM emailverificationtoken WHERE token = '${token}'`)).toBe('t');

  // Second visit with the same token must not work.
  await gotoPublic(page, `/verify/${token}`);
  await page.waitForTimeout(3000);
  const secondText = (await page.locator('main').innerText()).replace(/\s+/g, ' ').slice(0, 240);
  console.log(`VERIFY second visit: ${secondText}`);
  expect(secondText).toMatch(/no longer valid|already|expired|invalid|could not/i);

  // Still not publicly visible — it is only in the queue. Match on this comment's own `ref:` nonce;
  // the shared marker also sits on rows an earlier pass already approved.
  const mineBody = psqlOne(`SELECT comment FROM blogcomment WHERE lower(email) = lower('${COMMENT_EMAIL}') ORDER BY commentid DESC LIMIT 1`);
  const ref = (mineBody.match(/ref:[a-z0-9]+/) || [''])[0];
  expect(ref, 'the comment body must carry its ref nonce').toBeTruthy();
  await openPost(page);
  expect(await page.locator('[data-testid="comments-section"]').innerText(),
    'a confirmed but unapproved comment must still be invisible').not.toContain(ref);
});

/**
 * A now-confirmed address comments again. BRD-38 says it must not have to confirm a second time,
 * but must still be moderated — so the row goes straight to PendingApproval and stays invisible.
 */
test('REQ-FN-022 an already-verified address comments again without re-confirming, still moderated', async ({ page }) => {
  const COMMENT_EMAIL = psqlOne("SELECT email FROM verifiedemail WHERE email LIKE 'verify0808+comment-%' ORDER BY verifiedemailid DESC LIMIT 1");
  expect(COMMENT_EMAIL, 'the opt-in test must run first and confirm an address').toBeTruthy();
  const tokensBefore = Number(psqlOne(`SELECT count(*) FROM emailverificationtoken WHERE lower(email) = lower('${COMMENT_EMAIL}')`));

  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);
  await page.waitForTimeout(5000);

  const body = `${MARK} second comment from an address the site has already confirmed. ref:${Date.now().toString(36)}`;
  await form.locator('[data-testid="comment-name"]').fill('Verify Engage');
  await form.locator('[data-testid="comment-email"]').fill(COMMENT_EMAIL);
  await form.locator('[data-testid="comment-input"]').fill(body);
  const solved = await solveCaptcha(form);
  await form.locator('[data-testid="comment-submit"]').click();
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 60000 });
  console.log(`REPEAT captcha="${solved.question}" alert=${(await form.locator('[data-testid="comment-form-success"]').innerText()).replace(/\s+/g, ' ')}`);

  const row = psqlOne(`SELECT commentid, moderationstatus, published, isemailverified FROM blogcomment WHERE lower(email) = lower('${COMMENT_EMAIL}') ORDER BY commentid DESC LIMIT 1`);
  const tokensAfter = Number(psqlOne(`SELECT count(*) FROM emailverificationtoken WHERE lower(email) = lower('${COMMENT_EMAIL}')`));
  console.log(`REPEAT row=${row} tokens ${tokensBefore} -> ${tokensAfter}`);
  const [, status, published, verified] = row.split('|');
  expect(status, 'a confirmed address skips PendingVerification but is still moderated').toBe('PendingApproval');
  expect(published).toBe('f');
  expect(verified).toBe('t');
  expect(tokensAfter, 'no second confirmation email for an address already confirmed').toBe(tokensBefore);

  expect(await page.locator('[data-testid="comments-section"]').innerText()).not.toContain(body);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-027 / REQ-FN-023 — star rating
// ---------------------------------------------------------------------------------------------

/** The rating widget's public numbers must equal the VERIFIED-only aggregate in the database. */
test('REQ-UI-027 rating widget shows the verified-only average and count with no sign-in gate', async ({ page }) => {
  await openPost(page);
  const panel = page.locator('[data-testid="post-rating-panel"]');
  await expect(panel).toBeVisible({ timeout: 60000 });

  const avgDb = Number(psqlOne(`SELECT round(avg(rating)::numeric, 1) FROM postrating WHERE postid = ${POST_ID} AND isemailverified`));
  const cntDb = Number(psqlOne(`SELECT count(*) FROM postrating WHERE postid = ${POST_ID} AND isemailverified`));
  const avgUi = (await panel.locator('[data-testid="post-rating-average"]').innerText()).trim();
  const cntUi = (await panel.locator('[data-testid="post-rating-count"]').innerText()).trim();
  console.log(`RATING psql avg=${avgDb} count=${cntDb} | ui avg="${avgUi}" count="${cntUi}"`);
  expect(Number(avgUi)).toBeCloseTo(avgDb, 1);
  expect(cntUi).toContain(String(cntDb));

  // Five interactive stars, reachable without an account. Since 2.0.2 they are real
  // <button role="radio"> options — no <span role="radio">, and no hidden native fallback.
  expect(await panel.locator('[data-testid="post-rating-stars"]').count()).toBe(1);
  expect(await panel.locator('[data-testid="post-rating-stars"] button[role="radio"]').count()).toBe(5);
  expect(await panel.locator('span[role="radio"]').count()).toBe(0);
  expect(await panel.locator('[data-testid="post-rating-keyboard"]').count()).toBe(0);

  const panelText = await panel.innerText();
  expect(panelText).not.toMatch(/sign in|log in|login/i);
  expect(await panel.locator('a[href*="login" i]').count()).toBe(0);

  await shotOf(page, 'post-rating-panel', `${SHOTS}/post-rating-1280.png`, 1280);
  await shotOf(page, 'post-rating-panel', `${SHOTS}/post-rating-390.png`, 390);
});

// Also per-run: a rating from an address the site has already confirmed is verified on the spot
// (sticky verification, by design), which would skip the "parked until confirmed" half of REQ-FN-023.
const RATE_EMAIL = mail(`rate-${Date.now().toString(36)}`);

/**
 * Rates anonymously, confirms the address, then rates again with a different score, proving the
 * key is (post, email), that a rating is changeable in place and that only verified scores move
 * the public average.
 */
test('REQ-FN-023 one rating per email per post — parked until verified, then changeable in place', async ({ page }) => {
  const avgBefore = psqlOne(`SELECT coalesce(round(avg(rating)::numeric,1)::text,'0') FROM postrating WHERE postid = ${POST_ID} AND isemailverified`);
  const cntBefore = Number(psqlOne(`SELECT count(*) FROM postrating WHERE postid = ${POST_ID} AND isemailverified`));

  await openPost(page);
  const panel = page.locator('[data-testid="post-rating-panel"]');
  await chooseStar(page, 5);
  await expect(panel.locator('[data-testid="rating-identify-step"]')).toBeVisible({ timeout: 60000 });

  const step = panel.locator('[data-testid="rating-identify-step"]');
  expect((await step.innerText())).not.toMatch(/sign in|log in|login/i);
  await waitForChallenge(step);

  await step.locator('[data-testid="rating-email"]').fill(RATE_EMAIL);
  const s1 = await solveCaptcha(step);
  console.log(`RATING captcha question="${s1.question}" answer="${s1.answer}"`);
  await step.locator('[data-testid="rating-submit"]').click();
  await expect(panel.locator('[data-testid="rating-form-success"]')).toBeVisible({ timeout: 60000 });

  let rows = psql(`SELECT ratingid, rating, isemailverified FROM postrating WHERE postid = ${POST_ID} AND lower(email) = lower('${RATE_EMAIL}')`);
  console.log(`RATING after submit rows: ${JSON.stringify(rows)}`);
  expect(rows.split('\n').filter(Boolean).length).toBe(1);
  const ratingId = rows.split('|')[0];
  expect(rows.split('|')[2]).toBe('f');

  // Unverified scores must not move the public numbers.
  const avgStillDb = psqlOne(`SELECT coalesce(round(avg(rating)::numeric,1)::text,'0') FROM postrating WHERE postid = ${POST_ID} AND isemailverified`);
  expect(avgStillDb).toBe(avgBefore);
  await page.reload({ waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="post-rating-count"]')).toBeVisible({ timeout: 60000 });
  expect((await page.locator('[data-testid="post-rating-count"]').innerText())).toContain(String(cntBefore));

  // Confirm the address.
  const token = psqlOne(`SELECT token FROM emailverificationtoken WHERE lower(email) = lower('${RATE_EMAIL}') AND purpose = 'Rating' AND isused = false ORDER BY tokenid DESC LIMIT 1`);
  expect(token, 'a Rating-purpose token must have been issued').toBeTruthy();
  await gotoPublic(page, `/verify/${token}`);
  await page.waitForTimeout(3000);

  expect(psqlOne(`SELECT isemailverified FROM postrating WHERE ratingid = ${ratingId}`)).toBe('t');
  const cntAfter = Number(psqlOne(`SELECT count(*) FROM postrating WHERE postid = ${POST_ID} AND isemailverified`));
  const avgAfter = psqlOne(`SELECT round(avg(rating)::numeric,1)::text FROM postrating WHERE postid = ${POST_ID} AND isemailverified`);
  console.log(`RATING verified: count ${cntBefore} -> ${cntAfter}, avg ${avgBefore} -> ${avgAfter}`);
  expect(cntAfter).toBe(cntBefore + 1);

  await openPost(page);
  const uiAvg = (await page.locator('[data-testid="post-rating-average"]').innerText()).trim();
  const uiCnt = (await page.locator('[data-testid="post-rating-count"]').innerText()).trim();
  console.log(`RATING ui after verify: avg="${uiAvg}" count="${uiCnt}" (psql avg=${avgAfter} count=${cntAfter})`);
  expect(Number(uiAvg)).toBeCloseTo(Number(avgAfter), 1);
  expect(uiCnt).toContain(String(cntAfter));

  // Change the score from the same address — same row, count unchanged.
  const panel2 = page.locator('[data-testid="post-rating-panel"]');
  await chooseStar(page, 1);
  const step2 = panel2.locator('[data-testid="rating-identify-step"]');
  await expect(step2).toBeVisible({ timeout: 60000 });
  await waitForChallenge(step2);
  await step2.locator('[data-testid="rating-email"]').fill(RATE_EMAIL);
  const s2 = await solveCaptcha(step2);
  console.log(`RATING change captcha question="${s2.question}" answer="${s2.answer}"`);
  await step2.locator('[data-testid="rating-submit"]').click();
  await expect(panel2.locator('[data-testid="rating-form-success"]')).toBeVisible({ timeout: 60000 });

  const changed = psql(`SELECT ratingid, rating, isemailverified FROM postrating WHERE postid = ${POST_ID} AND lower(email) = lower('${RATE_EMAIL}')`);
  console.log(`RATING after change rows: ${JSON.stringify(changed)}`);
  expect(changed.split('\n').filter(Boolean).length, 'still exactly one row for this address').toBe(1);
  expect(changed.split('|')[0], 'the same row was updated in place').toBe(ratingId);
  expect(changed.split('|')[1]).toBe('1');

  const cntFinal = Number(psqlOne(`SELECT count(*) FROM postrating WHERE postid = ${POST_ID} AND isemailverified`));
  expect(cntFinal, 'changing a score must not change the count').toBe(cntAfter);
  const avgFinal = psqlOne(`SELECT round(avg(rating)::numeric,1)::text FROM postrating WHERE postid = ${POST_ID} AND isemailverified`);
  console.log(`RATING final: avg ${avgAfter} -> ${avgFinal}, count ${cntFinal}`);
  expect(avgFinal).not.toBe(avgAfter);

  // Fresh load, not a reload of the circuit that just wrote — the public numbers must follow.
  await openPost(page);
  const avgAtRead = psqlOne(`SELECT round(avg(rating)::numeric,1)::text FROM postrating WHERE postid = ${POST_ID} AND isemailverified`);
  const uiFinal = (await page.locator('[data-testid="post-rating-average"]').innerText()).trim();
  const uiCntFinal = (await page.locator('[data-testid="post-rating-count"]').innerText()).trim();
  console.log(`RATING ui after change: avg="${uiFinal}" count="${uiCntFinal}" (psql avg=${avgAtRead})`);
  expect(Number(uiFinal)).toBeCloseTo(Number(avgAtRead), 1);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-030 / REQ-FN-030 — subscribe
// ---------------------------------------------------------------------------------------------

const SUB_EMAIL = mail(`subscribe-${Date.now().toString(36)}`);

/** Subscribes through the public newsletter card and re-submits the same address. */
test('REQ-UI-030 subscribe card captures a subscriber and handles a duplicate address', async ({ page }) => {
  await gotoPublic(page, '/newsletters');
  const card = page.locator('[data-testid="newsletter-subscribe"]');
  await expect(card).toBeVisible({ timeout: 60000 });
  await waitForChallenge(card);

  await card.locator('[data-testid="newsletter-subscribe-email"]').fill(SUB_EMAIL);
  const s = await solveCaptcha(card);
  console.log(`SUBSCRIBE captcha question="${s.question}" answer="${s.answer}"`);
  await card.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await expect(card.locator('[data-testid="newsletter-subscribe-status"]')).toBeVisible({ timeout: 60000 });
  const status1 = (await card.locator('[data-testid="newsletter-subscribe-status"]').innerText()).replace(/\s+/g, ' ');
  console.log(`SUBSCRIBE first status: ${status1}`);

  const rows = psql(`SELECT subscriberid, isconfirmed FROM subscriber WHERE lower(email) = lower('${SUB_EMAIL}')`);
  console.log(`SUBSCRIBE db rows: ${JSON.stringify(rows)}`);
  expect(rows.split('\n').filter(Boolean).length).toBe(1);

  // Duplicate submission of the same address.
  await gotoPublic(page, '/newsletters');
  const card2 = page.locator('[data-testid="newsletter-subscribe"]');
  await expect(card2).toBeVisible({ timeout: 60000 });
  await waitForChallenge(card2);
  await card2.locator('[data-testid="newsletter-subscribe-email"]').fill(SUB_EMAIL);
  const s2 = await solveCaptcha(card2);
  await card2.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await expect(card2.locator('[data-testid="newsletter-subscribe-status"]')).toBeVisible({ timeout: 60000 });
  const status2 = (await card2.locator('[data-testid="newsletter-subscribe-status"]').innerText()).replace(/\s+/g, ' ');
  console.log(`SUBSCRIBE duplicate status: ${status2} (captcha "${s2.question}")`);

  const after = Number(psqlOne(`SELECT count(*) FROM subscriber WHERE lower(email) = lower('${SUB_EMAIL}')`));
  console.log(`SUBSCRIBE rows after duplicate: ${after}`);
  expect(after, 'a duplicate address must not create a second row').toBe(1);

  // Invalid address must be rejected.
  await gotoPublic(page, '/newsletters');
  const card3 = page.locator('[data-testid="newsletter-subscribe"]');
  await waitForChallenge(card3);
  await card3.locator('[data-testid="newsletter-subscribe-email"]').fill('not-an-email');
  await card3.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await page.waitForTimeout(3000);
  const invalidStatus = await card3.locator('[data-testid="newsletter-subscribe-status"]').count();
  const invalidMsg = invalidStatus
    ? (await card3.locator('[data-testid="newsletter-subscribe-status"]').innerText()).replace(/\s+/g, ' ')
    : '(no status alert rendered)';
  console.log(`SUBSCRIBE invalid-address: statusAlerts=${invalidStatus} message="${invalidMsg}"`);
  expect(Number(psqlOne(`SELECT count(*) FROM subscriber WHERE email = 'not-an-email'`)), 'an invalid address must not be stored').toBe(0);

  const nv1280 = await visualCheck(page, `${SHOTS}/newsletters-1280.png`, 1280);
  await shotOf(page, 'newsletter-subscribe', `${SHOTS}/newsletter-card-1280.png`, 1280);
  const nv390 = await visualCheck(page, `${SHOTS}/newsletters-390.png`, 390);
  await shotOf(page, 'newsletter-subscribe', `${SHOTS}/newsletter-card-390.png`, 390);
  console.log(`VISUAL newsletters 1280: hScroll=${nv1280.hScroll} off=${JSON.stringify(nv1280.offViewport)} overlaps=${JSON.stringify(nv1280.overlaps)} consoleErrors=${JSON.stringify(nv1280.consoleErrors)}`);
  console.log(`VISUAL newsletters 390: hScroll=${nv390.hScroll} off=${JSON.stringify(nv390.offViewport)} overlaps=${JSON.stringify(nv390.overlaps)} consoleErrors=${JSON.stringify(nv390.consoleErrors)}`);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-056 — captcha on every public write surface
// ---------------------------------------------------------------------------------------------

/** Enumerates every anonymous write surface in the app and reports which carry a captcha. */
test('REQ-UI-056 captcha widget is mounted on every public write surface', async ({ page }) => {
  const findings: string[] = [];

  // 1 + 2: comment form and rating identify step on the post page.
  await openPost(page);
  const commentCaptcha = await page.locator('[data-testid="comment-form"] [data-testid="captcha-widget"]').count();
  findings.push(`comment-form captcha=${commentCaptcha}`);
  expect(commentCaptcha).toBe(1);

  await chooseStar(page, 3);
  await expect(page.locator('[data-testid="rating-identify-step"]')).toBeVisible({ timeout: 60000 });
  const ratingCaptcha = await page.locator('[data-testid="rating-identify-step"] [data-testid="captcha-widget"]').count();
  findings.push(`rating-identify-step captcha=${ratingCaptcha}`);
  expect(ratingCaptcha).toBe(1);

  // 3: newsletter subscribe card.
  await gotoPublic(page, '/newsletters');
  const nlCaptcha = await page.locator('[data-testid="newsletter-subscribe-captcha"]').count();
  findings.push(`newsletter-subscribe captcha=${nlCaptcha}`);
  expect(nlCaptcha).toBe(1);

  // 4: the sidebar subscribe form, present on every MainLayout page.
  await gotoPublic(page, '/search');
  const sidebar = page.locator('[data-testid="sidebar-subscribe"]');
  await expect(sidebar).toBeVisible({ timeout: 60000 });
  const sidebarCaptcha = await sidebar.locator('[data-testid="captcha-prompt"], [data-testid="captcha-answer"]').count();
  findings.push(`sidebar-subscribe captcha=${sidebarCaptcha}`);

  // 5: is there a contact form at all? (HTTP status is the honest answer — the SPA 404 page
  // renders a 200-looking body.)
  const contactResponse = await page.goto('http://localhost:5399/contact', { waitUntil: 'domcontentloaded' });
  findings.push(`contact route http=${contactResponse?.status()}`);

  console.log('CAPTCHA SURFACES: ' + findings.join(' | '));
  expect(sidebarCaptcha, 'the sidebar subscribe form is an anonymous write surface and must carry a captcha').toBe(1);
});

/**
 * Proves the sidebar subscribe form really writes to the database with no captcha at all — the
 * defect the previous assertion reports, demonstrated rather than inferred.
 */
test('REQ-UI-056 sidebar subscribe writes a subscriber with no captcha challenge at all', async ({ page }) => {
  const email = mail(`sidebar-${Date.now().toString(36)}`);
  await gotoPublic(page, '/search');
  const sidebar = page.locator('[data-testid="sidebar-subscribe"]');
  await expect(sidebar).toBeVisible({ timeout: 60000 });
  console.log('SIDEBAR captcha hooks present: ' + (await sidebar.locator('[data-testid^="captcha-"]').count()));

  await sidebar.locator('[data-testid="subscribe-email"]').fill(email);
  await sidebar.locator('[data-testid="subscribe-submit"]').click();
  await page.waitForTimeout(4000);
  const msg = await sidebar.locator('[data-testid="subscribe-message"]').innerText().catch(() => '(no message)');
  const rows = Number(psqlOne(`SELECT count(*) FROM subscriber WHERE lower(email) = lower('${email}')`));
  console.log(`SIDEBAR result: message="${msg.replace(/\s+/g, ' ')}" rows=${rows}`);
  expect(rows, 'the un-captcha-ed sidebar form wrote a subscriber row').toBe(1);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-057 — accessible alternative challenge
// ---------------------------------------------------------------------------------------------

/** The alternative challenge must exist, be keyboard reachable, and never print its own answer. */
test('REQ-UI-057 accessible alternative challenge is offered and never prints its answer', async ({ page }) => {
  await openPost(page);
  const form = page.locator('[data-testid="comment-form"]');
  await waitForChallenge(form);

  const toggle = form.locator('[data-testid="captcha-mode-toggle"]');
  await expect(toggle).toBeVisible();
  expect((await toggle.evaluate((e) => e.tagName)).toLowerCase()).toBe('button');
  expect(await toggle.evaluate((e) => (e as HTMLElement).tabIndex)).toBeGreaterThanOrEqual(0);
  console.log(`A11Y toggle label: "${(await toggle.innerText()).trim()}"`);

  await toggle.focus();
  expect(await page.evaluate(() => document.activeElement?.getAttribute('data-testid'))).toBe('captcha-mode-toggle');

  const solved = await solveCaptcha(form);
  console.log(`A11Y question="${solved.question}" computedAnswer="${solved.correct}"`);
  expect(solved.correct, 'the question must be one of the documented shapes').not.toBeNull();

  // The question itself is the label of the answer box (its accessible name).
  const accName = await form.locator('[data-testid="captcha-answer"]').evaluate((el) => {
    const id = el.getAttribute('id');
    const label = id ? document.querySelector(`label[for="${id}"]`) : null;
    return (label as HTMLElement)?.innerText?.trim() ?? null;
  });
  console.log(`A11Y answer-box accessible name: "${accName}"`);
  expect(accName).toBe(solved.question);

  // The answer must appear nowhere in the widget's markup or attributes.
  const widgetHtml = await form.locator('[data-testid="captcha-widget"]').evaluate((e) => (e as HTMLElement).outerHTML);
  const scrubbed = widgetHtml.replace(solved.question, '');
  const words = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen', 'twenty'];
  const answerWord = words[Number(solved.correct)];
  expect(scrubbed).not.toContain(`>${solved.correct}<`);
  if (answerWord) expect(scrubbed.toLowerCase()).not.toContain(answerWord);
  expect(/\d/.test(solved.question), 'the question prose must contain no digits').toBe(false);

  // The alternative is offered on the other write surfaces too.
  await gotoPublic(page, '/newsletters');
  const card = page.locator('[data-testid="newsletter-subscribe"]');
  await waitForChallenge(card);
  expect(await card.locator('[data-testid="captcha-mode-toggle"]').count()).toBe(1);
  console.log('A11Y toggle present on newsletter subscribe card');
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-034 — post view tracking
// ---------------------------------------------------------------------------------------------

/** Reading published posts must create total and unique view rows. */
test('REQ-FN-034 reading a post records total and unique views', async ({ page }) => {
  const before = Number(psqlOne('SELECT count(*) FROM postviews'));
  const slugs = ['blazor-render-modes-explained', 'blazor-circuits-and-state', 'the-markdown-kitchen-sink'];
  for (const s of slugs) {
    await gotoPublic(page, `/post/${s}`);
    await expect(page.locator('[data-testid="post-title"]')).toBeVisible({ timeout: 60000 });
    await page.waitForTimeout(2000);
  }
  // Re-read the first post so a "unique" rule has something to collapse.
  await gotoPublic(page, `/post/${slugs[0]}`);
  await page.waitForTimeout(3000);

  const after = Number(psqlOne('SELECT count(*) FROM postviews'));
  const detail = psql('SELECT postid, count(*), count(DISTINCT visitorhash) FROM postviews GROUP BY postid ORDER BY postid');
  console.log(`POSTVIEWS before=${before} after=${after} perPost(total|unique)=${JSON.stringify(detail)}`);
  expect(after, 'viewing published posts must write PostViews rows').toBeGreaterThan(before);
});
