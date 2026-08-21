/**
 * vall-resume-helpers.ts — shared plumbing for the three `vall-resume*.spec.ts` files.
 *
 * Deliberately NOT a `.spec.ts`: Playwright collects every test in an imported spec file, so
 * exporting these from the spec made a single-file run execute the whole cluster.
 */
import { expect, Page } from '@playwright/test';
import { execFileSync } from 'child_process';
import { BASE, USERS, visualCheck, ControlResult, VisualResult } from './_gates';

const SHOTS = '.verify/shots/resume';

/** Runs a read-only (or restorative) statement inside the shared WinPostgre container. */
export function psql(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'psql', '-U', 'PgVectorAdmin', '-d', 'TechieBlog', '-tAc', sql],
    { encoding: 'utf8' },
  ).trim();
}

/**
 * Runs a statement that is EXPECTED to fail and returns whatever psql said. The failure is the
 * evidence, so stderr is folded into stdout inside the container and the exit code is neutralised.
 */
export function psqlExpectError(sql: string): string {
  return execFileSync(
    'docker',
    ['exec', 'WinPostgre', 'sh', '-c', `psql -U PgVectorAdmin -d TechieBlog -c "${sql}" 2>&1 || true`],
    { encoding: 'utf8' },
  ).trim();
}

/** Reports a control map row-by-row so the §4a verdict is legible in the run log. */
export function report(screen: string, controls: ControlResult[], visuals: VisualResult[]) {
  console.log(`\n### DEVGUIDE ${screen}`);
  for (const c of controls) console.log(`  [${c.verdict}] ${c.control} — ${c.detail}`);
  for (const v of visuals) {
    console.log(
      `  VISUAL@${v.width}: overlaps=${JSON.stringify(v.overlaps)} zero=${JSON.stringify(v.zeroSized)} ` +
        `off=${JSON.stringify(v.offViewport)} hScroll=${v.hScroll} consoleErrors=${JSON.stringify(v.consoleErrors)} ` +
        `shot=${v.screenshot}`,
    );
  }
}

/**
 * Names of controls that sit inside a deliberate horizontal scroll container (the resume section
 * nav is `overflow-x: auto` by design). Such a control extending past the viewport is the design
 * working, not a clipped control, so it is exempt from the off-viewport rule — the page body's
 * own `hScroll` still has to be zero.
 */
async function scrollExempt(page: Page): Promise<Set<string>> {
  return new Set(
    await page.evaluate(() => {
      const named = (e: Element) =>
        e.getAttribute('data-testid') ||
        `${e.tagName.toLowerCase()}${e.className && typeof e.className === 'string' ? '.' + e.className.split(' ')[0] : ''}`;
      const out: string[] = [];
      for (const e of Array.from(document.querySelectorAll('[data-testid]'))) {
        let n: Element | null = e.parentElement;
        while (n && n !== document.documentElement) {
          const s = getComputedStyle(n);
          if (/(auto|scroll)/.test(s.overflowX) && n.scrollWidth > n.clientWidth + 1) {
            out.push(named(e));
            break;
          }
          n = n.parentElement;
        }
      }
      return out;
    }),
  );
}

/** Asserts the §4b geometry gate: nothing overlapping, clipped, offscreen or erroring. */
export function expectVisualClean(v: VisualResult) {
  expect(v.overlaps, `overlapping siblings at ${v.width}px`).toEqual([]);
  expect(v.zeroSized, `zero-sized controls at ${v.width}px`).toEqual([]);
  expect(v.offViewport, `off-viewport controls at ${v.width}px`).toEqual([]);
  expect(v.hScroll, `horizontal page scroll at ${v.width}px`).toBe(0);
  expect(v.consoleErrors, `console errors at ${v.width}px`).toEqual([]);
}

/**
 * Blocks until the page is BOTH interactive and finished loading its data.
 *
 * Two distinct waits, and both are needed. Every page here is prerendered server-side, so the
 * finished markup is on screen for several seconds before Blazor's interactive render replaces it;
 * during that replacement each section component re-runs `OnInitializedAsync` and swaps its
 * content for a `*-loading` placeholder. Measuring in that window makes a fully-populated screen
 * report every control "absent" — which is exactly what the first pass of this cluster recorded
 * for `/resume`, `/admin/skills` and `/admin/awards` while the screenshots showed full data.
 *
 * So: first wait for any `_bl_` marker (proof the interactive render has happened at all), then
 * wait for every `*-loading` placeholder to disappear (proof its data has arrived).
 */
export async function settle(page: Page, timeout = 120000) {
  await page.waitForFunction(
    () => Array.from(document.querySelectorAll('*')).some((e) => Array.from(e.attributes).some((a) => a.name.startsWith('_bl_'))),
    undefined,
    { timeout, polling: 250 },
  );
  await settleDom(page, timeout);
  await page.waitForFunction(
    () => document.querySelectorAll('[data-testid$="-loading"]').length === 0,
    undefined,
    { timeout, polling: 250 },
  );
  await settleDom(page, timeout);
  await page.waitForTimeout(800);
}

/**
 * Waits until the rendered control set stops changing.
 *
 * The `_bl_` marker only proves *some* component went interactive — the header does so before the
 * page body finishes swapping, which left `/resume` measured mid-swap with every section reported
 * absent. Requiring five consecutive identical counts (2 s) spans the swap.
 */
async function settleDom(page: Page, timeout: number) {
  await page.waitForFunction(
    () => {
      const w = window as unknown as { tbLastCount?: number; tbStable?: number };
      const n = document.querySelectorAll('[data-testid]').length;
      if (w.tbLastCount === n) {
        w.tbStable = (w.tbStable ?? 0) + 1;
      } else {
        w.tbLastCount = n;
        w.tbStable = 0;
      }
      return (w.tbStable ?? 0) >= 5;
    },
    undefined,
    { timeout, polling: 400 },
  );
  await page.evaluate(() => {
    const w = window as unknown as { tbLastCount?: number; tbStable?: number };
    w.tbLastCount = undefined;
    w.tbStable = 0;
  });
}

/**
 * Blocks until Blazor has taken over the given control, i.e. until the interactive re-render has
 * stamped it with an `_bl_<guid>` event marker. Clicking before that runs the *static* markup —
 * on a form that means a native POST and an HTTP 400 from the host.
 */
export async function waitInteractive(page: Page, testId: string, timeout = 90000) {
  await page.waitForFunction(
    (id) => {
      const el = document.querySelector(`[data-testid="${id}"]`);
      return !!el && Array.from(el.attributes).some((a) => a.name.startsWith('_bl_'));
    },
    testId,
    { timeout, polling: 250 },
  );
  await page.waitForTimeout(300);
}

/**
 * Signs in only once the login form has actually become interactive.
 *
 * `_gates.login` fills the form after a fixed 2s pause. `LoginPage.razor` is an `EditForm` under
 * `@rendermode="InteractiveServer"`, and measurement on this host shows the interactive handoff
 * completing at roughly **9.6 s** while seven verification agents share the process — a click
 * before that submits the *static* markup natively and the host answers HTTP 400
 * (*"The POST request does not specify which form is being submitted"* / *"A valid antiforgery
 * token was not provided"*), which is what failed every test in this cluster's first run.
 *
 * The handoff is observable rather than guessable: the interactive re-render drops the static
 * form's `action` attribute and stamps the submit button with Blazor's `_bl_<guid>` event marker.
 * Waiting on that marker makes the gate causal, so the login is as fast as the host allows and no
 * faster.
 */
export async function signIn(page: Page, role: 'admin' | 'editor' | 'author' = 'admin') {
  const user = USERS[role];
  let last: unknown;

  for (let attempt = 1; attempt <= 3; attempt++) {
    try {
      await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
      await page.waitForSelector('[data-testid="login-email"]', { timeout: 60000 });

      await page.waitForFunction(
        () => {
          const btn = document.querySelector('[data-testid="login-submit"]');
          const form = document.querySelector('form');
          const interactive = !!btn && Array.from(btn.attributes).some((a) => a.name.startsWith('_bl_'));
          return interactive && !!form && !form.hasAttribute('action');
        },
        undefined,
        { timeout: 90000, polling: 250 },
      );
      await page.waitForTimeout(500);

      await page.fill('[data-testid="login-email"]', user.email);
      await page.fill('[data-testid="login-password"]', user.password);
      await page.click('[data-testid="login-submit"]');
      await page.waitForURL((u) => !u.pathname.toLowerCase().includes('login'), { timeout: 60000 });
      await page.waitForTimeout(2000);
      return page.url();
    } catch (err) {
      last = err;
      console.log(`signIn(${role}) attempt ${attempt} failed: ${(err as Error).message.split('\n')[0]}`);
      await page.waitForTimeout(3000);
    }
  }
  throw last;
}

/** Prints the measured rectangles behind an overlap so the finding can be judged, not just seen. */
async function describeOverlaps(page: Page, v: VisualResult) {
  if (v.overlaps.length === 0) return;
  const names = Array.from(new Set(v.overlaps.flatMap((o) => [o.a, o.b])));
  const rects = await page.evaluate((wanted) => {
    const named = (e: Element) =>
      e.getAttribute('data-testid') ||
      `${e.tagName.toLowerCase()}${e.className && typeof e.className === 'string' ? '.' + e.className.split(' ')[0] : ''}`;
    const out: Record<string, string> = {};
    for (const e of Array.from(document.querySelectorAll('*'))) {
      const n = named(e);
      if (!wanted.includes(n) || out[n]) continue;
      const r = e.getBoundingClientRect();
      out[n] = `x=${Math.round(r.left)} y=${Math.round(r.top)} w=${Math.round(r.width)} h=${Math.round(r.height)} pos=${getComputedStyle(e).position}`;
    }
    return out;
  }, names);
  console.log(`  VISUAL@${v.width} overlap geometry:`, JSON.stringify(rects));
}

/** Captures both required breakpoints and restores the desktop viewport. */
export async function bothWidths(page: Page, stem: string): Promise<VisualResult[]> {
  const wide = await visualCheck(page, `${SHOTS}/${stem}-1280.png`, 1280);
  await describeOverlaps(page, wide);
  const wideExempt = await scrollExempt(page);
  const narrow = await visualCheck(page, `${SHOTS}/${stem}-390.png`, 390);
  await describeOverlaps(page, narrow);
  const narrowExempt = await scrollExempt(page);
  for (const [v, exempt] of [[wide, wideExempt], [narrow, narrowExempt]] as [VisualResult, Set<string>][]) {
    const kept = v.offViewport.filter((o) => !exempt.has(o.split('@')[0]));
    if (kept.length !== v.offViewport.length) {
      console.log(
        `  VISUAL@${v.width}: ignoring ${v.offViewport.length - kept.length} control(s) inside an ` +
          `overflow-x:auto scroller — ${JSON.stringify(v.offViewport.filter((o) => exempt.has(o.split('@')[0])))}`,
      );
    }
    v.offViewport = kept;
  }
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.waitForTimeout(400);
  return [wide, narrow];
}

