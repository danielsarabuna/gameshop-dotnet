import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './specs',
  testMatch: '**/mobile-responsive-audit.spec.ts',
  fullyParallel: true,
  timeout: 60000,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:5200',
    browserName: 'chromium',
  },
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5200',
    reuseExistingServer: true,
    timeout: 120 * 1000,
  },
});
