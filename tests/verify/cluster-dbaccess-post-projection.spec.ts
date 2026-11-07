import { test, expect, Page } from '@playwright/test';

import { createPost, deletePostByTitle, idOf, slugOf, loginHard, sqlOne, goTo, openPostById } from './fxv1-fixtures';

/**
 * REQ-NFR-008 (documentation pass, BlogEngine/DbAccess partition) — projection defect smoke.
 *
 * THE DEFECT. `BlogPostRepo.SelectByIdSql` — the statement behind `GetSingle` / `GetSingleAsync` —
 * did not project `PublishedOn` or `ScheduledPublishOn`, while `UpdateSql` writes both columns
 * unconditionally from the entity it is handed. An earlier fix (REQ-UI-017) had added those columns
 * to `SelectAllSql` and `SelectAllByUserSql` but not to the by-id or by-slug lookups, so the two
 * shapes had silently drifted apart.
 *
 * Two consequences, neither visible to the compiler or to any unit test using a fake repository:
 *
 *  1. RENDER. A scheduled post opened in the editor loaded with `ScheduledPublishOn == null`, so
 *     `BlogPost.IsScheduled` was false, the status badge read "Draft" instead of "Scheduled", and
 *     the schedule pickers came up empty — for a row that plainly carried a future publish date.
 *  2. DATA LOSS. Every read-modify-write in `BlogSvc` (`PublishPostAsync`, `UnpublishPostAsync`,
 *     `QuickPublishAsync`, `SchedulePostAsync`) loads through `GetSingleAsync` and saves through
 *     `UpdateAsync`. The unprojected columns came back null, and the update stored that null — so
 *     unpublishing a post permanently erased its first-publication date, and saving a scheduled
 *     post silently cancelled its schedule. `QuickPublishAsync`'s `if (!post.PublishedOn.HasValue)`
 *     guard, whose documented purpose is to preserve the original date, could never see a value.
 *
 * THE FIX. `SelectByIdSql` and `SelectBySlugSql` now project `p.PublishedOn, p.ScheduledPublishOn`,
 * matching `SelectAllSql` column for column.
 *
 * WHY THIS SPEC EXISTS. A green build and a green unit suite were both green while the defect was
 * live, because the fakes under `tests/unit/` return whatever they were handed and never execute the
 * SQL. Only a real read of a real row through the real statement can tell the fix apart from the
 * appearance of one, so this drives the running application against the migrated database.
 *
 * Gates (.tfcore/tasks/_smoke-test-policy.md):
 *  - RENDER-TRUTH: the editor must show the post as Scheduled and render the schedule date read
 *    from the database. An empty or "Draft" render is a FAILURE, not a pass.
 *  - VISUAL-TRUTH: 1280 and 390. No horizontal page scroll at either width.
 *
 * 2026-10-28 (T2 triage): the original hard-coded https://localhost:7373, the retired admin password,
 * post id 17 and a schedule date the caller injected. It now runs against TB_BASE with the current admin
 * account, schedules a fixture post for tomorrow THROUGH THE EDITOR (`fxv8 Projection Scheduled`), reads
 * the stored ScheduledPublishOn back with a psql SELECT as the "live database reading", and publishes a
 * second fixture (`fxv8 Projection Public`) for the public-page check. Both are deleted through
 * /BlogsList in afterAll. No account is invented and no password is altered.
 */

const BASE = process.env.TB_BASE ?? 'http://localhost:5450';

const ADMIN = { email: 'Ravi@techieblog.com', password: 'VerifyAdmin#2026' };

const SCHED_TITLE = 'fxv8 Projection Scheduled';
const PUBLIC_TITLE = 'fxv8 Projection Public';

/** Resolved in beforeAll: the fixture's post id and the schedule date the database stored for it. */
let SCHEDULED_POST_ID = '';
let SCHEDULED_ON_UTC = '';
let PUBLIC_SLUG = '';

/** Text Blazor's default ErrorBoundary renders when a component throws. */
const ERROR_BOUNDARY_TEXT = 'An unhandled error has occurred';

/** Waits for a live Blazor Server circuit rather than just a painted DOM. */
async function gotoInteractive(page: Page, url: string) {
  const socket = page.waitForEvent('websocket', {
    predicate: ws => ws.url().includes('_blazor'),
    timeout: 30000,
  });
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await socket;
  await page.waitForFunction(
    () => (window as unknown as { Blazor?: unknown }).Blazor !== undefined,
    null,
    { timeout: 30000 });
  await page.waitForTimeout(1500);
}

/**
 * Navigates client-side. A full page load of a protected route bounces to login because the auth
 * token lives in local storage and is unreadable during prerender — a known, separately tracked
 * session defect that must not be allowed to masquerade as a render failure here.
 */
async function spaNavigate(page: Page, href: string) {
  await page.evaluate(target => {
    const link = document.createElement('a');
    link.href = target;
    link.id = 'smoke-spa-link';
    link.textContent = 'go';
    link.style.position = 'fixed';
    link.style.top = '0';
    link.style.left = '0';
    link.style.zIndex = '2147483647';
    document.body.appendChild(link);
    link.click();
  }, href);
  await page.waitForTimeout(3500);
  await page.evaluate(() => document.getElementById('smoke-spa-link')?.remove());
}

/** Fails when the PAGE itself scrolls sideways, which VISUAL-TRUTH forbids at every width. */
async function expectNoHorizontalPageScroll(page: Page, label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label} scrolls horizontally by ${overflow}px`).toBeLessThanOrEqual(1);
}

/**
 * Captures viewport-sized evidence at a given scroll offset.
 *
 * Deliberately NOT `fullPage`: headless Chromium in this WSL environment composites a stale surface
 * for full-page captures, producing images that show a doubled layout while the live DOM is correct.
 * Evidence that lies is worse than no evidence, so tall pages are covered by several offsets.
 */
async function captureAt(page: Page, scrollY: number, path: string) {
  await page.evaluate(y => window.scrollTo(0, y), scrollY);
  await page.waitForTimeout(600);
  await page.screenshot({ path });
}

/** Schedules the draft `id` for tomorrow through the editor's own date and time pickers. */
async function scheduleTomorrow(admin: Page, id: string) {
  await openPostById(admin, id);
  await expect(admin.locator('[data-testid="schedule-section"]')).toBeVisible({ timeout: 30000 });
  const t = new Date(Date.now() + 24 * 3600 * 1000);
  const stamp = `${t.getFullYear()}${String(t.getMonth() + 1).padStart(2, '0')}${String(t.getDate()).padStart(2, '0')}`;
  await admin.click('[data-testid="publish-date-picker"]');
  const dayCell = admin.locator(`button[id$="-day-${stamp}"]:not([disabled])`).first();
  await expect(dayCell, 'tomorrow is selectable in the date picker').toBeVisible({ timeout: 30000 });
  await dayCell.click();
  await admin.waitForTimeout(1000);
  await admin.keyboard.press('Escape');
  await admin.click('[data-testid="publish-time-picker"]');
  await admin.getByRole('button', { name: 'Now' }).click();
  await admin.waitForTimeout(1000);
  await admin.keyboard.press('Escape');
  await expect(admin.locator('[data-testid="scheduled-summary"]')).toBeVisible({ timeout: 15000 });
  await admin.click('[data-testid="schedule-post"]');
  const end = Date.now() + 45000;
  while (Date.now() < end && sqlOne(`SELECT (scheduledpublishon IS NOT NULL)::text FROM blogpost WHERE postid = ${id}`) !== 'true') {
    await new Promise((r) => setTimeout(r, 1500));
  }
  expect(sqlOne(`SELECT (scheduledpublishon IS NOT NULL)::text FROM blogpost WHERE postid = ${id}`), 'schedule saved').toBe('true');
}

async function signInAsAdmin(page: Page) {
  await loginHard(page, 'admin');

  const landed = new URL(page.url()).pathname;
  expect(landed, 'the admin sign in did not take').not.toContain('/login');
}

test.use({ ignoreHTTPSErrors: true });

test.describe('REQ-NFR-008 BlogPostRepo by-id projection', () => {
  test.describe.configure({ mode: 'serial' });

  test.beforeAll(async ({ browser }) => {
    test.setTimeout(900000);
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const admin = await ctx.newPage();
    try {
      await loginHard(admin, 'admin');
      if (!idOf(SCHED_TITLE)) await createPost(admin, { title: SCHED_TITLE, body: 'fxv8 projection body, scheduled.', abstract: 'fxv8 scheduled', mode: 'draft' });
      if (!idOf(PUBLIC_TITLE)) await createPost(admin, { title: PUBLIC_TITLE, body: 'fxv8 projection body, public.\n\n'.repeat(20), abstract: 'fxv8 public', mode: 'publish' });
      SCHEDULED_POST_ID = idOf(SCHED_TITLE);
      PUBLIC_SLUG = slugOf(PUBLIC_TITLE);
      if (sqlOne(`SELECT (scheduledpublishon IS NOT NULL)::text FROM blogpost WHERE postid = ${SCHEDULED_POST_ID}`) !== 'true') await scheduleTomorrow(admin, SCHEDULED_POST_ID);
      SCHEDULED_ON_UTC = sqlOne(`SELECT to_char(scheduledpublishon, 'YYYY-MM-DD HH24:MI:SS') FROM blogpost WHERE postid = ${SCHEDULED_POST_ID}`);
    } finally {
      await ctx.close();
    }
  });

  test.afterAll(async ({ browser }) => {
    test.setTimeout(600000);
    const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const admin = await ctx.newPage();
    try {
      await loginHard(admin, 'admin');
      for (const t of [SCHED_TITLE, PUBLIC_TITLE]) await deletePostByTitle(admin, t);
    } finally {
      await ctx.close();
    }
  });

  test('the editor loads a scheduled post as Scheduled, with the date read from the database', async ({ page }) => {
    const pageErrors: string[] = [];
    page.on('pageerror', e => pageErrors.push(String(e)));

    await page.setViewportSize({ width: 1280, height: 900 });
    await signInAsAdmin(page);
    await spaNavigate(page, `/ManagePost/${SCHEDULED_POST_ID}`);

    const landed = new URL(page.url()).pathname;
    expect(landed, 'the editor route bounced away').toContain('ManagePost');

    const bodyText = await page.evaluate(() => document.body.innerText);
    expect(bodyText, 'the editor hit the ErrorBoundary').not.toContain(ERROR_BOUNDARY_TEXT);
    expect(bodyText, 'the editor could not find the post').not.toContain('Post Not Found');
    expect(bodyText.trim().length, 'the editor rendered an empty body').toBeGreaterThan(200);

    // RENDER-TRUTH #1 — the status badge. `BlogPost.IsScheduled` is derived purely from
    // ScheduledPublishOn, so this badge reads "Scheduled" if and only if the column was projected.
    // Before the fix it read "Draft" for this very row.
    const badge = page.locator('[data-testid="post-status-badge"]').first();
    await expect(badge, 'the editor showed no status badge').toBeVisible({ timeout: 20000 });
    await expect(badge, 'the post did not load as Scheduled — ScheduledPublishOn came back null')
      .toHaveText(/Scheduled/i, { timeout: 15000 });

    // RENDER-TRUTH #2 — the date itself, cross-checked against the live database reading the caller
    // injected. Asserting only "Scheduled" would pass on any non-null date; this pins the value.
    const scheduledFor = page.locator('[data-testid="post-scheduled-for"]').first();
    await expect(scheduledFor, 'the scheduled-for line did not render').toBeVisible({ timeout: 15000 });

    const [, month, day, year] = /^(\d{4})-(\d{2})-(\d{2})/.exec(SCHEDULED_ON_UTC)
      ? [null,
         SCHEDULED_ON_UTC.slice(5, 7),
         SCHEDULED_ON_UTC.slice(8, 10),
         SCHEDULED_ON_UTC.slice(0, 4)]
      : [null, '', '', ''];
    expect(year, 'the injected schedule date is not a live database reading').not.toBe('');

    const monthNames = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
                        'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
    const expectedMonth = monthNames[parseInt(month, 10) - 1];

    const scheduledText = (await scheduledFor.innerText()).trim();
    expect(scheduledText, `the schedule line rendered no date: "${scheduledText}"`)
      .toMatch(new RegExp(`${expectedMonth}\\s+${day},?\\s+${year}`, 'i'));

    // RENDER-TRUTH #3 — the pickers were populated during load. `ScheduledDate` is only set inside
    // the `if (PageObj.ScheduledPublishOn.HasValue)` branch of ManagePost's initialiser, so this
    // summary existing at all proves that branch ran.
    const summary = page.locator('[data-testid="scheduled-summary"]').first();
    await expect(summary, 'the schedule pickers were not populated from the loaded post')
      .toBeVisible({ timeout: 15000 });
    await expect(summary, 'the schedule summary rendered empty')
      .toHaveText(new RegExp(`${year}`), { timeout: 15000 });

    await expectNoHorizontalPageScroll(page, 'editor @1280');
    await captureAt(page, 0, 'tests/.artifacts/verify/T2/cluster-dbaccess/editor-1280-top.png');
    const scheduleY = await page.evaluate(() => {
      const el = document.querySelector('[data-testid="schedule-section"]');
      return el ? Math.max(0, el.getBoundingClientRect().top + window.scrollY - 200) : 0;
    });
    await captureAt(page, scheduleY, 'tests/.artifacts/verify/T2/cluster-dbaccess/editor-1280-schedule.png');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await expectNoHorizontalPageScroll(page, 'editor @390');
    await captureAt(page, 0, 'tests/.artifacts/verify/T2/cluster-dbaccess/editor-390-top.png');

    expect(pageErrors, `the editor raised script errors: ${pageErrors.join(' | ')}`)
      .toHaveLength(0);
  });

  test('the public post page still renders after the projection change', async ({ page }) => {
    // SelectBySlugSql gained the same two columns. It feeds the public article page, so the change
    // is only safe if that page still renders its real content — a widened projection that broke
    // Dapper's mapping would surface here and nowhere else.
    const pageErrors: string[] = [];
    page.on('pageerror', e => pageErrors.push(String(e)));

    await page.setViewportSize({ width: 1280, height: 900 });
    await gotoInteractive(page, `${BASE}/`);
    await page.waitForFunction(
      () => document.querySelectorAll('header').length === 1
        && document.querySelectorAll('a[href^="/post/"]').length > 0,
      null,
      { timeout: 30000 });

    // the home page lists articles (the fxv8 fixture is published, so at least one exists)
    expect(await page.locator('a[href^="/post/"]').count(), 'the home page listed no article to open').toBeGreaterThan(0);

    // open the fixture article itself, so the by-slug read is the one under test
    await spaNavigate(page, `/post/${PUBLIC_SLUG}`);
    expect(new URL(page.url()).pathname, 'the fixture article opened').toBe(`/post/${PUBLIC_SLUG}`);

    const bodyText = await page.evaluate(() => document.body.innerText);
    expect(bodyText, 'the article page hit the ErrorBoundary').not.toContain(ERROR_BOUNDARY_TEXT);
    expect(bodyText.trim().length, 'the article page rendered an empty body').toBeGreaterThan(400);

    await expectNoHorizontalPageScroll(page, 'article @1280');
    await captureAt(page, 0, 'tests/.artifacts/verify/T2/cluster-dbaccess/article-1280-top.png');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await expectNoHorizontalPageScroll(page, 'article @390');
    await captureAt(page, 0, 'tests/.artifacts/verify/T2/cluster-dbaccess/article-390-top.png');

    expect(pageErrors, `the article page raised script errors: ${pageErrors.join(' | ')}`)
      .toHaveLength(0);
  });
});
