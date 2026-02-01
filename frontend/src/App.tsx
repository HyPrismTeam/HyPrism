import React, { useState, useEffect, useRef, lazy, Suspense } from 'react';
import { useTranslation } from 'react-i18next';
import { BrowserOpenURL, EventsOn } from '@/api/bridge';
import { GameBranch } from './constants/enums';
import { BackgroundImage } from './components/BackgroundImage';
import { ProfileSection } from './components/ProfileSection';
import { ControlSection } from './components/ControlSection';
import { MusicPlayer } from './components/MusicPlayer';
import { UpdateOverlay } from './components/UpdateOverlay';
import { DiscordIcon } from './components/DiscordIcon';
// Controller detection removed - not using floating indicator
import hytaleLogo from './assets/logo.png';

// Lazy load heavy modals for better initial load performance
const ErrorModal = lazy(() => import('./components/ErrorModal').then(m => ({ default: m.ErrorModal })));
const DeleteConfirmationModal = lazy(() => import('./components/DeleteConfirmationModal').then(m => ({ default: m.DeleteConfirmationModal })));
const ModManager = lazy(() => import('./components/ModManager').then(m => ({ default: m.ModManager })));
const SettingsModal = lazy(() => import('./components/SettingsModal').then(m => ({ default: m.SettingsModal })));
const UpdateConfirmationModal = lazy(() => import('./components/UpdateConfirmationModal').then(m => ({ default: m.UpdateConfirmationModal })));
const NewsPreview = lazy(() => import('./components/NewsPreview').then(m => ({ default: m.NewsPreview })));
const ProfileEditor = lazy(() => import('./components/ProfileEditor').then(m => ({ default: m.ProfileEditor })));
const OnboardingModal = lazy(() => import('./components/OnboardingModal').then(m => ({ default: m.OnboardingModal })));

import {
  DownloadAndLaunch,
  OpenInstanceFolder,
  GetNick,
  SetNick,
  GetUUID,
  DeleteGame,
  Update,
  ExitGame,
  IsGameRunning,
  GetRecentLogs,
  CancelDownload,
  // Version Manager
  GetVersionType,
  SetVersionType,
  SetSelectedVersion,
  GetVersionList,
  IsVersionInstalled,
  GetInstalledVersionsForBranch,
  CheckLatestNeedsUpdate,
  GetPendingUpdateInfo,
  CopyUserData,
  // Settings
  GetCustomInstanceDir,
  SetInstanceDirectory,
  GetNews,
  GetLauncherVersion,
  GetLauncherBranch,
  SetLauncherBranch,
  CheckRosettaStatus,
  GetCloseAfterLaunch,
  WindowClose,
  GetBackgroundMode,
  GetDisableNews,
  GetAccentColor,
  GetHasCompletedOnboarding,
} from '@/api/backend';
import appIcon from './assets/appicon.png';

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
  const [downloaded, setDownloaded] = useState<number>(0);
  const [total, setTotal] = useState<number>(0);

  // Update state
  const [updateAsset, setUpdateAsset] = useState<any>(null);
  const [isUpdatingLauncher, setIsUpdatingLauncher] = useState<boolean>(false);
  const [updateStats, setUpdateStats] = useState({ d: 0, t: 0 });

  // Modal state
  const [showDelete, setShowDelete] = useState<boolean>(false);
  const [showModManager, setShowModManager] = useState<boolean>(false);
  const [modManagerSearchQuery, setModManagerSearchQuery] = useState<string>('');
  const [showSettings, setShowSettings] = useState<boolean>(false);
  const [showProfileEditor, setShowProfileEditor] = useState<boolean>(false);
  const [error, setError] = useState<any>(null);
  const [launchTimeoutError, setLaunchTimeoutError] = useState<{ message: string; logs: string[] } | null>(null);
  const [avatarRefreshTrigger, setAvatarRefreshTrigger] = useState<number>(0);

  // Settings state
  const [launcherBranch, setLauncherBranch] = useState<string>('release');
  const [rosettaWarning, setRosettaWarning] = useState<{ message: string; command: string; tutorialUrl?: string } | null>(null);

  // Game launch tracking
  const gameLaunchTimeRef = useRef<number | null>(null);

  // Version state
  const [currentBranch, setCurrentBranch] = useState<string>(GameBranch.RELEASE);
  const [currentVersion, setCurrentVersion] = useState<number>(0);
  const [availableVersions, setAvailableVersions] = useState<number[]>([]);
  const [installedVersions, setInstalledVersions] = useState<number[]>([]);
  const [isLoadingVersions, setIsLoadingVersions] = useState<boolean>(false);
  const [isVersionInstalled, setIsVersionInstalled] = useState<boolean>(false);
  const [isCheckingInstalled, setIsCheckingInstalled] = useState<boolean>(false);
  const [customInstanceDir, setCustomInstanceDir] = useState<string>("");
  const [latestNeedsUpdate, setLatestNeedsUpdate] = useState<boolean>(false);

  // Background, news, and accent color settings
  const [backgroundMode, setBackgroundMode] = useState<string>('slideshow');
  const [newsDisabled, setNewsDisabled] = useState<boolean>(false);
  const [_accentColor, setAccentColor] = useState<string>('#FFA845'); // Used only for SettingsModal callback

  // Onboarding state
  const [showOnboarding, setShowOnboarding] = useState<boolean>(false);
  const [onboardingChecked, setOnboardingChecked] = useState<boolean>(false);

  // Pending game update modal
  const [pendingUpdate, setPendingUpdate] = useState<{
    oldVersion: number;
    newVersion: number;
    hasOldUserData: boolean;
    branch: string;
  } | null>(null);

  // Check if current version is installed when branch or version changes
  useEffect(() => {
    const checkInstalled = async () => {
      if (currentVersion === 0) {
        // Version 0 is the auto-updating "latest" instance
        // Check if it's actually installed
        setIsCheckingInstalled(true);
        try {
          const installed = await IsVersionInstalled(currentBranch, 0);
          setIsVersionInstalled(installed);
          // Check if latest needs update
          const needsUpdate = await CheckLatestNeedsUpdate(currentBranch);
          setLatestNeedsUpdate(needsUpdate);
        } catch (e) {
          console.error('Failed to check latest instance:', e);
          setIsVersionInstalled(false);
          setLatestNeedsUpdate(false);
        }
        setIsCheckingInstalled(false);
        return;
      }
      if (currentVersion < 0) {
        // Uninitialized or invalid version
        setIsVersionInstalled(false);
        setLatestNeedsUpdate(false);
        return;
      }
      setIsCheckingInstalled(true);
      try {
        const installed = await IsVersionInstalled(currentBranch, currentVersion);
        setIsVersionInstalled(installed);
        setLatestNeedsUpdate(false); // Versioned instances don't auto-update
      } catch (e) {
        console.error('Failed to check if version installed:', e);
        setIsVersionInstalled(false);
        setLatestNeedsUpdate(false);
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
        if (latestInstalled && !installedWithLatest.includes(0)) installedWithLatest.unshift(0);
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
      if (latestInstalled && !installedWithLatest.includes(0)) installedWithLatest.unshift(0);
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

    // Load background mode, news settings, and accent color
    GetBackgroundMode().then((mode: string) => setBackgroundMode(mode || 'slideshow'));
    GetDisableNews().then((disabled: boolean) => setNewsDisabled(disabled));
    GetAccentColor().then((color: string) => setAccentColor(color || '#FFA845'));

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
        if (latestInstalled && !installedWithLatest.includes(0)) installedWithLatest.unshift(0);
        setInstalledVersions(installedWithLatest);

        // Check if "latest" (version 0) is installed first

        if (latestInstalled) {
          // Use latest if installed
          setCurrentVersion(0);
          await SetSelectedVersion(0);
          setIsVersionInstalled(true);
        } else if (installed && installed.length > 0) {
          // If latest not installed but other versions exist, select the highest installed version
          const highestInstalled = Math.max(...installed.filter(v => v > 0));
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
        }

        setIsLoadingVersions(false);
      } catch (e) {
        console.error('Failed to load settings:', e);
        setIsLoadingVersions(false);
      }
    };
    loadSettings();

    // Event listeners
    const unsubProgress = EventsOn('progress-update', (data: any) => {
      // Handle cancellation first
      if (data.stage === 'cancelled') {
        console.log('Download cancelled event received');
        setIsDownloading(false);
        setProgress(0);
        setDownloaded(0);
        setTotal(0);
        setDownloadState('downloading');
        return;
      }

      setProgress(data.progress);
      setDownloaded(data.downloaded);
      setTotal(data.total);

      // Update download state based on stage or progress ranges
      if (data.stage === 'download' || data.stage === 'update') {
        setDownloadState('downloading');
      } else if (data.stage === 'install') {
        setDownloadState('extracting');
      } else if (data.stage === 'complete') {
        setDownloadState('launching');
        // Game is now installed, update state
        if (data.progress >= 100) {
          setIsVersionInstalled(true);
        }
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
      if (data.state === 'started') {
        setIsGameRunning(true);
        setIsDownloading(false);
        setProgress(0);
        
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
        // Only show error if exit code is non-zero (crash/error)
        // Exit code 0 or undefined = normal exit, null = unknown
        const exitCode = data.exitCode;
        if (exitCode !== undefined && exitCode !== null && exitCode !== 0) {
          try {
            const logs = await GetRecentLogs(10);
            setLaunchTimeoutError({
              message: t('Game crashed with exit code {{code}}', { code: exitCode }),
              logs: logs || []
            });
          } catch {
            setLaunchTimeoutError({
              message: t('Game crashed with exit code {{code}}', { code: exitCode }),
              logs: []
            });
          }
        }
        setIsGameRunning(false);
        setProgress(0);
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
        message: t('Downloaded the latest HyPrism to your Downloads folder.'),
        technical: t('We attempted to open the file so you can install it. If it did not open, go to Downloads and run the file manually.'),
        timestamp: new Date().toISOString()
      });
    } catch (err) {
      console.error('Update failed:', err);
      setError({
        type: 'UPDATE_ERROR',
        message: t('Failed to update launcher'),
        technical: err instanceof Error ? err.message : String(err),
        timestamp: new Date().toISOString()
      });
    } finally {
      setIsUpdatingLauncher(false);
    }
  };

  const handlePlay = async () => {
    const trimmedUsername = username.trim();
    if (!trimmedUsername || trimmedUsername.length < 1 || trimmedUsername.length > 16) {
      setError({
        type: 'VALIDATION',
        message: t('Invalid Nickname'),
        technical: t('Nickname must be between 1 and 16 characters'),
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

    doLaunch();
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

  const handleCustomDirChange = async () => {
    try {
      const input = window.prompt(
        t('Enter the full path where you want HyPrism instances stored:'),
        customInstanceDir || ''
      );

      if (!input || !input.trim()) return;

      const selectedDir = await SetInstanceDirectory(input.trim());

      if (!selectedDir) {
        setError({
          type: 'SETTINGS_ERROR',
          message: t('Failed to change instance directory'),
          technical: t('The path could not be used. Please check it exists and you have write permissions.'),
          timestamp: new Date().toISOString()
        });
        return;
      }

      setCustomInstanceDir(selectedDir);
      console.log('Instance directory updated to:', selectedDir);

      window.alert(
        t('Instance Directory Updated') + '\n\n' +
        t('Game instances will now be stored in:\n{{dir}}\n\nNote: The following remain in AppData:\n• Java Runtime (JRE)\n• Butler tool\n• Cache files\n• Logs\n• Launcher settings\n• WebView2 (EBWebView folder)\n\nYou may need to reinstall the game if switching drives.', { dir: selectedDir })
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

        // Check if latest needs update
        if (currentVersion === 0) {
          const needsUpdate = await CheckLatestNeedsUpdate(currentBranch);
          setLatestNeedsUpdate(needsUpdate);
        }
      } catch (e) {
        console.error('Failed to reload versions after directory change:', e);
      }
      setIsLoadingVersions(false);
    } catch (err) {
      console.error('Failed to change instance directory:', err);
      setError({
        type: 'SETTINGS_ERROR',
        message: t('Failed to change instance directory'),
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

  // If onboarding needs to be shown, render only the onboarding modal
  if (showOnboarding) {
    return (
      <Suspense fallback={<div className="fixed inset-0 bg-[#0a0a0a]" />}>
        <OnboardingModal onComplete={handleOnboardingComplete} />
      </Suspense>
    );
  }

  return (
    <div className="relative w-screen h-screen bg-[#090909] text-white overflow-hidden font-sans select-none">
      <BackgroundImage mode={backgroundMode} />

      {/* Music Player - positioned in top right */}
      <div className="absolute top-4 right-4 z-20">
        <MusicPlayer forceMuted={isGameRunning} />
      </div>

      {isUpdatingLauncher && (
        <UpdateOverlay
          progress={progress}
          downloaded={updateStats.d}
          total={updateStats.t}
        />
      )}

      <main className="relative z-10 h-full p-10 flex flex-col justify-between pt-[60px]">
        <div className="flex justify-between items-start">
          <ProfileSection
            username={username}
            uuid={uuid}
            isEditing={isEditing}
            onEditToggle={setIsEditing}
            onUserChange={handleNickChange}
            updateAvailable={!!updateAsset}
            onUpdate={handleUpdate}
            launcherVersion={launcherVersion}
            onOpenProfileEditor={() => setShowProfileEditor(true)}
            refreshTrigger={avatarRefreshTrigger}
          />
          {/* Hytale Logo & News - Right Side */}
          <div className="flex flex-col items-end gap-3">
            <img src={hytaleLogo} alt="Hytale" className="h-24 drop-shadow-2xl" />
            {/* Social Buttons Row */}
            <div className="flex items-center gap-3">
              {/* Discord Button */}
              <button
                onClick={() => BrowserOpenURL('https://discord.gg/3U8KNbap3g')}
                className="p-2 rounded-xl hover:bg-[#5865F2]/20 transition-all duration-150 cursor-pointer active:scale-95"
                title={t('Join Discord')}
              >
                <DiscordIcon size={28} className="drop-shadow-lg" />
              </button>
              {/* GitHub Button */}
              <button
                onClick={() => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism')}
                className="w-10 h-10 rounded-xl bg-transparent flex items-center justify-center text-white/60 hover:text-white hover:bg-white/10 active:scale-95 transition-all duration-150"
                title={t('GitHub Repository')}
              >
                <svg xmlns="http://www.w3.org/2000/svg" width="20" height="20" viewBox="0 0 24 24" fill="currentColor"><path d="M12 0c-6.626 0-12 5.373-12 12 0 5.302 3.438 9.8 8.207 11.387.599.111.793-.261.793-.577v-2.234c-3.338.726-4.033-1.416-4.033-1.416-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.083-.729.083-.729 1.205.084 1.839 1.237 1.839 1.237 1.07 1.834 2.807 1.304 3.492.997.107-.775.418-1.305.762-1.604-2.665-.305-5.467-1.334-5.467-5.931 0-1.311.469-2.381 1.236-3.221-.124-.303-.535-1.524.117-3.176 0 0 1.008-.322 3.301 1.23.957-.266 1.983-.399 3.003-.404 1.02.005 2.047.138 3.006.404 2.291-1.552 3.297-1.23 3.297-1.23.653 1.653.242 2.874.118 3.176.77.84 1.235 1.911 1.235 3.221 0 4.609-2.807 5.624-5.479 5.921.43.372.823 1.102.823 2.222v3.293c0 .319.192.694.801.576 4.765-1.589 8.199-6.086 8.199-11.386 0-6.627-5.373-12-12-12z"/></svg>
              </button>
              {/* Bug Report Button */}
              <button
                onClick={() => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism/issues/new')}
                className="w-10 h-10 rounded-xl bg-transparent flex items-center justify-center text-white/60 hover:text-red-400 hover:bg-red-400/10 active:scale-95 transition-all duration-150"
                title={t('Report a Bug')}
              >
                <svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m8 2 1.88 1.88"/><path d="M14.12 3.88 16 2"/><path d="M9 7.13v-1a3.003 3.003 0 1 1 6 0v1"/><path d="M12 20c-3.3 0-6-2.7-6-6v-3a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v3c0 3.3-2.7 6-6 6"/><path d="M12 20v-9"/><path d="M6.53 9C4.6 8.8 3 7.1 3 5"/><path d="M6 13H2"/><path d="M3 21c0-2.1 1.7-3.9 3.8-4"/><path d="M20.97 5c0 2.1-1.6 3.8-3.5 4"/><path d="M22 13h-4"/><path d="M17.2 17c2.1.1 3.8 1.9 3.8 4"/></svg>
              </button>
            </div>
            {!newsDisabled && (
              <Suspense fallback={<div className="w-80 h-32 animate-pulse bg-white/5 rounded-xl" />}>
                <NewsPreview
                  isPaused={isDownloading}
                  getNews={async (count) => {
                    const releases = await fetchLauncherReleases(i18n.language);
                    const hytale = await GetNews(Math.max(0, count));

                    const hytaleItems = (hytale || []).map((item: any) => {
                      const dateMs = parseDateMs(item?.publishedAt || item?.date);
                      return {
                        item: { 
                          ...item, 
                          source: 'hytale' as const,
                          date: formatDateConsistent(dateMs, i18n.language)
                        },
                        dateMs
                      };
                    });

                    const combined = [...releases, ...hytaleItems]
                      .sort((a, b) => b.dateMs - a.dateMs)
                      .map((x) => x.item);

                    return combined;
                  }}
                />
              </Suspense>
            )}
          </div>
        </div>

        <ControlSection
          onPlay={handlePlay}
          onDownload={handlePlay}
          onExit={handleExit}
          onCancelDownload={handleCancelDownload}
          isDownloading={isDownloading}
          downloadState={downloadState}
          canCancel={isDownloading && !isGameRunning && (downloadState === 'downloading' || downloadState === 'extracting')}
          isGameRunning={isGameRunning}
          isVersionInstalled={isVersionInstalled}
          isCheckingInstalled={isCheckingInstalled}
          progress={progress}
          downloaded={downloaded}
          total={total}
          currentBranch={currentBranch}
          currentVersion={currentVersion}
          availableVersions={availableVersions}
          installedVersions={installedVersions}
          isLoadingVersions={isLoadingVersions}
          latestNeedsUpdate={latestNeedsUpdate}
          onBranchChange={handleBranchChange}
          onVersionChange={handleVersionChange}
          onCustomDirChange={handleCustomDirChange}
          onOpenSettings={() => setShowSettings(true)}
          actions={{
            openFolder: () => OpenInstanceFolder(currentBranch, currentVersion),
            showDelete: () => setShowDelete(true),
            showModManager: (query?: string) => {
              setModManagerSearchQuery(query || '');
              setShowModManager(true);
            }
          }}
        />
      </main>

      {/* Modals - wrapped in Suspense for lazy loading */}
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

        {showModManager && (
          <ModManager
            onClose={() => setShowModManager(false)}
            currentBranch={currentBranch}
            currentVersion={currentVersion}
            initialSearchQuery={modManagerSearchQuery}
          />
        )}

        {showSettings && (
          <SettingsModal
            onClose={() => setShowSettings(false)}
            launcherBranch={launcherBranch}
            onLauncherBranchChange={handleLauncherBranchChange}
            onShowModManager={(query) => {
              setModManagerSearchQuery(query || '');
              setShowModManager(true);
            }}
            rosettaWarning={rosettaWarning}
            onBackgroundModeChange={(mode) => setBackgroundMode(mode)}
            onNewsDisabledChange={(disabled) => setNewsDisabled(disabled)}
            onAccentColorChange={(color) => setAccentColor(color)}
          />
        )}

        {/* Profile Editor */}
        {showProfileEditor && (
          <ProfileEditor
            isOpen={showProfileEditor}
            onClose={() => setShowProfileEditor(false)}
            onProfileUpdate={reloadProfile}
          />
        )}

      </Suspense>
    </div>
  );
};

export default App;
