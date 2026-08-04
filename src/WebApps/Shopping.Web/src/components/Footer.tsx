import React from 'react';
import { useLanguage } from '../context/LanguageContext';

export const Footer: React.FC = () => {
  const { t } = useLanguage();
  const year = new Date().getFullYear();

  return (
    <footer className="footer">
      <div className="footer-content">
        <img src="/images/logo.svg" alt="GameShop" className="footer-logo" />
        <a
          href="mailto:support@example.invalid"
          style={{ color: 'var(--text-muted)', textDecoration: 'none' }}
        >
          support@example.invalid
        </a>
        <div style={{ display: 'flex', gap: 24, fontSize: '0.88rem', flexWrap: 'wrap', justifyContent: 'center' }}>
          <a href="#privacy" style={{ color: 'var(--text-muted)', textDecoration: 'none' }}>
            {t('Конфиденциальность', 'Privacy Policy', 'Datenschutz', 'Confidentialité', 'Privacidad')}
          </a>
          <span>•</span>
          <a href="#terms" style={{ color: 'var(--text-muted)', textDecoration: 'none' }}>
            {t('Условия', 'Terms of Service', 'AGB', 'Conditions', 'Términos')}
          </a>
        </div>
        <p>© {year} Example Game Studio. {t('Все права защищены.', 'All rights reserved.', 'Alle Rechte vorbehalten.', 'Tous droits réservés.', 'Todos los derechos reservados.')}</p>
      </div>
    </footer>
  );
};
