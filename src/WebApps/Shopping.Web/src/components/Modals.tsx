import React, { FormEvent, useEffect, useRef, useState } from 'react';
import type { PlayerContext } from '../services/api';
import { useAuth } from '../context/AuthContext';
import { useLanguage } from '../context/LanguageContext';
import { CrownIcon } from './Icons';
import { AlertCircle, Check, ChevronDown, LoaderCircle, LogOut, RefreshCw, User, X } from 'lucide-react';

export const useDialogA11y = (open: boolean, close: () => void, ref: React.RefObject<HTMLElement | null>) => {
  useEffect(() => {
    if (!open) return;
    const previousFocus = document.activeElement as HTMLElement | null;
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    window.setTimeout(() => ref.current?.querySelector<HTMLElement>('input, button')?.focus(), 0);

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') close();
      if (event.key !== 'Tab' || !ref.current) return;
      const focusable = Array.from(ref.current.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), summary'));
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    };
    window.addEventListener('keydown', onKeyDown);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener('keydown', onKeyDown);
      previousFocus?.focus();
    };
  }, [close, open, ref]);
};

export const LoginModal: React.FC = () => {
  const {
    loginModalOpen,
    closeLoginModal,
    playerId,
    authStatus,
    authErrorCode,
    resolvePlayerId,
    confirmRecipient,
  } = useAuth();
  const { t } = useLanguage();
  const [input, setInput] = useState('');
  const [preview, setPreview] = useState<PlayerContext | null>(null);
  const cardRef = useRef<HTMLDivElement>(null);
  useDialogA11y(loginModalOpen, closeLoginModal, cardRef);

  useEffect(() => {
    if (!loginModalOpen) return;
    setInput(playerId);
    setPreview(null);
  }, [loginModalOpen]);

  if (!loginModalOpen) return null;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (preview) {
      confirmRecipient(preview);
      closeLoginModal();
      return;
    }
    setPreview(await resolvePlayerId(input));
  };

  const errorText = authErrorCode === 'context_missing'
    ? t('Откройте игру один раз, чтобы синхронизировать профиль с магазином.', 'Open the game once to sync this profile with the shop.')
    : authErrorCode
      ? t('Не удалось подтвердить игрока. Проверьте ID и попробуйте ещё раз.', 'Could not confirm the player. Check the ID and try again.')
      : null;

  return (
    <div className="modal-overlay" onMouseDown={(event) => event.target === event.currentTarget && closeLoginModal()}>
      <div ref={cardRef} className="modal-card auth-modal" role="dialog" aria-modal="true" aria-labelledby="login-title">
        <header className="modal-header">
          <h2 id="login-title" className="modal-title"><User size={20} />{t('Получатель покупки', 'Purchase recipient')}</h2>
          <button type="button" className="cart-close" aria-label={t('Закрыть', 'Close')} onClick={closeLoginModal}><X size={17} /></button>
        </header>

        {preview ? (
          <form onSubmit={submit} className="auth-form">
            <div className="recipient-preview recipient-preview-success">
              <span className="recipient-avatar"><Check size={20} /></span>
              <span><strong>{preview.playerName || t('Игрок найден', 'Player found')}</strong><small>{t('Регион', 'Region')}: {preview.region}</small></span>
            </div>
            <p className="modal-copy">{t('Покупка будет начислена этому игровому профилю.', 'The purchase will be delivered to this game profile.')}</p>
            <button className="btn btn-primary" type="submit">{t('Подтвердить', 'Confirm')}</button>
          </form>
        ) : (
          <form onSubmit={submit} className="auth-form">
            <p className="modal-copy">{t('Скопируйте Player ID в игре. Имя, регион и каталог подтянутся автоматически.', 'Copy the Player ID in the game. Name, region, and catalog will load automatically.')}</p>
            <label className="field-label" htmlFor="player-id">Player ID</label>
            <input
              id="player-id"
              className="cart-input"
              value={input}
              onChange={(event) => setInput(event.target.value)}
              placeholder="00000000-0000-0000-0000-000000000000"
              autoComplete="off"
              spellCheck={false}
              aria-describedby={errorText ? 'player-id-error' : undefined}
            />
            {errorText && <div id="player-id-error" className="inline-alert error" role="alert"><AlertCircle size={16} />{errorText}</div>}
            <button className="btn btn-primary" type="submit" disabled={!input.trim() || authStatus === 'resolving'}>
              {authStatus === 'resolving' ? <><LoaderCircle className="spin" size={17} />{t('Проверяем…', 'Checking…')}</> : t('Найти игрока', 'Find player')}
            </button>
          </form>
        )}
      </div>
    </div>
  );
};

export const ProfileModal: React.FC = () => {
  const { profileModalOpen, closeProfileModal, playerId, playerName, region, storeChannel, gameVersion, authStatus, clearSession } = useAuth();
  const { t } = useLanguage();
  const cardRef = useRef<HTMLDivElement>(null);
  useDialogA11y(profileModalOpen, closeProfileModal, cardRef);
  if (!profileModalOpen) return null;
  const isRecipient = authStatus === 'recipient-session';

  return (
    <div className="modal-overlay" onMouseDown={(event) => event.target === event.currentTarget && closeProfileModal()}>
      <div ref={cardRef} className="modal-card profile-modal" role="dialog" aria-modal="true" aria-labelledby="profile-title">
        <header className="modal-header">
          <h2 id="profile-title" className="modal-title"><CrownIcon size={20} color="#ffaa00" />{t('Профиль игрока', 'Player profile')}</h2>
          <button type="button" className="cart-close" aria-label={t('Закрыть', 'Close')} onClick={closeProfileModal}><X size={17} /></button>
        </header>
        <div className="recipient-preview">
          <span className="recipient-avatar"><User size={20} /></span>
          <span><strong>{playerName || t('Игрок', 'Player')}</strong><small>{isRecipient ? t('Получатель покупки', 'Purchase recipient') : t('Подключено через игру', 'Connected through the game')}</small></span>
        </div>
        <div className="profile-region"><span>{t('Регион', 'Region')}</span><strong>{region}</strong></div>
        <details className="profile-details">
          <summary>{t('Технические данные', 'Technical details')} <ChevronDown size={15} /></summary>
          <dl><dt>Player ID</dt><dd>{playerId}</dd><dt>Store</dt><dd>{storeChannel}</dd><dt>Version</dt><dd>{gameVersion || 'global'}</dd></dl>
        </details>
        <div className="modal-actions">
          <button type="button" className="btn btn-ghost btn-sm" onClick={clearSession}><LogOut size={16} />{t('Сменить игрока', 'Change player')}</button>
          <button type="button" className="btn btn-primary btn-sm" onClick={closeProfileModal}>{t('Готово', 'Done')}</button>
        </div>
      </div>
    </div>
  );
};

export const AuthNotice: React.FC = () => {
  const { authStatus, authErrorCode, loginModalOpen, retryDeeplink, dismissAuthError } = useAuth();
  const { t } = useLanguage();
  if (loginModalOpen) return null;
  if (authStatus === 'resolving') return <div className="auth-notice" role="status"><LoaderCircle className="spin" size={18} />{t('Подключаем профиль игрока…', 'Connecting player profile…')}</div>;
  if (authStatus !== 'error') return null;
  const missing = authErrorCode === 'context_missing';
  return (
    <div className="auth-notice auth-notice-error" role="alert">
      <AlertCircle size={18} />
      <span>{missing ? t('Откройте игру один раз для синхронизации профиля.', 'Open the game once to sync the profile.') : t('Вход через игру не подтверждён.', 'Game sign-in could not be confirmed.')}</span>
      <button type="button" onClick={() => void retryDeeplink()}><RefreshCw size={15} />{t('Повторить', 'Retry')}</button>
      <button type="button" aria-label={t('Закрыть', 'Close')} onClick={dismissAuthError}><X size={15} /></button>
    </div>
  );
};

export const DeeplinkToast: React.FC = () => {
  const { deeplinkToastMessage, dismissDeeplinkToast } = useAuth();
  const { t } = useLanguage();
  if (!deeplinkToastMessage) return null;
  return <div className="deeplink-toast" role="status"><Check size={17} /><span>{deeplinkToastMessage}</span><button type="button" aria-label={t('Закрыть', 'Close')} onClick={dismissDeeplinkToast}><X size={16} /></button></div>;
};
