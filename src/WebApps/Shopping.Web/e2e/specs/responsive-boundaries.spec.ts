import { test, expect } from '@playwright/test';
import { assertZeroHorizontalScroll } from '../utils/layout-helpers';

test.describe('Responsive breakpoint boundaries', () => {
  const routes = ['/', '/projects', '/diamonds', '/subscription', '/help', '/about'];

  test.describe('1. Exact Boundary Pixel Widths Evaluation', () => {

    test('767px (Mobile Upper Limit): 100% stage, 1-column cards grid, hidden vignettes, hamburger active', async ({ page }) => {
      await page.setViewportSize({ width: 767, height: 1024 });

      for (const route of routes) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');

        // Zero horizontal scroll
        await assertZeroHorizontalScroll(page);

        // Stage container fills width (767px)
        const stage = page.locator('.stage-center-container');
        const stageBox = await stage.boundingBox();
        expect(stageBox).not.toBeNull();
        expect(Math.round(stageBox!.width)).toBe(767);

        // Vignette is hidden
        const vignette = page.locator('.side-gradient-vignette');
        const vignetteDisplay = await vignette.evaluate(el => window.getComputedStyle(el).display);
        expect(vignetteDisplay).toBe('none');

        // Nav links hidden, hamburger visible
        const navLinks = page.locator('.nav-links');
        const hamburger = page.locator('.hamburger-btn');
        await expect(navLinks).not.toBeVisible();
        await expect(hamburger).toBeVisible();

        // Cards grid 1 column on catalog / card pages
        const grid = page.locator('.cards-grid, .cards-grid-3').first();
        if (await grid.count() > 0) {
          const gridTemplate = await grid.evaluate(el => window.getComputedStyle(el).gridTemplateColumns);
          const colCount = gridTemplate.split(' ').length;
          expect(colCount).toBe(1);
        }
      }
    });

    test('768px (Tablet Portrait Lower Limit): 100% stage, 2-column cards grid, hidden vignettes, hamburger active', async ({ page }) => {
      await page.setViewportSize({ width: 768, height: 1024 });

      for (const route of routes) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');

        // Zero horizontal scroll
        await assertZeroHorizontalScroll(page);

        // Stage container fills width (768px)
        const stage = page.locator('.stage-center-container');
        const stageBox = await stage.boundingBox();
        expect(stageBox).not.toBeNull();
        expect(Math.round(stageBox!.width)).toBe(768);

        // Vignette is hidden
        const vignette = page.locator('.side-gradient-vignette');
        const vignetteDisplay = await vignette.evaluate(el => window.getComputedStyle(el).display);
        expect(vignetteDisplay).toBe('none');

        // Nav links hidden, hamburger visible
        const navLinks = page.locator('.nav-links');
        const hamburger = page.locator('.hamburger-btn');
        await expect(navLinks).not.toBeVisible();
        await expect(hamburger).toBeVisible();

        // Cards grid 2 columns on catalog / card pages
        const grid = page.locator('.cards-grid, .cards-grid-3').first();
        if (await grid.count() > 0) {
          const gridTemplate = await grid.evaluate(el => window.getComputedStyle(el).gridTemplateColumns);
          const colCount = gridTemplate.split(' ').length;
          expect(colCount).toBe(2);
        }
      }
    });

    test('1023px (Tablet Portrait Upper Limit): 100% stage, 2-column cards grid, hidden vignettes, hamburger active', async ({ page }) => {
      await page.setViewportSize({ width: 1023, height: 768 });

      for (const route of routes) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');

        // Zero horizontal scroll
        await assertZeroHorizontalScroll(page);

        // Stage container fills width (1023px)
        const stage = page.locator('.stage-center-container');
        const stageBox = await stage.boundingBox();
        expect(stageBox).not.toBeNull();
        expect(Math.round(stageBox!.width)).toBe(1023);

        // Vignette is hidden
        const vignette = page.locator('.side-gradient-vignette');
        const vignetteDisplay = await vignette.evaluate(el => window.getComputedStyle(el).display);
        expect(vignetteDisplay).toBe('none');

        // Nav links hidden, hamburger visible
        const navLinks = page.locator('.nav-links');
        const hamburger = page.locator('.hamburger-btn');
        await expect(navLinks).not.toBeVisible();
        await expect(hamburger).toBeVisible();

        // Cards grid 2 columns on catalog / card pages
        const grid = page.locator('.cards-grid, .cards-grid-3').first();
        if (await grid.count() > 0) {
          const gridTemplate = await grid.evaluate(el => window.getComputedStyle(el).gridTemplateColumns);
          const colCount = gridTemplate.split(' ').length;
          expect(colCount).toBe(2);
        }
      }
    });

    test('1024px (Tablet Landscape Lower Limit): ~60% stage width, side vignettes visible, desktop nav active', async ({ page }) => {
      await page.setViewportSize({ width: 1024, height: 768 });

      for (const route of routes) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');

        // Zero horizontal scroll
        await assertZeroHorizontalScroll(page);

        // Stage container centered at 60% (~614px)
        const stage = page.locator('.stage-center-container');
        const stageBox = await stage.boundingBox();
        expect(stageBox).not.toBeNull();
        const expectedStageWidth = 1024 * 0.6; // 614.4px
        expect(Math.abs(stageBox!.width - expectedStageWidth)).toBeLessThan(2);

        // Vignette is visible (display: block)
        const vignette = page.locator('.side-gradient-vignette');
        const vignetteDisplay = await vignette.evaluate(el => window.getComputedStyle(el).display);
        expect(vignetteDisplay).toBe('block');

        // Desktop nav links visible, hamburger hidden
        const navLinks = page.locator('.nav-links');
        const hamburger = page.locator('.hamburger-btn');
        await expect(navLinks).toBeVisible();
        await expect(hamburger).not.toBeVisible();
      }
    });
  });

  test.describe('2. Orientation Transition & Dynamic Resizing Stress', () => {

    test('Dynamic resize 768x1024 (Portrait) -> 1024x768 (Landscape) -> 768x1024 (Portrait)', async ({ page }) => {
      await page.setViewportSize({ width: 768, height: 1024 });
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      // Verify Portrait at initial load
      await assertZeroHorizontalScroll(page);
      let vignette = page.locator('.side-gradient-vignette');
      expect(await vignette.evaluate(el => window.getComputedStyle(el).display)).toBe('none');
      await expect(page.locator('.hamburger-btn')).toBeVisible();

      // Transition to Tablet Landscape (1024x768)
      await page.setViewportSize({ width: 1024, height: 768 });
      await page.waitForTimeout(200);

      await assertZeroHorizontalScroll(page);
      expect(await vignette.evaluate(el => window.getComputedStyle(el).display)).toBe('block');
      await expect(page.locator('.nav-links')).toBeVisible();
      await expect(page.locator('.hamburger-btn')).not.toBeVisible();

      // Transition back to Tablet Portrait (768x1024)
      await page.setViewportSize({ width: 768, height: 1024 });
      await page.waitForTimeout(200);

      await assertZeroHorizontalScroll(page);
      expect(await vignette.evaluate(el => window.getComputedStyle(el).display)).toBe('none');
      await expect(page.locator('.hamburger-btn')).toBeVisible();
      await expect(page.locator('.nav-links')).not.toBeVisible();
    });

    test('Boundary step resize 767px -> 768px -> 1023px -> 1024px', async ({ page }) => {
      await page.goto('/subscription');
      await page.waitForLoadState('networkidle');

      // Step 1: 767px
      await page.setViewportSize({ width: 767, height: 900 });
      await assertZeroHorizontalScroll(page);
      const grid767 = page.locator('.cards-grid, .cards-grid-3').first();
      let cols = (await grid767.evaluate(el => window.getComputedStyle(el).gridTemplateColumns)).split(' ').length;
      expect(cols).toBe(1);

      // Step 2: 768px (switches to 2 cols)
      await page.setViewportSize({ width: 768, height: 900 });
      await assertZeroHorizontalScroll(page);
      cols = (await grid767.evaluate(el => window.getComputedStyle(el).gridTemplateColumns)).split(' ').length;
      expect(cols).toBe(2);

      // Step 3: 1023px (remains 2 cols, tablet portrait)
      await page.setViewportSize({ width: 1023, height: 900 });
      await assertZeroHorizontalScroll(page);
      cols = (await grid767.evaluate(el => window.getComputedStyle(el).gridTemplateColumns)).split(' ').length;
      expect(cols).toBe(2);
      expect(await page.locator('.side-gradient-vignette').evaluate(el => window.getComputedStyle(el).display)).toBe('none');

      // Step 4: 1024px (switches to desktop PC mode)
      await page.setViewportSize({ width: 1024, height: 900 });
      await assertZeroHorizontalScroll(page);
      expect(await page.locator('.side-gradient-vignette').evaluate(el => window.getComputedStyle(el).display)).toBe('block');
      await expect(page.locator('.nav-links')).toBeVisible();
    });
  });

  test.describe('3. Element Overflow & Clipping Checks', () => {
    const boundaryWidths = [767, 768, 1023, 1024];

    for (const width of boundaryWidths) {
      test(`No horizontal overflow or clipping on any route at width ${width}px`, async ({ page }) => {
        await page.setViewportSize({ width, height: 900 });

        for (const route of routes) {
          await page.goto(route);
          await page.waitForLoadState('networkidle');

          const bodyScrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
          const clientWidth = await page.evaluate(() => document.documentElement.clientWidth);

          expect(bodyScrollWidth).toBeLessThanOrEqual(clientWidth + 1);

          // Check primary content wrapper for clipping
          const stage = page.locator('.stage-center-container');
          const stageScrollWidth = await stage.evaluate(el => el.scrollWidth);
          const stageClientWidth = await stage.evaluate(el => el.clientWidth);

          expect(stageScrollWidth).toBeLessThanOrEqual(stageClientWidth + 1);
        }
      });
    }
  });
});
