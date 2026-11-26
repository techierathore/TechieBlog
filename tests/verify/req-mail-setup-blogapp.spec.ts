/**
 * REQ-UI-072 in BlogApp: the desktop Settings screen's "Send test mail" shows the mail server's real
 * answer, good or bad (owner request 2026-11-26).
 *
 * Isolation (owner rules 2026-11-21 and 2026-11-26):
 *  - BlogApp is launched from this repository's build with `--profile` inside tests/.artifacts, so it
 *    never reads the saved live connection or the OS credential store.
 *  - It is pointed at a THROWAWAY database named by TB_SMOKE_DB, which must start `tbsmoke_`; the spec
 *    refuses any other name. Database user and password come from TB_DB_USER / TB_DB_PASS.
 *  - WebView2's DevTools port is TB_CDP_PORT (9303 for this job — 9223 and 9243 belong to other
 *    projects being checked on this laptop); the port must be free before launch.
 *  - An SMTP catcher listens on 127.0.0.1:TB_MAIL_PORT and writes mails into TB_MAIL_DIR.
 */
import { test, expect, chromium, Browser, Page } from '@playwright/test';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { REPO, winPath, sleep, fill, click, textOf, siteKeys, ensureRendered, spaNav } from './_blogapp';

const CDP_PORT = process.env.TB_CDP_PORT ?? '9303';
const CDP = `http://127.0.0.1:${CDP_PORT}`;
const DB = process.env.TB_SMOKE_DB ?? '';
const MAIL_PORT = process.env.TB_MAIL_PORT ?? '2525';
const MAIL_DIR = process.env.TB_MAIL_DIR ?? path.join(REPO, 'tests/.artifacts/mailjob/mail');
const PROFILE = path.join(REPO, 'tests/.artifacts/mailjob/blogapp-profile');
const OUT = path.join(REPO, 'tests/.artifacts/mailjob/shots-blogapp');
const EXE = path.join(REPO, 'source/BlogApp/bin/Debug/net10.0-windows10.0.19041.0/win-x64/BlogApp.exe');

let browser: Browser;
let page: Page;

function ps(command: string): string {
  return execFileSync('powershell.exe', ['-NoProfile', '-Command', command], { timeout: 120000 }).toString();
}

function repoAppIds(): number[] {
  const out = ps(`$exe='${winPath(EXE).replace(/'/g, "''")}'; Get-Process BlogApp -ErrorAction SilentlyContinue | ` +
    `Where-Object { $_.Path -eq $exe } | ForEach-Object { $_.Id }; exit 0`);
  return out.split(/\s+/).filter(Boolean).map(Number);
}

function portBusy(port: string): boolean {
  return ps(`if (Get-NetTCPConnection -LocalPort ${port} -State Listen -ErrorAction SilentlyContinue) { 'busy' } else { 'free' }`).includes('busy');
}

async function attachPage(): Promise<Page> {
  const deadline = Date.now() + 120000;
  while (Date.now() < deadline) {
    try {
      if (!browser || !browser.isConnected()) browser = await chromium.connectOverCDP(CDP);
      for (const ctx of browser.contexts())
        for (const p of ctx.pages())
          if (p.url() && p.url() !== 'about:blank') { await ensureRendered(p); return p; }
    } catch { /* not up yet */ }
    await sleep(1000);
  }
  throw new Error('BlogApp never exposed a page over CDP');
}

async function openEmailTab(p: Page, port: string) {
  await spaNav(p, '/settings', 2500);
  await p.waitForSelector('[data-testid="settings-page"]', { timeout: 60000 });
  for (let i = 0; i < 20 && !(await p.locator('[data-testid="smtp-host"]').isVisible()); i++) {
    await click(p, 'tab-email');
    await sleep(1000);
  }
  await fill(p, 'smtp-host', '127.0.0.1');
  await fill(p, 'smtp-port', port);
  await fill(p, 'smtp-from-address', 'blog@desktop.test');
  const ssl = p.locator('[data-testid="smtp-ssl"]');
  if ((await ssl.getAttribute('aria-checked')) === 'true' || (await ssl.getAttribute('data-state')) === 'checked') await ssl.click();
  await fill(p, 'smtp-test-to', 'owner@desktop.test');
}

test.describe.configure({ mode: 'serial' });

test.beforeAll(async () => {
  test.setTimeout(400000);
  if (!/^tbsmoke_[0-9]+$/.test(DB)) throw new Error(`REFUSING: TB_SMOKE_DB "${DB}" is not a throwaway tbsmoke_ database`);
  expect(repoAppIds(), 'BlogApp from this repository must be closed before the run').toEqual([]);
  expect(portBusy(CDP_PORT), `port ${CDP_PORT} must be free`).toBe(false);
  fs.mkdirSync(OUT, { recursive: true });
  fs.rmSync(PROFILE, { recursive: true, force: true });
  fs.mkdirSync(PROFILE, { recursive: true });
  ps(`$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS='--remote-debugging-port=${CDP_PORT} --remote-allow-origins=*'; ` +
     `Start-Process -FilePath '${winPath(EXE)}' -ArgumentList '--profile','"${winPath(PROFILE)}"' | Out-Null`);
  page = await attachPage();
  await page.waitForSelector('[data-testid="connection-host"]', { timeout: 60000 });
  await fill(page, 'connection-host', 'localhost');
  await fill(page, 'connection-port', '5550');
  await fill(page, 'connection-database', DB);
  await fill(page, 'connection-username', process.env.TB_DB_USER ?? '');
  await fill(page, 'connection-password', process.env.TB_DB_PASS ?? '');
  const keys = siteKeys();
  await fill(page, 'connection-jwt-key', keys.jwt);
  await fill(page, 'connection-encryption-key', keys.enc);
  await fill(page, 'site-base-url', 'http://localhost:5399').catch(() => {});
  await click(page, 'connection-test').catch(() => {});
  await sleep(4000);
  const refused = await textOf(page, 'connection-error');
  if (refused) throw new Error('throwaway database connection refused: ' + refused);
  const firstIds = repoAppIds();
  await click(page, 'connection-save');
  // BlogApp restarts itself with the same --profile; wait for the first process to go away.
  for (let i = 0; i < 60 && repoAppIds().some(id => firstIds.includes(id)); i++) await sleep(1000);
  await browser?.close().catch(() => {});
  await sleep(3000);
  page = await attachPage();
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
  const chip = (await textOf(page, 'desktop-connection-host')) ?? '';
  expect(chip).toMatch(/localhost/i);
  for (let attempt = 0; attempt < 3; attempt++) {
    await fill(page, 'login-email', 'Ravi@techieblog.com');
    await fill(page, 'login-password', process.env.TB_ADMIN_PASSWORD ?? 'admin_password');
    await click(page, 'login-submit');
    await sleep(7000);
    if (!(await page.evaluate(() => location.pathname.toLowerCase())).startsWith('/login')) break;
  }
  await page.screenshot({ path: `${OUT}/blogapp-signed-in.png` });
  expect(await page.evaluate(() => location.pathname.toLowerCase()), 'signed in').not.toMatch(/^\/login/);
});

test.afterAll(async () => {
  await browser?.close().catch(() => {});
  for (const id of repoAppIds()) ps(`Stop-Process -Id ${id} -Force`);
});

test('REQ-UI-072 BlogApp: Send test mail is accepted by a listening mail server and the mail arrives', async () => {
  test.setTimeout(200000);
  await openEmailTab(page, MAIL_PORT);
  await click(page, 'smtp-test-send');
  await page.waitForSelector('[data-testid="smtp-test-accepted"], [data-testid="smtp-test-refused"]', { timeout: 90000 });
  const answer = (await textOf(page, 'smtp-test-answer')) ?? '';
  await page.screenshot({ path: `${OUT}/blogapp-test-mail-accepted.png` });
  expect(await page.locator('[data-testid="smtp-test-accepted"]').count(), answer).toBe(1);
  const caught = fs.existsSync(MAIL_DIR) ? fs.readdirSync(MAIL_DIR).map(f => fs.readFileSync(path.join(MAIL_DIR, f), 'utf8')) : [];
  expect(caught.some(m => m.toLowerCase().includes('x-catcher-rcpt: owner@desktop.test'))).toBe(true);
});

test('REQ-UI-072 BlogApp: Send test mail shows the answer when nothing answers on the port', async () => {
  test.setTimeout(200000);
  await openEmailTab(page, '2599');
  await click(page, 'smtp-test-send');
  await page.waitForSelector('[data-testid="smtp-test-refused"]', { timeout: 90000 });
  expect((await textOf(page, 'smtp-test-answer')) ?? '').toContain('127.0.0.1:2599');
  await page.screenshot({ path: `${OUT}/blogapp-test-mail-refused.png` });
});
