import { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { completeMockPayment } from '../services/api';

export const MockCheckout: React.FC = () => {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const orderId = params.get('orderId') ?? '';
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const finish = async (status: 'succeeded' | 'failed') => {
    if (!orderId || busy) return;
    setBusy(true);
    setError('');
    if (await completeMockPayment(orderId, status)) {
      navigate(`/order/complete?order_id=${encodeURIComponent(orderId)}`);
      return;
    }
    setBusy(false);
    setError('Mock payment could not be completed. Check the local API logs.');
  };

  return (
    <div style={{ minHeight: '60vh', display: 'grid', placeItems: 'center', padding: 24 }}>
      <section style={{ width: 'min(460px, 100%)', padding: 28, borderRadius: 20, background: 'rgba(20,16,28,.96)', border: '1px solid var(--border-color)', textAlign: 'center' }}>
        <h1 style={{ color: '#fff', marginTop: 0 }}>Local mock payment</h1>
        <p style={{ color: 'var(--text-muted)' }}>No money is charged. Choose the provider result to test the complete flow.</p>
        <code style={{ display: 'block', overflowWrap: 'anywhere', color: 'var(--text-dim)', marginBottom: 20 }}>{orderId || 'Missing orderId'}</code>
        <div style={{ display: 'flex', gap: 12, justifyContent: 'center' }}>
          <button className="btn btn-primary" disabled={!orderId || busy} onClick={() => finish('succeeded')}>Approve</button>
          <button className="btn btn-outline" disabled={!orderId || busy} onClick={() => finish('failed')}>Decline</button>
        </div>
        {error && <p style={{ color: '#ff4d4d' }}>{error}</p>}
      </section>
    </div>
  );
};
