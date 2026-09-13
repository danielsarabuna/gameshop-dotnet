import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll } from '../utils/layout-helpers';
import { HeaderPOM } from '../page-objects/header.page';
import { CartDrawerPOM } from '../page-objects/cart.page';

test.describe('EMPIRICAL CHALLENGER M3 — E2E Suite Layout & Viewport Correctness', () => {

  /* -------------------------------------------------------------------------- */
  /* 1. Sensitivity Test: Zero Horizontal Scroll Assertion Verification         */
  /* -------------------------------------------------------------------------- */
  test.describe('1. Zero Horizontal Scroll Assertion Sensitivity & Robustness', () => {
    test.use({ viewport: { width: 390, height: 844 } });

    test('should pass assertZeroHorizontalScroll under normal clean state', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');
      await assertZeroHorizontalScroll(page);
    });

    test('should fail assertZeroHorizontalScroll when horizontal overflow element is injected (Oracle Sensitivity)', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Inject 3000px wide element to trigger horizontal scroll
      await page.evaluate(() => {
        const div = document.createElement('div');
        div.id = 'leak-test-overflow';
        div.style.width = '3000px';
        div.style.height = '10px';
        div.style.background = 'red';
        document.body.appendChild(div);
      });

      // Verify that assertZeroHorizontalScroll detects the leak and throws
      let caughtError = false;
      try {
        await assertZeroHorizontalScroll(page);
      } catch (err) {
        caughtError = true;
      }
      expect(caughtError, 'assertZeroHorizontalScroll MUST detect horizontal overflow elements').toBe(true);

      // Clean up injected element
      await page.evaluate(() => {
        document.getElementById('leak-test-overflow')?.remove();
      });

      // Verify clean pass after removal
      await assertZeroHorizontalScroll(page);
    });
  });

  /* -------------------------------------------------------------------------- */
  /* 2. Burger Overlay Interactions & Key Accessibility                         */
  /* -------------------------------------------------------------------------- */
  test.describe('2. Burger Overlay Mobile Interactions (390x844)', () => {
    test.use({ viewport: { width: 390, height: 844 } });

    test('should toggle mobile menu, backdrop blur, close on Escape, and close on item click', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);

      // 1. Desktop nav hidden, hamburger visible
      await expect(header.navLinks).not.toBeVisible();
      await expect(header.hamburgerBtn).toBeVisible();

      // 2. Open mobile menu
      await header.openMobileMenu();
      await expect(header.mobileMenuOverlay).toBeVisible();

      // Verify all 5 nav links present
      const navItems = page.locator('.mobile-menu-overlay .mobile-nav-item');
      await expect(navItems).toHaveCount(5);

      // 3. Test Escape key dismissal
      await page.keyboard.press('Escape');
      await expect(header.mobileMenuOverlay).not.toBeVisible();

      // 4. Open again and test nav link navigation
      await header.openMobileMenu();
      await expect(header.mobileMenuOverlay).toBeVisible();
      await navItems.filter({ hasText: 'АЛМАЗЫ' }).or(navItems.filter({ hasText: 'DIAMONDS' })).first().click();

      // Overlay should close and URL change to /diamonds
      await expect(header.mobileMenuOverlay).not.toBeVisible();
      expect(page.url()).toContain('/diamonds');
    });
  });

  /* -------------------------------------------------------------------------- */
  /* 3. Cart Drawer Width Assertions across Mobile & Desktop                    */
  /* -------------------------------------------------------------------------- */
  test.describe('3. Cart Drawer Width Assertions (Mobile vs Desktop)', () => {
    test('should be a compact bottom sheet on 390x844 mobile', async ({ page }) => {
      await page.setViewportSize({ width: 390, height: 844 });
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      const cart = new CartDrawerPOM(page);

      await page.getByRole('button', { name: 'В корзину' }).first().click();
      await cart.assertMobileBottomSheet();

      const box = await cart.getDrawerBoundingBox();
      expect(box).not.toBeNull();
      expect(Math.abs(box!.width - 390)).toBeLessThanOrEqual(2);
    });

    test('should keep the sticky footer on 320x568 ultra-small mobile', async ({ page }) => {
      await page.setViewportSize({ width: 320, height: 568 });
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      const cart = new CartDrawerPOM(page);

      await page.getByRole('button', { name: 'В корзину' }).first().click();
      await cart.assertMobileBottomSheet();

      const box = await cart.getDrawerBoundingBox();
      expect(box).not.toBeNull();
      expect(Math.abs(box!.width - 320)).toBeLessThanOrEqual(2);
    });

    test('should stay within the 420–460px desktop drawer range', async ({ page }) => {
      await page.setViewportSize({ width: 1024, height: 768 });
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);
      const cart = new CartDrawerPOM(page);

      await header.openCart();
      await expect(cart.drawer).toHaveClass(/open/);

      const box = await cart.getDrawerBoundingBox();
      expect(box).not.toBeNull();
      expect(box!.width).toBeGreaterThanOrEqual(420);
      expect(box!.width).toBeLessThanOrEqual(460);
    });
  });

  /* -------------------------------------------------------------------------- */
  /* 4. Tablet Landscape (1024x768) Vignette Centering & Breakpoint Boundary     */
  /* -------------------------------------------------------------------------- */
  test.describe('4. Tablet Landscape Vignette Centering & 1023px/1024px Breakpoint Boundary', () => {
    test('1024x768 Landscape: side vignettes visible, stage container 60% centered', async ({ page }) => {
      await page.setViewportSize({ width: 1024, height: 768 });
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Side vignette is visible
      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).toBeVisible();

      // Stage container is 60% width = 614.4px
      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      expect(box).not.toBeNull();
      expect(Math.abs(box!.width - 614.4)).toBeLessThanOrEqual(3);

      // Centered: margin x = (1024 - 614.4) / 2 = 204.8px
      const expectedX = (1024 - 614.4) / 2;
      expect(Math.abs(box!.x - expectedX)).toBeLessThanOrEqual(3);
    });

    test('1023x768 Portrait Boundary: side vignettes hidden, stage container 100% full width', async ({ page }) => {
      await page.setViewportSize({ width: 1023, height: 768 });
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Side vignette is hidden
      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).not.toBeVisible();

      // Stage container fills width = 1023px
      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      expect(box).not.toBeNull();
      expect(Math.abs(box!.width - 1023)).toBeLessThanOrEqual(2);
      expect(Math.abs(box!.x - 0)).toBeLessThanOrEqual(2);
    });
  });
});
