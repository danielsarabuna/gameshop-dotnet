import { test, expect } from '@playwright/test';
import { HeaderPOM } from '../page-objects/header.page';

test.describe('Desktop & Tablet Landscape Mode (1024x768 & 1440x900)', () => {
  test.describe('Tablet Landscape (1024x768)', () => {
    test.use({ viewport: { width: 1024, height: 768 } });

    test('should render side gradient vignettes and centered stage container', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const vignette = page.locator('.side-gradient-vignette');
      await expect(vignette).toBeVisible();

      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      const viewportWidth = await page.evaluate(() => window.innerWidth);
      expect(box).not.toBeNull();
      const expectedWidth = viewportWidth * 0.6;
      expect(Math.abs(box!.width - expectedWidth)).toBeLessThanOrEqual(5);
    });

    test('should display desktop nav links and hide hamburger button', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);
      await expect(header.navLinks).toBeVisible();
      await expect(header.hamburgerBtn).not.toBeVisible();
    });

    test('should not overflow cards or clip text on tablet landscape across routes', async ({ page }) => {
      const routes = ['/', '/diamonds', '/subscription', '/projects', '/about'];

      for (const route of routes) {
        await page.goto(route);
        await page.waitForLoadState('networkidle');

        // Verify side vignette visible and 60% stage width on every page
        const vignette = page.locator('.side-gradient-vignette');
        await expect(vignette).toBeVisible();

        const stage = page.locator('.stage-center-container');
        const box = await stage.boundingBox();
        expect(box).not.toBeNull();
        expect(Math.abs(box!.width - 614.4)).toBeLessThanOrEqual(5);

        // Verify no clipped text in card titles and content
        const overflowingElements = await page.evaluate(() => {
          const details: string[] = [];
          const cards = document.querySelectorAll('.cards-grid > *, .cards-grid-3 > *, .card, .project-card');
          cards.forEach((card) => {
            if (card.scrollWidth > card.clientWidth + 1) {
              details.push(`${card.tagName}.${card.className} (scrollWidth=${card.scrollWidth}, clientWidth=${card.clientWidth}, text="${card.textContent?.trim().slice(0, 40)}")`);
            }
          });
          return details;
        });

        if (overflowingElements.length > 0) {
          console.error(`[Tablet Landscape 1024x768] Overflow on route ${route}:`, overflowingElements);
        }
        expect(overflowingElements).toEqual([]);
      }
    });
  });

  test.describe('Desktop (1440x900)', () => {
    test.use({ viewport: { width: 1440, height: 900 } });

    test('should limit stage container width and keep centered', async ({ page }) => {
      await page.goto('/');
      await page.waitForLoadState('networkidle');

      const stage = page.locator('.stage-center-container');
      await expect(stage).toBeVisible();

      const box = await stage.boundingBox();
      const viewportWidth = await page.evaluate(() => window.innerWidth);
      expect(box).not.toBeNull();

      const expectedWidth = Math.min(1180, viewportWidth * 0.6);
      expect(Math.abs(box!.width - expectedWidth)).toBeLessThanOrEqual(5);

      const expectedMargin = (viewportWidth - box!.width) / 2;
      expect(Math.abs(box!.x - expectedMargin)).toBeLessThanOrEqual(5);
    });

    test('should display desktop header navigation', async ({ page }) => {
      await page.goto('/diamonds');
      await page.waitForLoadState('networkidle');

      const header = new HeaderPOM(page);
      await expect(header.navLinks).toBeVisible();
      await expect(header.hamburgerBtn).not.toBeVisible();
    });
  });
});
