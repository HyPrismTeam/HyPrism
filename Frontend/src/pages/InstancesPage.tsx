import React, { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { 
  HardDrive, FolderOpen, Trash2, Upload, RefreshCw, 
  Clock, Box, Loader2, AlertTriangle, Check, Plus,
  Search, Package, MoreVertical,
  ChevronRight, Image, Map, Globe, Play, X, Edit2,
  Download, AlertCircle
} from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';

import { ipc, InstalledInstance, invoke, send, SaveInfo, InstanceValidationDetails } from '@/lib/ipc';
import { InlineModBrowser } from '../components/InlineModBrowser';
import { formatBytes } from '../utils/format';
import { GameBranch } from '@/constants/enums';
import { CreateInstanceModal } from '../components/modals/CreateInstanceModal';
import { EditInstanceModal } from '../components/modals/EditInstanceModal';

// IPC calls for instance operations - uses invoke to send to backend
const ExportInstance = async (instanceId: string): Promise<string> => {
  try {
    return await invoke<string>('hyprism:instance:export', { instanceId });
  } catch (e) {
    console.warn('[IPC] ExportInstance:', e);
    return '';
  }
};

const DeleteGame = async (instanceId: string, branch: string, version: number): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:delete', { instanceId, branch, version });
  } catch (e) {
    console.warn('[IPC] DeleteGame:', e);
    return false;
  }
};

const OpenInstanceFolder = (instanceId: string): void => {
  send('hyprism:instance:openFolder', { instanceId });
};

const ImportInstanceFromZip = async (): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:import');
  } catch (e) {
    console.warn('[IPC] ImportInstanceFromZip:', e);
    return false;
  }
};

const GetCustomInstanceDir = async (): Promise<string> => {
  return (await ipc.settings.get()).dataDirectory ?? '';
};

// Mod-related IPC calls
const GetInstanceInstalledMods = async (branch: string, version: number, instanceId?: string): Promise<ModInfo[]> => {
  try {
    return await invoke<ModInfo[]>('hyprism:mods:installed', { branch, version, instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceInstalledMods:', e);
    return [];
  }
};

const UninstallInstanceMod = async (modId: string, branch: string, version: number, instanceId?: string): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:mods:uninstall', { modId, branch, version, instanceId });
  } catch (e) {
    console.warn('[IPC] UninstallInstanceMod:', e);
    return false;
  }
};

const OpenInstanceModsFolder = (instanceId: string): void => {
  send('hyprism:instance:openModsFolder', { instanceId });
};

const CheckInstanceModUpdates = async (branch: string, version: number, instanceId?: string): Promise<ModInfo[]> => {
  try {
    return await invoke<ModInfo[]>('hyprism:mods:checkUpdates', { branch, version, instanceId });
  } catch (e) {
    console.warn('[IPC] CheckInstanceModUpdates:', e);
    return [];
  }
};

// World/Save IPC calls
const GetInstanceSaves = async (instanceId: string, branch: string, version: number): Promise<SaveInfo[]> => {
  try {
    return await invoke<SaveInfo[]>('hyprism:instance:saves', { instanceId, branch, version });
  } catch (e) {
    console.warn('[IPC] GetInstanceSaves:', e);
    return [];
  }
};

const OpenSaveFolder = (instanceId: string, branch: string, version: number, saveName: string): void => {
  send('hyprism:instance:openSaveFolder', { instanceId, branch, version, saveName });
};

const DeleteSaveFolder = async (instanceId: string, branch: string, version: number, saveName: string): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:deleteSave', { instanceId, branch, version, saveName });
  } catch (e) {
    console.warn('[IPC] DeleteSaveFolder:', e);
    return false;
  }
};

// Instance icon IPC calls
const GetInstanceIcon = async (instanceId: string): Promise<string | null> => {
  try {
    return await invoke<string | null>('hyprism:instance:getIcon', { instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceIcon:', e);
    return null;
  }
};

// Types
interface ModInfo {
  id: string;
  name: string;
  slug?: string;
  version: string;
  author: string;
  description: string;
  enabled: boolean;
  iconUrl?: string;
  downloads?: number;
  category?: string;
  categories?: string[];
  curseForgeId?: number;
  fileId?: number;
  releaseType?: number;
  latestVersion?: string;
  latestFileId?: number;
}

// Convert InstalledInstance to InstalledVersionInfo
const toVersionInfo = (inst: InstalledInstance): InstalledVersionInfo => ({
  id: inst.id,
  branch: inst.branch,
  version: inst.version,
  path: inst.path,
  sizeBytes: inst.totalSize,
  isLatest: false,
  isLatestInstance: inst.version === 0,
  iconPath: undefined,
  validationStatus: inst.validationStatus,
  validationDetails: inst.validationDetails,
  customName: inst.customName,
});

export interface InstalledVersionInfo {
  id: string;
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
  iconPath?: string;
  customName?: string;
  validationStatus?: 'Valid' | 'NotInstalled' | 'Corrupted' | 'Unknown';
  validationDetails?: InstanceValidationDetails;
}

const pageVariants = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -12 },
};

// Instance detail tabs
type InstanceTab = 'content' | 'browse' | 'worlds';

interface InstancesPageProps {
  onInstanceDeleted?: () => void;
  onInstanceSelected?: () => void;
  isGameRunning?: boolean;
  runningBranch?: string;
  runningVersion?: number;
  onStopGame?: () => void;
  activeTab?: InstanceTab;
  onTabChange?: (tab: InstanceTab) => void;
  // Download progress
  isDownloading?: boolean;
  downloadingBranch?: string;
  downloadingVersion?: number;
  downloadState?: 'downloading' | 'extracting' | 'launching';
  progress?: number;
  downloaded?: number;
  total?: number;
  launchState?: string;
  launchDetail?: string;
  canCancel?: boolean;
  onCancelDownload?: () => void;
  // Launch callback — routes through App.tsx so download state is tracked
  onLaunchInstance?: (branch: string, version: number, instanceId?: string) => void;
  // Official server blocking
  officialServerBlocked?: boolean;
}

export const InstancesPage: React.FC<InstancesPageProps> = ({ 
  onInstanceDeleted,
  onInstanceSelected,
  isGameRunning = false,
  runningBranch,
  runningVersion,
  onStopGame,
  activeTab: controlledTab,
  onTabChange,
  isDownloading = false,
  downloadingBranch,
  downloadingVersion,
  downloadState: _downloadState = 'downloading',
  progress = 0,
  downloaded = 0,
  total = 0,
  launchState = '',
  launchDetail = '',
  canCancel = false,
  onCancelDownload,
  onLaunchInstance,
  officialServerBlocked = false,
}) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();

  const [instances, setInstances] = useState<InstalledVersionInfo[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [instanceDir, setInstanceDir] = useState('');
  const [instanceToDelete, setInstanceToDelete] = useState<InstalledVersionInfo | null>(null);
  const [exportingInstance, setExportingInstance] = useState<string | null>(null);
  const [isImporting, setIsImporting] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  // Selected instance for detail view
  const [selectedInstance, setSelectedInstance] = useState<InstalledVersionInfo | null>(null);
  // Ref to avoid infinite loop in loadInstances useCallback
  const selectedInstanceRef = useRef<InstalledVersionInfo | null>(null);
  // Tab state — controlled from parent (persists across page navigations) or local fallback
  const [localTab, setLocalTab] = useState<InstanceTab>(controlledTab ?? 'content');
  const activeTab = controlledTab ?? localTab;
  const setActiveTab = useCallback((tab: InstanceTab) => {
    onTabChange?.(tab);
    setLocalTab(tab);
  }, [onTabChange]);

  // Installed mods for selected instance
  const [installedMods, setInstalledMods] = useState<ModInfo[]>([]);
  const [isLoadingMods, setIsLoadingMods] = useState(false);
  const [modsSearchQuery, setModsSearchQuery] = useState('');
  const [selectedMods, setSelectedMods] = useState<Set<string>>(new Set());
  const contentSelectionAnchorRef = useRef<number | null>(null);
  const [modToDelete, setModToDelete] = useState<ModInfo | null>(null);
  const [isDeletingMod, setIsDeletingMod] = useState(false);
  const [editingInstanceName, setEditingInstanceName] = useState(false);
  const [showEditModal, setShowEditModal] = useState(false);
  const [editNameValue, setEditNameValue] = useState('');
  
  // Updates
  const [modsWithUpdates, setModsWithUpdates] = useState<ModInfo[]>([]);
  const [updateCount, setUpdateCount] = useState(0);

  // Saves/Worlds for selected instance
  const [saves, setSaves] = useState<SaveInfo[]>([]);
  const [isLoadingSaves, setIsLoadingSaves] = useState(false);

  // Instance icons cache
  const [instanceIcons, setInstanceIcons] = useState<Record<string, string>>({});
  const iconLoadSeqRef = useRef(0);

  // Instance action menu
  const [showInstanceMenu, setShowInstanceMenu] = useState(false);
  const instanceMenuRef = useRef<HTMLDivElement>(null);
  const [inlineMenuInstanceId, setInlineMenuInstanceId] = useState<string | null>(null);
  const inlineMenuRef = useRef<HTMLDivElement>(null);

  // Create Instance Modal
  const [showCreateModal, setShowCreateModal] = useState(false);

  // Tab slider animation state
  const tabContainerRef = useRef<HTMLDivElement>(null);
  const tabRefs = useRef<Record<string, HTMLButtonElement | null>>({});
  const [sliderStyle, setSliderStyle] = useState<{ left: number; width: number }>({ left: 0, width: 0 });
  const [sliderReady, setSliderReady] = useState(false);
  const prevTabRef = useRef<InstanceTab>(activeTab);

  // Measure and update slider position
  const updateSlider = useCallback(() => {
    const btn = tabRefs.current[activeTab];
    const container = tabContainerRef.current;
    if (btn && container) {
      const containerRect = container.getBoundingClientRect();
      const btnRect = btn.getBoundingClientRect();
      // Only update if we got valid measurements (element is in DOM and visible)
      if (btnRect.width > 0) {
        setSliderStyle({
          left: btnRect.left - containerRect.left,
          width: btnRect.width,
        });
        if (!sliderReady) setSliderReady(true);
      }
    }
  }, [activeTab, sliderReady]);

  useEffect(() => {
    // Use rAF to ensure DOM has been painted before measuring
    const rafId = requestAnimationFrame(() => {
      updateSlider();
    });
    window.addEventListener('resize', updateSlider);
    return () => {
      cancelAnimationFrame(rafId);
      window.removeEventListener('resize', updateSlider);
    };
  }, [updateSlider]);

  // Re-measure when selectedInstance changes (tabs become visible)
  useEffect(() => {
    if (selectedInstance) {
      // Double rAF: first for React commit, second for browser paint
      const id1 = requestAnimationFrame(() => {
        const id2 = requestAnimationFrame(() => {
          updateSlider();
        });
        // Clean up inner rAF on unmount
        return () => cancelAnimationFrame(id2);
      });
      return () => cancelAnimationFrame(id1);
    }
  }, [selectedInstance, updateSlider]);

  // Track previous tab for slider animation
  useEffect(() => {
    prevTabRef.current = activeTab;
  }, [activeTab]);

  // Keep selectedInstanceRef in sync with state
  useEffect(() => {
    selectedInstanceRef.current = selectedInstance;
  }, [selectedInstance]);

  const tabs: InstanceTab[] = ['content', 'browse', 'worlds'];

  const loadInstances = useCallback(async () => {
    setIsLoading(true);
    try {
      const [data, selected] = await Promise.all([
        ipc.game.instances(),
        ipc.instance.getSelected()
      ]);
      const instanceList = (data || []).map(toVersionInfo);
      setInstances(instanceList);
      
      // Try to restore previously selected instance (use ref to avoid infinite loop)
      const currentSelected = selectedInstanceRef.current;
      if (selected && instanceList.length > 0) {
        const found = instanceList.find(inst => inst.id === selected.id);
        if (found) {
          setSelectedInstance(found);
        } else if (!currentSelected) {
          // Fallback to first instance if selected not found
          setSelectedInstance(instanceList[0]);
        }
      } else if (instanceList.length > 0 && !currentSelected) {
        // Auto-select first instance if none selected
        setSelectedInstance(instanceList[0]);
      }
    } catch (err) {
      console.error('Failed to load instances:', err);
    }
    setIsLoading(false);
  }, []);

  useEffect(() => {
    loadInstances();
    GetCustomInstanceDir().then(dir => dir && setInstanceDir(dir)).catch(() => {});
  }, [loadInstances]);

  // Load installed mods when selected instance changes
  const loadInstalledMods = useCallback(async () => {
    if (!selectedInstance) {
      setInstalledMods([]);
      return;
    }
    const currentInstance = selectedInstance;
    
    setIsLoadingMods(true);
    try {
      const mods = await GetInstanceInstalledMods(currentInstance.branch, currentInstance.version, currentInstance.id);
      const normalized = normalizeInstalledMods(mods || []);
      setInstalledMods(normalized);
      setIsLoadingMods(false);
      
      // Check updates in background to keep the list snappy
      void (async () => {
        try {
          const updates = await CheckInstanceModUpdates(currentInstance.branch, currentInstance.version, currentInstance.id);
          const normalizedUpdates = normalizeInstalledMods(updates || []);
          // Apply only if still on same instance
          if (selectedInstanceRef.current?.id === currentInstance.id) {
            setModsWithUpdates(normalizedUpdates);
            setUpdateCount(normalizedUpdates.length);
          }
        } catch {
          if (selectedInstanceRef.current?.id === currentInstance.id) {
            setModsWithUpdates([]);
            setUpdateCount(0);
          }
        }
      })();
    } catch (err) {
      console.error('Failed to load installed mods:', err);
      setInstalledMods([]);
      setModsWithUpdates([]);
      setUpdateCount(0);
      setIsLoadingMods(false);
    }
  }, [selectedInstance]);

  useEffect(() => {
    loadInstalledMods();
  }, [loadInstalledMods]);

  // Load saves when selected instance changes
  const loadSaves = useCallback(async () => {
    if (!selectedInstance) {
      setSaves([]);
      return;
    }
    
    setIsLoadingSaves(true);
    try {
      const savesData = await GetInstanceSaves(selectedInstance.id, selectedInstance.branch, selectedInstance.version);
      setSaves(savesData || []);
    } catch (err) {
      console.error('Failed to load saves:', err);
      setSaves([]);
    }
    setIsLoadingSaves(false);
  }, [selectedInstance]);

  const refreshInstanceIcon = useCallback(async (instanceId: string) => {
    if (!instanceId) return;
    try {
      const icon = await GetInstanceIcon(instanceId);
      setInstanceIcons(prev => {
        const next = { ...prev };
        if (icon) {
          const suffix = icon.includes('?') ? '&' : '?';
          next[instanceId] = `${icon}${suffix}ts=${Date.now()}`;
        } else {
          delete next[instanceId];
        }
        return next;
      });
    } catch (err) {
      console.error('Failed to refresh instance icon:', err);
    }
  }, []);

  const loadAllInstanceIcons = useCallback(async () => {
    const requestSeq = ++iconLoadSeqRef.current;

    if (instances.length === 0) {
      setInstanceIcons({});
      return;
    }

    try {
      const nextIcons: Record<string, string> = {};

      for (const inst of instances) {
        if (requestSeq !== iconLoadSeqRef.current) {
          return;
        }

        if (!inst.id) {
          continue;
        }

        const icon = await GetInstanceIcon(inst.id);
        if (icon) {
          const suffix = icon.includes('?') ? '&' : '?';
          nextIcons[inst.id] = `${icon}${suffix}ts=${Date.now()}`;
        }
      }

      if (requestSeq !== iconLoadSeqRef.current) {
        return;
      }

      setInstanceIcons(nextIcons);
    } catch (err) {
      console.error('Failed to refresh instance icons:', err);
    }
  }, [instances]);

  useEffect(() => {
    if (activeTab === 'worlds') {
      loadSaves();
    }
  }, [loadSaves, activeTab]);

  // Initial/structural icon sync: load icons for current instance list
  useEffect(() => {
    loadAllInstanceIcons();
  }, [loadAllInstanceIcons]);

  // Normalize backend payload casing and defaults
  const normalizeInstalledMods = (mods: unknown[]): ModInfo[] => {
    return (mods || []).map((m: unknown) => {
      const mod = m as Record<string, unknown>;
      const curseForgeId = mod.curseForgeId || mod.CurseForgeId || (typeof mod.id === 'string' && (mod.id as string).startsWith('cf-') ? (mod.id as string).replace('cf-', '') : mod.id);
      return {
        id: mod.id as string,
        name: mod.name as string || mod.Name as string || '',
        slug: mod.slug as string,
        version: mod.version as string || mod.Version as string || '',
        author: mod.author as string || mod.Author as string || '',
        description: mod.description as string || mod.Description as string || mod.summary as string || '',
        enabled: mod.enabled as boolean ?? true,
        iconUrl: mod.iconUrl as string || mod.IconUrl as string || mod.iconURL as string || '',
        curseForgeId: curseForgeId as number,
        fileId: mod.fileId as number || mod.FileId as number,
        latestVersion: mod.latestVersion as string || mod.LatestVersion as string,
        latestFileId: mod.latestFileId as number || mod.LatestFileId as number,
      } as ModInfo;
    });
  };

  // Filter mods by search query
  const filteredMods = useMemo(() => {
    if (!modsSearchQuery.trim()) return installedMods;
    const query = modsSearchQuery.toLowerCase();
    return installedMods.filter(mod =>
      mod.name.toLowerCase().includes(query) ||
      mod.author?.toLowerCase().includes(query)
    );
  }, [installedMods, modsSearchQuery]);

  const toggleContentModSelection = useCallback((modId: string, index: number) => {
    setSelectedMods((prev) => {
      const next = new Set(prev);
      if (next.has(modId)) {
        next.delete(modId);
      } else {
        next.add(modId);
      }
      return next;
    });
    contentSelectionAnchorRef.current = index;
  }, []);

  const handleContentShiftLeftClick = useCallback((e: React.MouseEvent, index: number) => {
    if (!e.shiftKey) {
      return;
    }

    e.preventDefault();

    if (filteredMods.length === 0) {
      return;
    }

    const anchor = contentSelectionAnchorRef.current ?? index;
    const start = Math.min(anchor, index);
    const end = Math.max(anchor, index);
    const ids = filteredMods.slice(start, end + 1).map((mod) => mod.id);

    setSelectedMods(new Set(ids));
  }, [filteredMods]);

  const getCurseForgeUrl = useCallback((mod: ModInfo): string => {
    if (mod.slug) {
      return `https://www.curseforge.com/hytale/mods/${mod.slug}`;
    }
    const id = mod.curseForgeId || (typeof mod.id === 'string' && mod.id.startsWith('cf-') ? mod.id.replace('cf-', '') : mod.id);
    return `https://www.curseforge.com/hytale/mods/search?search=${encodeURIComponent(String(id || mod.name))}`;
  }, []);

  const getTabLabel = useCallback((tab: InstanceTab) => {
    if (tab === 'content') return t('instances.tab.content');
    return t(`instances.tab.${tab}`);
  }, [t]);

  const handleOpenModPage = useCallback((e: React.MouseEvent, mod: ModInfo) => {
    e.preventDefault();
    e.stopPropagation();
    ipc.browser.open(getCurseForgeUrl(mod));
  }, [getCurseForgeUrl]);

  const handleDeleteSave = useCallback(async (e: React.MouseEvent, saveName: string) => {
    e.preventDefault();
    e.stopPropagation();
    if (!selectedInstance) return;

    const ok = await DeleteSaveFolder(selectedInstance.id, selectedInstance.branch, selectedInstance.version, saveName);
    if (ok) {
      setMessage({ type: 'success', text: 'World deleted' });
      await loadSaves();
    } else {
      setMessage({ type: 'error', text: 'Failed to delete world' });
    }
    setTimeout(() => setMessage(null), 3000);
  }, [selectedInstance, loadSaves, t]);

  const handleExport = async (inst: InstalledVersionInfo) => {
    setExportingInstance(inst.id);
    try {
      const result = await ExportInstance(inst.id);
      if (result) {
        setMessage({ type: 'success', text: t('instances.exportedSuccess') });
      } else {
        // Empty result means user cancelled - don't show error
        // setMessage({ type: 'error', text: t('instances.exportFailed') });
      }
    } catch {
      setMessage({ type: 'error', text: t('instances.exportFailed') });
    }
    setExportingInstance(null);
    setTimeout(() => setMessage(null), 3000);
  };

  const handleDelete = async (inst: InstalledVersionInfo) => {
    try {
      await DeleteGame(inst.id, inst.branch, inst.version);
      setInstanceToDelete(null);
      if (selectedInstance?.id === inst.id) {
        setSelectedInstance(null);
      }
      loadInstances();
      onInstanceDeleted?.();
      setMessage({ type: 'success', text: t('instances.deleted') });
      setTimeout(() => setMessage(null), 3000);
    } catch {
      setMessage({ type: 'error', text: t('instances.deleteFailed') });
    }
  };

  const handleImport = async () => {
    setIsImporting(true);
    try {
      const result = await ImportInstanceFromZip();
      if (result) {
        setMessage({ type: 'success', text: t('instances.importedSuccess') });
        loadInstances();
      }
    } catch {
      setMessage({ type: 'error', text: t('instances.importFailed') });
    }
    setIsImporting(false);
    setTimeout(() => setMessage(null), 3000);
  };

  const handleOpenFolder = (inst: InstalledVersionInfo) => {
    OpenInstanceFolder(inst.id);
  };

  const handleOpenModsFolder = () => {
    if (selectedInstance) {
      OpenInstanceModsFolder(selectedInstance.id);
    }
  };

  const handleOpenModsFolderFor = (inst: InstalledVersionInfo) => {
    OpenInstanceModsFolder(inst.id);
  };

  // Launch an instance
  const handleLaunchInstance = (inst: InstalledVersionInfo) => {
    const runningIdentityKnown = !!runningBranch && runningVersion !== undefined;
    const isLikelyRunningThis = isGameRunning && (!runningIdentityKnown || (runningBranch === inst.branch && runningVersion === inst.version));

    // If this instance is currently running, stop it instead
    if (isLikelyRunningThis) {
      onStopGame?.();
    } else {
      onLaunchInstance?.(inst.branch, inst.version, inst.id);
    }
  };

  const handleRenameInstance = async (inst: InstalledVersionInfo, customName: string | null) => {
    try {
      const result = await invoke<boolean>('hyprism:instance:rename', { 
        instanceId: inst.id, 
        customName: customName || null 
      });
      if (result) {
        await loadInstances();
        if (selectedInstance?.id === inst.id) {
          setSelectedInstance(prev => prev ? { ...prev, customName: customName || undefined } : null);
        }
        setEditingInstanceName(false);
      }
    } catch (e) {
      console.error('Failed to rename instance:', e);
    }
  };

  const handleDeleteMod = async (mod: ModInfo) => {
    if (!selectedInstance) return;
    setIsDeletingMod(true);
    try {
      await UninstallInstanceMod(mod.id, selectedInstance.branch, selectedInstance.version, selectedInstance.id);
      setModToDelete(null);
      await loadInstalledMods();
      setMessage({ type: 'success', text: t('modManager.modDeleted') });
      setTimeout(() => setMessage(null), 3000);
    } catch {
      setMessage({ type: 'error', text: t('modManager.deleteFailed') });
    }
    setIsDeletingMod(false);
  };

  const handleBulkDeleteMods = async () => {
    if (!selectedInstance || selectedMods.size === 0) return;
    setIsDeletingMod(true);
    try {
      for (const modId of selectedMods) {
        await UninstallInstanceMod(modId, selectedInstance.branch, selectedInstance.version, selectedInstance.id);
      }
      setSelectedMods(new Set());
      await loadInstalledMods();
      setMessage({ type: 'success', text: t('modManager.modsDeleted') });
      setTimeout(() => setMessage(null), 3000);
    } catch {
      setMessage({ type: 'error', text: t('modManager.deleteFailed') });
    }
    setIsDeletingMod(false);
  };

  const getInstanceDisplayName = (inst: InstalledVersionInfo) => {
    // Use custom name if set
    if (inst.customName) {
      return inst.customName;
    }
    
    const branchLabel = inst.branch === GameBranch.RELEASE
      ? t('modManager.releaseType.release')
      : inst.branch === GameBranch.PRE_RELEASE
        ? t('common.preRelease')
        : t('modManager.releaseType.release');
    
    if (inst.isLatestInstance) {
      return `${branchLabel} (${t('common.latest')})`;
    }
    return `${branchLabel} v${inst.version}`;
  };

  // Get validation status info for display
  const getValidationInfo = (inst: InstalledVersionInfo): { 
    status: 'valid' | 'warning' | 'error'; 
    label: string; 
    color: string;
    bgColor: string;
    icon: React.ReactNode;
  } => {
    const status = inst.validationStatus || 'Unknown';
    
    switch (status) {
      case 'Valid':
        return {
          status: 'valid',
          label: t('instances.status.ready'),
          color: '#22c55e',
          bgColor: 'rgba(34, 197, 94, 0.1)',
          icon: <Check size={12} />
        };
      case 'NotInstalled':
        return {
          status: 'error',
          label: t('instances.status.notInstalled'),
          color: '#ef4444',
          bgColor: 'rgba(239, 68, 68, 0.1)',
          icon: <AlertCircle size={12} />
        };
      case 'Corrupted':
        return {
          status: 'error',
          label: t('instances.status.corrupted'),
          color: '#ef4444',
          bgColor: 'rgba(239, 68, 68, 0.1)',
          icon: <AlertTriangle size={12} />
        };
      default:
        return {
          status: 'warning',
          label: t('instances.status.unknown'),
          color: '#6b7280',
          bgColor: 'rgba(107, 114, 128, 0.1)',
          icon: <AlertCircle size={12} />
        };
    }
  };

  const getInstanceIcon = (inst: InstalledVersionInfo, size: number = 18) => {
    const key = inst.id;
    const customIcon = instanceIcons[key];
    
    if (customIcon) {
      return (
        <img 
          src={customIcon} 
          alt="" 
          className="w-full h-full object-cover rounded-lg"
          onError={() => {
            setInstanceIcons(prev => {
              const next = { ...prev };
              delete next[key];
              return next;
            });
          }}
        />
      );
    }
    
    // Show version number for all instances
    const versionLabel = inst.isLatestInstance ? '★' : `v${inst.version}`;
    return <span className="font-bold" style={{ color: accentColor, fontSize: size * 0.8 }}>{versionLabel}</span>;
  };

  // Close instance menu on click outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (instanceMenuRef.current && !instanceMenuRef.current.contains(e.target as Node)) {
        setShowInstanceMenu(false);
      }
      if (inlineMenuRef.current && !inlineMenuRef.current.contains(e.target as Node)) {
        setInlineMenuInstanceId(null);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full flex px-4 pt-6 pb-28 gap-4"
    >
      {/* Left Sidebar - Instances List (macOS Tahoe style) */}
      <div className="w-72 flex-shrink-0 flex flex-col">
        {/* Sidebar Header */}
        <div className="flex items-center justify-between mb-3 px-3">
          <div className="flex items-center gap-2">
            <HardDrive size={18} className="text-white/70" />
            <h2 className="text-sm font-semibold text-white">{t('instances.title')}</h2>
          </div>
          <div className="flex items-center gap-1">
            <button
              onClick={() => setShowCreateModal(true)}
              className="p-1.5 rounded-lg text-white/50 hover:text-white hover:bg-white/10 transition-all"
              title={t('instances.addInstance')}
            >
              <Plus size={14} />
            </button>
            <button
              onClick={handleImport}
              disabled={isImporting}
              className="p-1.5 rounded-lg text-white/50 hover:text-white hover:bg-white/10 transition-all"
              title={t('instances.import')}
            >
              {isImporting ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
            </button>
            <button
              onClick={loadInstances}
              disabled={isLoading}
              className="p-1.5 rounded-lg text-white/50 hover:text-white hover:bg-white/10 transition-all"
              title={t('common.refresh')}
            >
              <RefreshCw size={14} className={isLoading ? 'animate-spin' : ''} />
            </button>
          </div>
        </div>

        {/* Instance List & Storage Info - Unified glass panel */}
        <div className={`flex-1 flex flex-col overflow-hidden rounded-2xl glass-panel-static-solid min-h-0`}>
          <div className="flex-1 overflow-y-auto">
          <div className="p-2 space-y-1">
          {isLoading ? (
            <div className="flex items-center justify-center py-8">
              <Loader2 size={24} className="animate-spin" style={{ color: accentColor }} />
            </div>
          ) : instances.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-8 text-white/40">
              <Box size={32} className="mb-2 opacity-50" />
              <p className="text-xs text-center mb-3">{t('instances.noInstances')}</p>
              <button
                onClick={() => setShowCreateModal(true)}
                className="flex items-center gap-2 px-3 py-2 rounded-lg bg-white/10 hover:bg-white/15 text-white/80 hover:text-white text-xs transition-all"
              >
                <Plus size={14} />
                {t('instances.addInstance')}
              </button>
            </div>
          ) : (
            instances.map((inst) => {
              const key = inst.id;
              const isSelected = selectedInstance?.id === inst.id;
              const validation = getValidationInfo(inst);
              
              return (
                <div key={key} className="relative">
                <button
                  onClick={() => {
                    setSelectedInstance(inst);
                    setInlineMenuInstanceId(null);
                    // Save selected instance to config
                    if (inst.id) {
                      ipc.instance.select({ id: inst.id }).then(() => {
                        onInstanceSelected?.();
                      }).catch(console.error);
                    }
                  }}
                  onContextMenu={(e) => {
                    e.preventDefault();
                    setSelectedInstance(inst);
                    if (inst.id) {
                      ipc.instance.select({ id: inst.id }).catch(console.error);
                    }
                    setInlineMenuInstanceId(inst.id);
                    setShowInstanceMenu(false);
                  }}
                  className={`w-full p-3 rounded-xl flex items-center gap-3 text-left transition-all duration-150 ${
                    isSelected 
                      ? 'shadow-md' 
                      : 'hover:bg-white/[0.04]'
                  }`}
                  style={isSelected ? { 
                    backgroundColor: `${accentColor}18`,
                    boxShadow: `0 0 0 1px ${accentColor}40`
                  } : undefined}
                >
                  {/* Instance Icon */}
                  <div 
                    className="w-11 h-11 rounded-xl flex items-center justify-center flex-shrink-0 border border-white/[0.08]"
                    style={{ backgroundColor: isSelected ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}
                  >
                    {getInstanceIcon(inst)}
                  </div>
                  
                  {/* Instance Info */}
                  <div className="flex-1 min-w-0">
                    <p 
                      className="text-white text-sm font-medium leading-tight overflow-hidden whitespace-nowrap"
                      title={getInstanceDisplayName(inst)}
                      style={{
                        maskImage: 'linear-gradient(to right, black 85%, transparent 100%)',
                        WebkitMaskImage: 'linear-gradient(to right, black 85%, transparent 100%)'
                      }}
                    >
                      {getInstanceDisplayName(inst)}
                    </p>
                    <div className="flex items-center gap-2 mt-0.5">
                      <span className="text-white/40 text-xs">
                        {inst.sizeBytes ? formatBytes(inst.sizeBytes) : t('common.unknown')}
                      </span>
                      {/* Validation Status Badge */}
                      <span 
                        className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded text-[10px] font-medium"
                        style={{ backgroundColor: validation.bgColor, color: validation.color }}
                        title={inst.validationDetails?.errorMessage || validation.label}
                      >
                        {validation.icon}
                        {validation.status !== 'valid' && validation.label}
                      </span>
                    </div>
                  </div>

                  {/* Selection indicator */}
                  {isSelected && (
                    <ChevronRight size={14} style={{ color: accentColor }} />
                  )}
                </button>
                {inlineMenuInstanceId === inst.id && (
                  <div
                    ref={inlineMenuRef}
                    className="absolute left-2 right-2 top-full mt-1 bg-[#1c1c1e] border border-white/[0.08] rounded-xl shadow-xl overflow-hidden z-40"
                  >
                    <button
                      onClick={() => {
                        setSelectedInstance(inst);
                        setShowEditModal(true);
                        setInlineMenuInstanceId(null);
                      }}
                      className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                    >
                      <Edit2 size={14} />
                      {t('common.edit')}
                    </button>
                    <button
                      onClick={() => {
                        handleOpenFolder(inst);
                        setInlineMenuInstanceId(null);
                      }}
                      className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                    >
                      <FolderOpen size={14} />
                      {t('common.openFolder')}
                    </button>
                    <button
                      onClick={() => {
                        handleOpenModsFolderFor(inst);
                        setInlineMenuInstanceId(null);
                      }}
                      className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                    >
                      <Package size={14} />
                      {t('modManager.openModsFolder')}
                    </button>
                    <button
                      onClick={() => {
                        handleExport(inst);
                        setInlineMenuInstanceId(null);
                      }}
                      disabled={exportingInstance !== null}
                      className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                    >
                      {exportingInstance === inst.id ? (
                        <Loader2 size={14} className="animate-spin" />
                      ) : (
                        <Upload size={14} />
                      )}
                      {t('common.export')}
                    </button>
                    <div className="border-t border-white/10 my-1" />
                    <button
                      onClick={() => {
                        setInstanceToDelete(inst);
                        setInlineMenuInstanceId(null);
                      }}
                      className="w-full px-4 py-2.5 text-sm text-left text-red-400 hover:text-red-300 hover:bg-red-500/10 flex items-center gap-2"
                    >
                      <Trash2 size={14} />
                      {t('common.delete')}
                    </button>
                  </div>
                )}
                </div>
              );
            })
          )}
          </div>
          </div>

          {/* Storage Info */}
          {instanceDir && (
            <div className="px-3 py-2 border-t border-white/[0.06] text-xs text-white/40 truncate flex-shrink-0">
              {instanceDir}
            </div>
          )}
        </div>
      </div>

      {/* Main Content - Instance Detail */}
      <div className="flex-1 flex flex-col min-w-0">
        {selectedInstance ? (
          <>
            {/* Unified instance detail panel */}
            <div className={`flex-1 flex flex-col overflow-hidden rounded-2xl glass-panel-static-solid`}>
            {/* Tabs & Actions */}
            <div className="flex items-center justify-between gap-4 px-3 py-3 flex-shrink-0 border-b border-white/[0.06]">
              {/* Left side: Tabs */}
              <div className="flex items-center gap-3">
                {/* Tabs with sliding indicator */}
                <div
                  ref={tabContainerRef}
                  className="relative flex items-center gap-1 px-1.5 py-1.5 bg-[#1c1c1e] rounded-2xl border border-white/10"
                >
                  {/* Sliding indicator */}
                  <div
                    className="absolute rounded-xl"
                    style={{
                      background: `linear-gradient(135deg, ${accentColor}, ${accentColor}cc)`,
                      left: sliderStyle.left,
                      width: sliderStyle.width,
                      top: '0.3rem',
                      bottom: '0.3rem',
                      transition: sliderReady
                        ? 'left 0.3s cubic-bezier(0.4, 0, 0.2, 1), width 0.25s cubic-bezier(0.4, 0, 0.2, 1)'
                        : 'none',
                      opacity: sliderReady ? 1 : 0,
                    }}
                  />
                  {tabs.map((tab) => {
                  const isTabDisabled = tab === 'browse' && selectedInstance.validationStatus !== 'Valid';
                  return (
                  <button
                    key={tab}
                    ref={(el) => { tabRefs.current[tab] = el; }}
                    onClick={() => !isTabDisabled && setActiveTab(tab)}
                    disabled={isTabDisabled}
                    className={`relative z-10 px-5 py-2 rounded-xl text-sm font-bold transition-colors duration-200 whitespace-nowrap ${
                      activeTab === tab
                        ? 'text-white'
                        : isTabDisabled
                          ? 'text-white/25 cursor-not-allowed'
                          : 'text-white/80 hover:text-white'
                    }`}
                    style={activeTab === tab ? { color: accentTextColor } : undefined}
                    title={isTabDisabled ? t('instances.instanceNotInstalled') : undefined}
                  >
                    {getTabLabel(tab)}
                  </button>
                  );
                })}
                </div>
              </div>

              {/* Right side: Action Buttons */}
              <div className="flex items-center gap-2">
                {/* Full state-aware Play/Stop/Download button */}
                {(() => {
                  const runningIdentityKnown = !!runningBranch && runningVersion !== undefined;
                  const isThisRunning = isGameRunning && (!runningIdentityKnown || (runningBranch === selectedInstance.branch && runningVersion === selectedInstance.version));
                  const isThisDownloading = isDownloading && downloadingBranch === selectedInstance.branch && downloadingVersion === selectedInstance.version;
                  const isInstalled = selectedInstance.validationStatus === 'Valid';

                  // Game running on THIS instance → Stop
                  if (isThisRunning) {
                    return (
                      <button
                        onClick={() => handleLaunchInstance(selectedInstance)}
                        className="px-4 py-2 rounded-xl text-sm font-bold flex items-center gap-2 transition-all hover:opacity-90 shadow-lg bg-gradient-to-r from-red-600 to-red-500 text-white"
                      >
                        <X size={16} />
                        {t('main.stop')}
                      </button>
                    );
                  }

                  // Downloading THIS instance
                  if (isThisDownloading) {
                    const stateKey = `launch.state.${launchState}`;
                    const stateLabel = t(stateKey) !== stateKey ? t(stateKey) : (launchState || t('launch.state.preparing'));
                    return (
                      <div
                        className={`px-4 py-2 flex items-center justify-center relative overflow-hidden rounded-xl min-w-[140px] ${canCancel ? 'cursor-pointer' : 'cursor-default'}`}
                        style={{ background: 'rgba(255,255,255,0.05)' }}
                        onClick={() => canCancel && onCancelDownload?.()}
                      >
                        <div
                          className="absolute inset-0 transition-all duration-300"
                          style={{ width: `${Math.min(progress, 100)}%`, backgroundColor: `${accentColor}40` }}
                        />
                        <div className="relative z-10 flex items-center gap-2">
                          <Loader2 size={14} className="animate-spin text-white" />
                          <span className="text-sm font-bold text-white">{stateLabel}</span>
                          {canCancel && (
                            <span className="ml-1 text-xs text-red-400 hover:text-red-300">
                              <X size={12} className="inline" />
                            </span>
                          )}
                        </div>
                      </div>
                    );
                  }

                  // Game running on ANOTHER instance → disabled
                  if (isGameRunning && runningIdentityKnown) {
                    return (
                      <button
                        disabled
                        className="px-4 py-2 rounded-xl text-sm font-bold flex items-center gap-2 transition-all opacity-50 cursor-not-allowed"
                        style={{ backgroundColor: '#555', color: accentTextColor }}
                      >
                        <Play size={16} fill="currentColor" />
                        {t('main.play')}
                      </button>
                    );
                  }

                  // Not installed → Download (disabled if another download in progress)
                  if (!isInstalled) {
                    const anotherDownloading = isDownloading && !isThisDownloading;
                    return (
                      <button
                        onClick={() => !anotherDownloading && handleLaunchInstance(selectedInstance)}
                        disabled={anotherDownloading}
                        className={`px-4 py-2 rounded-xl text-sm font-bold flex items-center gap-2 transition-all shadow-lg bg-gradient-to-r from-green-500 to-emerald-600 text-white ${
                          anotherDownloading ? 'opacity-50 cursor-not-allowed' : 'hover:brightness-110 active:scale-[0.98]'
                        }`}
                      >
                        <Download size={16} />
                        {t('main.download')}
                      </button>
                    );
                  }

                  // Installed → Play (or blocked if unofficial profile on official servers)
                  if (officialServerBlocked) {
                    return (
                      <div className="relative group">
                        <button
                          disabled
                          className="px-4 py-2 rounded-xl text-sm font-bold flex items-center gap-2 transition-all opacity-50 cursor-not-allowed"
                          style={{ backgroundColor: '#555', color: accentTextColor }}
                        >
                          <Play size={16} fill="currentColor" />
                          {t('main.play')}
                        </button>
                        <div className="absolute bottom-full left-1/2 -translate-x-1/2 mb-2 px-3 py-2 bg-black/90 text-white text-xs rounded-lg opacity-0 group-hover:opacity-100 transition-opacity whitespace-nowrap pointer-events-none z-50">
                          {t('main.officialServerBlocked')}
                        </div>
                      </div>
                    );
                  }

                  return (
                    <button
                      onClick={() => handleLaunchInstance(selectedInstance)}
                      className="px-4 py-2 rounded-xl text-sm font-bold flex items-center gap-2 transition-all hover:brightness-110 active:scale-[0.98] shadow-lg"
                      style={{ backgroundColor: accentColor, color: accentTextColor }}
                    >
                      <Play size={16} fill="currentColor" />
                      {t('main.play')}
                    </button>
                  );
                })()}

                {/* Settings Menu */}
                <div className="relative" ref={instanceMenuRef}>
                  <button
                    onClick={() => setShowInstanceMenu(!showInstanceMenu)}
                    className="p-2 rounded-xl bg-white/10 hover:bg-white/20 text-white/60 hover:text-white transition-all"
                  >
                    <MoreVertical size={18} />
                  </button>

                  {showInstanceMenu && (
                    <div className="absolute right-0 top-full mt-2 w-48 bg-[#1c1c1e] border border-white/[0.08] rounded-xl shadow-xl z-50 overflow-hidden">
                      <button
                        onClick={() => {
                          setShowEditModal(true);
                          setShowInstanceMenu(false);
                        }}
                        className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                      >
                        <Edit2 size={14} />
                        {t('common.edit')}
                      </button>
                      <button
                        onClick={() => {
                          handleOpenFolder(selectedInstance);
                          setShowInstanceMenu(false);
                        }}
                        className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                      >
                        <FolderOpen size={14} />
                        {t('common.openFolder')}
                      </button>
                      <button
                        onClick={() => {
                          handleOpenModsFolder();
                          setShowInstanceMenu(false);
                        }}
                        className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                      >
                        <Package size={14} />
                        {t('modManager.openModsFolder')}
                      </button>
                      <button
                        onClick={() => {
                          handleExport(selectedInstance);
                          setShowInstanceMenu(false);
                        }}
                        disabled={exportingInstance !== null}
                        className="w-full px-4 py-2.5 text-sm text-left text-white/70 hover:text-white hover:bg-white/10 flex items-center gap-2"
                      >
                        {exportingInstance === selectedInstance.id ? (
                          <Loader2 size={14} className="animate-spin" />
                        ) : (
                          <Upload size={14} />
                        )}
                        {t('common.export')}
                      </button>
                      <div className="border-t border-white/10 my-1" />
                      <button
                        onClick={() => {
                          setInstanceToDelete(selectedInstance);
                          setShowInstanceMenu(false);
                        }}
                        className="w-full px-4 py-2.5 text-sm text-left text-red-400 hover:text-red-300 hover:bg-red-500/10 flex items-center gap-2"
                      >
                        <Trash2 size={14} />
                        {t('common.delete')}
                      </button>
                    </div>
                  )}
                </div>
              </div>
            </div>

            {/* Download Progress Counter - only show when selected instance is downloading */}
            <AnimatePresence>
              {isDownloading && downloadingBranch === selectedInstance?.branch && downloadingVersion === selectedInstance?.version && launchState !== 'complete' && (
                <motion.div
                  initial={{ opacity: 0, height: 0 }}
                  animate={{ opacity: 1, height: 'auto' }}
                  exit={{ opacity: 0, height: 0 }}
                  transition={{ duration: 0.2 }}
                  className="px-4 py-2 border-b border-white/[0.06] flex-shrink-0"
                >
                  <div
                    className={`rounded-xl px-3 py-2 border border-white/[0.06] ${canCancel ? 'cursor-pointer' : ''}`}
                    style={{ background: 'rgba(28,28,30,0.98)' }}
                    onClick={() => canCancel && onCancelDownload?.()}
                  >
                    <div className="h-1.5 w-full bg-[#1c1c1e] rounded-full overflow-hidden mb-1.5">
                      <div
                        className="h-full rounded-full transition-all duration-300"
                        style={{ width: `${Math.min(progress, 100)}%`, backgroundColor: accentColor }}
                      />
                    </div>
                    <div className="flex justify-between items-center text-[10px]">
                      <span className="text-white/60 truncate max-w-[280px]">
                        {launchDetail
                          ? (t(launchDetail) !== launchDetail
                            ? t(launchDetail).replace('{0}', `${Math.min(Math.round(progress), 100)}`)
                            : launchDetail)
                          : (() => { const k = `launch.state.${launchState}`; const v = t(k); return v !== k ? v : (launchState || t('launch.state.preparing')); })()}
                      </span>
                      <div className="flex items-center gap-2">
                        <span className="text-white/50 font-mono">
                          {total > 0
                            ? `${formatBytes(downloaded)} / ${formatBytes(total)}`
                            : `${Math.min(Math.round(progress), 100)}%`}
                        </span>
                        {canCancel && (
                          <span className="text-red-400 hover:text-red-300 transition-colors text-[9px] font-bold uppercase">
                            <X size={10} className="inline" /> {t('main.cancel')}
                          </span>
                        )}
                      </div>
                    </div>
                  </div>
                </motion.div>
              )}
            </AnimatePresence>

            {/* Tab Content */}
            <div className="flex-1 overflow-hidden relative">
                {/* Content tab */}
                <div
                  className={`absolute inset-0 flex flex-col ${
                    activeTab === 'content' ? 'opacity-100 z-10' : 'opacity-0 z-0 pointer-events-none'
                  }`}
                >
                  {selectedInstance.validationStatus === 'Valid' && (
                    <>
                  {/* Content Header */}
                  <div className="p-4 border-b border-white/[0.06] flex items-center gap-3">
                    {/* Search */}
                    <div className="relative flex-1 max-w-md">
                      <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-white/40" />
                      <input
                        type="text"
                        value={modsSearchQuery}
                        onChange={(e) => setModsSearchQuery(e.target.value)}
                        placeholder={t('modManager.searchMods')}
                        className="w-full h-10 pl-10 pr-4 rounded-xl bg-[#2c2c2e] border border-white/[0.08] text-white text-sm placeholder-white/40 focus:outline-none focus:border-white/20"
                      />
                    </div>

                    {/* Actions */}
                    <div className="flex items-center gap-2 ml-auto">
                      {updateCount > 0 && (
                        <span className="px-2.5 py-1 rounded-lg text-xs font-medium bg-green-500/15 text-green-400 border border-green-500/20">
                          {updateCount} {t('modManager.updatesAvailableShort')}
                        </span>
                      )}
                      <button
                        onClick={loadInstalledMods}
                        disabled={isLoadingMods}
                        className="p-2 rounded-xl text-white/50 hover:text-white hover:bg-white/[0.06] transition-all"
                        title={t('common.refresh')}
                      >
                        <RefreshCw size={16} className={isLoadingMods ? 'animate-spin' : ''} />
                      </button>
                      {selectedMods.size > 0 && (
                        <button
                          onClick={handleBulkDeleteMods}
                          disabled={isDeletingMod}
                          className="px-3 py-2 rounded-xl text-sm font-medium bg-red-500/15 text-red-400 hover:bg-red-500/20 border border-red-500/20 flex items-center gap-2 transition-all"
                        >
                          <Trash2 size={14} />
                          {t('modManager.deleteSelected')} ({selectedMods.size})
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Mods List Header */}
                  <div className="px-4 py-2.5 border-b border-white/[0.06] flex items-center text-xs text-white/50 font-medium uppercase tracking-wide">
                    <div className="w-8" />
                    <div className="flex-1 pl-3">{t('modManager.name')}</div>
                    <div className="w-32 text-center">{t('modManager.version')}</div>
                    <div className="w-20 text-center">{t('modManager.enabled')}</div>
                    <div className="w-20" />
                  </div>
                    </>
                  )}

                  {/* Mods List */}
                  <div className="flex-1 overflow-y-auto">
                    {selectedInstance.validationStatus !== 'Valid' ? (
                      <div className="flex flex-col items-center justify-center h-full text-white/40">
                        <Download size={48} className="mb-4 opacity-40" />
                        <p className="text-lg font-medium text-white/60">{t('instances.instanceNotInstalled')}</p>
                        <p className="text-sm mt-1">{t('instances.instanceNotInstalledHint')}</p>
                      </div>
                    ) : isLoadingMods ? (
                      <div className="flex items-center justify-center h-full">
                        <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
                      </div>
                    ) : filteredMods.length === 0 ? (
                      <div className="flex flex-col items-center justify-center h-full text-white/40">
                        <Package size={48} className="mb-4 opacity-40" />
                        <p className="text-lg font-medium text-white/60">{t('modManager.noModsInstalled')}</p>
                        <p className="text-sm mt-1">{t('modManager.clickInstallContent')}</p>
                        <button
                          onClick={() => onTabChange?.('browse')}
                          className="mt-4 px-4 py-2 rounded-xl text-sm font-medium flex items-center gap-2 shadow-lg"
                          style={{ backgroundColor: accentColor, color: accentTextColor }}
                        >
                          <Plus size={16} />
                          {t('instances.installContent')}
                        </button>
                      </div>
                    ) : (
                      <div className="divide-y divide-white/5">
                        {filteredMods.map((mod, index) => {
                          const hasUpdate = modsWithUpdates.some(u => u.id === mod.id);
                          const isSelected = selectedMods.has(mod.id);

                          return (
                            <div
                              key={mod.id}
                              onClick={(e) => {
                                if (e.shiftKey) {
                                  handleContentShiftLeftClick(e, index);
                                  return;
                                }
                                contentSelectionAnchorRef.current = index;
                              }}
                              className={`px-4 py-3 flex items-center gap-3 hover:bg-white/5 transition-colors ${
                                isSelected ? 'bg-white/5' : ''
                              }`}
                            >
                              {/* Selection Checkbox */}
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  if (e.shiftKey) {
                                    handleContentShiftLeftClick(e, index);
                                    return;
                                  }
                                  toggleContentModSelection(mod.id, index);
                                }}
                                className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${
                                  isSelected ? '' : 'bg-transparent border-white/30 hover:border-white/50'
                                }`}
                                style={isSelected ? { backgroundColor: accentColor, borderColor: accentColor } : undefined}
                              >
                                {isSelected && <Check size={12} style={{ color: accentTextColor }} />}
                              </button>

                              {/* Mod Icon */}
                              <div className="w-10 h-10 rounded-lg bg-[#1c1c1e] flex items-center justify-center overflow-hidden flex-shrink-0">
                                {mod.iconUrl ? (
                                  <img src={mod.iconUrl} alt="" className="w-full h-full object-cover" />
                                ) : (
                                  <Package size={18} className="text-white/40" />
                                )}
                              </div>

                              {/* Mod Info */}
                              <div className="flex-1 min-w-0">
                                <div className="flex items-center gap-2">
                                  <button
                                    onClick={(e) => handleOpenModPage(e, mod)}
                                    className="text-white font-medium truncate hover:underline underline-offset-2 text-left"
                                    title="Open CurseForge page"
                                  >
                                    {mod.name}
                                  </button>
                                  {hasUpdate && (
                                    <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/20 text-green-400">
                                      {t('modManager.updateBadge')}
                                    </span>
                                  )}
                                </div>
                                <p className="text-white/40 text-xs truncate">
                                  {mod.author || t('modManager.unknownAuthor')}
                                </p>
                              </div>

                              {/* Version */}
                              <div className="w-32 text-center flex items-center justify-center gap-1.5">
                                <span className="text-white/60 text-sm truncate">{mod.version || '-'}</span>
                                {mod.releaseType && mod.releaseType !== 1 && (
                                  <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium flex-shrink-0 ${
                                    mod.releaseType === 2 ? 'bg-yellow-500/20 text-yellow-400'
                                      : 'bg-red-500/20 text-red-400'
                                  }`}>
                                    {mod.releaseType === 2 ? 'β' : 'α'}
                                  </span>
                                )}
                              </div>

                              {/* Toggle */}
                              <div className="w-20 flex items-center justify-center">
                                <button
                                  className="w-11 h-6 rounded-full p-0.5 transition-colors"
                                  style={{ backgroundColor: mod.enabled ? accentColor : 'rgba(255,255,255,0.18)' }}
                                  onClick={async () => {
                                    if (!selectedInstance) return;
                                    try {
                                      const ok = await ipc.mods.toggle({
                                        modId: mod.id,
                                        instanceId: selectedInstance.id,
                                        branch: selectedInstance.branch,
                                        version: selectedInstance.version,
                                      });
                                      if (ok) {
                                        setInstalledMods(prev =>
                                          prev.map(m => m.id === mod.id ? { ...m, enabled: !m.enabled } : m)
                                        );
                                      }
                                    } catch (e) {
                                      console.warn('[IPC] ToggleMod:', e);
                                    }
                                  }}
                                >
                                  <motion.div
                                    className="w-5 h-5 rounded-full shadow-md"
                                    style={{ backgroundColor: mod.enabled ? accentTextColor : 'white' }}
                                    animate={{ x: mod.enabled ? 20 : 0 }}
                                    transition={{ type: 'spring', stiffness: 500, damping: 30 }}
                                  />
                                </button>
                              </div>

                              {/* Actions */}
                              <div className="w-20 flex items-center justify-end gap-1">
                                <button
                                  onClick={() => setModToDelete(mod)}
                                  className="p-1.5 rounded-lg text-white/30 hover:text-red-400 hover:bg-red-500/10 transition-all"
                                  title={t('common.delete')}
                                >
                                  <Trash2 size={14} />
                                </button>
                              </div>
                            </div>
                          );
                        })}
                      </div>
                    )}
                  </div>

                  {/* Pagination / Footer */}
                  {filteredMods.length > 0 && (
                    <div className="p-4 border-t border-white/10 flex items-center justify-between text-sm text-white/50">
                      <span>{filteredMods.length} {t('modManager.modsInstalled')}</span>
                    </div>
                  )}
                </div>

                {/* Browse tab — always mounted so downloads survive tab switches */}
                <div
                  className={`absolute inset-0 ${
                    activeTab === 'browse' ? 'opacity-100 z-10' : 'opacity-0 z-0 pointer-events-none'
                  }`}
                >
                  {selectedInstance && (
                    <InlineModBrowser
                      currentInstanceId={selectedInstance.id}
                      currentBranch={selectedInstance.branch}
                      currentVersion={selectedInstance.version}
                      installedModIds={new Set(installedMods.map(m => m.curseForgeId ? `cf-${m.curseForgeId}` : m.id))}
                      installedFileIds={new Set(installedMods.filter(m => m.fileId).map(m => String(m.fileId)))}
                      onModsInstalled={() => loadInstalledMods()}
                      onBack={() => setActiveTab('content')}
                    />
                  )}
                </div>

                {/* Worlds tab */}
                <div
                  className={`absolute inset-0 flex flex-col ${
                    activeTab === 'worlds' ? 'opacity-100 z-10' : 'opacity-0 z-0 pointer-events-none'
                  }`}
                >
                  {/* Saves Header */}
                  <div className="p-4 border-b border-white/10 flex items-center justify-between">
                    <h3 className="text-white font-medium flex items-center gap-2">
                      <Globe size={18} />
                      {t('instances.saves')}
                    </h3>
                    <div className="flex items-center gap-2">
                      <button
                        onClick={loadSaves}
                        disabled={isLoadingSaves}
                        className="p-2 rounded-xl text-white/50 hover:text-white hover:bg-white/10 transition-all"
                        title={t('common.refresh')}
                      >
                        <RefreshCw size={16} className={isLoadingSaves ? 'animate-spin' : ''} />
                      </button>
                    </div>
                  </div>

                  {/* Saves List */}
                  <div className="flex-1 overflow-y-auto p-4">
                    {isLoadingSaves ? (
                      <div className="flex items-center justify-center py-12">
                        <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
                      </div>
                    ) : saves.length === 0 ? (
                      <div className="flex flex-col items-center justify-center py-12 text-white/30">
                        <Map size={48} className="mb-4 opacity-50" />
                        <p className="text-lg font-medium">{t('instances.noSaves')}</p>
                        <p className="text-sm mt-1">{t('instances.noSavesHint')}</p>
                      </div>
                    ) : (
                      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
                        {saves.map((save) => (
                          <div
                            key={save.name}
                            onClick={() => OpenSaveFolder(selectedInstance!.id, selectedInstance!.branch, selectedInstance!.version, save.name)}
                            className="group relative rounded-xl overflow-hidden border border-white/10 hover:border-white/20 transition-all bg-white/5 hover:bg-white/10 cursor-pointer"
                          >
                            {/* Preview Image */}
                            <div className="aspect-video w-full bg-black/40 flex items-center justify-center overflow-hidden">
                              {save.previewPath ? (
                                <img
                                  src={save.previewPath}
                                  alt={save.name}
                                  className="w-full h-full object-cover group-hover:scale-105 transition-transform duration-300"
                                  onError={(e) => {
                                    (e.target as HTMLImageElement).style.display = 'none';
                                    (e.target as HTMLImageElement).nextElementSibling?.classList.remove('hidden');
                                  }}
                                />
                              ) : null}
                              <div className={`flex items-center justify-center ${save.previewPath ? 'hidden' : ''}`}>
                                <Image size={32} className="text-white/20" />
                              </div>
                            </div>

                            {/* Save Info */}
                            <div className="p-3">
                              <p className="text-white font-medium text-sm truncate">{save.name}</p>
                              <div className="flex items-center justify-between mt-1 text-xs text-white/40">
                                {save.lastModified && (
                                  <span className="flex items-center gap-1">
                                    <Clock size={10} />
                                    {new Date(save.lastModified).toLocaleDateString()}
                                  </span>
                                )}
                                {save.sizeBytes && (
                                  <span>{formatBytes(save.sizeBytes)}</span>
                                )}
                              </div>
                            </div>

                            {/* Hover Overlay */}
                            <div className="absolute inset-0 bg-black/55 opacity-0 group-hover:opacity-100 transition-opacity flex flex-col items-center justify-center gap-3">
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  OpenSaveFolder(selectedInstance!.id, selectedInstance!.branch, selectedInstance!.version, save.name);
                                }}
                                className="px-6 py-3 rounded-xl bg-white/20 hover:bg-white/30 text-white text-sm font-semibold flex items-center justify-center gap-2 min-w-[200px]"
                              >
                                <FolderOpen size={18} />
                                {t('common.openFolder')}
                              </button>
                              <button
                                onClick={(e) => handleDeleteSave(e, save.name)}
                                className="px-6 py-3 rounded-xl bg-red-500/30 hover:bg-red-500/40 text-red-100 text-sm font-semibold flex items-center justify-center gap-2 min-w-[200px]"
                              >
                                <Trash2 size={18} />
                                {t('common.delete')}
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </div>
            </div>
            </div>

            {/* Edit Instance Modal */}
            <EditInstanceModal
              isOpen={showEditModal}
              onClose={() => setShowEditModal(false)}
              onSave={() => {
                loadInstances();
              }}
              instanceId={selectedInstance.id}
              initialName={selectedInstance.customName || getInstanceDisplayName(selectedInstance)}
              initialIconUrl={instanceIcons[selectedInstance.id]}
            />
          </>
        ) : instances.length === 0 ? (
          /* No Instances Available - Prompt to Create */
          <div className={`flex-1 flex flex-col items-center justify-center rounded-2xl glass-panel-static-solid`}>
            <Box size={64} className="mb-4 text-white/20" />
            <p className="text-xl font-medium text-white/70">{t('instances.noInstances')}</p>
            <p className="text-sm mt-2 text-white/40 text-center max-w-xs">{t('instances.createInstanceHint')}</p>
            <button
              onClick={() => setShowCreateModal(true)}
              className="mt-6 px-6 py-3 rounded-xl text-sm font-bold flex items-center gap-2 transition-all hover:opacity-90 shadow-lg"
              style={{ backgroundColor: accentColor, color: accentTextColor }}
            >
              <Plus size={18} />
              {t('instances.createInstance')}
            </button>
          </div>
        ) : (
          /* No Instance Selected */
          <div className="flex-1 flex flex-col items-center justify-center text-white/30">
            <HardDrive size={64} className="mb-4 opacity-30" />
            <p className="text-xl font-medium">{t('instances.selectInstance')}</p>
            <p className="text-sm mt-2">{t('instances.selectInstanceHint')}</p>
          </div>
        )}
      </div>

      {/* Message Toast */}
      <AnimatePresence>
        {message && (
          <motion.div
            initial={{ opacity: 0, y: 50 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: 50 }}
            className={`fixed bottom-32 left-1/2 -translate-x-1/2 px-4 py-2 rounded-xl text-sm flex items-center gap-2 z-50 ${
              message.type === 'success' 
                ? 'bg-green-500/20 text-green-400 border border-green-500/20' 
                : 'bg-red-500/20 text-red-400 border border-red-500/20'
            }`}
          >
            {message.type === 'success' ? <Check size={14} /> : <AlertTriangle size={14} />}
            {message.text}
          </motion.div>
        )}
      </AnimatePresence>

      {/* Delete Instance Confirmation */}
      <AnimatePresence>
        {instanceToDelete && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className={`fixed inset-0 z-[300] flex items-center justify-center bg-[#0a0a0a]/90`}
            onClick={(e) => e.target === e.currentTarget && setInstanceToDelete(null)}
          >
            <motion.div
              initial={{ scale: 0.95, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.95, opacity: 0 }}
              className={`p-6 max-w-sm mx-4 shadow-2xl glass-panel-static-solid`}
            >
              <h3 className="text-white font-bold text-lg mb-2">{t('instances.deleteTitle')}</h3>
              <p className="text-white/60 text-sm mb-4">
                {t('instances.deleteConfirm')} <strong>{getInstanceDisplayName(instanceToDelete)}</strong>?
              </p>
              <div className="flex gap-2 justify-end">
                <button onClick={() => setInstanceToDelete(null)}
                  className="px-4 py-2 rounded-xl text-sm text-white/60 hover:text-white hover:bg-white/10 transition-all">
                  {t('common.cancel')}
                </button>
                <button onClick={() => handleDelete(instanceToDelete)}
                  className="px-4 py-2 rounded-xl text-sm font-medium bg-red-500/20 text-red-400 hover:bg-red-500/30 transition-all">
                  {t('common.delete')}
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Delete Mod Confirmation */}
      <AnimatePresence>
        {modToDelete && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className={`fixed inset-0 z-[300] flex items-center justify-center bg-[#0a0a0a]/90`}
            onClick={(e) => e.target === e.currentTarget && setModToDelete(null)}
          >
            <motion.div
              initial={{ scale: 0.95, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.95, opacity: 0 }}
              className={`p-6 max-w-sm mx-4 shadow-2xl glass-panel-static-solid`}
            >
              <h3 className="text-white font-bold text-lg mb-2">{t('modManager.deleteModTitle')}</h3>
              <p className="text-white/60 text-sm mb-4">
                {t('modManager.deleteModConfirm')} <strong>{modToDelete.name}</strong>?
              </p>
              <div className="flex gap-2 justify-end">
                <button onClick={() => setModToDelete(null)}
                  className="px-4 py-2 rounded-xl text-sm text-white/60 hover:text-white hover:bg-white/10 transition-all">
                  {t('common.cancel')}
                </button>
                <button 
                  onClick={() => handleDeleteMod(modToDelete)}
                  disabled={isDeletingMod}
                  className="px-4 py-2 rounded-xl text-sm font-medium bg-red-500/20 text-red-400 hover:bg-red-500/30 transition-all flex items-center gap-2">
                  {isDeletingMod && <Loader2 size={14} className="animate-spin" />}
                  {t('common.delete')}
                </button>
              </div>
            </motion.div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Create Instance Modal */}
      <CreateInstanceModal
        isOpen={showCreateModal}
        onClose={() => setShowCreateModal(false)}
        onCreateStart={() => {
          // Refresh instances after creation starts
          loadInstances();
        }}
      />
    </motion.div>
  );
};
