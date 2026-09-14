import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll } from '../utils/layout-helpers';

test.describe('Tablet responsiveness and orientation', () => {
  // Boundary 1: Tablet Portrait exact breakpoint at 768x1024
  test.describe('Tablet Portrait Boundary (768x1024)', () => {
    test.use({ viewport: { width: 768, height: 1024 } });

    test('verify zero horizontal scroll across all routes on 768x1024', async ({ page }) => {
      const routes = ['/', '/diamonds', '/subscription', '/projects', '/help', '/about'];
      for (const route of routes) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');
        await assertZeroHorizontalScroll(page);
      }
    });

    test('verify 100% full-width stage container on 768x1024', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      expect(box).not.toBeNull();
      expect(Math.abs(box!.width - 768)).toBeLessThanOrEqual(2);
    });

    test('verify side vignettes are strictly hidden on 768x1024', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).not.toBeVisible();
    });

    test('verify hamburger menu button visible and nav links hidden on 768x1024', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const hamburger = page.locator('.hamburger-btn');
      const navLinks = page.locator('.nav-links');

      await expect(hamburger).toBeVisible();
      await expect(navLinks).not.toBeVisible();
    });
  });

  // Boundary 2: Upper Tablet Portrait limit at 1023x1366
  test.describe('Tablet Portrait Upper Limit (1023x1366)', () => {
    test.use({ viewport: { width: 1023, height: 1366 } });

    test('verify stage container is 100% full width and vignettes hidden at 1023px breakpoint', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();
      const box = await stage.boundingBox();
      expect(box).not.toBeNull();
      expect(Math.abs(box!.width - 1023)).toBeLessThanOrEqual(2);

      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).not.toBeVisible();
    });
  });

  // Boundary 3: Tablet Landscape exact breakpoint at 1024x768
  test.describe('Tablet Landscape Boundary (1024x768)', () => {
    test.use({ viewport: { width: 1024, height: 768 } });

    test('verify zero horizontal scroll across all routes on 1024x768', async ({ page }) => {
      const routes = ['/', '/diamonds', '/subscription', '/projects', '/help', '/about'];
      for (const route of routes) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');
        await assertZeroHorizontalScroll(page);
      }
    });

    test('verify stage container is centered 60% width (614.4px) on 1024x768', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      expect(box).not.toBeNull();
      expect(Math.abs(box!.width - 614.4)).toBeLessThanOrEqual(5);

      // Verify horizontal centering
      const expectedLeftMargin = (1024 - box!.width) / 2;
      expect(Math.abs(box!.x - expectedLeftMargin)).toBeLessThanOrEqual(5);
    });

    test('verify side vignettes are visible with backdrop blur on 1024x768', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).toBeVisible();
    });

    test('verify full desktop navigation links visible and hamburger hidden on 1024x768', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const navLinks = page.locator('.nav-links');
      const hamburger = page.locator('.hamburger-btn');

      await expect(navLinks).toBeVisible();
      await expect(hamburger).not.toBeVisible();
    });
  });
});
