import React, { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { getCatalogItems } from '../services/api';
import { SkeletonCard } from '../components/Skeleton';
import { DiamondIcon } from '../components/Icons';
import { ShoppingCart, RefreshCw, AlertCircle, ChevronDown } from 'lucide-react';
import { formatPrice } from '../utils/format';
import { getDiamondImageFallback } from '../utils/images';

interface DiamondPack {
  sku: string;
  title: string;
  amount: number;
  price: number;
  currency: string;
  badge?: { text: string; kind: 'pink' | 'gold' | 'cyan' };
  imageUrl: string;
  isLiveBackend?: boolean;
}

const FALLBACK_DIAMOND_PACKS: DiamondPack[] = [
  {
    sku: 'd1a00000-0000-0000-0000-000000000060',
    title: '60 Алмазов',
    amount: 60,
    price: 1.23,
    currency: 'EUR',
    imageUrl: '/images/diamonds_60.png',
  },
  {
    sku: 'd1a00000-0000-0000-0000-000000000150',
    title: '150 Алмазов',
    amount: 150,
    price: 4.99,
    currency: 'EUR',
    imageUrl: '/images/diamonds_120.png',
  },
  {
    sku: 'd1a00000-0000-0000-0000-000000000300',
    title: '300 Алмазов',
    amount: 300,
    price: 9.98,
    currency: 'EUR',
    badge: { text: 'ПОПУЛЯРНО', kind: 'pink' },
    imageUrl: '/images/diamonds_350.png',
  },
  {
    sku: 'd1a00000-0000-0000-0000-000000000450',
    title: '450 Алмазов',
    amount: 450,
    price: 14.97,
    currency: 'EUR',
    imageUrl: '/images/diamonds_350.png',
  },
  {
    sku: 'd1a00000-0000-0000-0000-000000000600',
    title: '600 Алмазов',
    amount: 600,
    price: 19.96,
    currency: 'EUR',
    badge: { text: 'ВЫГОДНО', kind: 'gold' },
    imageUrl: '/images/diamonds_800.png',
  },
  {
    sku: 'd1a00000-0000-0000-0000-000000001200',
    title: '1200 Алмазов',
    amount: 1200,
    price: 34.99,
    currency: 'EUR',
    badge: { text: 'ХИТ', kind: 'pink' },
    imageUrl: '/images/diamonds_2000.png',
  },
  {
    sku: 'd1a00000-0000-0000-0000-000000002500',
    title: '2500 Алмазов',
    amount: 2500,
    price: 69.99,
    currency: 'EUR',
    badge: { text: 'МЕГА ПАК', kind: 'gold' },
    imageUrl: '/images/diamonds_4500.png',
  },
  {
    sku: 'd1a00000-0000-0000-0000-000000009000',
    title: '9000 Алмазов',
    amount: 9000,
    price: 199.99,
    currency: 'EUR',
    badge: { text: 'ЛЕГЕНДА', kind: 'cyan' },
    imageUrl: '/images/diamonds_9000.png',
  },
];

export const Diamonds: React.FC = () => {
  const { t } = useLanguage();
  const { addItem, openCartDrawer, reconcile } = useCart();
  const { region, storeChannel, gameVersion } = useAuth();
  const [catalogItems, setCatalogItems] = useState<any[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [isBackendConnected, setIsBackendConnected] = useState<boolean | null>(null);

  const fetchCatalog = () => {
    setLoading(true);
    getCatalogItems(region, storeChannel, gameVersion).then((items) => {
      setLoading(false);
      if (items !== null) {
        setIsBackendConnected(true);
        const currencyItems = items.filter((i) => i.type === 'Currency');
        setCatalogItems(currencyItems);
        // Identity-driven context switch (e.g. player ID entered in cart):
        // re-price cart lines against the freshly loaded regional config.
        reconcile(items);
      } else {
        // Backend offline / unavailable
        setIsBackendConnected(false);
        setCatalogItems(null);
      }
    });
  };

  useEffect(() => {
    fetchCatalog();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [region, storeChannel, gameVersion]);

  const packs = React.useMemo<DiamondPack[]>(() => {
    const rawPacks = (isBackendConnected === false || catalogItems === null)
      ? FALLBACK_DIAMOND_PACKS
      : catalogItems.map((item) => {
          const amt = parseInt(item.metadata?.diamonds || '0', 10) || 100;
          return {
            sku: item.id,
            title: item.title,
            amount: amt,
            price: item.price,
            currency: item.currency || 'EUR',
            badge: item.metadata?.badgeKey === 'hot' ? { text: 'ПОПУЛЯРНО', kind: 'pink' as const } :
                   item.metadata?.badgeKey === 'best' ? { text: 'ВЫГОДНО', kind: 'gold' as const } :
                   item.metadata?.badgeKey === 'mega' ? { text: 'МЕГА ПАК', kind: 'gold' as const } : undefined,
            imageUrl: item.imageUrl || getDiamondImageFallback(amt),
            isLiveBackend: true,
          };
        });

    return rawPacks.map((pack) => {
      // Localize badge text
      let localizedBadge = pack.badge;
      if (pack.badge) {
        let badgeText = pack.badge.text;
        if (pack.badge.text === 'ПОПУЛЯРНО' || pack.badge.text === 'POPULAR') {
          badgeText = t('ПОПУЛЯРНО', 'POPULAR', 'BELIEBT', 'POPULAIRE', 'POPULAR');
        } else if (pack.badge.text === 'ВЫГОДНО' || pack.badge.text === 'VALUE') {
          badgeText = t('ВЫГОДНО', 'BEST VALUE', 'BESTES ANGEBOT', 'MEILLEURE OFFRE', 'MEJOR VALOR');
        } else if (pack.badge.text === 'ХИТ' || pack.badge.text === 'HIT') {
          badgeText = t('ХИТ', 'HIT', 'HIT', 'HIT', 'ÉXITO');
        } else if (pack.badge.text === 'МЕГА ПАК' || pack.badge.text === 'MEGA PACK') {
          badgeText = t('МЕГА ПАК', 'MEGA PACK', 'MEGA-PACK', 'MEGA PACK', 'MEGA PACK');
        } else if (pack.badge.text === 'ЛЕГЕНДА' || pack.badge.text === 'LEGEND') {
          badgeText = t('ЛЕГЕНДА', 'LEGEND', 'LEGENDE', 'LÉGENDE', 'LEYENDA');
        }
        localizedBadge = { ...pack.badge, text: badgeText };
      }

      // Backend titles already come from the selected regional catalog. Only
      // synthesize localized labels for the built-in offline preview offers.
      const amt = pack.amount;
      const localizedTitle = pack.isLiveBackend
        ? pack.title
        : t(`${amt} Алмазов`, `${amt} Diamonds`, `${amt} Diamanten`, `${amt} Diamants`, `${amt} Diamantes`);

      return {
        ...pack,
        title: localizedTitle,
        badge: localizedBadge,
      };
    });
  }, [catalogItems, isBackendConnected, t]);

  const handleAddToCart = (pack: DiamondPack) => {
    addItem(pack.sku, pack.title, pack.price, 1, pack.imageUrl, pack.currency, 'Currency');
    openCartDrawer();
  };

  const packChunks = React.useMemo(() => {
    const chunks: DiamondPack[][] = [];
    for (let i = 0; i < packs.length; i += 2) {
      chunks.push(packs.slice(i, i + 2));
    }
    return chunks.length > 0 ? chunks : [[]];
  }, [packs]);

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
      {/* DESKTOP VIEW (≥1024px): Single Page with all diamond cards */}
      <div className="desktop-only-view" style={{ position: 'relative', overflow: 'hidden' }}>
        <img src="/images/Story Realms-bg.jpg" alt="" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }} />

        <div style={{ position: 'relative', zIndex: 3, maxWidth: 1100, margin: '0 auto' }}>
          <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 24 }}>
            <h1 className="page-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontWeight: 800 }}>
              <DiamondIcon size={34} color="var(--accent-pink)" />
              <span className="gradient-text">{t('Алмазы', 'Diamonds', 'Diamanten', 'Diamants', 'Diamantes')}</span>
            </h1>
            <p className="page-subtitle" style={{ fontSize: '1.02rem', maxWidth: 600, margin: '8px auto 0' }}>
              {t(
                'Выбирайте наборы алмазов для открытия эксклюзивных выборов и нарядов в ваших любимых историях.',
                'Select diamond packs to unlock premium choices and outfits in your favorite visual stories.'
              )}
            </p>
          </div>

          {/* Offline notice if any */}
          {isBackendConnected === false && (
            <div
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '8px',
                padding: '6px 14px',
                borderRadius: 12,
                marginBottom: 20,
                background: 'rgba(255, 170, 0, 0.12)',
                border: '1px solid rgba(255, 170, 0, 0.3)',
                fontSize: '0.82rem',
                color: '#ffaa00',
              }}
            >
              <AlertCircle size={14} />
              <span>{t('Сервер каталога временно недоступен. Показаны офферы.', 'The catalog is temporarily unavailable. Showing preview offers.')}</span>
            </div>
          )}

          {/* All Cards Grid */}
          <div
            className="cards-grid-3"
            style={{
              display: 'grid',
              gridTemplateColumns: packs.length === 1 ? '1fr' : 'repeat(auto-fit, minmax(260px, 1fr))',
              gap: '20px',
              maxWidth: packs.length === 1 ? '380px' : '900px',
              margin: '0 auto',
              width: '100%',
            }}
          >
            {loading ? (
              <SkeletonCard count={2} />
            ) : packs.length === 0 ? (
              <div className="glass-card" style={{ gridColumn: '1 / -1', textAlign: 'center', padding: '48px 24px' }}>
                <AlertCircle size={40} color="var(--accent-pink)" style={{ marginBottom: '16px' }} />
                <h3 style={{ fontSize: '1.4rem', color: '#fff', marginBottom: '8px' }}>
                  {t('Каталог пуст', 'Catalog is empty')}
                </h3>
                <p className="muted" style={{ marginBottom: '20px' }}>
                  {t('В настоящий момент нет доступных предложений.', 'No offers available at the moment.')}
                </p>
                <button type="button" className="btn btn-primary" onClick={fetchCatalog}>
                  <RefreshCw size={16} />
                  {t('Повторить попытку', 'Retry loading')}
                </button>
              </div>
            ) : (
              packs.map((pack) => (
                <div
                  key={pack.sku}
                  className="glass-card"
                  style={{
                    display: 'flex',
                    flexDirection: 'column',
                    alignItems: 'center',
                    textAlign: 'center',
                    padding: '24px 20px',
                    position: 'relative',
                    border: pack.badge ? '1px solid var(--accent-pink)' : undefined,
                    boxShadow: pack.badge ? '0 10px 30px rgba(255, 51, 102, 0.25)' : undefined,
                  }}
                >
                  {/* Badge */}
                  <div style={{ height: '22px', width: '100%', display: 'flex', justifyContent: 'flex-end', marginBottom: '4px' }}>
                    {pack.badge && <span className={`badge-tag badge-${pack.badge.kind}`}>{pack.badge.text}</span>}
                  </div>

                  {/* Diamond Icon */}
                  <div style={{ margin: '6px 0 14px', height: '90px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                    <img
                      src={pack.imageUrl}
                      alt={pack.title}
                      style={{ maxHeight: '85px', maxWidth: '85px', objectFit: 'contain', filter: 'drop-shadow(0 6px 16px rgba(153, 51, 255, 0.4))' }}
                      onError={(e) => {
                        const target = e.currentTarget;
                        const fallback = getDiamondImageFallback(pack.amount);
                        if (!target.src.endsWith(fallback)) {
                          target.src = fallback;
                        } else {
                          target.onerror = null;
                        }
                      }}
                    />
                  </div>

                  <h3 style={{ fontSize: '1.25rem', fontWeight: 800, color: '#fff', marginBottom: '8px' }}>
                    {pack.title}
                  </h3>

                  <div style={{ fontSize: '1.45rem', fontWeight: 900, color: 'var(--accent-pink)', marginBottom: '16px', letterSpacing: '-0.3px' }}>
                    {formatPrice(pack.price, pack.currency)}
                  </div>

                  <button
                    type="button"
                    className="btn btn-primary"
                    style={{ width: '100%', borderRadius: '20px' }}
                    onClick={() => handleAddToCart(pack)}
                  >
                    <ShoppingCart size={16} color="#ffffff" />
                    <span>{t('В корзину', 'Add to Cart', 'In den Warenkorb', 'Au panier', 'Al carrito')}</span>
                  </button>
                </div>
              ))
            )}
          </div>
        </div>
      </div>

      {/* MOBILE VIEW (<1024px): Snapping sections in pairs */}
      <div className="mobile-only-snap diamonds-snap-container">
        {packChunks.map((chunk, idx) => (
          <section
            key={idx}
            className="showcase-section"
            id={`diamonds-section-${idx + 1}`}
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

            <div style={{ position: 'relative', zIndex: 3, width: '100%', maxWidth: 860, margin: '0 auto', textAlign: 'center' }}>
              {idx === 0 && (
                <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 20 }}>
                  <h1 className="page-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontWeight: 800 }}>
                    <DiamondIcon size={32} color="var(--accent-pink)" />
                    <span className="gradient-text">{t('Алмазы', 'Diamonds', 'Diamanten', 'Diamants', 'Diamantes')}</span>
                  </h1>
                  <p className="page-subtitle" style={{ fontSize: '0.98rem', maxWidth: 580, margin: '8px auto 0' }}>
                    {t(
                      'Выбирайте наборы алмазов для открытия эксклюзивных выборов и нарядов в ваших любимых историях.',
                      'Select diamond packs to unlock premium choices and outfits in your favorite visual stories.'
                    )}
                  </p>
                </div>
              )}

              {idx > 0 && (
                <div style={{ textAlign: 'center', marginBottom: 16 }}>
                  <h2 style={{ fontSize: '1.4rem', fontWeight: 800, color: '#fff' }}>
                    {t('Коллекция Алмазов', 'Diamond Collection')}
                  </h2>
                </div>
              )}

              {/* CARDS GRID (PAIRS PER SCREEN) */}
              <div
                style={{
                  display: 'grid',
                  gridTemplateColumns: chunk.length === 1 ? '1fr' : 'repeat(auto-fit, minmax(260px, 1fr))',
                  gap: '16px',
                  maxWidth: chunk.length === 1 ? '380px' : '720px',
                  margin: '0 auto',
                  width: '100%',
                }}
              >
                {loading ? (
                  <SkeletonCard count={2} />
                ) : chunk.map((pack) => (
                  <div
                    key={pack.sku}
                    className="glass-card"
                    style={{
                      display: 'flex',
                      flexDirection: 'column',
                      alignItems: 'center',
                      textAlign: 'center',
                      padding: '24px 20px',
                      position: 'relative',
                      border: pack.badge ? '1px solid var(--accent-pink)' : undefined,
                      boxShadow: pack.badge ? '0 10px 30px rgba(255, 51, 102, 0.25)' : undefined,
                    }}
                  >
                    {/* Badge */}
                    <div style={{ height: '22px', width: '100%', display: 'flex', justifyContent: 'flex-end', marginBottom: '4px' }}>
                      {pack.badge && <span className={`badge-tag badge-${pack.badge.kind}`}>{pack.badge.text}</span>}
                    </div>

                    {/* Diamond Icon */}
                    <div style={{ margin: '6px 0 14px', height: '90px', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
                      <img
                        src={pack.imageUrl}
                        alt={pack.title}
                        style={{ maxHeight: '85px', maxWidth: '85px', objectFit: 'contain', filter: 'drop-shadow(0 6px 16px rgba(153, 51, 255, 0.4))' }}
                        onError={(e) => {
                          const target = e.currentTarget;
                          const fallback = getDiamondImageFallback(pack.amount);
                          if (!target.src.endsWith(fallback)) {
                            target.src = fallback;
                          } else {
                            target.onerror = null;
                          }
                        }}
                      />
                    </div>

                    <h3 style={{ fontSize: '1.25rem', fontWeight: 800, color: '#fff', marginBottom: '8px' }}>
                      {pack.title}
                    </h3>

                    <div style={{ fontSize: '1.45rem', fontWeight: 900, color: 'var(--accent-pink)', marginBottom: '16px', letterSpacing: '-0.3px' }}>
                      {formatPrice(pack.price, pack.currency)}
                    </div>

                    <button
                      type="button"
                      className="btn btn-primary"
                      style={{ width: '100%', borderRadius: '20px' }}
                      onClick={() => handleAddToCart(pack)}
                    >
                      <ShoppingCart size={16} color="#ffffff" />
                      <span>{t('В корзину', 'Add to Cart', 'In den Warenkorb', 'Au panier', 'Al carrito')}</span>
                    </button>
                  </div>
                ))}
              </div>
            </div>

            {/* Scroll mouse indicator to next section if not last */}
            {idx < packChunks.length - 1 && (
              <ScrollMouse target={`diamonds-section-${idx + 2}`} />
            )}
          </section>
        ))}
      </div>
    </>
  );
};
