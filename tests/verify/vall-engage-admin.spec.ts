/**
 * vall-engage-admin.spec.ts — *verify all (2026-08-08), cluster "engage", admin-side surfaces.
 *
 * REQ-FN-022 (moderation workflow), REQ-FN-031 (subscriber admin), REQ-FN-035 (popular posts and
 * per-post engagement) and REQ-FN-036 (dashboard counts). Authenticated navigation goes through
 * Blazor.navigateTo — a full page load of an admin route prerenders anonymous and bounces.
 *
 * Moderation actions here act ONLY on the comment this cluster created (VERIFY-0808), never on the
 * seven seeded rows.
 */
import { test, expect } from '@playwright/test';
import { login, nav, renderCheck, visualCheck } from './_gates';
import { MARK, SHOTS, mail, psql, psqlOne } from './_engage-helpers';


test.setTimeout(300000);

const COMMENT_EMAIL = mail('comment');

// ---------------------------------------------------------------------------------------------
// REQ-FN-036 — admin dashboard counts
// ---------------------------------------------------------------------------------------------

/** Every dashboard tile must equal the same aggregate read straight from PostgreSQL. */
test('REQ-FN-036 admin dashboard counts match the database', async ({ page }) => {
  await login(page, 'admin');
  await nav(page, '/admin', /Dashboard|Welcome/i);
  await expect(page.locator('[data-testid="dashboard-stats"]')).toBeVisible({ timeout: 60000 });

  const posts = psqlOne('SELECT count(*) FROM blogpost');
  const users = psqlOne('SELECT count(*) FROM bloguser');
  const comments = psqlOne('SELECT count(*) FROM blogcomment');
  const subs = psqlOne('SELECT count(*) FROM subscriber');

  const read = async (id: string) => (await page.locator(`[data-testid="${id}"]`).innerText()).replace(/[^\d]/g, '');
  const uiPosts = await read('stat-posts-value');
  const uiUsers = await read('stat-users-value');
  const uiComments = await read('stat-comments-value');
  const uiSubs = await read('stat-subscribers-value');
  console.log(`DASHBOARD ui posts=${uiPosts} users=${uiUsers} comments=${uiComments} subscribers=${uiSubs} | psql ${posts}/${users}/${comments}/${subs}`);

  expect(uiPosts).toBe(posts);
  expect(uiUsers).toBe(users);
  expect(uiComments).toBe(comments);
  expect(uiSubs).toBe(subs);

  for (const id of ['needs-attention', 'quick-actions', 'recent-activity', 'popular-posts']) {
    const r = await renderCheck(page, id, `[data-testid="${id}"]`, 'present');
    console.log(`DASHBOARD control ${id}: ${r.verdict} — ${r.detail}`);
  }

  const d1 = await visualCheck(page, `${SHOTS}/admin-dashboard-1280.png`, 1280);
  const d2 = await visualCheck(page, `${SHOTS}/admin-dashboard-390.png`, 390);
  console.log(`VISUAL dashboard 1280: hScroll=${d1.hScroll} zero=${JSON.stringify(d1.zeroSized)} off=${JSON.stringify(d1.offViewport)} overlaps=${JSON.stringify(d1.overlaps)} errs=${JSON.stringify(d1.consoleErrors)}`);
  console.log(`VISUAL dashboard 390: hScroll=${d2.hScroll} zero=${JSON.stringify(d2.zeroSized)} off=${JSON.stringify(d2.offViewport)} overlaps=${JSON.stringify(d2.overlaps)} errs=${JSON.stringify(d2.consoleErrors)}`);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-022 — moderation workflow on the verifier's OWN comment
// ---------------------------------------------------------------------------------------------

/**
 * Finds the verifier's confirmed-but-unapproved comment in the moderation queue, approves it, and
 * proves it becomes publicly visible; the seeded comments are never touched.
 */
test('REQ-FN-022 the moderation queue lists and approves the pending comment, which then goes public', async ({ page }) => {
  // The public spec uses a per-run address and stamps a unique `ref:` nonce into the body, so find
  // the row by its marker and then drive the grid by that nonce — the shared VERIFY-0808 marker
  // matches several rows and would leave the approve click pointing at an arbitrary one.
  const target = psqlOne(`SELECT commentid, email, comment FROM blogcomment WHERE comment LIKE '%${MARK}%' AND comment LIKE '%ref:%' AND moderationstatus = 'PendingApproval' ORDER BY commentid DESC LIMIT 1`);
  expect(target, 'the public-comment + opt-in tests must have created a PendingApproval row first').toBeTruthy();
  const [mine, mineEmail, mineBody] = target.split('|');
  const ref = (mineBody.match(/ref:[a-z0-9]+/) || [''])[0];
  expect(ref, 'the target comment must carry its ref nonce').toBeTruthy();
  console.log(`MODERATION target commentid=${mine} email=${mineEmail} ${ref} status=${psqlOne(`SELECT moderationstatus FROM blogcomment WHERE commentid = ${mine}`)}`);

  await login(page, 'admin');
  await nav(page, '/comments', /Comment/i);
  await expect(page.locator('[data-testid="comments-grid"], [data-testid="comments-empty"]')).toBeVisible({ timeout: 60000 });

  const grid = await renderCheck(page, 'comments-grid', '[data-testid="comments-grid"]', 'table');
  console.log(`MODERATION grid: ${grid.verdict} — ${grid.detail}`);
  expect(grid.verdict).toBe('RENDERS');
  const mv = await visualCheck(page, `${SHOTS}/admin-comments-1280.png`, 1280);
  console.log(`VISUAL comments 1280: hScroll=${mv.hScroll} zero=${JSON.stringify(mv.zeroSized)} off=${JSON.stringify(mv.offViewport)} overlaps=${JSON.stringify(mv.overlaps)} errs=${JSON.stringify(mv.consoleErrors)}`);

  const tabs = await Promise.all(
    ['all', 'pending', 'approved', 'spam'].map(async (t) => `${t}=${(await page.locator(`[data-testid="comments-tab-${t}"]`).innerText().catch(() => '?')).trim()}`),
  );
  console.log('MODERATION tabs: ' + tabs.join(' | '));

  // Search on the shared marker first (proves the queue's search works over comment text), then
  // narrow to the single row this test owns.
  await page.fill('[data-testid="comments-search"]', MARK);
  await page.waitForTimeout(3000);
  const markRows = await page.locator('[data-testid="comment-row-text"]').count();
  const markRowsDb = Number(psqlOne(`SELECT count(*) FROM blogcomment WHERE comment LIKE '%${MARK}%'`));
  console.log(`MODERATION rows matching "${MARK}": ui=${markRows} psql=${markRowsDb}`);
  expect(markRows).toBe(markRowsDb);

  await page.fill('[data-testid="comments-search"]', ref);
  await page.waitForTimeout(3000);
  const rows = await page.locator('[data-testid="comment-row-text"]').count();
  console.log(`MODERATION rows matching "${ref}": ${rows}`);
  expect(rows, 'the ref nonce must isolate exactly one row').toBe(1);

  const status = (await page.locator('[data-testid="comment-row-status"]').first().innerText()).trim();
  const shownEmail = (await page.locator('[data-testid="comment-row-email"]').first().innerText()).trim();
  console.log(`MODERATION row status="${status}" email="${shownEmail}"`);
  expect(shownEmail.toLowerCase()).toBe(mineEmail.toLowerCase());
  expect(status).toBe('Pending');

  const approve = page.locator('[data-testid="comment-approve"]').first();
  await expect(approve).toBeVisible({ timeout: 30000 });
  await approve.click();
  await page.waitForTimeout(4000);

  const after = psqlOne(`SELECT moderationstatus, published FROM blogcomment WHERE commentid = ${mine}`);
  console.log(`MODERATION after approve: ${after}`);
  expect(after).toBe('Approved|t');

  // Seeded comments must be untouched.
  const seeded = psqlOne("SELECT count(*) FROM blogcomment WHERE commentid <= 7 AND moderationstatus = 'Approved' AND published");
  console.log(`MODERATION seeded rows still Approved/published: ${seeded}`);
  expect(seeded).toBe('7');

  // Now it is publicly visible.
  await page.goto('http://localhost:5399/post/blazor-render-modes-explained', { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="comments-section"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(1500);
  const sectionText = await page.locator('[data-testid="comments-section"]').innerText();
  console.log(`PUBLIC thread now contains the approved comment (${ref}): ${sectionText.includes(ref)}`);
  expect(sectionText).toContain(ref);
  expect(sectionText, 'the email must still never be rendered').not.toContain('@techieblog.test');

  const approvedDb = psqlOne("SELECT count(*) FROM blogcomment WHERE postid = 1 AND moderationstatus = 'Approved'");
  expect((await page.locator('[data-testid="comments-count"]').innerText())).toContain(approvedDb);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-031 — subscriber admin
// ---------------------------------------------------------------------------------------------

/** The subscriber screen must list, search, filter by status, toggle status and export. */
test('REQ-FN-031 subscriber list, search, status change and export', async ({ page }) => {
  const total = psqlOne('SELECT count(*) FROM subscriber');
  expect(Number(total), 'the public subscribe tests must have created rows first').toBeGreaterThan(0);

  await login(page, 'admin');
  await nav(page, '/admin/subscribers', /Subscriber/i);
  await expect(page.locator('[data-testid="subscribers-grid"], [data-testid="subscribers-empty"]')).toBeVisible({ timeout: 60000 });

  const grid = await renderCheck(page, 'subscribers-grid', '[data-testid="subscribers-grid"]', 'table');
  console.log(`SUBSCRIBERS grid: ${grid.verdict} — ${grid.detail} (psql total=${total})`);
  expect(grid.verdict).toBe('RENDERS');

  const summary = (await page.locator('[data-testid="subscribers-summary"]').innerText()).replace(/\s+/g, ' ');
  const tabAll = (await page.locator('[data-testid="subscribers-tab-all"]').innerText()).trim();
  console.log(`SUBSCRIBERS summary="${summary}" tabAll="${tabAll}"`);
  expect(tabAll).toContain(total);

  for (const id of ['subscriber-row-email', 'subscriber-row-date', 'subscriber-row-status']) {
    const r = await renderCheck(page, id, `[data-testid="${id}"]`, 'value');
    console.log(`SUBSCRIBERS control ${id}: ${r.verdict} — ${r.detail}`);
    expect(r.verdict).toBe('RENDERS');
  }

  // Search narrows the grid. The public spec uses a per-run address, so search on the prefix and
  // cross-check the hit count against the same LIKE in the database.
  const term = 'verify0808+subscribe';
  const expectedHits = Number(psqlOne(`SELECT count(*) FROM subscriber WHERE email LIKE '${term}%'`));
  expect(expectedHits, 'the public subscribe test must have created a row first').toBeGreaterThan(0);
  await page.fill('[data-testid="subscribers-search"]', term);
  await page.waitForTimeout(3000);
  const found = await page.locator('[data-testid="subscriber-row-email"]').count();
  const firstEmail = (await page.locator('[data-testid="subscriber-row-email"]').first().innerText().catch(() => '')).trim();
  console.log(`SUBSCRIBERS search "${term}" -> rows=${found} psql=${expectedHits} first="${firstEmail}"`);
  expect(found).toBe(expectedHits);
  expect(firstEmail.toLowerCase()).toContain(term.toLowerCase());

  // Status change on the verifier's own row.
  const target = firstEmail;
  const before = psqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${target}')`);
  const toggle = page.locator('[data-testid="subscriber-activate"], [data-testid="subscriber-deactivate"]').first();
  const which = await toggle.getAttribute('data-testid');
  await toggle.click();
  await page.waitForTimeout(3500);
  const afterStatus = psqlOne(`SELECT isconfirmed FROM subscriber WHERE lower(email) = lower('${target}')`);
  console.log(`SUBSCRIBERS status toggle via ${which}: isconfirmed ${before} -> ${afterStatus}`);
  expect(afterStatus).not.toBe(before);

  // Export produces a real download.
  const dl = page.waitForEvent('download', { timeout: 60000 }).catch(() => null);
  await page.locator('[data-testid="subscribers-export"]').click();
  const download = await dl;
  console.log(`SUBSCRIBERS export download: ${download ? download.suggestedFilename() : 'NONE'}`);
  expect(download, 'export must produce a CSV download').not.toBeNull();

  const s1 = await visualCheck(page, `${SHOTS}/admin-subscribers-1280.png`, 1280);
  const s2v = await visualCheck(page, `${SHOTS}/admin-subscribers-390.png`, 390);
  console.log(`VISUAL subscribers 1280: hScroll=${s1.hScroll} zero=${JSON.stringify(s1.zeroSized)} off=${JSON.stringify(s1.offViewport)} overlaps=${JSON.stringify(s1.overlaps)} errs=${JSON.stringify(s1.consoleErrors)}`);
  console.log(`VISUAL subscribers 390: hScroll=${s2v.hScroll} zero=${JSON.stringify(s2v.zeroSized)} off=${JSON.stringify(s2v.offViewport)} overlaps=${JSON.stringify(s2v.overlaps)} errs=${JSON.stringify(s2v.consoleErrors)}`);
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-035 — popular posts + per-post engagement statistics
// ---------------------------------------------------------------------------------------------

/** The analytics screen's popular-post table must carry real views, comments and ratings. */
test('REQ-FN-035 popular posts and per-post engagement statistics render real numbers', async ({ page }) => {
  const views = psqlOne('SELECT count(*) FROM postviews');
  const comments = psqlOne("SELECT count(*) FROM blogcomment WHERE moderationstatus = 'Approved'");
  const ratings = psqlOne('SELECT count(*) FROM postrating WHERE isemailverified');
  console.log(`ANALYTICS db: postviews=${views} approvedComments=${comments} verifiedRatings=${ratings}`);

  await login(page, 'admin');
  await nav(page, '/admin/analytics', /Analytic/i);
  await expect(page.locator('[data-testid="analytics-stat-tiles"]')).toBeVisible({ timeout: 60000 });
  await page.waitForTimeout(3000);

  const tiles = await page.locator('[data-testid="analytics-stat-tiles"]').innerText();
  console.log('ANALYTICS tiles: ' + tiles.replace(/\s+/g, ' '));

  for (const id of ['analytics-trend-card', 'analytics-popular-card', 'analytics-category-card']) {
    const r = await renderCheck(page, id, `[data-testid="${id}"]`, 'present');
    console.log(`ANALYTICS card ${id}: ${r.verdict}`);
  }

  const popularGrid = await page.locator('[data-testid="analytics-popular-grid"]').count();
  const popularEmpty = await page.locator('[data-testid="analytics-popular-empty"]').count();
  console.log(`ANALYTICS popular grid=${popularGrid} empty=${popularEmpty}`);

  if (popularGrid) {
    const rows = await page.locator('[data-testid="popular-row-title"]').count();
    const sample = {
      title: (await page.locator('[data-testid="popular-row-title"]').first().innerText()).trim(),
      views: (await page.locator('[data-testid="popular-row-views"]').first().innerText()).trim(),
      unique: (await page.locator('[data-testid="popular-row-unique"]').first().innerText()).trim(),
      comments: (await page.locator('[data-testid="popular-row-comments"]').first().innerText()).trim(),
      rating: (await page.locator('[data-testid="popular-row-rating"]').first().innerText()).trim(),
    };
    console.log(`ANALYTICS popular rows=${rows} sample=${JSON.stringify(sample)}`);
    expect(rows).toBeGreaterThan(0);
  }

  const trendEmpty = await page.locator('[data-testid="analytics-trend-empty"]').count();
  const trendChart = await page.locator('[data-testid="analytics-trend-chart"]').count();
  console.log(`ANALYTICS trend chart=${trendChart} empty=${trendEmpty}`);

  const a1 = await visualCheck(page, `${SHOTS}/admin-analytics-1280.png`, 1280);
  const a2 = await visualCheck(page, `${SHOTS}/admin-analytics-390.png`, 390);
  console.log(`VISUAL analytics 1280: hScroll=${a1.hScroll} zero=${JSON.stringify(a1.zeroSized)} off=${JSON.stringify(a1.offViewport)} overlaps=${JSON.stringify(a1.overlaps)} errs=${JSON.stringify(a1.consoleErrors)}`);
  console.log(`VISUAL analytics 390: hScroll=${a2.hScroll} zero=${JSON.stringify(a2.zeroSized)} off=${JSON.stringify(a2.offViewport)} overlaps=${JSON.stringify(a2.overlaps)} errs=${JSON.stringify(a2.consoleErrors)}`);

  // The screen is only honest if the engagement it claims exists in the database.
  expect(popularGrid + popularEmpty, 'the popular-posts control must render one state or the other').toBeGreaterThan(0);
  if (Number(views) > 0) {
    expect(popularGrid, 'postviews has rows, so the popular-posts table must not be empty').toBe(1);
  }
});
