import React, { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { X, MessageCircle, Check, Bug, Coffee } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { ipc } from '@/lib/ipc';

// TODO: These functions need IPC channels in IpcService.cs
export interface DiscordAnnouncementType {
  Id: string;
  Content: string;
  AuthorName?: string;
  AuthorAvatar?: string;
  AuthorRole?: string;
  RoleColor?: string;
  Timestamp: string;
  ImageUrl?: string;
}
async function GetDiscordAnnouncement(): Promise<DiscordAnnouncementType | null> {
  console.warn('[IPC] GetDiscordAnnouncement: no IPC channel yet');
  return null;
}
async function DismissAnnouncement(_id: string): Promise<void> {
  console.warn('[IPC] DismissAnnouncement: no IPC channel yet');
}
async function GetShowDiscordAnnouncements(): Promise<boolean> {
  const s = await ipc.settings.get();
  return s.showDiscordAnnouncements ?? true;
}
async function SetShowDiscordAnnouncements(value: boolean): Promise<void> {
  await ipc.settings.update({ showDiscordAnnouncements: value });
}

interface DiscordAnnouncementProps {
  onDismiss?: () => void;
  manualAnnouncement?: DiscordAnnouncementType | null;
}

export const DiscordAnnouncement: React.FC<DiscordAnnouncementProps> = ({ onDismiss, manualAnnouncement }) => {
  const { t } = useTranslation();
  const [announcement, setAnnouncement] = useState<DiscordAnnouncementType | null>(null);
  const [dismissed, setDismissed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [showAnnouncements, setShowAnnouncements] = useState(true);

  useEffect(() => {
    const fetchAnnouncement = async () => {
      try {
        // Check if announcements are enabled
        const enabled = await GetShowDiscordAnnouncements();
        setShowAnnouncements(enabled);
        
        if (!enabled) {
          setLoading(false);
          return;
        }

        const data = await GetDiscordAnnouncement();
        if (data) {
          setAnnouncement(data);
        }
      } catch (err) {
        console.error('Failed to fetch Discord announcement:', err);
      } finally {
        setLoading(false);
      }
    };

    fetchAnnouncement();
  }, []);

  // Handle manual announcement from test button
  useEffect(() => {
    if (manualAnnouncement) {
      setAnnouncement(manualAnnouncement);
      setDismissed(false);
      setLoading(false);
      setShowAnnouncements(true);
    }
  }, [manualAnnouncement]);

  const handleDismiss = async () => {
    if (announcement) {
      // Dismiss this specific announcement in the backend
      await DismissAnnouncement(announcement.Id);
    }
    setDismissed(true);
    onDismiss?.();
  };

  const handleToggleAnnouncements = async () => {
    const newValue = !showAnnouncements;
    setShowAnnouncements(newValue);
    await SetShowDiscordAnnouncements(newValue);
    
    if (!newValue) {
      // If disabling, also dismiss the current announcement
      setDismissed(true);
      onDismiss?.();
    }
  };

  // Format timestamp to readable date
  const formatDate = (timestamp: string) => {
    try {
      const date = new Date(timestamp);
      return date.toLocaleDateString(undefined, { 
        month: 'short', 
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
      });
    } catch {
      return '';
    }
  };

  if (loading || dismissed || !announcement) {
    return null;
  }

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0, y: 20, scale: 0.95 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        exit={{ opacity: 0, y: 20, scale: 0.95 }}
        className="fixed bottom-4 left-4 z-50 max-w-md"
      >
        <div className="glass rounded-xl border border-white/10 shadow-2xl overflow-hidden">
          {/* Header */}
          <div className="flex items-center justify-between px-4 py-3 bg-[#5865F2]/20 border-b border-white/5">
            <div className="flex items-center gap-2">
              <MessageCircle size={18} className="text-[#5865F2]" />
              <span className="text-sm font-semibold text-white/90">{t('discord.announcement')}</span>
            </div>
            <button
              onClick={handleDismiss}
              className="p-1 rounded-lg hover:bg-white/10 transition-colors"
              title={t('common.dismiss')}
            >
              <X size={16} className="text-white/60" />
            </button>
          </div>

          {/* Author */}
          <div className="flex items-center gap-3 px-4 py-3 border-b border-white/5">
            {announcement.AuthorAvatar ? (
              <img
                src={announcement.AuthorAvatar}
                alt={announcement.AuthorName || 'Author'}
                className="w-10 h-10 rounded-full border-2 border-[#5865F2]/50"
              />
            ) : (
              <div className="w-10 h-10 rounded-full bg-[#5865F2]/30 flex items-center justify-center">
                <span className="text-white font-bold text-sm">
                  {(announcement.AuthorName || 'A').charAt(0).toUpperCase()}
                </span>
              </div>
            )}
            <div className="flex flex-col">
              <span className="text-white font-semibold text-sm">
                {announcement.AuthorName || 'Unknown'}
              </span>
              <div className="flex items-center gap-2">
                {announcement.AuthorRole && (
                  <span 
                    className="text-xs px-1.5 py-0.5 rounded"
                    style={{ 
                      backgroundColor: announcement.RoleColor ? `${announcement.RoleColor}20` : 'rgba(88, 101, 242, 0.2)',
                      color: announcement.RoleColor || '#5865F2'
                    }}
                  >
                    {announcement.AuthorRole}
                  </span>
                )}
                <span className="text-white/40 text-xs">
                  {formatDate(announcement.Timestamp)}
                </span>
              </div>
            </div>
          </div>

          {/* Content */}
          <div className="px-4 py-3">
            <p className="text-white/80 text-sm whitespace-pre-wrap break-words">
              {announcement.Content}
            </p>
          </div>

          {/* Image if present */}
          {announcement.ImageUrl && (
            <div className="px-4 pb-3">
              <img
                src={announcement.ImageUrl}
                alt="Announcement"
                className="w-full rounded-lg border border-white/10 max-h-48 object-cover"
              />
            </div>
          )}

          {/* Footer with toggle and buttons */}
          <div className="px-4 py-3 border-t border-white/5 bg-black/20">
            {/* Action buttons */}
            <div className="flex items-center gap-2 mb-3">
              <button
                onClick={() => ipc.browser.open('https://discord.gg/hyprism')}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[#5865F2]/20 hover:bg-[#5865F2]/30 transition-colors text-[#5865F2] text-xs font-medium"
                title="Join Discord"
              >
                <MessageCircle size={14} />
                Discord
              </button>
              <button
                onClick={() => ipc.browser.open('https://github.com/HyPrism/HyPrism/issues/new')}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-white/10 hover:bg-white/20 transition-colors text-white/80 text-xs font-medium"
                title="Report a Bug"
              >
                <Bug size={14} />
                {t('settings.aboutSettings.bugReport')}
              </button>
              <button
                onClick={() => ipc.browser.open('https://buymeacoffee.com/hyprism')}
                className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg bg-[#FFDD00]/20 hover:bg-[#FFDD00]/30 transition-colors text-[#FFDD00] text-xs font-medium"
                title="Buy Me a Coffee"
              >
                <Coffee size={14} />
                Support
              </button>
            </div>
            
            {/* Toggle */}
            <label className="flex items-center gap-2 cursor-pointer">
              <div 
                className={`w-5 h-5 rounded border flex items-center justify-center transition-colors ${
                  showAnnouncements 
                    ? 'bg-[#5865F2] border-[#5865F2]' 
                    : 'bg-transparent border-white/30 hover:border-white/50'
                }`}
                onClick={handleToggleAnnouncements}
              >
                {showAnnouncements && <Check size={14} className="text-white" />}
              </div>
              <span className="text-xs text-white/60">
                {t('discord.showAnnouncements')}
              </span>
            </label>
          </div>
        </div>
      </motion.div>
    </AnimatePresence>
  );
};
