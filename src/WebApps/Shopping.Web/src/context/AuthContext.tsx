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
      const saved = localStorage.getItem(AUTH_STORAGE_KEY);
      if (saved) {
        const parsed = JSON.parse(saved);
        if (parsed.playerId) setPlayerId(parsed.playerId);
        if (parsed.playerName) setPlayerName(parsed.playerName);
        if (parsed.playerEmail) setPlayerEmail(parsed.playerEmail);
      }
    } catch (e) {
      console.error('Failed to parse auth storage', e);
    }
  }, []);

  useEffect(() => {
    try {
      localStorage.setItem(
        AUTH_STORAGE_KEY,
        JSON.stringify({ playerId, playerName, playerEmail })
      );
    } catch (e) {
      console.error('Failed to save auth storage', e);
    }
  }, [playerId, playerName, playerEmail]);

  useEffect(() => {
    const searchParams = new URLSearchParams(window.location.search);
    const ticketParam = searchParams.get('ticket') || searchParams.get('auth_ticket') || searchParams.get('code');
    const regionParam = searchParams.get('region');
    const storeParam = searchParams.get('store') || searchParams.get('store_channel');
    const versionParam = searchParams.get('version') || searchParams.get('game_version');

    // Query params give an immediate first guess; the backend verifier then resolves the
    // authoritative region/store/version for this player (per the player-context requirement).
    if (regionParam) setRegion(regionParam);
    if (storeParam) setStoreChannel(storeParam);
    if (versionParam) setGameVersion(versionParam);

    const resolve = async () => {
      // Ticket deeplink → consume via backend (authoritative region/store/version from Supabase).
      if (ticketParam) {
        const ctx = await claimTicket(ticketParam);
        if (ctx?.isValid) {
          setPlayerId(ctx.userId);
          if (!ctx.accessToken) return;
          setAccessToken(ctx.accessToken);
          window.history.replaceState({}, document.title, window.location.pathname);
          if (ctx.region) setRegion(ctx.region);
          if (ctx.store) setStoreChannel(ctx.store);
          if (ctx.gameVersion) setGameVersion(ctx.gameVersion);
          setDeeplinkToastMessage(`Signed in! Player ID: ${ctx.userId}`);
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
