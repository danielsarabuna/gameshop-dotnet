import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : 3,
  timeout: 60000,
  reporter: 'list',
  use: {
    baseURL: 'http://localhost:5200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'mobile-small',
      testMatch: ['**/mobile-small.spec.ts', '**/challenger-m3-stress.spec.ts', '**/challenger-m2-c2-stress.spec.ts'],
      use: {
        browserName: 'chromium',
        viewport: { width: 320, height: 568 },
      },
    },
    {
      name: 'mobile-portrait',
      testMatch: '**/mobile-portrait.spec.ts',
      use: {
        ...devices['iPhone 13'],
        browserName: 'chromium',
        viewport: { width: 390, height: 844 },
      },
    },
    {
      name: 'tablet-portrait',
      testMatch: '**/tablet-portrait.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 768, height: 1024 },
      },
    },
    {
      name: 'tablet-landscape',
      testMatch: '**/desktop-landscape.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1024, height: 768 },
      },
    },
    {
      name: 'desktop',
      testMatch: ['**/desktop-landscape.spec.ts', '**/catalog-data.spec.ts', '**/challenger-m4-stress.spec.ts', '**/challenger-m3-c2-boundary-stress.spec.ts', '**/local-unity-flow.spec.ts'],
      use: {
        browserName: 'chromium',
        viewport: { width: 1280, height: 720 },
      },
    },
    {
      name: 'desktop-4k',
      testMatch: '**/challenger-m4-stress.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1920, height: 1080 },
      },
    },
    {
      name: 'challenger-tier5-layout',
      testMatch: '**/challenger-tier5-layout.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1280, height: 720 },
      },
    },
    {
      name: 'challenger-tier5-catalog',
      testMatch: '**/challenger-tier5-catalog.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1280, height: 720 },
      },
    },
    {
      name: 'emp-challenger-m3',
      testMatch: '**/emp-challenger-m3-verification.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1280, height: 720 },
      },
    },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5200',
    reuseExistingServer: true,
    timeout: 120 * 1000,
  },
});
