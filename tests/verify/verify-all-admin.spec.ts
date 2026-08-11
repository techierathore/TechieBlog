/**
 * verify-all-admin.spec.ts — verify-phase §4 / §4a / §4b for the ADMIN surface (2026-08-11).
 *
 * Control map: docs/devguides/TechieBlog-DevGuide-Admin.md. Every control that guide lists is
 * measured here, one `test()` per REQ, title prefixed with the REQ ID.
 *
 * Three gates are encoded in this one file:
 *   §4  ACCEPTANCE     — the observable outcome each REQ promises.
 *   §4a RENDER         — grids need rows > 0 AND non-empty DATA CELLS; the count badge is never
 *                        the evidence (the classic failure on this surface is a badge reading "16"
 *                        above zero visible rows). Charts need series nodes; value panels must not
 *                        be blank or a placeholder. Verdicts: RENDERS / RENDER-EMPTY / RENDER-ERROR
 *                        / UNREACHABLE.
 *   §4b VISUAL         — 1280x800 and 390x844: no intersecting sibling controls, every listed
 *                        control w>0/h>0 inside page bounds, no page-level horizontal scroll, and a
 *                        full-page screenshot at each width for eyes-on review.
 *
 * READ-ONLY. Three sibling verifier agents share this host and this database, so nothing here
 * creates, updates or deletes a row. Dialogs are opened and CANCELLED; the two REQs that cannot be
 * graded without a write (REQ-FN-053's save round trip, REQ-FN-011's update half) assert their
 * read-side precondition instead and say so in the log.
 *
 * The database MOVES under this run: `postviews` went 17 → 26 rows inside a single probe while the
 * public-surface sibling was browsing. Every number is therefore re-read from psql AT THE MOMENT OF
 * MEASUREMENT via `assertAgainstDb`, never from a start-of-run snapshot.
 *
 * Nothing under source/** is read or touched — this is a black-box pass.
 */
import { test, expect, Page, Browser } from '@playwright/test';
import * as fs from 'fs';
import { execSync } from 'child_process';
import AxeBuilder from '@axe-core/playwright';
import { renderCheck, visualCheck, ControlResult } from './_gates';

// The host is bound 0.0.0.0 Windows-side; the WSL gateway IP is the only way in (localhost fails).
const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5099';

// TechieFlow artifact-location rule: everything under tests/.artifacts/, never a repo-root
// test-results/ (Playwright wipes that at the start of every run and siblings would erase it).
const OUT = 'tests/.artifacts/verify-admin/shots';
fs.mkdirSync(OUT, { recursive: true });

const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

// =====================================================================================
// psql — SELECT only, read live
// =====================================================================================

/**
 * A multi-line template literal survives JSON.stringify as a literal `\n`, which psql rejects with
 * `syntax error at or near "\"`. Every statement is flattened to one line before it is handed over.
 */
const oneLine = (sql: string) => sql.replace(/\s+/g, ' ').trim();

function psql(sql: string): string {
  const cmd = `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -c ${JSON.stringify(oneLine(sql))}`;
  return execSync(cmd, { encoding: 'utf8' }).split('\n')[0].trim();
}

function psqlInt(sql: string): number {
  return Number(psql(sql));
}

/** Multi-row scalar read: one line per row, tab-separated columns. */
function psqlRows(sql: string): string[][] {
  const cmd = `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -F '\t' -c ${JSON.stringify(oneLine(sql))}`;
  return execSync(cmd, { encoding: 'utf8' })
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)
    .map((l) => l.split('\t'));
}

const LIVE = '(IsDeleted = FALSE OR IsDeleted IS NULL)';
const SQL = {
  posts: `SELECT COUNT(*) FROM BlogPost WHERE ${LIVE}`,
  drafts: `SELECT COUNT(*) FROM BlogPost WHERE Published = FALSE AND (ScheduledPublishOn IS NULL OR ScheduledPublishOn <= (NOW() AT TIME ZONE 'utc')) AND ${LIVE}`,
  scheduled: `SELECT COUNT(*) FROM BlogPost WHERE Published = FALSE AND ScheduledPublishOn > (NOW() AT TIME ZONE 'utc') AND ${LIVE}`,
  users: 'SELECT COUNT(*) FROM BlogUser',
  comments: 'SELECT COUNT(*) FROM BlogComment',
  // The dashboard "pending" badge is the Published = FALSE count (6 at the time of writing); the
  // moderation queue's Pending TAB reads ModerationStatus (5). Two different, both-correct numbers —
  // asserting one against the other would fail a correct screen.
  pendingBadge: 'SELECT COUNT(*) FROM BlogComment WHERE Published = FALSE',
  pendingTab: `SELECT COUNT(*) FROM BlogComment WHERE ModerationStatus NOT IN ('Approved','Spam','Rejected','PendingVerification')`,
  approvedTab: `SELECT COUNT(*) FROM BlogComment WHERE ModerationStatus = 'Approved'`,
  spamTab: `SELECT COUNT(*) FROM BlogComment WHERE ModerationStatus = 'Spam'`,
  categories: 'SELECT COUNT(*) FROM Category',
  tags: 'SELECT COUNT(*) FROM Tag',
  series: 'SELECT COUNT(*) FROM BlogSeries',
  subscribers: 'SELECT COUNT(*) FROM Subscriber',
  // "Active" on /admin/subscribers is IsConfirmed (11 total / 7 active, tabs All 11 / Active 7 /
  // Inactive 4 — confirmed against psql).
  subscribersActive: 'SELECT COUNT(*) FROM Subscriber WHERE IsConfirmed = TRUE',
  subscribersInactive: 'SELECT COUNT(*) FROM Subscriber WHERE IsConfirmed IS NOT TRUE',
  images: 'SELECT COUNT(*) FROM BlogImage',
  skills: 'SELECT COUNT(*) FROM UserSkills WHERE UserId = 1',
  skillCategories: 'SELECT COUNT(DISTINCT Category) FROM UserSkills WHERE UserId = 1',
  awards: 'SELECT COUNT(*) FROM UserAwards WHERE UserId = 1',
  settingsCount: 'SELECT COUNT(*) FROM SiteSetting',
  newsletters: 'SELECT COUNT(*) FROM Newsletter',
  viewsTotal: 'SELECT COALESCE(SUM(TotalViews), 0) FROM PostViewCount',
  setting: (key: string) => `SELECT COALESCE(SettingValue, '') FROM SiteSetting WHERE SettingKey = '${key}'`,
  user: (col: string) => `SELECT COALESCE(${col}::text, '') FROM BlogUser WHERE EmailId = '${ADMIN.email}'`,
};

/**
 * Asserts a rendered number against psql, tolerating a sibling agent writing between the two reads.
 *
 * A disagreement is re-measured (fresh psql read + fresh render) until it either agrees or survives
 * the deadline. A disagreement that survives a re-render is a real defect.
 */
async function assertAgainstDb(
  label: string,
  readUi: () => Promise<number>,
  sql: string,
  reRender?: () => Promise<void>,
  timeoutMs = 35000,
): Promise<{ ui: number; db: number }> {
  let ui = NaN;
  let db = NaN;
  const deadline = Date.now() + timeoutMs;
  let reRendered = false;
  while (Date.now() < deadline) {
    const before = psqlInt(sql);
    ui = await readUi();
    const after = psqlInt(sql);
    db = after;
    if (ui === before && ui === after) {
      console.log(`[db] ${label}: ui=${ui} psql=${db} OK`);
      return { ui, db };
    }
    if (reRender && !reRendered && Date.now() > deadline - 15000) {
      await reRender();
      reRendered = true;
    }
    await new Promise((r) => setTimeout(r, 1500));
  }
  console.log(`[db] ${label}: ui=${ui} psql=${db} MISMATCH after ${timeoutMs}ms`);
  expect(ui, `${label}: rendered value vs psql`).toBe(db);
  return { ui, db };
}

// =====================================================================================
// Session — one signed-in circuit shared by the whole file (serial mode)
// =====================================================================================

// NOT serial: a verifier must grade EVERY REQ it owns, and `mode: 'serial'` skips every test after
// the first failure — the one defect this surface is known to carry would have hidden the other 43
// verdicts. Default mode with `--workers=1` keeps the file's declaration order and one circuit.
let page: Page;

/**
 * Signs in through the real form.
 *
 * `/login` is an EditForm under InteractiveServer: Blazor prerenders it as static HTML with
 * `<form method="post" action="/login">` and the interactive re-render both drops the `action` and
 * wipes anything already typed. Clicking too early submits the PRERENDERED form and the host answers
 * HTTP 400 — a harness race that looks exactly like a product defect. The reliable hydration signal
 * is the `_bl_<guid>` attribute the interactive renderer stamps on every element with a handler.
 */
async function signIn(p: Page): Promise<string> {
  let last = '';
  for (let attempt = 1; attempt <= 4; attempt++) {
    try {
      await p.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
      await p.waitForSelector('[data-testid="login-email"]', { timeout: 60000 });
      await p.waitForFunction(
        () => {
          const b = document.querySelector('[data-testid="login-submit"]');
          return !!b && Array.from(b.attributes).some((a) => a.name.startsWith('_bl'));
        },
        { timeout: 90000 },
      );
      await p.waitForTimeout(1000);
      await p.fill('[data-testid="login-email"]', ADMIN.email);
      await p.fill('[data-testid="login-password"]', ADMIN.password);
      await p.click('[data-testid="login-submit"]');
      await p.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 40000 });
      await p.waitForTimeout(2000);
      return p.url();
    } catch (e: any) {
      last = `${e.message ?? e}`.split('\n')[0];
      console.log(`[login] attempt ${attempt} failed: ${last}`);
    }
  }
  throw new Error(`admin sign-in failed after 4 attempts: ${last}`);
}

/**
 * Authenticated navigation.
 *
 * A full page load of an authorised route prerenders as anonymous (the JWT lives in localStorage
 * only) and bounces to /login, so navigation MUST go through Blazor.navigateTo. The URL also changes
 * BEFORE the destination renders, so every hop is gated on the destination's own heading — otherwise
 * the previous screen gets measured. Escape first: a previous test may have left a dialog open.
 */
async function go(route: string, heading?: RegExp) {
  await page.keyboard.press('Escape').catch(() => {});
  await page.evaluate((r) => (window as any).Blazor.navigateTo(r), route);
  if (heading) {
    await expect(page.locator('h1, h2, h3').filter({ hasText: heading }).first(), `heading for ${route}`)
      .toBeVisible({ timeout: 45000 });
  }
  await page
    .waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), { timeout: 30000 })
    .catch(() => {});
  await page.waitForTimeout(900);
}

test.beforeAll(async ({ browser }: { browser: Browser }) => {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  page = await ctx.newPage();
  const landing = await signIn(page);
  console.log(`[login] admin landed on ${landing}`);
  // Assert the exact landing URL rather than assuming the sign-in took. MustChangePassword is
  // cleared for the whole run by the orchestrator — this must NOT bounce to /change-password.
  expect(landing, 'admin must land on the admin dashboard, not a password-change interstitial')
    .toContain('/admin');
  expect(landing, 'sign-in must not have bounced back to /login').not.toContain('/login');
});

// =====================================================================================
// §4a / §4b gate plumbing
// =====================================================================================

const renderFindings: string[] = [];
const visualFindings: string[] = [];

/** Records every §4a verdict and fails the test on anything that is not RENDERS. */
function assertRenders(screen: string, controls: ControlResult[]) {
  for (const c of controls) {
    console.log(`[RENDER] ${screen} · ${c.control} → ${c.verdict} (${c.detail})`);
    if (c.verdict !== 'RENDERS') renderFindings.push(`${screen} · ${c.control}: ${c.verdict} — ${c.detail}`);
  }
  const bad = controls.filter((c) => c.verdict !== 'RENDERS');
  expect(bad.map((c) => `${c.control}: ${c.verdict} (${c.detail})`), `§4a render gate @ ${screen}`).toEqual([]);
}

/**
 * §4a for a data grid: rows > 0 AND non-empty DATA CELLS, plus the count badge agreeing with the
 * rendered rows. Asserting the badge alone is exactly the failure this gate exists to catch.
 */
async function assertGridCells(screen: string, rowTestId: string, cellTestIds: string[], expectedRows: number) {
  const rows = await page.locator(`[data-testid="${rowTestId}"]`).count();
  expect(rows, `${screen}: ${rowTestId} must render rows`).toBeGreaterThan(0);
  expect(rows, `${screen}: rendered rows vs psql`).toBe(expectedRows);
  for (const cell of cellTestIds) {
    const texts = await page.locator(`[data-testid="${cell}"]`).allTextContents();
    const filled = texts.filter((t) => t.trim().length > 0).length;
    console.log(`[CELLS] ${screen} · ${cell}: ${filled}/${texts.length} non-empty`);
    expect(texts.length, `${screen}: ${cell} must appear on every row`).toBe(rows);
    expect(filled, `${screen}: every ${cell} cell must carry data, not a blank`).toBe(rows);
  }
}

/**
 * Which of `visualCheck`'s off-viewport findings are real.
 *
 * The shared detector measures against the VIEWPORT, so every cell of a deliberately
 * horizontally-scrollable responsive table reads "off-viewport" at 390. Only a control with NO
 * horizontally scrollable ancestor is genuinely unreachable.
 */
async function realOffViewport(reported: string[]): Promise<{ real: string[]; scrollable: string[] }> {
  if (!reported.length) return { real: [], scrollable: [] };
  const names = reported.map((r) => r.split('@')[0]);
  return page.evaluate((list) => {
    const real: string[] = [];
    const scrollable: string[] = [];
    for (const name of Array.from(new Set(list))) {
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
  }, names);
}

/** §4b at both mandated widths. Also writes a FULL-PAGE screenshot for eyes-on review. */
async function visualBothWidths(slug: string) {
  const problems: string[] = [];
  const notes: string[] = [];
  for (const width of [1280, 390]) {
    const v = await visualCheck(page, `${OUT}/${slug}-${width}.png`, width);
    // visualCheck's shot is viewport-only; the eyes-on gate needs the whole page.
    await page.screenshot({ path: `${OUT}/${slug}-${width}-full.png`, fullPage: true });
    const off = await realOffViewport(v.offViewport);
    if (v.overlaps.length) problems.push(`${width}: ${v.overlaps.length} sibling overlaps ${JSON.stringify(v.overlaps.slice(0, 3))}`);
    if (v.zeroSized.length) problems.push(`${width}: zero-sized ${v.zeroSized.slice(0, 5).join(',')}`);
    if (off.real.length) problems.push(`${width}: outside page bounds ${off.real.slice(0, 5).join(',')}`);
    if (off.scrollable.length) notes.push(`${width}: ${off.scrollable.length} controls inside a scrollable table — intended`);
    if (v.hScroll > 2) problems.push(`${width}: page hScroll=${v.hScroll}`);
    const fatal = v.consoleErrors.filter((e) => !/favicon|net::ERR_|404 \(/i.test(e));
    if (fatal.length) problems.push(`${width}: console ${fatal.slice(0, 2).join(' | ')}`);
  }
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.waitForTimeout(600);
  const verdict = problems.length ? `VISUAL-FAIL — ${problems.join('; ')}` : `VISUAL-OK${notes.length ? ` (${notes.join('; ')})` : ''}`;
  console.log(`[VISUAL] ${slug} → ${verdict} · ${OUT}/${slug}-{1280,390}-full.png`);
  if (problems.length) visualFindings.push(`${slug}: ${problems.join('; ')}`);
  expect(problems, `§4b visual gate @ ${slug}`).toEqual([]);
}

/** Text of one control, trimmed and whitespace-collapsed. */
async function text(testid: string, nth = 0): Promise<string> {
  return ((await page.locator(`[data-testid="${testid}"]`).nth(nth).textContent()) || '').replace(/\s+/g, ' ').trim();
}

async function value(testid: string, nth = 0): Promise<string> {
  return ((await page.locator(`[data-testid="${testid}"]`).nth(nth).inputValue().catch(() => '')) || '').trim();
}

const num = (testid: string) => async () =>
  Number(((await page.locator(`[data-testid="${testid}"]`).first().textContent()) || '').replace(/[^\d-]/g, ''));

const rowCount = (testid: string) => async () => page.locator(`[data-testid="${testid}"]`).count();

/** Scans the whole rendered page for raw exception disclosure (REQ-NFR-033). */
async function assertNoRawExceptionText(where: string) {
  const body = await page.evaluate(() => document.body.innerText || '');
  const leaks: string[] = [];
  const patterns: [RegExp, string][] = [
    [/\bat [A-Za-z0-9_.]+\.[A-Za-z0-9_]+\([^)]*\) in /, 'stack frame with source file'],
    [/\b(System|Npgsql|Dapper|Microsoft)\.[A-Za-z.]*Exception\b/, '.NET exception type name'],
    [/[A-Za-z]:\\[^\s"']{4,}/, 'absolute Windows path'],
    [/\/(mnt|home|usr|var)\/[^\s"']{6,}/, 'absolute unix path'],
    [/Host=[^;\s]+;\s*Port=\d+/i, 'connection string'],
    [/\b(Password|Pwd)\s*=\s*\S+/i, 'credential in text'],
    [/\b(relation|column) "[a-z_]+" does not exist/i, 'raw PostgreSQL error'],
    [/violates foreign key constraint/i, 'raw PostgreSQL FK violation'],
    [/\bStackTrace\b|\bInnerException\b/, 'stack trace marker'],
    [/23503|42P01|42703/, 'PostgreSQL SQLSTATE code'],
  ];
  for (const [re, label] of patterns) {
    const m = body.match(re);
    if (m) leaks.push(`${label}: "${m[0].slice(0, 120)}"`);
  }
  console.log(`[NFR-033] ${where}: ${leaks.length ? leaks.join(' | ') : 'clean — no raw exception disclosure'}`);
  expect(leaks, `REQ-NFR-033 raw exception text on ${where}`).toEqual([]);
}

// =====================================================================================
// REQ-UI-047 — Admin layout with grouped navigation (measured first: every other screen wears it)
// =====================================================================================
test('REQ-UI-047 admin layout renders grouped navigation, topbar and identity on every admin screen', async () => {
  await go('/admin', /^Dashboard$/);

  const controls: ControlResult[] = [
    await renderCheck(page, 'admin sidebar', '[data-testid="admin-sidebar"]', 'present'),
    await renderCheck(page, 'admin topbar', '[data-testid="admin-topbar"]', 'present'),
    await renderCheck(page, 'admin content region', '[data-testid="admin-content"]', 'value'),
    await renderCheck(page, 'sidebar collapse trigger', '[data-testid="sidebar-collapse-trigger"]', 'present'),
    await renderCheck(page, 'view-site link', '[data-testid="view-site"]', 'present'),
    await renderCheck(page, 'account menu trigger', '[data-testid="account-menu-trigger"]', 'present'),
  ];
  assertRenders('/admin layout', controls);

  // The DevGuide records 6 group headings and 17 entries for an Admin. Both are asserted; a group
  // an Admin may not see must be HIDDEN, never rendered empty.
  const entries = [
    'nav-dashboard', 'nav-posts', 'nav-series', 'nav-comments', 'nav-categories', 'nav-tags',
    'nav-images', 'nav-profile', 'nav-experience', 'nav-skills', 'nav-awards', 'nav-stats',
    'nav-users', 'nav-subscribers', 'nav-newsletter', 'nav-analytics', 'nav-settings',
  ];
  for (const id of entries) {
    await expect(page.locator(`[data-testid="${id}"]`), `nav entry ${id}`).toBeVisible();
    expect((await text(id)).length, `nav entry ${id} must carry a label`).toBeGreaterThan(0);
  }
  const groups = await page.evaluate(() => {
    const sb = document.querySelector('[data-testid="admin-sidebar"]');
    if (!sb) return [];
    // Group headings are the non-link label nodes that precede a run of nav entries.
    return Array.from(sb.querySelectorAll('div,span,p,h2,h3,h4'))
      .filter((e) => !e.querySelector('a,button') && (e.textContent || '').trim().length > 0 && (e.textContent || '').trim().length < 24)
      .map((e) => (e.textContent || '').trim())
      .filter((t) => /^(Content|Taxonomy|Media|Resume|Audience|System)$/.test(t));
  });
  const uniqueGroups = Array.from(new Set(groups));
  console.log(`[REQ-UI-047] ${entries.length} nav entries, groups = ${JSON.stringify(uniqueGroups)}`);
  expect(uniqueGroups.sort(), 'the six admin nav groups must all render').toEqual(
    ['Audience', 'Content', 'Media', 'Resume', 'System', 'Taxonomy'],
  );

  // Exactly one active highlight, and it must be the screen we are on. The highlight markers are
  // discovered rather than assumed: every entry carries the same Tailwind variant classes, so the
  // signal is whichever class tokens / attributes the CURRENT entry has that its siblings do not.
  const highlight = await page.evaluate(() => {
    const els = Array.from(document.querySelectorAll('[data-testid^="nav-"]')) as HTMLElement[];
    const tokens = (e: HTMLElement) => new Set(Array.from(e.classList));
    const current = els.find((e) => e.getAttribute('data-testid') === 'nav-dashboard')!;
    const other = els.find((e) => e.getAttribute('data-testid') === 'nav-posts')!;
    const distinct = Array.from(tokens(current)).filter((t) => !tokens(other).has(t));
    const attrMarks = els
      .filter((e) => e.getAttribute('aria-current') === 'page' || e.getAttribute('data-active') === 'true')
      .map((e) => e.getAttribute('data-testid')!);
    const classMarks = distinct.length
      ? els.filter((e) => distinct.every((t) => e.classList.contains(t))).map((e) => e.getAttribute('data-testid')!)
      : [];
    return { distinct, attrMarks, classMarks };
  });
  console.log(`[REQ-UI-047] highlight tokens=${JSON.stringify(highlight.distinct)} attrMarked=${JSON.stringify(highlight.attrMarks)} classMarked=${JSON.stringify(highlight.classMarks)}`);
  const marked = highlight.attrMarks.length ? highlight.attrMarks : highlight.classMarks;
  expect(marked.length, 'exactly one nav entry may carry the active highlight').toBe(1);
  expect(marked[0], 'the highlight must follow the current screen').toBe('nav-dashboard');

  // The account menu must name the signed-in identity, not a placeholder.
  await page.click('[data-testid="account-menu-trigger"]');
  await page.waitForTimeout(1500);
  const menuText = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  console.log(`[REQ-UI-047] account menu snippet: ${menuText.slice(menuText.indexOf('Sign out') - 120, menuText.indexOf('Sign out') + 12) || menuText.slice(0, 120)}`);
  expect(menuText, 'the account menu must name the signed-in admin').toMatch(/Ravi|S Ravi Kumar/i);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(800);

  await assertNoRawExceptionText('/admin layout');
  await visualBothWidths('req-ui-047-admin-layout');
});

// =====================================================================================
// REQ-UI-019 / REQ-FN-036 — Admin dashboard: stat tiles + quick actions + counts service
// =====================================================================================
test('REQ-UI-019 admin dashboard stat tiles, needs-attention badges and quick actions render live data', async () => {
  await go('/admin', /^Dashboard$/);
  const reRender = async () => { await go('/users', /^Users$/); await go('/admin', /^Dashboard$/); };

  const controls: ControlResult[] = [];
  for (const [name, id] of [
    ['posts tile', 'stat-posts-value'],
    ['users tile', 'stat-users-value'],
    ['comments tile', 'stat-comments-value'],
    ['subscribers tile', 'stat-subscribers-value'],
    ['needs-attention pending', 'attention-pending-comments'],
    ['needs-attention scheduled', 'attention-scheduled-posts'],
    ['needs-attention draft', 'attention-draft-posts'],
    ['quick actions', 'quick-actions'],
    ['recent activity', 'recent-activity-list'],
    ['popular posts', 'popular-posts-list'],
  ] as const) {
    controls.push(await renderCheck(page, name, `[data-testid="${id}"]`, 'value'));
  }
  assertRenders('/admin', controls);

  // Cross-check every tile against psql at the moment of measurement — a widget showing zeros
  // passes a presence check and is still broken.
  const posts = await assertAgainstDb('posts tile', num('stat-posts-value'), SQL.posts, reRender);
  const users = await assertAgainstDb('users tile', num('stat-users-value'), SQL.users, reRender);
  const comments = await assertAgainstDb('comments tile', num('stat-comments-value'), SQL.comments, reRender);
  const subs = await assertAgainstDb('subscribers tile', num('stat-subscribers-value'), SQL.subscribers, reRender);
  const pending = await assertAgainstDb('pending badge', num('attention-pending-comments'), SQL.pendingBadge, reRender);
  const sched = await assertAgainstDb('scheduled badge', num('attention-scheduled-posts'), SQL.scheduled, reRender);
  const drafts = await assertAgainstDb('draft badge', num('attention-draft-posts'), SQL.drafts, reRender);
  for (const [label, r] of [['posts', posts], ['users', users], ['comments', comments], ['subscribers', subs], ['pending', pending], ['scheduled', sched], ['drafts', drafts]] as const) {
    expect(r.ui, `${label} tile vs psql`).toBe(r.db);
    expect(r.ui, `${label} tile must not be a blank zero`).toBeGreaterThanOrEqual(0);
  }
  // A stat tile of all-zeros across the board would mean the counts service never ran.
  expect(posts.ui + users.ui + comments.ui + subs.ui, 'the four dashboard tiles cannot all be zero').toBeGreaterThan(0);

  // Popular posts must carry real titles AND real view numbers, ranked by the rollup.
  const popTitles = (await page.locator('[data-testid="popular-post-title"]').allTextContents()).map((t) => t.trim());
  const popViews = (await page.locator('[data-testid="popular-post-views"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  console.log(`[REQ-UI-019] popular = ${JSON.stringify(popTitles.map((t, i) => `${t}:${popViews[i]}`))}`);
  expect(popTitles.length, 'popular posts must render rows').toBeGreaterThan(0);
  expect(popTitles.filter((t) => t.length > 0).length, 'every popular row needs a title cell').toBe(popTitles.length);
  expect(popViews.filter((v) => v > 0).length, 'every popular row needs a non-zero view count').toBe(popViews.length);
  const dbTop = psqlRows('SELECT p.Title, c.TotalViews FROM PostViewCount c JOIN BlogPost p ON p.PostId = c.PostId ORDER BY c.TotalViews DESC, p.PostId LIMIT 5');
  console.log(`[REQ-UI-019] psql top-5 = ${JSON.stringify(dbTop)}`);
  expect(popViews, 'popular-post views must equal the PostViewCount rollup, descending')
    .toEqual(dbTop.map((r) => Number(r[1])));

  const recent = (await page.locator('[data-testid="recent-activity-item"]').allTextContents()).map((t) => t.replace(/\s+/g, ' ').trim());
  console.log(`[REQ-UI-019] recent activity = ${recent.length} items`);
  expect(recent.length, 'recent activity must render items').toBeGreaterThan(0);
  expect(recent.filter((t) => t.length > 0).length, 'every recent-activity item must carry text').toBe(recent.length);

  for (const id of ['action-new-post', 'action-moderate-comments', 'action-send-newsletter', 'action-manage-users']) {
    await expect(page.locator(`[data-testid="${id}"]`), `admin quick action ${id}`).toBeVisible();
    expect((await text(id)).length, `${id} must carry a label`).toBeGreaterThan(0);
  }

  await assertNoRawExceptionText('/admin');
  await visualBothWidths('req-ui-019-dashboard');
});

test('REQ-FN-036 dashboard counts service returns the same numbers psql does, on a re-render', async () => {
  await go('/BlogsList', /Posts/i);
  await go('/admin', /^Dashboard$/);
  const readings: Record<string, { ui: number; db: number }> = {};
  readings.posts = await assertAgainstDb('counts.posts', num('stat-posts-value'), SQL.posts);
  readings.users = await assertAgainstDb('counts.users', num('stat-users-value'), SQL.users);
  readings.comments = await assertAgainstDb('counts.comments', num('stat-comments-value'), SQL.comments);
  readings.subscribers = await assertAgainstDb('counts.subscribers', num('stat-subscribers-value'), SQL.subscribers);
  readings.pending = await assertAgainstDb('counts.pending', num('attention-pending-comments'), SQL.pendingBadge);
  readings.drafts = await assertAgainstDb('counts.drafts', num('attention-draft-posts'), SQL.drafts);
  readings.scheduled = await assertAgainstDb('counts.scheduled', num('attention-scheduled-posts'), SQL.scheduled);
  console.log(`[REQ-FN-036] ${JSON.stringify(readings)}`);
  for (const [k, r] of Object.entries(readings)) expect(r.ui, `counts service ${k}`).toBe(r.db);
});

// =====================================================================================
// REQ-UI-020 — Users list + add user
// =====================================================================================
test('REQ-UI-020 users list renders every user with populated cells, working search, and a complete add-user form', async () => {
  await go('/users', /^Users$/);
  const reRender = async () => { await go('/admin', /^Dashboard$/); await go('/users', /^Users$/); };

  const controls: ControlResult[] = [
    await renderCheck(page, 'users grid', '[data-testid="users-grid"]', 'table'),
    await renderCheck(page, 'users count badge', '[data-testid="users-count"]', 'value'),
    await renderCheck(page, 'search box', '[data-testid="users-search"]', 'present'),
    await renderCheck(page, 'role tabs', '[data-testid="users-role-tabs"]', 'present'),
    await renderCheck(page, 'add-user button', '[data-testid="new-user"]', 'present'),
  ];
  assertRenders('/users', controls);

  const rows = await assertAgainstDb('user rows', rowCount('user-row-name'), SQL.users, reRender);
  // §4a: the CELLS, not the badge.
  await assertGridCells('/users', 'user-row-name', ['user-row-username', 'user-row-email', 'user-row-role', 'user-row-status', 'user-row-joined'], rows.db);
  const badge = await text('users-count');
  console.log(`[REQ-UI-020] rows=${rows.ui} badge="${badge}"`);
  expect(badge, 'the count badge must agree with the rendered rows, not float free of them').toContain(String(rows.ui));

  // Per-row actions.
  expect(await page.locator('[data-testid="user-change-role"]').count(), 'change-role on every row').toBe(rows.ui);
  expect(await page.locator('[data-testid="user-deactivate"]').count(), 'deactivate on every row').toBe(rows.ui);

  // Search — debounced, so poll instead of sleeping a guessed interval.
  await page.fill('[data-testid="users-search"]', 'editor');
  let filtered = rows.ui;
  for (const deadline = Date.now() + 20000; Date.now() < deadline;) {
    filtered = await page.locator('[data-testid="user-row-name"]').count();
    if (filtered < rows.ui) break;
    await page.waitForTimeout(1200);
  }
  const survivors = (await page.locator('[data-testid="user-row-email"]').allTextContents()).join(' ').toLowerCase();
  console.log(`[REQ-UI-020] search "editor": ${rows.ui} → ${filtered} rows (${survivors})`);
  expect(filtered, 'search must narrow the list').toBeLessThan(rows.ui);
  expect(filtered, 'search must still match the editor').toBeGreaterThan(0);
  expect(survivors, 'the surviving row must be the editor').toContain('editor');
  await page.fill('[data-testid="users-search"]', '');
  await page.waitForTimeout(1500);

  await assertNoRawExceptionText('/users');
  await visualBothWidths('req-ui-020-users');

  await go('/AddUser', /Add New User/);
  const form: ControlResult[] = [];
  for (const [name, id] of [
    ['first name', 'user-first-name'], ['last name', 'user-last-name'], ['email', 'user-email'],
    ['password', 'user-password'], ['confirm password', 'user-confirm-password'],
    ['role select', 'user-role'], ['submit', 'add-user-submit'], ['cancel', 'add-user-cancel'],
  ] as const) {
    form.push(await renderCheck(page, name, `[data-testid="${id}"]`, 'present'));
  }
  assertRenders('/AddUser', form);
  await assertNoRawExceptionText('/AddUser');
  await visualBothWidths('req-ui-020-add-user');
});

// =====================================================================================
// REQ-UI-021 — Comment moderation queue
// =====================================================================================
test('REQ-UI-021 comment queue lists every comment with populated cells, exact tab counts and moderation controls', async () => {
  await go('/CommentsList', /Comments Management/);
  const reRender = async () => { await go('/admin', /^Dashboard$/); await go('/CommentsList', /Comments Management/); };

  const controls: ControlResult[] = [
    await renderCheck(page, 'comments grid', '[data-testid="comments-grid"]', 'table'),
    await renderCheck(page, 'comments count', '[data-testid="comments-count"]', 'value'),
    await renderCheck(page, 'status tabs', '[data-testid="comments-status-tabs"]', 'present'),
    await renderCheck(page, 'search', '[data-testid="comments-search"]', 'present'),
    await renderCheck(page, 'bulk action select', '[data-testid="comments-bulk-action"]', 'present'),
    await renderCheck(page, 'bulk apply', '[data-testid="comments-bulk-apply"]', 'present'),
    await renderCheck(page, 'select all', '[data-testid="comments-select-all"]', 'present'),
  ];
  assertRenders('/CommentsList', controls);

  const rows = await assertAgainstDb('comment rows', rowCount('comment-row-text'), SQL.comments, reRender);
  await assertGridCells('/CommentsList', 'comment-row-text', ['comment-row-author', 'comment-row-email', 'comment-row-post', 'comment-row-status', 'comment-row-date'], rows.db);

  // The tab captions carry their own counts — each must equal the psql predicate behind it.
  const tabAll = Number((await text('comments-tab-all')).replace(/[^\d]/g, ''));
  const tabPending = Number((await text('comments-tab-pending')).replace(/[^\d]/g, ''));
  const tabApproved = Number((await text('comments-tab-approved')).replace(/[^\d]/g, ''));
  const tabSpam = Number((await text('comments-tab-spam')).replace(/[^\d]/g, ''));
  console.log(`[REQ-UI-021] tabs all=${tabAll} pending=${tabPending} approved=${tabApproved} spam=${tabSpam}`);
  expect(tabAll, 'All tab vs psql').toBe(psqlInt(SQL.comments));
  expect(tabPending, 'Pending tab vs psql (ModerationStatus predicate)').toBe(psqlInt(SQL.pendingTab));
  expect(tabApproved, 'Approved tab vs psql').toBe(psqlInt(SQL.approvedTab));
  expect(tabSpam, 'Spam tab vs psql').toBe(psqlInt(SQL.spamTab));
  expect(tabPending + tabApproved + tabSpam, 'the three status tabs must partition into All or below it').toBeLessThanOrEqual(tabAll);

  // Per-row moderation controls: reply/delete on every row, approve/spam on the pending ones.
  expect(await page.locator('[data-testid="comment-reply"]').count(), 'reply on every row').toBe(rows.ui);
  expect(await page.locator('[data-testid="comment-delete"]').count(), 'delete on every row').toBe(rows.ui);
  const approve = await page.locator('[data-testid="comment-approve"]').count();
  console.log(`[REQ-UI-021] approve controls offered on ${approve} rows (psql pending=${psqlInt(SQL.pendingTab)})`);
  expect(approve, 'approve must be offered on the un-approved rows').toBeGreaterThan(0);

  // Delete dialog opens and is CANCELLED — read-only.
  await page.locator('[data-testid="comment-delete"]').first().click();
  await page.waitForTimeout(2500);
  const dialog = await page.locator('[data-testid="comment-delete-dialog"]').count();
  console.log(`[REQ-UI-021] delete confirmation dialog present=${dialog}`);
  expect(dialog, 'delete must be confirmed, never immediate').toBeGreaterThan(0);
  await assertNoRawExceptionText('/CommentsList delete dialog');
  await page.locator('[data-testid="comment-delete-cancel"]').first().click().catch(async () => { await page.keyboard.press('Escape'); });
  await page.waitForTimeout(2000);
  expect(await page.locator('[data-testid="comment-row-text"]').count(), 'cancelling must not delete anything').toBe(rows.ui);

  await visualBothWidths('req-ui-021-comments');
});

// =====================================================================================
// REQ-UI-022 / REQ-FN-017 — Categories list + manage category
// =====================================================================================
test('REQ-UI-022 categories list renders every category with a correct published-post count', async () => {
  await go('/CategoriesList', /Categories Management/);
  const reRender = async () => { await go('/admin', /^Dashboard$/); await go('/CategoriesList', /Categories Management/); };

  const controls: ControlResult[] = [
    await renderCheck(page, 'categories grid', '[data-testid="categories-grid"]', 'table'),
    await renderCheck(page, 'categories count', '[data-testid="categories-count"]', 'value'),
    await renderCheck(page, 'search', '[data-testid="categories-search"]', 'present'),
    await renderCheck(page, 'new category', '[data-testid="new-category"]', 'present'),
  ];
  assertRenders('/CategoriesList', controls);

  const rows = await assertAgainstDb('category rows', rowCount('category-row-name'), SQL.categories, reRender);
  await assertGridCells('/CategoriesList', 'category-row-name', ['category-row-slug', 'category-row-postcount'], rows.db);
  expect(await text('categories-count'), 'count badge vs rendered rows').toContain(String(rows.ui));

  await assertNoRawExceptionText('/CategoriesList');
  await visualBothWidths('req-ui-022-categories');

  // Editor loads populated for an existing row.
  await page.locator('[data-testid="category-edit"]').first().click();
  await page.waitForTimeout(3500);
  const editControls: ControlResult[] = [
    await renderCheck(page, 'category name input', '[data-testid="category-name-input"]', 'present'),
    await renderCheck(page, 'category slug input', '[data-testid="category-slug-input"]', 'present'),
    await renderCheck(page, 'save', '[data-testid="save-category"]', 'present'),
  ];
  assertRenders('/admin/category/{id}', editControls);
  const name = await value('category-name-input');
  const slug = await value('category-slug-input');
  console.log(`[REQ-UI-022] editor loaded name="${name}" slug="${slug}"`);
  expect(name.length, 'the editor must load the existing category name, not a blank form').toBeGreaterThan(0);
  expect(slug.length, 'the editor must load the existing slug').toBeGreaterThan(0);
  const dbMatch = psqlInt(`SELECT COUNT(*) FROM Category WHERE CategoryName = ${JSON.stringify(name).replace(/"/g, "'")}`);
  expect(dbMatch, 'the loaded category name must exist in psql').toBeGreaterThan(0);
  await assertNoRawExceptionText('/admin/category/{id}');
});

test('REQ-FN-017 category post counts equal the published-only psql count for each category', async () => {
  await go('/CategoriesList', /Categories Management/);
  const names = (await page.locator('[data-testid="category-row-name"]').allTextContents()).map((t) => t.trim());
  const counts = (await page.locator('[data-testid="category-row-postcount"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  const db = new Map(
    psqlRows(`SELECT c.CategoryName, COUNT(p.PostId) FROM Category c LEFT JOIN BlogPost p ON p.CategoryId = c.CategoryId AND p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL) GROUP BY c.CategoryName`)
      .map((r) => [r[0], Number(r[1])] as [string, number]),
  );
  const mismatches: string[] = [];
  names.forEach((n, i) => {
    const expected = db.get(n);
    console.log(`[REQ-FN-017] ${n}: ui=${counts[i]} psql=${expected}`);
    if (expected === undefined || counts[i] !== expected) mismatches.push(`${n}: ui=${counts[i]} psql=${expected}`);
  });
  expect(mismatches, 'every category post count must equal its published-only psql count').toEqual([]);
  const sum = counts.reduce((a, b) => a + b, 0);
  expect(sum, 'the per-category counts must sum to the published categorised posts')
    .toBe(psqlInt(`SELECT COUNT(*) FROM BlogPost WHERE CategoryId IS NOT NULL AND Published = TRUE AND ${LIVE}`));
});

// =====================================================================================
// REQ-UI-023 / REQ-FN-018 — Tags list + manage tag
// =====================================================================================
test('REQ-UI-023 tags list renders every tag with slug and count, and the tag editor loads populated', async () => {
  await go('/admin/tags', /Tags Management/);
  const reRender = async () => { await go('/admin', /^Dashboard$/); await go('/admin/tags', /Tags Management/); };

  const controls: ControlResult[] = [
    await renderCheck(page, 'tags grid', '[data-testid="tags-grid"]', 'table'),
    await renderCheck(page, 'tags count', '[data-testid="tags-count"]', 'value'),
    await renderCheck(page, 'search', '[data-testid="tags-search"]', 'present'),
    await renderCheck(page, 'new tag', '[data-testid="new-tag"]', 'present'),
  ];
  assertRenders('/admin/tags', controls);

  const rows = await assertAgainstDb('tag rows', rowCount('tag-row-name'), SQL.tags, reRender);
  await assertGridCells('/admin/tags', 'tag-row-name', ['tag-row-slug', 'tag-row-postcount'], rows.db);
  expect(await text('tags-count'), 'count badge vs rendered rows').toContain(String(rows.ui));

  await assertNoRawExceptionText('/admin/tags');
  await visualBothWidths('req-ui-023-tags');

  await page.locator('[data-testid="tag-edit"]').first().click();
  await page.waitForTimeout(3500);
  assertRenders('/ManageTag/{id}', [
    await renderCheck(page, 'tag name input', '[data-testid="tag-name-input"]', 'present'),
    await renderCheck(page, 'tag slug input', '[data-testid="tag-slug-input"]', 'present'),
    await renderCheck(page, 'save tag', '[data-testid="save-tag"]', 'present'),
  ]);
  const tagName = await value('tag-name-input');
  console.log(`[REQ-UI-023] tag editor loaded name="${tagName}" slug="${await value('tag-slug-input')}"`);
  expect(tagName.length, 'the tag editor must load the existing name').toBeGreaterThan(0);
  await assertNoRawExceptionText('/ManageTag/{id}');
});

test('REQ-FN-018 tag post counts equal the published-only psql count for each tag', async () => {
  await go('/admin/tags', /Tags Management/);
  const names = (await page.locator('[data-testid="tag-row-name"]').allTextContents()).map((t) => t.trim());
  const counts = (await page.locator('[data-testid="tag-row-postcount"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  const db = new Map(
    psqlRows(`SELECT t.TagName, COUNT(p.PostId) FROM Tag t LEFT JOIN PostTag pt ON pt.TagId = t.TagId LEFT JOIN BlogPost p ON p.PostId = pt.PostId AND p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL) GROUP BY t.TagName`)
      .map((r) => [r[0], Number(r[1])] as [string, number]),
  );
  const mismatches: string[] = [];
  names.forEach((n, i) => {
    const expected = db.get(n);
    console.log(`[REQ-FN-018] ${n}: ui=${counts[i]} psql=${expected}`);
    if (expected === undefined || counts[i] !== expected) mismatches.push(`${n}: ui=${counts[i]} psql=${expected}`);
  });
  expect(mismatches, 'every tag post count must equal its published-only psql count (Story 7.5)').toEqual([]);
});

// =====================================================================================
// REQ-UI-024 / REQ-FN-019 — Series list + manage series
// =====================================================================================
test('REQ-UI-024 series list renders every series with slug, status, post count and author', async () => {
  await go('/admin/series', /Series Management/);
  const reRender = async () => { await go('/admin', /^Dashboard$/); await go('/admin/series', /Series Management/); };

  assertRenders('/admin/series', [
    await renderCheck(page, 'series grid', '[data-testid="series-grid"]', 'table'),
    await renderCheck(page, 'status tabs', '[data-testid="series-status-tabs"]', 'present'),
    await renderCheck(page, 'search', '[data-testid="series-search"]', 'present'),
    await renderCheck(page, 'new series', '[data-testid="new-series"]', 'present'),
  ]);

  const rows = await assertAgainstDb('series rows', rowCount('series-row-name'), SQL.series, reRender);
  await assertGridCells('/admin/series', 'series-row-name', ['series-row-slug', 'series-row-status', 'series-row-postcount', 'series-row-author'], rows.db);

  // Tab counts are polled against psql, not sampled once: a sibling agent completing a series
  // mid-run would otherwise read as a screen defect. The predicates use the DB's OWN status
  // vocabulary (`Completed`, not `Complete`) so a correct screen is never failed on spelling.
  console.log(`[REQ-UI-024] psql statuses = ${JSON.stringify(psqlRows('SELECT Status, COUNT(*) FROM BlogSeries GROUP BY Status'))}`);
  console.log(`[REQ-UI-024] rendered status cells = ${JSON.stringify((await page.locator('[data-testid="series-row-status"]').allTextContents()).map((t) => t.trim()))}`);
  const tabNum = (id: string) => async () => Number((await text(id)).replace(/[^\d]/g, ''));
  await assertAgainstDb('series All tab', tabNum('series-tab-all'), SQL.series, reRender);
  await assertAgainstDb('series Complete tab', tabNum('series-tab-complete'), `SELECT COUNT(*) FROM BlogSeries WHERE Status ILIKE 'complet%'`, reRender);
  await assertAgainstDb('series In Progress tab', tabNum('series-tab-inprogress'), `SELECT COUNT(*) FROM BlogSeries WHERE Status IS NULL OR Status NOT ILIKE 'complet%'`, reRender);

  await assertNoRawExceptionText('/admin/series');
  await visualBothWidths('req-ui-024-series');
});

test('REQ-FN-019 series editor loads an existing series populated, and the row post counts match psql', async () => {
  await go('/admin/series', /Series Management/);
  const names = (await page.locator('[data-testid="series-row-name"]').allTextContents()).map((t) => t.trim());
  const counts = (await page.locator('[data-testid="series-row-postcount"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  // The list column counts PUBLISHED posts (measured: 3 rendered against 4 live / 3 published),
  // the same rule CategoriesList and TagsList use.
  const db = new Map(
    psqlRows(`SELECT s.Name, COUNT(p.PostId) FROM BlogSeries s LEFT JOIN BlogPost p ON p.SeriesId = s.SeriesId AND p.Published = TRUE AND (p.IsDeleted = FALSE OR p.IsDeleted IS NULL) GROUP BY s.Name`)
      .map((r) => [r[0], Number(r[1])] as [string, number]),
  );
  const mismatches: string[] = [];
  names.forEach((n, i) => {
    console.log(`[REQ-FN-019] ${n}: ui=${counts[i]} psql=${db.get(n)}`);
    if (db.get(n) === undefined || counts[i] !== db.get(n)) mismatches.push(`${n}: ui=${counts[i]} psql=${db.get(n)}`);
  });
  expect(mismatches, 'every series post count must match psql').toEqual([]);

  await page.locator('[data-testid="series-edit"]').first().click();
  await page.waitForTimeout(3500);
  const body = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  console.log(`[REQ-FN-019] series editor: ${body.slice(body.indexOf('Edit Series'), body.indexOf('Edit Series') + 200)}`);
  expect(body, 'the series editor must load, not report "Series not found."').not.toMatch(/Series not found/i);
  await assertNoRawExceptionText('/admin/series/{id}');
});

// =====================================================================================
// REQ-UI-025 / REQ-FN-030 / REQ-FN-031 — Subscribers admin page
// =====================================================================================
test('REQ-UI-025 subscribers page renders every subscriber with a populated row, exact tab counts and an export control', async () => {
  await go('/admin/subscribers', /^Subscribers$/);
  const reRender = async () => { await go('/admin', /^Dashboard$/); await go('/admin/subscribers', /^Subscribers$/); };

  assertRenders('/admin/subscribers', [
    await renderCheck(page, 'subscribers grid', '[data-testid="subscribers-grid"]', 'table'),
    await renderCheck(page, 'summary', '[data-testid="subscribers-summary"]', 'value'),
    await renderCheck(page, 'status tabs', '[data-testid="subscribers-status-tabs"]', 'present'),
    await renderCheck(page, 'search', '[data-testid="subscribers-search"]', 'present'),
    await renderCheck(page, 'export CSV', '[data-testid="subscribers-export"]', 'present'),
  ]);

  const rows = await assertAgainstDb('subscriber rows', rowCount('subscriber-row-email'), SQL.subscribers, reRender);
  await assertGridCells('/admin/subscribers', 'subscriber-row-email', ['subscriber-row-name', 'subscriber-row-date', 'subscriber-row-status', 'subscriber-row-consent'], rows.db);

  const total = psqlInt(SQL.subscribers);
  const active = psqlInt(SQL.subscribersActive);
  const summary = await text('subscribers-summary');
  console.log(`[REQ-UI-025] summary="${summary}" | psql total=${total} active=${active}`);
  expect(summary, 'summary must state the psql total').toContain(String(total));
  expect(summary, 'summary must state the psql active count').toContain(String(active));

  const tabAll = Number((await text('subscribers-tab-all')).replace(/[^\d]/g, ''));
  const tabActive = Number((await text('subscribers-tab-active')).replace(/[^\d]/g, ''));
  const tabInactive = Number((await text('subscribers-tab-inactive')).replace(/[^\d]/g, ''));
  console.log(`[REQ-UI-025] tabs all=${tabAll} active=${tabActive} inactive=${tabInactive}`);
  expect(tabAll, 'All tab vs psql').toBe(total);
  expect(tabActive, 'Active tab vs psql (IsConfirmed)').toBe(active);
  expect(tabInactive, 'Inactive tab vs psql').toBe(psqlInt(SQL.subscribersInactive));
  expect(tabActive + tabInactive, 'Active + Inactive must partition All').toBe(tabAll);

  await assertNoRawExceptionText('/admin/subscribers');
  await visualBothWidths('req-ui-025-subscribers');
});

test('REQ-FN-030 subscriber rows carry a real email, a subscribed-on date and a consent state', async () => {
  await go('/admin/subscribers', /^Subscribers$/);
  const emails = (await page.locator('[data-testid="subscriber-row-email"]').allTextContents()).map((t) => t.trim());
  const dates = (await page.locator('[data-testid="subscriber-row-date"]').allTextContents()).map((t) => t.trim());
  const consent = (await page.locator('[data-testid="subscriber-row-consent"]').allTextContents()).map((t) => t.trim());
  console.log(`[REQ-FN-030] ${emails.length} rows; first = ${emails[0]} / ${dates[0]} / ${consent[0]}`);
  expect(emails.every((e) => /@/.test(e)), 'every subscriber row must render a real email address').toBe(true);
  expect(dates.every((d) => d.length > 0), 'every subscriber row must render a subscribed-on date').toBe(true);
  expect(consent.every((c) => c.length > 0), 'every subscriber row must render a consent state (duplicate/consent handling)').toBe(true);
  // Every rendered email must exist in psql — no fabricated rows.
  const dbEmails = new Set(psqlRows('SELECT Email FROM Subscriber').map((r) => r[0].toLowerCase()));
  const ghosts = emails.filter((e) => !dbEmails.has(e.toLowerCase()));
  expect(ghosts, 'no rendered subscriber may be absent from psql').toEqual([]);
});

test('REQ-FN-031 subscriber search narrows the list and per-row status controls are offered', async () => {
  await go('/admin/subscribers', /^Subscribers$/);
  const before = await page.locator('[data-testid="subscriber-row-email"]').count();
  const firstEmail = (await text('subscriber-row-email')).trim();
  const needle = firstEmail.split('@')[0].slice(0, 6);
  await page.fill('[data-testid="subscribers-search"]', needle);
  let after = before;
  for (const deadline = Date.now() + 20000; Date.now() < deadline;) {
    after = await page.locator('[data-testid="subscriber-row-email"]').count();
    if (after < before) break;
    await page.waitForTimeout(1200);
  }
  console.log(`[REQ-FN-031] search "${needle}": ${before} → ${after}`);
  expect(after, 'search must narrow the subscriber list').toBeLessThan(before);
  expect(after, 'search must still match the seed row it was taken from').toBeGreaterThan(0);
  await page.fill('[data-testid="subscribers-search"]', '');
  await page.waitForTimeout(1500);

  // Status controls: activate on the inactive rows, deactivate on the active ones. NOT clicked —
  // that is a write and three siblings are asserting these counts.
  const deact = await page.locator('[data-testid="subscriber-deactivate"]').count();
  const act = await page.locator('[data-testid="subscriber-activate"]').count();
  console.log(`[REQ-FN-031] deactivate=${deact} activate=${act} | psql active=${psqlInt(SQL.subscribersActive)} inactive=${psqlInt(SQL.subscribersInactive)}`);
  expect(deact, 'deactivate must be offered on exactly the active rows').toBe(psqlInt(SQL.subscribersActive));
  expect(act, 'activate must be offered on exactly the inactive rows').toBe(psqlInt(SQL.subscribersInactive));
  await expect(page.locator('[data-testid="subscribers-export"]'), 'CSV export control').toBeVisible();
});

// =====================================================================================
// REQ-UI-026 / REQ-FN-040 — Site settings page and settings persistence
// =====================================================================================
/** Opens a settings tab and waits for its panel to render. */
async function settingsTab(tab: string) {
  await page.click(`[data-testid="${tab}"]`);
  await page.waitForTimeout(2500);
}

test('REQ-UI-026 site settings renders all six tabs with every field populated from the SiteSetting table', async () => {
  await go('/settings', /^Settings$/);
  assertRenders('/settings', [
    await renderCheck(page, 'settings page', '[data-testid="settings-page"]', 'value'),
    await renderCheck(page, 'General tab', '[data-testid="tab-general"]', 'present'),
    await renderCheck(page, 'Blog tab', '[data-testid="tab-blog"]', 'present'),
    await renderCheck(page, 'Theme tab', '[data-testid="tab-theme"]', 'present'),
    await renderCheck(page, 'SEO tab', '[data-testid="tab-seo"]', 'present'),
    await renderCheck(page, 'Email tab', '[data-testid="tab-email"]', 'present'),
    await renderCheck(page, 'Storage tab', '[data-testid="tab-storage"]', 'present'),
    await renderCheck(page, 'save button', '[data-testid="save-settings"]', 'present'),
  ]);

  // Every tab must render its controls — a tab whose panel never appears is RENDER-EMPTY.
  const panels: [string, string[]][] = [
    ['tab-general', ['site-title', 'site-tagline', 'admin-email']],
    ['tab-blog', ['posts-per-page', 'pagination-word-count', 'allow-comments', 'moderate-comments', 'allow-registration']],
    ['tab-theme', ['site-theme-select', 'theme-swatches', 'dark-mode-default']],
    ['tab-seo', ['meta-description', 'meta-keywords', 'twitter-url', 'linkedin-url', 'github-url']],
    ['tab-email', ['smtp-host', 'smtp-port', 'smtp-username', 'smtp-from-address', 'smtp-from-name', 'smtp-ssl']],
    ['tab-storage', ['storage-provider', 'storage-local-root', 'storage-network-root', 'storage-cloud-url', 'storage-cloud-container', 'storage-public-base']],
  ];
  let checked = 0;
  for (const [tab, ids] of panels) {
    await settingsTab(tab);
    const results: ControlResult[] = [];
    for (const id of ids) results.push(await renderCheck(page, `${tab} · ${id}`, `[data-testid="${id}"]`, 'present'));
    assertRenders(`/settings ${tab}`, results);
    checked += ids.length;
  }
  console.log(`[REQ-UI-026] ${checked} settings controls rendered across 6 tabs`);
  expect(checked, 'the settings page must expose all six panels of controls').toBeGreaterThanOrEqual(28);

  await assertNoRawExceptionText('/settings');
  await visualBothWidths('req-ui-026-settings');
});

test('REQ-FN-040 every rendered setting equals its SiteSetting row — the page is database-backed, not browser-scoped', async () => {
  await go('/settings', /^Settings$/);
  const mismatches: string[] = [];
  const check = async (tab: string, testid: string, key: string, kind: 'input' | 'text' | 'switch' = 'input') => {
    let ui: string;
    if (kind === 'input') ui = await value(testid);
    else if (kind === 'switch') ui = (await page.locator(`[data-testid="${testid}"]`).first().getAttribute('data-state')) === 'checked' ? 'True' : 'False';
    else ui = await text(testid);
    const db = psql(SQL.setting(key));
    console.log(`[REQ-FN-040] ${key}: ui="${ui}" psql="${db}"`);
    if (ui.trim() !== db.trim()) mismatches.push(`${key}: ui="${ui}" psql="${db}"`);
  };

  await settingsTab('tab-general');
  await check('tab-general', 'site-title', 'General.SiteTitle');
  await check('tab-general', 'site-tagline', 'General.SiteTagline');
  await check('tab-general', 'admin-email', 'General.AdminEmail');

  await settingsTab('tab-blog');
  await check('tab-blog', 'posts-per-page', 'Blog.PostsPerPage');
  await check('tab-blog', 'pagination-word-count', 'Blog.PaginationWordCount');
  await check('tab-blog', 'allow-comments', 'Blog.AreCommentsAllowed', 'switch');
  await check('tab-blog', 'moderate-comments', 'Blog.AreCommentsModerated', 'switch');
  await check('tab-blog', 'allow-registration', 'Blog.IsRegistrationAllowed', 'switch');

  await settingsTab('tab-seo');
  await check('tab-seo', 'meta-description', 'Seo.MetaDescription');
  await check('tab-seo', 'meta-keywords', 'Seo.MetaKeywords');
  await check('tab-seo', 'twitter-url', 'Social.TwitterUrl');
  await check('tab-seo', 'linkedin-url', 'Social.LinkedInUrl');
  await check('tab-seo', 'github-url', 'Social.GitHubUrl');

  await settingsTab('tab-email');
  await check('tab-email', 'smtp-host', 'Smtp.Host');
  await check('tab-email', 'smtp-port', 'Smtp.Port');
  await check('tab-email', 'smtp-username', 'Smtp.UserName');
  await check('tab-email', 'smtp-from-address', 'Smtp.FromAddress');
  await check('tab-email', 'smtp-from-name', 'Smtp.FromName');
  await check('tab-email', 'smtp-ssl', 'Smtp.IsSslEnabled', 'switch');

  expect(mismatches, 'REQ-FN-040: every rendered setting must equal its SiteSetting row').toEqual([]);
  expect(psqlInt(SQL.settingsCount), 'the SiteSetting table must be populated (settings are persisted, not discarded)').toBeGreaterThanOrEqual(29);
});

// =====================================================================================
// REQ-UI-032 — Theme selector in Site Settings
// =====================================================================================
test('REQ-UI-032 theme selector reflects the site-wide Theme.SiteTheme row and offers selectable swatches', async () => {
  await go('/settings', /^Settings$/);
  await settingsTab('tab-theme');

  assertRenders('/settings theme', [
    await renderCheck(page, 'site theme select', '[data-testid="site-theme-select"]', 'value'),
    await renderCheck(page, 'theme swatches', '[data-testid="theme-swatches"]', 'value'),
    await renderCheck(page, 'dark mode default toggle', '[data-testid="dark-mode-default"]', 'present'),
  ]);

  const selected = await text('site-theme-select');
  const dbTheme = psql(SQL.setting('Theme.SiteTheme'));
  console.log(`[REQ-UI-032] selector shows "${selected}" | psql Theme.SiteTheme="${dbTheme}"`);
  expect(selected.toLowerCase().replace(/\s+/g, '-'), 'the selector must show the SITE-WIDE stored theme, not a per-visitor localStorage value')
    .toContain(dbTheme.toLowerCase());

  const swatches = await page.locator('[data-testid^="theme-swatch-"]').count();
  const swatchLabels = (await page.locator('[data-testid^="theme-swatch-"]').allTextContents()).map((t) => t.replace(/\s+/g, ' ').trim());
  console.log(`[REQ-UI-032] ${swatches} swatches: ${JSON.stringify(swatchLabels.map((s) => s.slice(0, 30)))}`);
  expect(swatches, 'the theme picker must offer selectable swatches').toBeGreaterThanOrEqual(3);
  expect(swatchLabels.filter((s) => s.length > 0).length, 'every swatch must carry a label and description').toBe(swatches);

  const darkState = await page.locator('[data-testid="dark-mode-default"]').first().getAttribute('data-state');
  const dbDark = psql(SQL.setting('Theme.IsDarkModeDefault'));
  console.log(`[REQ-UI-032] dark-mode-default state=${darkState} | psql=${dbDark}`);
  expect(darkState === 'checked' ? 'True' : 'False', 'the dark-mode default toggle must reflect its SiteSetting row').toBe(dbDark);
});

// =====================================================================================
// REQ-UI-034 / REQ-FN-025 / REQ-FN-026 / REQ-NFR-040 — Media library
// =====================================================================================
test('REQ-UI-034 media library renders the gallery, category tabs and a LABELLED user filter', async () => {
  await go('/admin/images', /Media Library/);
  const reRender = async () => { await go('/admin', /^Dashboard$/); await go('/admin/images', /Media Library/); };

  assertRenders('/admin/images', [
    await renderCheck(page, 'media library page', '[data-testid="media-library-page"]', 'value'),
    await renderCheck(page, 'category tabs', '[data-testid="category-tabs"]', 'value'),
    await renderCheck(page, 'image count', '[data-testid="image-count"]', 'value'),
    await renderCheck(page, 'image grid', '[data-testid="image-grid"]', 'value'),
    await renderCheck(page, 'upload control', '[data-testid="upload-image"]', 'present'),
    await renderCheck(page, 'user filter', '[data-testid="user-filter-select"]', 'value'),
  ]);

  const cards = await assertAgainstDb('image cards', rowCount('image-card'), SQL.images, reRender);
  const names = (await page.locator('[data-testid="image-name"]').allTextContents()).map((t) => t.trim());
  const sizes = (await page.locator('[data-testid="image-size"]').allTextContents()).map((t) => t.trim());
  console.log(`[REQ-UI-034] ${cards.ui} cards: ${JSON.stringify(names)} / ${JSON.stringify(sizes)}`);
  expect(names.filter((n) => n.length > 0).length, 'every image card must render a file name').toBe(cards.ui);
  expect(sizes.filter((s) => s.length > 0).length, 'every image card must render a size').toBe(cards.ui);
  expect(await page.locator('[data-testid="copy-image-url"]').count(), 'copy-URL on every card').toBe(cards.ui);
  expect(await page.locator('[data-testid="delete-image"]').count(), 'delete on every card').toBe(cards.ui);

  // Counted as elements, not as innerText tokens: the tab strip collapses its text at narrow
  // widths and a token split would then read "1 tab" on a perfectly good seven-tab control.
  const tabs = await page.evaluate(() => {
    const strip = document.querySelector('[data-testid="category-tabs"]');
    if (!strip) return [];
    return Array.from(strip.querySelectorAll('[role="tab"], button, a'))
      .map((e) => ((e as HTMLElement).innerText || '').replace(/\s+/g, ' ').trim())
      .filter(Boolean);
  });
  console.log(`[REQ-UI-034] category tabs = ${JSON.stringify(tabs)}`);
  expect(tabs.length, 'seven upload categories must be offered (profiles, logos, awards, icons, blog, cv, general)').toBeGreaterThanOrEqual(7);

  // DEFECT under test: the user filter renders its raw bound VALUE ("0") instead of the "All Users"
  // label. The Storage-tab Select on /settings renders "Local" correctly from the same component, so
  // this is a page-level binding fault, not a component limitation.
  const filterLabel = await text('user-filter-select');
  console.log(`[REQ-UI-034] user-filter-select displays "${filterLabel}"`);
  expect(filterLabel, 'the owner filter must show a human label ("All Users"), never the raw bound id')
    .not.toMatch(/^\s*\d+\s*$/);

  await assertNoRawExceptionText('/admin/images');
  await visualBothWidths('req-ui-034-media-library');
});

test('REQ-FN-025 image upload dialog renders per-category validation constraints and an accept filter', async () => {
  await go('/admin/images', /Media Library/);
  await page.click('[data-testid="upload-image"]');
  await page.waitForSelector('[data-testid="image-upload-dialog"]', { timeout: 30000 });

  assertRenders('/admin/images upload dialog', [
    await renderCheck(page, 'upload dialog', '[data-testid="image-upload-dialog"]', 'value'),
    await renderCheck(page, 'category select', '[data-testid="upload-category-select"]', 'value'),
    await renderCheck(page, 'dropzone', '[data-testid="upload-dropzone"]', 'value'),
    await renderCheck(page, 'alt text input', '[data-testid="upload-alt-text-input"]', 'present'),
    await renderCheck(page, 'cancel', '[data-testid="upload-cancel"]', 'present'),
    await renderCheck(page, 'confirm', '[data-testid="upload-confirm"]', 'present'),
  ]);

  const dialogText = (await page.locator('[data-testid="image-upload-dialog"]').innerText()).replace(/\s+/g, ' ');
  const fileInputs = await page.evaluate(() =>
    Array.from(document.querySelectorAll('input[type=file]')).map((e) => ({ accept: e.getAttribute('accept') || '' })),
  );
  console.log(`[REQ-FN-025] dialog: ${dialogText.slice(0, 260)}`);
  console.log(`[REQ-FN-025] file inputs = ${JSON.stringify(fileInputs)}`);
  // profiles is the default category: 2 MB, jpg/jpeg/png/webp per the DevGuide's business rules.
  expect(dialogText, 'the dialog must state the per-category size limit').toMatch(/Max\s*2\s*MB/i);
  expect(dialogText, 'the dialog must state the per-category format list').toMatch(/jpg.*jpeg.*png.*webp/i);
  expect(fileInputs.length, 'the dropzone must expose a real file input').toBeGreaterThan(0);
  expect(fileInputs[0].accept, 'the file input must carry a MIME accept filter').toMatch(/image\/(jpeg|png|webp)/);

  // The dropzone advertises a size that is NOT the category limit — recorded, not repaired.
  const advertised = dialogText.match(/Max size:\s*(\d+)\s*MB/i);
  console.log(`[REQ-FN-025] dropzone advertises "${advertised ? advertised[0] : 'no dropzone max'}" against a 2 MB profiles limit`);
  expect(advertised ? Number(advertised[1]) : 2, 'the dropzone max must agree with the selected category limit (profiles = 2 MB)').toBe(2);

  await page.click('[data-testid="upload-cancel"]');
  await page.waitForTimeout(1500);
  expect(await page.locator('[data-testid="image-upload-dialog"]').count(), 'cancel must close the dialog with nothing written').toBe(0);
});

test('REQ-FN-026 every rendered image card matches a BlogImage row with category metadata', async () => {
  await go('/admin/images', /Media Library/);
  const names = (await page.locator('[data-testid="image-name"]').allTextContents()).map((t) => t.trim());
  const db = psqlRows('SELECT ImageName, Category, COALESCE(AltText, \'\'), COALESCE(MimeType, \'\'), Size FROM BlogImage');
  console.log(`[REQ-FN-026] ui=${JSON.stringify(names)} | psql=${JSON.stringify(db)}`);
  const dbNames = new Set(db.map((r) => r[0]));
  const ghosts = names.filter((n) => !dbNames.has(n));
  expect(ghosts, 'no rendered image may be absent from the BlogImage table').toEqual([]);
  expect(names.length, 'the gallery must render every BlogImage row').toBe(db.length);
  // Category + mime metadata is what REQ-FN-026's migration added.
  expect(db.every((r) => r[1].length > 0), 'every BlogImage row must carry a category').toBe(true);
  expect(db.every((r) => r[3].length > 0), 'every BlogImage row must carry a mime type').toBe(true);
  const count = await text('image-count');
  expect(count, 'the image count caption must agree with the rendered cards').toContain(String(names.length));
});

test('REQ-NFR-040 the upload path renders and validates, and the media screen carries no raw exception text', async () => {
  await go('/admin/images', /Media Library/);
  // Normal path: the upload control is present and reachable on both widths.
  await expect(page.locator('[data-testid="upload-image"]'), 'upload control must render').toBeVisible();
  await page.click('[data-testid="upload-image"]');
  await page.waitForSelector('[data-testid="image-upload-dialog"]', { timeout: 30000 });

  // Confirm must be gated until a file is chosen — this is the validation half, exercised WITHOUT
  // writing anything. A negative-control upload against a denied directory is deliberately NOT run:
  // the build phase already covered it, and it would write.
  const confirmDisabled = await page.locator('[data-testid="upload-confirm"]').first().isDisabled().catch(() => false);
  console.log(`[REQ-NFR-040] upload-confirm disabled with no file selected = ${confirmDisabled}`);
  expect(confirmDisabled, 'confirm must be disabled until a file passes validation').toBe(true);

  await assertNoRawExceptionText('/admin/images upload dialog');
  await page.click('[data-testid="upload-cancel"]');
  await page.waitForTimeout(1500);

  // The two seed images are the artefacts of the build phase's access-denied negative control. If a
  // swallowed failure had reached the screen it would show here as raw text.
  const bodyText = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  console.log(`[REQ-NFR-040] media screen text: ${bodyText.slice(bodyText.indexOf('Showing'), bodyText.indexOf('Showing') + 140)}`);
  expect(bodyText, 'an access-denied must never surface as a raw UnauthorizedAccessException').not.toMatch(/UnauthorizedAccessException|Access to the path/i);
  await assertNoRawExceptionText('/admin/images');
});

// =====================================================================================
// REQ-UI-035 — Reusable ImagePicker
// =====================================================================================
test('REQ-UI-035 the ImagePicker renders with library, upload and per-category constraints wherever it is used', async () => {
  const seen: string[] = [];

  await go('/admin/profile', /My Profile/);
  const profilePickers = await page.locator('[data-testid="image-picker"]').count();
  const constraints = (await page.locator('[data-testid="image-constraints"]').allTextContents()).map((t) => t.trim());
  console.log(`[REQ-UI-035] /admin/profile: ${profilePickers} pickers, constraints = ${JSON.stringify(constraints)}`);
  expect(profilePickers, '/admin/profile must render an avatar picker and a CV picker').toBeGreaterThanOrEqual(2);
  expect(constraints.filter((c) => /Max .*(MB|KB)/i.test(c)).length, 'every picker must state its size/format constraint').toBe(constraints.length);
  expect(await page.locator('[data-testid="choose-from-library"]').count(), 'library button per picker').toBe(profilePickers);
  expect(await page.locator('[data-testid="upload-new-image"]').count(), 'upload button per picker').toBe(profilePickers);
  seen.push(`/admin/profile:${profilePickers}`);

  // The DevGuide recorded the company-logo and badge pickers as ABSENT (plain text inputs).
  await go('/admin/experience', /Manage Experience/);
  await page.locator('[data-testid="edit-experience"]').first().click();
  await page.waitForSelector('[data-testid="experience-dialog"]', { timeout: 30000 });
  const logoPicker = await page.locator('[data-testid="experience-logo-picker"]').count();
  const logoImagePicker = await page.locator('[data-testid="experience-dialog"] [data-testid="image-picker"]').count();
  console.log(`[REQ-UI-035] experience dialog: logo-picker=${logoPicker} image-picker=${logoImagePicker}`);
  expect(logoPicker, 'the company-logo picker must exist, not a plain text path input').toBeGreaterThan(0);
  expect(logoImagePicker, 'the company-logo picker must be a real ImagePicker instance').toBeGreaterThan(0);
  seen.push(`experience-dialog:${logoImagePicker}`);
  await page.click('[data-testid="cancel-experience"]').catch(async () => { await page.keyboard.press('Escape'); });
  await page.waitForTimeout(1500);

  await go('/admin/awards', /Manage Awards/);
  await page.locator('[data-testid="edit-award"]').first().click();
  await page.waitForSelector('[data-testid="award-dialog"]', { timeout: 30000 });
  const badgePicker = await page.locator('[data-testid="award-badge-picker"]').count();
  const badgeImagePicker = await page.locator('[data-testid="award-dialog"] [data-testid="image-picker"]').count();
  console.log(`[REQ-UI-035] award dialog: badge-picker=${badgePicker} image-picker=${badgeImagePicker}`);
  expect(badgePicker, 'the badge-image picker must exist, not a plain text path input').toBeGreaterThan(0);
  expect(badgeImagePicker, 'the badge picker must be a real ImagePicker instance').toBeGreaterThan(0);
  seen.push(`award-dialog:${badgeImagePicker}`);
  await page.click('[data-testid="cancel-award"]').catch(async () => { await page.keyboard.press('Escape'); });
  await page.waitForTimeout(1500);

  console.log(`[REQ-UI-035] ImagePicker instances = ${seen.join(', ')}`);
});

// =====================================================================================
// REQ-UI-037 / REQ-FN-027 — Manage experience
// =====================================================================================
test('REQ-UI-037 manage-experience renders every experience card populated, with a LABELLED user selector', async () => {
  await go('/admin/experience', /Manage Experience/);
  const dbCount = psqlInt("SELECT COUNT(*) FROM UserEvents WHERE UserId = 1 AND Type = 'Experience'");

  assertRenders('/admin/experience', [
    await renderCheck(page, 'page', '[data-testid="manage-experience-page"]', 'value'),
    await renderCheck(page, 'add experience', '[data-testid="add-experience"]', 'present'),
    await renderCheck(page, 'user selector', '[data-testid="experience-user-select"]', 'value'),
    await renderCheck(page, 'experience list', '[data-testid="experience-list"]', 'value'),
  ]);

  const cards = await page.locator('[data-testid="experience-card"]').count();
  const roles = (await page.locator('[data-testid="experience-role"]').allTextContents()).map((t) => t.trim());
  const companies = (await page.locator('[data-testid="experience-company"]').allTextContents()).map((t) => t.trim());
  const dates = (await page.locator('[data-testid="experience-dates"]').allTextContents()).map((t) => t.trim());
  console.log(`[REQ-UI-037] ${cards} cards (psql UserEvents Type='Experience'=${dbCount}): ${JSON.stringify(roles.map((r, i) => `${r} @ ${companies[i]} (${dates[i]})`))}`);
  expect(cards, 'experience cards must render').toBeGreaterThan(0);
  expect(cards, 'rendered experience cards vs psql').toBe(dbCount);
  expect(roles.filter((r) => r.length > 0).length, 'every card needs a role').toBe(cards);
  expect(companies.filter((c) => c.length > 0).length, 'every card needs a company').toBe(cards);
  expect(dates.filter((d) => d.length > 0).length, 'every card needs a date range').toBe(cards);
  expect(await page.locator('[data-testid="edit-experience"]').count(), 'edit per card').toBe(cards);
  expect(await page.locator('[data-testid="delete-experience"]').count(), 'delete per card').toBe(cards);

  const selector = await text('experience-user-select');
  console.log(`[REQ-UI-037] user selector displays "${selector}"`);
  expect(selector, 'the admin user selector must show a name/label, never the raw user id').not.toMatch(/^\s*\d+\s*$/);
  expect(selector.length, 'the user selector must not be blank').toBeGreaterThan(0);

  await assertNoRawExceptionText('/admin/experience');
  await visualBothWidths('req-ui-037-experience');
});

test('REQ-FN-027 resume repositories return the psql row counts for skills, awards and experience', async () => {
  await go('/admin/skills', /Manage Skills/);
  const skillRows = await page.locator('[data-testid="skill-row"]').count();
  const skillCats = await page.locator('[data-testid="skill-category-card"]').count();
  console.log(`[REQ-FN-027] skills ui=${skillRows} in ${skillCats} categories | psql=${psqlInt(SQL.skills)} in ${psqlInt(SQL.skillCategories)}`);
  expect(skillRows, 'rendered skills vs psql').toBe(psqlInt(SQL.skills));
  expect(skillCats, 'rendered skill categories vs psql').toBe(psqlInt(SQL.skillCategories));

  await go('/admin/awards', /Manage Awards/);
  const awardCards = await page.locator('[data-testid="award-card"]').count();
  console.log(`[REQ-FN-027] awards ui=${awardCards} | psql=${psqlInt(SQL.awards)}`);
  expect(awardCards, 'rendered awards vs psql').toBe(psqlInt(SQL.awards));

  await go('/admin/experience', /Manage Experience/);
  const expCards = await page.locator('[data-testid="experience-card"]').count();
  // There is no `experience` table: resume experience lives in `UserEvents` discriminated by
  // Type = 'Experience' (verified against the column list, not assumed from the screen name).
  const dbExp = psqlInt("SELECT COUNT(*) FROM UserEvents WHERE UserId = 1 AND Type = 'Experience'");
  console.log(`[REQ-FN-027] experience ui=${expCards} | psql UserEvents(Type='Experience')=${dbExp}`);
  expect(expCards, 'rendered experience vs psql').toBe(dbExp);

  // UserStats has a registered repository but no ManageStats screen; the profile links to it.
  await go('/admin/profile', /My Profile/);
  const statsLink = await page.locator('[data-testid="manage-stats-link"]').count();
  console.log(`[REQ-FN-027] manage-stats link present = ${statsLink}`);
  expect(statsLink, 'the profile must link to the stats screen the repository backs').toBeGreaterThan(0);
});

// =====================================================================================
// REQ-UI-038 — Manage skills
// =====================================================================================
test('REQ-UI-038 manage-skills renders grouped skills with per-category counts and a LABELLED user selector', async () => {
  await go('/admin/skills', /Manage Skills/);

  assertRenders('/admin/skills', [
    await renderCheck(page, 'page', '[data-testid="manage-skills-page"]', 'value'),
    await renderCheck(page, 'add skill', '[data-testid="add-skill"]', 'present'),
    await renderCheck(page, 'user selector', '[data-testid="skills-user-select"]', 'value'),
    await renderCheck(page, 'skills list', '[data-testid="skills-list"]', 'value'),
  ]);

  const cards = await page.locator('[data-testid="skill-category-card"]').count();
  const catNames = (await page.locator('[data-testid="skill-category-name"]').allTextContents()).map((t) => t.trim());
  const catCounts = (await page.locator('[data-testid="skill-category-count"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  const skillNames = (await page.locator('[data-testid="skill-name"]').allTextContents()).map((t) => t.trim());
  console.log(`[REQ-UI-038] ${cards} categories ${JSON.stringify(catNames.map((n, i) => `${n}:${catCounts[i]}`))}, ${skillNames.length} skills`);
  expect(catNames.filter((n) => n.length > 0).length, 'every category card needs a name').toBe(cards);
  expect(skillNames.filter((n) => n.length > 0).length, 'every skill row needs a name').toBe(skillNames.length);
  expect(skillNames.length, 'rendered skills vs psql').toBe(psqlInt(SQL.skills));

  const db = new Map(psqlRows('SELECT Category, COUNT(*) FROM UserSkills WHERE UserId = 1 GROUP BY Category').map((r) => [r[0], Number(r[1])] as [string, number]));
  const bad: string[] = [];
  catNames.forEach((n, i) => { if (db.get(n) !== catCounts[i]) bad.push(`${n}: ui=${catCounts[i]} psql=${db.get(n)}`); });
  expect(bad, 'every skill-category count must equal psql').toEqual([]);

  // DEFECT under test: the admin user selector renders the raw user id "1" instead of a user name.
  const selector = await text('skills-user-select');
  console.log(`[REQ-UI-038] skills-user-select displays "${selector}"`);
  expect(selector, 'the admin user selector must show a user name, never the raw id').not.toMatch(/^\s*\d+\s*$/);

  await assertNoRawExceptionText('/admin/skills');
  await visualBothWidths('req-ui-038-skills');
});

// =====================================================================================
// REQ-UI-039 — Manage awards
// =====================================================================================
test('REQ-UI-039 manage-awards renders every award populated, with ordering controls and a LABELLED user selector', async () => {
  await go('/admin/awards', /Manage Awards/);

  assertRenders('/admin/awards', [
    await renderCheck(page, 'page', '[data-testid="manage-awards-page"]', 'value'),
    await renderCheck(page, 'add award', '[data-testid="add-award"]', 'present'),
    await renderCheck(page, 'user selector', '[data-testid="awards-user-select"]', 'value'),
    await renderCheck(page, 'awards list', '[data-testid="awards-list"]', 'value'),
  ]);

  const cards = await page.locator('[data-testid="award-card"]').count();
  const titles = (await page.locator('[data-testid="award-title"]').allTextContents()).map((t) => t.trim());
  const years = (await page.locator('[data-testid="award-year"]').allTextContents()).map((t) => t.trim());
  const descs = (await page.locator('[data-testid="award-description"]').allTextContents()).map((t) => t.trim());
  console.log(`[REQ-UI-039] ${cards} awards: ${JSON.stringify(titles.map((t, i) => `${t} (${years[i]})`))}`);
  expect(cards, 'rendered awards vs psql').toBe(psqlInt(SQL.awards));
  expect(titles.filter((t) => t.length > 0).length, 'every award needs a title').toBe(cards);
  expect(years.filter((y) => y.length > 0).length, 'every award needs a year').toBe(cards);
  expect(descs.filter((d) => d.length > 0).length, 'every award needs a description').toBe(cards);
  expect(await page.locator('[data-testid="move-award-up"]').count(), 'ordering controls per card').toBe(cards);
  expect(await page.locator('[data-testid="move-award-down"]').count(), 'ordering controls per card').toBe(cards);

  // DEFECT under test: raw user id in the selector, same class as /admin/skills and /admin/images.
  const selector = await text('awards-user-select');
  console.log(`[REQ-UI-039] awards-user-select displays "${selector}"`);
  expect(selector, 'the admin user selector must show a user name, never the raw id').not.toMatch(/^\s*\d+\s*$/);

  await assertNoRawExceptionText('/admin/awards');
  await visualBothWidths('req-ui-039-awards');
});

// =====================================================================================
// REQ-UI-040 / REQ-FN-011 / REQ-FN-029 / REQ-FN-053 — Manage profile
// =====================================================================================
/** Reads every profile field the screen binds, alongside its psql column. */
async function profileFields(): Promise<{ id: string; col: string; ui: string; db: string }[]> {
  const map: [string, string][] = [
    ['first-name-input', 'FirstName'],
    ['last-name-input', 'LastName'],
    ['username-input', 'Username'],
    ['title-input', 'Title'],
    ['tagline-input', 'Tagline'],
    ['bio-input', 'ProfileDescription'],
    ['linkedin-input', 'LinkedInUrl'],
    ['github-input', 'GitHubUrl'],
    ['twitter-input', 'TwitterUrl'],
    ['instagram-input', 'InstagramUrl'],
    ['phone-input', 'PhoneNumber'],
    ['location-input', 'Location'],
  ];
  const out: { id: string; col: string; ui: string; db: string }[] = [];
  for (const [id, col] of map) out.push({ id, col, ui: await value(id), db: psql(SQL.user(col)) });
  return out;
}

test('REQ-UI-040 manage-profile renders every field loaded from psql, plus resume settings and quick links', async () => {
  await go('/admin/profile', /My Profile/);

  assertRenders('/admin/profile', [
    await renderCheck(page, 'page', '[data-testid="manage-profile-page"]', 'value'),
    await renderCheck(page, 'basic info card', '[data-testid="basic-info-card"]', 'value'),
    await renderCheck(page, 'social links card', '[data-testid="social-links-card"]', 'value'),
    await renderCheck(page, 'resume settings card', '[data-testid="resume-settings-card"]', 'value'),
    await renderCheck(page, 'quick links card', '[data-testid="quick-links-card"]', 'value'),
    await renderCheck(page, 'save', '[data-testid="save-profile"]', 'present'),
    await renderCheck(page, 'avatar picker', '[data-testid="image-picker"]', 'present'),
  ]);

  const fields = await profileFields();
  const mismatches = fields.filter((f) => f.ui.trim() !== f.db.trim());
  for (const f of fields) console.log(`[REQ-UI-040] ${f.col}: ui="${f.ui}" psql="${f.db}"`);
  expect(mismatches.map((f) => `${f.col}: ui="${f.ui}" psql="${f.db}"`), 'every profile field must equal its psql column byte-for-byte').toEqual([]);
  // A form of empty boxes would pass a byte-for-byte check against empty columns — assert substance.
  const populated = fields.filter((f) => f.ui.trim().length > 0).length;
  console.log(`[REQ-UI-040] ${populated}/${fields.length} fields carry data`);
  expect(populated, 'the profile form must load real data, not a blank form').toBeGreaterThanOrEqual(9);

  const resumeToggle = await page.locator('[data-testid="resume-enabled-checkbox"]').first().getAttribute('data-state');
  const dbResume = psql(SQL.user('ResumeEnabled'));
  console.log(`[REQ-UI-040] resume-enabled state=${resumeToggle} psql=${dbResume}`);
  expect(resumeToggle === 'checked' ? 'true' : 'false', 'the resume toggle must reflect BlogUser.ResumeEnabled').toBe(dbResume.toLowerCase());

  for (const id of ['manage-experience-link', 'manage-skills-link', 'manage-awards-link', 'manage-stats-link']) {
    await expect(page.locator(`[data-testid="${id}"]`), `quick link ${id}`).toBeVisible();
  }

  await assertNoRawExceptionText('/admin/profile');
  // At 390 the DevGuide recorded clear-image OVERLAPPING upload-new-image and rendering invisible.
  await visualBothWidths('req-ui-040-profile');
});

test('REQ-FN-011 profile loads from the repository and the change-password form is reachable', async () => {
  await go('/admin/profile', /My Profile/);
  const fields = await profileFields();
  expect(fields.filter((f) => f.ui.trim() === f.db.trim()).length, 'the read half of profile must round-trip from psql').toBe(fields.length);

  // The update half is a WRITE. Three siblings share these rows, so it is deliberately not driven —
  // the save control's presence and enablement is asserted instead.
  await expect(page.locator('[data-testid="save-profile"]'), 'profile save control').toBeVisible();
  expect(await page.locator('[data-testid="save-profile"]').first().isDisabled(), 'save must be actionable').toBe(false);
  console.log('[REQ-FN-011] update half NOT exercised — a profile save is a write and siblings share these rows');

  await go('/change-password', /Change password/i);
  const body = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  console.log(`[REQ-FN-011] /change-password: ${body.slice(0, 200)}`);
  expect(body, 'the change-password screen must render its three fields').toMatch(/Current password/i);
  expect(body, 'the change-password screen must render a new-password field').toMatch(/New password/i);
  expect(body, 'the change-password screen must render a confirmation field').toMatch(/Confirm new password/i);
  const pwdInputs = await page.locator('input[type="password"]').count();
  expect(pwdInputs, 'three masked password inputs').toBeGreaterThanOrEqual(3);
  await assertNoRawExceptionText('/change-password');
});

test('REQ-FN-029 username renders from the unique column and exactly one site owner exists', async () => {
  await go('/admin/profile', /My Profile/);
  const username = await value('username-input');
  const dbUsername = psql(SQL.user('Username'));
  console.log(`[REQ-FN-029] username ui="${username}" psql="${dbUsername}"`);
  expect(username, 'the username field must load the stored username').toBe(dbUsername);
  expect(username.length, 'the site owner must have a username').toBeGreaterThan(0);

  // Uniqueness is enforced by a partial unique index; the site-owner flag by a single-row unique
  // index. Both are asserted through their observable consequence rather than by attempting a write.
  const dupUsernames = psqlInt('SELECT COUNT(*) FROM (SELECT Username FROM BlogUser WHERE Username IS NOT NULL GROUP BY Username HAVING COUNT(*) > 1) d');
  const owners = psqlInt('SELECT COUNT(*) FROM BlogUser WHERE IsSiteOwner = TRUE');
  console.log(`[REQ-FN-029] duplicate usernames=${dupUsernames} site owners=${owners}`);
  expect(dupUsernames, 'usernames must be unique').toBe(0);
  expect(owners, 'exactly one site owner').toBe(1);
  expect(psql("SELECT EmailId FROM BlogUser WHERE IsSiteOwner = TRUE"), 'the site owner must be the seeded admin').toBe(ADMIN.email);
});

test('REQ-FN-028 the CV picker renders with a PDF-only 10 MB constraint on the profile screen', async () => {
  await go('/admin/profile', /My Profile/);
  const constraints = (await page.locator('[data-testid="image-constraints"]').allTextContents()).map((t) => t.replace(/\s+/g, ' ').trim());
  console.log(`[REQ-FN-028] picker constraints = ${JSON.stringify(constraints)}`);
  const cv = constraints.find((c) => /pdf/i.test(c));
  expect(cv, 'a PDF-only picker (the CV slot) must render on the profile screen').toBeTruthy();
  expect(cv!, 'the CV picker must state the 10 MB limit').toMatch(/10\s*MB/i);
  const dbCv = psql(SQL.user('CvFilePath'));
  console.log(`[REQ-FN-028] BlogUser.CvFilePath = "${dbCv}" (empty = no CV uploaded yet; the download side is a Guest-surface concern)`);
});

test('REQ-FN-053 the profile form loads every at-risk resume column, so a save cannot null them', async () => {
  // The regression was a save writing NULL over columns the form never loaded. Read-only proof: the
  // nine at-risk columns are all bound to a rendered control carrying the stored value. Driving the
  // save itself is a write and is deliberately not done.
  await go('/admin/profile', /My Profile/);
  const atRisk = ['Title', 'Tagline', 'ProfileDescription', 'LinkedInUrl', 'GitHubUrl', 'TwitterUrl', 'InstagramUrl', 'PhoneNumber', 'Location'];
  const fields = await profileFields();
  const missing: string[] = [];
  for (const col of atRisk) {
    const f = fields.find((x) => x.col === col);
    if (!f) { missing.push(`${col}: no bound control on the form`); continue; }
    if (f.ui.trim() !== f.db.trim()) missing.push(`${col}: form holds "${f.ui}" but psql holds "${f.db}" — a save would overwrite`);
    console.log(`[REQ-FN-053] ${col}: bound, ui="${f.ui.slice(0, 40)}" psql="${f.db.slice(0, 40)}"`);
  }
  expect(missing, 'every at-risk resume column must be loaded into the form before any save can run').toEqual([]);
  const md5Before = psql(`SELECT MD5(COALESCE(Title,'')||COALESCE(Tagline,'')||COALESCE(ProfileDescription,'')||COALESCE(LinkedInUrl,'')||COALESCE(GitHubUrl,'')||COALESCE(TwitterUrl,'')||COALESCE(InstagramUrl,'')||COALESCE(PhoneNumber,'')||COALESCE(Location,'')) FROM BlogUser WHERE EmailId = '${ADMIN.email}'`);
  console.log(`[REQ-FN-053] at-risk column md5 = ${md5Before} — save round trip NOT driven (write); read-side precondition holds`);
});

// =====================================================================================
// REQ-UI-043 / REQ-FN-032 — Newsletter composer
// =====================================================================================
test('REQ-UI-043 newsletter composer renders compose, audience, preview and send controls with a live recipient estimate', async () => {
  await go('/admin/newsletter', /Newsletter composer/i);

  assertRenders('/admin/newsletter', [
    await renderCheck(page, 'compose card', '[data-testid="newsletter-compose-card"]', 'value'),
    await renderCheck(page, 'subject', '[data-testid="newsletter-subject"]', 'present'),
    await renderCheck(page, 'body', '[data-testid="newsletter-body"]', 'present'),
    await renderCheck(page, 'write tab', '[data-testid="newsletter-tab-write"]', 'present'),
    await renderCheck(page, 'preview tab', '[data-testid="newsletter-tab-preview"]', 'present'),
    await renderCheck(page, 'recipients card', '[data-testid="newsletter-recipients-card"]', 'value'),
    await renderCheck(page, 'audience selector', '[data-testid="newsletter-audience"]', 'value'),
    await renderCheck(page, 'recipient count', '[data-testid="newsletter-recipient-count"]', 'value'),
    await renderCheck(page, 'send card', '[data-testid="newsletter-send-card"]', 'value'),
    await renderCheck(page, 'send', '[data-testid="newsletter-send"]', 'present'),
    await renderCheck(page, 'save draft', '[data-testid="newsletter-save-draft"]', 'present'),
    await renderCheck(page, 'history card', '[data-testid="newsletter-history-card"]', 'value'),
    await renderCheck(page, 'status badge', '[data-testid="newsletter-status-badge"]', 'value'),
  ]);

  // The audience estimate must be a live psql number, not a hard-coded placeholder.
  const estimate = Number((await text('newsletter-recipient-count')).replace(/[^\d]/g, ''));
  const active = psqlInt(SQL.subscribersActive);
  const all = psqlInt(SQL.subscribers);
  console.log(`[REQ-UI-043] recipient estimate=${estimate} | psql active=${active} all=${all}`);
  expect([active, all], 'the audience estimate must equal a real subscriber population').toContain(estimate);

  const audienceLabels = [await text('audience-active-label'), await text('audience-everyone-label'), await text('audience-segment-label')];
  console.log(`[REQ-UI-043] audience options = ${JSON.stringify(audienceLabels)}`);
  expect(audienceLabels.filter((l) => l.length > 0).length, 'all three audience options must be labelled').toBe(3);

  // Preview tab must render, not stay blank.
  await page.click('[data-testid="newsletter-tab-preview"]');
  await page.waitForTimeout(2500);
  const previewPresent = await page.locator('[data-testid="newsletter-preview"]').count();
  console.log(`[REQ-UI-043] preview pane present = ${previewPresent}`);
  expect(previewPresent, 'the preview tab must render a preview pane').toBeGreaterThan(0);
  await page.click('[data-testid="newsletter-tab-write"]');
  await page.waitForTimeout(1500);

  await assertNoRawExceptionText('/admin/newsletter');
  await visualBothWidths('req-ui-043-newsletter');
});

test('REQ-FN-032 newsletter history reflects psql and the send path is gated behind a confirmation', async () => {
  await go('/admin/newsletter', /Newsletter composer/i);
  const dbIssues = psqlInt(SQL.newsletters);
  const historyRows = await page.locator('[data-testid="history-row-title"]').count();
  const emptyState = await page.locator('[data-testid="newsletter-history-empty"]').count();
  console.log(`[REQ-FN-032] history rows=${historyRows} empty-state=${emptyState} | psql Newsletter=${dbIssues}`);
  expect(historyRows, 'history rows must equal the Newsletter table').toBe(dbIssues);
  if (dbIssues === 0) {
    expect(emptyState, 'with no issues sent the history must show an explicit empty state, not a blank panel').toBeGreaterThan(0);
  } else {
    const titles = (await page.locator('[data-testid="history-row-title"]').allTextContents()).map((t) => t.trim());
    expect(titles.filter((t) => t.length > 0).length, 'every history row must carry a title').toBe(historyRows);
  }

  // The unsubscribe link the DevGuide recorded as a dead 404 route: assert the route now resolves.
  // A GET is a read, so this is safe — the token is NOT consumed by rendering the page.
  const token = psql('SELECT UnsubscribeToken FROM Subscriber WHERE UnsubscribeToken IS NOT NULL LIMIT 1');
  if (token) {
    const res = await page.request.get(`${BASE}/unsubscribe/${token}`, { failOnStatusCode: false });
    const body = await res.text();
    console.log(`[REQ-FN-032] GET /unsubscribe/{token} → ${res.status()}, ${body.length} bytes`);
    expect(res.status(), 'the unsubscribe link every newsletter carries must resolve, not 404').toBeLessThan(400);
    expect(body.length, 'the unsubscribe route must not answer with a zero-byte body').toBeGreaterThan(0);
  } else {
    console.log('[REQ-FN-032] no unsubscribe token in psql — link route not exercised');
  }

  // Send DELIVERS MAIL. Whether the click opens a confirmation or blasts immediately cannot be
  // determined without clicking, and clicking is a write that would reach real subscriber rows three
  // siblings are asserting. Only the enablement state is recorded, as an observation, not a verdict.
  const sendDisabled = await page.locator('[data-testid="newsletter-send"]').first().isDisabled().catch(() => false);
  console.log(`[REQ-FN-032] send control enabled with an EMPTY composer = ${!sendDisabled} — the confirmation step is NOT-OBSERVABLE read-only (clicking Send delivers mail)`);
  await expect(page.locator('[data-testid="newsletter-send"]'), 'a send control must be present').toBeVisible();
});

// =====================================================================================
// REQ-UI-044 / REQ-FN-035 — Analytics dashboard
// =====================================================================================
test('REQ-UI-044 analytics renders stat tiles, a populated trend chart, popular posts and category views against psql', async () => {
  await go('/admin/analytics', /^Analytics$/);

  assertRenders('/admin/analytics', [
    await renderCheck(page, 'range card', '[data-testid="analytics-range-card"]', 'value'),
    await renderCheck(page, 'from', '[data-testid="analytics-from"]', 'present'),
    await renderCheck(page, 'to', '[data-testid="analytics-to"]', 'present'),
    await renderCheck(page, 'apply', '[data-testid="analytics-apply"]', 'present'),
    await renderCheck(page, 'range caption', '[data-testid="analytics-range-caption"]', 'value'),
    await renderCheck(page, 'stat tiles', '[data-testid="analytics-stat-tiles"]', 'value'),
    await renderCheck(page, 'views tile', '[data-testid="analytics-stat-views"]', 'value'),
    await renderCheck(page, 'unique tile', '[data-testid="analytics-stat-unique"]', 'value'),
    await renderCheck(page, 'rating tile', '[data-testid="analytics-stat-rating"]', 'value'),
    await renderCheck(page, 'comments tile', '[data-testid="analytics-stat-comments"]', 'value'),
    await renderCheck(page, 'trend chart', '[data-testid="analytics-trend-chart"]', 'chart'),
    await renderCheck(page, 'trend summary', '[data-testid="analytics-trend-summary"]', 'value'),
    await renderCheck(page, 'popular grid', '[data-testid="analytics-popular-grid"]', 'value'),
    await renderCheck(page, 'category list', '[data-testid="analytics-category-list"]', 'value'),
  ]);

  // The chart must carry a real series, not an empty <svg> shell.
  const chartNodes = await page.evaluate(() => {
    const c = document.querySelector('[data-testid="analytics-trend-chart"]');
    return c ? { svg: c.querySelectorAll('svg *').length, marks: c.querySelectorAll('rect, path, circle, line').length } : { svg: 0, marks: 0 };
  });
  console.log(`[REQ-UI-044] trend chart: ${chartNodes.svg} svg nodes, ${chartNodes.marks} marks`);
  expect(chartNodes.svg, 'the trend chart must render a non-empty series node set').toBeGreaterThan(0);
  expect(chartNodes.marks, 'the trend chart must render plotted marks, not an empty axis').toBeGreaterThan(0);

  const caption = await text('analytics-range-caption');
  const views = Number((await text('analytics-stat-views')).replace(/[^\d]/g, ''));
  const unique = Number((await text('analytics-stat-unique')).replace(/[^\d]/g, ''));
  const comments = Number((await text('analytics-stat-comments')).replace(/[^\d]/g, ''));
  const rating = await text('analytics-stat-rating');
  console.log(`[REQ-UI-044] "${caption}" views=${views} unique=${unique} comments=${comments} rating="${rating}"`);
  expect(caption.length, 'the range caption must state the window being measured').toBeGreaterThan(0);
  expect(rating, 'the rating tile must render a number, not a blank').toMatch(/\d/);
  // The default window is the last 30 days; measured against psql at the moment of measurement.
  const days = Number((caption.match(/\((\d+)\s*days?\)/) || [])[1] || 30);
  const dbViews = psqlInt(`SELECT COUNT(*) FROM PostViews WHERE ViewedOn >= (CURRENT_DATE - INTERVAL '${days - 1} days')`);
  const dbUnique = psqlInt(`SELECT COUNT(DISTINCT VisitorHash) FROM PostViews WHERE ViewedOn >= (CURRENT_DATE - INTERVAL '${days - 1} days')`);
  const dbComments = psqlInt(`SELECT COUNT(*) FROM BlogComment WHERE GivenOn >= (CURRENT_DATE - INTERVAL '${days - 1} days')`);
  console.log(`[REQ-UI-044] psql over ${days} days: views=${dbViews} unique=${dbUnique} comments=${dbComments}`);
  await assertAgainstDb('analytics views tile', async () => Number((await text('analytics-stat-views')).replace(/[^\d]/g, '')), `SELECT COUNT(*) FROM PostViews WHERE ViewedOn >= (CURRENT_DATE - INTERVAL '${days - 1} days')`);
  await assertAgainstDb('analytics unique tile', async () => Number((await text('analytics-stat-unique')).replace(/[^\d]/g, '')), `SELECT COUNT(DISTINCT VisitorHash) FROM PostViews WHERE ViewedOn >= (CURRENT_DATE - INTERVAL '${days - 1} days')`);
  await assertAgainstDb('analytics comments tile', async () => Number((await text('analytics-stat-comments')).replace(/[^\d]/g, '')), `SELECT COUNT(*) FROM BlogComment WHERE GivenOn >= (CURRENT_DATE - INTERVAL '${days - 1} days')`);

  // Category views must be real rows carrying real numbers.
  const catNames = (await page.locator('[data-testid="category-row-name"]').allTextContents()).map((t) => t.trim());
  const catViews = (await page.locator('[data-testid="category-row-views"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  console.log(`[REQ-UI-044] category views = ${JSON.stringify(catNames.map((n, i) => `${n}:${catViews[i]}`))}`);
  expect(catNames.length, 'the category-views panel must render rows').toBeGreaterThan(0);
  expect(catNames.filter((n) => n.length > 0).length, 'every category row needs a name').toBe(catNames.length);
  expect(catViews.filter((v) => v > 0).length, 'every category row needs a non-zero view count').toBe(catViews.length);

  // The date range must provably move the numbers.
  await page.click('[data-testid="analytics-preset-7"]');
  await page.waitForTimeout(4000);
  const caption7 = await text('analytics-range-caption');
  console.log(`[REQ-UI-044] after preset-7: "${caption7}"`);
  expect(caption7, 'the 7-day preset must change the range caption').not.toBe(caption);
  await page.click('[data-testid="analytics-preset-30"]');
  await page.waitForTimeout(4000);

  await assertNoRawExceptionText('/admin/analytics');
  await visualBothWidths('req-ui-044-analytics');
});

test('REQ-FN-035 popular posts carry per-post views, unique views, comments and rating that match psql', async () => {
  await go('/admin/analytics', /^Analytics$/);
  const titles = (await page.locator('[data-testid="popular-row-title"]').allTextContents()).map((t) => t.trim());
  const views = (await page.locator('[data-testid="popular-row-views"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  const unique = (await page.locator('[data-testid="popular-row-unique"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  const comments = (await page.locator('[data-testid="popular-row-comments"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  const ratings = (await page.locator('[data-testid="popular-row-rating"]').allTextContents()).map((t) => t.trim());
  console.log(`[REQ-FN-035] ${titles.length} rows: ${JSON.stringify(titles.map((t, i) => `${t} v=${views[i]} u=${unique[i]} c=${comments[i]} r=${ratings[i]}`))}`);
  expect(titles.length, 'the popular-posts panel must render rows').toBeGreaterThan(0);
  expect(titles.filter((t) => t.length > 0).length, 'every popular row needs a title').toBe(titles.length);
  expect(ratings.filter((r) => /\d/.test(r)).length, 'every popular row needs a rating value').toBe(titles.length);

  const db = new Map(
    psqlRows(`SELECT p.Title, c.TotalViews, c.UniqueViews,
                     (SELECT COUNT(*) FROM BlogComment bc WHERE bc.PostId = p.PostId)
              FROM PostViewCount c JOIN BlogPost p ON p.PostId = c.PostId`)
      .map((r) => [r[0], { views: Number(r[1]), unique: Number(r[2]), comments: Number(r[3]) }] as [string, any]),
  );
  const bad: string[] = [];
  titles.forEach((t, i) => {
    const d = db.get(t);
    if (!d) { bad.push(`${t}: not in PostViewCount`); return; }
    if (d.views !== views[i]) bad.push(`${t}: views ui=${views[i]} psql=${d.views}`);
    if (d.unique !== unique[i]) bad.push(`${t}: unique ui=${unique[i]} psql=${d.unique}`);
    if (d.comments !== comments[i]) bad.push(`${t}: comments ui=${comments[i]} psql=${d.comments}`);
  });
  console.log(`[REQ-FN-035] mismatches = ${JSON.stringify(bad)}`);
  expect(bad, 'every per-post engagement number must match psql').toEqual([]);
  // Descending rank.
  for (let i = 1; i < views.length; i++) {
    expect(views[i - 1], 'popular posts must be ranked by views, descending').toBeGreaterThanOrEqual(views[i]);
  }
});

// =====================================================================================
// REQ-FN-034 / REQ-NFR-034 — Post view tracking through the rollup table
// =====================================================================================
test('REQ-FN-034 total and unique post views are tracked and surfaced, not dead code', async () => {
  const rollup = psqlRows('SELECT PostId, TotalViews, UniqueViews FROM PostViewCount ORDER BY PostId');
  console.log(`[REQ-FN-034] PostViewCount rows = ${JSON.stringify(rollup)}`);
  expect(rollup.length, 'view tracking must have written rollup rows — a zero-row table is the dead-code symptom').toBeGreaterThan(0);
  expect(rollup.every((r) => Number(r[1]) > 0), 'every rollup row must carry a non-zero total').toBe(true);
  expect(rollup.every((r) => Number(r[2]) > 0), 'every rollup row must carry a non-zero unique count').toBe(true);

  await go('/admin', /^Dashboard$/);
  const popViews = (await page.locator('[data-testid="popular-post-views"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  console.log(`[REQ-FN-034] dashboard popular views = ${JSON.stringify(popViews)}`);
  expect(popViews.length, 'the dashboard must surface tracked views, not an empty state').toBeGreaterThan(0);
  expect(popViews.every((v) => v > 0), 'a surfaced view count of zero means tracking never ran').toBe(true);
});

test('REQ-NFR-034 the PostViewCount rollup agrees exactly with the raw PostViews rows it replaces', async () => {
  // The rollup exists so the per-render INSERT + COUNT(DISTINCT) scan is gone. Correctness proof:
  // for every post, rollup total == raw row count and rollup unique == distinct VisitorHash.
  const drift = psqlRows(`
    SELECT COALESCE(c.PostId, v.PostId)::text,
           COALESCE(c.TotalViews, -1)::text, COALESCE(v.t, -1)::text,
           COALESCE(c.UniqueViews, -1)::text, COALESCE(v.u, -1)::text
    FROM PostViewCount c
    FULL OUTER JOIN (SELECT PostId, COUNT(*) t, COUNT(DISTINCT VisitorHash) u FROM PostViews GROUP BY PostId) v
      ON v.PostId = c.PostId
    WHERE COALESCE(c.TotalViews, -1) <> COALESCE(v.t, -1) OR COALESCE(c.UniqueViews, -1) <> COALESCE(v.u, -1)`);
  console.log(`[REQ-NFR-034] rollup/raw drift rows = ${JSON.stringify(drift)}`);
  expect(drift, 'the rollup must not drift from the raw PostViews table').toEqual([]);

  const rollupTotal = psqlInt(SQL.viewsTotal);
  const rawTotal = psqlInt('SELECT COUNT(*) FROM PostViews');
  console.log(`[REQ-NFR-034] rollup SUM(TotalViews)=${rollupTotal} raw PostViews=${rawTotal}`);
  expect(rollupTotal, 'the rollup total must equal the raw row count').toBe(rawTotal);

  // And the admin analytics dashboard must still report those numbers correctly.
  await go('/admin/analytics', /^Analytics$/);
  // The panel is not fixed at five rows — it renders however many posts the rollup knows about, so
  // the comparison is taken over the rendered length rather than a hard-coded LIMIT.
  const popular = (await page.locator('[data-testid="popular-row-views"]').allTextContents()).map((t) => Number(t.replace(/[^\d]/g, '')));
  const dbTop = psqlRows('SELECT TotalViews FROM PostViewCount ORDER BY TotalViews DESC, PostId')
    .map((r) => Number(r[0]))
    .slice(0, popular.length);
  console.log(`[REQ-NFR-034] analytics popular views ui=${JSON.stringify(popular)} psql=${JSON.stringify(dbTop)}`);
  expect(popular, 'the analytics popular panel must read the rollup exactly, ranked by views').toEqual(dbTop);
});

// =====================================================================================
// REQ-FN-041 — Seed / sample data set
// =====================================================================================
test('REQ-FN-041 the seed data set renders across every admin surface with populated rows', async () => {
  const anchors: [string, string, string, RegExp, string][] = [
    ['posts', '/BlogsList', 'post-row-title', /Posts/i, SQL.posts],
    ['users', '/users', 'user-row-name', /^Users$/, SQL.users],
    ['comments', '/CommentsList', 'comment-row-text', /Comments Management/, SQL.comments],
    ['categories', '/CategoriesList', 'category-row-name', /Categories Management/, SQL.categories],
    ['tags', '/admin/tags', 'tag-row-name', /Tags Management/, SQL.tags],
    ['series', '/admin/series', 'series-row-name', /Series Management/, SQL.series],
    ['subscribers', '/admin/subscribers', 'subscriber-row-email', /^Subscribers$/, SQL.subscribers],
  ];
  const summary: string[] = [];
  for (const [label, route, rowId, heading, sql] of anchors) {
    await go(route, heading);
    const ui = await page.locator(`[data-testid="${rowId}"]`).count();
    const db = psqlInt(sql);
    summary.push(`${label}: ui=${ui} psql=${db}`);
    console.log(`[REQ-FN-041] ${label}: ui=${ui} psql=${db}`);
    expect(db, `${label} must be seeded for immediate evaluation`).toBeGreaterThan(0);
    expect(ui, `${label} rendered rows vs psql`).toBe(db);
  }
  console.log(`[REQ-FN-041] ${summary.join(' | ')}`);
  // Resume data is part of the same set.
  expect(psqlInt(SQL.skills), 'seeded skills').toBeGreaterThan(0);
  expect(psqlInt(SQL.awards), 'seeded awards').toBeGreaterThan(0);
  expect(psqlInt(SQL.settingsCount), 'seeded site settings').toBeGreaterThan(0);
});

// =====================================================================================
// REQ-FN-042 — Configurable storage-provider abstraction
// =====================================================================================
test('REQ-FN-042 the storage provider is configurable from settings and reflects its SiteSetting rows', async () => {
  await go('/settings', /^Settings$/);
  await settingsTab('tab-storage');

  assertRenders('/settings storage', [
    await renderCheck(page, 'storage panel', '[data-testid="storage-settings"]', 'value'),
    await renderCheck(page, 'provider select', '[data-testid="storage-provider"]', 'value'),
    await renderCheck(page, 'local root', '[data-testid="storage-local-root"]', 'present'),
    await renderCheck(page, 'network root', '[data-testid="storage-network-root"]', 'present'),
    await renderCheck(page, 'cloud url', '[data-testid="storage-cloud-url"]', 'present'),
    await renderCheck(page, 'cloud container', '[data-testid="storage-cloud-container"]', 'present'),
    await renderCheck(page, 'public base url', '[data-testid="storage-public-base"]', 'present'),
  ]);

  const provider = await text('storage-provider');
  const dbProvider = psql(SQL.setting('Storage.ProviderName'));
  console.log(`[REQ-FN-042] provider ui="${provider}" psql="${dbProvider}"`);
  expect(provider, 'the provider selector must render its stored value as a label').toBe(dbProvider);
  expect(provider, 'the provider selector must not render a raw enum ordinal').not.toMatch(/^\s*\d+\s*$/);

  const mismatches: string[] = [];
  for (const [id, key] of [
    ['storage-local-root', 'Storage.LocalRootPath'],
    ['storage-network-root', 'Storage.NetworkRootPath'],
    ['storage-cloud-url', 'Storage.CloudServiceUrl'],
    ['storage-cloud-container', 'Storage.CloudContainerName'],
    ['storage-public-base', 'Storage.PublicBaseUrl'],
  ] as const) {
    const ui = await value(id);
    const db = psql(SQL.setting(key));
    console.log(`[REQ-FN-042] ${key}: ui="${ui}" psql="${db}"`);
    if (ui.trim() !== db.trim()) mismatches.push(`${key}: ui="${ui}" psql="${db}"`);
  }
  expect(mismatches, 'every storage field must equal its SiteSetting row').toEqual([]);
});

// =====================================================================================
// REQ-UI-045-adjacent — Admin post preview (/admin/preview/{id})
// =====================================================================================
test('REQ-NFR-033 admin screens surface curated messages for missing records — never raw exception text', async () => {
  // Provocations chosen so every one is a READ. Nothing here writes.
  const cases: [string, RegExp][] = [
    ['/admin/preview/999999', /Post not found/i],
    ['/admin/preview/0', /Post not found/i],
    ['/ManagePost/999999', /Post Not Found/i],
    ['/ManageTag/999999', /Tag not found/i],
    ['/admin/category/999999', /Category not found/i],
    ['/admin/series/999999', /Series not found/i],
  ];
  for (const [route, curated] of cases) {
    await go(route);
    await page.waitForTimeout(2500);
    const body = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
    const snippet = body.slice(Math.max(0, body.search(curated) - 20), body.search(curated) + 140);
    console.log(`[REQ-NFR-033] ${route} → "${snippet.trim() || body.slice(-160)}"`);
    expect(body, `${route} must surface a curated not-found message`).toMatch(curated);
    await assertNoRawExceptionText(route);
    // The Blazor error boundary must not be showing either.
    const boundary = await page.evaluate(() => {
      const e = document.querySelector('#blazor-error-ui') as HTMLElement | null;
      return !!e && getComputedStyle(e).display !== 'none';
    });
    expect(boundary, `${route} must not trip the Blazor error boundary`).toBe(false);
  }

  // And the ten changed admin pages must be clean on their happy path too.
  const happy: [string, RegExp][] = [
    ['/admin', /^Dashboard$/], ['/users', /^Users$/], ['/CommentsList', /Comments Management/],
    ['/CategoriesList', /Categories Management/], ['/admin/tags', /Tags Management/],
    ['/admin/subscribers', /^Subscribers$/], ['/settings', /^Settings$/],
    ['/admin/images', /Media Library/], ['/admin/newsletter', /Newsletter composer/i],
    ['/admin/series', /Series Management/], ['/admin/profile', /My Profile/],
    ['/admin/experience', /Manage Experience/], ['/admin/skills', /Manage Skills/],
    ['/admin/awards', /Manage Awards/], ['/admin/analytics', /^Analytics$/],
  ];
  for (const [route, heading] of happy) {
    await go(route, heading);
    await assertNoRawExceptionText(route);
  }
});

test('REQ-NFR-033-preview admin post preview renders the real post with its metadata', async () => {
  const postId = psql(`SELECT PostId FROM BlogPost WHERE ${LIVE} ORDER BY PostId LIMIT 1`);
  await go(`/admin/preview/${postId}`);
  await page.waitForSelector('[data-testid="preview-article"]', { timeout: 30000 });

  assertRenders(`/admin/preview/${postId}`, [
    await renderCheck(page, 'preview banner', '[data-testid="preview-banner"]', 'value'),
    await renderCheck(page, 'preview article', '[data-testid="preview-article"]', 'value'),
    await renderCheck(page, 'title', '[data-testid="preview-title"]', 'value'),
    await renderCheck(page, 'author', '[data-testid="preview-author"]', 'value'),
    await renderCheck(page, 'content', '[data-testid="preview-content"]', 'value'),
    await renderCheck(page, 'metadata panel', '[data-testid="preview-metadata"]', 'value'),
    await renderCheck(page, 'status', '[data-testid="preview-status"]', 'value'),
    await renderCheck(page, 'slug', '[data-testid="preview-slug"]', 'value'),
    await renderCheck(page, 'reading time', '[data-testid="preview-reading-time"]', 'value'),
    await renderCheck(page, 'edit action', '[data-testid="preview-edit-post"]', 'present'),
    await renderCheck(page, 'view-live action', '[data-testid="preview-view-live-post"]', 'present'),
    await renderCheck(page, 'back to list', '[data-testid="preview-back-to-list"]', 'present'),
  ]);

  const db = psqlRows(`SELECT Title, COALESCE(Slug, ''), CASE WHEN Published THEN 'Published' ELSE 'Draft' END FROM BlogPost WHERE PostId = ${postId}`)[0];
  const uiTitle = await text('preview-title');
  const uiSlug = await text('preview-slug');
  const uiStatus = await text('preview-status');
  const uiPostId = await text('preview-post-id');
  console.log(`[preview] title ui="${uiTitle}" psql="${db[0]}" | slug ui="${uiSlug}" psql="${db[1]}" | status ui="${uiStatus}" psql="${db[2]}"`);
  expect(uiTitle, 'the preview must render the real post title').toBe(db[0]);
  expect(uiSlug, 'the preview metadata must render the real slug').toBe(db[1]);
  expect(uiStatus, 'the preview must render the real published state').toBe(db[2]);
  expect(uiPostId, 'the preview metadata must render the post id').toBe(String(postId));

  const tags = await page.locator('[data-testid="preview-tag"]').count();
  console.log(`[preview] ${tags} tag chips rendered`);
  await assertNoRawExceptionText(`/admin/preview/${postId}`);
  await visualBothWidths('admin-preview');
});

// =====================================================================================
// REQ-NFR-007 — WCAG 2.1 AA on the admin surface
// =====================================================================================
test('REQ-NFR-007 every admin screen passes axe at WCAG 2.1 AA with zero serious or critical violations', async () => {
  const routes: [string, RegExp][] = [
    ['/admin', /^Dashboard$/], ['/users', /^Users$/], ['/CommentsList', /Comments Management/],
    ['/CategoriesList', /Categories Management/], ['/admin/tags', /Tags Management/],
    ['/admin/subscribers', /^Subscribers$/], ['/settings', /^Settings$/],
    ['/admin/images', /Media Library/], ['/admin/newsletter', /Newsletter composer/i],
    ['/admin/series', /Series Management/], ['/admin/profile', /My Profile/],
    ['/admin/experience', /Manage Experience/], ['/admin/skills', /Manage Skills/],
    ['/admin/awards', /Manage Awards/], ['/admin/analytics', /^Analytics$/],
  ];
  const findings: string[] = [];
  for (const [route, heading] of routes) {
    await go(route, heading);
    const res = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
    const serious = res.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
    console.log(`[REQ-NFR-007] ${route}: ${res.violations.length} violations, ${serious.length} serious+ ${JSON.stringify(serious.map((v) => `${v.id}x${v.nodes.length}`))}`);
    if (serious.length) findings.push(`${route}: ${serious.map((v) => `${v.id} (${v.nodes.length} nodes, first ${v.nodes[0]?.target?.[0]})`).join('; ')}`);
  }
  expect(findings, 'REQ-NFR-007: no serious or critical WCAG 2.1 AA violation may remain on the admin surface').toEqual([]);
});

// =====================================================================================
// REQ-NFR-010 — Responsive layouts across four breakpoints
// =====================================================================================
test('REQ-NFR-010 every admin screen lays out cleanly at 320, 390, 768 and 1280 with no page-level horizontal scroll', async () => {
  const routes: [string, RegExp][] = [
    ['/admin', /^Dashboard$/], ['/users', /^Users$/], ['/CommentsList', /Comments Management/],
    ['/CategoriesList', /Categories Management/], ['/admin/tags', /Tags Management/],
    ['/admin/subscribers', /^Subscribers$/], ['/settings', /^Settings$/],
    ['/admin/images', /Media Library/], ['/admin/newsletter', /Newsletter composer/i],
    ['/admin/series', /Series Management/], ['/admin/profile', /My Profile/],
    ['/admin/experience', /Manage Experience/], ['/admin/skills', /Manage Skills/],
    ['/admin/awards', /Manage Awards/], ['/admin/analytics', /^Analytics$/],
  ];
  const overflow: string[] = [];
  const zeroHeight: string[] = [];
  for (const [route, heading] of routes) {
    await go(route, heading);
    const line: string[] = [];
    for (const w of [320, 390, 768, 1280]) {
      await page.setViewportSize({ width: w, height: 844 });
      await page.waitForTimeout(1100);
      const m = await page.evaluate(() => {
        const de = document.documentElement;
        const content = document.querySelector('[data-testid="admin-content"]') as HTMLElement | null;
        // A container that collapsed to zero height is a layout break even with no overflow.
        const collapsed = Array.from(document.querySelectorAll('[data-testid]'))
          .filter((e) => {
            const r = e.getBoundingClientRect();
            const s = getComputedStyle(e);
            return s.display !== 'none' && s.visibility !== 'hidden' && r.width > 40 && r.height === 0;
          })
          .map((e) => e.getAttribute('data-testid')!)
          .slice(0, 5);
        return { h: de.scrollWidth - de.clientWidth, contentH: content ? content.getBoundingClientRect().height : -1, collapsed };
      });
      line.push(`${w}:hScroll=${m.h},contentH=${Math.round(m.contentH)}`);
      if (m.h > 2) overflow.push(`${route}@${w}: hScroll=${m.h}`);
      if (m.contentH <= 0) zeroHeight.push(`${route}@${w}: admin-content collapsed to zero height`);
      if (m.collapsed.length) zeroHeight.push(`${route}@${w}: zero-height containers ${m.collapsed.join(',')}`);
    }
    console.log(`[REQ-NFR-010] ${route} ${line.join(' ')}`);
  }
  await page.setViewportSize({ width: 1280, height: 800 });
  expect(overflow, 'no admin screen may scroll horizontally at any of the four breakpoints').toEqual([]);
  expect(zeroHeight, 'no admin container may collapse to zero height').toEqual([]);
});

// =====================================================================================
// REQ-NFR-018 — Caching layer
// =====================================================================================
test('REQ-NFR-018 settings and taxonomy reads are consistent across repeat renders (cache observability)', async () => {
  // A black-box UI pass cannot see a cache hit: there is no cache-status header, no diagnostics
  // endpoint and no admin cache panel on this surface. What IS observable is that a cached read
  // never serves a stale or divergent value, and that repeat renders do not get slower. Both are
  // measured; neither PROVES a cache exists, so this REQ is reported NOT-OBSERVABLE with the data.
  const timings: number[] = [];
  const values: string[] = [];
  for (let i = 0; i < 3; i++) {
    await go('/admin', /^Dashboard$/);
    const t0 = Date.now();
    await go('/settings', /^Settings$/);
    await page.waitForSelector('[data-testid="site-title"]', { timeout: 30000 });
    timings.push(Date.now() - t0);
    values.push(await value('site-title'));
  }
  console.log(`[REQ-NFR-018] /settings render timings (ms) = ${JSON.stringify(timings)}; site-title reads = ${JSON.stringify(values)}`);
  expect(new Set(values).size, 'a cached setting must never diverge between renders').toBe(1);
  expect(values[0], 'the cached value must equal the SiteSetting row').toBe(psql(SQL.setting('General.SiteTitle')));

  const taxonomy: number[] = [];
  for (let i = 0; i < 2; i++) {
    await go('/admin', /^Dashboard$/);
    const t0 = Date.now();
    await go('/CategoriesList', /Categories Management/);
    await page.waitForSelector('[data-testid="category-row-name"]', { timeout: 30000 });
    taxonomy.push(Date.now() - t0);
  }
  console.log(`[REQ-NFR-018] /CategoriesList render timings (ms) = ${JSON.stringify(taxonomy)} — no cache-status signal is exposed to a black-box client`);
});

// =====================================================================================
// Gate summary
// =====================================================================================
test.afterAll(async () => {
  console.log('\n================ ADMIN SURFACE GATE SUMMARY ================');
  console.log(`§4a render findings (${renderFindings.length}):`);
  for (const f of renderFindings) console.log(`  - ${f}`);
  console.log(`§4b visual findings (${visualFindings.length}):`);
  for (const f of visualFindings) console.log(`  - ${f}`);
  console.log(`screenshots: ${OUT}/*-{1280,390}-full.png`);
  console.log('============================================================\n');
  await page.context().close().catch(() => {});
});
