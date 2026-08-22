/**
 * Verify-phase acceptance + render + visual gates for the scoped REQ list.
 *
 * Scope: REQ-UI-005, REQ-UI-020, REQ-UI-049, REQ-FN-020, REQ-FN-058.
 * Every test title is PREFIXED with the REQ ID it grades, per verify-phase §4.
 *
 * Black box: this suite never touches application source, and never creates a user — it signs in as
 * the documented Admin from docs/TechieBlog-UsageGuide.md (_smoke-test-policy.md).
 */
import { test, expect, Page } from '@playwright/test';

const BASE = process.env.VERIFY_BASE ?? 'http://172.18.144.1:5099';
const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

const DESKTOP = { width: 1280, height: 800 };
const MOBILE = { width: 390, height: 844 };
const NARROW = { width: 320, height: 800 };   // REQ-UI-005 acceptance names 320px explicitly

async function signIn(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'networkidle' });
  await page.getByTestId('login-email').fill(ADMIN.email);
  await page.getByTestId('login-password').fill(ADMIN.password);
  await page.getByTestId('login-submit').click();
  await page.waitForURL(u => !u.pathname.endsWith('/login'), { timeout: 25000 });
}

/** Horizontal overflow of the document, in CSS pixels. 0 means the page does not scroll sideways. */
async function overflow(page: Page) {
  return page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
}

/**
 * §4b geometry check: no two of the given elements may have intersecting boxes, and each must be
 * on-screen with a non-zero size.
 */
async function assertNoOverlapAndSized(page: Page, selector: string, label: string) {
  const boxes = await page.locator(selector).evaluateAll(nodes =>
    nodes.map(n => {
      const r = n.getBoundingClientRect();
      return { x: r.x, y: r.y, w: r.width, h: r.height };
    }));

  for (const [i, b] of boxes.entries()) {
    expect(b.w, `${label}[${i}] has zero width (clipped/collapsed)`).toBeGreaterThan(0);
    expect(b.h, `${label}[${i}] has zero height (clipped/collapsed)`).toBeGreaterThan(0);
  }

  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) {
      const a = boxes[i], b = boxes[j];
      const hit = a.x < b.x + b.w - 1 && a.x + a.w - 1 > b.x
        && a.y < b.y + b.h - 1 && a.y + a.h - 1 > b.y;
      expect(hit, `${label}[${i}] overlaps ${label}[${j}]`).toBeFalsy();
    }
  }
}

// ---------------------------------------------------------------------------
// REQ-UI-005 — public shell: header, nav, footer, mobile drawer, 320px
// ---------------------------------------------------------------------------
test('REQ-UI-005 public shell renders on every public route', async ({ page }) => {
  for (const route of ['/', '/series', '/search', '/about', '/newsletters', '/speaker-profile', '/resume']) {
    await page.setViewportSize(DESKTOP);
    await page.goto(`${BASE}${route}`, { waitUntil: 'networkidle' });

    await expect(page.getByTestId('public-header'), `header missing on ${route}`).toHaveCount(1);
    await expect(page.getByTestId('primary-nav'), `nav missing on ${route}`).toHaveCount(1);
    await expect(page.getByTestId('public-footer'), `footer missing on ${route}`).toHaveCount(1);
    await expect(page.getByTestId('main-content'), `main missing on ${route}`).toHaveCount(1);
  }
});

test('REQ-UI-005 nav collapses to a drawer below 768px', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
  await expect(page.getByTestId('primary-nav')).toBeVisible();

  await page.setViewportSize(MOBILE);
  await page.waitForTimeout(400);
  await expect(page.getByTestId('primary-nav'), 'primary nav should hide below 768px').toBeHidden();
  await expect(page.getByTestId('mobile-nav-trigger'), 'drawer trigger should appear').toBeVisible();
});

test('REQ-UI-005 no horizontal scroll at 320px on any public route', async ({ page }) => {
  await page.setViewportSize(NARROW);
  for (const route of ['/', '/series', '/search', '/about', '/speaker-profile', '/resume']) {
    await page.goto(`${BASE}${route}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(300);
    expect(await overflow(page), `${route} scrolls horizontally at 320px`).toBeLessThanOrEqual(1);
  }
});

// ---------------------------------------------------------------------------
// REQ-UI-049 — portfolio home: hero, stats, about, latest articles, contact
// ---------------------------------------------------------------------------
test('REQ-UI-049 portfolio home renders its sections in order', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });

  await expect(page.getByTestId('home-page')).toHaveCount(1);
  // Hero: photo + "Hi, I'm {name}" + title + CTA + social links
  await expect(page.getByTestId('resume-name')).toBeVisible();
  await expect(page.getByTestId('resume-greeting')).toHaveText(/Hi, I'm/);
  await expect(page.getByTestId('get-in-touch')).toBeVisible();
  await expect(page.getByTestId('resume-social-links')).toBeVisible();
  // About + latest-articles + contact sections
  await expect(page.getByTestId('home-about')).toHaveCount(1);
  await expect(page.getByTestId('home-latest-articles')).toHaveCount(1);
  await expect(page.getByTestId('contact-section')).toHaveCount(1);

  // Hero name must not be blank — the render gate's "value is present" check.
  expect((await page.getByTestId('resume-name').innerText()).trim().length).toBeGreaterThan(8);
});

test('REQ-UI-049 home looks right at desktop and mobile', async ({ page }) => {
  for (const [name, size] of [['1280', DESKTOP], ['390', MOBILE]] as const) {
    await page.setViewportSize(size);
    await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(400);
    expect(await overflow(page), `home overflows at ${name}`).toBeLessThanOrEqual(1);
    await assertNoOverlapAndSized(page, '[data-testid="home-stat-card"]', `home-stat-card@${name}`);
    await page.screenshot({ path: `tests/.artifacts/verify/home-${name}.png`, fullPage: true });
  }
});

// ---------------------------------------------------------------------------
// REQ-UI-020 — users list: badges, search, and the actions (now incl. edit/delete)
// ---------------------------------------------------------------------------
test('REQ-UI-020 users list renders rows with data in every cell', async ({ page }) => {
  await signIn(page);
  await page.setViewportSize(DESKTOP);
  await page.goto(`${BASE}/users`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="users-grid"]', { timeout: 25000 });

  const rows = await page.getByTestId('user-row-email').count();
  expect(rows, 'users grid rendered zero rows').toBeGreaterThan(0);

  // Render gate: cells non-empty, not just a count badge.
  for (const id of ['user-row-name', 'user-row-email', 'user-row-role', 'user-row-status']) {
    const texts = await page.getByTestId(id).allInnerTexts();
    expect(texts.length, `${id} rendered no cells`).toBe(rows);
    expect(texts.every(t => t.trim().length > 0), `${id} has a blank cell`).toBeTruthy();
  }

  // The count badge must agree with the visible rows (the "16 over blank rows" failure).
  const badge = await page.getByTestId('users-count').innerText();
  expect(badge).toContain(String(rows));
});

test('REQ-UI-020 users list exposes add, edit and delete actions', async ({ page }) => {
  await signIn(page);
  await page.goto(`${BASE}/users`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="users-grid"]', { timeout: 25000 });

  const rows = await page.getByTestId('user-row-email').count();
  await expect(page.getByTestId('new-user')).toBeVisible();
  expect(await page.getByTestId('user-edit').count(), 'edit action missing').toBe(rows);
  expect(await page.getByTestId('user-delete').count(), 'delete action missing').toBe(rows);
});

test('REQ-UI-020 search narrows the list', async ({ page }) => {
  await signIn(page);
  await page.goto(`${BASE}/users`, { waitUntil: 'networkidle' });
  await page.waitForSelector('[data-testid="users-grid"]', { timeout: 25000 });
  const before = await page.getByTestId('user-row-email').count();

  await page.getByTestId('users-search').fill('editor');
  await page.waitForTimeout(900);
  const after = await page.getByTestId('user-row-email').count();

  expect(after, 'search returned nothing').toBeGreaterThan(0);
  expect(after, 'search did not narrow the list').toBeLessThan(before);
});

test('REQ-UI-020 users screen looks right at desktop and mobile', async ({ page }) => {
  await signIn(page);
  for (const [name, size] of [['1280', DESKTOP], ['390', MOBILE]] as const) {
    await page.setViewportSize(size);
    await page.goto(`${BASE}/users`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="users-grid"]', { timeout: 25000 });
    expect(await overflow(page), `users overflows at ${name}`).toBeLessThanOrEqual(1);
    await page.screenshot({ path: `tests/.artifacts/verify/users-${name}.png`, fullPage: true });
  }
});

// ---------------------------------------------------------------------------
// REQ-FN-058 — a valid session must survive a deep link into an admin route
// ---------------------------------------------------------------------------
test('REQ-FN-058 deep link into an admin route keeps the session', async ({ page }) => {
  await signIn(page);
  // Full document load straight at a deep admin route — the reported failure was a bounce to /.
  await page.goto(`${BASE}/admin/speaking`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(1200);
  expect(new URL(page.url()).pathname, 'deep link bounced away from the admin route')
    .toBe('/admin/speaking');
  await expect(page.getByTestId('manage-speaking-page')).toHaveCount(1);

  await page.goto(`${BASE}/users`, { waitUntil: 'networkidle' });
  await page.waitForTimeout(1000);
  expect(new URL(page.url()).pathname, '/users deep link bounced').toBe('/users');
});

// ---------------------------------------------------------------------------
// REQ-FN-020 — listings / featured / reading time.
// The database is intentionally empty of posts (UAT-007), so the DATA-BEARING half cannot be
// observed today. What IS observable is the empty-state contract, which is asserted here; the
// data-bearing half is reported as unobservable rather than passed or failed.
// ---------------------------------------------------------------------------
test('REQ-FN-020 published-listing surfaces resolve and render their empty state', async ({ page }) => {
  await page.setViewportSize(DESKTOP);

  await page.goto(`${BASE}/`, { waitUntil: 'networkidle' });
  await expect(page.getByTestId('home-latest-articles')).toHaveCount(1);
  // With zero published posts the section must show its empty state, not a broken grid.
  await expect(page.getByTestId('home-articles-empty')).toHaveCount(1);
  // ...and the featured block must be ABSENT, not blank.
  await expect(page.getByTestId('home-featured')).toHaveCount(0);

  const res = await page.goto(`${BASE}/search`, { waitUntil: 'networkidle' });
  expect(res?.status(), '/search did not serve').toBeLessThan(400);

  const series = await page.goto(`${BASE}/series`, { waitUntil: 'networkidle' });
  expect(series?.status(), '/series did not serve').toBeLessThan(400);
});
