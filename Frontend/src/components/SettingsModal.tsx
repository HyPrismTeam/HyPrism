import React, { useState, useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { X, Github, Bug, Check, AlertTriangle, ChevronDown, ExternalLink, Power, FolderOpen, Trash2, Settings, Database, Globe, Code, Image, Loader2, Languages, FlaskConical, RotateCcw, Monitor, Zap, Download, HardDrive, Package, RefreshCw, Pin, Box, Wifi } from 'lucide-react';
import { ipc } from '@/lib/ipc';
import { changeLanguage } from '../i18n';

// Alias for compatibility — maps to ipc.browser.open
const BrowserOpenURL = (url: string) => ipc.browser.open(url);

// InstalledVersionInfo type (was in api/backend)
export interface InstalledVersionInfo {
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
async function GetDisableNews(): Promise<boolean> { return (await ipc.settings.get()).disableNews ?? false; }
async function SetDisableNews(v: boolean): Promise<void> { await ipc.settings.update({ disableNews: v }); }
async function GetBackgroundMode(): Promise<string> { return (await ipc.settings.get()).backgroundMode ?? 'image'; }
async function SetBackgroundMode(v: string): Promise<void> { await ipc.settings.update({ backgroundMode: v }); }
async function GetCustomInstanceDir(): Promise<string> { return (await ipc.settings.get()).dataDirectory ?? ''; }
async function GetNick(): Promise<string> { return (await ipc.profile.get()).nick ?? 'HyPrism'; }
async function SetNick(name: string): Promise<void> { console.warn('[IPC] SetNick: no dedicated channel'); }
async function GetUUID(): Promise<string> { return (await ipc.profile.get()).uuid ?? ''; }
async function SetUUID(uuid: string): Promise<void> { console.warn('[IPC] SetUUID: no dedicated channel'); }
async function GetAvatarPreview(): Promise<string | null> { return (await ipc.profile.get()).avatarPath ?? null; }
async function GetAuthDomain(): Promise<string> { return (await ipc.settings.get()).authDomain ?? 'sessions.sanasol.ws'; }
async function GetDiscordLink(): Promise<string> { console.warn('[IPC] GetDiscordLink: stub'); return 'https://discord.gg/hyprism'; }

// TODO: These need dedicated IPC channels
const _stub = <T,>(name: string, fb: T) => async (..._a: any[]): Promise<T> => { console.warn(`[IPC] ${name}: no channel`); return fb; };
const OpenLauncherFolder = _stub('OpenLauncherFolder', undefined as void);
const DeleteLauncherData = _stub('DeleteLauncherData', true);
const GetLauncherFolderPath = _stub('GetLauncherFolderPath', '');
const GetDefaultInstanceDir = _stub('GetDefaultInstanceDir', '');
const SetInstanceDirectory = _stub<void>('SetInstanceDirectory', undefined as void);
const BrowseFolder = _stub('BrowseFolder', '');
const GetLauncherDataDirectory = _stub('GetLauncherDataDirectory', '');
const SetLauncherDataDirectory = _stub<void>('SetLauncherDataDirectory', undefined as void);
const GetInstalledVersionsDetailed = _stub<InstalledVersionInfo[]>('GetInstalledVersionsDetailed', []);
const ExportInstance = _stub('ExportInstance', '');
const DeleteGame = _stub('DeleteGame', false);
const OpenInstanceFolder = _stub('OpenInstanceFolder', undefined as void);
const ResetOnboarding = _stub('ResetOnboarding', undefined as void);
const GetShowAlphaMods = _stub('GetShowAlphaMods', false);
const SetShowAlphaMods = _stub<void>('SetShowAlphaMods', undefined as void);
const ImportInstanceFromZip = _stub('ImportInstanceFromZip', true);
const InstallOptimizationMods = _stub('InstallOptimizationMods', true);
const GetLastExportPath = _stub('GetLastExportPath', '');
import { useAccentColor } from '../contexts/AccentColorContext';
import { DiscordIcon } from './icons/DiscordIcon';
import { Language } from '../constants/enums';
import { LANGUAGE_CONFIG } from '../constants/languages';
import { ACCENT_COLORS, SOLID_COLORS } from '../constants/colors';
import appIcon from '../assets/images/appicon.png';

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
    onShowModManager?: (query?: string) => void;
    rosettaWarning?: { message: string; command: string; tutorialUrl?: string } | null;
    onBackgroundModeChange?: (mode: string) => void;
    onNewsDisabledChange?: (disabled: boolean) => void;
    onAccentColorChange?: (color: string) => void;
    onInstanceDeleted?: () => void;
    pageMode?: boolean;
}

type SettingsTab = 'general' | 'visual' | 'graphics' | 'language' | 'data' | 'instances' | 'about' | 'developer';

// Auth server base URL for avatar/skin head
const DEFAULT_AUTH_DOMAIN = 'sessions.sanasol.ws';

export const SettingsModal: React.FC<SettingsModalProps> = ({
    onClose,
    launcherBranch,
    onLauncherBranchChange,
    onShowModManager,
    rosettaWarning,
    onBackgroundModeChange,
    onNewsDisabledChange,
    onAccentColorChange,
    onInstanceDeleted,
    pageMode: isPageMode = false
}) => {
    const { i18n, t } = useTranslation();
    const [activeTab, setActiveTab] = useState<SettingsTab>('general');
    const [isLanguageOpen, setIsLanguageOpen] = useState(false);
    const [isBranchOpen, setIsBranchOpen] = useState(false);
    const [showTranslationConfirm, setShowTranslationConfirm] = useState<{ langName: string; langCode: string; searchQuery: string } | null>(null);
    const [dontAskAgain, setDontAskAgain] = useState(false);
    const [selectedLauncherBranch, setSelectedLauncherBranch] = useState(launcherBranch);
    const [closeAfterLaunch, setCloseAfterLaunch] = useState(false);
    const [launcherFolderPath, setLauncherFolderPath] = useState('');
    const [instanceDir, setInstanceDir] = useState('');
    const [devModeEnabled, setDevModeEnabled] = useState(false);
    const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
    const [disableNews, setDisableNews] = useState(false);
    const [showAlphaMods, setShowAlphaModsState] = useState(false);
    const [onlineMode, setOnlineMode] = useState(true);
    const [backgroundMode, setBackgroundModeState] = useState('slideshow');
    const [showAllBackgrounds, setShowAllBackgrounds] = useState(false);
    const [launcherDataDir, setLauncherDataDir] = useState('');
    const [isInstallingOptMods, setIsInstallingOptMods] = useState(false);
    const [optModsMessage, setOptModsMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
    const { accentColor, accentTextColor, setAccentColor: setAccentColorContext } = useAccentColor();
    const [contributors, setContributors] = useState<Contributor[]>([]);
    const [isLoadingContributors, setIsLoadingContributors] = useState(false);
    const languageDropdownRef = useRef<HTMLDivElement>(null);
    const branchDropdownRef = useRef<HTMLDivElement>(null);
    
    // Instances state
    const [installedInstances, setInstalledInstances] = useState<InstalledVersionInfo[]>([]);
    const [isLoadingInstances, setIsLoadingInstances] = useState(false);
    const [exportingInstance, setExportingInstance] = useState<string | null>(null);
    const [exportMessage, setExportMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
    const [instanceToDelete, setInstanceToDelete] = useState<InstalledVersionInfo | null>(null);
    const [isImportingInstance, setIsImportingInstance] = useState(false);
    const [isDraggingZip, setIsDraggingZip] = useState(false);
    const [showImportModal, setShowImportModal] = useState<{ zipBase64: string; fileName: string } | null>(null);
    const [importTargetBranch, setImportTargetBranch] = useState<'release' | 'pre-release'>('release');
    const [importTargetVersion, setImportTargetVersion] = useState<number>(0);
    const [showInstanceExportModal, setShowInstanceExportModal] = useState<InstalledVersionInfo | null>(null);
    const [instanceExportPath, setInstanceExportPath] = useState<string>('');

    // Profile state
    const [profileUsername, setProfileUsername] = useState('');
    const [profileUuid, setProfileUuid] = useState('');
    const [editingUsername, setEditingUsername] = useState(false);
    const [editingUuid, setEditingUuid] = useState(false);
    const [editUsernameValue, setEditUsernameValue] = useState('');
    const [editUuidValue, setEditUuidValue] = useState('');
    const [copiedUuid, setCopiedUuid] = useState(false);
    const [authDomain, setAuthDomain] = useState('sessions.sanasol.ws');
    const [localAvatar, setLocalAvatar] = useState<string | null>(null);

    useEffect(() => {
        const loadSettings = async () => {
            try {
                const closeAfter = await GetCloseAfterLaunch();
                setCloseAfterLaunch(closeAfter);
                
                const folderPath = await GetLauncherFolderPath();
                setLauncherFolderPath(folderPath);
                
                const customDir = await GetCustomInstanceDir();
                setInstanceDir(customDir || folderPath); // Show real path
                
                const newsDisabled = await GetDisableNews();
                setDisableNews(newsDisabled);
                
                const showAlpha = await GetShowAlphaMods();
                setShowAlphaModsState(showAlpha);

                const online = (await ipc.settings.get()).onlineMode ?? true;
                setOnlineMode(online);
                
                const bgMode = await GetBackgroundMode();
                setBackgroundModeState(bgMode);
                
                const dataDir = await GetLauncherDataDirectory();
                setLauncherDataDir(dataDir || folderPath); // Show real path
                
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
                if (domain) setAuthDomain(domain);
                
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
    
    const handleInstanceZipDrop = async (e: React.DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        setIsDraggingZip(false);
        
        const file = e.dataTransfer.files[0];
        if (!file || !file.name.toLowerCase().endsWith('.zip')) {
            return;
        }
        
        // Read file as base64
        const reader = new FileReader();
        reader.onload = (event) => {
            const base64 = (event.target?.result as string)?.split(',')[1];
            if (base64) {
                setShowImportModal({ zipBase64: base64, fileName: file.name });
            }
        };
        reader.readAsDataURL(file);
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
    
    const formatSize = (bytes: number): string => {
        if (bytes === 0) return '0 B';
        const k = 1024;
        const sizes = ['B', 'KB', 'MB', 'GB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
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
                if (showTranslationConfirm) {
                    setShowTranslationConfirm(null);
                } else if (showAllBackgrounds) {
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
    }, [onClose, showTranslationConfirm, showAllBackgrounds]);

    const handleLanguageSelect = async (langCode: Language) => {
        setIsLanguageOpen(false);

        try {
            await changeLanguage(langCode);
            console.log(`Language set to: ${langCode}`);
        } catch (error) {
            console.warn('Failed to change language:', error);
        }

        if (localStorage.getItem('suppressTranslationPrompt') === 'true') {
            return;
        }

        if (langCode !== Language.ENGLISH && onShowModManager) {
            const langConfig = LANGUAGE_CONFIG[langCode];
            if (langConfig) {
                setDontAskAgain(false);
                setShowTranslationConfirm({
                    langName: langConfig.nativeName,
                    langCode: langCode,
                    searchQuery: langConfig.searchQuery
                });
            }
        }
    };

    const handleTranslationConfirm = () => {
        if (showTranslationConfirm && onShowModManager) {
            if (dontAskAgain) {
                localStorage.setItem('suppressTranslationPrompt', 'true');
            }
            onShowModManager(showTranslationConfirm.searchQuery);
        }
        setShowTranslationConfirm(null);
    };

    const handleTranslationDismiss = () => {
        if (dontAskAgain) {
            localStorage.setItem('suppressTranslationPrompt', 'true');
        }
        setShowTranslationConfirm(null);
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

    const handleOpenLauncherFolder = async () => {
        try {
            await OpenLauncherFolder();
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
                setInstanceDir(selectedPath);
                await SetInstanceDirectory(selectedPath);
            }
        } catch (err) {
            console.error('Failed to set instance directory:', err);
        }
    };

    const handleDisableNewsChange = async (disabled: boolean) => {
        setDisableNews(disabled);
        try {
            await SetDisableNews(disabled);
            onNewsDisabledChange?.(disabled);
        } catch (err) {
            console.error('Failed to set news setting:', err);
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

    const handleBrowseLauncherDataDir = async () => {
        try {
            const selectedPath = await BrowseFolder(launcherDataDir || launcherFolderPath);
            if (selectedPath) {
                setLauncherDataDir(selectedPath);
                await SetLauncherDataDirectory(selectedPath);
            }
        } catch (err) {
            console.error('Failed to browse folder:', err);
        }
    };

    const handleResetInstanceDir = async () => {
        try {
            const defaultDir = await GetDefaultInstanceDir();
            setInstanceDir(defaultDir);
            await SetInstanceDirectory(defaultDir);
        } catch (err) {
            console.error('Failed to reset instance directory:', err);
        }
    };

    const handleResetLauncherDataDir = async () => {
        try {
            setLauncherDataDir(launcherFolderPath);
            await SetLauncherDataDirectory(launcherFolderPath);
        } catch (err) {
            console.error('Failed to reset launcher data directory:', err);
        }
    };

    const handleInstallOptimizationMods = async () => {
        setIsInstallingOptMods(true);
        setOptModsMessage(null);
        try {
            const success = await InstallOptimizationMods();
            if (success) {
                setOptModsMessage({ type: 'success', text: t('settings.graphicsSettings.optModsInstalled') });
            } else {
                setOptModsMessage({ type: 'error', text: t('settings.graphicsSettings.optModsFailed') });
            }
        } catch (err) {
            console.error('Failed to install optimization mods:', err);
            setOptModsMessage({ type: 'error', text: t('settings.graphicsSettings.optModsFailed') });
        }
        setIsInstallingOptMods(false);
        
        // Clear message after 3 seconds
        setTimeout(() => setOptModsMessage(null), 3000);
    };

    const handleAccentColorChange = async (color: string) => {
        // Update the global context (which also saves to backend)
        await setAccentColorContext(color);
        onAccentColorChange?.(color);
    };

    const handleLanguageChange = (langCode: Language) => {
        i18n.changeLanguage(langCode);
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
        { id: 'visual' as const, icon: Image, label: t('settings.visual') },
        { id: 'graphics' as const, icon: Monitor, label: t('settings.graphics') },
        { id: 'language' as const, icon: Languages, label: t('settings.language') },
        { id: 'data' as const, icon: Database, label: t('settings.data') },
        { id: 'instances' as const, icon: HardDrive, label: t('settings.instances') },
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

    const handleSaveUsername = async () => {
        if (editUsernameValue.trim() && editUsernameValue.length <= 16) {
            try {
                await SetNick(editUsernameValue.trim());
                setProfileUsername(editUsernameValue.trim());
                setEditingUsername(false);
            } catch (err) {
                console.error('Failed to save username:', err);
            }
        }
    };

    const handleSaveUuid = async () => {
        const trimmed = editUuidValue.trim();
        if (trimmed) {
            try {
                await SetUUID(trimmed);
                setProfileUuid(trimmed);
                setEditingUuid(false);
            } catch (err) {
                console.error('Failed to save UUID:', err);
            }
        }
    };

    const handleRandomizeUuid = () => {
        const newUuid = generateUUID();
        setEditUuidValue(newUuid);
    };

    const handleCopyUuid = async () => {
        try {
            await navigator.clipboard.writeText(profileUuid);
            setCopiedUuid(true);
            setTimeout(() => setCopiedUuid(false), 2000);
        } catch (err) {
            console.error('Failed to copy UUID:', err);
        }
    };

    // Get avatar head URL from auth server
    const getAvatarHeadUrl = (uuid: string) => {
        const domain = authDomain || DEFAULT_AUTH_DOMAIN;
        return `https://${domain}/avatar/${uuid}/head?bg=transparent`;
    };

    const truncateName = (name: string, maxLength: number = 10) => {
        if (name.length <= maxLength) return name;
        return name.slice(0, maxLength - 2) + '...';
    };

    // Get the maintainer (yyyumeniku)
    const maintainer = contributors.find(c => c.login.toLowerCase() === 'yyyumeniku');
    const otherContributors = contributors.filter(c => c.login.toLowerCase() !== 'yyyumeniku');


    return (
        <>
            <div className={isPageMode
                ? "w-full h-full"
                : "fixed inset-0 z-[200] flex items-center justify-center bg-black/60 backdrop-blur-sm"
            }>
                <div className={`bg-[#1a1a1a] border border-white/10 rounded-2xl shadow-2xl flex overflow-hidden ${isPageMode ? 'w-full h-full' : 'mx-4'}`} style={isPageMode ? undefined : { width: '800px', height: '600px' }}>
                    {/* Sidebar */}
                    <div className="w-48 bg-[#151515] border-r border-white/5 flex flex-col py-4">
                        <h2 className="text-lg font-bold text-white px-4 mb-4">{t('settings.title')}</h2>
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
                        <div className="px-2 pt-4 border-t border-white/5 mx-2">
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

                    {/* Content */}
                    <div className="flex-1 flex flex-col min-w-0">
                        {/* Header */}
                        <div className="flex items-center justify-between p-4 border-b border-white/5">
                            <h3 className="text-white font-medium">{tabs.find(t => t.id === activeTab)?.label}</h3>
                            {!isPageMode && (
                                <button
                                    onClick={onClose}
                                    className="p-2 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/10 transition-colors"
                                >
                                    <X size={20} />
                                </button>
                            )}
                        </div>

                        {/* Scrollable Content */}
                        <div className="flex-1 overflow-y-auto p-6 space-y-6">
                            {/* Rosetta Warning (shown on all tabs) */}
                            {rosettaWarning && (
                                <div className="p-4 bg-yellow-500/10 border border-yellow-500/30 rounded-xl">
                                    <div className="flex items-start gap-3">
                                        <AlertTriangle size={20} className="text-yellow-500 flex-shrink-0 mt-0.5" />
                                        <div className="flex-1">
                                            <p className="text-yellow-500 text-sm font-medium mb-2">{rosettaWarning.message}</p>
                                            <div className="flex flex-col gap-2">
                                                <code className="text-xs text-white/70 bg-black/30 px-2 py-1 rounded font-mono break-all">
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
                                                className="w-full h-12 px-4 rounded-xl bg-[#151515] border border-white/10 flex items-center justify-between text-white transition-colors"
                                                style={{ borderColor: isLanguageOpen ? `${accentColor}50` : undefined }}
                                            >
                                                <div className="flex items-center gap-2">
                                                    <span className="font-medium">{currentLangConfig.nativeName}</span>
                                                    <span className="text-white/50 text-sm">({currentLangConfig.name})</span>
                                                </div>
                                                <ChevronDown size={16} className={`text-white/40 transition-transform ${isLanguageOpen ? 'rotate-180' : ''}`} />
                                            </button>

                                            {isLanguageOpen && (
                                                <div className="absolute top-full left-0 right-0 mt-2 z-10 max-h-60 overflow-y-auto bg-[#1a1a1a] border border-white/10 rounded-xl shadow-xl shadow-black/50">
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
                                                className="w-full h-12 px-4 rounded-xl bg-[#151515] border border-white/10 flex items-center justify-between text-white transition-colors"
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
                                                <div className="absolute top-full left-0 right-0 mt-2 z-10 bg-[#1a1a1a] border border-white/10 rounded-xl shadow-xl shadow-black/50 overflow-hidden">
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

                                    {/* Toggle Settings */}
                                    <div className="space-y-3">
                                        {/* Close After Launch */}
                                        <div 
                                            className="flex items-center justify-between p-3 rounded-xl bg-[#151515] border border-white/10 cursor-pointer hover:border-white/20 transition-colors"
                                            onClick={handleCloseAfterLaunchChange}
                                        >
                                            <div className="flex items-center gap-3">
                                                <Power size={18} className="text-white/60" />
                                                <div>
                                                    <span className="text-white text-sm">{t('settings.generalSettings.closeLauncher')}</span>
                                                    <p className="text-xs text-white/40">{t('settings.generalSettings.closeLauncherHint')}</p>
                                                </div>
                                            </div>
                                            <div 
                                                className="w-10 h-6 rounded-full flex items-center transition-colors"
                                                style={{ backgroundColor: closeAfterLaunch ? accentColor : 'rgba(255,255,255,0.2)' }}
                                            >
                                                <div 
                                                    className={`w-4 h-4 rounded-full shadow-md transform transition-transform ${closeAfterLaunch ? 'translate-x-5' : 'translate-x-1'}`}
                                                    style={{ backgroundColor: closeAfterLaunch ? accentTextColor : 'white' }}
                                                />
                                            </div>
                                        </div>

                                        {/* Show Alpha Mods Toggle */}
                                        <div 
                                            className="flex items-center justify-between p-3 rounded-xl bg-[#151515] border border-white/10 cursor-pointer hover:border-white/20 transition-colors"
                                            onClick={async () => {
                                                const newValue = !showAlphaMods;
                                                setShowAlphaModsState(newValue);
                                                await SetShowAlphaMods(newValue);
                                            }}
                                        >
                                            <div className="flex items-center gap-3">
                                                <FlaskConical size={18} className="text-white/60" />
                                                <div>
                                                    <span className="text-white text-sm">{t('settings.generalSettings.showAlphaMods')}</span>
                                                    <p className="text-xs text-white/40">{t('settings.generalSettings.showAlphaModsHint')}</p>
                                                </div>
                                            </div>
                                            <div 
                                                className="w-10 h-6 rounded-full flex items-center transition-colors"
                                                style={{ backgroundColor: showAlphaMods ? accentColor : 'rgba(255,255,255,0.2)' }}
                                            >
                                                <div 
                                                    className={`w-4 h-4 rounded-full shadow-md transform transition-transform ${showAlphaMods ? 'translate-x-5' : 'translate-x-1'}`}
                                                    style={{ backgroundColor: showAlphaMods ? accentTextColor : 'white' }}
                                                />
                                            </div>
                                        </div>

                                        {/* Online Mode Toggle */}
                                        <div 
                                            className="flex items-center justify-between p-3 rounded-xl bg-[#151515] border border-white/10 cursor-pointer hover:border-white/20 transition-colors"
                                            onClick={async () => {
                                                const newValue = !onlineMode;
                                                setOnlineMode(newValue);
                                                await ipc.settings.update({ onlineMode: newValue });
                                            }}
                                        >
                                            <div className="flex items-center gap-3">
                                                <Wifi size={18} className="text-white/60" />
                                                <div>
                                                    <span className="text-white text-sm">{t('settings.generalSettings.onlineMode')}</span>
                                                    <p className="text-xs text-white/40">{t('settings.generalSettings.onlineModeHint')}</p>
                                                </div>
                                            </div>
                                            <div 
                                                className="w-10 h-6 rounded-full flex items-center transition-colors"
                                                style={{ backgroundColor: onlineMode ? accentColor : 'rgba(255,255,255,0.2)' }}
                                            >
                                                <div 
                                                    className={`w-4 h-4 rounded-full shadow-md transform transition-transform ${onlineMode ? 'translate-x-5' : 'translate-x-1'}`}
                                                    style={{ backgroundColor: onlineMode ? accentTextColor : 'white' }}
                                                />
                                            </div>
                                        </div>
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

                                        {/* Solid colors section */}
                                        <div className="mt-3 p-3 rounded-xl bg-[#0f0f0f] border border-white/5">
                                            <p className="text-xs text-white/40 mb-2">{t('settings.visualSettings.solidColors')}</p>
                                            <div className="grid grid-cols-8 gap-2">
                                                {SOLID_COLORS.map((color) => (
                                                    <div
                                                        key={color}
                                                        className="aspect-square rounded-lg cursor-pointer border-2 transition-all flex items-center justify-center"
                                                        style={{
                                                            backgroundColor: color,
                                                            borderColor: backgroundMode === `color:${color}` ? accentColor : 'transparent',
                                                            boxShadow: backgroundMode === `color:${color}` ? `0 0 0 2px ${accentColor}30` : 'none'
                                                        }}
                                                        onClick={() => handleBackgroundModeChange(`color:${color}`)}
                                                    >
                                                        {backgroundMode === `color:${color}` && (
                                                            <Check size={14} className="text-white drop-shadow-lg" />
                                                        )}
                                                    </div>
                                                ))}
                                            </div>
                                        </div>
                                    </div>

                                    {/* Disable News Toggle */}
                                    <div 
                                        className="flex items-center justify-between p-3 rounded-xl bg-[#151515] border border-white/10 cursor-pointer hover:border-white/20 transition-colors"
                                        onClick={() => handleDisableNewsChange(!disableNews)}
                                    >
                                        <div className="flex items-center gap-3">
                                            <Globe size={18} className="text-white/60" />
                                            <div>
                                                <span className="text-white text-sm">{t('settings.visualSettings.hideNews')}</span>
                                                <p className="text-xs text-white/40">{t('settings.visualSettings.hideNewsHint')}</p>
                                            </div>
                                        </div>
                                        <div 
                                            className="w-10 h-6 rounded-full flex items-center transition-colors"
                                            style={{ backgroundColor: disableNews ? accentColor : 'rgba(255,255,255,0.2)' }}
                                        >
                                            <div 
                                                className={`w-4 h-4 rounded-full shadow-md transform transition-transform ${disableNews ? 'translate-x-5' : 'translate-x-1'}`}
                                                style={{ backgroundColor: disableNews ? accentTextColor : 'white' }}
                                            />
                                        </div>
                                    </div>
                                </div>
                            )}

                            {/* Graphics Tab */}
                            {activeTab === 'graphics' && (
                                <div className="space-y-6">
                                    {/* Optimization Mods */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.graphicsSettings.optimizationMods')}</label>
                                        <p className="text-xs text-white/40 mb-4">{t('settings.graphicsSettings.optimizationModsHint')}</p>
                                        <button
                                            onClick={handleInstallOptimizationMods}
                                            disabled={isInstallingOptMods}
                                            className="w-full p-4 rounded-xl border border-white/10 hover:border-white/20 transition-all flex items-center justify-between disabled:opacity-50 disabled:cursor-not-allowed"
                                            style={{ backgroundColor: '#151515' }}
                                        >
                                            <div className="flex items-center gap-3">
                                                <div 
                                                    className="w-10 h-10 rounded-xl flex items-center justify-center"
                                                    style={{ backgroundColor: `${accentColor}20` }}
                                                >
                                                    <Zap size={20} style={{ color: accentColor }} />
                                                </div>
                                                <div className="text-left">
                                                    <div className="text-white font-medium">{t('settings.graphicsSettings.enableOptimizationMods')}</div>
                                                    <div className="text-xs text-white/40">
                                                        {isInstallingOptMods ? t('settings.graphicsSettings.installingOptMods') : t('settings.graphicsSettings.installOptModsAction')}
                                                    </div>
                                                </div>
                                            </div>
                                            {isInstallingOptMods && (
                                                <Loader2 size={20} className="animate-spin text-white/40" />
                                            )}
                                        </button>
                                        {optModsMessage && (
                                            <div 
                                                className={`mt-2 p-3 rounded-lg text-sm ${
                                                    optModsMessage.type === 'success' 
                                                        ? 'bg-green-500/20 text-green-400' 
                                                        : 'bg-red-500/20 text-red-400'
                                                }`}
                                            >
                                                {optModsMessage.text}
                                            </div>
                                        )}
                                    </div>
                                </div>
                            )}

                            {/* Language Tab */}
                            {activeTab === 'language' && (
                                <div className="space-y-6">
                                    <div>
                                        <label className="block text-sm text-white/60 mb-3">{t('settings.languageSettings.selectLanguage')}</label>
                                        <p className="text-xs text-white/40 mb-4">{t('settings.languageSettings.interfaceLanguageHint')}</p>
                                        
                                        <div className="grid grid-cols-2 gap-2">
                                            {Object.values(LANGUAGE_CONFIG).map((lang) => (
                                                <button
                                                    key={lang.code}
                                                    onClick={() => handleLanguageChange(lang.code)}
                                                    className="p-3 rounded-xl border transition-all text-left"
                                                    style={{
                                                        backgroundColor: i18n.language === lang.code ? `${accentColor}20` : '#151515',
                                                        borderColor: i18n.language === lang.code ? `${accentColor}50` : 'rgba(255,255,255,0.1)'
                                                    }}
                                                >
                                                    <div className="flex items-center gap-3">
                                                        {i18n.language === lang.code && (
                                                            <div 
                                                                className="w-5 h-5 rounded-full flex items-center justify-center"
                                                                style={{ backgroundColor: accentColor }}
                                                            >
                                                                <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />
                                                            </div>
                                                        )}
                                                        <div className={i18n.language !== lang.code ? 'ml-8' : ''}>
                                                            <span className="text-white text-sm font-medium block">{lang.nativeName}</span>
                                                            <span className="text-xs text-white/40">{lang.name}</span>
                                                        </div>
                                                    </div>
                                                </button>
                                            ))}
                                        </div>
                                    </div>
                                </div>
                            )}

                            {/* Data Tab */}
                            {activeTab === 'data' && (
                                <div className="space-y-6">
                                    {/* Instance Folder */}
                                    <div>
                                        <label className="block text-sm text-white/60 mb-2">{t('settings.dataSettings.instanceFolder')}</label>
                                        <div className="flex gap-2">
                                            <input
                                                type="text"
                                                value={instanceDir}
                                                onChange={(e) => setInstanceDir(e.target.value)}
                                                onBlur={async () => {
                                                    if (instanceDir.trim()) {
                                                        await SetInstanceDirectory(instanceDir.trim());
                                                    }
                                                }}
                                                className="flex-1 h-12 px-4 rounded-xl bg-[#151515] border border-white/10 text-white text-sm focus:outline-none focus:border-white/30"
                                            />
                                            <div className="flex rounded-full overflow-hidden border border-white/10">
                                                <button
                                                    onClick={handleResetInstanceDir}
                                                    className="h-12 px-4 bg-[#151515] flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                                                    title={t('settings.dataSettings.resetToDefault')}
                                                >
                                                    <RotateCcw size={18} />
                                                    <span className="ml-2 text-sm">{t('common.reset')}</span>
                                                </button>
                                                <div className="w-px bg-white/10" />
                                                <button
                                                    onClick={handleBrowseInstanceDir}
                                                    className="h-12 px-4 bg-[#151515] flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                                                    title={t('common.browse')}
                                                >
                                                    <FolderOpen size={18} />
                                                    <span className="ml-2 text-sm">{t('common.select')}</span>
                                                </button>
                                                <div className="w-px bg-white/10" />
                                                <button
                                                    onClick={() => BrowserOpenURL(`file://${instanceDir}`)}
                                                    className="h-12 px-4 bg-[#151515] flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
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
                                                onChange={(e) => setLauncherDataDir(e.target.value)}
                                                onBlur={async () => {
                                                    if (launcherDataDir.trim()) {
                                                        await SetLauncherDataDirectory(launcherDataDir.trim());
                                                    }
                                                }}
                                                className="flex-1 h-12 px-4 rounded-xl bg-[#151515] border border-white/10 text-white text-sm focus:outline-none focus:border-white/30"
                                            />
                                            <div className="flex rounded-full overflow-hidden border border-white/10">
                                                <button
                                                    onClick={handleResetLauncherDataDir}
                                                    className="h-12 px-4 bg-[#151515] flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                                                    title={t('settings.dataSettings.resetToDefault')}
                                                >
                                                    <RotateCcw size={18} />
                                                    <span className="ml-2 text-sm">{t('common.reset')}</span>
                                                </button>
                                                <div className="w-px bg-white/10" />
                                                <button
                                                    onClick={handleBrowseLauncherDataDir}
                                                    className="h-12 px-4 bg-[#151515] flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                                                    title={t('common.browse')}
                                                >
                                                    <FolderOpen size={18} />
                                                    <span className="ml-2 text-sm">{t('common.select')}</span>
                                                </button>
                                                <div className="w-px bg-white/10" />
                                                <button
                                                    onClick={() => BrowserOpenURL(`file://${launcherDataDir}`)}
                                                    className="h-12 px-4 bg-[#151515] flex items-center justify-center text-white/60 hover:text-white hover:bg-white/5 transition-colors"
                                                    title={t('common.openFolder')}
                                                >
                                                    <ExternalLink size={18} />
                                                    <span className="ml-2 text-sm">{t('settings.dataSettings.open')}</span>
                                                </button>
                                            </div>
                                        </div>
                                        <p className="mt-2 text-xs text-white/40">{t('settings.dataSettings.changesAfterRestart')}</p>
                                    </div>

                                    {/* Launcher Folder Actions */}
                                    <div className="space-y-3">
                                        <button
                                            onClick={handleOpenLauncherFolder}
                                            className="w-full h-12 px-4 rounded-xl bg-[#151515] border border-white/10 flex items-center gap-3 text-white/70 hover:text-white hover:border-white/20 transition-colors"
                                        >
                                            <FolderOpen size={18} />
                                            <span>{t('settings.dataSettings.openLauncherFolder')}</span>
                                        </button>

                                        <button
                                            onClick={() => setShowDeleteConfirm(true)}
                                            className="w-full h-12 px-4 rounded-xl bg-[#151515] border border-red-500/30 flex items-center gap-3 text-red-400 hover:text-red-300 hover:bg-red-500/10 transition-colors"
                                        >
                                            <Trash2 size={18} />
                                            <span>{t('settings.dataSettings.deleteAllData')}</span>
                                        </button>
                                    </div>
                                </div>
                            )}
                            
                            {/* Instances Tab */}
                            {activeTab === 'instances' && (
                                <div 
                                    className="space-y-4 relative"
                                    onDragOver={(e) => {
                                        e.preventDefault();
                                        e.stopPropagation();
                                        if (e.dataTransfer.types.includes('Files')) {
                                            setIsDraggingZip(true);
                                        }
                                    }}
                                    onDragLeave={(e) => {
                                        e.preventDefault();
                                        e.stopPropagation();
                                        setIsDraggingZip(false);
                                    }}
                                    onDrop={handleInstanceZipDrop}
                                >
                                    {/* Drag overlay */}
                                    {isDraggingZip && (
                                        <div className="absolute inset-0 z-10 flex items-center justify-center bg-black/60 rounded-xl border-2 border-dashed border-white/40">
                                            <div className="text-center">
                                                <Download size={32} className="mx-auto mb-2 text-white/60" />
                                                <p className="text-white/80">{t('settings.instanceSettings.dropZipImport')}</p>
                                            </div>
                                        </div>
                                    )}
                                    
                                    {/* Export/Import message */}
                                    {exportMessage && (
                                        <div className={`p-3 rounded-xl text-sm flex items-center gap-2 ${
                                            exportMessage.type === 'success' 
                                                ? 'bg-green-500/20 text-green-400 border border-green-500/30' 
                                                : 'bg-red-500/20 text-red-400 border border-red-500/30'
                                        }`}>
                                            {exportMessage.type === 'success' ? '✓' : '✕'} {exportMessage.text}
                                        </div>
                                    )}
                                    
                                    {/* Instance List */}
                                    {isLoadingInstances ? (
                                        <div className="flex items-center justify-center py-12">
                                            <Loader2 size={28} className="animate-spin" style={{ color: accentColor }} />
                                        </div>
                                    ) : installedInstances.length === 0 ? (
                                        <div className="py-12 text-center">
                                            <Box size={40} className="mx-auto mb-3 text-white/20" />
                                            <p className="text-white/40 text-sm">{t('settings.instanceSettings.noInstances')}</p>
                                            <p className="text-white/30 text-xs mt-1">{t('settings.instanceSettings.installHytale')}</p>
                                        </div>
                                    ) : (
                                        <div className="space-y-2">
                                            {installedInstances.map((instance) => {
                                                const key = `${instance.branch}-${instance.version}`;
                                                const isReleaseBranch = instance.branch?.toLowerCase() === 'release';
                                                const isExporting = exportingInstance === key;
                                                
                                                // Display labels
                                                const isLatest = instance.isLatestInstance;
                                                const versionLabel = isLatest ? t('common.latest') : `v${instance.version}`;
                                                
                                                // Format playtime
                                                const playTime = (instance.playTimeSeconds ?? 0) > 0 
                                                    ? instance.playTimeFormatted 
                                                    : t('settings.instanceSettings.notPlayed');
                                                
                                                // Format dates
                                                const createdDate = instance.createdAt 
                                                    ? new Date(instance.createdAt).toLocaleDateString() 
                                                    : '-';
                                                const lastPlayedDate = instance.lastPlayedAt 
                                                    ? new Date(instance.lastPlayedAt).toLocaleDateString() 
                                                    : (instance.updatedAt ? new Date(instance.updatedAt).toLocaleDateString() : t('common.never'));
                                                const updatedDate = instance.updatedAt
                                                    ? new Date(instance.updatedAt).toLocaleDateString()
                                                    : '-';
                                                
                                                return (
                                                    <div 
                                                        key={key} 
                                                        className="p-4 rounded-xl bg-[#151515] border border-white/10 hover:border-white/20 transition-colors"
                                                    >
                                                        {/* Header: Branch Icon + Version + Actions */}
                                                        <div className="flex items-center justify-between mb-3">
                                                            <div className="flex items-center gap-3">
                                                                {/* Branch Icon */}
                                                                <div 
                                                                    className={`w-10 h-10 rounded-lg flex items-center justify-center ${
                                                                        isReleaseBranch 
                                                                            ? 'bg-green-500/15' 
                                                                            : 'bg-yellow-500/15'
                                                                    }`}
                                                                >
                                                                    {isLatest ? (
                                                                        <RefreshCw size={20} className={isReleaseBranch ? 'text-green-400' : 'text-yellow-400'} />
                                                                    ) : (
                                                                        <Pin size={20} className={isReleaseBranch ? 'text-green-400' : 'text-yellow-400'} />
                                                                    )}
                                                                </div>
                                                                <div>
                                                                    <div className="flex items-center gap-2">
                                                                        <span className="text-white font-medium">{versionLabel}</span>
                                                                        <span 
                                                                            className={`px-1.5 py-0.5 text-[10px] font-medium rounded ${
                                                                                isReleaseBranch 
                                                                                    ? 'bg-green-500/15 text-green-400' 
                                                                                    : 'bg-yellow-500/15 text-yellow-400'
                                                                            }`}
                                                                        >
                                                                            {isReleaseBranch ? t('common.release') : t('common.preRelease')}
                                                                        </span>
                                                                    </div>
                                                                    <p className="text-xs text-white/40">v{instance.version}</p>
                                                                </div>
                                                            </div>
                                                            
                                                            {/* Actions */}
                                                            <div className="flex items-center gap-1">
                                                                <button
                                                                    onClick={() => OpenInstanceFolder(instance.branch, isLatest ? 0 : (instance.version ?? 0))}
                                                                    className="p-2 rounded-lg hover:bg-white/10 text-white/40 hover:text-white transition-colors"
                                                                    title={t('common.openFolder')}
                                                                >
                                                                    <FolderOpen size={16} />
                                                                </button>
                                                                <button
                                                                    onClick={async () => {
                                                                        setShowInstanceExportModal(instance);
                                                                        const lastPath = await GetLastExportPath();
                                                                        setInstanceExportPath(lastPath || '');
                                                                    }}
                                                                    disabled={isExporting}
                                                                    className="p-2 rounded-lg hover:bg-white/10 text-white/40 hover:text-white transition-colors"
                                                                    title={t('settings.instanceSettings.exportInstance')}
                                                                >
                                                                    {isExporting ? (
                                                                        <Loader2 size={16} className="animate-spin" />
                                                                    ) : (
                                                                        <Download size={16} />
                                                                    )}
                                                                </button>
                                                                <button
                                                                    onClick={() => setInstanceToDelete(instance)}
                                                                    className="p-2 rounded-lg hover:bg-red-500/15 text-white/40 hover:text-red-400 transition-colors"
                                                                    title={t('settings.instanceSettings.delete')}
                                                                >
                                                                    <Trash2 size={16} />
                                                                </button>
                                                            </div>
                                                        </div>
                                                        
                                                        {/* Stats Grid */}
                                                        <div className="grid grid-cols-5 gap-3">
                                                            <div>
                                                                <p className="text-xs text-white/40 mb-1">{t('settings.instanceSettings.playtime')}</p>
                                                                <p className="text-sm text-white/80">{playTime}</p>
                                                            </div>
                                                            <div>
                                                                <p className="text-xs text-white/40 mb-1">{t('settings.instanceSettings.version')}</p>
                                                                <p className="text-sm text-white/80">v{instance.version}</p>
                                                            </div>
                                                            <div>
                                                                <p className="text-xs text-white/40 mb-1">{t('settings.instanceSettings.created')}</p>
                                                                <p className="text-sm text-white/80">{createdDate}</p>
                                                            </div>
                                                            <div>
                                                                <p className="text-xs text-white/40 mb-1">{t('settings.instanceSettings.updated')}</p>
                                                                <p className="text-sm text-white/80">{updatedDate}</p>
                                                            </div>
                                                            <div>
                                                                <p className="text-xs text-white/40 mb-1">{t('settings.instanceSettings.lastPlayed')}</p>
                                                                <p className="text-sm text-white/80">{lastPlayedDate}</p>
                                                            </div>
                                                        </div>
                                                    </div>
                                                );
                                            })}
                                        </div>
                                    )}
                                    
                                    {/* Refresh hint */}
                                    {installedInstances.length > 0 && (
                                        <button
                                            onClick={loadInstances}
                                            className="w-full text-center text-xs text-white/30 hover:text-white/50 py-2 transition-colors"
                                        >
                                            {t('settings.instanceSettings.clickToRefresh')}
                                        </button>
                                    )}
                                </div>
                            )}

                            {/* About Tab */}
                            {activeTab === 'about' && (
                                <div className="space-y-6">
                                    {/* App Icon and Info - No button styling */}
                                    <div className="flex flex-col items-center py-4">
                                        <img 
                                            src={appIcon} 
                                            alt="HyPrism" 
                                            className="w-20 h-20 mb-3"
                                        />
                                        <h3 className="text-xl font-bold text-white">HyPrism</h3>
                                        <p className="text-sm text-white/50">{t('settings.aboutSettings.unofficial')}</p>
                                    </div>

                                    {/* Action Buttons - White icons like main menu */}
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

                                    {/* Contributors Section - No separator */}
                                    <div className="pt-2">
                                        {isLoadingContributors ? (
                                            <div className="flex justify-center py-4">
                                                <div className="w-6 h-6 border-2 rounded-full animate-spin" style={{ borderColor: `${accentColor}30`, borderTopColor: accentColor }} />
                                            </div>
                                        ) : (
                                            <div className="space-y-4">
                                                {/* Maintainer & Auth Server Creator */}
                                                <div className="flex justify-center gap-4 flex-wrap">
                                                    {maintainer && (
                                                        <button
                                                            onClick={() => BrowserOpenURL(maintainer.html_url)}
                                                            className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors"
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
                                                        className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors"
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
                                                </div>

                                                {/* Description */}
                                                <p className="text-xs text-white/40 text-center">{t('settings.aboutSettings.contributorsDescription')}</p>

                                                {/* Other Contributors - 5 per row, larger avatars */}
                                                {otherContributors.length > 0 && (
                                                    <div className="grid grid-cols-5 gap-3 justify-items-center">
                                                        {otherContributors.map((contributor) => (
                                                            <button
                                                                key={contributor.login}
                                                                onClick={() => BrowserOpenURL(contributor.html_url)}
                                                                className="flex flex-col items-center gap-1.5 p-2 rounded-lg hover:bg-white/5 transition-colors w-full"
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

                                    {/* Disclaimer */}
                                    <div className="p-4 rounded-xl bg-[#151515] border border-white/5">
                                        <p className="text-white/50 text-sm text-center">
                                            {t('settings.aboutSettings.disclaimer')}
                                        </p>
                                    </div>

                                    {/* Replay Introduction Button */}
                                    <button
                                        onClick={async () => {
                                            await ResetOnboarding();
                                            window.location.reload();
                                        }}
                                        className="w-full p-3 rounded-xl bg-white/5 border border-white/10 text-white/70 hover:bg-white/10 hover:text-white transition-all text-sm"
                                    >
                                        {t('settings.aboutSettings.replayIntro')}
                                    </button>
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
                                    <div className="p-4 rounded-xl bg-[#151515] border border-white/5 space-y-4">
                                        <h3 className="text-white font-medium text-sm">{t('settings.developerSettings.onboarding')}</h3>
                                        <button
                                            onClick={async () => {
                                                await ResetOnboarding();
                                                alert(t('settings.developerSettings.introRestart'));
                                            }}
                                            className="w-full p-3 rounded-xl bg-white/5 border border-white/10 text-white/70 hover:bg-white/10 hover:text-white transition-all text-sm"
                                        >
                                            {t('settings.developerSettings.showIntro')}
                                        </button>
                                    </div>

                                    <div className="p-4 rounded-xl bg-[#151515] border border-white/5">
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

            {/* Translation Confirmation Modal */}
            {showTranslationConfirm && (
                <div className="fixed inset-0 z-[250] flex items-center justify-center bg-black/60 backdrop-blur-sm">
                    <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl p-6 max-w-md w-full mx-4">
                        <h3 className="text-lg font-bold text-white mb-3">{t('settings.languageChanged.title')}</h3>
                        <p className="text-white/70 text-sm mb-4">
                            {t('settings.languageChanged.message', { language: showTranslationConfirm.langName })}
                        </p>
                        
                        <label className="flex items-center gap-2 mb-4 cursor-pointer">
                            <input
                                type="checkbox"
                                checked={dontAskAgain}
                                onChange={(e) => setDontAskAgain(e.target.checked)}
                                className="w-4 h-4 rounded"
                                style={{ accentColor: accentColor }}
                            />
                            <span className="text-sm text-white/60">{t('settings.languageChanged.dontAskAgain')}</span>
                        </label>

                        <div className="flex gap-3">
                            <button
                                onClick={handleTranslationDismiss}
                                className="flex-1 h-10 rounded-xl bg-white/5 text-white/70 hover:bg-white/10 transition-colors"
                            >
                                {t('settings.languageChanged.noThanks')}
                            </button>
                            <button
                                onClick={handleTranslationConfirm}
                                className="flex-1 h-10 rounded-xl font-medium transition-colors"
                                style={{ backgroundColor: accentColor, color: accentTextColor }}
                            >
                                {t('settings.languageChanged.searchMods')}
                            </button>
                        </div>
                    </div>
                </div>
            )}

            {/* Delete Confirmation Modal */}
            {showDeleteConfirm && (
                <div className="fixed inset-0 z-[250] flex items-center justify-center bg-black/60 backdrop-blur-sm">
                    <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl p-6 max-w-md w-full mx-4">
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
                <div className="fixed inset-0 z-[250] flex items-center justify-center bg-black/60 backdrop-blur-sm">
                    <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl p-6 max-w-md w-full mx-4">
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
                <div className="fixed inset-0 z-[250] flex items-center justify-center bg-black/80 backdrop-blur-sm">
                    <div className="bg-[#1a1a1a] rounded-2xl border border-white/10 p-6 w-full max-w-md mx-4">
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
                <div className="fixed inset-0 z-[250] flex items-center justify-center bg-black/60 backdrop-blur-sm">
                    <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl p-6 max-w-md w-full mx-4">
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
                                    className="w-full h-10 px-3 rounded-lg bg-[#151515] border border-white/10 text-white text-sm focus:outline-none"
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
                <div className="fixed inset-0 z-[250] flex items-center justify-center bg-black/60 backdrop-blur-sm">
                    <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl max-w-2xl w-full mx-4 max-h-[80vh] flex flex-col overflow-hidden">
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

                            {/* Solid colors */}
                            <div className="p-3 rounded-xl bg-[#0f0f0f] border border-white/5">
                            <p className="text-xs text-white/40 mb-2">{t('settings.visualSettings.solidColors')}</p>
                            <div className="grid grid-cols-6 gap-2">
                                {SOLID_COLORS.map((color) => (
                                    <div
                                        key={color}
                                        className="aspect-video rounded-lg cursor-pointer border-2 transition-all flex items-center justify-center"
                                        style={{
                                            backgroundColor: color,
                                            borderColor: backgroundMode === `color:${color}` ? accentColor : 'transparent',
                                            boxShadow: backgroundMode === `color:${color}` ? `0 0 0 2px ${accentColor}30` : 'none'
                                        }}
                                        onClick={() => handleBackgroundModeChange(`color:${color}`)}
                                    >
                                        {backgroundMode === `color:${color}` && (
                                            <Check size={16} className="text-white drop-shadow-lg" />
                                        )}
                                    </div>
                                ))}
                            </div>
                            </div>
                        </div>
                    </div>
                </div>
            )}
        </>
    );
};
