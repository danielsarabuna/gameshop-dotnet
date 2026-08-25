import axios from 'axios';
import {
  PaymentMethodInfo,
  CatalogItem,
  CreateOrderPayload,
  CreateOrderResult,
  PaymentResult,
  ApplyPromoRequest,
  ApplyPromoResponse,
} from '../types';

const api = axios.create({
  baseURL: '/',
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 5000,
});

export const getPaymentMethods = async (): Promise<PaymentMethodInfo[] | null> => {
  try {
    const response = await api.get<PaymentMethodInfo[]>('api/v1/payment-methods');
    return response.data;
  } catch {
    return null;
  }
};

export const getCatalogItems = async (
  region?: string,
  store?: string,
  gameVersion?: string
): Promise<CatalogItem[] | null> => {
  try {
    const params = new URLSearchParams();
    if (region) params.set('region', region);
    if (store) params.set('store', store);
    if (gameVersion) params.set('gameVersion', gameVersion);
    const qs = params.toString();
    const url = qs ? `api/v1/catalog/items?${qs}` : 'api/v1/catalog/items';
    const response = await api.get<CatalogItem[]>(url);
    return response.data;
  } catch {
    return null;
  }
};

// Player-context resolution via backend (authoritative region/store/version from Supabase)
export interface PlayerContext {
  isValid: boolean;
  userId: string;
  region: string;
  store: string;
  gameVersion: string;
  errorMessage?: string;
}

export const claimTicket = async (ticket: string): Promise<PlayerContext | null> => {
  try {
    const response = await api.post<PlayerContext>('api/v1/auth/claim-ticket', null, {
      params: { ticket },
    });
    return response.data;
  } catch {
    return null;
  }
};

export const verifyPlayer = async (userId: string): Promise<PlayerContext | null> => {
  try {
    const response = await api.post<PlayerContext>('api/v1/auth/verify-player', null, {
      params: { userId },
    });
    return response.data;
  } catch {
    return null;
  }
};

export const applyPromoCode = async (
  request: ApplyPromoRequest
): Promise<ApplyPromoResponse | null> => {
  try {
    // Backend contract: POST /api/v1/promocodes/apply with { code, items }.
    const response = await api.post<ApplyPromoResponse>('api/v1/promocodes/apply', request);
    return response.data;
  } catch {
    return null;
  }
};

export const createOrder = async (
  payload: CreateOrderPayload
): Promise<{ success: boolean; data?: CreateOrderResult; error?: string }> => {
  try {
    const response = await api.post<CreateOrderResult>('api/v1/orders/create', payload);
    return { success: true, data: response.data };
  } catch (error: any) {
    const message = error.response?.data?.error || 'Could not create order.';
    return { success: false, error: message };
  }
};

export const createPayment = async (
  orderId: string,
  provider: string
): Promise<{ success: boolean; data?: PaymentResult; error?: string }> => {
  try {
    const response = await api.post<PaymentResult>(`api/v1/payments/${provider}`, { orderId });
    return { success: true, data: response.data };
  } catch (error: any) {
    const message = error.response?.data?.error || 'Could not create payment.';
    return { success: false, error: message };
  }
};
