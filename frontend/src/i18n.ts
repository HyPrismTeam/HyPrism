import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import { Language } from './constants/enums';
import ru from './locales/ru.json';
import en from './locales/en.json';
import tr from './locales/tr.json';
import fr from './locales/fr.json';
import es from './locales/es.json';
import pt from './locales/pt.json';
import de from './locales/de.json';
import zh from './locales/zh.json';
import ja from './locales/ja.json';
import ko from './locales/ko.json';

const getSavedLanguage = (): string => {
    const saved = localStorage.getItem('i18nextLng');
    const supportedLanguages = Object.values(Language) as string[];

    if (saved && supportedLanguages.includes(saved)) {
        return saved;
    }
    return Language.ENGLISH;
};

i18n
    .use(initReactI18next)
    .init({
        resources: {
            [Language.ENGLISH]: {
                translation: en,
            },
            [Language.RUSSIAN]: {
                translation: ru,
            },
            [Language.TURKISH]: {
                translation: tr,
            },
            [Language.FRENCH]: {
                translation: fr,
            },
            [Language.SPANISH]: {
                translation: es,
            },
            [Language.PORTUGUESE]: {
                translation: pt,
            },
            [Language.GERMAN]: {
                translation: de,
            },
            [Language.CHINESE]: {
                translation: zh,
            },
            [Language.JAPANESE]: {
                translation: ja,
            },
            [Language.KOREAN]: {
                translation: ko,
            },
        },
        lng: getSavedLanguage(),
        fallbackLng: Language.ENGLISH,
        interpolation: {
            escapeValue: false,
        },
    });

i18n.on('languageChanged', (lng) => {
    localStorage.setItem('i18nextLng', lng);
});

export default i18n;
