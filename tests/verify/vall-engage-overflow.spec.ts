/**
 * vall-engage-overflow.spec.ts — pins down the horizontal overflow the §4b gate found on the post
 * page at 390 px, so the finding names an element instead of a number.
 */
import { test } from '@playwright/test';
import { gotoPublic } from './_engage-helpers';

test.setTimeout(180000);

/**
 * Two posts: one whose Markdown contains a table, one whose does not. If only the first overflows,
 * the finding belongs to the article-body renderer, not to the engagement components on the page.
 */
const SLUGS = ['blazor-render-modes-explained', 'blazor-circuits-and-state'];

for (const slug of SLUGS) {
test(`REQ-UI-029/REQ-UI-027 post page horizontal overflow at 390px — ${slug}`, async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await gotoPublic(page, `/post/${slug}`);
  await page.evaluate(() => window.scrollTo(0, 0));
  await page.waitForTimeout(2000);

  const report = await page.evaluate(() => {
    const vw = document.documentElement.clientWidth;
    const offenders: any[] = [];
    document.querySelectorAll('*').forEach((el) => {
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0) return;
      const right = r.left + r.width + window.scrollX;
      if (right > vw + 1) {
        const s = getComputedStyle(el);
        offenders.push({
          tag: el.tagName.toLowerCase(),
          testid: el.getAttribute('data-testid'),
          cls: (typeof el.className === 'string' ? el.className : '').slice(0, 70),
          right: Math.round(right),
          width: Math.round(r.width),
          overflowX: s.overflowX,
          parentOverflowX: el.parentElement ? getComputedStyle(el.parentElement).overflowX : null,
        });
      }
    });
    // Deepest offenders first — the shallow ones are just containers of the real culprit.
    return {
      vw,
      scrollWidth: document.documentElement.scrollWidth,
      bodyScrollWidth: document.body.scrollWidth,
      hScroll: document.documentElement.scrollWidth - vw,
      offenders: offenders.sort((a, b) => b.right - a.right).slice(0, 15),
    };
  });

  console.log(`OVERFLOW ${slug} hScroll=${report.hScroll} vw=${report.vw} scrollWidth=${report.scrollWidth}`);
  console.log('OVERFLOW offenders ' + JSON.stringify(report.offenders));
});
}
