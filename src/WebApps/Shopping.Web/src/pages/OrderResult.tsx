import { useEffect, useRef, useState } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { getOrderStatus } from '../services/api';
import { CheckCircle2, Clock, XCircle } from 'lucide-react';
import { useCart } from '../context/CartContext';

type Phase = 'loading' | 'paid' | 'pending' | 'failed' | 'unknown';

// Landing page for provider redirects (Stripe Success/Cancel, YooKassa/Xsolla
// return_url). The webhook — not this page — is what actually credits the order;
// here we only reflect its status while the player waits.
export const OrderResult: React.FC<{ outcome: 'complete' | 'cancelled' }> = ({ outcome }) => {
  const { t } = useLanguage();
  const { clearCart } = useCart();
  const [params] = useSearchParams();
  const orderId = params.get('order_id') ?? '';
  const [phase, setPhase] = useState<Phase>(outcome === 'cancelled' ? 'pending' : 'loading');
  const cartCleared = useRef(false);

  useEffect(() => {
    if (outcome === 'cancelled' || !orderId) return;

    let mounted = true;
    let attempts = 0;
    const tick = async () => {
      attempts += 1;
      const status = await getOrderStatus(orderId);
      if (!mounted) return;
      if (status === 'Paid') setPhase('paid');
      else if (status === 'Failed') setPhase('failed');
      else if (attempts < 40) setTimeout(tick, 3000); // webhook may still be in flight
      else setPhase('pending');
    };
    tick();
    return () => {
      mounted = false;
    };
  }, [orderId, outcome]);

  useEffect(() => {
    if (phase === 'paid' && !cartCleared.current) {
      cartCleared.current = true;
      clearCart();
    }
  }, [phase, clearCart]);

  const icon = phase === 'paid' ? <CheckCircle2 size={56} color="#00f2fe" />
    : phase === 'failed' ? <XCircle size={56} color="#ff4d4d" />
    : <Clock size={56} color="var(--accent-pink)" />;

  const title = phase === 'paid' ? t('Оплата прошла!', 'Payment received!', 'Zahlung erhalten!', 'Paiement reçu !', '¡Pago recibido!')
    : phase === 'failed' ? t('Платёж не прошёл', 'Payment failed', 'Zahlung fehlgeschlagen', 'Paiement échoué', 'Pago fallido')
    : t('Платёж обрабатывается…', 'Payment is being processed…', 'Zahlung wird verarbeitet…', 'Paiement en cours…', 'Pago en proceso…');

  const subtitle = phase === 'paid'
    ? t('Награда придёт в игру автоматически.', 'Your reward will arrive in the game automatically.', 'Die Belohnung kommt automatisch ins Spiel.', 'La récompense arrivera automatiquement dans le jeu.', 'Tu recompensa llegará al juego automáticamente.')
    : phase === 'failed'
      ? t('Вы можете попробовать ещё раз в любое время.', 'You can try again at any time.', 'Du kannst es jederzeit erneut versuchen.', 'Vous pouvez réessayer à tout moment.', 'Puedes intentarlo de nuevo en cualquier momento.')
      : t('Обычно это занимает меньше минуты. Награда придёт автоматически.', 'It usually takes less than a minute. The reward arrives automatically.', 'Dauert meist weniger als eine Minute. Die Belohnung kommt automatisch.', 'Cela prend généralement moins d’une minute.', 'Suele tardar menos de un minuto.');

  return (
    <div style={{ minHeight: '60vh', display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 14, textAlign: 'center', padding: '24px' }}>
      {icon}
      <h1 style={{ fontSize: '1.6rem', fontWeight: 900, color: '#fff', margin: 0 }}>{title}</h1>
      <p style={{ color: 'var(--text-muted)', maxWidth: 420, margin: 0 }}>{subtitle}</p>
      {orderId && (
        <div style={{ fontSize: '0.78rem', color: 'var(--text-dim)' }}>
          {t('Заказ', 'Order', 'Bestellung', 'Commande', 'Pedido')}: {orderId}
        </div>
      )}
      <Link to="/diamonds" className="btn btn-primary" style={{ marginTop: 10, padding: '12px 28px', borderRadius: 14 }}>
        {t('Вернуться в магазин', 'Back to shop', 'Zurück zum Shop', 'Retour à la boutique', 'Volver a la tienda')}
      </Link>
    </div>
  );
};
