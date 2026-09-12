import React, { createContext, useContext, useState, useEffect } from 'react';
import { claimTicket, setAccessToken } from '../services/api';

interface AuthContextType {
  playerId: string;
  setPlayerId: (id: string) => void;
  playerName: string;
  setPlayerName: (name: string) => void;
  playerEmail: string;
  setPlayerEmail: (email: string) => void;
  region: string;
  storeChannel: string;
  gameVersion: string;
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
  clearSession: () => void;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);
const AUTH_STORAGE_KEY = 'GameShop_player_session';
const TICKET_SESSION_KEY = 'GameShop_webshop_ticket_session';

const readTicketParam = () => {
  const params = new URLSearchParams(window.location.search);
  return params.get('ticket') || params.get('auth_ticket') || params.get('code');
};

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [playerId, setPlayerId] = useState('');
  const [playerName, setPlayerName] = useState('');
  const [playerEmail, setPlayerEmail] = useState('');
  // No deeplink context → Global config (backend resolves bucket/global/global).
  // Real values arrive from the game deeplink / ticket verification.
  const [region, setRegion] = useState('global');
  const [storeChannel, setStoreChannel] = useState('global');
  const [gameVersion, setGameVersion] = useState('');

  const [loginModalOpen, setLoginModalOpen] = useState(false);
  const [profileModalOpen, setProfileModalOpen] = useState(false);
  const [deeplinkToastMessage, setDeeplinkToastMessage] = useState<string | null>(null);

  useEffect(() => {
    try {
      const ticketParam = readTicketParam();
      // Remove identity data left by older builds that persisted it across browser sessions.
      localStorage.removeItem(AUTH_STORAGE_KEY);
      const saved = sessionStorage.getItem(AUTH_STORAGE_KEY);
      if (saved) {
        const parsed = JSON.parse(saved);
        // A fresh Unity ticket is authoritative. Never flash or reuse another
        // character's cached identity while the ticket is being claimed.
        if (!ticketParam && parsed.playerId) setPlayerId(parsed.playerId);
        if (!ticketParam && parsed.playerName) setPlayerName(parsed.playerName);
        if (parsed.playerEmail) setPlayerEmail(parsed.playerEmail);
      }

      const ticketSession = sessionStorage.getItem(TICKET_SESSION_KEY);
      if (ticketSession && !ticketParam) {
        const parsed = JSON.parse(ticketSession);
        if (parsed.accessToken && (!parsed.expiresAtUtc || Date.parse(parsed.expiresAtUtc) > Date.now())) {
          setAccessToken(parsed.accessToken);
          setPlayerId(parsed.playerId || '');
          setPlayerName(parsed.playerName || '');
          setRegion(parsed.region || 'global');
          setStoreChannel(parsed.storeChannel || 'global');
          setGameVersion(parsed.gameVersion || 'global');
        } else {
          sessionStorage.removeItem(TICKET_SESSION_KEY);
        }
      } else if (ticketParam) {
        setAccessToken('');
        sessionStorage.removeItem(TICKET_SESSION_KEY);
      }
    } catch (e) {
      console.error('Failed to parse auth storage', e);
    }
  }, []);

  useEffect(() => {
    try {
      sessionStorage.setItem(
        AUTH_STORAGE_KEY,
        JSON.stringify({ playerId, playerName, playerEmail })
      );
    } catch (e) {
      console.error('Failed to save auth storage', e);
    }
  }, [playerId, playerName, playerEmail]);

  useEffect(() => {
    const ticketParam = readTicketParam();
    const resolve = async () => {
      // Ticket deeplink → consume via backend (authoritative region/store/version from Supabase).
      if (ticketParam) {
        // Strip the bearer ticket and legacy player metadata from browser history
        // before the asynchronous claim can emit any outbound request.
        window.history.replaceState({}, document.title, `${window.location.pathname}${window.location.hash}`);
        const ctx = await claimTicket(ticketParam);
        if (ctx?.isValid) {
          setPlayerId(ctx.userId);
          setPlayerName(ctx.playerName || '');
          if (!ctx.accessToken) return;
          setAccessToken(ctx.accessToken);
          if (ctx.region) setRegion(ctx.region);
          if (ctx.store) setStoreChannel(ctx.store);
          if (ctx.gameVersion) setGameVersion(ctx.gameVersion);
          sessionStorage.setItem(TICKET_SESSION_KEY, JSON.stringify({
            accessToken: ctx.accessToken,
            expiresAtUtc: ctx.expiresAtUtc,
            playerId: ctx.userId,
            playerName: ctx.playerName,
            region: ctx.region,
            storeChannel: ctx.store,
            gameVersion: ctx.gameVersion,
          }));
          setDeeplinkToastMessage(ctx.playerName ? `С возвращением, ${ctx.playerName}!` : 'Вход выполнен');
          scheduleDismiss();
          return;
        }
      }
    };

    let timer: ReturnType<typeof setTimeout> | undefined;
    const scheduleDismiss = () => {
      timer = setTimeout(() => setDeeplinkToastMessage(null), 5000);
    };

    if (ticketParam) {
      resolve();
    }

    return () => {
      if (timer) clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const openLoginModal = () => {
    setLoginModalOpen(true);
    setProfileModalOpen(false);
  };

  const closeLoginModal = () => setLoginModalOpen(false);

  const openProfileModal = () => {
    setProfileModalOpen(true);
    setLoginModalOpen(false);
  };

  const closeProfileModal = () => setProfileModalOpen(false);

  const dismissDeeplinkToast = () => setDeeplinkToastMessage(null);

  const clearSession = () => {
    setPlayerId('');
    setPlayerName('');
    setPlayerEmail('');
    setAccessToken('');
    setProfileModalOpen(false);
    setLoginModalOpen(false);
    localStorage.removeItem(AUTH_STORAGE_KEY);
    sessionStorage.removeItem(AUTH_STORAGE_KEY);
    sessionStorage.removeItem(TICKET_SESSION_KEY);
  };

  useEffect(() => {
    window.addEventListener('webshop-auth-expired', clearSession);
    return () => window.removeEventListener('webshop-auth-expired', clearSession);
  });

  return (
    <AuthContext.Provider
      value={{
        playerId,
        setPlayerId,
        playerName,
        setPlayerName,
        playerEmail,
        setPlayerEmail,
    region,
    storeChannel,
    gameVersion,
    setRegion,
    setStoreChannel,
    setGameVersion,
        loginModalOpen,
        openLoginModal,
        closeLoginModal,
        profileModalOpen,
        openProfileModal,
        closeProfileModal,
        deeplinkToastMessage,
        dismissDeeplinkToast,
        clearSession,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
