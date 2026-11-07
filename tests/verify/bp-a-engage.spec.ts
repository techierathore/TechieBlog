import { test, expect, Page } from '@playwright/test';
import { gotoPublic, solveCaptcha, psql } from './_engage-helpers';
import { visualCheck } from './_gates';

const BASE = process.env.TB_BASE!;
const SH = 'tests/.artifacts/build-phase-20261027/a/eng';
test.beforeEach(({}, ti) => ti.setTimeout(300000));
const txt = async (l: any) => ((await l.first().innerText().catch(() => '')) as string).replace(/\s+/g, ' ').trim();

test('UI-027 rating: anon rate, unconfirmed not counted, confirmed counted; captcha wrong-then-right', async ({ page }) => {
  const errs: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 160)); });
  await gotoPublic(page, '/post/fxa-post-3');
  const panel = page.locator('[data-testid="post-rating-panel"]');
  await expect(panel).toBeVisible();
  console.log('RATING initial', await txt(panel.locator('[data-testid="post-rating-average"]')), '|', await txt(panel.locator('[data-testid="post-rating-count"]')));
  const stars = panel.locator('[data-testid="post-rating-stars"] [role="radio"]');
  console.log('star radios', await stars.count());
  await stars.nth(3).click();
  await expect(panel.locator('[data-testid="rating-identify-step"]')).toBeVisible();
  await panel.locator('[data-testid="rating-email"]').fill('fxa-rate@example.com');
  // wrong captcha
  const prompt1 = await panel.locator('[data-testid="captcha-prompt"]').innerText();
  const img1 = await panel.locator('[data-testid="captcha-image"]').getAttribute('src', { timeout: 1500 }).catch(() => null);
  const s1 = await solveCaptcha(panel, { wrong: true });
  console.log('captcha q1', s1.question);
  await panel.locator('[data-testid="rating-submit"]').click();
  await expect(panel.locator('[data-testid="captcha-error"]')).toBeVisible({ timeout: 20000 });
  console.log('CAPTCHA wrong -> error:', await txt(panel.locator('[data-testid="captcha-error"]')), '| status:', await txt(panel.locator('[data-testid="rating-form-error"]')));
  console.log('rows after wrong captcha', psql(`select count(*) from postrating where lower(email)='fxa-rate@example.com'`));
  await page.screenshot({ path: `${SH}/rating-wrong-captcha.png`, fullPage: true });
  const q2 = await txt(panel.locator('[data-testid="captcha-prompt"]'));
  const imgNow = await panel.locator('[data-testid="captcha-image"]').getAttribute('src', { timeout: 1500 }).catch(() => null);
  console.log('fresh challenge? prompt before/after', JSON.stringify([prompt1.trim(), q2]), 'imgChanged', img1 !== imgNow, 'answer cleared', JSON.stringify(await panel.locator('[data-testid="captcha-answer"]').inputValue()));
  // correct
  const s2 = await solveCaptcha(panel);
  console.log('captcha q2', s2.question, '->', s2.answer);
  await panel.locator('[data-testid="rating-submit"]').click();
  await expect(panel.locator('[data-testid="rating-form-success"]')).toBeVisible({ timeout: 30000 });
  console.log('RATING success msg:', await txt(panel.locator('[data-testid="rating-form-success"]')));
  console.log('RATING row', psql(`select rating,isemailverified from postrating where lower(email)='fxa-rate@example.com'`));
  console.log('RATING after unconfirmed', await txt(panel.locator('[data-testid="post-rating-average"]')), '|', await txt(panel.locator('[data-testid="post-rating-count"]')));
  // confirm
  const tok = psql(`select token||'|'||purpose from emailverificationtoken where lower(email)='fxa-rate@example.com' and isused=false order by tokenid desc limit 1`);
  console.log('TOKEN purpose', tok.split('|')[1]);
  await page.goto(`${BASE}/verify/${tok.split('|')[0]}`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(12000);
  console.log('VERIFY page', await txt(page.locator('[data-testid="verify-card"]')));
  console.log('RATING row after confirm', psql(`select rating,isemailverified from postrating where lower(email)='fxa-rate@example.com'`));
  await page.waitForTimeout(500);
  await gotoPublic(page, '/post/fxa-post-3');
  console.log('RATING after confirm (reload; cached?)', await txt(page.locator('[data-testid="post-rating-average"]')), '|', await txt(page.locator('[data-testid="post-rating-count"]')));
  for (const w of [1280, 390]) { const v = await visualCheck(page, `${SH}/post3-${w}.png`, w); console.log('POST geo', w, JSON.stringify([v.overlaps, v.hScroll])); }
  console.log('console errors', JSON.stringify(errs));
});

test('UI-029 comments: anon comment, email never rendered, unconfirmed/unapproved never shown', async ({ page }) => {
  // existing fixtures on post 1
  const raw = await (await page.request.get(`${BASE}/post/fxa-post-1`)).text();
  console.log('RAW has approved text?', /fxa Reader/.test(raw), 'pending?', /fxa Pending/.test(raw), 'email reader?', /fxa-reader@example\.com/.test(raw), 'email pending?', /fxa-pending@example\.com/.test(raw));
  await gotoPublic(page, '/post/fxa-post-1');
  const dom = await page.content();
  console.log('DOM reader email', /fxa-reader@/.test(dom), 'pending email', /fxa-pending@/.test(dom), 'comment items', await page.locator('[data-testid="comment-item"]').count(), 'authors', JSON.stringify(await page.locator('[data-testid="comment-author"]').allInnerTexts()));
  // new comment on post 2
  await gotoPublic(page, '/post/fxa-post-2');
  const form = page.locator('[data-testid="comment-form"]');
  await form.scrollIntoViewIfNeeded();
  await form.locator('[data-testid="comment-submit"]').click();
  await page.waitForTimeout(1500);
  console.log('EMPTY submit errors', await txt(form.locator('[data-testid="comment-name-error"]')), '|', await txt(form.locator('[data-testid="comment-email-error"]')), '|', await txt(form.locator('[data-testid="comment-body-error"]')));
  await form.locator('[data-testid="comment-name"]').fill('fxa Anon');
  await form.locator('[data-testid="comment-email"]').fill('fxa-comment@example.com');
  await form.locator('[data-testid="comment-input"]').fill('fxa anonymous comment body');
  await solveCaptcha(form, { wrong: true });
  await form.locator('[data-testid="comment-submit"]').click();
  await expect(form.locator('[data-testid="captcha-error"]')).toBeVisible({ timeout: 20000 });
  console.log('COMMENT wrong captcha ->', await txt(form.locator('[data-testid="captcha-error"]')), 'rows', psql(`select count(*) from blogcomment where lower(email)='fxa-comment@example.com'`));
  const s = await solveCaptcha(form);
  await form.locator('[data-testid="comment-submit"]').click();
  await expect(form.locator('[data-testid="comment-form-success"]')).toBeVisible({ timeout: 30000 });
  console.log('COMMENT success msg', await txt(form.locator('[data-testid="comment-form-success"]')));
  console.log('COMMENT row', psql(`select published,isemailverified,moderationstatus from blogcomment where lower(email)='fxa-comment@example.com'`));
  console.log('visible after submit?', /fxa anonymous comment body/.test(await page.content()));
  await page.screenshot({ path: `${SH}/comment-submitted.png`, fullPage: true });
  // confirm via token
  const tok = psql(`select token||'|'||purpose from emailverificationtoken where lower(email)='fxa-comment@example.com' and isused=false order by tokenid desc limit 1`);
  console.log('TOKEN purpose', tok.split('|')[1]);
  await page.goto(`${BASE}/verify/${tok.split('|')[0]}`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(12000);
  console.log('VERIFY', await txt(page.locator('[data-testid="verify-card"]')));
  console.log('COMMENT row after verify', psql(`select published,isemailverified,moderationstatus from blogcomment where lower(email)='fxa-comment@example.com'`));
  const raw2 = await (await page.request.get(`${BASE}/post/fxa-post-2`)).text();
  console.log('COMMENT visible in raw after verify (unmoderated)?', /fxa anonymous comment body/.test(raw2), 'email in raw?', /fxa-comment@example/.test(raw2));
  await gotoPublic(page, '/post/fxa-post-2');
  console.log('COMMENT visible in DOM after verify?', /fxa anonymous comment body/.test(await page.content()), /fxa-comment@example/.test(await page.content()));
  for (const w of [1280, 390]) { const v = await visualCheck(page, `${SH}/post2-${w}.png`, w); console.log('POST2 geo', w, JSON.stringify([v.overlaps, v.hScroll])); }
});
