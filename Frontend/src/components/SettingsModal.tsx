import React, { useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import { X, Github, Bug, Check, AlertTriangle, ChevronDown, ExternalLink, Power, FolderOpen, Trash2, Settings, Database, Globe, Code, Image, Loader2, FlaskConical, RotateCcw, Monitor, Download, HardDrive, Package, Box, Wifi, Coffee, Server, Edit3, FileText, Terminal } from 'lucide-react';
import { ipc, on } from '@/lib/ipc';
import { changeLanguage } from '../i18n';

// Alias for compatibility — maps to ipc.browser.open
const BrowserOpenURL = (url: string) => ipc.browser.open(url);

// InstalledVersionInfo type (was in api/backend)
export interface InstalledVersionInfo {
    id?: string;
  branch: string;
  version: number;
  path: string;
  sizeBytes?: number;
  isLatest?: boolean;
  isLatestInstance?: boolean;
  playTimeSeconds?: number;
  playTimeFormatted?: string;
  createdAt?: string;
  lastPlayedAt?: string;
  updatedAt?: string;
}

// Settings-backed helpers
async function GetCloseAfterLaunch(): Promise<boolean> { return (await ipc.settings.get()).closeAfterLaunch ?? false; }
async function SetCloseAfterLaunch(v: boolean): Promise<void> { await ipc.settings.update({ closeAfterLaunch: v }); }
async function GetBackgroundMode(): Promise<string> { return (await ipc.settings.get()).backgroundMode ?? 'image'; }
async function SetBackgroundMode(v: string): Promise<void> { await ipc.settings.update({ backgroundMode: v }); }
async function GetCustomInstanceDir(): Promise<string> { return (await ipc.settings.get()).instanceDirectory ?? ''; }
async function GetNick(): Promise<string> { return (await ipc.profile.get()).nick ?? 'HyPrism'; }
async function GetUUID(): Promise<string> { return (await ipc.profile.get()).uuid ?? ''; }
async function GetAvatarPreview(): Promise<string | null> { return (await ipc.profile.get()).avatarPath ?? null; }
async function GetAuthDomain(): Promise<string> { return (await ipc.settings.get()).authDomain ?? 'sessions.sanasol.ws'; }
async function GetDiscordLink(): Promise<string> { console.warn('[IPC] GetDiscordLink: stub'); return 'https://discord.gg/ekZqTtynjp'; }

// Real IPC functions that now have channels
async function GetLauncherFolderPath(): Promise<string> { return ipc.settings.launcherPath(); }
async function GetDefaultInstanceDir(): Promise<string> { return ipc.settings.defaultInstanceDir(); }
async function SetInstanceDirectory(path: string): Promise<{ success: boolean, path: string }> { 
    const result = await ipc.settings.setInstanceDir(path); 
    console.log('[IPC] SetInstanceDirectory result:', result);
    return result;
}
async function BrowseFolder(initialPath?: string): Promise<string> { return (await ipc.file.browseFolder(initialPath)) ?? ''; }
async function BrowseJavaExecutable(): Promise<string> { return (await ipc.file.browseJavaExecutable()) ?? ''; }
async function FileExists(path: string): Promise<boolean> { return await ipc.file.exists(path); }

// TODO: These still need dedicated IPC channels
const _stub = <T,>(name: string, fb: T) => async (..._a: any[]): Promise<T> => { console.warn(`[IPC] ${name}: no channel`); return fb; };
const DeleteLauncherData = _stub('DeleteLauncherData', true);
const GetInstalledVersionsDetailed = async (): Promise<InstalledVersionInfo[]> => {
    const instances = await ipc.game.instances();
    return (instances || []).map((inst) => ({
        id: inst.id,
        branch: inst.branch,
        version: inst.version,
        path: inst.path,
        sizeBytes: inst.totalSize,
        isLatest: inst.version === 0,
        isLatestInstance: inst.version === 0,
    }));
};
const ExportInstance = _stub('ExportInstance', '');
const DeleteGame = _stub('DeleteGame', false);
const ResetOnboarding = _stub('ResetOnboarding', undefined as void);
const ImportInstanceFromZip = _stub('ImportInstanceFromZip', true);
import { useAccentColor } from '../contexts/AccentColorContext';

import { DiscordIcon } from './icons/DiscordIcon';
import { Language } from '../constants/enums';
import { LANGUAGE_CONFIG } from '../constants/languages';
import { ACCENT_COLORS } from '../constants/colors';
import appIcon from '../assets/images/logo.png';
import { LogsPage } from '../pages/LogsPage';

// Import background images for previews
const backgroundModulesJpg = import.meta.glob('../assets/backgrounds/bg_*.jpg', { query: '?url', import: 'default', eager: true });
const backgroundModulesPng = import.meta.glob('../assets/backgrounds/bg_*.png', { query: '?url', import: 'default', eager: true });
const allBackgrounds = { ...backgroundModulesJpg, ...backgroundModulesPng };
const backgroundImages = Object.entries(allBackgrounds)
  .sort(([a], [b]) => {
    const numA = parseInt(a.match(/bg_(\d+)/)?.[1] || '0');
    const numB = parseInt(b.match(/bg_(\d+)/)?.[1] || '0');
    return numA - numB;
  })
  .map(([path, url]) => ({ 
    name: path.match(/bg_(\d+)/)?.[0] || 'bg_1', 
    url: url as string 
  }));

interface Contributor {
    login: string;
    avatar_url: string;
    html_url: string;
    contributions: number;
}

interface SettingsModalProps {
    onClose: () => void;
    launcherBranch: string;
    onLauncherBranchChange: (branch: string) => void;
    onShowModManager?: () => void;
    rosettaWarning?: { message: string; command: string; tutorialUrl?: string } | null;
    onBackgroundModeChange?: (mode: string) => void;
    onAccentColorChange?: (color: string) => void;
    onInstanceDeleted?: () => void;
    onAuthSettingsChange?: () => void;
    pageMode?: boolean;
    isGameRunning?: boolean;
    onMovingDataChange?: (isMoving: boolean) => void;
}

type SettingsTab = 'general' | 'java' | 'visual' | 'network' | 'graphics' | 'variables' | 'logs' | 'language' | 'data' | 'instances' | 'about' | 'developer';

// Auth server base URL for avatar/skin head
const DEFAULT_AUTH_DOMAIN = 'sessions.sanasol.ws';

export const SettingsModal: React.FC<SettingsModalProps> = ({
    onClose,
    launcherBranch,
    onLauncherBranchChange,
    onShowModManager,
    rosettaWarning,
    onBackgroundModeChange,
    onAccentColorChange,
    onInstanceDeleted,
    onAuthSettingsChange,
    pageMode: isPageMode = false,
    isGameRunning = false,
    onMovingDataChange
}) => {
    const { i18n, t } = useTranslation();
    const [activeTab, setActiveTab] = useState<SettingsTab>('general');
    const [isLanguageOpen, setIsLanguageOpen] = useState(false);
    const [isBranchOpen, setIsBranchOpen] = useState(false);

    const [selectedLauncherBranch, setSelectedLauncherBranch] = useState(launcherBranch);
    const [closeAfterLaunch, setCloseAfterLaunch] = useState(false);
    const [javaArguments, setJavaArguments] = useState('');
    const [javaRamMb, setJavaRamMb] = useState(4096);
    const [javaInitialRamMb, setJavaInitialRamMb] = useState(1024);
    const [javaGcMode, setJavaGcMode] = useState<'auto' | 'g1'>('auto');
    const [javaRuntimeMode, setJavaRuntimeMode] = useState<'bundled' | 'custom'>('bundled');
    const [customJavaPath, setCustomJavaPath] = useState('');
    const [javaCustomPathError, setJavaCustomPathError] = useState('');
    const [javaArgumentsError, setJavaArgumentsError] = useState('');
    const [systemMemoryMb, setSystemMemoryMb] = useState(8192);
    const [launcherFolderPath, setLauncherFolderPath] = useState('');
    const [instanceDir, setInstanceDir] = useState('');
    const [devModeEnabled, setDevModeEnabled] = useState(false);
    const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
    const [onlineMode, setOnlineMode] = useState(true);
    const [backgroundMode, setBackgroundModeState] = useState('slideshow');
    const [showAllBackgrounds, setShowAllBackgrounds] = useState(false);
    const [launcherDataDir, setLauncherDataDir] = useState('');
    const [gpuPreference, setGpuPreferenceState] = useState<string>('dedicated');
    const [gpuAdapters, setGpuAdapters] = useState<Array<{ name: string; vendor: string; type: string }>>([]);
    const [hasSingleGpu, setHasSingleGpu] = useState(false);
    const [gameEnvVars, setGameEnvVars] = useState('');
    const [gameEnvVarsError, setGameEnvVarsError] = useState('');
    
    // Environment variable presets state
    const [envForceX11, setEnvForceX11] = useState(false);
    const [envDisableVkLayers, setEnvDisableVkLayers] = useState(false);
    const [envForceAmdVulkan, setEnvForceAmdVulkan] = useState(false);
    
    // Platform detection (for showing/hiding Linux-only features)
    const [isLinux, setIsLinux] = useState(false);

    const detectedSystemRamMb = Math.max(4096, systemMemoryMb);
    const minJavaRamMb = 1024;
    const maxJavaRamMb = Math.max(minJavaRamMb, Math.floor((detectedSystemRamMb * 0.75) / 256) * 256);
    
    // Data move progress state
    const [isMovingData, setIsMovingData] = useState(false);
    const [moveProgress, setMoveProgress] = useState(0);
    const [moveCurrentFile, setMoveCurrentFile] = useState('');
    
    // Notify parent about moving state change (for hiding navigation)
    useEffect(() => {
        onMovingDataChange?.(isMovingData);
    }, [isMovingData, onMovingDataChange]);
    
    const { accentColor, accentTextColor, setAccentColor: setAccentColorContext } = useAccentColor();

    // Glass-aware control background class for toggle rows, dropdowns, inputs
    const gc = 'glass-control-solid';
    const [contributors, setContributors] = useState<Contributor[]>([]);
    const [isLoadingContributors, setIsLoadingContributors] = useState(false);
    const languageDropdownRef = useRef<HTMLDivElement>(null);
    const branchDropdownRef = useRef<HTMLDivElement>(null);
    
    // Instances state
    const [installedInstances, setInstalledInstances] = useState<InstalledVersionInfo[]>([]);
    const [isLoadingInstances, setIsLoadingInstances] = useState(false);
    const [exportingInstance, setExportingInstance] = useState<string | null>(null);
    const [_exportMessage, setExportMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
    const [instanceToDelete, setInstanceToDelete] = useState<InstalledVersionInfo | null>(null);
    const [isImportingInstance, setIsImportingInstance] = useState(false);
    const [showImportModal, setShowImportModal] = useState<{ zipBase64: string; fileName: string } | null>(null);
    const [importTargetBranch, setImportTargetBranch] = useState<'release' | 'pre-release'>('release');
    const [importTargetVersion, setImportTargetVersion] = useState<number>(0);
    const [showInstanceExportModal, setShowInstanceExportModal] = useState<InstalledVersionInfo | null>(null);
    const [instanceExportPath, setInstanceExportPath] = useState<string>('');


    // Profile state
    const [_profileUsername, setProfileUsername] = useState('');
    const [profileUuid, setProfileUuid] = useState('');
    const [editUsernameValue, setEditUsernameValue] = useState('');
    const [editUuidValue, setEditUuidValue] = useState('');
    const [authDomain, setAuthDomain] = useState('sessions.sanasol.ws');
    const [authMode, setAuthModeState] = useState<'default' | 'official' | 'custom'>('default');
    const [customAuthDomain, setCustomAuthDomain] = useState('');
    const [_localAvatar, setLocalAvatar] = useState<string | null>(null);

    const parseJavaHeapMb = (args: string, flag: 'xmx' | 'xms'): number | null => {
        const match = args.match(new RegExp(`(?:^|\\s)-${flag}(\\d+(?:\\.\\d+)?)([kKmMgG])(?:\\s|$)`, 'i'));
        if (!match) {
            return null;
        }

        const value = Number.parseFloat(match[1]);
        if (!Number.isFinite(value) || value <= 0) {
            return null;
        }

        const unit = match[2].toUpperCase();
        if (unit === 'G') {
            return Math.round(value * 1024);
        }
        if (unit === 'K') {
            return Math.max(1, Math.round(value / 1024));
        }

        return Math.round(value);
    };

    const upsertJavaHeapArgument = (args: string, flag: 'Xmx' | 'Xms', ramMb: number): string => {
        const pattern = new RegExp(`(?:^|\\s)-${flag}\\S+`, 'gi');
        const sanitized = args.replace(pattern, ' ').replace(/\s+/g, ' ').trim();
        const heapArg = `-${flag}${ramMb}M`;
        return sanitized.length > 0 ? `${heapArg} ${sanitized}` : heapArg;
    };

    const removeJavaFlag = (args: string, pattern: RegExp): string => {
        return args.replace(pattern, ' ').replace(/\s+/g, ' ').trim();
    };

    const upsertJavaGcMode = (args: string, mode: 'auto' | 'g1'): string => {
        const withoutGc = removeJavaFlag(args, /(?:^|\s)-XX:[+-]UseG1GC(?:\s|$)/gi);
        if (mode === 'auto') {
            return withoutGc;
        }

        return withoutGc.length > 0 ? `-XX:+UseG1GC ${withoutGc}` : '-XX:+UseG1GC';
    };

    const detectJavaGcMode = (args: string): 'auto' | 'g1' => {
        return /(?:^|\s)-XX:\+UseG1GC(?:\s|$)/i.test(args) ? 'g1' : 'auto';
    };

    const sanitizeAdvancedJavaArguments = (args: string): { sanitized: string; blocked: boolean } => {
        let result = args;

        const blockedPatterns = [
            /(?:^|\s)-javaagent:\S+/gi,
            /(?:^|\s)-agentlib:\S+/gi,
            /(?:^|\s)-agentpath:\S+/gi,
            /(?:^|\s)-Xbootclasspath(?::\S+)?/gi,
            /(?:^|\s)-jar(?:\s+\S+)?/gi,
            /(?:^|\s)-cp(?:\s+\S+)?/gi,
            /(?:^|\s)-classpath(?:\s+\S+)?/gi,
            /(?:^|\s)--class-path(?:\s+\S+)?/gi,
            /(?:^|\s)--module-path(?:\s+\S+)?/gi,
            /(?:^|\s)-Djava\.home=\S+/gi,
        ];

        const hadBlocked = blockedPatterns.some((pattern) => pattern.test(result));

        for (const pattern of blockedPatterns) {
            result = result.replace(pattern, ' ');
        }

        result = result.replace(/\s+/g, ' ').trim();
        return { sanitized: result, blocked: hadBlocked };
    };

    const formatRamLabel = (ramMb: number): string => {
        const gb = ramMb / 1024;
        return Number.isInteger(gb) ? `${gb} GB` : `${gb.toFixed(1)} GB`;
    };

    useEffect(() => {
        const loadSettings = async () => {
            try {
                // Load platform info first
                try {
                    const platformInfo = await ipc.system.platform();
                    setIsLinux(platformInfo.isLinux);
                } catch (err) {
                    console.error('Failed to load platform info:', err);
                }
                
                const closeAfter = await GetCloseAfterLaunch();
                setCloseAfterLaunch(closeAfter);
                
                const folderPath = await GetLauncherFolderPath();
                setLauncherFolderPath(folderPath);
                
                const customDir = await GetCustomInstanceDir();
                const defaultInstanceDir = await GetDefaultInstanceDir();
                setInstanceDir(customDir || defaultInstanceDir); // Show real path

                const settingsSnapshot = await ipc.settings.get();
                const online = settingsSnapshot.onlineMode ?? true;
                setOnlineMode(online);

                const loadedJavaArgs = settingsSnapshot.javaArguments;
                const normalizedJavaArgs = typeof loadedJavaArgs === 'string' ? loadedJavaArgs : '';
                setJavaArguments(normalizedJavaArgs);

                const loadedSystemMemoryMb = typeof settingsSnapshot.systemMemoryMb === 'number' && settingsSnapshot.systemMemoryMb > 0
                    ? settingsSnapshot.systemMemoryMb
                    : systemMemoryMb;
                setSystemMemoryMb(loadedSystemMemoryMb);
                const loadedDetectedSystemRamMb = Math.max(4096, loadedSystemMemoryMb);
                const loadedMaxJavaRamMb = Math.max(minJavaRamMb, Math.floor((loadedDetectedSystemRamMb * 0.75) / 256) * 256);

                const parsedJavaRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xmx');
                const targetJavaRamMb = parsedJavaRamMb ?? 4096;
                const clampedJavaRamMb = Math.min(loadedMaxJavaRamMb, Math.max(minJavaRamMb, Math.round(targetJavaRamMb / 256) * 256));
                setJavaRamMb(clampedJavaRamMb);

                const parsedJavaInitialRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xms');
                const fallbackInitial = Math.max(minJavaRamMb, Math.min(clampedJavaRamMb, Math.floor(clampedJavaRamMb / 2 / 256) * 256 || minJavaRamMb));
                const initialRamTarget = parsedJavaInitialRamMb ?? fallbackInitial;
                const clampedInitial = Math.min(clampedJavaRamMb, Math.max(minJavaRamMb, Math.round(initialRamTarget / 256) * 256));
                setJavaInitialRamMb(clampedInitial);

                setJavaGcMode(detectJavaGcMode(normalizedJavaArgs));
                setJavaRuntimeMode(settingsSnapshot.useCustomJava ? 'custom' : 'bundled');
                setCustomJavaPath(typeof settingsSnapshot.customJavaPath === 'string' ? settingsSnapshot.customJavaPath : '');
                
                const bgMode = await GetBackgroundMode();
                setBackgroundModeState(bgMode);

                setLauncherDataDir(folderPath);
                
                // Load GPU preference and adapters
                const gpu = settingsSnapshot.gpuPreference ?? 'dedicated';
                setGpuPreferenceState(gpu);
                
                // Load custom game environment variables and parse presets
                const envVars = settingsSnapshot.gameEnvironmentVariables ?? '';
                const envVarsStr = typeof envVars === 'string' ? envVars : '';
                setGameEnvVars(envVarsStr);
                
                // Parse preset toggles from env vars string
                setEnvForceX11(envVarsStr.includes('SDL_VIDEODRIVER=x11'));
                setEnvDisableVkLayers(envVarsStr.includes('VK_LOADER_LAYERS_DISABLE=all'));
                setEnvForceAmdVulkan(envVarsStr.includes('VK_ICD_FILENAMES=') && envVarsStr.includes('radeon'));
                
                try {
                    const adapters = await ipc.system.gpuAdapters();
                    setGpuAdapters(adapters || []);
                    const singleGpu = !adapters || adapters.length <= 1;
                    setHasSingleGpu(singleGpu);
                    // If only one GPU, force "auto" mode
                    if (singleGpu && gpu !== 'auto') {
                        setGpuPreferenceState('auto');
                        await ipc.settings.update({ gpuPreference: 'auto' });
                    }
                } catch (err) {
                    console.error('Failed to load GPU adapters:', err);
                }
                
                // Load profile data
                const username = await GetNick();
                // Use 'HyPrism' as fallback - matches backend default
                const displayName = username || 'HyPrism';
                setProfileUsername(displayName);
                setEditUsernameValue(displayName);
                
                const uuid = await GetUUID();
                setProfileUuid(uuid || '');
                setEditUuidValue(uuid || '');
                
                // Load auth domain for profile pictures
                const domain = await GetAuthDomain();
                if (domain) {
                    setAuthDomain(domain);
                    // Derive auth mode from domain value
                    if (domain === 'sessionserver.hytale.com' || domain === 'official') {
                        setAuthModeState('official');
                    } else if (domain === 'sessions.sanasol.ws' || domain === '' || !domain) {
                        setAuthModeState('default');
                    } else {
                        setAuthModeState('custom');
                        setCustomAuthDomain(domain);
                    }
                }
                
                // Load local avatar preview
                try {
                    const avatar = await GetAvatarPreview();
                    if (avatar) setLocalAvatar(avatar);
                } catch { /* ignore */ }
                
                // Accent color is now handled by AccentColorContext
                
                const savedDevMode = localStorage.getItem('hyprism_dev_mode');
                setDevModeEnabled(savedDevMode === 'true');
            } catch (err) {
                console.error('Failed to load settings:', err);
            }
        };
        loadSettings();
    }, []);

    // Subscribe to data move progress events
    useEffect(() => {
        const unsub = on('hyprism:game:progress', (data: any) => {
            if (data.state === 'moving-instances') {
                setIsMovingData(true);
                setMoveProgress(data.progress ?? 0);
                // Extract filename from args if available
                if (Array.isArray(data.args) && data.args.length > 0) {
                    setMoveCurrentFile(String(data.args[0]));
                }
            } else if (data.state === 'moving-instances-complete' && isMovingData) {
                setMoveProgress(100);
                setTimeout(() => {
                    setIsMovingData(false);
                    setMoveProgress(0);
                    setMoveCurrentFile('');
                }, 1500);
            }
        });
        return unsub;
    }, [isMovingData]);

    // Load contributors when About tab is active
    useEffect(() => {
        if (activeTab === 'about' && contributors.length === 0 && !isLoadingContributors) {
            setIsLoadingContributors(true);
            fetch('https://api.github.com/repos/yyyumeniku/HyPrism/contributors')
                .then(res => res.json())
                .then(data => {
                    if (Array.isArray(data)) {
                        setContributors(data);
                    }
                })
                .catch(err => console.error('Failed to load contributors:', err))
                .finally(() => setIsLoadingContributors(false));
        }
    }, [activeTab, contributors.length, isLoadingContributors]);
    
    // Load installed instances when Instances tab is active
    useEffect(() => {
        if (activeTab === 'instances' && installedInstances.length === 0 && !isLoadingInstances) {
            loadInstances();
        }
    }, [activeTab]);

    const loadInstances = async () => {
        setIsLoadingInstances(true);
        try {
            const instances = await GetInstalledVersionsDetailed();
            console.log('[SettingsModal] Loaded instances:', JSON.stringify(instances, null, 2));
            setInstalledInstances(instances || []);
        } catch (err) {
            console.error('Failed to load instances:', err);
        }
        setIsLoadingInstances(false);
    };
    
    const handleExportInstance = async (instance: InstalledVersionInfo) => {
        const key = `${instance.branch}-${instance.version}`;
        setExportingInstance(key);
        setExportMessage(null);
        try {
            const exportPath = await ExportInstance(instance.branch, instance.version, instanceExportPath || undefined);
            if (exportPath) {
                setExportMessage({ type: 'success', text: `${t('settings.instanceSettings.exportedSuccess')}: ${exportPath.split('/').pop()}` });
                setTimeout(() => setExportMessage(null), 5000);
            } else {
                setExportMessage({ type: 'error', text: t('settings.instanceSettings.noUserData') });
                setTimeout(() => setExportMessage(null), 5000);
            }
        } catch (err) {
            console.error('Failed to export instance:', err);
            setExportMessage({ type: 'error', text: t('settings.instanceSettings.exportFailed') });
            setTimeout(() => setExportMessage(null), 5000);
        }
        setExportingInstance(null);
        setShowInstanceExportModal(null);
    };
    
    const handleBrowseInstanceExportFolder = async () => {
        try {
            const selectedPath = await BrowseFolder(instanceExportPath || '');
            if (selectedPath) {
                setInstanceExportPath(selectedPath);
            }
        } catch (err) {
            console.error('Failed to browse folder:', err);
        }
    };
    
    const handleDeleteInstance = async () => {
        if (!instanceToDelete) return;
        try {
            // Ensure version is a number
            const version = typeof instanceToDelete.version === 'number' ? instanceToDelete.version : parseInt(String(instanceToDelete.version)) || 0;
            await DeleteGame(instanceToDelete.branch, version);
            await loadInstances();
            // Notify parent to refresh installed versions in dropdown
            onInstanceDeleted?.();
        } catch (err) {
            console.error('Failed to delete instance:', err);
        }
        setInstanceToDelete(null);
    };
    
    const handleImportInstance = async () => {
        if (!showImportModal) return;
        
        setIsImportingInstance(true);
        try {
            const success = await ImportInstanceFromZip(importTargetBranch, importTargetVersion, showImportModal.zipBase64);
            if (success) {
                await loadInstances();
                console.log('Successfully imported instance');
            } else {
                console.error('Failed to import instance');
            }
        } catch (err) {
            console.error('Failed to import instance:', err);
        }
        setIsImportingInstance(false);
        setShowImportModal(null);
    };

    useEffect(() => {
        const handleClickOutside = (e: MouseEvent) => {
            if (languageDropdownRef.current && !languageDropdownRef.current.contains(e.target as Node)) {
                setIsLanguageOpen(false);
            }
            if (branchDropdownRef.current && !branchDropdownRef.current.contains(e.target as Node)) {
                setIsBranchOpen(false);
            }
        };

        const handleEscape = (e: KeyboardEvent) => {
            if (e.key === 'Escape') {
                if (showAllBackgrounds) {
                    setShowAllBackgrounds(false);
                } else {
                    onClose();
                }
            }
        };

        document.addEventListener('mousedown', handleClickOutside);
        document.addEventListener('keydown', handleEscape);
        return () => {
            document.removeEventListener('mousedown', handleClickOutside);
            document.removeEventListener('keydown', handleEscape);
        };
    }, [onClose, showAllBackgrounds]);

    const handleLanguageSelect = async (langCode: Language) => {
        setIsLanguageOpen(false);

        try {
            await changeLanguage(langCode);
            console.log(`Language set to: ${langCode}`);
        } catch (error) {
            console.warn('Failed to change language:', error);
        }
    };



    const handleLauncherBranchChange = async (branch: string) => {
        setSelectedLauncherBranch(branch);
        setIsBranchOpen(false);
        onLauncherBranchChange(branch);
    };

    const handleCloseAfterLaunchChange = async () => {
        const newValue = !closeAfterLaunch;
        setCloseAfterLaunch(newValue);
        await SetCloseAfterLaunch(newValue);
    };

    const handleSaveJavaArguments = async () => {
        const { sanitized, blocked } = sanitizeAdvancedJavaArguments(javaArguments);

        if (blocked) {
            setJavaArgumentsError(t('settings.javaSettings.jvmArgumentsBlocked'));
        } else {
            setJavaArgumentsError('');
        }

        setJavaArguments(sanitized);

        try {
            await ipc.settings.update({ javaArguments: sanitized });
            const parsedJavaRamMb = parseJavaHeapMb(sanitized, 'xmx');
            if (parsedJavaRamMb != null) {
                const clampedJavaRamMb = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, Math.round(parsedJavaRamMb / 256) * 256));
                setJavaRamMb(clampedJavaRamMb);

                const parsedJavaInitialRamMb = parseJavaHeapMb(sanitized, 'xms');
                if (parsedJavaInitialRamMb != null) {
                    const clampedInitial = Math.min(clampedJavaRamMb, Math.max(minJavaRamMb, Math.round(parsedJavaInitialRamMb / 256) * 256));
                    setJavaInitialRamMb(clampedInitial);
                }
            }

            setJavaGcMode(detectJavaGcMode(sanitized));
        } catch (err) {
            console.error('Failed to update Java arguments:', err);
        }
    };

    const handleJavaRamChange = async (value: number) => {
        const clampedJavaRamMb = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, value));
        setJavaRamMb(clampedJavaRamMb);

        const clampedInitial = Math.min(clampedJavaRamMb, javaInitialRamMb);
        if (clampedInitial !== javaInitialRamMb) {
            setJavaInitialRamMb(clampedInitial);
        }

        const withMaxHeap = upsertJavaHeapArgument(javaArguments, 'Xmx', clampedJavaRamMb);
        const updatedJavaArgs = upsertJavaHeapArgument(withMaxHeap, 'Xms', clampedInitial);
        setJavaArguments(updatedJavaArgs);

        try {
            await ipc.settings.update({ javaArguments: updatedJavaArgs });
        } catch (err) {
            console.error('Failed to update Java RAM arguments:', err);
        }
    };

    const handleJavaInitialRamChange = async (value: number) => {
        const clampedInitial = Math.min(javaRamMb, Math.max(minJavaRamMb, value));
        setJavaInitialRamMb(clampedInitial);

        const updatedJavaArgs = upsertJavaHeapArgument(javaArguments, 'Xms', clampedInitial);
        setJavaArguments(updatedJavaArgs);

        try {
            await ipc.settings.update({ javaArguments: updatedJavaArgs });
        } catch (err) {
            console.error('Failed to update Java initial RAM arguments:', err);
        }
    };

    const handleJavaGcModeChange = async (mode: 'auto' | 'g1') => {
        setJavaGcMode(mode);
        const updatedJavaArgs = upsertJavaGcMode(javaArguments, mode);
        setJavaArguments(updatedJavaArgs);

        try {
            await ipc.settings.update({ javaArguments: updatedJavaArgs });
        } catch (err) {
            console.error('Failed to update Java GC mode:', err);
        }
    };

    const handleJavaRuntimeModeChange = async (mode: 'bundled' | 'custom') => {
        setJavaRuntimeMode(mode);
        setJavaCustomPathError('');

        try {
            await ipc.settings.update({ useCustomJava: mode === 'custom' });
        } catch (err) {
            console.error('Failed to update Java runtime mode:', err);
        }
    };

    const handleCustomJavaPathSave = async () => {
        const normalizedPath = customJavaPath.trim();
        setCustomJavaPath(normalizedPath);

        if (!normalizedPath) {
            setJavaCustomPathError(t('settings.javaSettings.customJavaPathRequired'));
            return;
        }

        const exists = await FileExists(normalizedPath);
        if (!exists) {
            setJavaCustomPathError(t('settings.javaSettings.customJavaPathNotFound'));
            return;
        }

        setJavaCustomPathError('');

        try {
            await ipc.settings.update({ customJavaPath: normalizedPath, useCustomJava: true });
            setJavaRuntimeMode('custom');
        } catch (err) {
            console.error('Failed to save custom Java path:', err);
        }
    };

    const handleBrowseCustomJavaPath = async () => {
        try {
            const picked = await BrowseJavaExecutable();
            if (!picked) return;
            setCustomJavaPath(picked);
            setJavaCustomPathError('');
        } catch (err) {
            console.error('Failed to browse Java executable:', err);
        }
    };

    const handleOpenLauncherFolder = async () => {
        try {
            const path = launcherFolderPath || await GetLauncherFolderPath();
            if (!path) return;
            BrowserOpenURL(`file://${encodeURI(path)}`);
        } catch (err) {
            console.error('Failed to open launcher folder:', err);
            if (launcherFolderPath) {
                BrowserOpenURL(`file://${encodeURI(launcherFolderPath)}`);
            }
        }
    };

    const handleDeleteLauncherData = async () => {
        const success = await DeleteLauncherData();
        if (success) {
            setShowDeleteConfirm(false);
            onClose();
        }
    };

    const handleBrowseInstanceDir = async () => {
        try {
            const selectedPath = await BrowseFolder(instanceDir || launcherFolderPath);
            if (selectedPath) {
                console.log('[SettingsModal] Setting instance directory to:', selectedPath);
                setIsMovingData(true);
                setMoveProgress(0);
                setMoveCurrentFile('');
                
                const result = await SetInstanceDirectory(selectedPath);
                if (result.success) {
                    setInstanceDir(result.path || selectedPath);
                    console.log('[SettingsModal] Instance directory updated successfully');
                } else {
                    console.error('[SettingsModal] Failed to set instance directory');
                    setIsMovingData(false);
                }
            }
        } catch (err) {
            console.error('Failed to set instance directory:', err);
            setIsMovingData(false);
        }
    };

    const handleBackgroundModeChange = async (mode: string) => {
        setBackgroundModeState(mode);
        try {
            await SetBackgroundMode(mode);
            onBackgroundModeChange?.(mode);
        } catch (err) {
            console.error('Failed to set background mode:', err);
        }
    };


    const handleResetInstanceDir = async () => {
        try {
            const defaultDir = await GetDefaultInstanceDir();
            console.log('[SettingsModal] Resetting instance directory to default (empty config path)');
            setIsMovingData(true);
            setMoveProgress(0);
            setMoveCurrentFile('');

            const result = await SetInstanceDirectory('');
            if (result.success) {
                setInstanceDir(result.path || defaultDir);
            } else {
                setIsMovingData(false);
            }
        } catch (err) {
            console.error('Failed to reset instance directory:', err);
            setIsMovingData(false);
        }
    };

    const getInstanceLabel = (instance: InstalledVersionInfo) => {
        const branchLabel = instance.branch === 'release' ? t('common.release') : t('common.preRelease');
        const versionLabel = instance.version === 0 ? t('common.latest') : `v${instance.version}`;
        return `${branchLabel} ${versionLabel}`;
    };


    const handleGpuPreferenceChange = async (preference: string) => {
        setGpuPreferenceState(preference);
        try {
            await ipc.settings.update({ gpuPreference: preference });
        } catch (err) {
            console.error('Failed to update GPU preference:', err);
        }
    };

    // Environment variable preset definitions
    const ENV_PRESETS = {
        forceX11: 'SDL_VIDEODRIVER=x11',
        disableVkLayers: 'VK_LOADER_LAYERS_DISABLE=all',
        forceAmdVulkan: 'VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/radeon_icd.x86_64.json',
    };

    // Helper to add/remove preset from env vars string
    const toggleEnvPreset = (currentVars: string, preset: string, enabled: boolean): string => {
        const vars = currentVars.split(/\s+/).filter(v => v.trim());
        const withoutPreset = vars.filter(v => !v.startsWith(preset.split('=')[0] + '='));
        
        if (enabled) {
            withoutPreset.push(preset);
        }
        
        return withoutPreset.join(' ');
    };

    const handleEnvPresetToggle = async (presetKey: keyof typeof ENV_PRESETS, enabled: boolean) => {
        const preset = ENV_PRESETS[presetKey];
        const newEnvVars = toggleEnvPreset(gameEnvVars, preset, enabled);
        setGameEnvVars(newEnvVars);
        
        // Update state
        if (presetKey === 'forceX11') setEnvForceX11(enabled);
        if (presetKey === 'disableVkLayers') setEnvDisableVkLayers(enabled);
        if (presetKey === 'forceAmdVulkan') setEnvForceAmdVulkan(enabled);
        
        // Auto-save
        try {
            await ipc.settings.update({ gameEnvironmentVariables: newEnvVars });
        } catch (err) {
            console.error('Failed to save environment variables:', err);
        }
    };

    const validateEnvVars = (value: string): { valid: boolean; error: string } => {
        if (!value.trim()) return { valid: true, error: '' };
        
        // Now space-separated format: KEY=VALUE KEY2=VALUE2
        const parts = value.split(/\s+/).filter(v => v.trim());
        const envVarPattern = /^[A-Za-z_][A-Za-z0-9_]*=.*/;
        
        for (const part of parts) {
            if (!envVarPattern.test(part.trim())) {
                return { valid: false, error: t('settings.variablesSettings.envVarsInvalidFormat') };
            }
        }
        return { valid: true, error: '' };
    };

    const handleSaveGameEnvVars = async () => {
        const { valid, error } = validateEnvVars(gameEnvVars);
        if (!valid) {
            setGameEnvVarsError(error);
            return;
        }
        setGameEnvVarsError('');
        
        // Update preset states based on new value
        setEnvForceX11(gameEnvVars.includes('SDL_VIDEODRIVER=x11'));
        setEnvDisableVkLayers(gameEnvVars.includes('VK_LOADER_LAYERS_DISABLE=all'));
        setEnvForceAmdVulkan(gameEnvVars.includes('VK_ICD_FILENAMES=') && gameEnvVars.includes('radeon'));
        
        try {
            await ipc.settings.update({ gameEnvironmentVariables: gameEnvVars });
        } catch (err) {
            console.error('Failed to save game environment variables:', err);
        }
    };

    const handleAccentColorChange = async (color: string) => {
        // Update the global context (which also saves to backend)
        await setAccentColorContext(color);
        onAccentColorChange?.(color);
    };

    const handleDevModeToggle = () => {
        const newValue = !devModeEnabled;
        setDevModeEnabled(newValue);
        localStorage.setItem('hyprism_dev_mode', newValue ? 'true' : 'false');
    };

    const openGitHub = () => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism');
    const openBugReport = () => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism/issues/new');
    const openDiscord = async () => {
        const link = await GetDiscordLink();
        BrowserOpenURL(link);
    };

    const currentLangConfig = LANGUAGE_CONFIG[i18n.language as Language] || LANGUAGE_CONFIG[Language.ENGLISH];

    const tabs = [
        { id: 'general' as const, icon: Settings, label: t('settings.general') },
        { id: 'java' as const, icon: Coffee, label: t('settings.java') },
        { id: 'visual' as const, icon: Image, label: t('settings.visual') },
        { id: 'network' as const, icon: Wifi, label: t('settings.network') },
        { id: 'graphics' as const, icon: Monitor, label: t('settings.graphics') },
        ...(isLinux ? [{ id: 'variables' as const, icon: Terminal, label: t('settings.variables') }] : []),
        { id: 'logs' as const, icon: FileText, label: t('logs.title') },
        { id: 'data' as const, icon: Database, label: t('settings.data') },
        { id: 'about' as const, icon: Globe, label: t('settings.about') },
        ...(devModeEnabled ? [{ id: 'developer' as const, icon: Code, label: t('settings.developer') }] : []),
    ];

    // Profile helpers
    const generateUUID = () => {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
            const r = (Math.random() * 16) | 0;
            const v = c === 'x' ? r : (r & 0x3) | 0x8;
            return v.toString(16);
        });
    };

    const truncateName = (name: string, maxLength: number = 10) => {
        if (name.length <= maxLength) return name;
        return name.slice(0, maxLength - 2) + '...';
    };

    // Get the maintainer (yyyumeniku)
    const maintainer = contributors.find(c => c.login.toLowerCase() === 'yyyumeniku');
    const otherContributors = contributors.filter(c => !['yyyumeniku', 'freakdaniel'].includes(c.login.toLowerCase()));

    return (
        <>
            <div className={isPageMode
                ? "w-full h-full flex gap-4"
                : `fixed inset-0 z-[200] flex items-center justify-center bg-[#0a0a0a]/90`
            }>
                {/* In modal mode, constrain width/height; in page mode, 'contents' makes this invisible to layout */}
                <div className={isPageMode
                    ? "contents"
                    : "w-full max-w-4xl mx-4 max-h-[85vh] flex gap-2 relative"
                }>
                    {/* Sidebar - Independent glass panel */}
                    <div className={`${isPageMode ? 'w-52' : 'w-48'} flex-shrink-0 flex flex-col py-4 rounded-2xl glass-panel-static-solid`}>
                        <nav className="flex-1 space-y-1 px-2">
                            {tabs.map((tab) => (
                                <button
                                    key={tab.id}
                                    onClick={() => setActiveTab(tab.id)}
                                    className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors ${
                                        activeTab === tab.id
                                            ? 'bg-white/10 text-white'
                                            : 'text-white/60 hover:text-white hover:bg-white/5'
                                    }`}
                                    style={activeTab === tab.id ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                                >
                                    <tab.icon size={18} />
                                    <span>{tab.label}</span>
                                </button>
                            ))}
                        </nav>
                        {/* Dev Mode Toggle at bottom */}
                        <div className="px-2 pt-4 border-t border-white/[0.06] mx-2">
                            <div 
                                className="flex items-center justify-between px-3 py-2 rounded-lg cursor-pointer hover:bg-white/5 transition-colors"
                                onClick={handleDevModeToggle}
                            >
                                <div className="flex items-center gap-2 text-white/40">
                                    <Code size={14} />
                                    <span className="text-xs">{t('settings.devMode')}</span>
                                </div>
                                <div 
                                    className={`w-8 h-4 rounded-full flex items-center transition-colors`}
                                    style={{ backgroundColor: devModeEnabled ? accentColor : 'rgba(255,255,255,0.2)' }}
                                >
                                    <div 
                                        className={`w-3 h-3 rounded-full shadow-md transform transition-transform ${devModeEnabled ? 'translate-x-4' : 'translate-x-0.5'}`} 
                                        style={{ backgroundColor: devModeEnabled ? accentTextColor : 'white' }}
                                    />
                                </div>
                            </div>
                        </div>
                    </div>

                    {/* Content - Independent glass panel */}
                    <div className={`flex-1 flex flex-col min-w-0 overflow-hidden rounded-2xl glass-panel-static-solid`}>
                        {/* Header */}
                        {activeTab !== 'logs' && (
                            <div className="flex items-center justify-between p-4 border-b border-white/[0.06]">
                                <h3 className="text-white font-medium">{tabs.find(t => t.id === activeTab)?.label}</h3>
                                {!isPageMode && onClose && (
                                    <button onClick={onClose} className="p-2 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/10 transition-colors">
                                        <X size={18} />
                                    </button>
                                )}
                            </div>
                        )}

                        {/* Scrollable Content */}
                        <div className={activeTab === 'logs' ? 'flex-1 min-h-0' : 'flex-1 overflow-y-auto p-6 space-y-6'}>
                            {/* Rosetta Warning (shown on all tabs) */}
                            {rosettaWarning && activeTab !== 'logs' && (
                                <div className="p-4 bg-yellow-500/10 border border-yellow-500/30 rounded-xl">
                                    <div className="flex items-start gap-3">
                                        <AlertTriangle size={20} className="text-yellow-500 flex-shrink-0 mt-0.5" />
                                        <div className="flex-1">
                                            <p className="text-yellow-500 text-sm font-medium mb-2">{rosettaWarning.message}</p>
                                            <div className="flex flex-col gap-2">
                                                <code className="text-xs text-white/70 bg-[#1c1c1e] px-2 py-1 rounded font-mono break-all">
                                                    {rosettaWarning.command}
                                                </code>
                                                {rosettaWarning.tutorialUrl && (
                                                    <button
                                                        onClick={() => BrowserOpenURL(rosettaWarning.tutorialUrl!)}
                                                        className="flex items-center gap-1 text-xs hover:opacity-80 transition-colors w-fit"
                                                        style={{ color: accentColor }}
                                                    >
                                                        <ExternalLink size={12} />
                                                        {t('settings.generalSettings.watchTutorial')}
                                                    </button>
                                                )}
                                            </div>
                                        </div>
                                    </div>
                                </div>
                            )}

                            {/* General Tab */}
                            {activeTab === 'general' && (
                                <div className="space-y-6">
                                    {/* Language Selector */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.language')}</label>
                                        <div ref={languageDropdownRef} className="relative">
                                            <button
                                                onClick={() => {
                                                    setIsLanguageOpen(!isLanguageOpen);
                                                    setIsBranchOpen(false);
                                                }}
                                                className={`w-full h-12 px-4 rounded-xl ${gc} flex items-center justify-between text-white transition-colors hover:border-white/[0.12]`}
                                                style={{ borderColor: isLanguageOpen ? `${accentColor}50` : undefined }}
                                            >
                                                <div className="flex items-center gap-2">
                                                    <span className="font-medium">{currentLangConfig.nativeName}</span>
                                                    <span className="text-white/50 text-sm">({currentLangConfig.name})</span>
                                                </div>
                                                <ChevronDown size={16} className={`text-white/40 transition-transform ${isLanguageOpen ? 'rotate-180' : ''}`} />
                                            </button>

                                            {isLanguageOpen && (
                                                <div className={`absolute top-full left-0 right-0 mt-2 z-10 max-h-60 overflow-y-auto ${gc} rounded-xl shadow-xl shadow-black/50`}>
                                                    {Object.values(LANGUAGE_CONFIG).map((lang) => (
                                                        <button
                                                            key={lang.code}
                                                            onClick={() => handleLanguageSelect(lang.code)}
                                                            className={`w-full px-4 py-3 flex items-center gap-2 text-sm ${i18n.language === lang.code
                                                                ? 'text-white'
                                                                : 'text-white/70 hover:bg-white/10 hover:text-white'
                                                                }`}
                                                            style={i18n.language === lang.code ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                                                        >
                                                            {i18n.language === lang.code && <Check size={14} style={{ color: accentColor }} strokeWidth={3} />}
                                                            <div className={`flex flex-col items-start ${i18n.language === lang.code ? '' : 'ml-[22px]'}`}>
                                                                <span className="font-medium">{lang.nativeName}</span>
                                                                <span className="text-xs opacity-50">{lang.name}</span>
                                                            </div>
                                                        </button>
                                                    ))}
                                                </div>
                                            )}
                                        </div>
                                    </div>

                                    {/* Launcher Branch Selector */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.generalSettings.updateChannel')}</label>
                                        <div ref={branchDropdownRef} className="relative">
                                            <button
                                                onClick={() => {
                                                    setIsBranchOpen(!isBranchOpen);
                                                    setIsLanguageOpen(false);
                                                }}
                                                className={`w-full h-12 px-4 rounded-xl ${gc} flex items-center justify-between text-white transition-colors hover:border-white/[0.12]`}
                                                style={{ borderColor: isBranchOpen ? `${accentColor}50` : undefined }}
                                            >
                                                <div className="flex items-center gap-2">
                                                    <span className="font-medium">
                                                        {selectedLauncherBranch === 'beta' ? t('settings.generalSettings.updateChannelBeta') : t('settings.generalSettings.updateChannelStable')}
                                                    </span>
                                                    {selectedLauncherBranch === 'beta' && (
                                                        <span className="text-xs px-2 py-0.5 rounded bg-yellow-500/20 text-yellow-500">
                                                            {t('settings.generalSettings.updateChannelExperimental')}
                                                        </span>
                                                    )}
                                                </div>
                                                <ChevronDown size={16} className={`text-white/40 transition-transform ${isBranchOpen ? 'rotate-180' : ''}`} />
                                            </button>

                                            {isBranchOpen && (
                                                <div className={`absolute top-full left-0 right-0 mt-2 z-10 ${gc} rounded-xl shadow-xl shadow-black/50 overflow-hidden`}>
                                                    <button
                                                        onClick={() => handleLauncherBranchChange('release')}
                                                        className={`w-full px-4 py-3 flex items-center gap-2 text-sm ${selectedLauncherBranch === 'release'
                                                            ? 'text-white'
                                                            : 'text-white/70 hover:bg-white/10 hover:text-white'
                                                            }`}
                                                        style={selectedLauncherBranch === 'release' ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                                                    >
                                                        {selectedLauncherBranch === 'release' && <Check size={14} style={{ color: accentColor }} strokeWidth={3} />}
                                                        <div className={`flex flex-col items-start ${selectedLauncherBranch === 'release' ? '' : 'ml-[22px]'}`}>
                                                            <span className="font-medium">{t('settings.generalSettings.updateChannelStable')}</span>
                                                            <span className="text-xs opacity-50">{t('settings.generalSettings.updateChannelStableHint')}</span>
                                                        </div>
                                                    </button>
                                                    <button
                                                        onClick={() => handleLauncherBranchChange('beta')}
                                                        className={`w-full px-4 py-3 flex items-center gap-2 text-sm ${selectedLauncherBranch === 'beta'
                                                            ? 'text-white'
                                                            : 'text-white/70 hover:bg-white/10 hover:text-white'
                                                            }`}
                                                        style={selectedLauncherBranch === 'beta' ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                                                    >
                                                        {selectedLauncherBranch === 'beta' && <Check size={14} style={{ color: accentColor }} strokeWidth={3} />}
                                                        <div className={`flex flex-col items-start ${selectedLauncherBranch === 'beta' ? '' : 'ml-[22px]'}`}>
                                                            <span className="font-medium">{t('settings.generalSettings.updateChannelBeta')}</span>
                                                            <span className="text-xs opacity-50">{t('settings.generalSettings.updateChannelBetaHint')}</span>
                                                        </div>
                                                    </button>
                                                </div>
                                            )}
                                        </div>
                                        <p className="mt-2 text-xs text-white/40">
                                            {selectedLauncherBranch === 'beta' 
                                                ? t('settings.generalSettings.updateChannelBetaWarning')
                                                : t('settings.generalSettings.updateChannelHint')}
                                        </p>
                                    </div>

                                    {/* Toggle Settings - macOS Tahoe style */}
                                    <div className="space-y-3">
                                        {/* Close After Launch */}
                                        <div 
                                            className={`flex items-center justify-between p-4 rounded-2xl ${gc} cursor-pointer hover:border-white/[0.12] transition-all`}
                                            onClick={handleCloseAfterLaunchChange}
                                        >
                                            <div className="flex items-center gap-3">
                                                <div className="w-8 h-8 rounded-lg bg-white/[0.06] flex items-center justify-center">
                                                    <Power size={16} className="text-white/70" />
                                                </div>
                                                <div>
                                                    <span className="text-white text-sm font-medium">{t('settings.generalSettings.closeLauncher')}</span>
                                                    <p className="text-xs text-white/40">{t('settings.generalSettings.closeLauncherHint')}</p>
                                                </div>
                                            </div>
                                            <div 
                                                className="w-12 h-7 rounded-full flex items-center transition-all duration-200"
                                                style={{ backgroundColor: closeAfterLaunch ? accentColor : 'rgba(255,255,255,0.15)' }}
                                            >
                                                <div 
                                                    className={`w-5 h-5 rounded-full shadow-md transform transition-all duration-200 ${closeAfterLaunch ? 'translate-x-6' : 'translate-x-1'}`}
                                                    style={{ backgroundColor: closeAfterLaunch ? accentTextColor : 'white' }}
                                                />
                                            </div>
                                        </div>

                                    </div>
                                </div>
                            )}

                            {/* Java Tab */}
                            {activeTab === 'java' && (
                                <div className="space-y-6">
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.javaSettings.javaRuntime')}</label>
                                        <p className="text-xs text-white/40 mb-4">{t('settings.javaSettings.javaRuntimeHint')}</p>

                                        <div className="space-y-2">
                                            <div
                                                className={`flex items-center gap-3 p-4 rounded-xl border cursor-pointer transition-all ${
                                                    javaRuntimeMode === 'bundled'
                                                        ? 'border-white/20'
                                                        : 'border-white/[0.06] hover:border-white/[0.12] bg-[#1c1c1e]'
                                                }`}
                                                style={javaRuntimeMode === 'bundled' ? { backgroundColor: `${accentColor}15`, borderColor: `${accentColor}50` } : undefined}
                                                onClick={() => handleJavaRuntimeModeChange('bundled')}
                                            >
                                                <div className="w-8 h-8 rounded-lg flex items-center justify-center" style={{ backgroundColor: javaRuntimeMode === 'bundled' ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}>
                                                    <Coffee size={16} className={javaRuntimeMode === 'bundled' ? '' : 'text-white/70'} style={javaRuntimeMode === 'bundled' ? { color: accentColor } : undefined} />
                                                </div>
                                                <div className="flex-1">
                                                    <span className="text-white text-sm font-medium">{t('settings.javaSettings.useBundledJava')}</span>
                                                    <p className="text-xs text-white/40">{t('settings.javaSettings.useBundledJavaHint')}</p>
                                                </div>
                                                <div className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-all ${javaRuntimeMode === 'bundled' ? '' : 'border-white/30'}`} style={javaRuntimeMode === 'bundled' ? { borderColor: accentColor, backgroundColor: accentColor } : undefined}>
                                                    {javaRuntimeMode === 'bundled' && <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />}
                                                </div>
                                            </div>

                                            <div
                                                className={`rounded-xl border transition-all ${
                                                    javaRuntimeMode === 'custom'
                                                        ? 'border-white/20'
                                                        : 'border-white/[0.06] hover:border-white/[0.12] bg-[#1c1c1e]'
                                                }`}
                                                style={javaRuntimeMode === 'custom' ? { backgroundColor: `${accentColor}15`, borderColor: `${accentColor}50` } : undefined}
                                            >
                                                <div className="flex items-center gap-3 p-4 cursor-pointer" onClick={() => handleJavaRuntimeModeChange('custom')}>
                                                    <div className="w-8 h-8 rounded-lg flex items-center justify-center" style={{ backgroundColor: javaRuntimeMode === 'custom' ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}>
                                                        <Coffee size={16} className={javaRuntimeMode === 'custom' ? '' : 'text-white/70'} style={javaRuntimeMode === 'custom' ? { color: accentColor } : undefined} />
                                                    </div>
                                                    <div className="flex-1">
                                                        <span className="text-white text-sm font-medium">{t('settings.javaSettings.useCustomJava')}</span>
                                                        <p className="text-xs text-white/40">{t('settings.javaSettings.useCustomJavaHint')}</p>
                                                    </div>
                                                    <div className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-all ${javaRuntimeMode === 'custom' ? '' : 'border-white/30'}`} style={javaRuntimeMode === 'custom' ? { borderColor: accentColor, backgroundColor: accentColor } : undefined}>
                                                        {javaRuntimeMode === 'custom' && <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />}
                                                    </div>
                                                </div>

                                                {javaRuntimeMode === 'custom' && (
                                                    <div className="px-4 pb-4">
                                                        <div className="flex gap-2">
                                                            <input
                                                                type="text"
                                                                value={customJavaPath}
                                                                onChange={(event) => setCustomJavaPath(event.target.value)}
                                                                onKeyDown={async (event) => {
                                                                    if (event.key === 'Enter' && customJavaPath.trim()) {
                                                                        await handleCustomJavaPathSave();
                                                                    }
                                                                }}
                                                                placeholder={t('settings.javaSettings.customJavaPathPlaceholder')}
                                                                className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm placeholder-white/30 focus:outline-none`}
                                                            />
                                                            <div className={`flex rounded-full overflow-hidden ${gc}`}>
                                                                <button
                                                                    onClick={handleBrowseCustomJavaPath}
                                                                    className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                                                                    title={t('common.select')}
                                                                >
                                                                    <FolderOpen size={18} />
                                                                    <span className="ml-2 text-sm">{t('common.select')}</span>
                                                                </button>
                                                                <div className="w-px bg-white/10" />
                                                                <button
                                                                    onClick={handleCustomJavaPathSave}
                                                                    disabled={!customJavaPath.trim()}
                                                                    className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-white/60"
                                                                    title={t('common.save')}
                                                                >
                                                                    <Check size={18} />
                                                                    <span className="ml-2 text-sm">{t('common.save')}</span>
                                                                </button>
                                                            </div>
                                                        </div>
                                                        {javaCustomPathError && (
                                                            <p className="mt-2 text-xs text-red-300">{javaCustomPathError}</p>
                                                        )}
                                                    </div>
                                                )}
                                            </div>
                                        </div>
                                    </div>

                                    <div className={`p-4 rounded-2xl ${gc}`}>
                                        <div className="flex items-center gap-3 mb-3">
                                            <div className="w-8 h-8 rounded-lg bg-white/[0.06] flex items-center justify-center">
                                                <HardDrive size={16} className="text-white/70" />
                                            </div>
                                            <div>
                                                <span className="text-white text-sm font-medium">{t('settings.javaSettings.ramAllocation')}</span>
                                                <p className="text-xs text-white/40">{t('settings.javaSettings.ramAllocationHint')}</p>
                                            </div>
                                        </div>

                                        <div className="flex items-center justify-between mb-2 text-xs text-white/60">
                                            <span>{t('settings.javaSettings.detectedMemory', { value: formatRamLabel(detectedSystemRamMb) })}</span>
                                            <span>{t('settings.javaSettings.maxRecommended', { value: formatRamLabel(maxJavaRamMb) })}</span>
                                        </div>

                                        <div className="grid grid-cols-2 gap-4">
                                            <div className={`p-3 rounded-xl ${gc}`}>
                                                <div className="flex items-center justify-between mb-2 text-xs text-white/50">
                                                    <span>{t('settings.javaSettings.maxRam')}</span>
                                                    <span>{formatRamLabel(javaRamMb)}</span>
                                                </div>
                                                <input
                                                    type="range"
                                                    min={minJavaRamMb}
                                                    max={maxJavaRamMb}
                                                    step={256}
                                                    value={javaRamMb}
                                                    onChange={(event) => handleJavaRamChange(Number.parseInt(event.target.value, 10) || minJavaRamMb)}
                                                    className="java-range w-full accent-current appearance-none border-0 outline-none focus:outline-none focus:ring-0"
                                                    style={{ color: accentColor }}
                                                />
                                            </div>

                                            <div className={`p-3 rounded-xl ${gc}`}>
                                                <div className="flex items-center justify-between mb-2 text-xs text-white/50">
                                                    <span>{t('settings.javaSettings.initialRam')}</span>
                                                    <span>{formatRamLabel(javaInitialRamMb)}</span>
                                                </div>
                                                <input
                                                    type="range"
                                                    min={minJavaRamMb}
                                                    max={javaRamMb}
                                                    step={256}
                                                    value={javaInitialRamMb}
                                                    onChange={(event) => handleJavaInitialRamChange(Number.parseInt(event.target.value, 10) || minJavaRamMb)}
                                                    className="java-range w-full accent-current appearance-none border-0 outline-none focus:outline-none focus:ring-0"
                                                    style={{ color: accentColor }}
                                                />
                                            </div>
                                        </div>
                                    </div>

                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.javaSettings.gcMode')}</label>
                                        <p className="text-xs text-white/40 mb-3">{t('settings.javaSettings.gcModeHint')}</p>
                                        <div className="space-y-2">
                                            {(['auto', 'g1'] as const).map((mode) => (
                                                <button
                                                    key={mode}
                                                    onClick={() => handleJavaGcModeChange(mode)}
                                                    className="w-full p-3 rounded-xl border transition-all text-left"
                                                    style={{
                                                        backgroundColor: javaGcMode === mode ? `${accentColor}15` : '#151515',
                                                        borderColor: javaGcMode === mode ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
                                                    }}
                                                >
                                                    <div className="flex items-center justify-between">
                                                        <div>
                                                            <div className="text-white text-sm font-medium">{t(`settings.javaSettings.gc_${mode}`)}</div>
                                                            <div className="text-[11px] text-white/35 mt-0.5">{t(`settings.javaSettings.gc_${mode}Hint`)}</div>
                                                        </div>
                                                        {javaGcMode === mode && (
                                                            <div className="w-5 h-5 rounded-full flex items-center justify-center" style={{ backgroundColor: accentColor }}>
                                                                <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />
                                                            </div>
                                                        )}
                                                    </div>
                                                </button>
                                            ))}
                                        </div>
                                    </div>

                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.javaSettings.jvmArguments')}</label>
                                        <div className="flex gap-2">
                                            <input
                                                type="text"
                                                value={javaArguments}
                                                onChange={(event) => {
                                                    setJavaArguments(event.target.value);
                                                    if (javaArgumentsError) setJavaArgumentsError('');
                                                }}
                                                placeholder={t('settings.javaSettings.jvmArgumentsPlaceholder')}
                                                className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm placeholder-white/35 focus:outline-none`}
                                            />
                                            <div className={`flex rounded-full overflow-hidden ${gc}`}>
                                                <button
                                                    onClick={handleSaveJavaArguments}
                                                    className="h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                                                    title={t('common.save')}
                                                >
                                                    <Check size={18} />
                                                    <span className="ml-2 text-sm">{t('common.save')}</span>
                                                </button>
                                            </div>
                                        </div>
                                        <p className="mt-2 text-xs text-white/40">{t('settings.javaSettings.jvmArgumentsHint')}</p>
                                        {javaArgumentsError && (
                                            <p className="mt-2 text-xs text-yellow-300">{javaArgumentsError}</p>
                                        )}
                                    </div>
                                </div>
                            )}

                            {/* Visual Tab */}
                            {activeTab === 'visual' && (
                                <div className="space-y-6">
                                    {/* Accent Color Chooser */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-3">{t('settings.visualSettings.accentColor')}</label>
                                        <div className="flex flex-wrap gap-2">
                                            {ACCENT_COLORS.map((color) => (
                                                <button
                                                    key={color}
                                                    onClick={() => handleAccentColorChange(color)}
                                                    className={`w-8 h-8 rounded-full transition-all ${accentColor === color ? 'ring-2 ring-white ring-offset-2 ring-offset-[#1a1a1a]' : 'hover:scale-110'}`}
                                                    style={{ backgroundColor: color }}
                                                />
                                            ))}
                                        </div>
                                    </div>

                                    {/* Background Chooser */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-3">{t('settings.visualSettings.background')}</label>
                                        
                                        {/* Slideshow option */}
                                        <div 
                                            className={`p-3 rounded-xl border cursor-pointer transition-colors mb-3`}
                                            style={{
                                                backgroundColor: backgroundMode === 'slideshow' ? `${accentColor}20` : '#151515',
                                                borderColor: backgroundMode === 'slideshow' ? `${accentColor}50` : 'rgba(255,255,255,0.1)'
                                            }}
                                            onClick={() => handleBackgroundModeChange('slideshow')}
                                        >
                                            <div className="flex items-center gap-3">
                                                <div 
                                                    className="w-5 h-5 rounded-full border-2 flex items-center justify-center"
                                                    style={{ borderColor: backgroundMode === 'slideshow' ? accentColor : 'rgba(255,255,255,0.3)' }}
                                                >
                                                    {backgroundMode === 'slideshow' && <div className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: accentColor }} />}
                                                </div>
                                                <div>
                                                    <span className="text-white text-sm font-medium">{t('settings.visualSettings.slideshow')}</span>
                                                    <p className="text-xs text-white/40">{t('settings.visualSettings.slideshowHint')}</p>
                                                </div>
                                            </div>
                                        </div>

                                        {/* Background grid - show all backgrounds in scrollable container */}
                                        <p className="text-xs text-white/40 mb-2">{t('settings.visualSettings.staticBackground')}</p>
                                        <div className="max-h-[280px] overflow-y-auto rounded-xl">
                                            <div className="grid grid-cols-4 gap-2 pr-1">
                                                {backgroundImages.map((bg) => (
                                                    <div
                                                        key={bg.name}
                                                        className="relative aspect-video rounded-lg overflow-hidden cursor-pointer border-2 transition-all"
                                                        style={{
                                                            borderColor: backgroundMode === bg.name ? accentColor : 'transparent',
                                                            boxShadow: backgroundMode === bg.name ? `0 0 0 2px ${accentColor}30` : 'none'
                                                        }}
                                                        onClick={() => handleBackgroundModeChange(bg.name)}
                                                    >
                                                        <img 
                                                            src={bg.url} 
                                                            alt={bg.name}
                                                            className="w-full h-full object-cover"
                                                        />
                                                        {backgroundMode === bg.name && (
                                                            <div className="absolute inset-0 flex items-center justify-center" style={{ backgroundColor: `${accentColor}30` }}>
                                                                <Check size={20} className="text-white drop-shadow-lg" />
                                                            </div>
                                                        )}
                                                    </div>
                                                ))}
                                            </div>
                                        </div>

                                    </div>


                                </div>
                            )}

                            {/* Network Tab */}
                            {activeTab === 'network' && (
                                <div className="space-y-6">
                                    {/* Online Mode Toggle */}
                                    <div className="space-y-3">
                                        <div 
                                            className={`flex items-center justify-between p-4 rounded-2xl ${gc} cursor-pointer hover:border-white/[0.12] transition-all`}
                                            onClick={async () => {
                                                const newValue = !onlineMode;
                                                setOnlineMode(newValue);
                                                await ipc.settings.update({ onlineMode: newValue });
                                                onAuthSettingsChange?.();
                                            }}
                                        >
                                            <div className="flex items-center gap-3">
                                                <div className="w-8 h-8 rounded-lg bg-white/[0.06] flex items-center justify-center">
                                                    <Wifi size={16} className="text-white/70" />
                                                </div>
                                                <div>
                                                    <span className="text-white text-sm font-medium">{t('settings.networkSettings.onlineMode')}</span>
                                                    <p className="text-xs text-white/40">{t('settings.networkSettings.onlineModeHint')}</p>
                                                </div>
                                            </div>
                                            <div 
                                                className="w-12 h-7 rounded-full flex items-center transition-all duration-200"
                                                style={{ backgroundColor: onlineMode ? accentColor : 'rgba(255,255,255,0.15)' }}
                                            >
                                                <div 
                                                    className={`w-5 h-5 rounded-full shadow-md transform transition-all duration-200 ${onlineMode ? 'translate-x-6' : 'translate-x-1'}`}
                                                    style={{ backgroundColor: onlineMode ? accentTextColor : 'white' }}
                                                />
                                            </div>
                                        </div>
                                    </div>

                                    {/* Auth Server Selector */}
                                    {onlineMode && (
                                        <div>
                                            <label className="block text-sm text-white/60 mb-2">{t('settings.networkSettings.authServer')}</label>
                                            <p className="text-xs text-white/40 mb-4">{t('settings.networkSettings.authServerHint')}</p>
                                            
                                            <div className="space-y-2">
                                                {/* Default (sessions.sanasol.ws) */}
                                                <div
                                                    className={`flex items-center gap-3 p-4 rounded-xl border cursor-pointer transition-all ${
                                                        authMode === 'default' 
                                                            ? 'border-white/20' 
                                                            : 'border-white/[0.06] hover:border-white/[0.12] bg-[#1c1c1e]'
                                                    }`}
                                                    style={authMode === 'default' ? { backgroundColor: `${accentColor}15`, borderColor: `${accentColor}50` } : undefined}
                                                    onClick={async () => {
                                                        setAuthModeState('default');
                                                        setAuthDomain('sessions.sanasol.ws');
                                                        await ipc.settings.update({ authDomain: 'sessions.sanasol.ws' });
                                                        onAuthSettingsChange?.();
                                                    }}
                                                >
                                                    <div className="w-8 h-8 rounded-lg flex items-center justify-center" style={{ backgroundColor: authMode === 'default' ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}>
                                                        <Server size={16} className={authMode === 'default' ? '' : 'text-white/70'} style={authMode === 'default' ? { color: accentColor } : undefined} />
                                                    </div>
                                                    <div className="flex-1">
                                                        <span className="text-white text-sm font-medium">{t('settings.networkSettings.authDefault')}</span>
                                                        <p className="text-xs text-white/40">sessions.sanasol.ws</p>
                                                    </div>
                                                    <div className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-all ${authMode === 'default' ? '' : 'border-white/30'}`} style={authMode === 'default' ? { borderColor: accentColor, backgroundColor: accentColor } : undefined}>
                                                        {authMode === 'default' && <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />}
                                                    </div>
                                                </div>

                                                {/* Official (Mojang) */}
                                                <div
                                                    className={`flex items-center gap-3 p-4 rounded-xl border cursor-pointer transition-all ${
                                                        authMode === 'official' 
                                                            ? 'border-white/20' 
                                                            : 'border-white/[0.06] hover:border-white/[0.12] bg-[#1c1c1e]'
                                                    }`}
                                                    style={authMode === 'official' ? { backgroundColor: `${accentColor}15`, borderColor: `${accentColor}50` } : undefined}
                                                    onClick={async () => {
                                                        setAuthModeState('official');
                                                        setAuthDomain('sessionserver.hytale.com');
                                                        await ipc.settings.update({ authDomain: 'sessionserver.hytale.com' });
                                                        onAuthSettingsChange?.();
                                                    }}
                                                >
                                                    <div className="w-8 h-8 rounded-lg flex items-center justify-center" style={{ backgroundColor: authMode === 'official' ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}>
                                                        <Globe size={16} className={authMode === 'official' ? '' : 'text-white/70'} style={authMode === 'official' ? { color: accentColor } : undefined} />
                                                    </div>
                                                    <div className="flex-1">
                                                        <span className="text-white text-sm font-medium">{t('settings.networkSettings.authOfficial')}</span>
                                                        <p className="text-xs text-white/40">{t('settings.networkSettings.authOfficialHint')}</p>
                                                    </div>
                                                    <div className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-all ${authMode === 'official' ? '' : 'border-white/30'}`} style={authMode === 'official' ? { borderColor: accentColor, backgroundColor: accentColor } : undefined}>
                                                        {authMode === 'official' && <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />}
                                                    </div>
                                                </div>

                                                {/* Custom */}
                                                <div
                                                    className={`rounded-xl border transition-all ${
                                                        authMode === 'custom' 
                                                            ? 'border-white/20' 
                                                            : 'border-white/[0.06] hover:border-white/[0.12] bg-[#1c1c1e]'
                                                    }`}
                                                    style={authMode === 'custom' ? { backgroundColor: `${accentColor}15`, borderColor: `${accentColor}50` } : undefined}
                                                >
                                                    <div
                                                        className="flex items-center gap-3 p-4 cursor-pointer"
                                                        onClick={() => {
                                                            if (authMode !== 'custom') {
                                                                setAuthModeState('custom');
                                                            }
                                                        }}
                                                    >
                                                        <div className="w-8 h-8 rounded-lg flex items-center justify-center" style={{ backgroundColor: authMode === 'custom' ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}>
                                                            <Edit3 size={16} className={authMode === 'custom' ? '' : 'text-white/70'} style={authMode === 'custom' ? { color: accentColor } : undefined} />
                                                        </div>
                                                        <div className="flex-1">
                                                            <span className="text-white text-sm font-medium">{t('settings.networkSettings.authCustom')}</span>
                                                            <p className="text-xs text-white/40">{t('settings.networkSettings.authCustomHint')}</p>
                                                        </div>
                                                        <div className={`w-5 h-5 rounded-full border-2 flex items-center justify-center transition-all ${authMode === 'custom' ? '' : 'border-white/30'}`} style={authMode === 'custom' ? { borderColor: accentColor, backgroundColor: accentColor } : undefined}>
                                                            {authMode === 'custom' && <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />}
                                                        </div>
                                                    </div>
                                                    
                                                    {/* Custom domain input */}
                                                    {authMode === 'custom' && (
                                                        <div className="px-4 pb-4">
                                                            <div className="flex gap-2">
                                                                <input
                                                                    type="text"
                                                                    value={customAuthDomain}
                                                                    onChange={(e) => setCustomAuthDomain(e.target.value)}
                                                                    onKeyDown={async (e) => {
                                                                        if (e.key === 'Enter' && customAuthDomain.trim()) {
                                                                            setAuthDomain(customAuthDomain.trim());
                                                                            await ipc.settings.update({ authDomain: customAuthDomain.trim() });
                                                                            onAuthSettingsChange?.();
                                                                        }
                                                                    }}
                                                                    placeholder="auth.example.com"
                                                                    className="flex-1 h-10 px-3 rounded-lg bg-[#1c1c1e] border border-white/[0.08] text-white text-sm placeholder-white/30 focus:outline-none focus:border-white/20 transition-colors"
                                                                />
                                                                <button
                                                                    onClick={async () => {
                                                                        if (customAuthDomain.trim()) {
                                                                            setAuthDomain(customAuthDomain.trim());
                                                                            await ipc.settings.update({ authDomain: customAuthDomain.trim() });
                                                                            onAuthSettingsChange?.();
                                                                        }
                                                                    }}
                                                                    disabled={!customAuthDomain.trim()}
                                                                    className="px-4 h-10 rounded-lg text-sm font-medium transition-all disabled:opacity-40"
                                                                    style={{ backgroundColor: accentColor, color: accentTextColor }}
                                                                >
                                                                    {t('common.save')}
                                                                </button>
                                                            </div>
                                                            {authDomain && authDomain !== 'sessions.sanasol.ws' && authDomain !== 'sessionserver.hytale.com' && (
                                                                <p className="text-xs text-white/30 mt-2">
                                                                    {t('settings.networkSettings.currentServer')}: <span className="text-white/50">{authDomain}</span>
                                                                </p>
                                                            )}
                                                        </div>
                                                    )}
                                                </div>
                                            </div>
                                        </div>
                                    )}
                                </div>
                            )}

                            {/* Graphics Tab */}
                            {activeTab === 'graphics' && (
                                <div className="space-y-6">
                                    {/* GPU Preference */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.graphicsSettings.gpuPreference')}</label>
                                        <p className="text-xs text-white/40 mb-4">{t('settings.graphicsSettings.gpuPreferenceHint')}</p>
                                        {hasSingleGpu && (
                                            <div className="mb-3 p-2.5 rounded-lg bg-[#2c2c2e] border border-white/[0.08] text-xs text-white/50 flex items-center gap-2">
                                                <Settings size={14} className="flex-shrink-0 text-white/40" />
                                                {t('settings.graphicsSettings.singleGpuNotice')}
                                            </div>
                                        )}
                                        <div className="space-y-2">
                                            {(['dedicated', 'integrated', 'auto'] as const).map((option) => {
                                                const icons: Record<string, React.ReactNode> = {
                                                    dedicated: <Monitor size={18} />,
                                                    integrated: <HardDrive size={18} />,
                                                    auto: <Settings size={18} />,
                                                };
                                                const isSelected = gpuPreference === option;
                                                const isDisabled = hasSingleGpu && option !== 'auto';
                                                // Find GPU model name for this type
                                                const matchingGpu = gpuAdapters.find(g => g.type === option);
                                                const gpuModelName = option === 'auto'
                                                    ? (gpuAdapters.length > 0 ? gpuAdapters.map(g => g.name).join(' / ') : undefined)
                                                    : matchingGpu?.name;
                                                return (
                                                    <button
                                                        key={option}
                                                        onClick={() => !isDisabled && handleGpuPreferenceChange(option)}
                                                        disabled={isDisabled}
                                                        className={`w-full p-3 rounded-xl border transition-all text-left ${isDisabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'}`}
                                                        style={{
                                                            backgroundColor: isSelected ? `${accentColor}15` : '#151515',
                                                            borderColor: isSelected ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
                                                        }}
                                                    >
                                                        <div className="flex items-center justify-between">
                                                            <div className="flex items-center gap-3">
                                                                <div
                                                                    className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                                                                    style={{ backgroundColor: isSelected ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}
                                                                >
                                                                    <span style={{ color: isSelected ? accentColor : 'rgba(255,255,255,0.5)' }}>{icons[option]}</span>
                                                                </div>
                                                                <div>
                                                                    <div className="text-white text-sm font-medium">{t(`settings.graphicsSettings.gpu_${option}`)}</div>
                                                                    {gpuModelName ? (
                                                                        <div className="text-[11px] text-white/40 mt-0.5">{gpuModelName}</div>
                                                                    ) : (
                                                                        <div className="text-[11px] text-white/30 mt-0.5">{t(`settings.graphicsSettings.gpu_${option}Hint`)}</div>
                                                                    )}
                                                                </div>
                                                            </div>
                                                            {isSelected && (
                                                                <div
                                                                    className="w-5 h-5 rounded-full flex items-center justify-center flex-shrink-0"
                                                                    style={{ backgroundColor: accentColor }}
                                                                >
                                                                    <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />
                                                                </div>
                                                            )}
                                                        </div>
                                                    </button>
                                                );
                                            })}
                                        </div>
                                    </div>

                                </div>
                            )}

                            {/* Variables Tab */}
                            {activeTab === 'variables' && (
                                <div className="space-y-6">
                                    {/* Common Presets */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.variablesSettings.commonPresets')}</label>
                                        <p className="text-xs text-white/40 mb-4">{t('settings.variablesSettings.commonPresetsHint')}</p>
                                        <div className="space-y-2">
                                            {/* Force X11 */}
                                            <div 
                                                className={`p-3 rounded-xl border cursor-pointer transition-colors`}
                                                style={{
                                                    backgroundColor: envForceX11 ? `${accentColor}15` : '#151515',
                                                    borderColor: envForceX11 ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
                                                }}
                                                onClick={() => handleEnvPresetToggle('forceX11', !envForceX11)}
                                            >
                                                <div className="flex items-center justify-between">
                                                    <div>
                                                        <div className="text-white text-sm font-medium">{t('settings.variablesSettings.forceX11')}</div>
                                                        <div className="text-[11px] text-white/40 mt-0.5">{t('settings.variablesSettings.forceX11Hint')}</div>
                                                        <code className="text-[10px] text-white/30 mt-1 block font-mono">SDL_VIDEODRIVER=x11</code>
                                                    </div>
                                                    <div 
                                                        className={`w-10 h-5 rounded-full flex items-center transition-colors flex-shrink-0`}
                                                        style={{ backgroundColor: envForceX11 ? accentColor : 'rgba(255,255,255,0.15)' }}
                                                    >
                                                        <div 
                                                            className={`w-4 h-4 rounded-full shadow-md transform transition-transform mx-0.5 ${envForceX11 ? 'translate-x-5' : 'translate-x-0'}`}
                                                            style={{ backgroundColor: 'white' }}
                                                        />
                                                    </div>
                                                </div>
                                            </div>

                                            {/* Disable Vulkan Layers */}
                                            <div 
                                                className={`p-3 rounded-xl border cursor-pointer transition-colors`}
                                                style={{
                                                    backgroundColor: envDisableVkLayers ? `${accentColor}15` : '#151515',
                                                    borderColor: envDisableVkLayers ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
                                                }}
                                                onClick={() => handleEnvPresetToggle('disableVkLayers', !envDisableVkLayers)}
                                            >
                                                <div className="flex items-center justify-between">
                                                    <div>
                                                        <div className="text-white text-sm font-medium">{t('settings.variablesSettings.disableVkLayers')}</div>
                                                        <div className="text-[11px] text-white/40 mt-0.5">{t('settings.variablesSettings.disableVkLayersHint')}</div>
                                                        <code className="text-[10px] text-white/30 mt-1 block font-mono">VK_LOADER_LAYERS_DISABLE=all</code>
                                                    </div>
                                                    <div 
                                                        className={`w-10 h-5 rounded-full flex items-center transition-colors flex-shrink-0`}
                                                        style={{ backgroundColor: envDisableVkLayers ? accentColor : 'rgba(255,255,255,0.15)' }}
                                                    >
                                                        <div 
                                                            className={`w-4 h-4 rounded-full shadow-md transform transition-transform mx-0.5 ${envDisableVkLayers ? 'translate-x-5' : 'translate-x-0'}`}
                                                            style={{ backgroundColor: 'white' }}
                                                        />
                                                    </div>
                                                </div>
                                            </div>

                                            {/* Force AMD RADV Vulkan */}
                                            <div 
                                                className={`p-3 rounded-xl border cursor-pointer transition-colors`}
                                                style={{
                                                    backgroundColor: envForceAmdVulkan ? `${accentColor}15` : '#151515',
                                                    borderColor: envForceAmdVulkan ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
                                                }}
                                                onClick={() => handleEnvPresetToggle('forceAmdVulkan', !envForceAmdVulkan)}
                                            >
                                                <div className="flex items-center justify-between">
                                                    <div>
                                                        <div className="text-white text-sm font-medium">{t('settings.variablesSettings.forceAmdVulkan')}</div>
                                                        <div className="text-[11px] text-white/40 mt-0.5">{t('settings.variablesSettings.forceAmdVulkanHint')}</div>
                                                        <code className="text-[10px] text-white/30 mt-1 block font-mono">VK_ICD_FILENAMES=...radeon_icd...</code>
                                                    </div>
                                                    <div 
                                                        className={`w-10 h-5 rounded-full flex items-center transition-colors flex-shrink-0`}
                                                        style={{ backgroundColor: envForceAmdVulkan ? accentColor : 'rgba(255,255,255,0.15)' }}
                                                    >
                                                        <div 
                                                            className={`w-4 h-4 rounded-full shadow-md transform transition-transform mx-0.5 ${envForceAmdVulkan ? 'translate-x-5' : 'translate-x-0'}`}
                                                            style={{ backgroundColor: 'white' }}
                                                        />
                                                    </div>
                                                </div>
                                            </div>
                                        </div>
                                    </div>

                                    {/* Custom Environment Variables */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.variablesSettings.customEnvVars')}</label>
                                        <p className="text-xs text-white/40 mb-3">{t('settings.variablesSettings.customEnvVarsHint')}</p>
                                        <div className="flex gap-2">
                                            <input
                                                type="text"
                                                value={gameEnvVars}
                                                onChange={(e) => {
                                                    setGameEnvVars(e.target.value);
                                                    if (gameEnvVarsError) setGameEnvVarsError('');
                                                }}
                                                placeholder={t('settings.variablesSettings.customEnvVarsPlaceholder')}
                                                className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm placeholder-white/35 focus:outline-none font-mono`}
                                            />
                                            <button
                                                onClick={handleSaveGameEnvVars}
                                                className={`h-12 px-4 rounded-xl ${gc} flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors`}
                                            >
                                                <Check size={18} />
                                            </button>
                                        </div>
                                        {gameEnvVarsError && (
                                            <p className="mt-2 text-xs text-yellow-300">{gameEnvVarsError}</p>
                                        )}
                                    </div>
                                </div>
                            )}

                            {/* Logs Tab */}
                            {activeTab === 'logs' && <LogsPage embedded />}

                            {/* Data Tab */}
                            {activeTab === 'data' && (
                                <div className="space-y-6">
                                    {/* Game Running Warning */}
                                    {isGameRunning && (
                                        <div className="p-4 rounded-xl bg-yellow-500/10 border border-yellow-500/20 flex items-center gap-3">
                                            <AlertTriangle size={20} className="text-yellow-400 flex-shrink-0" />
                                            <p className="text-sm text-yellow-400">{t('settings.dataSettings.gameRunningWarning')}</p>
                                        </div>
                                    )}
                                    
                                    {/* Instance Folder */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.dataSettings.instanceFolder')}</label>
                                        <div className="flex gap-2">
                                            <input
                                                type="text"
                                                value={instanceDir}
                                                readOnly
                                                className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm focus:outline-none cursor-default`}
                                            />
                                            <div className={`flex rounded-full overflow-hidden ${gc}`}>
                                                <button
                                                    onClick={handleResetInstanceDir}
                                                    disabled={isGameRunning}
                                                    className={`h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-white/60`}
                                                    title={t('settings.dataSettings.resetToDefault')}
                                                >
                                                    <RotateCcw size={18} />
                                                    <span className="ml-2 text-sm">{t('common.reset')}</span>
                                                </button>
                                                <div className="w-px bg-white/10" />
                                                <button
                                                    onClick={handleBrowseInstanceDir}
                                                    disabled={isGameRunning}
                                                    className={`h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent disabled:hover:text-white/60`}
                                                    title={t('common.browse')}
                                                >
                                                    <FolderOpen size={18} />
                                                    <span className="ml-2 text-sm">{t('common.select')}</span>
                                                </button>
                                                <div className="w-px bg-white/10" />
                                                <button
                                                    onClick={() => BrowserOpenURL(`file://${instanceDir}`)}
                                                    className={`h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors`}
                                                    title={t('common.openFolder')}
                                                >
                                                    <ExternalLink size={18} />
                                                    <span className="ml-2 text-sm">{t('settings.dataSettings.open')}</span>
                                                </button>
                                            </div>
                                        </div>
                                    </div>

                                    {/* Launcher Data Folder */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.dataSettings.launcherDataFolder')}</label>
                                        <div className="flex gap-2">
                                            <input
                                                type="text"
                                                value={launcherDataDir}
                                                readOnly
                                                className={`flex-1 h-12 px-4 rounded-xl ${gc} text-white text-sm focus:outline-none cursor-default`}
                                            />
                                            <div className={`flex rounded-full overflow-hidden ${gc}`}>
                                                <button
                                                    onClick={() => BrowserOpenURL(`file://${launcherDataDir}`)}
                                                    className={`h-12 px-4 flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors`}
                                                    title={t('common.openFolder')}
                                                >
                                                    <ExternalLink size={18} />
                                                    <span className="ml-2 text-sm">{t('settings.dataSettings.open')}</span>
                                                </button>
                                            </div>
                                        </div>
                                    </div>

                                    {/* Launcher Folder Actions */}
                                    <div className="space-y-3">
                                        <button
                                            onClick={handleOpenLauncherFolder}
                                            className={`w-full h-12 px-4 rounded-xl ${gc} flex items-center gap-3 text-white/70 hover:text-white hover:border-white/20 transition-colors`}
                                        >
                                            <FolderOpen size={18} />
                                            <span>{t('settings.dataSettings.openLauncherFolder')}</span>
                                        </button>

                                        <button
                                            onClick={() => setShowDeleteConfirm(true)}
                                            className={`w-full h-12 px-4 rounded-xl ${gc} !border-red-500/30 flex items-center gap-3 text-red-400 hover:text-red-300 hover:bg-red-500/10 transition-colors`}
                                        >
                                            <Trash2 size={18} />
                                            <span>{t('settings.dataSettings.deleteAllData')}</span>
                                        </button>
                                    </div>
                                </div>
                            )}

                            {/* About Tab */}
                            {activeTab === 'about' && (
                                <div className="space-y-6 w-full max-w-6xl mx-auto">
                                    <div className="grid grid-cols-1 xl:grid-cols-[260px_minmax(0,1fr)] gap-6 items-start">
                                        <div className={`p-5 rounded-2xl ${gc} space-y-5`}>
                                            <div className="flex flex-col items-center text-center">
                                                <img
                                                    src={appIcon}
                                                    alt="HyPrism"
                                                    className="w-20 h-20 mb-3"
                                                />
                                                <h3 className="text-xl font-bold text-white">HyPrism</h3>
                                                <p className="text-sm text-white/50">{t('settings.aboutSettings.unofficial')}</p>
                                            </div>

                                            <div className="flex justify-center gap-4">
                                                <button
                                                    onClick={openGitHub}
                                                    className="opacity-80 hover:opacity-100 transition-opacity"
                                                    title="GitHub"
                                                >
                                                    <Github size={28} className="text-white" />
                                                </button>
                                                <button
                                                    onClick={openDiscord}
                                                    className="opacity-80 hover:opacity-100 transition-opacity"
                                                    title="Discord"
                                                >
                                                    <DiscordIcon size={20} color="white" />
                                                </button>
                                                <button
                                                    onClick={openBugReport}
                                                    className="opacity-80 hover:opacity-100 transition-opacity"
                                                    title={t('settings.aboutSettings.bugReport')}
                                                >
                                                    <Bug size={28} className="text-white" />
                                                </button>
                                            </div>

                                            <button
                                                onClick={async () => {
                                                    await ResetOnboarding();
                                                    localStorage.removeItem('hyprism_onboarding_done');
                                                    window.location.reload();
                                                }}
                                                className="w-full p-3 rounded-xl bg-white/5 border border-white/10 text-white/70 hover:bg-white/10 hover:text-white transition-all text-sm"
                                            >
                                                {t('settings.aboutSettings.replayIntro')}
                                            </button>
                                        </div>

                                        <div className="pt-1">
                                        {isLoadingContributors ? (
                                            <div className="flex justify-center py-4">
                                                <div className="w-6 h-6 border-2 rounded-full animate-spin" style={{ borderColor: `${accentColor}30`, borderTopColor: accentColor }} />
                                            </div>
                                        ) : (
                                            <div className="space-y-4">
                                                {/* Maintainer & Auth Server Creator */}
                                                <div className="flex flex-wrap gap-3 xl:gap-4">
                                                    {maintainer && (
                                                        <button
                                                            onClick={() => BrowserOpenURL(maintainer.html_url)}
                                                            className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors min-w-[240px] max-w-[360px]"
                                                        >
                                                            <img 
                                                                src={maintainer.avatar_url} 
                                                                alt={maintainer.login}
                                                                className="w-12 h-12 rounded-full"
                                                            />
                                                            <div className="text-left">
                                                                <span className="text-white font-medium text-sm">{maintainer.login}</span>
                                                                <p className="text-xs text-white/40">{t('settings.aboutSettings.maintainerRole')}</p>
                                                            </div>
                                                        </button>
                                                    )}
                                                    <button
                                                        onClick={() => BrowserOpenURL('https://github.com/sanasol')}
                                                        className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors min-w-[240px] max-w-[360px]"
                                                    >
                                                        <img 
                                                            src="https://avatars.githubusercontent.com/u/1709666?v=4" 
                                                            alt="sanasol"
                                                            className="w-12 h-12 rounded-full"
                                                        />
                                                        <div className="text-left">
                                                            <span className="text-white font-medium text-sm">sanasol</span>
                                                            <p className="text-xs text-white/40">{t('settings.aboutSettings.authRole')}</p>
                                                        </div>
                                                    </button>
                                                    <button
                                                        onClick={() => BrowserOpenURL('https://github.com/freakdaniel')}
                                                        className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors min-w-[240px] max-w-[360px]"
                                                    >
                                                        <img 
                                                            src="https://avatars.githubusercontent.com/u/212660794?v=4" 
                                                            alt="freakdaniel"
                                                            className="w-12 h-12 rounded-full"
                                                        />
                                                        <div className="text-left">
                                                            <span className="text-white font-medium text-sm">Daniel Freak</span>
                                                            <p className="text-xs text-white/40">CoDev, Creator of ton features</p>
                                                        </div>
                                                    </button>
                                                </div>

                                                {/* Description */}
                                                <p className="text-xs text-white/40 text-center xl:text-left">{t('settings.aboutSettings.contributorsDescription')}</p>

                                                {/* Other Contributors */}
                                                {otherContributors.length > 0 && (
                                                    <div className="flex flex-wrap gap-3 sm:gap-4 justify-center xl:justify-start">
                                                        {otherContributors.map((contributor) => (
                                                            <button
                                                                key={contributor.login}
                                                                onClick={() => BrowserOpenURL(contributor.html_url)}
                                                                className="flex flex-col items-center gap-1.5 p-2 rounded-lg hover:bg-white/5 transition-colors w-[88px]"
                                                                title={`${contributor.login} - ${contributor.contributions} contributions`}
                                                            >
                                                                <img 
                                                                    src={contributor.avatar_url} 
                                                                    alt={contributor.login}
                                                                    className="w-12 h-12 rounded-full"
                                                                />
                                                                <span className="text-xs text-white/60 max-w-full truncate text-center">
                                                                    {truncateName(contributor.login, 10)}
                                                                </span>
                                                            </button>
                                                        ))}
                                                    </div>
                                                )}
                                            </div>
                                        )}
                                    </div>
                                    </div>

                                    {/* Disclaimer */}
                                    <div className={`p-4 rounded-2xl ${gc}`}>
                                        <p className="text-white/50 text-sm text-center">
                                            {t('settings.aboutSettings.disclaimer')}
                                        </p>
                                    </div>
                                </div>
                            )}

                            {/* Developer Tab */}
                            {activeTab === 'developer' && devModeEnabled && (
                                <div className="space-y-6">
                                    <div className="p-4 bg-yellow-500/10 border border-yellow-500/30 rounded-xl">
                                        <div className="flex items-center gap-2 text-yellow-500 text-sm font-medium">
                                            <AlertTriangle size={16} />
                                            {t('settings.developerSettings.warning')}
                                        </div>
                                    </div>

                                    {/* Show Intro on Next Launch */}
                                    <div className={`p-4 rounded-2xl ${gc} space-y-4`}>
                                        <h3 className="text-white font-medium text-sm">{t('settings.developerSettings.onboarding')}</h3>
                                        <button
                                            onClick={async () => {
                                                await ResetOnboarding();
                                                localStorage.removeItem('hyprism_onboarding_done');
                                                alert(t('settings.developerSettings.introRestart'));
                                            }}
                                            className="w-full p-3 rounded-xl bg-white/5 border border-white/10 text-white/70 hover:bg-white/10 hover:text-white transition-all text-sm"
                                        >
                                            {t('settings.developerSettings.showIntro')}
                                        </button>
                                    </div>

                                    <div className={`p-4 rounded-2xl ${gc}`}>
                                        <p className="text-white/40 text-xs">
                                            {t('settings.developerSettings.debugInfo')} Tab={activeTab}, Branch={selectedLauncherBranch}, Accent={accentColor}
                                        </p>
                                    </div>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            </div>

            {/* Data Moving Overlay */}
            <AnimatePresence>
                {isMovingData && (
                    <motion.div 
                        className="fixed inset-0 z-[300] flex items-center justify-center bg-[#0a0a0a]/95"
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        transition={{ duration: 0.2 }}
                    >
                        <motion.div 
                            className="max-w-lg w-full mx-8 text-center"
                            initial={{ opacity: 0, scale: 0.95, y: 10 }}
                            animate={{ opacity: 1, scale: 1, y: 0 }}
                            exit={{ opacity: 0, scale: 0.95, y: 10 }}
                            transition={{ duration: 0.25, delay: 0.05 }}
                        >
                            <motion.div
                                key="moving-progress"
                                initial={{ opacity: 0, y: 6, scale: 0.99 }}
                                animate={{ opacity: 1, y: 0, scale: 1 }}
                                exit={{ opacity: 0, y: -10, scale: 0.98 }}
                                transition={{ duration: 0.3, ease: 'easeOut' }}
                            >
                                <h2 className="text-2xl font-bold text-white mb-2">{t('settings.dataSettings.movingData')}</h2>
                                <p className="text-white/60 mb-8">{t('settings.dataSettings.movingDataHint', { file: moveCurrentFile || '...' })}</p>
                                
                                {/* Progress bar */}
                                <div className="relative h-3 bg-white/10 rounded-full overflow-hidden mb-4">
                                    <motion.div 
                                        className="absolute inset-y-0 left-0 rounded-full"
                                        initial={{ width: 0 }}
                                        animate={{ width: `${moveProgress}%` }}
                                        transition={{ duration: 0.3, ease: 'easeOut' }}
                                        style={{ backgroundColor: accentColor }}
                                    />
                                    {moveProgress === 0 && (
                                        <motion.div 
                                            className="absolute inset-y-0 w-1/3 rounded-full"
                                            style={{
                                                background: `linear-gradient(90deg, transparent, ${accentColor}80, transparent)`
                                            }}
                                            animate={{ x: ['-100%', '400%'] }}
                                            transition={{ duration: 1.5, repeat: Infinity, ease: 'linear' }}
                                        />
                                    )}
                                </div>
                                
                                <div className="flex justify-center text-sm">
                                    <span className="text-white/80">{moveProgress > 0 ? `${moveProgress}%` : ''}</span>
                                </div>
                            </motion.div>
                        </motion.div>
                    </motion.div>
                )}
            </AnimatePresence>

            {/* Delete Confirmation Modal */}
            {showDeleteConfirm && (
                <div className={`fixed inset-0 z-[250] flex items-center justify-center bg-[#0a0a0a]/90`}>
                    <div className={`p-6 max-w-md w-full mx-4 glass-panel-static-solid`}>
                        <div className="flex items-center gap-3 mb-4">
                            <div className="w-10 h-10 rounded-full bg-red-500/20 flex items-center justify-center">
                                <Trash2 size={20} className="text-red-400" />
                            </div>
                            <h3 className="text-lg font-bold text-white">{t('settings.deleteAllData.title')}</h3>
                        </div>
                        <p className="text-white/70 text-sm mb-6">
                            {t('settings.deleteAllData.message')}
                        </p>
                        <div className="flex gap-3">
                            <button
                                onClick={() => setShowDeleteConfirm(false)}
                                className="flex-1 h-10 rounded-xl bg-white/5 text-white/70 hover:bg-white/10 transition-colors"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                onClick={handleDeleteLauncherData}
                                className="flex-1 h-10 rounded-xl bg-red-500 text-white font-medium hover:bg-red-600 transition-colors"
                            >
                                {t('common.delete')}
                            </button>
                        </div>
                    </div>
                </div>
            )}
            
            {/* Delete Instance Confirmation Modal */}
            {instanceToDelete && (
                <div className={`fixed inset-0 z-[250] flex items-center justify-center bg-[#0a0a0a]/90`}>
                    <div className={`p-6 max-w-md w-full mx-4 glass-panel-static-solid`}>
                        <div className="flex items-center gap-3 mb-4">
                            <div className="w-10 h-10 rounded-full bg-red-500/20 flex items-center justify-center">
                                <Trash2 size={20} className="text-red-400" />
                            </div>
                            <h3 className="text-lg font-bold text-white">{t('settings.deleteInstance.title')}</h3>
                        </div>
                        <p className="text-white/70 text-sm mb-6">
                            {t('settings.deleteInstance.message')
                                .replace('{{branch}}', instanceToDelete.branch === 'release' ? t('common.release') : t('common.preRelease'))
                                .replace('{{version}}', instanceToDelete.version === 0 ? t('common.latest') : `v${instanceToDelete.version}`)}
                        </p>
                        <div className="flex gap-3">
                            <button
                                onClick={() => setInstanceToDelete(null)}
                                className="flex-1 h-10 rounded-xl bg-white/5 text-white/70 hover:bg-white/10 transition-colors"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                onClick={handleDeleteInstance}
                                className="flex-1 h-10 rounded-xl bg-red-500 text-white font-medium hover:bg-red-600 transition-colors"
                            >
                                {t('common.delete')}
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* Export Instance Modal */}
            {showInstanceExportModal && (
                <div className={`fixed inset-0 z-[250] flex items-center justify-center bg-[#0a0a0a]/95`}>
                    <div className={`p-6 w-full max-w-md mx-4 glass-panel-static-solid`}>
                        <div className="flex items-center justify-between mb-6">
                            <h3 className="text-xl font-bold text-white">{t('settings.instanceSettings.exportInstance')}</h3>
                            <button
                                onClick={() => setShowInstanceExportModal(null)}
                                className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white"
                            >
                                <X size={20} />
                            </button>
                        </div>

                        {/* Export Type - ZIP Only */}
                        <div className="mb-4">
                            <label className="block text-white/60 text-sm mb-2">{t('settings.exportInstance.exportType')}</label>
                            <div className="flex gap-2">
                                <div
                                    className="flex-1 p-3 rounded-xl border-2 transition-colors"
                                    style={{ borderColor: accentColor, backgroundColor: `${accentColor}20` }}
                                >
                                    <Package size={20} className="mx-auto mb-1" style={{ color: accentColor }} />
                                    <div className="text-sm text-white font-medium text-center">{t('settings.exportInstance.zipArchive')}</div>
                                    <div className="text-xs text-white/40 text-center">{t('settings.exportInstance.zipArchiveHint')}</div>
                                </div>
                            </div>
                        </div>

                        {/* Export Folder */}
                        <div className="mb-6">
                            <label className="block text-white/60 text-sm mb-2">{t('settings.exportInstance.exportFolder')}</label>
                            <div className="flex gap-2">
                                <input
                                    type="text"
                                    value={instanceExportPath}
                                    onChange={(e) => setInstanceExportPath(e.target.value)}
                                    placeholder={t('settings.exportInstance.selectExportFolder')}
                                    className="flex-1 px-4 py-2 bg-[#252525] border border-white/10 rounded-xl text-white placeholder-white/40 focus:outline-none focus:border-white/30"
                                />
                                <button
                                    onClick={handleBrowseInstanceExportFolder}
                                    className="px-4 py-2 rounded-xl bg-white/10 hover:bg-white/20 text-white transition-colors"
                                >
                                    <FolderOpen size={20} />
                                </button>
                            </div>
                        </div>

                        {/* Export Button */}
                        <button
                            onClick={() => handleExportInstance(showInstanceExportModal)}
                            disabled={exportingInstance !== null || !instanceExportPath}
                            className={`w-full py-3 rounded-xl font-bold text-lg transition-all ${
                                exportingInstance !== null || !instanceExportPath
                                    ? 'bg-white/10 text-white/40 cursor-not-allowed'
                                    : 'hover:opacity-90'
                            }`}
                            style={exportingInstance === null && instanceExportPath ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
                        >
                            {exportingInstance !== null ? (
                                <span className="flex items-center justify-center gap-2">
                                    <Loader2 size={20} className="animate-spin" />
                                    {t('common.exporting')}
                                </span>
                            ) : (
                                t('common.export')
                            )}
                        </button>
                    </div>
                </div>
            )}

            {/* Import Instance Modal */}
            {showImportModal && (
                <div className={`fixed inset-0 z-[250] flex items-center justify-center bg-[#0a0a0a]/90`}>
                    <div className={`p-6 max-w-md w-full mx-4 glass-panel-static-solid`}>
                        <div className="flex items-center gap-3 mb-4">
                            <div className="w-10 h-10 rounded-full flex items-center justify-center" style={{ backgroundColor: `${accentColor}20` }}>
                                <Download size={20} style={{ color: accentColor }} />
                            </div>
                            <h3 className="text-lg font-bold text-white">{t('settings.importInstance.title')}</h3>
                        </div>
                        <p className="text-white/70 text-sm mb-2">
                            {t('settings.importInstance.importing')}: <span className="text-white">{showImportModal.fileName}</span>
                        </p>
                        <p className="text-white/50 text-xs mb-4">
                            {t('settings.importInstance.selectInstance')}
                        </p>
                        
                        <div className="space-y-3 mb-6">
                            <div>
                                <label className="text-xs text-white/60 mb-1 block">{t('common.branch')}</label>
                                <div className="flex gap-2">
                                    <button
                                        onClick={() => setImportTargetBranch('release')}
                                        className={`flex-1 px-3 py-2 text-sm rounded-lg transition-all flex items-center justify-center gap-2 ${
                                            importTargetBranch === 'release' 
                                                ? 'bg-white/20 text-white' 
                                                : 'bg-white/5 text-white/50 hover:text-white/70'
                                        }`}
                                    >
                                        <Box size={14} />
                                        {t('common.release')}
                                    </button>
                                    <button
                                        onClick={() => setImportTargetBranch('pre-release')}
                                        className={`flex-1 px-3 py-2 text-sm rounded-lg transition-all flex items-center justify-center gap-2 ${
                                            importTargetBranch === 'pre-release' 
                                                ? 'bg-white/20 text-white' 
                                                : 'bg-white/5 text-white/50 hover:text-white/70'
                                        }`}
                                    >
                                        <FlaskConical size={14} />
                                        {t('common.preRelease')}
                                    </button>
                                </div>
                            </div>
                            
                            <div>
                                <label className="text-xs text-white/60 mb-1 block">{t('settings.instanceSettings.version')}</label>
                                <select
                                    value={importTargetVersion}
                                    onChange={(e) => setImportTargetVersion(parseInt(e.target.value))}
                                    className={`w-full h-10 px-3 rounded-lg ${gc} text-white text-sm focus:outline-none`}
                                >
                                    <option value={0}>{t('common.Latest')}</option>
                                    {installedInstances
                                        .filter(i => i.branch === importTargetBranch && i.version !== 0)
                                        .map(i => (
                                            <option key={i.version} value={i.version}>v{i.version}</option>
                                        ))
                                    }
                                </select>
                            </div>
                        </div>
                        
                        <div className="flex gap-3">
                            <button
                                onClick={() => setShowImportModal(null)}
                                disabled={isImportingInstance}
                                className="flex-1 h-10 rounded-xl bg-white/5 text-white/70 hover:bg-white/10 transition-colors"
                            >
                                {t('common.cancel')}
                            </button>
                            <button
                                onClick={handleImportInstance}
                                disabled={isImportingInstance}
                                className="flex-1 h-10 rounded-xl font-medium transition-colors flex items-center justify-center gap-2"
                                style={{ backgroundColor: accentColor, color: accentTextColor }}
                            >
                                {isImportingInstance ? (
                                    <>
                                        <Loader2 size={16} className="animate-spin" />
                                        {t('common.importing')}
                                    </>
                                ) : (
                                    t('common.import')
                                )}
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* All Backgrounds Modal - Now includes solid colors */}
            {showAllBackgrounds && (
                <div className={`fixed inset-0 z-[250] flex items-center justify-center bg-[#0a0a0a]/90`}>
                    <div className={`max-w-2xl w-full mx-4 max-h-[80vh] flex flex-col overflow-hidden glass-panel-static-solid`}>
                        <div className="flex items-center justify-between p-4 border-b border-white/5">
                            <h3 className="text-lg font-bold text-white">{t('settings.visualSettings.chooseBackground')}</h3>
                            <button
                                onClick={() => setShowAllBackgrounds(false)}
                                className="p-2 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/10 transition-colors"
                            >
                                <X size={20} />
                            </button>
                        </div>
                        <div className="flex-1 overflow-y-auto p-4">
                            {/* Slideshow option */}
                            <div 
                                className="p-3 rounded-xl border cursor-pointer transition-colors mb-4"
                                style={{
                                    backgroundColor: backgroundMode === 'slideshow' ? `${accentColor}20` : '#151515',
                                    borderColor: backgroundMode === 'slideshow' ? `${accentColor}50` : 'rgba(255,255,255,0.1)'
                                }}
                                onClick={() => handleBackgroundModeChange('slideshow')}
                            >
                                <div className="flex items-center gap-3">
                                    <div 
                                        className="w-5 h-5 rounded-full border-2 flex items-center justify-center"
                                        style={{ borderColor: backgroundMode === 'slideshow' ? accentColor : 'rgba(255,255,255,0.3)' }}
                                    >
                                        {backgroundMode === 'slideshow' && <div className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: accentColor }} />}
                                    </div>
                                    <div>
                                        <span className="text-white text-sm font-medium">{t('settings.visualSettings.slideshow')}</span>
                                        <p className="text-xs text-white/40">{t('settings.visualSettings.slideshowHint')}</p>
                                    </div>
                                </div>
                            </div>

                            {/* Image backgrounds */}
                            <p className="text-xs text-white/40 mb-2">{t('settings.visualSettings.images')}</p>
                            <div className="grid grid-cols-4 gap-3 mb-6">
                                {backgroundImages.map((bg) => (
                                    <div
                                        key={bg.name}
                                        className="relative aspect-video rounded-lg overflow-hidden cursor-pointer border-2 transition-all"
                                        style={{
                                            borderColor: backgroundMode === bg.name ? accentColor : 'transparent',
                                            boxShadow: backgroundMode === bg.name ? `0 0 0 2px ${accentColor}30` : 'none'
                                        }}
                                        onClick={() => handleBackgroundModeChange(bg.name)}
                                    >
                                        <img 
                                            src={bg.url} 
                                            alt={bg.name}
                                            className="w-full h-full object-cover"
                                        />
                                        {backgroundMode === bg.name && (
                                            <div className="absolute inset-0 flex items-center justify-center" style={{ backgroundColor: `${accentColor}30` }}>
                                                <Check size={24} className="text-white drop-shadow-lg" />
                                            </div>
                                        )}
                                    </div>
                                ))}
                            </div>

                        </div>
                    </div>
                </div>
            )}
        </>
    );
};
