import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { AlertCircle, CheckCircle2, Clock, LoaderCircle, RefreshCw, RotateCcw, X, XCircle } from 'lucide-react';
import { useLanguage } from '../context/LanguageContext';
import { createPayment, getOrderStatus } from '../services/api';
import { useCart } from '../context/CartContext';
import { useDialogA11y } from '../components/Modals';
import {
  clearPendingPayment,
  readPendingPayment,
  safeReturnPath,
  StoredPaymentResult,
} from '../services/paymentSession';

type Phase = 'paid' | 'processing' | 'cancelled' | 'failed' | 'missing-order' | 'status-unavailable';

interface PaymentReturnProps {
  outcome: StoredPaymentResult['outcome'];
  onResult: (result: StoredPaymentResult) => void;
}

export const PaymentReturn: React.FC<PaymentReturnProps> = ({ outcome, onResult }) => {
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const orderId = params.get('order_id') ?? '';

  useEffect(() => {
    const pending = readPendingPayment();
    onResult({ orderId, outcome });
    navigate(safeReturnPath(pending?.returnPath), { replace: true });
  }, [navigate, onResult, orderId, outcome]);

  return null;
};

interface PaymentResultModalProps {
  result: StoredPaymentResult;
  onClose: () => void;
}

export const PaymentResultModal: React.FC<PaymentResultModalProps> = ({ result, onClose }) => {
  const { t } = useLanguage();
  const { clearCart, openCartDrawer } = useCart();
  const { orderId, outcome } = result;
  const [phase, setPhase] = useState<Phase>(!orderId ? 'missing-order' : outcome === 'cancelled' ? 'cancelled' : outcome === 'failed' ? 'failed' : 'processing');
  const [retryBusy, setRetryBusy] = useState(false);
  const [statusCheckVersion, setStatusCheckVersion] = useState(0);
  const cartCleared = useRef(false);
  const cardRef = useRef<HTMLElement>(null);
  useDialogA11y(true, onClose, cardRef);

  const checkStatus = useCallback(() => {
    if (!orderId || outcome !== 'complete') return () => undefined;
    let active = true;
    let attempts = 0;
    let failures = 0;
    let timer: number | undefined;
    setPhase('processing');
    const tick = async () => {
      attempts += 1;
      const status = await getOrderStatus(orderId);
      if (!active) return;
      if (status === 'Paid') setPhase('paid');
      else if (status === 'Failed') setPhase('failed');
      else {
        failures = status === null ? failures + 1 : 0;
        if (failures >= 5 || attempts >= 40) setPhase('status-unavailable');
        else timer = window.setTimeout(tick, 3000);
      }
    };
    void tick();
    return () => { active = false; if (timer) window.clearTimeout(timer); };
  }, [orderId, outcome, statusCheckVersion]);

  useEffect(() => checkStatus(), [checkStatus]);
  useEffect(() => {
    if (phase !== 'paid' || cartCleared.current) return;
    cartCleared.current = true;
    clearCart();
    clearPendingPayment();
  }, [phase, clearCart]);

  const retryPayment = async () => {
    if (retryBusy) return;
    const pending = readPendingPayment();
    if (pending?.orderId !== orderId || !pending.provider) {
      setPhase('status-unavailable');
      return;
    }
    try {
      setRetryBusy(true);
      const payment = await createPayment(orderId, pending.provider);
      if (payment.success && payment.data?.checkoutUrl) window.location.assign(payment.data.checkoutUrl);
      else setPhase('status-unavailable');
    } finally {
      setRetryBusy(false);
    }
  };

  const returnToCheckout = () => {
    onClose();
    openCartDrawer();
  };

  const content = {
    paid: [<CheckCircle2 size={32} />, t('Оплата прошла', 'Payment received'), t('Награда отправлена в игру и появится в окне получения покупки.', 'Your reward has been sent to the game and will appear in the purchase reward popup.')],
    processing: [<Clock size={32} />, t('Платёж обрабатывается', 'Payment is processing'), t('Обычно это занимает меньше минуты. Статус обновится автоматически.', 'This usually takes less than a minute. The status updates automatically.')],
    cancelled: [<RotateCcw size={32} />, t('Оплата отменена', 'Payment cancelled'), t('Деньги не списаны. Корзина сохранена, и оплату можно повторить.', 'No money was charged. Your cart is saved and you can retry.')],
    failed: [<XCircle size={32} />, t('Платёж не прошёл', 'Payment failed'), t('Корзина сохранена. Вернитесь к оплате и попробуйте ещё раз.', 'Your cart is saved. Return to checkout and try again.')],
    'missing-order': [<AlertCircle size={32} />, t('Заказ не найден', 'Order not found'), t('В ссылке отсутствует номер заказа. Корзина и магазин по-прежнему доступны.', 'The link has no order ID. Your cart and the shop are still available.')],
    'status-unavailable': [<AlertCircle size={32} />, t('Статус пока недоступен', 'Status is unavailable'), t('Заказ сохранён. Проверьте статус ещё раз или продолжите покупки.', 'Your order is saved. Check again or continue shopping.')],
  }[phase];

  return (
    <div className="modal-overlay payment-result-overlay" onMouseDown={(event) => event.target === event.currentTarget && onClose()}>
      <section ref={cardRef} className={`payment-card payment-result-modal ${phase}`} role="dialog" aria-modal="true" aria-labelledby="payment-result-title" aria-live="polite">
        <button type="button" className="cart-close payment-result-close" aria-label={t('Закрыть', 'Close')} onClick={onClose}><X size={17} /></button>
        <span className="payment-card-icon">{phase === 'processing' ? <LoaderCircle className="spin" size={32} /> : content[0]}</span>
        <h1 id="payment-result-title">{content[1]}</h1>
        <p>{content[2]}</p>
        {orderId && <details className="payment-order-details"><summary>{t('Детали заказа', 'Order details')}</summary><div className="payment-order-id">{orderId}</div></details>}
        <div className={`payment-actions ${phase === 'paid' || phase === 'missing-order' ? 'single' : ''}`}>
          {phase === 'cancelled' && <button className="btn btn-primary" disabled={retryBusy} onClick={() => void retryPayment()}>{retryBusy ? <LoaderCircle className="spin" size={17} /> : <RefreshCw size={17} />}{t('Повторить оплату', 'Retry payment')}</button>}
          {phase === 'failed' && <button className="btn btn-primary" onClick={returnToCheckout}><RefreshCw size={17} />{t('Вернуться к оплате', 'Return to checkout')}</button>}
          {phase === 'status-unavailable' && <button className="btn btn-primary" onClick={() => setStatusCheckVersion((value) => value + 1)}><RefreshCw size={17} />{t('Проверить ещё раз', 'Check again')}</button>}
          <button type="button" className={phase === 'paid' || phase === 'missing-order' ? 'btn btn-primary' : 'btn btn-outline'} onClick={onClose}>{t('Продолжить покупки', 'Continue shopping')}</button>
        </div>
      </section>
    </div>
  );
};
