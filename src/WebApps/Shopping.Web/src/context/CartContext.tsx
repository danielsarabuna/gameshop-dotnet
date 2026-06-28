import React, { createContext, useContext, useState, useEffect } from 'react';
import { CartItem } from '../types';

interface CartContextType {
  lines: CartItem[];
  count: number;
  total: number;
  cartBump: boolean;
  cartDrawerOpen: boolean;
  openCartDrawer: () => void;
  closeCartDrawer: () => void;
  toggleCartDrawer: () => void;
  addItem: (sku: string, title: string, unitPrice: number, quantity?: number, imageUrl?: string, currency?: string) => void;
  increment: (sku: string) => void;
  decrement: (sku: string) => void;
  removeItem: (sku: string) => void;
  clearCart: () => void;
}

const CartContext = createContext<CartContextType | undefined>(undefined);
const CART_STORAGE_KEY = 'GameShop_cart';

export const CartProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [lines, setLines] = useState<CartItem[]>(() => {
    try {
      const saved = localStorage.getItem(CART_STORAGE_KEY);
      return saved ? JSON.parse(saved) : [];
    } catch {
      return [];
    }
  });

  const [cartBump, setCartBump] = useState(false);
  const [cartDrawerOpen, setCartDrawerOpen] = useState(false);

  useEffect(() => {
    try {
      localStorage.setItem(CART_STORAGE_KEY, JSON.stringify(lines));
    } catch (e) {
      console.error('Failed to save cart to localStorage', e);
    }
  }, [lines]);

  const triggerBump = () => {
    setCartBump(true);
    setTimeout(() => setCartBump(false), 300);
  };

  const count = lines.reduce((acc, line) => acc + line.quantity, 0);
  const total = lines.reduce((acc, line) => acc + line.unitPrice * line.quantity, 0);

  const openCartDrawer = () => setCartDrawerOpen(true);
  const closeCartDrawer = () => setCartDrawerOpen(false);
  const toggleCartDrawer = () => setCartDrawerOpen((prev) => !prev);

  const addItem = (
    sku: string,
    title: string,
    unitPrice: number,
    quantity = 1,
    imageUrl?: string,
    currency?: string
  ) => {
    if (quantity <= 0 || !sku) return;
    setLines((prev) => {
      const existing = prev.find((item) => item.sku === sku);
      if (existing) {
        return prev.map((item) =>
          item.sku === sku ? { ...item, quantity: item.quantity + quantity } : item
        );
      }
      return [...prev, { sku, title, unitPrice, quantity, imageUrl, currency }];
    });
    triggerBump();
  };

  const increment = (sku: string) => {
    setLines((prev) =>
      prev.map((item) => (item.sku === sku ? { ...item, quantity: item.quantity + 1 } : item))
    );
    triggerBump();
  };

  const decrement = (sku: string) => {
    setLines((prev) =>
      prev
        .map((item) => (item.sku === sku ? { ...item, quantity: item.quantity - 1 } : item))
        .filter((item) => item.quantity > 0)
    );
    triggerBump();
  };

  const removeItem = (sku: string) => {
    setLines((prev) => prev.filter((item) => item.sku !== sku));
    triggerBump();
  };

  const clearCart = () => {
    setLines([]);
    triggerBump();
  };

  return (
    <CartContext.Provider
      value={{
        lines,
        count,
        total,
        cartBump,
        cartDrawerOpen,
        openCartDrawer,
        closeCartDrawer,
        toggleCartDrawer,
        addItem,
        increment,
        decrement,
        removeItem,
        clearCart,
      }}
    >
      {children}
    </CartContext.Provider>
  );
};

export const useCart = (): CartContextType => {
  const context = useContext(CartContext);
  if (!context) {
    throw new Error('useCart must be used within a CartProvider');
  }
  return context;
};
