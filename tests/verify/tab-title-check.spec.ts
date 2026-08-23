/**
 * What does the browser TAB actually say, on each public page?
 *
 * The document head carries two <title> elements — the server-rendered shell's in `App.razor` and
 * the one Blazor's HeadOutlet emits from <PageTitle>. Only `document.title` after hydration tells
 * you which one the tab ends up showing, so this reads that rather than the markup.
 */
import { test, expect } from '@playwright/test';
import { BASE } from './_gates';

const ROUTES = ['/', '/categories', '/series', '/newsletters', '/resume', '/speaker-profile', '/search'];

test('report the browser tab title for every public route', async ({ page }) => {
  test.setTimeout(5 * 60 * 1000);

  const seen: Record<string, string> = {};
  for (const route of ROUTES) {
    const response = await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    if (response && response.status() !== 200) {
      seen[route] = `(HTTP ${response.status()})`;
      continue;
    }
    // Wait for hydration to settle so PageTitle has had its say.
    await page.waitForTimeout(2500);
    seen[route] = await page.title();
  }

  for (const [route, title] of Object.entries(seen)) {
    console.log(`  ${route.padEnd(14)} -> ${JSON.stringify(title)}`);
  }

  // Every tab title must carry the configured site name; none may show the old hard-coded tagline.
  for (const [route, title] of Object.entries(seen)) {
    if (title.startsWith('(HTTP')) continue;
    expect(title, `${route} should carry the configured site name`).toContain('TechieRathore');
    expect(title, `${route} must not show the hard-coded tagline`).not.toContain('shipping software');
    expect(title, `${route} must not show the fallback name`).not.toBe('TechieBlog');
  }
});
