import React, { useState, useRef, useEffect } from 'react';
import { FolderOpen, Play, Package, Square, Github, Bug, GitBranch, Loader2, Download, ChevronDown, HardDrive, Check, Coffee } from 'lucide-react';
import { BrowserOpenURL } from '../../wailsjs/runtime/runtime';

interface ControlSectionProps {
  onPlay: () => void;
  onDownload?: () => void;
  onExit?: () => void;
  isDownloading: boolean;
  isGameRunning: boolean;
  isVersionInstalled: boolean;
  latestNeedsUpdate?: boolean;
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
  actions: {
    openFolder: () => void;
    showDelete: () => void;
    showModManager: () => void;
  };
}

const NavBtn: React.FC<{ onClick?: () => void; icon: React.ReactNode; tooltip?: string }> = ({ onClick, icon, tooltip }) => (
  <button
    onClick={onClick}
    className="w-12 h-12 rounded-xl glass border border-white/5 flex items-center justify-center text-white/60 hover:text-[#FFA845] hover:bg-[#FFA845]/10 active:scale-95 transition-all duration-150 relative group"
    title={tooltip}
  >
    {icon}
    {tooltip && (
      <span className="absolute -top-10 left-1/2 -translate-x-1/2 px-2 py-1 text-xs bg-black/90 text-white rounded opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-50">
        {tooltip}
      </span>
    )}
  </button>
);

const formatBytes = (bytes: number): string => {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
};

export const ControlSection: React.FC<ControlSectionProps> = ({
  onPlay,
  onDownload,
  onExit,
  isDownloading,
  isGameRunning,
  isVersionInstalled,
  latestNeedsUpdate = false,
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
  onCustomDirChange,
  actions
}) => {
  const [isBranchOpen, setIsBranchOpen] = useState(false);
  const [isVersionOpen, setIsVersionOpen] = useState(false);
  const branchDropdownRef = useRef<HTMLDivElement>(null);
  const versionDropdownRef = useRef<HTMLDivElement>(null);

  const openGitHub = () => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism');
  const openBugReport = () => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism/issues/new');
  const openCoffee = () => BrowserOpenURL('https://buymeacoffee.com/yyyumeniku');

  // Close dropdowns on click outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (branchDropdownRef.current && !branchDropdownRef.current.contains(e.target as Node)) {
        setIsBranchOpen(false);
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

  const branchLabel = currentBranch === 'release' ? 'Release' : 'Pre-Release';

  // Calculate width to fit content properly
  const selectorWidth = 'w-[290px]';

  return (
    <div className="flex flex-col gap-3">
      {/* Row 1: Version Selector - spans width of nav buttons below */}
      <div className={`${selectorWidth} h-12 rounded-xl glass border border-white/5 flex items-center`}>
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
            title="Select Branch"
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
            <div className="absolute bottom-full left-0 mb-2 z-[100] min-w-[140px] bg-[#1a1a1a] backdrop-blur-xl border border-white/10 rounded-xl shadow-xl shadow-black/50 overflow-hidden">
              {['release', 'pre-release'].map((branch) => (
                <button
                  key={branch}
                  onClick={() => handleBranchSelect(branch)}
                  className={`w-full px-3 py-2 flex items-center gap-2 text-sm ${
                    currentBranch === branch 
                      ? 'bg-white/20 text-white' 
                      : 'text-white/70 hover:bg-white/10 hover:text-white'
                  }`}
                >
                  {currentBranch === branch && <Check size={14} className="text-white" strokeWidth={3} />}
                  <span className={currentBranch === branch ? '' : 'ml-[22px]'}>{branch === 'release' ? 'Release' : 'Pre-Release'}</span>
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
            className={`
              h-full w-full px-3
              flex items-center justify-center gap-2
              text-white/60 hover:text-[#FFA845] hover:bg-[#FFA845]/10
              disabled:opacity-50 disabled:cursor-not-allowed
              active:scale-95 transition-all duration-150 rounded-r-xl
              ${isVersionOpen ? 'text-[#FFA845] bg-[#FFA845]/10' : ''}
            `}
            title="Select Version"
          >
            <span className="text-sm font-medium">
              {isLoadingVersions ? '...' : currentVersion === 0 ? 'latest' : `v${currentVersion}`}
            </span>
            <ChevronDown 
              size={12} 
              className={`text-white/40 transition-transform duration-150 ${isVersionOpen ? 'rotate-180' : ''}`}
            />
          </button>

          {/* Version Dropdown Menu (opens up) */}
          {isVersionOpen && (
            <div className="absolute bottom-full right-0 mb-2 z-[100] min-w-[120px] max-h-60 overflow-y-auto bg-[#1a1a1a] backdrop-blur-xl border border-white/10 rounded-xl shadow-xl shadow-black/50">
              {availableVersions.length > 0 ? (
                availableVersions.map((version) => {
                  const isInstalled = (installedVersions || []).includes(version) || version === 0;
                  return (
                    <button
                      key={version}
                      onClick={() => handleVersionSelect(version)}
                      className={`w-full px-3 py-2 flex items-center justify-between gap-2 text-sm ${
                        currentVersion === version 
                          ? 'bg-[#FFA845]/20 text-[#FFA845]' 
                          : 'text-white/70 hover:bg-white/10 hover:text-white'
                      }`}
                    >
                      <div className="flex items-center gap-2">
                        {currentVersion === version && (
                          <Check size={14} className="text-[#FFA845]" strokeWidth={3} />
                        )}
                        <span className={currentVersion === version ? '' : 'ml-[22px]'}>
                          {version === 0 ? 'latest' : `v${version}`}
                        </span>
                      </div>
                      {isInstalled && (
                        <span className="text-[10px] px-1.5 py-0.5 rounded bg-green-500/20 text-green-400 font-medium">
                          {version === 0 ? 'latest' : '✓'}
                        </span>
                      )}
                    </button>
                  );
                })
              ) : (
                <div className="px-3 py-2 text-sm text-white/40">No versions</div>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Row 2: Nav buttons */}
      <div className="flex gap-3 items-center">
        <NavBtn onClick={actions.showModManager} icon={<Package size={20} />} tooltip="Mod Manager" />
        <NavBtn onClick={actions.openFolder} icon={<FolderOpen size={20} />} tooltip="Open Instance Folder" />
        <NavBtn 
          onClick={() => {
            if (onCustomDirChange) {
              onCustomDirChange();
            }
          }} 
          icon={<HardDrive size={20} />} 
          tooltip="Change Instance Location" 
        />
        <NavBtn onClick={openGitHub} icon={<Github size={20} />} tooltip="GitHub" />
        <NavBtn onClick={openBugReport} icon={<Bug size={20} />} tooltip="Report Bug" />
        <button
          onClick={openCoffee}
          className="h-12 px-4 rounded-xl glass border border-white/5 flex items-center justify-center gap-2 text-white/60 hover:text-[#FFA845] hover:bg-[#FFA845]/10 active:scale-95 transition-all duration-150"
          title="Buy Me a Coffee"
        >
          <span className="text-sm font-medium">Buy me a</span>
          <Coffee size={20} />
        </button>
        
        {/* Spacer + Disclaimer in center */}
        <div className="flex-1 flex justify-center">
          <p className="text-white/40 text-xs whitespace-nowrap">
            Educational only. Like it? <button onClick={() => BrowserOpenURL('https://hytale.com')} className="text-[#FFA845] font-semibold hover:underline cursor-pointer">BUY IT</button>
          </p>
        </div>

        {/* Play/Download button on right - Fixed width container */}
        <div className="w-[200px] flex justify-end">
          {isGameRunning ? (
            <button
              onClick={onExit}
              className="h-12 px-8 rounded-xl font-black text-xl tracking-tight flex items-center justify-center gap-2 bg-gradient-to-r from-red-600 to-red-500 text-white hover:shadow-lg hover:shadow-red-500/25 hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              <Square size={20} fill="currentColor" />
              <span>EXIT</span>
            </button>
          ) : isDownloading ? (
            <div className="h-12 px-6 rounded-xl bg-[#151515] border border-white/10 flex items-center justify-center gap-4 relative overflow-hidden min-w-[200px]">
              <div 
                className="absolute inset-0 bg-gradient-to-r from-[#FFA845]/30 to-[#FF6B35]/30 transition-all duration-300"
                style={{ width: `${Math.min(progress, 100)}%` }}
              />
              <div className="relative z-10 flex items-center gap-3">
                <span className="text-lg font-bold text-white">{Math.round(progress)}%</span>
                {total > 0 && (
                  <span className="text-xs text-gray-400">
                    {formatBytes(downloaded)} / {formatBytes(total)}
                  </span>
                )}
              </div>
            </div>
          ) : isCheckingInstalled ? (
            <button
              disabled
              className="h-12 px-8 rounded-xl font-black text-xl tracking-tight flex items-center justify-center gap-2 bg-white/10 text-white/50 cursor-not-allowed"
            >
              <Loader2 size={20} className="animate-spin" />
              <span>CHECKING...</span>
            </button>
          ) : latestNeedsUpdate && currentVersion === 0 ? (
            <button
              onClick={onDownload}
              className="h-12 px-8 rounded-xl font-black text-xl tracking-tight flex items-center justify-center gap-2 bg-gradient-to-r from-blue-500 to-blue-600 text-white hover:shadow-lg hover:shadow-blue-500/25 hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              <Download size={20} />
              <span>UPDATE</span>
            </button>
          ) : isVersionInstalled ? (
            <button
              onClick={onPlay}
              className="h-12 px-8 rounded-xl font-black text-xl tracking-tight flex items-center justify-center gap-2 bg-gradient-to-r from-[#FFA845] to-[#FF6B35] text-white hover:shadow-lg hover:shadow-[#FFA845]/25 hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              <Play size={20} fill="currentColor" />
              <span>PLAY</span>
            </button>
          ) : (
            <button
              onClick={onDownload}
              className="h-12 px-8 rounded-xl font-black text-xl tracking-tight flex items-center justify-center gap-2 bg-gradient-to-r from-green-500 to-emerald-600 text-white hover:shadow-lg hover:shadow-green-500/25 hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 cursor-pointer"
            >
              <Download size={20} />
              <span>DOWNLOAD</span>
            </button>
          )}
        </div>
      </div>
    </div>
  );
};
