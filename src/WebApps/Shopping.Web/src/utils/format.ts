/**
 * Format a monetary amount using the locale-aware Intl formatter.
 * Falls back gracefully for unknown currency codes (shows the raw code).
 *
 * Catalog items carry their own ISO 4217 currency (e.g. "RUB", "EUR");
 * the UI must honor it rather than hardcoding a symbol.
 */
export const formatPrice = (amount: number, currency: string | undefined | null): string => {
  const code = (currency || '').toUpperCase().trim();

  if (!code) {
    return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  try {
    return new Intl.NumberFormat(undefined, {
      style: 'currency',
      currency: code,
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(amount);
  } catch {
    // Invalid/unknown ISO code — Intl throws. Show amount + raw code.
    return `${amount.toFixed(2)} ${code}`;
  }
};
