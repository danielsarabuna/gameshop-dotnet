import React from 'react';
import { useLanguage } from '../context/LanguageContext';
import { DiamondIcon, SparklesIcon } from '../components/Icons';

export const About: React.FC = () => {
  const { t } = useLanguage();

  return (
    <div style={{ position: 'relative', width: '100%', minHeight: '100vh' }}>
      <img src="/images/story-bg.jpg" alt="" className="showcase-bg" />
      <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 1, pointerEvents: 'none' }} />
      <div className="page-content page-container" style={{ padding: '130px 6% 80px', maxWidth: 1000, margin: '0 auto' }}>
          <div className="page-header seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 40 }}>
            <h1 className="page-title">
              <span className="gradient-text">
                {t('Об игре GameShop', 'About GameShop', 'Über GameShop', 'À propos de GameShop', 'Acerca de GameShop')}
              </span>
            </h1>
            <p className="page-subtitle" style={{ fontSize: '1.05rem', margin: '12px auto 0' }}>
              {t(
                'Интерактивная романтическая новелла, где каждое принятое вами решение создаёт уникальную историю любви и приключений.',
                'An interactive romantic visual novel where every choice you make creates a unique love story.'
              )}
            </p>
          </div>

          <div className="glass-card seq-item seq-delay-2" style={{ marginBottom: 50, padding: 36 }}>
            <h2 className="serif-title" style={{ fontSize: '1.8rem', marginBottom: 20, color: 'var(--accent-pink)' }}>
              "{t(
                'Любовь — это выбор, который вы делаете каждый день',
                'Love is a choice you make every day'
              )}"
            </h2>
            <p style={{ fontSize: '1.05rem', lineHeight: 1.7, color: 'var(--text-muted)', marginBottom: 16 }}>
              {t(
                'GameShop — это погружение в захватывающие миры романтики, тайн и эмоций. Мы создаём сюжеты, которые трогают до глубины души, и даём игрокам полную свободу выбора.',
                'GameShop is an immersive journey into romance, secrets, and emotion. We craft stories that resonate deeply, giving players full freedom of choice.'
              )}
            </p>
          </div>

          <div className="cards-grid seq-item seq-delay-3">
            <div className="glass-card" style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
              <h3 style={{ fontSize: '1.35rem', color: '#ffffff', display: 'flex', alignItems: 'center', gap: 10 }}>
                <DiamondIcon size={22} color="var(--accent-pink)" />
                {t('Как купить алмазы', 'How to buy diamonds')}
              </h3>
              <p style={{ fontSize: '0.95rem', color: 'var(--text-muted)', lineHeight: 1.6 }}>
                {t(
                  'Перейдите на вкладку «Алмазы», выберите необходимый комплект, нажмите «В корзину», укажите ваш ID игрока и завершите оплату.',
                  'Go to the Diamonds tab, select a pack, click Add to Cart, enter your Player ID and complete payment.'
                )}
              </p>
            </div>

            <div className="glass-card" style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
              <h3 style={{ fontSize: '1.35rem', color: '#ffffff', display: 'flex', alignItems: 'center', gap: 10 }}>
                <SparklesIcon size={22} color="var(--accent-cyan)" />
                {t('Как активировать Premium', 'How to activate Premium')}
              </h3>
              <p style={{ fontSize: '0.95rem', color: 'var(--text-muted)', lineHeight: 1.6 }}>
                {t(
                  'Перейдите на вкладку «Подписка», выберите подходящий тариф (1, 3 или 12 месяцев) и добавьте его в корзину для мгновенной активации.',
                  'Go to the Subscription tab, select a plan (1, 3 or 12 months) and add to cart for instant activation.'
                )}
              </p>
            </div>
          </div>
      </div>
    </div>
  );
};
