import { test, expect, Page } from '@playwright/test';

/**
 * Cluster E — the tail of REQ-NFR-026 stage 3, plus REQ-FN-057 and REQ-FN-059.
 *
 * Seven Blazor call sites were flipped onto newly written async service twins:
 *   CommentSvc.GetCommentsByPostIdAsync / GetAllCommentsAsync / ApproveCommentAsync /
 *   DeleteCommentAsync, and RatingSvc.GetAverageRatingAsync / GetRatingCountAsync /
 *   GetPostRatingStatsAsync.
 *
 * The failure mode a green build cannot see is a twin that quietly reads a DIFFERENT query — a
 * thread that returns every comment on the site, a rating widget zeroed because the async twin
 * dropped the verified filter. Asserting mere PRESENCE passes on a zeroed widget, so every figure
 * below is cross-checked against a number read straight out of PostgreSQL, and every page is walked
 * at 1280 and 390 with a screenshot kept.
 */

import { psql, psqlRows, q } from './w3-helpers';
import { loginHard } from './vall-authoring-helpers';
import * as fs from 'fs';

// Repaired 2026-10-28 (triage T3): the old version hard-coded retired demo data (slug blazor-render-modes-explained,
// fixed comment/rating counts, "CLUSTER-E" seed comments), the retired admin password and a stale port. Ground truth
// is now a read-only psql SELECT taken in the same test against the verify seed post (fxv0-), never a constant.
const BASE = process.env.TB_BASE ?? 'http://localhost:5450';
const SHOTS = 'tests/.artifacts/verify/T3/cluster-e';
const POST_SLUG = 'fxv0-markdown-showcase';
const dbPostId = () => psql(`SELECT postid FROM blogpost WHERE slug='${q(POST_SLUG)}' AND isdeleted IS NOT TRUE`);
/** Approved comments PostgreSQL holds for the post (the repository's own filter). */
const dbApproved = () => Number(psql(`SELECT count(*) FROM blogcomment WHERE postid=${dbPostId()} AND moderationstatus='Approved'`));
/** Texts of the comments that must never reach the public thread. */
const dbHiddenTexts = () => psqlRows(`SELECT replace(comment, '|', ' ') FROM blogcomment WHERE postid=${dbPostId()} AND moderationstatus<>'Approved'`).map((r) => r[0]);
fs.mkdirSync(SHOTS, { recursive: true });

let consoleErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  consoleErrors = [];
  page.on('console', m => {
    if (m.type() === 'error') consoleErrors.push(m.text());
  });
  page.on('pageerror', e => consoleErrors.push(`pageerror: ${e.message}`));
});

test.afterEach(() => {
  expect(consoleErrors, `console errors: ${consoleErrors.join(' | ')}`).toEqual([]);
});

/** Fails the check if the page scrolls horizontally or paints a zero-size body. */
async function visualTruth(page: Page, label: string) {
  const overflow = await page.evaluate(() =>
    document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow, `${label}: horizontal overflow`).toBeLessThanOrEqual(1);
  const bodyBox = await page.locator('body').boundingBox();
  expect(bodyBox!.height, `${label}: body has no height`).toBeGreaterThan(200);
}

async function walk(page: Page, path: string, name: string, assertContent: () => Promise<void>) {
  for (const [w, h, tag] of [[1280, 900, 'desktop'], [390, 844, 'mobile']] as const) {
    await page.setViewportSize({ width: w, height: h });
    await page.goto(`${BASE}${path}`, { waitUntil: 'networkidle' });
    await page.waitForTimeout(1500);
    await assertContent();
    await visualTruth(page, `${name}@${tag}`);
    await page.screenshot({ path: `${SHOTS}/e-${name}-${tag}.png`, fullPage: true });
  }
}

/** Admin routes are reached by CLICKING the nav link — the JWT lives in localStorage. */
async function gotoAdmin(page: Page, href: string) {
  const link = page.locator(`a[href="${href}"]`).first();
  await link.waitFor({ state: 'attached', timeout: 30000 });
  await link.click();
  await page.waitForURL(u => u.pathname.toLowerCase() === href.toLowerCase(), { timeout: 30000 });
  await page.waitForTimeout(3000);
}

test.describe('REQ-NFR-026 — the seven flipped call sites still render their data', () => {
  test('the post thread renders exactly the approved comments PostgreSQL holds', async ({ page }) => {
    test.skip(dbApproved() === 0, 'the verify seed post holds no approved comment right now');
    await walk(page, `/post/${POST_SLUG}`, 'postview-comments', async () => {
      // GetCommentsByPostIdAsync must reach GetAllByIdAsync, not GetAllAsync: a twin that read
      // the whole table would render every comment on the site under this one article and still
      // look like a healthy thread in a screenshot.
      const approved = dbApproved();
      await expect(page.locator('[data-testid="comments-count"]'))
        .toHaveText(`(${approved})`);
      const items = page.locator('[data-testid="comment-item"], [data-testid="comment-reply"]');
      expect(await items.count(), 'comment thread is EMPTY').toBe(approved);
      // Nothing unconfirmed or unapproved may leak into the thread.
      for (const hidden of dbHiddenTexts()) {
        await expect(page.locator('[data-testid="comments-list"]')).not.toContainText(hidden);
      }
    });
  });

  test('the post rating panel shows the verified average and count, not zeroes', async ({ page }) => {
    // Needs a published post that already holds verified ratings; one cannot be made here without the
    // captcha + email-token flow (fxv5-rating-subscribe), whose fixtures are deleted afterwards.
    const rated = psqlRows(`SELECT p.slug, count(*), round(avg(r.rating),2) FROM postrating r JOIN blogpost p ON p.postid=r.postid
      WHERE r.isemailverified AND p.published AND p.isdeleted IS NOT TRUE GROUP BY p.slug ORDER BY count(*) DESC LIMIT 1`)[0];
    test.skip(!rated, 'no published post holds a verified rating right now (ratings are created only through the captcha + token flow)');
    const [slug] = rated;
    await walk(page, `/post/${slug}`, 'postview-rating', async () => {
      // GetPostRatingStatsAsync. Presence would pass on a zeroed widget, so both figures are compared to the
      // verified-only aggregate read from psql now.
      const [, count, avg] = psqlRows(`SELECT p.slug, count(*), round(avg(r.rating),2) FROM postrating r JOIN blogpost p ON p.postid=r.postid
        WHERE r.isemailverified AND p.slug='${q(slug)}' GROUP BY p.slug`)[0];
      const shown = await page.locator('[data-testid="post-rating-average"]').innerText();
      expect(Number(shown), 'average equals the verified-only aggregate').toBeCloseTo(Number(avg), 1);
      await expect(page.locator('[data-testid="post-rating-count"]')).toContainText(`${count} rating`);
    });
  });

  test('the home grid renders each card its own verified star figures', async ({ page }) => {
    await walk(page, '/', 'home-ratings', async () => {
      // GetAverageRatingAsync + GetRatingCountAsync sat on the most-requested route. Truth: the newest published posts
      // (the 3 in the latest grid) straight from psql, and each card's rating figures compared to the verified aggregate.
      const latestCount = 3; // the home page's "latest" grid shows the 3 newest published posts
      const rows = psqlRows(`SELECT p.postid, p.title, (SELECT count(*) FROM postrating r WHERE r.postid=p.postid AND r.isemailverified),
          coalesce((SELECT round(avg(r.rating)) FROM postrating r WHERE r.postid=p.postid AND r.isemailverified),0)
        FROM blogpost p WHERE p.published AND p.isdeleted IS NOT TRUE ORDER BY COALESCE(p.publishedon, p.createdon) DESC, p.postid DESC LIMIT ${latestCount}`);
      expect(rows.length, 'the database holds published posts to render').toBeGreaterThan(0);
      const cards = page.locator('[data-testid="post-card"]');
      await expect(cards, 'home grid lists exactly the newest published posts').toHaveCount(rows.length);
      expect(await page.locator('[data-testid="post-card-title"]').allInnerTexts(), 'newest first, same rows as the database')
        .toEqual(rows.map((r) => r[1]));
      for (const [, title, count, stars] of rows.filter((r) => Number(r[2]) > 0)) {
        const card = cards.filter({ hasText: title }).first();
        await expect(card.locator('[data-testid="total-rating-value"]')).toHaveText(`(${count})`);
        await expect(card.locator('[data-testid="star-rating-text"]')).toContainText(`${stars}`);
      }
      const drafts = psqlRows(`SELECT title FROM blogpost WHERE published=false AND isdeleted IS NOT TRUE`).map((r) => r[0]);
      for (const d of drafts) expect(await page.locator('body').innerText(), `draft "${d}" never listed`).not.toContain(d);
    });
  });

  test('the admin comment grid loads every comment through the async twin', async ({ page }) => {
    await page.setViewportSize({ width: 1280, height: 900 });
    await loginHard(page, 'admin');
    await gotoAdmin(page, '/CommentsList');

    // GetAllCommentsAsync: the grid must hold every row in every state. Compare with psql taken now.
    const dbAll = Number(psql('SELECT count(*) FROM blogcomment'));
    test.skip(dbAll === 0, 'the database holds no comment right now');
    const rows = page.locator('tbody tr');
    await expect.poll(async () => await rows.count(), { timeout: 30000 }).toBe(Math.min(dbAll, 20));
    await visualTruth(page, 'commentslist@desktop');
    await page.screenshot({ path: `${SHOTS}/e-commentslist-desktop.png`, fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(1200);
    await visualTruth(page, 'commentslist@mobile');
    await page.screenshot({ path: `${SHOTS}/e-commentslist-mobile.png`, fullPage: true });
  });

  test('moderation still works: approve and delete run through the async twins', async ({ page }) => {
    test.skip(true, 'needs a PendingApproval comment, which exists only after the captcha + email-token comment flow (fxv5-comments REQ-FN-022 builds one); the CLUSTER-E seed rows this test used no longer exist and approving/deleting another writer\'s comment is not allowed');
    await page.setViewportSize({ width: 1280, height: 900 });
    await loginHard(page, 'admin');
    await gotoAdmin(page, '/CommentsList');

    // The grid pages at 20 rows, so the seeded rows are isolated with the search box rather than
    // hunted for; both are removed again by the harness after this spec.
    await page.fill('[data-testid="comments-search"]', 'CLUSTER-E');
    await page.waitForTimeout(2000);

    // ---- Approve, through ApproveCommentAsync. The seeded row is PendingApproval with a
    // confirmed address, so the twin's "address not confirmed" guard must let it through.
    const pendingRow = page.locator('tr', { hasText: 'CLUSTER-E approve me' }).first();
    await expect(pendingRow, 'the seeded PendingApproval row is not in the grid').toBeVisible();
    await pendingRow.locator('[data-testid="comment-approve"]').click();
    await page.waitForTimeout(2500);
    await expect(page.locator('body')).toContainText('Comment approved.');
    await page.screenshot({ path: `${SHOTS}/e-moderation-approved.png`, fullPage: true });

    // The status badge is re-read from the database rather than from the mutated view model:
    // TrBlazeUI's DataTable does not repaint a CellTemplate when a bound item's property changes
    // in place (TR-065), which is pre-existing and identical on the synchronous path. Reloading
    // the grid both dodges that and re-exercises GetAllCommentsAsync.
    await gotoAdmin(page, '/admin');
    await gotoAdmin(page, '/CommentsList');
    await page.fill('[data-testid="comments-search"]', 'CLUSTER-E');
    await page.waitForTimeout(2000);
    await expect(page.locator('tr', { hasText: 'CLUSTER-E approve me' }).first())
      .toContainText('Approved');

    // ---- Delete, through DeleteCommentAsync.
    const deleteRow = page.locator('tr', { hasText: 'CLUSTER-E delete me' }).first();
    await expect(deleteRow, 'the seeded throwaway row is not in the grid').toBeVisible();
    await deleteRow.locator('[data-testid="comment-delete"]').click();
    await page.waitForTimeout(1500);
    await page.click('[data-testid="comment-delete-confirm"]');
    await page.waitForTimeout(2500);
    await expect(page.locator('body')).toContainText('Comment deleted successfully.');
    await expect(page.locator('tr', { hasText: 'CLUSTER-E delete me' })).toHaveCount(0);
    await page.screenshot({ path: `${SHOTS}/e-moderation-deleted.png`, fullPage: true });
  });
});

test.describe('REQ-FN-057 — the detail page and the tag archive date by PublishedOn', () => {
  /** "October 27, 2026" - the detail page's date format. */
  const longDate = (col: string, id: string) => psql(`SELECT to_char(${col}, 'FMMonth FMDD, YYYY') FROM blogpost WHERE postid=${id}`);

  test('the post detail page shows the publication date, not the creation date', async ({ page }) => {
    const id = dbPostId();
    const published = longDate('publishedon', id);
    const created = longDate('createdon', id);
    test.skip(published === created, `fixture createdon and publishedon fall on the same day (${published}); the clause cannot be told apart`);

    await walk(page, `/post/${POST_SLUG}`, 'postview-date', async () => {
      await expect(page.locator('[data-testid="post-date"]')).toHaveText(published);
    });
  });

  test('the tag archive dates its cards by the publication date', async ({ page }) => {
    const row = psqlRows(`SELECT t.tagname, p.postid, p.title FROM tag t JOIN posttag pt ON pt.tagid=t.tagid JOIN blogpost p ON p.postid=pt.postid
      WHERE p.published AND p.isdeleted IS NOT TRUE AND to_char(p.publishedon,'YYYY-MM-DD') <> to_char(p.createdon,'YYYY-MM-DD') LIMIT 1`)[0];
    test.skip(!row, 'no published tagged post has a publication date different from its creation date');
    const [tag, id, title] = row;
    const expected = psql(`SELECT to_char(publishedon, 'Mon FMDD, YYYY') FROM blogpost WHERE postid=${id}`);

    await walk(page, `/tag/${encodeURIComponent(tag)}`, 'tag-archive-date', async () => {
      const card = page.locator('[data-testid="posts-grid"] [data-testid="post-card"]').filter({ hasText: title }).first();
      await expect(card.locator('[data-testid="post-card-date"]')).toHaveText(expected);
    });
  });
});
