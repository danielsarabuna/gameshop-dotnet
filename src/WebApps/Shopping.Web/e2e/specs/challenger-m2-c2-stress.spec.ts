import { test, expect } from '@playwright/test';

const MOBILE_VIEWPORTS = [
  { name: '320px', width: 320, height: 568 },
  { name: '375px', width: 375, height: 667 },
  { name: '390x844', width: 390, height: 844 },
  { name: '480px', width: 480, height: 800 },
];

const TARGET_ROUTES = ['/subscription', '/help', '/about'];

test.describe('Challenger 2 — M2 Page Cards & Overlay Stress Tests', () => {

  for (const vp of MOBILE_VIEWPORTS) {
    test.describe(`Viewport: ${vp.name}`, () => {
      test.use({ viewport: { width: vp.width, height: vp.height } });

      for (const route of TARGET_ROUTES) {
        test(`Zero horizontal scroll on ${route} at ${vp.name}`, async ({ page }) => {
          await page.goto(route);
          await page.waitForLoadState('networkidle');

          const scrollX = await page.evaluate(() => window.scrollX);
          const scrollWidth = await page.evaluate(() => document.documentElement.scrollWidth);
          const clientWidth = await page.evaluate(() => document.documentElement.clientWidth);

          expect(scrollX).toBe(0);
          expect(scrollWidth).toBeLessThanOrEqual(clientWidth);
        });

        test(`Fluid title typography on ${route} at ${vp.name}`, async ({ page }) => {
          await page.goto(route);
          await page.waitForLoadState('networkidle');

          const titleFontSize = await page.evaluate(() => {
            const el = document.querySelector('.page-title');
            if (!el) return null;
            return window.getComputedStyle(el).fontSize;
          });

          expect(titleFontSize).not.toBeNull();
          const fontSizePx = parseFloat(titleFontSize!);
          // On mobile small (<=360px), title should be ~24px (1.5rem), on <767px ~28.8px (1.8rem)
          expect(fontSizePx).toBeGreaterThanOrEqual(20);
          expect(fontSizePx).toBeLessThanOrEqual(48);
        });

        test(`Single-column card grid stacking on ${route} at ${vp.name}`, async ({ page }) => {
          await page.goto(route);
          await page.waitForLoadState('networkidle');

          const isSingleColumn = await page.evaluate(() => {
            const grids = Array.from(document.querySelectorAll('.cards-grid, .cards-grid-3'));
            if (grids.length === 0) return true;
            return grids.every(grid => {
              const columns = window.getComputedStyle(grid).gridTemplateColumns.split(' ');
              return columns.length === 1;
            });
          });

          expect(isSingleColumn).toBe(true);
        });
      }
    });
  }

  test.describe('Subscription Plan Badge Alignment & Text Collision (320px)', () => {
    test.use({ viewport: { width: 320, height: 568 } });

    test('badge tag for 12 Месяцев does not collide with duration title', async ({ page }) => {
      await page.route('**/api/v1/catalog/items*', (route) => route.abort('failed'));
      await page.goto('/subscription');
      await page.waitForLoadState('networkidle');

      const planCard = page
        .locator('.sub-snap-container #sub-plans-section .glass-card')
        .filter({ hasText: 'МАКСИМАЛЬНАЯ ВЫГОДА' });
      await expect(planCard).toBeVisible();

      const badge = planCard.locator('.badge-tag');
      await expect(badge).toBeVisible();
      await expect(badge).toHaveText('МАКСИМАЛЬНАЯ ВЫГОДА');

      const durationTitle = planCard.locator('div', { hasText: /^12 Месяц/ }).first();
      await expect(durationTitle).toBeVisible();

      const badgeBox = await badge.boundingBox();
      const durationBox = await durationTitle.boundingBox();

      expect(badgeBox).not.toBeNull();
      expect(durationBox).not.toBeNull();

      if (badgeBox && durationBox) {
        // Badge must be positioned visually above duration title with no vertical overlap
        expect(badgeBox.y + badgeBox.height).toBeLessThanOrEqual(durationBox.y + 2); // 2px margin tolerance
        // Ensure horizontal bounds are strictly within the viewport width (320px)
        expect(badgeBox.x).toBeGreaterThanOrEqual(0);
        expect(badgeBox.x + badgeBox.width).toBeLessThanOrEqual(320);
        expect(durationBox.x).toBeGreaterThanOrEqual(0);
        expect(durationBox.x + durationBox.width).toBeLessThanOrEqual(320);
      }
    });
  });
});
