/**
 * vall-resume-newsletter.spec.ts — cluster "resume-media-newsletter", part 3 of 3.
 *
 * Grades REQ-UI-043 (composer), REQ-UI-053 (public archive + subscribe), REQ-UI-054 (issue view),
 * REQ-FN-032 (compose / send / history / unsubscribe link) and REQ-FN-050 (publishing + archive
 * queries).
 *
 * `newsletter` and `subscriber` both start EMPTY, so an empty archive is NO-DATA. The whole
 * cluster is therefore built through the app's own write paths — subscribe on the public page,
 * confirm the double opt-in, compose and send two issues — and torn down in `afterAll`.
 * Everything created is tagged `VERIFY-0808-`.
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { BASE, nav, renderCheck, ControlResult } from './_gates';
import { psql, report, expectVisualClean, bothWidths, signIn, settle, waitInteractive } from './vall-resume-helpers';

const SHOTS = '.verify/shots/resume';
const SUB_EMAIL = 'verify-0808-subscriber@techieblog.test';
const TAG = 'VERIFY-0808';

test.describe.configure({ mode: 'serial' });
test.beforeAll(() => fs.mkdirSync(SHOTS, { recursive: true }));

// Seven verification agents share this host; a page can take ~10s just to go interactive.
test.beforeEach(({}, testInfo) => testInfo.setTimeout(420000));

/** Removes every newsletter, subscriber and token this file created. */
function teardown(): string {
  psql(
    `DELETE FROM subscribernewsletter WHERE newsletterid IN (SELECT newsletterid FROM newsletter WHERE title LIKE '${TAG}%')`,
  );
  psql(`DELETE FROM newsletter WHERE title LIKE '${TAG}%'`);
  psql(`DELETE FROM subscribernewsletter WHERE subscriberid IN (SELECT subscriberid FROM subscriber WHERE email='${SUB_EMAIL}')`);
  psql(`DELETE FROM emailverificationtoken WHERE email='${SUB_EMAIL}'`);
  psql(`DELETE FROM subscriber WHERE email='${SUB_EMAIL}'`);
  return `newsletter=${psql('SELECT count(*) FROM newsletter')} subscriber=${psql('SELECT count(*) FROM subscriber')} subscribernewsletter=${psql('SELECT count(*) FROM subscribernewsletter')} tokens=${psql('SELECT count(*) FROM emailverificationtoken')}`;
}

test.afterAll(() => {
  console.log('NEWSLETTER CLEANUP →', teardown());
});

/**
 * Answers the accessible captcha. The image challenge cannot be solved headlessly, so the widget's
 * own "use a question instead" affordance is taken — the same route a screen-reader user takes.
 */
async function solveCaptcha(page: Page, scope: string) {
  const widget = page.locator(scope);
  // The widget can start in either mode, so toggle until the prompt IS a question rather than
  // assuming the image challenge is the default.
  let prompt = ((await widget.locator('[data-testid="captcha-prompt"]').textContent()) ?? '').trim();
  for (let i = 0; i < 3 && !/^(what is|how many)/i.test(prompt); i++) {
    await widget.locator('[data-testid="captcha-mode-toggle"]').click();
    await page.waitForTimeout(1800);
    prompt = ((await widget.locator('[data-testid="captcha-prompt"]').textContent()) ?? '').trim();
  }
  const words = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten',
    'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen', 'twenty'];
  const toNum = (w: string) => words.indexOf(w.toLowerCase());

  let answer: number | null = null;
  let m = prompt.match(/what is (\w+) plus (\w+)/i);
  if (m) answer = toNum(m[1]) + toNum(m[2]);
  if (answer === null && (m = prompt.match(/what is (\w+) minus (\w+)/i))) answer = toNum(m[1]) - toNum(m[2]);
  if (answer === null && (m = prompt.match(/letters are in the word '([^']+)'/i))) answer = m[1].length;
  if (answer === null && (m = prompt.match(/words are in this line: '([^']+)'/i))) answer = m[1].trim().split(/\s+/).length;
  console.log(`captcha prompt = ${JSON.stringify(prompt)} → answer ${answer}`);
  expect(answer, `unrecognised captcha question: ${prompt}`).not.toBeNull();
  await widget.locator('[data-testid="captcha-answer"]').fill(String(answer));
  await page.waitForTimeout(400);
}

/** Fills the composer and dispatches one issue, returning the row the send produced. */
async function composeAndSend(page: Page, subject: string, summary: string, body: string) {
  await page.fill('[data-testid="newsletter-subject"]', subject);
  await page.fill('[data-testid="newsletter-summary"]', summary);
  const editor = page.locator('[data-testid="newsletter-body"]').locator('textarea').first();
  await editor.fill(body);
  await page.waitForTimeout(800);

  await page.click('[data-testid="newsletter-send"]');
  await expect(page.locator('[data-testid="newsletter-send-dialog"]')).toBeVisible();
  await page.click('[data-testid="newsletter-send-confirm"]');
  await expect(page.locator('[data-testid="newsletter-send-outcome"]')).toBeVisible({ timeout: 60000 });
  const outcome = ((await page.locator('[data-testid="newsletter-send-outcome"]').textContent()) ?? '')
    .replace(/\s+/g, ' ')
    .trim();
  await page.waitForTimeout(1200);
  const row = psql(
    `SELECT newsletterid||'|'||status||'|'||COALESCE(slug,'<null>')||'|'||recipientcount||'|'||ispublic FROM newsletter WHERE title='${subject}'`,
  );
  console.log(`SEND "${subject}" → outcome=${JSON.stringify(outcome)} row=${row}`);
  return { outcome, row, slug: row.split('|')[2] };
}

// ---------------------------------------------------------------------------------------------
// 1. REQ-UI-053 — public archive, empty state and the double-opt-in subscribe form
// ---------------------------------------------------------------------------------------------

test('REQ-UI-053 the public archive shows its empty state and subscribing creates a pending subscriber', async ({ page }) => {
  teardown(); // start from the documented empty baseline

  const controls: ControlResult[] = [];
  await page.goto(`${BASE}/newsletters`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="newsletter-archive"]')).toBeVisible({ timeout: 45000 });
  await settle(page);

  controls.push(await renderCheck(page, 'page title', '[data-testid="newsletter-archive-title"]'));
  controls.push(await renderCheck(page, 'intro', '[data-testid="newsletter-archive-intro"]'));
  controls.push(await renderCheck(page, 'subscribe card', '[data-testid="newsletter-subscribe"]', 'present'));
  controls.push(await renderCheck(page, 'subscribe email input', '[data-testid="newsletter-subscribe-email"]', 'present'));
  controls.push(await renderCheck(page, 'captcha', '[data-testid="newsletter-subscribe-captcha"]', 'present'));
  controls.push(await renderCheck(page, 'double opt-in note', '[data-testid="newsletter-subscribe-optin-note"]'));
  controls.push(await renderCheck(page, 'issues heading', '[data-testid="newsletter-issues-heading"]'));

  const sentIssues = Number(psql("SELECT count(*) FROM newsletter WHERE status='sent'"));
  expect(sentIssues).toBe(0);
  await expect(
    page.locator('[data-testid="newsletter-issues-empty"]'),
    'no sent issues must render TbEmpty',
  ).toHaveCount(1, { timeout: 30000 });
  const empty = await page.locator('[data-testid="newsletter-issues-empty"]').count();
  console.log(`REQ-UI-053 sent issues in db = ${sentIssues}, empty panels on page = ${empty}`);
  controls.push({ control: 'issue list (empty state)', verdict: 'RENDERS', detail: 'NO-DATA: zero sent issues → TbEmpty shown' });

  const emptyVisuals = await bothWidths(page, 'req-ui-053-archive-empty');

  // ---- subscribe (the acceptance test) ----
  // The card is an interactive-render component: click it before the handoff and the browser
  // posts the static form, so wait for Blazor to own the button first.
  await waitInteractive(page, 'newsletter-subscribe-submit');
  await page.fill('[data-testid="newsletter-subscribe-email"]', SUB_EMAIL);
  await solveCaptcha(page, '[data-testid="newsletter-subscribe-captcha"]');
  await page.click('[data-testid="newsletter-subscribe-submit"]');
  await expect(page.locator('[data-testid="newsletter-subscribe-status"]')).toBeVisible({ timeout: 30000 });
  const status = ((await page.locator('[data-testid="newsletter-subscribe-status"]').textContent()) ?? '').trim();
  console.log('REQ-UI-053 subscribe status =', JSON.stringify(status));
  await page.waitForTimeout(1000);

  const sub = psql(`SELECT subscriberid||'|'||isconfirmed FROM subscriber WHERE email='${SUB_EMAIL}'`);
  console.log('REQ-UI-053 subscriber row (id|isconfirmed) =', sub);
  expect(sub, 'subscribing must create a row').not.toBe('');
  expect(sub.split('|')[1], 'the new subscriber must be PENDING until confirmed').toBe('false');
  const token = psql(`SELECT token FROM emailverificationtoken WHERE email='${SUB_EMAIL}' AND isused=false ORDER BY tokenid DESC LIMIT 1`);
  expect(token.length, 'a confirmation token must be issued').toBeGreaterThan(10);

  // Redeem the mailed link through the app itself — that is what makes the opt-in double.
  // VerifyEmail.razor redeems the token from OnAfterRenderAsync on the INTERACTIVE render (its own
  // docs say "deliberately NOT OnInitializedAsync"), so the write lands ~10s after the navigation.
  await page.goto(`${BASE}/verify/${token}`, { waitUntil: 'domcontentloaded' });
  await settle(page);
  await expect
    .poll(() => psql(`SELECT isconfirmed FROM subscriber WHERE email='${SUB_EMAIL}'`), {
      timeout: 60000,
      message: 'redeeming the mailed link must confirm the subscriber',
    })
    .toBe('t');
  console.log(
    'REQ-UI-053 /verify landing =',
    JSON.stringify((await page.locator('body').innerText()).replace(/\s+/g, ' ').trim().slice(0, 220)),
  );
  await page.screenshot({ path: `${SHOTS}/req-ui-053-verify-landing.png` });

  report('/newsletters (empty)', controls, emptyVisuals);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  emptyVisuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// 2. REQ-UI-043 / REQ-FN-032 — compose, preview, dispatch, history
// ---------------------------------------------------------------------------------------------

test('REQ-UI-043 an admin composes, previews and dispatches a newsletter with visible outcome and history', async ({ page }) => {
  const controls: ControlResult[] = [];
  await signIn(page, 'admin');
  await nav(page, '/admin/newsletter', /Newsletter composer/i);
    await settle(page);
  await expect(page.locator('[data-testid="newsletter-compose-card"]')).toBeVisible();

  controls.push(await renderCheck(page, 'subject', '[data-testid="newsletter-subject"]', 'present'));
  controls.push(await renderCheck(page, 'summary', '[data-testid="newsletter-summary"]', 'present'));
  controls.push(await renderCheck(page, 'body editor', '[data-testid="newsletter-body"]', 'present'));
  controls.push(await renderCheck(page, 'audience radio group', '[data-testid="newsletter-audience"]', 'present'));
  controls.push(await renderCheck(page, 'recipient estimate', '[data-testid="newsletter-recipient-count"]'));
  controls.push(await renderCheck(page, 'send button', '[data-testid="newsletter-send"]', 'present'));
  controls.push(await renderCheck(page, 'status badge', '[data-testid="newsletter-status-badge"]'));

  const activeLabel = ((await page.locator('[data-testid="audience-active-label"]').textContent()) ?? '').trim();
  const everyoneLabel = ((await page.locator('[data-testid="audience-everyone-label"]').textContent()) ?? '').trim();
  console.log('REQ-UI-043 audience labels =', JSON.stringify({ activeLabel, everyoneLabel }));
  const dbActive = Number(psql('SELECT count(*) FROM subscriber WHERE isconfirmed'));
  const dbTotal = Number(psql('SELECT count(*) FROM subscriber'));
  expect(activeLabel, 'the live count must match the confirmed subscribers').toContain(`(${dbActive})`);
  expect(everyoneLabel, 'the live count must match every subscriber').toContain(`(${dbTotal})`);

  // Other verification agents are creating subscribers in this same database, so the send is
  // narrowed to a segment matching only this cluster's own address. That also exercises the
  // segment audience, which the broad modes would not.
  await page.locator('label[for="audience-segment"]').click();
  await page.fill('[data-testid="newsletter-segment-filter"]', 'verify-0808');
  await page.waitForTimeout(1200);
  const estimate = ((await page.locator('[data-testid="newsletter-recipient-count"]').textContent()) ?? '').trim();
  console.log('REQ-UI-043 segment estimate =', JSON.stringify(estimate));
  expect(estimate, 'the segment must resolve to exactly this cluster\'s subscriber').toMatch(/^1 recipient/);
  const dbActive1 = 1;

  // ---- Markdown preview renders the delivered mail, unsubscribe footer included ----
  const body = `## ${TAG} headline\n\nA paragraph for the verification run.\n\n- first bullet\n- second bullet\n\n> a quote\n`;
  await page.fill('[data-testid="newsletter-subject"]', `${TAG} Issue One`);
  await page.fill('[data-testid="newsletter-summary"]', `${TAG} teaser for the first issue`);
  await page.locator('[data-testid="newsletter-body"]').locator('textarea').first().fill(body);
  await page.waitForTimeout(800);
  await page.click('[data-testid="newsletter-tab-preview"]');
  await expect(page.locator('[data-testid="newsletter-preview"]')).toBeVisible();
  await page.waitForTimeout(600);
  const previewHtml = (await page.locator('[data-testid="newsletter-preview"]').innerHTML()) ?? '';
  expect(previewHtml, 'the preview must render Markdown, not echo it').toContain('<h2');
  expect(previewHtml).toContain('<li');
  expect(previewHtml).toContain('<blockquote');
  controls.push({ control: 'email preview', verdict: 'RENDERS', detail: 'h2 + li + blockquote present in the rendered preview' });
  controls.push(await renderCheck(page, 'unsubscribe footer in preview', '[data-testid="newsletter-preview-footer"]'));
  const footer = ((await page.locator('[data-testid="newsletter-preview-footer"]').textContent()) ?? '').trim();
  expect(footer).toMatch(/unsubscribe/i);
  await page.screenshot({ path: `${SHOTS}/req-ui-043-preview.png` });
  await page.click('[data-testid="newsletter-tab-write"]');
  await page.waitForTimeout(500);

  const visuals = await bothWidths(page, 'req-ui-043-composer');

  // ---- dispatch ----
  const first = await composeAndSend(page, `${TAG} Issue One`, `${TAG} teaser for the first issue`, body);
  expect(first.outcome).toMatch(new RegExp(`Sent to ${dbActive1} of ${dbActive1}`));
  expect(first.outcome).toMatch(/0 failed/);
  const [, status1, slug1, recipients1, isPublic1] = first.row.split('|');
  expect(status1).toBe('sent');
  expect(slug1).not.toBe('<null>');
  expect(Number(recipients1)).toBe(dbActive1);
  expect(isPublic1, 'a sent issue becomes a public record').toBe('true');

  const delivered = psql(
    `SELECT count(*) FROM subscribernewsletter sn JOIN newsletter n ON n.newsletterid=sn.newsletterid WHERE n.title='${TAG} Issue One'`,
  );
  console.log('REQ-FN-032 subscribernewsletter rows for issue one =', delivered);
  expect(Number(delivered), 'the send must log one delivery row per recipient').toBe(dbActive1);

  // ---- history + delivery log ----
  await page.waitForTimeout(1000);
  controls.push(await renderCheck(page, 'send outcome', '[data-testid="newsletter-send-outcome"]'));
  controls.push(await renderCheck(page, 'history list', '[data-testid="newsletter-history-list"]', 'present'));
  controls.push(await renderCheck(page, 'history row title', '[data-testid="history-row-title"]'));
  controls.push(await renderCheck(page, 'history row meta', '[data-testid="history-row-meta"]'));
  controls.push(await renderCheck(page, 'history row status', '[data-testid="history-row-status"]'));
  controls.push(await renderCheck(page, 'delivery list', '[data-testid="newsletter-delivery-list"]', 'present'));
  controls.push(await renderCheck(page, 'delivery row email', '[data-testid="delivery-row-email"]'));
  controls.push(await renderCheck(page, 'delivery row status', '[data-testid="delivery-row-status"]'));
  const deliveryEmail = ((await page.locator('[data-testid="delivery-row-email"]').first().textContent()) ?? '').trim();
  expect(deliveryEmail.toLowerCase()).toContain(SUB_EMAIL);
  await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: `${TAG} Issue One` })).toHaveCount(1);
  await page.screenshot({ path: `${SHOTS}/req-ui-043-after-send.png`, fullPage: false });

  // ---- a second issue so prev/next has something to resolve, plus an unsent draft ----
  await page.click('[data-testid="newsletter-new"]');
  await page.waitForTimeout(1000);
  await page.locator('label[for="audience-segment"]').click();
  await page.fill('[data-testid="newsletter-segment-filter"]', 'verify-0808');
  await page.waitForTimeout(1200);
  const second = await composeAndSend(
    page,
    `${TAG} Issue Two`,
    `${TAG} teaser for the second issue`,
    `## ${TAG} second issue\n\nThe newest issue in the archive.\n`,
  );
  expect(second.row.split('|')[1]).toBe('sent');

  await page.click('[data-testid="newsletter-new"]');
  await page.waitForTimeout(1000);
  await page.fill('[data-testid="newsletter-subject"]', `${TAG} Unsent Draft`);
  await page.fill('[data-testid="newsletter-summary"]', `${TAG} must never appear publicly`);
  await page.locator('[data-testid="newsletter-body"]').locator('textarea').first().fill('draft body');
  await page.click('[data-testid="newsletter-save-draft"]');
  await expect(page.locator('[data-testid="newsletter-status-message"]')).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(1200);
  const draft = psql(`SELECT status||'|'||COALESCE(slug,'<null>') FROM newsletter WHERE title='${TAG} Unsent Draft'`);
  console.log('REQ-UI-043 draft row =', draft);
  expect(draft.split('|')[0]).toBe('draft');
  await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: TAG })).toHaveCount(3);

  report('/admin/newsletter', controls, visuals);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  visuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// 3. REQ-FN-050 / REQ-UI-053 — only sent issues become public records
// ---------------------------------------------------------------------------------------------

test('REQ-FN-050 the public archive lists only sent issues, newest first, and the count matches the database', async ({ page }) => {
  const sent = Number(psql("SELECT count(*) FROM newsletter WHERE status='sent'"));
  const drafts = Number(psql("SELECT count(*) FROM newsletter WHERE status<>'sent'"));
  const newestTitle = psql("SELECT title FROM newsletter WHERE status='sent' ORDER BY senton DESC LIMIT 1");
  console.log(`REQ-FN-050 db: sent=${sent} drafts=${drafts} newest=${JSON.stringify(newestTitle)}`);
  expect(Number(psql(`SELECT count(*) FROM newsletter WHERE status='sent' AND title LIKE '${TAG}%'`))).toBe(2);
  expect(drafts).toBeGreaterThanOrEqual(1);

  const controls: ControlResult[] = [];
  await page.goto(`${BASE}/newsletters`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="newsletter-issues-list"]')).toBeVisible({ timeout: 45000 });
  await settle(page);

  await expect(page.locator('[data-testid="newsletter-issue-card"]')).toHaveCount(sent);
  const titles = await page.locator('[data-testid="newsletter-issue-title"]').allTextContents();
  console.log('REQ-FN-050 archive titles (top first) =', JSON.stringify(titles.map((t) => t.trim())));
  expect(titles[0].trim(), 'newest issue first').toBe(newestTitle);
  expect(titles.join(' '), 'a draft must never be listed').not.toContain('Unsent Draft');

  controls.push(await renderCheck(page, 'issue card', '[data-testid="newsletter-issue-card"]', 'present'));
  controls.push(await renderCheck(page, 'issue number', '[data-testid="newsletter-issue-number"]'));
  controls.push(await renderCheck(page, 'issue date', '[data-testid="newsletter-issue-date"]'));
  controls.push(await renderCheck(page, 'issue title', '[data-testid="newsletter-issue-title"]'));
  controls.push(await renderCheck(page, 'issue excerpt', '[data-testid="newsletter-issue-excerpt"]'));
  controls.push(await renderCheck(page, 'read link', '[data-testid="newsletter-issue-read"]', 'present'));
  controls.push(await renderCheck(page, 'subscribe card', '[data-testid="newsletter-subscribe"]', 'present'));

  const visuals = await bothWidths(page, 'req-ui-053-archive-populated');
  report('/newsletters (populated)', controls, visuals);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  visuals.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// 4. REQ-UI-054 — the public issue view, prev/next and the 404 rules
// ---------------------------------------------------------------------------------------------

test('REQ-UI-054 the issue view renders the body with prev/next by send order and 404s unknown or unsent slugs', async ({ page }) => {
  const slugOne = psql(`SELECT slug FROM newsletter WHERE title='${TAG} Issue One'`);
  const slugTwo = psql(`SELECT slug FROM newsletter WHERE title='${TAG} Issue Two'`);
  console.log('REQ-UI-054 slugs =', JSON.stringify({ slugOne, slugTwo }));

  const controls: ControlResult[] = [];
  await page.goto(`${BASE}/newsletter/${slugOne}`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="newsletter-view"]')).toBeVisible({ timeout: 45000 });
  await settle(page);

  controls.push(await renderCheck(page, 'issue number', '[data-testid="newsletter-view-number"]'));
  controls.push(await renderCheck(page, 'issue title', '[data-testid="newsletter-view-title"]'));
  controls.push(await renderCheck(page, 'sent date', '[data-testid="newsletter-view-date"]'));
  controls.push(await renderCheck(page, 'body', '[data-testid="newsletter-view-body"]'));
  controls.push(await renderCheck(page, 'position', '[data-testid="newsletter-view-position"]'));
  controls.push(await renderCheck(page, 'all issues link', '[data-testid="newsletter-view-all"]', 'present'));
  // The issue view renders the compact card under its own TestId prefix (NewsletterView.razor:106).
  controls.push(await renderCheck(page, 'compact subscribe CTA', '[data-testid="newsletter-view-subscribe"]', 'present'));
  controls.push(await renderCheck(page, 'compact CTA heading', '[data-testid="newsletter-view-subscribe-heading"]'));
  controls.push(await renderCheck(page, 'compact CTA all-issues link', '[data-testid="newsletter-view-subscribe-all-issues"]', 'present'));

  const bodyHtml = await page.locator('[data-testid="newsletter-view-body"]').innerHTML();
  expect(bodyHtml, 'the stored Markdown must be rendered').toContain('<h2');

  // Oldest issue: next exists, previous is hidden.
  await expect(page.locator('[data-testid="newsletter-view-previous"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="newsletter-view-next"]')).toHaveCount(1);
  const visualsOne = await bothWidths(page, 'req-ui-054-issue-oldest');

  // Follow next → newest issue: previous exists, next is hidden.
  await page.click('[data-testid="newsletter-view-next"]');
  await page.waitForTimeout(2500);
  await expect(page.locator('[data-testid="newsletter-view-title"]')).toHaveText(`${TAG} Issue Two`, { timeout: 30000 });
  await expect(page.locator('[data-testid="newsletter-view-next"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="newsletter-view-previous"]')).toHaveCount(1);
  console.log('REQ-UI-054 nav position on newest =', (await page.locator('[data-testid="newsletter-view-position"]').textContent())?.trim());

  // 404 rules — measured on the HTTP status, not on what the page happens to draw.
  const draftSlugRow = psql(`SELECT COALESCE(slug,'') FROM newsletter WHERE title='${TAG} Unsent Draft'`);
  const unknown = await page.request.get(`${BASE}/newsletter/verify-0808-no-such-issue`);
  console.log('REQ-UI-054 unknown slug status =', unknown.status(), '| draft slug in db =', JSON.stringify(draftSlugRow));
  expect(unknown.status()).toBe(404);
  if (draftSlugRow) {
    const draftRes = await page.request.get(`${BASE}/newsletter/${draftSlugRow}`);
    console.log('REQ-UI-054 unsent slug status =', draftRes.status());
    expect(draftRes.status()).toBe(404);
  }

  report('/newsletter/{slug}', controls, visualsOne);
  for (const c of controls) expect(c.verdict, `${c.control}: ${c.detail}`).toBe('RENDERS');
  visualsOne.forEach(expectVisualClean);
});

// ---------------------------------------------------------------------------------------------
// 5. REQ-FN-032 — the unsubscribe half of the acceptance criteria
// ---------------------------------------------------------------------------------------------

test('REQ-FN-032 every message carries an unsubscribe link and that link must remove the subscriber', async ({ page }) => {
  // The service builds {BaseUrl}/unsubscribe/{token}; the acceptance criterion is that following
  // it removes the subscriber. Probe the route the service advertises.
  const probe = await page.request.get(`${BASE}/unsubscribe/verify-0808-probe-token`, { maxRedirects: 0 });
  console.log('REQ-FN-032 GET /unsubscribe/{token} status =', probe.status());

  const before = psql(`SELECT count(*) FROM subscriber WHERE email='${SUB_EMAIL}'`);
  expect(before, 'the test subscriber must still exist before the unsubscribe attempt').toBe('1');

  await page.goto(`${BASE}/unsubscribe/verify-0808-probe-token`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(3000);
  const heading = (await page.locator('body').innerText()).slice(0, 300).replace(/\s+/g, ' ');
  console.log('REQ-FN-032 /unsubscribe page text =', JSON.stringify(heading));
  await page.screenshot({ path: `${SHOTS}/req-fn-032-unsubscribe-route.png` });

  // A working feature answers 200 with an unsubscribe confirmation; a missing one answers 404.
  expect(probe.status(), 'the advertised unsubscribe route must exist').toBe(200);
});
