import React, { useState, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useCart } from '../context/CartContext';
import { useLanguage } from '../context/LanguageContext';
import { useAuth } from '../context/AuthContext';
import { PaymentMethodInfo } from '../types';
import { getPaymentMethods, getCatalogPaymentProviders, applyPromoCode, createOrder, createPayment, completeMockPayment } from '../services/api';
import { ShoppingBagIcon } from './Icons';
import { ShoppingCart, X, Minus, Plus, Tag, User, CreditCard, CheckCircle2 } from 'lucide-react';
import { formatPrice } from '../utils/format';

export const CartDrawer: React.FC = () => {
  const navigate = useNavigate();
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
  const {
    playerId,
    playerName,
    setPlayerName,
    playerEmail,
    setPlayerEmail,
    region,
    storeChannel,
    gameVersion,
  } = useAuth();

  const [promoCode, setPromoCode] = useState('');
  const [appliedPromoCode, setAppliedPromoCode] = useState<string | null>(null);
  const [promoDiscountAmount, setPromoDiscountAmount] = useState(0);
  const [promoMessage, setPromoMessage] = useState<string | null>(null);
  const [promoBusy, setPromoBusy] = useState(false);

  const [paymentMethods, setPaymentMethods] = useState<(PaymentMethodInfo & { isAvailable: boolean; isSandbox?: boolean })[]>([]);
  const [selectedPaymentMethod, setSelectedPaymentMethod] = useState('');
  const [paymentMethodsFailed, setPaymentMethodsFailed] = useState(false);

  const [checkoutMessage, setCheckoutMessage] = useState<string | null>(null);
  const [checkoutBusy, setCheckoutBusy] = useState(false);
  const [pendingMockOrderId, setPendingMockOrderId] = useState('');

  // Production sessions are established only by a signed game deeplink ticket.
  const [idCheck, setIdCheck] = useState<'idle' | 'checking' | 'valid' | 'invalid'>('idle');
  const handlePlayerIdCommit = () => setIdCheck(playerId ? 'invalid' : 'idle');

  useEffect(() => {
    let mounted = true;
    Promise.all([
      getPaymentMethods(),
      getCatalogPaymentProviders(region, storeChannel, gameVersion || 'global'),
    ]).then(([configured, regional]) => {
      if (!mounted) return;
      const configuredByCode = new Map((configured ?? []).map((method) => [method.code.toLowerCase(), method]));
      const regionalMethods = (regional ?? []).filter((provider) => provider.isEnabled).map((provider) => {
        const configuredMethod = configuredByCode.get(provider.id.toLowerCase());
        return {
          code: provider.id.toLowerCase(),
          name: provider.displayName || configuredMethod?.name || provider.id,
          iconUrl: provider.iconUrl || configuredMethod?.iconUrl,
          isSandbox: provider.isSandbox,
          isAvailable: Boolean(configuredMethod),
        };
      });
      const list = regionalMethods.length > 0
        ? regionalMethods
        : (configured ?? []).map((method) => ({ ...method, isAvailable: true }));
      const available = list.filter((method) => method.isAvailable);
      setPaymentMethods(list);
      setSelectedPaymentMethod((current) =>
        available.some((method) => method.code === current)
          ? current
          : available.length === 1 ? available[0].code : ''
      );
      setPaymentMethodsFailed(available.length === 0);
    });
    return () => {
      mounted = false;
    };
  }, [region, storeChannel, gameVersion]);

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

  const hasMixedRewardTypes = new Set(lines.map((line) => line.type).filter(Boolean)).size > 1;
  const canCheckout =
    lines.length > 0 &&
    !hasMixedRewardTypes &&
    Boolean(playerId.trim()) &&
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
      code: promoCode.trim(),
      items: apiLines,
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
    if (hasMixedRewardTypes) {
      setCheckoutMessage(t(
        'Этот заказ нельзя создать одним платежом. Удалите подписку или алмазы и оформите покупки по очереди.',
        'This order cannot be created as one payment. Remove either the subscription or diamonds and buy them separately.'
      ));
      return;
    }

    if (!canCheckout) {
      setCheckoutMessage(t(
        'Проверьте вход через игру, email и выбранный способ оплаты.',
        'Check the game sign-in, email, and selected payment method.'
      ));
      return;
    }

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

    if (!paymentRes.success || !paymentRes.data) {
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

    if (selectedPaymentMethod.toLowerCase() === 'mockprovider') {
      setPendingMockOrderId(orderRes.data.orderId);
      return;
    }

    if (!paymentRes.data.checkoutUrl) {
      setCheckoutMessage(t('Провайдер не вернул ссылку оплаты.', 'The provider did not return a checkout URL.'));
      return;
    }

    window.location.href = paymentRes.data.checkoutUrl;
  };

  const finishMockPayment = async (status: 'succeeded' | 'failed') => {
    if (!pendingMockOrderId || checkoutBusy) return;
    setCheckoutBusy(true);
    setCheckoutMessage(null);
    const completed = await completeMockPayment(pendingMockOrderId, status);
    setCheckoutBusy(false);
    if (!completed) {
      setCheckoutMessage(t('Не удалось завершить тестовую оплату.', 'Could not complete the test payment.'));
      return;
    }

    if (status === 'succeeded') {
      const orderId = pendingMockOrderId;
      clearCart();
      setPendingMockOrderId('');
      closeCartDrawer();
      navigate(`/order/complete?order_id=${encodeURIComponent(orderId)}`);
      return;
    }

    setPendingMockOrderId('');
    setCheckoutMessage(t('Тестовый платёж отклонён. Корзина сохранена для повтора.', 'Test payment declined. Your cart is ready to retry.'));
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
          <div style={{ display: 'flex', flexDirection: 'column', flex: 1, minHeight: 0, overflowY: 'auto' }}>
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
                  readOnly
                  placeholder={t('Откройте магазин через игровой deeplink', 'Open the shop through a game deeplink', 'Shop über den Spiel-Deeplink öffnen', 'Ouvrez la boutique via le deeplink du jeu', 'Abra la tienda mediante el deeplink del juego')}
                  style={{
                    background: 'rgba(255,255,255,0.06)',
                    border: `1px solid ${idCheck === 'invalid' ? '#ff4d4d' : 'var(--border-color)'}`,
                    borderRadius: '10px',
                    padding: '8px 12px',
                    color: '#fff',
                    fontSize: '0.88rem',
                  }}
                />
                {idCheck === 'checking' && (
                  <div style={{ fontSize: '0.76rem', color: 'var(--text-muted)' }}>
                    {t('Проверяем ID…', 'Verifying ID…', 'ID wird geprüft…', 'Vérification de l’ID…', 'Verificando ID…')}
                  </div>
                )}
                {idCheck === 'valid' && (
                  <div style={{ fontSize: '0.76rem', color: '#00f2fe' }}>
                    {t(
                      'ID подтверждён — цены и регион обновлены.',
                      'ID verified — region and prices updated.',
                      'ID bestätigt — Region und Preise aktualisiert.',
                      'ID vérifié — région et prix mis à jour.',
                      'ID verificado: región y precios actualizados.'
                    )}
                  </div>
                )}
                {idCheck === 'invalid' && (
                  <div style={{ fontSize: '0.76rem', color: '#ff4d4d' }}>
                    {t(
                      'Игрок с таким ID не найден.',
                      'No player found with this ID.',
                      'Kein Spieler mit dieser ID gefunden.',
                      'Aucun joueur trouvé avec cet ID.',
                      'No se encontró ningún jugador con este ID.'
                    )}
                  </div>
                )}
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
                <div style={{ display: 'grid', gap: 8 }}>
                  {paymentMethods.map((method) => {
                    const selected = selectedPaymentMethod === method.code;
                    return (
                      <button
                        type="button"
                        key={method.code}
                        disabled={!method.isAvailable || checkoutBusy}
                        onClick={() => setSelectedPaymentMethod(method.code)}
                        style={{
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'space-between',
                          gap: 12,
                          padding: '10px 12px',
                          borderRadius: 12,
                          border: `1px solid ${selected ? 'var(--accent-pink)' : 'var(--border-color)'}`,
                          background: selected ? 'rgba(255,51,102,.13)' : 'rgba(255,255,255,.035)',
                          color: method.isAvailable ? '#fff' : 'var(--text-dim)',
                          cursor: method.isAvailable ? 'pointer' : 'not-allowed',
                          opacity: method.isAvailable ? 1 : 0.62,
                          textAlign: 'left',
                        }}
                      >
                        <span style={{ fontWeight: 750 }}>{method.name}</span>
                        <span style={{ fontSize: '.72rem', color: selected ? 'var(--accent-pink)' : 'var(--text-dim)' }}>
                          {method.isAvailable
                            ? method.isSandbox ? 'Sandbox' : t('Доступно', 'Available')
                            : t('Нужны ключи', 'Credentials required')}
                        </span>
                      </button>
                    );
                  })}
                </div>
                {paymentMethodsFailed && (
                  <div style={{ marginTop: 8, fontSize: '.76rem', color: '#ff7d8f' }}>
                    {t('Ни один способ оплаты не настроен локально.', 'No payment method is configured locally.')}
                  </div>
                )}
              </div>
            </div>

            {pendingMockOrderId && (
              <div style={{ marginBottom: 20, padding: 16, borderRadius: 14, border: '1px solid rgba(0,242,254,.35)', background: 'rgba(0,242,254,.08)' }}>
                <div style={{ display: 'flex', gap: 8, alignItems: 'center', color: '#fff', fontWeight: 800, marginBottom: 6 }}>
                  <CheckCircle2 size={18} color="var(--accent-cyan)" />
                  {t('Тестовая оплата', 'Test payment')}
                </div>
                <div style={{ color: 'var(--text-muted)', fontSize: '.82rem', lineHeight: 1.45, marginBottom: 12 }}>
                  {t('Деньги не списываются. Подтвердите результат прямо здесь, чтобы проверить начисление в Unity.', 'No money is charged. Confirm the result here to test delivery in Unity.')}
                </div>
                <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
                  <button type="button" className="btn btn-primary btn-sm" disabled={checkoutBusy} onClick={() => finishMockPayment('succeeded')}>
                    {t('Подтвердить', 'Approve')}
                  </button>
                  <button type="button" className="btn btn-outline btn-sm" disabled={checkoutBusy} onClick={() => finishMockPayment('failed')}>
                    {t('Отклонить', 'Decline')}
                  </button>
                </div>
              </div>
            )}

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

              {hasMixedRewardTypes ? (
                <div
                  role="alert"
                  style={{
                    fontSize: '0.82rem',
                    color: '#ff9aaa',
                    marginBottom: '10px',
                    padding: '10px 12px',
                    borderRadius: '10px',
                    border: '1px solid rgba(255, 77, 109, .55)',
                    background: 'rgba(255, 51, 102, .10)',
                    textAlign: 'center',
                    lineHeight: 1.4,
                  }}
                >
                  {t(
                    'Алмазы и подписку нужно оформить отдельно. Удалите один тип товара, оплатите его, затем оформите второй.',
                    'Diamonds and subscriptions must be purchased separately. Remove one reward type, pay, then buy the other.'
                  )}
                </div>
              ) : !canCheckout && (
                <div style={{ fontSize: '0.78rem', color: 'var(--text-dim)', marginBottom: '10px', textAlign: 'center' }}>
                  {t(
                    'Откройте магазин из игры и выберите способ оплаты.',
                    'Open the shop from the game and select a payment method.'
                  )}
                </div>
              )}

              <button
                type="button"
                className="btn btn-primary"
                style={{ width: '100%', padding: '14px', borderRadius: '14px', fontSize: '1rem' }}
                disabled={lines.length === 0 || checkoutBusy}
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
