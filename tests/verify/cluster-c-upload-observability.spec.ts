import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

/**
 * Cluster C smoke — REQ-NFR-040 (upload failures are audible) and REQ-NFR-033 (their text is not
 * disclosed), driven end-to-end through the admin UI against a running host.
 *
 * THE POINT IS THE NEGATIVE CONTROL. The defect was pure invisibility: the container stayed Up,
 * /healthz stayed 200, the startup log printed its usual "uploads configured: True", and the
 * container log carried zero [ERR]/[WRN]/[FTL] lines while every upload failed. A happy-path
 * smoke would have passed against the broken build, so this suite deliberately breaks the uploads
 * directory first, uploads through the real admin screen, and asserts on BOTH observables:
 *
 *   (a) an [ERR] line now exists in the host log naming the target path and the exception, and
 *   (b) the administrator's message distinguishes "the server cannot write here" from the generic
 *       retry-able failure — while carrying no exception text and no absolute server path.
 *
 * Then the permissions are restored and a normal upload is proven to still work, so the fix is
 * shown not to have broken the feature it instruments.
 *
 * The host is launched separately (published to a private folder, Uploads__Path pointed at
 * UPLOADS_DIR) and its stdout is captured to HOST_LOG. Both are injected, never guessed.
 */

const BASE = process.env.SMOKE_BASE ?? 'http://172.18.144.1:5403';

/** Documented seeded Admin from docs/TechieBlog-UsageGuide.md. No account is invented. */
const ADMIN = { email: 'Ravi@techieblog.com', password: 'admin_password' };

/** Absolute WSL path of the host's stdout capture. */
const HOST_LOG = required('HOST_LOG');

/** Absolute WSL path of the directory the host writes uploads beneath. */
const UPLOADS_DIR = required('UPLOADS_DIR');

/** Absolute WSL path of the batch file that flips the directory's Windows write ACL. */
const ACL_SCRIPT_WIN = required('ACL_SCRIPT_WIN');

/**
 * Category the upload dialog opens on, and therefore the subdirectory the bytes are written to.
 * Not a guess: the host's own success log line names it, and it is what the dialog's category
 * select defaults to. Named here so the log assertion and the restored-upload check cannot drift
 * apart from the directory the ACL is applied to.
 */
const UPLOAD_CATEGORY = 'profiles';

/** Where evidence screenshots are written. */
const SHOTS = process.env.SHOT_DIR ?? 'tests/.artifacts/cluster-c';

/**
 * Reads an environment value that must be injected by the runner.
 *
 * @param name Environment variable name.
 * @returns Its value.
 */
function required(name: string): string {
  const raw = process.env[name];
  if (!raw) {
    throw new Error(`${name} must be injected by the smoke runner; it is not a value to guess`);
  }
  return raw;
}

/**
 * Navigates and waits for the Blazor circuit, so a click lands on an interactive component.
 *
 * @param page The page under test.
 * @param url Absolute URL to open.
 */
async function gotoInteractive(page: Page, url: string) {
  const socket = page.waitForEvent('websocket', {
    predicate: ws => ws.url().includes('_blazor'),
    timeout: 30000,
  });
  await page.goto(url, { waitUntil: 'domcontentloaded' });
  await socket;
  await page.waitForFunction(() => (window as unknown as { Blazor?: unknown }).Blazor !== undefined,
    null, { timeout: 30000 });
  await page.waitForTimeout(1500);
}

/**
 * Signs in as the seeded Admin and asserts the landing URL rather than assuming success.
 *
 * @param page The page under test.
 */
async function loginAsAdmin(page: Page) {
  await gotoInteractive(page, `${BASE}/login`);
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.fill('[data-testid="login-email"]', ADMIN.email);
  await page.fill('[data-testid="login-password"]', ADMIN.password);
  await page.click('[data-testid="login-submit"]');
  await page.waitForTimeout(5000);

  const landed = new URL(page.url()).pathname;
  expect(landed, 'the admin sign-in did not succeed').not.toContain('/login');
  expect(landed, 'the admin account is pinned to the forced password gate (REQ-NFR-023)')
    .not.toContain('/change-password');
}

/** A tiny but genuinely valid 1x1 PNG, so nothing is rejected by format validation. */
const PNG_1X1 = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==',
  'base64');

/**
 * Opens the upload dialog, attaches a PNG and confirms, returning whatever the screen reports.
 *
 * @param page The page under test.
 * @param fileName Name given to the attached file.
 * @returns The upload error text, or null when the upload succeeded.
 */
async function attemptUpload(page: Page, fileName: string): Promise<string | null> {
  await gotoInteractive(page, `${BASE}/admin/images`);
  await page.click('[data-testid="upload-image"]');
  await page.waitForSelector('[data-testid="image-upload-dialog"]', { timeout: 20000 });

  const input = page.locator('[data-testid="upload-dropzone"] input[type="file"]');
  await input.setInputFiles({ name: fileName, mimeType: 'image/png', buffer: PNG_1X1 });
  await expect(page.locator('[data-testid="upload-selected-file"]'),
    'the file was never accepted by the picker').toBeVisible({ timeout: 20000 });

  await page.click('[data-testid="upload-confirm"]');
  await page.waitForTimeout(6000);

  const error = page.locator('[data-testid="upload-error"]');
  return (await error.count()) > 0 && await error.isVisible()
    ? ((await error.innerText()).trim())
    : null;
}

/**
 * Reads the host's captured stdout.
 *
 * @returns The whole log as text.
 */
function readHostLog(): string {
  return fs.readFileSync(HOST_LOG, 'utf8');
}

/**
 * Waits for a marker to appear in the log written since a known offset.
 *
 * The host's stdout is redirected to a file rather than a console, so the stream is block-buffered
 * and a line can lag the HTTP response it belongs to by seconds. Reading once and asserting would
 * make this suite flaky in the one direction that matters — reporting "nothing was logged", the
 * exact defect under test, when the line simply had not been flushed yet. So the read is polled,
 * and only a genuine absence after the timeout fails.
 *
 * @param marker Substring to wait for.
 * @param offset Character offset the log had reached before the action.
 * @param timeoutMs How long to keep re-reading.
 * @returns Everything appended since the offset, whether or not the marker arrived.
 */
async function waitForLogMarker(marker: string, offset: number, timeoutMs = 30000): Promise<string> {
  const deadline = Date.now() + timeoutMs;
  let added = '';
  while (Date.now() < deadline) {
    added = readHostLog().slice(offset);
    if (added.includes(marker)) {
      // Give the exception block, written immediately after the message, time to land too.
      await new Promise(resolve => setTimeout(resolve, 1500));
      return readHostLog().slice(offset);
    }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  return added;
}

/**
 * Denies or restores write access to the uploads category directory through Windows ACLs.
 *
 * WSL `chmod` is a no-op on a `/mnt/c` DrvFs mount, so the permission has to be set with the
 * Windows tool. The deny ACE is written against the well-known Everyone SID (`*S-1-1-0`) rather
 * than a user name, because a name resolved from the WSL environment does not map to a Windows
 * account; a deny ACE also outranks every allow, so it blocks the host process whatever identity
 * it runs as. This reproduces the production condition — a directory the process may not write —
 * on the platform the smoke actually runs on.
 *
 * @param deny True to deny write, false to restore it.
 */
function setBlogDirWritable(deny: boolean) {
  const { execSync } = require('child_process');
  execSync(`cmd.exe /c "${ACL_SCRIPT_WIN}" ${deny ? 'deny' : 'allow'}`, { stdio: 'pipe' });
}

test.describe('REQ-NFR-040 / REQ-NFR-033 — an upload that cannot be written is audible, not silent', () => {
  test.describe.configure({ mode: 'serial' });

  test('a permissions refusal shows a "server cannot write" message and writes an ERR line', async ({ page }) => {
    fs.mkdirSync(path.join(UPLOADS_DIR, UPLOAD_CATEGORY), { recursive: true });
    fs.mkdirSync(SHOTS, { recursive: true });

    const before = readHostLog();
    setBlogDirWritable(true);

    let shown: string | null = null;
    try {
      await loginAsAdmin(page);
      shown = await attemptUpload(page, 'nfr040-denied.png');
      await page.screenshot({ path: path.join(SHOTS, 'nfr040-upload-denied.png'), fullPage: true });
    } finally {
      setBlogDirWritable(false);
    }

    // (b) THE ADMIN MESSAGE distinguishes a hosting problem from a retry-able one...
    expect(shown, 'the upload unexpectedly succeeded against a deny-write directory').not.toBeNull();
    expect(shown!, 'the message must say the SERVER cannot write, not "try again"')
      .toContain('cannot write to its upload location');
    expect(shown!, 'the message must tell the operator a retry will not fix a permissions problem')
      .toContain('Retrying will not help');
    expect(shown!, 'the old generic sentence must be gone from this failure class')
      .not.toContain('An error occurred while uploading the file');

    // ...and REQ-NFR-033: it discloses neither the exception text nor an absolute server path.
    expect(shown!, 'the exception text must never reach the screen').not.toContain('UnauthorizedAccessException');
    expect(shown!, 'the exception text must never reach the screen').not.toContain('Access to the path');
    expect(shown!, 'no absolute server path may reach the screen').not.toMatch(/[A-Za-z]:\\/);
    expect(shown!, 'no absolute server path may reach the screen').not.toContain('/app/uploads');

    // (a) THE LOG carries what the screen does not.
    const added = await waitForLogMarker('Upload REFUSED', before.length);
    // The host's console template is `[{Timestamp:HH:mm:ss} {Level:u3}]`, so an error line reads
    // `[07:10:04 ERR]` - the level token, not a bare `[ERR]`. Matched on the real shape, because an
    // assertion that can only fail is not a gate either.
    expect(added, 'no error-level line was written - this is the whole defect').toMatch(/\[\d{2}:\d{2}:\d{2} ERR\]/);
    expect(added, 'the error line must be recognisable at a glance').toContain('Upload REFUSED');
    expect(added, 'the error line must name the target path').toContain(`uploads/${UPLOAD_CATEGORY}/`);
    expect(added, 'the underlying exception must be in the log').toContain('UnauthorizedAccessException');
  });

  test('with the directory writable again a normal upload still succeeds', async ({ page }) => {
    setBlogDirWritable(false);

    await loginAsAdmin(page);
    const shown = await attemptUpload(page, 'nfr040-restored.png');
    await page.screenshot({ path: path.join(SHOTS, 'nfr040-upload-restored.png'), fullPage: true });

    expect(shown, `the restored upload still failed: ${shown}`).toBeNull();

    const written = fs.readdirSync(path.join(UPLOADS_DIR, UPLOAD_CATEGORY));
    expect(written.length, 'no file reached the uploads directory after the permission was restored')
      .toBeGreaterThan(0);
  });
});
