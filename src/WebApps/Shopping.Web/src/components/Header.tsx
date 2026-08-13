import React, { useState, useEffect, useRef } from 'react';
import { NavLink, Link } from 'react-router-dom';
import { useLanguage } from '../context/LanguageContext';
import { useCart } from '../context/CartContext';
import { useAuth } from '../context/AuthContext';
import { LanguageCode } from '../types';
import { Globe, ChevronDown, ShoppingCart, Menu, X, User } from 'lucide-react';

export const Header: React.FC = () => {
  const { language, setLanguage, t } = useLanguage();
  const { count, cartBump, openCartDrawer } = useCart();
  const { playerId, playerName, openLoginModal, openProfileModal } = useAuth();

  const [langMenuOpen, setLangMenuOpen] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [scrolled, setScrolled] = useState(false);
  const langRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleScroll = () => setScrolled(window.scrollY > 40);
    window.addEventListener('scroll', handleScroll, { passive: true });
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setMobileNavOpen(false);
        setLangMenuOpen(false);
      }
    };
    const handleClickOutside = (e: MouseEvent) => {
      if (langRef.current && !langRef.current.contains(e.target as Node)) {
        setLangMenuOpen(false);
      }
    };
    if (mobileNavOpen || langMenuOpen) {
      window.addEventListener('keydown', handleKeyDown);
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => {
      window.removeEventListener('keydown', handleKeyDown);
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, [mobileNavOpen, langMenuOpen]);

  const languages: { code: LanguageCode; name: string; flag: string }[] = [
    { code: 'RU', name: 'Русский', flag: '🇷🇺' },
    { code: 'EN', name: 'English', flag: '🇬🇧' },
    { code: 'DE', name: 'Deutsch', flag: '🇩🇪' },
    { code: 'FR', name: 'Français', flag: '🇫🇷' },
    { code: 'ES', name: 'Español', flag: '🇪🇸' },
  ];

  const activeLang = languages.find((l) => l.code === language) || languages[0];
  const displayName = playerName || playerId || null;

  return (
    <header className={`header-wrapper ${scrolled ? 'glass' : ''}`}>
      <div className="header-container">
        {/* IMAGE LOGO (matches Website .logo-link / .logo-img) */}
        <Link to="/" className="logo-link">
          <img src="/images/logo.svg" alt="GameShop" className="logo-img" />
        </Link>

        {/* DESKTOP NAVIGATION TABS */}
        <nav className="nav-links" aria-label="Primary">
          <NavLink to="/" end className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            {t('ГЛАВНАЯ', 'HOME', 'START', 'ACCUEIL', 'INICIO')}
          </NavLink>
          <NavLink to="/projects" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            {t('ИГРА', 'GAME', 'SPIEL', 'JEU', 'JUEGO')}
          </NavLink>
          <NavLink to="/diamonds" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            {t('АЛМАЗЫ', 'DIAMONDS', 'DIAMANTEN', 'DIAMANTS', 'DIAMANTES')}
          </NavLink>
          <NavLink to="/subscription" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            {t('ПОДПИСКА', 'SUBSCRIPTION', 'ABO', 'ABONNEMENT', 'SUSCRIPCIÓN')}
          </NavLink>
          <NavLink to="/help" className={({ isActive }) => `nav-item ${isActive ? 'active' : ''}`}>
            {t('ПОМОЩЬ', 'HELP', 'HILFE', 'AIDE', 'AYUDA')}
          </NavLink>
        </nav>

        {/* RIGHT ACTION CONTROLS */}
        <div className="header-actions">
          {/* LANGUAGE SELECTOR PILL with flag + click-outside close */}
          <div ref={langRef} style={{ position: 'relative' }}>
            <button
              className="icon-btn lang-pill-btn"
              type="button"
              onClick={() => setLangMenuOpen((prev) => !prev)}
            >
              <Globe size={16} color="#ff3366" />
              <span style={{ fontSize: '1rem', lineHeight: 1 }}>{activeLang.flag}</span>
              <span className="lang-code-text">{language}</span>
              <ChevronDown size={14} color="rgba(255,255,255,0.7)" style={{ transition: 'transform 0.2s ease', transform: langMenuOpen ? 'rotate(180deg)' : 'none' }} />
            </button>

            {langMenuOpen && (
              <div className="lang-dropdown">
                {languages.map((l) => (
                  <button
                    key={l.code}
                    className={`lang-option ${language === l.code ? 'active' : ''}`}
                    type="button"
                    onClick={() => {
                      setLanguage(l.code);
                      setLangMenuOpen(false);
                    }}
                  >
                    <span style={{ fontSize: '1.1rem', lineHeight: 1 }}>{l.flag}</span>
                    <span style={{ fontWeight: 800, color: language === l.code ? '#ff3366' : '#fff' }}>
                      {l.code}
                    </span>
                    <span className="muted" style={{ fontSize: '0.8rem' }}>
                      {l.name}
                    </span>
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* AUTH / PROFILE BUTTON */}
          {displayName ? (
            <button
              className="icon-btn auth-pill-btn"
              type="button"
              onClick={openProfileModal}
            >
              <User size={16} color="#ff3366" />
              <span>{displayName}</span>
            </button>
          ) : (
            <button
              className="icon-btn auth-pill-btn"
              type="button"
              onClick={openLoginModal}
            >
              <User size={16} color="rgba(255,255,255,0.9)" />
              <span>{t('Войти', 'Sign in', 'Anmelden', 'Connexion', 'Iniciar sesión')}</span>
            </button>
          )}

          {/* CART BUTTON */}
          <button
            className={`icon-btn cart-square-btn ${cartBump ? 'bump' : ''}`}
            type="button"
            aria-label="Cart"
            onClick={openCartDrawer}
          >
            <ShoppingCart size={18} color="#ffffff" />
            {count > 0 && <span className="cart-badge">{count}</span>}
          </button>

          {/* HAMBURGER TOGGLE */}
          <button
            className="icon-btn hamburger-btn"
            type="button"
            aria-label="Menu"
            onClick={() => setMobileNavOpen(true)}
          >
            {mobileNavOpen ? <X size={18} color="#ffffff" /> : <Menu size={18} color="#ffffff" />}
          </button>
        </div>
      </div>

      {/* MOBILE NAV OVERLAY */}
      {mobileNavOpen && (
        <div className="mobile-menu-overlay" onClick={() => setMobileNavOpen(false)}>
          <button
            className="mobile-close-btn"
            type="button"
            aria-label="Close menu"
            onClick={() => setMobileNavOpen(false)}
          >
            <X size={24} color="#ffffff" />
          </button>
          <img src="/images/logo.svg" alt="GameShop" className="mobile-logo" />
          <ul className="mobile-nav-list">
            <li>
              <NavLink
                to="/"
                end
                className="mobile-nav-item"
                onClick={() => setMobileNavOpen(false)}
              >
                {t('ГЛАВНАЯ', 'HOME', 'START', 'ACCUEIL', 'INICIO')}
              </NavLink>
            </li>
            <li>
              <NavLink
                to="/projects"
                className="mobile-nav-item"
                onClick={() => setMobileNavOpen(false)}
              >
                {t('ИГРА', 'GAME', 'SPIEL', 'JEU', 'JUEGO')}
              </NavLink>
            </li>
            <li>
              <NavLink
                to="/diamonds"
                className="mobile-nav-item"
                onClick={() => setMobileNavOpen(false)}
              >
                {t('АЛМАЗЫ', 'DIAMONDS', 'DIAMANTEN', 'DIAMANTS', 'DIAMANTES')}
              </NavLink>
            </li>
            <li>
              <NavLink
                to="/subscription"
                className="mobile-nav-item"
                onClick={() => setMobileNavOpen(false)}
              >
                {t('ПОДПИСКА', 'SUBSCRIPTION', 'ABO', 'ABONNEMENT', 'SUSCRIPCIÓN')}
              </NavLink>
            </li>
            <li>
              <NavLink
                to="/help"
                className="mobile-nav-item"
                onClick={() => setMobileNavOpen(false)}
              >
                {t('ПОМОЩЬ', 'HELP', 'HILFE', 'AIDE', 'AYUDA')}
              </NavLink>
            </li>
          </ul>
          <div className="mobile-social-bar">
            <a href="https://t.me/example-game-studio" target="_blank" rel="noreferrer" className="social-icon-link" title="Telegram">
              <img src="/icons/telegram.svg" alt="Telegram" />
            </a>
            <a href="https://vk.com/example-game-studio" target="_blank" rel="noreferrer" className="social-icon-link" title="VK">
              <img src="/icons/vk.svg" alt="VK" />
            </a>
            <a href="https://youtube.com" target="_blank" rel="noreferrer" className="social-icon-link" title="YouTube">
              <img src="/icons/youtube.svg" alt="YouTube" />
            </a>
          </div>
        </div>
      )}
    </header>
  );
};
