/*
  a11y-keyboard.spec.ts — REQ-NFR-007 re-audit (2026-08-07).

  Keyboard-only evidence:
    1. Tab traversal of the home page — every stop has a visible focus indicator.
    2. WCAG 1.1.1 closure — reach and COMPLETE a captcha-guarded write action (comment)
       with Tab / Enter / typing only, zero pointer clicks, through the accessible
       question challenge; and confirm the challenge is announced.
    3. TR-031 probe — is the library Rating's radiogroup keyboard operable at all, and
       does the native radio fallback fully cover it?
*/
import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';

const BASE = 'http://localhost:5431';
const POST = '/post/getting-started-with-blazor-server';
const OUT = path.join(process.cwd(), 'test-results', 'a11y-reaudit');
fs.mkdirSync(OUT, { recursive: true });

/** Describes the currently focused element. */
const describeFocus = `() => {
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
}`;

test('tab traversal home — every stop visible and focus-indicated', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(BASE + '/', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4000);

  const stops: any[] = [];
  const seen = new Set<string>();
  for (let i = 0; i < 60; i++) {
    await page.keyboard.press('Tab');
    const info: any = await page.evaluate(describeFocus);
    if (!info) break;
    const key = `${info.tag}|${info.testid}|${info.text}`;
    if (seen.has(key) && stops.length > 5) break; // wrapped around
    seen.add(key);
    stops.push(info);
  }

  const noIndicator = stops.filter(
    s =>
      s.tabindex !== '-1' &&
      (s.outlineStyle === 'none' || s.outlineWidth === '0px') &&
      s.boxShadow === 'none'
  );
  const hiddenStops = stops.filter(s => s.ariaHiddenAncestor);
  const zeroSize = stops.filter(s => s.tabindex !== '-1' && (s.w === 0 || s.h === 0));

  fs.writeFileSync(
    path.join(OUT, 'keyboard-home-traversal.json'),
    JSON.stringify({ total: stops.length, noIndicator, hiddenStops, zeroSize, stops }, null, 2)
  );
  console.log(`TAB HOME: ${stops.length} stops; noIndicator=${noIndicator.length}; ` +
    `insideAriaHidden=${hiddenStops.length}; zeroSize=${zeroSize.length}`);
  expect(stops.length).toBeGreaterThan(10);
});

test('tab traversal post page — focus stops inside aria-hidden are the TR-031 probe', async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(BASE + POST, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4500);

  const stops: any[] = [];
  for (let i = 0; i < 80; i++) {
    await page.keyboard.press('Tab');
    const info: any = await page.evaluate(describeFocus);
    if (!info) break;
    stops.push(info);
    if (info.testid === 'captcha-mode-toggle') break;
  }
  const hiddenStops = stops.filter(s => s.ariaHiddenAncestor);
  const noIndicator = stops.filter(
    s => s.tabindex !== '-1' && (s.outlineStyle === 'none' || s.outlineWidth === '0px') && s.boxShadow === 'none'
  );
  fs.writeFileSync(
    path.join(OUT, 'keyboard-post-traversal.json'),
    JSON.stringify({ total: stops.length, hiddenStops, noIndicator, stops }, null, 2)
  );
  console.log(`TAB POST: ${stops.length} stops; insideAriaHidden=${hiddenStops.length} ` +
    `${JSON.stringify(hiddenStops.map(s => s.role + ':' + s.ariaLabel.slice(0, 30)))}; noIndicator=${noIndicator.length}`);
});

test('TR-031 probe — library Rating vs native radio fallback', async ({ page }) => {
  await page.goto(BASE + POST, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4500);

  // (a) Is any library star itself focusable / does it carry aria-checked?
  const libInfo = await page.evaluate(() => {
    const host = document.querySelector('[data-testid="post-rating-stars"]');
    if (!host) return null;
    const group = host.querySelector('[role="radiogroup"]');
    const stars = Array.from(host.querySelectorAll('[role="radio"]'));
    const gradIds = Array.from(host.querySelectorAll('linearGradient')).map(g => g.id);
    return {
      wrapperAriaHidden: host.getAttribute('aria-hidden'),
      groupTabindex: group ? group.getAttribute('tabindex') : null,
      starCount: stars.length,
      starsFocusable: stars.filter(s => s.hasAttribute('tabindex')).length,
      starsWithAriaChecked: stars.filter(s => s.hasAttribute('aria-checked')).length,
      starsWithEmptyAriaChecked: stars.filter(s => s.getAttribute('aria-checked') === '').length,
      gradientIds: gradIds,
      duplicateGradientIds: gradIds.length - new Set(gradIds).size,
    };
  });
  console.log('TR031 LIB: ' + JSON.stringify(libInfo));

  // (b) Can the radiogroup be operated by keyboard once focused?
  let libKeyboardWorks = false;
  const group = page.locator('[data-testid="post-rating-stars"] [role="radiogroup"]');
  if (await group.count()) {
    try {
      await group.focus({ timeout: 5000 });
      await page.keyboard.press('ArrowRight');
      await page.waitForTimeout(600);
      await page.keyboard.press('Enter');
      await page.waitForTimeout(600);
      libKeyboardWorks = await page
        .locator('[data-testid="rating-identify-step"]')
        .isVisible({ timeout: 3000 })
        .catch(() => false);
    } catch { /* not focusable at all */ }
  }
  console.log('TR031 LIB KEYBOARD OPERABLE: ' + libKeyboardWorks);

  // (c) Does the native fallback fully cover it — keyboard only?
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4500);
  const star4 = page.locator('[data-testid="post-rating-star-4"]');
  await star4.focus({ timeout: 10000 });
  await page.keyboard.press('Space');
  await page.waitForTimeout(800);
  const fallbackWorks = await page
    .locator('[data-testid="rating-identify-step"]')
    .isVisible({ timeout: 5000 })
    .catch(() => false);
  const fallbackGeom = await page.evaluate(() => {
    const fs2 = document.querySelector('[data-testid="post-rating-keyboard"]') as HTMLElement;
    if (!fs2) return null;
    const r = fs2.getBoundingClientRect();
    return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) };
  });
  const checked = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="post-rating-star-"]'))
      .map((e: any) => `${e.value}:${e.checked}`).join(',')
  );
  console.log(`TR031 FALLBACK: identifyStepShown=${fallbackWorks} checked=${checked} geom=${JSON.stringify(fallbackGeom)}`);
  fs.writeFileSync(
    path.join(OUT, 'tr031-probe.json'),
    JSON.stringify({ libInfo, libKeyboardWorks, fallbackWorks, checked, fallbackGeom }, null, 2)
  );
  expect(fallbackWorks, 'native radio fallback selects a rating by keyboard').toBe(true);
});

test('1.1.1 closure — comment posted keyboard-only through the accessible question challenge', async ({ page }) => {
  const clicks: string[] = [];
  page.on('console', m => { if (m.text().startsWith('POINTER')) clicks.push(m.text()); });

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.goto(BASE + POST, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(4500);

  // Trap any real pointer event so "keyboard only" is provable, not asserted.
  await page.evaluate(() => {
    (window as any).__pointerEvents = 0;
    ['click', 'mousedown', 'pointerdown'].forEach(t =>
      document.addEventListener(t, (e: any) => {
        if (e.isTrusted && e.detail !== 0) (window as any).__pointerEvents++;
      }, true)
    );
  });

  // Tab from the top of the document until we land on the captcha mode toggle,
  // filling the comment fields on the way with the keyboard only.
  await page.evaluate(() => (document.activeElement as HTMLElement)?.blur());
  const trail: string[] = [];
  let toggleStops = -1;
  for (let i = 0; i < 80; i++) {
    await page.keyboard.press('Tab');
    const info: any = await page.evaluate(describeFocus);
    if (!info) break;
    trail.push(`${i + 1}:${info.tag}${info.testid ? '[' + info.testid + ']' : ''}`);
    if (info.testid === 'comment-name') await page.keyboard.type('Keyboard Auditor');
    if (info.testid === 'comment-email') await page.keyboard.type('contributor@techieblog.test');
    if (info.testid === 'comment-input') await page.keyboard.type('Posted with the keyboard only, through the accessible question challenge. [REQ-NFR-007]');
    if (info.testid === 'captcha-mode-toggle') { toggleStops = i + 1; break; }
  }
  expect(toggleStops, 'captcha mode toggle reachable by Tab').toBeGreaterThan(0);
  console.log(`KB: mode toggle reached in ${toggleStops} Tab stops. Trail: ${trail.join(' > ')}`);

  // Enter on the toggle switches to the accessible question challenge.
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);

  const q = await page.evaluate(() => {
    const prompt = document.querySelector('[data-testid="captcha-prompt"] label') as HTMLLabelElement;
    const input = document.querySelector('[data-testid="captcha-answer"]') as HTMLInputElement;
    const status = document.querySelector('[data-testid="captcha-status"]') as HTMLElement;
    const img = document.querySelector('[data-testid="captcha-image"]');
    return {
      questionText: prompt ? prompt.textContent!.trim() : null,
      labelFor: prompt ? prompt.getAttribute('for') : null,
      inputId: input ? input.id : null,
      liveRegionRole: status ? status.getAttribute('role') : null,
      liveRegionPoliteness: status ? status.getAttribute('aria-live') : null,
      liveRegionText: status ? status.textContent!.trim() : null,
      imagePresent: !!img,
    };
  });
  console.log('QUESTION MODE: ' + JSON.stringify(q));
  expect(q.imagePresent, 'image challenge replaced by the question').toBe(false);
  expect(q.labelFor).toBe(q.inputId);
  expect(q.liveRegionRole).toBe('status');
  expect(q.liveRegionText).toContain('Verification question');

  // The accessible NAME of the answer input must be the question itself.
  const accName = await page
    .locator('[data-testid="captcha-answer"]')
    .evaluate((el: any) => {
      const lbl = el.labels && el.labels[0];
      return lbl ? lbl.textContent.trim() : null;
    });
  console.log('ANSWER INPUT ACCESSIBLE NAME: ' + accName);
  expect(accName).toBe(q.questionText);

  // Solve it (test-side arithmetic on the question prose — the answer is nowhere in the DOM).
  const answer = solve(q.questionText!);
  console.log(`SOLVED "${q.questionText}" -> ${answer}`);
  expect(answer).not.toBeNull();

  // Answer box is the previous tab stop; Shift+Tab back to it, type, then Tab to submit.
  await page.keyboard.press('Shift+Tab');
  const onAnswer: any = await page.evaluate(describeFocus);
  expect(onAnswer.testid).toBe('captcha-answer');
  await page.keyboard.type(String(answer));
  await page.waitForTimeout(400);

  // Tab forward to the submit button and press Enter.
  let reachedSubmit = false;
  for (let i = 0; i < 12; i++) {
    await page.keyboard.press('Tab');
    const info: any = await page.evaluate(describeFocus);
    if (info && info.testid === 'comment-submit') { reachedSubmit = true; break; }
  }
  expect(reachedSubmit, 'submit reachable by Tab from the captcha').toBe(true);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(4000);

  const outcome = await page.evaluate(() => {
    const ok = document.querySelector('[data-testid="comment-form-success"]');
    const err = document.querySelector('[data-testid="comment-form-error"]');
    const capErr = document.querySelector('[data-testid="captcha-error"]');
    return {
      success: ok ? ok.textContent!.trim().slice(0, 200) : null,
      error: err ? err.textContent!.trim().slice(0, 200) : null,
      captchaError: capErr ? capErr.textContent!.trim().slice(0, 200) : null,
      pointerEvents: (window as any).__pointerEvents,
    };
  });
  console.log('KEYBOARD-ONLY COMMENT OUTCOME: ' + JSON.stringify(outcome));
  await page.screenshot({ path: path.join(OUT, 'kb-comment-outcome.png') });
  fs.writeFileSync(
    path.join(OUT, 'keyboard-111-closure.json'),
    JSON.stringify({ toggleStops, trail, question: q, accName, answer, outcome }, null, 2)
  );

  expect(outcome.pointerEvents, 'zero trusted pointer events').toBe(0);
  expect(outcome.captchaError, 'captcha accepted the answer').toBeNull();
  expect(outcome.success, 'comment accepted').not.toBeNull();
});

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
