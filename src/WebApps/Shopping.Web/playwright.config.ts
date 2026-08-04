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
      testMatch: ['**/mobile-small.spec.ts', '**/tablet-responsive.spec.ts', '**/mobile-page-layout.spec.ts', '**/checkout-polish.spec.ts'],
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
      testMatch: ['**/desktop-landscape.spec.ts', '**/catalog-data.spec.ts', '**/catalog-failure-resilience.spec.ts', '**/responsive-boundaries.spec.ts', '**/local-unity-flow.spec.ts', '**/checkout-polish.spec.ts'],
      use: {
        browserName: 'chromium',
        viewport: { width: 1280, height: 720 },
      },
    },
    {
      name: 'desktop-4k',
      testMatch: '**/catalog-failure-resilience.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1920, height: 1080 },
      },
    },
    {
      name: 'checkout-layout-resilience',
      testMatch: '**/checkout-layout-resilience.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1280, height: 720 },
      },
    },
    {
      name: 'catalog-data-resilience',
      testMatch: '**/catalog-data-resilience.spec.ts',
      use: {
        browserName: 'chromium',
        viewport: { width: 1280, height: 720 },
      },
    },
    {
      name: 'layout-oracle',
      testMatch: '**/layout-oracle.spec.ts',
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
