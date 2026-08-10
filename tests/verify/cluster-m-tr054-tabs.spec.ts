/*
  cluster-m-tr054-tabs.spec.ts — REQ-NFR-007 (WCAG 2.1 AA), TR-054 residual, 2026-08-09.

  Cluster L left the admin area at 48 axe violation nodes, ALL of them emitted by
  TrBlazeUI `Tabs`:
      44x aria-valid-attr-value  (22 per theme) — every inactive `TabsTrigger` names
                                  an `aria-controls` id that is not in the document,
                                  because only the ACTIVE `TabsContent` renders.
       4x aria-required-parent   ( 2 per theme) — /admin/newsletter's NESTED Tabs.

  This spec measures the same five routes with the same method cluster L used
  (@axe-core/playwright, tags wcag2a/2aa/21a/21aa, light AND dark, SPA navigation
  after a real sign-in), so the numbers are a delta and not a claim. Run it twice:

      TB_TAG=before   against the tree without the App.razor observer
      TB_TAG=after    with the observer shipped

  It ALSO proves the Tabs still work. Stripping a dangling `aria-controls` must not
  cost the control anything: tab switching, arrow/Home/End roving focus and the
  visible focus ring are all asserted after the sweep, on /settings and /admin/images.
  A fix that silences axe and breaks the widget is a failure, so those assertions are
  hard `expect`s rather than recordings.
*/
import { test, expect, Page } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5394';
const TAG = process.env.TB_TAG ?? 'after';
const OUT = path.join(process.cwd(), 'test-results-tr054');
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

/** The five admin routes TR-054 lands on, with cluster L's per-route node counts. */
const TAB_ROUTES = [
  { name: 'admin-images', url: '/admin/images', clusterLNodes: 7 },
  { name: 'admin-settings', url: '/settings', clusterLNodes: 5 },
  { name: 'admin-comments', url: '/comments', clusterLNodes: 4 },
  { name: 'admin-posts', url: '/BlogsList', clusterLNodes: 4 },
  { name: 'admin-newsletter', url: '/admin/newsletter', clusterLNodes: 4 },
];

fs.mkdirSync(OUT, { recursive: true });

/** Appends one JSON record to a per-run results file. */
function record(file: string, data: unknown) {
  fs.appendFileSync(path.join(OUT, `${file}-${TAG}.jsonl`), JSON.stringify(data) + '\n');
}

/** Forces light or dark before the document runs, the way a stored preference does. */
async function seedTheme(page: Page, dark: boolean) {
  await page.addInitScript((isDark: boolean) => {
    try {
      localStorage.setItem('techieblog-dark-mode', isDark ? 'true' : 'false');
    } catch { /* private mode — the run still measures the default theme */ }
  }, dark);
}

/** Signs in as the documented seeded site owner. Never creates an account. */
async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(2500);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2000);
}

/**
 * Navigates an authorised route through the SPA router.
 *
 * A full document load drops the resolved auth state while it rehydrates and the
 * router bounces to "/", so page.goto on an admin route silently audits home.
 */
async function spaNavigate(page: Page, url: string) {
  await page.evaluate((u: string) => (window as any).Blazor.navigateTo(u), url);
  await page.waitForTimeout(3000);
}

/** Waits until the prerender/interactive handover has settled. */
async function settle(page: Page) {
  await page.waitForFunction(
    () =>
      (window as any).Blazor !== undefined &&
      document.querySelectorAll('header').length <= 1 &&
      document.querySelectorAll('main').length <= 1,
    null,
    { timeout: 20000 }
  ).catch(() => { /* recorded either way */ });
  await page.waitForTimeout(2200);
}

/** Runs axe and returns a compact violation summary. */
async function axeScan(page: Page) {
  const results = await new AxeBuilder({ page }).withTags(TAGS).analyze();
  const nodes = results.violations.reduce((sum, v) => sum + v.nodes.length, 0);
  return {
    nodes,
    violations: results.violations.map(v => ({
      id: v.id,
      impact: v.impact,
      count: v.nodes.length,
      targets: v.nodes.slice(0, 4).map(n => n.target.join(' ')),
    })),
  };
}

/**
 * Reads the raw `aria-controls` state of every tab on the page.
 *
 * `dangling` is the population TR-054 is about: a trigger asserting control over an
 * id no element carries. `stripped` counts triggers with no `aria-controls` at all,
 * which is what the App.razor observer should leave behind.
 */
async function tabAriaState(page: Page) {
  return page.evaluate(() => {
    const tabs = Array.from(document.querySelectorAll('[role="tab"]'));
    let withControls = 0;
    let dangling = 0;
    let stripped = 0;
    const danglingIds: string[] = [];
    for (const t of tabs) {
      const id = t.getAttribute('aria-controls');
      if (!id) { stripped++; continue; }
      withControls++;
      if (!document.getElementById(id)) { dangling++; danglingIds.push(id); }
    }
    const orphanTabs = tabs.filter(t => !t.closest('[role="tablist"]')).length;
    return {
      tabs: tabs.length,
      withControls,
      dangling,
      stripped,
      orphanTabs,
      danglingIds: danglingIds.slice(0, 6),
      tabpanels: document.querySelectorAll('[role="tabpanel"]').length,
      // the observer's own bookkeeping — a parked value it can put back
      controlsParked: document.querySelectorAll('[data-a11y-controls-removed]').length,
      rolesParked: document.querySelectorAll('[data-a11y-role-removed]').length,
      // TR-063: does the selected state exist in a form ARIA can read?
      ariaSelectedTrue: document.querySelectorAll('[role="tab"][aria-selected="true"]').length,
      ariaSelectedFalse: document.querySelectorAll('[role="tab"][aria-selected="false"]').length,
      ariaSelectedEmpty: Array.from(document.querySelectorAll('[role="tab"]'))
        .filter(t => t.getAttribute('aria-selected') === '').length,
    };
  });
}

// ---------------------------------------------------------------------------
// 1. axe over the five tabbed admin routes, light and dark.
// ---------------------------------------------------------------------------
for (const dark of [false, true]) {
  const theme = dark ? 'dark' : 'light';

  test(`axe tabs admin ${theme}`, async ({ page }) => {
    test.setTimeout(600000);
    await page.setViewportSize({ width: 1280, height: 900 });
    await seedTheme(page, dark);
    await login(page);

    let total = 0;
    for (const route of TAB_ROUTES) {
      await spaNavigate(page, route.url);
      await settle(page);

      // A run that bounced is measuring the WRONG PAGE — to "/" when the auth state
      // has not rehydrated, and to "/change-password" when MustChangePassword is
      // re-armed mid-run by a sibling agent, which is exactly how eight admin audits
      // once reported a false clean pass. Assert the landing route BEFORE trusting it.
      const landedOn = new URL(page.url()).pathname.toLowerCase();
      expect(landedOn, `route ${route.url} bounced to ${landedOn}`).toBe(route.url.toLowerCase());

      const scan = await axeScan(page);
      const aria = await tabAriaState(page);
      total += scan.nodes;
      record('axe-tabs', { theme, route: route.name, landedOn, clusterLNodes: route.clusterLNodes, ...scan, aria });
      await page.screenshot({ path: path.join(OUT, `axe-${route.name}-${theme}-${TAG}.png`) });
      console.log(`[axe ${theme}] ${route.name}: ${scan.nodes} nodes  ${JSON.stringify(aria)}`);
    }
    record('axe-summary', { theme, totalNodes: total, routes: TAB_ROUTES.length });
    console.log(`[axe tabs admin ${theme}] total violation nodes: ${total}`);
  });
}

// ---------------------------------------------------------------------------
// 2. The Tabs must still WORK. Removing `aria-controls` removes a false claim; it
//    must not remove behaviour. Every assertion here is a hard failure.
// ---------------------------------------------------------------------------

/** True when the element paints a focus indicator (outline or ring box-shadow). */
async function hasFocusRing(page: Page) {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el) return { ringed: false, role: 'none', label: '' };
    const cs = getComputedStyle(el);
    return {
      ringed: (cs.outlineStyle !== 'none' && parseFloat(cs.outlineWidth) >= 1) || cs.boxShadow !== 'none',
      role: el.getAttribute('role') ?? el.tagName.toLowerCase(),
      label: (el.textContent ?? '').trim().slice(0, 30),
      outline: `${cs.outlineStyle} ${cs.outlineWidth} ${cs.outlineColor}`,
    };
  });
}

/**
 * Clicks through every tab on a route and asserts the matching panel really renders.
 *
 * The panel identity is read from its TEXT, not from `aria-controls` — the whole point
 * of the change under test is that the attribute may be absent, so a check that leaned
 * on it would pass for the wrong reason.
 */
async function exerciseTabs(page: Page, routeName: string) {
  const tabs = page.locator('[role="tab"]');
  const count = await tabs.count();
  expect(count, `${routeName}: expected a tablist`).toBeGreaterThan(1);

  const seenPanels: string[] = [];
  let ownsPanel = false;
  for (let i = 0; i < count; i++) {
    const tab = tabs.nth(i);
    const name = (await tab.innerText()).trim();
    await tab.click();
    await page.waitForTimeout(900);

    /*
      Selection is read from the library's OWN `data-state`, not from `aria-selected`.
      Both attributes are touched by the change under test — aria-selected is written by
      the observer — so leaning on it here would let the test pass for the wrong reason,
      and it does not exist in a usable form in the `before` run at all (the library emits
      aria-selected="", which is empty and invisible to AT).

      Three of the five routes render NO TabsContent — they drive the tabs themselves and
      paint the panel elsewhere on the page — so "the panel" is the page's main content
      when there is no tabs-content element to find.
    */
    const state = await page.evaluate(() => {
      const sel = document.querySelector('[role="tab"][data-state="active"]') as HTMLElement | null;
      const owned =
        (document.querySelector('[role="tabpanel"]') as HTMLElement | null) ??
        (document.querySelector('[data-slot="tabs-content"]') as HTMLElement | null);
      // On the self-driven screens the panel is the page body the tabs filter.
      const panel =
        owned ??
        (document.querySelector('[data-testid="media-library-page"]') as HTMLElement | null) ??
        (document.querySelector('main') as HTMLElement | null);
      const countLabel = document.querySelector('[data-testid="image-count"]') as HTMLElement | null;
      /*
        The media library's empty state NAMES the selected category ("Nothing has been
        uploaded to the Logos category."), so on a library with no images it is still a
        per-tab answer — and it is the answer a user sees. It is included in the
        signature so the assertion does not depend on the shared database holding
        content, which this one does not and which no smoke run may create.
      */
      const emptyState = document.querySelector('[data-testid="images-empty"]') as HTMLElement | null;
      return {
        selected: (sel?.textContent ?? '').trim(),
        selectedCount: document.querySelectorAll('[role="tab"][data-state="active"]').length,
        ariaSelected: sel?.getAttribute('aria-selected'),
        ownsPanel: !!owned,
        panelText: (panel?.innerText ?? '').replace(/\s+/g, ' ').trim().slice(0, 160),
        panelChars: (panel?.innerText ?? '').trim().length,
        panelVisible: !!panel && panel.getBoundingClientRect().height > 0,
        // the self-driven screens' observable answer to a tab click
        signature:
          `${(countLabel?.innerText ?? '').trim()}` +
          `|${document.querySelectorAll('[data-testid="image-card"]').length}` +
          `|${(emptyState?.innerText ?? '').replace(/\s+/g, ' ').trim()}`,
      };
    });

    // exactly one tab selected, and it is the one that was clicked
    expect(state.selectedCount, `${routeName}: tab ${i} selection count`).toBe(1);
    expect(state.selected, `${routeName}: tab ${i} "${name}" did not become selected`).toBe(name);
    // the panel for that tab actually rendered content
    expect(state.panelVisible, `${routeName}: tab ${i} "${name}" rendered no visible panel`).toBe(true);
    expect(state.panelChars, `${routeName}: tab ${i} "${name}" panel is empty`).toBeGreaterThan(0);

    seenPanels.push(state.ownsPanel ? state.panelText : state.signature);
    ownsPanel = state.ownsPanel;
    record('tabs-click', { route: routeName, index: i, name, ...state });
  }

  /*
    The click must change the CONTENT, not just the highlight. Where the library owns the
    panel (a real `TabsContent`, e.g. /settings) that is the panel's own text. Where the
    page drives the tabs itself and renders no TabsContent (e.g. /admin/images) the
    observable answer is the filtered result set — the count label plus the number of
    cards. A category that happens to be empty legitimately matches another empty one, so
    this asks for at least two distinct states across the whole tab set rather than a
    distinct state per tab.
  */
  const distinct = new Set(seenPanels).size;
  expect(distinct, `${routeName}: ${count} tabs produced only ${distinct} distinct content states`)
    .toBeGreaterThan(1);
  return { count, distinct, ownsPanel };
}

/** Arrow / Home / End roving focus over the tablist. */
async function exerciseTabKeyboard(page: Page, routeName: string) {
  const count = await page.locator('[role="tab"]').count();

  // Make the FIRST tab the active one, then arrive on it with the Tab key rather than
  // with .focus(). The library paints its ring through `focus-visible:ring-2`, and a
  // programmatic focus that follows a mouse click does not satisfy :focus-visible — so
  // a .focus() call would report "no focus ring" on a control that rings correctly for
  // the keyboard user this assertion is about. Only the active tab is in the tab order
  // (roving tabindex), so the traversal has exactly one tab stop to find.
  await page.locator('[role="tab"]').first().click();
  await page.waitForTimeout(800);
  await page.evaluate(() => (document.activeElement as HTMLElement)?.blur());
  await page.evaluate(() => window.scrollTo(0, 0));

  let arrived = false;
  for (let i = 0; i < 120 && !arrived; i++) {
    await page.keyboard.press('Tab');
    arrived = await page.evaluate(() => document.activeElement?.getAttribute('role') === 'tab');
  }
  expect(arrived, `${routeName}: no tab is reachable with the Tab key`).toBe(true);

  const firstRing = await hasFocusRing(page);
  expect(firstRing.ringed, `${routeName}: keyboard-focused tab has no visible focus indicator`).toBe(true);

  const read = () =>
    page.evaluate(() => {
      const el = document.activeElement as HTMLElement | null;
      const tabs = Array.from(document.querySelectorAll('[role="tab"]'));
      return {
        index: el ? tabs.indexOf(el) : -1,
        role: el?.getAttribute('role') ?? '',
        text: (el?.textContent ?? '').trim().slice(0, 30),
      };
    });

  const start = await read();
  expect(start.index, `${routeName}: first tab did not take focus`).toBe(0);

  await page.keyboard.press('ArrowRight');
  await page.waitForTimeout(400);
  const right = await read();

  await page.keyboard.press('End');
  await page.waitForTimeout(400);
  const end = await read();

  await page.keyboard.press('Home');
  await page.waitForTimeout(400);
  const home = await read();

  const ringAfter = await hasFocusRing(page);
  const result = { route: routeName, count, start, right, end, home, firstRing, ringAfter };
  record('tabs-keyboard', result);
  console.log(`[keyboard ${routeName}] ${JSON.stringify(result)}`);

  // Roving focus stays inside the tablist and moves.
  expect(right.role, `${routeName}: ArrowRight left the tablist`).toBe('tab');
  expect(right.index, `${routeName}: ArrowRight did not move focus`).toBe(1);
  expect(end.role, `${routeName}: End left the tablist`).toBe('tab');
  expect(end.index, `${routeName}: End did not reach the last tab`).toBe(count - 1);
  expect(home.role, `${routeName}: Home left the tablist`).toBe('tab');
  expect(home.index, `${routeName}: Home did not return to the first tab`).toBe(0);
  expect(ringAfter.ringed, `${routeName}: keyboard-focused tab has no visible focus indicator`).toBe(true);
}

test('tabs still work after the aria-controls strip', async ({ page }) => {
  test.setTimeout(900000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  for (const route of ['/settings', '/admin/images']) {
    await spaNavigate(page, route);
    await settle(page);
    const landedOn = new URL(page.url()).pathname.toLowerCase();
    expect(landedOn, `route ${route} bounced to ${landedOn}`).toBe(route.toLowerCase());

    const clicks = await exerciseTabs(page, route);
    await exerciseTabKeyboard(page, route);
    const aria = await tabAriaState(page);
    record('tabs-summary', { route, ...clicks, aria });
    console.log(`[tabs ${route}] ${JSON.stringify({ ...clicks, aria })}`);
    await page.screenshot({ path: path.join(OUT, `tabs-${route.replace(/\W+/g, '-')}-1280-${TAG}.png`) });
  }
});

// ---------------------------------------------------------------------------
// 2b. TR-063 — the selected state as CHROME sees it, not as the DOM spells it.
//
// axe cannot answer this: its aria-valid-attr-value rule skips empty values, so
// aria-selected="" passes every audit while exposing nothing. The browser's own
// accessibility tree is the only honest witness, so it is read over CDP.
// ---------------------------------------------------------------------------
test('selected state reaches the accessibility tree', async ({ page, context }) => {
  test.setTimeout(600000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  const cdp = await context.newCDPSession(page);
  await cdp.send('Accessibility.enable');

  for (const route of ['/settings', '/admin/images']) {
    await spaNavigate(page, route);
    await settle(page);
    expect(new URL(page.url()).pathname.toLowerCase()).toBe(route.toLowerCase());

    const tree: any = await cdp.send('Accessibility.getFullAXTree');
    const axTabs = (tree.nodes ?? [])
      .filter((n: any) => n.role?.value === 'tab')
      .map((n: any) => ({
        name: n.name?.value,
        selected: ((n.properties ?? []).find((p: any) => p.name === 'selected') ?? {}).value?.value,
      }));
    const selectedExposed = axTabs.filter((t: any) => t.selected === true).length;
    const stateExposed = axTabs.filter((t: any) => t.selected !== undefined).length;
    record('axtree', { route, tabs: axTabs.length, selectedExposed, stateExposed, axTabs });
    console.log(`[axtree ${route}] tabs=${axTabs.length} selected=${selectedExposed} stateExposed=${stateExposed}`);
  }
});

// ---------------------------------------------------------------------------
// 3. Visual check at 1280 and 390 — the strip is an attribute change, so nothing
//    should move; the captures exist to prove that.
// ---------------------------------------------------------------------------
test('visual tabs 1280 and 390', async ({ page }) => {
  test.setTimeout(900000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  for (const [w, h] of [[1280, 900], [390, 844]] as Array<[number, number]>) {
    await page.setViewportSize({ width: w, height: h });
    for (const route of TAB_ROUTES) {
      await spaNavigate(page, route.url);
      await settle(page);
      const landedOn = new URL(page.url()).pathname.toLowerCase();
      expect(landedOn, `route ${route.url} bounced to ${landedOn}`).toBe(route.url.toLowerCase());
      const layout = await page.evaluate(() => ({
        scrollWidth: document.documentElement.scrollWidth,
        clientWidth: document.documentElement.clientWidth,
        tabs: document.querySelectorAll('[role="tab"]').length,
        tablists: document.querySelectorAll('[role="tablist"]').length,
      }));
      record('visual', { width: w, route: route.name, ...layout });
      await page.screenshot({ path: path.join(OUT, `visual-${route.name}-${w}-${TAG}.png`) });
      expect(layout.scrollWidth, `${route.name}@${w}: horizontal overflow`).toBeLessThanOrEqual(layout.clientWidth);
      expect(layout.tabs, `${route.name}@${w}: tabs vanished`).toBeGreaterThan(0);
    }
  }
});
