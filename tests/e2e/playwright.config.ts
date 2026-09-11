import { defineConfig } from '@playwright/test';

// APP_BASE_URL points at the compose `api` service inside docker,
// or http://localhost:8080 when running against a local `docker compose up`.
export default defineConfig({
  timeout: 90_000,
  fullyParallel: false,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.APP_BASE_URL ?? 'http://localhost:8080',
    actionTimeout: 15_000,
    headless: true,
    args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }]
});
