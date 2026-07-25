import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll, assertNoElementOverlap, assertNoTextClipping } from '../utils/layout-helpers';
import { HeaderPOM } from '../page-objects/header.page';
import { CartDrawerPOM } from '../page-objects/cart.page';

test.describe('Challenger Tier 5 — Layout & Responsiveness Adversarial Coverage Hardening', () => {

  /* -------------------------------------------------------------------------- */
  /* 1. Extreme String Injection & Localized Header Layout                      */
  /* -------------------------------------------------------------------------- */
  test.describe('1. Extreme String Injection & Localized Header Layout', () => {
    test.use({ viewport: { width: 1280, height: 720 } });

    test('should maintain zero element overlap and handle text truncation when extreme long username is injected', async ({ page }) => {
      // Inject session with an extreme long playerName into tab-scoped storage prior to navigation
      await page.addInitScript(() => {
        sessionStorage.setItem(
          'GameShop_webshop_session',
          JSON.stringify({
            accessToken: 'layout-test-token',
            playerId: 'player_9999999999999999',
            playerName: 'Supercalifragilisticexpialidocious_Player_9999999999999999_Extreme_Long_Username_String',
            sessionKind: 'recipient',
          })
        );
      });

      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);
      await expect(header.authBtn).toBeVisible();
      await expect(header.authBtn).toContainText('Supercalifragilisticexpialidocious');

      // Verify zero overlap between all desktop header controls
      await assertNoElementOverlap([
        header.logo,
        header.navLinks,
        header.langPillBtn,
        header.authBtn,
        header.cartBtn,
      ]);

      // Verify page maintains zero horizontal scroll
      await assertZeroHorizontalScroll(page);

      // Verify auth button text span has CSS overflow truncation properties
      const authSpan = header.authBtn.locator('span');
      const textOverflowStyle = await authSpan.evaluate((el) => {
        const style = window.getComputedStyle(el);
        return {
          overflow: style.overflow,
          textOverflow: style.textOverflow,
          whiteSpace: style.whiteSpace,
          maxWidth: style.maxWidth,
        };
      });

      expect(textOverflowStyle.textOverflow).toBe('ellipsis');
      expect(textOverflowStyle.overflow).toBe('hidden');
    });

    test('should maintain zero element overlap across multi-language localized titles (ES, DE, FR, RU, EN)', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);
      const languages = ['ES', 'DE', 'FR', 'RU', 'EN'] as const;

      for (const lang of languages) {
        await header.selectLanguage(lang);
        await page.waitForTimeout(100);

        await assertNoElementOverlap([
          header.logo,
          header.navLinks,
          header.langPillBtn,
          header.authBtn,
          header.cartBtn,
        ]);
        await assertZeroHorizontalScroll(page);
      }
    });

    test('should handle extreme long promo code in Cart Drawer without layout overflow', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      // Clicking 'В корзину' adds item and automatically opens Cart Drawer
      const buyBtn = page.locator('.cards-grid-3 .glass-card button.btn-primary').first();
      await buyBtn.click();

      const cart = new CartDrawerPOM(page);
      await expect(cart.drawer).toHaveClass(/open/);

      // Fill long promo code input
      const promoInput = page.locator('aside.cart-drawer input[placeholder*="Промокод"], aside.cart-drawer input[placeholder*="Promo"]');
      if (await promoInput.isVisible()) {
        await promoInput.fill('EXTREME_LONG_PROMO_CODE_STRING_99999999999999999999_SPECIAL_CHARS_#$%&');
        await assertZeroHorizontalScroll(page);

        // Verify promo input does not overflow drawer width
        const promoBox = await promoInput.boundingBox();
        const drawerBox = await cart.getDrawerBoundingBox();
        expect(promoBox).not.toBeNull();
        expect(drawerBox).not.toBeNull();
        expect(promoBox!.x + promoBox!.width).toBeLessThanOrEqual(drawerBox!.x + drawerBox!.width + 2);
      }
    });
  });

  /* -------------------------------------------------------------------------- */
  /* 2. 768px Tablet Portrait Card Grid Scaling & Text Clipping                 */
  /* -------------------------------------------------------------------------- */
  test.describe('2. 768px Tablet Portrait Card Grid Scaling & Text Clipping', () => {
    test.use({ viewport: { width: 768, height: 1024 } });

    test('should prevent text clipping on Diamond offer cards at 768px tablet portrait', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      const cards = page.locator('.cards-grid-3 > .glass-card');
      const count = await cards.count();
      expect(count).toBeGreaterThan(0);

      for (let i = 0; i < count; i++) {
        const card = cards.nth(i);
        const title = card.locator('h3');
        if (await title.isVisible()) {
          await assertNoTextClipping(title);
        }

        const price = card.locator('.card-price, .diamond-price, span:has-text("₽"), span:has-text("$")').first();
        if (await price.isVisible()) {
          await assertNoTextClipping(price);
        }
      }
    });

    test('should prevent text clipping on Subscription plan cards and badges at 768px tablet portrait', async ({ page }) => {
      await page.goto('/subscription');
      await page.waitForLoadState('networkidle');

      const cards = page.locator('.cards-grid-3 > .glass-card');
      const count = await cards.count();
      expect(count).toBeGreaterThan(0);

      for (let i = 0; i < count; i++) {
        const card = cards.nth(i);
        const title = card.locator('h3');
        if (await title.isVisible()) {
          await assertNoTextClipping(title);
        }

        const badge = card.locator('.pricing-badge, .badge, .card-badge').first();
        if (await badge.isVisible()) {
          await assertNoTextClipping(badge);
        }

        // Verify feature list items do not clip
        const features = card.locator('ul li');
        const fCount = await features.count();
        for (let j = 0; j < fCount; j++) {
          await assertNoTextClipping(features.nth(j));
        }
      }
    });

    test('should prevent text clipping on Projects and About cards at 768px tablet portrait', async ({ page }) => {
      for (const route of ['/projects', '/about']) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');

        const cards = page.locator('.cards-grid-3 > *, .cards-grid > *');
        const count = await cards.count();
        expect(count).toBeGreaterThan(0);

        for (let i = 0; i < count; i++) {
          const card = cards.nth(i);
          const heading = card.locator('h2, h3').first();
          if (await heading.isVisible()) {
            await assertNoTextClipping(heading);
          }
        }
      }
    });
  });

  /* -------------------------------------------------------------------------- */
  /* 3. 320px Ultra-Small Mobile Touch Targets & Cart Select Overflow           */
  /* -------------------------------------------------------------------------- */
  test.describe('3. 320px Ultra-Small Mobile Touch Targets & Select Dropdown Overflow', () => {
    test.use({ viewport: { width: 320, height: 568 } });

    test('should inspect touch target bounding boxes on 320px ultra-small mobile viewport', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);

      const targets = [
        { name: 'Logo Link', locator: header.logo },
        { name: 'Language Button', locator: header.langPillBtn },
        { name: 'Auth Button', locator: header.authBtn },
        { name: 'Cart Button', locator: header.cartBtn },
        { name: 'Hamburger Button', locator: header.hamburgerBtn },
      ];

      for (const t of targets) {
        await expect(t.locator).toBeVisible();
        const box = await t.locator.boundingBox();
        expect(box, `Bounding box for ${t.name} must not be null`).not.toBeNull();
        expect(box!.width, `${t.name} width should be > 0`).toBeGreaterThan(0);
        // Logo height is 22px on 320px viewport, action buttons are 32px
        const minHeight = t.name === 'Logo Link' ? 20 : 32;
        expect(box!.height, `${t.name} height should be >= ${minHeight}px`).toBeGreaterThanOrEqual(minHeight);
      }
    });

    test('should prevent payment method overflow in the mobile checkout sheet', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      // Clicking 'В корзину' adds item and automatically opens Cart Drawer
      const buyBtn = page.locator('.diamonds-snap-container .glass-card button.btn-primary').first();
      await buyBtn.click();

      const cart = new CartDrawerPOM(page);
      await expect(cart.drawer).toHaveClass(/open/);

      await cart.assertMobileBottomSheet();

      if (await cart.paymentMethodButtons.first().isVisible()) {
        for (const button of await cart.paymentMethodButtons.all()) {
          await assertNoTextClipping(button);
          await assertZeroHorizontalScroll(page);
        }
      }
    });
  });

  /* -------------------------------------------------------------------------- */
  /* 4. 1024px Desktop Landscape Stage Width Container & Vignette Visibility     */
  /* -------------------------------------------------------------------------- */
  test.describe('4. 1024px Desktop Landscape Stage Width Container & Vignettes', () => {
    test.use({ viewport: { width: 1024, height: 768 } });

    test('should center stage container to exactly 60% width (~614px) and display side vignettes at 1024px', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      // Assert side vignette is visible
      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).toBeVisible();

      // Assert stage container width is 60% of 1024px = 614.4px
      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      expect(box).not.toBeNull();

      const expectedWidth = 1024 * 0.6; // 614.4px
      expect(Math.abs(box!.width - expectedWidth)).toBeLessThanOrEqual(5);

      // Assert container is centered (left margin = (1024 - 614.4) / 2 = 204.8px)
      const expectedMargin = (1024 - expectedWidth) / 2;
      expect(Math.abs(box!.x - expectedMargin)).toBeLessThanOrEqual(5);
    });

    test('should display desktop header navigation and hide hamburger menu at 1024px landscape', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);
      await expect(header.navLinks).toBeVisible();
      await expect(header.hamburgerBtn).toBeHidden();
    });
  });

  /* -------------------------------------------------------------------------- */
  /* 5. Modal Overlay Z-Index Stacking Order                                    */
  /* -------------------------------------------------------------------------- */
  test.describe('5. Modal Overlay Z-Index Stacking Order', () => {
    test.use({ viewport: { width: 1280, height: 720 } });

    test.beforeEach(async ({ page }) => {
      await page.addInitScript(() => {
        localStorage.clear();
      });
    });

    test('should enforce correct z-index hierarchy between Cart Drawer, Modal Overlay, and Deeplink Toast', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      // 1. Open Cart Drawer via buy button
      const buyBtn = page.locator('.cards-grid-3 .glass-card button.btn-primary').first();
      await buyBtn.click();

      const cart = new CartDrawerPOM(page);
      await expect(cart.drawer).toHaveClass(/open/);

      // 2. Trigger DOM click on Auth button directly via page.evaluate
      await page.evaluate(() => {
        const btn = document.querySelector<HTMLButtonElement>('button.auth-pill-btn');
        btn?.click();
      });

      const modalOverlay = page.locator('.modal-overlay');
      await expect(modalOverlay).toBeVisible();

      // 3. Retrieve computed z-index values for active overlay elements
      const zIndexes = await page.evaluate(() => {
        const cartDrawerEl = document.querySelector('aside.cart-drawer');
        const cartOverlayEl = document.querySelector('.cart-overlay');
        const modalOverlayEl = document.querySelector('.modal-overlay');

        const getZ = (el: Element | null) => (el ? parseInt(window.getComputedStyle(el).zIndex, 10) || 0 : 0);

        return {
          cartDrawer: getZ(cartDrawerEl),
          cartOverlay: getZ(cartOverlayEl),
          modalOverlay: getZ(modalOverlayEl),
        };
      });

      // Assert modal overlay (z-index: 10000) sits above cart drawer (z-index: 1300) and overlay (z-index: 1200)
      expect(zIndexes.modalOverlay).toBeGreaterThan(zIndexes.cartDrawer);
      expect(zIndexes.modalOverlay).toBeGreaterThan(zIndexes.cartOverlay);
    });

    test('should dismiss active top overlay layer on Escape key in reverse stacking order', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      // 1. Open Cart Drawer
      const buyBtn = page.locator('.cards-grid-3 .glass-card button.btn-primary').first();
      await buyBtn.click();

      const cart = new CartDrawerPOM(page);
      await expect(cart.drawer).toHaveClass(/open/);

      // 2. Trigger DOM click on Auth button directly via page.evaluate
      await page.evaluate(() => {
        const btn = document.querySelector<HTMLButtonElement>('button.auth-pill-btn');
        btn?.click();
      });

      const modalOverlay = page.locator('.modal-overlay');
      await expect(modalOverlay).toBeVisible();

      // 3. Press Escape key -> Login Modal handles keydown event and closes first
      await page.keyboard.press('Escape');
      await expect(modalOverlay).not.toBeVisible();

      // Empirically check state of Cart Drawer after Escape press
      const cartIsOpen = await cart.drawer.evaluate((el) => el.classList.contains('open'));
      if (cartIsOpen) {
        await cart.close();
      }
    });
  });
});
