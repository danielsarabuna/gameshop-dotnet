import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll } from '../utils/layout-helpers';

test.describe('Challenger Stress Testing — M3 Layout & Responsiveness', () => {
  const pages = ['/', '/projects', '/diamonds', '/subscription', '/help', '/about'];

  test.describe('1. Zero Horizontal Scroll on 320px Viewport', () => {
    test.use({ viewport: { width: 320, height: 568 } });

    for (const pagePath of pages) {
      test(`page ${pagePath} on 320px has zero scrollX`, async ({ page }) => {
        await page.goto(pagePath);
        await page.waitForLoadState('networkidle');
        await assertZeroHorizontalScroll(page);
      });
    }
  });

  test.describe('2. Zero Horizontal Scroll on 390px Viewport', () => {
    test.use({ viewport: { width: 390, height: 844 } });

    for (const pagePath of pages) {
      test(`page ${pagePath} on 390px has zero scrollX`, async ({ page }) => {
        await page.goto(pagePath);
        await page.waitForLoadState('networkidle');
        await assertZeroHorizontalScroll(page);
      });
    }
  });

  test.describe('3. Cart Drawer Responsiveness on 320px Viewport', () => {
    test.use({ viewport: { width: 320, height: 568 } });

    test('cart drawer buttons are visible, clickable and not clipped on 320px', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      // Add item to cart to show full drawer content
      const addBtn = page.locator('.cards-grid-3 .glass-card button.btn-primary').first();
      await addBtn.click();

      const drawer = page.locator('aside.cart-drawer');
      await expect(drawer).toBeVisible();

      // Check drawer close button scoped to cart drawer
      const closeBtn = drawer.locator('button.cart-close-btn, button.mobile-close-btn');
      await expect(closeBtn).toBeVisible();

      // Check item elements: image, title, remove button
      const itemTitle = drawer.locator('div', { hasText: /Diamonds|Алмазов/i }).first();
      await expect(itemTitle).toBeVisible();

      const removeBtn = drawer.locator('button[title="Remove"]');
      await expect(removeBtn).toBeVisible();

      // Check checkout / action button in drawer footer
      const checkoutBtn = drawer.locator('button.checkout-btn, button.btn-primary').last();
      await expect(checkoutBtn).toBeVisible();

      // Verify drawer width fits viewport perfectly (<= 320px)
      const box = await drawer.boundingBox();
      expect(box).not.toBeNull();
      expect(Math.round(box!.width)).toBeLessThanOrEqual(320);

      // Verify zero scrollX while drawer is open
      await assertZeroHorizontalScroll(page);
    });
  });

  test.describe('4. Mobile Menu Overlay Vertical Scroll on Short Screen (320x568)', () => {
    test.use({ viewport: { width: 320, height: 568 } });

    test('mobile menu overlay supports vertical scrolling without horizontal overflow on 320x568', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const hamburger = page.locator('button.hamburger-btn');
      await hamburger.click();

      const overlay = page.locator('.mobile-menu-overlay');
      await expect(overlay).toBeVisible();

      // Verify overflow-y CSS property on overlay
      const overflowY = await overlay.evaluate((el) => window.getComputedStyle(el).overflowY);
      expect(['auto', 'scroll']).toContain(overflowY);

      // Verify zero horizontal scroll with menu open
      await assertZeroHorizontalScroll(page);

      // Verify all nav links inside mobile menu are reachable/visible
      const navItems = overlay.locator('.mobile-nav-item');
      const count = await navItems.count();
      expect(count).toBeGreaterThanOrEqual(5);

      for (let i = 0; i < count; i++) {
        const item = navItems.nth(i);
        await item.scrollIntoViewIfNeeded();
        await expect(item).toBeVisible();
      }

      // Close menu scoped to mobile menu overlay
      const closeBtn = overlay.locator('button.mobile-close-btn');
      await closeBtn.click();
      await expect(overlay).not.toBeVisible();
    });
  });
});
