// _content.mjs - shared seed/teardown driver for the verify-phase per-screen seeds (group W0, prefix fxv0).
//
// Usage:  node _content.mjs seed <piece> [<piece> ...]      (TF_BASE = app address)
//         node _content.mjs teardown
//         node _content.mjs seed <piece> --print-route      (prints "TF_ROUTE=<value>" lines for params)
//
// Everything is done THROUGH THE APP: sign in as the admin with Playwright and fill the admin forms and
// the public forms. The only database access is a psql READ of the double-opt-in token (the app mails
// that link; in Development no mail leaves the host, so the token is read from emailverificationtoken).
// No psql writes. Idempotent: every piece checks what already exists and creates only what is missing.
//
// Everything created is prefixed "fxv0" and is plainly test content.

import { chromium } from '@playwright/test';
import { execFileSync } from 'node:child_process';

export const BASE = (process.env.TF_BASE || process.env.TB_BASE || 'http://localhost:5450').replace(/\/$/, '');
const ADMIN = { email: process.env.TB_ADMIN_EMAIL || 'Ravi@techieblog.com', password: process.env.TB_ADMIN_PASSWORD || 'VerifyAdmin#2026' };
const PREFIX = 'fxv0';
const T0 = Date.now();
const log = (m) => console.error(`[fxv0 ${Math.round((Date.now() - T0) / 1000)}s] ${m}`);

// ------------------------------------------------------------------ catalogue of test content
const NOTE = 'Test content created by the verify-phase seed (fxv0). Not real site content.';
const MD_SHOWCASE = [
  '## fxv0 Markdown showcase', '',
  `${NOTE} It exists so the post page has a heading, a list, a code block and a table to render.`, '',
  '### A list', '', '- first **bold** item', '- second item with `inline code`', '- third item', '',
  '### A code block', '', '```csharp', 'public static int Add(int a, int b) => a + b;', '```', '',
  '### A table', '', '| Name | Value |', '|------|-------|', '| alpha | 1 |', '| beta | 2 |', '',
  '> A block quote from the fxv0 seed.', '',
].join('\n');
const MD_SHORT = (what) => [`## fxv0 ${what}`, '', `${NOTE}`, '', '- point one', '- point two', '', 'Closing paragraph of the fxv0 test post.', ''].join('\n');

export const POSTS = {
  markdown: { slug: 'fxv0-markdown-showcase', title: 'fxv0 Markdown showcase', abstract: 'fxv0 test post: headings, list, code and table.', body: MD_SHOWCASE, category: 'Web Development', tags: ['Blazor', 'Tutorial'], publish: true },
  programming: { slug: 'fxv0-programming-notes', title: 'fxv0 Programming notes', abstract: 'fxv0 test post in the Programming category.', body: MD_SHORT('Programming notes'), category: 'Programming', tags: ['Architecture', 'Blazor'], publish: true },
  draft: { slug: 'fxv0-draft-post', title: 'fxv0 Draft post', abstract: 'fxv0 test draft, not published.', body: MD_SHORT('Draft post'), category: 'Web Development', tags: ['Tutorial'], publish: false },
  part1: { slug: 'fxv0-series-part-1', title: 'fxv0 Series part 1', abstract: 'fxv0 test series, first part.', body: MD_SHORT('Series part 1'), category: 'Programming', tags: ['Tutorial'], publish: true, series: 'fxv0 Test series' },
  part2: { slug: 'fxv0-series-part-2', title: 'fxv0 Series part 2', abstract: 'fxv0 test series, second part.', body: MD_SHORT('Series part 2'), category: 'Programming', tags: ['Tutorial'], publish: true, series: 'fxv0 Test series' },
  part3: { slug: 'fxv0-series-part-3', title: 'fxv0 Series part 3', abstract: 'fxv0 test series, third part.', body: MD_SHORT('Series part 3'), category: 'Programming', tags: ['Tutorial'], publish: true, series: 'fxv0 Test series' },
};
export const SERIES = { name: 'fxv0 Test series', slug: 'fxv0-test-series', description: `${NOTE} A three-part series.` };
export const SUBS = [
  { email: 'fxv0-sub1@example.com', active: true },
  { email: 'fxv0-sub2@example.com', active: true },
  { email: 'fxv0-sub3@example.com', active: false }, // confirmed then deactivated -> shows the Inactive state
];
export const NEWSLETTERS = [
  { title: 'fxv0 Newsletter issue 1', summary: 'fxv0 test issue one', body: '## fxv0 issue one\n\nTest newsletter issue **one** (fxv0 seed). Not real content.' },
  { title: 'fxv0 Newsletter issue 2', summary: 'fxv0 test issue two', body: '## fxv0 issue two\n\nTest newsletter issue **two** (fxv0 seed). Not real content.' },
];
export const COMMENTS = [
  { name: 'fxv0 Commenter', email: 'fxv0-commenter@example.com', text: 'fxv0 approved test comment: thanks for the showcase.', approve: true },
  { name: 'fxv0 Pending', email: 'fxv0-pending@example.com', text: 'fxv0 pending test comment awaiting moderation.', approve: false },
];
const EXP = { role: 'fxv0 Test role', company: 'fxv0 Test Company', desc: '- fxv0 test bullet (not biography)', order: '95' };
const SKILL = { name: 'fxv0 Test skill', category: 'fxv0 Test skills' };
const AWARD = { title: 'fxv0 Test award', desc: 'fxv0 test award (not biography)', year: '2024' };
const MEDIA = 'fxv0-media.png';
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==', 'base64');

// ------------------------------------------------------------------ low-level helpers
export function psqlRead(sql) {
  return execFileSync('docker', ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql], { encoding: 'utf8' }).trim();
}
async function until(fn, ms = 40000, step = 700) {
  const t = Date.now();
  while (Date.now() - t < ms) { try { if (await fn()) return true; } catch { /* retry */ } await new Promise((r) => setTimeout(r, step)); }
  return false;
}
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function interactiveForm(page, timeout = 90000) {
  await page.waitForFunction(() => { const f = document.querySelector('form'); return !!f && !f.hasAttribute('action'); }, { timeout });
}
export async function login(page) {
  let last = '';
  for (let i = 1; i <= 3; i++) {
    try {
      await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded', timeout: 90000 });
      await page.waitForSelector('[data-testid="login-email"]', { timeout: 90000 });
      await interactiveForm(page);
      await sleep(1000);
      await page.fill('[data-testid="login-email"]', ADMIN.email);
      await page.fill('[data-testid="login-password"]', ADMIN.password);
      await page.click('[data-testid="login-submit"]');
      await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
      await sleep(2000);
      return;
    } catch (e) { last = String(e).slice(0, 200); }
  }
  throw new Error(`admin sign-in failed: ${last}`);
}
/** Client-side navigation inside the live circuit, then wait for a destination-owned selector. */
async function open(page, route, ready, timeout = 90000) {
  // Re-opening the route we are on does not re-instantiate the component (stale list): hop via /admin first.
  if (new URL(page.url()).pathname.toLowerCase() === route.toLowerCase() && page.url().startsWith(BASE) && route !== '/admin') {
    await page.evaluate(() => window.Blazor && window.Blazor.navigateTo('/admin')).catch(() => {});
    await page.waitForFunction(() => location.pathname.toLowerCase() === '/admin', { timeout: 20000 }).catch(() => {});
    await sleep(800);
  }
  const hasBlazor = await page.evaluate(() => !!(window.Blazor && window.Blazor.navigateTo)).catch(() => false);
  if (hasBlazor) await page.evaluate((p) => window.Blazor.navigateTo(p), route);
  else { await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded', timeout }); }
  await page.waitForSelector(ready, { state: 'visible', timeout });
  await page.waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), { timeout: 30000 }).catch(() => {});
  await sleep(1500);
}
async function fillCommitted(page, testId, value, scope) {
  const el = (scope || page).locator(`[data-testid="${testId}"]`);
  await el.fill(value);
  await el.dispatchEvent('change');
  await sleep(500);
}
async function pickSelect(page, triggerTestId, optionText) {
  const option = page.locator('[role="option"]').filter({ hasText: optionText }).first();
  for (let i = 1; i <= 3; i++) {
    await page.click(`[data-testid="${triggerTestId}"]`);
    try { await option.waitFor({ state: 'visible', timeout: 8000 }); await option.click(); await sleep(800); return; }
    catch { await page.keyboard.press('Escape').catch(() => {}); await sleep(1200); }
  }
  await page.screenshot({ path: 'tests/.artifacts/verify/W0/pick-failed.png', fullPage: true }).catch(() => {});
  throw new Error(`could not pick "${optionText}" in "${triggerTestId}"`);
}
async function setMarkdown(page, text) {
  const el = page.locator('[data-testid="markdown-input"]');
  for (let i = 0; i < 4; i++) {
    await el.fill(text); await el.dispatchEvent('change'); await sleep(2000);
    if ((await el.inputValue()) === text) return true;
  }
  return (await el.inputValue()) === text;
}

// captcha: the accessible question mode is the only form a headless browser can answer honestly
const NUM = ['zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten', 'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen', 'eighteen', 'nineteen', 'twenty'];
const nv = (w) => NUM.indexOf(w.trim().toLowerCase());
function answerQuestion(q) {
  let m = q.match(/what is\s+([a-z]+)\s+plus\s+([a-z]+)/i); if (m) { const a = nv(m[1]), b = nv(m[2]); return a >= 0 && b >= 0 ? String(a + b) : null; }
  m = q.match(/what is\s+([a-z]+)\s+minus\s+([a-z]+)/i); if (m) { const a = nv(m[1]), b = nv(m[2]); return a >= 0 && b >= 0 ? String(a - b) : null; }
  m = q.match(/how many letters are in the word '([^']+)'/i); if (m) return String(m[1].length);
  m = q.match(/how many words are in this line: '([^']+)'/i); if (m) return String(m[1].split(/\s+/).filter(Boolean).length);
  return null;
}
async function solveCaptcha(scope) {
  const prompt = scope.locator('[data-testid="captcha-prompt"]').first();
  const toggle = scope.locator('[data-testid="captcha-mode-toggle"]').first();
  await prompt.waitFor({ state: 'visible', timeout: 60000 });
  await until(async () => (await scope.locator('[data-testid="captcha-image-placeholder"]').count()) === 0, 90000, 1000);
  let q = '';
  for (let i = 0; i < 12; i++) {
    q = ((await prompt.textContent()) || '').trim();
    const a = q && !/Loading the verification question/i.test(q) ? answerQuestion(q) : null;
    if (a !== null) { await scope.locator('[data-testid="captcha-answer"]').first().fill(a); return; }
    // the image mode ("Type the characters shown") cannot be read by a machine: switch to the accessible question mode
    if (/characters/i.test(q) && (await toggle.count())) await toggle.click().catch(() => {});
    await sleep(1500);
  }
  throw new Error(`unrecognised captcha question: "${q}"`);
}
/** Loads a PUBLIC page in the anonymous context and waits until the Blazor circuit is live. */
async function gotoPublic(page, path, ready) {
  await page.goto(`${BASE}${path}`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForFunction(() => window.Blazor !== undefined, { timeout: 60000 }).catch(() => {});
  await page.waitForSelector(ready, { state: 'visible', timeout: 90000 });
  await sleep(2500);
}
async function status(url) {
  const r = await fetch(`${BASE}${url}`, { redirect: 'manual' }).catch(() => null);
  return r ? r.status : 0;
}
function tokenFor(email, purpose) {
  const safe = email.replace(/'/g, '');
  return psqlRead(`select token from emailverificationtoken where lower(email)=lower('${safe}') and purpose='${purpose}' and not isused and expireson>now() order by tokenid desc limit 1`);
}
async function waitToken(email, purpose) {
  let t = '';
  await until(async () => { t = tokenFor(email, purpose); return !!t; }, 45000, 1500);
  return t;
}

// ------------------------------------------------------------------ session
export async function withSession(fn) {
  const browser = await chromium.launch();
  try {
    const admin = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage();
    admin.setDefaultTimeout(30000);
    const newAnon = async () => { const p = await (await browser.newContext({ viewport: { width: 1280, height: 900 } })).newPage(); p.setDefaultTimeout(30000); return p; };
    return await fn({ admin, newAnon, browser });
  } finally { await browser.close(); }
}
let loggedIn = false;
async function ensureLogin(admin) { if (!loggedIn) { await login(admin); loggedIn = true; } }

// ------------------------------------------------------------------ posts
async function filteredRows(admin, searchBox, rowSel, search, readRow) {
  if (search) {
    await admin.fill(`[data-testid="${searchBox}"]`, search);
    await admin.dispatchEvent(`[data-testid="${searchBox}"]`, 'change');
    // wait until the filter has been applied: every visible row mentions the search text (or none remain)
    await until(async () => admin.$$eval(rowSel, (trs, q) => trs.every((tr) => tr.innerText.toLowerCase().includes(q)), search.toLowerCase()), 15000, 600);
    await sleep(500);
  }
  // read until two consecutive reads agree and no loading indicator is up (an empty table while loading is not "no rows")
  let prev = null, cur = null;
  for (let i = 0; i < 12; i++) {
    const loading = await admin.locator('[data-testid$="-loading"]').count();
    cur = JSON.stringify(await admin.$$eval(rowSel, readRow));
    if (!loading && cur === prev) break;
    prev = cur; await sleep(900);
  }
  return JSON.parse(cur);
}
const readPost = (trs) => trs.map((tr) => ({
  title: tr.querySelector('[data-testid="post-row-title"]')?.textContent?.trim() || '',
  slug: (tr.querySelector('[data-testid="post-row-slug"]')?.textContent || '').trim().replace(/^\//, ''),
  status: tr.querySelector('[data-testid="post-row-status"]')?.textContent?.trim() || '',
  id: (tr.querySelector('[data-testid="post-edit"]')?.getAttribute('href') || '').split('/').pop(),
}));
async function postRows(admin, search) {
  await open(admin, '/BlogsList', '[data-testid="posts-grid"], [data-testid="posts-empty"]');
  return filteredRows(admin, 'posts-search', 'tbody tr', search, readPost);
}
let postIndexCache = null;
async function postIndex(admin, force) {
  if (!postIndexCache || force) postIndexCache = await postRows(admin, 'fxv0-');
  return postIndexCache;
}
export async function postId(admin, key, force) {
  const r = (await postIndex(admin, force)).find((x) => x.slug === POSTS[key].slug);
  return r ? { id: r.id, status: r.status } : null;
}
async function ensureSeries(admin) {
  await open(admin, '/admin/series', '[data-testid="series-grid"], [data-testid="series-empty"]');
  const slugs = await admin.$$eval('[data-testid="series-row-slug"]', (n) => n.map((x) => x.textContent.trim().replace(/^\//, '')));
  if (slugs.includes(SERIES.slug)) return;
  log('create series');
  await open(admin, '/admin/series/new', '[data-testid="series-name-input"]');
  await sleep(2000);
  await fillCommitted(admin, 'series-name-input', SERIES.name);
  if (await admin.locator('[data-testid="series-slug-input"]').count()) await fillCommitted(admin, 'series-slug-input', SERIES.slug);
  await fillCommitted(admin, 'series-description-input', SERIES.description);
  await admin.click('[data-testid="save-series"]');
  const ok = await until(async () => {
    await open(admin, '/admin/series', '[data-testid="series-grid"], [data-testid="series-empty"]');
    return (await admin.$$eval('[data-testid="series-row-slug"]', (n) => n.map((x) => x.textContent.trim().replace(/^\//, '')))).includes(SERIES.slug);
  }, 40000, 1000);
  if (!ok) throw new Error('series was not created');
}
async function ensurePost(admin, key) {
  const p = POSTS[key];
  const found = await postId(admin, key);
  const want = p.publish ? 'Published' : 'Draft';
  if (found && (found.status === want || (!p.publish && found.status !== 'Published'))) { log(`post ${key} present (${found.status})`); return found.id; }
  if (p.series) await ensureSeries(admin);
  if (!found) {
    log(`create post ${key}`);
    await open(admin, '/ManagePost', '[data-testid="markdown-input"]');
    await sleep(2500);
  } else {
    log(`publish existing post ${key}`);
    await open(admin, `/ManagePost/${found.id}`, '[data-testid="markdown-input"]');
    await sleep(2500);
  }
  if (!found) {
    log('editor open'); await pickSelect(admin, 'category-select', p.category); log('category set');
    await fillCommitted(admin, 'post-title-input', p.title);
    await fillCommitted(admin, 'post-slug-input', p.slug);
    await fillCommitted(admin, 'post-excerpt-input', p.abstract);
    if (!(await setMarkdown(admin, p.body))) throw new Error(`markdown editor would not hold the body of ${key}`);
    log('body set');
    for (const t of p.tags) {
      await fillCommitted(admin, 'tag-input', t);
      await admin.click('[data-testid="add-tag"]');
      await sleep(1000);
    }
    if (p.series) { await pickSelect(admin, 'series-select', p.series); await sleep(800); }
  }
  log('submitting');
  if (p.publish) await admin.click('[data-testid="publish-post"]'); else await admin.click('[data-testid="save-draft"]');
  let fin = null;
  const ok = await until(async () => { fin = await postId(admin, key, true); return !!fin && (p.publish ? fin.status === 'Published' : true); }, 90000, 1500);
  if (!ok) throw new Error(`post ${key} did not reach the expected state`);
  return fin.id;
}

// ------------------------------------------------------------------ subscribers
async function subRows(admin) {
  await open(admin, '/admin/subscribers', '[data-testid="subscribers-grid"], [data-testid="subscribers-empty"]');
  return filteredRows(admin, 'subscribers-search', 'tbody tr', 'fxv0', (trs) => trs.map((tr) => ({
    email: tr.querySelector('[data-testid="subscriber-row-email"]')?.textContent?.trim() || '',
    status: tr.querySelector('[data-testid="subscriber-row-status"]')?.textContent?.trim() || '',
  })));
}
async function subscribePublic(newAnon, email) {
  const page = await newAnon();
  await gotoPublic(page, '/search', '[data-testid="sidebar-subscribe"]');
  const card = page.locator('[data-testid="sidebar-subscribe"]');
  await card.locator('[data-testid="subscribe-email"]').fill(email);
  await solveCaptcha(card);
  await card.locator('[data-testid="subscribe-submit"]').click();
  const ok = await until(async () => (await card.locator('[data-testid="subscribe-message"]').count()) > 0, 30000);
  const msg = ok ? ((await card.locator('[data-testid="subscribe-message"]').textContent()) || '').trim() : 'no message';
  await page.context().close();
  log(`subscribe ${email}: ${msg.slice(0, 80)}`);
}
async function confirmToken(newAnon, token) {
  const page = await newAnon();
  await page.goto(`${BASE}/verify/${token}`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="verify-confirmed"], [data-testid="verify-subscribed"], [data-testid="verify-already"], [data-testid="verify-expired"]', { timeout: 60000 }).catch(() => {});
  await sleep(1500);
  await page.context().close();
}
async function ensureSubs(admin, newAnon) {
  await ensureLogin(admin);
  let rows = await subRows(admin);
  for (const s of SUBS) {
    if (rows.some((r) => r.email.toLowerCase() === s.email)) continue;
    await subscribePublic(newAnon, s.email);
    const tok = await waitToken(s.email, 'Subscription');
    if (!tok) throw new Error(`no confirmation token for ${s.email}`);
    await confirmToken(newAnon, tok);
  }
  rows = await subRows(admin);
  for (const s of SUBS) {
    const r = rows.find((x) => x.email.toLowerCase() === s.email);
    if (!r) throw new Error(`subscriber ${s.email} not listed after confirmation`);
    if (!s.active && r.status === 'Active') {
      log(`deactivate ${s.email}`);
      await admin.locator('tbody tr').filter({ hasText: s.email }).locator('[data-testid="subscriber-deactivate"]').click();
      await until(async () => (await subRows(admin)).find((x) => x.email.toLowerCase() === s.email)?.status !== 'Active', 20000, 1000);
    }
  }
}

// ------------------------------------------------------------------ verify token (mockup 44)
async function ensureVerifyToken(admin, newAnon) {
  const live = psqlRead("select token from emailverificationtoken where email like 'fxv0-verify-%@example.com' and purpose='Subscription' and not isused and expireson>now() order by tokenid desc limit 1");
  if (live) return live;
  const email = `fxv0-verify-${Date.now().toString(36)}@example.com`;
  await subscribePublic(newAnon, email);
  const tok = await waitToken(email, 'Subscription');
  if (!tok) throw new Error('no verify token was issued');
  return tok;
}

// ------------------------------------------------------------------ comments
async function commentRows(admin) {
  await open(admin, '/comments', '[data-testid="comments-grid"], [data-testid="comments-empty"]');
  await admin.click('[data-testid="comments-tab-all"]').catch(() => {});
  await sleep(800);
  return filteredRows(admin, 'comments-search', 'tbody tr', 'fxv0', (trs) => trs.map((tr) => ({
    author: tr.querySelector('[data-testid="comment-row-author"]')?.textContent?.trim() || '',
    status: tr.querySelector('[data-testid="comment-row-status"]')?.textContent?.trim() || '',
  })).filter((r) => r.author.startsWith('fxv0')));
}
async function ensureComments(admin, newAnon) {
  await ensureLogin(admin);
  await ensurePost(admin, 'markdown');
  let rows = await commentRows(admin);
  for (const c of COMMENTS) {
    if (rows.some((r) => r.author === c.name)) continue;
    const page = await newAnon();
    await gotoPublic(page, `/post/${POSTS.markdown.slug}`, '[data-testid="comment-form"]');
    const form = page.locator('[data-testid="comment-form"]');
    await form.locator('[data-testid="comment-name"]').fill(c.name);
    await form.locator('[data-testid="comment-email"]').fill(c.email);
    await form.locator('[data-testid="comment-input"]').fill(c.text);
    await solveCaptcha(form);
    await form.locator('[data-testid="comment-submit"]').click();
    await until(async () => (await page.locator('[data-testid="comment-form-success"], [data-testid="comment-form-error"]').count()) > 0, 30000);
    const outcome = (await page.locator('[data-testid="comment-form-success"], [data-testid="comment-form-error"]').first().textContent().catch(() => '?'))?.replace(/\s+/g, ' ').trim() || '';
    log(`comment ${c.name}: ${outcome.slice(0, 200)}`);
    await page.context().close();
    // An address confirmed on an earlier run posts straight into moderation (REQ-FN-048): no token is issued.
    if (/awaiting moderation/i.test(outcome)) continue;
    const tok = await waitToken(c.email, 'Comment');
    if (!tok) throw new Error(`no comment confirmation token for ${c.email}`);
    await confirmToken(newAnon, tok);
  }
  rows = await commentRows(admin);
  for (const c of COMMENTS.filter((x) => x.approve)) {
    const r = rows.find((x) => x.author === c.name);
    if (!r) throw new Error(`comment ${c.name} not in the queue`);
    if (!/approved/i.test(r.status)) {
      log(`approve ${c.name}`);
      await admin.locator('tbody tr').filter({ hasText: c.name }).locator('[data-testid="comment-approve"]').first().click();
      const ok = await until(async () => /approved/i.test((await commentRows(admin)).find((x) => x.author === c.name)?.status || ''), 25000, 1200);
      if (!ok) throw new Error(`approving ${c.name} did not stick`);
    }
  }
}

// ------------------------------------------------------------------ newsletter
async function archiveIssues(newAnon) {
  const page = await newAnon();
  await page.goto(`${BASE}/newsletters`, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid="newsletter-issues-list"], [data-testid="newsletter-issues-empty"]', { timeout: 60000 }).catch(() => {});
  await sleep(1500);
  const out = await page.$$eval('[data-testid="newsletter-issue-title"]', (a) => a.map((x) => ({ title: x.textContent.trim(), href: x.getAttribute('href') })));
  await page.context().close();
  return out;
}
async function ensureNewsletters(admin, newAnon) {
  await ensureLogin(admin);
  await ensureSubs(admin, newAnon);
  for (const n of NEWSLETTERS) {
    if ((await archiveIssues(newAnon)).some((i) => i.title === n.title)) { log(`newsletter "${n.title}" already sent`); continue; }
    log(`compose + send "${n.title}"`);
    await open(admin, '/admin/newsletter', '[data-testid="newsletter-subject"]');
    await sleep(1500);
    await admin.fill('[data-testid="newsletter-subject"]', n.title);
    await admin.fill('[data-testid="newsletter-summary"]', n.summary);
    await admin.locator('[data-testid="newsletter-body"] textarea').first().fill(n.body);
    await admin.click('[data-testid="newsletter-save-draft"]');
    await admin.locator('[data-testid="newsletter-status-message"]').waitFor({ timeout: 25000 });
    await admin.click('#audience-segment');
    await admin.fill('[data-testid="newsletter-segment-filter"]', 'fxv0-sub');
    await sleep(1200);
    await admin.keyboard.press('Tab');
    const counted = await until(async () => /^\d+ recipient/.test(((await admin.locator('[data-testid="newsletter-recipient-count"]').innerText()) || '').trim()), 25000, 800);
    if (!counted) throw new Error('recipient count never resolved');
    await admin.click('[data-testid="newsletter-send"]');
    await admin.locator('[data-testid="newsletter-send-dialog"]').waitFor({ state: 'visible', timeout: 15000 });
    await admin.locator('[data-testid="newsletter-send-confirm"]').click();
    await admin.locator('[data-testid="newsletter-send-outcome"]').waitFor({ state: 'visible', timeout: 90000 });
    log(`outcome: ${((await admin.locator('[data-testid="newsletter-send-outcome"]').innerText()) || '').replace(/\s+/g, ' ').slice(0, 80)}`);
  }
}

// ------------------------------------------------------------------ resume (site owner)
async function ensureExperience(admin) {
  await ensureLogin(admin);
  await open(admin, '/admin/experience', '[data-testid="experience-card"], [data-testid="experience-empty"]');
  if ((await admin.locator('[data-testid="experience-role"]', { hasText: EXP.role }).count()) > 0) return;
  log('add experience');
  await admin.click('[data-testid="add-experience"], [data-testid="add-first-experience"]');
  const d = admin.locator('[data-testid="experience-dialog"]');
  await d.waitFor({ state: 'visible' });
  await d.locator('[data-testid="experience-role-input"]').fill(EXP.role);
  await d.locator('[data-testid="experience-company-input"]').fill(EXP.company);
  await d.locator('[data-testid="experience-description-input"]').fill(EXP.desc);
  await d.locator('[data-testid="experience-order-input"]').fill(EXP.order);
  await d.locator('[data-testid="save-experience"]').click();
  await d.waitFor({ state: 'hidden', timeout: 45000 });
}
async function ensureSkill(admin) {
  await ensureLogin(admin);
  await open(admin, '/admin/skills', '[data-testid="skill-category-card"], [data-testid="skills-empty"]');
  if ((await admin.locator('[data-testid="skill-name"]', { hasText: SKILL.name }).count()) > 0) return;
  log('add skill');
  await admin.click('[data-testid="add-skill"]');
  const d = admin.locator('[data-testid="add-skill-dialog"]');
  await d.waitFor({ state: 'visible' });
  await d.locator('[data-testid="skill-name-input"]').fill(SKILL.name);
  await d.locator('[data-testid="skill-category-select"]').click();
  const existing = admin.getByRole('option', { name: SKILL.category, exact: true });
  if (await existing.count()) await existing.click();
  else { await admin.getByRole('option', { name: /Create New Category/ }).click(); await d.locator('[data-testid="new-category-input"]').fill(SKILL.category); }
  await d.locator('[data-testid="save-skill"]').click();
  await admin.locator('[data-testid="skill-name"]', { hasText: SKILL.name }).first().waitFor({ timeout: 25000 });
}
async function ensureAward(admin) {
  await ensureLogin(admin);
  await open(admin, '/admin/awards', '[data-testid="award-card"], [data-testid="awards-empty"]');
  if ((await admin.locator('[data-testid="award-title"]', { hasText: AWARD.title }).count()) > 0) return;
  log('add award');
  await admin.click('[data-testid="add-award"]');
  const d = admin.locator('[data-testid="award-dialog"]');
  await d.waitFor({ state: 'visible' });
  await d.locator('[data-testid="award-title-input"]').fill(AWARD.title);
  await d.locator('[data-testid="award-description-input"]').fill(AWARD.desc);
  await d.locator('[data-testid="award-year-input"]').fill(AWARD.year);
  await d.locator('[data-testid="save-award"]').click();
  await d.waitFor({ state: 'hidden', timeout: 45000 });
}

// ------------------------------------------------------------------ media
async function ensureMedia(admin) {
  await ensureLogin(admin);
  await open(admin, '/admin/images', '[data-testid="image-grid"], [data-testid="images-empty"]');
  if ((await admin.locator('[data-testid="image-name"]', { hasText: 'fxv0-media' }).count()) > 0) return;
  log('upload media');
  await admin.click('[data-testid="upload-image"], [data-testid="upload-first-image"]');
  const dlg = admin.locator('[data-testid="image-upload-dialog"]');
  await dlg.waitFor({ state: 'visible' });
  await sleep(1500);
  for (let i = 0; i < 3; i++) {
    await dlg.locator('input[type="file"]').setInputFiles({ name: MEDIA, mimeType: 'image/png', buffer: PNG });
    if (await dlg.locator('[data-testid="upload-selected-file"]').waitFor({ timeout: 8000 }).then(() => true, () => false)) break;
  }
  await dlg.locator('[data-testid="upload-alt-text-input"]').fill('fxv0 test image').catch(() => {});
  await dlg.locator('[data-testid="upload-confirm"]').click();
  await dlg.waitFor({ state: 'hidden', timeout: 40000 });
  await admin.locator('[data-testid="image-name"]', { hasText: 'fxv0-media' }).first().waitFor({ timeout: 25000 });
}

// ------------------------------------------------------------------ dispatcher
export async function seed(pieces, printRoute) {
  const routes = [];
  await withSession(async ({ admin, newAnon }) => {
    for (const piece of pieces) {
      const [kind, arg] = piece.split(':');
      if (kind === 'post') { await ensureLogin(admin); const id = await ensurePost(admin, arg); routes.push(`post.${arg}.id=${id}`, `post.${arg}.slug=${POSTS[arg].slug}`); }
      else if (kind === 'series') {
        await ensureLogin(admin);
        for (const k of ['part1', 'part2', 'part3']) await ensurePost(admin, k);
        routes.push(`series.slug=${SERIES.slug}`);
      }
      else if (kind === 'comments') await ensureComments(admin, newAnon);
      else if (kind === 'subs') await ensureSubs(admin, newAnon);
      else if (kind === 'newsletter') {
        await ensureNewsletters(admin, newAnon);
        const issues = (await archiveIssues(newAnon)).filter((i) => i.title.startsWith('fxv0'));
        for (const i of issues) routes.push(`newsletter.slug[${i.title}]=${(i.href || '').split('/').pop()}`);
      }
      else if (kind === 'verify') { await ensureLogin(admin); routes.push(`verify.token=${await ensureVerifyToken(admin, newAnon)}`); }
      else if (kind === 'resume') { await ensureExperience(admin); await ensureSkill(admin); await ensureAward(admin); }
      else if (kind === 'experience') await ensureExperience(admin);
      else if (kind === 'skill') await ensureSkill(admin);
      else if (kind === 'award') await ensureAward(admin);
      else if (kind === 'media') await ensureMedia(admin);
      else if (kind === 'users') {
        await ensureLogin(admin);
        await open(admin, '/users', 'tbody tr');
        if ((await admin.locator('tbody tr').count()) < 1) throw new Error('/users lists no account');
      }
      else throw new Error(`unknown piece "${piece}"`);
      log(`piece ${piece} ok`);
    }
  });
  if (printRoute) for (const r of routes) console.log(`TF_ROUTE ${r}`);
  return routes;
}

// ------------------------------------------------------------------ teardown (through the UI)
export async function teardown() {
  const left = [];
  await withSession(async ({ admin, newAnon }) => {
    await ensureLogin(admin);
    // comments
    for (let i = 0; i < 10; i++) {
      await open(admin, '/comments', '[data-testid="comments-grid"], [data-testid="comments-empty"]');
      const row = admin.locator('tbody tr').filter({ hasText: 'fxv0' }).first();
      if (!(await row.count())) break;
      await row.locator('[data-testid="comment-delete"]').click();
      await admin.locator('[data-testid="comment-delete-dialog"] button', { hasText: /^Delete$/ }).first().click().catch(async () => { await admin.locator('[data-testid="comment-delete-confirm"]').click(); });
      await sleep(2000);
    }
    // posts (a series' parts go with their posts)
    for (let i = 0; i < 12; i++) {
      const rows = (await postRows(admin, 'fxv0')).filter((r) => r.slug.startsWith('fxv0-'));
      if (!rows.length) break;
      log(`delete post ${rows[0].slug}`);
      await admin.locator('tbody tr').filter({ hasText: rows[0].slug }).locator('[data-testid="post-delete"]').click();
      await admin.locator('[data-testid="post-delete-confirm"]').click();
      await sleep(2500);
    }
    // series
    await open(admin, '/admin/series', '[data-testid="series-grid"], [data-testid="series-empty"]');
    for (let i = 0; i < 4; i++) {
      const row = admin.locator('tbody tr').filter({ hasText: SERIES.slug }).first();
      if (!(await row.count())) break;
      await row.locator('[data-testid="series-delete"]').click();
      await admin.locator('[data-testid="series-delete-confirm"]').click();
      await sleep(2500);
      await open(admin, '/admin/series', '[data-testid="series-grid"], [data-testid="series-empty"]');
    }
    // resume rows
    await open(admin, '/admin/experience', '[data-testid="experience-card"], [data-testid="experience-empty"]');
    while (await admin.locator('[data-testid="experience-card"]', { hasText: 'fxv0' }).count()) {
      await admin.locator('[data-testid="experience-card"]', { hasText: 'fxv0' }).first().locator('[data-testid="delete-experience"]').click();
      await admin.locator('[data-testid="delete-confirm"]').click(); await sleep(2500);
    }
    await open(admin, '/admin/skills', '[data-testid="skill-category-card"], [data-testid="skills-empty"]');
    while (await admin.locator('[data-testid="skill-row"]', { hasText: 'fxv0' }).count()) {
      await admin.locator('[data-testid="skill-row"]', { hasText: 'fxv0' }).first().locator('[data-testid="delete-skill"]').click();
      await admin.locator('[data-testid="delete-confirm"]').click(); await sleep(2500);
    }
    await open(admin, '/admin/awards', '[data-testid="award-card"], [data-testid="awards-empty"]');
    while (await admin.locator('[data-testid="award-card"]', { hasText: 'fxv0' }).count()) {
      await admin.locator('[data-testid="award-card"]', { hasText: 'fxv0' }).first().locator('[data-testid="delete-award"]').click();
      await admin.locator('[data-testid="delete-confirm"]').click(); await sleep(2500);
    }
    // media
    await open(admin, '/admin/images', '[data-testid="image-grid"], [data-testid="images-empty"]');
    while (await admin.locator('[data-testid="image-card"]', { hasText: 'fxv0' }).count()) {
      await admin.locator('[data-testid="image-card"]', { hasText: 'fxv0' }).first().locator('[data-testid="delete-image"]').click();
      await admin.locator('[data-testid="delete-confirm"]').click(); await sleep(2500);
    }
    // subscribers: the app has no remove action (consent evidence, REQ-FN-059) - deactivate only
    const rows = await subRows(admin);
    for (const r of rows.filter((x) => x.status === 'Active')) {
      await admin.locator('tbody tr').filter({ hasText: r.email }).locator('[data-testid="subscriber-deactivate"]').click();
      await sleep(2000);
      await subRows(admin);
    }
    const subsLeft = (await subRows(admin)).length;
    if (subsLeft) left.push(`${subsLeft} fxv0 subscriber rows remain (deactivated; the app offers no remove)`);
    const issues = (await archiveIssues(newAnon)).filter((i) => i.title.startsWith('fxv0'));
    if (issues.length) left.push(`${issues.length} sent fxv0 newsletter issue(s) remain (the app offers no delete for sent issues)`);
  });
  for (const l of left) console.log(`TEARDOWN-LEFT ${l}`);
  return left;
}

// ------------------------------------------------------------------ CLI
if (import.meta.url === `file://${process.argv[1]}`) {
  const [cmd, ...rest] = process.argv.slice(2);
  const printRoute = rest.includes('--print-route');
  const pieces = rest.filter((x) => !x.startsWith('--'));
  try {
    if (cmd === 'seed') await seed(pieces, printRoute);
    else if (cmd === 'teardown') await teardown();
    else { console.error('usage: _content.mjs seed <piece...> | teardown'); process.exit(2); }
    process.exit(0);
  } catch (e) { console.error(`fxv0 seed FAILED: ${e && e.stack ? e.stack.split('\n').slice(0, 4).join(' | ') : e}`); process.exit(1); }
}
