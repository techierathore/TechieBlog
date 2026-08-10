import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/verify',
  outputDir: './tests/.artifacts/test-results',   // TechieFlow artifact-location rule (verify-phase §1)
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
