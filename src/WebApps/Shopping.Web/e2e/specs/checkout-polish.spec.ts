import { expect, type Page, test } from '@playwright/test';

const diamondId = 'd1a00000-0000-0000-0000-000000000060';
const subscriptionId = '9aa00000-0000-0000-0000-000000000001';

const seedV2Session = async (page: Page, lines: unknown[]) => {
  await page.addInitScript(({ cart }) => {
    sessionStorage.setItem('GameShop_webshop_session', JSON.stringify({
      accessToken: 'test-token',
      expiresAtUtc: new Date(Date.now() + 60_000).toISOString(),
      playerId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      playerName: 'Дарья',
      region: 'russia',
      storeChannel: 'ru_store',
      gameVersion: '0.0.36',
      deliveryContractVersion: 2,
      sessionKind: 'game',
    }));
    localStorage.setItem('GameShop_cart', JSON.stringify(cart));
  }, { cart: lines });
};

const mockCheckoutApi = async (page: Page, onOrder?: (body: { items: unknown[] }) => void) => {
  await page.route('**/api/v1/**', async (route) => {
    const request = route.request();
    const path = new URL(request.url()).pathname;
    if (path === '/api/v1/payment-methods') return route.fulfill({ json: [{ code: 'mockprovider', name: 'Тестовая оплата' }] });
    if (path === '/api/v1/catalog/payment-providers') return route.fulfill({ json: { providers: [{ id: 'mockprovider', displayName: 'Тестовая оплата', isEnabled: true, isSandbox: true }] } });
    if (path === '/api/v1/catalog/items') return route.fulfill({ json: [
      { id: diamondId, title: '60 алмазов', type: 'Currency', price: 199, currency: 'RUB', isActive: true, metadata: { diamonds: '60' } },
      { id: subscriptionId, title: 'Premium — 1 месяц', type: 'Subscription', price: 399, currency: 'RUB', isActive: true, metadata: { subscriptionDays: '30' } },
    ] });
    if (path === '/api/v1/orders/create') {
      onOrder?.(request.postDataJSON() as { items: unknown[] });
      return route.fulfill({ status: 201, json: { orderId: '11111111-2222-3333-4444-555555555555', status: 'Pending', subtotal: 598, discountAmount: 0, total: 598, currency: 'RUB' } });
    }
    if (path === '/api/v1/payments/mockprovider') return route.fulfill({ json: { paymentId: 'payment', provider: 'MockProvider', status: 'Pending', checkoutUrl: '/order/mock-checkout' } });
    if (path.endsWith('/complete')) return route.fulfill({ json: { status: 'succeeded' } });
    if (path === '/api/v1/orders/11111111-2222-3333-4444-555555555555') return route.fulfill({ json: { status: 'Paid' } });
    return route.continue();
  });
};

test.describe('auth and payment polish', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      sessionStorage.clear();
      localStorage.removeItem('GameShop_cart');
    });
  });

  test('manual Player ID resolves recipient metadata before checkout', async ({ page }) => {
    await page.route('**/api/v1/auth/resolve-player', (route) => route.fulfill({ json: {
      isValid: true,
      userId: 'aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee',
      playerName: 'Тестовый игрок',
      region: 'global',
      store: 'global',
      gameVersion: 'global',
      deliveryContractVersion: 2,
      accessToken: 'test-token',
      sessionKind: 'recipient',
    } }));
    await page.goto('/diamonds');
    await page.locator('button.auth-pill-btn').click();
    const dialog = page.getByRole('dialog', { name: 'Получатель покупки' });
    await dialog.getByLabel('Player ID').fill('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
    await dialog.getByRole('button', { name: 'Найти игрока' }).click();
    await expect(dialog.getByText('Тестовый игрок')).toBeVisible();
    await expect(page.locator('button.auth-pill-btn')).toHaveAttribute('aria-label', 'Ввести Player ID');
    await dialog.getByRole('button', { name: 'Подтвердить' }).click();

    await page.getByRole('button', { name: 'В корзину' }).first().click();
    const drawer = page.locator('aside.cart-drawer');
    await expect(drawer.getByText('Тестовый игрок')).toBeVisible();
    await expect(drawer.locator('input[type="email"]')).toHaveCount(0);
    await expect(drawer.locator('.checkout-footer')).toBeVisible();
  });

  test('payment return pages distinguish cancelled and missing orders', async ({ page }) => {
    await page.goto('/order/cancelled?order_id=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
    await expect(page.getByRole('heading', { name: 'Оплата отменена' })).toBeVisible();
    await page.goto('/order/complete');
    await expect(page.getByRole('heading', { name: 'Заказ не найден' })).toBeVisible();
  });

  test('legacy mock checkout is localized and informative', async ({ page }) => {
    await page.goto('/order/mock-checkout?orderId=aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
    await expect(page.getByRole('heading', { name: 'Тестовая оплата' })).toBeVisible();
    await expect(page.getByText('Деньги не списываются')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Подтвердить' })).toBeVisible();
  });

  test('mock success stays on the shop and never leaves checkout busy', async ({ page }) => {
    await seedV2Session(page, [{ sku: diamondId, title: '60 алмазов', unitPrice: 199, quantity: 1, currency: 'RUB', type: 'Currency' }]);
    await mockCheckoutApi(page);
    await page.goto('/diamonds');
    await page.getByLabel('Cart').click();
    const drawer = page.locator('aside.cart-drawer');
    await drawer.getByRole('button', { name: 'Перейти к оплате' }).click();
    await drawer.getByRole('button', { name: 'Подтвердить' }).click();

    await expect(page).toHaveURL(/\/diamonds$/);
    await expect(page.getByRole('dialog', { name: 'Оплата прошла' })).toBeVisible();
    await page.getByRole('dialog', { name: 'Оплата прошла' }).getByRole('button', { name: 'Закрыть' }).click();
    await page.getByRole('button', { name: 'В корзину' }).first().click();
    await expect(drawer.getByRole('button', { name: 'Перейти к оплате' })).toBeEnabled();
    await expect(drawer.getByText('Обработка…')).toHaveCount(0);
  });

  test('v2 session submits diamonds and subscription as one order', async ({ page }) => {
    let orderBody: { items: unknown[] } | undefined;
    await seedV2Session(page, [
      { sku: diamondId, title: '60 алмазов', unitPrice: 199, quantity: 1, currency: 'RUB', type: 'Currency' },
      { sku: subscriptionId, title: 'Premium — 1 месяц', unitPrice: 399, quantity: 1, currency: 'RUB', type: 'Subscription' },
    ]);
    await mockCheckoutApi(page, (body) => { orderBody = body; });
    await page.goto('/diamonds');
    await page.getByLabel('Cart').click();
    const drawer = page.locator('aside.cart-drawer');

    await expect(drawer.getByText('Алмазы и подписку нужно оформить отдельно.')).toHaveCount(0);
    await drawer.getByRole('button', { name: 'Перейти к оплате' }).click();
    await expect(drawer.getByText('Тестовая оплата', { exact: true }).last()).toBeVisible();
    expect(orderBody?.items).toHaveLength(2);
  });
});
