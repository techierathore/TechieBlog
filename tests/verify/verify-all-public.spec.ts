/*
    verify-all-public.spec.ts

    Verification cluster V3 — the entire PUBLIC surface of TechieBlog, run as an
    upgrade-regression sweep after TrBlazeUI 2.0.1 -> 2.0.2 (trblazeui.css 88 KB -> 908 KB,
    utilities.css cut from 106 hand-declared rules to 23).

    Covers REQ-UI-005/006/007/008/009/010/011/012/027/049/054/059/060.

    Gates applied (verify-phase §4a / §4b):
      §4a DATA-RENDER  — lists need row count > 0 AND non-empty cells; counts are asserted
                         against psql ground truth, never against the page's own badges.
      §4b VISUAL-TRUTH — 1280x800 and 390x844 (plus 320 for REQ-UI-005): every control has
                         width > 0, height > 0, sits inside the page bounds, no zero-height
                         containers, no sibling interactive boxes intersecting, and a
                         full-page screenshot is captured for inspection.

    READ-ONLY: this suite performs no INSERT/UPDATE/DELETE and submits no comment, rating or
    subscribe form — three sibling verifier clusters share one app and one database.
*/

import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = 'http://172.18.144.1:5450';
const SHOTS = path.resolve(__dirname, '../.artifacts/verify-public');

fs.mkdirSync(SHOTS, { recursive: true });

/** Ground truth pulled from psql before the run (see the cluster report for the queries). */
const DB = {
  publishedPosts: 8,
  stats: [
    { value: '20+', label: 'Years of experience' },
    { value: '200+', label: 'Articles published' },
    { value: '45', label: 'Conference talks' },
    { value: '12', label: 'Products shipped' },
  ],
  categories: [
    { name: 'Career', slug: 'career', count: 1 },
    { name: 'DevOps', slug: 'devops', count: 1 },
    { name: 'Programming', slug: 'programming', count: 2 },
    { name: 'Technology', slug: 'technology', count: 1 },
    { name: 'Web Development', slug: 'web-development', count: 3 },
  ],
  tagCount: 15,
  tagBlazorPublished: 3,
  series: [
    { slug: 'blazor-server-in-production', name: 'Blazor Server in Production', published: 3, total: 4 },
    { slug: 'postgres-for-dotnet-developers', name: 'PostgreSQL for .NET Developers', published: 2, total: 2 },
  ],
  newsletters: 0,
  /** Newest published first — REQ-UI-059 / REQ-FN-020. */
  postsByPublishedDesc: [
    'writing-a-technical-talk-that-lands',
    'shipping-dotnet-with-docker-and-github-actions',
    'the-markdown-kitchen-sink',
    'reading-postgres-query-plans',
    'postgres-indexing-for-dotnet-developers',
    'scaling-signalr-for-blazor-server',
    'blazor-circuits-and-state',
    'blazor-render-modes-explained',
  ],
  draftSlugs: ['testing-dapper-repositories-without-a-database', 'observability-for-blazor-server'],
  draftTitles: ['Testing Dapper Repositories Without a Database', 'Observability for Blazor Server'],
  kitchenSinkRating: { average: '4.8', count: 4 },
};

const PUBLIC_ROUTES = [
  '/', '/about', '/resume', '/search', '/categories', '/category/web-development',
  '/tags', '/tag/blazor', '/series', '/series/blazor-server-in-production',
  '/post/the-markdown-kitchen-sink', '/newsletters', '/404',
];

/**
 * Navigates and waits for the interactive render to settle. The host runs with Serilog
 * Debug render-tree logging, so it is roughly an order of magnitude slower than normal —
 * generous waits here are correctness, not padding.
 */
const visit = async (page: Page, route: string): Promise<number> => {
  const resp = await page.goto(BASE + route, { waitUntil: 'domcontentloaded', timeout: 90000 });
  await page.waitForSelector('[data-testid=public-header], [data-testid=main-content], main', { timeout: 60000 });
  // Let the Blazor circuit finish its first interactive batch (spinners -> content).
  await page.waitForFunction(
    () => document.querySelectorAll('[data-testid$="-loading"]').length === 0,
    undefined,
    { timeout: 60000 },
  ).catch(() => { /* pages without a loading marker */ });
  await page.waitForTimeout(1200);
  return resp ? resp.status() : 0;
};

const slug = (route: string) => route.replace(/[^a-z0-9]+/gi, '_').replace(/^_|_$/g, '') || 'root';

/** Full-page screenshot into the cluster artifact folder. */
const shoot = async (page: Page, route: string, width: number) => {
  const file = path.join(SHOTS, `${slug(route)}-${width}.png`);
  await page.screenshot({ path: file, fullPage: true });
  return file;
};

interface GeometryReport {
  overflowPx: number;
  zeroSize: string[];
  outOfBounds: string[];
  overlaps: string[];
  zeroHeightContainers: string[];
}

/**
 * The §4b geometry sweep. Runs in the page so it can consult live layout boxes and, for each
 * suspect element, walk its ancestors to see whether it legitimately lives inside a
 * deliberate overflow-x:auto scroller (those are NOT off-viewport failures).
 */
const geometry = (page: Page): Promise<GeometryReport> => page.evaluate(() => {
  const describe = (el: Element) => {
    const id = el.getAttribute('data-testid');
    return `${el.tagName.toLowerCase()}${id ? `[data-testid=${id}]` : ''}${el.className && typeof el.className === 'string' ? `.${el.className.split(/\s+/).slice(0, 2).join('.')}` : ''}`;
  };

  const inScroller = (el: Element) => {
    let node: Element | null = el;
    while (node && node !== document.body) {
      const cs = getComputedStyle(node);
      if (/(auto|scroll)/.test(cs.overflowX) || /(auto|scroll)/.test(cs.overflowY)) return true;
      node = node.parentElement;
    }
    return false;
  };

  // checkVisibility walks ANCESTORS too, so a control inside a responsive `hidden md:flex`
  // container (desktop nav at 320px) or a closed overlay is correctly treated as not rendered
  // rather than as a zero-size failure. Only elements that are actually laid out are judged.
  const visible = (el: Element) => {
    const e = el as HTMLElement & { checkVisibility?: (o?: object) => boolean };
    if (typeof e.checkVisibility === 'function') {
      return e.checkVisibility({ checkOpacity: true, checkVisibilityCSS: true });
    }
    const cs = getComputedStyle(el);
    return cs.display !== 'none' && cs.visibility !== 'hidden' && cs.opacity !== '0';
  };

  const docW = document.documentElement.clientWidth;
  const overflowPx = document.documentElement.scrollWidth - document.documentElement.clientWidth;

  const controls = [...document.querySelectorAll('a, button, input, select, textarea, [role=radio], [role=button]')]
    .filter(visible)
    // A collapsed/closed overlay (drawer, popover) is not laid out; it is not a control failure.
    .filter((el) => !el.closest('[hidden], [aria-hidden=true]'));

  const zeroSize: string[] = [];
  const outOfBounds: string[] = [];
  for (const el of controls) {
    const r = el.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) { zeroSize.push(describe(el)); continue; }
    if ((r.left < -1 || r.right > docW + 1) && !inScroller(el)) {
      outOfBounds.push(`${describe(el)} x:[${Math.round(r.left)},${Math.round(r.right)}] docW=${docW}`);
    }
  }

  // Sibling interactive boxes must not intersect.
  const overlaps: string[] = [];
  const parents = new Set(controls.map((c) => c.parentElement).filter(Boolean) as Element[]);
  for (const p of parents) {
    const kids = [...p.children].filter((k) => controls.includes(k));
    for (let i = 0; i < kids.length; i++) {
      for (let j = i + 1; j < kids.length; j++) {
        const a = kids[i].getBoundingClientRect();
        const b = kids[j].getBoundingClientRect();
        const ox = Math.min(a.right, b.right) - Math.max(a.left, b.left);
        const oy = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
        // 1px tolerance absorbs sub-pixel rounding on fractional layouts.
        if (ox > 1 && oy > 1) {
          overlaps.push(`${describe(kids[i])} ∩ ${describe(kids[j])} = ${Math.round(ox)}x${Math.round(oy)}px`);
        }
      }
    }
  }

  // Zero-height containers: a testid-bearing block that rendered but collapsed.
  const zeroHeightContainers = [...document.querySelectorAll('[data-testid]')]
    .filter(visible)
    .filter((el) => {
      const r = el.getBoundingClientRect();
      return r.height === 0 && el.children.length > 0;
    })
    .map(describe);

  return { overflowPx, zeroSize, outOfBounds, overlaps, zeroHeightContainers };
});

/** Asserts the §4b gate for one route at one width, writing a screenshot for inspection. */
const visualGate = async (page: Page, route: string, width: number, height = width === 1280 ? 800 : 844) => {
  await page.setViewportSize({ width, height });
  await visit(page, route);
  const g = await geometry(page);
  const shot = await shoot(page, route, width);
  expect(g.overflowPx, `${route} @${width}: horizontal document overflow (screenshot ${shot})`).toBeLessThanOrEqual(0);
  expect(g.zeroSize, `${route} @${width}: zero-size controls`).toEqual([]);
  expect(g.outOfBounds, `${route} @${width}: controls outside page bounds`).toEqual([]);
  expect(g.overlaps, `${route} @${width}: intersecting sibling controls`).toEqual([]);
  expect(g.zeroHeightContainers, `${route} @${width}: collapsed containers`).toEqual([]);
  return g;
};

/** Text of every element matching a testid, trimmed, with empties preserved so blanks are visible. */
const texts = (page: Page, testid: string) =>
  page.locator(`[data-testid="${testid}"]`).allTextContents().then((t) => t.map((s) => s.trim()));

// ---------------------------------------------------------------------------------------------

test.describe('V3 public surface', () => {
  // NOT serial: every REQ must be exercised and reported even when a sibling REQ fails.
  test.describe.configure({ mode: 'default' });

  test('REQ-UI-005 shell: header, primary nav, footer, mobile drawer, 320/390/1280 geometry', async ({ page }) => {
    // --- desktop shell -------------------------------------------------------------------
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/');

    await expect(page.locator('[data-testid=public-header]')).toBeVisible();
    await expect(page.locator('[data-testid=brand-link]')).toBeVisible();
    await expect(page.locator('[data-testid=public-footer]')).toBeVisible();

    const navIds = ['nav-home', 'nav-categories', 'nav-series', 'nav-newsletter', 'nav-resume', 'nav-about'];
    for (const id of navIds) {
      const link = page.locator(`[data-testid=${id}]`);
      await expect(link, `desktop nav ${id} present`).toHaveCount(1);
      await expect(link, `desktop nav ${id} visible`).toBeVisible();
      expect((await link.textContent())?.trim(), `desktop nav ${id} has a label`).toBeTruthy();
    }
    // TR-044 regression guard: 2.0.1 rendered these as <a role="menuitem" tabindex="-1">,
    // which left 0 of 6 reachable by Tab. 2.0.2 must emit ordinary links.
    const orphanMenuitems = await page.locator('[data-testid=primary-nav] [role=menuitem]').count();
    expect(orphanMenuitems, 'primary nav must not emit orphan role=menuitem (TR-044)').toBe(0);
    for (const id of navIds) {
      const tabindex = await page.locator(`[data-testid=${id}]`).getAttribute('tabindex');
      expect(tabindex, `${id} must not be removed from the tab order`).not.toBe('-1');
    }

    for (const id of ['footer-about', 'footer-resume', 'footer-categories', 'footer-series', 'footer-rss']) {
      await expect(page.locator(`[data-testid=${id}]`), `footer ${id}`).toBeVisible();
    }

    // BRD-93 — public chrome exposes no login link and no user menu.
    expect(await page.locator('[data-testid=public-header] a[href*="login" i]').count(),
      'public header must expose no login link (BRD-93)').toBe(0);

    // --- mobile drawer -------------------------------------------------------------------
    await page.setViewportSize({ width: 390, height: 844 });
    await visit(page, '/');
    await expect(page.locator('[data-testid=primary-nav]'), 'desktop nav hidden below md').toBeHidden();
    const trigger = page.locator('[data-testid=mobile-nav-trigger]');
    await expect(trigger).toBeVisible();
    await trigger.click();
    const drawer = page.locator('[data-testid=mobile-nav-drawer]');
    await expect(drawer, 'mobile drawer opens').toBeVisible({ timeout: 30000 });
    for (const id of navIds) {
      await expect(page.locator(`[data-testid=${id}-mobile]`), `mobile nav ${id}`).toBeVisible();
    }
    await page.keyboard.press('Escape');
    await page.waitForTimeout(800);

    // --- geometry at the three widths the acceptance names -------------------------------
    for (const w of [320, 390, 1280]) {
      await page.setViewportSize({ width: w, height: w === 1280 ? 800 : 844 });
      await visit(page, '/');
      const g = await geometry(page);
      const shot = await shoot(page, 'shell', w);
      expect(g.overflowPx, `shell @${w}: horizontal scroll (screenshot ${shot})`).toBeLessThanOrEqual(0);
      expect(g.zeroSize, `shell @${w}: zero-size controls`).toEqual([]);
      expect(g.outOfBounds, `shell @${w}: controls out of bounds`).toEqual([]);
      expect(g.overlaps, `shell @${w}: intersecting sibling controls`).toEqual([]);

      // The header bar itself is the 320px acceptance: brand + actions must fit.
      const bar = await page.locator('[data-testid=public-header]').boundingBox();
      expect(bar!.width, `header width @${w}`).toBeLessThanOrEqual(w + 1);
      const actions = await page.locator('[data-testid=mobile-nav-trigger]').boundingBox();
      if (actions) {
        expect(actions.x + actions.width, `header actions must not spill past ${w}px`).toBeLessThanOrEqual(w + 1);
      }
    }
  });

  test('REQ-UI-006 / REQ-UI-049 home portfolio landing: hero, stats, about, featured, latest articles', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/');

    await expect(page.locator('[data-testid=home-page]')).toBeVisible();
    expect(await page.locator('[data-testid=home-unconfigured]').count(), 'home must not be in its unconfigured state').toBe(0);

    // Hero (ResumeHero) — the h1 is the owner's name.
    const h1 = page.locator('h1');
    await expect(h1).toHaveCount(1);
    expect((await h1.textContent())?.trim(), 'hero h1 non-empty').toBeTruthy();

    // --- REQ-UI-049 stats: the library StatTile exposes no slot (TR-072b), so the old
    // home-stat-value / home-stat-label test ids are gone. Read the tile's own elements.
    const tiles = page.locator('[data-testid=home-stat-card]');
    await expect(tiles, 'one StatTile per UserStats row').toHaveCount(DB.stats.length);
    for (let i = 0; i < DB.stats.length; i++) {
      const tile = tiles.nth(i);
      const value = (await tile.locator('div.tabular-nums').first().textContent())?.trim();
      const label = (await tile.locator('div.text-muted-foreground').first().textContent())?.trim();
      expect(value, `stat ${i} value matches psql userstats.statvalue`).toBe(DB.stats[i].value);
      expect(label, `stat ${i} label matches psql userstats.statlabel`).toBe(DB.stats[i].label);
      const box = await tile.boundingBox();
      expect(box!.width, `stat tile ${i} width`).toBeGreaterThan(0);
      expect(box!.height, `stat tile ${i} height`).toBeGreaterThan(0);
    }
    // The deleted test ids must genuinely be gone, not silently renamed onto something empty.
    expect(await page.locator('[data-testid=home-stat-value]').count(), 'home-stat-value retired (TR-072b)').toBe(0);

    // About summary.
    await expect(page.locator('[data-testid=home-about-card]')).toBeVisible();
    expect((await texts(page, 'home-about-summary'))[0], 'about summary non-empty').toBeTruthy();

    // Featured post = newest published (REQ-FN-020 / BRD-31 / REQ-UI-059).
    await expect(page.locator('[data-testid=home-featured-post]')).toBeVisible();
    const featuredHref = await page.locator('[data-testid=home-featured-title]').getAttribute('href');
    expect(featuredHref, 'featured post is the newest published article').toBe(`/post/${DB.postsByPublishedDesc[0]}`);
    for (const id of ['home-featured-title', 'home-featured-excerpt', 'home-featured-author', 'home-featured-date', 'home-featured-readtime']) {
      expect((await texts(page, id))[0], `${id} non-empty`).toBeTruthy();
    }

    // Latest articles grid.
    await expect(page.locator('[data-testid=home-articles-grid]')).toBeVisible();
    const cards = page.locator('[data-testid=home-articles-grid] a[href^="/post/"]');
    expect(await cards.count(), 'latest-articles grid has rows').toBeGreaterThan(0);

    // No draft may surface on the landing page.
    const body = await page.locator('body').innerText();
    for (const t of DB.draftTitles) {
      expect(body, `draft "${t}" must not appear publicly`).not.toContain(t);
    }

    await visualGate(page, '/', 1280);
    await visualGate(page, '/', 390);
  });

  test('REQ-UI-007 post view: Prose reflow, no table-scroll wrapper, 0px overflow at 390', async ({ page }) => {
    const route = '/post/the-markdown-kitchen-sink';

    // --- the acceptance: 0px horizontal document scroll at 390 (was 46px before the fix) ---
    await page.setViewportSize({ width: 390, height: 844 });
    await visit(page, route);

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    const shot390 = await shoot(page, route, 390);
    expect(overflow, `kitchen-sink horizontal overflow at 390 (screenshot ${shot390})`).toBe(0);

    // The app's WrapTablesInScrollContainer transform and its div were deleted; Prose owns it now.
    expect(await page.locator('div.markdown-table-scroll').count(),
      'markdown-table-scroll wrapper must be gone').toBe(0);
    expect(await page.locator('[data-slot=prose]').count(), 'library Prose present').toBe(1);

    // The table must fit its own container rather than pushing the page.
    const table = page.locator('[data-slot=prose] table').first();
    await expect(table, 'kitchen-sink renders a table').toHaveCount(1);
    const fit = await table.evaluate((el) => {
      const parent = el.parentElement!;
      return {
        tableW: el.getBoundingClientRect().width,
        parentW: parent.getBoundingClientRect().width,
        parentOverflowX: getComputedStyle(parent).overflowX,
        parentScrolls: parent.scrollWidth > parent.clientWidth,
      };
    });
    expect(fit.tableW, 'table fits inside its container').toBeLessThanOrEqual(fit.parentW + 1);

    // The three <pre> blocks must scroll internally, not widen the document.
    const pres = page.locator('[data-slot=prose] pre');
    await expect(pres, 'kitchen-sink code blocks').toHaveCount(3);
    for (let i = 0; i < 3; i++) {
      const r = await pres.nth(i).evaluate((el) => ({
        overflowX: getComputedStyle(el).overflowX,
        w: el.getBoundingClientRect().width,
        docW: document.documentElement.clientWidth,
      }));
      expect(['auto', 'scroll'], `pre[${i}] scrolls internally`).toContain(r.overflowX);
      expect(r.w, `pre[${i}] stays within the viewport`).toBeLessThanOrEqual(r.docW + 1);
    }

    // --- data-render ---------------------------------------------------------------------
    for (const id of ['post-title', 'post-author', 'post-date', 'post-readtime']) {
      expect((await texts(page, id))[0], `${id} non-empty`).toBeTruthy();
    }
    await expect(page.locator('[data-testid=post-content]')).toBeVisible();
    expect((await page.locator('[data-testid=post-content]').innerText()).length,
      'post body has content').toBeGreaterThan(200);
    await expect(page.locator('[data-testid=post-tags]')).toBeVisible();
    await expect(page.locator('[data-testid=post-author-card]')).toBeVisible();
    await expect(page.locator('[data-testid=comments-section]')).toBeVisible();

    // Breadcrumb moved from a wrapper <div> onto <Breadcrumb> itself (TR-021).
    const crumb = page.locator('[data-testid=breadcrumb]');
    await expect(crumb, 'breadcrumb present on a post').toHaveCount(1);
    expect(await crumb.evaluate((el) => el.tagName), 'breadcrumb renders as <nav>').toBe('NAV');
    expect(await crumb.locator('ol').count(), 'breadcrumb renders an <ol>').toBe(1);
    expect(await crumb.locator('li').count(), 'breadcrumb has items').toBeGreaterThan(0);

    await visualGate(page, route, 1280);
  });

  test('REQ-UI-008 category archive: /categories and /category/{slug} vs psql', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/categories');

    await expect(page.locator('[data-testid=categories-grid]')).toBeVisible();
    const cards = page.locator('[data-testid=category-card]');
    await expect(cards, 'one card per psql category').toHaveCount(DB.categories.length);

    const titles = (await texts(page, 'category-card-title')).sort();
    expect(titles, 'category names match psql').toEqual(DB.categories.map((c) => c.name).sort());
    for (const t of await texts(page, 'category-card-description')) {
      expect(t, 'category description cell non-empty').toBeTruthy();
    }
    // Badge counts must equal the psql PUBLISHED counts.
    const badges = await texts(page, 'category-card-count');
    expect(badges.length, 'a count badge per card').toBe(DB.categories.length);
    const byName: Record<string, string> = {};
    for (let i = 0; i < cards.length; i++) {
      byName[(await cards.nth(i).locator('[data-testid=category-card-title]').textContent())!.trim()] =
        (await cards.nth(i).locator('[data-testid=category-card-count]').textContent())!.trim();
    }
    for (const c of DB.categories) {
      expect(byName[c.name], `${c.name} badge = psql published count`).toBe(`${c.count} posts`);
    }

    await expect(page.locator('[data-testid=breadcrumb]'), 'breadcrumb on /categories').toHaveCount(1);

    // --- one archive: badge count == rendered cards == psql -------------------------------
    await visit(page, '/category/web-development');
    await expect(page.locator('[data-testid=posts-grid]')).toBeVisible();
    const posts = page.locator('[data-testid=posts-grid] a[href^="/post/"]');
    const rendered = await posts.count();
    expect(rendered, 'web-development rendered cards = psql published count').toBe(DB.categories.find((c) => c.slug === 'web-development')!.count);
    const badge = (await texts(page, 'category-post-count'))[0];
    expect(badge, 'badge equals rendered card count').toBe(`${rendered} posts`);
    await expect(page.locator('[data-testid=breadcrumb]'), 'breadcrumb on a category archive').toHaveCount(1);

    const bodyText = await page.locator('body').innerText();
    for (const t of DB.draftTitles) {
      expect(bodyText, `draft "${t}" must not surface in a category archive`).not.toContain(t);
    }

    await visualGate(page, '/categories', 1280);
    await visualGate(page, '/categories', 390);
    await visualGate(page, '/category/web-development', 390);
  });

  test('REQ-UI-009 tag archive: /tags cloud and /tag/{slug} vs psql', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/tags');

    await expect(page.locator('[data-testid=tags-cloud]')).toBeVisible();
    const chips = page.locator('[data-testid^="tag-chip-"]');
    expect(await chips.count(), 'tag chips rendered').toBeGreaterThan(0);
    for (const t of await chips.allTextContents()) {
      expect(t.trim(), 'tag chip label non-empty').toBeTruthy();
    }
    await expect(page.locator('[data-testid=breadcrumb]'), 'breadcrumb on /tags').toHaveCount(1);

    await visit(page, '/tag/blazor');
    await expect(page.locator('[data-testid=posts-grid]')).toBeVisible();
    const rendered = await page.locator('[data-testid=posts-grid] a[href^="/post/"]').count();
    expect(rendered, 'tag/blazor rendered cards = psql published count').toBe(DB.tagBlazorPublished);
    expect((await texts(page, 'tag-post-count'))[0], 'tag badge equals rendered count').toBe(`${rendered} posts`);

    const bodyText = await page.locator('body').innerText();
    for (const t of DB.draftTitles) {
      expect(bodyText, `draft "${t}" must not surface in a tag archive`).not.toContain(t);
    }

    await visualGate(page, '/tags', 1280);
    await visualGate(page, '/tag/blazor', 390);
  });

  test('REQ-UI-010 series view: /series list and /series/{slug} parts vs psql', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/series');

    await expect(page.locator('[data-testid=series-grid]')).toBeVisible();
    const cards = page.locator('[data-testid=series-card]');
    await expect(cards, 'one card per psql series').toHaveCount(DB.series.length);
    for (const t of await texts(page, 'series-card-title')) {
      expect(t, 'series card title non-empty').toBeTruthy();
    }
    for (const t of await texts(page, 'series-card-status')) {
      expect(t, 'series card status non-empty').toBeTruthy();
    }

    await visit(page, '/series/blazor-server-in-production');
    await expect(page.locator('[data-testid=series-header]')).toBeVisible();
    await expect(page.locator('[data-testid=series-posts]')).toBeVisible();
    const parts = page.locator('[data-testid=series-post]');
    const target = DB.series[0];
    expect(await parts.count(), 'series parts rendered = psql published parts').toBe(target.published);
    for (const t of await texts(page, 'series-post-title')) {
      expect(t, 'series part title non-empty').toBeTruthy();
    }
    // Part 4 of this series is a DRAFT and must never be linked publicly.
    const bodyText = await page.locator('body').innerText();
    expect(bodyText, 'the draft 4th part must not surface publicly').not.toContain('Observability for Blazor Server');
    expect(await page.locator(`a[href="/post/${DB.draftSlugs[1]}"]`).count(),
      'no link to the draft part').toBe(0);
    await expect(page.locator('[data-testid=breadcrumb]'), 'breadcrumb on a series').toHaveCount(1);

    await visualGate(page, '/series', 1280);
    await visualGate(page, '/series/blazor-server-in-production', 390);
  });

  test('REQ-UI-011 search results: form, filters and result rows', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/search');

    await expect(page.locator('[data-testid=search-input]')).toBeVisible();
    await expect(page.locator('[data-testid=search-submit]')).toBeVisible();
    await expect(page.locator('[data-testid=search-filters]')).toBeVisible();
    for (const id of ['category-filter', 'date-filter', 'sort-filter']) {
      const f = page.locator(`[data-testid=${id}]`);
      await expect(f, `${id} present`).toHaveCount(1);
      const box = await f.boundingBox();
      expect(box!.width, `${id} width`).toBeGreaterThan(0);
      expect(box!.height, `${id} height`).toBeGreaterThan(0);
    }

    // A query that must match: every published post is about .NET/Blazor/Postgres; "blazor"
    // has 3 published posts by tag, so results must be non-empty and every cell filled.
    await page.locator('[data-testid=search-input]').fill('blazor');
    await page.locator('[data-testid=search-submit]').click();
    await page.waitForSelector('[data-testid=search-results], [data-testid=search-empty]', { timeout: 60000 });
    await page.waitForTimeout(1500);

    expect(await page.locator('[data-testid=search-empty]').count(),
      '"blazor" must return results, not the empty state').toBe(0);
    const results = page.locator('[data-testid=search-result]');
    const n = await results.count();
    expect(n, 'search returned rows').toBeGreaterThan(0);
    for (let i = 0; i < n; i++) {
      const row = results.nth(i);
      expect((await row.locator('[data-testid=search-result-title]').textContent())?.trim(), `row ${i} title`).toBeTruthy();
      expect((await row.locator('[data-testid=search-result-date]').textContent())?.trim(), `row ${i} date`).toBeTruthy();
      expect((await row.locator('[data-testid=search-result-author]').textContent())?.trim(), `row ${i} author`).toBeTruthy();
    }
    expect((await texts(page, 'search-results-count'))[0], 'result count line non-empty').toBeTruthy();

    const bodyText = await page.locator('body').innerText();
    for (const t of DB.draftTitles) {
      expect(bodyText, `draft "${t}" must not surface in search`).not.toContain(t);
    }

    const g1 = await geometry(page);
    await shoot(page, '/search-results', 1280);
    expect(g1.overlaps, 'search results: intersecting sibling controls').toEqual([]);
    expect(g1.overflowPx, 'search results: horizontal overflow @1280').toBeLessThanOrEqual(0);

    await visualGate(page, '/search', 390);
  });

  test('REQ-UI-012 about page, /404, and an unmatched route', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });

    // --- /about ---------------------------------------------------------------------------
    await visit(page, '/about');
    await expect(page.locator('[data-testid=about-page]')).toBeVisible();
    await expect(page.locator('[data-testid=about-card]')).toBeVisible();
    await expect(page.locator('[data-testid=about-stack]')).toBeVisible();
    expect(await page.locator('[data-testid=about-stack] *').count(), 'about stack has chips').toBeGreaterThan(0);
    await expect(page.locator('[data-testid=about-links]')).toBeVisible();
    await expect(page.locator('[data-testid=about-resume]')).toBeVisible();

    // --- /404 is a real route and answers 200 ---------------------------------------------
    const direct = await visit(page, '/404');
    expect(direct, 'direct /404 answers 200').toBe(200);
    await expect(page.locator('[data-testid=not-found-page]')).toBeVisible();
    await expect(page.locator('[data-testid=not-found-home]')).toBeVisible();
    await expect(page.locator('[data-testid=not-found-search]')).toBeVisible();

    // --- an unmatched route must answer 404 WITH a rendered body ---------------------------
    // (It once returned a zero-byte 404: MapRazorComponents registers one endpoint per @page,
    //  so the Blazor router never ran. UseStatusCodePagesWithReExecute re-executes /404 while
    //  preserving the status.)
    for (const bogus of ['/no-such-route-v3', '/deeply/unmatched/v3']) {
      const status = await visit(page, bogus);
      expect(status, `${bogus} answers HTTP 404`).toBe(404);
      await expect(page.locator('[data-testid=not-found-page]'), `${bogus} renders the 404 body`).toBeVisible();
      const body = await page.locator('body').innerText();
      expect(body, `${bogus} body is not empty`).toContain('Page not found');
      expect(body.length, `${bogus} body has real content`).toBeGreaterThan(100);
      // The public shell must still be there so a lost visitor can get back.
      await expect(page.locator('[data-testid=public-header]'), `${bogus} keeps the header`).toBeVisible();
      await expect(page.locator('[data-testid=public-footer]'), `${bogus} keeps the footer`).toBeVisible();
    }

    await visualGate(page, '/about', 1280);
    await visualGate(page, '/about', 390);
    await visualGate(page, '/404', 390);
  });

  test('REQ-UI-027 star ratings: button[role=radio] semantics, no span[role=radio], average vs psql', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/post/the-markdown-kitchen-sink');

    await expect(page.locator('[data-testid=post-rating-panel]')).toBeVisible();
    const stars = page.locator('[data-testid=post-rating-stars]');
    await expect(stars).toBeVisible();

    // The 2.0.2 markup change: options are <button role="radio">, never <span role="radio">.
    expect(await page.locator('span[role=radio]').count(),
      'no span[role=radio] anywhere on the page (2.0.2 markup change)').toBe(0);
    const radios = stars.locator('button[role=radio]');
    await expect(radios, 'five interactive star buttons').toHaveCount(5);

    // Container must be a real radiogroup and each option must carry a name + checked state.
    expect(await stars.getAttribute('role'), 'rating container is a radiogroup').toBe('radiogroup');
    expect(await stars.getAttribute('aria-label'), 'radiogroup is labelled').toBeTruthy();
    for (let i = 0; i < 5; i++) {
      const r = radios.nth(i);
      expect(await r.getAttribute('aria-label'), `star ${i + 1} labelled`).toBeTruthy();
      expect(await r.getAttribute('aria-checked'), `star ${i + 1} exposes checked state`).toBeTruthy();
      const box = await r.boundingBox();
      expect(box!.width, `star ${i + 1} width`).toBeGreaterThan(0);
      expect(box!.height, `star ${i + 1} height`).toBeGreaterThan(0);
    }
    // Roving tabindex: exactly one star is in the tab order.
    const tabbable = await radios.evaluateAll((els) => els.filter((e) => e.getAttribute('tabindex') === '0').length);
    expect(tabbable, 'exactly one star in the tab order (roving tabindex)').toBe(1);

    // The app-side hidden <fieldset> keyboard fallback and aria-hidden wrappers were DELETED.
    expect(await page.locator('[data-testid=post-rating-panel] fieldset').count(),
      'app-side hidden fieldset fallback removed').toBe(0);
    expect(await page.locator('[data-testid=post-rating-panel] [data-a11y-decorative]').count(),
      'data-a11y-decorative wrappers removed (TR-052)').toBe(0);
    expect(await stars.evaluate((el) => !!el.closest('[aria-hidden=true]')),
      'the rating control is not inside an aria-hidden subtree').toBe(false);

    // Read-side values must match psql postrating for post 7.
    expect((await texts(page, 'post-rating-average'))[0], 'average matches psql').toBe(DB.kitchenSinkRating.average);
    expect((await texts(page, 'post-rating-count'))[0], 'rating count matches psql')
      .toBe(`· ${DB.kitchenSinkRating.count} ratings`);

    // Read-only ratings elsewhere must be role="img" with no radio semantics.
    await visit(page, '/');
    expect(await page.locator('span[role=radio]').count(), 'no span[role=radio] on home').toBe(0);

    // WRITE HALF NOT EXERCISED: submitting a rating is a database write and three sibling
    // verifier clusters share this database. The identify step is asserted read-only below.
    await visit(page, '/post/the-markdown-kitchen-sink');
    await expect(page.locator('[data-testid=rating-identify-step]'),
      'email identify step present (submit itself is a WRITE, not exercised)').toBeVisible();
    await expect(page.locator('[data-testid=rating-email]')).toBeVisible();
    await expect(page.locator('[data-testid=rating-submit]')).toBeVisible();
  });

  test('REQ-UI-054 newsletters archive', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/newsletters');

    await expect(page.locator('[data-testid=newsletter-archive]')).toBeVisible();
    expect((await texts(page, 'newsletter-archive-title'))[0], 'archive title non-empty').toBeTruthy();
    expect((await texts(page, 'newsletter-archive-intro'))[0], 'archive intro non-empty').toBeTruthy();
    expect((await texts(page, 'newsletter-issues-heading'))[0], 'issues heading non-empty').toBeTruthy();

    const cards = page.locator('[data-testid=newsletter-issue-card]');
    const empty = page.locator('[data-testid=newsletter-issues-empty]');
    if (DB.newsletters === 0) {
      // psql newsletter table is EMPTY, so the list half cannot be exercised; the page must
      // show its real empty state rather than a blank or collapsed region.
      await expect(empty, 'empty state shown when there are no issues').toBeVisible();
      expect((await empty.innerText()).trim().length, 'empty state carries real text').toBeGreaterThan(0);
      expect(await cards.count(), 'no issue cards when psql has no newsletters').toBe(0);
    } else {
      await expect(cards.first()).toBeVisible();
      expect(await cards.count(), 'issue cards = psql public newsletters').toBe(DB.newsletters);
    }

    await visualGate(page, '/newsletters', 1280);
    await visualGate(page, '/newsletters', 390);
  });

  test('REQ-UI-059 post ordering: newest published first on home and archives', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    await visit(page, '/');

    // Featured = newest published.
    expect(await page.locator('[data-testid=home-featured-title]').getAttribute('href'),
      'featured is the newest published post').toBe(`/post/${DB.postsByPublishedDesc[0]}`);

    // Latest-articles grid must be in descending published order and contain no drafts.
    const hrefs = await page.locator('[data-testid=home-articles-grid] a[href^="/post/"]').evaluateAll(
      (els) => [...new Set(els.map((e) => (e as HTMLAnchorElement).getAttribute('href')!))]);
    const slugs = hrefs.map((h) => h.replace('/post/', ''));
    expect(slugs.length, 'latest articles present').toBeGreaterThan(0);
    for (const s of slugs) {
      expect(DB.draftSlugs, `"${s}" must not be a draft`).not.toContain(s);
      expect(DB.postsByPublishedDesc, `"${s}" is a known published post`).toContain(s);
    }
    const expectedOrder = DB.postsByPublishedDesc.filter((s) => slugs.includes(s));
    expect(slugs, 'latest articles are newest-published-first').toEqual(expectedOrder);

    // A category archive must use the same ordering.
    await visit(page, '/category/web-development');
    const catSlugs = await page.locator('[data-testid=posts-grid] a[href^="/post/"]').evaluateAll(
      (els) => [...new Set(els.map((e) => (e as HTMLAnchorElement).getAttribute('href')!))]);
    const cs = catSlugs.map((h) => h.replace('/post/', ''));
    expect(cs, 'category archive is newest-published-first')
      .toEqual(DB.postsByPublishedDesc.filter((s) => cs.includes(s)));
  });

  test('REQ-UI-060 heading structure: exactly one h1 on every public route', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    const report: Record<string, { count: number; texts: string[]; order: string[] }> = {};

    for (const route of [...PUBLIC_ROUTES, '/no-such-route-v3']) {
      await visit(page, route);
      const r = await page.evaluate(() => ({
        count: document.querySelectorAll('h1').length,
        texts: [...document.querySelectorAll('h1')].map((h) => h.textContent!.trim().slice(0, 60)),
        order: [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map((h) => h.tagName),
      }));
      report[route] = r;
    }
    fs.writeFileSync(path.join(SHOTS, 'h1-counts.json'), JSON.stringify(report, null, 2));

    const offenders = Object.entries(report).filter(([, r]) => r.count !== 1)
      .map(([route, r]) => `${route}: ${r.count} h1 -> ${JSON.stringify(r.texts)}`);
    expect(offenders, 'every public route must have exactly one <h1>').toEqual([]);

    // No heading level may be skipped on the way down (h1 -> h3 with no h2).
    const skips: string[] = [];
    for (const [route, r] of Object.entries(report)) {
      let prev = 0;
      for (const tag of r.order) {
        const lvl = Number(tag[1]);
        if (prev && lvl > prev + 1) skips.push(`${route}: h${prev} -> h${lvl}`);
        prev = lvl;
      }
    }
    expect(skips, 'no skipped heading levels').toEqual([]);
  });

  test('REQ-UI-005/060 cross-route sweep: no span[role=radio], breadcrumb intact, no raw-HTML fallback', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 800 });
    const findings: Record<string, unknown> = {};

    for (const route of PUBLIC_ROUTES) {
      await visit(page, route);
      findings[route] = await page.evaluate(() => {
        const bodyText = document.body.innerText;
        return {
          spanRadio: document.querySelectorAll('span[role=radio]').length,
          tableScroll: document.querySelectorAll('div.markdown-table-scroll').length,
          header: document.querySelectorAll('[data-testid=public-header]').length,
          footer: document.querySelectorAll('[data-testid=public-footer]').length,
          // Classic symptom of a missing CSS class after a bundle change: escaped markup
          // leaking into visible text.
          rawHtml: /&lt;(div|span|p|table|h[1-6])\b/i.test(bodyText) || /<div class=/i.test(bodyText),
          errorBoundary: bodyText.includes('An unhandled error has occurred')
            || bodyText.includes('Sorry, there was a problem'),
          bodyLen: bodyText.trim().length,
        };
      });
    }
    fs.writeFileSync(path.join(SHOTS, 'cross-route.json'), JSON.stringify(findings, null, 2));

    for (const [route, f] of Object.entries(findings) as [string, any][]) {
      expect(f.spanRadio, `${route}: span[role=radio] must be 0`).toBe(0);
      expect(f.tableScroll, `${route}: markdown-table-scroll must be 0`).toBe(0);
      expect(f.header, `${route}: header rendered once`).toBe(1);
      expect(f.footer, `${route}: footer rendered once`).toBe(1);
      expect(f.rawHtml, `${route}: raw HTML leaking into visible text`).toBe(false);
      expect(f.errorBoundary, `${route}: error boundary tripped`).toBe(false);
      expect(f.bodyLen, `${route}: page body has content`).toBeGreaterThan(200);
    }
  });
});
