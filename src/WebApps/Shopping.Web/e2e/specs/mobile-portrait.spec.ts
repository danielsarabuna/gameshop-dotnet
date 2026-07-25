import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll, assertNoElementOverlap } from '../utils/layout-helpers';
import { HeaderPOM } from '../page-objects/header.page';
import { CartDrawerPOM } from '../page-objects/cart.page';

test.describe('Mobile & Responsive Header (320px–844px)', () => {
  const routes = ['/', '/projects', '/diamonds', '/subscription', '/help', '/about'];

  for (const route of routes) {
    test(`should have zero horizontal scrollX on route ${route}`, async ({ page }) => {
      await page.goto(route);
      await page.waitForLoadState('networkidle');
      await assertZeroHorizontalScroll(page);
    });
  }

  test('should not have overlapping elements in header and hero section', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const header = new HeaderPOM(page);
    await assertNoElementOverlap([header.logo, header.langPillBtn, header.authBtn, header.cartBtn, header.hamburgerBtn]);
  });

  test('should hide desktop nav-links and expand/collapse burger menu overlay', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const header = new HeaderPOM(page);

    // Desktop nav tabs should be hidden on mobile
    await expect(header.navLinks).not.toBeVisible();

    // Hamburger button should be visible
    await expect(header.hamburgerBtn).toBeVisible();

    // Open mobile burger menu
    await header.openMobileMenu();
    await expect(header.mobileMenuOverlay).toBeVisible();
    await expect(header.mobileNavItems.first()).toBeVisible();

    // Close mobile menu
    await header.closeMobileMenu();
    await expect(header.mobileMenuOverlay).not.toBeVisible();
  });

  test('should present a compact cart bottom sheet on mobile', async ({ page }) => {
    await page.goto('/diamonds');
    await page.waitForLoadState('networkidle');

    const cart = new CartDrawerPOM(page);

    await page.getByRole('button', { name: 'В корзину' }).first().click();
    await cart.assertMobileBottomSheet();

    await cart.close();
    await expect(cart.drawer).not.toHaveClass(/open/);
  });
});
