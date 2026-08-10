/*
  cluster-l-probe.spec.ts — DOM probe kept as the evidence record for TR-061 / TR-062
  (Cluster L, 2026-08-09).

  It answers two questions the component library's AI reference does not: what markup
  TrBlazeUI's ItemGroup/Item actually emit, and whether Input renders the Id it is given
  onto the <input> element a <label for> has to point at.

  CAPTURED 2026-08-09, BEFORE the fixes (this is the finding the checklist cites; the
  admin dashboard no longer uses ItemGroup, so re-running the first test today will not
  reproduce it from /admin — put an <ItemGroup> on any page to see it again):

    <div role="list" class="flex flex-col gap-0.5">
      <div data-slot="item" class="group relative flex items-center gap-3 rounded-lg px-4 py-3">
        <div data-slot="item-media"    class="flex shrink-0 items-center justify-center size-8 rounded-md border border-border"> … </div>
        <div data-slot="item-content"  class="flex flex-1 flex-col gap-1">
          <div data-slot="item-title"       class="flex items-center gap-2 font-medium leading-none">New post published</div>
          <div data-slot="item-description" class="line-clamp-2 text-sm text-muted-foreground">Writing a Technical Talk That Lands</div>
        </div>
        <div data-slot="item-actions"  class="flex items-center gap-2"> … </div>
      </div>
      … 4 more identical children …
    </div>

    anyRoleList: [{ cls: "flex flex-col gap-0.5", childRoles: ["div","div","div","div","div"] }]

  i.e. role="list" whose five children carry NO role — axe `aria-required-children`
  (critical), and "list, 0 items" to a screen reader. See TR-061.

  And for /rss: the Input rendered `<input type="text" class="flex h-10 w-full …">` with
  `rssId: ""` — the element carrying data-testid IS the <input>, so `Id` lands where a
  `<label for>` needs it, which is what made the TR-062 workaround viable.
*/
import { test, Page } from '@playwright/test';

const BASE = process.env.TB_BASE ?? 'http://172.18.144.1:5392';
const ADMIN_EMAIL = 'Ravi@techieblog.com';
const ADMIN_PASSWORD = 'admin_password';

async function login(page: Page) {
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page.waitForSelector('[data-testid="login-email"]');
  await page.waitForFunction(() => (window as any).Blazor !== undefined, null, { timeout: 30000 });
  await page.waitForTimeout(2500);
  await page.fill('[data-testid="login-email"]', ADMIN_EMAIL);
  await page.fill('[data-testid="login-password"]', ADMIN_PASSWORD);
  await page.click('[data-testid="login-submit"]');
  await page.waitForURL(u => !u.pathname.toLowerCase().includes('login'), { timeout: 30000 });
  await page.waitForTimeout(2500);
}

test('probe role=list children across the admin area', async ({ page }) => {
  test.setTimeout(180000);
  await page.setViewportSize({ width: 1280, height: 900 });
  await login(page);
  const found: Array<unknown> = [];
  for (const route of ['/admin', '/admin/analytics', '/settings']) {
    await page.evaluate((u: string) => (window as any).Blazor.navigateTo(u), route);
    await page.waitForTimeout(4000);
    const lists = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[role="list"]')).map(e => ({
        cls: e.className.toString().slice(0, 120),
        childRoles: Array.from(e.children).map(c => c.getAttribute('role') ?? c.tagName.toLowerCase()),
      }))
    );
    found.push({ route, lists });
  }
  console.log('ROLE_LIST:', JSON.stringify(found, null, 1));
});

test('probe rss input labelling', async ({ page }) => {
  test.setTimeout(120000);
  await page.goto(`${BASE}/rss`, { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(5000);
  const info = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="rss-url"]') as HTMLElement | null;
    const input = el?.tagName === 'INPUT' ? (el as HTMLInputElement) : (el?.querySelector('input') as HTMLInputElement | null);
    const label = input?.labels?.[0] ?? null;
    return {
      tag: el?.tagName,
      id: input?.id,
      accessibleNameSource: label ? `label[for=${label.getAttribute('for')}]` : (input?.getAttribute('aria-label') ?? 'NONE'),
      labelText: label?.textContent ?? '',
    };
  });
  console.log('RSSINPUT:', JSON.stringify(info, null, 1));
});
