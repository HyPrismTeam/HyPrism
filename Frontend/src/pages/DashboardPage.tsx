import React, { useState, useEffect, useRef, useCallback, memo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Play, Download, Loader2, X, RefreshCw, User, ShieldAlert, Plus } from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';

import { ipc, InstanceInfo } from '@/lib/ipc';
import { DiscordIcon } from '../components/icons/DiscordIcon';
import { formatBytes } from '../utils/format';
import previewLogo from '../assets/images/preview_logo.png';

interface DashboardPageProps {
  // Profile
  username: string;
  uuid: string;
  launcherVersion: string;
  updateAvailable: boolean;
  avatarRefreshTrigger: number;
  onOpenProfileEditor: () => void;
  onLauncherUpdate: () => void;
  // Game state
  isDownloading: boolean;
  downloadState: 'downloading' | 'extracting' | 'launching';
  canCancel: boolean;
  isGameRunning: boolean;
  progress: number;
  downloaded: number;
  total: number;
  launchState: string;
  launchDetail: string;
  // Instance-based
  selectedInstance: InstanceInfo | null;
  instances: InstanceInfo[];
  hasInstances: boolean;
  isCheckingInstance: boolean;
  hasUpdateAvailable: boolean;
  // Actions
  onPlay: () => void;
  onStopGame: () => void;
  onDownload: () => void;
  onUpdate: () => void;
  onCancelDownload: () => void;
  onNavigateToInstances: () => void;
  onInstanceSelect: (instance: InstanceInfo) => void;
  // Official server state  
  officialServerBlocked: boolean;
  isOfficialProfile: boolean;
  isOfficialServerMode: boolean;
}

const pageVariants = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -12 },
};

export const DashboardPage: React.FC<DashboardPageProps> = memo((props) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();

  const [localAvatar, setLocalAvatar] = useState<string | null>(null);
  const [showCancelButton, setShowCancelButton] = useState(false);
  const [isSwitcherOpen, setIsSwitcherOpen] = useState(false);
  const [instanceIconMap, setInstanceIconMap] = useState<Record<string, string>>({});
  const switcherRef = useRef<HTMLDivElement>(null);

  const withCacheBust = (iconUrl: string) => {
    if (!iconUrl) return iconUrl;
    const separator = iconUrl.includes('?') ? '&' : '?';
    return `${iconUrl}${separator}t=${Date.now()}`;
  };

  useEffect(() => {
    ipc.profile.get().then(p => { if (p.avatarPath) setLocalAvatar(p.avatarPath); }).catch(() => {});
  }, [props.uuid, props.avatarRefreshTrigger]);

  useEffect(() => {
    if (!props.isDownloading) setShowCancelButton(false);
  }, [props.isDownloading]);

  // Check if selectors should be hidden (during download/launch or game running)
  const shouldHideInfo = props.isDownloading || props.isGameRunning;

  // Load icons for instances that do not have one cached yet
  useEffect(() => {
    const missing = props.instances.filter((inst) => !instanceIconMap[inst.id]);
    if (missing.length === 0) {
      return;
    }

    let isDisposed = false;
    const loadIcons = async () => {
      const found: Record<string, string> = {};
      await Promise.all(
        missing.map(async (inst) => {
          try {
            const icon = await ipc.instance.getIcon({ instanceId: inst.id });
            if (icon) {
              found[inst.id] = withCacheBust(icon);
            }
          } catch {
            // Ignore per-instance icon loading errors
          }
        })
      );

      if (isDisposed || Object.keys(found).length === 0) {
        return;
      }

      setInstanceIconMap((prev) => ({ ...prev, ...found }));
    };

    loadIcons();
    return () => {
      isDisposed = true;
    };
  }, [props.instances, instanceIconMap]);

  useEffect(() => {
    const selected = props.selectedInstance;
    if (!selected || instanceIconMap[selected.id]) {
      return;
    }

    let cancelled = false;
    const loadSelectedIcon = async () => {
      try {
        const icon = await ipc.instance.getIcon({ instanceId: selected.id });
        if (!cancelled && icon) {
          setInstanceIconMap((prev) => ({ ...prev, [selected.id]: withCacheBust(icon) }));
        }
      } catch {
        // Ignore selected icon loading errors
      }
    };

    loadSelectedIcon();
    return () => {
      cancelled = true;
    };
  }, [props.selectedInstance, instanceIconMap]);

  useEffect(() => {
    const selected = props.selectedInstance;
    if (!selected) return;

    let cancelled = false;
    const retry = setTimeout(async () => {
      if (cancelled) return;
      try {
        const icon = await ipc.instance.getIcon({ instanceId: selected.id });
        if (!cancelled && icon) {
          setInstanceIconMap((prev) => ({ ...prev, [selected.id]: withCacheBust(icon) }));
        }
      } catch {
        // Ignore retry errors
      }
    }, 350);

    return () => {
      cancelled = true;
      clearTimeout(retry);
    };
  }, [props.selectedInstance]);

  // Close switcher on outside click
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (switcherRef.current && !switcherRef.current.contains(e.target as Node)) {
        setIsSwitcherOpen(false);
      }
    };

    if (isSwitcherOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }
  }, [isSwitcherOpen]);

  useEffect(() => {
    if (shouldHideInfo || !props.selectedInstance) {
      setIsSwitcherOpen(false);
    }
  }, [shouldHideInfo, props.selectedInstance]);

  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setIsSwitcherOpen(false);
      }
    };

    if (isSwitcherOpen) {
      document.addEventListener('keydown', handleEscape);
      return () => document.removeEventListener('keydown', handleEscape);
    }
  }, [isSwitcherOpen]);

  const handleSwitcherSelect = useCallback((inst: InstanceInfo) => {
    setIsSwitcherOpen(false);
    props.onInstanceSelect(inst);
  }, [props.onInstanceSelect]);

  // Get translated launch state label
  const getLaunchStateLabel = () => {
    const stateKey = `launch.state.${props.launchState}`;
    const translated = t(stateKey);
    // If translation returns the key itself, fall back to raw state or generic text
    return translated !== stateKey ? translated : (props.launchState || t('launch.state.preparing'));
  };

  const getInstanceSubLabel = (inst: InstanceInfo) => {
    return `${inst.branch} ${inst.version > 0 ? `v${inst.version}` : t('common.latest')}`;
  };

  const getNotInstalledLabel = () => {
    const translated = t('common.notInstalled');
    return translated === 'common.notInstalled' ? t('instances.status.notInstalled') : translated;
  };

  // Compute display name with fallback (like InstancesPage)
  const getInstanceDisplayName = (inst?: InstanceInfo | null) => {
    const target = inst ?? props.selectedInstance;
    if (!target) return '';
    const { name, branch, version } = target;
    if (name && name.trim()) return name;
    const branchLabel = branch === 'release' ? t('common.release') : t('common.preRelease');
    return `${branchLabel} v${version}`;
  };

  // Render an instance icon (custom image or version badge)
  const renderInstanceIcon = (inst: InstanceInfo, size: number = 28, full: boolean = false) => {
    const customIcon = instanceIconMap[inst.id];
    if (customIcon) {
      return (
        <img
          src={customIcon}
          alt=""
          className={full ? 'w-full h-full object-cover rounded-[inherit]' : 'rounded-lg object-cover'}
          style={full ? undefined : { width: size, height: size }}
          onError={() => {
            setInstanceIconMap((prev) => {
              if (!prev[inst.id]) return prev;
              const next = { ...prev };
              delete next[inst.id];
              return next;
            });
          }}
        />
      );
    }
    const versionLabel = inst.version === 0 ? '★' : `v${inst.version}`;
    return (
      <span className="font-bold" style={{ color: accentColor, fontSize: full ? 20 : size * 0.5 }}>
        {versionLabel}
      </span>
    );
  };

  // Render the action section of the play button
  const renderActionButton = () => {
    // Official servers with unofficial profile — block play
    if (props.officialServerBlocked) {
      return (
        <button
          disabled
          className="h-full px-8 flex items-center gap-2 font-black text-base rounded-2xl cursor-not-allowed opacity-50 bg-white/5 text-white/40"
        >
          <ShieldAlert size={16} />
          <span>{t('main.play')}</span>
        </button>
      );
    }

    if (props.isGameRunning) {
      return (
        <button
          onClick={props.onStopGame}
          className="h-full px-8 flex items-center gap-2 font-black text-base tracking-tight bg-gradient-to-r from-red-600 to-red-500 text-white rounded-2xl hover:brightness-110 active:scale-[0.98] transition-all"
        >
          <X size={16} />
          <span>{t('main.stop')}</span>
        </button>
      );
    }

    if (props.isDownloading) {
      return (
        <div
          className={`h-full px-6 flex items-center justify-center relative overflow-hidden w-[160px] rounded-2xl ${props.canCancel ? 'cursor-pointer' : 'cursor-default'}`}
          style={{ background: 'rgba(255,255,255,0.05)' }}
          onMouseEnter={() => props.canCancel && setShowCancelButton(true)}
          onMouseLeave={() => setShowCancelButton(false)}
          onClick={() => showCancelButton && props.canCancel && props.onCancelDownload()}
        >
          <div
            className="absolute inset-0 transition-all duration-300"
            style={{ width: `${Math.min(props.progress, 100)}%`, backgroundColor: `${accentColor}40` }}
          />
          {showCancelButton && props.canCancel ? (
            <div className="relative z-10 flex items-center gap-2 text-red-500 hover:text-red-400 transition-colors">
              <X size={16} />
              <span className="text-xs font-bold uppercase">{t('main.cancel')}</span>
            </div>
          ) : (
            <div className="relative z-10 flex items-center gap-2">
              <Loader2 size={14} className="animate-spin text-white" />
              <span className="text-sm font-bold text-white">{getLaunchStateLabel()}</span>
            </div>
          )}
        </div>
      );
    }

    if (props.isCheckingInstance) {
      return (
        <button disabled className="h-full px-8 flex items-center gap-2 font-black text-base bg-white/10 text-white/50 cursor-not-allowed rounded-2xl">
          <Loader2 size={16} className="animate-spin" />
          <span>{t('main.checking')}</span>
        </button>
      );
    }

    // Has instance with update available
    if (props.selectedInstance && props.hasUpdateAvailable) {
      return (
        <div className="flex items-center h-full">
          <button
            onClick={props.onUpdate}
            className="h-full px-5 flex items-center gap-2 font-black text-sm bg-gradient-to-r from-blue-500 to-cyan-500 text-white rounded-l-2xl hover:brightness-110 active:scale-[0.98] transition-all"
          >
            <RefreshCw size={14} />
            <span>{t('main.update')}</span>
          </button>
          <div className="w-px h-6 bg-white/10" />
          <button
            onClick={props.onPlay}
            className="h-full px-6 flex items-center gap-2 font-black text-base rounded-r-2xl hover:brightness-110 active:scale-[0.98] transition-all"
            style={{ background: `linear-gradient(135deg, ${accentColor}, ${accentColor}cc)`, color: accentTextColor }}
          >
            <Play size={16} fill="currentColor" />
            <span>{t('main.play')}</span>
          </button>
        </div>
      );
    }

    // Has selected instance - play or download
    if (props.selectedInstance) {
      // Not installed - show download button
      if (!props.selectedInstance.isInstalled) {
        return (
          <button
            onClick={props.onPlay}
            className="h-full px-8 flex items-center gap-2 font-black text-base rounded-2xl bg-gradient-to-r from-green-500 to-emerald-600 text-white hover:brightness-110 active:scale-[0.98] transition-all"
          >
            <Download size={16} />
            <span>{t('main.download')}</span>
          </button>
        );
      }
      // Installed - show play button
      return (
        <button
          onClick={props.onPlay}
          className="h-full px-8 flex items-center gap-2 font-black text-lg rounded-2xl hover:brightness-110 active:scale-[0.98] transition-all"
          style={{ background: `linear-gradient(135deg, ${accentColor}, ${accentColor}cc)`, color: accentTextColor }}
        >
          <Play size={18} fill="currentColor" />
          <span>{t('main.play')}</span>
        </button>
      );
    }

    // No instance selected but instances exist - go to instances page
    if (props.hasInstances) {
      return (
        <button
          onClick={props.onNavigateToInstances}
          className="h-full px-8 flex items-center gap-2 font-black text-base rounded-2xl bg-gradient-to-r from-purple-500 to-violet-600 text-white hover:brightness-110 active:scale-[0.98] transition-all"
        >
          <span>{t('main.selectInstance')}</span>
        </button>
      );
    }

    // No instances - download
    return (
      <button
        onClick={props.onDownload}
        className="h-full px-8 flex items-center gap-2 font-black text-base rounded-2xl bg-gradient-to-r from-green-500 to-emerald-600 text-white hover:brightness-110 active:scale-[0.98] transition-all"
      >
        <Download size={16} />
        <span>{t('main.download')}</span>
      </button>
    );
  };

  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full flex flex-col items-center px-8 pt-6 pb-28"
    >
      {/* Top Row: Profile left, Social right */}
      <div className="w-full flex justify-between items-start">
        {/* Profile Section */}
        <motion.div
          initial={{ opacity: 0, x: -20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.1 }}
          className="flex items-center gap-3"
        >
          <motion.button
            whileHover={{ scale: 1.05 }}
            whileTap={{ scale: 0.95 }}
            onClick={props.onOpenProfileEditor}
            className="w-12 h-12 rounded-full overflow-hidden border-2 flex items-center justify-center flex-shrink-0"
            style={{ borderColor: accentColor, backgroundColor: localAvatar ? 'transparent' : `${accentColor}20` }}
            title={t('main.editProfile')}
          >
            {localAvatar ? (
              <img src={localAvatar} className="w-full h-full object-cover object-[center_20%]" alt="Avatar" />
            ) : (
              <User size={20} style={{ color: accentColor }} />
            )}
          </motion.button>
          <div className="flex flex-col">
            <div className="flex items-center gap-2">
              <span className="text-lg font-bold text-white">{props.username}</span>
            </div>
            <div className="flex items-center gap-2">
              <span className="text-xs text-white/30">HyPrism {props.launcherVersion}</span>
              {props.updateAvailable && (
                <motion.button whileTap={{ scale: 0.95 }} onClick={props.onLauncherUpdate}
                  className="text-[10px] font-medium transition-colors hover:opacity-80" style={{ color: accentColor }}>
                  <Download size={10} className="inline mr-1" />{t('main.updateAvailable')}
                </motion.button>
              )}
            </div>
          </div>
        </motion.div>

        {/* Social Links */}
        <motion.div
          initial={{ opacity: 0, x: 20 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ delay: 0.1 }}
          className="flex items-center gap-2"
        >
          <button
            onClick={async () => { ipc.browser.open('https://discord.gg/ekZqTtynjp'); }}
            className="p-2 rounded-xl hover:bg-[#5865F2]/20 transition-all active:scale-95"
            title={t('main.joinDiscord')}
          >
            <DiscordIcon size={22} className="drop-shadow-lg" />
          </button>
          <button
            onClick={() => ipc.browser.open('https://github.com/yyyumeniku/HyPrism')}
            className="w-9 h-9 rounded-xl flex items-center justify-center text-white/50 hover:text-white hover:bg-white/10 active:scale-95 transition-all"
            title={t('main.gitHubRepository')}
          >
            <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
          </button>
        </motion.div>
      </div>

      {/* Center: Logo + Label + Play Bar */}
      <div className="flex-1 flex flex-col items-center justify-center gap-3">
        <motion.div
          initial={{ opacity: 0, scale: 0.9 }}
          animate={{ opacity: 1, scale: 1 }}
          transition={{ delay: 0.2, type: 'spring', stiffness: 200 }}
          className="flex flex-col items-center gap-3"
        >
          <div className="flex flex-col items-center select-none">
            <img 
              src={previewLogo} 
              alt="HyPrism" 
              className="h-24 drop-shadow-xl select-none"
              draggable={false}
            />
            {/* Badge area - show either educational or official server blocked */}
            <AnimatePresence mode="wait">
              {props.officialServerBlocked && !props.isDownloading && !props.isGameRunning ? (
                <motion.div
                  key="blocked"
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -8 }}
                  transition={{ duration: 0.2 }}
                  className="mt-3"
                >
                  <div className="bg-orange-400/10 rounded-full px-4 py-1.5 border border-orange-400/20 flex items-center gap-1.5">
                    <ShieldAlert size={12} className="text-orange-400/80 flex-shrink-0" />
                    <span className="text-orange-400/80 text-[11px] whitespace-nowrap">
                      {t('main.officialServerBlocked')}
                    </span>
                  </div>
                </motion.div>
              ) : !props.isOfficialProfile && !props.isOfficialServerMode ? (
                <motion.div
                  key="educational"
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -8 }}
                  transition={{ duration: 0.2 }}
                  className="mt-3"
                >
                  <button
                    onClick={() => ipc.browser.open('https://hytale.com')}
                    className="px-3 py-1.5 rounded-full text-[11px] text-white/80 hover:text-white border border-white/20 hover:border-white/30 bg-[#1c1c1e] transition-all cursor-pointer"
                  >
                    {t('main.educational')} — <span className="font-semibold" style={{ color: accentColor }}>{t('main.buyIt')}</span>
                  </button>
                </motion.div>
              ) : null}
            </AnimatePresence>
          </div>
        </motion.div>

        {/* Play Button */}
        <motion.div
          initial={{ y: 16 }}
          animate={{ y: 0 }}
          transition={{ delay: 0.35, duration: 0.4, ease: 'easeOut' }}
          className="flex flex-col items-center"
        >
          {/* Button bar with relative positioning */}
          <div className="relative mt-3">
            <div className="flex items-center h-14 gap-2">
              {/* Instance Switcher - icon button with dropdown */}
              <div className="w-14 h-14 relative flex items-center justify-center" ref={switcherRef}>
                <AnimatePresence mode="wait">
                  {!shouldHideInfo && props.selectedInstance && props.instances.length > 0 && (
                    <motion.div
                      initial={{ opacity: 0, width: 0 }}
                      animate={{ opacity: 1, width: 'auto' }}
                      exit={{ opacity: 0, width: 0 }}
                      transition={{ duration: 0.25, ease: 'easeInOut' }}
                      className="relative flex items-center h-full"
                    >
                      <button
                        onClick={() => setIsSwitcherOpen((prev) => !prev)}
                        className="h-14 w-14 flex items-center justify-center rounded-xl bg-[#1c1c1e] border border-white/20 hover:border-white/30 active:scale-95 transition-all overflow-hidden"
                        title={getInstanceDisplayName()}
                        aria-label={t('main.selectInstance')}
                        aria-expanded={isSwitcherOpen}
                      >
                        {renderInstanceIcon(props.selectedInstance, 30, true)}
                      </button>

                      <AnimatePresence>
                        {isSwitcherOpen && (
                          <motion.div
                            initial={{ opacity: 0, y: -6 }}
                            animate={{ opacity: 1, y: 0 }}
                            exit={{ opacity: 0, y: -6 }}
                            transition={{ duration: 0.14, ease: 'easeOut' }}
                            className="absolute top-full left-0 mt-2 min-w-[260px] max-h-[320px] overflow-y-auto rounded-2xl bg-[#1a1a1a] shadow-2xl z-50"
                          >
                          {props.instances.map((inst) => {
                            const isSelected = inst.id === props.selectedInstance?.id;
                            return (
                              <button
                                key={inst.id}
                                onClick={() => handleSwitcherSelect(inst)}
                                className={`w-full px-3 py-2.5 flex items-center gap-3 transition-colors text-left ${
                                  isSelected ? 'bg-white/10' : 'hover:bg-white/5'
                                }`}
                              >
                                <div
                                  className="w-9 h-9 rounded-md flex items-center justify-center flex-shrink-0 border overflow-hidden"
                                  style={{
                                    borderColor: isSelected ? `${accentColor}60` : 'rgba(255,255,255,0.12)',
                                    backgroundColor: isSelected ? `${accentColor}15` : 'rgba(255,255,255,0.04)',
                                  }}
                                >
                                  {renderInstanceIcon(inst, 24, true)}
                                </div>
                                <div className="min-w-0 flex-1 flex flex-col items-start">
                                  <span className={`text-sm font-semibold truncate max-w-[170px] ${isSelected ? 'text-white' : 'text-white/75'}`}>
                                    {getInstanceDisplayName(inst)}
                                  </span>
                                  <span className="text-[10px] text-white/35">
                                    {getInstanceSubLabel(inst)}
                                    {!inst.isInstalled && ` · ${getNotInstalledLabel()}`}
                                  </span>
                                </div>
                              </button>
                            );
                          })}
                          <div className="border-t border-white/10" />
                          <button
                            onClick={() => {
                              setIsSwitcherOpen(false);
                              props.onNavigateToInstances();
                            }}
                            className="w-full px-3 py-2.5 flex items-center gap-3 text-left text-white/75 hover:text-white hover:bg-white/5 transition-colors"
                          >
                            <div className="w-9 h-9 rounded-xl border border-white/20 bg-[#242426] flex items-center justify-center flex-shrink-0">
                              <Plus size={16} />
                            </div>
                            <span className="text-sm font-semibold">{t('instances.addInstance')}</span>
                          </button>
                        </motion.div>
                        )}
                      </AnimatePresence>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>

              {/* Action Button (Play/Download/Update) */}
              <div className="h-14 flex items-center justify-center">
                {renderActionButton()}
              </div>

              {/* Right spacer keeps play button centered like old layout */}
              <div className="w-14 h-14" aria-hidden="true" />
            </div>

            {/* Progress Bar - only show when downloading and NOT in complete state */}
            <AnimatePresence>
              {props.isDownloading && props.launchState !== 'complete' && (
                <motion.div
                  initial={{ opacity: 0, y: 8, x: '-50%' }}
                  animate={{ opacity: 1, y: 0, x: '-50%' }}
                  exit={{ opacity: 0, y: 8, x: '-50%' }}
                  transition={{ duration: 0.2 }}
                  className="absolute top-full mt-2 w-[350px] left-1/2"
                >
                  <div className={`bg-[#1a1a1a]/95 rounded-xl px-3 py-2 border border-white/5`}>
                    {/* Progress bar container */}
                    <div className="h-1.5 w-full bg-white/10 rounded-full overflow-hidden">
                      <div
                        className="h-full rounded-full transition-all duration-300"
                        style={{ width: `${Math.min(props.progress, 100)}%`, backgroundColor: accentColor }}
                      />
                    </div>
                    {/* Info row: launchDetail on left, bytes on right */}
                    <div className="flex justify-between items-center mt-1.5 text-[10px]">
                      <span className="text-white/60 truncate max-w-[250px]">
                        {props.launchDetail ? (t(props.launchDetail) !== props.launchDetail 
                        ? t(props.launchDetail).replace('{0}', `${Math.min(Math.round(props.progress), 100)}`) : props.launchDetail) 
                        : getLaunchStateLabel()}
                      </span>
                      <span className="text-white/50 font-mono">
                        {props.total > 0
                          ? `${formatBytes(props.downloaded)} / ${formatBytes(props.total)}`
                          : `${Math.min(Math.round(props.progress), 100)}%`
                        }
                      </span>
                    </div>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        </motion.div>
      </div>


    </motion.div>
  );
});

DashboardPage.displayName = 'DashboardPage';
