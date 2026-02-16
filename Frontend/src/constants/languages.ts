import { Language } from './enums';

export interface LanguageMetadata {
    name: string;
    nativeName: string;
    code: Language;
    searchQuery: string;
}

export const LANGUAGE_CONFIG: Record<Language, LanguageMetadata> = {
    [Language.ENGLISH]: {
        name: 'English',
        nativeName: 'English',
        code: Language.ENGLISH,
        searchQuery: '',
    },
    [Language.RUSSIAN]: {
        name: 'Russian',
        nativeName: 'Русский',
        code: Language.RUSSIAN,
        searchQuery: 'Russian Translation (RU)',
    },
    [Language.TURKISH]: {
        name: 'Turkish',
        nativeName: 'Türkçe',
        code: Language.TURKISH,
        searchQuery: 'Türkçe çeviri',
    },
    [Language.FRENCH]: {
        name: 'French',
        nativeName: 'Français',
        code: Language.FRENCH,
        searchQuery: 'French Translation',
    },
    [Language.ITALIAN]: {
        name: 'Italian',
        nativeName: 'Italiano',
        code: Language.ITALIAN,
        searchQuery: 'Italian Translation',
    },
    [Language.SPANISH]: {
        name: 'Spanish',
        nativeName: 'Español',
        code: Language.SPANISH,
        searchQuery: 'Spanish Translation',
    },
    [Language.PORTUGUESE]: {
        name: 'Portuguese',
        nativeName: 'Português',
        code: Language.PORTUGUESE,
        searchQuery: 'Portuguese Translation',
    },
    [Language.GERMAN]: {
        name: 'German',
        nativeName: 'Deutsch',
        code: Language.GERMAN,
        searchQuery: 'German Translation',
    },
    [Language.CHINESE]: {
        name: 'Chinese',
        nativeName: '中文',
        code: Language.CHINESE,
        searchQuery: 'Chinese Translation',
    },
    [Language.JAPANESE]: {
        name: 'Japanese',
        nativeName: '日本語',
        code: Language.JAPANESE,
        searchQuery: 'Japanese Translation',
    },
    [Language.KOREAN]: {
        name: 'Korean',
        nativeName: '한국어',
        code: Language.KOREAN,
        searchQuery: 'Korean Translation',
    },
    [Language.UKRAINIAN]: {
        name: 'Ukrainian',
        nativeName: 'Українська',
        code: Language.UKRAINIAN,
        searchQuery: 'Ukrainian Translation (UA)',
    },
    [Language.BELARUSIAN]: {
        name: 'Belarusian',
        nativeName: 'Беларуская',
        code: Language.BELARUSIAN,
        searchQuery: 'Belarusian Translation (BE)',
    },
};
