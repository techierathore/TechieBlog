/**
 * vall-public.spec.ts — cluster "public": the anonymous / guest reading surface.
 *
 * Scope: REQ-UI-005, 006 (graded against its superseder 049), 007, 008, 009, 010, 011, 012,
 * 031, 045, 046, 049, 050.
 *
 * Two runtime facts this spec had to learn the hard way and now encodes:
 *
 *  1. `#blazor-error-ui` ships hidden in EVERY Blazor document, so `toContainText('An unhandled
 *     error has occurred')` on <body> is always true. Only innerText (which respects display)
 *     may be used to detect the error boundary.
 *  2. This host prerenders, then re-renders interactively. For roughly 1.5 s the PRERENDERED
 *     shell and the INTERACTIVE shell are BOTH in the DOM — measured: 1 header at 0-2.5 s,
 *     2 headers at 3.0-4.0 s, 1 header from 4.5 s. Measuring inside that window produces both
 *     phantom "strict mode violation: resolved to 2 elements" failures and phantom zero-row
 *     readings off the prerendered copy. `settle()` below waits for the interactive copy to be
 *     the ONLY copy (interactive nodes carry a `_bl_*` attribute; prerendered ones do not).
 *
 * Database truth asserted against (psql, 2026-08-08):
 *   8 published posts (1,2,3,5,6,7,8,9) + 2 drafts (4, 10)
 *   categories: web-development 3, programming 2, career 1, devops 1, technology 1
 *   tags: dotnet 4, blazor 3, tutorial 3, aspnet-core 2 … csharp 0
 *   series blazor-server-in-production: 4 parts, 3 of them published (part 4 = draft postid 4)
 *   series postgres-for-dotnet-developers: 2 parts, both published
 *   BlogUser(IsSiteOwner).CVFilePath is EMPTY — the Download-CV button is legitimately absent.
 */
import { test, expect, Page } from '@playwright/test';
import { BASE, renderCheck, visualCheck } from './_gates';

// NOT under test-results/ — Playwright wipes that directory at the start of every run and
// sibling verifier agents run concurrently, which would erase this cluster's visual evidence.
const SHOT = '.verify/shots/public';

/**
 * True once the interactive render has replaced the prerendered one:
 * at most one public header, and the header (or theme toggle) carries a `_bl_*` interactive
 * marker. Pages on AuthLayout have no public header, so any interactive node will do.
 */
const HYDRATED = `(() => {
  const bl = (e) => Array.from(e.attributes).some(a => a.name.startsWith('_bl_'));
  const hs = document.querySelectorAll('[data-testid="public-header"]');
  if (hs.length > 1) return false;
  if (hs.length === 1) {
    const t = document.querySelector('[data-testid="theme-toggle"]');
    return bl(hs[0]) || (!!t && bl(t));
  }
  return Array.from(document.querySelectorAll('*')).some(e => bl(e));
})()`;

/** Anonymous page load that waits for hydration to settle before anything is measured. */
async function open(page: Page, route: string, heading?: RegExp) {
  await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(HYDRATED, undefined, { timeout: 60000 });
  if (heading) {
    await expect(page.locator('h1, h2').filter({ hasText: heading }).first()).toBeVisible({ timeout: 45000 });
  }
  await page.waitForFunction(() => !/^\s*Loading\b/i.test(document.body.innerText || ''), { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(900);
}

/** Collects console errors for the whole test; returns a getter. */
function trackConsole(page: Page) {
  const errs: string[] = [];
  page.on('console', (m) => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
  page.on('pageerror', (e) => errs.push(`pageerror: ${String(e).slice(0, 200)}`));
  return () => errs;
}

/** Console errors that are not the app's fault (asset 404s from the test harness). */
function fatalErrors(errs: string[]) {
  return errs.filter((e) => !/favicon|the server responded with a status of 404/i.test(e));
}

/**
 * Asserts the Blazor error boundary is not SHOWING. `#blazor-error-ui` is always present but
 * display:none, so only innerText counts.
 */
async function noErrorBoundary(page: Page) {
  const blazorErr = page.locator('#blazor-error-ui');
  if (await blazorErr.count()) {
    await expect(blazorErr.first(), 'Blazor unhandled-error UI is visible').toBeHidden();
  }
  const visibleText = await page.evaluate(() => (document.body as HTMLElement).innerText || '');
  expect(visibleText, 'error boundary text is visible on the page')
    .not.toMatch(/An unhandled error has occurred|Oops, something went wrong/i);
}

/** REQ-UI-048 evidence: does the page look styled, or is it raw unstyled HTML? */
async function styleProbe(page: Page, label: string) {
  const s = await page.evaluate(() => {
    const b = getComputedStyle(document.body);
    const h1 = document.querySelector('h1');
    return {
      bg: b.backgroundColor,
      font: b.fontFamily.split(',')[0],
      h1Size: h1 ? getComputedStyle(h1).fontSize : null,
      styledNodes: document.querySelectorAll('[class*="rounded-"], [class*="text-"], [class*="flex"]').length,
    };
  });
  console.log(`REQ-UI-048 style probe ${label}:`, JSON.stringify(s));
  return s;
}

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-005 — shell: header, nav, footer, theme toggle, mobile drawer
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-005 public shell renders header nav, footer and mobile drawer', async ({ page }) => {
  // This host takes 2-6 s per route to hand off from prerender to the interactive circuit and
  // this test walks eight routes at three viewports, so the 120 s default is not enough.
  test.setTimeout(360000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/about', /About/i);
  await noErrorBoundary(page);
  await styleProbe(page, '/about');

  await expect(page.locator('[data-testid="public-header"]')).toHaveCount(1);
  await expect(page.locator('[data-testid="public-header"]')).toBeVisible();
  await expect(page.locator('[data-testid="brand-link"]')).toBeVisible();
  await expect(page.locator('[data-testid="primary-nav"]')).toBeVisible();
  const navIds = ['nav-home', 'nav-categories', 'nav-series', 'nav-newsletter', 'nav-resume', 'nav-about'];
  for (const id of navIds) {
    await expect(page.locator(`[data-testid="${id}"]`), `desktop nav entry ${id}`).toBeVisible();
  }
  await expect(page.locator('[data-testid="header-search"]')).toBeVisible();
  await expect(page.locator('[data-testid="theme-toggle"]')).toBeVisible();
  await expect(page.locator('[data-testid="public-footer"]')).toBeVisible();

  // The shell wraps every public page (MainLayout and FullWidthLayout alike).
  for (const route of ['/', '/categories', '/series', '/search', '/rss', '/post/blazor-render-modes-explained']) {
    await open(page, route);
    await expect(page.locator('[data-testid="public-header"]'), `header on ${route}`).toBeVisible();
    await expect(page.locator('[data-testid="public-footer"]'), `footer on ${route}`).toBeVisible();
    await noErrorBoundary(page);
  }

  // Below 768px: desktop nav collapses, drawer trigger appears and opens.
  await page.setViewportSize({ width: 390, height: 844 });
  await open(page, '/about', /About/i);
  await expect(page.locator('[data-testid="primary-nav"]')).toBeHidden();
  const trigger = page.locator('[data-testid="mobile-nav-trigger"]');
  await expect(trigger).toHaveCount(1);
  await expect(trigger).toBeVisible();
  await trigger.click();
  await expect(page.locator('[data-testid="nav-home-mobile"]'), 'mobile drawer did not open').toBeVisible({ timeout: 20000 });
  await expect(page.locator('[data-testid="nav-about-mobile"]')).toBeVisible();
  await page.screenshot({ path: `${SHOT}/req-ui-005-drawer-390.png` });
  await page.keyboard.press('Escape');
  await page.waitForTimeout(600);

  // No horizontal scroll at 320px on the densest public listing.
  await page.setViewportSize({ width: 320, height: 800 });
  await open(page, '/category/web-development', /Web Development/i);
  const h320 = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  await page.screenshot({ path: `${SHOT}/req-ui-005-shell-320.png` });
  console.log('REQ-UI-005 horizontal overflow @320 =', h320);
  expect(h320, `horizontal overflow at 320px = ${h320}px`).toBeLessThanOrEqual(2);

  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-049 / REQ-UI-006 — portfolio home
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-049 portfolio home renders hero, stats, about, latest articles and contact', async ({ page }) => {
  test.setTimeout(300000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/');
  await noErrorBoundary(page);
  await styleProbe(page, '/');

  await expect(page.locator('[data-testid="home-page"]')).toBeVisible();
  await expect(page.locator('[data-testid="home-unconfigured"]')).toHaveCount(0);

  // Hero — real site-owner data, not placeholders.
  await expect(page.locator('[data-testid="resume-hero"]')).toBeVisible();
  for (const id of ['resume-greeting', 'resume-name', 'resume-title', 'resume-tagline']) {
    const t = ((await page.locator(`[data-testid="${id}"]`).first().textContent()) || '').trim();
    console.log(`REQ-UI-049 ${id} = "${t.slice(0, 70)}"`);
    expect(t.length, `${id} is blank`).toBeGreaterThan(2);
  }
  await expect(page.locator('[data-testid="resume-photo"]').first()).toBeVisible();
  await expect(page.locator('[data-testid="get-in-touch"]')).toBeVisible();
  // Download-CV is data-gated on BlogUser.CVFilePath, which is EMPTY in the database right now,
  // so its absence is NO-DATA, not a render defect. Recorded either way.
  const cv = await page.locator('[data-testid="download-cv"]').count();
  console.log(`REQ-UI-049 download-cv present=${cv} (DB CVFilePath is empty → NO-DATA when 0)`);

  // Stats — tiles with non-blank values AND labels.
  const tiles = page.locator('[data-testid="home-stat-card"]');
  const tileCount = await tiles.count();
  console.log('REQ-UI-049 stat tiles', tileCount);
  expect(tileCount, 'home stat tiles').toBeGreaterThan(0);
  // TrBlazeUI 2.0.2 (TR-022): the tiles are StatTile now, and StatTile renders Value/Label from
  // string parameters — there is no slot to hang `home-stat-value` / `home-stat-label` on, so the
  // two inner hooks are gone (logged as TR-069). The tile's own parts are read instead; the
  // assertion below is unchanged — both lines must be non-blank.
  for (let i = 0; i < tileCount; i++) {
    const v = ((await tiles.nth(i).locator('.tabular-nums').first().textContent()) || '').trim();
    const l = ((await tiles.nth(i).locator('.text-muted-foreground').first().textContent()) || '').trim();
    console.log(`REQ-UI-049 tile ${i}: "${v}" / "${l}"`);
    expect(v.length, `stat tile ${i} value blank`).toBeGreaterThan(0);
    expect(l.length, `stat tile ${i} label blank`).toBeGreaterThan(0);
  }

  // About summary.
  const about = ((await page.locator('[data-testid="home-about-summary"]').textContent()) || '').trim();
  console.log('REQ-UI-049 about summary chars', about.length);
  expect(about.length, 'home about summary blank').toBeGreaterThan(20);

  // Latest articles — real posts, each linking to a resolvable /post/{slug}.
  await expect(page.locator('[data-testid="home-articles-empty"]')).toHaveCount(0);
  const cards = page.locator('[data-testid="home-articles-grid"] [data-testid="post-card"]');
  const cardCount = await cards.count();
  console.log('REQ-UI-049 latest article cards', cardCount);
  expect(cardCount, 'latest article cards').toBeGreaterThan(0);
  const hrefs = await page.locator('[data-testid="home-articles-grid"] [data-testid="post-card-title"]').evaluateAll(
    (ns) => ns.map((n) => (n as HTMLAnchorElement).getAttribute('href') || (n.closest('a')?.getAttribute('href') ?? '')),
  );
  console.log('REQ-UI-049 article hrefs', JSON.stringify(hrefs));
  expect(hrefs.filter((h) => h.startsWith('/post/')).length, `article hrefs ${JSON.stringify(hrefs)}`).toBe(cardCount);
  for (const h of hrefs) {
    const r = await page.request.get(`${BASE}${h}`, { failOnStatusCode: false });
    expect(r.status(), `${h} returned ${r.status()}`).toBe(200);
  }

  // Contact block.
  await expect(page.locator('[data-testid="contact-section"]')).toBeVisible();
  const email = ((await page.locator('[data-testid="contact-email-value"]').textContent()) || '').trim();
  console.log('REQ-UI-049 contact email', email);
  expect(email, 'contact email blank').toMatch(/@/);

  // REQ-UI-050 on the landing page.
  await expect(page.locator('a[href="/login"], a[href^="/admin"]')).toHaveCount(0);

  const v1 = await visualCheck(page, `${SHOT}/req-ui-049-home-1280.png`, 1280);
  const v2 = await visualCheck(page, `${SHOT}/req-ui-049-home-390.png`, 390);
  console.log('REQ-UI-049 visual 1280', JSON.stringify({ o: v1.overlaps, z: v1.zeroSized, off: v1.offViewport, h: v1.hScroll }));
  console.log('REQ-UI-049 visual 390', JSON.stringify({ o: v2.overlaps, z: v2.zeroSized, off: v2.offViewport, h: v2.hScroll }));
  expect(v1.hScroll, 'h-scroll @1280').toBeLessThanOrEqual(2);
  expect(v2.hScroll, 'h-scroll @390').toBeLessThanOrEqual(2);
  expect(v1.overlaps, 'overlaps @1280').toHaveLength(0);
  expect(v2.overlaps, 'overlaps @390').toHaveLength(0);
  expect(v1.offViewport, 'off-viewport @1280').toHaveLength(0);
  expect(v2.offViewport, 'off-viewport @390').toHaveLength(0);

  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

test('REQ-UI-006 home is the superseding portfolio landing, not the old featured+grid page', async ({ page }) => {
  test.setTimeout(180000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/');
  await expect(page.locator('[data-testid="home-page"]')).toBeVisible();
  await expect(page.locator('[data-testid="resume-hero"]')).toBeVisible();
  await expect(page.locator('[data-testid="home-stats"]')).toBeVisible();
  await expect(page.locator('[data-testid="home-latest-articles"]')).toBeVisible();
  await expect(page.locator('[data-testid="contact-section"]')).toBeVisible();
  // The retired affordances must be gone: no blog sidebar, no home pagination.
  await expect(page.locator('[data-testid="blog-sidebar"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="pagination"]')).toHaveCount(0);
  await page.screenshot({ path: `${SHOT}/req-ui-006-home-superseded.png` });
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-007 — post view
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-007 post view renders article body, metadata, series nav and engagement', async ({ page }) => {
  test.setTimeout(300000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });
  // Part 1 of a 4-part series → exercises series navigation too.
  await open(page, '/post/blazor-render-modes-explained', /Blazor Render Modes Explained/i);
  await noErrorBoundary(page);
  await styleProbe(page, '/post/{slug}');
  await expect(page.locator('[data-testid="post-not-found"]')).toHaveCount(0);

  const results = [] as any[];
  for (const [name, sel] of [
    ['title', 'post-title'], ['author', 'post-author'], ['date', 'post-date'],
    ['category', 'post-category'], ['readtime', 'post-readtime'], ['abstract', 'post-abstract'],
    ['tags', 'post-tags'], ['content', 'post-content'], ['series-navigation', 'series-navigation'],
  ] as const) {
    results.push(await renderCheck(page, name, `[data-testid="${sel}"]`));
  }
  // Related posts render as Cards, not table rows — count the cards themselves.
  const relatedCards = await page.locator('[data-testid="related-post-card"]').count();
  const relatedTitles = await page.locator('[data-testid="related-post-title"]').allTextContents();
  results.push({
    control: 'related-posts',
    verdict: relatedCards > 0 && relatedTitles.every((t) => t.trim().length > 0) ? 'RENDERS' : 'RENDER-EMPTY',
    detail: `${relatedCards} cards: ${JSON.stringify(relatedTitles)}`,
  });
  results.push(await renderCheck(page, 'comments-section', '[data-testid="comments-section"]', 'present'));
  console.log('REQ-UI-007 controls', JSON.stringify(results, null, 1));
  const empty = results.filter((r) => r.verdict !== 'RENDERS');
  expect(empty, `RENDER-EMPTY controls: ${JSON.stringify(empty)}`).toHaveLength(0);

  // Markdown really became HTML, not escaped text.
  const md = await page.locator('[data-testid="post-content"]').evaluate((n) => ({
    blocks: n.querySelectorAll('h1,h2,h3,p,ul,ol,pre,code,blockquote,table').length,
    raw: /(^|\n)#{1,3}\s|\*\*[^*]+\*\*/.test((n.textContent || '')),
    len: (n.textContent || '').trim().length,
  }));
  console.log('REQ-UI-007 markdown', JSON.stringify(md));
  expect(md.blocks, 'rendered markdown block elements').toBeGreaterThan(3);
  expect(md.len, 'article body length').toBeGreaterThan(300);
  expect(md.raw, 'raw markdown syntax leaked into the rendered body').toBeFalsy();

  // Tag chips are real links.
  const tagLinks = await page.locator('[data-testid="post-tags"] a').count();
  console.log('REQ-UI-007 tag links', tagLinks);
  expect(tagLinks, 'tag links').toBeGreaterThan(0);

  // Series navigation shows the real series and part number.
  const sName = ((await page.locator('[data-testid="series-navigation-name"]').textContent()) || '').trim();
  const sPart = ((await page.locator('[data-testid="series-navigation-part"]').textContent()) || '').trim();
  console.log('REQ-UI-007 series nav:', sName, '|', sPart);
  expect(sName.length).toBeGreaterThan(3);
  expect(sPart).toMatch(/\d/);
  await expect(page.locator('[data-testid="series-next-post"]'), 'part 1 has no next-part link').toBeVisible();

  // Engagement surface for an anonymous visitor.
  const cCount = ((await page.locator('[data-testid="comments-count"]').textContent()) || '').trim();
  console.log('REQ-UI-007 comments count text:', cCount);

  // Geometry of THIS post is captured now, before navigating away; the assertions are deferred
  // to the end of the test so a visual failure cannot hide the remaining render evidence.
  const v1 = await visualCheck(page, `${SHOT}/req-ui-007-post-1280.png`, 1280);
  const v2 = await visualCheck(page, `${SHOT}/req-ui-007-post-390.png`, 390);
  console.log('REQ-UI-007 visual /post/blazor-render-modes-explained',
    JSON.stringify({ o1: v1.overlaps, off1: v1.offViewport, h1: v1.hScroll, o2: v2.overlaps, off2: v2.offViewport, h2: v2.hScroll }));
  if (v2.hScroll > 2) {
    // Identify what actually overflows, so the finding names a control rather than a number.
    const wide = await page.evaluate(() => {
      const vw = document.documentElement.clientWidth;
      const out: string[] = [];
      document.querySelectorAll('[data-testid="post-content"] *').forEach((e) => {
        const r = e.getBoundingClientRect();
        if (r.width > 0 && r.right > vw + 2) out.push(`${e.tagName.toLowerCase()}(w=${Math.round(r.width)},overflowX=${getComputedStyle(e).overflowX})`);
      });
      return out.slice(0, 6);
    });
    console.log('REQ-UI-007 overflow offenders @390:', JSON.stringify(wide));
  }

  // The markdown kitchen-sink post is the stress case for the renderer.
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/post/the-markdown-kitchen-sink', /Markdown Kitchen Sink/i);
  const ks = await page.locator('[data-testid="post-content"]').evaluate((n) => ({
    pre: n.querySelectorAll('pre').length,
    table: n.querySelectorAll('table').length,
    lists: n.querySelectorAll('ul,ol').length,
  }));
  console.log('REQ-UI-007 kitchen sink', JSON.stringify(ks));
  expect(ks.pre + ks.table + ks.lists, 'kitchen-sink rich blocks').toBeGreaterThan(2);
  const v3 = await visualCheck(page, `${SHOT}/req-ui-007-kitchensink-1280.png`, 1280);
  expect(v3.hScroll, 'kitchen-sink h-scroll').toBeLessThanOrEqual(2);

  expect(v1.hScroll, 'post view h-scroll @1280').toBeLessThanOrEqual(2);
  expect(v1.overlaps, 'post view overlaps @1280').toHaveLength(0);
  expect(v2.overlaps, 'post view overlaps @390').toHaveLength(0);
  expect(v2.hScroll, 'post view h-scroll @390').toBeLessThanOrEqual(2);


  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-008 — category archive
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-008 category archive filters posts and matches the database counts', async ({ page }) => {
  test.setTimeout(360000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });

  // Index mode — all five seeded categories with counts.
  await open(page, '/categories', /All Categories/i);
  await noErrorBoundary(page);
  await styleProbe(page, '/categories');
  await expect(page.locator('[data-testid="categories-empty"]')).toHaveCount(0);
  const catCards = page.locator('[data-testid="category-card"]');
  const nCats = await catCards.count();
  console.log('REQ-UI-008 category cards', nCats);
  expect(nCats, 'category cards vs 5 seeded categories').toBe(5);
  for (let i = 0; i < nCats; i++) {
    const title = ((await catCards.nth(i).locator('[data-testid="category-card-title"]').textContent()) || '').trim();
    const count = ((await catCards.nth(i).locator('[data-testid="category-card-count"]').textContent()) || '').trim();
    console.log(`REQ-UI-008 card ${i}: "${title}" — "${count}"`);
    expect(title.length, `category card ${i} title blank`).toBeGreaterThan(0);
    expect(count, `category card ${i} count blank`).toMatch(/\d+\s*posts/i);
  }
  await visualCheck(page, `${SHOT}/req-ui-008-categories-1280.png`, 1280);

  // Detail mode — the count badge must equal the cards listed and the DB truth.
  await page.setViewportSize({ width: 1280, height: 900 });
  const expected: Record<string, number> = {
    'web-development': 3, programming: 2, career: 1, devops: 1, technology: 1,
  };
  for (const [slug, n] of Object.entries(expected)) {
    await open(page, `/category/${slug}`);
    await expect(page.locator('[data-testid="category-not-found"]')).toHaveCount(0);
    await expect(page.locator('[data-testid="category-posts-empty"]')).toHaveCount(0);
    const badge = ((await page.locator('[data-testid="category-post-count"]').textContent()) || '').trim();
    const shown = await page.locator('[data-testid="posts-grid"] [data-testid="post-card"]').count();
    console.log(`REQ-UI-008 /category/${slug}: badge="${badge}" cards=${shown} db=${n}`);
    expect(shown, `${slug} rendered cards vs DB`).toBe(n);
    expect(parseInt(badge.replace(/\D/g, ''), 10), `${slug} badge vs DB`).toBe(n);
  }

  // Only published posts appear; the archive has a breadcrumb.
  await open(page, '/category/web-development', /Web Development/i);
  const titles = await page.locator('[data-testid="posts-grid"] [data-testid="post-card-title"]').allTextContents();
  console.log('REQ-UI-008 web-development titles', JSON.stringify(titles));
  expect(titles.join('|'), 'draft post leaked into the archive').not.toContain('Observability for Blazor Server');
  await expect(page.locator('[data-testid="breadcrumb"]')).toBeVisible();

  const v = await visualCheck(page, `${SHOT}/req-ui-008-category-390.png`, 390);
  console.log('REQ-UI-008 visual 390', JSON.stringify({ o: v.overlaps, off: v.offViewport, h: v.hScroll }));
  expect(v.hScroll).toBeLessThanOrEqual(2);
  expect(v.overlaps).toHaveLength(0);
  await page.setViewportSize({ width: 1280, height: 900 });
  const v2 = await visualCheck(page, `${SHOT}/req-ui-008-category-1280.png`, 1280);
  expect(v2.hScroll).toBeLessThanOrEqual(2);
  expect(v2.overlaps).toHaveLength(0);

  // Unknown slug is handled, not exploded.
  await open(page, '/category/no-such-category');
  await expect(page.locator('[data-testid="category-not-found"]')).toBeVisible();
  await noErrorBoundary(page);

  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-009 — tag archive (the Story 7.5 count regression)
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-009 tag archive count equals the posts actually listed', async ({ page }) => {
  test.setTimeout(360000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });

  await open(page, '/tags', /All Tags/i);
  await noErrorBoundary(page);
  await styleProbe(page, '/tags');
  await expect(page.locator('[data-testid="tags-empty"]')).toHaveCount(0);
  const cloud = page.locator('[data-testid="tags-cloud"]');
  await expect(cloud).toBeVisible();
  const cloudLinks = await cloud.locator('a').count();
  console.log('REQ-UI-009 tag cloud entries', cloudLinks, '(DB has 15 tags)');
  expect(cloudLinks, 'tag cloud entries').toBeGreaterThanOrEqual(14);
  await visualCheck(page, `${SHOT}/req-ui-009-tags-1280.png`, 1280);

  await page.setViewportSize({ width: 1280, height: 900 });
  const expected: Record<string, number> = { dotnet: 4, blazor: 3, tutorial: 3, 'aspnet-core': 2, architecture: 1 };
  for (const [slug, n] of Object.entries(expected)) {
    await open(page, `/tag/${slug}`);
    await expect(page.locator('[data-testid="tag-not-found"]')).toHaveCount(0);
    const badge = ((await page.locator('[data-testid="tag-post-count"]').textContent()) || '').trim();
    const shown = await page.locator('[data-testid="posts-grid"] [data-testid="post-card"]').count();
    console.log(`REQ-UI-009 /tag/${slug}: badge="${badge}" cards=${shown} db_published=${n}`);
    expect(shown, `${slug} cards vs DB published count`).toBe(n);
    expect(parseInt(badge.replace(/\D/g, ''), 10), `${slug} badge vs listed cards (Story 7.5 regression)`).toBe(shown);
  }

  // FIX-009: the category name on each card must be a real category, not blank.
  await open(page, '/tag/dotnet');
  const cats = await page.locator('[data-testid="posts-grid"] [data-testid="post-card-category"]').allTextContents();
  console.log('REQ-UI-009 card categories', JSON.stringify(cats));
  expect(cats.length).toBeGreaterThan(0);
  for (const c of cats) expect(c.trim().length, `blank category badge in ${JSON.stringify(cats)}`).toBeGreaterThan(0);

  const v = await visualCheck(page, `${SHOT}/req-ui-009-tag-390.png`, 390);
  console.log('REQ-UI-009 visual 390', JSON.stringify({ o: v.overlaps, off: v.offViewport, h: v.hScroll }));
  expect(v.hScroll).toBeLessThanOrEqual(2);
  expect(v.overlaps).toHaveLength(0);
  await page.setViewportSize({ width: 1280, height: 900 });
  const v2 = await visualCheck(page, `${SHOT}/req-ui-009-tag-1280.png`, 1280);
  expect(v2.hScroll).toBeLessThanOrEqual(2);
  expect(v2.overlaps).toHaveLength(0);

  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-010 — series view (the "0 Parts" regression)
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-010 series view lists parts in reading order with correct part counts', async ({ page }) => {
  test.setTimeout(300000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });

  // DB truth: blazor-server-in-production = 4 parts, 3 published (part 4 is a draft);
  //           postgres-for-dotnet-developers = 2 parts, both published.
  // The badge counts PUBLISHED parts; the detail list also renders the unpublished part as
  // "Coming Soon". Both readings are asserted so a silent change in either is caught.
  await open(page, '/series', /All Series/i);
  await noErrorBoundary(page);
  await styleProbe(page, '/series');
  await expect(page.locator('[data-testid="series-empty"]')).toHaveCount(0);
  const cards = page.locator('[data-testid="series-card"]');
  const nCards = await cards.count();
  console.log('REQ-UI-010 series cards', nCards);
  expect(nCards, 'series cards vs 2 seeded series').toBe(2);
  const parts = await page.locator('[data-testid="series-card-parts"]').allTextContents();
  console.log('REQ-UI-010 series index part badges', JSON.stringify(parts));
  const nums = parts.map((p) => parseInt(p.replace(/\D/g, ''), 10)).sort((a, b) => a - b);
  expect(nums.filter((n) => n === 0), `"0 Parts" regression: ${JSON.stringify(parts)}`).toHaveLength(0);
  expect(nums, `part badges on /series were ${JSON.stringify(parts)} (expected published counts 2 and 3)`).toEqual([2, 3]);
  await visualCheck(page, `${SHOT}/req-ui-010-series-index-1280.png`, 1280);

  await page.setViewportSize({ width: 1280, height: 900 });
  const expected: Record<string, { published: number; rows: number }> = {
    'blazor-server-in-production': { published: 3, rows: 4 },
    'postgres-for-dotnet-developers': { published: 2, rows: 2 },
  };
  for (const [slug, e] of Object.entries(expected)) {
    await open(page, `/series/${slug}`);
    await expect(page.locator('[data-testid="series-not-found"]')).toHaveCount(0);
    await expect(page.locator('[data-testid="series-posts-empty"]')).toHaveCount(0);
    const badge = ((await page.locator('[data-testid="series-part-count"]').textContent()) || '').trim();
    const rows = await page.locator('[data-testid="series-post"]').count();
    const statuses = await page.locator('[data-testid="series-post-status"]').allTextContents();
    console.log(`REQ-UI-010 /series/${slug}: header="${badge}" rows=${rows} statuses=${JSON.stringify(statuses)} db(published=${e.published}, total=${e.rows})`);
    expect(parseInt(badge.replace(/\D/g, ''), 10), `${slug} header part count (published)`).toBe(e.published);
    expect(rows, `${slug} rendered part rows (all parts incl. unpublished)`).toBe(e.rows);
    // Reading order: part numbers ascend.
    const order = (await page.locator('[data-testid="series-post-number"]').allTextContents())
      .map((t) => parseInt(t.replace(/\D/g, ''), 10));
    console.log(`REQ-UI-010 ${slug} order`, JSON.stringify(order));
    expect(order, `${slug} parts are not in reading order`).toEqual([...order].sort((a, b) => a - b));
    const titles = await page.locator('[data-testid="series-post-title"]').allTextContents();
    console.log(`REQ-UI-010 ${slug} titles`, JSON.stringify(titles));
    for (const t of titles) expect(t.trim().length, 'blank series part title').toBeGreaterThan(0);
    await expect(page.locator('[data-testid="series-author"]')).toBeVisible();
  }

  const v = await visualCheck(page, `${SHOT}/req-ui-010-series-390.png`, 390);
  console.log('REQ-UI-010 visual 390', JSON.stringify({ o: v.overlaps, off: v.offViewport, h: v.hScroll }));
  expect(v.hScroll).toBeLessThanOrEqual(2);
  expect(v.overlaps).toHaveLength(0);
  await page.setViewportSize({ width: 1280, height: 900 });
  const v2 = await visualCheck(page, `${SHOT}/req-ui-010-series-detail-1280.png`, 1280);
  expect(v2.hScroll).toBeLessThanOrEqual(2);
  expect(v2.overlaps).toHaveLength(0);

  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-011 — search
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-011 search returns database results with highlighting, filter and paging', async ({ page }) => {
  test.setTimeout(360000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });

  await open(page, '/search');
  await noErrorBoundary(page);
  await styleProbe(page, '/search');
  await expect(page.locator('[data-testid="search-input"]')).toBeVisible();

  // The category filter is a TrBlazeUI Select (a combobox button + a portal listbox), not a
  // native <select>, so its options only exist in the DOM once it is opened.
  const filter = page.locator('[data-testid="category-filter"]');
  await expect(filter).toBeVisible();
  await filter.click();
  await page.waitForTimeout(1200);
  const opts = (await page.locator('[role="option"]').allTextContents()).map((t) => t.trim()).filter(Boolean);
  console.log('REQ-UI-011 category filter options', JSON.stringify(opts));
  expect(opts.length, `category filter options: ${JSON.stringify(opts)}`).toBeGreaterThanOrEqual(6); // 5 categories + "All"
  for (const c of ['Web Development', 'Programming', 'Career', 'DevOps', 'Technology']) {
    expect(opts.join('|'), `category "${c}" missing from the filter`).toContain(c);
  }
  await page.keyboard.press('Escape');
  await page.waitForTimeout(600);

  // Query with a term that exists in the seed data.
  await page.fill('[data-testid="search-input"]', 'blazor');
  await page.click('[data-testid="search-submit"]');
  await expect(page.locator('[data-testid="search-results"]')).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(1200);
  await expect(page.locator('[data-testid="search-empty"]')).toHaveCount(0);

  const rows = page.locator('[data-testid="search-result"]');
  const n = await rows.count();
  const countTxt = ((await page.locator('[data-testid="search-results-count"]').textContent()) || '').trim();
  console.log(`REQ-UI-011 "blazor": ${n} results, count text "${countTxt}"`);
  expect(n, 'search results for "blazor"').toBeGreaterThan(0);
  expect(countTxt).toMatch(/\d/);
  expect(parseInt(countTxt.replace(/\D/g, ''), 10), 'result count badge vs rows rendered').toBe(n);

  // Each row is fully populated, not a placeholder shell.
  for (let i = 0; i < n; i++) {
    for (const id of ['search-result-title', 'search-result-author', 'search-result-date', 'search-result-readtime']) {
      const t = ((await rows.nth(i).locator(`[data-testid="${id}"]`).first().textContent()) || '').trim();
      expect(t.length, `result ${i} ${id} blank`).toBeGreaterThan(0);
    }
  }

  // Highlighting. `HighlightSearchTerms` only rewrites the EXCERPT (post.Abstract); the title is
  // never marked. None of the three "blazor" hits carries the word in its abstract (psql: title
  // matches only), so this query legitimately produces no <mark> — recorded, then the mechanism
  // is proved with a term that IS in an abstract ("index", postid 5).
  const marksBlazor = await page.locator('[data-testid="search-results"] mark').allTextContents();
  console.log('REQ-UI-011 highlights for a TITLE-only match ("blazor"):', JSON.stringify(marksBlazor));

  await page.fill('[data-testid="search-input"]', 'index');
  await page.click('[data-testid="search-submit"]');
  await expect(page.locator('[data-testid="search-results"]')).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(1500);
  const marks = await page.locator('[data-testid="search-results"] mark').allTextContents();
  const idxRows = await page.locator('[data-testid="search-result"]').count();
  console.log(`REQ-UI-011 "index": ${idxRows} results, highlights ${JSON.stringify(marks.slice(0, 8))}`);
  expect(idxRows, 'no results for "index"').toBeGreaterThan(0);
  expect(marks.length, 'no <mark> highlighting even for an excerpt-matching term').toBeGreaterThan(0);
  expect(marks.some((m) => /index/i.test(m)), `highlighted text ${JSON.stringify(marks.slice(0, 8))}`).toBeTruthy();

  // Back to the "blazor" query for the remaining result-content assertions.
  await page.fill('[data-testid="search-input"]', 'blazor');
  await page.click('[data-testid="search-submit"]');
  await expect(page.locator('[data-testid="search-results"]')).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(1500);

  // Titles really come from the database.
  const titles = await page.locator('[data-testid="search-result-title"]').allTextContents();
  console.log('REQ-UI-011 titles', JSON.stringify(titles));
  expect(titles.join(' ').toLowerCase()).toContain('blazor');
  // Drafts must never surface in search.
  expect(titles.join('|'), 'draft leaked into search results').not.toContain('Observability for Blazor Server');

  const v1 = await visualCheck(page, `${SHOT}/req-ui-011-search-1280.png`, 1280);
  const v2 = await visualCheck(page, `${SHOT}/req-ui-011-search-390.png`, 390);
  console.log('REQ-UI-011 visual', JSON.stringify({ o1: v1.overlaps, off1: v1.offViewport, h1: v1.hScroll, o2: v2.overlaps, off2: v2.offViewport, h2: v2.hScroll }));
  expect(v1.hScroll).toBeLessThanOrEqual(2);
  expect(v2.hScroll).toBeLessThanOrEqual(2);
  expect(v1.overlaps).toHaveLength(0);
  expect(v2.overlaps).toHaveLength(0);

  // A term with no hits produces the empty state, not a crash or stale rows.
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.fill('[data-testid="search-input"]', 'zzqqxnotathing');
  await page.click('[data-testid="search-submit"]');
  await expect(page.locator('[data-testid="search-empty"]')).toBeVisible({ timeout: 25000 });
  expect(await page.locator('[data-testid="search-result"]').count(), 'stale rows after a no-hit query').toBe(0);
  await noErrorBoundary(page);

  // Paging reflects real counts — 8 published posts, so a broad query stays on one page.
  await page.fill('[data-testid="search-input"]', 'the');
  await page.click('[data-testid="search-submit"]');
  await page.waitForTimeout(2500);
  const pag = await page.locator('[data-testid="pagination"]').count();
  const broadRows = await page.locator('[data-testid="search-result"]').count();
  console.log(`REQ-UI-011 broad query "the": rows=${broadRows} paginator=${pag}`);
  expect(broadRows, 'broad query returned nothing').toBeGreaterThan(0);

  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-012 — about + 404
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-012 about page and 404 page render', async ({ page }) => {
  test.setTimeout(300000);
  const errs = trackConsole(page);
  await page.setViewportSize({ width: 1280, height: 900 });

  await open(page, '/about', /About/i);
  await noErrorBoundary(page);
  await styleProbe(page, '/about');
  await expect(page.locator('[data-testid="about-page"]')).toBeVisible();
  await expect(page.locator('[data-testid="about-card"]')).toBeVisible();
  const stack = ((await page.locator('[data-testid="about-stack"]').textContent()) || '').trim();
  console.log('REQ-UI-012 about stack chars', stack.length);
  expect(stack.length, 'about stack section blank').toBeGreaterThan(10);
  await expect(page.locator('[data-testid="about-links"]')).toBeVisible();
  const v1 = await visualCheck(page, `${SHOT}/req-ui-012-about-1280.png`, 1280);
  const v2 = await visualCheck(page, `${SHOT}/req-ui-012-about-390.png`, 390);
  console.log('REQ-UI-012 about visual', JSON.stringify({ o1: v1.overlaps, off1: v1.offViewport, h1: v1.hScroll, o2: v2.overlaps, off2: v2.offViewport, h2: v2.hScroll }));
  expect(v1.hScroll).toBeLessThanOrEqual(2);
  expect(v2.hScroll).toBeLessThanOrEqual(2);
  expect(v1.overlaps).toHaveLength(0);
  expect(v2.overlaps).toHaveLength(0);

  // Explicit /404 route (AuthLayout — no public header).
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/404');
  await noErrorBoundary(page);
  const body404 = await page.evaluate(() => (document.body as HTMLElement).innerText || '');
  console.log('REQ-UI-012 /404 visible text:', body404.slice(0, 200).replace(/\s+/g, ' '));
  await page.screenshot({ path: `${SHOT}/req-ui-012-404-1280.png` });
  expect(body404, '/404 does not present a not-found message').toMatch(/page not found|404/i);

  // An unmatched route must land on a not-found surface, not a raw framework error.
  await page.goto(`${BASE}/this-route-does-not-exist-xyz`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(5000);
  const unknown = await page.evaluate(() => (document.body as HTMLElement).innerText || '');
  console.log('REQ-UI-012 unknown route visible text:', unknown.slice(0, 250).replace(/\s+/g, ' '));
  await page.screenshot({ path: `${SHOT}/req-ui-012-unknown-route.png` });
  expect(unknown, 'unmatched route did not produce a not-found surface')
    .toMatch(/page not found|404|not found|nothing at this address/i);

  const fatal = fatalErrors(errs());
  expect(fatal, `console errors: ${fatal.join(' | ')}`).toHaveLength(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-031 — light/dark toggle  (+ REQ-UI-033 dark legibility evidence)
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-031 theme toggle flips dark mode and persists across reload', async ({ page }) => {
  test.setTimeout(480000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/about', /About/i);

  const toggle = page.locator('[data-testid="theme-toggle"]');
  await expect(toggle).toHaveCount(1);
  await expect(toggle).toBeVisible();

  const before = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  await toggle.click();
  await page.waitForTimeout(1500);
  const after = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  console.log(`REQ-UI-031 dark before=${before} after=${after}`);
  expect(after, 'clicking the toggle did not flip the dark class').not.toBe(before);

  const stored = await page.evaluate(() => JSON.stringify(Object.fromEntries(
    Object.keys(localStorage).map((k) => [k, localStorage.getItem(k)]),
  )));
  console.log('REQ-UI-031 localStorage', stored.slice(0, 300));

  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForFunction(HYDRATED, undefined, { timeout: 60000 });
  await page.waitForTimeout(1500);
  const afterReload = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  console.log(`REQ-UI-031 after reload=${afterReload}`);
  expect(afterReload, 'theme choice did not survive a reload').toBe(after);

  // The toggle is on every public page and the choice follows the visitor.
  for (const route of ['/', '/categories', '/series', '/search', '/rss', '/tags']) {
    await open(page, route);
    await expect(page.locator('[data-testid="theme-toggle"]'), `theme toggle on ${route}`).toBeVisible();
    const d = await page.evaluate(() => document.documentElement.classList.contains('dark'));
    expect(d, `theme choice lost on ${route}`).toBe(after);
  }

  // Ensure the dark screenshots below really are the dark variant.
  let isDark = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  if (!isDark) {
    await page.locator('[data-testid="theme-toggle"]').click();
    await page.waitForTimeout(1500);
    isDark = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  }
  console.log('REQ-UI-033 capturing dark screenshots, isDark =', isDark);
  for (const [name, route] of [
    ['home', '/'], ['post', '/post/blazor-render-modes-explained'], ['category', '/category/web-development'],
    ['tag', '/tag/dotnet'], ['search', '/search'], ['about', '/about'], ['series', '/series'], ['rss', '/rss'],
  ] as const) {
    await open(page, route);
    const contrast = await page.evaluate(() => {
      const b = getComputedStyle(document.body);
      const h = document.querySelector('h1');
      return { bodyBg: b.backgroundColor, bodyFg: b.color, h1Fg: h ? getComputedStyle(h).color : null };
    });
    console.log(`REQ-UI-033 dark ${name}`, JSON.stringify(contrast));
    await page.screenshot({ path: `${SHOT}/dark-${name}.png` });
  }
});

test('REQ-UI-031 theme toggle is keyboard reachable and exposes role="switch"', async ({ page }) => {
  test.setTimeout(180000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/about', /About/i);

  const attrs = await page.locator('[data-testid="theme-toggle"]').evaluate((n) => ({
    tag: n.tagName.toLowerCase(),
    role: n.getAttribute('role'),
    ariaLabel: n.getAttribute('aria-label'),
    ariaChecked: n.getAttribute('aria-checked'),
    tabindex: n.getAttribute('tabindex'),
  }));
  console.log('REQ-UI-031 toggle attrs', JSON.stringify(attrs));

  // Keyboard reachability: tab from the brand link until the toggle takes focus.
  await page.locator('[data-testid="brand-link"]').focus();
  let focused = false;
  for (let i = 0; i < 25 && !focused; i++) {
    await page.keyboard.press('Tab');
    focused = await page.evaluate(() => document.activeElement?.getAttribute('data-testid') === 'theme-toggle');
  }
  console.log('REQ-UI-031 keyboard reachable:', focused);
  expect(focused, 'theme toggle is not reachable by Tab from the header brand link').toBeTruthy();

  const preKey = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  const postKey = await page.evaluate(() => document.documentElement.classList.contains('dark'));
  console.log(`REQ-UI-031 keyboard activation ${preKey} -> ${postKey}`);
  expect(postKey, 'Enter did not activate the theme toggle').not.toBe(preKey);

  expect(attrs.role, `theme toggle renders <${attrs.tag} role="${attrs.role}"> — acceptance requires role="switch"`).toBe('switch');
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-045 — shared components
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-045 PostCard, Pagination, Breadcrumb and Sidebar render consistently with real counts', async ({ page }) => {
  test.setTimeout(360000);
  await page.setViewportSize({ width: 1280, height: 900 });

  // PostCard shape must be identical on home, category archive and tag archive.
  const shapes: Record<string, string[]> = {};
  const surfaces: [string, string, string][] = [
    ['home', '/', '[data-testid="home-articles-grid"]'],
    ['category', '/category/web-development', '[data-testid="posts-grid"]'],
    ['tag', '/tag/dotnet', '[data-testid="posts-grid"]'],
  ];
  for (const [name, route, scope] of surfaces) {
    await open(page, route);
    const card = page.locator(`${scope} [data-testid="post-card"]`).first();
    await expect(card, `${name} has no PostCard`).toBeVisible();
    shapes[name] = await card.evaluate((n) =>
      Array.from(n.querySelectorAll('[data-testid]'))
        .map((e) => e.getAttribute('data-testid')!)
        .filter((t) => t.startsWith('post-card'))
        .sort());
    for (const id of ['post-card-title', 'post-card-author', 'post-card-date', 'post-card-readtime', 'post-card-excerpt']) {
      const t = ((await card.locator(`[data-testid="${id}"]`).first().textContent()) || '').trim();
      console.log(`REQ-UI-045 ${name} ${id} = "${t.slice(0, 50)}"`);
      expect(t.length, `${name} PostCard ${id} blank`).toBeGreaterThan(0);
    }
  }
  console.log('REQ-UI-045 PostCard shapes', JSON.stringify(shapes));
  expect(shapes.category, 'PostCard differs between category and tag archives').toEqual(shapes.tag);
  // Ignore the image/placeholder variant when comparing structure; the image itself is asserted
  // separately below because that is where the real divergence lives.
  const normalise = (a: string[]) => a.map((t) => t.replace('post-card-image-placeholder', 'post-card-image')).sort();
  expect(normalise(shapes.home), 'PostCard structure differs between home and the archives')
    .toEqual(normalise(shapes.category));

  // Breadcrumb on the archive pages.
  await open(page, '/category/web-development', /Web Development/i);
  const crumbs = (await page.locator('[data-testid="breadcrumb"] a, [data-testid="breadcrumb"] li').allTextContents())
    .map((c) => c.trim()).filter(Boolean);
  console.log('REQ-UI-045 breadcrumb', JSON.stringify(crumbs));
  expect(crumbs.length, 'breadcrumb has no items').toBeGreaterThan(0);

  // Sidebar widgets fed by real taxonomy data.
  await open(page, '/tags', /All Tags/i);
  await expect(page.locator('[data-testid="blog-sidebar"]')).toBeVisible();
  await expect(page.locator('[data-testid="sidebar-categories-empty"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="sidebar-tags-empty"]')).toHaveCount(0);
  const sc = await page.locator('[data-testid="sidebar-categories"] a').count();
  const stg = await page.locator('[data-testid="sidebar-tags"] a').count();
  const sSearch = await page.locator('[data-testid="sidebar-search-input"]').count();
  const sSub = await page.locator('[data-testid="sidebar-subscribe"], [data-testid="subscribe-email"]').count();
  console.log(`REQ-UI-045 sidebar categories=${sc} tags=${stg} search=${sSearch} subscribe=${sSub}`);
  expect(sc, 'sidebar categories widget empty while the DB has 5').toBe(5);
  expect(stg, 'sidebar tags widget empty while the DB has 15').toBeGreaterThan(0);
  expect(sSearch, 'sidebar search input missing').toBeGreaterThan(0);
  expect(sSub, 'sidebar subscribe widget missing').toBeGreaterThan(0);
  await page.screenshot({ path: `${SHOT}/req-ui-045-sidebar-1280.png` });

  // Pagination reflects real counts: 3 posts with PageSize 9 → no paginator (correct, not a bug).
  await open(page, '/category/web-development', /Web Development/i);
  const archivePager = await page.locator('[data-testid="pagination"]').count();
  console.log(`REQ-UI-045 archive paginator count=${archivePager} (3 posts, PageSize 9 → expected 0)`);
  expect(archivePager, 'paginator shown for a single-page archive').toBe(0);

  // The BlogPagination component itself must work where it does appear — the per-post paginator.
  await open(page, '/post/the-markdown-kitchen-sink', /Markdown Kitchen Sink/i);
  const postPager = await page.locator('[data-testid="post-paginator"]').count();
  console.log('REQ-UI-045 post paginator present:', postPager);
  if (postPager) {
    const info = ((await page.locator('[data-testid="post-paginator-info"]').textContent()) || '').trim();
    console.log('REQ-UI-045 post paginator info:', info);
    expect(info, 'post paginator info blank').toMatch(/\d/);
  }

  // Every published post has a FeaturedImage in the database (psql: 8/8 non-empty), and home
  // renders it. The archives must render it too — a placeholder over a post that has an image is
  // RENDER-EMPTY, not a styling choice.
  const imgState: Record<string, { img: number; placeholder: number }> = {};
  for (const [name, route, scope] of surfaces) {
    await open(page, route);
    imgState[name] = {
      img: await page.locator(`${scope} [data-testid="post-card-image"]`).count(),
      placeholder: await page.locator(`${scope} [data-testid="post-card-image-placeholder"]`).count(),
    };
  }
  console.log('REQ-UI-045 PostCard image state', JSON.stringify(imgState));
  expect(imgState.category.img,
    `category archive renders ${imgState.category.placeholder} image PLACEHOLDERS and ${imgState.category.img} real images, though all 8 published posts have a FeaturedImage in the DB`)
    .toBeGreaterThan(0);
  expect(imgState.tag.img,
    `tag archive renders ${imgState.tag.placeholder} image PLACEHOLDERS and ${imgState.tag.img} real images, though all 8 published posts have a FeaturedImage in the DB`)
    .toBeGreaterThan(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-046 — RSS page + auto-discovery link
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-046 RSS page renders and the head advertises the feed', async ({ page }) => {
  test.setTimeout(180000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await open(page, '/rss', /RSS Feed/i);
  await noErrorBoundary(page);
  await styleProbe(page, '/rss');

  await expect(page.locator('[data-testid="rss-page"]')).toBeVisible();
  const url = await page.locator('[data-testid="rss-url"]').inputValue();
  console.log('REQ-UI-046 advertised feed URL:', url);
  expect(url, 'feed URL blank').toMatch(/^https?:\/\//);

  await expect(page.locator('[data-testid="rss-posts-empty"]')).toHaveCount(0);
  const recent = await page.locator('[data-testid="rss-recent-post-title"]').allTextContents();
  console.log('REQ-UI-046 recent posts preview', JSON.stringify(recent));
  expect(recent.length, 'RSS recent-posts preview empty while 8 posts are published').toBeGreaterThan(0);
  for (const t of recent) expect(t.trim().length).toBeGreaterThan(0);

  const v = await visualCheck(page, `${SHOT}/req-ui-046-rss-1280.png`, 1280);
  console.log('REQ-UI-046 visual', JSON.stringify({ o: v.overlaps, off: v.offViewport, h: v.hScroll }));
  expect(v.hScroll).toBeLessThanOrEqual(2);
  expect(v.overlaps).toHaveLength(0);

  // The advertised feed URL must actually serve a feed.
  const feed = await page.request.get(url, { failOnStatusCode: false });
  console.log(`REQ-UI-046 GET ${url} → ${feed.status()} ${feed.headers()['content-type'] ?? ''}`);

  // Auto-discovery <link rel="alternate" type="application/rss+xml"> in the head.
  const links = await page.evaluate(() =>
    Array.from(document.querySelectorAll('link[rel="alternate"]')).map((l) => ({
      type: l.getAttribute('type'), href: l.getAttribute('href'), title: l.getAttribute('title'),
    })));
  console.log('REQ-UI-046 head alternate links', JSON.stringify(links));

  expect(feed.status(), `advertised feed URL ${url} returned HTTP ${feed.status()} — nothing serves the feed`).toBeLessThan(400);
  expect(links.filter((l) => (l.type || '').includes('rss')).length,
    `no <link rel="alternate" type="application/rss+xml"> in <head> (found ${JSON.stringify(links)})`).toBeGreaterThan(0);
});

// ────────────────────────────────────────────────────────────────────────────
// REQ-UI-050 — no public login / admin entry points
// ────────────────────────────────────────────────────────────────────────────
test('REQ-UI-050 public chrome exposes no login or admin affordance', async ({ page }) => {
  test.setTimeout(480000);
  await page.setViewportSize({ width: 1280, height: 900 });
  const routes = ['/', '/about', '/categories', '/category/web-development', '/tags', '/tag/dotnet',
    '/series', '/series/blazor-server-in-production', '/search', '/rss', '/resume',
    '/post/blazor-render-modes-explained', '/newsletters'];

  const offenders: string[] = [];
  for (const route of routes) {
    await open(page, route);
    const found = await page.evaluate(() => {
      const bad: string[] = [];
      document.querySelectorAll('a[href]').forEach((a) => {
        const h = (a.getAttribute('href') || '').toLowerCase();
        if (h === '/login' || h.startsWith('/login?') || h === '/admin' || h.startsWith('/admin/') || h === '/register') {
          bad.push(`${h} :: "${(a.textContent || '').trim().slice(0, 40)}"`);
        }
      });
      const header = document.querySelector('[data-testid="public-header"]');
      if (header && /sign in|log in|login|my account|sign out/i.test((header as HTMLElement).innerText || '')) {
        bad.push('header text mentions sign-in');
      }
      document.querySelectorAll('[data-testid*="user-menu"], [data-testid*="login"], [data-testid*="signin"], [data-testid*="sign-in"]')
        .forEach((e) => bad.push(`testid ${e.getAttribute('data-testid')}`));
      return bad;
    });
    console.log(`REQ-UI-050 ${route}: ${found.length ? found.join(', ') : 'clean'}`);
    if (found.length) offenders.push(`${route}: ${found.join(', ')}`);
  }
  expect(offenders, `public pages exposing a login/admin affordance: ${offenders.join(' | ')}`).toHaveLength(0);

  // /login must still work by direct URL.
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('[data-testid="login-email"]')).toBeVisible({ timeout: 30000 });
  await expect(page.locator('[data-testid="login-submit"]')).toBeVisible();
  console.log('REQ-UI-050 /login still reachable by direct URL: yes');
});
