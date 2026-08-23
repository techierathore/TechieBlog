/**
 * REQ-UI-064 — skill category order is data-driven (not hard-coded alphabetical) and a skill's
 * order is visible on /admin/skills.
 *
 * The fix orders category groups by the LOWEST `DisplayOrder` they contain, name as tie-break, in
 * BOTH `ManageSkills.OrderCategories` and `ResumeSkills`, so admin and the public resume agree.
 *
 * The surrounding verify run seeds 13 skills whose authored order is deliberately NOT alphabetical:
 *   Languages (1-3) · Frameworks (4-6) · Data (7-8) · Cloud and DevOps (9-11) · Practices (12-13)
 * Sorted alphabetically that would read Cloud and DevOps · Data · Frameworks · Languages ·
 * Practices, so the two orders disagree on every position — which is what makes this a real check
 * rather than one a regressed build could still pass.
 */
import { test, expect, request } from '@playwright/test';
import { BASE, login, nav } from './_gates';

const AUTHORED = ['Languages', 'Frameworks', 'Data', 'Cloud and DevOps', 'Practices'];
const ALPHABETICAL = [...AUTHORED].sort((a, b) => a.localeCompare(b));

/** Returns the seeded category names in the order they appear in the given HTML. */
function categoryOrder(html: string): string[] {
  const seen: string[] = [];
  const pattern = new RegExp(AUTHORED.map((c) => c.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|'), 'g');
  for (const match of html.matchAll(pattern)) {
    if (!seen.includes(match[0])) seen.push(match[0]);
  }
  return seen;
}

test('REQ-UI-064 /admin/skills renders categories in the authored order, not alphabetically', async ({ page }) => {
  await login(page, 'admin');
  await nav(page, '/admin/skills');
  await expect(page.locator('[data-testid="skill-row"]').first()).toBeVisible({ timeout: 45000 });

  const rows = await page.locator('[data-testid="skill-row"]').count();
  expect(rows, 'all 13 seeded skills should render').toBe(13);

  const order = categoryOrder(await page.content());
  expect(order, 'categories must follow the authored DisplayOrder').toEqual(AUTHORED);
  expect(order, 'categories must NOT be hard-coded alphabetical').not.toEqual(ALPHABETICAL);
});

test('REQ-UI-064 every skill row carries a visible order badge', async ({ page }) => {
  await login(page, 'admin');
  await nav(page, '/admin/skills');
  await expect(page.locator('[data-testid="skill-row"]').first()).toBeVisible({ timeout: 45000 });

  const badges = page.locator('[data-testid="skill-order"]');
  const count = await badges.count();
  expect(count, 'one order badge per skill row, matching /admin/experience parity').toBe(13);

  // Gapless and tie-free 1..N is the property the ApplyOrder renumbering pass guarantees.
  const values = (await badges.allInnerTexts())
    .map((text) => parseInt(text.replace(/\D+/g, ''), 10))
    .filter((n) => !Number.isNaN(n))
    .sort((a, b) => a - b);
  expect(values, 'order badges must read 1..13, gapless and tie-free').toEqual(
    Array.from({ length: 13 }, (unused, i) => i + 1));
});

test('REQ-UI-064 the public resume renders the same category order as admin', async () => {
  const api = await request.newContext({ baseURL: BASE });
  try {
    const html = await (await api.get('/resume')).text();
    const order = categoryOrder(html);
    expect(order, 'the public resume must agree with the admin screen').toEqual(AUTHORED);
  } finally {
    await api.dispose();
  }
});
