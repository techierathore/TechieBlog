import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/verify',
  timeout: 120000,
  expect: { timeout: 30000 },
  reporter: 'line',
  use: {
    ignoreHTTPSErrors: true,
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
