import { Page, Locator, expect } from '@playwright/test';

export class CartDrawerPOM {
  readonly page: Page;
  readonly drawer: Locator;
  readonly overlay: Locator;
  readonly closeBtn: Locator;
  readonly emptyState: Locator;
  readonly clearCartBtn: Locator;
  readonly checkoutBtn: Locator;
  readonly playerIdInput: Locator;
  readonly playerNameInput: Locator;
  readonly paymentMethodSelect: Locator;

  constructor(page: Page) {
    this.page = page;
    this.drawer = page.locator('aside.cart-drawer');
    this.overlay = page.locator('.cart-overlay');
    this.closeBtn = page.locator('aside.cart-drawer .cart-close-btn, aside.cart-drawer .mobile-close-btn');
    this.emptyState = page.locator('.cart-empty');
    this.clearCartBtn = page.locator('aside.cart-drawer button:has-text("Очистить"), aside.cart-drawer button:has-text("Clear")');
    this.checkoutBtn = page.locator('aside.cart-drawer button.btn-primary');
    this.playerIdInput = page.locator('aside.cart-drawer input[placeholder*="ID"]');
    this.playerNameInput = page.locator('aside.cart-drawer input[placeholder*="Имя"], aside.cart-drawer input[placeholder*="Name"]');
    this.paymentMethodSelect = page.locator('aside.cart-drawer select');
  }

  async close() {
    await this.closeBtn.click();
  }

  async getDrawerBoundingBox() {
    return await this.drawer.boundingBox();
  }

  async assert100vwMobileWidth() {
    await expect(this.drawer).toHaveClass(/open/);
    const box = await this.getDrawerBoundingBox();
    const viewportWidth = await this.page.evaluate(() => window.innerWidth);
    expect(box).not.toBeNull();
    expect(Math.abs(box!.width - viewportWidth)).toBeLessThanOrEqual(2);
  }
}
