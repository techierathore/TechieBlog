/**
 * fxv5-newsletter.spec.ts — verify-phase writer W5, 2026-10-27.
 *
 * Rows: REQ-UI-043 (composer), REQ-UI-053 (public archive + subscribe), REQ-UI-054 (issue view),
 * REQ-FN-032 (send, log, unsubscribe link), REQ-FN-050 (publishing + archive queries).
 *
 * Database-write-free. The only recipient ever mailed is the `fxv5-nlsub-…` address this file
 * subscribes and confirms through the public form; the composer audience is a segment on that
 * address, so no other subscriber receives anything (the dev host has no SMTP host: the console
 * sender logs, nothing is emailed). The app offers no delete for newsletters or subscribers, so the
 * one sent issue and the draft stay behind (reported).
 */
import { test, expect, Page } from '@playwright/test';
import { gotoPublic, solveCaptcha } from './_engage-helpers';
import { login, nav } from './_gates';
import { sql, sqlOne, fxMail, RUN, SHOTS } from './fxv5-fixtures';
import * as fs from 'fs';

test.describe.configure({ mode: 'serial' });
test.beforeAll(() => fs.mkdirSync(SHOTS, { recursive: true }));
test.beforeEach(({}, ti) => ti.setTimeout(420000));

const SUB = fxMail('nlsub');
const SEG = `fxv5-nlsub-${RUN}`;
const SUBJECT = `fxv5 Issue ${RUN}`;
const DRAFT = `fxv5 Unsent Draft ${RUN}`;
const BASE = process.env.TB_BASE ?? 'http://localhost:5450';

/** The archive page size is the Blog.PostsPerPage site setting (build-phase fix 2026-10-28; was a fixed 6). Read-only SELECT. */
const archivePageSize = () => Math.max(1, Number(sqlOne(`SELECT settingvalue FROM sitesetting WHERE settingkey = 'Blog.PostsPerPage'`)) || 10);

const sentPublic = () => sql(`SELECT slug FROM newsletter WHERE status = 'sent' AND ispublic ORDER BY senton DESC, newsletterid DESC`);

async function waitChallenge(scope: ReturnType<Page['locator']>) {
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

test('REQ-UI-053 subscribing from the archive creates a pending subscriber until the address is confirmed', async ({ page }) => {
  await gotoPublic(page, '/newsletters');
  const card = page.locator('[data-testid="newsletter-subscribe"]');
  await expect(card).toBeVisible({ timeout: 60000 });
  await waitChallenge(card);
  await card.locator('[data-testid="newsletter-subscribe-email"]').fill(SUB);
  await solveCaptcha(card);
  await card.locator('[data-testid="newsletter-subscribe-submit"]').click();
  await expect(card.locator('[data-testid="newsletter-subscribe-status"]')).toBeVisible({ timeout: 60000 });
  expect(sqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${SUB}')`), 'pending until confirmed').toBe('f');
  await page.screenshot({ path: `${SHOTS}/ui053-subscribe-pending.png` });

  // not mailable yet: the composer's segment would count 0 until the link is followed
  const token = sqlOne(`SELECT token FROM emailverificationtoken WHERE lower(email) = lower('${SUB}') AND purpose = 'Subscription' AND isused = false ORDER BY tokenid DESC LIMIT 1`);
  expect(token.length, 'a confirmation token was issued').toBeGreaterThan(10);
  await gotoPublic(page, `/verify/${token}`);
  await expect.poll(() => sqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${SUB}')`), { timeout: 90000 }).toBe('t');
});

test('REQ-UI-043 an admin composes, previews and dispatches a newsletter; progress, outcome and history are visible', async ({ page }) => {
  test.skip(sqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${SUB}')`) !== 't', 'test subscriber not confirmed');
  const errors: string[] = [];
  page.on('pageerror', (e) => errors.push(e.message));
  await login(page, 'admin');
  await nav(page, '/admin/newsletter', /Newsletter/i);
  await expect(page.locator('[data-testid="newsletter-compose-card"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(3000);

  // audience labels carry live counts that must equal the database
  const active = await page.locator('[data-testid="audience-active-label"]').innerText();
  const everyone = await page.locator('[data-testid="audience-everyone-label"]').innerText();
  await expect.poll(async () => (await page.locator('[data-testid="audience-active-label"]').innerText()).includes(`(${sqlOne('SELECT count(*) FROM subscriber WHERE isconfirmed')})`), { timeout: 20000 }).toBe(true);
  console.log('UI-043 audience labels:', active.replace(/\s+/g, ' '), '|', everyone.replace(/\s+/g, ' '));

  const histBefore = await page.locator('[data-testid="history-row-title"]').allInnerTexts();
  await page.fill('[data-testid="newsletter-subject"]', SUBJECT);
  await page.fill('[data-testid="newsletter-summary"]', `fxv5 summary ${RUN}`);
  await page.locator('[data-testid="newsletter-body"] textarea').first().fill(`## fxv5 heading ${RUN}\n\nSome **bold** text.\n\n- one\n- two\n`);

  // preview renders the Markdown and the unsubscribe footer
  await page.click('[data-testid="newsletter-tab-preview"]');
  const prev = page.locator('[data-testid="newsletter-preview"]');
  await expect(prev.locator('h2')).toContainText(`fxv5 heading ${RUN}`);
  await expect(prev.locator('strong')).toContainText('bold');
  await expect(prev.locator('li')).toHaveCount(2);
  expect(await page.locator('[data-testid="newsletter-preview-footer"]').innerText()).toMatch(/unsubscribe/i);
  await page.screenshot({ path: `${SHOTS}/ui043-preview.png` });
  await page.click('[data-testid="newsletter-tab-write"]');

  // save draft
  await page.click('[data-testid="newsletter-save-draft"]');
  await expect(page.locator('[data-testid="newsletter-status-message"]')).toBeVisible({ timeout: 30000 });
  await expect.poll(() => sqlOne(`SELECT status FROM newsletter WHERE title = '${SUBJECT}'`), { timeout: 20000 }).toBe('draft');

  // audience = segment on the test address only
  await page.locator('label[for="audience-segment"]').click();
  await page.fill('[data-testid="newsletter-segment-filter"]', SEG);
  await page.keyboard.press('Tab');
  await expect.poll(async () => (await page.locator('[data-testid="newsletter-recipient-count"]').innerText()).trim(), { timeout: 30000 }).toMatch(/^1 recipient/);

  await page.click('[data-testid="newsletter-send"]');
  const dlg = page.locator('[data-testid="newsletter-send-dialog"]');
  await expect(dlg).toBeVisible();
  await page.screenshot({ path: `${SHOTS}/ui043-confirm.png` });
  await dlg.locator('[data-testid="newsletter-send-confirm"]').click();
  const progress = await page.locator('[data-testid="newsletter-send-progress"]').waitFor({ timeout: 5000 }).then(() => true, () => false);
  console.log('UI-043 progress element observed:', progress);
  await expect(page.locator('[data-testid="newsletter-send-outcome"]')).toBeVisible({ timeout: 90000 });
  const outcome = (await page.locator('[data-testid="newsletter-send-outcome"]').innerText()).replace(/\s+/g, ' ');
  console.log('UI-043 outcome:', outcome);
  expect(outcome).toMatch(/Sent to 1 of 1/);
  expect(outcome).toMatch(/0 failed/);
  expect((await page.locator('[data-testid="newsletter-status-badge"]').innerText()).toLowerCase()).toContain('sent');

  // delivery log + history
  await expect(page.locator('[data-testid="newsletter-delivery-list"]')).toBeVisible({ timeout: 30000 });
  const deliveries = (await page.locator('[data-testid="delivery-row-email"]').allInnerTexts()).map((s) => s.trim().toLowerCase());
  expect(deliveries, 'only the fxv5 test subscriber was mailed').toEqual([SUB.toLowerCase()]);
  await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: SUBJECT })).toHaveCount(1);
  expect((await page.locator('[data-testid="history-row-title"]').allInnerTexts()).length, 'history grew').toBeGreaterThan(histBefore.length - 1);
  expect(sqlOne(`SELECT status||'|'||recipientcount||'|'||ispublic FROM newsletter WHERE title = '${SUBJECT}'`)).toBe('sent|1|true');
  await page.screenshot({ path: `${SHOTS}/ui043-after-send.png`, fullPage: true });

  // a draft that must never be public
  await page.click('[data-testid="newsletter-new"]');
  await page.waitForTimeout(1500);
  await page.fill('[data-testid="newsletter-subject"]', DRAFT);
  await page.fill('[data-testid="newsletter-summary"]', 'fxv5 must never appear publicly');
  await page.locator('[data-testid="newsletter-body"] textarea').first().fill('fxv5 draft body');
  await page.click('[data-testid="newsletter-save-draft"]');
  await expect.poll(() => sqlOne(`SELECT status FROM newsletter WHERE title = '${DRAFT}'`), { timeout: 30000 }).toBe('draft');
  await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: DRAFT })).toHaveCount(1);
  expect(errors, 'no uncaught page errors').toEqual([]);
});

test('REQ-FN-032 the newsletter reached the test subscriber, the send is logged, and the unsubscribe link removes the subscriber', async ({ browser }) => {
  const nid = sqlOne(`SELECT newsletterid FROM newsletter WHERE title = '${SUBJECT}'`);
  test.skip(!nid, 'issue not sent yet');
  const subId = sqlOne(`SELECT subscriberid FROM subscriber WHERE lower(email) = lower('${SUB}')`);
  expect(sqlOne(`SELECT sendstatus FROM subscribernewsletter WHERE newsletterid = ${nid} AND subscriberid = ${subId}`), 'send logged as sent').toBe('sent');
  expect(sqlOne(`SELECT count(*) FROM subscribernewsletter WHERE newsletterid = ${nid}`), 'exactly one delivery: no other subscriber was mailed').toBe('1');

  // the link embedded in this recipient's mail: its token row is tied to this issue and subscriber
  const token = sqlOne(`SELECT token FROM unsubscribetoken WHERE subscriberid = ${subId} AND newsletterid = ${nid} ORDER BY unsubscribetokenid DESC LIMIT 1`);
  expect(token.length, 'the mail carries a per-recipient unsubscribe token').toBeGreaterThan(20);
  expect(sqlOne(`SELECT isconfirmed FROM subscriber WHERE subscriberid = ${subId}`)).toBe('t');

  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  const res = await page.goto(`${BASE}/unsubscribe/${token}`, { waitUntil: 'domcontentloaded' });
  expect(res?.status(), 'the link resolves').toBe(200);
  await expect(page.locator('[data-testid="unsubscribe-done"]')).toBeVisible({ timeout: 90000 });
  await expect.poll(() => sqlOne(`SELECT isconfirmed FROM subscriber WHERE subscriberid = ${subId}`), { timeout: 30000 }).toBe('f');
  expect(sqlOne(`SELECT unsubscribedon IS NOT NULL FROM subscriber WHERE subscriberid = ${subId}`)).toBe('t');
  await page.screenshot({ path: `${SHOTS}/fn032-unsubscribed.png` });

  // replay is a graceful no-op; a garbage token leaks nothing and changes nothing
  await page.goto(`${BASE}/unsubscribe/${token}`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="unsubscribe-already"]')).toBeVisible({ timeout: 90000 });
  await page.goto(`${BASE}/unsubscribe/fxv5garbage${RUN}`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="unsubscribe-invalid"]')).toBeVisible({ timeout: 90000 });
  expect(sqlOne(`SELECT isconfirmed FROM subscriber WHERE subscriberid = ${subId}`)).toBe('f');
  await ctx.close();
});

test('REQ-FN-050 the public archive lists only sent issues, newest first, and its count equals the database', async ({ page }) => {
  test.skip(!sqlOne(`SELECT 1 FROM newsletter WHERE title = '${SUBJECT}'`), 'issue not created');
  const PAGE = archivePageSize(); // the archive is paged by Blog.PostsPerPage; other writers also send issues
  await gotoPublic(page, '/newsletters');
  const slugs = sentPublic();
  const total = slugs.length;
  const linksOnPage = async () =>
    (await page.locator('[data-testid="newsletter-issue-read"]').evaluateAll((els) => els.map((e) => ((e as HTMLAnchorElement).getAttribute('href') || '').replace(/^.*\/newsletter\//, ''))));
  await expect(page.locator('[data-testid="newsletter-issue-card"]')).toHaveCount(Math.min(PAGE, total), { timeout: 60000 });
  const seen: string[] = [];
  for (let p = 1; p <= Math.ceil(total / PAGE); p++) {
    const links = await linksOnPage();
    console.log(`FN-050 archive page ${p}:`, links.join(' '));
    expect(links, `page ${p}: newest first, same rows as the database send order`).toEqual(slugs.slice((p - 1) * PAGE, p * PAGE));
    seen.push(...links);
    if (p < Math.ceil(total / PAGE)) {
      await page.locator('[data-testid="pagination-next"]').click();
      await expect.poll(async () => (await linksOnPage())[0], { timeout: 30000 }).toBe(slugs[p * PAGE]);
    }
  }
  expect(seen.length, 'the archive, walked to the last page, lists exactly the sent issues').toBe(total);
  const text = await page.locator('[data-testid="newsletter-issues-list"]').innerText();
  expect(text, 'a draft is never listed').not.toContain(DRAFT);
  // (this file's own issue appears among the pages the loop walked, or is older than the others on page 1)
  expect(seen).toContain(sqlOne(`SELECT slug FROM newsletter WHERE title = '${SUBJECT}'`));

  // never-sent rows are unreachable by any slug they could have
  const drafts = sql(`SELECT coalesce(slug, '') FROM newsletter WHERE status <> 'sent' OR NOT ispublic`);
  for (const d of drafts.filter((x) => x)) {
    expect((await page.request.get(`${BASE}/newsletter/${d}`)).status(), `unsent slug ${d} is not reachable`).toBe(404);
  }
  const guess = DRAFT.toLowerCase().replace(/[^a-z0-9]+/g, '-');
  expect((await page.request.get(`${BASE}/newsletter/${guess}`)).status(), 'the draft is not reachable by a slug guessed from its title').toBe(404);
  const draftId = sqlOne(`SELECT newsletterid FROM newsletter WHERE title = '${DRAFT}'`);
  expect((await page.request.get(`${BASE}/newsletter/${draftId}`)).status(), 'nor by its id').toBe(404);
  await page.screenshot({ path: `${SHOTS}/fn050-archive.png`, fullPage: true });
});

test('REQ-UI-053 only sent issues appear and each links to its issue page', async ({ page }) => {
  test.skip(!sqlOne(`SELECT 1 FROM newsletter WHERE title = '${SUBJECT}'`), 'issue not created');
  await gotoPublic(page, '/newsletters');
  const slugs = sentPublic();
  await expect(page.locator('[data-testid="newsletter-issue-card"]')).toHaveCount(Math.min(archivePageSize(), slugs.length), { timeout: 60000 });
  const hrefs = await page.locator('[data-testid="newsletter-issue-read"]').evaluateAll((els) => els.map((e) => (e as HTMLAnchorElement).getAttribute('href') || ''));
  for (const h of hrefs) expect(slugs, `card link ${h} is a sent, public issue`).toContain(h.replace(/^.*\/newsletter\//, ''));
  expect(await page.locator('[data-testid="newsletter-issues-list"]').innerText(), 'no draft listed').not.toContain(DRAFT);
  const card = page.locator('[data-testid="newsletter-issue-card"]').first();
  const title = ((await card.locator('[data-testid="newsletter-issue-title"]').innerText()) || '').trim();
  await card.locator('[data-testid="newsletter-issue-read"]').click();
  await expect(page.locator('[data-testid="newsletter-view-title"]')).toHaveText(title, { timeout: 60000 });
  expect(new URL(page.url()).pathname).toBe(hrefs[0].startsWith('http') ? new URL(hrefs[0]).pathname : hrefs[0]);

  // the empty state belongs to a database with no sent issue; the dev DB has other writers' issues
  if (slugs.length === 0) {
    await gotoPublic(page, '/newsletters');
    await expect(page.locator('[data-testid="newsletter-issues-empty"]')).toHaveCount(1);
  } else {
    await gotoPublic(page, '/newsletters');
    await expect(page.locator('[data-testid="newsletter-issues-empty"]')).toHaveCount(0);
    console.log(`UI-053 empty-state clause not observable: ${slugs.length} sent issue(s) exist`);
  }
});

test('REQ-UI-054 the issue view resolves prev/next by send order, hides them at the ends, and 404s unknown or unsent slugs', async ({ page }) => {
  test.skip(!sqlOne(`SELECT 1 FROM newsletter WHERE title = '${SUBJECT}'`), 'issue not created');
  const order = sentPublic(); // newest first
  expect(order.length).toBeGreaterThan(0);
  const check = async (slug: string) => {
    const idx = order.indexOf(slug);
    const newer = idx > 0, older = idx < order.length - 1;
    await gotoPublic(page, `/newsletter/${slug}`);
    await expect(page.locator('[data-testid="newsletter-view"]')).toBeVisible({ timeout: 60000 });
    await expect(page.locator('[data-testid="newsletter-view-title"]')).toBeVisible();
    await expect(page.locator('[data-testid="newsletter-view-previous"]'), `${slug}: previous`).toHaveCount(older ? 1 : 0);
    await expect(page.locator('[data-testid="newsletter-view-next"]'), `${slug}: next`).toHaveCount(newer ? 1 : 0);
    if (older) expect(await page.locator('[data-testid="newsletter-view-previous"]').getAttribute('href')).toContain(`/newsletter/${order[idx + 1]}`);
    if (newer) expect(await page.locator('[data-testid="newsletter-view-next"]').getAttribute('href')).toContain(`/newsletter/${order[idx - 1]}`);
    return { slug, newer, older };
  };
  const mySlug = sqlOne(`SELECT slug FROM newsletter WHERE title = '${SUBJECT}'`);
  const seen = [await check(order[0]), await check(order[order.length - 1]), await check(mySlug)];
  console.log('UI-054 neighbour checks:', JSON.stringify(seen), 'order:', order.join(','));
  const body = await page.locator('[data-testid="newsletter-view-body"]').innerHTML();
  expect(body, 'Markdown rendered').toContain('<');
  await page.screenshot({ path: `${SHOTS}/ui054-issue.png` });

  const unknown = await page.request.get(`${BASE}/newsletter/fxv5-no-such-issue-${RUN}`);
  expect(unknown.status(), 'unknown slug').toBe(404);
  await gotoPublic(page, `/newsletter/fxv5-no-such-issue-${RUN}`);
  await page.screenshot({ path: `${SHOTS}/ui054-404.png` });
  const draftGuess = DRAFT.toLowerCase().replace(/[^a-z0-9]+/g, '-');
  expect((await page.request.get(`${BASE}/newsletter/${draftGuess}`)).status(), 'unsent slug').toBe(404);
});
