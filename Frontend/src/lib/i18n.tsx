import { createContext } from 'preact';
import { useContext, useEffect, useState } from 'preact/hooks';
import { ipc } from './ipc';

// Import all locales
import beBY from '../assets/locales/be-BY.json';
import deDE from '../assets/locales/de-DE.json';
import enUS from '../assets/locales/en-US.json';
import esES from '../assets/locales/es-ES.json';
import frFR from '../assets/locales/fr-FR.json';
import itIT from '../assets/locales/it-IT.json';
import jaJP from '../assets/locales/ja-JP.json';
import koKR from '../assets/locales/ko-KR.json';
import ptBR from '../assets/locales/pt-BR.json';
import ruRU from '../assets/locales/ru-RU.json';
import trTR from '../assets/locales/tr-TR.json';
import ukUA from '../assets/locales/uk-UA.json';
import zhCN from '../assets/locales/zh-CN.json';

const locales: Record<string, any> = {
  'be-BY': beBY,
  'de-DE': deDE,
  'en-US': enUS,
  'es-ES': esES,
  'fr-FR': frFR,
  'it-IT': itIT,
  'ja-JP': jaJP,
  'ko-KR': koKR,
  'pt-BR': ptBR,
  'ru-RU': ruRU,
  'tr-TR': trTR,
  'uk-UA': ukUA,
  'zh-CN': zhCN,
};

type I18nContextType = {
  language: string;
  setLanguage: (lang: string) => Promise<void>;
  t: (key: string, paramsOrFallback?: Record<string, string | number> | string) => string;
};

export const I18nContext = createContext<I18nContextType>({
  language: 'en-US',
  setLanguage: async () => {},
  t: (key) => key,
});

export function useTranslation() {
  const context = useContext(I18nContext);
  return {
    t: context.t,
    language: context.language,
    setLanguage: context.setLanguage,
    i18n: { language: context.language, changeLanguage: context.setLanguage },
  };
}

export function I18nProvider({ children }: { children: any }) {
  const [language, setLanguageState] = useState('en-US');

  useEffect(() => {
    ipc.i18n.current().then((lang) => {
      if (lang && locales[lang]) {
        setLanguageState(lang);
      }
    }).catch(console.error);
  }, []);

  const setLanguage = async (lang: string) => {
    if (locales[lang]) {
      setLanguageState(lang);
      await ipc.i18n.set(lang);
    }
  };

  const t = (key: string, paramsOrFallback?: Record<string, string | number> | string): string => {
    const params = typeof paramsOrFallback === 'object' ? paramsOrFallback : undefined;
    const fallback = typeof paramsOrFallback === 'string' ? paramsOrFallback : undefined;
    const keys = key.split('.');
    let value: any = locales[language] || locales['en-US'];
    
    for (const k of keys) {
      if (value && typeof value === 'object' && k in value) {
        value = value[k];
      } else {
        // Fallback to en-US
        let fallbackValue: any = locales['en-US'];
        for (const fk of keys) {
          if (fallbackValue && typeof fallbackValue === 'object' && fk in fallbackValue) {
            fallbackValue = fallbackValue[fk];
          } else {
            return fallback ?? key;
          }
        }
        value = fallbackValue;
        break;
      }
    }

    if (typeof value !== 'string') {
      return fallback ?? key;
    }

    if (params) {
      return Object.entries(params).reduce(
        (acc, [k, v]) => acc.replace(new RegExp(`{{${k}}}`, 'g'), String(v)),
        value
      );
    }

    return value;
  };

  return (
    <I18nContext.Provider value={{ language, setLanguage, t }}>
      {children}
    </I18nContext.Provider>
  );
}

/** Alias for backwards-compat with react-i18next migration */
export const useI18n = useTranslation;
