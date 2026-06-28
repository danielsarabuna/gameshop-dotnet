import React, { useEffect, useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { getCatalogItems } from '../services/api';
import { SkeletonCard } from '../components/Skeleton';
import { DiamondIcon } from '../components/Icons';
import { ShoppingCart, RefreshCw, AlertCircle } from 'lucide-react';
import { formatPrice } from '../utils/format';

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
  const { addItem, openCartDrawer } = useCart();
  const { region, storeChannel, gameVersion } = useAuth();
  const [packs, setPacks] = useState<DiamondPack[]>([]);
  const [loading, setLoading] = useState(true);
  const [isBackendConnected, setIsBackendConnected] = useState<boolean | null>(null);

  const fetchCatalog = () => {
    setLoading(true);
    getCatalogItems(region, storeChannel, gameVersion).then((items) => {
      setLoading(false);
      if (items && items.length > 0) {
        const currencyItems = items.filter((i) => i.type === 'Currency');
        if (currencyItems.length > 0) {
          setIsBackendConnected(true);
          const mapped: DiamondPack[] = currencyItems.map((item) => {
            let badge: DiamondPack['badge'] = undefined;
            if (item.metadata?.badgeKey === 'hot') badge = { text: 'ПОПУЛЯРНО', kind: 'pink' };
            if (item.metadata?.badgeKey === 'best') badge = { text: 'ВЫГОДНО', kind: 'gold' };
            if (item.metadata?.badgeKey === 'mega') badge = { text: 'МЕГА ПАК', kind: 'gold' };

            const amt = parseInt(item.metadata?.diamonds || '0', 10) || 100;
            // Prefer the backend-cached asset URL; fall back to local images only when absent.
            let img = item.imageUrl || `/images/diamonds_${amt}.png`;
            if (!item.imageUrl) {
              if (amt === 150) img = '/images/diamonds_120.png';
              if (amt === 300) img = '/images/diamonds_350.png';
              if (amt === 450) img = '/images/diamonds_350.png';
              if (amt === 600) img = '/images/diamonds_800.png';
              if (amt === 1200) img = '/images/diamonds_2000.png';
              if (amt === 2500) img = '/images/diamonds_4500.png';
            }

            return {
              sku: item.id,
              title: item.title,
              amount: amt,
              price: item.price,
              currency: item.currency || 'EUR',
              badge,
              imageUrl: img,
              isLiveBackend: true,
            };
          });
          setPacks(mapped);
          return;
        }
      }

      // If backend returns empty or unavailable
      setIsBackendConnected(false);
      setPacks(FALLBACK_DIAMOND_PACKS);
    });
  };

  useEffect(() => {
    fetchCatalog();
  }, []);

  const handleAddToCart = (pack: DiamondPack) => {
    addItem(pack.sku, pack.title, pack.price, 1, pack.imageUrl, pack.currency);
    openCartDrawer();
  };

  return (
    <div style={{ position: 'relative', width: '100%', minHeight: '100vh' }}>
      <img src="/images/Story Realms-bg.jpg" alt="" className="page-bg" />
      <div className="page-content page-container" style={{ padding: '130px 6% 80px', maxWidth: 1180, margin: '0 auto' }}>
        <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 24 }}>
          <h1 className="page-title" style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 12, fontSize: '2.4rem', fontWeight: 800 }}>
            <DiamondIcon size={36} color="var(--accent-pink)" />
            <span className="gradient-text">{t('Алмазы', 'Diamonds', 'Diamanten', 'Diamants', 'Diamantes')}</span>
          </h1>
          <p className="page-subtitle" style={{ fontSize: '1.05rem', maxWidth: 640, margin: '12px auto 0' }}>
            {t(
              'Выбирайте наборы алмазов для открытия эксклюзивных выборов и нарядов в ваших любимых историях.',
              'Select diamond packs to unlock premium choices and outfits in your favorite visual stories.'
            )}
          </p>
        </div>

          {/* OFFLINE PLACEHOLDER ONLY (no success banner — see issue requirement) */}
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
                marginBottom: 28,
                background: 'rgba(255, 170, 0, 0.12)',
                border: '1px solid rgba(255, 170, 0, 0.3)',
                fontSize: '0.86rem',
                color: '#ffaa00',
              }}
            >
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <AlertCircle size={16} />
                <span>
                  {t('Сервер каталога временно недоступен. Показаны ознакомительные офферы.', 'Catalog server temporarily offline. Showing preview offers.')}
                </span>
              </div>
              <button
                type="button"
                className="btn btn-ghost btn-sm"
                onClick={fetchCatalog}
                style={{ padding: '4px 10px', fontSize: '0.8rem', color: 'inherit' }}
              >
                <RefreshCw size={12} />
                {t('Обновить', 'Refresh')}
              </button>
            </div>
          )}

          {/* CARDS GRID / SKELETON */}
          <div className="cards-grid-3 seq-item seq-delay-2" style={{ minHeight: '740px' }}>
            {loading ? (
              <SkeletonCard count={6} />
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
                    justifyContent: 'space-between',
                    textAlign: 'center',
                    padding: '24px',
                    minHeight: '360px',
                    position: 'relative',
                  }}
                >
                  {/* BADGE CONTAINER */}
                  <div style={{ height: '24px', width: '100%', display: 'flex', justifyContent: 'flex-end', marginBottom: '4px' }}>
                    {pack.badge && (
                      <span className={`badge-tag badge-${pack.badge.kind}`}>{pack.badge.text}</span>
                    )}
                  </div>

                  {/* IMAGE */}
                  <div
                    style={{
                      width: '120px',
                      height: '120px',
                      margin: '8px 0 16px',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      filter: 'drop-shadow(0 10px 20px rgba(255, 51, 102, 0.35))',
                    }}
                  >
                    <img
                      src={pack.imageUrl}
                      alt={pack.title}
                      style={{ width: '100%', height: '100%', objectFit: 'contain' }}
                      onError={(e) => {
                        const target = e.currentTarget;
                        const primaryFallback = `/images/diamonds_${pack.amount}.png`;
                        if (!target.src.endsWith(primaryFallback) && !target.src.endsWith('/images/diamonds_60.png')) {
                          target.src = primaryFallback;
                        } else {
                          target.src = '/images/diamonds_60.png';
                        }
                      }}
                    />
                  </div>

                  {/* TITLE & PRICE */}
                  <h3 style={{ fontSize: '1.3rem', fontWeight: 800, color: '#ffffff', marginBottom: '4px' }}>
                    {pack.title}
                  </h3>

                  <div style={{ fontSize: '1.6rem', fontWeight: 900, color: 'var(--accent-pink)', marginBottom: '16px' }}>
                    {formatPrice(pack.price, pack.currency)}
                  </div>

                  {/* ADD TO CART BUTTON */}
                  <button
                    type="button"
                    className="btn btn-primary"
                    style={{ width: '100%', borderRadius: '20px', padding: '12px 16px' }}
                    onClick={() => handleAddToCart(pack)}
                  >
                    <ShoppingCart size={18} color="#ffffff" />
                    <span>{t('В корзину', 'Add to Cart', 'In den Warenkorb', 'Ajouter au panier', 'Añadir al carrito')}</span>
                  </button>
                </div>
              ))
            )}
          </div>
      </div>
    </div>
  );
};
