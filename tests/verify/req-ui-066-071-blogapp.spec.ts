import { test, expect, Browser, Page, chromium } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { LOCAL_DB, REPO, sleep, winPath, ensureRendered, spaNav, fillConnection, fill, testConnection,
  textOf, click, assertLocalDatabase, signIn } from './_blogapp';

/**
 * The 2026-11-21 rows (REQ-UI-066, 067, 068, 070, 071) in BlogApp, the desktop admin app.
 *
 * SAFETY — the owner's BlogApp is connected to the LIVE database. This spec therefore never starts
 * BlogApp with the saved connection: it launches the exe with `--profile <folder>` (DesktopProfile),
 * which makes BlogApp ignore the OS credential store and keep its connection file, logs, uploads and
 * WebView2 data inside tests/.artifacts/blogapp-profile. The profile starts empty, so BlogApp opens
 * its connection screen; the spec points it at the LOCAL test database and refuses to go on unless
 * the connection chip then reads localhost:5550. BlogApp's own restart after saving keeps the switch.
 *
 * Settings come from tests/verify/testsettings.json (BlogApp section), never from environment
 * variables. The DevTools port is opened through WebView2's own launch argument, as the existing
 * harness does.
 */
type Settings = { CdpPort: number; ProfileFolder: string; WebsiteAddress: string };
const SETTINGS: Settings = JSON.parse(fs.readFileSync(path.join(__dirname, 'testsettings.json'), 'utf8')).BlogApp;
const CDP = `http://127.0.0.1:${SETTINGS.CdpPort}`;
const PROFILE = path.join(REPO, SETTINGS.ProfileFolder);
const OUT = path.join(REPO, 'tests/.artifacts/verify/req-ui-066-071-blogapp');
const EXE = path.join(REPO, 'source/BlogApp/bin/Debug/net10.0-windows10.0.19041.0/win-x64/BlogApp.exe');
const MASKED = /^[a-z0-9]•••@[a-z0-9.-]+$/;

let browser: Browser;
let page: Page;

function ps(command: string): string {
  return execFileSync('powershell.exe', ['-NoProfile', '-Command', command], { timeout: 120000 }).toString();
}

/** BlogApp processes started from this repository's build (never another install). */
function repoAppIds(): number[] {
  const out = ps(`$exe='${winPath(EXE).replace(/'/g, "''")}'; Get-Process BlogApp -ErrorAction SilentlyContinue | ` +
    `Where-Object { $_.Path -eq $exe } | ForEach-Object { $_.Id }; exit 0`);
  return out.split(/\s+/).filter(Boolean).map(Number);
}

function launchIsolated(): void {
  // Always from an EMPTY profile, so every run proves the same path: connection screen -> local DB.
  // The folder is under tests/.artifacts/ (git-ignored) and holds only what earlier runs wrote.
  if (!PROFILE.includes(`${path.sep}tests${path.sep}.artifacts${path.sep}`)) throw new Error(`refusing to clear ${PROFILE}`);
  fs.rmSync(PROFILE, { recursive: true, force: true });
  fs.mkdirSync(PROFILE, { recursive: true });
  ps(`$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=${SETTINGS.CdpPort} --remote-allow-origins=*'; ` +
     `Start-Process -FilePath '${winPath(EXE)}' -ArgumentList '--profile','"${winPath(PROFILE)}"' | Out-Null`);
}

async function attachPage(): Promise<Page> {
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    try {
      if (!browser || !browser.isConnected()) browser = await chromium.connectOverCDP(CDP);
      for (const ctx of browser.contexts())
        for (const p of ctx.pages())
          if (p.url() && p.url() !== 'about:blank') { await ensureRendered(p); return p; }
    } catch { /* not up yet, or the old process just quit */ }
    await sleep(1000);
  }
  throw new Error('BlogApp (isolated profile) never exposed a page over CDP');
}

async function openAnalytics(p: Page) {
  await spaNav(p, '/admin/analytics', 1000);
  await p.waitForSelector('[data-testid="analytics-popular-card"]', { timeout: 60000 });
  await p.waitForFunction(() => !document.querySelector('[data-testid="analytics-popular-loading"], [data-testid="analytics-referrer-loading"]'),
    undefined, { timeout: 60000 });
  await sleep(1500);
}

async function firstRated(p: Page, testId: string) {
  const cells = p.locator(`[data-testid="${testId}"]`);
  for (let i = 0; i < await cells.count(); i++)
    if (/★\s*\(\d+\)/.test(await cells.nth(i).innerText())) return cells.nth(i);
  return null;
}

async function assertMaskedDialog(p: Page) {
  const dialog = p.locator('[data-testid="ratings-dialog"]');
  await expect(dialog).toBeVisible({ timeout: 20000 });
  await p.waitForFunction(() => !document.querySelector('[data-testid="ratings-dialog-loading"]'), undefined, { timeout: 20000 });
  expect(await dialog.locator('[data-testid="ratings-row"]').count()).toBeGreaterThan(0);
  for (const r of await dialog.locator('[data-testid="ratings-row-rater"]').allInnerTexts()) expect(r.trim()).toMatch(MASKED);
  expect(/[A-Za-z0-9._%+-]{2,}@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/.test(await dialog.innerHTML())).toBe(false);
}

test.describe.configure({ mode: 'serial' });

test.beforeAll(async () => {
  test.setTimeout(400000);
  fs.mkdirSync(OUT, { recursive: true });
  expect(repoAppIds(), 'BlogApp must be closed before the run').toEqual([]);
  launchIsolated();
  page = await attachPage();
  // The empty profile has no connection, so BlogApp must open its connection screen.
  await page.waitForSelector('[data-testid="connection-host"]', { timeout: 60000 });
  await fillConnection(page, LOCAL_DB);
  await fill(page, 'site-base-url', SETTINGS.WebsiteAddress).catch(() => {});
  await testConnection(page);
  const refused = await textOf(page, 'connection-error');
  if (refused) throw new Error('local connection refused: ' + refused);
  await click(page, 'connection-save');
  await sleep(8000); // save -> store in the profile -> BlogApp restarts itself with the same --profile
  page = await attachPage();
  await page.waitForSelector('[data-testid="login-email"], [data-testid="desktop-connection-host"]', { timeout: 90000 });
  await assertLocalDatabase(page);
  await signIn(page, 'editor');
  await page.screenshot({ path: `${OUT}/signed-in.png` });
});

test.afterAll(async () => {
  await browser?.close().catch(() => {});
  for (const id of repoAppIds()) ps(`Stop-Process -Id ${id} -Force`);
});

test('REQ-UI-066 BlogApp: dashboard Quick Actions offers Analytics and it opens /admin/analytics', async () => {
  test.setTimeout(200000);
  await assertLocalDatabase(page);
  await spaNav(page, '/admin', 2500);
  const button = page.locator('[data-testid="quick-actions"] [data-testid="action-analytics"]');
  await expect(button).toBeVisible({ timeout: 60000 });
  await page.screenshot({ path: `${OUT}/dashboard.png` });
  await button.click();
  await page.waitForFunction(() => location.pathname.toLowerCase() === '/admin/analytics', undefined, { timeout: 60000 });
});

test('REQ-UI-067 BlogApp: Analytics Rating cell opens the masked ratings dialog', async () => {
  test.setTimeout(200000);
  await openAnalytics(page);
  const cell = await firstRated(page, 'popular-row-rating');
  expect(cell, 'a rated post is in range').not.toBeNull();
  expect((await cell!.innerText()).trim()).toMatch(/^\d\.\d ★ \(\d+\)$/);
  await cell!.click();
  await assertMaskedDialog(page);
  await page.screenshot({ path: `${OUT}/analytics-ratings-dialog.png` });
  await click(page, 'ratings-dialog-close');
});

test('REQ-UI-070 BlogApp: Copy numbers puts the numbers on the Windows clipboard as plain text', async () => {
  test.setTimeout(200000);
  await openAnalytics(page);
  const today = (await page.locator('[data-testid="analytics-views-today"]').innerText()).trim();
  await click(page, 'analytics-copy-numbers');
  await sleep(2500);
  expect(await page.locator('[data-testid="analytics-copy-fallback"]').count(), 'the clipboard accepted the text').toBe(0);
  const text = ps('Get-Clipboard -Raw');
  fs.writeFileSync(`${OUT}/copied-numbers.txt`, text);
  expect(text).toContain(`- Today (UTC): ${today}`);
  expect(text).toContain('Ten most read posts');
  expect(text).toContain('Where readers came from');
});

test('REQ-UI-071 BlogApp: Where readers came from lists referring sites by name', async () => {
  test.setTimeout(200000);
  await openAnalytics(page);
  const card = page.locator('[data-testid="analytics-referrer-card"]');
  await card.scrollIntoViewIfNeeded();
  const names = (await card.locator('[data-testid="referrer-row-name"]').allInnerTexts()).map((n) => n.trim());
  expect(names.length).toBeGreaterThan(0);
  expect(names.some((n) => /^[a-z0-9.-]+\.[a-z]{2,}$/.test(n)), 'at least one site name').toBe(true);
  expect(names.some((n) => n.includes('/') || n.includes('?'))).toBe(false);
  await page.screenshot({ path: `${OUT}/analytics-referrers.png` });
});

test('REQ-UI-068 BlogApp: Posts list Rating cell opens the ratings dialog', async () => {
  test.setTimeout(200000);
  await spaNav(page, '/BlogsList', 5000);
  await page.waitForSelector('[data-testid="posts-grid"]', { timeout: 60000 });
  await sleep(3000);
  const cells = page.locator('[data-testid="post-row-rating"]');
  expect(await cells.count()).toBeGreaterThan(0);
  const rated = await firstRated(page, 'post-row-rating');
  expect(rated, 'a listed post has a confirmed rating').not.toBeNull();
  await rated!.click();
  await assertMaskedDialog(page);
  await page.screenshot({ path: `${OUT}/posts-list-ratings-dialog.png` });
  await click(page, 'ratings-dialog-close');
});
