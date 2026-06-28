import React, { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { getCatalogItems } from '../services/api';
import { SkeletonCard } from '../components/Skeleton';
import { SparklesIcon, CrownIcon } from '../components/Icons';
import { CheckCircle2, Zap, Volume2, Lock, ShoppingCart, RefreshCw, AlertCircle } from 'lucide-react';
import { formatPrice } from '../utils/format';

interface SubPlan {
  sku: string;
  title: string;
  duration: string;
  price: number;
  currency: string;
  perMonth: string;
  badge?: { text: string; kind: 'gold' | 'pink' | 'cyan' };
  features: string[];
}

const FALLBACK_PLANS: SubPlan[] = [
  {
    sku: '9aa00000-0000-0000-0000-000000000001',
    title: 'Premium — 1 месяц',
    duration: '1 Месяц',
    price: 6.99,
    currency: 'EUR',
    perMonth: '€6.99/мес',
    features: [
      'Доступ к секретным веткам сюжета',
      'Эксклюзивная озвучка персонажей',
      'Ежедневный бонус +10 алмазов',
    ],
  },
  {
    sku: '9aa00000-0000-0000-0000-000000000003',
    title: 'Premium — 3 месяца',
    duration: '3 Месяца',
    price: 17.99,
    currency: 'EUR',
    perMonth: '€5.99/мес (-15%)',
    badge: { text: 'ХИТ ПРОДАЖ', kind: 'pink' },
    features: [
      'Все привилегии на 3 месяца',
      'Скидка 15% на стоимость подписки',
      'Приоритетная поддержка 24/7',
    ],
  },
  {
    sku: '9aa00000-0000-0000-0000-000000000012',
    title: 'Premium — 12 месяцев',
    duration: '12 Месяцев',
    price: 59.99,
    currency: 'EUR',
    perMonth: '€4.99/мес (-30%)',
    badge: { text: 'МАКСИМАЛЬНАЯ ВЫГОДА', kind: 'gold' },
    features: [
      'Максимальный статус VIP в игре',
      'Скидка 30% на годовую подписку',
      'Эксклюзивные аватары и значки',
    ],
  },
];

export const Subscription: React.FC = () => {
  const { t } = useLanguage();
  const { addItem, openCartDrawer } = useCart();
  const { region, storeChannel, gameVersion } = useAuth();
  const [plans, setPlans] = useState<SubPlan[]>([]);
  const [loading, setLoading] = useState(true);
  const [isBackendConnected, setIsBackendConnected] = useState<boolean | null>(null);

  const fetchPlans = () => {
    setLoading(true);
    getCatalogItems(region, storeChannel, gameVersion).then((items) => {
      setLoading(false);
      if (items && items.length > 0) {
        const subItems = items.filter((i) => i.type === 'Subscription');
        if (subItems.length > 0) {
          setIsBackendConnected(true);
          const mapped: SubPlan[] = subItems.map((item) => {
            let badge: SubPlan['badge'] = undefined;
            if (item.metadata?.badgeKey === 'best') badge = { text: 'МАКСИМАЛЬНАЯ ВЫГОДА', kind: 'gold' };

            const months = item.metadata?.months || '1';
            const monthsNum = parseInt(months, 10) || 1;
            const currency = item.currency || 'EUR';
            return {
              sku: item.id,
              title: item.title,
              duration: `${months} ${months === '1' ? 'Месяц' : 'Месяца'}`,
              price: item.price,
              currency,
              perMonth: `${formatPrice(item.price / monthsNum, currency)}/мес`,
              badge,
              features: [
                t('Доступ к секретным веткам', 'Access to secret story branches'),
                t('Эксклюзивная озвучка персонажей', 'Exclusive character voiceovers'),
                t('Ежедневные бонусы алмазов', 'Daily bonus diamonds'),
              ],
            };
          });
          setPlans(mapped);
          return;
        }
      }

      setIsBackendConnected(false);
      setPlans(FALLBACK_PLANS);
    });
  };

  useEffect(() => {
    fetchPlans();
  }, []);

  const handleAddToCart = (plan: SubPlan) => {
    addItem(plan.sku, plan.title, plan.price, 1, undefined, plan.currency);
    openCartDrawer();
  };

  return (
    <div style={{ position: 'relative', width: '100%', minHeight: '100vh' }}>
      <img src="/images/Story Realms-bg.jpg" alt="" className="page-bg" />
      <div className="page-content page-container" style={{ padding: '130px 6% 80px', maxWidth: 1180, margin: '0 auto' }}>
        {/* PAGE HEADER */}
        <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 24 }}>
          <h1 className="page-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontSize: '2.4rem', fontWeight: 800 }}>
            <SparklesIcon size={36} color="var(--accent-pink)" />
            <span className="gradient-text">{t('Подписка Premium', 'Premium Subscription', 'Premium-Abonnement', 'Abonnement Premium', 'Suscripción Premium')}</span>
          </h1>
          <p className="page-subtitle" style={{ fontSize: '1.05rem', maxWidth: 640, margin: '12px auto 0' }}>
            {t(
              'Откройте полный доступ ко всем историям, озвучке персонажей и ультимативным романтическим сценариям.',
              'Unlock full access to all visual stories, character voiceovers, and ultimate romantic scenarios.'
            )}
          </p>
        </div>

          {/* OFFLINE PLACEHOLDER ONLY (no success banner) */}
          {isBackendConnected === false && (
            <div
              className="seq-item seq-delay-1"
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                flexWrap: 'wrap',
                gap: '8px',
                padding: '10px 18px',
                borderRadius: 14,
                marginBottom: 24,
                background: 'rgba(255, 170, 0, 0.12)',
                border: '1px solid rgba(255, 170, 0, 0.3)',
                fontSize: '0.86rem',
                color: '#ffaa00',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <AlertCircle size={16} />
                <span>
                  {t('Сервер каталога временно недоступен. Показаны ознакомительные тарифы.', 'Catalog server temporarily offline. Showing preview plans.')}
                </span>
              </div>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={fetchPlans}
                style={{ padding: '4px 10px', fontSize: '0.8rem', color: 'inherit' }}
              >
                <RefreshCw size={12} />
                {t('Обновить', 'Refresh')}
              </button>
            </div>
          )}

          {/* PERKS SUMMARY BANNER */}
          <div className="glass-card seq-item seq-delay-2" style={{ marginBottom: '36px', padding: '28px' }}>
            <h2 style={{ fontSize: '1.4rem', fontWeight: 800, color: '#ffffff', marginBottom: '20px', display: 'flex', alignItems: 'center', gap: '10px' }}>
              <CrownIcon size={24} color="#ffaa00" />
              {t('Что дает Premium?', 'What does Premium offer?', 'Was bietet Premium?', 'Que propose Premium ?', '¿Qué ofrece Premium?')}
            </h2>
            <div className="cards-grid-3">
              <div style={{ display: 'flex', gap: '14px', alignItems: 'flex-start' }}>
                <div style={{ padding: '10px', borderRadius: '12px', background: 'rgba(255, 51, 102, 0.15)', color: 'var(--accent-pink)' }}>
                  <Lock size={20} />
                </div>
                <div>
                  <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.98rem' }}>
                    {t('Секретные ветки', 'Secret Branches', 'Geheime Pfade', 'Branches secrètes', 'Rutas secretas')}
                  </div>
                  <div style={{ fontSize: '0.86rem', color: 'var(--text-muted)', marginTop: '4px' }}>
                    {t('Сценарии, недоступные в стандартной версии', 'Scenarios unavailable in standard mode')}
                  </div>
                </div>
              </div>

              <div style={{ display: 'flex', gap: '14px', alignItems: 'flex-start' }}>
                <div style={{ padding: '10px', borderRadius: '12px', background: 'rgba(0, 242, 254, 0.15)', color: 'var(--accent-cyan)' }}>
                  <Volume2 size={20} />
                </div>
                <div>
                  <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.98rem' }}>
                    {t('Озвучка диалогов', 'Full Voice Acting', 'Vollständige Vertonung', 'Doublage intégral', 'Doblaje completo')}
                  </div>
                  <div style={{ fontSize: '0.86rem', color: 'var(--text-muted)', marginTop: '4px' }}>
                    {t('Чувственные голоса ваших любимых героев', 'Sensual character voice lines')}
                  </div>
                </div>
              </div>

              <div style={{ display: 'flex', gap: '14px', alignItems: 'flex-start' }}>
                <div style={{ padding: '10px', borderRadius: '12px', background: 'rgba(255, 170, 0, 0.15)', color: '#ffaa00' }}>
                  <Zap size={20} />
                </div>
                <div>
                  <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.98rem' }}>
                    {t('Ежедневные бонусы', 'Daily Bonuses', 'Tägliche Boni', 'Bonus quotidiens', 'Bonus diarios')}
                  </div>
                  <div style={{ fontSize: '0.86rem', color: 'var(--text-muted)', marginTop: '4px' }}>
                    {t('Алмазы за каждый день захода в играх', 'Extra diamonds every day')}
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* SUBSCRIPTION PLANS GRID / SKELETON */}
          <div className="cards-grid-3 seq-item seq-delay-3">
            {loading ? (
              <SkeletonCard count={3} />
            ) : plans.length === 0 ? (
              <div className="glass-card" style={{ gridColumn: '1 / -1', textAlign: 'center', padding: '48px 24px' }}>
                <AlertCircle size={40} color="var(--accent-pink)" style={{ marginBottom: '16px' }} />
                <h3 style={{ fontSize: '1.4rem', color: '#fff', marginBottom: '8px' }}>
                  {t('Тарифы недоступны', 'No plans available')}
                </h3>
                <p className="muted" style={{ marginBottom: '20px' }}>
                  {t('В настоящий момент нет доступных тарифов подписки.', 'No subscription plans available at the moment.')}
                </p>
                <button type="button" className="btn btn-primary" onClick={fetchPlans}>
                  <RefreshCw size={16} />
                  {t('Повторить попытку', 'Retry loading')}
                </button>
              </div>
            ) : (
              plans.map((plan) => (
                <div
                  key={plan.sku}
                  className="glass-card"
                  style={{
                    display: 'flex',
                    flexDirection: 'column',
                    justifyContent: 'space-between',
                    padding: '32px 24px',
                    minHeight: '360px',
                    border: plan.badge ? '1px solid var(--accent-pink)' : undefined,
                    boxShadow: plan.badge ? '0 10px 30px rgba(255, 51, 102, 0.25)' : undefined,
                  }}
                >
                  <div>
                    {plan.badge && (
                      <div style={{ position: 'absolute', top: '12px', right: '12px' }}>
                        <span className={`badge-tag badge-${plan.badge.kind}`}>{plan.badge.text}</span>
                      </div>
                    )}

                    <div style={{ fontSize: '1.4rem', fontWeight: 900, color: '#ffffff', marginBottom: '8px' }}>
                      {plan.duration}
                    </div>

                    <div style={{ display: 'flex', alignItems: 'baseline', flexWrap: 'wrap', gap: '6px', margin: '16px 0 8px' }}>
                      <span style={{ fontSize: 'clamp(1.5rem, 3.5vw, 2.2rem)', fontWeight: 900, color: '#ffffff', wordBreak: 'break-word' }}>
                        {formatPrice(plan.price, plan.currency)}
                      </span>
                      <span style={{ fontSize: '0.88rem', color: 'var(--text-dim)' }}>/ {plan.duration}</span>
                    </div>

                    <div style={{ fontSize: '0.85rem', color: 'var(--accent-cyan)', fontWeight: 700, marginBottom: '24px' }}>
                      {plan.perMonth}
                    </div>

                    <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginBottom: '32px' }}>
                      {plan.features.map((feat, idx) => (
                        <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: '10px', fontSize: '0.9rem', color: 'var(--text-muted)' }}>
                          <CheckCircle2 size={16} color="var(--accent-pink)" />
                          <span>{feat}</span>
                        </div>
                      ))}
                    </div>
                  </div>

                  <button
                    type="button"
                    className={`btn ${plan.badge?.kind === 'gold' ? 'btn-gold' : 'btn-primary'}`}
                    style={{ width: '100%', borderRadius: '20px' }}
                    onClick={() => handleAddToCart(plan)}
                  >
                    <ShoppingCart size={18} color="#ffffff" />
                    <span>{t('Выбрать тариф', 'Select Plan', 'Plan wählen', 'Choisir l\'offre', 'Elegir plan')}</span>
                  </button>
                </div>
              ))
            )}
          </div>
      </div>
    </div>
  );
};
