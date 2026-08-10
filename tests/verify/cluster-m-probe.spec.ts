/*
  cluster-m-probe.spec.ts — throwaway probe backing two judgement calls in the TR-054 pass.

    1. TR-054b. The two `role="tab"` buttons with no `role="tablist"` parent on
       /admin/newsletter are `tabs-96-trigger-{write,preview}` — the Write/Preview toggle
       INSIDE TrBlazeUI's own `MarkdownEditor`, not the composer's nested Tabs. They carry
       the Tabs id scheme, so they are real `TabsTrigger`s whose list wrapper is a plain
       div. The question that decides whether `role="tablist"` may be injected from outside
       is whether roving arrow-key navigation actually works on them: declaring a tablist
       promises AT that arrows move between tabs.

    2. TrBlazeUI emits `aria-selected=""` (EMPTY) on the active tab and omits it entirely
       on inactive ones. ARIA falls back to the attribute default for an empty token value,
       so this probe reads Chrome's own accessibility tree (CDP Accessibility domain) to
       see whether the selected state reaches AT at all.
*/
import { test, Page } from '@playwright/test';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5394';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]', { timeout: 30000 });
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(2500);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2500);
}

test('probe orphan tabs keyboard and selected state', async ({ page, context }) => {
  test.setTimeout(600000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);

  // ---- 1. the orphan (no-tablist) pair -----------------------------------
  await page.evaluate(() => (window as any).Blazor.navigateTo('/admin/newsletter'));
  await page.waitForTimeout(5000);

  const orphanIds = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[role="tab"]'))
      .filter(t => !t.closest('[role="tablist"]'))
      .map(t => t.id));
  console.log('ORPHAN IDS: ' + JSON.stringify(orphanIds));

  if (orphanIds.length > 0) {
    const wrapper = await page.evaluate((id: string) => {
      const el = document.getElementById(id)!;
      const p = el.parentElement!;
      return {
        parentTag: p.tagName,
        parentAttrs: Array.from(p.attributes).map(a => `${a.name}="${a.value}"`).join(' '),
        parentChildren: Array.from(p.children).map(c => ({
          tag: c.tagName,
          role: c.getAttribute('role'),
          text: (c.textContent ?? '').trim().slice(0, 20),
        })),
      };
    }, orphanIds[0]);
    console.log('ORPHAN WRAPPER: ' + JSON.stringify(wrapper, null, 1));

    // Do arrow keys rove between them? This is what a role="tablist" would promise.
    await page.locator(`#${orphanIds[0]}`).focus();
    await page.waitForTimeout(400);
    const before = await page.evaluate(() => document.activeElement?.id);
    await page.keyboard.press('ArrowRight');
    await page.waitForTimeout(700);
    const afterRight = await page.evaluate(() => document.activeElement?.id);
    await page.keyboard.press('End');
    await page.waitForTimeout(700);
    const afterEnd = await page.evaluate(() => document.activeElement?.id);
    console.log(`ORPHAN KEYBOARD: focus=${before} ArrowRight=${afterRight} End=${afterEnd}`);
  }

  // ---- 2. is aria-selected="" visible to AT? ------------------------------
  await page.evaluate(() => (window as any).Blazor.navigateTo('/settings'));
  await page.waitForTimeout(4500);

  const raw = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[role="tab"]')).map(t => ({
      text: (t.textContent ?? '').trim().slice(0, 16),
      dataState: t.getAttribute('data-state'),
      ariaSelected: JSON.stringify(t.getAttribute('aria-selected')),
      tabindex: t.getAttribute('tabindex'),
    })));
  console.log('RAW /settings: ' + JSON.stringify(raw));

  const cdp = await context.newCDPSession(page);
  await cdp.send('Accessibility.enable');
  const tree: any = await cdp.send('Accessibility.getFullAXTree');
  const tabs = (tree.nodes ?? []).filter((n: any) => n.role?.value === 'tab');
  console.log('AXTREE TABS: ' + JSON.stringify(tabs.map((n: any) => ({
    name: n.name?.value,
    selected: (n.properties ?? []).filter((p: any) => ['selected', 'focusable'].includes(p.name))
      .map((p: any) => `${p.name}=${JSON.stringify(p.value?.value)}`),
  }))));
});
