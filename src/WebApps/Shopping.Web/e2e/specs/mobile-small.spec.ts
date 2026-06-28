import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll, assertNoElementOverlap } from '../utils/layout-helpers';
import { HeaderPOM } from '../page-objects/header.page';
import { CartDrawerPOM } from '../page-objects/cart.page';

test.describe('Mobile Small Viewport (320x568)', () => {
  const routes = ['/', '/projects', '/diamonds', '/subscription', '/help', '/about'];

  for (const route of routes) {
    test(`should have zero horizontal scrollX on 320px for route ${route}`, async ({ page }) => {
      await page.goto(route);
      await page.waitForLoadState('networkidle');
      await assertZeroHorizontalScroll(page);
    });
  }

  test('should not have overlapping header elements on 320px viewport', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const header = new HeaderPOM(page);
    await assertNoElementOverlap([header.logo, header.langPillBtn, header.authBtn, header.cartBtn, header.hamburgerBtn]);
  });

  test('should expand and collapse burger menu overlay cleanly at 320px', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const header = new HeaderPOM(page);
    await expect(header.navLinks).not.toBeVisible();
    await expect(header.hamburgerBtn).toBeVisible();

    await header.openMobileMenu();
    await expect(header.mobileMenuOverlay).toBeVisible();
    await expect(header.mobileNavItems.first()).toBeVisible();

    await header.closeMobileMenu();
    await expect(header.mobileMenuOverlay).not.toBeVisible();
  });

  test('should present cart drawer full-width (100vw) on 320px mobile viewport', async ({ page }) => {
    await page.goto('/diamonds');
    await page.waitForLoadState('networkidle');

    const header = new HeaderPOM(page);
    const cart = new CartDrawerPOM(page);

    await header.openCart();
    await cart.assert100vwMobileWidth();

    await cart.close();
    await expect(cart.drawer).not.toHaveClass(/open/);
  });
});
