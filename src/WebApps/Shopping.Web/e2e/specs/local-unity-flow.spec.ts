import { expect, test } from '@playwright/test';

const ticket = process.env.WEBSHOP_TEST_TICKET;
const playerName = process.env.WEBSHOP_TEST_PLAYER_NAME;
const shouldPurchase = process.env.WEBSHOP_TEST_PURCHASES === '1';

test.describe('local Unity → WebShop flow', () => {
  test.skip(!ticket || !playerName, 'WEBSHOP_TEST_TICKET and WEBSHOP_TEST_PLAYER_NAME are required');

  test('validates auth, catalog, providers and optional mock purchases', async ({ page }) => {
    await page.goto(`/diamonds?ticket=${encodeURIComponent(ticket!)}&userId=ignored&region=ignored&store=ignored&version=ignored`);

    await expect(page.getByRole('button', { name: playerName! })).toBeVisible({ timeout: 15_000 });
    await expect(page).toHaveURL(/\/diamonds$/);

    const diamondOffers = page.locator('.desktop-only-view').getByRole('button', { name: 'В корзину' });
    await expect(diamondOffers).toHaveCount(6);
    await diamondOffers.first().click();

    const cart = page.locator('aside.cart-drawer');
    await expect(cart.getByRole('button', { name: /ЮKassa/ })).toBeDisabled();
    await expect(cart.getByRole('button', { name: /Xsolla/ })).toBeDisabled();
    await expect(cart.getByRole('button', { name: /Тестовая оплата/ })).toBeEnabled();

    if (!shouldPurchase) {
      await cart.getByRole('button', { name: 'Очистить корзину' }).click();
      await expect(cart.getByText('Корзина пуста')).toBeVisible();
      return;
    }

    await cart.getByRole('button', { name: 'Оформить заказ' }).click();
    await expect(cart.getByText(/Подтвердите результат прямо здесь/)).toBeVisible();
    await cart.getByRole('button', { name: 'Подтвердить' }).click();
    await expect(page.getByRole('heading', { name: 'Оплата прошла!' })).toBeVisible({ timeout: 20_000 });

    await page.goto('/subscription');
    const subscriptions = page.locator('.desktop-only-view').getByRole('button', { name: 'Выбрать тариф' });
    await expect(subscriptions).toHaveCount(3);
    await subscriptions.first().click();
    await cart.getByRole('button', { name: 'Оформить заказ' }).click();
    await expect(cart.getByText(/Подтвердите результат прямо здесь/)).toBeVisible();
    await cart.getByRole('button', { name: 'Подтвердить' }).click();
    await expect(page.getByRole('heading', { name: 'Оплата прошла!' })).toBeVisible({ timeout: 20_000 });

    await page.getByRole('button', { name: 'Cart', exact: true }).click();
    await expect(cart.getByText('Корзина пуста')).toBeVisible();
  });
});
