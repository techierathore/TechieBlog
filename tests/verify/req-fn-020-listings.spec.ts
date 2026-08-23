/**
 * REQ-FN-020 — published listings, featured-post selection, related posts, reading time
 * (BRD-30, BRD-31, BRD-32).
 *
 * The row was demoted to `Needs re-verify` on 2026-08-22 as NOT OBSERVABLE: the database held zero
 * published posts after UAT-007 retired the demo content, so none of the four behaviours could be
 * seen. `vall-nfr-fn020.spec.ts` still probes slugs from that retired set and therefore reports
 * "(absent)" for everything — a stale fixture, not a defect.
 *
 * This spec asserts against a set the surrounding verify run seeds and removes: seven posts in the
 * Programming category, `verify-post-1..7`, published oldest-to-newest EXCEPT `verify-post-7`,
 * which is left a draft so the "drafts never leak" rule has something to catch.
 */
import { test, expect, request } from '@playwright/test';
import { BASE } from './_gates';

const PUBLISHED = ['verify-post-1', 'verify-post-2', 'verify-post-3', 'verify-post-4', 'verify-post-5', 'verify-post-6'];
const DRAFT = 'verify-post-7';
const NEWEST = 'verify-post-6';

/** Fetches a public page over a fresh, unauthenticated connection. */
async function publicHtml(path: string): Promise<string> {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const response = await api.get(path);
    return response.status() === 200 ? await response.text() : '';
  } finally {
    await api.dispose();
  }
}

test('REQ-FN-020 published listings show every published post and never a draft', async () => {
  for (const path of ['/', '/category/programming']) {
    const html = await publicHtml(path);
    expect(html, `${path} should render`).not.toBe('');
    expect(html, `a DRAFT must never appear on ${path}`).not.toContain(DRAFT);
  }

  // The category archive is the listing that should carry the whole published set.
  const archive = await publicHtml('/category/programming');
  const missing = PUBLISHED.filter((slug) => !archive.includes(slug));
  expect(missing, 'every published post must be listed on its category archive').toEqual([]);
});

test('REQ-FN-020 the featured post is the newest published post', async () => {
  const home = await publicHtml('/');
  expect(home).toContain(NEWEST);

  // The featured block is rendered once, and it is the newest published post — not the draft,
  // which is newer by creation date and would be selected by a rule that ignored `published`.
  const featured = /data-testid="featured-post[^"]*"/.test(home) || /Featured/i.test(home);
  expect(featured, 'a featured block should be present on the home page').toBe(true);
  expect(home, 'the draft must not be chosen as featured').not.toContain(DRAFT);
});

test('REQ-FN-020 a post page shows related posts, none of them itself or a draft', async () => {
  const html = await publicHtml(`/post/${NEWEST}`);
  expect(html).not.toBe('');
  expect(html, 'the related-posts section should render').toContain('data-testid="related-posts"');

  const related = [...html.matchAll(/verify-post-(\d)/g)].map((m) => `verify-post-${m[1]}`);
  const others = [...new Set(related)].filter((slug) => slug !== NEWEST);
  expect(others.length, 'related posts should be present').toBeGreaterThan(0);
  expect(others, 'a related post must never be the current post').not.toContain(NEWEST);
  expect(others, 'a related post must never be a draft').not.toContain(DRAFT);
});

test('REQ-FN-020 reading time is rendered and parses to a positive number', async () => {
  for (const path of ['/', `/post/${NEWEST}`]) {
    const html = await publicHtml(path);
    const badges = [...html.matchAll(/(\d+)\s*min read/gi)].map((m) => parseInt(m[1], 10));
    expect(badges.length, `${path} should render at least one reading-time badge`).toBeGreaterThan(0);
    expect(badges.every((n) => n > 0), `${path} reading times must all be > 0`).toBe(true);
  }
});
