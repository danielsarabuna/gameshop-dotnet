import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll, assertNoElementOverlap } from '../utils/layout-helpers';
import { HeaderPOM } from '../page-objects/header.page';

test.describe('Challenger M4 — Tier 5 Adversarial & Extreme Breakpoint Coverage', () => {
  const routes = ['/', '/projects', '/diamonds', '/subscription', '/help', '/about'];

  test.describe('1. 2560px 4K Ultra-Wide Viewport Stress Test', () => {
    test.use({ viewport: { width: 2560, height: 1440 } });

    for (const route of routes) {
      test(`route ${route} on 2560px 4K has zero horizontal scroll`, async ({ page }) => {
        await page.goto(route);
        await page.waitForLoadState('networkidle');
        await assertZeroHorizontalScroll(page);
      });
    }

    test('should center stage container and cap max-width at 1180px on 2560px 4K', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      expect(box).not.toBeNull();

      // Width should be capped at 1180px
      expect(Math.abs(box!.width - 1180)).toBeLessThanOrEqual(5);

      // Margin left should center the container: (2560 - 1180) / 2 = 690px
      const expectedMargin = (2560 - 1180) / 2;
      expect(Math.abs(box!.x - expectedMargin)).toBeLessThanOrEqual(10);

      // Side vignette should be visible
      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).toBeVisible();
    });

    test('should not have overlapping header elements on 2560px 4K viewport', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);
      await expect(header.navLinks).toBeVisible();
      await assertNoElementOverlap([header.logo, header.navLinks, header.langPillBtn, header.authBtn, header.cartBtn]);
    });
  });

  test.describe('2. Asset Proxy Traversal & Invalid Parameters', () => {
    test('should return 404 for path traversal attempts via Catalog API asset proxy', async ({ page }) => {
      const traversalUrls = [
        '/api/v1/catalog/assets/russia/ru_store/0.0.36/../../secret.png',
        '/api/v1/catalog/assets/russia/ru_store/0.0.36/..%2f..%2fsecret.png',
        '/api/v1/catalog/assets/invalid_region/invalid_store/0.0.36/nonexistent.png',
      ];

      for (const url of traversalUrls) {
        const response = await page.request.get(url);
        expect(response.status()).toBe(404);
      }
    });

    test('should handle invalid store/region parameters gracefully without breaking catalog page', async ({ page }) => {
      await page.route('**/api/v1/catalog/items*', (route) => {
        // Simulates 404 or empty response for invalid region/store params
        route.fulfill({
          status: 404,
          contentType: 'application/json',
          body: JSON.stringify({ error: 'Catalog config not found for invalid region' }),
        });
      });

      await page.goto('/diamonds');
      await page.waitForLoadState('domcontentloaded');

      // Verify amber warning banner is shown for missing/invalid catalog
      const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
      await expect(warningBanner).toBeVisible();

      // Verify fallback diamond packs render safely
      const cards = page.locator('.cards-grid-3 .glass-card');
      await expect(cards).toHaveCount(8);
    });
  });

  test.describe('3. Offline & Disconnected Catalog Service Fallback Verification', () => {
    test('should display amber warning banner and render fallback diamond packs when backend drops connection', async ({ page }) => {
      await page.route('**/api/v1/catalog/items*', (route) => route.abort('failed'));

      await page.goto('/diamonds');
      await page.waitForLoadState('domcontentloaded');

      const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
      await expect(warningBanner).toBeVisible();

      const cards = page.locator('.cards-grid-3 .glass-card');
      await expect(cards).toHaveCount(8);
      await expect(cards.first().locator('h3')).toContainText(/60 Алмазов/i);
    });

    test('should display amber warning banner and render fallback subscription plans when backend drops connection', async ({ page }) => {
      await page.route('**/api/v1/catalog/items*', (route) => route.abort('failed'));

      await page.goto('/subscription');
      await page.waitForLoadState('domcontentloaded');

      const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
      await expect(warningBanner).toBeVisible();

      const plans = page.locator('.cards-grid-3 .glass-card');
      await expect(plans).toHaveCount(3);
      await expect(plans.first()).toContainText(/Premium — 1 месяц|1 Месяц/i);
    });
  });
});
