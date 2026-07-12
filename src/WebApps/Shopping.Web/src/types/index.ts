export type LanguageCode = 'RU' | 'EN' | 'DE' | 'FR' | 'ES';

export interface CartItem {
  sku: string;
  title: string;
  unitPrice: number;
  quantity: number;
  imageUrl?: string;
  currency?: string;
}

export interface PaymentMethodInfo {
  code: string;
  name: string;
  iconUrl?: string;
}

export interface CatalogItem {
  id: string;
  title: string;
  description: string;
  type: string; // 'Currency' | 'Subscription'
  price: number;
  currency: string;
  isActive: boolean;
  metadata?: Record<string, string>;
  imageUrl?: string;
}

export interface CreateOrderLinePayload {
  productId: string;
  quantity: number;
}

export interface CreateOrderPayload {
  gameUserId: string;
  paymentMethod: string;
  items: CreateOrderLinePayload[];
  promoCode?: string | null;
}

export interface CreateOrderResult {
  orderId: string;
  status: string;
  subtotal: number;
  discountAmount: number;
  total: number;
  currency: string;
}

export interface PaymentResult {
  paymentId: string;
  provider: string;
  status: string;
  checkoutUrl: string;
}

export interface ApplyPromoLine {
  productId: string;
  quantity: number;
}

export interface ApplyPromoRequest {
  code: string;
  items: ApplyPromoLine[];
}

export interface ApplyPromoResponse {
  isValid: boolean;
  discountAmount: number;
  error?: string;
}
