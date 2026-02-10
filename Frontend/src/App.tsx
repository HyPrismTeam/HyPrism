import React, { useState, useEffect, useRef, lazy, Suspense, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence } from 'framer-motion';
import { ipc, on, NewsItem } from '@/lib/ipc';
import { GameBranch } from './constants/enums';
import { BackgroundImage } from './components/layout/BackgroundImage';
import { MusicPlayer } from './components/layout/MusicPlayer';
import { UpdateOverlay } from './components/layout/UpdateOverlay';
import { DockMenu } from './components/layout/DockMenu';
import type { PageType } from './components/layout/DockMenu';
import { DashboardPage } from './pages/DashboardPage';
import { NewsPage } from './pages/NewsPage';
import { ProfilesPage } from './pages/ProfilesPage';
import { InstancesPage } from './pages/InstancesPage';
import { SettingsPage } from './pages/SettingsPage';
import { BrowseModsModal } from './components/BrowseModsModal';
// Controller detection removed - not using floating indicator

// Lazy load heavy modals for better initial load performance
const NewsPreview = lazy(() => import('./components/NewsPreview').then(m => ({ default: m.NewsPreview })));
const ErrorModal = lazy(() => import('./components/modals/ErrorModal').then(m => ({ default: m.ErrorModal })));
const DeleteConfirmationModal = lazy(() => import('./components/modals/DeleteConfirmationModal').then(m => ({ default: m.DeleteConfirmationModal })));
const UpdateConfirmationModal = lazy(() => import('./components/modals/UpdateConfirmationModal').then(m => ({ default: m.UpdateConfirmationModal })));
const OnboardingModal = lazy(() => import('./components/modals/OnboardingModal').then(m => ({ default: m.OnboardingModal })));

// VersionStatus type (was in api/backend)
type VersionStatus = {
  status: 'installed' | 'update_available' | 'not_installed' | 'unknown';
  installedVersion?: number;
  latestVersion?: number;
};

// Functions that map to real IPC channels
const _BrowserOpenURL = (url: string) => ipc.browser.open(url);
const WindowClose = () => ipc.windowCtl.close();
const CancelDownload = () => ipc.game.cancel();
const GetNews = (_count: number): Promise<NewsItem[]> => ipc.news.get();

function EventsOn(event: string, cb: (...args: any[]) => void): () => void {
  const channelMap: Record<string, string> = {
    'progress-update': 'hyprism:game:progress',
    'game-state': 'hyprism:game:state',
    'error': 'hyprism:game:error',
    'update:available': 'hyprism:update:available',
    'update:progress': 'hyprism:update:progress',
  };
  const channel = channelMap[event] ?? event;
  return on(channel, (data: unknown) => cb(data));
}

// Settings-backed getters
async function GetBackgroundMode(): Promise<string> { return (await ipc.settings.get()).backgroundMode ?? 'image'; }
async function GetMusicEnabled(): Promise<boolean> { return (await ipc.settings.get()).musicEnabled ?? true; }
async function GetAccentColor(): Promise<string> { return (await ipc.settings.get()).accentColor ?? '#FF6B2B'; }
async function GetCloseAfterLaunch(): Promise<boolean> { return (await ipc.settings.get()).closeAfterLaunch ?? false; }
async function GetDisableNews(): Promise<boolean> { return (await ipc.settings.get()).disableNews ?? false; }
async function GetHasCompletedOnboarding(): Promise<boolean> { return (await ipc.settings.get()).hasCompletedOnboarding ?? false; }
async function GetLauncherBranch(): Promise<string> { return (await ipc.settings.get()).launcherBranch ?? 'release'; }
async function GetLauncherVersion(): Promise<string> { return (await ipc.settings.get()).launcherBranch ?? ''; }

// Profile-backed getters
async function GetNick(): Promise<string> { return (await ipc.profile.get()).nick ?? 'HyPrism'; }
async function GetUUID(): Promise<string> { return (await ipc.profile.get()).uuid ?? ''; }
async function SetNick(_name: string): Promise<void> { console.warn('[IPC] SetNick: no dedicated channel yet'); }

// Game actions - map to ipc.game where possible, stub rest
function DownloadAndLaunch(_username?: string): void { ipc.game.launch(); }
function DownloadOnly(): void { ipc.game.launch(); }
function LaunchOnly(): void { ipc.game.launch(); }

// TODO: These need dedicated IPC channels in IpcService.cs
const stub = <T,>(name: string, fallback: T) => async (..._args: any[]): Promise<T> => {
  console.warn(`[IPC] ${name}: no IPC channel yet`);
  return fallback;
};
const _OpenInstanceFolder = stub('OpenInstanceFolder', undefined as void);
const DeleteGame = stub('DeleteGame', false);
const Update = stub('Update', undefined as void);
const ExitGame = stub('ExitGame', undefined as void);
const GetRecentLogs = stub<string[]>('GetRecentLogs', []);

// Real IPC implementation for Version Persistence
async function GetVersionType(): Promise<string> { 
  return (await ipc.settings.get()).versionType as string ?? 'release'; 
}
async function SetVersionType(type: string): Promise<void> { 
  await ipc.settings.update({ versionType: type });
}
async function SetSelectedVersion(version: number): Promise<void> {
  await ipc.settings.update({ selectedVersion: version });
}

// Real IPC call to check if game is running
async function IsGameRunning(): Promise<boolean> {
  try {
    return await ipc.game.isRunning();
  } catch (e) {
    console.error('[IPC] IsGameRunning failed:', e);
    return false;
  }
}

// Get available versions from server (includes versions for download)
async function GetVersionList(branch: string): Promise<number[]> {
  try {
    // Get available versions from server
    const availableVersions = await ipc.game.versions({ branch });
    console.log('[IPC] GetVersionList: available versions for', branch, ':', availableVersions);
    // Always include "latest" (0) as an option at the start
    if (!availableVersions.includes(0)) {
      availableVersions.unshift(0);
    }
    return availableVersions;
  } catch (e) {
    console.error('[IPC] GetVersionList failed:', e);
    return [0];
  }
}

async function IsVersionInstalled(branch: string, version: number): Promise<boolean> {
  try {
    const instances = await ipc.game.instances();
    console.log('[IPC] IsVersionInstalled: checking', branch, version, 'in', instances);
    // Version 0 means "latest" - check if there's at least one instance of the branch that IS VALID
    if (version === 0) {
      return instances.some(inst => inst.branch === branch && inst.isValid);
    }
    return instances.some(inst => inst.branch === branch && inst.version === version && inst.isValid);
  } catch (e) {
    console.error('[IPC] IsVersionInstalled failed:', e);
    return false;
  }
}

export interface InstalledVersionInfo {
  version: number;
  isValid: boolean;
}

async function GetInstalledVersionsForBranch(branch: string): Promise<InstalledVersionInfo[]> {
  try {
    const instances = await ipc.game.instances();
    console.log('[IPC] GetInstalledVersionsForBranch: for', branch, 'found', instances);
    return instances
      .filter(inst => inst.branch === branch)
      .map(inst => ({ version: inst.version, isValid: inst.isValid }))
      .sort((a, b) => b.version - a.version); // Sort descending
  } catch (e) {
    console.error('[IPC] GetInstalledVersionsForBranch failed:', e);
    return [];
  }
}

const GetLatestVersionStatus = stub<VersionStatus | null>('GetLatestVersionStatus', null);
const _ForceUpdateLatest = stub('ForceUpdateLatest', undefined as void);
const DuplicateLatest = stub('DuplicateLatest', true);
const GetPendingUpdateInfo = stub<{ HasOldUserData: boolean; OldVersion: number; NewVersion: number; Branch: string } | null>('GetPendingUpdateInfo', null);
const CopyUserData = stub('CopyUserData', false);
const GetCustomInstanceDir = stub('GetCustomInstanceDir', '');
const SetInstanceDirectory = stub('SetInstanceDirectory', '');
const GetWrapperStatus = stub<null>('GetWrapperStatus', null);
const WrapperInstallLatest = stub('WrapperInstallLatest', true);
const WrapperLaunch = stub('WrapperLaunch', true);
const SetLauncherBranch = stub<void>('SetLauncherBranch', undefined as void);
const CheckRosettaStatus = stub<{ NeedsInstall: boolean; Message: string; Command: string; TutorialUrl?: string } | null>('CheckRosettaStatus', null);
const _GetDiscordLink = stub('GetDiscordLink', 'https://discord.gg/hyprism');
import appIcon from './assets/images/appicon.png';

// Modal loading fallback - minimal spinner
const ModalFallback = () => (
  <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
    <div className="w-8 h-8 border-2 border-white/20 border-t-white rounded-full animate-spin" />
  </div>
);

const withLatest = (versions: number[] | null | undefined): number[] => {
  const base = Array.isArray(versions) ? versions : [];
  return base.includes(0) ? base : [0, ...base];
};

const normalizeBranch = (branch: string | null | undefined): string => {
  return branch === GameBranch.PRE_RELEASE ? GameBranch.PRE_RELEASE : GameBranch.RELEASE;
};

const parseDateMs = (dateValue: string | number | Date | undefined): number => {
  if (!dateValue) return 0;
  const ms = new Date(dateValue).getTime();
  return Number.isNaN(ms) ? 0 : ms;
};

const formatDateConsistent = (dateMs: number, locale = 'en-US') => {
  return new Date(dateMs).toLocaleDateString(locale, {
    day: 'numeric',
    month: 'long',
    year: 'numeric'
  });
};

const fetchLauncherReleases = async (locale: string) => {
  try {
    const res = await fetch('https://api.github.com/repos/yyyumeniku/HyPrism/releases?per_page=100');
    if (!res.ok) return [] as Array<{ item: any; dateMs: number }>;
    const data = await res.json();
    return (Array.isArray(data) ? data : []).map((r: any) => {
      const rawName = (r?.name || r?.tag_name || '').toString();
      const cleaned = rawName.replace(/[()]/g, '').trim();
      const dateMs = parseDateMs(r?.published_at || r?.created_at);
      return {
        item: {
          title: `Hyprism ${cleaned || 'Release'} release`,
          excerpt: `Hyprism ${cleaned || 'Release'} release — click to see changelog.`,
          url: r?.html_url || 'https://github.com/yyyumeniku/HyPrism/releases',
          date: formatDateConsistent(dateMs || Date.now(), locale),
          author: 'HyPrism',
          imageUrl: appIcon,
          source: 'hyprism' as const,
        },
        dateMs
      };
    });
  } catch {
    return [] as Array<{ item: any; dateMs: number }>;
  }
};



const App: React.FC = () => {
  const { t, i18n } = useTranslation();
  // User state
  const [username, setUsername] = useState<string>("HyPrism");
  const [uuid, setUuid] = useState<string>("");
  const [isEditing, setIsEditing] = useState<boolean>(false);
  const [launcherVersion, setLauncherVersion] = useState<string>("dev");

  // Download state
  const [progress, setProgress] = useState<number>(0);
  const [isDownloading, setIsDownloading] = useState<boolean>(false);
  const [downloadState, setDownloadState] = useState<'downloading' | 'extracting' | 'launching'>('downloading');
  const [isGameRunning, setIsGameRunning] = useState<boolean>(false);
  const [runningBranch, setRunningBranch] = useState<string | undefined>(undefined);
  const [runningVersion, setRunningVersion] = useState<number | undefined>(undefined);
  const [downloaded, setDownloaded] = useState<number>(0);
  const [total, setTotal] = useState<number>(0);
  const [launchState, setLaunchState] = useState<string>('');
  const [launchDetail, setLaunchDetail] = useState<string>('');

  // Update state
  const [updateAsset, setUpdateAsset] = useState<any>(null);
  const [isUpdatingLauncher, setIsUpdatingLauncher] = useState<boolean>(false);
  const [updateStats, setUpdateStats] = useState({ d: 0, t: 0 });

  // Modal state
  const [showDelete, setShowDelete] = useState<boolean>(false);
  const [showModBrowser, setShowModBrowser] = useState<boolean>(false);
  const [modBrowserInstance, setModBrowserInstance] = useState<{ branch: string; version: number } | null>(null);
  const [error, setError] = useState<any>(null);
  const [launchTimeoutError, setLaunchTimeoutError] = useState<{ message: string; logs: string[] } | null>(null);
  const [avatarRefreshTrigger, setAvatarRefreshTrigger] = useState<number>(0);

  // Page navigation state
  const [currentPage, setCurrentPage] = useState<PageType>('dashboard');

  // Settings state
  const [launcherBranch, setLauncherBranch] = useState<string>('release');
  const [rosettaWarning, setRosettaWarning] = useState<{ message: string; command: string; tutorialUrl?: string } | null>(null);

  // Game launch tracking
  const gameLaunchTimeRef = useRef<number | null>(null);

  // Version state
  const [currentBranch, setCurrentBranch] = useState<string>(GameBranch.RELEASE);
  const [currentVersion, setCurrentVersion] = useState<number>(0);
  // Refs to track current values for event listeners
  const currentBranchRef = useRef<string>(GameBranch.RELEASE);
  const currentVersionRef = useRef<number>(0);
  const [availableVersions, setAvailableVersions] = useState<number[]>([]);
  const [installedVersions, setInstalledVersions] = useState<InstalledVersionInfo[]>([]);
  const [isLoadingVersions, setIsLoadingVersions] = useState<boolean>(false);
  const [isVersionInstalled, setIsVersionInstalled] = useState<boolean>(false);
  const [isCheckingInstalled, setIsCheckingInstalled] = useState<boolean>(false);
  const [customInstanceDir, setCustomInstanceDir] = useState<string>("");
  const [versionStatus, setVersionStatus] = useState<VersionStatus | null>(null);

  // Background, news, and accent color settings
  const [backgroundMode, setBackgroundMode] = useState<string>('slideshow');
  const [newsDisabled, setNewsDisabled] = useState<boolean>(false);
  const [_accentColor, setAccentColor] = useState<string>('#FFA845'); // Used only for SettingsModal callback
  const [isMuted, setIsMuted] = useState<boolean>(false);
  
  const handleToggleMute = useCallback(() => {
     setIsMuted(prev => {
       const newState = !prev;
       ipc.settings.update({ musicEnabled: !newState });
       return newState;
     });
  }, []);

  // Keep refs in sync with state for event listeners
  useEffect(() => {
    currentBranchRef.current = currentBranch;
  }, [currentBranch]);

  useEffect(() => {
    currentVersionRef.current = currentVersion;
  }, [currentVersion]);

  // Onboarding state
  const [showOnboarding, setShowOnboarding] = useState<boolean>(false);
  const [onboardingChecked, setOnboardingChecked] = useState<boolean>(false);

  // Wrapper mode state (for Flatpak/AppImage wrapper delivery)
  const isWrapperMode = typeof window !== 'undefined' && (window.location.search.includes('wrapper=1') || window.location.search.includes('wrapper=true'));
  const [wrapperStatus, setWrapperStatus] = useState<null | {
    installed: boolean;
    installedVersion: string;
    latestVersion: string;
    updateAvailable: boolean;
    downloadUrl: string;
    assetName: string;
    message: string;
  }>(null);
  const [isWrapperWorking, setIsWrapperWorking] = useState<boolean>(false);

  const refreshWrapperStatus = async () => {
    try {
      const s = await GetWrapperStatus();
      setWrapperStatus(s as any);
    } catch (e) {
      console.error('Wrapper status failed', e);
    }
  };

  useEffect(() => {
    if (!isWrapperMode) return;
    refreshWrapperStatus();
  }, []);

  // Pending game update modal
  const [pendingUpdate, setPendingUpdate] = useState<{
    oldVersion: number;
    newVersion: number;
    hasOldUserData: boolean;
    branch: string;
  } | null>(null);

  // Check if current version is installed when branch or version changes
  useEffect(() => {
    console.log('[checkInstalled] Effect triggered, currentVersion:', currentVersion, 'currentBranch:', currentBranch);
    const checkInstalled = async () => {
      if (currentVersion === 0) {
        console.log('[checkInstalled] currentVersion is 0, checking latest instance...');
        // Version 0 is the auto-updating "latest" instance
        // Check if it's actually installed
        setIsCheckingInstalled(true);
        try {
          const installed = await IsVersionInstalled(currentBranch, 0);
          console.log('[checkInstalled] IsVersionInstalled result:', installed);
          setIsVersionInstalled(installed);
          // Get version status (not_installed, update_available, or current)
          console.log('[checkInstalled] Calling GetLatestVersionStatus...');
          const status = await GetLatestVersionStatus(currentBranch);
          console.log('[checkInstalled] GetLatestVersionStatus response:', status);
          setVersionStatus(status);
        } catch (e) {
          console.error('Failed to check latest instance:', e);
          setIsVersionInstalled(false);
          setVersionStatus(null);
        }
        setIsCheckingInstalled(false);
        return;
      }
      if (currentVersion < 0) {
        // Uninitialized or invalid version
        setIsVersionInstalled(false);
        setVersionStatus(null);
        return;
      }
      setIsCheckingInstalled(true);
      try {
        const installed = await IsVersionInstalled(currentBranch, currentVersion);
        setIsVersionInstalled(installed);
        // For historical versions, still get version status to check if duplication is possible
        const status = await GetLatestVersionStatus(currentBranch);
        setVersionStatus(status);
      } catch (e) {
        console.error('Failed to check if version installed:', e);
        setIsVersionInstalled(false);
        setVersionStatus(null);
      }
      setIsCheckingInstalled(false);
    };
    checkInstalled();
  }, [currentBranch, currentVersion]);

  // Load version list when branch changes
  useEffect(() => {
    const loadVersions = async () => {
      setIsLoadingVersions(true);
      try {
        const versions = await GetVersionList(currentBranch);
        setAvailableVersions(withLatest(versions || []));

        // Load installed versions
        const installed = await GetInstalledVersionsForBranch(currentBranch);
        const latestInstalled = await IsVersionInstalled(currentBranch, 0);
        const installedWithLatest = [...(installed || [])];
        if (latestInstalled && !installedWithLatest.some(v => v.version === 0)) installedWithLatest.unshift({ version: 0, isValid: true });
        setInstalledVersions(installedWithLatest);

        // If current version is not valid for this branch, set to latest
        if (currentVersion !== 0 && versions && !versions.includes(currentVersion) && versions.length > 0) {
          setCurrentVersion(0);
          await SetSelectedVersion(0);
        }
      } catch (e) {
        console.error('Failed to load versions:', e);
        setAvailableVersions([]);
        setInstalledVersions([]);
      }
      setIsLoadingVersions(false);
    };
    loadVersions();
  }, [currentBranch]);

  // Handle branch change - immediately load and set latest version for new branch
  const handleBranchChange = async (branch: string) => {
    setCurrentBranch(branch);
    await SetVersionType(branch);

    // Load versions for new branch and set to latest (version 0)
    setIsLoadingVersions(true);
    try {
      const versions = await GetVersionList(branch);
      setAvailableVersions(withLatest(versions));

      const installed = await GetInstalledVersionsForBranch(branch);
      const latestInstalled = await IsVersionInstalled(branch, 0);
      const installedWithLatest = [...(installed || [])];
      if (latestInstalled && !installedWithLatest.some(v => v.version === 0)) installedWithLatest.unshift({ version: 0, isValid: true });
      setInstalledVersions(installedWithLatest);

      // Always set to "latest" (version 0) when switching branches
      setCurrentVersion(0);
      await SetSelectedVersion(0);
    } catch (e) {
      console.error('Failed to load versions for branch:', e);
    }
    setIsLoadingVersions(false);
  };

  // Handle version change
  const handleVersionChange = async (version: number) => {
    setCurrentVersion(version);
    await SetSelectedVersion(version);
  };

  // Check for existing game process on startup
  useEffect(() => {
    const checkExistingGame = async () => {
      try {
        const running = await IsGameRunning();
        if (running) {
          console.log('[App] Found existing game process, connecting...');
          setIsGameRunning(true);
          setLaunchState('running');
        }
      } catch (e) {
        console.error('[App] Failed to check existing game process:', e);
      }
    };
    checkExistingGame();
  }, []);

  // Game state polling with launch timeout detection
  useEffect(() => {
    if (!isGameRunning) {
      gameLaunchTimeRef.current = null;
      return;
    }

    // Record when the game was launched
    if (!gameLaunchTimeRef.current) {
      gameLaunchTimeRef.current = Date.now();
    }

    const pollInterval = setInterval(async () => {
      try {
        const running = await IsGameRunning();
        if (!running) {
          // Just update state - error handling is done by game-state event with exit code
          setIsGameRunning(false);
          setProgress(0);
          gameLaunchTimeRef.current = null;
        }
      } catch (e) {
        console.error('Failed to check game state:', e);
      }
    }, 3000); // Check every 3 seconds

    return () => clearInterval(pollInterval);
  }, [isGameRunning, t]);

  // Reload profile data from backend
  const reloadProfile = async () => {
    const nick = await GetNick();
    if (nick) setUsername(nick);
    const uuid = await GetUUID();
    if (uuid) setUuid(uuid);
    // Trigger avatar refresh
    setAvatarRefreshTrigger(prev => prev + 1);
  };

  // Check if onboarding has been completed
  useEffect(() => {
    const checkOnboarding = async () => {
      try {
        const completed = await GetHasCompletedOnboarding();
        if (!completed) {
          setShowOnboarding(true);
        }
        setOnboardingChecked(true);
      } catch (err) {
        console.error('Failed to check onboarding status:', err);
        setOnboardingChecked(true); // Continue even if check fails
      }
    };
    checkOnboarding();
  }, []);

  // Handle onboarding completion
  const handleOnboardingComplete = async () => {
    setShowOnboarding(false);
    // Reload profile data after onboarding
    await reloadProfile();
    // Reload instance directory
    try {
      const dir = await GetCustomInstanceDir();
      if (dir) setCustomInstanceDir(dir);
    } catch { /* ignore */ }
    // Reload background mode (user may have changed it in onboarding)
    try {
      const mode = await GetBackgroundMode();
      setBackgroundMode(mode || 'slideshow');
    } catch { /* ignore */ }
  };

  useEffect(() => {
    // Initialize user settings
    GetNick().then((n: string) => n && setUsername(n));
    GetUUID().then((u: string) => u && setUuid(u));
    GetLauncherVersion().then((v: string) => setLauncherVersion(v));
    GetCustomInstanceDir().then((dir: string) => dir && setCustomInstanceDir(dir));

    // Check if game is already running on startup
    IsGameRunning().then((running: boolean) => {
      console.log('[App] Initial game running state:', running);
      if (running) {
        setIsGameRunning(true);
      }
    });

    // Load background mode, news settings, and accent color
    GetBackgroundMode().then((mode: string) => setBackgroundMode(mode || 'slideshow'));
    GetDisableNews().then((disabled: boolean) => setNewsDisabled(disabled));
    GetAccentColor().then((color: string) => setAccentColor(color || '#FFA845'));
    GetMusicEnabled().then((enabled: boolean) => setIsMuted(!enabled));

    // Load saved branch and version - must load branch first, then version
    const loadSettings = async () => {
      try {
        // Get saved branch (defaults to "release" in backend if not set)
        const savedBranch = await GetVersionType();
        const branch = normalizeBranch(savedBranch || GameBranch.RELEASE);
        setCurrentBranch(branch);

        // Load launcher branch (release/beta channel)
        try {
          const savedLauncherBranch = await GetLauncherBranch();
          setLauncherBranch(savedLauncherBranch || 'release');
        } catch (e) {
          console.error('Failed to load launcher branch:', e);
        }

        // Check Rosetta status on macOS
        try {
          const rosettaStatus = await CheckRosettaStatus();
          if (rosettaStatus && rosettaStatus.NeedsInstall) {
            setRosettaWarning({
              message: rosettaStatus.Message,
              command: rosettaStatus.Command,
              tutorialUrl: rosettaStatus.TutorialUrl || undefined
            });
          }
        } catch (e) {
          console.error('Failed to check Rosetta status:', e);
        }

        // Load version list for this branch
        setIsLoadingVersions(true);
        const versions = await GetVersionList(branch);
        setAvailableVersions(withLatest(versions));

        // Load installed versions
        const installed = await GetInstalledVersionsForBranch(branch);
        const latestInstalled = await IsVersionInstalled(branch, 0);
        const installedWithLatest = [...(installed || [])];
        if (latestInstalled && !installedWithLatest.some(v => v.version === 0)) installedWithLatest.unshift({ version: 0, isValid: true });
        setInstalledVersions(installedWithLatest);

        // Check if "latest" (version 0) is installed first

        if (latestInstalled) {
          // Use latest if installed
          setCurrentVersion(0);
          await SetSelectedVersion(0);
          setIsVersionInstalled(true);
          // Get version status for latest instance
          try {
            const status = await GetLatestVersionStatus(branch);
            console.log('loadSettings: GetLatestVersionStatus response:', status);
            setVersionStatus(status);
          } catch (e) {
            console.error('Failed to get version status:', e);
            setVersionStatus(null);
          }
        } else if (installed && installed.length > 0) {
          // If latest not installed but other versions exist, select the highest installed version
          const highestInstalled = Math.max(...installed.map(x => x.version).filter(v => v > 0));
          if (highestInstalled > 0) {
            setCurrentVersion(highestInstalled);
            await SetSelectedVersion(highestInstalled);
            setIsVersionInstalled(true);
          } else {
            // Only version 0 in the list but not actually installed
            setCurrentVersion(0);
            await SetSelectedVersion(0);
            setIsVersionInstalled(false);
          }
        } else {
          // No versions installed, default to latest
          setCurrentVersion(0);
          await SetSelectedVersion(0);
          setIsVersionInstalled(false);
          // Get version status even if not installed (will show not_installed)
          try {
            const status = await GetLatestVersionStatus(branch);
            console.log('loadSettings (not installed): GetLatestVersionStatus response:', status);
            setVersionStatus(status);
          } catch (e) {
            console.error('Failed to get version status:', e);
            setVersionStatus(null);
          }
        }

        setIsLoadingVersions(false);
      } catch (e) {
        console.error('Failed to load settings:', e);
        setIsLoadingVersions(false);
      }
    };
    loadSettings();

    // Wrapper mode skips event listener setup
    if (isWrapperMode) return;

    // Event listeners
    const unsubProgress = EventsOn('progress-update', (data: any) => {
      // Handle cancellation first
      if (data.state === 'cancelled') {
        console.log('Download cancelled event received');
        setIsDownloading(false);
        setProgress(0);
        setDownloaded(0);
        setTotal(0);
        setLaunchState('');
        setLaunchDetail('');
        setDownloadState('downloading');
        return;
      }

      setProgress(data.progress ?? 0);
      setDownloaded(data.downloadedBytes ?? 0);
      setTotal(data.totalBytes ?? 0);
      setLaunchState(data.state ?? '');
      
      // Build launch detail from messageKey and args
      const key = data.messageKey || '';
      const args = Array.isArray(data.args) ? data.args : [];
      // Simple interpolation: replace {0}, {1}, etc. with args (use regex for global replace)
      let detail = key;
      args.forEach((arg: any, i: number) => {
        detail = detail.replace(new RegExp(`\\{${i}\\}`, 'g'), String(arg));
      });
      setLaunchDetail(detail);

      // Update download state based on state field
      if (data.state === 'download' || data.state === 'update') {
        setDownloadState('downloading');
      } else if (data.state === 'install') {
        setDownloadState('extracting');
      } else if (data.state === 'complete') {
        console.log('[App] Progress complete state received, waiting for game-state event');
        setDownloadState('launching');
        // Game is now installed, update state
        if (data.progress >= 100) {
          setIsVersionInstalled(true);
          // Update version status for latest instance so DUPLICATE button appears
          if (currentVersionRef.current === 0) {
            GetLatestVersionStatus(currentBranchRef.current).then(status => {
              setVersionStatus(status);
            }).catch(err => {
              console.error('Failed to get version status after download:', err);
            });
          }
        }
      } else if (data.state === 'launch' || data.state === 'launching') {
        // Game is launching - set running immediately for better UX
        console.log('[App] Launch/launching state received, setting isGameRunning=true');
        setIsGameRunning(true);
        setIsDownloading(false);
        setProgress(0);
        setLaunchState('');
        setLaunchDetail('');
      } else {
        // Fallback to progress-based detection
        if (data.progress >= 0 && data.progress < 70) {
          setDownloadState('downloading');
        } else if (data.progress >= 70 && data.progress < 100) {
          setDownloadState('extracting');
        } else if (data.progress >= 100) {
          setDownloadState('launching');
        }
      }
    });
    
    // Game state event listener
    const unsubGameState = EventsOn('game-state', async (data: any) => {
      console.log('[App] Game state event received:', data);
      if (data.state === 'started') {
        console.log('[App] Game started, resetting download state');
        setIsGameRunning(true);
        // Track which instance is running
        setRunningBranch(currentBranchRef.current);
        setRunningVersion(currentVersionRef.current);
        setIsDownloading(false);
        setProgress(0);
        setLaunchState('');
        setLaunchDetail('');
        
        // Check if close after launch is enabled
        try {
          const closeAfterLaunch = await GetCloseAfterLaunch();
          if (closeAfterLaunch) {
            // Small delay to ensure game has started
            setTimeout(() => {
              WindowClose();
            }, 1000);
          }
        } catch (err) {
          console.error('Failed to check close after launch:', err);
        }
      } else if (data.state === 'stopped') {
        console.log('[App] Game stopped, exitCode:', data.exitCode);
        // Only show error if exit code is non-zero (crash/error)
        // Exit code 0 or undefined = normal exit, null = unknown
        const exitCode = data.exitCode;
        if (exitCode !== undefined && exitCode !== null && exitCode !== 0) {
          try {
            const logs = await GetRecentLogs(10);
            setLaunchTimeoutError({
              message: t('app.gameCrashed', { code: exitCode }),
              logs: logs || []
            });
          } catch {
            setLaunchTimeoutError({
              message: t('app.gameCrashed', { code: exitCode }),
              logs: []
            });
          }
        }
        setIsGameRunning(false);
        setRunningBranch(undefined);
        setRunningVersion(undefined);
        setIsDownloading(false);
        setProgress(0);
        setLaunchState('');
        setLaunchDetail('');
        gameLaunchTimeRef.current = null; // Clear launch time to prevent polling error
      }
    });

    const unsubUpdate = EventsOn('update:available', (asset: any) => {
      setUpdateAsset(asset);
      // Don't auto-update - let user click the update button
      console.log('Update available:', asset);
    });

    const unsubUpdateProgress = EventsOn('update:progress', (_stage: string, progress: number, _message: string, _file: string, _speed: string, downloaded: number, total: number) => {
      setProgress(progress);
      setUpdateStats({ d: downloaded, t: total });
    });

    const unsubError = EventsOn('error', (err: any) => {
      setError(err);
      setIsDownloading(false);
    });

    return () => {
      unsubProgress();
      unsubGameState();
      unsubUpdate();
      unsubUpdateProgress();
      unsubError();
    };
  }, []);

  const handleUpdate = async () => {
    setIsUpdatingLauncher(true);
    setProgress(0);
    setUpdateStats({ d: 0, t: 0 });

    try {
      await Update();
      setError({
        type: 'INFO',
        message: t('app.downloadedUpdate'),
        technical: t('app.downloadedUpdateHint'),
        timestamp: new Date().toISOString()
      });
    } catch (err) {
      console.error('Update failed:', err);
      setError({
        type: 'UPDATE_ERROR',
        message: t('app.failedUpdate'),
        technical: err instanceof Error ? err.message : String(err),
        timestamp: new Date().toISOString()
      });
    } finally {
      setIsUpdatingLauncher(false);
    }
  };

  const handlePlay = async () => {
    // Prevent launching if game is already running or download is in progress
    if (isGameRunning) {
      console.log('[App] Game already running, ignoring play request');
      return;
    }
    if (isDownloading) {
      console.log('[App] Download already in progress, ignoring play request');
      return;
    }

    const trimmedUsername = username.trim();
    if (!trimmedUsername || trimmedUsername.length < 1 || trimmedUsername.length > 16) {
      setError({
        type: 'VALIDATION',
        message: t('app.invalidNickname'),
        technical: t('app.nicknameLengthError'),
        timestamp: new Date().toISOString(),
        launcherVersion: launcherVersion
      });
      return;
    }

    // Check if using "Latest" and there's a pending update with userdata
    if (currentVersion === 0) {
      try {
        const updateInfo = await GetPendingUpdateInfo(currentBranch);
        if (updateInfo && updateInfo.HasOldUserData) {
          // Show the update confirmation modal
          setPendingUpdate({
            oldVersion: updateInfo.OldVersion,
            newVersion: updateInfo.NewVersion,
            hasOldUserData: updateInfo.HasOldUserData,
            branch: updateInfo.Branch
          });
          return; // Don't proceed, wait for modal decision
        }
      } catch (err) {
        console.error('Failed to check pending update:', err);
        // Continue anyway if check fails
      }
    }

    // If there's an update available and user is on latest (version 0), 
    // use LaunchOnly to skip the update check
    if (currentVersion === 0 && versionStatus?.status === 'update_available') {
      doLaunchOnly();
    } else {
      doLaunch();
    }
  };

  const doLaunchOnly = async () => {
    setIsDownloading(true);
    setDownloadState('launching');
    try {
      await LaunchOnly();
      // Button state will be managed by progress events
    } catch (err) {
      console.error('Launch failed:', err);
      setIsDownloading(false);
    }
  };

  const doLaunch = async () => {
    setIsDownloading(true);
    setDownloadState('downloading');
    try {
      await DownloadAndLaunch(username);
      // Button state will be managed by progress events:
      // - 'launch' event sets isGameRunning=true and isDownloading=false
      // - 'error' event sets isDownloading=false
    } catch (err) {
      console.error('Launch failed:', err);
      setIsDownloading(false);
    }
  };

  const handleGameUpdate = async () => {
    // Trigger update/download for the latest instance
    setIsDownloading(true);
    setDownloadState('downloading');
    try {
      // Download/update the latest version without launching
      await DownloadOnly();
      // Button state will be managed by progress events
      // After download completes, refresh version status
      const status = await GetLatestVersionStatus(currentBranch);
      setVersionStatus(status);
    } catch (err) {
      console.error('Update failed:', err);
      setIsDownloading(false);
    }
  };

  const handleGameDuplicate = async () => {
    try {
      const result = await DuplicateLatest(currentBranch);
      if (result) {
        // Refresh the installed versions list
        const installed = await GetInstalledVersionsForBranch(currentBranch);
        const latestInstalled = await IsVersionInstalled(currentBranch, 0);
        const installedWithLatest = [...(installed || [])];
        if (latestInstalled && !installedWithLatest.some(v => v.version === 0)) {
          installedWithLatest.unshift({ version: 0, isValid: true });
        }
        setInstalledVersions(installedWithLatest);
      }
    } catch (err) {
      console.error('Failed to duplicate latest:', err);
    }
  };

  const handleUpdateConfirmWithCopy = async () => {
    if (!pendingUpdate) return;
    try {
      // Copy userdata from old version (0 = latest instance) to the new version
      await CopyUserData(pendingUpdate.branch, 0, pendingUpdate.newVersion);
    } catch (err) {
      console.error('Failed to copy userdata:', err);
    }
    setPendingUpdate(null);
    doLaunch();
  };

  const handleUpdateConfirmWithoutCopy = async () => {
    setPendingUpdate(null);
    doLaunch();
  };

  const handleUpdateCancel = () => {
    setPendingUpdate(null);
  };

  const handleCancelDownload = async () => {
    console.log('Cancel download requested');
    // Immediately update UI to show cancellation is happening
    setDownloadState('downloading');
    try {
      const result = await CancelDownload();
      console.log('Cancel download result:', result);
      // Reset state immediately on successful cancel call
      setIsDownloading(false);
      setProgress(0);
      setDownloaded(0);
      setTotal(0);
    } catch (err) {
      console.error('Cancel failed:', err);
      // Still try to reset UI state even if call fails
      setIsDownloading(false);
      setProgress(0);
      setDownloaded(0);
      setTotal(0);
    }
  };

  const handleNickChange = async (newNick: string) => {
    setUsername(newNick);
    await SetNick(newNick);
  };


  const handleExit = async () => {
    try {
      await ExitGame();
    } catch (err) {
      console.error('Failed to exit game:', err);
    }
    setIsGameRunning(false);
    setProgress(0);
  };

  const handleLauncherBranchChange = async (branch: string) => {
    try {
      await SetLauncherBranch(branch);
      setLauncherBranch(branch);
      console.log('Launcher branch changed to:', branch);
    } catch (err) {
      console.error('Failed to change launcher branch:', err);
    }
  };

  const _handleCustomDirChange = async () => {
    try {
      const input = window.prompt(
        t('app.enterInstancePath'),
        customInstanceDir || ''
      );

      if (!input || !input.trim()) return;

      const selectedDir = await SetInstanceDirectory(input.trim());

      if (!selectedDir) {
        setError({
          type: 'SETTINGS_ERROR',
          message: t('app.failedChangeDir'),
          technical: t('app.pathError'),
          timestamp: new Date().toISOString()
        });
        return;
      }

      setCustomInstanceDir(selectedDir);
      console.log('Instance directory updated to:', selectedDir);

      window.alert(
        t('app.instanceDirUpdated') + '\n\n' +
        t('app.instanceDirUpdatedDetail', { dir: selectedDir })
      );

      // Reload version list and check installed versions for new directory
      setIsLoadingVersions(true);
      try {
        const versions = await GetVersionList(currentBranch);
        setAvailableVersions(versions || []);

        const installed = await GetInstalledVersionsForBranch(currentBranch);
        setInstalledVersions(installed || []);

        // Re-check if current version is installed in new directory
        const isInstalled = await IsVersionInstalled(currentBranch, currentVersion);
        setIsVersionInstalled(isInstalled);

        // Check version status for latest
        if (currentVersion === 0) {
          const status = await GetLatestVersionStatus(currentBranch);
          setVersionStatus(status);
        }
      } catch (e) {
        console.error('Failed to reload versions after directory change:', e);
      }
      setIsLoadingVersions(false);
    } catch (err) {
      console.error('Failed to change instance directory:', err);
      setError({
        type: 'SETTINGS_ERROR',
        message: t('app.failedChangeDir'),
        technical: err instanceof Error ? err.message : String(err),
        timestamp: new Date().toISOString()
      });
    }
  };

  // Show loading screen until onboarding check is complete to prevent UI flash
  if (!onboardingChecked) {
    return (
      <div className="fixed inset-0 bg-[#0a0a0a] z-[100]" />
    );
  }

  // Wrapper mode - simplified UI: background, news, and update/install/launch controls
  if (isWrapperMode) {
    const refreshWrapperStatusLocal = async () => {
      setIsWrapperWorking(true);
      try {
        await refreshWrapperStatus();
      } catch (e) {
        console.error('refreshWrapperStatusLocal failed', e);
      }
      setIsWrapperWorking(false);
    };

    const doInstallWrapper = async () => {
      setIsWrapperWorking(true);
      try {
        const ok = await WrapperInstallLatest();
        if (!ok) {
          window.alert('Install failed - check logs');
        }
      } catch (e) {
        console.error('Install failed', e);
      }
      await refreshWrapperStatus();
      setIsWrapperWorking(false);
    };

    const doLaunchWrapper = async () => {
      setIsWrapperWorking(true);
      try {
        const ok = await WrapperLaunch();
        if (!ok) window.alert('Failed to launch HyPrism');
      } catch (e) {
        console.error('Launch failed', e);
      }
      setIsWrapperWorking(false);
    };

    return (
      <div className="w-full h-full relative text-white">
        <BackgroundImage />
        <div className="absolute inset-0 flex items-center justify-center">
          <div className="bg-black/70 p-6 rounded-lg w-[720px] max-w-full">
            <h1 className="text-2xl font-bold mb-2">HyPrism</h1>
            <p className="mb-4">Wrapper mode — scarica e avvia la versione completa di HyPrism</p>

            <div className="mb-4">
              <div>Installed: {wrapperStatus?.installed ? wrapperStatus.installedVersion : 'none'}</div>
              <div>Latest: {wrapperStatus?.latestVersion || '—'}</div>
              <div className="mt-2">{wrapperStatus?.updateAvailable ? <span className="text-yellow-400">Update available</span> : <span className="text-green-400">Up to date</span>}</div>
            </div>

            <div className="flex gap-3 mb-4">
              <button className="px-4 py-2 bg-slate-700 rounded" onClick={refreshWrapperStatusLocal} disabled={isWrapperWorking}>Check for updates</button>
              <button className="px-4 py-2 bg-amber-500 rounded" onClick={doInstallWrapper} disabled={!wrapperStatus?.updateAvailable || isWrapperWorking}>Download & Install</button>
              <button className="px-4 py-2 bg-emerald-500 rounded" onClick={doLaunchWrapper} disabled={!wrapperStatus?.installed || isWrapperWorking}>Launch</button>
            </div>

            <div className="mt-6">
              <Suspense fallback={<div>Loading news…</div>}>
                <NewsPreview getNews={async (count) => { const n = await GetNews(count); return n; }} isPaused={false} />
              </Suspense>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // If onboarding needs to be shown, render only the onboarding modal
  if (showOnboarding) {
    return (
      <Suspense fallback={<div className="fixed inset-0 bg-[#0a0a0a]" />}>
        <OnboardingModal onComplete={handleOnboardingComplete} />
      </Suspense>
    );
  }

  // Helper to get combined news from backend (already includes both hytale + hyprism)
  const getCombinedNews = async (_count: number) => {
    const raw = await GetNews(_count);
    return (raw || []).map((item: any) => {
      const dateMs = parseDateMs(item?.publishedAt || item?.date);
      return {
        title: item?.title || '',
        excerpt: item?.excerpt || item?.description || '',
        url: item?.url || '',
        date: dateMs ? formatDateConsistent(dateMs, i18n.language) : (item?.date || ''),
        author: item?.author || '',
        imageUrl: item?.imageUrl || item?.coverImageUrl || '',
        source: item?.source || 'hytale',
      };
    }).sort((a: any, b: any) => {
      const aMs = parseDateMs(a.date);
      const bMs = parseDateMs(b.date);
      return bMs - aMs;
    });
  };

  const handleInstanceDeleted = async () => {
    const installed = await GetInstalledVersionsForBranch(currentBranch);
    const latestInstalled = await IsVersionInstalled(currentBranch, 0);
    const installedWithLatest = [...(installed || [])];
    if (latestInstalled && !installedWithLatest.some(v => v.version === 0)) installedWithLatest.unshift({ version: 0, isValid: true });
    setInstalledVersions(installedWithLatest);
    const stillInstalled = await IsVersionInstalled(currentBranch, currentVersion);
    setIsVersionInstalled(stillInstalled);
    if (currentVersion === 0) {
      const status = await GetLatestVersionStatus(currentBranch);
      setVersionStatus(status);
    }
  };

  return (
    <div className="relative w-screen h-screen bg-[#090909] text-white overflow-hidden font-sans select-none">
      <BackgroundImage mode={backgroundMode} />

      {/* Darkening overlay for background */}
      <div className="absolute inset-0 z-[5] bg-black/50 pointer-events-none" />

      {/* Music Player - invisible, controlled by DockMenu */}
      <MusicPlayer muted={isMuted} forceMuted={isGameRunning} />

      {isUpdatingLauncher && (
        <UpdateOverlay
          progress={progress}
          downloaded={updateStats.d}
          total={updateStats.t}
        />
      )}

      {/* Page Content with Transitions */}
      <main className="relative z-10 h-full">
        <AnimatePresence mode="wait">
          {currentPage === 'dashboard' && (
            <DashboardPage
              key="dashboard"
              username={username}
              uuid={uuid}
              isEditing={isEditing}
              launcherVersion={launcherVersion}
              updateAvailable={!!updateAsset}
              avatarRefreshTrigger={avatarRefreshTrigger}
              onEditToggle={setIsEditing}
              onUserChange={handleNickChange}
              onOpenProfileEditor={() => setCurrentPage('profiles')}
              onLauncherUpdate={handleUpdate}
              isDownloading={isDownloading}
              downloadState={downloadState}
              canCancel={isDownloading && !isGameRunning && (downloadState === 'downloading' || downloadState === 'extracting')}
              isGameRunning={isGameRunning}
              isVersionInstalled={isVersionInstalled}
              isCheckingInstalled={isCheckingInstalled}
              versionStatus={versionStatus}
              progress={progress}
              downloaded={downloaded}
              total={total}
              launchState={launchState}
              launchDetail={launchDetail}
              currentBranch={currentBranch}
              currentVersion={currentVersion}
              availableVersions={availableVersions}
              installedVersions={installedVersions}
              isLoadingVersions={isLoadingVersions}
              onBranchChange={handleBranchChange}
              onVersionChange={handleVersionChange}
              onPlay={handlePlay}
              onDownload={handlePlay}
              onUpdate={handleGameUpdate}
              onDuplicate={handleGameDuplicate}
              onExit={handleExit}
              onCancelDownload={handleCancelDownload}
            />
          )}

          {currentPage === 'news' && (
            <NewsPage
              key="news"
              getNews={getCombinedNews}
              newsDisabled={newsDisabled}
            />
          )}

          {currentPage === 'profiles' && (
            <ProfilesPage
              key="profiles"
              onProfileUpdate={reloadProfile}
            />
          )}

          {currentPage === 'instances' && (
            <InstancesPage
              key="instances"
              onInstanceDeleted={handleInstanceDeleted}
              onOpenModBrowser={(branch, version) => {
                setModBrowserInstance({ branch, version });
                setShowModBrowser(true);
              }}
              onNavigateToDashboard={() => setCurrentPage('dashboard')}
              isGameRunning={isGameRunning}
              runningBranch={runningBranch}
              runningVersion={runningVersion}
              onStopGame={handleExit}
            />
          )}

          {currentPage === 'settings' && (
            <SettingsPage
              key="settings"
              launcherBranch={launcherBranch}
              onLauncherBranchChange={handleLauncherBranchChange}
              rosettaWarning={rosettaWarning}
              onBackgroundModeChange={(mode) => setBackgroundMode(mode)}
              onNewsDisabledChange={(disabled) => setNewsDisabled(disabled)}
              onAccentColorChange={(color) => setAccentColor(color)}
              onInstanceDeleted={handleInstanceDeleted}
              onNavigateToMods={() => {
                setCurrentPage('instances');
              }}
            />
          )}
        </AnimatePresence>
      </main>

      {/* Floating Dock Menu */}
      <DockMenu 
        activePage={currentPage} 
        onPageChange={setCurrentPage} 
        isMuted={isMuted}
        onToggleMute={handleToggleMute}
      />

      {/* Modals - only essential overlays */}
      {/* Browse Mods Modal */}
      {showModBrowser && modBrowserInstance && (
        <BrowseModsModal
          isOpen={showModBrowser}
          onClose={() => {
            setShowModBrowser(false);
            setModBrowserInstance(null);
          }}
          currentBranch={modBrowserInstance.branch}
          currentVersion={modBrowserInstance.version}
          onModsInstalled={() => {
            // Refresh will happen automatically when modal closes
          }}
        />
      )}
      
      <Suspense fallback={<ModalFallback />}>
        {showDelete && (
          <DeleteConfirmationModal
            onConfirm={() => {
              DeleteGame(currentBranch, currentVersion);
              setShowDelete(false);
            }}
            onCancel={() => setShowDelete(false)}
          />
        )}

        {pendingUpdate && (
          <UpdateConfirmationModal
            oldVersion={pendingUpdate.oldVersion}
            newVersion={pendingUpdate.newVersion}
            hasOldUserData={pendingUpdate.hasOldUserData}
            onConfirmWithCopy={handleUpdateConfirmWithCopy}
            onConfirmWithoutCopy={handleUpdateConfirmWithoutCopy}
            onCancel={handleUpdateCancel}
          />
        )}

        {error && (
          <ErrorModal
            error={{...error, launcherVersion: launcherVersion}}
            onClose={() => setError(null)}
          />
        )}

        {launchTimeoutError && (
          <ErrorModal
            error={{
              type: 'LAUNCH_FAILED',
              message: launchTimeoutError.message,
              technical: launchTimeoutError.logs.length > 0 
                ? launchTimeoutError.logs.join('\n')
                : 'No log entries available',
              timestamp: new Date().toISOString(),
              launcherVersion: launcherVersion
            }}
            onClose={() => setLaunchTimeoutError(null)}
          />
        )}
      </Suspense>
    </div>
  );
};

export default App;
