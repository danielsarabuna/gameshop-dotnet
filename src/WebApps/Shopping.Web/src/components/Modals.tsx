import React, { useEffect } from 'react';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import { CrownIcon } from './Icons';
import { X, User, LogOut, Check, Ticket } from 'lucide-react';

export const LoginModal: React.FC = () => {
  const {
    loginModalOpen,
    closeLoginModal,
    playerId,
    setPlayerId,
    playerName,
    setPlayerName,
    playerEmail,
    setPlayerEmail,
  } = useAuth();
  const { t } = useLanguage();

  useEffect(() => {
    if (!loginModalOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeLoginModal();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [loginModalOpen, closeLoginModal]);

  if (!loginModalOpen) return null;

  return (
    <div className="modal-overlay" onClick={closeLoginModal}>
      <div className="modal-card glass" onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
          <h3 style={{ fontSize: '1.25rem', fontWeight: 800, color: '#ffffff', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
            <User size={20} color="var(--accent-pink)" />
            {t('Быстрый вход / ID игрока', 'Quick Sign-In / Player ID', 'Schnellanmeldung / Spieler-ID', 'Connexion rapide / ID joueur', 'Inicio rápido / ID de jugador')}
          </h3>
          <button type="button" className="cart-close" onClick={closeLoginModal}>
            <X size={16} />
          </button>
        </div>

        <p className="muted" style={{ marginBottom: '16px', fontSize: '0.9rem', lineHeight: 1.5 }}>
          {t(
            'Укажите ваш Player ID и имя для автоматической привязки заказов к профилю.',
            'Enter your Player ID and name to automatically attach your orders.'
          )}
        </p>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '14px' }}>
          <div>
            <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '4px' }}>
              {t('ID игрока', 'Player ID', 'Spieler-ID', 'ID joueur', 'ID de jugador')} *
            </label>
            <input
              type="text"
              className="cart-input"
              value={playerId}
              onChange={(e) => setPlayerId(e.target.value)}
              placeholder="player_12345"
            />
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '4px' }}>
              {t('Имя', 'Name', 'Name', 'Nom', 'Nombre')}
            </label>
            <input
              type="text"
              className="cart-input"
              value={playerName}
              onChange={(e) => setPlayerName(e.target.value)}
              placeholder="Alex"
            />
          </div>

          <div>
            <label style={{ display: 'block', fontSize: '0.8rem', fontWeight: 700, color: 'var(--text-muted)', marginBottom: '4px' }}>
              Email ({t('необязательно', 'optional', 'optional', 'facultatif', 'opcional')})
            </label>
            <input
              type="email"
              className="cart-input"
              value={playerEmail}
              onChange={(e) => setPlayerEmail(e.target.value)}
              placeholder="player@example.com"
            />
          </div>
        </div>

        <div style={{ marginTop: '24px', display: 'flex', justifyContent: 'flex-end', gap: '10px' }}>
          <button type="button" className="btn btn-ghost btn-sm" onClick={closeLoginModal}>
            {t('Отмена', 'Cancel', 'Abbrechen', 'Annuler', 'Cancelar')}
          </button>
          <button type="button" className="btn btn-primary btn-sm" onClick={closeLoginModal}>
            {t('Сохранить', 'Save', 'Speichern', 'Enregistrer', 'Guardar')}
          </button>
        </div>
      </div>
    </div>
  );
};

export const ProfileModal: React.FC = () => {
  const {
    profileModalOpen,
    closeProfileModal,
    playerId,
    playerName,
    region,
    storeChannel,
    clearSession,
  } = useAuth();
  const { t } = useLanguage();

  useEffect(() => {
    if (!profileModalOpen) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') closeProfileModal();
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [profileModalOpen, closeProfileModal]);

  if (!profileModalOpen) return null;

  return (
    <div className="modal-overlay" onClick={closeProfileModal}>
      <div className="modal-card glass" onClick={(e) => e.stopPropagation()}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: '16px' }}>
          <h3 style={{ fontSize: '1.25rem', fontWeight: 800, color: '#ffffff', margin: 0, display: 'flex', alignItems: 'center', gap: '8px' }}>
            <CrownIcon size={20} color="#ffaa00" />
            {t('Профиль Игрока', 'Player Profile', 'Spielerprofil', 'Profil joueur', 'Perfil de jugador')}
          </h3>
          <button type="button" className="cart-close" onClick={closeProfileModal}>
            <X size={16} />
          </button>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '14px', background: 'rgba(0,0,0,0.3)', padding: '18px', borderRadius: '16px', border: '1px solid var(--border-color)', marginBottom: '20px' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, borderBottom: '1px solid rgba(255,255,255,0.08)', paddingBottom: '8px' }}>
            <span style={{ color: 'var(--text-muted)', fontSize: '0.88rem' }}>{t('Игрок:', 'Player:', 'Spieler:', 'Joueur :', 'Jugador:')}</span>
            <span style={{ fontWeight: 800, color: '#ffffff', textAlign: 'right' }}>{playerName || t('Имя не задано', 'Name not set')}</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 16, borderBottom: '1px solid rgba(255,255,255,0.08)', paddingBottom: '8px' }}>
            <span style={{ color: 'var(--text-muted)', fontSize: '0.78rem' }}>Player ID:</span>
            <code style={{ color: 'var(--text-dim)', fontSize: '0.72rem', overflowWrap: 'anywhere', textAlign: 'right' }}>{playerId || '—'}</code>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid rgba(255,255,255,0.08)', paddingBottom: '8px' }}>
            <span style={{ color: 'var(--text-muted)', fontSize: '0.88rem' }}>{t('Регион:', 'Region:', 'Region:', 'Région :', 'Región:')}</span>
            <span style={{ fontWeight: 700, color: '#ffffff' }}>{region}</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between', borderBottom: '1px solid rgba(255,255,255,0.08)', paddingBottom: '8px' }}>
            <span style={{ color: 'var(--text-muted)', fontSize: '0.88rem' }}>{t('Магазин / Store:', 'Store channel:', 'Shop-Kanal:', 'Canal boutique :', 'Canal de tienda:')}</span>
            <span style={{ fontWeight: 700, color: '#ffffff' }}>{storeChannel}</span>
          </div>
          <div style={{ display: 'flex', justifyContent: 'space-between' }}>
            <span style={{ color: 'var(--text-muted)', fontSize: '0.88rem' }}>{t('Статус:', 'Status:', 'Status:', 'Statut :', 'Estado:')}</span>
            <span style={{ fontWeight: 700, color: '#00f2fe', display: 'flex', alignItems: 'center', gap: '6px' }}>
              <><Check size={14} /> Авторизован</>
            </span>
          </div>
        </div>

        <div style={{ display: 'flex', justifyContent: 'space-between', gap: '10px' }}>
          <button type="button" className="btn btn-ghost btn-sm" onClick={clearSession}>
            <LogOut size={16} />
            {t('Выйти', 'Log Out', 'Abmelden', 'Déconnexion', 'Cerrar sesión')}
          </button>
          <button type="button" className="btn btn-primary btn-sm" onClick={closeProfileModal}>
            <Check size={16} />
            {t('Готово', 'Done', 'Fertig', 'Fait', 'Listo')}
          </button>
        </div>
      </div>
    </div>
  );
};

export const DeeplinkToast: React.FC = () => {
  const { deeplinkToastMessage, dismissDeeplinkToast } = useAuth();

  if (!deeplinkToastMessage) return null;

  return (
    <div
      className="deeplink-toast"
      style={{
        position: 'fixed',
        bottom: '24px',
        right: '24px',
        background: 'linear-gradient(135deg, #ff3366, #9933ff)',
        color: '#fff',
        padding: '12px 20px',
        borderRadius: '16px',
        boxShadow: '0 10px 30px rgba(0,0,0,0.5)',
        zIndex: 3000,
        display: 'flex',
        alignItems: 'center',
        gap: '12px',
      }}
    >
      <span>{deeplinkToastMessage}</span>
      <button type="button" style={{ background: 'none', border: 'none', color: '#fff', cursor: 'pointer' }} onClick={dismissDeeplinkToast}>
        <X size={16} />
      </button>
    </div>
  );
};
