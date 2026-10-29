import { Page, expect } from '@playwright/test';
import { loginHard, goTo } from './vall-authoring-helpers';
export { loginHard, goTo };

/** Opens an existing post in the editor from a neutral screen so the editor loads the row, not a stale form. */
export async function openPostById(page: Page, postId: string | number) {
  await goTo(page, '/BlogsList', '[data-testid="posts-status-tabs"]', 120000);
  await goTo(page, `/ManagePost/${postId}`, '[data-testid="post-title-input"]', 120000);
  await expect(page.locator('[data-testid="post-title-input"]')).not.toHaveValue('', { timeout: 30000 });
}
