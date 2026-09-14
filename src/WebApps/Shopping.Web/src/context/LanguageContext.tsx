import React, { createContext, useContext, useState } from 'react';
import { LanguageCode } from '../types';

interface LanguageContextType {
  language: LanguageCode;
  setLanguage: (lang: LanguageCode) => void;
  t: (ru: string, en?: string, de?: string, fr?: string, es?: string) => string;
}

const LanguageContext = createContext<LanguageContextType | undefined>(undefined);

const LOCAL_STORAGE_KEY = 'gameshop_lang';

export const LanguageProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [language, setLanguageState] = useState<LanguageCode>(() => {
    const saved = localStorage.getItem(LOCAL_STORAGE_KEY) as LanguageCode;
    return saved && ['RU', 'EN', 'DE', 'FR', 'ES'].includes(saved) ? saved : 'RU';
  });

  const setLanguage = (lang: LanguageCode) => {
    setLanguageState(lang);
    localStorage.setItem(LOCAL_STORAGE_KEY, lang);
  };

  const t = (ru: string, en?: string, de?: string, fr?: string, es?: string): string => {
    const fallbackEn = en || ru;
    switch (language) {
      case 'RU':
        return ru;
      case 'EN':
        return fallbackEn;
      case 'DE':
        return de || fallbackEn;
      case 'FR':
        return fr || fallbackEn;
      case 'ES':
        return es || fallbackEn;
      default:
        return fallbackEn;
    }
  };

  return (
    <LanguageContext.Provider value={{ language, setLanguage, t }}>
      {children}
    </LanguageContext.Provider>
  );
};

export const useLanguage = (): LanguageContextType => {
  const context = useContext(LanguageContext);
  if (!context) {
    throw new Error('useLanguage must be used within a LanguageProvider');
  }
  return context;
};
