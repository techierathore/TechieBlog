import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/verify',
  outputDir: './tests/.artifacts/test-results',   // TechieFlow artifact-location rule (verify-phase §1)
  timeout: 120000,
  expect: { timeout: 30000 },
  reporter: 'line',
  // Opt-in: TF_PW_WORKERS=1 serialises a graded run — parallel editor tests on one Blazor host race.
  ...(process.env.TF_PW_WORKERS ? { workers: Number(process.env.TF_PW_WORKERS) } : {}),
  // Opt-in for a graded verify run: only tests that can map to a row (they name a REQ id), minus the
  // ones explicitly retired or superseded. Unset, every test runs as before.
  ...(process.env.TF_PW_ROWS_ONLY ? {
    grep: process.env.TF_PW_GREP ? new RegExp(process.env.TF_PW_GREP) : /REQ-[A-Z]+-\d+/,
    grepInvert: /retired:|superseded|builder evidence|probe \(/,
  } : {}),
  use: { baseURL: process.env.BASE_URL,
    ignoreHTTPSErrors: true,
    headless: true,
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
