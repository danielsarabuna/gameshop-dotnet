import { Page, Locator } from '@playwright/test';

export class HeaderPOM {
  readonly page: Page;
  readonly logo: Locator;
  readonly navLinks: Locator;
  readonly hamburgerBtn: Locator;
  readonly mobileMenuOverlay: Locator;
  readonly mobileCloseBtn: Locator;
  readonly mobileNavItems: Locator;
  readonly langPillBtn: Locator;
  readonly langDropdown: Locator;
  readonly cartBtn: Locator;
  readonly cartBadge: Locator;
  readonly authBtn: Locator;

  constructor(page: Page) {
    this.page = page;
    this.logo = page.locator('.logo-link');
    this.navLinks = page.locator('nav.nav-links');
    this.hamburgerBtn = page.locator('button.hamburger-btn');
    this.mobileMenuOverlay = page.locator('.mobile-menu-overlay');
    this.mobileCloseBtn = page.locator('.mobile-menu-overlay .mobile-close-btn');
    this.mobileNavItems = page.locator('.mobile-menu-overlay .mobile-nav-item');
    this.langPillBtn = page.locator('button.lang-pill-btn');
    this.langDropdown = page.locator('.lang-dropdown');
    this.cartBtn = page.locator('button.cart-square-btn');
    this.cartBadge = page.locator('.cart-badge');
    this.authBtn = page.locator('button.auth-pill-btn');
  }

  async openMobileMenu() {
    await this.hamburgerBtn.click();
  }

  async closeMobileMenu() {
    await this.mobileCloseBtn.click();
  }

  async openLanguageMenu() {
    await this.langPillBtn.click();
  }

  async selectLanguage(code: 'RU' | 'EN' | 'DE' | 'FR' | 'ES') {
    if (!(await this.langDropdown.isVisible())) {
      await this.openLanguageMenu();
    }
    await this.langDropdown.locator('.lang-option', { hasText: code }).click();
  }

  async openCart() {
    await this.cartBtn.click();
  }
}
