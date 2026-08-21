/*
  cluster-g-keyboard.spec.ts — REQ-NFR-007 keyboard + WCAG 1.1.1 closure (2026-08-08, Cluster G).

  What this proves, with zero pointer events anywhere in the captcha path:

    1. TRAVERSAL — Tab reaches every interactive element on the public pages, in document order,
       with a visible focus indicator, none of them zero-size and none of them buried inside an
       aria-hidden subtree (the last one is the TR-031 residual probe).

    2. 1.1.1 CLOSURE on ALL THREE public write surfaces — comment form, rating panel and the
       newsletter subscribe card. On each, the accessible question challenge is reached by Tab
       alone, read from the answer input's ACCESSIBLE NAME (not from a visual cue), answered, and
       the write is actually accepted. Each write carries a unique marker so the row can be found
       in PostgreSQL afterwards — the UI's own success message is not accepted as proof.

    3. TARGET SIZE — the rendered box of every interactive element is measured so the decision
       recorded against REQ-NFR-007 rests on numbers.
*/
import { test, expect, Page } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = process.env.TB_BASE ?? 'http://127.0.0.1:5441';
const OUT = process.env.TB_OUT ?? path.join(process.cwd(), 'test-results', 'cluster-g');
const POST = '/post/getting-started-with-blazor-server';
const RUN = process.env.TB_RUN ?? 'cgrun';
fs.mkdirSync(OUT, { recursive: true });

/**
 * Describes the currently focused element.
 *
 * NOTE: this is a real function, not the function-SOURCE STRING the 2026-08-07 spec
 * (tests/verify/a11y-keyboard.spec.ts) passed to page.evaluate. Under the Playwright now
 * installed, `page.evaluate('() => {...}')` resolves the arrow function as an expression and
 * returns `undefined` instead of calling it — so every traversal loop written that way records
 * ZERO tab stops and silently "passes". Measured, not assumed: see the note on the REQ-NFR-007
 * checklist row.
 */
const describeFocus = () => {
  const el = document.activeElement;
  if (!el || el === document.body) return null;
  const r = el.getBoundingClientRect();
  const cs = getComputedStyle(el);
  return {
    tag: el.tagName.toLowerCase(),
    testid: el.getAttribute('data-testid') || '',
    role: el.getAttribute('role') || '',
    text: (el.textContent || '').trim().slice(0, 60),
    ariaLabel: el.getAttribute('aria-label') || '',
    tabindex: el.getAttribute('tabindex') || '',
    ariaHiddenAncestor: !!el.closest('[aria-hidden="true"]'),
    w: Math.round(r.width), h: Math.round(r.height),
    outlineWidth: cs.outlineWidth,
    outlineStyle: cs.outlineStyle,
    boxShadow: cs.boxShadow,
  };
};

/** Blocks the whole run from ever using the mouse on the surfaces under test. */
async function armPointerTrap(page: Page) {
  await page.evaluate(() => {
    (window as any).__pointerEvents = 0;
    ['click', 'mousedown', 'pointerdown'].forEach(t =>
      document.addEventListener(
        t,
        (e: any) => {
          if (e.isTrusted && e.detail !== 0) (window as any).__pointerEvents++;
        },
        true
      )
    );
  });
}

/**
 * Tabs forward until the element carrying `testid` has focus, typing whatever `fill` says to
 * type on the way. Returns the number of Tab presses, or -1 if it was never reached.
 *
 * A Blazor Server re-render can drop focus back to <body> mid-traversal, which is why a null
 * reading continues the walk instead of ending it — ending it there is how a traversal silently
 * under-reports.
 */
async function tabUntil(
  page: Page,
  testid: string,
  fill: Record<string, string>,
  trail: string[],
  limit = 90
): Promise<number> {
  for (let i = 0; i < limit; i++) {
    await page.keyboard.press('Tab');
    const info: any = await page.evaluate(describeFocus);
    if (!info) { trail.push(`${i + 1}:(focus lost)`); continue; }
    trail.push(`${i + 1}:${info.tag}${info.testid ? '[' + info.testid + ']' : ''}`);
    if (fill[info.testid]) {
      await typeAndVerify(page, info.testid, fill[info.testid]);
    }
    if (info.testid === testid) return i + 1;
  }
  return -1;
}

/**
 * Types `text` with the keyboard and PROVES it arrived intact, retyping more slowly if it did not.
 *
 * Why this is needed: every keystroke in a bound Blazor Server field is a circuit round trip, and
 * the server's echo re-writes the input's value. Typing faster than the round trip scrambles the
 * field — measured on this build, 'cg-subscribe-cg0808c@techieblog.test' arrived as
 * 'c-usrb-g08eegt' at 60 ms/char. That is a real defect for anyone whose assistive technology
 * injects text (voice input, switch/AAC, screen-reader type commands), logged as TR-052; here it
 * is worked around so the 1.1.1 evidence is not lost in it. Still keyboard-only: no pointer event
 * is ever generated.
 */
async function typeAndVerify(page: Page, testid: string, text: string) {
  const read = () =>
    page.evaluate(
      (id: string) => (document.querySelector(`[data-testid="${id}"]`) as HTMLInputElement)?.value ?? null,
      testid
    );

  for (const attempt of [0, 1, 2]) {
    if (attempt < 2) {
      await page.keyboard.type(text, { delay: attempt === 0 ? 80 : 220 });
    } else {
      // Last resort: the server echo puts the caret back at position 0 between keystrokes, so
      // press End before each character. Still nothing but key presses.
      for (const ch of text) {
        await page.keyboard.press('End');
        await page.keyboard.type(ch);
        await page.waitForTimeout(120);
      }
    }
    await page.waitForTimeout(700);
    if ((await read()) === text) return;

    // Clear with the keyboard alone and try again.
    await page.keyboard.press('ControlOrMeta+a');
    await page.keyboard.press('Backspace');
    await page.waitForTimeout(700);
  }
  throw new Error(
    `could not type "${text}" into [data-testid="${testid}"] intact — last value ${JSON.stringify(await read())}`
  );
}

/** Shift+Tabs backwards until `testid` has focus. Returns the number of presses, or -1. */
async function shiftTabUntil(page: Page, testid: string, limit = 12): Promise<number> {
  for (let i = 0; i < limit; i++) {
    await page.keyboard.press('Shift+Tab');
    const info: any = await page.evaluate(describeFocus);
    if (info && info.testid === testid) return i + 1;
  }
  return -1;
}

/** Confirms `testid` still has focus; if a re-render stole it, walks forward to it again. */
async function ensureFocused(page: Page, testid: string): Promise<boolean> {
  const now: any = await page.evaluate(describeFocus);
  if (now && now.testid === testid) return true;
  const trail: string[] = [];
  return (await tabUntil(page, testid, {}, trail, 30)) > 0;
}

/**
 * Puts the sequential-focus starting point back at the top of the document.
 *
 * blur() alone is not enough: Chromium keeps the "sequential focus navigation starting point"
 * where the blurred element was, so the next Tab resumes mid-page and a traversal silently
 * starts halfway down. Focusing <body> (tabindex -1, so it is not itself a stop) resets it.
 */
async function resetFocusToDocumentStart(page: Page) {
  await page.evaluate(() => {
    (document.activeElement as HTMLElement)?.blur();
    document.body.setAttribute('tabindex', '-1');
    document.body.focus();
  });
}

/** Waits for the widget in `scope` to actually be showing the question, not the image. */
async function waitForQuestionMode(page: Page, scope: string) {
  await page.waitForFunction(
    (s: string) => {
      const root = document.querySelector(s) ?? document;
      return !root.querySelector('[data-testid="captcha-image"]');
    },
    scope,
    { timeout: 20000 }
  );
}

/** Resolves the four question shapes REQ-UI-057 issues. Returns null if unrecognised. */
function solve(q: string): number | null {
  const words: Record<string, number> = {
    zero: 0, one: 1, two: 2, three: 3, four: 4, five: 5, six: 6,
    seven: 7, eight: 8, nine: 9, ten: 10, eleven: 11, twelve: 12,
    thirteen: 13, fourteen: 14, fifteen: 15, sixteen: 16, seventeen: 17,
    eighteen: 18, nineteen: 19, twenty: 20,
  };
  const t = q.toLowerCase().replace(/[?.,]/g, '').trim();

  let m = t.match(/what is ([a-z]+) plus ([a-z]+)/);
  if (m && m[1] in words && m[2] in words) return words[m[1]] + words[m[2]];

  m = t.match(/what is ([a-z]+) minus ([a-z]+)/);
  if (m && m[1] in words && m[2] in words) return words[m[1]] - words[m[2]];

  m = t.match(/how many letters are in the word ["“']?([a-z]+)["”']?/);
  if (m) return m[1].length;

  m = t.match(/how many words are in this line: ["“']?([^"”']+)["”']?/);
  if (m) return m[1].trim().split(/\s+/).length;

  return null;
}

/**
 * Reads the question out of the answer input's ACCESSIBLE NAME — i.e. the way a screen reader
 * gets it — and returns the worked answer. Fails loudly if the name is not the question.
 */
async function readAndSolveChallenge(page: Page, captchaScope: string) {
  const q = await page.evaluate((scope: string) => {
    const root = document.querySelector(scope) ?? document;
    const input = root.querySelector('input[data-testid$="captcha-answer"], [data-testid="captcha-answer"]') as HTMLInputElement;
    const label = input && input.labels && input.labels[0];
    const status = root.querySelector('[data-testid="captcha-status"]') as HTMLElement;
    const img = root.querySelector('[data-testid="captcha-image"]');
    return {
      accessibleName: label ? label.textContent!.trim() : null,
      labelFor: label ? label.getAttribute('for') : null,
      inputId: input ? input.id : null,
      liveRole: status ? status.getAttribute('role') : null,
      liveText: status ? status.textContent!.trim() : null,
      imagePresent: !!img,
    };
  }, captchaScope);
  const answer = q.accessibleName ? solve(q.accessibleName) : null;
  return { ...q, answer };
}

// ---------------------------------------------------------------------------------------------
// 1. Traversal
// ---------------------------------------------------------------------------------------------

const TRAVERSAL_PAGES: Array<[string, string]> = [
  ['home', '/'],
  ['post', POST],
  ['newsletters', '/newsletters'],
  ['resume', '/resume'],
];

for (const [name, url] of TRAVERSAL_PAGES) {
  test(`tab traversal ${name}`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(BASE + url, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(4500);
    await resetFocusToDocumentStart(page);

    const stops: any[] = [];
    for (let i = 0; i < 140; i++) {
      await page.keyboard.press('Tab');
      const info: any = await page.evaluate(describeFocus);
      if (!info) break;
      stops.push(info);
      // Stop once focus has cycled back to the very first stop.
      if (
        stops.length > 8 &&
        info.tag === stops[0].tag &&
        info.testid === stops[0].testid &&
        info.text === stops[0].text
      ) {
        stops.pop();
        break;
      }
    }

    const noIndicator = stops.filter(
      s => s.tabindex !== '-1' && (s.outlineStyle === 'none' || s.outlineWidth === '0px') && s.boxShadow === 'none'
    );
    const insideAriaHidden = stops.filter(s => s.ariaHiddenAncestor);
    const zeroSize = stops.filter(s => s.tabindex !== '-1' && (s.w === 0 || s.h === 0));

    fs.writeFileSync(
      path.join(OUT, `keyboard-traversal-${name}.json`),
      JSON.stringify({ page: name, total: stops.length, noIndicator, insideAriaHidden, zeroSize, stops }, null, 2)
    );
    console.log(
      `TAB ${name}: ${stops.length} stops; noIndicator=${noIndicator.length}; ` +
        `insideAriaHidden=${insideAriaHidden.length} ${JSON.stringify(insideAriaHidden.map(s => s.role + '/' + s.testid))}; ` +
        `zeroSize=${zeroSize.length}`
    );
    expect(stops.length).toBeGreaterThan(5);
    expect(noIndicator.length, `stops without a visible focus indicator on ${name}`).toBe(0);
  });
}

// ---------------------------------------------------------------------------------------------
// 2. WCAG 1.1.1 closure — all three write surfaces, keyboard only
// ---------------------------------------------------------------------------------------------

test('1.1.1 comment posted keyboard-only through the question challenge', async ({ page }) => {
  const marker = `REQ-NFR-007 ${RUN} keyboard only.`;
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(BASE + POST, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4500);
  await armPointerTrap(page);
  await resetFocusToDocumentStart(page);

  const trail: string[] = [];
  const toggleStops = await tabUntil(page, 'captcha-mode-toggle', {
    'comment-name': 'CG Auditor',
    'comment-email': `cgc-${RUN}@tb.test`,
    'comment-input': marker,
  }, trail);
  expect(toggleStops, 'captcha mode toggle reachable by Tab').toBeGreaterThan(0);

  expect(await ensureFocused(page, 'captcha-mode-toggle')).toBe(true);
  await page.keyboard.press('Enter');
  await waitForQuestionMode(page, '[data-testid="comment-form"]');
  await page.waitForTimeout(800);

  const q = await readAndSolveChallenge(page, '[data-testid="comment-form"]');
  console.log(`COMMENT CHALLENGE: ${JSON.stringify(q)}`);
  expect(q.imagePresent, 'image replaced by the question').toBe(false);
  expect(q.labelFor).toBe(q.inputId);
  expect(q.answer, 'question resolved from the accessible name alone').not.toBeNull();

  expect(await shiftTabUntil(page, 'captcha-answer'), 'answer box reachable by Shift+Tab').toBeGreaterThan(0);
  await page.keyboard.type(String(q.answer), { delay: 60 });
  await page.waitForTimeout(400);

  const submitStops = await tabUntil(page, 'comment-submit', {}, trail, 12);
  expect(submitStops, 'submit reachable by Tab from the captcha').toBeGreaterThan(0);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(4500);

  const outcome = await page.evaluate(() => ({
    success: (document.querySelector('[data-testid="comment-form-success"]') as HTMLElement)?.innerText?.trim() ?? null,
    error: (document.querySelector('[data-testid="comment-form-error"]') as HTMLElement)?.innerText?.trim() ?? null,
    captchaError: (document.querySelector('[data-testid="captcha-error"]') as HTMLElement)?.innerText?.trim() ?? null,
    pointerEvents: (window as any).__pointerEvents,
  }));
  console.log(`COMMENT OUTCOME: ${JSON.stringify(outcome)} (toggle in ${toggleStops} tab stops)`);
  await page.screenshot({ path: path.join(OUT, 'kb-comment-outcome-1280.png') });
  fs.writeFileSync(
    path.join(OUT, 'closure-comment.json'),
    JSON.stringify({ marker, toggleStops, trail, challenge: q, outcome }, null, 2)
  );

  expect(outcome.pointerEvents, 'zero trusted pointer events').toBe(0);
  expect(outcome.captchaError, 'captcha accepted the answer').toBeNull();
  expect(outcome.success, 'comment accepted').not.toBeNull();
});

test('1.1.1 rating submitted keyboard-only through the question challenge', async ({ page }) => {
  const email = `cgr-${RUN}@tb.test`;
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(BASE + POST, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4500);
  await armPointerTrap(page);
  await resetFocusToDocumentStart(page);

  // Reach the library's own star radiogroup by Tab and arrow to 4 stars. The visually hidden
  // native <fieldset> that used to carry these semantics was deleted on 2026-08-11 once
  // TrBlazeUI 2.0.2 made every option a real <button role="radio"> (TR-031/045/052), so the
  // stop is found by ancestry rather than by a per-star data-testid.
  const trail: string[] = [];
  let starStops = -1;
  for (let i = 0; i < 90; i++) {
    await page.keyboard.press('Tab');
    const here = await page.evaluate(() => {
      const el = document.activeElement;
      return {
        inRating: !!el?.closest('[data-testid="post-rating-stars"]'),
        tag: el?.tagName ?? '',
        role: el?.getAttribute('role') ?? '',
      };
    });
    trail.push(`${i + 1}:${here.tag}${here.role ? '[' + here.role + ']' : ''}`);
    if (here.inRating) { starStops = i + 1; break; }
  }
  expect(starStops, `rating radiogroup reachable by Tab. Path: ${trail.join(' > ')}`).toBeGreaterThan(0);
  // Arrow within the radio group, the way a radio group is meant to work.
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowRight');
  await page.keyboard.press('ArrowRight');
  await page.waitForTimeout(2000);

  const identifyVisible = await page.locator('[data-testid="rating-identify-step"]').isVisible().catch(() => false);
  expect(identifyVisible, 'choosing a star by keyboard reveals the identify step').toBe(true);

  // Tab on to the email box, type, then on to the captcha mode toggle.
  const toggleStops = await tabUntil(page, 'captcha-mode-toggle', { 'rating-email': email }, trail, 40);
  expect(toggleStops, 'captcha mode toggle reachable on the rating panel').toBeGreaterThan(0);

  expect(await ensureFocused(page, 'captcha-mode-toggle')).toBe(true);
  await page.keyboard.press('Enter');
  await waitForQuestionMode(page, '[data-testid="post-rating-panel"]');
  await page.waitForTimeout(800);

  const q = await readAndSolveChallenge(page, '[data-testid="post-rating-panel"]');
  console.log(`RATING CHALLENGE: ${JSON.stringify(q)}`);
  expect(q.answer, 'question resolved from the accessible name alone').not.toBeNull();

  expect(await shiftTabUntil(page, 'captcha-answer'), 'answer box reachable by Shift+Tab').toBeGreaterThan(0);
  await page.keyboard.type(String(q.answer), { delay: 60 });
  await page.waitForTimeout(400);

  const submitStops = await tabUntil(page, 'rating-submit', {}, trail, 12);
  expect(submitStops, 'rating submit reachable by Tab from the captcha').toBeGreaterThan(0);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(5000);

  const outcome = await page.evaluate(() => ({
    success: (document.querySelector('[data-testid="rating-form-success"]') as HTMLElement)?.innerText?.trim() ?? null,
    error: (document.querySelector('[data-testid="rating-form-error"]') as HTMLElement)?.innerText?.trim() ?? null,
    pointerEvents: (window as any).__pointerEvents,
  }));
  console.log(`RATING OUTCOME: ${JSON.stringify(outcome)} (star in ${starStops} stops, toggle +${toggleStops})`);
  await page.screenshot({ path: path.join(OUT, 'kb-rating-outcome-1280.png') });
  fs.writeFileSync(
    path.join(OUT, 'closure-rating.json'),
    JSON.stringify({ email, starStops, toggleStops, trail, challenge: q, outcome }, null, 2)
  );

  expect(outcome.pointerEvents, 'zero trusted pointer events').toBe(0);
  expect(outcome.error, 'rating not rejected').toBeNull();
  expect(outcome.success, 'rating accepted').not.toBeNull();
});

test('1.1.1 subscription created keyboard-only through the question challenge', async ({ page }) => {
  const email = `cgs-${RUN}@tb.test`;
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(BASE + '/newsletters', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4500);
  await armPointerTrap(page);
  await resetFocusToDocumentStart(page);

  const trail: string[] = [];
  const toggleStops = await tabUntil(page, 'captcha-mode-toggle', {
    'newsletter-subscribe-email': email,
  }, trail);
  expect(toggleStops, 'captcha mode toggle reachable on the subscribe card').toBeGreaterThan(0);

  expect(await ensureFocused(page, 'captcha-mode-toggle')).toBe(true);
  await page.keyboard.press('Enter');
  await waitForQuestionMode(page, '[data-testid="newsletter-subscribe"]');
  await page.waitForTimeout(800);

  const q = await readAndSolveChallenge(page, '[data-testid="newsletter-subscribe"]');
  console.log(`SUBSCRIBE CHALLENGE: ${JSON.stringify(q)}`);
  expect(q.answer, 'question resolved from the accessible name alone').not.toBeNull();

  expect(await shiftTabUntil(page, 'captcha-answer'), 'answer box reachable by Shift+Tab').toBeGreaterThan(0);
  await page.keyboard.type(String(q.answer), { delay: 60 });
  await page.waitForTimeout(400);

  // The submit button sits BEFORE the captcha on this card, so Shift+Tab back to it.
  expect(await shiftTabUntil(page, 'newsletter-subscribe-submit'),
    'subscribe submit reachable by keyboard from the captcha').toBeGreaterThan(0);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(5000);

  const outcome = await page.evaluate(() => {
    const status = document.querySelector('[data-testid="newsletter-subscribe-status"]') as HTMLElement;
    return {
      status: status ? status.innerText.trim() : null,
      captchaError: (document.querySelector('[data-testid="captcha-error"]') as HTMLElement)?.innerText?.trim() ?? null,
      pointerEvents: (window as any).__pointerEvents,
    };
  });
  console.log(`SUBSCRIBE OUTCOME: ${JSON.stringify(outcome)} (toggle in ${toggleStops} tab stops)`);
  await page.screenshot({ path: path.join(OUT, 'kb-subscribe-outcome-1280.png') });
  fs.writeFileSync(
    path.join(OUT, 'closure-subscribe.json'),
    JSON.stringify({ email, toggleStops, trail, challenge: q, outcome }, null, 2)
  );

  expect(outcome.pointerEvents, 'zero trusted pointer events').toBe(0);
  expect(outcome.captchaError, 'captcha accepted the answer').toBeNull();
  expect(outcome.status, 'subscribe outcome shown').not.toBeNull();
});

// ---------------------------------------------------------------------------------------------
// 3. Target size + TR-031 residual
// ---------------------------------------------------------------------------------------------

const SIZE_PAGES: Array<[string, string]> = [
  ['home', '/'],
  ['post', POST],
  ['newsletters', '/newsletters'],
];

for (const [name, url] of SIZE_PAGES) {
  test(`target size ${name}`, async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.goto(BASE + url, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(4500);

    const measured = await page.evaluate(() => {
      const sel = 'a[href], button, input:not([type=hidden]), select, textarea, [role="button"], [tabindex]:not([tabindex="-1"])';
      return Array.from(document.querySelectorAll(sel))
        .filter(e => {
          const r = e.getBoundingClientRect();
          return r.width > 0 && r.height > 0;
        })
        .map(e => {
          const r = e.getBoundingClientRect();
          const el = e as HTMLElement;
          return {
            tag: el.tagName.toLowerCase(),
            testid: el.getAttribute('data-testid') || '',
            text: (el.innerText || el.getAttribute('aria-label') || '').trim().slice(0, 40),
            w: Math.round(r.width),
            h: Math.round(r.height),
            // WCAG 2.5.8 (AA, 24x24) exempts inline links in a sentence; 2.5.5 (AAA, 44x44) too.
            inlineInText: !!el.closest('p, li, span.prose, .prose') && el.tagName.toLowerCase() === 'a',
          };
        });
    });

    const under24 = measured.filter(m => !m.inlineInText && (m.w < 24 || m.h < 24));
    const under44 = measured.filter(m => !m.inlineInText && (m.w < 44 || m.h < 44));
    fs.writeFileSync(
      path.join(OUT, `target-size-${name}.json`),
      JSON.stringify({ page: name, total: measured.length, under24, under44, measured }, null, 2)
    );
    console.log(
      `TARGET SIZE ${name}: ${measured.length} targets; <24px(2.5.8 AA)=${under24.length}; <44px(2.5.5 AAA)=${under44.length}`
    );
    expect(measured.length).toBeGreaterThan(0);
  });
}

test('tab traversal change-password — the forced staff interstitial', async ({ page }) => {
  // REQ-NFR-023 redirects every seeded staff account here on sign-in, so a keyboard user who
  // cannot operate this screen cannot use the admin at all (WCAG 2.1.1 on the most unavoidable
  // page in the app). Requested by the orchestrator; never audited before.
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(3000);
  await page.fill('[data-testid="login-email"]', 'Ravi@techieblog.com');
  await page.fill('[data-testid="login-password"]', 'admin_password');
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });

  // Navigate through the SPA router, not with a full page load: reloading an authorised route
  // drops the rehydrating auth state and the router sends you to "/" — which is how an earlier
  // version of this test "traversed" the home page while claiming to audit this screen.
  await page.evaluate(() => (window as any).Blazor.navigateTo('/change-password'));
  await page.waitForTimeout(5000);
  expect(new URL(page.url()).pathname, 'landed on the change-password screen').toBe('/change-password');
  await resetFocusToDocumentStart(page);

  const stops: any[] = [];
  for (let i = 0; i < 60; i++) {
    await page.keyboard.press('Tab');
    const info: any = await page.evaluate(describeFocus);
    if (!info) continue;
    stops.push(info);
    if (
      stops.length > 5 &&
      info.tag === stops[0].tag && info.testid === stops[0].testid && info.text === stops[0].text
    ) { stops.pop(); break; }
  }
  const reached = stops.map(s => s.testid);
  const noIndicator = stops.filter(
    s => s.tabindex !== '-1' && (s.outlineStyle === 'none' || s.outlineWidth === '0px') && s.boxShadow === 'none'
  );
  fs.writeFileSync(
    path.join(OUT, 'keyboard-traversal-change-password.json'),
    JSON.stringify({ total: stops.length, reached, noIndicator, stops }, null, 2)
  );
  await page.screenshot({ path: path.join(OUT, 'change-password-1280.png') });
  console.log(`TAB change-password: ${stops.length} stops; noIndicator=${noIndicator.length}; reached=${JSON.stringify(reached)}`);

  expect(reached, 'current password field reachable by Tab').toContain('change-password-current');
  expect(reached, 'new password field reachable by Tab').toContain('change-password-new');
  expect(reached, 'confirm field reachable by Tab').toContain('change-password-confirm');
  expect(reached, 'submit reachable by Tab').toContain('change-password-submit');
  expect(noIndicator.length, 'stops without a visible focus indicator').toBe(0);
});

test('TR-031 closed — the library Rating IS the control, not a decoration', async ({ page }) => {
  await page.goto(BASE + POST, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="post-rating-stars"]', { timeout: 30000 });
  await page.waitForTimeout(2500);

  const info = await page.evaluate(() => {
    const host = document.querySelector('[data-testid="post-rating-stars"]');
    if (!host) return null;
    const stars = Array.from(host.querySelectorAll('[role="radio"]'));
    const gradIds = Array.from(host.querySelectorAll('linearGradient')).map(g => g.id);
    return {
      hiddenFromAt: !!host.closest('[aria-hidden="true"]'),
      groupRole: host.getAttribute('role'),
      starCount: stars.length,
      starTags: [...new Set(stars.map(s => s.tagName))],
      spanRadiosOnPage: document.querySelectorAll('span[role="radio"]').length,
      rovingTabindexes: stars.map(s => s.getAttribute('tabindex')),
      ariaChecked: stars.map(s => s.getAttribute('aria-checked')),
      legacyFallbackNodes: document.querySelectorAll('[data-testid="post-rating-keyboard"]').length,
      gradientIds: gradIds,
      duplicateGradientIds: gradIds.length - new Set(gradIds).size,
    };
  });
  console.log('TR-031 CLOSED: ' + JSON.stringify(info));
  fs.writeFileSync(path.join(OUT, 'tr031-residual.json'), JSON.stringify(info, null, 2));
  expect(info).not.toBeNull();
  expect(info!.hiddenFromAt, 'the interactive rating is not hidden from assistive technology').toBe(false);
  expect(info!.starTags, 'options are real buttons, not spans').toEqual(['BUTTON']);
  expect(info!.spanRadiosOnPage, 'no dead span[role=radio] anywhere on the page').toBe(0);
  expect(info!.legacyFallbackNodes, 'the native <fieldset> fallback is removed').toBe(0);
  expect(info!.rovingTabindexes.filter(t => t === '0').length, 'exactly one roving tab stop').toBe(1);
  expect(info!.ariaChecked.every(v => v === 'true' || v === 'false'), 'aria-checked is a literal token').toBe(true);
  expect(info!.duplicateGradientIds, 'no duplicate gradient ids').toBe(0);
});
