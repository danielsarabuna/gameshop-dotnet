import React, { useState } from 'react';
import { Link } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { GamepadIcon, DiamondIcon, SparklesIcon } from '../components/Icons';
import { MapPin, Sparkles, Shirt, Volume2, Play, Heart, ChevronDown } from 'lucide-react';

interface GameLocation {
  id: string;
  title: string;
  subtitle: string;
  description: string;
  image: string;
  badge: string;
  atmosphere: string;
  characters: string;
}

const scrollToSection = (id: string) => {
  document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' });
};

export const Projects: React.FC = () => {
  const { t } = useLanguage();
  const [activeLocationId, setActiveLocationId] = useState('mansion');

  const locations: GameLocation[] = [
    {
      id: 'mansion',
      title: t('Старинный Особняк Графа', 'The Count\'s Manor'),
      subtitle: t('Локация • Таинственная романтика', 'Location • Mystery Romance'),
      description: t(
        'Величественный замок с мраморными лестницами и скрытыми комнатами. Здесь каждая тень хранит секреты прошлого, а бал-маскарад становится местом судьбоносной встречи.',
        'A majestic castle with marble stairs and hidden rooms. Every shadow holds secrets of the past where a masquerade ball becomes a fateful encounter.'
      ),
      image: '/images/Story Realms-scene1.jpg',
      badge: t('ЛЕГЕНДАРНАЯ ЛОКАЦИЯ', 'LEGENDARY LOCATION'),
      atmosphere: t('Мрачная готика & Роскошь', 'Dark Gothic & Luxury'),
      characters: 'Lucian, Elena, Victor',
    },
    {
      id: 'academy',
      title: t('Академия Светлых Чар', 'Academy of Arcane Arts'),
      subtitle: t('Локация • Магия и Интриги', 'Location • Magic & Intrigue'),
      description: t(
        'Витражные аудитории и древняя библиотека. Учеба по магии переплетается с тайными союзами и романтическими дуэлями под ночным небом.',
        'Stained glass lecture halls and ancient library. Magic studies intertwine with secret alliances and romantic duels under the night sky.'
      ),
      image: '/images/Story Realms-scene2.jpg',
      badge: t('ПОПУЛЯРНАЯ ЛОКАЦИЯ', 'POPULAR LOCATION'),
      atmosphere: t('Магический нео-барокко', 'Magical Neo-Baroque'),
      characters: 'Aria, Gabriel, Celeste',
    },
    {
      id: 'metropolis',
      title: t('Неоновый Мегаполис', 'Neon Metropolis Penthouse'),
      subtitle: t('Локация • Современная Драма', 'Location • Modern Drama'),
      description: t(
        'Панорамный пентхаус на 85 этаже с видом на огни ночного города. Закулисные интриги большого бизнеса, вечеринки и искушения высшего света.',
        'Panoramic 85th floor penthouse overlooking neon city lights. Behind-the-scenes business intrigue, high society parties, and temptations.'
      ),
      image: '/images/Story Realms-bg.jpg',
      badge: t('НОВАЯ ЛОКАЦИЯ', 'NEW LOCATION'),
      atmosphere: t('Кибер-шик & Роскошь', 'Cyber-chic & Glamour'),
      characters: 'Damian, Chloe, Julian',
    },
  ];

  const activeLocation = locations.find((l) => l.id === activeLocationId) || locations[0];

  const ScrollMouse = ({ target }: { target: string }) => (
    <button type="button" className="scroll-mouse" onClick={() => scrollToSection(target)} aria-label="Scroll down">
      <span className="mouse-shape">
        <span className="mouse-wheel" />
      </span>
      <ChevronDown size={14} />
    </button>
  );

  return (
    <div className="game-showcase-container">
      {/* ============ SECTION 1 — GAME HERO ============ */}
      <section className="showcase-section" id="section-1">
        <img src="/images/Story Realms-bg.jpg" alt="GameShop" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 2, pointerEvents: 'none' }} />
        <img src="/images/Story Realms-char.png" alt="" className="hero-char" />

        <div className="hero-content">
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
            <img src="/images/Story Realms-logo.svg" alt="GameShop" style={{ height: 46, width: 'auto', maxWidth: '100%' }} />
          </div>

          <p className="hero-desc seq-item seq-delay-2" style={{ fontSize: '1.05rem', lineHeight: 1.6 }}>
            {t(
              'GameShop — это коллекция романтических историй, в которых вы делаете выборы, влияя на судьбоносный сюжет.',
              'GameShop is a collection of romantic visual stories where your choices shape the storyline.'
            )}
          </p>

          <div className="seq-item seq-delay-3 store-badges">
            <img src="/badges/app-store.svg" alt="App Store" className="badge-img" />
            <img src="/badges/google-play.svg" alt="Google Play" className="badge-img" />
          </div>

          <div className="seq-item seq-delay-4">
            <Link to="/diamonds" className="btn btn-primary" style={{ letterSpacing: 1, fontWeight: 700, textTransform: 'uppercase', borderRadius: 28 }}>
              {t('ИГРАТЬ СЕЙЧАС', 'PLAY NOW')}
            </Link>
          </div>

          <div className="seq-item seq-delay-5" style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginTop: 6 }}>
            <a href="https://vk.com/example-game-studio" target="_blank" rel="noreferrer" className="social-icon-link" title="VK"><img src="/icons/vk.svg" alt="VK" /></a>
            <a href="https://t.me/example-game-studio" target="_blank" rel="noreferrer" className="social-icon-link" title="Telegram"><img src="/icons/telegram.svg" alt="Telegram" /></a>
            <a href="https://youtube.com" target="_blank" rel="noreferrer" className="social-icon-link" title="YouTube"><img src="/icons/youtube.svg" alt="YouTube" /></a>
          </div>
        </div>

        <ScrollMouse target="section-2" />
      </section>

      {/* ============ SECTION 2 — INTERACTIVE GAMEPLAY ============ */}
      <section className="showcase-section" id="section-2">
        <img src="/images/Story Realms-scene1.jpg" alt="" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 2, pointerEvents: 'none' }} />

        <div className="hero-content" style={{ maxWidth: 680 }}>
          <div className="seq-item seq-delay-1" style={{ display: 'flex', alignItems: 'center', gap: 10, color: 'var(--accent-pink)', fontWeight: 700, textTransform: 'uppercase', letterSpacing: 1, fontSize: '0.9rem' }}>
            <Play size={18} fill="currentColor" />
            {t('Интерактивный геймплей', 'Interactive Gameplay')}
          </div>
          <h2 className="seq-item seq-delay-2" style={{ fontSize: 'clamp(1.8rem, 4vw, 2.6rem)', fontWeight: 900, lineHeight: 1.15, color: '#fff' }}>
            {t('Каждый выбор меняет историю', 'Every choice changes the story')}
          </h2>
          <p className="seq-item seq-delay-3" style={{ fontSize: '1.02rem', lineHeight: 1.65, color: 'var(--text-muted)' }}>
            {t(
              'Реплики, решения и жесты — всё формирует симпатии героев и открывает секретные ветки. Управляйте развитием романа своими руками.',
              'Your lines, decisions, and gestures shape character affinities and unlock secret branches. Steer the romance with your own hands.'
            )}
          </p>

          {/* Gameplay preview card with REC badge */}
          <div className="seq-item seq-delay-4 glass-card" style={{ padding: 0, overflow: 'hidden', borderRadius: 16, border: '1px solid rgba(255,51,102,0.4)', boxShadow: '0 12px 30px rgba(255,51,102,0.2)' }}>
            <div style={{ position: 'relative' }}>
              <img src="/media/Story Realms-gameplay-choice.gif" alt="Gameplay preview" style={{ width: '100%', display: 'block', maxHeight: 340, objectFit: 'cover' }} />
              <span className="badge-tag badge-pink" style={{ position: 'absolute', top: 12, left: 12, display: 'inline-flex', alignItems: 'center', gap: 6 }}>
                <span style={{ width: 8, height: 8, borderRadius: '50%', background: '#ff3366', display: 'inline-block' }} />
                REC • GAMEPLAY PREVIEW
              </span>
            </div>
          </div>
        </div>

        <ScrollMouse target="section-3" />
      </section>

      {/* ============ SECTION 3 — GAME LOCATIONS (existing content) ============ */}
      <section className="showcase-section" id="section-3">
        <img src="/images/Story Realms-scene2.jpg" alt="" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 2, pointerEvents: 'none' }} />

        <div className="hero-content" style={{ maxWidth: 1180, width: '100%' }}>
          <div className="seq-item seq-delay-1" style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 18 }}>
            <MapPin size={24} color="var(--accent-cyan)" />
            <span style={{ fontSize: '1.3rem', fontWeight: 800, color: '#fff' }}>
              {t('Игровые Локации & Сцены', 'In-Game Locations & Scenes')}
            </span>
          </div>

          <div className="seq-item seq-delay-2" style={{ display: 'flex', gap: 10, flexWrap: 'wrap', marginBottom: 22 }}>
            {locations.map((loc) => (
              <button
                key={loc.id}
                type="button"
                className={`filter-btn ${activeLocationId === loc.id ? 'active' : ''}`}
                onClick={() => setActiveLocationId(loc.id)}
              >
                {loc.title}
              </button>
            ))}
          </div>

          {/* Active location showcase card */}
          <div
            className="seq-item seq-delay-3 glass-card location-card"
            style={{
              border: '1px solid var(--accent-cyan)',
              boxShadow: '0 15px 40px rgba(0,242,254,0.15)',
            }}
          >
            <div className="location-media" style={{ position: 'relative', minHeight: 320, overflow: 'hidden' }}>
              <img src={activeLocation.image} alt={activeLocation.title} style={{ width: '100%', height: '100%', objectFit: 'cover' }} />
              <div style={{ position: 'absolute', inset: 0, background: 'linear-gradient(to right, rgba(0,0,0,0.1), rgba(18,14,23,0.95))' }} />
              <div style={{ position: 'absolute', top: 20, left: 20 }}>
                <span className="badge-tag badge-cyan">{activeLocation.badge}</span>
              </div>
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center' }}>
              <div style={{ fontSize: '0.85rem', color: 'var(--accent-cyan)', fontWeight: 700, textTransform: 'uppercase', marginBottom: 8 }}>
                {activeLocation.subtitle}
              </div>
              <h3 style={{ fontSize: '1.6rem', fontWeight: 900, color: '#fff', marginBottom: 12 }}>
                {activeLocation.title}
              </h3>
              <p style={{ fontSize: '0.95rem', color: 'var(--text-muted)', lineHeight: 1.6, marginBottom: 20 }}>
                {activeLocation.description}
              </p>
              <div style={{ display: 'flex', gap: 24, flexWrap: 'wrap' }}>
                <div>
                  <div style={{ fontSize: '0.74rem', color: 'var(--text-dim)', textTransform: 'uppercase', fontWeight: 700 }}>
                    {t('Атмосфера', 'Atmosphere')}
                  </div>
                  <div style={{ fontSize: '0.9rem', color: '#fff', fontWeight: 600, marginTop: 2 }}>
                    {activeLocation.atmosphere}
                  </div>
                </div>
                <div>
                  <div style={{ fontSize: '0.74rem', color: 'var(--text-dim)', textTransform: 'uppercase', fontWeight: 700 }}>
                    {t('Персонажи', 'Characters')}
                  </div>
                  <div style={{ fontSize: '0.9rem', color: 'var(--accent-pink)', fontWeight: 700, marginTop: 2 }}>
                    {activeLocation.characters}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <ScrollMouse target="section-4" />
      </section>

      {/* ============ SECTION 4 — ROMANTIC QUOTE ============ */}
      <section className="showcase-section" id="section-4">
        <img src="/images/Story Realms-bg.jpg" alt="" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 2, pointerEvents: 'none' }} />

        <div className="hero-content" style={{ maxWidth: 620 }}>
          <div className="seq-item seq-delay-1" style={{ display: 'flex', alignItems: 'center', gap: 10, color: 'var(--accent-cyan)', fontWeight: 700, textTransform: 'uppercase', letterSpacing: 1, fontSize: '0.9rem' }}>
            {t('Глава первая', 'Chapter One')}
          </div>
          <h2 className="seq-item seq-delay-2" style={{ fontSize: 'clamp(1.8rem, 4vw, 2.6rem)', fontWeight: 900, lineHeight: 1.2, color: '#fff', fontFamily: 'var(--font-serif)', fontStyle: 'italic' }}>
            {t('«Любовь — это выбор, который мы делаем каждый день»', '"Love is a choice we make every day"')}
          </h2>

          <div className="seq-item seq-delay-3 glass-card" style={{ display: 'flex', gap: 16, alignItems: 'flex-start' }}>
            <Heart size={32} color="var(--accent-pink)" fill="currentColor" style={{ flexShrink: 0, marginTop: 4 }} />
            <div>
              <div style={{ fontSize: '0.78rem', color: 'var(--accent-pink)', fontWeight: 800, letterSpacing: 1.5, textTransform: 'uppercase', marginBottom: 6 }}>LUCIAN</div>
              <p style={{ fontSize: '1rem', lineHeight: 1.6, color: 'var(--text-muted)', fontStyle: 'italic' }}>
                {t(
                  '«В этом замке каждый маскарад скрывает правду. Но только твои глаза, Elena, видят меня настоящего...»',
                  '"In this manor every masquerade hides the truth. But only your eyes, Elena, see the real me..."'
                )}
              </p>
            </div>
          </div>
        </div>

        <ScrollMouse target="section-5" />
      </section>

      {/* ============ SECTION 5 — FINALE: MECHANICS + STORE + MINI-FOOTER ============ */}
      <section className="showcase-section" id="section-5" style={{ flexDirection: 'column', justifyContent: 'center', textAlign: 'center' }}>
        <img src="/images/Story Realms-scene1.jpg" alt="" className="showcase-bg" />
        <div className="vignette-overlay" style={{ position: 'absolute', inset: 0, zIndex: 2, pointerEvents: 'none' }} />

        <div style={{ position: 'relative', zIndex: 4, maxWidth: 1180, width: '100%', margin: '0 auto' }}>
          <div className="seq-item seq-delay-1" style={{ textAlign: 'center', marginBottom: 40 }}>
            <h2 className="gradient-text" style={{ fontSize: 'clamp(1.8rem, 5vw, 2.8rem)', fontWeight: 900, display: 'flex', alignItems: 'center', justifyContent: 'center', gap: 14 }}>
              <GamepadIcon size={36} color="var(--accent-pink)" />
              {t('Вселенная GameShop', 'GameShop Universe')}
            </h2>
          </div>

          {/* Mechanics — existing 3 cards */}
          <div className="cards-grid-3 seq-item seq-delay-2" style={{ textAlign: 'left' }}>
            <div className="glass-card">
              <div style={{ width: 48, height: 48, borderRadius: 14, background: 'rgba(255,51,102,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--accent-pink)', marginBottom: 16 }}>
                <Sparkles size={24} />
              </div>
              <h3 style={{ fontSize: '1.2rem', fontWeight: 800, color: '#fff', marginBottom: 8 }}>
                {t('Интерактивные Выборы', 'Interactive Choices')}
              </h3>
              <p style={{ fontSize: '0.9rem', color: 'var(--text-muted)', lineHeight: 1.55 }}>
                {t('Каждая реплика открывает секретные диалоги и влияет на симпатию фаворитов.', 'Every line unlocks secret branch scenes and affects affinities.')}
              </p>
            </div>

            <div className="glass-card">
              <div style={{ width: 48, height: 48, borderRadius: 14, background: 'rgba(0,242,254,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--accent-cyan)', marginBottom: 16 }}>
                <Shirt size={24} />
              </div>
              <h3 style={{ fontSize: '1.2rem', fontWeight: 800, color: '#fff', marginBottom: 8 }}>
                {t('Гардероб и Прически', 'Wardrobe & Styling')}
              </h3>
              <p style={{ fontSize: '0.9rem', color: 'var(--text-muted)', lineHeight: 1.55 }}>
                {t('Сотни нарядов, макияжей и аксессуаров для балов, свиданий и расследований.', 'Hundreds of outfits, makeup looks, and accessories.')}
              </p>
            </div>

            <div className="glass-card">
              <div style={{ width: 48, height: 48, borderRadius: 14, background: 'rgba(255,170,0,0.15)', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#ffaa00', marginBottom: 16 }}>
                <Volume2 size={24} />
              </div>
              <h3 style={{ fontSize: '1.2rem', fontWeight: 800, color: '#fff', marginBottom: 8 }}>
                {t('Живой Дубляж & OST', 'Voice Acting & OST')}
              </h3>
              <p style={{ fontSize: '0.9rem', color: 'var(--text-muted)', lineHeight: 1.55 }}>
                {t('Чувственная озвучка сцен и кинематографичный саундтрек для полного погружения.', 'Full voice lines and cinematic OST for complete immersion.')}
              </p>
            </div>
          </div>

          <div className="seq-item seq-delay-3 store-badges" style={{ justifyContent: 'center', marginTop: 36 }}>
            <img src="/badges/app-store.svg" alt="App Store" className="badge-img" />
            <img src="/badges/google-play.svg" alt="Google Play" className="badge-img" />
          </div>

          {/* Mini-footer (Footer is suppressed on /projects) */}
          <div className="seq-item seq-delay-4" style={{ marginTop: 48, paddingTop: 24, borderTop: '1px solid var(--border-color)', color: 'var(--text-dim)', fontSize: '0.85rem', display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 10 }}>
            <div style={{ display: 'flex', gap: 20 }}>
              <a href="#privacy" style={{ color: 'var(--text-dim)', textDecoration: 'none' }}>{t('Конфиденциальность', 'Privacy')}</a>
              <span>•</span>
              <a href="#terms" style={{ color: 'var(--text-dim)', textDecoration: 'none' }}>{t('Условия', 'Terms')}</a>
            </div>
            <span>© {new Date().getFullYear()} GameShop</span>
          </div>
        </div>
      </section>
    </div>
  );
};
