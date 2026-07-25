import { test, expect } from '@playwright/test';
import { HeaderPOM } from '../page-objects/header.page';

test.describe('Tier 5 Catalog & Asset Proxy Resiliency Adversarial Coverage', () => {
  // Scenario 1: Upstream catalog service network failure / HTTP 503 response
  test('should handle HTTP 503 response with amber warning banner, 8 fallback diamond packs, 3 subscription plans, and add-to-cart capability', async ({ page }) => {
    // Intercept catalog API requests and respond with HTTP 503 Service Unavailable
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.fulfill({
        status: 503,
        contentType: 'application/json',
        body: JSON.stringify({ error: 'Service Unavailable' }),
      });
    });

    const header = new HeaderPOM(page);

    // 1. Check Diamonds page fallback rendering & add-to-cart
    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    // Verify amber warning banner is visible
    const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
    await expect(warningBanner).toBeVisible();

    // Verify 8 fallback diamond packs are rendered
    const diamondCards = page.locator('.cards-grid-3 .glass-card');
    await expect(diamondCards).toHaveCount(8);

    // Verify first diamond card title
    await expect(diamondCards.first().locator('h3')).toContainText(/60 (diamonds|Алмазов)/i);

    // Test add-to-cart capability for fallback pack
    const firstDiamondBtn = diamondCards.first().locator('button.btn-primary');
    await firstDiamondBtn.click();

    // Cart drawer should open and header badge should show 1
    const cartDrawer = page.locator('aside.cart-drawer');
    await expect(cartDrawer).toHaveClass(/open/);
    await expect(header.cartBadge).toHaveText('1');

    // Close drawer
    const closeBtn = cartDrawer.locator('.cart-drawer-close');
    if (await closeBtn.isVisible()) {
      await closeBtn.click();
    } else {
      // click backdrop or toggle
      await page.keyboard.press('Escape');
    }

    // 2. Check Subscription page fallback rendering & add-to-cart
    await page.goto('/subscription');
    await page.waitForLoadState('domcontentloaded');

    // Verify warning banner on subscription page
    const subWarningBanner = page.locator('.desktop-only-view').getByText('Сервер каталога временно недоступен', { exact: false });
    await expect(subWarningBanner).toBeVisible();

    // Verify 3 fallback subscription plans are rendered
    const planCards = page.locator('.cards-grid-3 .glass-card');
    await expect(planCards).toHaveCount(3);

    // Test add-to-cart capability for fallback subscription plan
    const firstPlanBtn = planCards.first().locator('button');
    await firstPlanBtn.click();

    await expect(cartDrawer).toHaveClass(/open/);
    // Badge count should now be 2
    await expect(header.cartBadge).toHaveText('2');
  });

  // Scenario 2: Catalog API returning empty array [] handling
  test('should handle empty catalog array [] by rendering empty state cards without crashing or displaying warning banner', async ({ page }) => {
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      });
    });

    // On Diamonds page
    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    // Warning banner should NOT be visible because connection was successful (items !== null)
    const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
    await expect(warningBanner).not.toBeVisible();

    // Empty catalog state card should be visible
    const emptyDiamondTitle = page.getByText(/Каталог пуст|Catalog is empty/i);
    await expect(emptyDiamondTitle).toBeVisible();

    // Retry button should be visible
    const retryBtn = page.getByRole('button', { name: /Повторить попытку|Retry loading/i });
    await expect(retryBtn).toBeVisible();

    // On Subscription page
    await page.goto('/subscription');
    await page.waitForLoadState('domcontentloaded');

    await expect(warningBanner).not.toBeVisible();
    const emptyPlanTitle = page.locator('.desktop-only-view').getByText(/Тарифы недоступны|No plans available/i);
    await expect(emptyPlanTitle).toBeVisible();
  });

  // Scenario 3: High network latency exceeding the 15s API timeout.
  test('should gracefully handle network delay exceeding the API timeout and show amber warning banner', async ({ page }) => {
    // Intercept catalog API requests and delay response beyond the 15s timeout.
    await page.route('**/api/v1/catalog/items*', async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 16000));
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      });
    });

    const startTime = Date.now();
    await page.goto('/diamonds');
    // Axios should time out at 15s, triggering the catch block (items === null).
    const warningBanner = page.getByText('Сервер каталога временно недоступен', { exact: false });
    await expect(warningBanner).toBeVisible({ timeout: 18000 });
    const elapsedTime = Date.now() - startTime;

    // Verify it timed out after ~5s and rendered fallbacks
    expect(elapsedTime).toBeGreaterThanOrEqual(14000);

    const cards = page.locator('.cards-grid-3 .glass-card');
    await expect(cards).toHaveCount(8);
  });

  // Scenario 4: Missing asset proxy binary (HTTP 404) & <img onError> handling without infinite loop
  test('should replace missing asset image (404) with local fallback image without infinite error loop', async ({ page }) => {
    // Track browser console errors and img error counts
    const errorLogs: string[] = [];
    page.on('console', (msg) => {
      if (msg.type() === 'error') {
        errorLogs.push(msg.text());
      }
    });

    let assetRequestCount = 0;
    await page.route('**/api/v1/catalog/assets/**', (route) => {
      assetRequestCount++;
      route.fulfill({
        status: 404,
        contentType: 'text/plain',
        body: 'Asset Not Found',
      });
    });

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
            imageUrl: '/api/v1/catalog/assets/russia/ru_store/0.0.36/nonexistent_gem.png',
            metadata: { diamonds: '60' },
          },
        ]),
      });
    });

    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    const img = page.locator('.cards-grid-3 .glass-card img').first();
    await expect(img).toBeVisible();

    // Verify <img onError> replaced src with local fallback asset
    await expect(img).toHaveAttribute('src', /\/images\/diamonds_60\.png/);

    // Wait a brief moment to ensure no infinite request loop occurs
    await page.waitForTimeout(1000);

    // Assert the missing asset route was hit exactly once for the broken asset
    expect(assetRequestCount).toBe(1);
  });

  // Scenario 5: Dynamic non-EUR currency formatting (USD, GBP, KZT, BRL) and missing item metadata defaults
  test('should format non-EUR currencies (USD, GBP, KZT, BRL) and use safe metadata defaults', async ({ page }) => {
    await page.route('**/api/v1/catalog/items*', (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: 'item-usd',
            title: 'USD Pack',
            type: 'Currency',
            price: 10.5,
            currency: 'USD',
            isActive: true,
            // metadata missing completely
          },
          {
            id: 'item-gbp',
            title: 'GBP Pack',
            type: 'Currency',
            price: 15.0,
            currency: 'GBP',
            isActive: true,
            metadata: { diamonds: '300' },
          },
          {
            id: 'item-kzt',
            title: 'KZT Pack',
            type: 'Currency',
            price: 2500.0,
            currency: 'KZT',
            isActive: true,
            metadata: { diamonds: '600' },
          },
          {
            id: 'item-brl',
            title: 'BRL Pack',
            type: 'Currency',
            price: 49.99,
            currency: 'BRL',
            isActive: true,
            metadata: {}, // metadata empty object
          },
          {
            id: 'sub-kzt',
            title: 'KZT Subscription',
            type: 'Subscription',
            price: 4500.0,
            currency: 'KZT',
            isActive: true,
            // missing metadata.months
          },
        ]),
      });
    });

    // 1. Verify Diamonds page currency formatting & metadata defaults
    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    const cards = page.locator('.cards-grid-3 .glass-card');
    await expect(cards).toHaveCount(4);

    // USD card
    const usdPrice = cards.nth(0).getByText(/\$|USD/i).first();
    await expect(usdPrice).toBeVisible();

    // GBP card
    const gbpPrice = cards.nth(1).getByText(/£|GBP/i).first();
    await expect(gbpPrice).toBeVisible();

    // KZT card
    const kztPrice = cards.nth(2).getByText(/KZT/i).first();
    await expect(kztPrice).toBeVisible();

    // BRL card
    const brlPrice = cards.nth(3).getByText(/BRL|R\$/i).first();
    await expect(brlPrice).toBeVisible();

    // Verify missing metadata defaults:
    // Item USD had no metadata, default amount is 100 -> fallback image should map to diamonds_60.png or diamonds_120.png (amount 100 -> diamonds_60.png)
    const usdImg = cards.nth(0).locator('img');
    await expect(usdImg).toHaveAttribute('src', /\/images\/diamonds_60\.png/);

    // 2. Verify Subscription page with missing metadata months (defaults to 1 month)
    await page.goto('/subscription');
    await page.waitForLoadState('domcontentloaded');

    const subCards = page.locator('.cards-grid-3 .glass-card');
    await expect(subCards).toHaveCount(1);
    await expect(subCards.first()).toContainText(/1 (Month|Месяц)/i);

    // Per month text should format correctly without NaN
    const perMonthText = subCards.first().locator('div', { hasText: /\/мес/i }).first();
    await expect(perMonthText).not.toContainText('NaN');
  });

  // Scenario 6: Offline-to-online transition with cart item persistence
  test('should persist offline cart items when backend comes back online and allow adding online items', async ({ page }) => {
    const header = new HeaderPOM(page);

    // Step 1: Start offline (HTTP 503)
    let isOffline = true;
    await page.route('**/api/v1/catalog/items*', (route) => {
      if (isOffline) {
        route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ error: 'Offline' }) });
      } else {
        route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: 'online-live-diamond-sku-999',
              title: 'Live Online Mega Pack',
              type: 'Currency',
              price: 25.99,
              currency: 'EUR',
              isActive: true,
              imageUrl: '/images/diamonds_2000.png',
              metadata: { diamonds: '2000' },
            },
          ]),
        });
      }
    });

    // Load Diamonds page in offline mode
    await page.goto('/diamonds');
    await page.waitForLoadState('domcontentloaded');

    // Add offline fallback pack (e.g. 60 Алмазов, price 1.23) to cart
    const offlineCards = page.locator('.cards-grid-3 .glass-card');
    await expect(offlineCards).toHaveCount(8);
    await offlineCards.first().locator('button.btn-primary').click();

    // Verify cart drawer is open with 1 item
    const cartDrawer = page.locator('aside.cart-drawer');
    await expect(cartDrawer).toHaveClass(/open/);
    await expect(header.cartBadge).toHaveText('1');
    await expect(cartDrawer).toContainText('60 Алмазов');

    // Close cart drawer
    const closeBtn = cartDrawer.locator('.cart-drawer-close');
    if (await closeBtn.isVisible()) {
      await closeBtn.click();
    } else {
      await page.keyboard.press('Escape');
    }

    // Step 2: Transition to online
    isOffline = false;

    // Refresh page or click retry
    await page.reload();
    await page.waitForLoadState('domcontentloaded');

    // Verify live online item is rendered
    const onlineCards = page.locator('.cards-grid-3 .glass-card');
    await expect(onlineCards).toHaveCount(1);
    await expect(onlineCards.first().locator('h3')).toContainText('Live Online Mega Pack');

    // Add live online item to cart
    await onlineCards.first().locator('button.btn-primary').click();

    // Verify cart drawer contains BOTH offline item and online item (2 items total)
    await expect(cartDrawer).toHaveClass(/open/);
    await expect(header.cartBadge).toHaveText('2');
    await expect(cartDrawer).toContainText('60 Алмазов');
    await expect(cartDrawer).toContainText('Live Online Mega Pack');

    // Step 3: Persist across page reload
    await page.reload();
    await page.waitForLoadState('domcontentloaded');

    await expect(header.cartBadge).toHaveText('2');

    // Open cart drawer and verify both items remain persisted in localStorage
    await header.cartBtn.click();
    await expect(cartDrawer).toHaveClass(/open/);
    await expect(cartDrawer).toContainText('60 Алмазов');
    await expect(cartDrawer).toContainText('Live Online Mega Pack');
  });
});
