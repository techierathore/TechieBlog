import { test, expect, Page, Locator } from '@playwright/test';

/**
 * REQ-NFR-026 Cluster A smoke — NewsletterRepo and SiteSettingRepo, converted to genuine async.
 *
 * Both repositories were Task-returning before this change and both opened their connection with
 * the blocking factory, so an async refactor here is exactly the kind of change that compiles,
 * passes every unit test and silently returns nothing at runtime. These checks therefore assert
 * that real rows arrive on the screens the two repositories feed, cross-checked against values read
 * straight out of PostgreSQL, and that those screens still look right at desktop and mobile widths.
 *
 * Screens: /newsletters and /newsletter/{slug} (NewsletterRepo, public archive path),
 * /admin/newsletter (NewsletterRepo, admin path) and /settings (SiteSettingRepo).
 *
 * Gates: RENDER-TRUTH (rows present, cells non-empty, values match the database) and VISUAL-TRUTH
 * (no overlapping siblings, no zero-size or off-viewport controls, no horizontal overflow) at 1280
 * and 390.
 */

const BASE = 'http://localhost:5430';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

/**
 * Ground truth read directly from PostgreSQL:
 *   SELECT title, slug FROM newsletter
 *   WHERE status='sent' AND ispublic=TRUE AND slug IS NOT NULL AND slug<>''
 *   ORDER BY senton DESC;
 */
const PUBLISHED_ISSUES = [
  { title: 'August 2026 — Cluster D smoke 23435', slug: 'august-2026-cluster-d-smoke-23435' },
  { title: 'August 2026 — Cluster D smoke 58274', slug: 'august-2026-cluster-d-smoke-58274' },
  { title: 'Shipping a MAUI admin app', slug: 'shipping-a-maui-admin-app' },
  { title: 'Render modes, one year on', slug: 'render-modes-one-year-on' },
  { title: 'Dapper, DbUp and boring migrations', slug: 'dapper-dbup-and-boring-migrations' },
];

/** SELECT count(*) FROM newsletter; — the admin history lists every status, not only published. */
const TOTAL_ISSUES = 6;

/** SELECT settingvalue FROM sitesetting WHERE settingkey IN (...); */
const SETTINGS = {
  siteTitle: 'TechieBlog A420315',
  tagline: 'Tagline A420315',
  adminEmail: 'Ravi@techieblog.com',
  postsPerPage: '7',
  paginationWordCount: '333',
};

/** Signs in with the documented seeded site owner from the UsageGuide. Never creates a user. */
async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  // The login form is an EditForm: submitting before the interactive circuit attaches degrades to
  // a static POST and 400s. Wait for Blazor to take over the document.
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 });
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

/**
 * Navigates to a route through the app's own router rather than a full page load.
 *
 * The JWT lives in localStorage only, which the server cannot read during Blazor Server's prerender
 * pass, so a full load of an authenticated route evaluates as anonymous and redirects. Pre-existing
 * defect, unrelated to REQ-NFR-026; this smoke navigates the way a signed-in admin does.
 */
async function routerGoto(page: Page, href: string) {
  await page.evaluate(path => (window as any).Blazor.navigateTo(path), href);
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

/** Asserts an element is present, visible and carries non-blank text. */
async function expectPopulated(locator: Locator, label: string) {
  await expect(locator, `${label} should be visible`).toBeVisible({ timeout: 30000 });
  const text = ((await locator.textContent()) ?? '').trim();
  expect(text.length, `${label} should not be blank`).toBeGreaterThan(0);
}

/** Asserts an input is present and carries a non-blank value. */
async function expectFilled(locator: Locator, label: string, expected?: string) {
  await expect(locator, `${label} should be visible`).toBeVisible({ timeout: 30000 });
  const value = ((await locator.inputValue()) ?? '').trim();
  expect(value.length, `${label} should not be blank`).toBeGreaterThan(0);
  if (expected !== undefined) {
    expect(value, `${label} should match the database`).toBe(expected);
  }
}

/**
 * VISUAL-TRUTH: at the supplied width the page must not scroll horizontally, every visible control
 * must have a non-zero box inside the viewport, and no two sibling cards may overlap.
 */
async function expectLooksRight(page: Page, width: number, name: string) {
  await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `test-results/cluster-a-${name}-${width}.png`, fullPage: true });

  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${name} at ${width} should not scroll horizontally`).toBeLessThanOrEqual(1);

  const problems = await page.evaluate(() => {
    const found: string[] = [];
    const viewportWidth = document.documentElement.clientWidth;

    const controls = Array.from(document.querySelectorAll('[data-testid]'));
    for (const element of controls) {
      const style = getComputedStyle(element);
      if (style.display === 'none' || style.visibility === 'hidden') continue;
      // offsetParent is null when the element or any ancestor is display:none — which is how the
      // responsive nav collapses at mobile widths. Such an element is not on screen at all, so it
      // cannot overlap anything or overflow; only genuinely rendered controls are checked.
      if ((element as HTMLElement).offsetParent === null && style.position !== 'fixed') continue;
      const box = element.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) {
        found.push(`zero-size: ${element.getAttribute('data-testid')}`);
      }
      if (box.left < -1 || box.right > viewportWidth + 1) {
        found.push(`off-viewport: ${element.getAttribute('data-testid')}`);
      }
    }

    // Sibling overlap: two cards in the same flow must not paint on top of each other.
    const cards = Array.from(document.querySelectorAll('[data-testid$="-card"]'));
    for (let i = 0; i < cards.length; i++) {
      for (let j = i + 1; j < cards.length; j++) {
        if (cards[i].contains(cards[j]) || cards[j].contains(cards[i])) continue;
        const a = cards[i].getBoundingClientRect();
        const b = cards[j].getBoundingClientRect();
        const overlaps =
          a.left < b.right - 1 && b.left < a.right - 1 &&
          a.top < b.bottom - 1 && b.top < a.bottom - 1;
        if (overlaps) {
          found.push(
            `overlap: ${cards[i].getAttribute('data-testid')} / ${cards[j].getAttribute('data-testid')}`);
        }
      }
    }

    return found;
  });

  expect(problems, `${name} at ${width} should have no layout defects`).toEqual([]);
}

test.describe('REQ-NFR-026 Cluster A — converted NewsletterRepo and SiteSettingRepo', () => {
  test('public archive lists every published issue from the converted repository', async ({ page }) => {
    await page.goto(`${BASE}/newsletters`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    await expect(page.locator('[data-testid="newsletter-issues-list"]')).toBeVisible({ timeout: 30000 });

    const titles = page.locator('[data-testid="newsletter-issue-title"]');
    await expect(titles).toHaveCount(PUBLISHED_ISSUES.length);

    // RENDER-TRUTH: every card carries a real title, a real date and a real issue number.
    for (let i = 0; i < PUBLISHED_ISSUES.length; i++) {
      await expect(titles.nth(i)).toHaveText(PUBLISHED_ISSUES[i].title);
      await expectPopulated(page.locator('[data-testid="newsletter-issue-date"]').nth(i), `date ${i}`);
      await expectPopulated(page.locator('[data-testid="newsletter-issue-number"]').nth(i), `number ${i}`);
    }

    // The draft issue exists in the table but must never reach the archive.
    await expect(page.getByText('Unsent draft — moderation queue internals')).toHaveCount(0);

    await expectLooksRight(page, 1280, 'newsletters');
    await expectLooksRight(page, 390, 'newsletters');
  });

  test('issue view renders body and neighbour navigation from the converted repository', async ({ page }) => {
    const issue = PUBLISHED_ISSUES[2];
    await page.goto(`${BASE}/newsletter/${issue.slug}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(3000);

    await expectPopulated(page.locator('[data-testid="newsletter-view-title"]'), 'issue title');
    await expect(page.locator('[data-testid="newsletter-view-title"]')).toHaveText(issue.title);
    await expectPopulated(page.locator('[data-testid="newsletter-view-date"]'), 'issue date');
    await expectPopulated(page.locator('[data-testid="newsletter-view-body"]'), 'issue body');

    // GetPreviousPublishedAsync / GetNextPublishedAsync: this issue sits in the middle of the
    // archive, so both neighbour links must resolve.
    await expectPopulated(page.locator('[data-testid="newsletter-view-previous"]'), 'previous link');
    await expectPopulated(page.locator('[data-testid="newsletter-view-next"]'), 'next link');

    await expectLooksRight(page, 1280, 'newsletter-view');
    await expectLooksRight(page, 390, 'newsletter-view');
  });

  test('admin composer lists every issue in any status', async ({ page }) => {
    await login(page);
    await routerGoto(page, '/admin/newsletter');

    await expect(page.locator('[data-testid="newsletter-history-list"]')).toBeVisible({ timeout: 30000 });

    const rows = page.locator('[data-testid="history-row-title"]');
    // At least the seeded issues; the write-path test below adds drafts of its own, so the count
    // is asserted as a floor rather than an equality that a second run would break.
    const rowCount = await rows.count();
    expect(rowCount, 'admin history should list every issue in any status').toBeGreaterThanOrEqual(TOTAL_ISSUES);

    for (let i = 0; i < rowCount; i++) {
      await expectPopulated(rows.nth(i), `history title ${i}`);
      await expectPopulated(page.locator('[data-testid="history-row-status"]').nth(i), `history status ${i}`);
      await expectPopulated(page.locator('[data-testid="history-row-meta"]').nth(i), `history meta ${i}`);
    }

    // Each seeded issue, published or not, appears exactly once.
    for (const issue of PUBLISHED_ISSUES) {
      await expect(rows.filter({ hasText: issue.title })).toHaveCount(1);
    }

    // The draft the archive hides is present here — that is the admin/public split working.
    await expect(page.getByText('Unsent draft — moderation queue internals')).toHaveCount(1);

    // GetRecipientsAsync feeds the audience counts.
    await expectPopulated(page.locator('[data-testid="newsletter-recipient-count"]'), 'recipient count');

    await expectLooksRight(page, 1280, 'newsletter-composer');
    await expectLooksRight(page, 390, 'newsletter-composer');
  });

  test('settings screen renders persisted values from the converted repository', async ({ page }) => {
    await login(page);
    await routerGoto(page, '/settings');

    await expect(page.locator('[data-testid="settings-page"]')).toBeVisible({ timeout: 30000 });
    await expect(page.locator('[data-testid="settings-loading"]')).toHaveCount(0);

    // RENDER-TRUTH: each field must carry the value PostgreSQL holds, not a blank or a placeholder.
    await expectFilled(page.locator('[data-testid="site-title"]'), 'site title', SETTINGS.siteTitle);
    // The tagline is the field the save test rewrites, so only its non-blankness is pinned here.
    await expectFilled(page.locator('[data-testid="site-tagline"]'), 'tagline');
    await expectFilled(page.locator('[data-testid="admin-email"]'), 'admin email', SETTINGS.adminEmail);

    await page.click('[data-testid="tab-blog"]');
    await page.waitForTimeout(1500);
    await expectFilled(page.locator('[data-testid="posts-per-page"]'), 'posts per page', SETTINGS.postsPerPage);
    await expectFilled(
      page.locator('[data-testid="pagination-word-count"]'), 'pagination words', SETTINGS.paginationWordCount);

    await page.click('[data-testid="tab-general"]');
    await page.waitForTimeout(1500);

    await expectLooksRight(page, 1280, 'settings');
    await expectLooksRight(page, 390, 'settings');
  });

  /**
   * The write paths are the ones a read-only smoke would miss. SiteSettingRepo's batch save was
   * rewritten onto an asynchronous connection and transaction (BeginTransactionAsync / CommitAsync),
   * and NewsletterRepo now binds every DateTime through DbTimestamp so a UTC value is not sent as
   * timestamptz into a TIMESTAMP column. Both changes compile and unit-test identically to the code
   * they replace; only a real round trip shows whether they persist.
   */
  test('settings save commits through the async transaction and reads back', async ({ page }) => {
    const marker = `Cluster A ${Date.now()}`;

    await login(page);
    await routerGoto(page, '/settings');

    await expect(page.locator('[data-testid="site-tagline"]')).toBeVisible({ timeout: 30000 });
    await page.fill('[data-testid="site-tagline"]', marker);
    await page.click('[data-testid="save-settings"]');
    await page.waitForTimeout(5000);

    // Re-enter the screen so the value comes back from the database through the reloaded cache,
    // not from the still-bound editor state.
    await routerGoto(page, '/dashboard');
    await routerGoto(page, '/settings');
    await expectFilled(page.locator('[data-testid="site-tagline"]'), 'saved tagline', marker);

    // Every other field must survive the batch — a transaction that committed only the changed row
    // would leave the rest blank and still look successful on this screen alone.
    await expectFilled(page.locator('[data-testid="site-title"]'), 'site title', SETTINGS.siteTitle);
    await expectFilled(page.locator('[data-testid="admin-email"]'), 'admin email', SETTINGS.adminEmail);

    // Put the seeded value back so the fixture the other checks read is unchanged by this one.
    await page.fill('[data-testid="site-tagline"]', SETTINGS.tagline);
    await page.click('[data-testid="save-settings"]');
    await page.waitForTimeout(5000);
  });

  test('newsletter draft save persists through the async write path', async ({ page }) => {
    const subject = `Cluster A draft ${Date.now()}`;

    await login(page);
    await routerGoto(page, '/admin/newsletter');

    await page.fill('[data-testid="newsletter-subject"]', subject);
    await page.fill('[data-testid="newsletter-summary"]', 'Async write-path smoke for REQ-NFR-026.');
    // newsletter-body is the markdown editor's wrapper div; the editable surface is its textarea.
    await page.fill('[data-testid="newsletter-body"] textarea', '## Async write path\n\nWritten by the smoke.');
    await page.click('[data-testid="newsletter-save-draft"]');
    await page.waitForTimeout(5000);

    await expectPopulated(page.locator('[data-testid="newsletter-status-message"]'), 'save status');

    // The history list is refreshed from GetAllAsync, so the new row appearing proves the insert
    // committed and the read saw it.
    await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: subject }))
      .toHaveCount(1, { timeout: 30000 });

    // Saving again exercises UpdateAsync, which stamps UpdatedOn through DbTimestamp.
    await page.fill('[data-testid="newsletter-summary"]', 'Updated by the smoke.');
    await page.click('[data-testid="newsletter-save-draft"]');
    await page.waitForTimeout(5000);

    await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: subject }))
      .toHaveCount(1);
  });
});
