/**
 * _blogapp.ts — helpers for driving BlogApp, the MAUI Blazor Hybrid desktop head, over WebView2 CDP.
 *
 * How the verifier boots it (WSL cannot reach the Windows loopback where WebView2 opens CDP):
 *   1. powershell.exe -NoProfile -File tests\verify\blogapp-launch.ps1          (prints BLOGAPP_PID)
 *   2. node.exe tests\verify\blogapp-cdp-forward.mjs 9334 9333                  (Windows node, background)
 *   3. CDP_URL=http://<default-route-gateway>:9334 npx playwright test blogapp-desktop
 * The specs skip with a reason when CDP_URL is unset. A spec that has to restart the app (the stored
 * connection must survive one; a warm start is one) does so through restartApp(), which stops only the
 * BlogApp.exe built from THIS repository and launches it again with the same debugging port.
 *
 * SAFETY: BlogApp's stored connection normally points at PRODUCTION. Nothing in these specs writes
 * until assertLocalDatabase() has read the connection chip and found localhost:5550.
 */
import { chromium, Browser, Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

export const CDP_URL = process.env.CDP_URL ?? '';
/** Public web host running against the same LOCAL database (the orchestrator's port 5421). */
export const WEB = process.env.TB_BASE ?? 'http://172.18.144.1:5421';
/** The same host as the Windows-side BlogApp sees it (BlogApp's "website address"). */
export const WEB_FROM_WINDOWS = process.env.TB_WEB_FROM_WINDOWS ?? 'http://localhost:5421';
export const REPO = path.resolve(__dirname, '..', '..');
export const SECRETS = process.env.TB_SECRETS
  ?? '/mnt/c/Users/srkra/AppData/Roaming/Microsoft/UserSecrets/techieblog-host-secrets/secrets.json';
export const STORE_FILE = process.env.BLOGAPP_STORE
  ?? '/mnt/c/Users/srkra/AppData/Local/TechieRathore/dev.techierathore.techieblog.blogapp/Settings/securestorage.dat';
export const LOCAL_UPLOADS = process.env.BLOGAPP_LOCAL_UPLOADS
  ?? '/mnt/c/Users/srkra/AppData/Local/TechieBlog/BlogApp/wwwroot/uploads';
/** The uploads folder the local web host serves at /uploads — stands in for "the server's folder". */
export const SERVER_UPLOADS = process.env.TB_SERVER_UPLOADS ?? path.join(REPO, 'source/TechieBlog/wwwroot/uploads');
export const SERVER_UPLOADS_WIN = process.env.TB_SERVER_UPLOADS_WIN ?? 'C:\\1MyCode\\TechieBlog\\source\\TechieBlog\\wwwroot\\uploads';

export const LOCAL_DB = { host: 'localhost', port: '5550', db: 'TechieBlog', user: 'PgVectorAdmin', pass: 'AdminPass@2025' };

export const USERS = {
  admin: { email: 'Ravi@techieblog.com', password: process.env.TB_PW_ADMIN ?? 'VerifyAdmin#2026', landing: '/admin' },
  editor: { email: 'editor@techieblog.test', password: process.env.TB_PW_EDITOR ?? 'TriageYolo2026', landing: '/admin' },
  author: { email: 'author@techieblog.test', password: process.env.TB_PW_AUTHOR ?? 'TriageYolo2026', landing: '/BlogsList' },
  contributor: { email: 'contributor@techieblog.test', password: process.env.TB_PW_CONTRIBUTOR ?? 'Contrib#Pass1', landing: '/' },
} as const;

export const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

export function winPath(p: string): string {
  return execFileSync('wslpath', ['-w', p]).toString().trim();
}

function ps(command: string): string {
  return execFileSync('powershell.exe', ['-NoProfile', '-Command', command], { timeout: 120000 }).toString();
}

export function siteKeys(): { jwt: string; enc: string } {
  const s = JSON.parse(fs.readFileSync(SECRETS, 'utf8'));
  return { jwt: s['JwtSigningKey'], enc: s['AppEncryptionKey'] };
}

// ------------------------------------------------------------------ process control

const exeWin = () => winPath(path.join(REPO, 'source/BlogApp/bin/Debug/net10.0-windows10.0.19041.0/win-x64/BlogApp.exe'));

/** Stops every BlogApp.exe built from this repository (never another install) and waits for exit. */
export function stopApp(): void {
  ps(`$exe='${exeWin().replace(/'/g, "''")}'; ` +
     `Get-Process BlogApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe } | ` +
     `ForEach-Object { [void]$_.CloseMainWindow(); if (-not $_.WaitForExit(8000)) { Stop-Process -Id $_.Id -Force } }`);
}

/** Launches BlogApp with CDP on the port in CDP_URL's Windows side (9333) and waits for it. */
export async function startApp(): Promise<void> {
  const script = winPath(path.join(REPO, 'tests/verify/blogapp-launch.ps1'));
  execFileSync('powershell.exe', ['-NoProfile', '-File', script], { timeout: 150000 });
  const deadline = Date.now() + 90000;
  while (Date.now() < deadline) {
    try { if ((await fetch(`${CDP_URL}/json/version`)).ok) return; } catch { /* not up yet */ }
    await sleep(500);
  }
  throw new Error('BlogApp CDP endpoint never came up');
}

export async function restartApp(): Promise<void> {
  stopApp();
  await sleep(1500);
  await startApp();
}

// ------------------------------------------------------------------ page access

export async function attach(): Promise<{ browser: Browser; page: Page }> {
  const browser = await chromium.connectOverCDP(CDP_URL);
  const deadline = Date.now() + 60000;
  while (Date.now() < deadline) {
    for (const ctx of browser.contexts()) {
      for (const p of ctx.pages()) {
        if (p.url() && p.url() !== 'about:blank') {
          await ensureRendered(p);
          return { browser, page: p };
        }
      }
    }
    await sleep(400);
  }
  throw new Error('no WebView2 page found on the CDP session');
}

export async function waitRendered(page: Page, ms = 60000): Promise<void> {
  const deadline = Date.now() + ms;
  while (Date.now() < deadline) {
    if (await page.evaluate(() => document.querySelectorAll('[data-testid]').length > 0).catch(() => false)) return;
    await sleep(600);
  }
  throw new Error('BlogApp never rendered past its splash');
}

/** waitRendered, reloading the document up to twice when the WebView is stuck on its error splash. */
export async function ensureRendered(page: Page): Promise<void> {
  for (let attempt = 0; attempt < 3; attempt++) {
    try { await waitRendered(page, 15000); return; } catch (e) {
      if (attempt === 2) throw e;
      await page.reload({ waitUntil: 'domcontentloaded' }).catch(() => {});
    }
  }
}

/** Navigates through the app's OWN router — a hard goto of a deep route re-boots the WebView. */
export async function spaNav(page: Page, route: string, settle = 3200): Promise<void> {
  await page.evaluate((r) => (window as any).Blazor.navigateTo(r), route);
  await sleep(settle);
  const retry = page.locator('[data-testid="error-retry"]');
  if (await retry.count()) {
    await retry.first().click().catch(() => {});
    await sleep(2200);
  }
}

export async function fill(page: Page, testid: string, value: string): Promise<void> {
  const loc = page.locator(`[data-testid="${testid}"]`).first();
  await loc.waitFor({ state: 'visible', timeout: 20000 });
  const inner = loc.locator('input');
  const target = (await inner.count()) > 0 ? inner.first() : loc;
  await target.fill(value);
}

export async function click(page: Page, testid: string, timeout = 20000): Promise<void> {
  const loc = page.locator(`[data-testid="${testid}"]`).first();
  await loc.waitFor({ state: 'visible', timeout });
  await loc.scrollIntoViewIfNeeded().catch(() => {});
  await loc.click();
}

export async function textOf(page: Page, testid: string): Promise<string | null> {
  const loc = page.locator(`[data-testid="${testid}"]`).first();
  if ((await loc.count()) === 0) return null;
  return (await loc.innerText()).replace(/\s+/g, ' ').trim();
}

/** Picks an option of a TrBlazeUI Select by its trigger's data-testid and the option's visible text. */
export async function selectOption(page: Page, triggerTestId: string, optionText: string): Promise<void> {
  await click(page, triggerTestId);
  await sleep(700);
  await page.getByRole('option', { name: optionText, exact: true }).first().click();
  await sleep(700);
}

// ------------------------------------------------------------------ connection screen

export async function fillConnection(page: Page, c: typeof LOCAL_DB, withKeys = true): Promise<void> {
  await fill(page, 'connection-host', c.host);
  await fill(page, 'connection-port', c.port);
  await fill(page, 'connection-database', c.db);
  await fill(page, 'connection-username', c.user);
  await fill(page, 'connection-password', c.pass);
  if (withKeys) {
    const k = siteKeys();
    await fill(page, 'connection-jwt-key', k.jwt);
    await fill(page, 'connection-encryption-key', k.enc);
  }
}

/** Presses Test and waits for the NEW result alert (a stale one from the previous attempt is cleared first). */
export async function testConnection(page: Page): Promise<void> {
  const alerts = '[data-testid="connection-error"], [data-testid="connection-success"]';
  await click(page, 'connection-test');
  await page.waitForFunction((sel) => document.querySelectorAll(sel).length === 0, alerts, { timeout: 40000 }).catch(() => {});
  await page.locator(alerts).first().waitFor({ state: 'visible', timeout: 60000 });
  await sleep(400);
}

/** From a clean (unconfigured) start: configure the LOCAL database, save, and wait for the self-restart. */
export async function configureLocal(page: Page): Promise<void> {
  await fillConnection(page, LOCAL_DB);
  await fill(page, 'site-base-url', WEB_FROM_WINDOWS).catch(() => {});
  await testConnection(page);
  if (await textOf(page, 'connection-error')) throw new Error('local connection refused: ' + (await textOf(page, 'connection-error')));
  await click(page, 'connection-save');
  await sleep(6000); // save -> probe -> store -> AppRestarter relaunches the process
}

/**
 * Reads the always-visible connection chip. Throws unless it names localhost:5550 — the guard that
 * keeps these specs from ever writing to the production database the owner's BlogApp normally uses.
 */
export async function assertLocalDatabase(page: Page): Promise<string> {
  const label = (await textOf(page, 'desktop-connection-host')) ?? '';
  if (!/localhost/i.test(label) || !/5550/.test(label)) {
    throw new Error(`REFUSING TO CONTINUE: BlogApp reports "${label}", not localhost:5550`);
  }
  return label;
}

// ------------------------------------------------------------------ sign-in

/** Waits out the BlazorWebView re-render and signs in through the real form. */
export async function signIn(page: Page, who: keyof typeof USERS): Promise<void> {
  const u = USERS[who];
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 60000 });
  await fill(page, 'login-email', u.email);
  await fill(page, 'login-password', u.password);
  await click(page, 'login-submit');
  await sleep(7000);
}

/** Forgets the remembered session so the next start lands on the sign-in screen. */
export async function signOutByClearing(page: Page): Promise<void> {
  await page.evaluate(() => { localStorage.clear(); sessionStorage.clear(); });
  await page.goto('https://0.0.0.1/login', { waitUntil: 'domcontentloaded' });
  // A document load can leave the BlazorWebView on "An unhandled error has occurred" (an artefact of
  // reloading a live WebView, not something an operator does); reloading again clears it.
  await ensureRendered(page);
  await sleep(1500);
}

export function pathOf(page: Page): string {
  return new URL(page.url()).pathname;
}
