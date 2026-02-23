import React, { useState, useEffect, useRef, lazy, Suspense, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { AnimatePresence } from 'framer-motion';
import { ipc, on, send, NewsItem, InstanceInfo } from '@/lib/ipc';
import { BackgroundImage } from './components/layout/BackgroundImage';
import { MusicPlayer } from './components/layout/MusicPlayer';
import { DockMenu } from './components/layout/DockMenu';
import type { PageType } from './components/layout/DockMenu';
import { DashboardPage } from './pages/DashboardPage';
import { NewsPage } from './pages/NewsPage';
import { ProfilesPage } from './pages/ProfilesPage';
import { InstancesPage } from './pages/InstancesPage';
import { SettingsPage } from './pages/SettingsPage';
import { LogsPage } from './pages/LogsPage';
import { Button, LauncherActionButton } from '@/components/ui/Controls';
import { LoadingSpinner } from '@/components/ui/LoadingSpinner';
// Controller detection removed - not using floating indicator

// Lazy load heavy modals for better initial load performance
const NewsPreview = lazy(() => import('./components/NewsPreview').then(m => ({ default: m.NewsPreview })));
const ErrorModal = lazy(() => import('./components/modals/ErrorModal').then(m => ({ default: m.ErrorModal })));
const DeleteConfirmationModal = lazy(() => import('./components/modals/DeleteConfirmationModal').then(m => ({ default: m.DeleteConfirmationModal })));
const OnboardingModal = lazy(() => import('./pages/onboarding').then(m => ({ default: m.OnboardingModal })));

// Functions that map to real IPC channels
const WindowClose = () => ipc.windowCtl.close();
const CancelDownload = () => ipc.game.cancel();
const StopGame = () => ipc.game.stop();
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
async function GetCloseAfterLaunch(): Promise<boolean> { return (await ipc.settings.get()).closeAfterLaunch ?? false; }
async function GetLaunchAfterDownload(): Promise<boolean> { return (await ipc.settings.get()).launchAfterDownload ?? true; }
async function GetHasCompletedOnboarding(): Promise<boolean> { return (await ipc.settings.get()).hasCompletedOnboarding ?? false; }
async function GetLauncherBranch(): Promise<string> { return (await ipc.settings.get()).launcherBranch ?? 'release'; }
async function GetLauncherVersion(): Promise<string> { return (await ipc.settings.get()).launcherVersion ?? ''; }

// Profile-backed getters
async function GetNick(): Promise<string> { return (await ipc.profile.get()).nick ?? 'HyPrism'; }
async function GetUUID(): Promise<string> { return (await ipc.profile.get()).uuid ?? ''; }

// Game actions
function LaunchGame(data?: unknown): void { ipc.game.launch(data); }

// Get recent logs from backend
async function GetRecentLogs(_count: number = 10): Promise<string[]> {
  try {
    return await ipc.logs.get();
  } catch (e) {
    console.error('[IPC] GetRecentLogs failed:', e);
    return [];
  }
}

// Stub for functions that don't have IPC channels yet
const stub = <T,>(name: string, fallback: T) => async (..._args: unknown[]): Promise<T> => {
  console.warn(`[IPC] ${name}: no IPC channel yet`);
  return fallback;
};

// Real IPC call to check if game is running
async function IsGameRunning(): Promise<boolean> {
  try {
    return await ipc.game.isRunning();
  } catch (e) {
    console.error('[IPC] IsGameRunning failed:', e);
    return false;
  }
}

// Instance-based functions
async function GetSelectedInstance(): Promise<InstanceInfo | null> {
  try {
    return await ipc.instance.getSelected();
  } catch (e) {
    console.error('[IPC] GetSelectedInstance failed:', e);
    return null;
  }
}

async function GetInstances(): Promise<InstanceInfo[]> {
  try {
    return await ipc.instance.list();
  } catch (e) {
    console.error('[IPC] GetInstances failed:', e);
    return [];
  }
}

const GetWrapperStatus = stub<null>('GetWrapperStatus', null);
const WrapperInstallLatest = stub('WrapperInstallLatest', true);
const WrapperLaunch = stub('WrapperLaunch', true);
const CheckRosettaStatus = stub<{ NeedsInstall: boolean; Message: string; Command: string; TutorialUrl?: string } | null>('CheckRosettaStatus', null);

// Modal loading fallback - minimal spinner
const ModalFallback = () => (
  <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
    <LoadingSpinner />
  </div>
);

const parseDateMs = (dateValue: string | number | Date | undefined): number => {
  if (!dateValue) return 0;
  const ms = new Date(dateValue).getTime();
  return Number.isNaN(ms) ? 0 : ms;
};

const resolveSelectedInstance = (
  selected: InstanceInfo | null,
  allInstances: InstanceInfo[]
): InstanceInfo | null => {
  if (selected) return selected;
  if (!Array.isArray(allInstances) || allInstances.length === 0) return null;

  const installed = allInstances.find((inst) => inst.isInstalled);
  return installed ?? allInstances[0];
};

const formatDateConsistent = (dateMs: number, locale = 'en-US') => {
  return new Date(dateMs).toLocaleDateString(locale, {
    day: 'numeric',
    month: 'long',
    year: 'numeric'
  });
};

const App: React.FC = () => {
  const { t, i18n } = useTranslation();
  // User state
  const [username, setUsername] = useState<string>("HyPrism");
  const [uuid, setUuid] = useState<string>("");
  const [launcherVersion, setLauncherVersion] = useState<string>("dev");

  // Download state
  const [progress, setProgress] = useState<number>(0);
  const [isDownloading, setIsDownloading] = useState<boolean>(false);
  const [downloadingBranch, setDownloadingBranch] = useState<string | undefined>(undefined);
  const [downloadingVersion, setDownloadingVersion] = useState<number | undefined>(undefined);
  const [downloadState, setDownloadState] = useState<'downloading' | 'extracting' | 'launching'>('downloading');
  const [isGameRunning, setIsGameRunning] = useState<boolean>(false);
  const [isMovingData, setIsMovingData] = useState<boolean>(false);
  const [runningBranch, setRunningBranch] = useState<string | undefined>(undefined);
  const [runningVersion, setRunningVersion] = useState<number | undefined>(undefined);
  const [downloaded, setDownloaded] = useState<number>(0);
  const [total, setTotal] = useState<number>(0);
  const [launchState, setLaunchState] = useState<string>('');
  const [launchDetail, setLaunchDetail] = useState<string>('');

  // Helper to clear all download state
  const clearDownloadState = useCallback(() => {
    setIsDownloading(false);
    setDownloadingBranch(undefined);
    setDownloadingVersion(undefined);
  }, []);

  // Update state - only track if update is available
  const [updateAsset, setUpdateAsset] = useState<any>(null);

  // Modal state
  const [showDelete, setShowDelete] = useState<boolean>(false);

  const [error, setError] = useState<any>(null);
  const [launchTimeoutError, setLaunchTimeoutError] = useState<{ message: string; logs: string[] } | null>(null);
  const [avatarRefreshTrigger, setAvatarRefreshTrigger] = useState<number>(0);

  // Page navigation state
  const [currentPage, setCurrentPage] = useState<PageType>('dashboard');

  // Instance sub-tab state (persisted across page navigations)
  const [instanceTab, setInstanceTab] = useState<'content' | 'browse' | 'worlds'>('content');

  // Settings state
  const [launcherBranch, setLauncherBranch] = useState<string>('release');
  const [rosettaWarning, setRosettaWarning] = useState<{ message: string; command: string; tutorialUrl?: string } | null>(null);

  // Game launch tracking
  const gameLaunchTimeRef = useRef<number | null>(null);

  // Instance-based state
  const [selectedInstance, setSelectedInstance] = useState<InstanceInfo | null>(null);
  const [instances, setInstances] = useState<InstanceInfo[]>([]);
  const [isCheckingInstance, setIsCheckingInstance] = useState<boolean>(false);
  const [hasUpdateAvailable, setHasUpdateAvailable] = useState<boolean>(false);
  // Refs to track current instance for event listeners
  const selectedInstanceRef = useRef<InstanceInfo | null>(null);

  // Official server blocked state: true when using official servers with an unofficial profile in online mode
  const [isOfficialProfile, setIsOfficialProfile] = useState<boolean>(false);
  const [isOfficialServerMode, setIsOfficialServerMode] = useState<boolean>(false);
  const officialServerBlocked = isOfficialServerMode && !isOfficialProfile;

  // Download sources state
  const [enabledMirrorCount, setEnabledMirrorCount] = useState<number>(0);
  const [hasOfficialAccount, setHasOfficialAccount] = useState<boolean>(false);
  const hasDownloadSources = enabledMirrorCount > 0 || (hasOfficialAccount && isOfficialProfile);

  const refreshDownloadSources = useCallback(async () => {
    try {
      const sourcesResult = await ipc.settings.hasDownloadSources();
      setEnabledMirrorCount(typeof sourcesResult.enabledMirrorCount === 'number' ? sourcesResult.enabledMirrorCount : 0);
      setHasOfficialAccount(!!sourcesResult.hasOfficialAccount);
    } catch (e) {
      console.error('Failed to check download sources:', e);
      setEnabledMirrorCount(0);
      setHasOfficialAccount(false);
    }
  }, []);

  // Background, news, and accent color settings
  // Initialize as null to prevent flash — don't render background until config is loaded
  const [backgroundMode, setBackgroundMode] = useState<string | null>(null);
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
    selectedInstanceRef.current = selectedInstance;
  }, [selectedInstance]);

  useEffect(() => {
    const handleMenuNavigate = (evt: Event) => {
      const event = evt as CustomEvent<{ page?: PageType }>;
      const page = event.detail?.page;
      if (page) {
        setCurrentPage(page);
      }
    };

    window.addEventListener('hyprism:menu:navigate', handleMenuNavigate as EventListener);
    return () => {
      window.removeEventListener('hyprism:menu:navigate', handleMenuNavigate as EventListener);
    };
  }, []);

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

  // Load selected instance and instances list on startup
  useEffect(() => {
    const loadInstanceState = async () => {
      setIsCheckingInstance(true);
      try {
        const [selected, allInstances] = await Promise.all([
          GetSelectedInstance(),
          GetInstances()
        ]);
        const resolvedSelected = resolveSelectedInstance(selected, allInstances);
        setSelectedInstance(resolvedSelected);
        selectedInstanceRef.current = resolvedSelected;
        setInstances(allInstances);

        if (resolvedSelected && !selected) {
          ipc.instance.select({ id: resolvedSelected.id }).catch((e) => {
            console.error('Failed to persist auto-selected instance:', e);
          });
        }

        // TODO: Check if update is available for selected instance
        setHasUpdateAvailable(false);
      } catch (e) {
        console.error('Failed to load instance state:', e);
        setSelectedInstance(null);
        setInstances([]);
      }
      setIsCheckingInstance(false);
    };
    loadInstanceState();
  }, []);

  // Refresh instances list
  const refreshInstances = async () => {
    try {
      const [selected, allInstances] = await Promise.all([
        GetSelectedInstance(),
        GetInstances()
      ]);
      const resolvedSelected = resolveSelectedInstance(selected, allInstances);
      setSelectedInstance(resolvedSelected);
      selectedInstanceRef.current = resolvedSelected;
      setInstances(allInstances);

      if (resolvedSelected && !selected) {
        ipc.instance.select({ id: resolvedSelected.id }).catch((e) => {
          console.error('Failed to persist auto-selected instance during refresh:', e);
        });
      }
    } catch (e) {
      console.error('Failed to refresh instances:', e);
    }
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

          const [selected, allInstances] = await Promise.all([
            GetSelectedInstance(),
            GetInstances()
          ]);
          const resolvedSelected = resolveSelectedInstance(selected, allInstances);
          if (resolvedSelected) {
            setRunningBranch(resolvedSelected.branch);
            setRunningVersion(resolvedSelected.version);
            setSelectedInstance(resolvedSelected);
            selectedInstanceRef.current = resolvedSelected;
          }
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
    // Update official profile status
    await refreshOfficialStatus();
    // Refresh download source availability (mirrors / official)
    await refreshDownloadSources();
  };

  // Refresh official server/profile status for play-button blocking
  const refreshOfficialStatus = async () => {
    try {
      const settings = await ipc.settings.get();
      const domain = settings.authDomain?.trim() ?? '';
      const isOfficial = domain === 'sessions.hytale.com' || domain === 'official'
        || domain.includes('hytale.com');
      setIsOfficialServerMode(isOfficial && settings.onlineMode);

      // Check if active profile is official
      const profiles = await ipc.profile.list();
      const profile = await ipc.profile.get();
      const activeUuid = profile.uuid;
      const activeProfile = (profiles as any[])?.find((p: any) => p.uuid === activeUuid || p.UUID === activeUuid);
      setIsOfficialProfile(activeProfile?.isOfficial === true || activeProfile?.IsOfficial === true);
    } catch (e) {
      console.error('Failed to refresh official status:', e);
    }
  };

  // Check if onboarding has been completed
  useEffect(() => {
    const checkOnboarding = async () => {
      try {
        // Quick check: localStorage flag (avoids IPC race conditions on startup)
        if (localStorage.getItem('hyprism_onboarding_done') === '1') {
          setOnboardingChecked(true);
          return;
        }
        const completed = await GetHasCompletedOnboarding();
        if (!completed) {
          setShowOnboarding(true);
        } else {
          // Cache in localStorage so next startup is instant
          localStorage.setItem('hyprism_onboarding_done', '1');
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
    localStorage.setItem('hyprism_onboarding_done', '1');
    // Reload profile data after onboarding
    await reloadProfile();
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

    // Compute official server/profile status for play-button blocking
    refreshOfficialStatus();

    // Check if game is already running on startup
    IsGameRunning().then((running: boolean) => {
      console.log('[App] Initial game running state:', running);
      if (running) {
        setIsGameRunning(true);
      }
    });

    // Load background mode, news settings, and accent color
    GetBackgroundMode().then((mode: string) => setBackgroundMode(mode || 'slideshow'));
    GetMusicEnabled().then((enabled: boolean) => setIsMuted(!enabled));

    // Load launcher branch and other settings
    const loadSettings = async () => {
      try {
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

        // Load download sources status
        await refreshDownloadSources();
      } catch (e) {
        console.error('Failed to load settings:', e);
      }
    };
    loadSettings();

    // Wrapper mode skips event listener setup
    if (isWrapperMode) return;

    // Event listeners
    const unsubProgress = EventsOn('progress-update', (data: any) => {
      if (data.state === 'moving-instances' || data.state === 'moving-instances-complete') {
        return;
      }

      // Handle cancellation first
      if (data.state === 'cancelled') {
        console.log('Download cancelled event received');
        clearDownloadState();
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
        // Refresh instances after download completes
        if (data.progress >= 100) {
          refreshInstances();
        }
      } else if (data.state === 'launch' || data.state === 'launching') {
        // Game is launching - set running immediately for better UX
        console.log('[App] Launch/launching state received, setting isGameRunning=true');
        setIsGameRunning(true);
        clearDownloadState();
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
        // Track which instance is running (use current selected instance)
        const inst = selectedInstanceRef.current;
        if (inst) {
          setRunningBranch(inst.branch);
          setRunningVersion(inst.version);
        } else {
          try {
            const [selected, allInstances] = await Promise.all([
              GetSelectedInstance(),
              GetInstances()
            ]);
            const resolvedSelected = resolveSelectedInstance(selected, allInstances);
            if (resolvedSelected) {
              setRunningBranch(resolvedSelected.branch);
              setRunningVersion(resolvedSelected.version);
              setSelectedInstance(resolvedSelected);
              selectedInstanceRef.current = resolvedSelected;
            }
          } catch (e) {
            console.error('[App] Failed to resolve running instance on start event:', e);
          }
        }
        clearDownloadState();
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
            // Clear any concurrent error to avoid showing two error modals
            setError(null);
            setLaunchTimeoutError({
              message: t('app.gameCrashed', { code: exitCode }),
              logs: logs || []
            });
          } catch {
            setError(null);
            setLaunchTimeoutError({
              message: t('app.gameCrashed', { code: exitCode }),
              logs: []
            });
          }
        }
        setIsGameRunning(false);
        setRunningBranch(undefined);
        setRunningVersion(undefined);
        clearDownloadState();
        setProgress(0);
        setLaunchState('');
        setLaunchDetail('');
        gameLaunchTimeRef.current = null; // Clear launch time to prevent polling error

        // Refresh instances so newly-installed instances appear as installed
        refreshInstances();
      }
    });

    const unsubUpdate = EventsOn('update:available', (asset: any) => {
      setUpdateAsset(asset);
      // Don't auto-update - user clicks version to open GitHub release
      console.log('Update available:', asset);
    });

    const unsubError = EventsOn('error', (err: any) => {
      // Clear any concurrent launch timeout error to avoid showing two error modals
      setLaunchTimeoutError(null);
      setError(err);
      clearDownloadState();
      setProgress(0);
      setDownloaded(0);
      setTotal(0);
      setLaunchState('');
      setLaunchDetail('');
      setDownloadState('downloading');
    });

    return () => {
      unsubProgress();
      unsubGameState();
      unsubUpdate();
      unsubError();
    };
  }, []);

  // Re-check download sources when navigating back from Settings (e.g. mirrors added/removed)
  useEffect(() => {
    if (currentPage === 'settings') return;
    void refreshDownloadSources();
  }, [currentPage, refreshDownloadSources]);

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

    // Launch the selected instance
    doLaunch();
  };

  const doLaunch = async () => {
    let launchTarget = selectedInstanceRef.current ?? selectedInstance;

    // Pull latest backend-selected instance right before launch to avoid stale UI state.
    try {
      const backendSelected = await GetSelectedInstance();
      if (backendSelected?.id) {
        launchTarget = backendSelected;

        const currentSelectedId = selectedInstanceRef.current?.id ?? selectedInstance?.id;
        if (currentSelectedId !== backendSelected.id) {
          setSelectedInstance(backendSelected);
          selectedInstanceRef.current = backendSelected;
        }
      }
    } catch (e) {
      console.warn('[App] Failed to refresh selected instance before launch:', e);
    }

    setIsDownloading(true);
    // Track which instance is downloading from the currently selected instance
    if (launchTarget) {
      setDownloadingBranch(launchTarget.branch);
      setDownloadingVersion(launchTarget.version);
    }
    setDownloadState('downloading');
    try {
      if (launchTarget) {
        LaunchGame({
          branch: launchTarget.branch,
          version: launchTarget.version,
          instanceId: launchTarget.id,
        });
      } else {
        LaunchGame();
      }
      // Button state will be managed by progress events:
      // - 'launch' event sets isGameRunning=true and isDownloading=false
      // - 'error' event sets isDownloading=false
    } catch (err) {
      console.error('Launch failed:', err);
      clearDownloadState();
    }
  };

  // Launch a specific instance from the Instances page — properly tracks download state
  const handleLaunchFromInstances = async (branch: string, version: number, instanceId?: string) => {
    if (isGameRunning || isDownloading) return;

    const launchAfterDownload = await GetLaunchAfterDownload();

    const launchingInstance = instanceId
      ? (instances.find(inst => inst.id === instanceId) ?? null)
      : (instances.find(inst => inst.branch === branch && inst.version === version) ?? null);
    if (launchingInstance) {
      setSelectedInstance(launchingInstance);
      selectedInstanceRef.current = launchingInstance;
      ipc.instance.select({ id: launchingInstance.id }).catch((e) => {
        console.error('Failed to persist launched instance selection:', e);
      });
    }

    setIsDownloading(true);
    setDownloadingBranch(branch);
    setDownloadingVersion(version);
    setDownloadState('downloading');
    send('hyprism:game:launch', {
      branch,
      version,
      launchAfterDownload,
      ...(launchingInstance?.id ? { instanceId: launchingInstance.id } : {})
    });
  };

  const handleGameUpdate = async () => {
    // TODO: Implement instance update
    setIsDownloading(true);
    setDownloadState('downloading');
    try {
      LaunchGame();
      // Refresh instances after update
      await refreshInstances();
    } catch (err) {
      console.error('Update failed:', err);
      clearDownloadState();
    }
  };

  const handleDownload = async () => {
    // Navigate to instances page to create a new instance
    setCurrentPage('instances');
  };

  const handleCancelDownload = async () => {
    try {
      CancelDownload();
    } catch (err) {
      console.error('Cancel failed:', err);
    }

    // UI reset is also handled by the backend 'cancelled' progress event,
    // but we reset optimistically for responsiveness.
    clearDownloadState();
    setProgress(0);
    setDownloaded(0);
    setTotal(0);
    setLaunchState('');
    setLaunchDetail('');
  };

  const handleExit = async () => {
    try {
      await StopGame();
    } catch (err) {
      console.error('Failed to exit game:', err);
    }
    setIsGameRunning(false);
    setProgress(0);
  };

  const handleLauncherBranchChange = async (branch: string) => {
    try {
      // Use ipc.settings.update instead of stub SetLauncherBranch
      await ipc.settings.update({ launcherBranch: branch });
      setLauncherBranch(branch);
      console.log('Launcher branch changed to:', branch);
    } catch (err) {
      console.error('Failed to change launcher branch:', err);
    }
  };

  // Instance directory change is now handled through Settings -> Instance management

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
          window.alert(t('wrapper.installFailed'));
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
        if (!ok) window.alert(t('wrapper.launchFailed'));
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
            <p className="mb-4">{t('wrapper.description')}</p>

            <div className="mb-4">
              <div>{t('wrapper.installed')} {wrapperStatus?.installed ? wrapperStatus.installedVersion : t('wrapper.none')}</div>
              <div>{t('wrapper.latest')} {wrapperStatus?.latestVersion || '—'}</div>
              <div className="mt-2">{wrapperStatus?.updateAvailable ? <span className="text-yellow-400">{t('wrapper.updateAvailable')}</span> : <span className="text-green-400">{t('wrapper.upToDate')}</span>}</div>
            </div>

            <div className="flex gap-3 mb-4">
              <Button onClick={refreshWrapperStatusLocal} disabled={isWrapperWorking}>
                {t('wrapper.checkForUpdates')}
              </Button>
              <LauncherActionButton
                variant="update"
                onClick={doInstallWrapper}
                disabled={!wrapperStatus?.updateAvailable || isWrapperWorking}
                className="h-10 px-4 rounded-xl text-sm"
              >
                {t('wrapper.downloadInstall')}
              </LauncherActionButton>
              <LauncherActionButton
                variant="play"
                onClick={doLaunchWrapper}
                disabled={!wrapperStatus?.installed || isWrapperWorking}
                className="h-10 px-4 rounded-xl text-sm"
              >
                {t('wrapper.launch')}
              </LauncherActionButton>
            </div>

            <div className="mt-6">
              <Suspense fallback={<div>{t('wrapper.loadingNews')}</div>}>
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
    await refreshInstances();
  };

  return (
    <div className="relative w-screen h-screen bg-[#090909] text-white overflow-hidden font-sans select-none">
      {backgroundMode !== null && <BackgroundImage mode={backgroundMode} />}

      {/* Darkening overlay for background */}
      <div className="absolute inset-0 z-[5] bg-black/50 pointer-events-none" />

      {/* Music Player - invisible, controlled by DockMenu */}
      <MusicPlayer muted={isMuted} forceMuted={isGameRunning} />

      {/* Page Content with Transitions */}
      <main className="relative z-10 h-full">
        {currentPage === 'logs' ? (
          <LogsPage key="logs" />
        ) : (
        <AnimatePresence mode="wait">
          {currentPage === 'dashboard' && (
            <DashboardPage
              key="dashboard"
              username={username}
              uuid={uuid}
              launcherVersion={launcherVersion}
              updateAvailable={!!updateAsset}
              launcherUpdateInfo={updateAsset}
              avatarRefreshTrigger={avatarRefreshTrigger}
              onOpenProfileEditor={() => setCurrentPage('profiles')}
              isDownloading={isDownloading}
              downloadState={downloadState}
              canCancel={isDownloading && !isGameRunning}
              isGameRunning={isGameRunning}
              progress={progress}
              downloaded={downloaded}
              total={total}
              launchState={launchState}
              launchDetail={launchDetail}
              selectedInstance={selectedInstance}
              instances={instances}
              hasInstances={instances.length > 0}
              isCheckingInstance={isCheckingInstance}
              hasUpdateAvailable={hasUpdateAvailable}
              onPlay={handlePlay}
              onStopGame={handleExit}
              onDownload={handleDownload}
              onUpdate={handleGameUpdate}
              onCancelDownload={handleCancelDownload}
              onNavigateToInstances={() => setCurrentPage('instances')}
              officialServerBlocked={officialServerBlocked}
              isOfficialProfile={isOfficialProfile}
              isOfficialServerMode={isOfficialServerMode}
              hasDownloadSources={hasDownloadSources}
            />
          )}

          {currentPage === 'news' && (
            <NewsPage
              key="news"
              getNews={getCombinedNews}
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
              onInstanceSelected={refreshInstances}
              isGameRunning={isGameRunning}
              runningBranch={runningBranch}
              runningVersion={runningVersion}
              onStopGame={handleExit}
              activeTab={instanceTab}
              onTabChange={setInstanceTab}
              isDownloading={isDownloading}
              downloadingBranch={downloadingBranch}
              downloadingVersion={downloadingVersion}
              downloadState={downloadState}
              progress={progress}
              downloaded={downloaded}
              total={total}
              launchState={launchState}
              launchDetail={launchDetail}
              canCancel={isDownloading && !isGameRunning}
              onCancelDownload={handleCancelDownload}
              onLaunchInstance={handleLaunchFromInstances}
              officialServerBlocked={officialServerBlocked}
              hasDownloadSources={hasDownloadSources}
            />
          )}

          {currentPage === 'settings' && (
            <SettingsPage
              key="settings"
              launcherBranch={launcherBranch}
              onLauncherBranchChange={handleLauncherBranchChange}
              rosettaWarning={rosettaWarning}
              onBackgroundModeChange={(mode) => setBackgroundMode(mode)}
              onInstanceDeleted={handleInstanceDeleted}
              onAuthSettingsChange={async () => {
                await refreshOfficialStatus();
                await refreshDownloadSources();
              }}
              onNavigateToMods={() => {
                setCurrentPage('instances');
              }}
              isGameRunning={isGameRunning}
              onMovingDataChange={setIsMovingData}
            />
          )}
        </AnimatePresence>
        )}
      </main>

      {/* Floating Dock Menu - hide during data migration */}
      {!isMovingData && (
        <DockMenu 
          activePage={currentPage} 
          onPageChange={setCurrentPage} 
          isMuted={isMuted}
          onToggleMute={handleToggleMute}
        />
      )}

      {/* Modals - only essential overlays */}
      
      <Suspense fallback={<ModalFallback />}>
        {showDelete && selectedInstance && (
          <DeleteConfirmationModal
            onConfirm={async () => {
              // TODO: Implement instance deletion via InstanceService
              console.log('Delete instance:', selectedInstance.id);
              setShowDelete(false);
              await refreshInstances();
            }}
            onCancel={() => setShowDelete(false)}
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
                : t('error.noLogEntries'),
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
