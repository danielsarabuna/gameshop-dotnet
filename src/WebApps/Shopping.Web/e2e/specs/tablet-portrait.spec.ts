import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll } from '../utils/layout-helpers';

test.describe('Tablet Portrait (768x1024)', () => {
  test.use({ viewport: { width: 768, height: 1024 } });

  test('should render tablet responsive view with hidden side vignettes', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const vignette = page.locator('.side-gradient-vignette');
    await expect(vignette).not.toBeVisible();
  });

  test('should hide hero character art on tablet portrait', async ({ page }) => {
    await page.goto('/');
    await page.waitForLoadState('networkidle');

    const heroChar = page.locator('.hero-char, .hero-char-left');
    await expect(heroChar).toBeHidden();
  });

  test('should expand stage container to full width on 768px tablet', async ({ page }) => {
    await page.goto('/diamonds');
    await page.waitForLoadState('networkidle');

    const stage = page.locator('.stage-center-container');
    await expect(stage).toBeVisible();

    const box = await stage.boundingBox();
    const viewportWidth = await page.evaluate(() => window.innerWidth);
    expect(box).not.toBeNull();
    expect(Math.abs(box!.width - viewportWidth)).toBeLessThanOrEqual(2);
  });

  test('should maintain zero horizontal scroll on tablet portrait', async ({ page }) => {
    await page.goto('/subscription');
    await page.waitForLoadState('networkidle');
    await assertZeroHorizontalScroll(page);
  });

  const routes = [
    { path: '/diamonds', selector: '.cards-grid-3 > *' },
    { path: '/subscription', selector: '.cards-grid-3 > *' },
    { path: '/projects', selector: '.cards-grid-3 > *' },
    { path: '/about', selector: '.cards-grid > *' },
  ];

  for (const r of routes) {
    test(`should arrange cards in 2-column grid on tablet portrait for ${r.path}`, async ({ page }) => {
      await page.goto(r.path);
      await page.waitForLoadState('networkidle');

      const cards = page.locator(r.selector);
      const count = await cards.count();
      expect(count).toBeGreaterThanOrEqual(2);

      const box0 = await cards.nth(0).boundingBox();
      const box1 = await cards.nth(1).boundingBox();
      expect(box0).not.toBeNull();
      expect(box1).not.toBeNull();
      expect(box1!.x).toBeGreaterThan(box0!.x);
    });
  }
});
