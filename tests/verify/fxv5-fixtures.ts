/**
 * fxv5-fixtures.ts — verify-phase writer W5 (comments, ratings, captcha, subscribe, newsletter,
 * verification, analytics), 2026-10-27. Fixture prefix `fxv5`.
 *
 * The dev DB holds no published content, so the posts the engagement specs need are created through
 * the admin UI (re-using the W1 editor helpers) and read back with psql SELECTs only. No DB writes.
 */
import { Page } from '@playwright/test';
import { execSync } from 'node:child_process';
import { createPost, deletePostByTitle, loginHard, goTo, idOf, slugOf, sql, sqlOne } from './fxv1-fixtures';

export { loginHard, goTo, sql, sqlOne };
export const BASE = process.env.TB_BASE ?? 'http://localhost:5450';
export const SHOTS = 'tests/.artifacts/verify/w5';

export const POST_TITLE = 'fxv5 Engage Post';
export const POST2_TITLE = 'fxv5 Engage Post Two';
export const DRAFT_TITLE = 'fxv5 Engage Draft';

/** Creates the fxv5 posts that are missing (published x2 + one draft). */
export async function ensurePosts(admin: Page) {
  if (!idOf(POST_TITLE)) await createPost(admin, { title: POST_TITLE, body: 'fxv5 engage body paragraph.\n\nSecond paragraph.', abstract: 'fxv5 engage abstract', mode: 'publish' });
  if (!idOf(POST2_TITLE)) await createPost(admin, { title: POST2_TITLE, body: 'fxv5 second body.', abstract: 'fxv5 two abstract', mode: 'publish' });
  if (!idOf(DRAFT_TITLE)) await createPost(admin, { title: DRAFT_TITLE, body: 'fxv5 draft body.', abstract: 'fxv5 draft abstract', mode: 'draft' });
}

export async function removePosts(admin: Page) {
  for (const t of [POST_TITLE, POST2_TITLE, DRAFT_TITLE]) await deletePostByTitle(admin, t);
}

export const postSlug = () => slugOf(POST_TITLE);
export const post2Slug = () => slugOf(POST2_TITLE);
export const postId = () => idOf(POST_TITLE);
export const post2Id = () => idOf(POST2_TITLE);

/** Unique-per-run address. Local part carries the fxv5 prefix. */
export const RUN = process.env.FXV5_NONCE ?? Date.now().toString(36);
export const fxMail = (tag: string) => `fxv5-${tag}-${RUN}@example.com`;

/** Hooks shared by every fxv5 spec: build the fixture posts in beforeAll, remove them in afterAll. */
import type { Browser } from '@playwright/test';
import { login as gateLogin, nav as gateNav } from './_gates';

export async function setupFixtures(browser: Browser) {
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const admin = await ctx.newPage();
  try {
    await loginHard(admin, 'admin');
    await ensurePosts(admin);
  } finally {
    await ctx.close();
  }
}

/** Deletes every comment whose text carries "fxv5" through the moderation grid (no delete exists for ratings, views or subscribers). */
export async function cleanupComments(browser: Browser): Promise<string[]> {
  const left: string[] = [];
  if (!sqlOne(`SELECT count(*) FROM blogcomment WHERE comment LIKE 'fxv5%' OR comment LIKE '%fxv5%'`) || sqlOne(`SELECT count(*) FROM blogcomment WHERE comment LIKE '%fxv5%'`) === '0') return left;
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const admin = await ctx.newPage();
  try {
    await gateLogin(admin, 'admin');
    await gateNav(admin, '/comments', /Comment/i);
    await admin.waitForSelector('[data-testid="comments-grid"], [data-testid="comments-empty"]', { timeout: 60000 });
    for (let guard = 0; guard < 40; guard++) {
      if (sqlOne(`SELECT count(*) FROM blogcomment WHERE comment LIKE '%fxv5%'`) === '0') break;
      await admin.fill('[data-testid="comments-search"]', 'fxv5');
      await admin.waitForTimeout(2500);
      const del = admin.locator('[data-testid="comment-delete"]');
      if ((await del.count()) === 0) break;
      await del.first().click();
      await admin.click('[data-testid="comment-delete-confirm"]');
      await admin.waitForTimeout(2500);
    }
  } catch (e) {
    left.push(String(e).slice(0, 120));
  } finally {
    await ctx.close();
  }
  const n = sqlOne(`SELECT count(*) FROM blogcomment WHERE comment LIKE '%fxv5%'`);
  if (n !== '0') left.push(`${n} fxv5 comment(s) still present`);
  return left;
}

export async function teardownFixtures(browser: Browser, opts: { comments?: boolean } = {}): Promise<string[]> {
  let left: string[] = [];
  if (process.env.FXV5_KEEP === '1') return left; // iteration aid only: leave the fixture posts in place between runs
  if (opts.comments) left = left.concat(await cleanupComments(browser));
  const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const admin = await ctx.newPage();
  try {
    await loginHard(admin, 'admin');
    await removePosts(admin);
  } catch (e) {
    left.push(String(e).slice(0, 120));
  } finally {
    await ctx.close();
  }
  if (left.length) console.log('FXV5 TEARDOWN LEFT BEHIND:', JSON.stringify(left));
  return left;
}
