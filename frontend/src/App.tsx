import React, { useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { GameBranch } from './constants/enums';
import { BackgroundImage } from './components/BackgroundImage';
import { ProfileSection } from './components/ProfileSection';
import { ControlSection } from './components/ControlSection';
import { MusicPlayer } from './components/MusicPlayer';
import { UpdateOverlay } from './components/UpdateOverlay';
import { ErrorModal } from './components/ErrorModal';
import { DeleteConfirmationModal } from './components/DeleteConfirmationModal';
import { ModManager } from './components/ModManager';
import hytaleLogo from './assets/logo.png';

import {
  DownloadAndLaunch,
  OpenFolder,
  GetNick,
  SetNick,
  GetUUID,
  SetUUID,
  DeleteGame,
  Update,
  ExitGame,
  IsGameRunning,
  GetRecentLogs,
  // Version Manager
  GetVersionType,
  SetVersionType,
  SetSelectedVersion,
  GetVersionList,
  IsVersionInstalled,
  GetInstalledVersionsForBranch,
  CheckLatestNeedsUpdate,
  // Settings
  GetCustomInstanceDir,
  SetInstanceDirectory,
  GetNews,
  GetLauncherVersion,
} from '../wailsjs/go/app/App';
import { EventsOn } from '../wailsjs/runtime/runtime';
import { NewsPreview } from './components/NewsPreview';
import appIcon from './assets/appicon.png';

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

const fetchLauncherReleases = async () => {
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
          date: new Date(dateMs || Date.now()).toLocaleDateString(),
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

// Helper to call CancelDownload via RPC
const CancelDownload = (): Promise<void> => {
  return new Promise((resolve, reject) => {
    const id = `call_${Date.now()}_${Math.random()}`;
    const message = JSON.stringify({
      method: 'CancelDownload',
      id: id
    });
    
    const handler = (e: CustomEvent) => {
      const data = e.detail;
      if (data.Id === id) {
        window.removeEventListener(id, handler as EventListener);
        if (data.Error) reject(new Error(data.Error));
        else resolve(data.Result);
      }
    };
    
    window.addEventListener(id, handler as EventListener);
    (window as any).external?.sendMessage?.(message);
  });
};

const App: React.FC = () => {
  const { t } = useTranslation();
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
  const [error, setError] = useState<any>(null);
  const [launchTimeoutError, setLaunchTimeoutError] = useState<{ message: string; logs: string[] } | null>(null);

  // Game launch tracking
  const gameLaunchTimeRef = useRef<number | null>(null);
  const LAUNCH_TIMEOUT_MS = 60000; // 1 minute

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
          // Check if we hit the timeout without the game ever running properly
          const launchTime = gameLaunchTimeRef.current;
          const elapsed = launchTime ? Date.now() - launchTime : 0;
          
          if (elapsed < LAUNCH_TIMEOUT_MS) {
            // Game stopped before timeout - likely crashed or failed to launch
            try {
              const logs = await GetRecentLogs(10);
              setLaunchTimeoutError({
                message: t('Game process exited unexpectedly'),
                logs: logs || []
              });
            } catch {
              setLaunchTimeoutError({
                message: t('Game process exited unexpectedly'),
                logs: []
              });
            }
          }
          
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

  useEffect(() => {
    // Initialize user settings
    GetNick().then((n: string) => n && setUsername(n));
    GetUUID().then((u: string) => u && setUuid(u));
    GetLauncherVersion().then((v: string) => setLauncherVersion(v));
    GetCustomInstanceDir().then((dir: string) => dir && setCustomInstanceDir(dir));

    // Load saved branch and version - must load branch first, then version
    const loadSettings = async () => {
      try {
        // Get saved branch (defaults to "release" in backend if not set)
        const savedBranch = await GetVersionType();
        const branch = normalizeBranch(savedBranch || GameBranch.RELEASE);
        setCurrentBranch(branch);

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
      setProgress(data.progress);
      setDownloaded(data.downloaded);
      setTotal(data.total);

      // Update download state based on progress ranges
      if (data.progress >= 0 && data.progress < 5) {
        setDownloadState('downloading'); // Butler installation
      } else if (data.progress >= 5 && data.progress < 70) {
        setDownloadState('downloading'); // Downloading PWR
      } else if (data.progress >= 70 && data.progress < 100) {
        setDownloadState('extracting'); // Extracting with butler
      } else if (data.progress >= 100) {
        setDownloadState('launching'); // Ready to launch
      }

      // When complete stage with 100% is received, game is launching
      if (data.stage === 'complete' && data.progress >= 100) {
        // Game is now installed, update state
        setIsVersionInstalled(true);
      }
    });
    
    // Game state event listener
    const unsubGameState = EventsOn('game-state', (data: any) => {
      if (data.state === 'started') {
        setIsGameRunning(true);
        setIsDownloading(false);
        setProgress(0);
      } else if (data.state === 'stopped') {
        setIsGameRunning(false);
        setProgress(0);
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
      const ok = await Update();
      if (ok) {
        setError({
          type: 'INFO',
          message: t('Downloaded the latest HyPrism to your Downloads folder.'),
          technical: t('We attempted to open the file so you can install it. If it did not open, go to Downloads and run the file manually.'),
          timestamp: new Date().toISOString()
        });
      } else {
        setError({
          type: 'INFO',
          message: t('Could not auto-download. Please download manually.'),
          technical: 'https://github.com/yyyumeniku/HyPrism/releases/latest',
          timestamp: new Date().toISOString()
        });
      }
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
    if (!username.trim() || username.length > 16) {
      setError({
        type: 'VALIDATION',
        message: t('Invalid Nickname'),
        technical: t('Nickname must be between 1 and 16 characters'),
        timestamp: new Date().toISOString()
      });
      return;
    }

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

  const handleCancelDownload = async () => {
    try {
      await CancelDownload();
      setIsDownloading(false);
      setProgress(0);
      setDownloaded(0);
      setTotal(0);
    } catch (err) {
      console.error('Cancel failed:', err);
    }
  };

  const handleNickChange = async (newNick: string) => {
    setUsername(newNick);
    await SetNick(newNick);
  };

  const handleUuidChange = async (newUuid: string) => {
    const ok = await SetUUID(newUuid);
    if (ok) {
      setUuid(newUuid);
      return true;
    }
    setError({
      type: 'VALIDATION',
      message: t('Invalid UUID'),
      technical: t('Please enter a valid UUID (e.g. 123e4567-e89b-12d3-a456-426614174000).'),
      timestamp: new Date().toISOString()
    });
    return false;
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

      // Show info about what gets moved
      setError({
        type: 'INFO',
        message: t('Instance Directory Updated'),
        technical: t('Game instances will now be stored in:\n{{dir}}\n\nNote: The following remain in AppData:\n• Java Runtime (JRE)\n• Butler tool\n• Cache files\n• Logs\n• Launcher settings\n• WebView2 (EBWebView folder)\n\nYou may need to reinstall the game if switching drives.', { dir: selectedDir }),
        timestamp: new Date().toISOString()
      });

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

  return (
    <div className="relative w-screen h-screen bg-[#090909] text-white overflow-hidden font-sans select-none">
      <BackgroundImage />

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
            onUuidChange={handleUuidChange}
            updateAvailable={!!updateAsset}
            onUpdate={handleUpdate}
            launcherVersion={launcherVersion}
          />
          {/* Hytale Logo & News - Right Side */}
          <div className="flex flex-col items-end gap-3">
            <img src={hytaleLogo} alt="Hytale" className="h-24 drop-shadow-2xl" />
            <NewsPreview
              getNews={async (count) => {
                const releases = await fetchLauncherReleases();
                const hytale = await GetNews(Math.max(0, count));

                const hytaleItems = (hytale || []).map((item: any) => ({
                  item: { ...item, source: 'hytale' as const },
                  dateMs: parseDateMs(item?.date)
                }));

                const combined = [...releases, ...hytaleItems]
                  .sort((a, b) => b.dateMs - a.dateMs)
                  .map((x) => x.item);

                return combined;
              }}
            />
          </div>
        </div>

        <ControlSection
          onPlay={handlePlay}
          onDownload={handlePlay}
          onExit={handleExit}
          onCancelDownload={handleCancelDownload}
          isDownloading={isDownloading}
          downloadState={downloadState}
          canCancel={downloadState === 'downloading' && !isGameRunning}
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
          actions={{
            openFolder: OpenFolder,
            showDelete: () => setShowDelete(true),
            showModManager: (query?: string) => {
              setModManagerSearchQuery(query || '');
              setShowModManager(true);
            }
          }}
        />
      </main>

      {/* Modals */}
      {showDelete && (
        <DeleteConfirmationModal
          onConfirm={() => {
            DeleteGame(currentBranch, currentVersion);
            setShowDelete(false);
          }}
          onCancel={() => setShowDelete(false)}
        />
      )}

      {error && (
        <ErrorModal
          error={error}
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
            timestamp: new Date().toISOString()
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
    </div>
  );
};

export default App;
