import React, { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { getCatalogItems } from '../services/api';
import { SkeletonCard } from '../components/Skeleton';
import { SparklesIcon, CrownIcon } from '../components/Icons';
import { CheckCircle2, Zap, Volume2, Lock, ShoppingCart, RefreshCw, AlertCircle, ChevronDown } from 'lucide-react';
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
  const [catalogItems, setCatalogItems] = useState<any[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [isBackendConnected, setIsBackendConnected] = useState<boolean | null>(null);

  const fetchPlans = () => {
    setLoading(true);
    getCatalogItems(region, storeChannel, gameVersion).then((items) => {
      setLoading(false);
      if (items !== null) {
        setIsBackendConnected(true);
        setCatalogItems(items.filter((i) => i.type === 'Subscription'));
      } else {
        // Backend offline / unavailable
        setIsBackendConnected(false);
        setCatalogItems(null);
      }
    });
  };

  useEffect(() => {
    fetchPlans();
  }, []);

  const plans = React.useMemo<SubPlan[]>(() => {
    const rawPlans = (isBackendConnected === false || catalogItems === null)
      ? FALLBACK_PLANS
      : catalogItems.map((item) => {
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
            badge: item.metadata?.badgeKey === 'best' ? { text: 'МАКСИМАЛЬНАЯ ВЫГОДА', kind: 'gold' as const } : undefined,
            features: [
              t('Доступ к секретным веткам', 'Access to secret story branches'),
              t('Эксклюзивная озвучка персонажей', 'Exclusive character voiceovers'),
              t('Ежедневные бонусы алмазов', 'Daily bonus diamonds'),
            ],
          };
        });

    return rawPlans.map((plan) => {
      // Localize badge
      let localizedBadge = plan.badge;
      if (plan.badge) {
        let badgeText = plan.badge.text;
        if (plan.badge.text === 'МАКСИМАЛЬНАЯ ВЫГОДА' || plan.badge.text === 'MAX VALUE') {
          badgeText = t('МАКСИМАЛЬНАЯ ВЫГОДА', 'MAX VALUE', 'MAXIMALER VORTEIL', 'MAXIMUM D\'AVANTAGES', 'MÁXIMO VALOR');
        } else if (plan.badge.text === 'ХИТ ПРОДАЖ' || plan.badge.text === 'BEST SELLER') {
          badgeText = t('ХИТ ПРОДАЖ', 'BEST SELLER', 'BESTSELLER', 'MEILLEURE VENTE', 'MÁS VENDIDO');
        }
        localizedBadge = { ...plan.badge, text: badgeText };
      }

      // Localize duration
      const months = plan.sku.includes('12') || plan.sku.includes('0012') ? '12' : plan.sku.includes('3') || plan.sku.includes('0003') ? '3' : '1';
      const monthsNum = parseInt(months, 10) || 1;
      let localizedDuration = '';
      if (monthsNum === 1) {
        localizedDuration = t('1 Месяц', '1 Month', '1 Monat', '1 mois', '1 mes');
      } else if (monthsNum === 3) {
        localizedDuration = t('3 Месяца', '3 Months', '3 Monate', '3 mois', '3 meses');
      } else if (monthsNum === 12) {
        localizedDuration = t('12 Месяцев', '12 Months', '12 Monate', '12 mois', '12 meses');
      }

      // Localize title
      let localizedTitle = plan.title;
      if (plan.title.toLowerCase().includes('premium')) {
        if (monthsNum === 1) {
          localizedTitle = t('Premium — 1 месяц', 'Premium — 1 month', 'Premium — 1 Monat', 'Premium — 1 mois', 'Premium — 1 mes');
        } else if (monthsNum === 3) {
          localizedTitle = t('Premium — 3 месяца', 'Premium — 3 months', 'Premium — 3 Monate', 'Premium — 3 mois', 'Premium — 3 meses');
        } else if (monthsNum === 12) {
          localizedTitle = t('Premium — 12 месяцев', 'Premium — 12 months', 'Premium — 12 Monate', 'Premium — 12 mois', 'Premium — 12 meses');
        }
      }

      // Localize perMonth text (e.g. price/mo)
      const currency = plan.currency;
      const formattedPerMonth = formatPrice(plan.price / monthsNum, currency);
      let localizedPerMonth = `${formattedPerMonth}/${t('мес', 'mo', 'Mon.', 'mois', 'mes')}`;
      if (monthsNum === 3) {
        localizedPerMonth = `${formattedPerMonth}/${t('мес', 'mo', 'Mon.', 'mois', 'mes')} (-15%)`;
      } else if (monthsNum === 12) {
        localizedPerMonth = `${formattedPerMonth}/${t('мес', 'mo', 'Mon.', 'mois', 'mes')} (-30%)`;
      }

      return {
        ...plan,
        title: localizedTitle,
        duration: localizedDuration,
        perMonth: localizedPerMonth,
        badge: localizedBadge,
      };
    });
  }, [catalogItems, isBackendConnected, t]);

  const handleAddToCart = (plan: SubPlan) => {
    addItem(plan.sku, plan.title, plan.price, 1, undefined, plan.currency);
    openCartDrawer();
  };

  const ScrollMouse = ({ target }: { target: string }) => (
    <button
      type="button"
      className="scroll-mouse"
      onClick={() => document.getElementById(target)?.scrollIntoView({ behavior: 'smooth' })}
      aria-label="Scroll down"
    >
      <span className="mouse-shape">
        <span className="mouse-wheel" />
      </span>
      <ChevronDown size={14} />
    </button>
  );

  return (
    <>
      {/* DESKTOP VIEW (≥1024px): Single Page with Header + Perks + Plans Grid */}
      <div className="desktop-only-view" style={{ position: 'relative', overflow: 'hidden' }}>
        <img src="/images/Story Realms-bg.jpg" alt="" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }} />

        <div style={{ position: 'relative', zIndex: 3, maxWidth: 1100, margin: '0 auto' }}>
          {/* Header */}
          <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 20 }}>
            <h1 className="page-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontWeight: 800 }}>
              <SparklesIcon size={34} color="var(--accent-pink)" />
              <span className="gradient-text">{t('Подписка Premium', 'Premium Subscription', 'Premium-Abonnement', 'Abonnement Premium', 'Suscripción Premium')}</span>
            </h1>
            <p className="page-subtitle" style={{ fontSize: '1.02rem', maxWidth: 640, margin: '8px auto 0' }}>
              {t(
                'Откройте полный доступ ко всем историям, озвучке персонажей и ультимативным романтическим сценариям.',
                'Unlock full access to all visual stories, character voiceovers, and ultimate romantic scenarios.'
              )}
            </p>
          </div>

          {/* Perks Summary Banner */}
          <div className="glass-card seq-item seq-delay-2" style={{ marginBottom: '24px', padding: '22px 28px' }}>
            <h2 style={{ fontSize: '1.25rem', fontWeight: 800, color: '#ffffff', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '10px' }}>
              <CrownIcon size={22} color="#ffaa00" />
              {t('Что дает Premium?', 'What does Premium offer?', 'Was bietet Premium?', 'Que propose Premium ?', '¿Qué ofrece Premium?')}
            </h2>
            <div className="cards-grid-3" style={{ gap: '18px' }}>
              <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                <div style={{ padding: '8px', borderRadius: '10px', background: 'rgba(255, 51, 102, 0.15)', color: 'var(--accent-pink)' }}>
                  <Lock size={18} />
                </div>
                <div>
                  <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.94rem' }}>
                    {t('Секретные ветки', 'Secret Branches', 'Geheime Pfade', 'Branches secrètes', 'Rutas secretas')}
                  </div>
                  <div style={{ fontSize: '0.84rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                    {t('Сценарии, недоступные в стандартной версии', 'Scenarios unavailable in standard mode')}
                  </div>
                </div>
              </div>

              <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                <div style={{ padding: '8px', borderRadius: '10px', background: 'rgba(0, 242, 254, 0.15)', color: 'var(--accent-cyan)' }}>
                  <Volume2 size={18} />
                </div>
                <div>
                  <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.94rem' }}>
                    {t('Озвучка диалогов', 'Full Voice Acting', 'Vollständige Vertonung', 'Doublage intégral', 'Doblaje completo')}
                  </div>
                  <div style={{ fontSize: '0.84rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                    {t('Чувственные голоса ваших любимых героев', 'Sensual character voice lines')}
                  </div>
                </div>
              </div>

              <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                <div style={{ padding: '8px', borderRadius: '10px', background: 'rgba(255, 170, 0, 0.15)', color: '#ffaa00' }}>
                  <Zap size={18} />
                </div>
                <div>
                  <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.94rem' }}>
                    {t('Ежедневные бонусы', 'Daily Bonuses', 'Tägliche Boni', 'Bonus quotidiens', 'Bonus diarios')}
                  </div>
                  <div style={{ fontSize: '0.84rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                    {t('Алмазы за каждый день захода в играх', 'Extra diamonds every day')}
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Offline Notice if applicable */}
          {isBackendConnected === false && (
            <div
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '8px',
                padding: '6px 14px',
                borderRadius: 12,
                marginBottom: 16,
                background: 'rgba(255, 170, 0, 0.12)',
                border: '1px solid rgba(255, 170, 0, 0.3)',
                fontSize: '0.82rem',
                color: '#ffaa00',
              }}
            >
              <AlertCircle size={14} />
              <span>{t('Сервер каталога недоступен. Показаны ознакомительные тарифы.', 'Showing preview plans.')}</span>
            </div>
          )}

          {/* Plans Grid */}
          <div
            style={{
              display: 'grid',
              gridTemplateColumns: plans.length === 1 ? '1fr' : 'repeat(auto-fit, minmax(260px, 1fr))',
              gap: '20px',
              maxWidth: plans.length === 1 ? '380px' : '900px',
              margin: '0 auto',
              width: '100%',
            }}
          >
            {loading ? (
              <SkeletonCard count={1} />
            ) : plans.map((plan) => (
              <div
                key={plan.sku}
                className="glass-card"
                style={{
                  display: 'flex',
                  flexDirection: 'column',
                  justifyContent: 'space-between',
                  padding: '24px 20px',
                  minHeight: '320px',
                  border: plan.badge ? '1px solid var(--accent-pink)' : undefined,
                  boxShadow: plan.badge ? '0 10px 30px rgba(255, 51, 102, 0.25)' : undefined,
                }}
              >
                <div>
                  <div style={{ height: '22px', width: '100%', display: 'flex', justifyContent: 'flex-end', marginBottom: '4px' }}>
                    {plan.badge && <span className={`badge-tag badge-${plan.badge.kind}`}>{plan.badge.text}</span>}
                  </div>

                  <div style={{ fontSize: '1.35rem', fontWeight: 900, color: '#ffffff', marginBottom: '6px', textAlign: 'center' }}>
                    {plan.duration}
                  </div>

                  <div style={{ display: 'flex', alignItems: 'baseline', flexWrap: 'wrap', gap: '6px', margin: '12px 0 6px', justifyContent: 'center' }}>
                    <span style={{ fontSize: 'clamp(1.3rem, 2vw, 1.7rem)', fontWeight: 900, color: '#ffffff', whiteSpace: 'nowrap' }}>
                      {formatPrice(plan.price, plan.currency)}
                    </span>
                    <span style={{ fontSize: '0.85rem', color: 'var(--text-dim)', whiteSpace: 'nowrap' }}>/ {plan.duration}</span>
                  </div>

                  <div style={{ fontSize: '0.84rem', color: 'var(--accent-cyan)', fontWeight: 700, marginBottom: '18px', textAlign: 'center' }}>
                    {plan.perMonth}
                  </div>

                  <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', marginBottom: '22px', textAlign: 'left' }}>
                    {plan.features.map((feat, idx) => (
                      <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.86rem', color: 'var(--text-muted)' }}>
                        <CheckCircle2 size={15} color="var(--accent-pink)" />
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
                  <ShoppingCart size={16} color="#ffffff" />
                  <span>{t('Выбрать тариф', 'Select Plan', 'Plan wählen', 'Choisir l\'offre', 'Elegir plan')}</span>
                </button>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* MOBILE VIEW (<1024px): 2 Snapping Sections */}
      <div className="mobile-only-snap sub-snap-container">
        {/* SECTION 1 — HERO & BENEFITS */}
        <section
          className="showcase-section"
          id="sub-hero-section"
          style={{
            position: 'relative',
            minHeight: '100vh',
            height: '100vh',
            width: '100%',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            alignItems: 'center',
            padding: '90px 5% 40px',
            scrollSnapAlign: 'start',
            scrollSnapStop: 'always',
            overflow: 'hidden',
          }}
        >
          <img src="/images/Story Realms-bg.jpg" alt="" className="showcase-bg" />
          <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }} />

          <div style={{ position: 'relative', zIndex: 3, width: '100%', maxWidth: 760, margin: '0 auto', textAlign: 'center' }}>
            {/* PAGE HEADER */}
            <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 20 }}>
              <h1 className="page-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontWeight: 800 }}>
                <SparklesIcon size={32} color="var(--accent-pink)" />
                <span className="gradient-text">{t('Подписка Premium', 'Premium Subscription', 'Premium-Abonnement', 'Abonnement Premium', 'Suscripción Premium')}</span>
              </h1>
              <p className="page-subtitle" style={{ fontSize: '0.98rem', maxWidth: 580, margin: '8px auto 0' }}>
                {t(
                  'Откройте полный доступ ко всем историям, озвучке персонажей и ультимативным романтическим сценариям.',
                  'Unlock full access to all visual stories, character voiceovers, and ultimate romantic scenarios.'
                )}
              </p>
            </div>

            {/* PERKS SUMMARY BANNER */}
            <div className="glass-card seq-item seq-delay-2" style={{ marginBottom: '20px', padding: '24px 20px', textAlign: 'left' }}>
              <h2 style={{ fontSize: '1.25rem', fontWeight: 800, color: '#ffffff', marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '10px' }}>
                <CrownIcon size={22} color="#ffaa00" />
                {t('Что дает Premium?', 'What does Premium offer?', 'Was bietet Premium?', 'Que propose Premium ?', '¿Qué ofrece Premium?')}
              </h2>
              <div className="cards-grid-3" style={{ gap: '14px' }}>
                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                  <div style={{ padding: '8px', borderRadius: '10px', background: 'rgba(255, 51, 102, 0.15)', color: 'var(--accent-pink)' }}>
                    <Lock size={18} />
                  </div>
                  <div>
                    <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.92rem' }}>
                      {t('Секретные ветки', 'Secret Branches', 'Geheime Pfade', 'Branches secrètes', 'Rutas secretas')}
                    </div>
                    <div style={{ fontSize: '0.82rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                      {t('Сценарии, недоступные в стандартной версии', 'Scenarios unavailable in standard mode')}
                    </div>
                  </div>
                </div>

                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                  <div style={{ padding: '8px', borderRadius: '10px', background: 'rgba(0, 242, 254, 0.15)', color: 'var(--accent-cyan)' }}>
                    <Volume2 size={18} />
                  </div>
                  <div>
                    <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.92rem' }}>
                      {t('Озвучка диалогов', 'Full Voice Acting', 'Vollständige Vertonung', 'Doublage intégral', 'Doblaje completo')}
                    </div>
                    <div style={{ fontSize: '0.82rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                      {t('Чувственные голоса ваших любимых героев', 'Sensual character voice lines')}
                    </div>
                  </div>
                </div>

                <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-start' }}>
                  <div style={{ padding: '8px', borderRadius: '10px', background: 'rgba(255, 170, 0, 0.15)', color: '#ffaa00' }}>
                    <Zap size={18} />
                  </div>
                  <div>
                    <div style={{ fontWeight: 700, color: '#fff', fontSize: '0.92rem' }}>
                      {t('Ежедневные бонусы', 'Daily Bonuses', 'Tägliche Boni', 'Bonus quotidiens', 'Bonus diarios')}
                    </div>
                    <div style={{ fontSize: '0.82rem', color: 'var(--text-muted)', marginTop: '2px' }}>
                      {t('Алмазы за каждый день захода в играх', 'Extra diamonds every day')}
                    </div>
                  </div>
                </div>
              </div>
            </div>

            <div className="seq-item seq-delay-3" style={{ textAlign: 'center' }}>
              <button
                type="button"
                className="btn btn-primary"
                style={{ borderRadius: '24px', padding: '10px 24px', fontWeight: 700 }}
                onClick={() => document.getElementById('sub-plans-section')?.scrollIntoView({ behavior: 'smooth' })}
              >
                <ShoppingCart size={16} />
                <span>{t('Смотреть тарифы', 'View Plans', 'Tarife ansehen', 'Voir les offres', 'Ver planes')}</span>
              </button>
            </div>
          </div>

          <ScrollMouse target="sub-plans-section" />
        </section>

        {/* SECTION 2 — PLANS */}
        <section
          className="showcase-section"
          id="sub-plans-section"
          style={{
            position: 'relative',
            minHeight: '100vh',
            height: '100vh',
            width: '100%',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
            alignItems: 'center',
            padding: '90px 5% 40px',
            scrollSnapAlign: 'start',
            scrollSnapStop: 'always',
            overflow: 'hidden',
          }}
        >
          <img src="/images/Story Realms-scene1.jpg" alt="" className="showcase-bg" />
          <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }} />

          <div style={{ position: 'relative', zIndex: 3, width: '100%', maxWidth: 860, margin: '0 auto', textAlign: 'center' }}>
            <div style={{ textAlign: 'center', marginBottom: 20 }}>
              <h2 style={{ fontSize: '1.6rem', fontWeight: 900, color: '#fff', marginBottom: 6 }}>
                {t('Выберите Тариф Подписки', 'Select Subscription Plan')}
              </h2>
              <p style={{ fontSize: '0.95rem', color: 'var(--text-muted)' }}>
                {t('Активация происходит сразу после подтверждения оплаты.', 'Activation occurs immediately after payment.')}
              </p>
            </div>

            {/* Offline Notice if applicable */}
            {isBackendConnected === false && (
              <div
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '8px',
                  padding: '6px 14px',
                  borderRadius: 12,
                  marginBottom: 16,
                  background: 'rgba(255, 170, 0, 0.12)',
                  border: '1px solid rgba(255, 170, 0, 0.3)',
                  fontSize: '0.82rem',
                  color: '#ffaa00',
                }}
              >
                <AlertCircle size={14} />
                <span>{t('Сервер каталога недоступен. Показаны ознакомительные тарифы.', 'Showing preview plans.')}</span>
              </div>
            )}

            {/* SUBSCRIPTION PLANS GRID */}
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: plans.length === 1 ? '1fr' : 'repeat(auto-fit, minmax(260px, 1fr))',
                gap: '16px',
                maxWidth: plans.length === 1 ? '380px' : '760px',
                margin: '0 auto',
                width: '100%',
              }}
            >
              {loading ? (
                <SkeletonCard count={1} />
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
                      padding: '24px 20px',
                      minHeight: '340px',
                      border: plan.badge ? '1px solid var(--accent-pink)' : undefined,
                      boxShadow: plan.badge ? '0 10px 30px rgba(255, 51, 102, 0.25)' : undefined,
                    }}
                  >
                    <div>
                      <div style={{ height: '22px', width: '100%', display: 'flex', justifyContent: 'flex-end', marginBottom: '4px' }}>
                        {plan.badge && <span className={`badge-tag badge-${plan.badge.kind}`}>{plan.badge.text}</span>}
                      </div>

                      <div style={{ fontSize: '1.3rem', fontWeight: 900, color: '#ffffff', marginBottom: '6px' }}>
                        {plan.duration}
                      </div>

                      <div style={{ display: 'flex', alignItems: 'baseline', flexWrap: 'wrap', gap: '6px', margin: '12px 0 6px', justifyContent: 'center' }}>
                        <span style={{ fontSize: 'clamp(1.3rem, 2vw, 1.7rem)', fontWeight: 900, color: '#ffffff', whiteSpace: 'nowrap' }}>
                          {formatPrice(plan.price, plan.currency)}
                        </span>
                        <span style={{ fontSize: '0.85rem', color: 'var(--text-dim)', whiteSpace: 'nowrap' }}>/ {plan.duration}</span>
                      </div>

                      <div style={{ fontSize: '0.84rem', color: 'var(--accent-cyan)', fontWeight: 700, marginBottom: '18px' }}>
                        {plan.perMonth}
                      </div>

                      <div style={{ display: 'flex', flexDirection: 'column', gap: '10px', marginBottom: '22px', textAlign: 'left' }}>
                        {plan.features.map((feat, idx) => (
                          <div key={idx} style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.86rem', color: 'var(--text-muted)' }}>
                            <CheckCircle2 size={15} color="var(--accent-pink)" />
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
                      <ShoppingCart size={16} color="#ffffff" />
                      <span>{t('Выбрать тариф', 'Select Plan', 'Plan wählen', 'Choisir l\'offre', 'Elegir plan')}</span>
                    </button>
                  </div>
                ))
              )}
            </div>
          </div>
        </section>
      </div>
    </>
  );
};
