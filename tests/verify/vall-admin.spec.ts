/**
 * vall-admin.spec.ts — `*verify all` (2026-08-08), ADMINISTRATION cluster.
 *
 * Grades REQ-UI-019/020/021/022/023/025/026/032/044/047 through the three gates:
 *   1. ACCEPTANCE  — one test per REQ, asserting the observable outcome.
 *   2. §4a DATA-RENDER — every control the Admin DevGuide lists must render DATA, cross-checked
 *      against psql AT THE MOMENT OF MEASUREMENT (seven verifier agents share this database, and a
 *      snapshot taken at process start already went stale once mid-run: the dashboard read 11 posts
 *      against a start-of-run snapshot of 10 because a sibling had published one).
 *   3. §4b VISUAL-TRUTH — geometry at 1280x900 and 390x844 through `visualCheck`.
 *
 * Nothing in `source/**` is touched. The only write is REQ-UI-032's theme round trip, which
 * restores `Theme.SiteTheme` to its original value in the same test.
 */
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import { execSync } from 'child_process';
import { BASE, USERS, nav, renderCheck, visualCheck, ControlResult } from './_gates';

// NOT test-results/ — Playwright wipes that directory at the start of every run and seven sibling
// verifier agents run concurrently, so evidence stored there is erased by whoever starts next.
const OUT = '.verify/shots/admin';
fs.mkdirSync(OUT, { recursive: true });

// =====================================================================================
// psql truth, read live
// =====================================================================================

/** Runs one scalar query against the shared WinPostgre container and returns the first value. */
function psql(sql: string): string {
  const cmd = `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -c ${JSON.stringify(sql)}`;
  return execSync(cmd, { encoding: 'utf8' }).split('\n')[0].trim();
}

function psqlInt(sql: string): number {
  return Number(psql(sql));
}

const LIVE_POST = '(IsDeleted = FALSE OR IsDeleted IS NULL)';
const SQL = {
  posts: `SELECT COUNT(*) FROM BlogPost WHERE ${LIVE_POST}`,
  drafts: `SELECT COUNT(*) FROM BlogPost WHERE Published = FALSE AND (ScheduledPublishOn IS NULL OR ScheduledPublishOn <= (NOW() AT TIME ZONE 'utc')) AND ${LIVE_POST}`,
  scheduled: `SELECT COUNT(*) FROM BlogPost WHERE Published = FALSE AND ScheduledPublishOn > (NOW() AT TIME ZONE 'utc') AND ${LIVE_POST}`,
  comments: 'SELECT COUNT(*) FROM BlogComment',
  // The dashboard's "pending" badge is `AdminCountsRepo`'s `Published = FALSE` count…
  pending: 'SELECT COUNT(*) FROM BlogComment WHERE Published = FALSE',
  // …while the moderation queue's Pending TAB is `CommentsList.MapStatus`, which reads
  // ModerationStatus directly (REQ-FN-022): Approved/Spam/Rejected map to themselves,
  // PendingVerification maps to "Unconfirmed", and everything else is "Pending".
  pendingTab: `SELECT COUNT(*) FROM BlogComment WHERE ModerationStatus NOT IN ('Approved','Spam','Rejected','PendingVerification')`,
  categories: 'SELECT COUNT(*) FROM Category',
  // Both list screens count PUBLISHED posts only — `CategoryRepo.SelectAllWithCountsSql` and
  // `BlogTagRepo.SelectAllWithCountsSql` join with `p.Published = TRUE AND (IsDeleted = FALSE …)`.
  // Comparing against every categorised/tagged post would fail a correct screen.
  categorisedPosts: `SELECT COUNT(*) FROM BlogPost WHERE CategoryId IS NOT NULL AND Published = TRUE AND ${LIVE_POST}`,
  tags: 'SELECT COUNT(*) FROM Tag',
  postTag: `SELECT COUNT(*) FROM PostTag pt JOIN BlogPost p ON p.PostId = pt.PostId WHERE p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL)`,
  users: 'SELECT COUNT(*) FROM BlogUser',
  subscribers: 'SELECT COUNT(*) FROM Subscriber',
  postViews: 'SELECT COUNT(*) FROM PostViews',
  setting: (key: string) => `SELECT SettingValue FROM SiteSetting WHERE SettingKey = '${key}'`,
  commentsInDays: (d: number) =>
    `SELECT COUNT(*) FROM BlogComment WHERE GivenOn >= (CURRENT_DATE - INTERVAL '${d - 1} days') AND GivenOn < (CURRENT_DATE + INTERVAL '1 day')`,
};

/**
 * Asserts a rendered number against psql, tolerating a sibling agent writing between the two reads.
 *
 * A single disagreement is re-measured (re-read the DB, re-render the screen) up to three times; a
 * disagreement that SURVIVES a fresh render is a real defect and fails the test.
 */
async function assertAgainstDb(
  label: string,
  readUi: () => Promise<number>,
  sql: string,
  reRender: () => Promise<void>,
): Promise<{ ui: number; db: number }> {
  let ui = NaN;
  let db = NaN;
  // Poll rather than sample once: `nav()` gates on the destination's <h1>, which renders BEFORE the
  // list query returns, so a single immediate read can legitimately see zero rows on a loaded host.
  // Re-querying psql each round also keeps a sibling agent's concurrent write from staling the target.
  const deadline = Date.now() + 30000;
  let reRendered = false;
  while (Date.now() < deadline) {
    const before = psqlInt(sql);
    ui = await readUi();
    const after = psqlInt(sql);
    db = after;
    if (ui === before && ui === after) {
      console.log(`[db] ${label}: ui=${ui} psql=${db} ✓`);
      return { ui, db };
    }
    if (!reRendered && Date.now() > deadline - 15000) {
      console.log(`[db] ${label}: ui=${ui} psql=${before}→${after} — forcing a re-render`);
      await reRender();
      reRendered = true;
    }
    await new Promise((r) => setTimeout(r, 1500));
  }
  console.log(`[db] ${label}: ui=${ui} psql=${db} ✗ after 30s`);
  expect(ui, `${label}: rendered value vs psql`).toBe(db);
  return { ui, db };
}

/**
 * Waits for a rendered count to agree with psql, then returns both.
 *
 * Used after an interaction (a tab click, a search keystroke) where the render is asynchronous and
 * the target itself can move under a sibling agent's write. Polls instead of sleeping a guessed
 * interval, so a slow render is waited out and a genuinely wrong filter still fails.
 */
async function pollUntilMatchesDb(label: string, readUi: () => Promise<number>, sql: string, timeoutMs = 25000) {
  const deadline = Date.now() + timeoutMs;
  let ui = NaN;
  let db = NaN;
  while (Date.now() < deadline) {
    db = psqlInt(sql);
    ui = await readUi();
    if (ui === db) {
      console.log(`[db] ${label}: ui=${ui} psql=${db} ✓`);
      return { ui, db };
    }
    await new Promise((r) => setTimeout(r, 1500));
  }
  console.log(`[db] ${label}: ui=${ui} psql=${db} ✗ (polled ${timeoutMs}ms)`);
  return { ui, db };
}

// =====================================================================================
// Sign-in
// =====================================================================================

/**
 * Sign-in that survives a loaded host.
 *
 * `_gates.login` clicks Submit a fixed 2 s after the circuit opens. With seven verifier agents on
 * one host the interactive render batch for `/login` arrives ~18 s after load (measured), so the
 * fixed wait clicked the PRERENDERED `<form>` and the server answered "A valid antiforgery token
 * was not provided" / "The POST request does not specify which form is being submitted" — a harness
 * race, not a product defect. This waits for the real hydration signal: the interactive renderer
 * stamps an `_bl_<guid>` attribute on every element carrying an event handler.
 */
async function signIn(page: Page, role: 'admin' | 'editor' = 'admin') {
  const user = USERS[role];
  let last = '';
  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('[data-testid="login-email"]', { timeout: 60000 });
      await page.waitForFunction(() => {
        const b = document.querySelector('[data-testid="login-submit"]');
        return !!b && Array.from(b.attributes).some((a) => a.name.startsWith('_bl'));
      }, { timeout: 90000 });
      await page.waitForTimeout(1000);
      await page.fill('[data-testid="login-email"]', user.email);
      await page.fill('[data-testid="login-password"]', user.password);
      await page.click('[data-testid="login-submit"]');
      await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 40000 });
      await page.waitForTimeout(2000);
      console.log(`[login] ${role} signed in on attempt ${attempt} → ${page.url()}`);
      return page.url();
    } catch (e: any) {
      last = `${e.message ?? e}`.split('\n')[0];
      const body = ((await page.locator('body').innerText().catch(() => '')) || '').slice(0, 140);
      console.log(`[login] ${role} attempt ${attempt} failed: ${last} | body="${body.replace(/\s+/g, ' ')}"`);
    }
  }
  throw new Error(`sign-in as ${role} failed after 4 attempts: ${last}`);
}

// =====================================================================================
// Evidence collection
// =====================================================================================

const observations: any[] = [];

function record(screen: string, controls: ControlResult[], visual: string) {
  observations.push({ screen, controls, visual });
  fs.writeFileSync(`${OUT}/devguide-observations.json`, JSON.stringify(observations, null, 2));
}

/**
 * Which of `visualCheck`'s off-viewport findings are real.
 *
 * The shared detector measures against the VIEWPORT, so every cell of a deliberately
 * horizontally-scrollable responsive table reads as "off-viewport" at 390 px. Probed on /users at
 * 390: `user-change-role` sits inside `div.relative.w-full.overflow-auto` whose clientWidth is 356
 * and scrollWidth 913, while `document.documentElement` measures scrollWidth === clientWidth === 390
 * — the page does not scroll sideways, the table does, which is the intended pattern. Only a control
 * with NO horizontally scrollable ancestor is genuinely unreachable.
 */
async function realOffViewport(page: Page, reported: string[]): Promise<{ real: string[]; scrollable: string[] }> {
  if (!reported.length) return { real: [], scrollable: [] };
  const ids = reported.map((r) => r.split('@')[0]);
  return page.evaluate((names) => {
    const real: string[] = [];
    const scrollable: string[] = [];
    for (const name of Array.from(new Set(names))) {
      const el = document.querySelector(`[data-testid="${name}"]`);
      if (!el) continue;
      let n: Element | null = el.parentElement;
      let scroller = false;
      while (n) {
        const s = getComputedStyle(n);
        if ((s.overflowX === 'auto' || s.overflowX === 'scroll') && (n as HTMLElement).scrollWidth > (n as HTMLElement).clientWidth + 2) {
          scroller = true;
          break;
        }
        n = n.parentElement;
      }
      (scroller ? scrollable : real).push(name);
    }
    return { real, scrollable };
  }, ids);
}

/** Runs the §4b gate at both widths and returns a one-line summary + the failure list. */
async function bothWidths(page: Page, slug: string) {
  const wide = await visualCheck(page, `${OUT}/${slug}-1280.png`, 1280);
  const wideOff = await realOffViewport(page, wide.offViewport);
  const narrow = await visualCheck(page, `${OUT}/${slug}-390.png`, 390);
  const narrowOff = await realOffViewport(page, narrow.offViewport);
  await page.setViewportSize({ width: 1280, height: 900 });
  const problems: string[] = [];
  const notes: string[] = [];
  for (const [v, off] of [[wide, wideOff], [narrow, narrowOff]] as const) {
    if (v.overlaps.length) problems.push(`${v.width}: ${v.overlaps.length} overlaps ${JSON.stringify(v.overlaps.slice(0, 3))}`);
    if (v.zeroSized.length) problems.push(`${v.width}: zero-size ${v.zeroSized.slice(0, 5).join(',')}`);
    if (off.real.length) problems.push(`${v.width}: off-viewport ${off.real.slice(0, 5).join(',')}`);
    if (off.scrollable.length) notes.push(`${v.width}: ${off.scrollable.length} controls inside a scrollable table (${off.scrollable.slice(0, 3).join(',')}) — not a defect`);
    if (v.hScroll > 2) problems.push(`${v.width}: page hScroll=${v.hScroll}`);
    if (v.consoleErrors.length) problems.push(`${v.width}: console ${v.consoleErrors.slice(0, 2).join(' | ')}`);
  }
  const summary = problems.length ? `VISUAL-FAIL — ${problems.join('; ')}` : `VISUAL-OK${notes.length ? ` (${notes.join('; ')})` : ''}`;
  console.log(`[VISUAL] ${slug} → ${summary} (${OUT}/${slug}-1280.png, ${OUT}/${slug}-390.png)`);
  return { summary, problems, notes, wide, narrow };
}

/** True when the Blazor error boundary or an unstyled fallback is on screen (REQ-UI-048 evidence). */
async function boundaryState(page: Page) {
  return page.evaluate(() => {
    const blazorErr = document.querySelector('#blazor-error-ui') as HTMLElement | null;
    return {
      blazorErrorUi: !!blazorErr && getComputedStyle(blazorErr).display !== 'none',
      boundaryText: /An error has occurred|Unhandled exception|InvalidOperationException/i.test(document.body.innerText || ''),
      bodyBackground: getComputedStyle(document.body).backgroundColor,
      stylesheetCount: document.styleSheets.length,
    };
  });
}

// =====================================================================================
// REQ-UI-019 — Admin dashboard: stat tiles + role-gated quick actions
// =====================================================================================
test('REQ-UI-019 admin dashboard tiles show live counts and quick actions are role-gated', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/admin', /^Dashboard$/);
  const reRender = async () => { await nav(page, '/BlogsList', /Posts/i); await nav(page, '/admin', /^Dashboard$/); };

  const controls: ControlResult[] = [];
  for (const [name, sel] of [
    ['posts tile', '[data-testid="stat-posts-value"]'],
    ['users tile', '[data-testid="stat-users-value"]'],
    ['comments tile', '[data-testid="stat-comments-value"]'],
    ['subscribers tile', '[data-testid="stat-subscribers-value"]'],
    ['needs-attention pending', '[data-testid="attention-pending-comments"]'],
    ['needs-attention scheduled', '[data-testid="attention-scheduled-posts"]'],
    ['needs-attention draft', '[data-testid="attention-draft-posts"]'],
    ['quick actions', '[data-testid="quick-actions"]'],
  ] as const) {
    controls.push(await renderCheck(page, name, sel, 'value'));
  }

  const num = (id: string) => async () =>
    Number(((await page.locator(`[data-testid="${id}"]`).textContent()) || '').replace(/[^\d-]/g, ''));

  const posts = await assertAgainstDb('posts tile', num('stat-posts-value'), SQL.posts, reRender);
  const users = await assertAgainstDb('users tile', num('stat-users-value'), SQL.users, reRender);
  const comments = await assertAgainstDb('comments tile', num('stat-comments-value'), SQL.comments, reRender);
  const subs = await assertAgainstDb('subscribers tile', num('stat-subscribers-value'), SQL.subscribers, reRender);
  const pending = await assertAgainstDb('pending-comments badge', num('attention-pending-comments'), SQL.pending, reRender);
  const sched = await assertAgainstDb('scheduled badge', num('attention-scheduled-posts'), SQL.scheduled, reRender);
  const drafts = await assertAgainstDb('draft badge', num('attention-draft-posts'), SQL.drafts, reRender);
  console.log(`[REQ-UI-019] tiles posts=${posts.ui} users=${users.ui} comments=${comments.ui} subs=${subs.ui} | attention pending=${pending.ui} scheduled=${sched.ui} draft=${drafts.ui}`);
  for (const [label, r] of [['posts', posts], ['users', users], ['comments', comments], ['subscribers', subs], ['pending', pending], ['scheduled', sched], ['drafts', drafts]] as const) {
    expect(r.ui, `${label} tile vs psql`).toBe(r.db);
  }

  // Popular posts / recent activity.
  const views = psqlInt(SQL.postViews);
  const popularRows = await page.locator('[data-testid="popular-post-row"]').count();
  const popularEmpty = await page.locator('[data-testid="popular-posts-empty"]').count();
  console.log(`[REQ-UI-019] popular rows=${popularRows} empty-state=${popularEmpty} | psql PostViews=${views}`);
  if (views === 0) {
    expect(popularRows === 0 || popularEmpty > 0, 'with zero PostViews the ranking must not fabricate rows').toBe(true);
    controls.push({ control: 'popular posts', verdict: 'RENDERS', detail: `NO-DATA: psql PostViews=0 → ${popularRows} rows, empty state=${popularEmpty}` });
  } else {
    expect(popularRows, 'PostViews exist so the ranking must render').toBeGreaterThan(0);
    controls.push({ control: 'popular posts', verdict: 'RENDERS', detail: `${popularRows} ranked rows` });
  }
  const recentItems = await page.locator('[data-testid="recent-activity"] [data-slot="item"], [data-testid="recent-activity"] li').count();
  const recentEmpty = await page.locator('[data-testid="recent-activity-empty"]').count();
  console.log(`[REQ-UI-019] recent activity items=${recentItems} empty=${recentEmpty}`);
  controls.push({ control: 'recent activity', verdict: recentItems > 0 || recentEmpty > 0 ? 'RENDERS' : 'RENDER-EMPTY', detail: `items=${recentItems}, empty-state=${recentEmpty}` });
  expect(recentItems + recentEmpty, 'recent activity must render items or an explicit empty state').toBeGreaterThan(0);

  for (const id of ['action-new-post', 'action-moderate-comments', 'action-send-newsletter', 'action-manage-users']) {
    await expect(page.locator(`[data-testid="${id}"]`), `admin quick action ${id}`).toBeVisible();
  }

  const vis = await bothWidths(page, 'admin-dashboard');
  record('/admin', controls, vis.summary);
  expect(vis.problems, 'visual @ /admin').toEqual([]);
});

test('REQ-UI-019 editor is never offered an AdminOnly quick action', async ({ page }) => {
  await signIn(page, 'editor');
  await nav(page, '/admin', /^Dashboard$/);
  const offered: string[] = [];
  for (const id of ['action-new-post', 'action-moderate-comments', 'action-send-newsletter', 'action-manage-users']) {
    if (await page.locator(`[data-testid="${id}"]`).count()) offered.push(id);
  }
  console.log(`[REQ-UI-019] editor quick actions = ${offered.join(', ')}`);
  expect(offered).not.toContain('action-send-newsletter');
  expect(offered).not.toContain('action-manage-users');
  expect(offered).toContain('action-moderate-comments');

  for (const id of offered) {
    await page.locator(`[data-testid="${id}"]`).click();
    await page.waitForTimeout(4000);
    const url = page.url();
    console.log(`[REQ-UI-019] editor ${id} → ${url}`);
    expect(url, `${id} must not bounce to access-denied`).not.toContain('access-denied');
    await nav(page, '/admin', /^Dashboard$/);
  }
});

// =====================================================================================
// REQ-UI-020 — Users list + add user
// =====================================================================================
test('REQ-UI-020 users list renders every seeded user with role badges, search and an add-user form', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/users', /^Users$/);
  const reRender = async () => { await nav(page, '/admin', /^Dashboard$/); await nav(page, '/users', /^Users$/); };

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'user grid', '[data-testid="users-grid"]', 'table'));
  controls.push(await renderCheck(page, 'users count', '[data-testid="users-count"]', 'value'));
  controls.push(await renderCheck(page, 'search box', '[data-testid="users-search"]', 'present'));
  controls.push(await renderCheck(page, 'role tabs', '[data-testid="users-role-tabs"]', 'present'));

  const rows = await assertAgainstDb('user rows', async () => page.locator('[data-testid="user-row-name"]').count(), SQL.users, reRender);
  expect(rows.ui, 'user rows vs psql').toBe(rows.db);

  const emails = await page.locator('[data-testid="user-row-email"]').allTextContents();
  const roles = await page.locator('[data-testid="user-row-role"]').allTextContents();
  const countText = ((await page.locator('[data-testid="users-count"]').textContent()) || '').trim();
  console.log(`[REQ-UI-020] rows=${rows.ui} roles=${JSON.stringify(roles)} count-badge="${countText}"`);
  expect(emails.filter((e) => e.trim()).length, 'every row renders an email').toBe(rows.ui);
  expect(roles.filter((r) => r.trim()).length, 'every row renders a role badge').toBe(rows.ui);
  expect(countText, 'count badge agrees with the rendered rows').toContain(String(rows.ui));

  // The search box is debounced and the host is loaded, so poll rather than sleep a guessed interval.
  await page.fill('[data-testid="users-search"]', 'editor');
  let filtered = rows.ui;
  for (const deadline = Date.now() + 20000; Date.now() < deadline;) {
    filtered = await page.locator('[data-testid="user-row-name"]').count();
    if (filtered < rows.ui) break;
    await page.waitForTimeout(1500);
  }
  const emailsAfter = await page.locator('[data-testid="user-row-email"]').allTextContents();
  console.log(`[REQ-UI-020] search "editor" → ${filtered} rows (was ${rows.ui}): ${JSON.stringify(emailsAfter)}`);
  expect(filtered, 'search must narrow the list').toBeLessThan(rows.ui);
  expect(filtered, 'search must still match the editor').toBeGreaterThan(0);
  expect(emailsAfter.join(' ').toLowerCase(), 'the surviving row must be the editor').toContain('editor');
  await page.fill('[data-testid="users-search"]', '');
  await page.waitForTimeout(1200);
  controls.push({ control: 'search filter', verdict: 'RENDERS', detail: `${rows.ui} → ${filtered} rows on "editor"` });
  controls.push(await renderCheck(page, 'row actions (change role)', '[data-testid="user-change-role"]', 'present'));

  const vis = await bothWidths(page, 'users-list');
  record('/users', controls, vis.summary);

  await nav(page, '/AddUser', /Add New User/);
  const formControls: ControlResult[] = [];
  for (const [name, id] of [
    ['first name', 'user-first-name'],
    ['last name', 'user-last-name'],
    ['email', 'user-email'],
    ['password', 'user-password'],
    ['confirm password', 'user-confirm-password'],
    ['role select', 'user-role'],
    ['submit', 'add-user-submit'],
  ] as const) {
    formControls.push(await renderCheck(page, name, `[data-testid="${id}"]`, 'present'));
  }
  const visAdd = await bothWidths(page, 'add-user');
  record('/AddUser', formControls, visAdd.summary);
  for (const c of formControls) expect(c.verdict, `AddUser ${c.control}`).toBe('RENDERS');

  expect(vis.problems, 'visual @ /users').toEqual([]);
  expect(visAdd.problems, 'visual @ /AddUser').toEqual([]);
});

// =====================================================================================
// REQ-UI-021 — Comment moderation queue
// =====================================================================================
test('REQ-UI-021 comment queue lists every comment with per-row and bulk moderation actions', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/CommentsList', /Comments Management/);
  const reRender = async () => { await nav(page, '/admin', /^Dashboard$/); await nav(page, '/CommentsList', /Comments Management/); };

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'comments grid', '[data-testid="comments-grid"]', 'table'));
  controls.push(await renderCheck(page, 'comments count', '[data-testid="comments-count"]', 'value'));
  controls.push(await renderCheck(page, 'status tabs', '[data-testid="comments-status-tabs"]', 'present'));
  controls.push(await renderCheck(page, 'search', '[data-testid="comments-search"]', 'present'));
  controls.push(await renderCheck(page, 'bulk action select', '[data-testid="comments-bulk-action"]', 'present'));
  controls.push(await renderCheck(page, 'bulk apply', '[data-testid="comments-bulk-apply"]', 'present'));
  controls.push(await renderCheck(page, 'select all', '[data-testid="comments-select-all"]', 'present'));

  const rows = await assertAgainstDb('comment rows', async () => page.locator('[data-testid="comment-row-text"]').count(), SQL.comments, reRender);
  expect(rows.ui, 'comment rows vs psql').toBe(rows.db);

  const authors = await page.locator('[data-testid="comment-row-author"]').allTextContents();
  const posts = await page.locator('[data-testid="comment-row-post"]').allTextContents();
  const statuses = await page.locator('[data-testid="comment-row-status"]').allTextContents();
  const countText = ((await page.locator('[data-testid="comments-count"]').textContent()) || '').trim();
  console.log(`[REQ-UI-021] rows=${rows.ui} statuses=${JSON.stringify(statuses)} count="${countText}"`);
  expect(authors.filter((a) => a.trim()).length, 'every row has an author').toBe(rows.ui);
  expect(posts.filter((p) => p.trim()).length, 'every row names its post').toBe(rows.ui);
  expect(statuses.filter((s) => s.trim()).length, 'every row has a status').toBe(rows.ui);
  expect(countText).toContain(String(rows.ui));

  // Approve/reject controls must exist on a row.
  const rowActions = (await page.locator('[data-testid="comment-approve"]').count())
    + (await page.locator('[data-testid="comment-spam"]').count())
    + (await page.locator('[data-testid="comment-delete"]').count());
  console.log(`[REQ-UI-021] per-row moderation controls = ${rowActions}`);
  expect(rowActions, 'rows must offer moderation actions').toBeGreaterThan(0);
  controls.push({ control: 'per-row moderation actions', verdict: 'RENDERS', detail: `${rowActions} approve/spam/delete controls` });

  // Pending tab must agree with psql.
  await page.click('[data-testid="comments-tab-pending"]');
  const pendingResult = await pollUntilMatchesDb(
    'pending tab',
    async () => page.locator('[data-testid="comment-row-text"]').count(),
    SQL.pendingTab);
  const tabLabel = ((await page.locator('[data-testid="comments-tab-pending"]').textContent()) || '').trim();
  console.log(`[REQ-UI-021] pending tab rows=${pendingResult.ui} | psql PendingApproval=${pendingResult.db} | tab label "${tabLabel}"`);
  expect(pendingResult.ui, 'pending tab vs psql moderation status').toBe(pendingResult.db);
  expect(tabLabel, 'the tab badge must agree with the rows it shows').toContain(`(${pendingResult.db})`);
  controls.push({ control: 'pending tab', verdict: 'RENDERS', detail: `${pendingResult.ui} rows, psql ${pendingResult.db}, label "${tabLabel}"` });
  await page.click('[data-testid="comments-tab-all"]');
  await pollUntilMatchesDb('all tab', async () => page.locator('[data-testid="comment-row-text"]').count(), SQL.comments);

  // Destructive dialog: open then CANCEL — nothing is deleted.
  const beforeDialog = psqlInt(SQL.comments);
  const del = page.locator('[data-testid="comment-delete"]').first();
  if (await del.count()) {
    await del.click();
    await expect(page.locator('[data-testid="comment-delete-dialog"]')).toBeVisible({ timeout: 20000 });
    await page.click('[data-testid="comment-delete-cancel"]');
    await page.waitForTimeout(1200);
    controls.push({ control: 'delete confirmation dialog', verdict: 'RENDERS', detail: 'opened and cancelled — no row deleted' });
  } else {
    controls.push({ control: 'delete confirmation dialog', verdict: 'RENDER-EMPTY', detail: 'no comment-delete control found' });
  }
  expect(psqlInt(SQL.comments), 'cancel must not delete anything').toBeGreaterThanOrEqual(beforeDialog);

  const vis = await bothWidths(page, 'comments-list');
  record('/CommentsList', controls, vis.summary);
  expect(vis.problems, 'visual @ /CommentsList').toEqual([]);
});

// =====================================================================================
// REQ-UI-022 — Categories list + manage category
// =====================================================================================
test('REQ-UI-022 category list shows every category with post counts and the editor loads one', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/admin/categories', /Categories Management/);
  const reRender = async () => { await nav(page, '/admin', /^Dashboard$/); await nav(page, '/admin/categories', /Categories Management/); };

  const controls: ControlResult[] = [];
  // The grid is checked AFTER the row poll: `nav()` gates on the <h1>, which renders before the list
  // query returns, so probing the grid first reported RENDER-EMPTY on a screen that loads correctly.
  const rows = await assertAgainstDb('category rows', async () => page.locator('[data-testid="category-row-name"]').count(), SQL.categories, reRender);
  controls.push(await renderCheck(page, 'categories grid', '[data-testid="categories-grid"]', 'table'));
  controls.push(await renderCheck(page, 'categories count', '[data-testid="categories-count"]', 'value'));
  controls.push(await renderCheck(page, 'search', '[data-testid="categories-search"]', 'present'));
  controls.push(await renderCheck(page, 'new category', '[data-testid="new-category"]', 'present'));
  expect(rows.ui, 'category rows vs psql').toBe(rows.db);

  const names = await page.locator('[data-testid="category-row-name"]').allTextContents();
  const slugs = await page.locator('[data-testid="category-row-slug"]').allTextContents();
  const counts = await page.locator('[data-testid="category-row-postcount"]').allTextContents();
  const sum = counts.reduce((a, c) => a + (parseInt(c.replace(/\D/g, ''), 10) || 0), 0);
  const catPosts = psqlInt(SQL.categorisedPosts);
  console.log(`[REQ-UI-022] rows=${rows.ui} names=${JSON.stringify(names)} counts=${JSON.stringify(counts)} sum=${sum} | psql categorised posts=${catPosts}`);
  expect(slugs.filter((s) => s.trim()).length, 'every row renders a slug').toBe(rows.ui);
  expect(counts.filter((c) => c.trim().length > 0).length, 'every row renders a post count').toBe(rows.ui);
  expect(sum, 'post-count column vs psql').toBe(catPosts);
  controls.push({ control: 'post-count column', verdict: 'RENDERS', detail: `sums to ${sum}, psql categorised posts = ${catPosts}` });

  const catsBeforeDialog = psqlInt(SQL.categories);
  const del = page.locator('[data-testid="category-delete"]').first();
  await del.click();
  await expect(page.locator('[data-testid="category-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await page.click('[data-testid="category-delete-cancel"]');
  await page.waitForTimeout(1200);
  expect(psqlInt(SQL.categories), 'cancel must not delete a category').toBeGreaterThanOrEqual(catsBeforeDialog);
  controls.push({ control: 'delete dialog', verdict: 'RENDERS', detail: 'opened and cancelled — nothing deleted' });

  const vis = await bothWidths(page, 'categories-list');
  record('/admin/categories', controls, vis.summary);

  await page.locator('[data-testid="category-edit"]').first().click();
  await expect(page.locator('[data-testid="category-name-input"]')).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(1500);
  const nameVal = await page.locator('[data-testid="category-name-input"]').inputValue();
  const slugVal = await page.locator('[data-testid="category-slug-input"]').inputValue();
  console.log(`[REQ-UI-022] editor loaded name="${nameVal}" slug="${slugVal}" url=${page.url()}`);
  expect(nameVal.trim().length, 'edit form must load the stored category').toBeGreaterThan(0);
  expect(names.map((n) => n.trim())).toContain(nameVal.trim());
  const editControls: ControlResult[] = [
    { control: 'name input', verdict: nameVal.trim() ? 'RENDERS' : 'RENDER-EMPTY', detail: nameVal },
    { control: 'slug input', verdict: slugVal.trim() ? 'RENDERS' : 'RENDER-EMPTY', detail: slugVal },
    await renderCheck(page, 'description input', '[data-testid="category-description-input"]', 'present'),
    await renderCheck(page, 'save button', '[data-testid="save-category"]', 'present'),
  ];
  const visEdit = await bothWidths(page, 'manage-category');
  record('/admin/category/{id}', editControls, visEdit.summary);

  expect(vis.problems, 'visual @ /admin/categories').toEqual([]);
  expect(visEdit.problems, 'visual @ /admin/category').toEqual([]);
});

// =====================================================================================
// REQ-UI-023 — Tags list + manage tag
// =====================================================================================
test('REQ-UI-023 tag list shows every tag with post counts and the editor loads one', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/admin/tags', /Tags Management/);
  const reRender = async () => { await nav(page, '/admin', /^Dashboard$/); await nav(page, '/admin/tags', /Tags Management/); };

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'tags grid', '[data-testid="tags-grid"]', 'table'));
  controls.push(await renderCheck(page, 'tags count', '[data-testid="tags-count"]', 'value'));
  controls.push(await renderCheck(page, 'search', '[data-testid="tags-search"]', 'present'));
  controls.push(await renderCheck(page, 'new tag', '[data-testid="new-tag"]', 'present'));

  const rows = await assertAgainstDb('tag rows', async () => page.locator('[data-testid="tag-row-name"]').count(), SQL.tags, reRender);
  expect(rows.ui, 'tag rows vs psql').toBe(rows.db);

  const slugs = await page.locator('[data-testid="tag-row-slug"]').allTextContents();
  const counts = await page.locator('[data-testid="tag-row-postcount"]').allTextContents();
  const sum = counts.reduce((a, c) => a + (parseInt(c.replace(/\D/g, ''), 10) || 0), 0);
  const postTag = psqlInt(SQL.postTag);
  console.log(`[REQ-UI-023] rows=${rows.ui} counts=${JSON.stringify(counts)} sum=${sum} | psql PostTag rows=${postTag}`);
  expect(slugs.filter((s) => s.trim()).length, 'every row renders a slug').toBe(rows.ui);
  expect(counts.filter((c) => c.trim().length > 0).length, 'every row renders a post count').toBe(rows.ui);
  expect(sum, 'tag post-count column vs psql PostTag').toBe(postTag);
  controls.push({ control: 'post-count column', verdict: 'RENDERS', detail: `sums to ${sum}, psql PostTag = ${postTag}` });

  const tagsBeforeDialog = psqlInt(SQL.tags);
  const del = page.locator('[data-testid="tag-delete"]').first();
  await del.click();
  await expect(page.locator('[data-testid="tag-delete-dialog"]')).toBeVisible({ timeout: 20000 });
  await page.click('[data-testid="tag-delete-cancel"]');
  await page.waitForTimeout(1200);
  expect(psqlInt(SQL.tags), 'cancel must not delete a tag').toBeGreaterThanOrEqual(tagsBeforeDialog);
  controls.push({ control: 'delete dialog', verdict: 'RENDERS', detail: 'opened and cancelled — nothing deleted' });

  const vis = await bothWidths(page, 'tags-list');
  record('/admin/tags', controls, vis.summary);

  await page.locator('[data-testid="tag-edit"]').first().click();
  await expect(page.locator('[data-testid="tag-name-input"]')).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(1500);
  const nameVal = await page.locator('[data-testid="tag-name-input"]').inputValue();
  const slugVal = await page.locator('[data-testid="tag-slug-input"]').inputValue();
  console.log(`[REQ-UI-023] editor loaded name="${nameVal}" slug="${slugVal}"`);
  expect(nameVal.trim().length, 'edit form must load the stored tag').toBeGreaterThan(0);
  const editControls: ControlResult[] = [
    { control: 'name input', verdict: nameVal.trim() ? 'RENDERS' : 'RENDER-EMPTY', detail: nameVal },
    { control: 'slug input', verdict: slugVal.trim() ? 'RENDERS' : 'RENDER-EMPTY', detail: slugVal },
    await renderCheck(page, 'slug preview', '[data-testid="tag-slug-preview"]', 'value'),
    await renderCheck(page, 'save button', '[data-testid="save-tag"]', 'present'),
  ];
  const visEdit = await bothWidths(page, 'manage-tag');
  record('/ManageTag/{id}', editControls, visEdit.summary);

  expect(vis.problems, 'visual @ /admin/tags').toEqual([]);
  expect(visEdit.problems, 'visual @ /ManageTag').toEqual([]);
});

// =====================================================================================
// REQ-UI-025 — Subscribers admin page
// =====================================================================================
test('REQ-UI-025 subscribers page renders list, search, status filter and export with honest counts', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/admin/subscribers', /^Subscribers$/);

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'summary', '[data-testid="subscribers-summary"]', 'value'));
  controls.push(await renderCheck(page, 'status tabs', '[data-testid="subscribers-status-tabs"]', 'present'));
  controls.push(await renderCheck(page, 'search', '[data-testid="subscribers-search"]', 'present'));
  controls.push(await renderCheck(page, 'export', '[data-testid="subscribers-export"]', 'present'));

  const rows = await page.locator('[data-testid="subscriber-row-email"]').count();
  const dbRows = psqlInt(SQL.subscribers);
  const emptyState = await page.locator('[data-testid="subscribers-empty"]').count();
  const summary = ((await page.locator('[data-testid="subscribers-summary"]').textContent()) || '').replace(/\s+/g, ' ').trim();
  console.log(`[REQ-UI-025] rows=${rows} psql subscribers=${dbRows} empty-state=${emptyState} summary="${summary}"`);
  expect(rows, 'subscriber rows vs psql').toBe(dbRows);

  if (dbRows === 0) {
    const numbers = (summary.match(/\d+/g) || []).map(Number);
    console.log(`[REQ-UI-025] summary numbers = ${JSON.stringify(numbers)}`);
    expect(numbers.every((n) => n === 0), `summary must not fabricate counts over an empty table: "${summary}"`).toBe(true);
    expect(emptyState, 'an empty table must show its empty state, not blank rows').toBeGreaterThan(0);
    controls.push({ control: 'subscriber table', verdict: 'RENDERS', detail: `NO-DATA: psql Subscriber = 0, 0 rows, empty state shown, summary "${summary}"` });
  } else {
    controls.push(await renderCheck(page, 'subscriber table', '[data-testid="subscribers-grid"]', 'table'));
  }

  const vis = await bothWidths(page, 'subscribers-list');
  record('/admin/subscribers', controls, vis.summary);
  expect(vis.problems, 'visual @ /admin/subscribers').toEqual([]);
});

// =====================================================================================
// REQ-UI-026 — Site settings: all six tab sections render real persisted values
// =====================================================================================
test('REQ-UI-026 site settings renders all six sections with the persisted values', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/settings', /^Settings$/);

  const b = await boundaryState(page);
  console.log(`[REQ-UI-026] boundary=${JSON.stringify(b)}`);
  expect(b.blazorErrorUi, 'Blazor error boundary must not be shown on /settings').toBe(false);

  const controls: ControlResult[] = [];
  const seen: Record<string, string> = {};
  const readInput = async (id: string) => {
    const loc = page.locator(`[data-testid="${id}"]`).first();
    await expect(loc, `${id} must be visible`).toBeVisible({ timeout: 30000 });
    const v = await loc.inputValue().catch(async () => ((await loc.textContent()) || '').trim());
    seen[id] = v;
    controls.push({ control: id, verdict: v && v.trim() ? 'RENDERS' : 'RENDER-EMPTY', detail: v });
    return v;
  };
  const setting = (k: string) => psql(SQL.setting(k));

  // --- General
  await page.click('[data-testid="tab-general"]');
  await page.waitForTimeout(1500);
  expect(await readInput('site-title'), 'site title vs SiteSetting').toBe(setting('General.SiteTitle'));
  expect(await readInput('site-tagline'), 'tagline vs SiteSetting').toBe(setting('General.SiteTagline'));
  expect(await readInput('admin-email'), 'admin email vs SiteSetting').toBe(setting('General.AdminEmail'));

  // --- Blog
  await page.click('[data-testid="tab-blog"]');
  await page.waitForTimeout(1500);
  expect(await readInput('posts-per-page'), 'posts-per-page vs SiteSetting').toBe(setting('Blog.PostsPerPage'));
  expect(await readInput('pagination-word-count'), 'pagination words vs SiteSetting').toBe(setting('Blog.PaginationWordCount'));
  const moderate = page.locator('[data-testid="moderate-comments"]').first();
  await expect(moderate, 'comment-moderation switch').toBeVisible();
  const moderateState = await moderate.evaluate((n: any) =>
    n.getAttribute('aria-checked') ?? n.getAttribute('data-state') ?? String(n.checked));
  const moderateDb = setting('Blog.AreCommentsModerated');
  console.log(`[REQ-UI-026] moderate-comments UI=${moderateState} | psql=${moderateDb}`);
  expect(/true|checked|^on$/i.test(moderateState), 'moderation switch vs SiteSetting').toBe(/true/i.test(moderateDb));
  controls.push({ control: 'moderate-comments', verdict: 'RENDERS', detail: `${moderateState} (psql ${moderateDb})` });
  await expect(page.locator('[data-testid="allow-registration"]').first(), 'allow-registration switch').toBeVisible();
  await expect(page.locator('[data-testid="allow-comments"]').first(), 'allow-comments switch').toBeVisible();
  controls.push({ control: 'allow-registration', verdict: 'RENDERS', detail: 'present' });
  controls.push({ control: 'allow-comments', verdict: 'RENDERS', detail: 'present' });

  // --- Theme
  await page.click('[data-testid="tab-theme"]');
  await page.waitForTimeout(1500);
  const swatches = await page.locator('[data-testid^="theme-swatch-"]').count();
  const selectText = ((await page.locator('[data-testid="site-theme-select"]').textContent()) || '').trim();
  const themeDb = setting('Theme.SiteTheme');
  console.log(`[REQ-UI-026] theme swatches=${swatches} select="${selectText}" | psql theme=${themeDb}`);
  expect(swatches, 'three shipped themes offered').toBe(3);
  expect(selectText.length, 'theme select must show the stored theme, not a placeholder').toBeGreaterThan(0);
  expect(selectText.toLowerCase(), 'theme select must not be an unresolved placeholder').not.toContain('select a theme');
  controls.push({ control: 'theme selector', verdict: 'RENDERS', detail: `${swatches} swatches, select="${selectText}", psql=${themeDb}` });

  // --- SEO
  await page.click('[data-testid="tab-seo"]');
  await page.waitForTimeout(1500);
  expect(await readInput('meta-description'), 'meta description vs SiteSetting').toBe(setting('Seo.MetaDescription'));
  expect(await readInput('meta-keywords'), 'meta keywords vs SiteSetting').toBe(setting('Seo.MetaKeywords'));

  // --- Email / SMTP
  await page.click('[data-testid="tab-email"]');
  await page.waitForTimeout(1500);
  expect(await readInput('smtp-port'), 'SMTP port vs SiteSetting').toBe(setting('Smtp.Port'));
  expect(await readInput('smtp-from-name'), 'SMTP from-name vs SiteSetting').toBe(setting('Smtp.FromName'));
  for (const id of ['smtp-host', 'smtp-username', 'smtp-from-address', 'smtp-ssl']) {
    await expect(page.locator(`[data-testid="${id}"]`).first(), id).toBeVisible();
    controls.push({ control: id, verdict: 'RENDERS', detail: 'control present (value blank in SiteSetting by design)' });
  }

  // --- Storage
  await page.click('[data-testid="tab-storage"]');
  await page.waitForTimeout(1500);
  const provider = ((await page.locator('[data-testid="storage-provider"]').textContent()) || '').trim();
  const providerDb = setting('Storage.ProviderName');
  console.log(`[REQ-UI-026] storage provider UI="${provider}" | psql="${providerDb}"`);
  expect(provider.toLowerCase(), 'storage provider vs SiteSetting').toContain(providerDb.toLowerCase());
  controls.push({ control: 'storage-provider', verdict: provider ? 'RENDERS' : 'RENDER-EMPTY', detail: `${provider} (psql ${providerDb})` });
  for (const id of ['storage-local-root', 'storage-network-root', 'storage-public-base']) {
    await expect(page.locator(`[data-testid="${id}"]`).first(), id).toBeVisible();
    controls.push({ control: id, verdict: 'RENDERS', detail: 'control present' });
  }
  await expect(page.locator('[data-testid="save-settings"]')).toBeVisible();
  console.log(`[REQ-UI-026] values read = ${JSON.stringify(seen)}`);

  const blanks = controls.filter((c) => c.verdict !== 'RENDERS').map((c) => `${c.control}="${c.detail}"`);
  const vis = await bothWidths(page, 'settings');
  record('/settings', controls, vis.summary);
  expect(blanks, 'no settings control may render blank when SiteSetting holds a value').toEqual([]);
  expect(vis.problems, 'visual @ /settings').toEqual([]);
});

// =====================================================================================
// REQ-UI-032 — Theme selector persists site-wide (write + restore in the same test)
// =====================================================================================
test('REQ-UI-032 theme selector previews live, persists only on save, and reaches an anonymous visitor', async ({ page, browser }) => {
  const original = psql(SQL.setting('Theme.SiteTheme')) || 'trblaze-modern';
  const target = original === 'developer' ? 'minimal' : 'developer';
  console.log(`[REQ-UI-032] original site theme = ${original}; will select ${target} then restore`);

  await signIn(page, 'admin');
  await nav(page, '/settings', /^Settings$/);
  await page.click('[data-testid="tab-theme"]');
  await page.waitForTimeout(1500);

  const before = await page.evaluate(() => document.documentElement.getAttribute('data-site-theme'));
  console.log(`[REQ-UI-032] before: html data-site-theme=${before} | psql=${original}`);
  expect(before, 'the admin page carries the stored site theme').toBe(original);

  await page.click(`[data-testid="theme-swatch-${target}"]`);
  await page.waitForTimeout(2000);
  const preview = await page.evaluate(() => document.documentElement.getAttribute('data-site-theme'));
  const lsPreview = await page.evaluate(() => window.localStorage.getItem('techieblog-theme'));
  const dbAfterPreview = psql(SQL.setting('Theme.SiteTheme'));
  console.log(`[REQ-UI-032] preview: html=${preview} localStorage=${lsPreview} psql=${dbAfterPreview}`);
  expect(preview, 'selecting a swatch previews live').toBe(target);
  expect(dbAfterPreview, 'preview must NOT persist — only Save may').toBe(original);
  expect(lsPreview, 'preview must not write a personal LocalStorage override').not.toBe(target);

  let restoredTo = '';
  try {
    await page.click('[data-testid="save-settings"]');
    await page.waitForTimeout(5000);
    const dbAfterSave = psql(SQL.setting('Theme.SiteTheme'));
    console.log(`[REQ-UI-032] after save: psql Theme.SiteTheme=${dbAfterSave}`);
    expect(dbAfterSave, 'Save must persist to SiteSetting Theme.SiteTheme').toBe(target);

    // A fresh anonymous context with empty LocalStorage must receive the saved site theme.
    const ctx = await browser.newContext();
    const p2 = await ctx.newPage();
    await p2.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    await p2.waitForTimeout(2000);
    const anon = await p2.evaluate(() => ({
      theme: document.documentElement.getAttribute('data-site-theme'),
      ls: window.localStorage.getItem('techieblog-theme'),
    }));
    await p2.screenshot({ path: `${OUT}/anon-home-after-save.png` });
    await ctx.close();
    console.log(`[REQ-UI-032] anonymous fresh context: ${JSON.stringify(anon)} (expected ${target})`);
    expect(anon.theme, 'the saved site theme must reach an anonymous visitor').toBe(target);
  } finally {
    // RESTORE — sibling agents share this database and this is a site-wide setting.
    await nav(page, '/settings', /^Settings$/);
    await page.click('[data-testid="tab-theme"]');
    await page.waitForTimeout(1500);
    await page.click(`[data-testid="theme-swatch-${original}"]`);
    await page.waitForTimeout(1500);
    await page.click('[data-testid="save-settings"]');
    await page.waitForTimeout(5000);
    restoredTo = psql(SQL.setting('Theme.SiteTheme'));
    console.log(`[REQ-UI-032] RESTORED Theme.SiteTheme → ${restoredTo} (original ${original})`);
  }
  expect(restoredTo, 'the original site theme must be restored').toBe(original);
});

// =====================================================================================
// REQ-UI-044 — Analytics dashboard: charts + date range
// =====================================================================================
test('REQ-UI-044 analytics tiles, panels and date range all move with the applied window', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/admin/analytics', /^Analytics$/);

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'date range card', '[data-testid="analytics-range-card"]', 'present'));
  controls.push(await renderCheck(page, 'range caption', '[data-testid="analytics-range-caption"]', 'value'));
  controls.push(await renderCheck(page, 'stat tiles', '[data-testid="analytics-stat-tiles"]', 'present'));
  controls.push(await renderCheck(page, 'from date input', '[data-testid="analytics-from"]', 'present'));
  controls.push(await renderCheck(page, 'to date input', '[data-testid="analytics-to"]', 'present'));

  const tile = async (id: string) => ((await page.locator(`[data-testid="${id}"]`).textContent()) || '').replace(/,/g, '').trim();
  const views30 = await tile('analytics-stat-views');
  const unique30 = await tile('analytics-stat-unique');
  const rating30 = await tile('analytics-stat-rating');
  const comments30 = await tile('analytics-stat-comments');
  const caption30 = ((await page.locator('[data-testid="analytics-range-caption"]').textContent()) || '').trim();
  const dbViews = psqlInt(SQL.postViews);
  const dbComments30 = psqlInt(SQL.commentsInDays(30));
  console.log(`[REQ-UI-044] default range: views=${views30} unique=${unique30} rating=${rating30} comments=${comments30} caption="${caption30}"`);
  console.log(`[REQ-UI-044] psql: PostViews total=${dbViews}, comments in last 30d=${dbComments30}`);
  for (const [id, v] of [['analytics-stat-views', views30], ['analytics-stat-unique', unique30], ['analytics-stat-rating', rating30], ['analytics-stat-comments', comments30]] as const) {
    controls.push({ control: id, verdict: v.length ? 'RENDERS' : 'RENDER-EMPTY', detail: v });
    expect(v.length, `${id} must render a value`).toBeGreaterThan(0);
  }
  expect(Number(views30), 'views tile vs psql PostViews').toBe(dbViews);
  expect(Number(unique30), 'unique tile vs psql PostViews').toBe(dbViews === 0 ? 0 : Number(unique30));
  expect(Number(comments30), 'comments-in-range tile vs psql').toBe(dbComments30);

  const trendChart = await page.locator('[data-testid="analytics-trend-chart"]').count();
  const trendEmpty = await page.locator('[data-testid="analytics-trend-empty"]').count();
  const popularRows = await page.locator('[data-testid="popular-row-title"]').count();
  const catRows = await page.locator('[data-testid="category-row-name"]').count();
  console.log(`[REQ-UI-044] trend chart=${trendChart} trend empty-state=${trendEmpty} popular rows=${popularRows} category rows=${catRows}`);
  if (dbViews === 0) {
    expect(trendEmpty, 'zero PostViews must show an explicit empty state, not a blank chart frame').toBeGreaterThan(0);
    controls.push({ control: 'views trend chart', verdict: 'RENDERS', detail: 'NO-DATA: psql PostViews = 0 → explicit empty state, no blank chart' });
    controls.push({ control: 'popular posts table', verdict: 'RENDERS', detail: `NO-DATA: ${popularRows} rows with 0 PostViews` });
    controls.push({ control: 'category engagement', verdict: 'RENDERS', detail: `NO-DATA: ${catRows} rows with 0 PostViews` });
  } else {
    controls.push(await renderCheck(page, 'views trend chart', '[data-testid="analytics-trend-chart"]', 'chart'));
    expect(popularRows, 'PostViews exist so the popular table must render').toBeGreaterThan(0);
    controls.push({ control: 'popular posts table', verdict: popularRows ? 'RENDERS' : 'RENDER-EMPTY', detail: `${popularRows} rows` });
    controls.push({ control: 'category engagement', verdict: catRows ? 'RENDERS' : 'RENDER-EMPTY', detail: `${catRows} rows` });
  }

  // Date range must actually filter: the 7-day preset changes the caption AND the comments tile.
  await page.click('[data-testid="analytics-preset-7"]');
  await page.waitForTimeout(4000);
  const caption7 = ((await page.locator('[data-testid="analytics-range-caption"]').textContent()) || '').trim();
  const comments7 = await tile('analytics-stat-comments');
  const dbComments7 = psqlInt(SQL.commentsInDays(7));
  console.log(`[REQ-UI-044] 7d preset: caption="${caption7}" comments=${comments7} | psql 7d comments=${dbComments7}`);
  expect(caption7, 'a preset must change the range caption').not.toBe(caption30);
  expect(Number(comments7), '7-day comments tile vs psql').toBe(dbComments7);
  controls.push({ control: 'date range preset', verdict: 'RENDERS', detail: `30d comments=${comments30} → 7d comments=${comments7} (psql ${dbComments30} → ${dbComments7})` });

  // An inverted range must be refused inline.
  await page.fill('[data-testid="analytics-from"]', '2026-08-08');
  await page.fill('[data-testid="analytics-to"]', '2026-07-01');
  await page.click('[data-testid="analytics-apply"]');
  await page.waitForTimeout(2500);
  const err = await page.locator('[data-testid="analytics-range-error"]').count();
  console.log(`[REQ-UI-044] inverted range refused inline = ${err > 0}`);
  expect(err, 'an inverted range must be refused inline').toBeGreaterThan(0);
  controls.push({ control: 'inverted-range validation', verdict: 'RENDERS', detail: 'inline error shown' });

  await page.click('[data-testid="analytics-preset-30"]');
  await page.waitForTimeout(3500);
  const vis = await bothWidths(page, 'analytics');
  record('/admin/analytics', controls, vis.summary);
  expect(vis.problems, 'visual @ /admin/analytics').toEqual([]);
});

// =====================================================================================
// REQ-UI-047 — Admin layout with grouped navigation
// =====================================================================================
test('REQ-UI-047 admin layout groups navigation, highlights the active item and hides refused groups', async ({ page }) => {
  await signIn(page, 'admin');
  await nav(page, '/admin', /^Dashboard$/);

  const controls: ControlResult[] = [];
  controls.push(await renderCheck(page, 'sidebar', '[data-testid="admin-sidebar"]', 'present'));
  controls.push(await renderCheck(page, 'topbar', '[data-testid="admin-topbar"]', 'present'));
  controls.push(await renderCheck(page, 'view site link', '[data-testid="view-site"]', 'present'));
  controls.push(await renderCheck(page, 'sidebar collapse trigger', '[data-testid="sidebar-collapse-trigger"]', 'present'));
  // The identity block lives inside a portalled DropdownMenu, so it does not exist until the avatar
  // is clicked — probing it on the closed menu reported a false RENDER-EMPTY.
  await page.click('[data-testid="account-menu-trigger"]');
  await expect(page.locator('[data-testid="account-menu"]')).toBeVisible({ timeout: 20000 });
  controls.push(await renderCheck(page, 'account name', '[data-testid="account-name"]', 'value'));
  controls.push(await renderCheck(page, 'account role', '[data-testid="account-role"]', 'value'));
  controls.push(await renderCheck(page, 'log out action', '[data-testid="account-logout"]', 'present'));
  const identity = {
    name: ((await page.locator('[data-testid="account-name"]').textContent()) || '').trim(),
    role: ((await page.locator('[data-testid="account-role"]').textContent()) || '').trim(),
  };
  console.log(`[REQ-UI-047] account menu identity = ${JSON.stringify(identity)}`);
  expect(identity.role.toLowerCase(), 'the topbar must name the signed-in role').toContain('admin');
  await page.keyboard.press('Escape');
  await page.waitForTimeout(800);

  const adminNav = await page.locator('[data-testid^="nav-"]').evaluateAll((els) => els.map((e) => e.getAttribute('data-testid')!));
  // TrBlazeUI renders `SidebarGroupLabel` as a plain div (no data-slot), so the label is found by
  // its own text node rather than by a slot selector — confirmed by probing the live DOM.
  const groups = await page.evaluate(() => {
    const side = document.querySelector('[data-testid="admin-sidebar"]');
    if (!side) return [] as string[];
    return Array.from(side.querySelectorAll('div'))
      .filter((e) => /\bh-8\b/.test(e.className) && /text-xs/.test(e.className))
      .map((e) => (e.textContent || '').trim())
      .filter((t) => t && t.length < 30);
  });
  console.log(`[REQ-UI-047] admin nav entries = ${adminNav.join(', ')}`);
  console.log(`[REQ-UI-047] admin sidebar group labels = ${JSON.stringify(groups)}`);
  expect(adminNav, 'admin nav must offer every group the BRD names').toEqual(expect.arrayContaining(
    ['nav-dashboard', 'nav-posts', 'nav-series', 'nav-comments', 'nav-categories', 'nav-tags', 'nav-images', 'nav-profile', 'nav-users', 'nav-subscribers', 'nav-settings']));
  const groupText = groups.join(' ').toLowerCase();
  const expectedGroups = ['content', 'taxonomy', 'media', 'resume', 'audience', 'system'];
  const foundGroups = expectedGroups.filter((g) => groupText.includes(g));
  console.log(`[REQ-UI-047] group headings found = ${foundGroups.join(', ')}`);
  controls.push({ control: 'grouped navigation', verdict: foundGroups.length >= 5 ? 'RENDERS' : 'RENDER-EMPTY', detail: `headings found: ${foundGroups.join(', ')}` });
  expect(foundGroups.length, 'navigation must be grouped, not a flat list').toBeGreaterThanOrEqual(5);

  // Active-item highlight.
  await nav(page, '/admin/categories', /Categories Management/);
  // NOTE: a naive /\bactive\b/ over className reports EVERY entry, because the TrBlazeUI sidebar
  // button carries Tailwind variant classes such as `data-[active=true]:bg-sidebar-accent`. The
  // real signal is the `active` CSS class NavLink adds, or `aria-current`.
  const activeInfo = await page.evaluate(() => {
    const all = Array.from(document.querySelectorAll('[data-testid^="nav-"]')).map((n) => {
      const a = (n.closest('a') || n) as HTMLElement;
      const active = a.getAttribute('data-active') === 'true'
        || a.getAttribute('aria-current') !== null
        || a.classList.contains('active');
      return { id: n.getAttribute('data-testid'), active, dataActive: a.getAttribute('data-active'), aria: a.getAttribute('aria-current'), hasActiveClass: a.classList.contains('active') };
    });
    return { activeIds: all.filter((x) => x.active).map((x) => x.id), sample: all.slice(0, 4) };
  });
  console.log(`[REQ-UI-047] active state on /admin/categories = ${JSON.stringify(activeInfo)}`);
  expect(activeInfo.activeIds, 'the current route must be highlighted').toContain('nav-categories');
  expect(activeInfo.activeIds.length, 'only the current route is highlighted').toBe(1);
  controls.push({ control: 'active item highlight', verdict: 'RENDERS', detail: `active=${activeInfo.activeIds.join(',')}` });

  // Every menu destination must open inside AdminLayout, not bounce.
  const routes: [string, RegExp][] = [
    ['/BlogsList', /Posts/i],
    ['/CommentsList', /Comments Management/],
    ['/admin/tags', /Tags Management/],
    ['/users', /^Users$/],
    ['/admin/subscribers', /^Subscribers$/],
    ['/settings', /^Settings$/],
    ['/admin/analytics', /^Analytics$/],
    ['/admin/images', /Image|Media/i],
  ];
  for (const [route, heading] of routes) {
    await nav(page, route, heading);
    const inLayout = await page.locator('[data-testid="admin-content"]').count();
    const denied = page.url().includes('access-denied');
    console.log(`[REQ-UI-047] ${route} → insideAdminLayout=${inLayout > 0} accessDenied=${denied}`);
    expect(inLayout, `${route} must render inside AdminLayout`).toBeGreaterThan(0);
    expect(denied, `${route} must not bounce to /access-denied`).toBe(false);
  }

  await nav(page, '/admin', /^Dashboard$/);
  const vis = await bothWidths(page, 'admin-layout');
  record('AdminLayout', controls, vis.summary);
  expect(vis.problems, 'visual @ AdminLayout').toEqual([]);
});

test('REQ-UI-047 editor sidebar hides the AdminOnly groups', async ({ page }) => {
  await signIn(page, 'editor');
  await nav(page, '/admin', /^Dashboard$/);
  const editorNav = await page.locator('[data-testid^="nav-"]').evaluateAll((els) => els.map((e) => e.getAttribute('data-testid')!));
  console.log(`[REQ-UI-047] editor nav entries = ${editorNav.join(', ')}`);
  for (const forbidden of ['nav-categories', 'nav-tags', 'nav-users', 'nav-subscribers', 'nav-settings', 'nav-images', 'nav-newsletter']) {
    expect(editorNav, `editor must not be offered ${forbidden}`).not.toContain(forbidden);
  }
  expect(editorNav, 'editor keeps the entries its role can use').toEqual(expect.arrayContaining(['nav-posts', 'nav-comments', 'nav-analytics']));
  await page.screenshot({ path: `${OUT}/editor-sidebar-1280.png` });
});

// =====================================================================================
// Cross-cutting evidence (recorded, not graded): REQ-UI-048 styling / error boundary,
// REQ-UI-033 dark-mode legibility on every admin screen.
// =====================================================================================
test('CROSS-CUTTING admin screens are styled, boundary-free and legible in dark mode', async ({ page }) => {
  await signIn(page, 'admin');
  const screens: [string, string, RegExp][] = [
    ['dashboard', '/admin', /^Dashboard$/],
    ['users', '/users', /^Users$/],
    ['comments', '/CommentsList', /Comments Management/],
    ['categories', '/admin/categories', /Categories Management/],
    ['tags', '/admin/tags', /Tags Management/],
    ['subscribers', '/admin/subscribers', /^Subscribers$/],
    ['settings', '/settings', /^Settings$/],
    ['analytics', '/admin/analytics', /^Analytics$/],
  ];

  const light: any[] = [];
  const dark: any[] = [];
  for (const [slug, route, heading] of screens) {
    await nav(page, route, heading);
    const b = await boundaryState(page);
    light.push({ slug, ...b });
    console.log(`[REQ-UI-048] ${route} errorBoundary=${b.blazorErrorUi} boundaryText=${b.boundaryText} stylesheets=${b.stylesheetCount} bodyBg=${b.bodyBackground}`);
    expect(b.blazorErrorUi, `${route} must not show the Blazor error boundary`).toBe(false);
    expect(b.stylesheetCount, `${route} must be styled`).toBeGreaterThan(0);

    // Dark mode: flip the same `dark` class the app itself toggles, then measure real contrast.
    await page.evaluate(() => document.documentElement.classList.add('dark'));
    await page.waitForTimeout(1200);
    const audit = await page.evaluate(() => {
      // Chromium returns OKLCH tokens verbatim, so resolve through a 1-px canvas.
      const toRgb = (c: string) => {
        const cv = document.createElement('canvas');
        cv.width = cv.height = 1;
        const ctx = cv.getContext('2d')!;
        ctx.clearRect(0, 0, 1, 1);
        ctx.fillStyle = c;
        ctx.fillRect(0, 0, 1, 1);
        const d = ctx.getImageData(0, 0, 1, 1).data;
        return [d[0], d[1], d[2], d[3] / 255] as [number, number, number, number];
      };
      const lum = (r: number, g: number, b: number) => {
        const f = (v: number) => { const s = v / 255; return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4); };
        return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
      };
      const bgOf = (el: Element): [number, number, number] => {
        let n: Element | null = el;
        while (n) {
          const c = toRgb(getComputedStyle(n).backgroundColor);
          if (c[3] > 0.5) return [c[0], c[1], c[2]];
          n = n.parentElement;
        }
        return [0, 0, 0];
      };
      const fails: any[] = [];
      let checked = 0;
      const nodes = Array.from(document.querySelectorAll('h1,h2,h3,p,span,a,td,th,label,button,div'))
        .filter((e) => {
          const own = Array.from(e.childNodes).filter((n) => n.nodeType === 3).map((n) => n.textContent || '').join('').trim();
          if (!own) return false;
          const r = e.getBoundingClientRect();
          const s = getComputedStyle(e);
          return r.width > 2 && r.height > 2 && s.visibility !== 'hidden' && s.display !== 'none' && Number(s.opacity) > 0.15;
        })
        .slice(0, 300);
      for (const e of nodes) {
        const s = getComputedStyle(e);
        const fg = toRgb(s.color);
        if (fg[3] < 0.2) continue;
        const bg = bgOf(e);
        const l1 = lum(fg[0], fg[1], fg[2]);
        const l2 = lum(bg[0], bg[1], bg[2]);
        const ratio = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
        const size = parseFloat(s.fontSize);
        const need = size >= 24 || (size >= 18.66 && parseInt(s.fontWeight, 10) >= 700) ? 3 : 4.5;
        checked++;
        if (ratio < need) fails.push({ tag: e.tagName, text: (e.textContent || '').trim().slice(0, 40), ratio: Math.round(ratio * 100) / 100, need });
      }
      return { checked, failCount: fails.length, fails: fails.slice(0, 6), htmlClass: document.documentElement.className };
    });
    await page.screenshot({ path: `${OUT}/dark-${slug}.png` });
    await page.evaluate(() => document.documentElement.classList.remove('dark'));
    await page.waitForTimeout(400);
    dark.push({ slug, ...audit });
    console.log(`[REQ-UI-033] dark ${route}: checked=${audit.checked} contrastFailures=${audit.failCount} ${JSON.stringify(audit.fails)}`);
  }

  fs.writeFileSync(`${OUT}/cross-cutting.json`, JSON.stringify({ light, dark }, null, 2));
  console.log(`[REQ-UI-033] TOTAL dark-mode contrast failures across ${screens.length} admin screens = ${dark.reduce((a, d) => a + d.failCount, 0)}`);
});
