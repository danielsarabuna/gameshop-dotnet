import React, { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react';
import { claimTicket, resolvePlayer, setAccessToken, PlayerContext } from '../services/api';

export type AuthStatus = 'initializing' | 'anonymous' | 'resolving' | 'game-session' | 'recipient-session' | 'error';

interface AuthContextType {
  playerId: string;
  playerName: string;
  region: string;
  storeChannel: string;
  gameVersion: string;
  deliveryContractVersion: number;
  authStatus: AuthStatus;
  authErrorCode: string | null;
  authErrorMessage: string | null;
  resolvePlayerId: (playerId: string) => Promise<PlayerContext | null>;
  confirmRecipient: (context: PlayerContext) => void;
  retryDeeplink: () => Promise<void>;
  setRegion: (value: string) => void;
  setStoreChannel: (value: string) => void;
  setGameVersion: (value: string) => void;
  loginModalOpen: boolean;
  openLoginModal: () => void;
  closeLoginModal: () => void;
  profileModalOpen: boolean;
  openProfileModal: () => void;
  closeProfileModal: () => void;
  deeplinkToastMessage: string | null;
  dismissDeeplinkToast: () => void;
  dismissAuthError: () => void;
  clearSession: () => void;
}

interface StoredSession {
  accessToken: string;
  expiresAtUtc?: string;
  playerId: string;
  playerName?: string;
  region?: string;
  storeChannel?: string;
  gameVersion?: string;
  deliveryContractVersion?: number;
  sessionKind?: 'game' | 'recipient';
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);
const SESSION_KEY = 'gameshop_webshop_session';
const LEGACY_KEYS = ['gameshop_player_session', 'gameshop_webshop_ticket_session'];

const readTicketParam = () => {
  const params = new URLSearchParams(window.location.search);
  return params.get('ticket') || params.get('auth_ticket') || params.get('code');
};

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [playerId, setPlayerId] = useState('');
  const [playerName, setPlayerName] = useState('');
  const [region, setRegion] = useState('global');
  const [storeChannel, setStoreChannel] = useState('global');
  const [gameVersion, setGameVersion] = useState('');
  const [deliveryContractVersion, setDeliveryContractVersion] = useState(1);
  const [authStatus, setAuthStatus] = useState<AuthStatus>('initializing');
  const [authErrorCode, setAuthErrorCode] = useState<string | null>(null);
  const [authErrorMessage, setAuthErrorMessage] = useState<string | null>(null);
  const [loginModalOpen, setLoginModalOpen] = useState(false);
  const [profileModalOpen, setProfileModalOpen] = useState(false);
  const [deeplinkToastMessage, setDeeplinkToastMessage] = useState<string | null>(null);
  const ticketRef = useRef<string | null>(null);
  const claimStarted = useRef(false);
  const lastSessionStatus = useRef<'anonymous' | 'game-session' | 'recipient-session'>('anonymous');

  const clearErrors = () => {
    setAuthErrorCode(null);
    setAuthErrorMessage(null);
  };

  const applySession = useCallback((ctx: PlayerContext, kind: 'game' | 'recipient') => {
    if (!ctx.accessToken) return false;
    setAccessToken(ctx.accessToken);
    setPlayerId(ctx.userId);
    setPlayerName(ctx.playerName || '');
    setRegion(ctx.region || 'global');
    setStoreChannel(ctx.store || 'global');
    setGameVersion(ctx.gameVersion || 'global');
    setDeliveryContractVersion(ctx.deliveryContractVersion ?? 1);
    setAuthStatus(kind === 'game' ? 'game-session' : 'recipient-session');
    lastSessionStatus.current = kind === 'game' ? 'game-session' : 'recipient-session';
    setAuthErrorCode(null);
    setAuthErrorMessage(null);
    sessionStorage.setItem(SESSION_KEY, JSON.stringify({
      accessToken: ctx.accessToken,
      expiresAtUtc: ctx.expiresAtUtc,
      playerId: ctx.userId,
      playerName: ctx.playerName,
      region: ctx.region,
      storeChannel: ctx.store,
      gameVersion: ctx.gameVersion,
      deliveryContractVersion: ctx.deliveryContractVersion ?? 1,
      sessionKind: kind,
    } satisfies StoredSession));
    return true;
  }, []);

  const claimDeeplink = useCallback(async (ticket: string) => {
    setAuthStatus('resolving');
    clearErrors();
    const ctx = await claimTicket(ticket);
    if (ctx?.isValid && applySession(ctx, 'game')) {
      setDeeplinkToastMessage(ctx.playerName ? `С возвращением, ${ctx.playerName}!` : 'Вход через игру выполнен');
      window.setTimeout(() => setDeeplinkToastMessage(null), 5000);
      return;
    }
    setAuthStatus('error');
    setAuthErrorCode(ctx?.errorCode || 'ticket_failed');
    setAuthErrorMessage(ctx?.errorMessage || 'Не удалось подтвердить вход через игру.');
  }, [applySession]);

  useEffect(() => {
    if (claimStarted.current) return;
    claimStarted.current = true;
    LEGACY_KEYS.forEach((key) => {
      localStorage.removeItem(key);
      sessionStorage.removeItem(key);
    });

    const ticket = readTicketParam();
    if (ticket) {
      ticketRef.current = ticket;
      window.history.replaceState({}, document.title, `${window.location.pathname}${window.location.hash}`);
      void claimDeeplink(ticket);
      return;
    }

    try {
      const saved = sessionStorage.getItem(SESSION_KEY);
      if (!saved) {
        setAuthStatus('anonymous');
        return;
      }
      const parsed = JSON.parse(saved) as StoredSession;
      if (!parsed.accessToken || (parsed.expiresAtUtc && Date.parse(parsed.expiresAtUtc) <= Date.now())) {
        sessionStorage.removeItem(SESSION_KEY);
        setAuthStatus('anonymous');
        return;
      }
      applySession({
        isValid: true,
        userId: parsed.playerId,
        playerName: parsed.playerName,
        region: parsed.region || 'global',
        store: parsed.storeChannel || 'global',
        gameVersion: parsed.gameVersion || 'global',
        deliveryContractVersion: parsed.deliveryContractVersion ?? 1,
        accessToken: parsed.accessToken,
        expiresAtUtc: parsed.expiresAtUtc,
        sessionKind: parsed.sessionKind,
      }, parsed.sessionKind || 'game');
    } catch {
      sessionStorage.removeItem(SESSION_KEY);
      setAuthStatus('anonymous');
    }
  }, [applySession, claimDeeplink]);

  const resolvePlayerId = useCallback(async (rawPlayerId: string) => {
    setAuthStatus('resolving');
    clearErrors();
    const ctx = await resolvePlayer(rawPlayerId.trim());
    if (ctx?.isValid && ctx.accessToken) {
      setAuthStatus(lastSessionStatus.current);
      return ctx;
    }
    setAuthStatus('error');
    setAuthErrorCode(ctx?.errorCode || 'lookup_unavailable');
    setAuthErrorMessage(ctx?.errorMessage || 'Не удалось найти игрока.');
    return null;
  }, []);

  const confirmRecipient = useCallback((ctx: PlayerContext) => {
    if (!applySession(ctx, 'recipient')) return;
    setDeeplinkToastMessage(ctx.playerName ? `Получатель: ${ctx.playerName}` : 'Получатель подтверждён');
    window.setTimeout(() => setDeeplinkToastMessage(null), 5000);
  }, [applySession]);

  const retryDeeplink = useCallback(async () => {
    if (ticketRef.current) await claimDeeplink(ticketRef.current);
    else setLoginModalOpen(true);
  }, [claimDeeplink]);

  const clearSession = useCallback(() => {
    setPlayerId('');
    setPlayerName('');
    setRegion('global');
    setStoreChannel('global');
    setGameVersion('');
    setDeliveryContractVersion(1);
    setAccessToken('');
    setAuthStatus('anonymous');
    lastSessionStatus.current = 'anonymous';
    clearErrors();
    setProfileModalOpen(false);
    setLoginModalOpen(false);
    sessionStorage.removeItem(SESSION_KEY);
  }, []);

  useEffect(() => {
    window.addEventListener('webshop-auth-expired', clearSession);
    return () => window.removeEventListener('webshop-auth-expired', clearSession);
  }, [clearSession]);

  return (
    <AuthContext.Provider value={{
      playerId,
      playerName,
      region,
      storeChannel,
      gameVersion,
      deliveryContractVersion,
      authStatus,
      authErrorCode,
      authErrorMessage,
      resolvePlayerId,
      confirmRecipient,
      retryDeeplink,
      setRegion,
      setStoreChannel,
      setGameVersion,
      loginModalOpen,
      openLoginModal: () => { setLoginModalOpen(true); setProfileModalOpen(false); clearErrors(); },
      closeLoginModal: () => {
        setLoginModalOpen(false);
        if (authStatus === 'error') setAuthStatus(lastSessionStatus.current);
        clearErrors();
      },
      profileModalOpen,
      openProfileModal: () => { setProfileModalOpen(true); setLoginModalOpen(false); },
      closeProfileModal: () => setProfileModalOpen(false),
      deeplinkToastMessage,
      dismissDeeplinkToast: () => setDeeplinkToastMessage(null),
      dismissAuthError: clearErrors,
      clearSession,
    }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) throw new Error('useAuth must be used within an AuthProvider');
  return context;
};
