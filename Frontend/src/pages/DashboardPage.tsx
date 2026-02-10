import React, { useState, useRef, useEffect, memo } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Play, Download, Loader2, X, GitBranch, ChevronDown, Check, RefreshCw, Copy, Edit3, User } from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';
import { useAnimatedGlass } from '../contexts/AnimatedGlassContext';
import { ipc } from '@/lib/ipc';
import { GameBranch } from '../constants/enums';
import { DiscordIcon } from '../components/icons/DiscordIcon';
import { formatBytes } from '../utils/format';

// VersionStatus type
export type VersionStatus = {
  status: 'installed' | 'update_available' | 'not_installed' | 'unknown';
  installedVersion?: number;
  latestVersion?: number;
};

interface DashboardPageProps {
  // Profile
  username: string;
  uuid: string;
  isEditing: boolean;
  launcherVersion: string;
  updateAvailable: boolean;
  avatarRefreshTrigger: number;
  onEditToggle: (editing: boolean) => void;
  onUserChange: (name: string) => void;
  onOpenProfileEditor: () => void;
  onLauncherUpdate: () => void;
  // Game state
  isDownloading: boolean;
  downloadState: 'downloading' | 'extracting' | 'launching';
  canCancel: boolean;
  isGameRunning: boolean;
  isVersionInstalled: boolean;
  isCheckingInstalled: boolean;
  versionStatus: VersionStatus | null;
  progress: number;
  downloaded: number;
  total: number;
  launchState: string;
  launchDetail: string;
  // Version
  currentBranch: string;
  currentVersion: number;
  availableVersions: number[];
  installedVersions: { version: number; isValid: boolean }[];
  isLoadingVersions: boolean;
  onBranchChange: (branch: string) => void;
  onVersionChange: (version: number) => void;
  // Actions
  onPlay: () => void;
  onDownload: () => void;
  onUpdate: () => void;
  onDuplicate: () => void;
  onExit: () => void;
  onCancelDownload: () => void;
}

const pageVariants = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -12 },
};

export const DashboardPage: React.FC<DashboardPageProps> = memo((props) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();
  const { animatedGlass } = useAnimatedGlass();
  const [editValue, setEditValue] = useState(props.username);
  const [localAvatar, setLocalAvatar] = useState<string | null>(null);
  const [isBranchOpen, setIsBranchOpen] = useState(false);
  const [isVersionOpen, setIsVersionOpen] = useState(false);
  const [versionDropdownLeft, setVersionDropdownLeft] = useState(0);
  const [showCancelButton, setShowCancelButton] = useState(false);
  const branchRef = useRef<HTMLDivElement>(null);
  const versionRef = useRef<HTMLDivElement>(null);

  useEffect(() => { setEditValue(props.username); }, [props.username]);

  useEffect(() => {
    ipc.profile.get().then(p => { if (p.avatarPath) setLocalAvatar(p.avatarPath); }).catch(() => {});
  }, [props.uuid, props.avatarRefreshTrigger]);

  // Close dropdowns on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (branchRef.current && !branchRef.current.contains(e.target as Node)) setIsBranchOpen(false);
      if (versionRef.current && !versionRef.current.contains(e.target as Node)) setIsVersionOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  useEffect(() => {
    if (!props.isDownloading) setShowCancelButton(false);
  }, [props.isDownloading]);

  const handleSave = () => {
    if (editValue.trim() && editValue.length <= 16) {
      props.onUserChange(editValue.trim());
      props.onEditToggle(false);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSave();
    else if (e.key === 'Escape') { setEditValue(props.username); props.onEditToggle(false); }
  };

  const branchLabel = props.currentBranch === GameBranch.RELEASE ? t('main.release')
    : props.currentBranch === GameBranch.PRE_RELEASE ? t('main.preRelease')
    : t('main.release');

  // Get translated launch state label
  const getLaunchStateLabel = () => {
    const stateKey = `launch.state.${props.launchState}`;
    const translated = t(stateKey);
    // If translation returns the key itself, fall back to raw state or generic text
    return translated !== stateKey ? translated : (props.launchState || t('launch.state.preparing'));
  };

  // Check if selectors should be hidden (during download/launch or game running)
  const shouldHideSelectors = props.isDownloading || props.isGameRunning;

  // Render the action section of the merged play button
  const renderActionButton = () => {
    if (props.isGameRunning) {
      return (
        <button
          disabled
          className="h-full px-8 flex items-center gap-2 font-black text-base tracking-tight bg-gradient-to-r from-red-600 to-red-500 text-white rounded-r-2xl cursor-not-allowed opacity-90"
        >
          <Loader2 size={16} className="animate-spin" />
          <span>{t('main.running')}</span>
        </button>
      );
    }

    if (props.isDownloading) {
      return (
        <div
          className={`h-full px-6 flex items-center justify-center relative overflow-hidden w-[160px] rounded-r-2xl ${props.canCancel ? 'cursor-pointer' : 'cursor-default'}`}
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

    if (props.isCheckingInstalled) {
      return (
        <button disabled className="h-full px-8 flex items-center gap-2 font-black text-base bg-white/10 text-white/50 cursor-not-allowed rounded-r-2xl">
          <Loader2 size={16} className="animate-spin" />
          <span>{t('main.checking')}</span>
        </button>
      );
    }

    // Update available + installed
    if (props.isVersionInstalled && props.versionStatus?.status === 'update_available' && props.currentVersion === 0) {
      return (
        <div className="flex items-center h-full">
          <button
            onClick={props.onUpdate}
            className="h-full px-5 flex items-center gap-2 font-black text-sm bg-gradient-to-r from-blue-500 to-cyan-500 text-white hover:brightness-110 active:scale-[0.98] transition-all"
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

    // Version installed - play
    if (props.isVersionInstalled) {
      return (
        <button
          onClick={props.onPlay}
          className="h-full px-8 flex items-center gap-2 font-black text-lg rounded-r-2xl hover:brightness-110 active:scale-[0.98] transition-all"
          style={{ background: `linear-gradient(135deg, ${accentColor}, ${accentColor}cc)`, color: accentTextColor }}
        >
          <Play size={18} fill="currentColor" />
          <span>{t('main.play')}</span>
        </button>
      );
    }

    // Duplicate available
    if (props.currentVersion > 0 && props.versionStatus?.installedVersion === props.currentVersion) {
      return (
        <button
          onClick={props.onDuplicate}
          className="h-full px-6 flex items-center gap-2 font-black text-base rounded-r-2xl bg-gradient-to-r from-purple-500 to-violet-600 text-white hover:brightness-110 active:scale-[0.98] transition-all"
        >
          <Copy size={16} />
          <span>{t('main.duplicate')}</span>
        </button>
      );
    }

    // Download
    return (
      <button
        onClick={props.onDownload}
        className="h-full px-8 flex items-center gap-2 font-black text-base rounded-r-2xl bg-gradient-to-r from-green-500 to-emerald-600 text-white hover:brightness-110 active:scale-[0.98] transition-all"
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
      className="h-full flex flex-col items-center justify-between px-8 pt-14 pb-28"
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
              {props.isEditing ? (
                <div className="flex items-center gap-2">
                  <input
                    type="text"
                    value={editValue}
                    onChange={(e) => setEditValue(e.target.value)}
                    onKeyDown={handleKeyDown}
                    maxLength={16}
                    autoFocus
                    className="bg-[#151515] text-white text-lg font-bold px-3 py-1 rounded-lg border outline-none w-36"
                    style={{ borderColor: `${accentColor}4d` }}
                    onFocus={(e) => { e.currentTarget.style.borderColor = accentColor; }}
                    onBlur={(e) => { e.currentTarget.style.borderColor = `${accentColor}4d`; }}
                  />
                  <motion.button whileTap={{ scale: 0.9 }} onClick={handleSave}
                    className="p-1.5 rounded-lg" style={{ backgroundColor: `${accentColor}33`, color: accentColor }}>
                    <Check size={14} />
                  </motion.button>
                </div>
              ) : (
                <>
                  <span className="text-lg font-bold text-white">{props.username}</span>
                  <motion.button whileTap={{ scale: 0.9 }} onClick={() => props.onEditToggle(true)}
                    className="p-1 rounded text-white/30 hover:text-white/60 transition-colors">
                    <Edit3 size={12} />
                  </motion.button>
                </>
              )}
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
            onClick={async () => { ipc.browser.open('https://discord.gg/hyprism'); }}
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
      <div className="flex flex-col items-center gap-5 -mt-4">
        <motion.div
          initial={{ opacity: 0, scale: 0.9 }}
          animate={{ opacity: 1, scale: 1 }}
          transition={{ delay: 0.2, type: 'spring', stiffness: 200 }}
          className="flex flex-col items-center gap-3"
        >
          <div className="flex flex-col items-center select-none">
            <h1 className="text-8xl tracking-tighter leading-tight font-black drop-shadow-xl" style={{ fontFamily: 'system-ui, -apple-system, sans-serif' }}>
              <span className="text-white">Hy</span>
              <motion.span 
                className="bg-clip-text text-transparent bg-[length:200%_auto]" 
                style={{ 
                  backgroundImage: `linear-gradient(90deg, ${accentColor}, #22d3ee, #e879f9, ${accentColor})`,
                  filter: `drop-shadow(0 0 30px ${accentColor}66)`
                }}
                animate={{ backgroundPosition: ['0%', '200%'] }}
                transition={{ duration: 8, repeat: Infinity, ease: 'linear' }}
              >Prism</motion.span>
            </h1>
          </div>
        </motion.div>

        {/* Merged Branch/Version/Play Button */}
        <motion.div
          initial={{ y: 16 }}
          animate={{ y: 0 }}
          transition={{ delay: 0.35, duration: 0.4, ease: 'easeOut' }}
          className="flex flex-col items-center"
        >
          {/* Button bar with relative positioning for dropdowns */}
          <div className="relative mt-7">
            {/* Educational label */}
            <AnimatePresence>
              {!shouldHideSelectors && (
                <motion.div
                  initial={{ opacity: 0, y: 12, scale: 0.95 }}
                  animate={{ opacity: 1, y: 0, scale: 1 }}
                  exit={{ opacity: 0, y: 12, scale: 0.95 }}
                  transition={{ duration: 0.2, ease: 'easeOut' }}
                  className="absolute bottom-full flex w-full justify-center"
                >
                  <div className={`${animatedGlass ? 'bg-black/40 backdrop-blur-sm' : 'bg-[#1a1a1a]/90'} rounded-t-lg px-4 py-1.5 border-x border-t border-white/5`}>
                    <p className="text-white/40 text-[11px] whitespace-nowrap text-center">
                      {t('main.educational')}{' '}
                      <button
                        onClick={() => ipc.browser.open('https://hytale.com')}
                        className="font-semibold hover:underline cursor-pointer"
                        style={{ color: accentColor }}
                      >
                        {t('main.buyIt')}
                      </button>
                    </p>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
            <div
              className={`flex items-center h-14 rounded-2xl overflow-hidden ${animatedGlass ? 'glass-bar-dashboard' : 'glass-bar-dashboard-solid'}`}
            >
              {/* Branch & Version Selectors - hidden during download/game running */}
              <AnimatePresence mode="wait">
                {!shouldHideSelectors && (
                  <motion.div
                    initial={{ opacity: 0, width: 0 }}
                    animate={{ opacity: 1, width: 'auto' }}
                    exit={{ opacity: 0, width: 0 }}
                    transition={{ duration: 0.25, ease: 'easeInOut' }}
                    className="flex items-center h-full overflow-hidden"
                  >
                    {/* Branch Selector Button */}
                    <div ref={branchRef} className="h-full">
                      <button
                        onClick={() => { setIsBranchOpen(!isBranchOpen); setIsVersionOpen(false); }}
                        disabled={props.isLoadingVersions}
                        className="h-full px-4 flex items-center gap-2 text-white/60 hover:text-white hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed active:scale-95 transition-all"
                      >
                        <GitBranch size={14} className="text-white/70" />
                        <span className="text-sm font-medium whitespace-nowrap">{branchLabel}</span>
                        <ChevronDown size={11} className={`text-white/40 transition-transform ${isBranchOpen ? 'rotate-180' : ''}`} />
                      </button>
                    </div>

                    <div className="w-px h-7 bg-white/10" />

                    {/* Version Selector Button */}
                    <div ref={versionRef} className="h-full">
                      <button
                        onClick={() => { 
                          if (versionRef.current) setVersionDropdownLeft(versionRef.current.offsetLeft);
                          setIsVersionOpen(!isVersionOpen); 
                          setIsBranchOpen(false); 
                        }}
                        disabled={props.isLoadingVersions}
                        className="h-full px-4 flex items-center gap-2 text-white/60 hover:text-white hover:bg-white/5 disabled:opacity-50 disabled:cursor-not-allowed active:scale-95 transition-all"
                      >
                        <span className="text-sm font-medium whitespace-nowrap">
                          {props.isLoadingVersions ? '...' : props.currentVersion === 0 ? t('main.latest') : `v${props.currentVersion}`}
                        </span>
                        <ChevronDown size={11} className={`text-white/40 transition-transform ${isVersionOpen ? 'rotate-180' : ''}`} />
                      </button>
                    </div>

                    <div className="w-px h-7 bg-white/10" />
                  </motion.div>
                )}
              </AnimatePresence>

              {/* Action Button (Play/Download/Update/Exit) - fixed width to prevent jumping */}
              <div className="min-w-[140px] h-full flex items-center justify-end">
                {renderActionButton()}
              </div>
            </div>

            {/* Dropdown Menus - positioned relative to the button bar */}
            <AnimatePresence>
              {!shouldHideSelectors && isBranchOpen && (
                <motion.div 
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -8 }}
                  transition={{ duration: 0.15 }}
                  className={`absolute top-full left-0 mt-2 z-[100] min-w-[140px] bg-[#1a1a1a] ${animatedGlass ? 'backdrop-blur-xl' : ''} border border-white/10 rounded-xl shadow-xl shadow-black/50 overflow-hidden p-1`}
                >
                  {[GameBranch.RELEASE, GameBranch.PRE_RELEASE].map((branch) => (
                    <button
                      key={branch}
                      onClick={() => { props.onBranchChange(branch); setIsBranchOpen(false); }}
                      className="w-full px-3 py-2 flex items-center gap-2 text-sm rounded-lg transition-colors"
                      style={props.currentBranch === branch ? { backgroundColor: `${accentColor}33`, color: 'white' } : undefined}
                      onMouseEnter={(e) => { if (props.currentBranch !== branch) { e.currentTarget.style.backgroundColor = `${accentColor}1a`; e.currentTarget.style.color = accentColor; } }}
                      onMouseLeave={(e) => { if (props.currentBranch !== branch) { e.currentTarget.style.backgroundColor = ''; e.currentTarget.style.color = ''; } }}
                    >
                      {props.currentBranch === branch && <Check size={14} className="text-white" strokeWidth={3} />}
                      <span className={props.currentBranch === branch ? '' : 'ml-[22px]'}>
                        {branch === GameBranch.RELEASE ? t('main.release') : t('main.preRelease')}
                      </span>
                    </button>
                  ))}
                </motion.div>
              )}
            </AnimatePresence>

            <AnimatePresence>
              {!shouldHideSelectors && isVersionOpen && (
                <motion.div 
                  initial={{ opacity: 0, y: -8 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -8 }}
                  transition={{ duration: 0.15 }}
                  className={`absolute top-full mt-2 z-[100] min-w-[140px] max-h-[168px] overflow-y-auto bg-[#1a1a1a] ${animatedGlass ? 'backdrop-blur-xl' : ''} border border-white/10 rounded-xl shadow-xl shadow-black/50 p-1`}
                  style={{ left: versionDropdownLeft }}
                >
                  {props.availableVersions.length > 0 ? (
                    props.availableVersions.map((version) => {
                      const installedInfo = props.installedVersions.find(v => v.version === version);
                      const isInstalled = !!installedInfo;
                      const isValid = installedInfo?.isValid ?? false;
                      const isSelected = props.currentVersion === version;
                      return (
                        <button
                          key={version}
                          onClick={() => { props.onVersionChange(version); setIsVersionOpen(false); }}
                          className={`w-full px-3 py-2 flex items-center gap-2 text-sm rounded-lg ${isSelected ? '' : 'text-white/70 hover:bg-white/10 hover:text-white'}`}
                          style={isSelected ? { backgroundColor: `${accentColor}33`, color: accentColor } : undefined}
                        >
                          {isInstalled && isValid ? (
                            <Check size={14} className={isSelected ? '' : 'text-green-400'} style={isSelected ? { color: accentColor } : undefined} strokeWidth={3} />
                          ) : isInstalled ? (
                            <Download size={14} className="text-yellow-500" />
                          ) : (
                            <Download size={14} className="text-white/40" />
                          )}
                          <span>{version === 0 ? t('main.latest') : `v${version}`}</span>
                        </button>
                      );
                    })
                  ) : (
                    <div className="px-3 py-2 text-sm text-white/40">{t('main.noVersions')}</div>
                  )}
                </motion.div>
              )}
            </AnimatePresence>

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
                  <div className={`${animatedGlass ? 'bg-black/60 backdrop-blur-md' : 'bg-[#1a1a1a]/95'} rounded-xl px-3 py-2 border border-white/5`}>
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

      {/* Spacer for bottom dock */}
      <div />
    </motion.div>
  );
});

DashboardPage.displayName = 'DashboardPage';
