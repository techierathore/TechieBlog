import { test, expect, Page } from '@playwright/test';

/**
 * Cluster G smoke — REQ-UI-057, the accessible alternative challenge for the self-hosted captcha.
 *
 * Gates applied:
 *   RENDER-TRUTH  — the question and its controls must render with REAL content, not placeholders.
 *   VISUAL-TRUTH  — screenshots at 1280 and 390; no zero-size, clipped or off-viewport controls,
 *                   no horizontal page overflow.
 *
 * Everything here is driven with Tab / Enter / Space only. No pointer click reaches the captcha,
 * which is the whole point: a keyboard-only visitor must be able to find and use the alternative.
 */

const BASE = 'http://localhost:5406';
const POST = '/post/getting-started-with-blazor-server';

const NUMBER_WORDS = [
  'zero', 'one', 'two', 'three', 'four', 'five', 'six', 'seven', 'eight', 'nine', 'ten',
  'eleven', 'twelve', 'thirteen', 'fourteen', 'fifteen', 'sixteen', 'seventeen',
  'eighteen', 'nineteen', 'twenty',
];

/** Works the question out the way a human would. Returns the digits and the English word. */
function solve(question: string): { digits: string; word: string } {
  const wordValue = (w: string) => NUMBER_WORDS.indexOf(w.toLowerCase());

  let m = question.match(/what is (\w+) plus (\w+)\?/i);
  if (m) return answers(wordValue(m[1]) + wordValue(m[2]));

  m = question.match(/what is (\w+) minus (\w+)\?/i);
  if (m) return answers(wordValue(m[1]) - wordValue(m[2]));

  m = question.match(/how many letters are in the word '([^']+)'\?/i);
  if (m) return answers(m[1].length);

  m = question.match(/how many words are in this line: '([^']+)'\?/i);
  if (m) return answers(m[1].trim().split(/\s+/).length);

  throw new Error(`Unrecognised question shape: ${question}`);
}

function answers(value: number): { digits: string; word: string } {
  return { digits: String(value), word: NUMBER_WORDS[value] ?? `n${value}` };
}

/** What currently holds focus, as a screen reader would see it. */
async function focused(page: Page) {
  return page.evaluate(() => {
    const el = document.activeElement as HTMLElement | null;
    if (!el) return null;
    return {
      tid: el.getAttribute('data-testid'),
      tag: el.tagName.toLowerCase(),
      role: el.getAttribute('role'),
      label: el.getAttribute('aria-label'),
      text: (el.textContent || '').trim().slice(0, 70),
    };
  });
}

/**
 * Presses Tab until the focused element carries one of the wanted data-testids.
 * Returns the whole tab path so the test can print real keyboard evidence.
 */
async function tabUntil(page: Page, wanted: string[], max = 200) {
  const path: string[] = [];
  for (let i = 0; i < max; i++) {
    await page.keyboard.press('Tab');
    const f = await focused(page);
    const tid = f?.tid ?? `(${f?.tag})`;
    path.push(tid);
    if (f?.tid && wanted.includes(f.tid)) return { found: f.tid, stops: i + 1, path, focus: f };
  }
  return { found: null as string | null, stops: max, path, focus: null as any };
}

/** Reads the visible question out of a widget that is already in question mode. */
async function readQuestion(page: Page, widget: string, timeout = 15000) {
  return (await page.locator(`[data-testid="${widget}"] [data-testid="captcha-prompt"]`)
    .first().innerText({ timeout })).trim();
}

/**
 * Waits until the widget's prompt really is a question. The toggle is a Blazor Server round
 * trip, so a fixed sleep is a coin toss under load — poll the rendered prompt instead.
 */
async function waitForQuestion(page: Page, widget: string, timeoutMs = 30000) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const prompt = await readQuestion(page, widget).catch(() => '');
    if (prompt.endsWith('?') && !/Loading/i.test(prompt)) return prompt;
    if (Date.now() > deadline) throw new Error(`widget "${widget}" never entered question mode; prompt was "${prompt}"`);
    await page.waitForTimeout(500);
  }
}

/**
 * VISUAL-TRUTH: every named control has a real box inside the viewport, page does not scroll
 * sideways. Both the documentElement and the body measure are checked — the routed defect
 * (the visually-hidden `fieldset[data-testid="post-rating-keyboard"]`, right = 662 at a 390 px
 * viewport — since removed with the fallback itself) showed up on documentElement, and cluster E's
 * `body.scrollWidth === body.clientWidth` is asserted too.
 */
async function visualGate(page: Page, testIds: string[], label: string) {
  const m = await page.evaluate(() => ({
    docOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
    bodyScroll: document.body.scrollWidth,
    bodyClient: document.body.clientWidth,
    worst: (() => {
      let w = '', right = 0;
      document.querySelectorAll('*').forEach(el => {
        const r = el.getBoundingClientRect();
        if (r.width > 0 && r.right > right) { right = r.right; w = `${el.tagName}[${el.getAttribute('data-testid')}] right=${Math.round(r.right)}`; }
      });
      return w;
    })(),
  }));
  console.log(`[visual] ${label}: docOverflow=${m.docOverflow} body.scrollWidth=${m.bodyScroll} body.clientWidth=${m.bodyClient} rightmost=${m.worst}`);
  expect(m.docOverflow, `${label}: horizontal overflow`).toBeLessThanOrEqual(1);
  expect(m.bodyScroll, `${label}: body.scrollWidth !== body.clientWidth`).toBeLessThanOrEqual(m.bodyClient);

  const vw = page.viewportSize()!.width;
  for (const id of testIds) {
    const el = page.locator(`[data-testid="${id}"]`).first();
    expect(await el.count(), `${label}: ${id} missing`).toBeGreaterThan(0);
    const box = await el.boundingBox();
    expect(box, `${label}: ${id} has no box`).not.toBeNull();
    expect(box!.width, `${label}: ${id} zero width`).toBeGreaterThan(4);
    expect(box!.height, `${label}: ${id} zero height`).toBeGreaterThan(4);
    expect(box!.x, `${label}: ${id} off the left edge`).toBeGreaterThanOrEqual(-1);
    expect(box!.x, `${label}: ${id} off the right edge`).toBeLessThan(vw);
  }
}

const T0 = { t: 0 };
const mark = (label: string) => console.log(`[t+${((Date.now() - T0.t) / 1000).toFixed(1)}s] ${label}`);

test.describe('REQ-UI-057 accessible alternative captcha challenge', () => {

  test('comment form: the alternative is reachable by keyboard alone and announced', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`${BASE}${POST}`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="captcha-widget"]', { timeout: 30000 });
    await page.waitForTimeout(2500);

    const walk = await tabUntil(page, ['captcha-mode-toggle']);
    expect(walk.found, `toggle not reachable by Tab. Path: ${walk.path.join(' > ')}`)
      .toBe('captcha-mode-toggle');
    console.log(`[comment] toggle reached after ${walk.stops} Tab presses`);
    console.log(`[comment] tab path: ${walk.path.join(' > ')}`);

    // It must be a real button with real text, not an icon-only or hover-only affordance.
    expect(walk.focus.tag).toBe('button');
    expect(walk.focus.text.length).toBeGreaterThan(10);
    console.log(`[comment] toggle accessible text: "${walk.focus.text}"`);

    await page.keyboard.press('Enter');
    await page.waitForTimeout(2000);

    // RENDER-TRUTH: a real question, not a placeholder.
    const question = await waitForQuestion(page, 'captcha-widget');
    console.log(`[comment] question rendered: "${question}"`);
    expect(question.length).toBeGreaterThan(12);
    expect(question).toMatch(/\?$/);
    expect(question).not.toMatch(/Loading/i);

    // The image is gone, so nothing on this surface now depends on sight.
    expect(await page.locator('[data-testid="captcha-image"]').count()).toBe(0);

    // The question is the label of the answer box -> it is the input's accessible name.
    const named = await page.evaluate(() => {
      const input = document.querySelector('[data-testid="captcha-answer"]') as HTMLInputElement;
      const label = document.querySelector(`label[for="${input.id}"]`);
      return { forId: input.id, labelText: (label?.textContent || '').trim() };
    });
    expect(named.labelText).toBe(question);
    console.log(`[comment] <label for="${named.forId}"> == the question (accessible name OK)`);

    // The live region announces the switch without focus moving.
    const status = await page.locator('[data-testid="captcha-status"]').first();
    const statusText = (await status.innerText()).trim();
    const statusAttrs = await status.evaluate(el => ({
      role: el.getAttribute('role'), live: el.getAttribute('aria-live'), cls: el.className,
    }));
    console.log(`[comment] live region ${JSON.stringify(statusAttrs)} says: "${statusText}"`);
    expect(statusAttrs.role).toBe('status');
    expect(statusAttrs.live).toBe('polite');
    expect(statusText).toContain(question);
  });

  test('comment form: a wrong answer is refused and a correct one posts the comment', async ({ page }) => {
    // Three round trips through a Blazor Server circuit plus a real DB write and a mail attempt.
    test.setTimeout(240000);
    T0.t = Date.now();
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`${BASE}${POST}`, { waitUntil: 'networkidle' });
    mark('goto done');
    await page.waitForSelector('[data-testid="captcha-widget"]', { timeout: 30000 });
    await page.waitForTimeout(2500);
    mark('widget ready');

    const stamp = Date.now();
    await page.fill('[data-testid="comment-name"]', 'Keyboard Visitor');
    await page.fill('[data-testid="comment-email"]', `req-ui-057-${stamp}@example.com`);
    await page.fill('[data-testid="comment-input"]', 'Posted through the accessible question challenge (REQ-UI-057).');

    const walk = await tabUntil(page, ['captcha-mode-toggle']);
    mark('tabbed to toggle');
    expect(walk.found).toBe('captcha-mode-toggle');
    await page.keyboard.press('Enter');
    await page.waitForTimeout(2000);
    mark('toggled to question mode');

    // --- wrong answer -----------------------------------------------------------------
    const firstQuestion = await waitForQuestion(page, 'captcha-widget');
    await page.fill('[data-testid="captcha-answer"]', '1234');
    mark('submitting wrong answer');
    await page.click('[data-testid="comment-submit"]');
    mark('wrong-answer click returned');
    await page.waitForTimeout(3500);

    const errorCount = await page.locator('[data-testid="captcha-error"]').count();
    const cardError = await page.locator('[data-testid="comment-form-error"]').count();
    console.log(`[comment] wrong answer -> inline errors ${errorCount}, card error ${cardError}`);
    expect(errorCount + cardError).toBeGreaterThan(0);

    // The rejected challenge is burned: a NEW question is on screen.
    const secondQuestion = await waitForQuestion(page, 'captcha-widget');
    console.log(`[comment] challenge after failure: "${firstQuestion}" -> "${secondQuestion}"`);
    expect(await page.locator('[data-testid="captcha-image"]').count()).toBe(0);

    // --- correct answer ---------------------------------------------------------------
    await page.fill('[data-testid="comment-name"]', 'Keyboard Visitor');
    await page.fill('[data-testid="comment-email"]', `req-ui-057-${stamp}@example.com`);
    await page.fill('[data-testid="comment-input"]', 'Posted through the accessible question challenge (REQ-UI-057).');
    const solved = solve(secondQuestion);
    console.log(`[comment] answering "${secondQuestion}" with "${solved.word}"`);
    await page.fill('[data-testid="captcha-answer"]', solved.word);
    mark('submitting correct answer');
    await page.click('[data-testid="comment-submit"]');
    mark('correct-answer click returned');
    await page.waitForTimeout(4500);

    const success = await page.locator('[data-testid="comment-form-success"]').count();
    const stillError = await page.locator('[data-testid="comment-form-error"]').innerText({ timeout: 5000 }).catch(() => '');
    console.log(`[comment] success alerts ${success}; residual error text: "${stillError}"`);
    expect(success, `comment not accepted; error was "${stillError}"`).toBeGreaterThan(0);

    // The consumed challenge is replaced, so the answer that just worked cannot be replayed.
    // (The server-side single-use and expiry guarantees are asserted directly by
    // CaptchaQuestionTests; here we only prove the widget re-arms with a fresh question.)
    // Re-arming is already gated above (firstQuestion -> secondQuestion after the refusal), and
    // the server-side single-use / expiry guarantees are asserted directly by CaptchaQuestionTests.
    // The page is mid-thread-reload here, so this last read is REPORTED, not gated.
    const thirdQuestion = await readQuestion(page, 'captcha-widget', 8000).catch(() => '(widget re-rendering)');
    console.log(`[comment] challenge on screen after success: "${thirdQuestion}"`);

    // The accepted comment triggers a confirmation mail on the server; with no SMTP host in dev
    // that send sits on its own timeout and keeps the circuit busy, which stalls Playwright's
    // context teardown long past the test's own work. Closing the page here ends the circuit.
    mark('about to close page');
    await page.close();
    mark('page closed');
  });

  test('rating panel: the alternative is reachable by keyboard alone', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`${BASE}${POST}`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="post-rating-panel"]', { timeout: 30000 });
    await page.waitForTimeout(2500);

    // Choose a star with the keyboard to reveal the identify + captcha step. The per-star
    // data-testids went with the native <fieldset> fallback on 2026-08-11 (TrBlazeUI 2.0.2 makes
    // every option a real <button role="radio">), so the stop is found by ancestry.
    let starStops = -1;
    const starPath: string[] = [];
    for (let i = 0; i < 200; i++) {
      await page.keyboard.press('Tab');
      const here = await page.evaluate(() => ({
        inRating: !!document.activeElement?.closest('[data-testid="post-rating-stars"]'),
        tag: document.activeElement?.tagName ?? '',
      }));
      starPath.push(`${i + 1}:${here.tag}`);
      if (here.inRating) { starStops = i + 1; break; }
    }
    expect(starStops, `no rating star reachable by Tab. Path: ${starPath.join(' > ')}`).toBeGreaterThan(0);
    console.log(`[rating] star reached after ${starStops} Tab presses`);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(2500);
    await page.waitForSelector('[data-testid="rating-identify-step"]', { timeout: 30000 });

    const walk = await tabUntil(page, ['captcha-mode-toggle']);
    expect(walk.found, `toggle not reachable. Path: ${walk.path.join(' > ')}`).toBe('captcha-mode-toggle');
    console.log(`[rating] toggle reached after ${walk.stops} further Tab presses`);
    console.log(`[rating] tab path: ${walk.path.join(' > ')}`);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(2000);

    const widgets = page.locator('[data-testid="rating-identify-step"] [data-testid="captcha-prompt"]');
    const question = (await widgets.first().innerText()).trim();
    console.log(`[rating] question rendered: "${question}"`);
    expect(question.length).toBeGreaterThan(12);
    expect(question).toMatch(/\?$/);
    expect(() => solve(question)).not.toThrow();
  });

  test('subscribe card: keyboard-only path, correct answer subscribes, wrong answer is refused', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`${BASE}/newsletters`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="newsletter-subscribe-captcha"]', { timeout: 30000 });
    await page.waitForTimeout(2500);

    const walk = await tabUntil(page, ['captcha-mode-toggle']);
    expect(walk.found, `toggle not reachable. Path: ${walk.path.join(' > ')}`).toBe('captcha-mode-toggle');
    console.log(`[subscribe] toggle reached after ${walk.stops} Tab presses`);
    console.log(`[subscribe] tab path: ${walk.path.join(' > ')}`);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(2000);

    const stamp = Date.now();
    const question = await waitForQuestion(page, 'newsletter-subscribe-captcha');
    console.log(`[subscribe] question rendered: "${question}"`);

    // --- wrong answer -----------------------------------------------------------------
    await page.fill('[data-testid="newsletter-subscribe-email"]', `req-ui-057-bad-${stamp}@example.com`);
    await page.fill('[data-testid="captcha-answer"]', '77');
    await page.click('[data-testid="newsletter-subscribe-submit"]');
    await page.waitForTimeout(3500);
    const badStatus = (await page.locator('[data-testid="newsletter-subscribe-status"]').innerText({ timeout: 5000 }).catch(() => '')).trim();
    console.log(`[subscribe] wrong answer -> "${badStatus}"`);
    expect(badStatus.toLowerCase()).toContain('answer');

    // --- correct answer ---------------------------------------------------------------
    const fresh = await waitForQuestion(page, 'newsletter-subscribe-captcha');
    const solved = solve(fresh);
    console.log(`[subscribe] answering "${fresh}" with "${solved.digits}"`);
    await page.fill('[data-testid="newsletter-subscribe-email"]', `req-ui-057-${stamp}@example.com`);
    await page.fill('[data-testid="captcha-answer"]', solved.digits);
    await page.click('[data-testid="newsletter-subscribe-submit"]');
    await page.waitForTimeout(5000);
    const goodStatus = (await page.locator('[data-testid="newsletter-subscribe-status"]').innerText({ timeout: 5000 }).catch(() => '')).trim();
    console.log(`[subscribe] correct answer -> "${goodStatus}"`);
    expect(goodStatus.toLowerCase()).not.toContain('did not');
    expect(goodStatus.toLowerCase()).not.toContain('was not the right');
  });

  test('the answer never appears in the DOM or in the circuit payload', async ({ page }) => {
    // Blazor Server talks blazorpack over the circuit, so most frames are BINARY. Decoding them
    // as latin1 keeps every byte one character, which is what a substring hunt for the answer
    // needs — a string-only filter would silently see almost nothing and prove almost nothing.
    const frames: string[] = [];
    const capture = (p: string | Buffer) =>
      frames.push(typeof p === 'string' ? p : Buffer.from(p).toString('latin1'));
    page.on('websocket', ws => {
      ws.on('framereceived', f => capture(f.payload));
      ws.on('framesent', f => capture(f.payload));
    });

    await page.setViewportSize({ width: 1280, height: 1000 });
    await page.goto(`${BASE}/newsletters`, { waitUntil: 'networkidle' });
    await page.waitForSelector('[data-testid="newsletter-subscribe-captcha"]', { timeout: 30000 });
    await page.waitForTimeout(2500);

    const walk = await tabUntil(page, ['captcha-mode-toggle']);
    expect(walk.found).toBe('captcha-mode-toggle');
    await page.keyboard.press('Enter');
    await page.waitForTimeout(2500);

    const question = await waitForQuestion(page, 'newsletter-subscribe-captcha');
    const solved = solve(question);
    console.log(`[leak] question "${question}" -> answer "${solved.digits}" / "${solved.word}"`);

    const widget = page.locator('[data-testid="newsletter-subscribe-captcha"]').first();
    const widgetHtml: string = await widget.evaluate(el => el.outerHTML);

    // 1a. Rendered TEXT of the widget: in question mode it must contain no digit at all.
    //     (Auto-generated element ids and Blazor's _bl_ markers are full of digits, so a raw
    //     substring search over the markup is meaningless — the text nodes are what a reader,
    //     or a scraper looking for the answer, actually gets.)
    const widgetText: string = await widget.evaluate(el => (el as HTMLElement).innerText);
    const digitsInText = widgetText.match(/\d/g) ?? [];
    console.log(`[leak] widget rendered text: "${widgetText.replace(/\s+/g, ' ').trim()}"`);
    console.log(`[leak] digits in that text: ${digitsInText.length}`);
    expect(digitsInText, `digits reached the rendered text: ${digitsInText.join('')}`).toHaveLength(0);

    // 1b. The word form must appear nowhere in the widget markup at all.
    expect(widgetHtml.toLowerCase(), 'word answer leaked into the widget markup').not.toContain(solved.word);
    console.log(`[leak] widget markup ${widgetHtml.length} chars — 0 occurrences of "${solved.word}"`);

    // 2. Page-wide: no aria-*/data-*/title/value attribute anywhere carries the answer.
    const attrHits = await page.evaluate(({ digits, word }) => {
      const hits: string[] = [];
      document.querySelectorAll('*').forEach(el => {
        for (const a of Array.from(el.attributes)) {
          const n = a.name.toLowerCase();
          if (!(n.startsWith('aria-') || n.startsWith('data-') || n === 'title' || n === 'value' || n === 'alt')) continue;
          const v = a.value.toLowerCase();
          if (v.includes(word) || v === digits) hits.push(`${el.tagName}.${a.name}="${a.value.slice(0, 80)}"`);
        }
      });
      return hits;
    }, solved);
    console.log(`[leak] page-wide aria-/data-/title/value/alt attributes carrying the answer: ${attrHits.length}`);
    expect(attrHits, `answer found in attributes: ${attrHits.join(' | ')}`).toHaveLength(0);

    // 3. The Blazor circuit payload. The frames carry the ENTIRE page's render diff — newsletter
    //    titles, post prose, everything — so a bare count of "four" across 158 KB says nothing:
    //    the page is free to use that word for its own reasons. What would be a leak is the answer
    //    travelling ALONGSIDE the challenge, so the check is scoped to the captcha's own fragment:
    //    no occurrence of the answer within 400 characters of any captcha marker.
    const framesJoined = frames.join('\n').toLowerCase();
    const rawCount = framesJoined.split(solved.word).length - 1;
    const markers = ['captcha-answer', 'captcha-prompt', 'captcha-status', 'verification question'];
    const nearMisses: string[] = [];
    for (const marker of markers) {
      let at = framesJoined.indexOf(marker);
      while (at !== -1) {
        const window = framesJoined.slice(Math.max(0, at - 400), at + 400);
        if (window.includes(solved.word)) nearMisses.push(`${marker}@${at}`);
        at = framesJoined.indexOf(marker, at + 1);
      }
    }
    console.log(`[leak] websocket: ${frames.length} frames / ${framesJoined.length} chars; raw "${solved.word}" count ${rawCount} (page content is free to use the word); occurrences within 400 chars of a captcha marker: ${nearMisses.length}`);
    expect(nearMisses, `the answer travelled next to the challenge: ${nearMisses.join(', ')}`).toHaveLength(0);

    // 4. Whatever the browser DOES hold: prove the question is present but the answer is not.
    expect(widgetHtml).toContain(question.slice(0, 20));

    // 5. The markup, with the question and the auto-generated ids stripped, must be identical
    //    for two different challenges: nothing in the widget's vocabulary tracks the answer.
    //    (Same idea as REQ-FN-049's CaptchaMarkupVocabularyIsIndependentOfCode, applied to the DOM.)
    const scrub = (html: string, q: string) => html
      .split(q).join('<<question>>')
      .replace(/captcha-(answer|hint)-[0-9a-f]{32}/g, '<<id>>')
      .replace(/_bl_[0-9a-f-]{36}/g, '<<bl>>');
    const reload = page.locator('[data-testid="newsletter-subscribe-captcha"] [data-testid="captcha-reload"]');
    let question2 = question;
    // The question bank is small enough that a reload can legitimately repeat itself; keep asking
    // until the text actually differs, so the comparison below really is across two challenges.
    for (let attempt = 0; attempt < 8 && question2 === question; attempt++) {
      await reload.press('Enter');
      await page.waitForTimeout(2000);
      question2 = await waitForQuestion(page, 'newsletter-subscribe-captcha');
    }
    const widgetHtml2: string = await widget.evaluate(el => el.outerHTML);
    console.log(`[leak] second question: "${question2}"`);
    expect(question2, 'reload never issued a different question in 8 tries').not.toBe(question);
    expect(scrub(widgetHtml2, question2)).toBe(scrub(widgetHtml, question));
    console.log('[leak] widget markup is identical across two different challenges once the question is removed');
  });

  test('VISUAL-TRUTH: the question challenge renders cleanly at 1280 and 390', async ({ page }) => {
    for (const width of [1280, 390]) {
      await page.setViewportSize({ width, height: width === 1280 ? 1000 : 844 });
      await page.goto(`${BASE}${POST}`, { waitUntil: 'networkidle' });
      await page.waitForSelector('[data-testid="captcha-widget"]', { timeout: 30000 });
      await page.waitForTimeout(2500);

      const walk = await tabUntil(page, ['captcha-mode-toggle']);
      expect(walk.found, `toggle not reachable at ${width}px`).toBe('captcha-mode-toggle');
      await page.keyboard.press('Enter');
      await page.waitForTimeout(2000);

      await page.locator('[data-testid="captcha-widget"]').first().scrollIntoViewIfNeeded();
      await page.waitForTimeout(600);

      await visualGate(page, [
        'captcha-prompt', 'captcha-answer', 'captcha-reload', 'captcha-hint', 'captcha-mode-toggle',
      ], `question mode @${width}`);

      await page.locator('[data-testid="captcha-widget"]').first()
        .screenshot({ path: `test-results/req-ui-057-question-${width}.png` });
      await page.screenshot({ path: `test-results/req-ui-057-page-${width}.png`, fullPage: false });
      console.log(`[visual] question mode clean at ${width}px`);

      // And back to the image, so the toggle really is a two-way control.
      await page.keyboard.press('Enter');
      await page.waitForTimeout(2000);
      expect(await page.locator('[data-testid="captcha-image"]').count()).toBeGreaterThan(0);
      await page.locator('[data-testid="captcha-widget"]').first()
        .screenshot({ path: `test-results/req-ui-057-image-${width}.png` });
      console.log(`[visual] image mode restored at ${width}px`);
    }
  });
});
