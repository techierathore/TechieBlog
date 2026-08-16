/**
 * cluster-h-post-prose.spec.ts — REQ-UI-007 (TrBlazeUI 2.0.2 adoption pass, 2026-08-11).
 *
 * `PostView.razor` used to post-process Markdig's HTML string, wrapping every rendered <table> in
 * `div.markdown-table-scroll`. TrBlazeUI 2.0.2 resolves TR-059 by shipping <Prose>, whose
 * `[data-slot="prose"]` rules give table/pre/img/iframe their own overflow context, so the string
 * transform is gone and the reflow now has to come from the library.
 *
 * This file re-measures exactly what the original REQ-UI-007 fix measured, so a regression is a
 * failed assertion and not a judgement call:
 *   1. document.documentElement horizontal overflow is 0 at 390 (it was 46 before the 2026-08-09
 *      fix) and 0 at 1280.
 *   2. The rendered table is no wider than the box that scrolls it (420px -> 358px inside 358px).
 *   3. The kitchen-sink post still renders 3 <pre> + 1 <table> + 5 lists, and NEITHER the <pre>
 *      blocks nor any long inline code introduce overflow of their own now the wrapper changed.
 *
 * Run with TB_BASE=http://<wsl-gateway>:5424 (cluster H's own port).
 */
import { test, expect, Page } from '@playwright/test';
import { BASE, visualCheck } from './_gates';
import * as fs from 'fs';

const SHOTS = 'tests/.artifacts/cluster-h';

/** The post REQ-UI-007 was measured on: 3 <pre> + 1 <table> + 5 lists. */
const KITCHEN_SINK = 'the-markdown-kitchen-sink';

/** Everything the reflow assertions need, measured in the page. */
interface Reflow {
  hScroll: number;
  clientWidth: number;
  bodyPresent: boolean;
  bodyChars: number;
  proseSlot: boolean;
  legacyWrappers: number;
  preCount: number;
  tableCount: number;
  listCount: number;
  tables: { width: number; scrollWidth: number; container: number; overflowX: string }[];
  pres: { width: number; scrollWidth: number; container: number; overflowX: string }[];
  /** Any element inside the article body painted past the viewport's right edge. */
  overflowingInBody: string[];
}

async function measure(page: Page): Promise<Reflow> {
  return page.evaluate(() => {
    const body = document.querySelector('[data-testid="post-content"]') as HTMLElement | null;
    const de = document.documentElement;
    const vw = de.clientWidth;
    const boxOf = (el: HTMLElement) => {
      const parent = el.parentElement as HTMLElement;
      return {
        width: Math.round(el.getBoundingClientRect().width),
        scrollWidth: el.scrollWidth,
        container: Math.round(parent.getBoundingClientRect().width),
        overflowX: getComputedStyle(el).overflowX,
      };
    };
    // An element wider than the viewport is only a DEFECT if nothing between it and the article
    // body scrolls: content inside a `pre`/`table` scroll box is exactly the outcome being sought,
    // so counting it would fail the very fix under test.
    const confined = (el: Element) => {
      let n: Element | null = el.parentElement;
      while (n && n !== body) {
        const ox = getComputedStyle(n).overflowX;
        if (ox === 'auto' || ox === 'scroll' || ox === 'hidden') return true;
        n = n.parentElement;
      }
      return false;
    };
    const inBody = body ? Array.from(body.querySelectorAll('*')) : [];
    const overflowingInBody = inBody
      .filter((e) => {
        const r = e.getBoundingClientRect();
        return r.width > 0 && r.right > vw + 2 && !confined(e);
      })
      .map((e) => `${e.tagName.toLowerCase()}@right=${Math.round(e.getBoundingClientRect().right)}`);

    return {
      hScroll: Math.max(0, de.scrollWidth - de.clientWidth),
      clientWidth: vw,
      bodyPresent: !!body,
      bodyChars: (body?.innerText || '').length,
      proseSlot: body?.getAttribute('data-slot') === 'prose',
      legacyWrappers: document.querySelectorAll('.markdown-table-scroll').length,
      preCount: body ? body.querySelectorAll('pre').length : 0,
      tableCount: body ? body.querySelectorAll('table').length : 0,
      listCount: body ? body.querySelectorAll('ul, ol').length : 0,
      tables: body ? Array.from(body.querySelectorAll('table')).map((t) => boxOf(t as HTMLElement)) : [],
      pres: body ? Array.from(body.querySelectorAll('pre')).map((p) => boxOf(p as HTMLElement)) : [],
      overflowingInBody,
    };
  });
}

test.describe('REQ-UI-007 — the post body reflows through <Prose>', () => {
  test('the kitchen-sink post has no page-level horizontal scroll at 390 or 1280', async ({ page }) => {
    test.setTimeout(180000);
    fs.mkdirSync(SHOTS, { recursive: true });

    const observed: Record<string, Reflow> = {};
    for (const width of [390, 1280]) {
      await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
      await page.goto(`${BASE}/post/${KITCHEN_SINK}`, { waitUntil: 'domcontentloaded' });
      await expect(page.locator('[data-testid="post-content"]')).toBeVisible({ timeout: 45000 });
      await page.waitForTimeout(2500);
      const reflow = await measure(page);
      observed[String(width)] = reflow;

      // 1. The acceptance criterion REQ-UI-007 recorded: 46px -> 0px at 390.
      expect(reflow.hScroll, `documentElement horizontal overflow at ${width}`).toBe(0);

      // 2. The body is really rendering, and really rendering through <Prose>.
      expect(reflow.bodyPresent).toBe(true);
      expect(reflow.bodyChars).toBeGreaterThan(400);
      expect(reflow.proseSlot, 'post-content is the <Prose> element').toBe(true);
      expect(reflow.legacyWrappers, 'the WrapTablesInScrollContainer wrapper is gone').toBe(0);

      // 3. The content the 2026-08-09 pass counted is all still there.
      expect(reflow.preCount, `<pre> blocks at ${width}`).toBe(3);
      expect(reflow.tableCount, `<table> at ${width}`).toBe(1);
      expect(reflow.listCount, `lists at ${width}`).toBe(5);

      // 4. Each table scrolls INSIDE its own box rather than widening the article.
      for (const t of reflow.tables) {
        expect(t.overflowX, `table overflow-x at ${width}`).toMatch(/auto|scroll/);
        expect(t.width, `table width vs container at ${width}`).toBeLessThanOrEqual(t.container + 1);
      }

      // 5. The <pre> blocks and long inline code must not have picked the overflow up instead.
      for (const p of reflow.pres) {
        expect(p.overflowX, `pre overflow-x at ${width}`).toMatch(/auto|scroll/);
        expect(p.width, `pre width vs container at ${width}`).toBeLessThanOrEqual(p.container + 1);
      }
      expect(reflow.overflowingInBody, `elements painted past the viewport at ${width}`).toEqual([]);
    }

    fs.writeFileSync(`${SHOTS}/prose-reflow.json`, JSON.stringify(observed, null, 2));
  });

  test('the post view is visually clean at 1280 and 390', async ({ page }) => {
    test.setTimeout(180000);
    await page.goto(`${BASE}/post/${KITCHEN_SINK}`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('[data-testid="post-content"]')).toBeVisible({ timeout: 45000 });
    await page.waitForTimeout(2000);

    const results = [];
    for (const width of [1280, 390]) {
      const result = await visualCheck(page, `${SHOTS}/req-ui-007-post-${width}.png`, width);
      results.push(result);
      expect(result.zeroSized, `zero-sized controls at ${width}`).toEqual([]);
      expect(result.offViewport, `off-viewport controls at ${width}`).toEqual([]);
      expect(result.overlaps, `overlapping sibling controls at ${width}`).toEqual([]);
      expect(result.hScroll, `horizontal document scroll at ${width}`).toBe(0);
    }
    fs.writeFileSync(`${SHOTS}/visual-postview.json`, JSON.stringify(results, null, 2));
  });

  test('a post without a table also renders and reflows', async ({ page }) => {
    test.setTimeout(180000);
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(`${BASE}/post/postgres-indexing-for-dotnet-developers`, { waitUntil: 'domcontentloaded' });
    await expect(page.locator('[data-testid="post-content"]')).toBeVisible({ timeout: 45000 });
    await page.waitForTimeout(2000);
    const reflow = await measure(page);
    expect(reflow.hScroll).toBe(0);
    expect(reflow.bodyChars).toBeGreaterThan(200);
    expect(reflow.proseSlot).toBe(true);
  });
});
