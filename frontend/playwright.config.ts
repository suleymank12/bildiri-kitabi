import { defineConfig, devices } from '@playwright/test';

const backendPort = process.env.E2E_BACKEND_PORT ?? '5081';
const frontendPort = process.env.E2E_FRONTEND_PORT ?? '5174';
const localBaseURL = `http://localhost:${frontendPort}`;
// Set to test an already running deployment (e.g. the Docker setup); then no servers are started here.
const externalBaseURL = process.env.E2E_BASE_URL;
const baseURL = externalBaseURL ?? localBaseURL;

/**
 * End-to-end tests against the real API (own database and storage, see e2e/support/start-backend.mjs) and the
 * Vite dev server, on a desktop and a phone-sized Chromium. `npm run screenshots` renders the README images.
 * With E2E_BASE_URL the same tests run against that address instead (`npm run test:e2e:docker`).
 */
export default defineConfig({
  testDir: 'e2e',
  globalTeardown: externalBaseURL ? undefined : './e2e/support/global-teardown.ts',
  outputDir: 'test-results',
  timeout: 90_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 2,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL,
    locale: 'tr-TR',
    timezoneId: 'Europe/Istanbul',
    trace: 'retain-on-failure',
  },
  projects: [
    {
      name: 'desktop',
      testIgnore: /screenshots\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], viewport: { width: 1280, height: 800 } },
    },
    {
      name: 'mobile',
      testIgnore: /screenshots\.spec\.ts/,
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 390, height: 844 },
        deviceScaleFactor: 3,
        isMobile: true,
        hasTouch: true,
      },
    },
    {
      name: 'screenshots',
      testMatch: /screenshots\.spec\.ts/,
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: externalBaseURL
    ? undefined
    : [
        {
          command: 'node e2e/support/start-backend.mjs',
          url: `http://localhost:${backendPort}/health`,
          env: { E2E_BACKEND_PORT: backendPort },
          timeout: 240_000,
          reuseExistingServer: false,
          stdout: 'ignore',
          stderr: 'pipe',
        },
        {
          command: `npx vite --port ${frontendPort} --strictPort`,
          url: localBaseURL,
          env: { BACKEND_URL: `http://localhost:${backendPort}`, PORT: frontendPort },
          timeout: 60_000,
          reuseExistingServer: false,
        },
      ],
});
