import React, { useState, useRef, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { Check } from 'lucide-react';
import { GameBranch, Language } from '../constants/enums';
import { LANGUAGE_CONFIG } from '../constants/languages';
import { ipc } from '@/lib/ipc';
import { useAccentColor } from '../contexts/AccentColorContext';
import { changeLanguage } from '../i18n';

interface LanguageSelectorProps {
    currentBranch?: string;
    currentVersion?: number;
    onShowModManager?: (query?: string) => void;
}

export const LanguageSelector: React.FC<LanguageSelectorProps> = ({
    currentBranch = GameBranch.RELEASE,
    currentVersion = 0,
    onShowModManager
}) => {
    const { i18n, t } = useTranslation();
    const { accentColor, accentTextColor } = useAccentColor();
    const [isOpen, setIsOpen] = useState(false);
    const dropdownRef = useRef<HTMLDivElement>(null);
    const [showConfirm, setShowConfirm] = useState<{ langName: string; langCode: string; searchQuery: string } | null>(null);
    const [dontAskAgain, setDontAskAgain] = useState(false);

    const handleLanguageSelect = async (langCode: Language) => {
        setIsOpen(false);

        try {
            await changeLanguage(langCode);
            console.log(`Language set to: ${langCode}`);
        } catch (error) {
            console.warn('Failed to change language:', error);
        }

        if (localStorage.getItem('suppressTranslationPrompt') === 'true') {
            return;
        }

        if (langCode !== Language.ENGLISH && onShowModManager) {
            const langConfig = LANGUAGE_CONFIG[langCode];
            if (langConfig) {
                try {
                    const installedMods = await ipc.mods.list(); // TODO: pass branch/version when IPC supports it
                    const hasTranslationMod = installedMods?.some((mod: any) =>
                        mod.name.toLowerCase().includes(langConfig.searchQuery.toLowerCase())
                    );

                    if (!hasTranslationMod) {
                        setDontAskAgain(false);
                        setShowConfirm({
                            langName: langConfig.nativeName,
                            langCode,
                            searchQuery: langConfig.searchQuery
                        });
                    }
                } catch (err) {
                    console.error('Failed to check installed mods:', err);
                }
            }
        }
    };

    const handleConfirmInstall = () => {
        if (showConfirm && onShowModManager) {
            onShowModManager(showConfirm.searchQuery);
            setShowConfirm(null);
        }
    };

    const handleIgnore = () => {
        if (dontAskAgain) {
            localStorage.setItem('suppressTranslationPrompt', 'true');
        }
        setShowConfirm(null);
    };

    useEffect(() => {
        const handleClickOutside = (event: MouseEvent) => {
            if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
                setIsOpen(false);
            }
        };

        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                setIsOpen(false);
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        document.addEventListener('keydown', handleEscape);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
            document.removeEventListener('keydown', handleEscape);
        };
    }, []);

    return (
        <>
            <div ref={dropdownRef} className="relative">
                <button
                    onClick={() => setIsOpen(!isOpen)}
                    className="w-12 h-12 rounded-xl glass border border-white/5 flex items-center justify-center text-white/60 active:scale-95 transition-all duration-150 relative group"
                    title={t('languageSelector.changeLanguage')}
                    onMouseEnter={(e) => {
                        e.currentTarget.style.color = accentColor;
                        e.currentTarget.style.backgroundColor = `${accentColor}1a`;
                    }}
                    onMouseLeave={(e) => {
                        e.currentTarget.style.color = '';
                        e.currentTarget.style.backgroundColor = '';
                    }}
                >
                    <span className="font-bold text-sm">{i18n.language.split('-')[0].toUpperCase()}</span>
                    <span className="absolute -top-10 left-1/2 -translate-x-1/2 px-2 py-1 text-xs bg-black/90 text-white rounded opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-50">
                        {t('languageSelector.changeLanguage')}
                    </span>
                </button>

                {/* Language Dropdown */}
                {isOpen && (
                    <div className="absolute bottom-full left-0 mb-2 z-[100] min-w-[150px] bg-[#1a1a1a] backdrop-blur-xl border border-white/10 rounded-xl shadow-xl shadow-black/50 overflow-hidden">
                        {Object.values(LANGUAGE_CONFIG).map((lang) => (
                            <button
                                key={lang.code}
                                onClick={() => handleLanguageSelect(lang.code)}
                                className={`w-full px-3 py-2 flex items-center gap-2 text-sm ${i18n.language === lang.code
                                    ? 'bg-white/20 text-white'
                                    : 'text-white/70 hover:bg-white/10 hover:text-white'
                                    }`}
                            >
                                {i18n.language === lang.code && <Check size={14} className="text-white" strokeWidth={3} />}
                                <div className={`flex flex-col items-start ${i18n.language === lang.code ? '' : 'ml-[22px]'}`}>
                                    <span className="font-medium">{lang.nativeName}</span>
                                    <span className="text-xs opacity-50">{lang.name}</span>
                                </div>
                            </button>
                        ))}
                    </div>
                )}
            </div>

            {/* Confirmation Modal */}
            {showConfirm && (
                <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/60 backdrop-blur-sm">
                    <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl p-6 max-w-sm w-full mx-4 shadow-2xl">
                        <h3 className="text-lg font-bold text-white mb-2">{t('languageSelector.installTranslation')}</h3>
                        <p className="text-white/60 text-sm mb-6">
                            {t('languageSelector.searchTranslationMods', { lang: showConfirm.langName })}
                        </p>

                        <div className="flex gap-3 mt-4">
                            <button
                                onClick={handleIgnore}
                                className="flex-1 py-2 rounded-xl bg-white/10 text-white text-sm hover:bg-white/20 transition-colors"
                            >
                                {t('languageSelector.no')}
                            </button>
                            <button
                                onClick={handleConfirmInstall}
                                className="flex-1 py-2 rounded-xl text-sm font-medium shadow-lg transition-all hover:opacity-90"
                                style={{ backgroundColor: accentColor, color: accentTextColor, boxShadow: `0 10px 15px -3px ${accentColor}33` }}
                            >
                                {t('languageSelector.yesSearch')}
                            </button>
                        </div>

                        <div className="mt-4 flex items-center justify-center">
                            <label className="flex items-center gap-2 text-white/50 text-xs cursor-pointer hover:text-white/70">
                                <input
                                    type="checkbox"
                                    checked={dontAskAgain}
                                    onChange={(e) => setDontAskAgain(e.target.checked)}
                                    className="rounded bg-white/10 border-white/20 focus:ring-offset-0"
                                    style={{ accentColor }}
                                />
                                {t('languageSelector.dontAskAgain')}
                            </label>
                        </div>
                    </div>
                </div>
            )}
        </>
    );
};
