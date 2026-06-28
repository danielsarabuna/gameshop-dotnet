import React, { useState, useEffect } from 'react';
import { useCart } from '../context/CartContext';
import { useLanguage } from '../context/LanguageContext';
import { useAuth } from '../context/AuthContext';
import { PaymentMethodInfo } from '../types';
import { getPaymentMethods, applyPromoCode, createOrder, createPayment } from '../services/api';
import { ShoppingBagIcon } from './Icons';
import { ShoppingCart, X, Minus, Plus, Tag, User, CreditCard } from 'lucide-react';
import { formatPrice } from '../utils/format';

export const CartDrawer: React.FC = () => {
  const {
    lines,
    total,
    cartDrawerOpen,
    closeCartDrawer,
    increment,
    decrement,
    removeItem,
    clearCart,
  } = useCart();

  const { t } = useLanguage();
  const { playerId, setPlayerId, playerName, setPlayerName, playerEmail, setPlayerEmail } = useAuth();

  const [promoCode, setPromoCode] = useState('');
  const [appliedPromoCode, setAppliedPromoCode] = useState<string | null>(null);
  const [promoDiscountAmount, setPromoDiscountAmount] = useState(0);
  const [promoMessage, setPromoMessage] = useState<string | null>(null);
  const [promoBusy, setPromoBusy] = useState(false);

  const [paymentMethods, setPaymentMethods] = useState<PaymentMethodInfo[]>([]);
  const [selectedPaymentMethod, setSelectedPaymentMethod] = useState('');
  const [paymentMethodsFailed, setPaymentMethodsFailed] = useState(false);

  const [checkoutMessage, setCheckoutMessage] = useState<string | null>(null);
  const [checkoutBusy, setCheckoutBusy] = useState(false);

  const FALLBACK_PAYMENT_METHODS: PaymentMethodInfo[] = [
    { code: 'card', name: 'Банковская карта (Visa / MasterCard / МИР)' },
    { code: 'sbp', name: 'Система Быстрых Платежей (СБП)' },
    { code: 'telegram', name: 'Telegram Stars / Wallet' },
  ];

  useEffect(() => {
    let mounted = true;
    getPaymentMethods().then((list) => {
      if (!mounted) return;
      if (list && list.length > 0) {
        setPaymentMethods(list);
        setPaymentMethodsFailed(false);
      } else {
        setPaymentMethods(FALLBACK_PAYMENT_METHODS);
        setPaymentMethodsFailed(true);
      }
    });
    return () => {
      mounted = false;
    };
  }, []);

  useEffect(() => {
    if (!cartDrawerOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeCartDrawer();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [cartDrawerOpen, closeCartDrawer]);

  const isValidEmail = (val: string) => {
    if (!val) return true;
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(val);
  };

  const canCheckout =
    lines.length > 0 &&
    Boolean(playerId.trim()) &&
    Boolean(playerName.trim()) &&
    isValidEmail(playerEmail.trim()) &&
    Boolean(selectedPaymentMethod);

  const totalWithPromo = Math.max(0, total - promoDiscountAmount);

  const handleApplyPromo = async () => {
    if (!promoCode.trim()) {
      setPromoMessage(t('Введите промокод.', 'Enter a promo code.', 'Promocode eingeben.', 'Saisissez un code promo.', 'Introduce un código promo.'));
      return;
    }

    setPromoBusy(true);
    setPromoMessage(null);

    const apiLines = lines.map((l) => ({
      productId: l.sku,
      quantity: l.quantity,
    }));

    const res = await applyPromoCode({
      promoCode: promoCode.trim(),
      lines: apiLines,
    });

    setPromoBusy(false);

    if (res && res.isValid) {
      setAppliedPromoCode(promoCode.trim());
      setPromoDiscountAmount(res.discountAmount || 0);
      setPromoMessage(t('Промокод применён.', 'Promo code applied.', 'Code angewendet.', 'Code appliqué.', 'Código aplicado.'));
    } else {
      setAppliedPromoCode(null);
      setPromoDiscountAmount(0);
      setPromoMessage(
        res?.error ||
          t(
            'Не удалось применить промокод.',
            'Could not apply promo code.',
            'Promocode konnte nicht angewendet werden.',
            'Impossible d’appliquer le code promo.',
            'No se pudo aplicar el código.'
          )
      );
    }
  };

  const handleCheckout = async () => {
    if (!canCheckout) return;

    setCheckoutBusy(true);
    setCheckoutMessage(null);

    const apiLines = lines.map((l) => ({
      productId: l.sku,
      quantity: l.quantity,
    }));

    const orderPayload = {
      gameUserId: playerId.trim(),
      paymentMethod: selectedPaymentMethod,
      items: apiLines,
      promoCode: appliedPromoCode || undefined,
    };

    const orderRes = await createOrder(orderPayload);
    if (!orderRes.success || !orderRes.data) {
      setCheckoutMessage(
        orderRes.error ||
          t('Не удалось создать заказ.', 'Could not create the order.', 'Die Bestellung konnte nicht erstellt werden.', 'Impossible de créer la commande.', 'No se pudo crear el pedido.')
      );
      setCheckoutBusy(false);
      return;
    }

    const paymentRes = await createPayment(orderRes.data.orderId, selectedPaymentMethod);
    setCheckoutBusy(false);

    if (!paymentRes.success || !paymentRes.data?.checkoutUrl) {
      setCheckoutMessage(
        paymentRes.error ||
          t(
            'Заказ создан, но платёж не удалось подготовить.',
            'The order was created, but payment could not be prepared.',
            'Die Bestellung wurde erstellt, aber die Zahlung konnte nicht vorbereitet werden.',
            'La commande a été créée, mais le paiement n’a pas pu être préparé.',
            'El pedido se creó, pero no se pudo preparar el pago.'
          )
      );
      return;
    }

    window.location.href = paymentRes.data.checkoutUrl;
  };

  // Catalog is single-currency per region; use the first line's currency for drawer totals.
  const cartCurrency = lines.find((l) => l.currency)?.currency;

  return (
    <>
      <div
        className={`cart-overlay ${cartDrawerOpen ? 'open' : ''}`}
        onClick={closeCartDrawer}
      />

      <aside className={`cart-drawer ${cartDrawerOpen ? 'open' : ''}`} aria-label="Cart drawer">
        {/* DRAWER HEADER */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            paddingBottom: '16px',
            borderBottom: '1px solid var(--border-color)',
            marginBottom: '16px',
          }}
        >
          <div style={{ fontSize: '1.25rem', fontWeight: 800, color: '#ffffff', display: 'flex', alignItems: 'center', gap: '10px' }}>
            <ShoppingCart size={22} color="var(--accent-pink)" />
            {t('Корзина', 'Cart', 'Warenkorb', 'Panier', 'Carrito')}
          </div>
          <button
            type="button"
            className="cart-close-btn"
            aria-label="Close cart"
            onClick={closeCartDrawer}
          >
            <X size={16} />
          </button>
        </div>

        {/* EMPTY STATE */}
        {lines.length === 0 ? (
          <div className="cart-empty">
            <div className="cart-empty-icon" style={{ color: 'var(--accent-pink)' }}>
              <ShoppingBagIcon size={36} />
            </div>
            <div className="cart-empty-title">
              {t('Корзина пуста', 'Cart is empty', 'Warenkorb leer', 'Panier vide', 'Carrito vacío')}
            </div>
            <div className="cart-empty-sub" style={{ fontSize: '0.88rem', color: 'var(--text-muted)', marginBottom: '20px' }}>
              {t(
                'Добавьте товары для оформления заказа',
                'Add items to place an order',
                'Füge Artikel hinzu, um zu bestellen',
                'Ajoutez des articles pour commander',
                'Añade productos para realizar el pedido'
              )}
            </div>
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflowY: 'auto' }}>
            {/* ITEMS LIST */}
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginBottom: '20px' }}>
              {lines.map((line) => (
                <div
                  key={line.sku}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: '12px',
                    background: 'rgba(255, 255, 255, 0.04)',
                    border: '1px solid var(--border-color)',
                    padding: '12px 14px',
                    borderRadius: '14px',
                  }}
                >
                  <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                    <div
                      style={{
                        width: '42px',
                        height: '42px',
                        borderRadius: '10px',
                        background: 'rgba(255, 51, 102, 0.15)',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        overflow: 'hidden',
                        flexShrink: 0,
                      }}
                    >
                      {line.imageUrl ? (
                        <img
                          src={line.imageUrl}
                          alt={line.title}
                          style={{ width: '80%', height: '80%', objectFit: 'contain' }}
                          onError={(e) => {
                            const target = e.currentTarget;
                            if (!target.src.endsWith('/images/diamonds_60.png')) {
                              target.src = '/images/diamonds_60.png';
                            }
                          }}
                        />
                      ) : (
                        <ShoppingBagIcon size={20} color="var(--accent-pink)" />
                      )}
                    </div>

                    <div style={{ minWidth: 0, flex: 1 }}>
                      <div style={{ fontWeight: 700, fontSize: '0.92rem', color: '#ffffff', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {line.title}
                      </div>
                      <div style={{ fontSize: '0.82rem', color: 'var(--accent-pink)', fontWeight: 700 }}>
                        {formatPrice(line.unitPrice * line.quantity, line.currency || cartCurrency)}
                      </div>
                    </div>
                  </div>

                  <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexShrink: 0 }}>
                    <div
                      style={{
                        display: 'inline-flex',
                        alignItems: 'center',
                        background: 'rgba(0,0,0,0.3)',
                        borderRadius: '16px',
                        padding: '2px 6px',
                        border: '1px solid var(--border-color)',
                      }}
                    >
                      <button
                        type="button"
                        style={{ background: 'none', border: 'none', color: '#fff', cursor: 'pointer', padding: '2px 6px' }}
                        onClick={() => decrement(line.sku)}
                      >
                        <Minus size={12} />
                      </button>
                      <span style={{ padding: '0 6px', fontSize: '0.85rem', fontWeight: 800, color: '#fff' }}>
                        {line.quantity}
                      </span>
                      <button
                        type="button"
                        style={{ background: 'none', border: 'none', color: '#fff', cursor: 'pointer', padding: '2px 6px' }}
                        onClick={() => increment(line.sku)}
                      >
                        <Plus size={12} />
                      </button>
                    </div>

                    <button
                      type="button"
                      style={{ background: 'none', border: 'none', color: 'var(--text-dim)', cursor: 'pointer', padding: '4px' }}
                      onClick={() => removeItem(line.sku)}
                      title="Remove"
                    >
                      <X size={16} />
                    </button>
                  </div>
                </div>
              ))}
            </div>

            {/* PROMO CODE SECTION */}
            <div style={{ marginBottom: '20px', background: 'rgba(0,0,0,0.2)', padding: '14px', borderRadius: '14px', border: '1px solid var(--border-color)' }}>
              <div style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '8px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                <Tag size={14} />
                {t('Промокод', 'Promo code', 'Promocode', 'Code promo', 'Código promo')}
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <input
                  type="text"
                  value={promoCode}
                  onChange={(e) => setPromoCode(e.target.value)}
                  placeholder={t('Введите код', 'Enter code', 'Code eingeben', 'Saisir le code', 'Introduce el código')}
                  style={{
                    flex: 1,
                    background: 'rgba(255,255,255,0.06)',
                    border: '1px solid var(--border-color)',
                    borderRadius: '10px',
                    padding: '8px 12px',
                    color: '#fff',
                    fontSize: '0.88rem',
                  }}
                />
                <button
                  type="button"
                  disabled={promoBusy}
                  onClick={handleApplyPromo}
                  className="btn btn-outline btn-sm"
                  style={{ padding: '8px 14px' }}
                >
                  {t('Применить', 'Apply', 'Anwenden', 'Appliquer', 'Aplicar')}
                </button>
              </div>
              {promoMessage && (
                <div style={{ fontSize: '0.8rem', marginTop: '6px', color: appliedPromoCode ? '#00f2fe' : '#ff4d4d' }}>
                  {promoMessage}
                </div>
              )}
            </div>

            {/* PLAYER FORM */}
            <div style={{ marginBottom: '20px', background: 'rgba(0,0,0,0.2)', padding: '14px', borderRadius: '14px', border: '1px solid var(--border-color)' }}>
              <div style={{ fontSize: '0.82rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '10px', display: 'flex', alignItems: 'center', gap: '6px' }}>
                <User size={14} />
                {t('Данные игрока', 'Player details', 'Spielerdaten', 'Informations du joueur', 'Datos del jugador')}
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                <input
                  type="text"
                  value={playerId}
                  onChange={(e) => setPlayerId(e.target.value)}
                  placeholder={`${t('ID игрока', 'Player ID', 'Spieler-ID', 'ID joueur', 'ID de jugador')} *`}
                  style={{
                    background: 'rgba(255,255,255,0.06)',
                    border: '1px solid var(--border-color)',
                    borderRadius: '10px',
                    padding: '8px 12px',
                    color: '#fff',
                    fontSize: '0.88rem',
                  }}
                />
                <input
                  type="text"
                  value={playerName}
                  onChange={(e) => setPlayerName(e.target.value)}
                  placeholder={`${t('Имя', 'Name', 'Name', 'Nom', 'Nombre')} *`}
                  style={{
                    background: 'rgba(255,255,255,0.06)',
                    border: '1px solid var(--border-color)',
                    borderRadius: '10px',
                    padding: '8px 12px',
                    color: '#fff',
                    fontSize: '0.88rem',
                  }}
                />
                <input
                  type="email"
                  value={playerEmail}
                  onChange={(e) => setPlayerEmail(e.target.value)}
                  placeholder={`Email (${t('необязательно', 'optional', 'optional', 'facultatif', 'opcional')})`}
                  style={{
                    background: 'rgba(255,255,255,0.06)',
                    border: '1px solid var(--border-color)',
                    borderRadius: '10px',
                    padding: '8px 12px',
                    color: '#fff',
                    fontSize: '0.88rem',
                  }}
                />
              </div>

              {/* PAYMENT METHOD SELECTOR */}
              <div style={{ marginTop: '12px' }}>
                <label style={{ display: 'flex', alignItems: 'center', gap: '6px', fontSize: '0.82rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '4px' }}>
                  <CreditCard size={14} />
                  {t('Способ оплаты', 'Payment method', 'Zahlungsmethode', 'Mode de paiement', 'Método de pago')} *
                </label>
                <select
                  value={selectedPaymentMethod}
                  onChange={(e) => setSelectedPaymentMethod(e.target.value)}
                  style={{
                    width: '100%',
                    background: '#1d1823',
                    border: '1px solid var(--border-color)',
                    borderRadius: '10px',
                    padding: '10px 12px',
                    color: '#fff',
                    fontSize: '0.88rem',
                  }}
                >
                  <option value="">
                    {t('Выберите способ оплаты', 'Select payment method', 'Zahlungsmethode wählen', 'Choisir le mode de paiement', 'Elegir método de pago')}
                  </option>
                  {paymentMethods.map((m) => (
                    <option key={m.code} value={m.code}>
                      {m.name}
                    </option>
                  ))}
                </select>
              </div>
            </div>

            {/* TOTAL & CHECKOUT FOOTER */}
            <div style={{ marginTop: 'auto', paddingTop: '16px', borderTop: '1px solid var(--border-color)' }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: '4px' }}>
                <span style={{ fontSize: '0.95rem', color: 'var(--text-muted)', fontWeight: 700 }}>
                  {t('Итого:', 'Total:', 'Summe:', 'Total :', 'Total:')}
                </span>
                <span style={{ fontSize: '1.6rem', fontWeight: 900, color: '#ffffff' }}>
                  {formatPrice(totalWithPromo, cartCurrency)}
                </span>
              </div>

              {promoDiscountAmount > 0 && (
                <div style={{ fontSize: '0.82rem', color: '#00f2fe', textAlign: 'right', marginBottom: '12px' }}>
                  {t('Скидка:', 'Discount:', 'Rabatt:', 'Remise :', 'Descuento:')} -{formatPrice(promoDiscountAmount, cartCurrency)}
                </div>
              )}

              {!canCheckout && (
                <div style={{ fontSize: '0.78rem', color: 'var(--text-dim)', marginBottom: '10px', textAlign: 'center' }}>
                  {t(
                    'Укажите ID игрока, имя и выберите способ оплаты для заказа.',
                    'Enter player ID, name, and select a payment method.'
                  )}
                </div>
              )}

              <button
                type="button"
                className="btn btn-primary"
                style={{ width: '100%', padding: '14px', borderRadius: '14px', fontSize: '1rem' }}
                disabled={!canCheckout || checkoutBusy}
                onClick={handleCheckout}
              >
                {checkoutBusy
                  ? t('Обработка...', 'Processing...', 'Wird bearbeitet...', 'Traitement...', 'Procesando...')
                  : t('Оформить заказ', 'Checkout', 'Bestellen', 'Commander', 'Finalizar')}
              </button>

              {checkoutMessage && (
                <div style={{ fontSize: '0.82rem', color: '#ff4d4d', marginTop: '8px', textAlign: 'center' }}>
                  {checkoutMessage}
                </div>
              )}

              <button
                type="button"
                className="btn btn-ghost btn-sm"
                style={{ width: '100%', marginTop: '10px' }}
                onClick={clearCart}
              >
                {t('Очистить корзину', 'Clear cart', 'Warenkorb leeren', 'Vider le panier', 'Vaciar carrito')}
              </button>
            </div>
          </div>
        )}
      </aside>
    </>
  );
};
