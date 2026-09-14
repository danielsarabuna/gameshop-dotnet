import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll, assertNoElementOverlap, assertNoTextClipping } from '../utils/layout-helpers';
import { HeaderPOM } from '../page-objects/header.page';
import { CartDrawerPOM } from '../page-objects/cart.page';

const VIEWPORTS = [
  { name: '320px (iPhone SE 1st gen)', width: 320, height: 568 },
  { name: '375px (iPhone 6/7/8/SE 2nd gen)', width: 375, height: 667 },
  { name: '390x844 (iPhone 12/13/14 portrait)', width: 390, height: 844 },
  { name: '480px (Mobile wide / small tablet)', width: 480, height: 800 },
];

const TARGET_PAGES = [
  { name: 'Home', path: '/' },
  { name: 'Projects', path: '/projects' },
  { name: 'Diamonds', path: '/diamonds' },
  { name: 'Subscription', path: '/subscription' },
  { name: 'Help', path: '/help' },
  { name: 'About', path: '/about' },
];

test.describe('Mobile responsiveness audit', () => {

  for (const vp of VIEWPORTS) {
    test.describe(`Viewport ${vp.name}`, () => {
      test.use({ viewport: { width: vp.width, height: vp.height } });

      for (const pg of TARGET_PAGES) {
        test(`[${vp.width}px] Page '${pg.name}' (${pg.path}) - Zero horizontal scroll`, async ({ page }) => {
          await page.goto(pg.path);
          await page.waitForLoadState('networkidle');
          await assertZeroHorizontalScroll(page);
        });

        test(`[${vp.width}px] Page '${pg.name}' (${pg.path}) - No main title text clipping`, async ({ page }) => {
          await page.goto(pg.path);
          await page.waitForLoadState('networkidle');
          
          const pageTitle = page.locator('h1.page-title, h1.hero-title').first();
          if (await pageTitle.isVisible()) {
            await assertNoTextClipping(pageTitle);
          }
        });
      }

      test(`[${vp.width}px] Header compact controls & hamburger toggle`, async ({ page }) => {
        await page.goto('/');
        await page.waitForLoadState('networkidle');

        const header = new HeaderPOM(page);

        // Desktop nav should be hidden (< 767px)
        await expect(header.navLinks).not.toBeVisible();

        // Hamburger button should be visible
        await expect(header.hamburgerBtn).toBeVisible();

        // Verify elements inside compact header do not overlap
        await assertNoElementOverlap([header.logo, header.langPillBtn, header.authBtn, header.cartBtn, header.hamburgerBtn]);

        // Test hamburger button toggle open
        await header.openMobileMenu();
        await expect(header.mobileMenuOverlay).toBeVisible();
        await expect(header.hamburgerBtn).toHaveAttribute('aria-expanded', 'true');

        // Test toggle close
        await header.closeMobileMenu();
        await expect(header.mobileMenuOverlay).not.toBeVisible();
      });

      test(`[${vp.width}px] CartDrawer full-width (100%) & checkout buttons visible above bottom edge`, async ({ page }) => {
        await page.goto('/diamonds');
        await page.waitForLoadState('networkidle');

        const header = new HeaderPOM(page);
        const cart = new CartDrawerPOM(page);

        // Open cart drawer
        await header.openCart();
        await expect(cart.drawer).toBeVisible();

        // Check 100% full-width
        const box = await cart.drawer.boundingBox();
        expect(box).not.toBeNull();
        expect(Math.abs(box!.width - vp.width)).toBeLessThanOrEqual(2);

        // Add a product if possible to check action buttons when items present
        const buyBtn = page.locator('button.buy-btn').first();
        if (await buyBtn.isVisible()) {
          await buyBtn.click();
        }

        // Verify action buttons exist and are visible inside viewport
        const checkoutBtn = page.locator('aside.cart-drawer button.btn-primary');
        if (await checkoutBtn.isVisible()) {
          const cBox = await checkoutBtn.boundingBox();
          expect(cBox).not.toBeNull();
          // Ensure bottom edge of checkout button is within the viewport height
          expect(cBox!.y + cBox!.height).toBeLessThanOrEqual(vp.height + 5); // allowing slight tolerance
        }
      });
    });
  }
});
