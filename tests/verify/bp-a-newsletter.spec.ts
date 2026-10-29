import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import { nav, visualCheck } from './_gates';
import { psql, signIn, settle } from './vall-resume-helpers';

/** Build-phase 2026-10-27 cluster A verify: REQ-UI-043. Recipients restricted to the fxa-sub segment. */
const OUT = 'tests/.artifacts/build-phase-20261027/a/newsletter';
test.beforeAll(() => fs.mkdirSync(OUT, { recursive: true }));

// retired 2026-10-27 (W5): needs fxa- subscribers the builders seeded with psql and then deleted; superseded by fxv5-newsletter REQ-UI-043.
test.skip('retired: compose, preview, dispatch to fxa- segment, outcome and history', async ({ page }) => {
  test.setTimeout(500000);
  const errors: string[] = [];
  page.on('console', (m) => m.type() === 'error' && errors.push(m.text()));
  try {
    console.log('LANDING', await signIn(page, 'admin'));
    await nav(page, '/admin/newsletter', /Newsletter/i);
    await settle(page);
    const hist0 = await page.locator('[data-testid="history-row-title"]').allInnerTexts();
    console.log('HISTORY BEFORE', hist0);
    for (const w of [1280, 390]) {
      const v = await visualCheck(page, `${OUT}/composer-${w}.png`, w);
      console.log(`GEOM composer@${w}`, JSON.stringify({ o: v.overlaps, z: v.zeroSized, off: v.offViewport, h: v.hScroll, c: v.consoleErrors }));
      expect(v.hScroll).toBe(0);
    }
    await page.setViewportSize({ width: 1280, height: 1000 });

    await page.fill('[data-testid="newsletter-subject"]', 'fxa composer test issue');
    await page.fill('[data-testid="newsletter-summary"]', 'fxa composer summary');
    const body = page.locator('[data-testid="newsletter-body"] textarea').first();
    await body.fill('## fxa heading\n\nSome **bold** fxa body text.');
    await page.click('[data-testid="newsletter-tab-preview"]');
    const prev = page.locator('[data-testid="newsletter-preview"]');
    await expect(prev.locator('h2')).toContainText('fxa heading');
    await expect(prev.locator('strong')).toContainText('bold');
    await expect(page.locator('[data-testid="newsletter-preview-footer"]')).toBeVisible();
    await page.screenshot({ path: `${OUT}/preview-1280.png` });

    await page.click('[data-testid="newsletter-save-draft"]');
    await expect(page.locator('[data-testid="newsletter-status-message"]')).toBeVisible({ timeout: 20000 });
    console.log('SAVE MSG', await page.locator('[data-testid="newsletter-status-message"]').innerText(), '| badge', await page.locator('[data-testid="newsletter-status-badge"]').innerText());
    console.log('DB', psql("select newsletterid||'|'||status||'|'||title from newsletter where title like 'fxa composer%'"));

    // audience = segment fxa-sub
    await page.click('#audience-segment');
    await page.fill('[data-testid="newsletter-segment-filter"]', 'fxa-sub');
    await page.waitForTimeout(1500);
    console.log('COUNT AFTER FILL (no blur)', await page.locator('[data-testid="newsletter-recipient-count"]').innerText());
    await page.keyboard.press('Tab');
    await page.waitForTimeout(1500);
    await expect.poll(async () => (await page.locator('[data-testid="newsletter-recipient-count"]').innerText()), { timeout: 20000 }).toMatch(/^\d+ recipient/);
    const cnt = await page.locator('[data-testid="newsletter-recipient-count"]').innerText();
    console.log('RECIPIENT COUNT', cnt, '| DB fxa-sub matches', psql("select string_agg(email||':'||isconfirmed::text, ',') from subscriber where email like '%fxa-sub%'"));
    expect(cnt).toMatch(/^[12] recipient/);

    await page.click('[data-testid="newsletter-send"]');
    const dlg = page.locator('[data-testid="newsletter-send-dialog"]');
    await expect(dlg).toBeVisible();
    console.log('CONFIRM TEXT', (await dlg.innerText()).replace(/\s+/g, ' '));
    await page.screenshot({ path: `${OUT}/confirm-1280.png` });
    await dlg.locator('[data-testid="newsletter-send-confirm"]').click();
    const seenProgress = await page.locator('[data-testid="newsletter-send-progress"]').waitFor({ timeout: 4000 }).then(() => true, () => false);
    console.log('PROGRESS OBSERVED', seenProgress);
    await expect(page.locator('[data-testid="newsletter-send-outcome"]')).toBeVisible({ timeout: 90000 });
    console.log('OUTCOME', (await page.locator('[data-testid="newsletter-send-outcome"]').innerText()).replace(/\s+/g, ' '));
    console.log('STATUS BADGE', await page.locator('[data-testid="newsletter-status-badge"]').innerText());
    await expect(page.locator('[data-testid="newsletter-delivery-list"]')).toBeVisible({ timeout: 20000 });
    const deliv = await page.locator('[data-testid="delivery-row-email"]').allInnerTexts();
    console.log('DELIVERY ROWS', deliv, await page.locator('[data-testid="delivery-row-status"]').allInnerTexts());
    expect(deliv.every((e) => e.startsWith('fxa-sub'))).toBe(true);
    const hist1 = await page.locator('[data-testid="history-row-title"]').allInnerTexts();
    console.log('HISTORY AFTER', hist1, await page.locator('[data-testid="history-row-status"]').allInnerTexts());
    expect(hist1).toContain('fxa composer test issue');
    console.log('DB AFTER', psql("select newsletterid||'|'||status||'|'||recipientcount from newsletter where title like 'fxa composer%'"));
    await page.screenshot({ path: `${OUT}/after-send-1280.png`, fullPage: true });
    for (const w of [1280, 390]) {
      const v = await visualCheck(page, `${OUT}/after-send-${w}.png`, w);
      console.log(`GEOM after-send@${w}`, JSON.stringify({ o: v.overlaps, z: v.zeroSized, off: v.offViewport, h: v.hScroll, c: v.consoleErrors }));
      expect(v.hScroll).toBe(0);
    }
  } finally {
    console.log('CONSOLE ERRORS', errors);
  }
});
