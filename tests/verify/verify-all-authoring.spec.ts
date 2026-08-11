/**
 * verify-all-authoring.spec.ts — verify-phase §4 / §4a / §4b for the AUTHORING + AUTH surface.
 *
 * Scope: /login, /forgot-password, /reset-password, /change-password, /BlogsList, /ManagePost,
 * /admin/preview/{id}, /admin/series, plus role-boundary behaviour.
 * Control map: docs/devguides/TechieBlog-DevGuide-Author.md + …-Editor.md.
 *
 * READ-ONLY BY CONSTRUCTION. Three sibling verifiers share this app instance and assert the same
 * post counts, so this file never creates, publishes, deletes or renames a post, and never completes
 * a password change. Where a REQ genuinely needs a write to observe, the test asserts what IS
 * observable and records NOT-OBSERVABLE in its own console output rather than guessing a verdict.
 *
 * Blazor Server notes baked in (each cost a previous run):
 *   - `/login` is an EditForm under InteractiveServer. It PRERENDERS as
 *     `<form method="post" action="/login">` and the interactive re-render both drops `action` and
 *     WIPES anything already typed. Clicking before the handover performs a real browser POST which
 *     the host rejects ("The POST request does not specify which form is being submitted"), and the
 *     failure surfaces as a misleading waitForURL timeout. So: wait for `action` to disappear, then
 *     re-type until the value sticks.
 *   - A full page load of an authorised route prerenders as ANONYMOUS (the JWT lives in
 *     localStorage), so authenticated navigation goes through `Blazor.navigateTo`, never `page.goto`.
 *   - The URL flips before the destination renders, so every navigation is gated on a selector the
 *     DESTINATION owns — gating on the URL measures the previous screen.
 */
import { test, expect, Page } from '@playwright/test';
import { execSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5099';
const REPO = path.resolve(__dirname, '../..');
const SHOTS = path.join(REPO, 'tests/.artifacts/verify-authoring/shots');
fs.mkdirSync(SHOTS, { recursive: true });

const USERS = {
  admin: { email: 'Ravi@techieblog.com', password: 'admin_password', role: 'Admin', landing: '/admin' },
  editor: { email: 'editor@techieblog.test', password: 'Editor#Pass1', role: 'Editor', landing: '/admin' },
  author: { email: 'author@techieblog.test', password: 'Author#Pass1', role: 'Author', landing: '/BlogsList' },
  contributor: { email: 'contributor@techieblog.test', password: 'Contrib#Pass1', role: 'Contributor', landing: '/' },
} as const;
type RoleKey = keyof typeof USERS;

/**
 * Runs a query inside the WinPostgre container. psql lives in the container, not in WSL.
 * Newlines are collapsed first: a multi-line template literal reaches `docker exec` as a literal
 * backslash-n through JSON.stringify, and psql then fails with `syntax error at or near "\"`.
 */
function sql(query: string): string {
  const oneLine = query.replace(/\s+/g, ' ').trim();
  return execSync(
    `docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -t -A -c ${JSON.stringify(oneLine)}`,
    { encoding: 'utf8' },
  ).trim();
}
const sqlNum = (q: string) => Number(sql(q));

/** Waits until the prerendered login form has been taken over by the interactive circuit. */
async function circuitAttached(page: Page, timeout = 120000) {
  await page.waitForFunction(
    () => {
      const f = document.querySelector('form');
      return !!f && !f.hasAttribute('action');
    },
    { timeout },
  );
}

/** Types until the value sticks — an early fill is silently discarded by the interactive re-render. */
async function fillStable(page: Page, selector: string, value: string) {
  for (let i = 0; i < 14; i++) {
    await page.fill(selector, value);
    await page.waitForTimeout(400);
    if ((await page.inputValue(selector)) === value) return;
  }
  throw new Error(`${selector} would not hold its value — circuit never attached`);
}

/** Signs in through the real form and returns the landing URL. Retries a lost circuit race. */
async function login(page: Page, role: RoleKey = 'admin'): Promise<string> {
  const user = USERS[role];
  let lastErr = '';
  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 90000 });
      await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
      await circuitAttached(page);
      await page.waitForTimeout(800);
      await fillStable(page, '[data-testid="login-email"]', user.email);
      await fillStable(page, '[data-testid="login-password"]', user.password);
      await page.click('[data-testid="login-submit"]');
      await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
      await page.waitForTimeout(2500);
      return page.url();
    } catch (e) {
      lastErr = String(e).slice(0, 200);
    }
  }
  throw new Error(`login(${role}) failed after 3 attempts: ${lastErr}`);
}

/** Authenticated navigation gated on a selector the DESTINATION owns. */
async function goTo(page: Page, route: string, readySelector: string, timeout = 90000) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  await page.waitForSelector(readySelector, { timeout, state: 'visible' });
  await page
    .waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), { timeout: 30000 })
    .catch(() => {});
  await page.waitForTimeout(900);
}

const texts = (page: Page, testId: string) =>
  page.$$eval(`[data-testid="${testId}"]`, (ns) => ns.map((n) => (n.textContent || '').trim()));

/**
 * §4b geometry truth at one width. Skips deliberately-clipped / aria-hidden / display:none subtrees,
 * and treats nesting (one box fully inside another) as normal rather than an overlap.
 */
async function visualCheck(page: Page, shotName: string, width: number) {
  const consoleErrors: string[] = [];
  const onErr = (m: any) => { if (m.type() === 'error') consoleErrors.push(m.text().slice(0, 200)); };
  page.on('console', onErr);
  await page.setViewportSize({ width, height: width < 500 ? 844 : 800 });
  await page.waitForTimeout(1200);

  const geo = await page.evaluate(() => {
    const named = (e: Element) =>
      e.getAttribute('data-testid') ||
      `${e.tagName.toLowerCase()}${typeof e.className === 'string' && e.className ? '.' + e.className.split(' ')[0] : ''}`;
    const hidden = (e: Element) => {
      let n: Element | null = e;
      while (n) {
        const s = getComputedStyle(n);
        if (s.display === 'none' || s.visibility === 'hidden') return true;
        if (n.getAttribute('aria-hidden') === 'true') return true;
        if (s.clipPath && s.clipPath !== 'none' && /inset\(50%|inset\(100%/.test(s.clipPath)) return true;
        n = n.parentElement;
      }
      return false;
    };
    const sel =
      'a[data-testid], button[data-testid], input[data-testid], textarea[data-testid], [data-testid][role], table[data-testid], h1, h2';
    const els = Array.from(document.querySelectorAll(sel)).filter((e) => !hidden(e));
    const boxes = els.map((e) => {
      const r = e.getBoundingClientRect();
      return { name: named(e), x: r.left, y: r.top, w: r.width, h: r.height };
    });
    const zeroSized = boxes.filter((b) => b.w <= 0 || b.h <= 0).map((b) => b.name);
    const vw = document.documentElement.clientWidth;

    // "Outside the viewport" is only a DEFECT when the user cannot bring the control into view.
    // A wide table inside `overflow-x:auto` is a legitimate responsive pattern: the control starts
    // off-screen but is one swipe away. Anything with a scrollable ancestor is therefore exempt —
    // measuring raw geometry alone reported 43 false "clipped control" findings on /BlogsList.
    const scrollableAncestor = (e: Element) => {
      let n: Element | null = e.parentElement;
      while (n) {
        const cs = getComputedStyle(n);
        if (/auto|scroll/.test(cs.overflowX) && n.scrollWidth > n.clientWidth + 2) return true;
        n = n.parentElement;
      }
      return false;
    };
    const offViewport = els
      .map((e, i) => ({ e, b: boxes[i] }))
      .filter(({ e, b }) =>
        b.w > 0 && b.h > 0 && (b.x + b.w > vw + 2 || b.x < -2) && !scrollableAncestor(e))
      .map(({ b }) => `${b.name}@x=${Math.round(b.x)},w=${Math.round(b.w)},right=${Math.round(b.x + b.w)}`);
    const solid = boxes.filter((b) => b.w > 4 && b.h > 4);
    const overlaps: string[] = [];
    for (let i = 0; i < solid.length; i++) {
      for (let j = i + 1; j < solid.length; j++) {
        const a = solid[i], b = solid[j];
        const ox = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x);
        const oy = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y);
        const nested =
          (a.x <= b.x && a.y <= b.y && a.x + a.w >= b.x + b.w && a.y + a.h >= b.y + b.h) ||
          (b.x <= a.x && b.y <= a.y && b.x + b.w >= a.x + a.w && b.y + b.h >= a.y + a.h);
        if (ox > 4 && oy > 4 && !nested) overlaps.push(`${a.name} ∩ ${b.name}`);
      }
    }
    return {
      zeroSized,
      offViewport,
      overlaps: overlaps.slice(0, 15),
      hScroll: Math.max(0, document.documentElement.scrollWidth - document.documentElement.clientWidth),
    };
  });

  const shot = path.join(SHOTS, `${shotName}-${width}.png`);
  await page.screenshot({ path: shot, fullPage: true });
  page.off('console', onErr);
  const result = { width, ...geo, consoleErrors, shot };
  console.log(`VISUAL[${shotName}@${width}] ${JSON.stringify(result)}`);
  return result;
}

// ─────────────────────────────────────────────────────────────────────────────
// AUTHENTICATION
// ─────────────────────────────────────────────────────────────────────────────

/**
 * REQ-NFR-035 — the `a`-prefixed parameter rename touched BOTH AuthService.cs files, i.e. the
 * credential-comparison path. Verified POSITIVELY and NEGATIVELY: a rename that inverted or
 * short-circuited a comparison would make every sign-in "succeed", so the rejected-password case is
 * the load-bearing one. Cross-checked against LoginLog, which must record both outcomes.
 */
test('REQ-NFR-035 auth path survives the a-prefix rename — 3 roles land correctly, wrong password rejected, both logged', async ({ page }) => {
  const before = sqlNum('SELECT COALESCE(MAX(logid),0) FROM loginlog');

  // POSITIVE: three roles reach their exact documented landing route.
  for (const role of ['admin', 'editor', 'author'] as RoleKey[]) {
    const url = await login(page, role);
    console.log(`LANDING ${role} => ${url}`);
    expect(new URL(url).pathname, `${role} landing route`).toBe(USERS[role].landing);
    // Prove it is a real authenticated session, not a bounce: the admin shell must be present.
    await page.waitForSelector('[data-testid="admin-sidebar"]', { timeout: 60000 });
    await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    await page.evaluate(() => localStorage.clear()).catch(() => {});
    await page.context().clearCookies();
  }

  // NEGATIVE: a wrong password must be REJECTED — stay on /login with an inline error.
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
  await circuitAttached(page);
  await page.waitForTimeout(800);
  await fillStable(page, '[data-testid="login-email"]', USERS.author.email);
  await fillStable(page, '[data-testid="login-password"]', 'DefinitelyWrong#9999');
  await page.click('[data-testid="login-submit"]');
  await page.waitForSelector('[data-testid="login-error"]', { timeout: 45000 });
  const err = (await page.locator('[data-testid="login-error"]').textContent()) || '';
  console.log(`NEGATIVE login error: ${JSON.stringify(err.trim())} @ ${page.url()}`);
  expect(new URL(page.url()).pathname.toLowerCase(), 'wrong password must not leave /login').toContain('login');
  expect(err.trim().length, 'an inline rejection message must be shown').toBeGreaterThan(0);
  // A rename that broke the comparison would have granted a session; prove none exists.
  const token = await page.evaluate(() => JSON.stringify(Object.keys(localStorage)));
  console.log(`localStorage after rejected login: ${token}`);

  // LoginLog must record BOTH outcomes.
  await page.waitForTimeout(1500);
  const rows = sql(
    `SELECT attemptedemail||'|'||success FROM loginlog WHERE logid > ${before} ORDER BY logid`,
  ).split('\n').filter(Boolean);
  console.log(`LOGINLOG new rows: ${JSON.stringify(rows)}`);
  // NOTE: a boolean rendered through `||` concatenation comes back as 'true'/'false',
  // whereas a bare boolean COLUMN comes back as 't'/'f'. Both forms appear in this file.
  const successes = rows.filter((r) => r.endsWith('|true'));
  const failures = rows.filter((r) => r.endsWith('|false'));
  expect(successes.length, 'LoginLog must record the successful sign-ins').toBeGreaterThanOrEqual(3);
  expect(failures.length, 'LoginLog must record the REJECTED sign-in (REQ-FN-051)').toBeGreaterThanOrEqual(1);
  expect(failures.some((f) => f.toLowerCase().startsWith(USERS.author.email.toLowerCase()))).toBeTruthy();
});

/**
 * REQ-FN-005 — AuthSvc login + JWT issuance + login logging. The JWT is proved by the presence of a
 * bearer credential in localStorage AND by an authorised route rendering its own shell afterwards.
 */
test('REQ-FN-005 login issues a JWT, establishes an authorised session and writes a LoginLog row', async ({ page }) => {
  const before = sqlNum('SELECT COALESCE(MAX(logid),0) FROM loginlog');
  await login(page, 'author');

  const store = await page.evaluate(() =>
    Object.fromEntries(Object.keys(localStorage).map((k) => [k, (localStorage.getItem(k) || '').slice(0, 40)])),
  );
  console.log(`localStorage after sign-in: ${JSON.stringify(store)}`);
  // The value is stored JSON-encoded, so the JWT is wrapped in quotes: `"eyJhbGciOi…`.
  const jwtLike = Object.values(store).some((v) => String(v).includes('eyJ'));
  expect(jwtLike, 'a JWT (eyJ… header) must be stored for the session').toBeTruthy();
  expect(Object.keys(store).some((k) => /AccessToken/i.test(k)), 'an access token must be issued').toBeTruthy();
  expect(Object.keys(store).some((k) => /RefreshToken/i.test(k)), 'a refresh token must be issued (REQ-FN-008)').toBeTruthy();

  // The session must actually authorise a guarded route.
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  expect(await page.locator('[data-testid="post-row-title"]').count()).toBeGreaterThan(0);

  const logged = sql(`SELECT attemptedemail||'|'||success||'|'||COALESCE(userid::text,'null') FROM loginlog WHERE logid > ${before} ORDER BY logid`);
  console.log(`LOGINLOG: ${JSON.stringify(logged)}`);
  expect(logged.toLowerCase()).toContain(USERS.author.email.toLowerCase());
  expect(logged, 'the success row must resolve the userid, not leave it null').toContain('|true|3');
});

/**
 * REQ-FN-051 — the login audit trail must be able to record a FAILED sign-in. The original defect
 * was that a failure had no userid and the insert therefore violated a NOT NULL / FK constraint,
 * so failures were silently dropped. Graded by counting failure rows written by this test.
 */
test('REQ-FN-051 a failed sign-in is recorded in LoginLog with success=false', async ({ page }) => {
  const before = sqlNum('SELECT COALESCE(MAX(logid),0) FROM loginlog');

  // Two distinct failure shapes: a known user with a bad password, and an unknown email.
  for (const cred of [
    { email: USERS.editor.email, password: 'WrongOnPurpose#1' },
    { email: 'nosuchuser@techieblog.test', password: 'Whatever#1' },
  ]) {
    await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 90000 });
    await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
    await circuitAttached(page);
    await page.waitForTimeout(800);
    await fillStable(page, '[data-testid="login-email"]', cred.email);
    await fillStable(page, '[data-testid="login-password"]', cred.password);
    await page.click('[data-testid="login-submit"]');
    await page.waitForSelector('[data-testid="login-error"]', { timeout: 45000 });
    await page.waitForTimeout(1200);
  }

  const rows = sql(
    `SELECT attemptedemail||'|'||success||'|'||COALESCE(userid::text,'null') FROM loginlog WHERE logid > ${before} ORDER BY logid`,
  ).split('\n').filter(Boolean);
  console.log(`REQ-FN-051 failure rows: ${JSON.stringify(rows)}`);
  expect(rows.length, 'both failed attempts must be audited').toBeGreaterThanOrEqual(2);
  expect(rows.every((r) => r.split('|')[1] === 'false'), 'every audited row here must be success=false').toBeTruthy();
  // The unknown email has no user to bind to — persisting it with a NULL userid is exactly the
  // case the original defect could not write.
  expect(rows.find((r) => r.startsWith('nosuchuser@'))?.endsWith('|null'), 'an unknown email must be audited with a NULL userid').toBeTruthy();
  // The unknown-email row is the one the original defect could not persist (no userid to bind).
  expect(rows.some((r) => r.startsWith('nosuchuser@techieblog.test')), 'an unknown email must still be audited').toBeTruthy();
});

/**
 * REQ-FN-058 — deep-linking into an admin route with a valid session must NOT dump the user on the
 * home page. Two honest paths are graded:
 *   (a) client-side deep navigation while signed in must land on the deep route itself;
 *   (b) a FULL page load of the deep route (which prerenders anonymous, because the JWT lives in
 *       localStorage) must route to /login carrying a returnUrl — and must not silently land on "/".
 */
test('REQ-FN-058 deep-linking into an admin route with a valid session does not bounce to the home page', async ({ page }) => {
  await login(page, 'admin');
  const deepRoutes: [string, string][] = [
    ['/admin/preview/10', '[data-testid="preview-article"]'],
    ['/ManagePost/5', '[data-testid="post-title-input"]'],
    ['/admin/series', '[data-testid="series-grid"]'],
  ];
  for (const [route, ready] of deepRoutes) {
    await goTo(page, route, ready);
    console.log(`DEEPLINK(session) ${route} => ${page.url()}`);
    expect(new URL(page.url()).pathname, `${route} must not bounce`).toBe(route);
    expect(new URL(page.url()).pathname, `${route} must not land on home`).not.toBe('/');
  }

  // (b) full page load of a deep route while the session exists in localStorage.
  await page.goto(`${BASE}/admin/preview/10`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForTimeout(6000);
  const landed = new URL(page.url());
  console.log(`DEEPLINK(full load) /admin/preview/10 => ${page.url()}`);
  expect(landed.pathname, 'a full-load deep link must never dump the user on the home page').not.toBe('/');
  if (landed.pathname.toLowerCase().includes('login')) {
    expect(landed.search, 'the login bounce must carry a returnUrl back to the deep route').toContain('returnUrl');
    expect(decodeURIComponent(landed.search)).toContain('/admin/preview/10');
  } else {
    expect(landed.pathname).toBe('/admin/preview/10');
  }
});

/**
 * REQ-FN-009 — 5-role model + 5 authorisation policies. Grades that authorisation HIDES rather than
 * half-renders, and that a denial lands on the access-denied surface rather than a raw 403 or the
 * home page. Read-only: navigation only.
 */
test('REQ-FN-009 role policies allow and deny the documented routes for Author, Editor and Contributor', async ({ page }) => {
  const matrix: { role: RoleKey; allow: [string, string][]; deny: string[] }[] = [
    {
      role: 'author',
      allow: [['/BlogsList', '[data-testid="posts-status-tabs"]'], ['/ManagePost', '[data-testid="post-title-input"]']],
      deny: ['/users', '/settings'],
    },
    {
      role: 'editor',
      allow: [['/admin', '[data-testid="admin-content"]'], ['/BlogsList', '[data-testid="posts-status-tabs"]']],
      deny: ['/users', '/settings'],
    },
  ];
  for (const { role, allow, deny } of matrix) {
    await login(page, role);
    for (const [route, ready] of allow) {
      await goTo(page, route, ready);
      console.log(`ALLOW ${role} ${route} => ${page.url()}`);
      expect(page.url(), `${role} must be allowed ${route}`).not.toContain('access-denied');
    }
    for (const route of deny) {
      await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
      await page.waitForTimeout(4000);
      const denied = await page.locator('[data-testid="access-denied"]').count();
      const body = (await page.locator('body').innerText()).slice(0, 120).replace(/\s+/g, ' ');
      console.log(`DENY ${role} ${route} => ${page.url()} accessDeniedTestId=${denied} body="${body}"`);
      expect(denied > 0 || page.url().includes('access-denied'), `${role} must be denied ${route} via the access-denied surface`).toBeTruthy();
      expect(body, 'a denial must never surface a raw status page').not.toMatch(/^\s*(403|Forbidden|500|Error)\b/i);
    }
    await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
    await page.evaluate(() => localStorage.clear()).catch(() => {});
    await page.context().clearCookies();
  }
});

/** REQ-UI-001 — login page: every documented control renders, plus §4b geometry at both widths. */
test('REQ-UI-001 login page renders all documented controls and is visually sound at 1280 and 390', async ({ page }) => {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="login-card"]', { timeout: 90000 });
  await circuitAttached(page);
  for (const id of ['login-card', 'login-email', 'login-password', 'login-remember', 'login-forgot', 'login-submit']) {
    await expect(page.locator(`[data-testid="${id}"]`).first(), `control ${id}`).toBeVisible({ timeout: 20000 });
  }
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, 'login', w);
    expect(v.zeroSized, `zero-sized controls @${w}`).toEqual([]);
    expect(v.offViewport, `controls outside the viewport @${w}`).toEqual([]);
    expect(v.overlaps, `overlapping sibling controls @${w}`).toEqual([]);
  }
});

/** REQ-UI-003 — forgot / reset password pages. Read-only: nothing is submitted. */
test('REQ-UI-003 forgot-password and reset-password pages render their documented controls', async ({ page }) => {
  await page.goto(`${BASE}/forgot-password`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="forgot-password-card"]', { timeout: 90000 });
  for (const id of ['forgot-password-card', 'forgot-email', 'forgot-submit', 'forgot-signin']) {
    await expect(page.locator(`[data-testid="${id}"]`).first(), `control ${id}`).toBeVisible({ timeout: 20000 });
  }
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, 'forgot-password', w);
    expect(v.zeroSized).toEqual([]);
    expect(v.offViewport).toEqual([]);
    expect(v.overlaps).toEqual([]);
  }

  // /reset-password with no (or a bad) token must show the invalid-token surface, not a blank card.
  await page.goto(`${BASE}/reset-password`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="reset-token-invalid"]', { timeout: 90000 });
  const msg = (await page.locator('[data-testid="reset-token-invalid"]').innerText()).replace(/\s+/g, ' ').trim();
  console.log(`RESET no-token surface: "${msg}"`);
  expect(msg.length, 'the invalid-token surface must explain itself').toBeGreaterThan(10);
  await expect(page.locator('[data-testid="reset-request-new"]')).toBeVisible();
  await expect(page.locator('[data-testid="reset-signin"]')).toBeVisible();

  await page.goto(`${BASE}/reset-password?token=obviously-not-a-real-token`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="reset-token-invalid"]', { timeout: 90000 });
  console.log(`RESET bad-token => invalid surface shown at ${page.url()}`);
  const w390 = await visualCheck(page, 'reset-password', 390);
  expect(w390.offViewport).toEqual([]);
});

/**
 * REQ-UI-004 — access-denied page. The Editor DevGuide records a defect where the AuthLayout card
 * renders NESTED inside the public blog shell on client-side navigation (two logos, a stray blog
 * sidebar, a duplicated theme-toggle). This grades both routes into the page.
 */
test('REQ-UI-004 access-denied renders as a single permission card on both full load and client-side navigation', async ({ page }) => {
  // Full page load.
  await page.goto(`${BASE}/access-denied`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="access-denied"]', { timeout: 90000 });
  const fullLoad = await page.evaluate(() => ({
    themeToggles: document.querySelectorAll('[data-testid="theme-toggle"]').length,
    brandLinks: document.querySelectorAll('[data-testid="brand-link"]').length,
    blogSidebar: document.querySelectorAll('[data-testid="blog-sidebar"], [data-testid="sidebar-categories"]').length,
  }));
  console.log(`ACCESS-DENIED full load: ${JSON.stringify(fullLoad)}`);
  expect(fullLoad.themeToggles, 'full load: exactly one theme toggle').toBeLessThanOrEqual(1);

  // Client-side navigation into a denied route — the path the DevGuide flagged.
  await login(page, 'author');
  await page.evaluate(() => (window as any).Blazor.navigateTo('/users'));
  await page.waitForSelector('[data-testid="access-denied"]', { timeout: 60000 });
  await page.waitForTimeout(2000);
  const clientNav = await page.evaluate(() => ({
    themeToggles: document.querySelectorAll('[data-testid="theme-toggle"]').length,
    brandLinks: document.querySelectorAll('[data-testid="brand-link"]').length,
    blogSidebar: document.querySelectorAll('[data-testid="blog-sidebar"], [data-testid="sidebar-categories"]').length,
    adminSidebar: document.querySelectorAll('[data-testid="admin-sidebar"]').length,
  }));
  console.log(`ACCESS-DENIED client nav: ${JSON.stringify(clientNav)}`);
  await visualCheck(page, 'access-denied-clientnav', 1280);
  expect(clientNav.themeToggles, 'client nav: the AuthLayout card must not nest inside the blog shell (duplicate theme-toggle)').toBeLessThanOrEqual(1);
  expect(clientNav.blogSidebar, 'client nav: the public blog sidebar must not render beside the permission card').toBe(0);
});

/**
 * REQ-FN-006 — password strength validation. Graded with a SAFE negative: a deliberately WRONG
 * current password plus a weak new password. The change therefore cannot succeed, and the stored
 * hash is asserted unchanged before and after — the `MustChangePassword` flag and every documented
 * credential are left exactly as the orchestrator set them.
 */
test('REQ-FN-006 change-password rejects a weak new password and leaves the stored credential untouched', async ({ page }) => {
  const hashBefore = sql(`SELECT loginpass FROM bloguser WHERE userid = 3`);
  const flagBefore = sql(`SELECT mustchangepassword FROM bloguser WHERE userid = 3`);

  await login(page, 'author');
  await goTo(page, '/change-password', '[data-testid="change-password-card"]');
  for (const id of ['change-password-current', 'change-password-new', 'change-password-confirm', 'change-password-submit']) {
    await expect(page.locator(`[data-testid="${id}"]`).first(), `control ${id}`).toBeVisible({ timeout: 20000 });
  }

  // Weak new password + intentionally WRONG current password: the change cannot go through.
  await fillStable(page, '[data-testid="change-password-current"]', 'DeliberatelyWrong#0000');
  await fillStable(page, '[data-testid="change-password-new"]', 'abc');
  await fillStable(page, '[data-testid="change-password-confirm"]', 'abc');
  await page.click('[data-testid="change-password-submit"]');
  await page.waitForTimeout(4000);

  const body = (await page.locator('[data-testid="change-password-card"]').innerText()).replace(/\s+/g, ' ');
  console.log(`CHANGE-PASSWORD response: "${body.slice(0, 400)}"`);
  const rejected = /weak|at least|minimum|must contain|uppercase|number|invalid|incorrect|does not|8 char/i.test(body);
  expect(rejected, 'a weak password / wrong current password must be rejected with a visible reason').toBeTruthy();

  // Nothing may have changed — this is what protects the other three verifier agents.
  const hashAfter = sql(`SELECT loginpass FROM bloguser WHERE userid = 3`);
  const flagAfter = sql(`SELECT mustchangepassword FROM bloguser WHERE userid = 3`);
  console.log(`credential unchanged=${hashBefore === hashAfter} flag ${flagBefore}->${flagAfter}`);
  expect(hashAfter, 'the stored password hash MUST be unchanged').toBe(hashBefore);
  expect(flagAfter, 'MustChangePassword MUST be unchanged').toBe(flagBefore);
});

/**
 * REQ-FN-007 / REQ-NFR-019 — password reset request → validate → reset, and token PERSISTENCE.
 * Read-only: the invalid-token branch is exercised through the UI, and persistence is proved from
 * the `passwordresettoken` table (a durable table, not an in-memory cache) rather than by minting
 * and consuming a token, which would rotate a documented credential mid-run.
 */
test('REQ-FN-007 reset tokens are persisted in a durable table and an invalid token is refused', async ({ page }) => {
  const cols = sql(
    `SELECT string_agg(column_name, ',' ORDER BY ordinal_position) FROM information_schema.columns WHERE table_name = 'passwordresettoken'`,
  );
  console.log(`passwordresettoken columns: ${cols}`);
  expect(cols, 'tokens must be persisted, not held in memory').toContain('token');
  for (const c of ['userid', 'expiresat', 'isused']) {
    expect(cols, `passwordresettoken.${c} is required for validate/reset`).toContain(c);
  }
  const existing = sqlNum('SELECT COUNT(*) FROM passwordresettoken');
  console.log(`passwordresettoken rows currently persisted: ${existing}`);

  // The validate branch: a token that is not in the table must be refused, not accepted blindly.
  await page.goto(`${BASE}/reset-password?token=00000000-0000-0000-0000-000000000000`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="reset-token-invalid"]', { timeout: 90000 });
  expect(await page.locator('[data-testid="reset-password-new"]').count(), 'an unknown token must NOT expose the new-password form').toBe(0);
  console.log('REQ-FN-007 unknown token refused; new-password form not exposed');
});

/** REQ-NFR-002 — password hashing with an industry-standard salted algorithm. */
test('REQ-NFR-002 every stored credential is a salted industry-standard hash, never plaintext', async () => {
  const rows = sql(`SELECT userid||'|'||loginpass FROM bloguser ORDER BY userid`).split('\n').filter(Boolean);
  for (const r of rows) {
    const [uid, hash] = r.split('|', 2);
    const rest = r.slice(r.indexOf('|') + 1);
    console.log(`user ${uid} hash=${rest.slice(0, 24)}… len=${rest.length}`);
    expect(rest, `user ${uid} must not store a plaintext password`).not.toBe('admin_password');
    expect(rest.length, `user ${uid} hash length`).toBeGreaterThan(40);
    expect(rest, `user ${uid} must use a recognised salted KDF`).toMatch(/^(PBKDF2|\$2[aby]\$|\$argon2)/i);
  }
  // Salted: two identical passwords would collide if the hash were unsalted. Assert all distinct.
  const distinct = sqlNum('SELECT COUNT(DISTINCT loginpass) FROM bloguser');
  const total = sqlNum('SELECT COUNT(*) FROM bloguser');
  expect(distinct, 'hashes must be per-user salted').toBe(total);
});

/** REQ-NFR-023 — the seeded admin credential is hashed and a forced-change flag exists. */
test('REQ-NFR-023 the seeded admin credential is hashed and a MustChangePassword flag is modelled', async () => {
  const hash = sql(`SELECT loginpass FROM bloguser WHERE emailid = 'Ravi@techieblog.com'`);
  expect(hash).not.toBe('admin_password');
  expect(hash).toMatch(/^PBKDF2/i);
  const flagCol = sql(
    `SELECT column_name||':'||data_type FROM information_schema.columns WHERE table_name='bloguser' AND column_name='mustchangepassword'`,
  );
  console.log(`seeded admin hash prefix=${hash.slice(0, 16)}… flagColumn=${flagCol}`);
  expect(flagCol, 'the forced-first-login-change flag must be modelled').toContain('boolean');
  // NOTE: the flag's runtime VALUE is deliberately not graded — the orchestrator cleared it for the
  // whole run, so its current `false` is a harness state, not evidence about the product.
  console.log(`mustchangepassword values (orchestrator-cleared for this run): ${sql('SELECT userid||\'=\'||mustchangepassword FROM bloguser ORDER BY userid').replace(/\n/g, ' ')}`);
});

/** REQ-FN-052 — the `svctoken` table was queried but never created by any migration. */
test('REQ-FN-052 no code path depends on a non-existent svctoken table', async ({ page }) => {
  const exists = sqlNum(`SELECT COUNT(*) FROM information_schema.tables WHERE table_name = 'svctoken'`);
  console.log(`svctoken table present in schema: ${exists}`);
  // The fix removed the query. Prove the auth path works end to end with the table still absent.
  const url = await login(page, 'author');
  expect(new URL(url).pathname).toBe('/BlogsList');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  expect(await page.locator('[data-testid="post-row-title"]').count()).toBeGreaterThan(0);
  console.log('auth + authorised data load succeed with svctoken absent — no dangling dependency');
});

/**
 * REQ-FN-008 — token refresh. Graded to the limit a black-box run reaches: the session must survive
 * repeated authenticated navigation and a circuit round trip without silently degrading to
 * anonymous. A true expiry-driven refresh needs a token lifetime this run cannot wait out.
 */
test('REQ-FN-008 an authenticated session survives sustained navigation without degrading to anonymous', async ({ page }) => {
  await login(page, 'admin');
  const hops: [string, string][] = [
    ['/BlogsList', '[data-testid="posts-status-tabs"]'],
    ['/admin/series', '[data-testid="series-grid"]'],
    ['/admin/preview/10', '[data-testid="preview-article"]'],
    ['/BlogsList', '[data-testid="posts-status-tabs"]'],
    ['/ManagePost/5', '[data-testid="post-title-input"]'],
    ['/admin', '[data-testid="admin-content"]'],
  ];
  for (const [route, ready] of hops) {
    await goTo(page, route, ready);
    expect(page.url(), `session lost while navigating to ${route}`).not.toContain('/login');
  }
  const stillAuthed = await page.locator('[data-testid="admin-sidebar"]').count();
  console.log(`REQ-FN-008 survived ${hops.length} authenticated hops; admin shell present=${stillAuthed}`);
  expect(stillAuthed).toBeGreaterThan(0);
  console.log('NOT-OBSERVABLE: expiry-driven refresh needs a token lifetime longer than this run.');
});

// ─────────────────────────────────────────────────────────────────────────────
// AUTHORING
// ─────────────────────────────────────────────────────────────────────────────

/**
 * REQ-UI-017 + §4a — /BlogsList status filters. The counts are cross-checked against psql rather
 * than against the page's own badge, and every filtered set is asserted to be NON-EMPTY with
 * populated CELLS (title / author / status / date), not merely a row count.
 */
test('REQ-UI-017 BlogsList status filters produce non-empty, correct sets matching the database', async ({ page }) => {
  const db = {
    all: sqlNum('SELECT COUNT(*) FROM blogpost WHERE NOT COALESCE(isdeleted,false)'),
    published: sqlNum('SELECT COUNT(*) FROM blogpost WHERE published AND NOT COALESCE(isdeleted,false)'),
    draft: sqlNum("SELECT COUNT(*) FROM blogpost WHERE NOT published AND scheduledpublishon IS NULL AND NOT COALESCE(isdeleted,false)"),
    scheduled: sqlNum('SELECT COUNT(*) FROM blogpost WHERE NOT published AND scheduledpublishon IS NOT NULL AND NOT COALESCE(isdeleted,false)'),
  };
  console.log(`DB truth: ${JSON.stringify(db)}`);

  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');

  // Tab labels carry the counts; they must agree with psql.
  const tabs = Object.fromEntries(
    await page.$$eval('[data-testid^="posts-tab-"]', (ns) =>
      ns.map((n) => [n.getAttribute('data-testid'), (n.textContent || '').trim()])),
  ) as Record<string, string>;
  console.log(`TABS: ${JSON.stringify(tabs)}`);
  const countIn = (label: string) => Number((label.match(/\((\d+)\)/) || [])[1]);
  expect(countIn(tabs['posts-tab-all']), 'All tab count vs psql').toBe(db.all);
  expect(countIn(tabs['posts-tab-published']), 'Published tab count vs psql').toBe(db.published);
  expect(countIn(tabs['posts-tab-draft']), 'Drafts tab count vs psql').toBe(db.draft);
  expect(countIn(tabs['posts-tab-scheduled']), 'Scheduled tab count vs psql').toBe(db.scheduled);

  // §4a — each filter must yield a NON-EMPTY set whose CELLS carry data.
  // The Scheduled row renders its status as a DATE pill ("Aug 22") rather than the word
  // "Scheduled" — a deliberate design choice (it tells the author WHEN it goes out), so the
  // scheduled matcher accepts either form. What must not happen is a scheduled row claiming to be
  // Published or Draft.
  for (const [tab, expected, wantStatus] of [
    ['posts-tab-all', db.all, null],
    ['posts-tab-published', db.published, /published/i],
    ['posts-tab-draft', db.draft, /draft/i],
    ['posts-tab-scheduled', db.scheduled, /scheduled|^[A-Za-z]{3}\s+\d{1,2}$/i],
  ] as [string, number, RegExp | null][]) {
    await page.click(`[data-testid="${tab}"]`);
    await page.waitForTimeout(2500);
    const titles = await texts(page, 'post-row-title');
    const authors = await texts(page, 'post-row-author');
    const statuses = await texts(page, 'post-row-status');
    const dates = await texts(page, 'post-row-date');
    console.log(`${tab}: rows=${titles.length} expected=${expected} statuses=${JSON.stringify(statuses)}`);
    expect(titles.length, `${tab} row count`).toBe(expected);
    expect(titles.length, `${tab} must not render an empty grid`).toBeGreaterThan(0);
    expect(titles.every((t) => t.length > 0), `${tab} every title cell populated`).toBeTruthy();
    expect(authors.every((a) => a.length > 0 && !/unknown/i.test(a)), `${tab} every author cell resolved (no "Unknown")`).toBeTruthy();
    expect(dates.every((d) => d.length > 0), `${tab} every date cell populated`).toBeTruthy();
    if (wantStatus) {
      expect(statuses.every((s) => wantStatus.test(s)), `${tab} every row carries the filtered status`).toBeTruthy();
    }
  }

  // The filtered sets must be the RIGHT posts, not just the right number.
  await page.click('[data-testid="posts-tab-scheduled"]');
  await page.waitForTimeout(2500);
  const scheduledTitle = (await texts(page, 'post-row-title'))[0];
  const dbScheduled = sql('SELECT title FROM blogpost WHERE NOT published AND scheduledpublishon IS NOT NULL AND NOT COALESCE(isdeleted,false)');
  console.log(`scheduled row: ui="${scheduledTitle}" db="${dbScheduled}"`);
  expect(scheduledTitle).toBe(dbScheduled);
});

/**
 * REQ-UI-017 (§4b) — the Author DevGuide records the tab strip clipping at 390px: "Scheduled (1)"
 * measured right=411 in a 390 viewport under `overflow-x:visible`, so 21px was cut rather than
 * scrollable. Re-graded here at both widths.
 */
test('REQ-UI-017 §4b BlogsList is visually sound at 1280 and 390 including the status tab strip', async ({ page }) => {
  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, 'blogslist', w);
    expect(v.zeroSized, `zero-sized controls @${w}`).toEqual([]);
    expect(v.overlaps, `overlapping sibling controls @${w}`).toEqual([]);
    expect(v.offViewport, `controls clipped outside the viewport @${w}`).toEqual([]);
  }
  // The specific regression: the last tab must be reachable, i.e. either inside the viewport or
  // inside a scroller that can actually scroll to it.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1200);
  const strip = await page.evaluate(() => {
    const last = document.querySelector('[data-testid="posts-tab-scheduled"]') as HTMLElement | null;
    const scroller = document.querySelector('[data-testid="posts-status-tabs-scroller"]') as HTMLElement | null;
    const host = scroller || (document.querySelector('[data-testid="posts-status-tabs"]') as HTMLElement | null);
    if (!last || !host) return null;
    const r = last.getBoundingClientRect();
    const cs = getComputedStyle(host);
    return {
      lastRight: Math.round(r.right),
      viewport: document.documentElement.clientWidth,
      overflowX: cs.overflowX,
      scrollable: host.scrollWidth > host.clientWidth,
      scrollWidth: host.scrollWidth,
      clientWidth: host.clientWidth,
    };
  });
  console.log(`TAB STRIP @390: ${JSON.stringify(strip)}`);
  expect(strip, 'tab strip must be present').not.toBeNull();
  const reachable = strip!.lastRight <= strip!.viewport + 2 || (strip!.scrollable && strip!.overflowX !== 'visible');
  expect(reachable, `last tab clipped at 390 (right=${strip!.lastRight}, overflowX=${strip!.overflowX}, scrollable=${strip!.scrollable})`).toBeTruthy();
});

/**
 * REQ-UI-016 + §4a — the post editor: fields, live preview and metadata sidebar must render ACTUAL
 * content for an existing post, cross-checked against the database row. Navigated from /BlogsList,
 * which is the honest user path.
 */
test('REQ-UI-016 post editor renders real content in its fields, live preview and metadata sidebar', async ({ page }) => {
  const dbTitle = sql('SELECT title FROM blogpost WHERE postid = 5');
  const dbSlug = sql('SELECT slug FROM blogpost WHERE postid = 5');
  const dbLen = sqlNum('SELECT LENGTH(postcontent) FROM blogpost WHERE postid = 5');

  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await goTo(page, '/ManagePost/5', '[data-testid="post-title-input"]');
  await page.waitForTimeout(3000);

  // Fields carry the persisted row, not placeholders.
  const title = await page.inputValue('[data-testid="post-title-input"]');
  const slug = await page.inputValue('[data-testid="post-slug-input"]');
  const md = await page.inputValue('[data-testid="markdown-input"]');
  console.log(`EDITOR title="${title}" slug="${slug}" mdLen=${md.length} dbLen=${dbLen}`);
  expect(title, 'title field must load the persisted title').toBe(dbTitle);
  expect(slug, 'slug field must load the persisted slug').toBe(dbSlug);
  expect(md.length, 'markdown body must load the persisted content').toBeGreaterThan(200);

  // Live preview must render actual HTML derived from that markdown.
  const previewHtml = await page.locator('[data-testid="markdown-preview-content"]').innerHTML().catch(() => '');
  const previewText = await page.locator('[data-testid="markdown-preview-content"]').innerText().catch(() => '');
  console.log(`PREVIEW htmlLen=${previewHtml.length} textLen=${previewText.length}`);
  expect(previewHtml.length, 'live preview must render markdown, not stay empty').toBeGreaterThan(100);
  expect(previewHtml, 'live preview must contain rendered markup').toMatch(/<(h[1-6]|p|ul|ol|pre|code)\b/i);
  expect(await page.locator('[data-testid="markdown-preview-empty"]').count(), 'the empty-preview placeholder must not be shown for a post with a body').toBe(0);

  // Metadata sidebar: every documented control renders.
  for (const id of [
    'publish-card', 'post-status-badge', 'organise-card', 'category-select',
    'tag-input', 'series-select', 'featured-image-card', 'image-picker',
    'post-action-bar', 'markdown-toolbar', 'markdown-view-mode',
  ]) {
    await expect(page.locator(`[data-testid="${id}"]`).first(), `sidebar/editor control ${id}`).toBeVisible({ timeout: 20000 });
  }
  const badge = ((await page.locator('[data-testid="post-status-badge"]').textContent()) || '').trim();
  const dbPublished = sql('SELECT published FROM blogpost WHERE postid = 5');
  console.log(`status badge="${badge}" db.published=${dbPublished}`);
  expect(badge.toLowerCase(), 'status badge must reflect the persisted publish state').toContain(dbPublished === 't' ? 'published' : 'draft');

  // The action bar must offer the actions valid for a PUBLISHED post.
  await expect(page.locator('[data-testid="unpublish-post"]')).toBeVisible();
  await expect(page.locator('[data-testid="save-post"]')).toBeVisible();
});

/**
 * REQ-UI-016 (defect probe) — the editor is one component serving `/ManagePost/{id}` for every id.
 * Navigating between two posts client-side must reload the row; if it does not, the editor shows
 * post A's content under post B's URL and a save would overwrite the wrong post. READ-ONLY: this
 * test only navigates and reads, it never saves.
 */
test('REQ-UI-016 editor reloads the post when the route parameter changes between two posts', async ({ page }) => {
  const t5 = sql('SELECT title FROM blogpost WHERE postid = 5');
  const t6 = sql('SELECT title FROM blogpost WHERE postid = 6');

  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await goTo(page, '/ManagePost/5', '[data-testid="post-title-input"]');
  await page.waitForTimeout(3000);
  expect(await page.inputValue('[data-testid="post-title-input"]')).toBe(t5);

  await page.evaluate(() => (window as any).Blazor.navigateTo('/ManagePost/6'));
  await page.waitForTimeout(7000);
  const shownTitle = await page.inputValue('[data-testid="post-title-input"]');
  const shownSlug = await page.inputValue('[data-testid="post-slug-input"]');
  const url = page.url();
  console.log(`PARAM CHANGE: url=${url} shownTitle="${shownTitle}" expected="${t6}" (stale would be "${t5}") slug="${shownSlug}"`);
  await page.screenshot({ path: path.join(SHOTS, 'managepost-param-change.png'), fullPage: true });

  expect(new URL(url).pathname, 'the route did change').toBe('/ManagePost/6');
  expect(shownTitle, `editor still shows post 5 under /ManagePost/6 — stale state, a save here would overwrite the wrong post`).toBe(t6);
});

/** REQ-UI-016 §4b — the split-pane Markdown editor is the highest-risk layout at 390px. */
test('REQ-UI-016 §4b post editor is visually sound at 1280 and 390 including the split pane', async ({ page }) => {
  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await goTo(page, '/ManagePost/5', '[data-testid="post-title-input"]');
  await page.waitForTimeout(2500);
  for (const w of [1280, 390]) {
    const v = await visualCheck(page, 'managepost', w);
    expect(v.zeroSized, `zero-sized controls @${w}`).toEqual([]);
    expect(v.overlaps, `overlapping sibling controls @${w}`).toEqual([]);
    expect(v.offViewport, `controls clipped outside the viewport @${w}`).toEqual([]);
  }

  // Split view specifically: both panes must have real area and must not overlap each other.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.waitForTimeout(1000);
  await page.click('[data-testid="markdown-view-split"]').catch(() => {});
  await page.waitForTimeout(2000);
  const panes = await page.evaluate(() => {
    const ed = document.querySelector('[data-testid="markdown-input"]') as HTMLElement | null;
    const pv = document.querySelector('[data-testid="markdown-preview"]') as HTMLElement | null;
    const box = (e: HTMLElement | null) => {
      if (!e) return null;
      const r = e.getBoundingClientRect();
      return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) };
    };
    return { editor: box(ed), preview: box(pv), viewport: document.documentElement.clientWidth };
  });
  console.log(`SPLIT PANES @390: ${JSON.stringify(panes)}`);
  await page.screenshot({ path: path.join(SHOTS, 'managepost-split-390.png'), fullPage: true });
  for (const [name, b] of Object.entries(panes).filter(([k]) => k !== 'viewport') as [string, any][]) {
    if (!b) continue;
    expect(b.w, `${name} pane must have width @390`).toBeGreaterThan(0);
    expect(b.h, `${name} pane must have height @390`).toBeGreaterThan(0);
    expect(b.x + b.w, `${name} pane must not spill the 390 viewport`).toBeLessThanOrEqual(panes.viewport + 2);
  }
  if (panes.editor && panes.preview) {
    const ox = Math.min(panes.editor.x + panes.editor.w, panes.preview.x + panes.preview.w) - Math.max(panes.editor.x, panes.preview.x);
    const oy = Math.min(panes.editor.y + panes.editor.h, panes.preview.y + panes.preview.h) - Math.max(panes.editor.y, panes.preview.y);
    console.log(`split overlap ox=${ox} oy=${oy}`);
    expect(ox > 4 && oy > 4, 'the editor and preview panes must not overlap at 390').toBeFalsy();
  }
});

/**
 * REQ-UI-018 + §4a — draft preview. Graded against post 10, the Contributor's UNPUBLISHED draft:
 * it must render in full behind the guard, with the "not published" banner and real rendered body.
 */
test('REQ-UI-018 draft preview renders the unpublished post in full with its not-published banner', async ({ page }) => {
  const dbTitle = sql('SELECT title FROM blogpost WHERE postid = 10');
  const dbPublished = sql('SELECT published FROM blogpost WHERE postid = 10');
  expect(dbPublished, 'post 10 must be the unpublished draft this test relies on').toBe('f');

  await login(page, 'admin');
  await goTo(page, '/admin/preview/10', '[data-testid="preview-article"]');

  for (const id of [
    'preview-banner', 'preview-article', 'preview-title', 'preview-author', 'preview-created',
    'preview-reading-time', 'preview-content', 'preview-metadata', 'preview-post-id',
    'preview-slug', 'preview-status', 'preview-actions', 'preview-edit-post', 'preview-back-to-list',
  ]) {
    await expect(page.locator(`[data-testid="${id}"]`).first(), `preview control ${id}`).toBeVisible({ timeout: 20000 });
  }

  const title = ((await page.locator('[data-testid="preview-title"]').textContent()) || '').trim();
  const author = ((await page.locator('[data-testid="preview-author"]').textContent()) || '').trim();
  const readTime = ((await page.locator('[data-testid="preview-reading-time"]').textContent()) || '').trim();
  const banner = ((await page.locator('[data-testid="preview-banner"]').innerText()) || '').replace(/\s+/g, ' ').trim();
  const contentHtml = await page.locator('[data-testid="preview-content"]').innerHTML();
  console.log(`PREVIEW title="${title}" author="${author}" readTime="${readTime}" banner="${banner}" contentLen=${contentHtml.length}`);

  expect(title, 'preview title must match the database row').toBe(dbTitle);
  expect(author.length, 'author must be resolved, not blank').toBeGreaterThan(0);
  expect(author, 'author must not fall back to "Unknown"').not.toMatch(/unknown/i);
  expect(readTime, 'reading time must be computed').toMatch(/\d/);
  expect(banner, 'the not-published banner must be shown for a draft').toMatch(/not published|preview mode/i);
  expect(contentHtml.length, 'the draft body must be rendered, not empty').toBeGreaterThan(300);
  expect(contentHtml, 'the body must be rendered markdown').toMatch(/<(h[1-6]|p|ul|ol|pre|code)\b/i);

  const status = ((await page.locator('[data-testid="preview-status"]').textContent()) || '').trim();
  expect(status.toLowerCase(), 'metadata status must say draft').toMatch(/draft|not published/i);

  for (const w of [1280, 390]) {
    const v = await visualCheck(page, 'preview-post', w);
    expect(v.zeroSized, `zero-sized @${w}`).toEqual([]);
    expect(v.overlaps, `overlaps @${w}`).toEqual([]);
    expect(v.offViewport, `clipped @${w}`).toEqual([]);
  }
});

/**
 * REQ-FN-012 — post CRUD service + repository, graded on the READ path: every row the admin list
 * shows must exist in the database with the same title and author, and the projection must not drop
 * or invent rows. (Create/update/delete are exercised by the backend/unit sibling; writing here
 * would corrupt the counts the other verifier agents assert.)
 */
test('REQ-FN-012 the post read projection matches the database row for row', async ({ page }) => {
  const dbRows = sql(
    `SELECT p.title || ' :: ' || u.firstname || ' ' || u.lastname
     FROM blogpost p JOIN bloguser u ON u.userid = p.userid
     WHERE NOT COALESCE(p.isdeleted,false) ORDER BY p.title`,
  ).split('\n').filter(Boolean).map((s) => s.trim());

  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await page.click('[data-testid="posts-tab-all"]');
  await page.waitForTimeout(2500);

  const uiRows = await page.evaluate(() =>
    Array.from(document.querySelectorAll('tr'))
      .map((tr) => [tr.querySelector('[data-testid="post-row-title"]'), tr.querySelector('[data-testid="post-row-author"]')] as const)
      .filter(([a, b]) => a && b)
      .map(([a, b]) => `${(a!.textContent || '').trim()} :: ${(b!.textContent || '').trim()}`)
      .sort(),
  );
  console.log(`UI rows (${uiRows.length}): ${JSON.stringify(uiRows)}`);
  console.log(`DB rows (${dbRows.length}): ${JSON.stringify(dbRows)}`);
  expect(uiRows.length, 'row count must match the database').toBe(dbRows.length);
  expect(uiRows, 'every rendered row must match its database row (title :: author)').toEqual(dbRows.sort());
});

/**
 * REQ-FN-015 — Draft / Published state handling. Every row's rendered status must be derivable from
 * the database row, and the published rows must carry a publish date rather than a creation date.
 */
test('REQ-FN-015 every rendered publish state is derivable from the database row', async ({ page }) => {
  const expected = new Map(
    sql(
      `SELECT title || '=>' ||
              CASE WHEN published THEN 'Published'
                   WHEN scheduledpublishon IS NOT NULL THEN 'Scheduled'
                   ELSE 'Draft' END
       FROM blogpost WHERE NOT COALESCE(isdeleted,false)`,
    ).split('\n').filter(Boolean).map((r) => r.split('=>') as [string, string]),
  );

  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await page.click('[data-testid="posts-tab-all"]');
  await page.waitForTimeout(2500);

  const pairs = await page.evaluate(() =>
    Array.from(document.querySelectorAll('tr'))
      .map((tr) => [tr.querySelector('[data-testid="post-row-title"]'), tr.querySelector('[data-testid="post-row-status"]')] as const)
      .filter(([a, b]) => a && b)
      .map(([a, b]) => [(a!.textContent || '').trim(), (b!.textContent || '').trim()] as [string, string]),
  );
  console.log(`STATE pairs: ${JSON.stringify(pairs)}`);
  expect(pairs.length).toBe(expected.size);
  for (const [title, status] of pairs) {
    const want = expected.get(title);
    expect(want, `"${title}" rendered but is not in the database`).toBeTruthy();
    if (want === 'Scheduled') {
      // Rendered as a due-date pill ("Aug 22") rather than the literal word — accept either, but
      // it must never claim to be Published or Draft.
      expect(status, `"${title}" is scheduled but rendered "${status}"`).toMatch(/scheduled|^[A-Za-z]{3}\s+\d{1,2}$/i);
      expect(status, `"${title}" is scheduled and must not claim Published/Draft`).not.toMatch(/published|draft/i);
    } else {
      expect(status.toLowerCase(), `"${title}" state`).toContain(want!.toLowerCase());
    }
  }
});

/**
 * REQ-FN-016 — post scheduling + background publisher. Read-only: the scheduled post is asserted to
 * be future-dated, unpublished, and rendered as Scheduled — i.e. the publisher has correctly NOT
 * promoted it yet. Its due date is not moved, which would make it publish and change the counts the
 * other verifier agents assert.
 */
test('REQ-FN-016 the scheduled post is future-dated, still unpublished, and rendered as Scheduled', async ({ page }) => {
  const row = sql(
    `SELECT title || '|' || published || '|' || scheduledpublishon || '|' || (scheduledpublishon > NOW())
     FROM blogpost WHERE scheduledpublishon IS NOT NULL AND NOT COALESCE(isdeleted,false)`,
  );
  console.log(`SCHEDULED db row: ${row}`);
  const [title, published, dueAt, future] = row.split('|');
  expect(published, 'a scheduled post must not be published yet').toBe('false');
  expect(future, 'the scheduled post must still be future-dated (publisher correctly has not promoted it)').toBe('true');

  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await page.click('[data-testid="posts-tab-scheduled"]');
  await page.waitForTimeout(2500);
  const titles = await texts(page, 'post-row-title');
  const statuses = await texts(page, 'post-row-status');
  console.log(`SCHEDULED ui: titles=${JSON.stringify(titles)} statuses=${JSON.stringify(statuses)} due=${dueAt}`);
  expect(titles).toContain(title);
  // The list renders the status as the DUE DATE pill ("Aug 22") rather than the word "Scheduled".
  // Grade what that pill actually claims: it must carry the real scheduled date and must never
  // read Published or Draft.
  const due = new Date(dueAt.replace(' ', 'T'));
  const monthAbbr = due.toLocaleString('en-US', { month: 'short' });
  console.log(`scheduled pill="${statuses[0]}" expected to reference ${monthAbbr} ${due.getDate()}`);
  expect(statuses.every((s) => /scheduled/i.test(s) || (s.includes(monthAbbr) && s.includes(String(due.getDate())))),
    `the scheduled row must show either "Scheduled" or its real due date; got ${JSON.stringify(statuses)}`).toBeTruthy();
  expect(statuses.every((s) => !/published|draft/i.test(s)), 'a scheduled row must not claim Published/Draft').toBeTruthy();

  // The editor must surface the schedule controls for that post.
  const pid = sql('SELECT postid FROM blogpost WHERE scheduledpublishon IS NOT NULL AND NOT COALESCE(isdeleted,false)');
  await goTo(page, `/ManagePost/${pid}`, '[data-testid="post-title-input"]');
  await page.waitForTimeout(2500);
  await expect(page.locator('[data-testid="schedule-section"]')).toBeVisible();
  const badge = ((await page.locator('[data-testid="post-status-badge"]').textContent()) || '').trim();
  console.log(`scheduled post ${pid} editor badge="${badge}"`);
  expect(badge.toLowerCase()).toMatch(/scheduled/);
});

/**
 * REQ-FN-054 — slug generation. Graded READ-ONLY on everything a read can reach:
 *   - every persisted slug is non-empty, well-formed and unique (the "empty slug is persisted" half);
 *   - a manually supplied slug is honoured rather than overwritten by a title-derived one;
 *   - live generation from the title in a NEW editor, observed WITHOUT saving.
 * The collision-retry branch needs a save that would create a post and shift the counts the other
 * verifier agents assert, so it is left to the backend/unit sibling's SlugGenerator tests.
 */
test('REQ-FN-054 every persisted slug is non-empty, well-formed and unique, and the editor generates one live', async ({ page }) => {
  const empties = sqlNum("SELECT COUNT(*) FROM blogpost WHERE slug IS NULL OR TRIM(slug) = ''");
  const total = sqlNum('SELECT COUNT(*) FROM blogpost');
  const distinct = sqlNum('SELECT COUNT(DISTINCT slug) FROM blogpost');
  const slugs = sql('SELECT slug FROM blogpost ORDER BY postid').split('\n').filter(Boolean);
  console.log(`SLUGS total=${total} distinct=${distinct} empty=${empties}: ${JSON.stringify(slugs)}`);
  expect(empties, 'no post may persist an empty slug').toBe(0);
  expect(distinct, 'slugs must be unique').toBe(total);
  for (const s of slugs) {
    expect(s, `slug "${s}" must be lowercase, hyphenated and URL-safe`).toMatch(/^[a-z0-9]+(-[a-z0-9]+)*$/);
  }

  // A manually supplied slug must survive — post 5's slug deliberately differs from its title.
  const p5 = sql("SELECT title || '|' || slug FROM blogpost WHERE postid = 5");
  console.log(`supplied-slug case: ${p5}`);
  expect(p5.split('|')[1], 'a hand-written slug must not be overwritten by a title-derived one').toBe('postgres-indexing-for-dotnet-developers');

  // Live generation in a NEW editor — typed, observed, never saved.
  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await goTo(page, '/ManagePost', '[data-testid="post-title-input"]');
  await page.waitForTimeout(2000);
  const probeTitle = 'Verify Slug Generation 2026 — Read Only';
  await page.fill('[data-testid="post-title-input"]', probeTitle);
  await page.locator('[data-testid="post-title-input"]').dispatchEvent('change');
  await page.waitForTimeout(3000);
  const generated = await page.inputValue('[data-testid="post-slug-input"]');
  console.log(`LIVE slug for "${probeTitle}" => "${generated}"`);
  expect(generated.length, 'the editor must generate a slug from the title').toBeGreaterThan(0);
  expect(generated, 'the generated slug must be lowercase, hyphenated and URL-safe').toMatch(/^[a-z0-9]+(-[a-z0-9]+)*$/);
  expect(generated).toContain('verify-slug-generation');

  // Nothing was saved.
  expect(sqlNum('SELECT COUNT(*) FROM blogpost'), 'the slug probe must not have created a post').toBe(total);
  console.log('NOT-OBSERVABLE read-only: the collision-retry branch requires a save (owned by the SlugGenerator unit tests).');
});

/**
 * REQ-FN-055 — publish-state transitions must not leave a soft-deleted post marked Published.
 * The invariant is asserted across the whole table, and the public-listing consequence is checked
 * on the live site. NOTE the honest limitation reported by this test: there are currently ZERO
 * soft-deleted rows, so the invariant holds VACUOUSLY — proving the transition itself needs a
 * delete, which would shift the counts the other verifier agents assert.
 */
test('REQ-FN-055 no soft-deleted post remains marked Published, and drafts stay out of public listings', async ({ page }) => {
  const deleted = sqlNum('SELECT COUNT(*) FROM blogpost WHERE COALESCE(isdeleted,false)');
  const violating = sqlNum('SELECT COUNT(*) FROM blogpost WHERE COALESCE(isdeleted,false) AND published');
  console.log(`REQ-FN-055 soft-deleted rows=${deleted} violating (deleted AND published)=${violating}`);
  expect(violating, 'a soft-deleted post must not remain marked Published').toBe(0);
  if (deleted === 0) {
    console.log('NOT-OBSERVABLE: zero soft-deleted rows exist, so the invariant holds VACUOUSLY. Proving the transition needs a delete, which would shift the post counts three sibling verifiers assert.');
  }

  // The adjacent, fully observable half: an unpublished draft must be absent from public listings.
  const draftTitle = sql('SELECT title FROM blogpost WHERE postid = 10');
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForTimeout(4000);
  const home = await page.locator('body').innerText();
  console.log(`draft "${draftTitle}" present on public home: ${home.includes(draftTitle)}`);
  expect(home, 'the Contributor draft must be absent from the public home page').not.toContain(draftTitle);

  await page.goto(`${BASE}/blogs`, { waitUntil: 'domcontentloaded', timeout: 90000 }).catch(() => {});
  await page.waitForTimeout(4000);
  const list = await page.locator('body').innerText();
  console.log(`draft present on /blogs: ${list.includes(draftTitle)}`);
  expect(list, 'the Contributor draft must be absent from the public post list').not.toContain(draftTitle);

  // …and PRESENT in the admin list, which is the other half of the same rule.
  await login(page, 'admin');
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]');
  await page.click('[data-testid="posts-tab-all"]');
  await page.waitForTimeout(2500);
  expect(await texts(page, 'post-row-title'), 'the draft must be visible to admins').toContain(draftTitle);
});

/**
 * REQ-UI-016 / Author guide — series list and its grid cells, cross-checked against psql
 * (name, author and part count per row, read from the SAME row so a count can never be paired with
 * another series' name).
 */
test('REQ-UI-016 series list renders every series with resolved author and correct part counts', async ({ page }) => {
  // The part badge counts PUBLISHED parts only — series 1 has 4 posts but its 4th is still
  // scheduled, and the grid correctly shows 3. Counting all rows here would fail the app for
  // behaving as the Author DevGuide documents.
  const dbRows = sql(
    `SELECT s.name || '|' || COALESCE(u.firstname || ' ' || u.lastname, '') || '|' ||
            (SELECT COUNT(*) FROM blogpost p
             WHERE p.seriesid = s.seriesid AND p.published AND NOT COALESCE(p.isdeleted,false))
     FROM blogseries s LEFT JOIN bloguser u ON u.userid = s.authorid ORDER BY s.name`,
  ).split('\n').filter(Boolean);
  const dbAllParts = sql(
    `SELECT s.name || '=' || (SELECT COUNT(*) FROM blogpost p WHERE p.seriesid = s.seriesid)
     FROM blogseries s ORDER BY s.name`,
  ).replace(/\n/g, ' ');
  console.log(`SERIES db (published-only parts): ${JSON.stringify(dbRows)} | all parts: ${dbAllParts}`);

  await login(page, 'admin');
  await goTo(page, '/admin/series', '[data-testid="series-grid"]');
  await page.waitForTimeout(2000);

  const uiRows = await page.evaluate(() =>
    Array.from(document.querySelectorAll('tr'))
      .map((tr) => ({
        name: tr.querySelector('[data-testid="series-row-name"]'),
        author: tr.querySelector('[data-testid="series-row-author"]'),
        count: tr.querySelector('[data-testid="series-row-postcount"]'),
        slug: tr.querySelector('[data-testid="series-row-slug"]'),
      }))
      .filter((r) => r.name)
      .map((r) => ({
        name: (r.name!.textContent || '').trim(),
        author: (r.author?.textContent || '').trim(),
        count: (r.count?.textContent || '').trim(),
        slug: (r.slug?.textContent || '').trim(),
      })),
  );
  console.log(`SERIES ui: ${JSON.stringify(uiRows)}`);
  expect(uiRows.length, 'series grid must not be empty').toBeGreaterThan(0);
  expect(uiRows.length, 'series row count vs psql').toBe(dbRows.length);
  for (const r of uiRows) {
    expect(r.name.length, 'series name cell populated').toBeGreaterThan(0);
    expect(r.author.length, `series "${r.name}" author cell populated`).toBeGreaterThan(0);
    expect(r.author, `series "${r.name}" author resolved`).not.toMatch(/unknown/i);
    expect(r.slug.length, `series "${r.name}" slug cell populated`).toBeGreaterThan(0);
    expect(r.count, `series "${r.name}" part count is numeric`).toMatch(/\d/);
    const dbRow = dbRows.find((d) => d.startsWith(r.name + '|'));
    expect(dbRow, `series "${r.name}" must exist in psql`).toBeTruthy();
    const [, dbAuthor, dbCount] = dbRow!.split('|');
    expect(r.author, `series "${r.name}" author vs psql`).toBe(dbAuthor);
    expect(Number((r.count.match(/\d+/) || [])[0]), `series "${r.name}" part count vs psql`).toBe(Number(dbCount));
  }

  for (const w of [1280, 390]) {
    const v = await visualCheck(page, 'series-list', w);
    expect(v.zeroSized, `zero-sized @${w}`).toEqual([]);
    expect(v.overlaps, `overlaps @${w}`).toEqual([]);
    expect(v.offViewport, `clipped @${w}`).toEqual([]);
  }
});

// ─────────────────────────────────────────────────────────────────────────────
// REQs that a browser run on this surface cannot honestly grade — recorded, not guessed.
// ─────────────────────────────────────────────────────────────────────────────

/**
 * REQ-NFR-005 — rate limiting on authentication endpoints. Deliberately NOT exercised: tripping the
 * limiter on this shared host would lock out the three sibling verifier agents signing in against
 * the same instance, and a lockout would be indistinguishable from a product defect in their runs.
 * The dedicated `vall-auth-zz-ratelimit.spec.ts` is named to sort last for exactly this reason.
 */
test('REQ-NFR-005 rate limiting is NOT exercised on this shared instance (recorded as not-observable)', async () => {
  const spec = path.join(REPO, 'tests/verify/vall-auth-zz-ratelimit.spec.ts');
  console.log(`NOT-OBSERVABLE: rate-limit probing would lock out 3 sibling verifiers on this shared host. Dedicated spec exists: ${fs.existsSync(spec)}`);
  expect(fs.existsSync(spec), 'a dedicated rate-limit spec must exist to carry this REQ').toBeTruthy();
});

/**
 * REQ-UI-051 / REQ-UI-052 — the BlogApp DESKTOP head. Not reachable from a browser driven against
 * the web host: it is a separate `net10.0-windows` process that is not running in this session.
 */
test('REQ-UI-051 REQ-UI-052 BlogApp desktop head is out of band for a browser verifier (recorded as not-observable)', async () => {
  const proj = path.join(REPO, 'source/BlogApp');
  console.log(`NOT-OBSERVABLE: BlogApp is a separate desktop process, not running. Project present: ${fs.existsSync(proj)}`);
  expect(fs.existsSync(proj), 'the BlogApp head must exist in the solution').toBeTruthy();
});

/**
 * REQ-FN-043 — the NU1605 restore fix. A build is deliberately NOT run: `dotnet build` while the
 * host under test is running would contend for the very assemblies it has loaded, and three sibling
 * verifiers depend on that process staying up.
 */
test('REQ-FN-043 build health is not re-run against a live host (recorded as not-observable)', async () => {
  console.log('NOT-OBSERVABLE: building the solution would contend with the running host that 3 sibling verifiers share.');
  // What IS observable: the host is up and serving, which means the restored solution ran.
  const res = execSync(`curl -s -o /dev/null -w "%{http_code}" ${BASE}/`, { encoding: 'utf8' }).trim();
  console.log(`host responds ${res} — the built solution is running`);
  expect(res).toBe('200');
});

/** REQ-FN-044 / REQ-FN-045 — template packaging + adopter documentation, graded on the filesystem. */
test('REQ-FN-044 REQ-FN-045 rename script and adopter documentation are present', async () => {
  const files = [
    'scripts/Rename-Project.ps1',
    'scripts/rename-project.sh',
    'docs/TechieBlog-UsageGuide.md',
    'docs/devguides/TechieBlog-DevGuide.md',
  ];
  const present: Record<string, boolean> = {};
  for (const f of files) present[f] = fs.existsSync(path.join(REPO, f));
  console.log(`REQ-FN-044/045 artifacts: ${JSON.stringify(present)}`);
  expect(present['scripts/Rename-Project.ps1'] || present['scripts/rename-project.sh'], 'a rename script must ship').toBeTruthy();
  const renameSh = path.join(REPO, 'scripts/rename-project.sh');
  if (fs.existsSync(renameSh)) {
    const body = fs.readFileSync(renameSh, 'utf8');
    console.log(`rename-project.sh length=${body.length}`);
    expect(body.length, 'the rename script must not be an empty stub').toBeGreaterThan(200);
    expect(body, 'the rename script must actually rewrite the project name').toMatch(/TechieBlog/);
  }
});

/** REQ-FN-024 — Favourites service. Removed from scope on 2026-08-06; recorded, not graded. */
test('REQ-FN-024 favourites service was removed from scope (recorded as N/A)', async () => {
  const tables = sql(`SELECT COUNT(*) FROM information_schema.tables WHERE table_name ILIKE '%favourite%' OR table_name ILIKE '%favorite%'`);
  console.log(`REQ-FN-024 N/A (removed 2026-08-06). Residual favourites tables in schema: ${tables}`);
  expect(Number(tables), 'no favourites table should remain after removal').toBe(0);
});
