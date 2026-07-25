import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { useCart } from '../context/CartContext';
import { useLanguage } from '../context/LanguageContext';
import { useAuth } from '../context/AuthContext';
import { PaymentMethodInfo } from '../types';
import { applyPromoCode, completeMockPayment, createOrder, createPayment, getCatalogPaymentProviders, getPaymentMethods } from '../services/api';
import { ShoppingBagIcon } from './Icons';
import { Check, CheckCircle2, CreditCard, LoaderCircle, Minus, Plus, RefreshCw, ShoppingCart, Tag, Trash2, User, X } from 'lucide-react';
import { formatPrice } from '../utils/format';
import {
  cartFingerprint,
  clearPendingPayment,
  PaymentOutcome,
  savePendingPayment,
} from '../services/paymentSession';

type AvailablePaymentMethod = PaymentMethodInfo & { isSandbox?: boolean };
type CheckoutPhase = 'idle' | 'creating-order' | 'creating-payment' | 'awaiting-mock' | 'redirecting' | 'error';

interface CartDrawerProps {
  onPaymentResult: (result: { orderId: string; outcome: PaymentOutcome }) => void;
}

export const CartDrawer: React.FC<CartDrawerProps> = ({ onPaymentResult }) => {
  const location = useLocation();
  const { lines, total, cartDrawerOpen, closeCartDrawer, increment, decrement, removeItem, clearCart } = useCart();
  const { t } = useLanguage();
  const { playerId, playerName, region, storeChannel, gameVersion, deliveryContractVersion, authStatus, openLoginModal } = useAuth();
  const hasPlayerSession = authStatus === 'game-session' || authStatus === 'recipient-session';

  const [promoCode, setPromoCode] = useState('');
  const [appliedPromoCode, setAppliedPromoCode] = useState<string | null>(null);
  const [promoDiscountAmount, setPromoDiscountAmount] = useState(0);
  const [promoMessage, setPromoMessage] = useState<string | null>(null);
  const [promoBusy, setPromoBusy] = useState(false);
  const [paymentMethods, setPaymentMethods] = useState<AvailablePaymentMethod[]>([]);
  const [selectedPaymentMethod, setSelectedPaymentMethod] = useState('');
  const [paymentMethodsBusy, setPaymentMethodsBusy] = useState(false);
  const [paymentMethodsFailed, setPaymentMethodsFailed] = useState(false);
  const [paymentReload, setPaymentReload] = useState(0);
  const [checkoutMessage, setCheckoutMessage] = useState<string | null>(null);
  const [checkoutPhase, setCheckoutPhase] = useState<CheckoutPhase>('idle');
  const [pendingMockOrderId, setPendingMockOrderId] = useState('');
  const checkoutAbort = useRef<AbortController | null>(null);
  const checkoutBusy = checkoutPhase === 'creating-order' || checkoutPhase === 'creating-payment' || checkoutPhase === 'redirecting';
  const checkoutLocked = checkoutBusy || checkoutPhase === 'awaiting-mock';

  useEffect(() => {
    let mounted = true;
    setPaymentMethodsBusy(true);
    Promise.all([getPaymentMethods(), getCatalogPaymentProviders(region, storeChannel, gameVersion || 'global')])
      .then(([configured, regional]) => {
        if (!mounted) return;
        const configuredByCode = new Map((configured ?? []).map((method) => [method.code.toLowerCase(), method]));
        const available = regional?.length
          ? regional.filter((provider) => provider.isEnabled && configuredByCode.has(provider.id.toLowerCase())).map((provider) => ({
              ...configuredByCode.get(provider.id.toLowerCase())!,
              code: provider.id.toLowerCase(),
              name: provider.displayName || configuredByCode.get(provider.id.toLowerCase())!.name,
              iconUrl: provider.iconUrl || configuredByCode.get(provider.id.toLowerCase())!.iconUrl,
              isSandbox: provider.isSandbox,
            }))
          : (configured ?? []).map((method) => ({ ...method, code: method.code.toLowerCase() }));
        setPaymentMethods(available);
        setSelectedPaymentMethod((current) => available.some((method) => method.code === current)
          ? current
          : available.length === 1 ? available[0].code : '');
        setPaymentMethodsFailed(available.length === 0);
        setPaymentMethodsBusy(false);
      });
    return () => { mounted = false; };
  }, [region, storeChannel, gameVersion, paymentReload]);

  const hasMixedRewardTypes = new Set(lines.map((line) => line.type).filter(Boolean)).size > 1;
  const mixedCheckoutUnsupported = hasMixedRewardTypes && deliveryContractVersion < 2;
  const canCheckout = lines.length > 0 && !mixedCheckoutUnsupported && hasPlayerSession && Boolean(selectedPaymentMethod);
  const totalWithPromo = Math.max(0, total - promoDiscountAmount);
  const cartCurrency = lines.find((line) => line.currency)?.currency;
  const currentCartFingerprint = cartFingerprint(lines);
  const previousCartFingerprint = useRef(currentCartFingerprint);

  const handleApplyPromo = async () => {
    if (!promoCode.trim()) {
      setPromoMessage(t('Введите промокод.', 'Enter a promo code.'));
      return;
    }
    setPromoBusy(true);
    setPromoMessage(null);
    const result = await applyPromoCode({ code: promoCode.trim(), items: lines.map((line) => ({ productId: line.sku, quantity: line.quantity })) });
    setPromoBusy(false);
    if (result?.isValid) {
      setAppliedPromoCode(promoCode.trim());
      setPromoDiscountAmount(result.discountAmount || 0);
      setPromoMessage(t('Промокод применён.', 'Promo code applied.'));
    } else {
      setAppliedPromoCode(null);
      setPromoDiscountAmount(0);
      setPromoMessage(result?.error || t('Не удалось применить промокод.', 'Could not apply the promo code.'));
    }
  };

  const resetCheckout = useCallback((clearPending = false) => {
    checkoutAbort.current?.abort();
    checkoutAbort.current = null;
    setCheckoutPhase('idle');
    setPendingMockOrderId('');
    if (clearPending) clearPendingPayment();
  }, []);

  const closeCheckout = useCallback(() => {
    resetCheckout(checkoutPhase === 'awaiting-mock');
    closeCartDrawer();
  }, [checkoutPhase, closeCartDrawer, resetCheckout]);

  useEffect(() => {
    if (previousCartFingerprint.current === currentCartFingerprint) return;
    previousCartFingerprint.current = currentCartFingerprint;
    if (checkoutPhase === 'idle') return;
    resetCheckout(true);
    setCheckoutMessage(null);
  }, [checkoutPhase, currentCartFingerprint, resetCheckout]);

  useEffect(() => {
    if (!cartDrawerOpen) return;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    const onKeyDown = (event: KeyboardEvent) => event.key === 'Escape' && closeCheckout();
    window.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKeyDown);
    };
  }, [cartDrawerOpen, closeCheckout]);

  useEffect(() => () => checkoutAbort.current?.abort(), []);

  const handleCheckout = async () => {
    if (!canCheckout || checkoutLocked) return;
    const controller = new AbortController();
    checkoutAbort.current?.abort();
    checkoutAbort.current = controller;
    setCheckoutMessage(null);
    try {
      setCheckoutPhase('creating-order');
      const order = await createOrder({
        gameUserId: playerId,
        paymentMethod: selectedPaymentMethod,
        items: lines.map((line) => ({ productId: line.sku, quantity: line.quantity })),
        promoCode: appliedPromoCode || undefined,
      }, controller.signal);
      if (controller.signal.aborted) return;
      if (!order.success || !order.data) {
        setCheckoutPhase('error');
        setCheckoutMessage(order.error || t('Не удалось создать заказ.', 'Could not create the order.'));
        return;
      }

      savePendingPayment({
        orderId: order.data.orderId,
        provider: selectedPaymentMethod,
        returnPath: `${location.pathname}${location.search}${location.hash}`,
        cartFingerprint: currentCartFingerprint,
        createdAtUtc: new Date().toISOString(),
      });
      setCheckoutPhase('creating-payment');
      const payment = await createPayment(order.data.orderId, selectedPaymentMethod, controller.signal);
      if (controller.signal.aborted) return;
      if (!payment.success || !payment.data) {
        setCheckoutPhase('error');
        setCheckoutMessage(payment.error || t('Заказ создан, но платёж не удалось подготовить.', 'The order was created, but payment could not be prepared.'));
        return;
      }
      if (selectedPaymentMethod === 'mockprovider') {
        setPendingMockOrderId(order.data.orderId);
        setCheckoutPhase('awaiting-mock');
        return;
      }
      if (!payment.data.checkoutUrl) {
        setCheckoutPhase('error');
        setCheckoutMessage(t('Провайдер не вернул ссылку оплаты.', 'The provider did not return a checkout URL.'));
        return;
      }
      setCheckoutPhase('redirecting');
      setCheckoutMessage(t('Переходим к безопасной оплате…', 'Opening secure payment…'));
      window.location.assign(payment.data.checkoutUrl);
    } catch {
      if (!controller.signal.aborted) {
        setCheckoutPhase('error');
        setCheckoutMessage(t('Не удалось начать оплату. Попробуйте ещё раз.', 'Could not start checkout. Try again.'));
      }
    } finally {
      if (checkoutAbort.current === controller) checkoutAbort.current = null;
    }
  };

  const finishMockPayment = async (status: 'succeeded' | 'failed') => {
    if (!pendingMockOrderId || checkoutBusy) return;
    const orderId = pendingMockOrderId;
    const controller = new AbortController();
    checkoutAbort.current = controller;
    setCheckoutPhase('creating-payment');
    try {
      if (!(await completeMockPayment(orderId, status, controller.signal))) {
        if (!controller.signal.aborted) {
          setCheckoutPhase('error');
          setCheckoutMessage(t('Не удалось завершить тестовую оплату.', 'Could not complete the test payment.'));
        }
        return;
      }
      setPendingMockOrderId('');
      setCheckoutPhase('idle');
      closeCartDrawer();
      onPaymentResult({ orderId, outcome: status === 'succeeded' ? 'complete' : 'failed' });
    } finally {
      if (checkoutAbort.current === controller) checkoutAbort.current = null;
    }
  };

  return (
    <>
      <div className={`cart-overlay ${cartDrawerOpen ? 'open' : ''}`} onClick={closeCheckout} />
      <aside className={`cart-drawer checkout-drawer ${cartDrawerOpen ? 'open' : ''}`} aria-label={t('Корзина и оплата', 'Cart and checkout')} aria-hidden={!cartDrawerOpen}>
        <header className="drawer-header">
          <h2><ShoppingCart size={21} />{t('Корзина', 'Cart')}</h2>
          <button type="button" className="cart-close-btn" aria-label={t('Закрыть корзину', 'Close cart')} onClick={closeCheckout}><X size={17} /></button>
        </header>

        {lines.length === 0 ? (
          <div className="cart-empty"><span className="cart-empty-icon"><ShoppingBagIcon size={34} /></span><h3>{t('Корзина пуста', 'Cart is empty')}</h3><p>{t('Добавьте товар, чтобы перейти к оплате.', 'Add an item to continue to checkout.')}</p><button className="btn btn-primary" onClick={closeCartDrawer}>{t('Продолжить покупки', 'Continue shopping')}</button></div>
        ) : (
          <>
            <div className="checkout-scroll">
              <details className="checkout-section" open>
                <summary><span><ShoppingBagIcon size={17} />{t('Товары', 'Items')}</span><strong>{lines.length}</strong></summary>
                <div className="checkout-section-body cart-lines">
                  {lines.map((line) => (
                    <article className="cart-line" key={line.sku}>
                      <span className="cart-line-image">{line.imageUrl ? <img src={line.imageUrl} alt="" /> : <ShoppingBagIcon size={19} />}</span>
                      <span className="cart-line-copy"><strong>{line.title}</strong><small>{formatPrice(line.unitPrice * line.quantity, line.currency || cartCurrency)}</small></span>
                      <span className="quantity-control"><button disabled={checkoutLocked} aria-label={t('Уменьшить', 'Decrease')} onClick={() => decrement(line.sku)}><Minus size={12} /></button><b>{line.quantity}</b><button disabled={checkoutLocked} aria-label={t('Увеличить', 'Increase')} onClick={() => increment(line.sku)}><Plus size={12} /></button></span>
                      <button className="remove-line" disabled={checkoutLocked} aria-label={t('Удалить товар', 'Remove item')} onClick={() => removeItem(line.sku)}><X size={15} /></button>
                    </article>
                  ))}
                  <div className="promo-row"><Tag size={15} /><input disabled={checkoutLocked} value={promoCode} onChange={(event) => setPromoCode(event.target.value)} placeholder={t('Промокод', 'Promo code')} /><button disabled={promoBusy || checkoutLocked} onClick={() => void handleApplyPromo()}>{t('Применить', 'Apply')}</button></div>
                  {promoMessage && <div className={`field-message ${appliedPromoCode ? 'success' : 'error'}`}>{promoMessage}</div>}
                </div>
              </details>

              <details className="checkout-section" open>
                <summary><span><User size={17} />{t('Получатель', 'Recipient')}</span>{hasPlayerSession && <Check size={16} />}</summary>
                <div className="checkout-section-body">
                  {hasPlayerSession ? (
                    <div className="recipient-summary"><span className="recipient-avatar"><User size={18} /></span><span><strong>{playerName || t('Игрок', 'Player')}</strong><small>{t('Регион', 'Region')}: {region}</small></span><button disabled={checkoutLocked} onClick={openLoginModal}>{t('Изменить', 'Change')}</button></div>
                  ) : (
                    <button className="recipient-empty" onClick={openLoginModal}><User size={19} /><span><strong>{t('Указать Player ID', 'Enter Player ID')}</strong><small>{t('Имя и регион загрузятся автоматически', 'Name and region load automatically')}</small></span></button>
                  )}
                </div>
              </details>

              <details className="checkout-section" open>
                <summary><span><CreditCard size={17} />{t('Способ оплаты', 'Payment method')}</span>{selectedPaymentMethod && <Check size={16} />}</summary>
                <div className="checkout-section-body payment-methods">
                  {paymentMethodsBusy && <div className="loading-row"><LoaderCircle className="spin" size={17} />{t('Загружаем способы оплаты…', 'Loading payment methods…')}</div>}
                  {paymentMethods.map((method) => <button type="button" key={method.code} className={selectedPaymentMethod === method.code ? 'selected' : ''} disabled={checkoutLocked} onClick={() => setSelectedPaymentMethod(method.code)}><span>{method.name}</span>{method.isSandbox && <small>Sandbox</small>}{selectedPaymentMethod === method.code && <CheckCircle2 size={17} />}</button>)}
                  {paymentMethodsFailed && <div className="inline-alert error"><span>{t('Способы оплаты временно недоступны.', 'Payment methods are temporarily unavailable.')}</span><button onClick={() => setPaymentReload((value) => value + 1)}><RefreshCw size={14} />{t('Повторить', 'Retry')}</button></div>}
                </div>
              </details>

              {pendingMockOrderId && <section className="mock-payment-panel"><CheckCircle2 size={20} /><div><strong>{t('Тестовая оплата', 'Test payment')}</strong><p>{t('Деньги не списываются. Выберите результат проверки.', 'No money is charged. Choose the test result.')}</p></div><div className="mock-actions"><button className="btn btn-primary btn-sm" disabled={checkoutBusy} onClick={() => void finishMockPayment('succeeded')}>{t('Подтвердить', 'Approve')}</button><button className="btn btn-outline btn-sm" disabled={checkoutBusy} onClick={() => void finishMockPayment('failed')}>{t('Отклонить', 'Decline')}</button></div></section>}
            </div>

            <footer className="checkout-footer">
              {mixedCheckoutUnsupported && <div className="inline-alert error" role="alert">{t('Для общей покупки алмазов и подписки откройте магазин из обновлённой игры.', 'Open the shop from the updated game to purchase diamonds and a subscription together.')}</div>}
              {checkoutMessage && <div className="field-message error" role="status">{checkoutMessage}</div>}
              <div className="checkout-total"><span>{t('Итого', 'Total')}</span><strong>{formatPrice(totalWithPromo, cartCurrency)}</strong></div>
              {promoDiscountAmount > 0 && <div className="checkout-discount">{t('Скидка', 'Discount')}: −{formatPrice(promoDiscountAmount, cartCurrency)}</div>}
              <button type="button" className="btn btn-primary checkout-button" disabled={!canCheckout || checkoutLocked} onClick={() => void handleCheckout()}>{checkoutBusy ? <><LoaderCircle className="spin" size={18} />{checkoutPhase === 'redirecting' ? t('Переходим к оплате…', 'Opening payment…') : t('Обработка…', 'Processing…')}</> : t('Перейти к оплате', 'Continue to payment')}</button>
              <button type="button" className="clear-cart" disabled={checkoutLocked} onClick={() => { resetCheckout(true); clearCart(); }}><Trash2 size={14} />{t('Очистить корзину', 'Clear cart')}</button>
            </footer>
          </>
        )}
      </aside>
    </>
  );
};
