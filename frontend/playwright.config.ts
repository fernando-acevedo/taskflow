import { defineConfig, devices } from '@playwright/test';

// Env: BASE_URL (frontend, default http://localhost:4200), API_URL (API, default http://localhost:5090),
// E2E_ADMIN_EMAIL / E2E_ADMIN_PASSWORD (the seeded admin), PW_CHANNEL (e.g. "chrome" to use an installed browser).
// The frontend is served by scripts/e2e-serve.mjs against API_URL, unless a server already answers on BASE_URL
// (or E2E_NO_SERVER=1).
const baseURL = process.env.BASE_URL ?? 'http://localhost:4200';
const ci = !!process.env.CI;

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  fullyParallel: true,
  forbidOnly: ci,
  retries: ci ? 1 : 0,
  workers: ci ? 2 : undefined,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: ci ? [['github'], ['html', { open: 'never' }]] : [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    // The app picks its language from the browser; the tests read the English texts.
    locale: 'en-US',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'], channel: process.env.PW_CHANNEL },
    },
  ],
  webServer: process.env.E2E_NO_SERVER
    ? undefined
    : {
        command: 'node scripts/e2e-serve.mjs',
        url: baseURL,
        reuseExistingServer: !ci,
        timeout: 240_000,
        stdout: 'pipe',
      },
});
