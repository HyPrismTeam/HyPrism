import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { X, HardDrive, AlertTriangle, Copy, SkipForward } from 'lucide-react';
import { useAccentColor } from '../../contexts/AccentColorContext';

interface UpdateConfirmationModalProps {
    oldVersion: number;
    newVersion: number;
    hasOldUserData: boolean;
    onConfirmWithCopy: () => void;
    onConfirmWithoutCopy: () => void;
    onCancel: () => void;
}

export const UpdateConfirmationModal = ({
    oldVersion,
    newVersion,
    hasOldUserData,
    onConfirmWithCopy,
    onConfirmWithoutCopy,
    onCancel
}: UpdateConfirmationModalProps) => {
    const { t } = useTranslation();
    const { accentColor, accentTextColor } = useAccentColor();
  
    const [isLoading, setIsLoading] = useState(false);

    const handleConfirmWithCopy = async () => {
        setIsLoading(true);
        await onConfirmWithCopy();
        setIsLoading(false);
    };

    const handleConfirmWithoutCopy = async () => {
        setIsLoading(true);
        await onConfirmWithoutCopy();
        setIsLoading(false);
    };

    return (
        <div className="fixed inset-0 z-[100] flex items-center justify-center">
            <div
                className={`absolute inset-0 `}
                style={{ background: 'rgba(0, 0, 0, 0.85)' }}
                onClick={onCancel}
            />

            <div className={`relative glass-panel-static-solid p-6 max-w-md w-full mx-4 shadow-2xl`}>
                {/* Header */}
                <div className="flex items-center justify-between mb-4">
                    <div className="flex items-center gap-3">
                        <div className="w-10 h-10 rounded-xl flex items-center justify-center" style={{ backgroundColor: `${accentColor}33` }}>
                            <HardDrive className="w-5 h-5" style={{ color: accentColor }} />
                        </div>
                        <h2 className="text-xl font-semibold text-white">
                            {t('updateConfirmation.title')}
                        </h2>
                    </div>
                    <button
                        onClick={onCancel}
                        className="w-8 h-8 rounded-lg bg-white/5 flex items-center justify-center text-white/40 hover:text-white hover:bg-white/10 transition-colors"
                    >
                        <X size={16} />
                    </button>
                </div>

                {/* Content */}
                <div className="space-y-4">
                    <div className="bg-[#151515] rounded-xl p-4 border border-white/5">
                        <div className="flex items-center justify-between text-sm">
                            <span className="text-white/60">{t('updateConfirmation.currentVersion')}</span>
                            <span className="text-white font-medium">v{oldVersion}</span>
                        </div>
                        <div className="flex items-center justify-center my-2">
                            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" style={{ color: accentColor }}>
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 14l-7 7m0 0l-7-7m7 7V3" />
                            </svg>
                        </div>
                        <div className="flex items-center justify-between text-sm">
                            <span className="text-white/60">{t('updateConfirmation.newVersion')}</span>
                            <span className="font-medium" style={{ color: accentColor }}>v{newVersion}</span>
                        </div>
                    </div>

                    {hasOldUserData ? (
                        <div className="space-y-3">
                            <p className="text-white/70 text-sm">
                                {t('updateConfirmation.hasDataMessage')}
                            </p>
                            
                            <div className="flex items-start gap-2 bg-yellow-500/10 border border-yellow-500/20 rounded-xl p-3">
                                <AlertTriangle className="w-5 h-5 text-yellow-500 flex-shrink-0 mt-0.5" />
                                <p className="text-yellow-400/80 text-xs">
                                    {t('updateConfirmation.copyDataQuestion')}
                                </p>
                            </div>

                            <div className="flex flex-col gap-2">
                                <button
                                    onClick={handleConfirmWithCopy}
                                    disabled={isLoading}
                                    className="w-full h-12 rounded-xl font-medium flex items-center justify-center gap-2 transition-colors disabled:opacity-50 hover:opacity-90"
                                    style={{ backgroundColor: accentColor, color: accentTextColor }}
                                >
                                    <Copy size={18} />
                                    {t('updateConfirmation.updateCopyData')}
                                </button>
                                <button
                                    onClick={handleConfirmWithoutCopy}
                                    disabled={isLoading}
                                    className="w-full h-12 rounded-xl bg-white/5 hover:bg-white/10 text-white/70 hover:text-white font-medium flex items-center justify-center gap-2 transition-colors disabled:opacity-50"
                                >
                                    <SkipForward size={18} />
                                    {t('updateConfirmation.updateWithoutCopy')}
                                </button>
                            </div>
                        </div>
                    ) : (
                        <div className="space-y-3">
                            <p className="text-white/70 text-sm">
                                {t('updateConfirmation.readyMessage')}
                            </p>

                            <button
                                onClick={handleConfirmWithoutCopy}
                                disabled={isLoading}
                                className="w-full h-12 rounded-xl font-medium flex items-center justify-center gap-2 transition-colors disabled:opacity-50 hover:opacity-90"
                                style={{ backgroundColor: accentColor, color: accentTextColor }}
                            >
                                {t('updateConfirmation.updateNow')}
                            </button>
                        </div>
                    )}

                    <button
                        onClick={onCancel}
                        className="w-full h-10 rounded-xl text-white/40 hover:text-white/70 text-sm transition-colors"
                    >
                        {t('common.cancel')}
                    </button>
                </div>
            </div>
        </div>
    );
};
