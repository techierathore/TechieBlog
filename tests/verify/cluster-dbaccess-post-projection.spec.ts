import { test, expect, Page } from '@playwright/test';

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
 * Credentials are the documented seeded Admin from docs/TechieBlog-UsageGuide.md. No account is
 * invented and no password is altered. The `MustChangePassword` flag is cleared to reach the admin
 * surface and RE-ARMED by the caller afterwards.
 */

const BASE = process.env.SMOKE_BASE ?? 'https://localhost:7373';

const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

/** The seeded scheduled post, and the schedule date read live from PostgreSQL by the caller. */
const SCHEDULED_POST_ID = process.env.SMOKE_SCHEDULED_POST_ID ?? '17';
const SCHEDULED_ON_UTC = process.env.SMOKE_SCHEDULED_ON_UTC ?? '2026-08-21 06:18:31';

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

async function signInAsAdmin(page: Page) {
  await gotoInteractive(page, `${BASE}/login`);
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.fill('[data-testid="login-email"]', ADMIN.email);
  await page.fill('[data-testid="login-password"]', ADMIN.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForTimeout(4500);

  const landed = new URL(page.url()).pathname;
  expect(landed, 'the admin sign in did not take').not.toContain('/login');
}

test.use({ ignoreHTTPSErrors: true });

test.describe('REQ-NFR-008 BlogPostRepo by-id projection', () => {
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
    await captureAt(page, 0, 'test-results/cluster-dbaccess/editor-1280-top.png');
    const scheduleY = await page.evaluate(() => {
      const el = document.querySelector('[data-testid="schedule-section"]');
      return el ? Math.max(0, el.getBoundingClientRect().top + window.scrollY - 200) : 0;
    });
    await captureAt(page, scheduleY, 'test-results/cluster-dbaccess/editor-1280-schedule.png');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await expectNoHorizontalPageScroll(page, 'editor @390');
    await captureAt(page, 0, 'test-results/cluster-dbaccess/editor-390-top.png');

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

    const firstPost = page.locator('a[href^="/post/"]').first();
    const href = await firstPost.getAttribute('href');
    expect(href, 'the home page listed no article to open').toBeTruthy();

    await spaNavigate(page, href!);

    const bodyText = await page.evaluate(() => document.body.innerText);
    expect(bodyText, 'the article page hit the ErrorBoundary').not.toContain(ERROR_BOUNDARY_TEXT);
    expect(bodyText.trim().length, 'the article page rendered an empty body').toBeGreaterThan(400);

    await expectNoHorizontalPageScroll(page, 'article @1280');
    await captureAt(page, 0, 'test-results/cluster-dbaccess/article-1280-top.png');

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await expectNoHorizontalPageScroll(page, 'article @390');
    await captureAt(page, 0, 'test-results/cluster-dbaccess/article-390-top.png');

    expect(pageErrors, `the article page raised script errors: ${pageErrors.join(' | ')}`)
      .toHaveLength(0);
  });
});
