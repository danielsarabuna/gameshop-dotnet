import React from 'react';
import { Link } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';

export const Home: React.FC = () => {
  const { t } = useLanguage();

  return (
    <div className="hero-snap-container">
      <section className="hero-section" id="section-hero">
        <img src="/images/story-bg.jpg" alt="GameShop" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 2, pointerEvents: 'none' }} />

        {/* Floating character artwork (desktop ≥1024px only) */}
        <img src="/images/story-character.png" alt="" className="hero-char" />

        <div className="hero-content">
          {/* App Icon + Title Logo */}
          <div className="seq-item seq-delay-1" style={{ display: 'flex', alignItems: 'center', gap: 18, marginBottom: 4, flexWrap: 'wrap' }}>
            <img
              src="/images/logo.svg"
              alt="GameShop"
              style={{
                width: 72, height: 72, borderRadius: 18,
                boxShadow: '0 8px 20px rgba(0,0,0,0.6)',
                border: '1px solid rgba(255,255,255,0.2)',
                objectFit: 'contain', padding: 12,
                background: 'rgba(255,255,255,0.05)',
              }}
            />
            <img src="/images/story-title.svg" alt="GameShop" style={{ height: 46, width: 'auto', maxWidth: '100%' }} />
          </div>

          {/* Subtitle description */}
          <p className="hero-desc seq-item seq-delay-2" style={{ fontSize: '1.05rem', lineHeight: 1.6 }}>
            {t(
              'GameShop — это коллекция романтических историй, в которых вы можете делать выборы, тем самым влияя на сюжет.',
              'GameShop is a collection of romantic visual stories where your choices directly shape the storyline.',
              'GameShop ist eine Sammlung romantischer Geschichten, in denen deine Entscheidungen den Verlauf bestimmen.',
              'GameShop est une collection d\'histoires romantiques où vos choix façonnent directement le récit.',
              'GameShop es una colección de historias románticas donde tus decisiones moldean el argumento.'
            )}
          </p>

          {/* Pink Primary Button */}
          <div className="seq-item seq-delay-3">
            <Link
              className="btn btn-primary"
              to="/projects"
              style={{ letterSpacing: 1, fontWeight: 700, textTransform: 'uppercase', borderRadius: 28 }}
            >
              {t('ПОДРОБНЕЕ', 'MORE DETAILS', 'MEHR ERFAHREN', 'EN SAVOIR PLUS', 'MÁS INFORMACIÓN')}
            </Link>
          </div>

          {/* Repository link */}
          <div className="seq-item seq-delay-4" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
            <span style={{ fontSize: '0.88rem', color: 'var(--text-muted)', fontWeight: 600 }}>
              {t('Исходный код:', 'Source code:', 'Quellcode:', 'Code source :', 'Código fuente:')}
            </span>
            <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
              <a href="https://github.com/danielsarabuna/web-shop" target="_blank" rel="noreferrer" className="social-icon-link" title="GitHub">
                <img src="/icons/github.svg" alt="GitHub" />
              </a>
            </div>
          </div>
        </div>
      </section>
    </div>
  );
};
