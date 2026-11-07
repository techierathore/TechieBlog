import { Page } from '@playwright/test';
import { execSync } from 'child_process';
export { loginHard } from './vall-authoring-helpers';

export const SHOT = 'tests/.artifacts/build-phase-20261027/a';

/** Runs a read/write psql statement and returns rows (pipe separated, unaligned, no header). */
export function psql(sql: string): string[][] {
  const out = execSync(`docker exec WinPostgre psql -U PgVectorAdmin -d TechieBlog -At -F '|' -c ${JSON.stringify(sql)}`, { encoding: 'utf8' });
  return out.split('\n').filter((l) => l.length).map((l) => l.split('|'));
}

/** Client-side navigation then wait until the destination owns a visible element. */
export async function open(page: Page, route: string, ready: string, timeout = 90000) {
  await page.evaluate((p) => (window as any).Blazor.navigateTo(p), route);
  await page.waitForSelector(ready, { state: 'visible', timeout });
  await page.waitForTimeout(1200);
}

export async function hOverflow(page: Page) {
  return page.evaluate(() => ({ sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth }));
}
