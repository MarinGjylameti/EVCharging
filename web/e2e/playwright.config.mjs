import { defineConfig } from '@playwright/test';
import { e2eEnvironment } from './environment.mjs';

const settings = e2eEnvironment(process.env);
export default defineConfig({
  testDir: './specs',
  testMatch: '**/*.spec.mjs',
  fullyParallel: false,
  workers: 1,
  retries: 0, // A physical start must never be silently retried by the runner.
  timeout: 120_000,
  expect: { timeout: 20_000 },
  reporter: [['list']],
  outputDir: './test-results',
  use: {
    baseURL: settings.baseUrl,
    browserName: 'chromium',
    headless: true,
    serviceWorkers: 'block',
    ignoreHTTPSErrors: false,
    trace: 'off', video: 'off', screenshot: 'off'
  }
  // No webServer, globalSetup, provisioning, dependency or browser installation.
});
