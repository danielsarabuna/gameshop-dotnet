import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { CheckCircle2, FlaskConical, LoaderCircle, XCircle } from 'lucide-react';
import { completeMockPayment } from '../services/api';
import { useLanguage } from '../context/LanguageContext';

export const MockCheckout: React.FC = () => {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const { t } = useLanguage();
  const orderId = params.get('orderId') ?? '';
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const finish = async (status: 'succeeded' | 'failed') => {
    if (!orderId || busy) return;
    setBusy(true);
    setError('');
    if (await completeMockPayment(orderId, status)) {
      navigate(status === 'succeeded'
        ? `/order/complete?order_id=${encodeURIComponent(orderId)}`
        : `/order/failed?order_id=${encodeURIComponent(orderId)}`);
      return;
    }
    setBusy(false);
    setError(t('Не удалось завершить тестовую оплату. Проверьте локальный API.', 'Could not complete the test payment. Check the local API.'));
  };

  return (
    <main className="payment-page">
      <section className="payment-card">
        <span className="payment-card-icon"><FlaskConical size={31} /></span>
        <h1>{t('Тестовая оплата', 'Test payment')}</h1>
        <p>{t('Это локальный режим проверки. Деньги не списываются — выберите результат, который должен вернуть провайдер.', 'This is a local test. No money is charged—choose the result the provider should return.')}</p>
        <div className="payment-order-id">{orderId ? `${t('Заказ', 'Order')}: ${orderId}` : t('Номер заказа отсутствует', 'Order ID is missing')}</div>
        <div className="payment-actions">
          <button className="btn btn-primary" disabled={!orderId || busy} onClick={() => void finish('succeeded')}>{busy ? <LoaderCircle className="spin" size={17} /> : <CheckCircle2 size={17} />}{t('Подтвердить', 'Approve')}</button>
          <button className="btn btn-outline" disabled={!orderId || busy} onClick={() => void finish('failed')}><XCircle size={17} />{t('Отклонить', 'Decline')}</button>
        </div>
        {error && <div className="inline-alert error" role="alert">{error}</div>}
        <Link className="clear-cart" to="/diamonds">{t('Вернуться в магазин', 'Back to shop')}</Link>
      </section>
    </main>
  );
};
