import { useState, useEffect, forwardRef } from 'react';
import { useTranslation } from 'react-i18next';
import { Wifi, WifiOff } from 'lucide-react';
import { ipc } from '@/lib/ipc';

interface OnlineToggleProps {
    className?: string;
    focused?: boolean;
    accentColor?: string;
}

export const OnlineToggle = forwardRef<HTMLButtonElement, OnlineToggleProps>(({ className, focused, accentColor = '#FFA845' }, ref) => {
    const { t } = useTranslation();
    const [isOnline, setIsOnline] = useState(true);
    const [isLoading, setIsLoading] = useState(true);

    useEffect(() => {
        const loadOnlineMode = async () => {
            try {
                const s = await ipc.settings.get();
                const mode = s.onlineMode;
                setIsOnline(mode);
            } catch (err) {
                console.error('Failed to load online mode:', err);
            }
            setIsLoading(false);
        };
        loadOnlineMode();
    }, []);

    const handleToggle = async () => {
        const newMode = !isOnline;
        setIsOnline(newMode);
        try {
            await ipc.settings.update({ onlineMode: newMode });
        } catch (err) {
            console.error('Failed to set online mode:', err);
            setIsOnline(!newMode); // Revert on error
        }
    };

    if (isLoading) {
        return (
            <div className={`w-12 h-12 rounded-xl glass border border-white/5 flex items-center justify-center text-white/30 ${className}`}>
                <Wifi size={20} className="animate-pulse" />
            </div>
        );
    }

    return (
        <button
            ref={ref}
            onClick={handleToggle}
            className={`w-12 h-12 rounded-xl glass border flex items-center justify-center active:scale-95 transition-all duration-150 relative group ${
                isOnline
                    ? 'border-green-500/30 text-green-400 hover:text-green-300 hover:bg-green-500/10'
                    : 'border-white/5 text-white/60 hover:text-white/80 hover:bg-white/5'
            } ${className}`}
            title={isOnline ? t('onlineToggle.online') : t('onlineToggle.offline')}
            style={focused ? { boxShadow: `0 0 0 2px ${accentColor}, 0 0 0 4px #090909` } : undefined}
        >
            {isOnline ? (
                <Wifi size={20} />
            ) : (
                <WifiOff size={20} />
            )}
            <span className="absolute -top-10 left-1/2 -translate-x-1/2 px-2 py-1 text-xs bg-black/90 text-white rounded opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-50">
                {isOnline ? t('onlineToggle.onlineClickDisable') : t('onlineToggle.offlineClickEnable')}
            </span>
        </button>
    );
});

OnlineToggle.displayName = 'OnlineToggle';
