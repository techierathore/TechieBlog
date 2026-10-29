import { test, expect, Page, Locator } from '@playwright/test';
import { execSync } from 'node:child_process';
import { login as gateLogin } from './_gates';

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

const BASE = process.env.TB_BASE ?? 'http://localhost:5450';

/**
 * Rewritten 2026-10-28 (triage T1): the original pinned retired demo issues, a retired admin password, a
 * fixed settings snapshot and a hard-coded port. Ground truth is now read from PostgreSQL (SELECT only)
 * at the moment of each assertion, so the test follows whatever rows other writers' fixtures leave.
 */
function psql(sql: string): string[] {
  const raw = execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -A -t -c ${JSON.stringify(sql.replace(/\s+/g, ' ').trim())}`,
    { encoding: 'utf8' });
  return raw.split('\n').map((l) => l.trim()).filter((l) => l.length > 0);
}
const setting = (key: string) => psql(`SELECT settingvalue FROM sitesetting WHERE settingkey='${key}'`)[0] ?? '';
const perPage = () => Math.max(1, Number(setting('Blog.PostsPerPage')) || 10);
/** Published issues exactly as the public archive selects them, newest first. */
const publishedIssues = () =>
  psql(`SELECT title || '||' || slug FROM newsletter WHERE status='sent' AND ispublic=TRUE AND slug IS NOT NULL AND slug<>'' ORDER BY senton DESC, newsletterid DESC`)
    .map((l) => { const [title, slug] = l.split('||'); return { title, slug }; });

/** Signs in as the seeded site owner through the real form (shared helper). */
async function login(page: Page) {
  const landed = await gateLogin(page, 'admin');
  expect(new URL(landed).pathname.toLowerCase(), 'sign-in lands off /login').not.toContain('login');
}

/** Authenticated navigation through the app router, gated on the destination's own marker. */
async function routerGoto(page: Page, href: string, marker: string) {
  await page.evaluate((path) => (window as any).Blazor.navigateTo(path), href);
  await page.waitForURL((u) => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await expect(page.locator(marker).first()).toBeVisible({ timeout: 45000 });
  await page.waitForTimeout(1500);
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
    await expect(page.locator('[data-testid="newsletter-issues-list"]')).toBeVisible({ timeout: 45000 });
    await page.waitForTimeout(3000);

    const db = publishedIssues();
    expect(db.length, 'the database holds published issues (fxv0 seed)').toBeGreaterThan(0);
    const expected = db.slice(0, perPage());
    const titles = page.locator('[data-testid="newsletter-issue-title"]');
    await expect(titles).toHaveCount(expected.length);

    // RENDER-TRUTH: every card carries a real title, a real date and a real issue number, newest first.
    for (let i = 0; i < expected.length; i++) {
      await expect(titles.nth(i)).toHaveText(expected[i].title);
      await expectPopulated(page.locator('[data-testid="newsletter-issue-date"]').nth(i), `date ${i}`);
      await expectPopulated(page.locator('[data-testid="newsletter-issue-number"]').nth(i), `number ${i}`);
    }

    // Drafts exist in the table but must never reach the archive.
    const drafts = psql(`SELECT DISTINCT title FROM newsletter WHERE status<>'sent' OR ispublic=FALSE`);
    const sentTitles = new Set(db.map((d) => d.title));
    for (const d of drafts.filter((t) => !sentTitles.has(t))) {
      await expect(page.locator('[data-testid="newsletter-issue-title"]').filter({ hasText: d })).toHaveCount(0);
    }

    await expectLooksRight(page, 1280, 'newsletters');
    await expectLooksRight(page, 390, 'newsletters');
  });

  test('issue view renders body and neighbour navigation from the converted repository', async ({ page }) => {
    const db = publishedIssues();
    test.skip(db.length < 3, 'needs three published issues so one sits in the middle of the archive');
    const issue = db[1];
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
    await routerGoto(page, '/admin/newsletter', '[data-testid="newsletter-history-list"]');

    const rows = page.locator('[data-testid="history-row-title"]');
    const total = Number(psql('SELECT count(*) FROM newsletter')[0]);
    const rowCount = await rows.count();
    // Other writers may add an issue between the two reads, so the screen may trail the database by a few rows.
    expect(rowCount, 'admin history should list every issue in any status').toBeGreaterThanOrEqual(total - 3);
    expect(rowCount).toBeGreaterThan(0);

    for (let i = 0; i < rowCount; i++) {
      await expectPopulated(rows.nth(i), `history title ${i}`);
      await expectPopulated(page.locator('[data-testid="history-row-status"]').nth(i), `history status ${i}`);
      await expectPopulated(page.locator('[data-testid="history-row-meta"]').nth(i), `history meta ${i}`);
    }

    // A published issue and a draft both appear: the admin/public split working.
    const newestSent = publishedIssues()[0];
    await expect(rows.filter({ hasText: newestSent.title }).first()).toBeVisible();
    const draftTitle = psql(`SELECT title FROM newsletter WHERE status='draft' ORDER BY newsletterid DESC LIMIT 1`)[0];
    if (draftTitle) await expect(rows.filter({ hasText: draftTitle }).first()).toBeVisible();

    // GetRecipientsAsync feeds the audience counts.
    await expectPopulated(page.locator('[data-testid="newsletter-recipient-count"]'), 'recipient count');

    await expectLooksRight(page, 1280, 'newsletter-composer');
    await expectLooksRight(page, 390, 'newsletter-composer');
  });

  test('settings screen renders persisted values from the converted repository', async ({ page }) => {
    await login(page);
    await routerGoto(page, '/settings', '[data-testid="settings-page"]');
    await expect(page.locator('[data-testid="settings-loading"]')).toHaveCount(0);

    // RENDER-TRUTH: each field must carry the value PostgreSQL holds, not a blank or a placeholder.
    await expectFilled(page.locator('[data-testid="site-title"]'), 'site title', setting('General.SiteTitle'));
    await expectFilled(page.locator('[data-testid="site-tagline"]'), 'tagline', setting('General.SiteTagline'));
    await expectFilled(page.locator('[data-testid="admin-email"]'), 'admin email', setting('General.AdminEmail'));

    await page.click('[data-testid="tab-blog"]');
    await page.waitForTimeout(1500);
    await expectFilled(page.locator('[data-testid="posts-per-page"]'), 'posts per page', setting('Blog.PostsPerPage'));
    await expectFilled(
      page.locator('[data-testid="pagination-word-count"]'), 'pagination words', setting('Blog.PaginationWordCount'));

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
    const originalTagline = setting('General.SiteTagline');
    const title = setting('General.SiteTitle');
    const adminEmail = setting('General.AdminEmail');

    await login(page);
    await routerGoto(page, '/settings', '[data-testid="settings-page"]');

    await expect(page.locator('[data-testid="site-tagline"]')).toBeVisible({ timeout: 30000 });
    await page.fill('[data-testid="site-tagline"]', marker);
    await page.click('[data-testid="save-settings"]');
    await expect.poll(() => setting('General.SiteTagline'), { timeout: 30000 }).toBe(marker);

    // Re-enter the screen so the value comes back from the database through the reloaded cache,
    // not from the still-bound editor state.
    await routerGoto(page, '/admin', '[data-testid="dashboard-stats"]');
    await routerGoto(page, '/settings', '[data-testid="settings-page"]');
    await expectFilled(page.locator('[data-testid="site-tagline"]'), 'saved tagline', marker);

    // Every other field must survive the batch — a transaction that committed only the changed row
    // would leave the rest blank and still look successful on this screen alone.
    await expectFilled(page.locator('[data-testid="site-title"]'), 'site title', title);
    await expectFilled(page.locator('[data-testid="admin-email"]'), 'admin email', adminEmail);

    // Put the original value back so the fixture the other checks read is unchanged by this one.
    await page.fill('[data-testid="site-tagline"]', originalTagline);
    await page.click('[data-testid="save-settings"]');
    await expect.poll(() => setting('General.SiteTagline'), { timeout: 30000 }).toBe(originalTagline);
  });

  test('newsletter draft save persists through the async write path', async ({ page }) => {
    const subject = `fxv7 Cluster A draft ${Date.now()}`;

    await login(page);
    await routerGoto(page, '/admin/newsletter', '[data-testid="newsletter-subject"]');

    await page.fill('[data-testid="newsletter-subject"]', subject);
    await page.fill('[data-testid="newsletter-summary"]', 'Async write-path smoke for REQ-NFR-026.');
    // newsletter-body is the markdown editor's wrapper div; the editable surface is its textarea.
    await page.fill('[data-testid="newsletter-body"] textarea', '## Async write path\n\nWritten by the smoke.');
    await page.click('[data-testid="newsletter-save-draft"]');
    await expect.poll(() => psql(`SELECT count(*) FROM newsletter WHERE title='${subject}'`)[0], { timeout: 30000 }).toBe('1');

    await expectPopulated(page.locator('[data-testid="newsletter-status-message"]'), 'save status');

    // The history list is refreshed from GetAllAsync, so the new row appearing proves the insert
    // committed and the read saw it.
    await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: subject }))
      .toHaveCount(1, { timeout: 30000 });

    // Saving again exercises UpdateAsync, which stamps UpdatedOn through DbTimestamp.
    await page.fill('[data-testid="newsletter-summary"]', 'Updated by the smoke.');
    await page.click('[data-testid="newsletter-save-draft"]');
    await expect.poll(() => psql(`SELECT summary FROM newsletter WHERE title='${subject}'`)[0] ?? '', { timeout: 30000 }).toBe('Updated by the smoke.');

    await expect(page.locator('[data-testid="history-row-title"]').filter({ hasText: subject }))
      .toHaveCount(1);
  });
});
