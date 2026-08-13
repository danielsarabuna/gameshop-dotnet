import { Page, Locator, expect } from '@playwright/test';

/**
 * Asserts that the page has zero horizontal scroll offset
 * and document and body scrollWidth do not exceed clientWidth.
 */
export async function assertZeroHorizontalScroll(page: Page): Promise<void> {
  const scrollInfo = await page.evaluate(() => ({
    scrollX: window.scrollX,
    docScrollWidth: document.documentElement.scrollWidth,
    bodyScrollWidth: document.body.scrollWidth,
    clientWidth: document.documentElement.clientWidth,
  }));

  expect(scrollInfo.scrollX).toBe(0);
  expect(scrollInfo.docScrollWidth).toBeLessThanOrEqual(scrollInfo.clientWidth);
  expect(scrollInfo.bodyScrollWidth).toBeLessThanOrEqual(scrollInfo.clientWidth);
}

export interface BoundingBox {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * Checks if two bounding boxes overlap horizontally and vertically.
 */
export function isOverlapping(rect1: BoundingBox, rect2: BoundingBox, epsilon: number = 1): boolean {
  return !(
    rect1.x + rect1.width - epsilon <= rect2.x ||
    rect2.x + rect2.width - epsilon <= rect1.x ||
    rect1.y + rect1.height - epsilon <= rect2.y ||
    rect2.y + rect2.height - epsilon <= rect1.y
  );
}

/**
 * Asserts that none of the provided locators overlap each other.
 * Requires at least 2 locators, and every locator must be visible with non-null bounding box.
 */
export async function assertNoElementOverlap(locators: Locator[]): Promise<void> {
  expect(locators.length, 'assertNoElementOverlap requires at least 2 locators').toBeGreaterThan(1);
  const boxes: BoundingBox[] = [];

  for (const loc of locators) {
    await expect(loc, `Locator ${loc} must be visible for overlap assertion`).toBeVisible();
    const box = await loc.boundingBox();
    expect(box, `Bounding box for locator ${loc} must not be null`).not.toBeNull();
    expect(box!.width, `Element width for locator ${loc} must be > 0`).toBeGreaterThan(0);
    expect(box!.height, `Element height for locator ${loc} must be > 0`).toBeGreaterThan(0);
    boxes.push(box!);
  }

  expect(boxes.length).toBe(locators.length);

  for (let i = 0; i < boxes.length; i++) {
    for (let j = i + 1; j < boxes.length; j++) {
      const overlap = isOverlapping(boxes[i], boxes[j]);
      expect(overlap, `Element at index ${i} overlaps with element at index ${j}`).toBe(false);
    }
  }
}

/**
 * Asserts that the element does not suffer from unexpected text/content clipping (scrollWidth <= clientWidth).
 */
export async function assertNoTextClipping(locator: Locator): Promise<void> {
  await expect(locator).toBeVisible();
  const isClipped = await locator.evaluate((el) => el.scrollWidth > el.clientWidth);
  expect(isClipped, `Text or content in locator ${locator} is clipped (scrollWidth > clientWidth)`).toBe(false);
}
