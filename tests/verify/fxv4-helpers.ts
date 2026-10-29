/**
 * fxv4-helpers.ts - shared plumbing for the W4 verify writers (admin dashboard, moderation, taxonomy,
 * subscribers, settings, theming). Fixture prefix: fxv4.
 *
 * Rules these helpers keep (tests/.artifacts/build-phase-20261027/verify-writer-notes.md):
 *   - the database is only ever READ here (psql SELECT). Every write goes through the app itself.
 *   - every fixture carries the `fxv4` prefix so the owner can find it, and is removed through the UI
 *     where the app offers a delete.
 */
import { execFileSync } from 'child_process';
import { Browser, BrowserContext, Page, expect } from '@playwright/test';
import { login, nav, BASE } from './_gates';
import { gotoPublic, solveCaptcha } from './_engage-helpers';

export { BASE, nav, login, gotoPublic, solveCaptcha };

/** Prefix for every fixture this group creates. */
export const PFX = 'fxv4';

/** psql READ. Returns the trimmed raw text; columns are separated by `|`. */
export function psql(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql],
    { encoding: 'utf8' },
  ).trim();
}
export const psqlInt = (sql: string) => Number(psql(sql));
export const psqlRows = (sql: string): string[][] =>
  psql(sql).split('\n').map((l) => l.trim()).filter(Boolean).map((l) => l.split('|'));

export const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/** Polls an async predicate until it is true or the time runs out. */
export async function until(fn: () => boolean | Promise<boolean>, ms = 40000, step = 700): Promise<boolean> {
  const t = Date.now();
  while (Date.now() - t < ms) {
    try { if (await fn()) return true; } catch { /* retry */ }
    await sleep(step);
  }
  return false;
}

/** Fill + `change`: the admin inputs commit on change, which Playwright's fill alone does not raise. */
export async function fillCommitted(page: Page, testId: string, value: string) {
  const el = page.locator(`[data-testid="${testId}"]`).first();
  await el.fill(value);
  await el.dispatchEvent('change');
  await sleep(500);
}

/** Opens a TrBlazeUI Select by its trigger test id and picks the option whose text matches. */
export async function pickSelect(page: Page, triggerTestId: string, optionText: string | RegExp) {
  const option = page.locator('[role="option"]').filter({ hasText: optionText }).first();
  for (let i = 1; i <= 3; i++) {
    await page.click(`[data-testid="${triggerTestId}"]`);
    try {
      await option.waitFor({ state: 'visible', timeout: 8000 });
      await option.click();
      await sleep(800);
      return;
    } catch {
      await page.keyboard.press('Escape').catch(() => {});
      await sleep(1200);
    }
  }
  throw new Error(`could not pick "${optionText}" in "${triggerTestId}"`);
}

/** The markdown editor drops early keystrokes under load; retype until the value sticks. */
export async function setMarkdown(page: Page, text: string): Promise<boolean> {
  const el = page.locator('[data-testid="markdown-input"]');
  for (let i = 0; i < 4; i++) {
    await el.fill(text);
    await el.dispatchEvent('change');
    await sleep(2000);
    if ((await el.inputValue()) === text) return true;
  }
  return (await el.inputValue()) === text;
}

/** A signed-in admin session in its own context. Caller closes `ctx`. */
export async function adminSession(
  browser: Browser,
  role: 'admin' | 'editor' = 'admin',
  viewport = { width: 1280, height: 900 },
): Promise<{ ctx: BrowserContext; page: Page }> {
  const ctx = await browser.newContext({ viewport });
  const page = await ctx.newPage();
  page.setDefaultTimeout(30000);
  const landing = await login(page, role);
  expect(landing, `${role} sign-in must land on the admin area`).toContain('/admin');
  return { ctx, page };
}

/**
 * Client-side navigation inside the live circuit, then wait for a destination-owned selector.
 * Re-opening the route we are already on does not re-instantiate the component (stale list), so hop
 * through /admin first.
 */
export async function openAdmin(page: Page, route: string, ready: string, timeout = 90000) {
  if (new URL(page.url()).pathname.toLowerCase() === route.toLowerCase() && route !== '/admin') {
    await page.evaluate(() => (window as any).Blazor?.navigateTo('/admin')).catch(() => {});
    await page.waitForFunction(() => location.pathname.toLowerCase() === '/admin', undefined, { timeout: 20000 }).catch(() => {});
    await sleep(800);
  }
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  await page.waitForSelector(ready, { state: 'visible', timeout });
  await page.waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), undefined, { timeout: 30000 }).catch(() => {});
  await sleep(1500);
}

export interface NewPost { title: string; slug: string; abstract: string; body: string; category: string; publish: boolean }

/**
 * Creates a post through the editor and returns { id, slug } (read back with psql). Idempotent on the
 * TITLE among live posts: a previous run's soft-deleted row keeps its slug reserved, so the app may
 * legitimately store this one under `<slug>-2` - callers must use the returned slug.
 */
export async function createPost(page: Page, p: NewPost): Promise<{ id: number; slug: string }> {
  const find = () => psql(`SELECT PostId || '|' || Slug FROM BlogPost WHERE Title = '${p.title}' AND (IsDeleted = FALSE OR IsDeleted IS NULL) ORDER BY PostId DESC LIMIT 1`);
  const existing = find();
  if (existing) { const [id, slug] = existing.split('|'); return { id: Number(id), slug }; }
  await openAdmin(page, '/ManagePost', '[data-testid="markdown-input"]');
  await sleep(2500);
  await pickSelect(page, 'category-select', p.category);
  await fillCommitted(page, 'post-title-input', p.title);
  await fillCommitted(page, 'post-slug-input', p.slug);
  await fillCommitted(page, 'post-excerpt-input', p.abstract);
  if (!(await setMarkdown(page, p.body))) throw new Error('markdown editor would not hold the body');
  await page.click(p.publish ? '[data-testid="publish-post"]' : '[data-testid="save-draft"]');
  const ok = await until(() => {
    const r = find();
    if (!r) return false;
    return !p.publish || psql(`SELECT Published FROM BlogPost WHERE PostId = ${r.split('|')[0]}`) === 't';
  }, 60000, 1500);
  if (!ok) throw new Error(`post "${p.title}" was not saved`);
  const [id, slug] = find().split('|');
  return { id: Number(id), slug };
}

/** Soft-deletes a post through the post list's own delete dialog. No-op when it is not there. */
export async function deletePostBySlug(page: Page, slug: string) {
  if (!psql(`SELECT 1 FROM BlogPost WHERE Slug = '${slug}' AND (IsDeleted = FALSE OR IsDeleted IS NULL)`)) return;
  await openAdmin(page, '/BlogsList', '[data-testid="posts-grid"], [data-testid="posts-empty"]');
  await page.fill('[data-testid="posts-search"]', slug);
  await page.dispatchEvent('[data-testid="posts-search"]', 'change');
  await sleep(2000);
  // Exact slug match: `fxv4-x` must not hit `fxv4-x-2`.
  const exact = new RegExp(`^/?${slug.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}$`);
  const row = page.locator('tbody tr').filter({ has: page.locator('[data-testid="post-row-slug"]').filter({ hasText: exact }) }).first();
  await row.locator('[data-testid="post-delete"]').click();
  await page.locator('[data-testid="post-delete-confirm"]').click();
  await until(() => !psql(`SELECT 1 FROM BlogPost WHERE Slug = '${slug}' AND (IsDeleted = FALSE OR IsDeleted IS NULL)`), 30000, 1000);
}

/** Confirms the e-mail link for an address by reading the issued token (psql READ) and opening it. */
export async function confirmEmail(browser: Browser, email: string, purpose: 'Subscription' | 'Comment') {
  const safe = email.replace(/'/g, '');
  let token = '';
  await until(() => {
    token = psql(`SELECT Token FROM EmailVerificationToken WHERE lower(Email) = lower('${safe}') AND Purpose = '${purpose}' AND NOT IsUsed AND ExpiresOn > now() ORDER BY TokenId DESC LIMIT 1`);
    return !!token;
  }, 45000, 1500);
  if (!token) throw new Error(`no ${purpose} confirmation token was issued for ${email}`);
  const ctx = await browser.newContext();
  const page = await ctx.newPage();
  await page.goto(`${BASE}/verify/${token}`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector(
    '[data-testid="verify-confirmed"], [data-testid="verify-subscribed"], [data-testid="verify-already"], [data-testid="verify-expired"], [data-testid="verify-invalid"]',
    { timeout: 60000 },
  ).catch(() => {});
  await sleep(1500);
  await ctx.close();
}

/** Submits the public subscribe form (sidebar card on /search) with the accessible captcha mode. */
async function subscribePublicOnce(browser: Browser, email: string): Promise<string> {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  page.setDefaultTimeout(30000);
  await gotoPublic(page, '/search');
  const card = page.locator('[data-testid="sidebar-subscribe"]');
  await card.waitFor({ state: 'visible', timeout: 60000 });
  await card.locator('[data-testid="subscribe-email"]').fill(email);
  await solveCaptcha(card);
  await sleep(4000);
  await card.locator('[data-testid="subscribe-submit"]').click();
  await until(async () => (await card.locator('[data-testid="subscribe-message"]').count()) > 0, 30000);
  const msg = ((await card.locator('[data-testid="subscribe-message"]').textContent().catch(() => '')) || '').trim();
  await ctx.close();
  return msg;
}

/** Posts a public comment on a published post, then confirms the e-mail link. */
async function commentPublicOnce(browser: Browser, slug: string, name: string, email: string, text: string) {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  page.setDefaultTimeout(30000);
  await gotoPublic(page, `/post/${slug}`);
  const form = page.locator('[data-testid="comment-form"]');
  await form.waitFor({ state: 'visible', timeout: 60000 });
  await form.locator('[data-testid="comment-name"]').fill(name);
  await form.locator('[data-testid="comment-email"]').fill(email);
  await form.locator('[data-testid="comment-input"]').fill(text);
  await solveCaptcha(form);
  // The spam guard refuses a form submitted less than 3 s after it rendered ("faster than a human can type").
  await sleep(4000);
  await form.locator('[data-testid="comment-submit"]').click();
  await until(async () => (await page.locator('[data-testid="comment-form-success"], [data-testid="comment-form-error"]').count()) > 0, 30000);
  await ctx.close();
  // An address that confirmed once before is already verified: its comment skips the e-mail step and goes
  // straight to moderation. Only an unverified address has a link to follow.
  const safe = email.replace(/'/g, '');
  const stored = await until(() => !!psql(`SELECT 1 FROM BlogComment WHERE lower(Email) = lower('${safe}')`), 30000, 1000);
  if (!stored) throw new Error(`the comment from ${email} was not accepted (spam guard / captcha refused it)`);
  if (psql(`SELECT IsEmailVerified FROM BlogComment WHERE lower(Email) = lower('${safe}') ORDER BY CommentId DESC LIMIT 1`) !== 't') {
    await confirmEmail(browser, email, 'Comment');
  }
}

/** Body text of a public page once the circuit has rendered. */
export async function publicText(browser: Browser, path: string, settleMs = 3500): Promise<{ text: string; status: number }> {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await ctx.newPage();
  const resp = await page.goto(`${BASE}${path}`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, undefined, { timeout: 60000 }).catch(() => {});
  await sleep(settleMs);
  const text = await page.locator('body').innerText();
  const status = resp ? resp.status() : 0;
  await ctx.close();
  return { text, status };
}

/**
 * The captcha issuer is rate limited per client and every verify writer shares this host's address, so a
 * challenge can stay on "Loading the verification question" for a while. Retry the whole form with a pause.
 */
async function withCaptchaRetry<T>(what: string, fn: () => Promise<T>): Promise<T> {
  let last: unknown;
  for (let attempt = 1; attempt <= 4; attempt++) {
    try { return await fn(); } catch (e) {
      last = e;
      if (!/captcha|verification|Timeout/i.test(String(e))) throw e;
      console.log(`[fxv4] ${what}: attempt ${attempt} hit the captcha limiter (${String(e).split('\n')[0].slice(0, 100)}); waiting`);
      await sleep(25000 * attempt);
    }
  }
  throw last;
}

/** Submits the public subscribe form (sidebar card on /search) with the accessible captcha mode. */
export const subscribePublic = (browser: Browser, email: string) =>
  withCaptchaRetry(`subscribe ${email}`, () => subscribePublicOnce(browser, email));

/** Subscribes an address and confirms it (double opt-in). */
export async function subscribeAndConfirm(browser: Browser, email: string) {
  const msg = await subscribePublic(browser, email);
  await confirmEmail(browser, email, 'Subscription');
  return msg;
}

/** Posts a public comment on a published post, then confirms the e-mail link when one is needed. */
export const commentPublic = (browser: Browser, slug: string, name: string, email: string, text: string) =>
  withCaptchaRetry(`comment ${email}`, () => commentPublicOnce(browser, slug, name, email, text));
