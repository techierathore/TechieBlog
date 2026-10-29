import { test, expect } from '@playwright/test';
import { gotoPublic, solveCaptcha, psql } from './_engage-helpers';
import { visualCheck } from './_gates';
const BASE = process.env.TB_BASE!;
const SH = 'tests/.artifacts/build-phase-20261027/a/eng';
test.beforeEach(({}, ti) => ti.setTimeout(300000));
const txt = async (l: any) => ((await l.first().innerText({ timeout: 3000 }).catch(() => '')) as string).replace(/\s+/g, ' ').trim();

test('UI-053/030/056 newsletters archive + subscribe + captcha', async ({ page }) => {
  const errs: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 160)); });
  await gotoPublic(page, '/newsletters');
  const titles = await page.locator('[data-testid="newsletter-issue-title"]').allInnerTexts();
  const hrefs = await page.locator('[data-testid="newsletter-issue-title"]').evaluateAll(e => e.map(x => (x as HTMLAnchorElement).getAttribute('href')));
  console.log('ARCHIVE titles', JSON.stringify(titles), JSON.stringify(hrefs), 'numbers', JSON.stringify(await page.locator('[data-testid="newsletter-issue-number"]').allInnerTexts()));
  console.log('unsent present?', titles.some(t => /Unsent/.test(t)), 'empty-state present?', await page.locator('[data-testid="newsletter-issues-empty"]').count());
  const sc = async (w: number, h: number) => {
    await page.setViewportSize({ width: w, height: h }); await page.waitForTimeout(500);
    console.log(`ARCH scroll @${w}x${h}`, JSON.stringify(await page.evaluate(() => ({ sh: document.documentElement.scrollHeight, ch: document.documentElement.clientHeight, hs: document.documentElement.scrollWidth - document.documentElement.clientWidth, body: getComputedStyle(document.body).overflowY, html: getComputedStyle(document.documentElement).overflowY }))));
  };
  await sc(1280, 800); await sc(390, 844);
  for (const w of [1280, 390]) { const v = await visualCheck(page, `${SH}/archive-${w}.png`, w); console.log('ARCH geo', w, JSON.stringify([v.overlaps, v.hScroll, v.zeroSized])); }
  await page.setViewportSize({ width: 1280, height: 900 });

  const card = page.locator('[data-testid="newsletter-subscribe"]');
  await expect(card).toBeVisible();
  const emailBox = card.locator('[data-testid="newsletter-subscribe-email"]');
  const submit = card.locator('[data-testid="newsletter-subscribe-submit"]');
  const status = card.locator('[data-testid="newsletter-subscribe-status"]');
  // invalid
  await emailBox.fill('not-an-email'); await submit.click(); await page.waitForTimeout(1500);
  console.log('SUB invalid ->', await txt(status));
  // wrong captcha with valid email
  await emailBox.fill('fxa-newsub@example.com');
  await solveCaptcha(card, { wrong: true });
  await submit.click(); await page.waitForTimeout(2500);
  console.log('SUB wrong captcha ->', await txt(card.locator('[data-testid="captcha-error"]')), '| status', await txt(status), '| rows', psql(`select count(*) from subscriber where email='fxa-newsub@example.com'`));
  // correct
  await solveCaptcha(card); await submit.click();
  await expect(status).toBeVisible({ timeout: 30000 });
  await page.waitForTimeout(1500);
  console.log('SUB valid ->', await txt(status), '| row', psql(`select email,isconfirmed from subscriber where email='fxa-newsub@example.com'`));
  // duplicate (pending)
  await emailBox.fill('fxa-newsub@example.com'); await solveCaptcha(card); await submit.click(); await page.waitForTimeout(3500);
  console.log('SUB duplicate pending ->', await txt(status), '| rows', psql(`select count(*) from subscriber where lower(email)='fxa-newsub@example.com'`));
  // duplicate (already confirmed fixture)
  await emailBox.fill('fxa-sub1@example.com'); await solveCaptcha(card); await submit.click(); await page.waitForTimeout(3500);
  console.log('SUB duplicate confirmed ->', await txt(status), '| rows', psql(`select count(*) from subscriber where lower(email)='fxa-sub1@example.com'`), 'confirmed still', psql(`select isconfirmed from subscriber where email='fxa-sub1@example.com'`));
  await page.screenshot({ path: `${SH}/subscribe-dup.png`, fullPage: true });
  console.log('tokens for newsub', psql(`select purpose,isused from emailverificationtoken where lower(email)='fxa-newsub@example.com'`));
  console.log('console errors', JSON.stringify(errs));
});

test('UI-056 captcha: reload, keyboard, labels, source', async ({ page }) => {
  await gotoPublic(page, '/newsletters');
  const w = page.locator('[data-testid="newsletter-subscribe-captcha"], [data-testid="captcha-widget"]').first();
  const html = await w.evaluate(e => e.outerHTML);
  console.log('widget html forbidden attrs', JSON.stringify(/data-answer|type="hidden"|answer=|data-solution/i.exec(html)), 'len', html.length);
  const rawPage = await (await page.request.get(`${BASE}/newsletters`)).text();
  console.log('SSR raw page has captcha image?', /data:image\/svg/.test(rawPage), 'has challenge id attr?', /challenge/i.test(rawPage.replace(/captcha-/g, '')));
  // wait for image
  await expect(w.locator('[data-testid="captcha-image"]')).toBeVisible({ timeout: 60000 });
  const src1 = await w.locator('[data-testid="captcha-image"]').getAttribute('src');
  await w.locator('[data-testid="captcha-reload"]').click(); await page.waitForTimeout(2500);
  const src2 = await w.locator('[data-testid="captcha-image"]').getAttribute('src');
  console.log('reload gives fresh image', src1 !== src2);
  const decoded = Buffer.from((src2 || '').split(',')[1] ?? '', 'base64').toString('utf8');
  console.log('svg has <text>/glyph?', /<text|font-family|<tspan/i.test(decoded));
  // labelled
  console.log('label for answer', await page.evaluate(() => { const i = document.querySelector('[data-testid="captcha-answer"]') as HTMLInputElement; const l = i.id ? document.querySelector(`label[for="${i.id}"]`) : null; return { id: !!i.id, label: l?.textContent?.trim(), describedby: i.getAttribute('aria-describedby'), imgAlt: document.querySelector('[data-testid="captcha-image"]')?.getAttribute('alt'), reloadLabel: document.querySelector('[data-testid="captcha-reload"]')?.getAttribute('aria-label') }; }));
  // keyboard: focus reload, tab -> answer -> tab -> toggle
  await w.locator('[data-testid="captcha-reload"]').focus();
  const order: string[] = [];
  for (let i = 0; i < 3; i++) { await page.keyboard.press('Tab'); order.push(await page.evaluate(() => (document.activeElement as HTMLElement)?.getAttribute('data-testid') || document.activeElement?.tagName || '')); }
  console.log('tab order after reload', JSON.stringify(order));
  await w.locator('[data-testid="captcha-mode-toggle"]').focus(); await page.keyboard.press('Enter'); await page.waitForTimeout(2500);
  const q = await txt(w.locator('[data-testid="captcha-prompt"]'));
  console.log('keyboard toggle -> question mode:', q, '| image count', await w.locator('[data-testid="captcha-image"]').count());
  const inner = await w.evaluate(e => e.outerHTML);
  const ans = (await import('./_engage-helpers')).answerQuestion(q);
  console.log('question answer', ans, 'appears as attr value?', ans ? new RegExp(`="${ans}"`).test(inner) : 'n/a');
  await page.screenshot({ path: `${SH}/captcha-question.png` });
});

test('UI-054 newsletter issue view', async ({ page }) => {
  for (const slug of ['fxa-issue-one', 'fxa-issue-two']) {
    await gotoPublic(page, `/newsletter/${slug}`);
    const nav = page.locator('[data-testid="newsletter-view-nav"]');
    console.log(slug, 'title', await txt(page.locator('[data-testid="newsletter-view-title"]')), '| num', await txt(page.locator('[data-testid="newsletter-view-number"]')), '| pos', await txt(page.locator('[data-testid="newsletter-view-position"]')),
      '| prev', await page.locator('[data-testid="newsletter-view-previous"]').count(), await page.locator('[data-testid="newsletter-view-previous"]').getAttribute('href', { timeout: 1000 }).catch(() => null),
      '| next', await page.locator('[data-testid="newsletter-view-next"]').count(), await page.locator('[data-testid="newsletter-view-next"]').getAttribute('href', { timeout: 1000 }).catch(() => null));
    for (const w of [1280, 390]) { const v = await visualCheck(page, `${SH}/${slug}-${w}.png`, w); console.log(slug, 'geo', w, JSON.stringify([v.overlaps, v.hScroll])); }
    await page.setViewportSize({ width: 1280, height: 900 });
  }
  for (const slug of ['fxa-unsent', 'no-such-issue-zzz']) {
    const r = await page.request.get(`${BASE}/newsletter/${slug}`);
    console.log(slug, 'status', r.status());
    await page.goto(`${BASE}/newsletter/${slug}`, { waitUntil: 'domcontentloaded' }); await page.waitForTimeout(4000);
    console.log(slug, 'body', (await page.locator('main, body').first().innerText()).replace(/\s+/g, ' ').slice(0, 120), '| h1s', await page.locator('h1').count());
  }
});
