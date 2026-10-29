/**
 * BlogApp (MAUI Blazor Hybrid desktop head) — REQ-FN-047, REQ-UI-051, REQ-UI-052, REQ-UI-063, REQ-FN-062.
 *
 * Attaches to the running app over WebView2 CDP (CDP_URL) and skips with a reason when it is not set.
 * Boot recipe and the production-safety rule are in _blogapp.ts. Serial on purpose: one app, one
 * stored connection, and each test leaves the app signed out on the sign-in screen for the next.
 *
 * Everything runs against the LOCAL database (localhost:5550). BlogApp's stored connection normally
 * points at production, so the first test refuses to continue unless the app reports localhost:5550.
 */
import { test, expect, Browser, Page } from '@playwright/test';
import fs from 'node:fs';
import { execFileSync, spawn } from 'node:child_process';
import {
  CDP_URL, WEB, USERS, STORE_FILE, LOCAL_DB, sleep, attach, restartApp, waitRendered, spaNav, fill, click,
  textOf, fillConnection, testConnection, configureLocal, assertLocalDatabase, signIn, signOutByClearing,
  pathOf, siteKeys, stopApp, selectOption, winPath, REPO, SERVER_UPLOADS, SERVER_UPLOADS_WIN, LOCAL_UPLOADS,
} from './_blogapp';

test.skip(!CDP_URL, 'CDP_URL is not set: boot BlogApp with tests/verify/blogapp-launch.ps1 and forward its CDP port (see _blogapp.ts)');
test.describe.configure({ mode: 'serial' });

let browser: Browser | undefined;
let page: Page;

async function reattach(): Promise<void> {
  await browser?.close().catch(() => {});
  ({ browser, page } = await attach());
}

async function restart(): Promise<void> {
  await browser?.close().catch(() => {});
  browser = undefined;
  await restartApp();
  await reattach();
}

test.beforeAll(async () => { await reattach(); });
test.afterAll(async () => { await browser?.close().catch(() => {}); });

/** Bytes of the stored connection, searched as UTF-8 and UTF-16 for text that must never be readable. */
function storedContains(needle: string): boolean {
  const raw = fs.readFileSync(STORE_FILE);
  return raw.includes(Buffer.from(needle, 'utf8')) || raw.includes(Buffer.from(needle, 'utf16le'));
}

// ===================================================================================================
test('REQ-FN-047 BlogApp rejects an invalid connection, stores a valid one encrypted across a restart, and returns to setup when it is deleted', async () => {
  test.setTimeout(600000);

  // --- Establish a known starting point WITHOUT ever touching a non-local connection. -------------
  if (!pathOf(page).startsWith('/blogapp/connection')) {
    // Already configured: only continue when the app reports the LOCAL database.
    await assertLocalDatabase(page);
  }

  // --- Clause: deleting the stored connection returns the app to the setup screen. ----------------
  if (fs.existsSync(STORE_FILE)) {
    fs.unlinkSync(STORE_FILE);
    await restart();
  }
  expect(pathOf(page), 'with no stored connection the app opens its first-run setup').toBe('/blogapp/connection');
  await expect(page.locator('[data-testid="connection-setup-heading"]')).toContainText('Connect to your blog');

  // --- Clause: an invalid connection is rejected with a clear message. ----------------------------
  await fillConnection(page, { ...LOCAL_DB, pass: 'wrong-password-xyz' });
  await testConnection(page);
  const rejected = await textOf(page, 'connection-error');
  expect(rejected, 'a wrong password must be refused').toBeTruthy();
  expect(await textOf(page, 'connection-success')).toBeNull();
  expect(rejected!, 'the message says what went wrong').toMatch(/password|authentic|28P01|credential|refus|fail/i);

  // --- Clause: a valid connection is accepted, saved, and survives a restart. ---------------------
  await fillConnection(page, LOCAL_DB);
  await testConnection(page);
  expect(await textOf(page, 'connection-error'), 'the valid connection is accepted').toBeNull();
  expect(await textOf(page, 'connection-success')).toBeTruthy();
  const note = (await textOf(page, 'connection-storage-note')) ?? '';
  expect(note, 'the screen says where the secret is kept').toMatch(/secure|credential|encrypt|DPAPI/i);
  await fill(page, 'site-base-url', 'http://localhost:5421').catch(() => {});
  await click(page, 'connection-save');
  await sleep(6000);
  await reattach();
  await waitRendered(page);
  expect(pathOf(page), 'after saving, the app leaves setup').not.toContain('/blogapp/connection');
  await assertLocalDatabase(page);

  // --- Clause: stored encrypted, never as readable text. ------------------------------------------
  expect(fs.existsSync(STORE_FILE), 'a stored connection exists').toBe(true);
  expect(fs.statSync(STORE_FILE).size).toBeGreaterThan(100);
  const k = siteKeys();
  // Positive control: the search must be able to find something, or every "false" below proves nothing.
  const sample = fs.readFileSync(STORE_FILE).subarray(20, 28);
  expect(fs.readFileSync(STORE_FILE).includes(sample), 'search positive control').toBe(true);
  for (const secret of [LOCAL_DB.pass, LOCAL_DB.user, LOCAL_DB.host, 'TechieBlog', k.jwt, k.enc]) {
    expect(storedContains(secret), `"${secret.slice(0, 4)}..." must not be readable in the stored file`).toBe(false);
  }

  // --- Persists across a full restart. -------------------------------------------------------------
  await restart();
  expect(pathOf(page), 'a restarted app does not ask for the connection again').not.toContain('/blogapp/connection');
  expect(await assertLocalDatabase(page)).toMatch(/5550/);
});

// ===================================================================================================
test('REQ-UI-051 BlogApp opens at the login screen, signs an Admin into the admin shell, keeps a Contributor out, and logout returns to login', async () => {
  test.setTimeout(300000);
  await assertLocalDatabase(page);
  await signOutByClearing(page);

  // Opens at login.
  await expect(page.locator('[data-testid="login-card"]')).toBeVisible();
  await expect(page.locator('[data-testid="login-heading"]')).toHaveText('Sign in');
  const vt = await page.evaluate(() => ({
    overflowX: document.documentElement.scrollWidth > window.innerWidth + 1,
    zero: [...document.querySelectorAll('[data-testid]')]
      .filter((n) => { const r = n.getBoundingClientRect(); const s = getComputedStyle(n); return s.display !== 'none' && (r.width === 0 || r.height === 0) && (n.textContent ?? '').trim() !== ''; }).length,
  }));
  expect(vt.overflowX, 'no horizontal overflow on the login screen').toBe(false);
  expect(vt.zero, 'no rendered control collapsed to zero size').toBe(0);

  // Unauthenticated routes bounce to login.
  for (const route of ['/admin', '/BlogsList', '/settings']) {
    await spaNav(page, route);
    await expect(page.locator('[data-testid="login-card"]'), `${route} as a guest shows the sign-in card`).toBeVisible({ timeout: 15000 });
  }

  // Wrong password is refused.
  await spaNav(page, '/login', 2500);
  await fill(page, 'login-email', USERS.admin.email);
  await fill(page, 'login-password', 'not-the-password');
  await click(page, 'login-submit');
  await sleep(5000);
  await expect(page.locator('[data-testid="login-error"]')).toBeVisible();

  // A Contributor signs in but is refused every staff surface.
  await signOutByClearing(page);
  await signIn(page, 'contributor');
  for (const route of ['/admin', '/BlogsList', '/settings']) {
    await spaNav(page, route);
    // Refused = the access-denied screen, never the admin shell and never a dead window.
    expect(await page.locator('[data-testid="admin-content"]').count(), `Contributor on ${route} must not see the admin shell`).toBe(0);
    expect(await page.locator('[data-testid="access-denied"]').count(), `Contributor on ${route} sees access-denied`).toBeGreaterThan(0);
  }

  // Admin: lands on the dashboard inside the shared AdminLayout.
  await signOutByClearing(page);
  await signIn(page, 'admin');
  expect(pathOf(page).toLowerCase()).toBe('/admin');
  const shell = await page.evaluate(() => ({
    navs: document.querySelectorAll('[data-testid^="nav-"]').length,
    content: !!document.querySelector('[data-testid="admin-content"]'),
    account: !!document.querySelector('[data-testid="account-menu-trigger"]'),
  }));
  expect(shell.navs, 'the shared admin navigation renders').toBeGreaterThan(5);
  expect(shell.content).toBe(true);
  expect(shell.account).toBe(true);
  await assertLocalDatabase(page);

  // Settings is AdminOnly — an Admin reaches it.
  await spaNav(page, '/settings', 3800);
  expect(await page.locator('[data-testid="access-denied"]').count()).toBe(0);

  // Logout returns to the login screen.
  await spaNav(page, '/admin', 3000);
  await click(page, 'account-menu-trigger');
  await sleep(800);
  await click(page, 'account-logout');
  await sleep(4000);
  await expect(page.locator('[data-testid="login-card"]')).toBeVisible({ timeout: 20000 });
  expect(pathOf(page)).toContain('/login');
});

// ===================================================================================================
test('REQ-UI-063 BlogApp opens on the admin dashboard on every warm start for a remembered Admin (and on the role landing route for others)', async () => {
  test.setTimeout(400000);
  await assertLocalDatabase(page);

  for (const who of ['admin', 'author'] as const) {
    await signOutByClearing(page);
    await signIn(page, who);
    expect(pathOf(page).toLowerCase(), `${who} lands on ${USERS[who].landing} after signing in`).toBe(USERS[who].landing.toLowerCase());

    // Warm start: close the app and open it again; the remembered session must carry straight in.
    await restart();
    await expect(page.locator('[data-testid="desktop-connection-host"]')).toBeVisible({ timeout: 30000 });
    await sleep(4000);
    expect(pathOf(page).toLowerCase(), `${who}: a warm start opens on ${USERS[who].landing}, not the public home`).toBe(USERS[who].landing.toLowerCase());
    expect(await page.locator('[data-testid="admin-content"]').count(), `${who}: the admin shell is showing`).toBeGreaterThan(0);
    expect(await page.locator('[data-testid="login-card"]').count(), `${who}: no sign-in screen`).toBe(0);

    // A second warm start, to prove "every", not "the first".
    await restart();
    await sleep(5000);
    expect(pathOf(page).toLowerCase(), `${who}: the second warm start`).toBe(USERS[who].landing.toLowerCase());
  }

  // A cold start with nobody remembered opens the sign-in screen.
  await signOutByClearing(page);
  await restart();
  await sleep(4000);
  await expect(page.locator('[data-testid="login-card"]')).toBeVisible({ timeout: 20000 });
});

// ===================================================================================================
const PAGE_FAMILIES: { family: string; route: string; table?: string; where?: string }[] = [
  { family: 'dashboard', route: '/admin' },
  { family: 'posts', route: '/BlogsList', table: 'blogpost' },
  { family: 'posts', route: '/ManagePost' },
  { family: 'series', route: '/admin/series' },
  { family: 'comments', route: '/CommentsList' },
  { family: 'taxonomy', route: '/admin/categories', table: 'category' },
  { family: 'taxonomy', route: '/admin/tags', table: 'tag' },
  { family: 'media', route: '/admin/images', table: 'blogimage' },
  { family: 'resume', route: '/admin/profile' },
  { family: 'resume', route: '/admin/experience', table: 'userevents', where: "type='Experience'" },
  { family: 'resume', route: '/admin/skills' },
  { family: 'resume', route: '/admin/awards' },
  { family: 'resume', route: '/admin/stats' },
  { family: 'resume', route: '/admin/speaking', table: 'userevents', where: "type='Speaking'" },
  { family: 'users', route: '/users' },
  { family: 'users', route: '/AddUser' },
  { family: 'subscribers', route: '/admin/subscribers', table: 'subscriber' },
  { family: 'subscribers', route: '/admin/newsletter' },
  { family: 'analytics', route: '/admin/analytics' },
  { family: 'settings', route: '/settings' },
];

function psql(sql: string): string {
  return execFileSync('docker', ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-At', '-c', sql]).toString().trim();
}

test('REQ-UI-052 every admin page family opens in BlogApp and shows the live site database', async () => {
  test.setTimeout(600000);
  await assertLocalDatabase(page);
  await signOutByClearing(page);
  await signIn(page, 'admin');
  await assertLocalDatabase(page);

  // The sidebar must offer every family the requirement names.
  const navIds = await page.evaluate(() => [...document.querySelectorAll('[data-testid^="nav-"]')].map((n) => n.getAttribute('data-testid')));
  for (const id of ['nav-posts', 'nav-series', 'nav-comments', 'nav-categories', 'nav-tags', 'nav-images', 'nav-profile',
                    'nav-experience', 'nav-skills', 'nav-awards', 'nav-stats', 'nav-users', 'nav-subscribers', 'nav-newsletter', 'nav-settings']) {
    expect(navIds, `the admin sidebar lists ${id}`).toContain(id);
  }

  const problems: string[] = [];
  for (const f of PAGE_FAMILIES) {
    await spaNav(page, f.route, 4200);
    const probe = await page.evaluate(() => {
      const has = (id: string) => !!document.querySelector(`[data-testid="${id}"]`);
      const rows = [...document.querySelectorAll('tbody tr')].filter((tr) => !(tr.querySelectorAll('td').length === 1 && tr.querySelector('td')!.hasAttribute('colspan')));
      const cards = document.querySelectorAll('[data-testid="post-row-title"],[data-testid$="-row"]').length;
      const vw = window.innerWidth;
      return {
        denied: has('access-denied'), login: has('login-card'), notFound: has('not-found'), error: has('error-boundary'),
        content: has('admin-content'), rows: rows.length, cards,
        filledCells: rows.reduce((n, tr) => n + [...tr.querySelectorAll('td')].filter((td) => (td as HTMLElement).innerText.trim() !== '').length, 0),
        imgs: document.querySelectorAll('[data-testid="admin-content"] img').length,
        inputs: document.querySelectorAll('input,textarea,select,button').length,
        overflowX: document.documentElement.scrollWidth > vw + 2,
        text: (document.body.innerText || '').length,
      };
    });
    const tag = `${f.family} ${f.route}`;
    if (probe.denied || probe.login || probe.notFound || probe.error || !probe.content) problems.push(`${tag}: denied=${probe.denied} login=${probe.login} notFound=${probe.notFound} error=${probe.error} shell=${probe.content}`);
    else if (probe.overflowX) problems.push(`${tag}: horizontal page overflow`);
    else if (probe.text < 120) problems.push(`${tag}: rendered almost nothing (${probe.text} chars)`);
    if (f.table) {
      const inDb = Number(psql(`select count(*) from ${f.table}${f.where ? ' where ' + f.where : ''}`));
      const shown = probe.rows + probe.cards + probe.imgs;
      if (inDb > 0 && shown === 0) problems.push(`${tag}: database has ${inDb} ${f.table} rows, page shows none`);
      if (inDb > 0 && probe.rows > 0 && probe.filledCells === 0) problems.push(`${tag}: rows render with empty cells`);
    }
    await page.screenshot({ path: `tests/.artifacts/test-results/blogapp-052-${f.route.replace(/\W+/g, '_')}.png` }).catch(() => {});
  }
  expect(problems, problems.join('\n')).toEqual([]);

  // Theme switch works from the desktop head too.
  await spaNav(page, '/admin', 3000);
  const before = await page.evaluate(() => document.documentElement.className);
  await click(page, 'theme-toggle');
  await sleep(1500);
  const after = await page.evaluate(() => document.documentElement.className);
  expect(after, 'the theme toggle changes the root class').not.toBe(before);
  await click(page, 'theme-toggle');
  await sleep(1000);
});

test('REQ-UI-052 a post authored and published in BlogApp is immediately visible on the public website', async () => {
  test.setTimeout(400000);
  await assertLocalDatabase(page);
  const stamp = Date.now();
  const slug = `fxa-roundtrip-${stamp}`;
  const title = `fxa roundtrip ${stamp}`;
  const web = async (p: string) => { const r = await fetch(WEB + p); return { status: r.status, body: await r.text() }; };

  expect((await web(`/post/${slug}`)).status, 'the fixture slug must not exist yet').not.toBe(200);

  await spaNav(page, '/ManagePost', 4500);
  await fill(page, 'post-title-input', title);
  await fill(page, 'post-slug-input', slug);
  await fill(page, 'post-excerpt-input', 'Created by the BlogApp desktop-head spec to prove the public round trip.');
  await page.evaluate(() => {
    const ta = [...document.querySelectorAll('textarea')].find((n) => n.getAttribute('data-testid') !== 'post-excerpt-input' && !n.closest('[data-testid="post-excerpt-input"]'));
    if (!ta) return;
    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value')!.set!.call(ta, '## Round trip\n\nWritten in the BlogApp desktop head against the local site database.');
    ta.dispatchEvent(new Event('input', { bubbles: true }));
    ta.dispatchEvent(new Event('change', { bubbles: true }));
  });
  await click(page, 'category-select');
  await sleep(1200);
  const option = (await page.locator('[role="option"]').allInnerTexts()).map((t) => t.trim()).find((t) => t && !t.startsWith('--'));
  expect(option, 'the category list offers a category').toBeTruthy();
  await page.getByRole('option', { name: option!, exact: true }).first().click();
  await sleep(1000);
  await click(page, 'publish-post', 30000);
  await sleep(6000);

  try {
    // Publishing returns the author to the post list, where the row carries the Published badge.
    await expect(page.locator('tr', { hasText: slug }).locator('[data-testid="post-row-status"]').first()).toHaveText(/Published/i, { timeout: 15000 });
    // "Immediately": no restart, no waiting out the 10-minute cache.
    const deadline = Date.now() + 15000;
    let post = await web(`/post/${slug}`);
    while (post.status !== 200 && Date.now() < deadline) { await sleep(1000); post = await web(`/post/${slug}`); }
    expect(post.status, 'the public site serves the new post').toBe(200);
    expect(post.body).toContain(title);
    const home = await web('/');
    expect(home.body, 'and lists it on the public home page').toContain(title);
    expect(psql(`select count(*) from blogpost where slug='${slug}' and published`)).toBe('1');
  } finally {
    // Remove the fixture through the app.
    await spaNav(page, '/BlogsList', 4500);
    await fill(page, 'posts-search', slug);
    await sleep(1500);
    const target = page.locator('tr', { hasText: slug }).first();
    if (await target.count()) {
      await target.locator('[data-testid="post-delete"]').first().click();
      await sleep(1500);
      await click(page, 'post-delete-confirm');
      await sleep(3500);
    }
  }
  expect(psql(`select count(*) from blogpost where slug='${slug}' and not coalesce(isdeleted,false)`), 'fixture removed').toBe('0');
});

// ===================================================================================================
// REQ-FN-062 — where BlogApp puts uploaded images. The delivery the code implements is explicit:
//   * transport "Keep uploads on this machine" (the default) -> the file stays on the desktop machine
//   * transport "Folder" / "SFTP"                           -> the file is delivered to the SERVER's uploads
//     directory and never lands on the desktop.
// SFTP is not exercised here: it needs SSH credentials for the real server, which this local run must
// never touch. The Folder transport stands in for "the server's folder" (the local web host's uploads dir).
const FIXTURE_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==', 'base64');

function listFiles(dir: string): string[] {
  const out: string[] = [];
  const walk = (d: string) => {
    if (!fs.existsSync(d)) return;
    for (const e of fs.readdirSync(d, { withFileTypes: true })) {
      const full = `${d}/${e.name}`;
      if (e.isDirectory()) walk(full); else out.push(full);
    }
  };
  walk(dir);
  return out.sort();
}

async function setMediaTransport(label: string, folderWin?: string): Promise<void> {
  await spaNav(page, '/blogapp/connection/settings', 3500);
  await fillConnection(page, LOCAL_DB);
  await selectOption(page, 'media-transport', label);
  if (folderWin) await fill(page, 'media-root-path', folderWin);
  await fill(page, 'site-base-url', 'http://localhost:5421');
  await testConnection(page);
  expect(await textOf(page, 'connection-error'), 'the connection test passes').toBeNull();
  await click(page, 'connection-save');
  await sleep(6000);
  await reattach();
  await waitRendered(page);
  await assertLocalDatabase(page);
  // The remembered session forwards to the role's landing route; wait for it so the forward cannot
  // overwrite the next navigation.
  await page.locator('[data-testid="admin-content"]').waitFor({ state: 'visible', timeout: 40000 });
  await sleep(1500);
}

/** Opens the upload dialog, answers the OS picker with the fixture, uploads, and returns the stored image row. */
async function uploadThroughPicker(category: string, fixtureWin: string): Promise<{ id: string; path: string }> {
  await spaNav(page, '/admin/images', 4000);
  await click(page, 'upload-image');
  await selectOption(page, 'upload-category-select', category);
  void click(page, 'upload-choose-native').catch(() => {});
  // Asynchronous on purpose: the click above is still in flight on this event loop and a blocking
  // child process would stop it ever being dispatched.
  const picked = await new Promise<string>((resolve) => {
    const child = spawn('powershell.exe', ['-NoProfile', '-File', winPath(`${REPO}/tests/verify/blogapp-pick-file.ps1`), '-File', fixtureWin, '-TimeoutSeconds', '40']);
    let out = '';
    child.stdout.on('data', (d) => { out += d; });
    child.stderr.on('data', (d) => { out += d; });
    child.on('close', () => resolve(out));
  });
  expect(picked, 'the OS file dialog accepted the fixture').toContain('PICKED=');
  await expect(page.locator('[data-testid="upload-selected-file"]')).toContainText('fxa-fn062.png', { timeout: 20000 });
  await fill(page, 'upload-alt-text-input', 'fxa fn062 fixture');
  await click(page, 'upload-confirm', 30000);
  await sleep(5000);
  const row = psql("select blogimageid || '|' || imagepath from blogimage where alttext='fxa fn062 fixture' order by blogimageid desc limit 1");
  expect(row, 'the upload recorded a blogimage row').toContain('|');
  const [id, imgPath] = row.split('|');
  return { id, path: imgPath };
}

test('REQ-FN-062 BlogApp delivers uploads where the transport says: the server folder when one is configured, this machine only when told so', async () => {
  test.setTimeout(900000);
  await assertLocalDatabase(page);
  await signOutByClearing(page);
  await signIn(page, 'admin');
  await assertLocalDatabase(page);

  const fixture = `${REPO}/tests/.artifacts/build-phase-20261028/a/fxa-fn062.png`;
  fs.mkdirSync(`${REPO}/tests/.artifacts/build-phase-20261028/a`, { recursive: true });
  fs.writeFileSync(fixture, FIXTURE_PNG);
  const fixtureWin = winPath(fixture);
  const created: string[] = [];

  try {
    // ---- 1. Folder transport -> the server's uploads folder, nothing on the desktop ---------------
    await setMediaTransport('Write to a mapped drive or network share', SERVER_UPLOADS_WIN);
    const serverBefore = listFiles(SERVER_UPLOADS);
    const localBefore = listFiles(LOCAL_UPLOADS);
    const a = await uploadThroughPicker('Logos', fixtureWin);
    created.push(a.id);
    const serverNew = listFiles(SERVER_UPLOADS).filter((f) => !serverBefore.includes(f));
    const localNew = listFiles(LOCAL_UPLOADS).filter((f) => !localBefore.includes(f));
    expect(serverNew.length, 'one new file in the server uploads folder').toBe(1);
    expect(localNew, 'nothing written to the desktop machine').toEqual([]);
    expect(a.path, 'the stored path is the site-relative /uploads path the website serves').toMatch(/^\/uploads\/logos\//);
    const served = await fetch(WEB + a.path);
    expect(served.status, 'the website serves the delivered file').toBe(200);
    expect((await served.arrayBuffer()).byteLength).toBe(FIXTURE_PNG.length);

    // ---- 2. Default transport -> stays on this machine (and the screen says so) --------------------
    await setMediaTransport('Keep uploads on this machine');
    await spaNav(page, '/blogapp/connection/settings', 3500);
    await expect(page.locator('[data-testid="media-none-warning"]')).toBeVisible();
    const serverBefore2 = listFiles(SERVER_UPLOADS);
    const localBefore2 = listFiles(LOCAL_UPLOADS);
    const b = await uploadThroughPicker('Logos', fixtureWin);
    created.push(b.id);
    expect(listFiles(LOCAL_UPLOADS).filter((f) => !localBefore2.includes(f)).length, 'one new file on the desktop machine').toBe(1);
    expect(listFiles(SERVER_UPLOADS).filter((f) => !serverBefore2.includes(f)), 'nothing reached the server folder').toEqual([]);
  } finally {
    // Remove the fixtures: rows through the app where possible, files from disk.
    for (const id of created) psql(`delete from blogimage where blogimageid=${id}`);
    for (const dir of [SERVER_UPLOADS, LOCAL_UPLOADS]) {
      for (const f of listFiles(dir)) {
        if (fs.statSync(f).size === FIXTURE_PNG.length && /logos\/logos-\d+-\d{14}-[0-9a-f]+\.png$/.test(f)
            && Date.now() - fs.statSync(f).mtimeMs < 30 * 60 * 1000) fs.unlinkSync(f);
      }
    }
  }
});
