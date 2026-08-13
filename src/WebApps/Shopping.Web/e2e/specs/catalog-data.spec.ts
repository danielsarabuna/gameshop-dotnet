import { test, expect } from '@playwright/test';
import { HeaderPOM } from '../page-objects/header.page';

test.describe('Catalog Data, Currency & Offline Fallback', () => {
  test('should render diamond offer packs with formatted prices and currency', async ({ page }) => {
    await page.goto('/diamonds');
    await page.waitForLoadState('networkidle');

    // Verify card items exist
    const cards = page.locator('.cards-grid-3 .glass-card');
    await expect(cards).toHaveCount(8);

    // Verify diamond offer titles and price formatting
    const firstCardTitle = cards.first().locator('h3');
    await expect(firstCardTitle).toContainText(/60 (diamonds|Алмазов)/i);

    const priceText = cards.first().getByText(/€|RUB|\$|£|EUR/i, { exact: false });
    await expect(priceText.first()).toBeVisible();
  });

  test('should render subscription plans with perks and formatted prices', async ({ page }) => {
    await page.goto('/subscription');
    await page.waitForLoadState('networkidle');

    const plans = page.locator('.cards-grid-3 .glass-card');
    await expect(plans).toHaveCount(3);

    // Check plan durations (1 Month, 3 Months, 12 Months / 1 Месяц, 3 Месяца, 12 Месяца)
    await expect(plans.nth(0)).toContainText(/1 (Month|Месяц)/i);
    await expect(plans.nth(1)).toContainText(/3 (Months|Месяц)/i);
    await expect(plans.nth(2)).toContainText(/12 (Months|Месяц)/i);

    // Check price contains currency symbol (EUR, RUB, $, etc.)
    await expect(plans.first().getByText(/€|RUB|\$|£|EUR/i, { exact: false }).first()).toBeVisible();
  });

  test('should add diamond pack to cart and update cart badge', async ({ page }) => {
    await page.goto('/diamonds');
    await page.waitForLoadState('networkidle');

    await expect(page.locator('.cards-grid-3 .glass-card')).toHaveCount(8);

    const header = new HeaderPOM(page);
    const firstCardBtn = page.locator('.cards-grid-3 .glass-card button.btn-primary').first();

    await firstCardBtn.click();

    // Verify cart drawer slides open
    const cartDrawer = page.locator('aside.cart-drawer');
    await expect(cartDrawer).toHaveClass(/open/);

    // Verify cart badge count in header updates
    await expect(header.cartBadge).toHaveText('1');
  });

  test('should display amber warning banner when catalog API is offline', async ({ page }) => {
    // Intercept catalog API requests and force network failure
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.abort('failed');
    });

    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    // Wait for fallback data and amber warning banner to appear
    const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
    await expect(warningBanner).toBeVisible();

    // Verify fallback packs are still displayed
    const cards = page.locator('.cards-grid-3 .glass-card');
    await expect(cards.first()).toBeVisible();
  });

  test('should display amber warning banner on subscription page when catalog API is offline', async ({ page }) => {
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.abort('failed');
    });

    await page.goto('/subscription');
    await page.waitForLoadState('domcontentloaded');

    const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
    await expect(warningBanner).toBeVisible();

    const plans = page.locator('.cards-grid-3 .glass-card');
    await expect(plans.first()).toBeVisible();
  });

  test('should format custom ISO currency codes dynamically from API response', async ({ page }) => {
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 'd1a00000-0000-0000-0000-000000000060',
            title: '60 Diamonds Pack',
            type: 'Currency',
            price: 12.34,
            currency: 'USD',
            isActive: true,
            metadata: { diamonds: '60' },
          },
        ]),
      });
    });

    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    const cards = page.locator('.cards-grid-3 .glass-card');
    await expect(cards).toHaveCount(1);
    const priceText = cards.first().getByText(/\$|USD/i).first();
    await expect(priceText).toBeVisible();
  });

  test('should fallback to local image placeholder when product asset URL returns 404', async ({ page }) => {
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 'd1a00000-0000-0000-0000-000000000060',
            title: '60 Diamonds Broken Asset',
            type: 'Currency',
            price: 1.23,
            currency: 'EUR',
            isActive: true,
            imageUrl: '/api/v1/catalog/assets/russia/ru_store/0.0.36/nonexistent_image.png',
            metadata: { diamonds: '60' },
          },
        ]),
      });
    });

    await page.route('**/api/v1/catalog/assets/**', (route) => {
      route.fulfill({ status: 404 });
    });

    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    const img = page.locator('.cards-grid-3 .glass-card img').first();
    await expect(img).toBeVisible();
    await expect(img).toHaveAttribute('src', /\/images\/diamonds_60\.png/);
  });
});
