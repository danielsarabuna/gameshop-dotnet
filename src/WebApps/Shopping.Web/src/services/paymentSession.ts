import { CartItem } from '../types';

export type PaymentOutcome = 'complete' | 'cancelled' | 'failed';

export interface PendingPayment {
  orderId: string;
  provider: string;
  returnPath: string;
  cartFingerprint: string;
  createdAtUtc: string;
}

export interface StoredPaymentResult {
  orderId: string;
  outcome: PaymentOutcome;
}

export const PENDING_ORDER_KEY = 'GameShop_pending_order';
export const PAYMENT_RESULT_KEY = 'GameShop_payment_result';

export const cartFingerprint = (lines: CartItem[]) => JSON.stringify(
  lines.map(({ sku, quantity }) => [sku, quantity]).sort(([left], [right]) => String(left).localeCompare(String(right)))
);

export const safeReturnPath = (value?: string) => {
  if (!value?.startsWith('/') || value.startsWith('//') || value.startsWith('/order/')) return '/diamonds';
  return value;
};

const readJson = <T>(key: string): T | null => {
  try {
    return JSON.parse(sessionStorage.getItem(key) || 'null') as T | null;
  } catch {
    return null;
  }
};

export const readPendingPayment = () => readJson<PendingPayment>(PENDING_ORDER_KEY);
export const savePendingPayment = (payment: PendingPayment) => sessionStorage.setItem(PENDING_ORDER_KEY, JSON.stringify(payment));
export const clearPendingPayment = () => sessionStorage.removeItem(PENDING_ORDER_KEY);

export const readPaymentResult = () => readJson<StoredPaymentResult>(PAYMENT_RESULT_KEY);
export const savePaymentResult = (result: StoredPaymentResult) => sessionStorage.setItem(PAYMENT_RESULT_KEY, JSON.stringify(result));
export const clearPaymentResult = () => sessionStorage.removeItem(PAYMENT_RESULT_KEY);
