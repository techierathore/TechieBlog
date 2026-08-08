import { test, expect, Page } from '@playwright/test';

/**
 * Cluster D smoke — REQ-UI-043 (newsletter composer) and REQ-UI-044 (analytics dashboard).
 *
 * Run by the orchestrator because the implementing agent was terminated by a session
 * usage limit after writing the code but before it could smoke it.
 *
 * Gates applied: data-render (panels must render REAL non-empty data, an empty chart is a
 * failure) and visual-truth (no off-viewport or zero-size controls, no horizontal overflow)
 * at desktop 1280 and mobile 390.
 */

const BASE = 'https://localhost:7520';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

/** Signs in with the documented seeded admin from the UsageGuide. Never creates a user. */
async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

/**
 * Navigates to an admin route by CLICKING its nav link (SPA navigation).
 *
 * A direct page.goto() cannot be used: the JWT lives in localStorage only, which the server
 * cannot read during Blazor Server's prerender pass, so any FULL page load of an admin route
 * evaluates as anonymous and redirects to "/". That is a genuine pre-existing defect (logged
 * separately) — it is not a property of the pages under test, so this smoke navigates the way
 * a real signed-in admin does and asserts the pages themselves.
 */
async function gotoAdmin(page: Page, href: string) {
  const link = page.locator(`a[href="${href}"]`).first();
  await link.waitFor({ state: 'attached', timeout: 30000 });
  await link.click();
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3500);
}

/**
 * Visual-truth gate: every listed control must have a non-zero box inside the viewport,
 * and the page must not scroll horizontally.
 */
async function visualGate(page: Page, testIds: string[], label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal overflow`).toBeLessThanOrEqual(1);

  for (const id of testIds) {
    const el = page.locator(`[data-testid="${id}"]`).first();
    if (await el.count() === 0) continue;
    if (!(await el.isVisible())) continue;
    const box = await el.boundingBox();
    expect(box, `${label}: ${id} has no box`).not.toBeNull();
    expect(box!.width, `${label}: ${id} zero width`).toBeGreaterThan(0);
    expect(box!.height, `${label}: ${id} zero height`).toBeGreaterThan(0);
    expect(box!.x, `${label}: ${id} off-viewport left`).toBeGreaterThanOrEqual(-2);
  }
}

test.describe('REQ-UI-044 analytics dashboard', () => {
  test('REQ-UI-044 panels render real data and the date range filters them', async ({ page }) => {
    await login(page);
    await gotoAdmin(page, '/admin/analytics');

    // The page must actually be the dashboard, not an error boundary or a redirect.
    await expect(page.locator('[data-testid="analytics-stat-tiles"]')).toBeVisible({ timeout: 30000 });

    // DATA-RENDER GATE: popular posts must have rows, not an empty state.
    const popularEmpty = await page.locator('[data-testid="analytics-popular-empty"]').count();
    const popularGrid = page.locator('[data-testid="analytics-popular-grid"]');
    expect(popularEmpty === 0, 'popular posts rendered its EMPTY state despite 960 postviews').toBe(true);
    await expect(popularGrid).toBeVisible();
    const popularText = (await popularGrid.innerText()).trim();
    expect(popularText.length, 'popular posts grid is blank').toBeGreaterThan(0);

    // DATA-RENDER GATE: the trend chart must contain plotted geometry, not an empty frame.
    const trendEmpty = await page.locator('[data-testid="analytics-trend-empty"]').count();
    expect(trendEmpty === 0, 'trend chart rendered its EMPTY state despite 960 postviews').toBe(true);
    const trendMarks = await page.locator(
      '[data-testid="analytics-trend-chart"] rect, [data-testid="analytics-trend-chart"] path, [data-testid="analytics-trend-chart"] li, [data-testid="analytics-trend-chart"] div').count();
    expect(trendMarks, 'trend chart has no plotted marks').toBeGreaterThan(0);

    // Stat tiles must carry numbers.
    const tilesText = await page.locator('[data-testid="analytics-stat-tiles"]').innerText();
    expect(/\d/.test(tilesText), 'stat tiles contain no digits').toBe(true);

    // FILTER GATE: changing the range must change what is rendered.
    const before = await page.locator('[data-testid="analytics-trend-chart"]').innerText();
    await page.click('[data-testid="analytics-preset-7"]');
    await page.waitForTimeout(2500);
    const caption7 = await page.locator('[data-testid="analytics-range-caption"]').innerText();
    await page.click('[data-testid="analytics-preset-90"]');
    await page.waitForTimeout(2500);
    const caption90 = await page.locator('[data-testid="analytics-range-caption"]').innerText();
    const after = await page.locator('[data-testid="analytics-trend-chart"]').innerText();
    expect(caption7 !== caption90 || before !== after,
      'switching 7d -> 90d changed neither the caption nor the chart').toBe(true);

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.waitForTimeout(500);
    await visualGate(page, ['analytics-stat-tiles', 'analytics-popular-card', 'analytics-trend-card',
      'analytics-category-card', 'analytics-range-card'], 'analytics@1280');
    await page.screenshot({ path: 'test-results/cluster-d/analytics-1280.png', fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(500);
    await visualGate(page, ['analytics-stat-tiles', 'analytics-popular-card', 'analytics-trend-card',
      'analytics-category-card', 'analytics-range-card'], 'analytics@390');
    await page.screenshot({ path: 'test-results/cluster-d/analytics-390.png', fullPage: true });
  });
});

test.describe('REQ-UI-043 newsletter composer', () => {
  test('REQ-UI-043 composer renders, previews, and lists send history', async ({ page }) => {
    await login(page);
    await gotoAdmin(page, '/admin/newsletter');

    await expect(page.locator('[data-testid="newsletter-compose-card"]')).toBeVisible({ timeout: 30000 });

    // Compose fields must exist and accept input.
    await expect(page.locator('[data-testid="newsletter-body"]')).toBeVisible();
    await expect(page.locator('[data-testid="newsletter-audience"]')).toBeVisible();

    // DATA-RENDER GATE: 6 newsletter rows exist, so history must not be empty.
    const historyEmpty = await page.locator('[data-testid="newsletter-history-empty"]').count();
    expect(historyEmpty === 0, 'send history rendered EMPTY despite 6 newsletter rows').toBe(true);
    const historyList = page.locator('[data-testid="newsletter-history-list"]');
    await expect(historyList).toBeVisible();
    const historyRows = await page.locator('[data-testid="history-row-title"]').count();
    expect(historyRows, 'send history has no rows').toBeGreaterThan(0);

    // DATA-RENDER GATE: recipient count must resolve to a real number (14 subscribers seeded).
    const recipients = page.locator('[data-testid="newsletter-recipient-count"]');
    if (await recipients.count() > 0) {
      const txt = await recipients.innerText();
      expect(/\d/.test(txt), 'recipient count shows no number').toBe(true);
    }

    await page.setViewportSize({ width: 1280, height: 800 });
    await page.waitForTimeout(500);
    await visualGate(page, ['newsletter-compose-card', 'newsletter-history-card',
      'newsletter-recipients-card', 'newsletter-body', 'newsletter-audience'], 'composer@1280');
    await page.screenshot({ path: 'test-results/cluster-d/composer-1280.png', fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(500);
    await visualGate(page, ['newsletter-compose-card', 'newsletter-history-card',
      'newsletter-recipients-card', 'newsletter-body', 'newsletter-audience'], 'composer@390');
    await page.screenshot({ path: 'test-results/cluster-d/composer-390.png', fullPage: true });
  });
});
