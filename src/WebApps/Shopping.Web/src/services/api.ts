import axios from 'axios';
import {
  PaymentMethodInfo,
  CatalogPaymentProvider,
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
  // Order creation fans out to Supabase identity + catalog gRPC; payment creation
  // calls the provider API. 5s was too tight on cold starts.
  timeout: 15000,
});

let accessToken = '';

export const setAccessToken = (token: string) => {
  accessToken = token;
};

export const getCatalogPaymentProviders = async (
  region: string,
  store: string,
  gameVersion: string
): Promise<CatalogPaymentProvider[] | null> => {
  try {
    const response = await api.get<{ providers: CatalogPaymentProvider[] }>(
      'api/v1/catalog/payment-providers',
      { params: { region, store, gameVersion } }
    );
    return Array.isArray(response.data?.providers) ? response.data.providers : null;
  } catch {
    return null;
  }
};

api.interceptors.request.use((config) => {
  if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`;
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) window.dispatchEvent(new Event('webshop-auth-expired'));
    return Promise.reject(error);
  }
);

export const getPaymentMethods = async (): Promise<PaymentMethodInfo[] | null> => {
  try {
    const response = await api.get<PaymentMethodInfo[]>('api/v1/payment-methods');
    return Array.isArray(response.data) ? response.data : null;
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
    return Array.isArray(response.data) ? response.data : null;
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
  deliveryContractVersion?: number;
  playerName?: string;
  errorMessage?: string;
  errorCode?: string;
  accessToken?: string;
  expiresAtUtc?: string;
  sessionKind?: 'game' | 'recipient';
}

export const claimTicket = async (ticket: string): Promise<PlayerContext | null> => {
  try {
    const response = await api.post<PlayerContext>('api/v1/auth/claim-ticket', { ticket });
    return response.data;
  } catch (error: any) {
    return error.response?.data ?? null;
  }
};

export const resolvePlayer = async (playerId: string): Promise<PlayerContext | null> => {
  try {
    const response = await api.post<PlayerContext>('api/v1/auth/resolve-player', { playerId });
    return response.data;
  } catch (error: any) {
    return error.response?.data ?? null;
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
  payload: CreateOrderPayload,
  signal?: AbortSignal
): Promise<{ success: boolean; data?: CreateOrderResult; error?: string }> => {
  try {
    const response = await api.post<CreateOrderResult>('api/v1/orders/create', payload, { signal });
    return { success: true, data: response.data };
  } catch (error: any) {
    const message = error.response?.data?.error || 'Could not create order.';
    return { success: false, error: message };
  }
};

export const createPayment = async (
  orderId: string,
  provider: string,
  signal?: AbortSignal
): Promise<{ success: boolean; data?: PaymentResult; error?: string }> => {
  try {
    const response = await api.post<PaymentResult>(`api/v1/payments/${provider}`, { orderId }, { signal });
    return { success: true, data: response.data };
  } catch (error: any) {
    const message = error.response?.data?.error || 'Could not create payment.';
    return { success: false, error: message };
  }
};

export const completeMockPayment = async (
  orderId: string,
  status: 'succeeded' | 'failed',
  signal?: AbortSignal
): Promise<boolean> => {
  try {
    await api.post(`api/v1/payments/mockprovider/${orderId}/complete`, { status }, { signal });
    return true;
  } catch {
    return false;
  }
};

// Lightweight status probe for the post-payment landing page.
export const getOrderStatus = async (
  orderId: string
): Promise<'Pending' | 'Paid' | 'Failed' | null> => {
  try {
    const response = await api.get<{ status: string }>(`api/v1/orders/${orderId}`);
    const raw = String(response.data?.status ?? '');
    if (raw.toLowerCase() === 'paid') return 'Paid';
    if (raw.toLowerCase() === 'failed') return 'Failed';
    return 'Pending';
  } catch {
    return null;
  }
};
