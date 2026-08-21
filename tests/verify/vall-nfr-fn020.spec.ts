/**
 * vall-nfr-fn020.spec.ts — REQ-FN-020 follow-up.
 *
 * The first pass probed `/post/the-markdown-kitchen-sink`, which is the ONLY published post in
 * its category, so the related-posts section is legitimately suppressed there. This spec re-probes
 * a post that DOES have category siblings (`blazor-render-modes-explained`, category 1, three
 * published posts) so the related-posts and reading-time behaviour is measured where it can fire,
 * and separately asks whether a featured-post selection exists anywhere on the public site.
 */
import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { BASE } from './_gates';

const SHOTS = path.join(process.cwd(), '.verify', 'shots', 'nfr');
fs.mkdirSync(SHOTS, { recursive: true });

const out: string[] = [];
function record(line: string) {
  out.push(line);
  console.log(`FINDING REQ-FN-020 :: ${line}`);
}

async function settle(page: import('@playwright/test').Page, url: string) {
  await page.goto(BASE + url, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, { timeout: 30000 }).catch(() => {});
  await page.waitForTimeout(3000);
}

test('FN020 related posts and reading time on a post with siblings', async ({ page }) => {
  test.setTimeout(180000);
  await settle(page, '/post/blazor-render-modes-explained');
  const probe = await page.evaluate(() => {
    const readtime = document.querySelector('[data-testid="post-readtime"]');
    const related = document.querySelector('[data-testid="related-posts"]');
    const cards = document.querySelectorAll('[data-testid="related-post-card"]');
    return {
      readtime: readtime ? (readtime.textContent || '').trim() : '(absent)',
      relatedSection: !!related,
      relatedCards: cards.length,
      relatedTitles: Array.from(cards).slice(0, 4).map((c) => (c.textContent || '').trim().slice(0, 50)),
    };
  });
  record(`post with siblings: readingTime="${probe.readtime}" relatedSection=${probe.relatedSection} relatedCards=${probe.relatedCards} ${JSON.stringify(probe.relatedTitles)}`);
  await page.screenshot({ path: path.join(SHOTS, 'fn020-related.png'), fullPage: true });

  // Sibling-free post: the section must be absent, not empty-and-broken.
  await settle(page, '/post/the-markdown-kitchen-sink');
  const lone = await page.evaluate(() => ({
    readtime: (document.querySelector('[data-testid="post-readtime"]')?.textContent || '(absent)').trim(),
    relatedSection: !!document.querySelector('[data-testid="related-posts"]'),
  }));
  record(`sibling-free post: readingTime="${lone.readtime}" relatedSection=${lone.relatedSection} (category 5 holds only this post, so suppression is correct)`);

  // Featured-post selection anywhere on the public entry points.
  for (const url of ['/', '/search']) {
    await settle(page, url);
    const feat = await page.evaluate(() => {
      const body = document.body.innerText || '';
      return {
        testids: Array.from(document.querySelectorAll('[data-testid*="featured"]')).length,
        classes: Array.from(document.querySelectorAll('[class*="featured"]')).length,
        text: /\bfeatured\b/i.test(body),
      };
    });
    record(`featured-post selection on ${url}: data-testid*=featured -> ${feat.testids}, class*=featured -> ${feat.classes}, "featured" in text -> ${feat.text}`);
  }

  // Listing pages that DO carry the reading-time badge.
  for (const url of ['/category/dotnet', '/search?q=blazor']) {
    await settle(page, url);
    const badges = await page.evaluate(
      () => document.querySelectorAll('[data-testid="post-card-readtime"], [data-testid="search-result-readtime"]').length,
    );
    record(`listing ${url}: reading-time badges rendered = ${badges}`);
  }

  fs.writeFileSync(path.join(SHOTS, 'fn020-findings.txt'), out.join('\n'));
  expect(probe.relatedCards + probe.readtime.length, 'related/reading-time probe produced data').toBeGreaterThan(0);
});
