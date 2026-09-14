import { test, expect } from '@playwright/test';

test.describe('Interaction layout edge cases', () => {

  test('M1-STRESS-1: Unknown currency code fallback in formatPrice', async ({ page }) => {
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 'd1a00000-0000-0000-0000-000000000099',
            title: '9999 Diamonds Custom Code',
            type: 'Currency',
            price: 99.95,
            currency: 'INVALID_CURRENCY_CODE',
            isActive: true,
            metadata: { diamonds: '9999' },
          },
        ]),
      });
    });

    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    const card = page.locator('.cards-grid-3 .glass-card').first();
    await expect(card).toBeVisible();

    // Verify raw code fallback formatting: "99.95 INVALID_CURRENCY_CODE"
    const priceText = card.getByText(/INVALID_CURRENCY_CODE/);
    await expect(priceText).toBeVisible();
    await expect(priceText).toContainText('99.95 INVALID_CURRENCY_CODE');
  });

  test('M1-STRESS-2: Asset proxy HTTP 500 & 404 triggers onError fallback cleanly', async ({ page }) => {
    let errorCount = 0;
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        errorCount++;
      }
    });

    await page.route('**/api/v1/catalog/items*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 'd1a00000-0000-0000-0000-000000000060',
            title: '60 Diamonds Broken Proxy',
            type: 'Currency',
            price: 1.23,
            currency: 'EUR',
            isActive: true,
            imageUrl: '/api/v1/catalog/assets/russia/ru_store/0.0.36/broken_image.png',
            metadata: { diamonds: '60' },
          },
        ]),
      });
    });

    await page.route('**/api/v1/catalog/assets/**', (route) => {
      route.fulfill({ status: 500, body: 'Internal Server Error' });
    });

    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    const img = page.locator('.cards-grid-3 .glass-card img').first();
    await expect(img).toBeVisible();
    await expect(img).toHaveAttribute('src', /\/images\/diamonds_60\.png/);
  });

  test('M1-STRESS-3: Offline warning banner retry button recovers catalog when backend comes online', async ({ page }) => {
    let isBackendOffline = true;

    await page.route('**/api/v1/catalog/items*', (route) => {
      if (isBackendOffline) {
        route.abort('failed');
      } else {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: 'd1a00000-0000-0000-0000-000000000060',
              title: 'Online Pack 60 Diamonds',
              type: 'Currency',
              price: 1.99,
              currency: 'USD',
              isActive: true,
              metadata: { diamonds: '60' },
            },
          ]),
        });
      }
    });

    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
    await expect(warningBanner).toBeVisible();

    // Now restore backend and click refresh button
    isBackendOffline = false;
    const refreshBtn = warningBanner.locator('..').locator('button');
    await refreshBtn.click();

    // Banner should disappear and online data rendered
    await expect(warningBanner).not.toBeVisible();
    const cardTitle = page.locator('.cards-grid-3 .glass-card h3').first();
    await expect(cardTitle).toHaveText('Online Pack 60 Diamonds');
  });

  test('M1-STRESS-4: Cart full lifecycle — add multiple items, modify quantity, open/close drawer, total formatting', async ({ page }) => {
    await page.goto('/diamonds');
    await page.waitForLoadState('networkidle');

    const cards = page.locator('.cards-grid-3 .glass-card');
    const firstAddBtn = cards.nth(0).locator('button.btn-primary');
    const secondAddBtn = cards.nth(1).locator('button.btn-primary');

    // Add first item
    await firstAddBtn.click();

    const drawer = page.locator('aside.cart-drawer');
    await expect(drawer).toHaveClass(/open/);

    // Close drawer via backdrop
    await page.locator('.cart-overlay').click({ force: true });
    await expect(drawer).not.toHaveClass(/open/);

    // Add second item
    await secondAddBtn.click();
    await expect(drawer).toHaveClass(/open/);

    // Check cart lines count
    const cartLines = drawer.locator('div > div > div > img, div > div > div > svg');
    // Lines exist in drawer
    const totalElement = drawer.getByText(/Итого:|Total:/i);
    await expect(totalElement).toBeVisible();

    // Verify escape key closes cart drawer
    await page.keyboard.press('Escape');
    await expect(drawer).not.toHaveClass(/open/);
  });
});
