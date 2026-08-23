/**
 * UAT-023 support — signs in as Admin and writes the issued JWT access token to a file so the
 * cross-process cache-refresh endpoint can be exercised from the shell.
 *
 * `IAuthService.GetUserByAccessTokenAsync` validates a JWT, not the `userlogins.logintoken` row, so
 * the token cannot be read out of the database — it has to come from a real sign-in, which is also
 * exactly how BlogApp obtains the one it presents.
 */
import { test } from '@playwright/test';
import * as fs from 'fs';
import { login } from './_gates';

const ARTIFACTS = 'tests/.artifacts/uat-023';

test('dump an Admin access token for the refresh-endpoint probe', async ({ page }) => {
  await login(page, 'admin');
  await page.waitForTimeout(1500);

  const token = await page.evaluate(() => {
    for (let i = 0; i < localStorage.length; i++) {
      const key = localStorage.key(i);
      if (key && key.startsWith('AccessToken-')) {
        return localStorage.getItem(key) ?? '';
      }
    }
    return '';
  });

  fs.mkdirSync(ARTIFACTS, { recursive: true });
  fs.writeFileSync(`${ARTIFACTS}/admin-token.txt`, token);
  console.log(`[UAT-023] token length ${token.length}`);
});
