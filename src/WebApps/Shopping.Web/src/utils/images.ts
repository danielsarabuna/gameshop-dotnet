/**
 * Helper utility to return a valid local fallback image for diamond packs.
 * Guaranteed to point to an existing asset in /images/ diamonds_*.png.
 */
export function getDiamondImageFallback(amount: number): string {
  if (amount >= 25000) return '/images/diamonds_25000.png';
  if (amount >= 15000) return '/images/diamonds_15000.png';
  if (amount >= 9000) return '/images/diamonds_9000.png';
  if (amount >= 2500) return '/images/diamonds_4500.png';
  if (amount >= 1200) return '/images/diamonds_2000.png';
  if (amount >= 600) return '/images/diamonds_800.png';
  if (amount >= 300) return '/images/diamonds_350.png';
  if (amount >= 150) return '/images/diamonds_120.png';
  return '/images/diamonds_60.png';
}
