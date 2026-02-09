import React, { useState, useRef, useEffect, memo } from 'react';
import { useTranslation } from 'react-i18next';
import { FolderOpen, Play, Package, Square, Settings, Loader2, Download, ChevronDown, Check, X, GitBranch, RefreshCw, Copy } from 'lucide-react';
import { CoffeeIcon } from './icons/CoffeeIcon';
import { OnlineToggle } from './icons/OnlineToggle';
import { ipc } from '@/lib/ipc';
import { GameBranch } from '../constants/enums';
import { useAccentColor } from '../contexts/AccentColorContext';
import { formatBytes } from '../utils/format';

// Memoized NavBtn component to prevent unnecessary re-renders
const NavBtn = memo(({ onClick, icon, tooltip, accentColor }: { onClick?: () => void; icon: React.ReactNode; tooltip?: string; accentColor: string }) => (
  <button
    onClick={onClick}
    className="w-12 h-12 rounded-xl glass border border-white/5 flex items-center justify-center text-white/60 hover:text-[var(--accent)] hover:bg-[var(--accent-bg)] active:scale-95 transition-all duration-150 relative group flex-shrink-0"
    style={{ 
      '--accent': accentColor,
      '--accent-bg': `${accentColor}1a`
    } as React.CSSProperties}
  >
    {icon}
    {tooltip && (
      <span className="absolute -top-10 left-1/2 -translate-x-1/2 px-2 py-1 text-xs bg-black/90 text-white rounded opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-50">
        {tooltip}
      </span>
    )}
  </button>
));

NavBtn.displayName = 'NavBtn';

// VersionStatus: describes installed version state
export type VersionStatus = {
  status: 'installed' | 'update_available' | 'not_installed' | 'unknown';
  installedVersion?: number;
  latestVersion?: number;
};

interface ControlSectionProps {
  onPlay: () => void;
  onDownload?: () => void;
  onUpdate?: () => void;
  onDuplicate?: () => void;
  onExit?: () => void;
  onCancelDownload?: () => void;
  isDownloading: boolean;
  downloadState?: 'downloading' | 'extracting' | 'launching';
  canCancel?: boolean;
  isGameRunning: boolean;
  isVersionInstalled: boolean;
  versionStatus?: VersionStatus | null;
  progress: number;
  downloaded: number;
  total: number;
  currentBranch: string;
  currentVersion: number;
  availableVersions: number[];
  installedVersions?: number[];
  isLoadingVersions?: boolean;
  isCheckingInstalled?: boolean;
  onBranchChange: (branch: string) => void;
  onVersionChange: (version: number) => void;
  onCustomDirChange?: () => void;
  onOpenSettings?: () => void;
  actions: {
    openFolder: () => void;
    showDelete: () => void;
    showModManager: (query?: string) => void;
  };
}

export const ControlSection: React.FC<ControlSectionProps> = memo(({
  onPlay,
  onDownload,
  onUpdate,
  onDuplicate,
  onExit,
  onCancelDownload,
  isDownloading,
  downloadState = 'downloading',
  canCancel = true,
  isGameRunning,
  isVersionInstalled,
  versionStatus = null,
  progress,
  downloaded,
  total,
  currentBranch,
  currentVersion,
  availableVersions,
  installedVersions = [],
  isLoadingVersions,
  isCheckingInstalled,
  onBranchChange,
  onVersionChange,
  onCustomDirChange: _onCustomDirChange,
  onOpenSettings,
  actions
}) => {
  const [isBranchOpen, setIsBranchOpen] = useState(false);
  const [isVersionOpen, setIsVersionOpen] = useState(false);
  const [showCancelButton, setShowCancelButton] = useState(false);
  const branchDropdownRef = useRef<HTMLDivElement>(null);
  const versionDropdownRef = useRef<HTMLDivElement>(null);
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();

  // Reset cancel button visibility when download starts/stops
  useEffect(() => {
    if (!isDownloading) {
      setShowCancelButton(false);
    }
  }, [isDownloading]);

  const openCoffee = () => ipc.browser.open('https://buymeacoffee.com/yyyumeniku');

  // Close dropdowns on click outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (branchDropdownRef.current && !branchDropdownRef.current.contains(e.target as Node)) {
        setIsBranchOpen(false);
      }
      if (versionDropdownRef.current && !versionDropdownRef.current.contains(e.target as Node)) {
        setIsVersionOpen(false);
      }
      if (versionDropdownRef.current && !versionDropdownRef.current.contains(e.target as Node)) {
        setIsVersionOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Close on escape
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setIsBranchOpen(false);
        setIsVersionOpen(false);
      }
    };
    document.addEventListener('keydown', handleEscape);
    return () => document.removeEventListener('keydown', handleEscape);
  }, []);

  const handleBranchSelect = (branch: string) => {
    onBranchChange(branch);
    setIsBranchOpen(false);
  };

  const handleVersionSelect = (version: number) => {
    onVersionChange(version);
    setIsVersionOpen(false);
  };

  const branchLabel = currentBranch === GameBranch.RELEASE
    ? t('common.release')
    : currentBranch === GameBranch.PRE_RELEASE
      ? t('common.preRelease')
      : t('common.release');

  // Use auto width to accommodate different translation lengths
  // min-w ensures it doesn't get too small

  const versionButtonStyle: React.CSSProperties = {};
  if (isVersionOpen) {
    versionButtonStyle.color = accentColor;
    versionButtonStyle.backgroundColor = `${accentColor}1a`;
  }

  return (
    <div className="flex flex-col gap-3">
      {/* Row 1: Version Selector - auto width to fit content */}
      <div className="h-12 rounded-xl glass border border-white/5 flex items-center w-fit min-w-[180px]">
        {/* Branch Dropdown (Left side) */}
        <div ref={branchDropdownRef} className="relative h-full flex-1">
          <button
            onClick={() => {
              setIsBranchOpen(!isBranchOpen);
              setIsVersionOpen(false);
            }}
            disabled={isLoadingVersions}
            className={`
              h-full w-full px-3
              flex items-center justify-center gap-2
              text-white/60 hover:text-white hover:bg-white/10
              disabled:opacity-50 disabled:cursor-not-allowed
              active:scale-95 transition-all duration-150 rounded-l-xl
              ${isBranchOpen ? 'text-white bg-white/10' : ''}
            `}
          >
            <GitBranch size={16} className="text-white/80" />
            <span className="text-sm font-medium">{branchLabel}</span>
            <ChevronDown
              size={12}
              className={`text-white/40 transition-transform duration-150 ${isBranchOpen ? 'rotate-180' : ''}`}
            />
          </button>

          {/* Branch Dropdown Menu (opens up) */}
          {isBranchOpen && (
            <div className="absolute bottom-full left-0 mb-2 z-[100] min-w-[140px] bg-[#1a1a1a] backdrop-blur-xl border border-white/10 rounded-xl shadow-xl shadow-black/50 overflow-hidden p-1">
              {[GameBranch.RELEASE, GameBranch.PRE_RELEASE].map((branch) => (
                <button
                  key={branch}
                  onClick={() => handleBranchSelect(branch)}
                  className={`w-full px-3 py-2 flex items-center gap-2 text-sm rounded-lg transition-colors`}
                  style={currentBranch === branch
                    ? { backgroundColor: `${accentColor}33`, color: 'white' }
                    : undefined
                  }
                  onMouseEnter={(e) => {
                    if (currentBranch !== branch) {
                      e.currentTarget.style.backgroundColor = `${accentColor}1a`;
                      e.currentTarget.style.color = accentColor;
                    }
                  }}
                  onMouseLeave={(e) => {
                    if (currentBranch !== branch) {
                      e.currentTarget.style.backgroundColor = '';
                      e.currentTarget.style.color = '';
                    }
                  }}
                >
                  {currentBranch === branch && <Check size={14} className="text-white" strokeWidth={3} />}
                  <span className={currentBranch === branch ? '' : 'ml-[22px]'}>
                    {branch === GameBranch.RELEASE ? t('common.release') : t('common.preRelease')}
                  </span>
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Version Dropdown (Right side) */}
        <div ref={versionDropdownRef} className="relative h-full flex-1">
          <button
            onClick={() => {
              setIsVersionOpen(!isVersionOpen);
              setIsBranchOpen(false);
            }}
            disabled={isLoadingVersions}
            className="h-full w-full px-3 flex items-center justify-center gap-2 text-white/60 disabled:opacity-50 disabled:cursor-not-allowed active:scale-95 transition-all duration-150 rounded-r-xl"
            style={versionButtonStyle}
            onMouseEnter={(e) => {
              if (!isVersionOpen) {
                e.currentTarget.style.color = accentColor;
                e.currentTarget.style.backgroundColor = `${accentColor}1a`;
              }
            }}
            onMouseLeave={(e) => {
              if (!isVersionOpen) {
                e.currentTarget.style.color = '';
                e.currentTarget.style.backgroundColor = '';
              }
            }}
          >
            <span className="text-sm font-medium">
              {isLoadingVersions ? '...' : currentVersion === 0 ? t('common.latest') : `v${currentVersion}`}
            </span>
            <ChevronDown
              size={12}
              className={`text-white/40 transition-transform duration-150 ${isVersionOpen ? 'rotate-180' : ''}`}
            />
          </button>

          {/* Version Dropdown Menu (opens up) */}
          {isVersionOpen && (
            <div className="absolute bottom-full right-0 mb-2 z-[100] min-w-[120px] max-h-60 overflow-y-auto bg-[#1a1a1a] backdrop-blur-xl border border-white/10 rounded-xl shadow-xl shadow-black/50 p-1">
              {availableVersions.length > 0 ? (
                availableVersions.map((version) => {
                  const isInstalled = (installedVersions || []).includes(version);
                  const isSelected = currentVersion === version;
                  return (
                    <button
                      key={version}
                      onClick={() => handleVersionSelect(version)}
                      className={`w-full px-3 py-2 flex items-center gap-2 text-sm rounded-lg ${
                        isSelected ? '' : 'text-white/70 hover:bg-white/10 hover:text-white'
                      }`}
                      style={isSelected ? { backgroundColor: `${accentColor}33`, color: accentColor } : undefined}
                    >
                      {/* Show checkmark for installed versions */}
                      {isInstalled ? (
                        <Check size={14} className={isSelected ? '' : 'text-green-400'} style={isSelected ? { color: accentColor } : undefined} strokeWidth={3} />
                      ) : (
                        <span className="w-[14px]" />
                      )}
                      <span>
                        {version === 0 ? t('common.latest') : `v${version}`}
                      </span>
                    </button>
                  );
                })
              ) : (
                <div className="px-3 py-2 text-sm text-white/40">{t('main.noVersions')}</div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Row 2: Nav buttons */}
      <div className="flex gap-2 items-center flex-wrap">
        <NavBtn onClick={() => actions.showModManager()} icon={<Package size={18} />} tooltip={t('main.modManager')} accentColor={accentColor} />
        <NavBtn onClick={actions.openFolder} icon={<FolderOpen size={18} />} tooltip={t('main.openInstanceFolder')} accentColor={accentColor} />
        <NavBtn onClick={onOpenSettings} icon={<Settings size={18} />} tooltip={t('main.settings')} accentColor={accentColor} />
        <OnlineToggle accentColor={accentColor} />
        <button
          tabIndex={-1}
          onClick={openCoffee}
          className="h-12 px-4 rounded-xl glass border border-white/5 flex items-center justify-center gap-2 text-white/60 hover:text-[var(--accent)] hover:bg-[var(--accent-bg)] active:scale-95 transition-all duration-150 relative group whitespace-nowrap"
          style={{ 
            '--accent': accentColor,
            '--accent-bg': `${accentColor}1a`
          } as React.CSSProperties}
        >
          <span className="text-xs">{t('main.buyMeA')}</span>
          <CoffeeIcon size={20} />
        </button>

        {/* Spacer + Disclaimer in center */}
        <div className="flex-1 flex justify-center min-w-0">
          <p className="text-white/40 text-xs whitespace-nowrap truncate">
            {t('main.educationalOnly')} {t('main.likeIt')} <button onClick={() => ipc.browser.open('https://hytale.com')} className="font-semibold hover:underline cursor-pointer" style={{ color: accentColor }}>{t('main.buyIt')}</button>
          </p>
        </div>

        {/* Play/Download button on right */}
        <div className="flex justify-end flex-shrink-0">
          {isGameRunning ? (
            <button
              tabIndex={-1}
              onClick={onExit}
              className="h-12 px-6 rounded-xl font-black text-base tracking-tight flex items-center justify-center gap-2 bg-gradient-to-r from-red-600 to-red-500 text-white hover:shadow-lg hover:shadow-red-500/25 hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              <Square size={16} fill="currentColor" />
              <span>{t('main.exit')}</span>
            </button>
          ) : isDownloading ? (
            <div 
              tabIndex={-1}
              className={`h-12 px-4 rounded-xl bg-[#1a1a1a] border border-white/10 flex items-center justify-center relative overflow-hidden min-w-[220px] group ${canCancel ? 'cursor-pointer' : 'cursor-default'}`}
              onMouseEnter={() => canCancel && setShowCancelButton(true)}
              onMouseLeave={() => setShowCancelButton(false)}
              onClick={() => showCancelButton && canCancel && onCancelDownload?.()}
            >
              <div
                className="absolute inset-0 transition-all duration-300"
                style={{ 
                  width: `${Math.min(progress, 100)}%`,
                  backgroundColor: `${accentColor}50`
                }}
              />
              {showCancelButton && canCancel && onCancelDownload ? (
                <div className="relative z-10 flex items-center gap-2 text-red-500 hover:text-red-400 transition-colors">
                  <X size={16} />
                  <span className="text-xs font-bold">{t('main.cancel')}</span>
                </div>
              ) : (
                <div className="relative z-10 flex flex-col items-center justify-center gap-0.5">
                  <div className="flex items-center gap-2">
                    <Loader2 size={12} className="animate-spin text-white flex-shrink-0" />
                    <span className="text-[10px] font-bold text-white uppercase">
                      {downloadState === 'downloading' && t('update.downloading')}
                      {downloadState === 'extracting' && t('update.extracting')}
                      {downloadState === 'launching' && t('update.launching')}
                    </span>
                    <span className="text-xs font-mono text-white/80 flex-shrink-0">{Math.min(Math.round(progress), 100)}%</span>
                  </div>
                  {downloadState === 'downloading' && total > 0 && (
                    <span className="text-[10px] font-mono text-white/60">
                      {formatBytes(downloaded)} / {formatBytes(total)}
                    </span>
                  )}
                </div>
              )}
            </div>
          ) : isCheckingInstalled ? (
            <button
              tabIndex={-1}
              disabled
              className="h-12 px-5 rounded-xl font-black text-base tracking-tight flex items-center justify-center gap-2 bg-white/10 text-white/50 cursor-not-allowed"
            >
              <Loader2 size={16} className="animate-spin" />
              <span>{t('main.checking')}</span>
            </button>
          ) : isVersionInstalled && versionStatus?.status === 'update_available' && currentVersion === 0 ? (
            // Show UPDATE button when latest instance needs an update
            <div className="flex items-center gap-2">
              <button
                tabIndex={-1}
                onClick={onUpdate || onDownload}
                disabled={isDownloading}
                className={`h-12 px-5 rounded-xl font-black text-base tracking-tight flex items-center justify-center gap-2 ${
                  isDownloading 
                    ? 'bg-white/10 text-white/50 cursor-not-allowed' 
                    : 'bg-gradient-to-r from-blue-500 to-cyan-500 text-white hover:shadow-lg hover:shadow-blue-500/25 hover:scale-[1.02] active:scale-[0.98] cursor-pointer'
                } transition-all duration-150`}
              >
                <RefreshCw size={16} className={isDownloading ? 'animate-spin' : ''} />
                <span>{t('main.update')}</span>
              </button>
              <button
                tabIndex={-1}
                onClick={onPlay}
                disabled={isDownloading}
                className={`h-12 px-5 rounded-xl font-black text-base tracking-tight flex items-center justify-center gap-2 ${
                  isDownloading ? 'opacity-50 cursor-not-allowed' : 'hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] cursor-pointer'
                } transition-all duration-150`}
                style={{ 
                  background: `linear-gradient(to right, ${accentColor}, ${accentColor}cc)`,
                  boxShadow: isDownloading ? 'none' : `0 10px 15px -3px ${accentColor}40`,
                  color: accentTextColor
                }}
              >
                <Play size={16} fill="currentColor" />
                <span>{t('main.play')}</span>
              </button>
            </div>
          ) : isVersionInstalled ? (
            <button
              tabIndex={-1}
              onClick={onPlay}
              className="h-12 px-6 rounded-xl font-black text-lg tracking-tight flex items-center justify-center gap-2 hover:shadow-lg hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
              style={{ 
                background: `linear-gradient(to right, ${accentColor}, ${accentColor}cc)`,
                boxShadow: `0 10px 15px -3px ${accentColor}40`,
                color: accentTextColor
              }}
            >
              <Play size={18} fill="currentColor" />
              <span>{t('main.play')}</span>
            </button>
          ) : currentVersion > 0 && versionStatus?.installedVersion === currentVersion ? (
            // Show DUPLICATE button for historical versions that can be duplicated from latest
            <button
              tabIndex={-1}
              onClick={onDuplicate}
              className="h-12 px-4 rounded-xl font-black text-base tracking-tight flex items-center justify-center gap-2 bg-gradient-to-r from-purple-500 to-violet-600 text-white hover:shadow-lg hover:shadow-purple-500/25 hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              <Copy size={16} />
              <span>{t('main.duplicate')}</span>
            </button>
          ) : (
            <button
              tabIndex={-1}
              onClick={onDownload}
              className="h-12 px-6 rounded-xl font-black text-base tracking-tight flex items-center justify-center gap-2 bg-gradient-to-r from-green-500 to-emerald-600 text-white hover:shadow-lg hover:shadow-green-500/25 hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              <Download size={16} />
              <span>{t('main.download')}</span>
            </button>
          )}
        </div>
      </div>
    </div>
  );
});

ControlSection.displayName = 'ControlSection';
