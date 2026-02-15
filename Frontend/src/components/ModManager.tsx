import React, { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import { createPortal } from 'react-dom';
import {
  X, Search, Download, Trash2, FolderOpen,
  Package, Loader2, AlertCircle,
  RefreshCw, Check, ChevronDown, ChevronLeft, ChevronRight, ArrowUpCircle, FileText,
  Upload, FilePlus2, ExternalLink
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { ipc } from '@/lib/ipc';

// Alias for compatibility
const BrowserOpenURL = (url: string) => ipc.browser.open(url);

// TODO: These need dedicated IPC channels in IpcService.cs
const _stub = <T,>(name: string, fb: T) => async (..._a: any[]): Promise<T> => { console.warn(`[IPC] ${name}: no channel`); return fb; };
const SearchMods = _stub<any[]>('SearchMods', []);
const GetModFiles = _stub<any[]>('GetModFiles', []);
const GetModCategories = _stub<any[]>('GetModCategories', []);
const GetInstanceInstalledMods = _stub<any[]>('GetInstanceInstalledMods', []);
const InstallModFileToInstance = _stub('InstallModFileToInstance', false);
const UninstallInstanceMod = _stub('UninstallInstanceMod', false);
const OpenInstanceModsFolder = _stub('OpenInstanceModsFolder', undefined as void);
const CheckInstanceModUpdates = _stub<any[]>('CheckInstanceModUpdates', []);
const InstallLocalModFile = _stub('InstallLocalModFile', false);
const ExportModsToFolder = _stub('ExportModsToFolder', '');
const GetLastExportPath = _stub('GetLastExportPath', '');
const ImportModList = _stub('ImportModList', 0);
const BrowseFolder = _stub('BrowseFolder', '');
const BrowseModFiles = _stub<string[]>('BrowseModFiles', []);
const InstallModFromBase64 = _stub('InstallModFromBase64', false);
const GetShowAlphaMods = _stub('GetShowAlphaMods', false);
import { GameBranch } from '@/constants/enums';
import { useAccentColor } from '../contexts/AccentColorContext';

// Types
interface Mod {
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
  screenshots?: { id: number; title: string; thumbnailUrl: string; url: string }[];
  latestVersion?: string;
  latestFileId?: number;
}

interface CurseForgeMod {
  id: number | string;
  name: string;
  slug: string;
  summary: string;
  downloadCount: number;
  logo?: { thumbnailUrl: string; url: string };
  iconUrl?: string;
  thumbnailUrl?: string;
  author?: string;
  screenshots?: { id: number; title: string; thumbnailUrl: string; url: string }[];
  authors?: { name: string }[];
  categories: (string | { id: number; name: string })[];
  dateModified?: string;
  dateReleased?: string;
  dateUpdated?: string;
  latestFiles?: ModFile[];
  latestFileId?: string;
}

interface ModFile {
  id: number | string;
  modId: number | string;
  displayName: string;
  fileName: string;
  fileLength: number;
  downloadUrl: string;
  fileDate: string;
  releaseType: number;
}

interface ModCategory {
  id: number;
  name: string;
}

interface ModManagerProps {
  onClose: () => void;
  currentBranch: string;
  currentVersion: number;
  initialSearchQuery?: string;
  currentProfileName?: string;
  pageMode?: boolean;
  headerActionsRef?: React.RefObject<HTMLDivElement | null>;
}

type DownloadJobStatus = {
  id: string | number;
  name: string;
  status: 'pending' | 'running' | 'success' | 'error';
  attempts: number;
  error?: string;
};

// Confirmation Modal
const ConfirmModal: React.FC<{
  title: string;
  message: string;
  confirmText: string;
  confirmColor: string;
  confirmStyle?: React.CSSProperties;
  confirmTextColor?: string;
  onConfirm: () => void;
  onCancel: () => void;
  children?: React.ReactNode;
}> = ({ title, message, confirmText, confirmColor, confirmStyle, confirmTextColor, onConfirm, onCancel, children }) => {
  const { t } = useTranslation();
  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-[#0a0a0a]/90">
      <div className="p-6 max-w-md w-full mx-4 glass-panel-static-solid">
        <h3 className="text-lg font-bold text-white mb-2">{title}</h3>
        <p className="text-white/60 text-sm mb-4">{message}</p>
        {children}
        <div className="flex gap-3 mt-4">
          <button
            onClick={onCancel}
            className="flex-1 py-2 rounded-xl bg-white/10 text-white text-sm hover:bg-white/20"
          >
            {t('common.cancel')}
          </button>
          <button onClick={onConfirm} className={`flex-1 py-2 rounded-xl text-sm ${confirmColor}`} style={{ ...confirmStyle, color: confirmTextColor }}>
            {confirmText}
          </button>
        </div>
      </div>
    </div>
  );
};

const formatDownloads = (count: number): string => {
  if (count >= 1000000) return `${(count / 1000000).toFixed(1)}M`;
  if (count >= 1000) return `${(count / 1000).toFixed(1)}K`;
  return count.toString();
};

const getReleaseTypeLabel = (type: number, t: (key: string) => string) => {
  switch (type) {
    case 1: return t('modManager.releaseType.release');
    case 2: return t('modManager.releaseType.beta');
    case 3: return t('modManager.releaseType.alpha');
    default: return t('modManager.releaseType.unknown');
  }
};

export const ModManager: React.FC<ModManagerProps> = ({
  onClose,
  currentBranch,
  currentVersion,
  initialSearchQuery = '',
  currentProfileName,
  pageMode: isPageMode = false,
  headerActionsRef
}) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();
  // Tab state
  const [activeTab, setActiveTab] = useState<'installed' | 'browse'>(initialSearchQuery ? 'browse' : 'installed');

  // Installed mods
  const [installedMods, setInstalledMods] = useState<Mod[]>([]);
  const [isLoadingInstalled, setIsLoadingInstalled] = useState(false);
  const [selectedInstalledMods, setSelectedInstalledMods] = useState<Set<string>>(new Set());
  const [highlightedInstalledMods, setHighlightedInstalledMods] = useState<Set<string>>(new Set());
  const [installedSearchQuery, setInstalledSearchQuery] = useState('');

  // Updates
  const [modsWithUpdates, setModsWithUpdates] = useState<Mod[]>([]);
  const [isLoadingUpdates, setIsLoadingUpdates] = useState(false);
  const [showUpdatesModal, setShowUpdatesModal] = useState(false);
  const [updateCount, setUpdateCount] = useState(0);

  // Browse mods
  const [searchQuery, setSearchQuery] = useState(initialSearchQuery);
  const [searchResults, setSearchResults] = useState<CurseForgeMod[]>([]);
  const [categories, setCategories] = useState<ModCategory[]>([]);
  const [selectedCategory, setSelectedCategory] = useState(0);
  const [selectedSortField, setSelectedSortField] = useState(6); // Default: TotalDownloads
  const [isSearching, setIsSearching] = useState(false);
  const [currentPage, setCurrentPage] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [isCategoryDropdownOpen, setIsCategoryDropdownOpen] = useState(false);
  const [isSortDropdownOpen, setIsSortDropdownOpen] = useState(false);
  const [selectedBrowseMods, setSelectedBrowseMods] = useState<Set<number | string>>(new Set());
  const [highlightedBrowseMods, setHighlightedBrowseMods] = useState<Set<number | string>>(new Set());

  // Mod files cache and selection
  const [modFilesCache, setModFilesCache] = useState<Map<string, ModFile[]>>(new Map());
  const [, setLoadingModFiles] = useState<Set<string>>(new Set());
  const [selectedVersions, setSelectedVersions] = useState<Map<string, number | string>>(new Map());

  const normalizeModKey = (id: number | string | undefined | null) => String(id ?? '');

  // Detail panel - shown in split view
  const [selectedMod, setSelectedMod] = useState<CurseForgeMod | Mod | null>(null);
  const [selectedModFiles, setSelectedModFiles] = useState<ModFile[]>([]);
  const [isLoadingModFiles, setIsLoadingModFilesState] = useState(false);
  const [detailSelectedFileId, setDetailSelectedFileId] = useState<number | string | undefined>();
  const [activeScreenshot, setActiveScreenshot] = useState(0);
  const [fullscreenImage, setFullscreenImage] = useState<{ url: string; title: string } | null>(null);

  // Actions
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState<{ current: number; total: number; currentMod: string } | null>(null);
  const [downloadJobs, setDownloadJobs] = useState<DownloadJobStatus[]>([]);
  const [error, setError] = useState<string | null>(null);
  
  // Drag and drop
  const [isDragging, setIsDragging] = useState(false);
  const [isImporting, setIsImporting] = useState(false);
  const [importProgress, setImportProgress] = useState<string | null>(null);

  // Confirmation modals
  const [confirmModal, setConfirmModal] = useState<{
    type: 'download' | 'delete';
    items: Array<{ id: string | number; name: string; fileId?: number | string }>;
  } | null>(null);

  // Export modal
  const [showExportModal, setShowExportModal] = useState(false);
  const [exportPath, setExportPath] = useState('');
  const [exportType, setExportType] = useState<'modlist' | 'zip'>('modlist');
  const [isExporting, setIsExporting] = useState(false);

  // Multi-select tracking
  const [lastClickedIndex, setLastClickedIndex] = useState<number | null>(null);

  // Alpha mods setting
  const [showAlphaMods, setShowAlphaMods] = useState(false);

  // Refs
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const categoryDropdownRef = useRef<HTMLDivElement>(null);
  const sortDropdownRef = useRef<HTMLDivElement>(null);

  // Sort options for CurseForge API
  const sortOptions = [
    { id: 1, name: t('modManager.sortRelevancy') },
    { id: 2, name: t('modManager.sortPopularity') },
    { id: 3, name: t('modManager.sortLatestUpdate') },
    { id: 11, name: t('modManager.sortCreationDate') },
    { id: 6, name: t('modManager.sortTotalDownloads') },
  ];

  const instanceBranchLabel = currentBranch === GameBranch.RELEASE
    ? t('modManager.releaseType.release')
    : currentBranch === GameBranch.PRE_RELEASE
      ? t('common.preRelease')
      : t('modManager.releaseType.release');
  const instanceName = currentVersion === 0 ? t('common.latest') : `${instanceBranchLabel} v${currentVersion}`;

  // Filter installed mods by search
  const filteredInstalledMods = useMemo(() => {
    if (!installedSearchQuery.trim()) return installedMods;
    const query = installedSearchQuery.toLowerCase();
    return installedMods.filter(mod =>
      mod.name.toLowerCase().includes(query) ||
      mod.author?.toLowerCase().includes(query)
    );
  }, [installedMods, installedSearchQuery]);

  // Normalize backend payload casing and defaults
  const normalizeInstalledMods = (mods: any[]): Mod[] => {
    return (mods || []).map((m: any) => {
      const curseForgeId = m.curseForgeId || m.CurseForgeId || (typeof m.id === 'string' && m.id.startsWith('cf-') ? m.id.replace('cf-', '') : m.id);
      return {
        ...m,
        id: m.id,
        curseForgeId,
        iconUrl: m.iconUrl || m.IconUrl || m.iconURL || '',
        description: m.description || m.Description || m.summary || '',
        screenshots: m.screenshots || m.Screenshots || [],
      } as Mod;
    });
  };

  // Load installed mods
  const loadInstalledMods = useCallback(async () => {
    setIsLoadingInstalled(true);
    try {
      const mods = await GetInstanceInstalledMods(currentBranch, currentVersion);
      const normalized = normalizeInstalledMods(mods || []);
      setInstalledMods(normalized);

      // Seed selected versions from manifest when present so re-download works without reselecting
      setSelectedVersions((prev) => {
        const next = new Map(prev);
        normalized.forEach((m) => {
          const modKey = normalizeModKey(m.curseForgeId || m.id);
          const manifestFileId = (m as any).fileId || (m as any).FileId || m.latestFileId;
          if (modKey && manifestFileId && !next.has(modKey)) {
            next.set(modKey, String(manifestFileId));
          }
        });
        return next;
      });
    } catch (err) {
      console.error('Failed to load installed mods:', err);
      setInstalledMods([]);
    }
    setIsLoadingInstalled(false);
  }, [currentBranch, currentVersion]);

  useEffect(() => {
    loadInstalledMods();
  }, [loadInstalledMods]);

  // Keyboard shortcuts (Ctrl+A / Cmd+A to select all mods in installed tab)
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Check for Ctrl+A (Windows/Linux) or Cmd+A (macOS)
      if ((e.ctrlKey || e.metaKey) && (e.key === 'a' || e.key === 'A')) {
        // Only handle if we're in the installed tab and not in a text input
        if (activeTab === 'installed' && 
            !(e.target instanceof HTMLInputElement) && 
            !(e.target instanceof HTMLTextAreaElement)) {
          e.preventDefault();
          console.log('[MOD MANAGER] Ctrl+A/Cmd+A pressed, selecting all mods');
          // Select all installed mods
          const allModIds = new Set(filteredInstalledMods.map(mod => mod.id));
          setSelectedInstalledMods(allModIds);
          setHighlightedInstalledMods(allModIds);
        }
      }
      // Escape to deselect all
      if (e.key === 'Escape') {
        setSelectedInstalledMods(new Set());
        setSelectedBrowseMods(new Set());
        setHighlightedInstalledMods(new Set());
        setHighlightedBrowseMods(new Set());
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [activeTab, filteredInstalledMods]);

  // Load categories and alpha mods setting on mount and whenever component opens
  useEffect(() => {
    const loadCategories = async () => {
      try {
        const cats = await GetModCategories();
        setCategories(cats || []);
      } catch (err) {
        console.error('Failed to load categories:', err);
      }
    };
    loadCategories();
  }, []);
  
  // Load alpha mods setting - reload every time component is visible
  // This ensures changes from Settings are reflected immediately
  useEffect(() => {
    const loadAlphaModsSetting = async () => {
      try {
        const showAlpha = await GetShowAlphaMods();
        setShowAlphaMods(showAlpha);
        console.log('[ModManager] Alpha mods setting:', showAlpha);
      } catch (err) {
        console.error('Failed to load alpha mods setting:', err);
      }
    };
    loadAlphaModsSetting();
    
    // Also poll for changes while the modal is open (in case user changes settings)
    const interval = setInterval(loadAlphaModsSetting, 2000);
    return () => clearInterval(interval);
  }, []);

  // Close category dropdown on click outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (categoryDropdownRef.current && !categoryDropdownRef.current.contains(e.target as Node)) {
        setIsCategoryDropdownOpen(false);
      }
      if (sortDropdownRef.current && !sortDropdownRef.current.contains(e.target as Node)) {
        setIsSortDropdownOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  // Search mods
  const handleSearch = useCallback(
    async (resetResults = true) => {
      if (resetResults) {
        setIsSearching(true);
        setCurrentPage(0);
      }

      try {
        const pageNum = resetResults ? 0 : currentPage;
        const pageSize = 20;
        // Categories as string array (empty if "all")
        const categoryFilter = selectedCategory === 0 ? [] : [selectedCategory.toString()];
        // sortField: 1=Featured, 2=Popularity, 3=LastUpdated, 6=TotalDownloads, 11=ReleasedDate
        // sortOrder: 2=desc
        const result: any = await SearchMods(searchQuery, pageNum, pageSize, categoryFilter, selectedSortField, 2);
        const mods = result?.Mods ?? result?.mods ?? [];

        if (resetResults) {
          setSearchResults(mods);
        } else {
          setSearchResults((prev) => [...prev, ...mods]);
        }
        setHasMore(mods.length >= pageSize);
      } catch (err: any) {
        setError(err.message || 'Failed to search mods');
        if (resetResults) setSearchResults([]);
      }

      setIsSearching(false);
      setIsLoadingMore(false);
    },
    [searchQuery, selectedCategory, selectedSortField, currentPage]
  );

  // Real-time search with debounce
  useEffect(() => {
    if (activeTab !== 'browse') return;

    if (searchTimeoutRef.current) {
      clearTimeout(searchTimeoutRef.current);
    }

    searchTimeoutRef.current = setTimeout(() => {
      handleSearch(true);
    }, 300);

    return () => {
      if (searchTimeoutRef.current) {
        clearTimeout(searchTimeoutRef.current);
      }
    };
  }, [searchQuery, selectedCategory, selectedSortField, activeTab]);

  // Load more on scroll
  const handleScroll = useCallback(
    (e: React.UIEvent<HTMLDivElement>) => {
      const target = e.target as HTMLDivElement;
      const scrollBottom = target.scrollHeight - target.scrollTop - target.clientHeight;

      if (scrollBottom < 200 && !isLoadingMore && hasMore && activeTab === 'browse') {
        setIsLoadingMore(true);
        setCurrentPage((prev) => prev + 1);
      }
    },
    [isLoadingMore, hasMore, activeTab]
  );

  useEffect(() => {
    if (currentPage > 0 && isLoadingMore) {
      handleSearch(false);
    }
  }, [currentPage, isLoadingMore, handleSearch]);

  // Auto-load mods when switching to browse tab or if we have an initial query
  useEffect(() => {
    if (activeTab === 'browse' && (searchResults.length === 0 || initialSearchQuery)) {
      handleSearch(true);
    }
  }, [activeTab]);

  // Clear cache when showAlphaMods setting changes
  useEffect(() => {
    // Clear cache so files are re-fetched with new filter
    setModFilesCache(new Map());
    setSelectedVersions(new Map());
  }, [showAlphaMods]);

  // Load mod files
  const loadModFiles = async (modId: number | string): Promise<ModFile[]> => {
    const cacheKey = normalizeModKey(modId);
    if (!cacheKey) return [];

    if (modFilesCache.has(cacheKey)) {
      return modFilesCache.get(cacheKey) || [];
    }

    setLoadingModFiles((prev) => new Set(prev).add(cacheKey));
    try {
      const result: any = await GetModFiles(String(modId), 0, 50);
      // Handle both array response and object with Files property
      let files = Array.isArray(result) ? result : (result?.Files ?? result?.files ?? []);
      
      // Filter out alpha mods (releaseType === 3) unless showAlphaMods is enabled
      if (!showAlphaMods) {
        files = files.filter((f: ModFile) => f.releaseType !== 3);
      }
      
      files.sort((a: ModFile, b: ModFile) => new Date(b.fileDate).getTime() - new Date(a.fileDate).getTime());
      setModFilesCache((prev) => new Map(prev).set(cacheKey, files));
      if (files.length > 0 && !selectedVersions.has(cacheKey)) {
        setSelectedVersions((prev) => new Map(prev).set(cacheKey, files[0].id));
      }
      return files;
    } catch (err) {
      console.error('Failed to load mod files:', err);
      return [];
    } finally {
      setLoadingModFiles((prev) => {
        const next = new Set(prev);
        next.delete(cacheKey);
        return next;
      });
    }
  };

  // Handle mod click - show in detail panel (single click)
  // Shift+click highlights mods, double-click toggles selection
  const handleModClick = async (mod: CurseForgeMod | Mod, index: number, e: React.MouseEvent) => {
    // Check if shift key is held for multi-select (range highlight - not checkmark)
    if (e.shiftKey && lastClickedIndex !== null) {
      e.preventDefault();
      const start = Math.min(lastClickedIndex, index);
      const end = Math.max(lastClickedIndex, index);

      if (activeTab === 'installed') {
        const newHighlight = new Set(highlightedInstalledMods);
        for (let i = start; i <= end; i++) {
          if (filteredInstalledMods[i]) newHighlight.add(filteredInstalledMods[i].id);
        }
        setHighlightedInstalledMods(newHighlight);
      } else {
        const newHighlight = new Set(highlightedBrowseMods);
        for (let i = start; i <= end; i++) {
          const m = searchResults[i];
          if (m) {
            newHighlight.add(m.id);
            loadModFiles(m.id);
          }
        }
        setHighlightedBrowseMods(newHighlight);
      }
      setLastClickedIndex(index);
      return; // Don't show in detail panel on shift+click
    } else if (e.detail === 2) {
      // Double click - toggle selection (checkmark)
      if (activeTab === 'installed') {
        const modId = (mod as Mod).id;
        setSelectedInstalledMods((prev) => {
          const next = new Set(prev);
          if (next.has(modId)) {
            next.delete(modId);
          } else {
            next.add(modId);
          }
          return next;
        });
        // Also clear from highlighted
        setHighlightedInstalledMods((prev) => {
          const next = new Set(prev);
          next.delete(modId);
          return next;
        });
      } else {
        const modId = (mod as CurseForgeMod).id;
        setSelectedBrowseMods((prev) => {
          const next = new Set(prev);
          if (next.has(modId)) {
            next.delete(modId);
          } else {
            next.add(modId);
            loadModFiles(modId);
          }
          return next;
        });
        // Also clear from highlighted
        setHighlightedBrowseMods((prev) => {
          const next = new Set(prev);
          next.delete(modId);
          return next;
        });
      }
      setLastClickedIndex(index);
    } else {
      // Single click - update last clicked index and clear highlights
      setLastClickedIndex(index);
      if (activeTab === 'installed') {
        setHighlightedInstalledMods(new Set());
      } else {
        setHighlightedBrowseMods(new Set());
      }
    }

    // Show mod in detail panel
    setSelectedMod(mod);
    setActiveScreenshot(0);
    setIsLoadingModFilesState(true);

    const modId = 'curseForgeId' in mod && mod.curseForgeId
      ? mod.curseForgeId
      : 'id' in mod
        ? (mod as any).id
        : undefined;
    const modKey = normalizeModKey(modId);
    if (modId) {
      const files = await loadModFiles(modId);
      const selectedFileId = selectedVersions.get(modKey) || files[0]?.id;
      setSelectedModFiles(files);
      if (selectedFileId) {
        setDetailSelectedFileId(selectedFileId);
        setSelectedVersions((prev) => new Map(prev).set(modKey, selectedFileId));
      } else {
        setDetailSelectedFileId(undefined);
      }
    } else {
      setSelectedModFiles([]);
      setDetailSelectedFileId(undefined);
    }
    setIsLoadingModFilesState(false);
  };

  // Is mod installed check
  const isModInstalled = (cfModId: number | string) => {
    return installedMods.some((m) => m.id === `cf-${cfModId}`);
  };

  // Show download confirmation
  const showDownloadConfirmation = async () => {
    let items: Array<{ id: string | number; name: string; fileId?: number | string }> = [];

    if (activeTab === 'browse' && selectedBrowseMods.size > 0) {
      const selected = Array.from(selectedBrowseMods);
      console.log(`[ModManager] Preparing download for ${selected.length} selected mods`);

      const resolved: Array<{ id: string | number; name: string; fileId?: string | number }> = [];
      for (const modId of selected) {
        const mod = searchResults.find((m) => m.id === modId);
        const modKey = normalizeModKey(modId);
        let fileId = selectedVersions.get(modKey);

        if (!fileId) {
          for (let attempt = 1; attempt <= 3; attempt++) {
            console.log(`[ModManager] Loading files for mod ${modId} (${mod?.name}) attempt ${attempt}/3`);
            const files = await loadModFiles(modId);
            fileId = files?.[0]?.id;
            if (fileId) break;
            await new Promise((resolve) => setTimeout(resolve, 250 * attempt));
          }
        }

        if (!fileId) {
          console.warn(`[ModManager] No file found for mod ${modId} (${mod?.name}), backend will resolve latest file`);
        }

        resolved.push({ id: modId, name: mod?.name || `Mod ${modId}`, fileId });
      }

      items = resolved;
      const withFile = items.filter((i) => !!i.fileId).length;
      console.log(`[ModManager] ${withFile}/${selected.length} mods resolved with explicit fileIds`);
    } else if (activeTab === 'installed' && selectedInstalledMods.size > 0) {
      const installedItems = Array.from(selectedInstalledMods)
        .map((modId) => {
          const mod = installedMods.find((m) => m.id === modId);
          if (!mod?.curseForgeId) return null;
          const modKey = normalizeModKey(mod.curseForgeId);
          const fallbackFileId = (mod as any).fileId || (mod as any).FileId || mod.latestFileId;
          const fileId = selectedVersions.get(modKey) || fallbackFileId;
          if (!fileId) return null;
          return { id: mod.curseForgeId, name: mod?.name || 'Unknown', fileId } as { id: string | number; name: string; fileId?: string | number };
        })
        .filter((item): item is { id: string | number; name: string; fileId?: string | number } => item !== null);
      items = installedItems;
    }

    if (items.length > 0) {
      setConfirmModal({ type: 'download', items });
    } else {
      setError(t('modManager.noDownloadableFiles'));
    }
  };

  // Show delete confirmation
  const showDeleteConfirmation = () => {
    // Combine selected and highlighted mods for deletion
    const allModsToDelete = new Set([...selectedInstalledMods, ...highlightedInstalledMods]);
    if (allModsToDelete.size === 0) return;

    const items = Array.from(allModsToDelete).map((modId) => {
      const mod = installedMods.find((m) => m.id === modId);
      return { id: modId, name: mod?.name || 'Unknown' };
    });

    setConfirmModal({ type: 'delete', items });
  };

  const runDownloadQueue = async (items: Array<{ id: string | number; name: string; fileId?: string | number }>) => {
    // Reduce concurrency to 1 to avoid race conditions and rate limiting
    const maxConcurrency = 1;
    const maxRetries = 3;

    setIsDownloading(true);
    setDownloadProgress({ current: 0, total: items.length, currentMod: '' });
    setDownloadJobs(items.map((item) => ({ id: item.id, name: item.name, status: 'pending', attempts: 0 })));

    let completed = 0;
    let successCount = 0;
    const queue = [...items];

    const runJob = async (item: { id: string | number; name: string; fileId?: string | number }): Promise<boolean> => {
      for (let attempt = 1; attempt <= maxRetries; attempt++) {
        setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: 'running', attempts: attempt } : j));
        try {
          console.log(`[ModManager] Downloading ${item.name} (attempt ${attempt}/${maxRetries})`);
          const ok = await InstallModFileToInstance(String(item.id), String(item.fileId ?? ''), currentBranch, currentVersion);
          if (!ok) {
            throw new Error(t('modManager.backendRefused'));
          }
          console.log(`[ModManager] Successfully installed ${item.name}`);
          setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: 'success', attempts: attempt } : j));
          return true;
        } catch (err: any) {
          console.error(`[ModManager] Failed to install ${item.name} (attempt ${attempt}):`, err?.message);
          const isLast = attempt === maxRetries;
          setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: isLast ? 'error' : 'pending', attempts: attempt, error: err?.message } : j));
          if (isLast) {
            return false;
          }
          // Wait before retry to avoid rate limiting
          await new Promise(resolve => setTimeout(resolve, 500 * attempt));
        }
      }
      return false;
    };

    const worker = async () => {
      while (true) {
        const next = queue.shift();
        if (!next) break;
        try {
          const success = await runJob(next);
          if (success) successCount++;
        } finally {
          completed += 1;
          setDownloadProgress({ current: completed, total: items.length, currentMod: next.name });
        }
      }
    };

    await Promise.all(Array.from({ length: Math.min(maxConcurrency, items.length) }, () => worker()));
    
    console.log(`[ModManager] Download complete: ${successCount}/${items.length} mods installed successfully`);
  };

  // Handle confirm download
  const handleConfirmDownload = async () => {
    if (!confirmModal || confirmModal.type !== 'download') return;

    const items = confirmModal.items;
    console.log(`[ModManager] Starting download of ${items.length} mods (original: ${confirmModal.items.length})`);
    const missingFileIds = items.filter((item) => !item.fileId).length;
    if (missingFileIds > 0) {
      console.warn(`[ModManager] ${missingFileIds} mods do not have explicit fileId, backend will resolve latest file`);
    }
    setConfirmModal(null);
    const errors: string[] = [];

    try {
      // No need to check if game is installed - mods can be downloaded anytime
      await runDownloadQueue(items as Array<{ id: string | number; name: string; fileId?: string | number }>);
    } catch (err: any) {
      errors.push(err?.message || 'Failed to download one or more mods');
    }

    setIsDownloading(false);
    setDownloadProgress(null);
    setDownloadJobs([]);
    setSelectedBrowseMods(new Set());
    setSelectedInstalledMods(new Set());
    await loadInstalledMods();

    if (errors.length > 0) {
      setError(errors.join('\n'));
    }
  };

  // Handle confirm delete
  const handleConfirmDelete = async () => {
    if (!confirmModal || confirmModal.type !== 'delete') return;

    setConfirmModal(null);

    for (const item of confirmModal.items) {
      try {
        await UninstallInstanceMod(item.id as string, currentBranch, currentVersion);
      } catch (err) {
        console.error('Failed to uninstall:', err);
      }
    }

    setSelectedInstalledMods(new Set());
    setSelectedMod(null);
    await loadInstalledMods();
  };

  // Check for updates (silently or show modal)
  const checkForUpdates = useCallback(async (showModal = false) => {
    setIsLoadingUpdates(true);
    try {
      const updates = await CheckInstanceModUpdates(currentBranch, currentVersion);
      // Normalize response - backend may use PascalCase or camelCase
      const normalizedUpdates = (updates || []).map((mod: any) => ({
        ...mod,
        id: mod.id || mod.Id || '',
        name: mod.name || mod.Name || '',
        curseForgeId: mod.curseForgeId || mod.CurseForgeId,
        latestFileId: mod.latestFileId || mod.LatestFileId,
        latestVersion: mod.latestVersion || mod.LatestVersion,
      } as Mod));
      setModsWithUpdates(normalizedUpdates);
      setUpdateCount(normalizedUpdates.length);
      if (showModal) {
        setShowUpdatesModal(true);
      }
    } catch (err) {
      console.error('Failed to check for updates:', err);
      setModsWithUpdates([]);
      setUpdateCount(0);
      if (showModal) {
        setShowUpdatesModal(true);
      }
    }
    setIsLoadingUpdates(false);
  }, [currentBranch, currentVersion]);

  // Auto-check for updates when modal opens
  useEffect(() => {
    checkForUpdates(false);
  }, [checkForUpdates]);

  // Check for updates and show modal (button click)
  const handleCheckUpdates = async () => {
    await checkForUpdates(true);
  };

  // Handle confirm update from modal
  const handleConfirmUpdate = async (modsToUpdate: Mod[]) => {
    setShowUpdatesModal(false);
    if (modsToUpdate.length === 0) return;

    const items = modsToUpdate
      .filter((mod) => mod.latestFileId && mod.curseForgeId)
      .map((mod) => ({ id: mod.curseForgeId!, name: mod.name, fileId: mod.latestFileId! }));

    const errors: string[] = [];
    try {
      await runDownloadQueue(items);
    } catch (err: any) {
      errors.push(err?.message || 'Failed to update one or more mods');
    }

    setIsDownloading(false);
    setDownloadProgress(null);
    setDownloadJobs([]);
    await loadInstalledMods();

    if (errors.length > 0) {
      setError(errors.join('\n'));
    }
  };

  // Open mods folder
  const handleOpenFolder = async () => {
    try {
      await OpenInstanceModsFolder(currentBranch, currentVersion);
    } catch (err) {
      console.error('Failed to open folder:', err);
    }
  };
  
  // Open export modal
  const handleOpenExportModal = async () => {
    try {
      const lastPath = await GetLastExportPath();
      setExportPath(lastPath || '');
      setShowExportModal(true);
    } catch (err) {
      console.error('Failed to get last export path:', err);
      setShowExportModal(true);
    }
  };
  
  // Browse for export folder
  const handleBrowseExportFolder = async () => {
    try {
      const folder = await BrowseFolder(exportPath || null);
      if (folder) {
        setExportPath(folder);
      }
    } catch (err) {
      console.error('Failed to browse folder:', err);
    }
  };
  
  // Perform export
  const handleExport = async () => {
    if (!exportPath) {
      setError(t('modManager.selectExportFolder'));
      return;
    }
    
    setIsExporting(true);
    try {
      const resultPath = await ExportModsToFolder(currentBranch, currentVersion, exportPath, exportType);
      if (resultPath) {
        setError(null);
        setShowExportModal(false);
        setImportProgress(
          exportType === 'zip' 
            ? t('modManager.exportedZip').replace('{{path}}', resultPath)
            : t('modManager.exportedList').replace('{{path}}', resultPath)
        );
        setTimeout(() => setImportProgress(null), 4000);
      } else {
        setError(exportType === 'zip' ? t('modManager.noModFiles') : t('modManager.noCurseForge'));
      }
    } catch (err: any) {
      setError(err?.message || t('modManager.exportFailed'));
    } finally {
      setIsExporting(false);
    }
  };
  
  // Handle drag and drop
  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);
  };
  
  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    // Only set false if leaving the drop zone entirely
    if (e.currentTarget === e.target) {
      setIsDragging(false);
    }
  };
  
  // Helper function to read file as base64 (using ArrayBuffer for reliability)
  const readFileAsBase64 = (file: File): Promise<string> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        const arrayBuffer = reader.result as ArrayBuffer;
        // Convert ArrayBuffer to base64
        const bytes = new Uint8Array(arrayBuffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) {
          binary += String.fromCharCode(bytes[i]);
        }
        const base64 = btoa(binary);
        resolve(base64);
      };
      reader.onerror = () => reject(reader.error);
      reader.readAsArrayBuffer(file);
    });
  };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    
    console.log('[MOD MANAGER] handleDrop called');
    console.log('[MOD MANAGER] Files dropped:', e.dataTransfer.files.length);
    
    const files = Array.from(e.dataTransfer.files);
    if (files.length === 0) {
      console.log('[MOD MANAGER] No files in drop event');
      return;
    }
    
    console.log('[MOD MANAGER] Processing files:', files.map(f => f.name));
    
    setIsImporting(true);
    let successCount = 0;
    let modListImported = false;
    
    try {
      // First, check if file.path is available (Electron-specific)
      const firstFile = files[0] as any;
      const hasFilePath = !!firstFile.path;
      console.log('[MOD MANAGER] Has file.path:', hasFilePath);
      
      for (const file of files) {
        const filePath = (file as any).path;
        console.log(`[MOD MANAGER] Processing ${file.name}, size: ${file.size} bytes`);
        
        // Check if it's a mod list JSON file
        if (file.name.endsWith('.json') && (file.name.includes('ModList') || file.name.includes('modlist'))) {
          if (hasFilePath && filePath) {
            // Use file path method
            setImportProgress(t('modManager.importingList'));
            console.log('[MOD MANAGER] Importing mod list from path:', filePath);
            const count = await ImportModList(filePath, currentBranch, currentVersion);
            console.log('[MOD MANAGER] Mod list imported, count:', count);
            if (count > 0) {
              successCount += count;
              modListImported = true;
            }
          } else {
            // Skip mod list JSON for now when no file path
            console.log('[MOD MANAGER] Mod list import requires file path access, skipping:', file.name);
          }
        }
        // Accept any mod file (JAR, ZIP, etc.)
        else {
          setImportProgress(t('modManager.installingMod').replace('{{name}}', file.name));
          
          if (hasFilePath && filePath) {
            // Use file path method (preferred, faster)
            console.log('[MOD MANAGER] Installing via file path:', filePath);
            const success = await InstallLocalModFile(filePath, currentBranch, currentVersion);
            console.log('[MOD MANAGER] InstallLocalModFile result:', success);
            if (success) {
              successCount++;
            }
          } else {
            // Use base64 method (works in all browsers)
            try {
              console.log('[MOD MANAGER] Reading file as base64:', file.name);
              const base64Content = await readFileAsBase64(file);
              console.log('[MOD MANAGER] Base64 length:', base64Content.length, 'first 50 chars:', base64Content.substring(0, 50));
              
              console.log('[MOD MANAGER] Calling InstallModFromBase64');
              const success = await InstallModFromBase64(file.name, base64Content, currentBranch, currentVersion);
              console.log('[MOD MANAGER] InstallModFromBase64 result:', success);
              if (success) {
                successCount++;
              }
            } catch (readErr) {
              console.error('[MOD MANAGER] Failed to read file:', file.name, readErr);
            }
          }
        }
      }
      
      console.log('[MOD MANAGER] Final success count:', successCount);
      
      if (successCount > 0) {
        console.log('[MOD MANAGER] Reloading installed mods');
        // Give the backend a moment to finish writing metadata
        await new Promise(resolve => setTimeout(resolve, 500));
        await loadInstalledMods();
        setImportProgress(
          modListImported 
            ? t('modManager.importedCount').replace('{{count}}', successCount.toString())
            : t('modManager.installedCount').replace('{{count}}', successCount.toString())
        );
        setTimeout(() => setImportProgress(null), 3000);
      } else {
        console.log('[MOD MANAGER] No mods installed, showing error');
        setError(t('modManager.installFailed'));
      }
    } catch (err: any) {
      console.error('[MOD MANAGER] Error in handleDrop:', err);
      setError(err?.message || t('modManager.importFailed'));
    } finally {
      setIsImporting(false);
    }
  };

  // Handle browsing for mod files via native file picker
  const handleBrowseModFiles = async () => {
    try {
      const filePaths = await BrowseModFiles();
      if (!filePaths || filePaths.length === 0) return;
      
      setIsImporting(true);
      let successCount = 0;
      let modListImported = false;
      
      for (const filePath of filePaths) {
        const fileName = filePath.split(/[/\\]/).pop() || '';
        
        // Check if it's a mod list JSON file
        if (fileName.endsWith('.json') && (fileName.includes('ModList') || fileName.includes('modlist'))) {
          setImportProgress(t('modManager.importingList'));
          const count = await ImportModList(filePath, currentBranch, currentVersion);
          if (count > 0) {
            successCount += count;
            modListImported = true;
          }
        }
        // Accept any mod file (JAR, ZIP, etc.)
        else {
          setImportProgress(t('modManager.installingMod').replace('{{name}}', fileName));
          const success = await InstallLocalModFile(filePath, currentBranch, currentVersion);
          if (success) {
            successCount++;
          }
        }
      }
      
      if (successCount > 0) {
        await loadInstalledMods();
        setImportProgress(
          modListImported 
            ? t('modManager.importedCount').replace('{{count}}', successCount.toString())
            : t('modManager.installedCount').replace('{{count}}', successCount.toString())
        );
        setTimeout(() => setImportProgress(null), 3000);
      } else {
        setError(t('modManager.installFailed'));
      }
    } catch (err: any) {
      setError(err?.message || t('modManager.importFailed'));
    } finally {
      setIsImporting(false);
    }
  };

  const getCategoryName = () => {
    if (selectedCategory === 0) return t('common.all');
    const cat = categories.find((c) => c.id === selectedCategory);
    return cat ? t(cat.name) : t('common.all');
  };

  // Get screenshots for selected mod - check both browse mods and installed mods
  const screenshots = selectedMod ? (
    // Support backend PascalCase payloads
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (selectedMod as any).screenshots || (selectedMod as any).Screenshots || []
  ) : [];


  return (
    <div 
      className={isPageMode
        ? "w-full h-full"
        : "fixed inset-0 z-50 flex items-center justify-center p-8 bg-[#0a0a0a]/95"
      }
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      <div className={`w-full ${isPageMode ? 'h-full !rounded-none' : 'max-w-6xl h-[85vh]'} flex flex-col overflow-hidden transition-colors glass-panel-static-solid ${isDragging ? '!border-2' : ''}`} style={isDragging ? { borderColor: accentColor } : undefined}>
        {/* Drag overlay */}
        {isDragging && (
          <div className="absolute inset-0 z-[100] flex items-center justify-center bg-black/80 rounded-2xl pointer-events-none">
            <div className="text-center">
              <Upload size={64} className="mx-auto mb-4" style={{ color: accentColor }} />
              <p className="text-white text-xl font-bold">{t('modManager.dropModsHere')}</p>
              <p className="text-white/60 mt-2">{t('modManager.dragModFiles')}</p>
            </div>
          </div>
        )}
        
        {/* Import progress overlay */}
        {isImporting && (
          <div className="absolute inset-0 z-[100] flex items-center justify-center bg-black/80 rounded-2xl">
            <div className="text-center">
              <Loader2 size={48} className="mx-auto mb-4 animate-spin" style={{ color: accentColor }} />
              <p className="text-white text-lg">{importProgress || t('common.importing')}</p>
            </div>
          </div>
        )}
        
        {/* Success/info message */}
        {importProgress && !isImporting && (
          <div className="absolute top-4 left-1/2 -translate-x-1/2 z-[100] px-4 py-2 rounded-xl text-sm" style={{ backgroundColor: accentColor, color: accentTextColor }}>
            {importProgress}
          </div>
        )}
        
        {/* Action buttons - shared between header (modal) and portal (page mode) */}
        {(() => {
          const actionButtons = (
            <>
              <button
                onClick={handleCheckUpdates}
                disabled={isLoadingUpdates || isDownloading}
                className="p-2 rounded-xl hover:bg-white/10 text-green-400 hover:text-green-300 disabled:opacity-50 relative"
                title={updateCount > 0 ? t('modManager.updatesAvailableCount').replace('{{count}}', updateCount.toString()) : t('modManager.checkForUpdates')}
              >
                <RefreshCw size={20} className={isLoadingUpdates ? 'animate-spin' : ''} />
                {updateCount > 0 && !isLoadingUpdates && (
                  <span 
                    className="absolute -top-1 -right-1 min-w-[18px] h-[18px] flex items-center justify-center text-xs font-bold rounded-full"
                    style={{ backgroundColor: accentColor, color: accentTextColor }}
                  >
                    {updateCount > 99 ? '99+' : updateCount}
                  </span>
                )}
              </button>
              <button
                onClick={(activeTab === 'browse' && selectedBrowseMods.size > 0) || (activeTab === 'installed' && selectedInstalledMods.size > 0) ? showDownloadConfirmation : undefined}
                disabled={isDownloading || !((activeTab === 'browse' && selectedBrowseMods.size > 0) || (activeTab === 'installed' && selectedInstalledMods.size > 0))}
                className={`p-2 rounded-xl ${(activeTab === 'browse' && selectedBrowseMods.size > 0) || (activeTab === 'installed' && selectedInstalledMods.size > 0)
                  ? ''
                  : 'text-white/20 cursor-not-allowed'
                  }`}
                style={(activeTab === 'browse' && selectedBrowseMods.size > 0) || (activeTab === 'installed' && selectedInstalledMods.size > 0) ? { color: accentColor } : undefined}
                title={
                  activeTab === 'browse' && selectedBrowseMods.size > 0
                    ? t(`Download {{count}} mod(s)`).replace('{{count}}', selectedBrowseMods.size.toString())
                    : activeTab === 'installed' && selectedInstalledMods.size > 0
                      ? t(`Re-download {{count}} mod(s)`).replace('{{count}}', selectedInstalledMods.size.toString())
                      : t('modManager.selectModsToDownload')
                }
              >
                <Download size={20} />
              </button>
              <button
                onClick={activeTab === 'installed' && (selectedInstalledMods.size > 0 || highlightedInstalledMods.size > 0) ? showDeleteConfirmation : undefined}
                disabled={activeTab !== 'installed' || (selectedInstalledMods.size === 0 && highlightedInstalledMods.size === 0)}
                className={`p-2 rounded-xl  ${activeTab === 'installed' && (selectedInstalledMods.size > 0 || highlightedInstalledMods.size > 0)
                  ? 'text-red-400 hover:bg-red-500/10'
                  : 'text-white/20 cursor-not-allowed'
                  }`}
                title={(selectedInstalledMods.size > 0 || highlightedInstalledMods.size > 0) ? t(`Delete {{count}} mod(s)`).replace('{{count}}', (selectedInstalledMods.size + highlightedInstalledMods.size).toString()) : t('modManager.selectModsToDelete')}
              >
                <Trash2 size={20} />
              </button>
              <button
                onClick={handleBrowseModFiles}
                disabled={isImporting}
                className={`p-2 rounded-xl hover:bg-white/10 ${isImporting ? 'text-white/20 cursor-not-allowed' : 'text-white/60 hover:text-white'}`}
                title={t('modManager.addMods')}
              >
                <FilePlus2 size={20} />
              </button>
              <button
                onClick={handleOpenExportModal}
                disabled={installedMods.length === 0}
                className={`p-2 rounded-xl hover:bg-white/10 ${installedMods.length > 0 ? 'text-white/60 hover:text-white' : 'text-white/20 cursor-not-allowed'}`}
                title={t('modManager.exportMods')}
              >
                <Upload size={20} />
              </button>
              <button
                onClick={handleOpenFolder}
                className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white"
                title={t('modManager.openModsFolder')}
              >
                <FolderOpen size={20} />
              </button>
            </>
          );

          return (
            <>
              {/* Portal action buttons to page header in pageMode */}
              {isPageMode && headerActionsRef?.current && createPortal(
                <div className="flex items-center gap-0.5 px-1.5 py-1 glass-panel-static-solid">{actionButtons}</div>,
                headerActionsRef.current
              )}

              {/* Full header in modal mode */}
              {!isPageMode && (
                <div className="p-4 border-b border-white/10 flex items-center justify-between flex-shrink-0">
                  <div className="flex items-center gap-3">
                    <Package size={24} style={{ color: accentColor }} />
                    <div>
                      <h2 className="text-lg font-bold text-white">{t('modManager.title')} <span className="text-white/50 font-normal">({instanceName})</span></h2>
                      {currentProfileName && (
                        <p className="text-xs text-white/40 flex items-center gap-1">
                          <span style={{ color: accentColor }}>●</span> {t('modManager.profile')}: {currentProfileName}
                        </p>
                      )}
                    </div>
                  </div>
                  <div className="flex items-center gap-1">
                    {actionButtons}
                    <button onClick={onClose} className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white">
                      <X size={20} />
                    </button>
                  </div>
                </div>
              )}
            </>
          );
        })()}

        {/* Tabs */}
        <div className="flex border-b border-white/10 flex-shrink-0">
          <button
            onClick={() => {
              setActiveTab('installed');
              setLastClickedIndex(null);
              setSelectedMod(null);
              setHighlightedInstalledMods(new Set());
            }}
            className={`flex-1 py-3 text-sm font-medium relative  ${activeTab === 'installed' ? 'text-white bg-white/5' : 'text-white/50 hover:text-white/70 hover:bg-white/5'
              }`}
          >
            {t('modManager.installed')} ({installedMods.length})
            {activeTab === 'installed' && <div className="absolute bottom-0 left-0 right-0 h-0.5" style={{ backgroundColor: accentColor }} />}
          </button>
          <button
            onClick={() => {
              setActiveTab('browse');
              setLastClickedIndex(null);
              setSelectedMod(null);
              setHighlightedBrowseMods(new Set());
            }}
            className={`flex-1 py-3 text-sm font-medium relative  ${activeTab === 'browse' ? 'text-white bg-white/5' : 'text-white/50 hover:text-white/70 hover:bg-white/5'
              }`}
          >
            {t('modManager.browse')}
            {selectedBrowseMods.size > 0 && (
              <span className="ml-2 text-white/50">
                {selectedBrowseMods.size}
              </span>
            )}
            {activeTab === 'browse' && <div className="absolute bottom-0 left-0 right-0 h-0.5" style={{ backgroundColor: accentColor }} />}
          </button>
        </div>

        {/* Search bar - Installed tab */}
        {activeTab === 'installed' && (
          <div className="p-3 border-b border-white/10 flex-shrink-0">
            <div className="relative">
              <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-white/40" />
              <input
                type="text"
                value={installedSearchQuery}
                onChange={(e) => setInstalledSearchQuery(e.target.value)}
                placeholder={t('modManager.searchInstalled')}
                className="w-full h-10 pl-10 pr-4 rounded-xl bg-white/5 border border-white/10 text-white text-sm placeholder-white/40 focus:outline-none"
              />
            </div>
          </div>
        )}

        {/* Search bar - Browse tab */}
        {activeTab === 'browse' && (
          <div className="p-3 border-b border-white/10 flex gap-2 flex-shrink-0">
            <div className="relative flex-1">
              <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-white/40" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t('modManager.search')}
                className="w-full h-10 pl-10 pr-4 rounded-xl bg-white/5 border border-white/10 text-white text-sm placeholder-white/40 focus:outline-none"
              />
            </div>

            {/* Category dropdown */}
            <div ref={categoryDropdownRef} className="relative">
              <button
                onClick={() => setIsCategoryDropdownOpen(!isCategoryDropdownOpen)}
                className="h-10 px-4 pr-10 rounded-xl bg-white/5 border border-white/10 text-white/80 text-sm hover:border-white/20 flex items-center gap-2 min-w-[140px] whitespace-nowrap"
              >
                <span className="truncate">{getCategoryName()}</span>
                <ChevronDown
                  size={14}
                  className={`absolute right-3 text-white/40 transition-transform ${isCategoryDropdownOpen ? 'rotate-180' : ''}`}
                />
              </button>

              {isCategoryDropdownOpen && (
                <div className="absolute top-full left-0 mt-2 z-[100] min-w-[200px] max-h-60 bg-[#1a1a1a] border border-white/10 rounded-xl shadow-xl overflow-y-auto">
                  <button
                    onClick={() => {
                      setSelectedCategory(0);
                      setIsCategoryDropdownOpen(false);
                    }}
                    className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${selectedCategory === 0 ? 'bg-white/5' : 'text-white/70'
                      }`}
                    style={selectedCategory === 0 ? { color: accentColor } : undefined}
                  >
                    {t('modManager.allCategories')}
                  </button>
                  {categories.map((cat) => (
                    <button
                      key={cat.id}
                      onClick={() => {
                        setSelectedCategory(cat.id);
                        setIsCategoryDropdownOpen(false);
                      }}
                      className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${selectedCategory === cat.id ? 'bg-white/5' : 'text-white/70'
                        }`}
                      style={selectedCategory === cat.id ? { color: accentColor } : undefined}
                    >
                      {t(cat.name)}
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* Sort dropdown */}
            <div ref={sortDropdownRef} className="relative">
              <button
                onClick={() => setIsSortDropdownOpen(!isSortDropdownOpen)}
                className="h-10 px-4 pr-10 rounded-xl bg-white/5 border border-white/10 text-white/80 text-sm hover:border-white/20 flex items-center gap-2 min-w-[160px] whitespace-nowrap"
              >
                <span className="text-white/50 mr-1">{t('modManager.sortBy')}</span>
                <span className="truncate">{sortOptions.find(s => s.id === selectedSortField)?.name || t('modManager.sortTotalDownloads')}</span>
                <ChevronDown
                  size={14}
                  className={`absolute right-3 text-white/40 transition-transform ${isSortDropdownOpen ? 'rotate-180' : ''}`}
                />
              </button>

              {isSortDropdownOpen && (
                <div className="absolute top-full right-0 mt-2 z-[100] min-w-[180px] bg-[#1a1a1a] border border-white/10 rounded-xl shadow-xl overflow-hidden">
                  {sortOptions.map((option) => (
                    <button
                      key={option.id}
                      onClick={() => {
                        setSelectedSortField(option.id);
                        setIsSortDropdownOpen(false);
                      }}
                      className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors flex items-center justify-between ${selectedSortField === option.id ? 'bg-white/5' : 'text-white/70'
                        }`}
                      style={selectedSortField === option.id ? { color: accentColor } : undefined}
                    >
                      {option.name}
                      {selectedSortField === option.id && (
                        <Check size={14} style={{ color: accentColor }} />
                      )}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        )}

        {/* Error display */}
        {error && (
          <div className="mx-4 mt-3 p-3 bg-red-500/20 border border-red-500/30 rounded-xl flex items-center gap-3 flex-shrink-0">
            <AlertCircle size={18} className="text-red-400 flex-shrink-0" />
            <span className="text-red-400 text-sm flex-1">{error}</span>
            <button onClick={() => setError(null)} className="text-red-400 hover:text-red-300">
              <X size={16} />
            </button>
          </div>
        )}

        {/* Main Content - Split View */}
        <div className="flex-1 flex min-h-0 overflow-hidden">
          {/* Left side - Mod List */}
          <div
            ref={scrollContainerRef}
            className="w-1/2 h-full overflow-y-auto flex-shrink-0"
            onScroll={activeTab === 'browse' ? handleScroll : undefined}
          >
            {activeTab === 'installed' ? (
              // Installed Mods Tab
              isLoadingInstalled ? (
                <div className="flex items-center justify-center py-20">
                  <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
                </div>
              ) : filteredInstalledMods.length === 0 ? (
                <div className="h-full flex flex-col items-center justify-center text-white/30">
                  <Package size={48} className="mb-4 opacity-50" />
                  <p className="text-sm">{installedSearchQuery ? t('modManager.noMatch') : t('modManager.noModsInstalled')}</p>
                </div>
              ) : (
                <div className="p-3 space-y-2">
                  {filteredInstalledMods.map((mod, index) => {
                    const isViewing = selectedMod && 'id' in selectedMod && selectedMod.id === mod.id;
                    const isSelected = selectedInstalledMods.has(mod.id);
                    const isHighlighted = highlightedInstalledMods.has(mod.id);

                    return (
                      <div
                        key={mod.id}
                        className={`p-3 rounded-xl border cursor-pointer ${isViewing || isHighlighted
                          ? ''
                          : 'bg-white/5 border-white/10 hover:border-white/20'
                          }`}
                        style={isViewing || isHighlighted ? { backgroundColor: `${accentColor}33`, borderColor: accentColor } : undefined}
                        onClick={(e) => handleModClick(mod, index, e)}
                      >
                        <div className="flex items-center gap-3">
                          {/* Selection checkbox */}
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              // Shift+click on checkbox for range selection
                              if (e.shiftKey && lastClickedIndex !== null) {
                                const start = Math.min(lastClickedIndex, index);
                                const end = Math.max(lastClickedIndex, index);
                                setSelectedInstalledMods((prev) => {
                                  const next = new Set(prev);
                                  for (let i = start; i <= end; i++) {
                                    if (filteredInstalledMods[i]) next.add(filteredInstalledMods[i].id);
                                  }
                                  return next;
                                });
                              } else {
                                // If there are highlighted mods, add all to selection
                                if (highlightedInstalledMods.size > 0) {
                                  setSelectedInstalledMods((prev) => {
                                    const next = new Set(prev);
                                    const isAdding = !next.has(mod.id);
                                    if (isAdding) {
                                      // Add current mod and all highlighted
                                      next.add(mod.id);
                                      highlightedInstalledMods.forEach((id) => next.add(id));
                                    } else {
                                      // Remove current mod and all highlighted
                                      next.delete(mod.id);
                                      highlightedInstalledMods.forEach((id) => next.delete(id));
                                    }
                                    return next;
                                  });
                                  // Keep highlighted mods highlighted
                                } else {
                                  setSelectedInstalledMods((prev) => {
                                    const next = new Set(prev);
                                    if (next.has(mod.id)) {
                                      next.delete(mod.id);
                                    } else {
                                      next.add(mod.id);
                                    }
                                    return next;
                                  });
                                }
                              }
                              setLastClickedIndex(index);
                            }}
                            className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${isSelected ? '' : 'bg-transparent border-white/30 hover:border-white/50'
                              }`}
                            style={isSelected ? { backgroundColor: accentColor, borderColor: accentColor } : undefined}
                            title={isSelected ? t('modManager.selected') : t('modManager.selectShift')}
                          >
                            {isSelected && <Check size={12} style={{ color: accentTextColor }} />}
                          </button>

                          {/* Icon */}
                          <div className="w-10 h-10 rounded-lg bg-white/10 flex items-center justify-center overflow-hidden flex-shrink-0">
                            {mod.iconUrl ? (
                              <img loading="lazy" src={mod.iconUrl} alt="" className="w-full h-full object-cover" />
                            ) : (
                              <Package size={18} className="text-white/40" />
                            )}
                          </div>

                          {/* Info */}
                          <div className="flex-1 min-w-0">
                            <p className="text-white font-medium truncate">
                              {mod.name}
                            </p>
                            <div className="flex items-center gap-2 text-white/50 text-xs">
                              <span>{mod.author || t('modManager.releaseType.unknown')}</span>
                              <span>•</span>
                              <span>{mod.version}</span>
                            </div>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )
            ) : (
              // Browse Mods Tab
              isSearching && searchResults.length === 0 ? (
                <div className="flex items-center justify-center py-20">
                  <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
                </div>
              ) : searchResults.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-20 text-white/40">
                  <Search size={48} className="mb-4 opacity-50" />
                  <p className="text-lg font-medium">{t('modManager.noModsFound')}</p>
                </div>
              ) : (
                <div className="p-3 space-y-2">
                  {searchResults.map((mod, index) => {
                    const isSelected = selectedBrowseMods.has(mod.id);
                    const isInstalled = isModInstalled(mod.id);
                    const isViewing = selectedMod && 'id' in selectedMod && selectedMod.id === mod.id;
                    const isHighlighted = highlightedBrowseMods.has(mod.id);

                    return (
                      <div
                        key={mod.id}
                        className={`p-3 rounded-xl border cursor-pointer ${isViewing || isHighlighted
                          ? ''
                          : 'bg-white/5 border-white/10 hover:border-white/20'
                          }`}
                        style={isViewing || isHighlighted ? { backgroundColor: `${accentColor}33`, borderColor: accentColor } : undefined}
                        onClick={(e) => handleModClick(mod, index, e)}
                      >
                        <div className="flex items-center gap-3">
                          {/* Selection checkbox - allow selecting even installed mods for re-download */}
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              // Shift+click on checkbox for range selection
                              if (e.shiftKey && lastClickedIndex !== null) {
                                const start = Math.min(lastClickedIndex, index);
                                const end = Math.max(lastClickedIndex, index);
                                setSelectedBrowseMods((prev) => {
                                  const next = new Set(prev);
                                  for (let i = start; i <= end; i++) {
                                    const m = searchResults[i];
                                    if (m) {
                                      next.add(m.id);
                                      loadModFiles(m.id);
                                    }
                                  }
                                  return next;
                                });
                              } else {
                                // If there are highlighted mods, add all to selection
                                if (highlightedBrowseMods.size > 0) {
                                  setSelectedBrowseMods((prev) => {
                                    const next = new Set(prev);
                                    const isAdding = !next.has(mod.id);
                                    if (isAdding) {
                                      // Add current mod and all highlighted
                                      next.add(mod.id);
                                      loadModFiles(mod.id);
                                      highlightedBrowseMods.forEach((id) => {
                                        next.add(id);
                                        loadModFiles(id);
                                      });
                                    } else {
                                      // Remove current mod and all highlighted
                                      next.delete(mod.id);
                                      highlightedBrowseMods.forEach((id) => next.delete(id));
                                    }
                                    return next;
                                  });
                                  // Keep highlighted mods highlighted
                                } else {
                                  setSelectedBrowseMods((prev) => {
                                    const next = new Set(prev);
                                    if (next.has(mod.id)) {
                                      next.delete(mod.id);
                                    } else {
                                      next.add(mod.id);
                                      loadModFiles(mod.id);
                                    }
                                    return next;
                                  });
                                }
                              }
                              setLastClickedIndex(index);
                            }}
                            className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${isSelected ? '' : 'bg-transparent border-white/30 hover:border-white/50'
                              }`}
                            style={isSelected ? { backgroundColor: accentColor, borderColor: accentColor } : undefined}
                            title={isSelected ? t('modManager.selectedForDownload') : t('modManager.selectForDownload')}
                          >
                            {isSelected && <Check size={12} style={{ color: accentTextColor }} />}
                          </button>

                          {/* Logo */}
                          <div className="w-10 h-10 rounded-lg bg-white/10 flex items-center justify-center overflow-hidden flex-shrink-0">
                            {(mod.iconUrl || mod.logo?.thumbnailUrl) ? (
                              <img loading="lazy" src={mod.iconUrl || mod.logo?.thumbnailUrl} alt="" className="w-full h-full object-cover" />
                            ) : (
                              <Package size={18} className="text-white/40" />
                            )}
                          </div>

                          {/* Info */}
                          <div className="flex-1 min-w-0">
                            <div className="flex items-center gap-2">
                              <button
                                onClick={(e) => {
                                  e.stopPropagation();
                                  if (mod.slug) {
                                    BrowserOpenURL(`https://www.curseforge.com/hytale/mods/${mod.slug}`);
                                  }
                                }}
                                className="text-white font-medium truncate text-left hover:opacity-80"
                              >
                                {mod.name}
                              </button>
                              {isInstalled && (
                                <span className="px-2 py-0.5 text-xs rounded-full bg-green-500/20 text-green-400 flex-shrink-0">
                                  {t('modManager.installedBadge')}
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-2 text-white/50 text-xs mt-1">
                              <span>{mod.author || mod.authors?.[0]?.name || t('modManager.releaseType.unknown')}</span>
                              <span>•</span>
                              <span>
                                <Download size={10} className="inline mr-1" />
                                {formatDownloads(mod.downloadCount)}
                              </span>
                            </div>
                          </div>
                        </div>
                      </div>
                    );
                  })}

                  {isLoadingMore && (
                    <div className="flex justify-center py-4">
                      <Loader2 size={24} className="animate-spin" style={{ color: accentColor }} />
                    </div>
                  )}
                </div>
              )
            )}
          </div>

          {/* Right side - Detail Panel */}
          <div className="w-1/2 h-full flex flex-col border-l border-white/10 flex-shrink-0">
            {selectedMod ? (
              <>
                {/* Detail Header */}
                <div className="p-4 border-b border-white/10 flex items-center gap-4 flex-shrink-0">
                  <div className="w-14 h-14 rounded-xl bg-white/10 flex items-center justify-center overflow-hidden flex-shrink-0">
                    {'logo' in selectedMod && selectedMod.logo?.thumbnailUrl ? (
                      <img loading="lazy" src={selectedMod.logo.thumbnailUrl} alt="" className="w-full h-full object-cover" />
                    ) : 'iconUrl' in selectedMod && selectedMod.iconUrl ? (
                      <img loading="lazy" src={selectedMod.iconUrl} alt="" className="w-full h-full object-cover" />
                    ) : (
                      <Package size={24} className="text-white/40" />
                    )}
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-1.5">
                      <button
                        onClick={() => {
                          const slug = 'slug' in selectedMod ? selectedMod.slug : undefined;
                          const cfId = 'curseForgeId' in selectedMod ? selectedMod.curseForgeId : 'id' in selectedMod ? (selectedMod as any).id : undefined;
                          if (slug) {
                            BrowserOpenURL(`https://www.curseforge.com/hytale/mods/${slug}`);
                          } else if (cfId) {
                            BrowserOpenURL(`https://www.curseforge.com/hytale/mods/${cfId}`);
                          }
                        }}
                        className="text-lg font-bold text-white hover:opacity-80 text-left flex items-center gap-1.5 group max-w-full"
                      >
                        <span className="truncate min-w-0">{selectedMod.name}</span>
                        <ExternalLink size={14} className="text-white/30 group-hover:text-white/60 transition-colors flex-shrink-0" />
                      </button>
                    </div>
                    <p className="text-white/50 text-sm truncate">
                      {'authors' in selectedMod ? selectedMod.authors?.[0]?.name : 'author' in selectedMod ? selectedMod.author : ''}
                    </p>
                    {/* Categories - below author name */}
                    {(('categories' in selectedMod && selectedMod.categories && (selectedMod.categories as (string | { name: string })[]).length > 0)) && (
                      <div className="flex items-center gap-1.5 flex-wrap mt-1.5">
                        {(selectedMod.categories as (string | { id: number; name: string })[]).map((cat, idx) => {
                          const catName = typeof cat === 'string' ? cat : cat.name;
                          return (
                            <span
                              key={idx}
                              className="px-1.5 py-0.5 text-[10px] rounded font-medium"
                              style={{ backgroundColor: `${accentColor}25`, color: accentColor }}
                            >
                              {t(catName)}
                            </span>
                          );
                        })}
                      </div>
                    )}
                  </div>
                </div>

                {/* Detail Content - Scrollable */}
                <div className="flex-1 overflow-y-auto p-4 space-y-4 min-h-0">
                  {/* Description */}
                  <div>
                    <h4 className="text-white/50 text-xs uppercase mb-2">{t('modManager.description')}</h4>
                    <p className="text-white/70 text-sm">
                      {'summary' in selectedMod ? selectedMod.summary : 'description' in selectedMod ? selectedMod.description : t('modManager.noDescription')}
                    </p>
                  </div>

                  {/* Screenshots - show for all mods */}
                  {screenshots && screenshots.length > 0 && (
                    <div>
                      <h4 className="text-white/50 text-xs uppercase mb-2">{t('modManager.screenshots')}</h4>
                      <div className="relative">
                        <button
                          onClick={() => setFullscreenImage({
                            url: screenshots[activeScreenshot]?.url,
                            title: screenshots[activeScreenshot]?.title || ''
                          })}
                          className="w-full h-40 rounded-xl overflow-hidden bg-black/30 cursor-pointer hover:ring-2 transition-all"
                          style={{ '--tw-ring-color': `${accentColor}80` } as React.CSSProperties}
                          title={t('modManager.clickToViewImage')}
                        >
                          <img
                            src={screenshots[activeScreenshot]?.url}
                            alt={screenshots[activeScreenshot]?.title || ''}
                            className="w-full h-full object-contain"
                          />
                        </button>
                        {screenshots.length > 1 && (
                          <>
                            <button
                              onClick={() => setActiveScreenshot((prev) => (prev > 0 ? prev - 1 : screenshots.length - 1))}
                              className="absolute left-2 top-1/2 -translate-y-1/2 p-1 rounded-full bg-black/60 text-white/80 hover:bg-black/80"
                            >
                              <ChevronLeft size={16} />
                            </button>
                            <button
                              onClick={() => setActiveScreenshot((prev) => (prev < screenshots.length - 1 ? prev + 1 : 0))}
                              className="absolute right-2 top-1/2 -translate-y-1/2 p-1 rounded-full bg-black/60 text-white/80 hover:bg-black/80"
                            >
                              <ChevronRight size={16} />
                            </button>
                            <div className="absolute bottom-2 left-1/2 -translate-x-1/2 flex gap-1">
                              {screenshots.map((_: unknown, i: number) => (
                                <button
                                  key={i}
                                  onClick={() => setActiveScreenshot(i)}
                                  className={`w-2 h-2 rounded-full ${i === activeScreenshot ? 'bg-white' : 'bg-white/30'}`}
                                />
                              ))}
                            </div>
                          </>
                        )}
                      </div>
                    </div>
                  )}
                </div>

                {/* Fixed Footer - Version + Select Button */}
                <div className="p-4 border-t border-white/10 space-y-3 flex-shrink-0">
                  {/* Version Selector Dropdown */}
                  <div>
                    <h4 className="text-white/50 text-xs uppercase mb-2">{t('modManager.versionSelected')}</h4>
                    {isLoadingModFiles ? (
                      <div className="flex items-center gap-2 text-white/40">
                        <Loader2 size={14} className="animate-spin" />
                        <span className="text-sm">{t('modManager.loadingVersions')}</span>
                      </div>
                    ) : selectedModFiles.length === 0 ? (
                      <p className="text-white/40 text-sm">{t('modManager.noVersionsAvailable')}</p>
                    ) : (
                      <div className="relative">
                        <select
                          value={detailSelectedFileId || ''}
                          onChange={(e) => {
                            const fileId = e.target.value;
                            setDetailSelectedFileId(fileId);
                            const modId = 'enabled' in selectedMod
                              ? (selectedMod as Mod).curseForgeId
                              : (selectedMod as CurseForgeMod).id;
                            const modKey = normalizeModKey(modId);
                            if (modKey) {
                              setSelectedVersions((prev) => new Map(prev).set(modKey, fileId));
                            }
                          }}
                          className="w-full px-4 py-3 rounded-xl bg-white/10 text-white text-sm font-medium appearance-none cursor-pointer focus:outline-none border border-white/20"
                        >
                          {selectedModFiles.map((file) => (
                            <option key={file.id} value={file.id} className="bg-[#1a1a1a] text-white">
                              {file.displayName} [{getReleaseTypeLabel(file.releaseType, t).toLowerCase()}]
                            </option>
                          ))}
                        </select>
                        <ChevronDown size={16} className="absolute right-3 top-1/2 -translate-y-1/2 text-white/70 pointer-events-none" />
                      </div>
                    )}
                  </div>

                  {/* Select mod button - works for both browse and installed mods */}
                  {'enabled' in selectedMod ? (
                    // Installed mod - toggle selection (also handles highlighted mods)
                    <button
                      onClick={() => {
                        const modId = (selectedMod as Mod).id;
                        const hasHighlighted = highlightedInstalledMods.size > 0;
                        const isCurrentModSelected = selectedInstalledMods.has(modId);

                        if (hasHighlighted) {
                          // Check if all highlighted mods + current are already selected
                          const allHighlightedSelected = isCurrentModSelected &&
                            Array.from(highlightedInstalledMods).every(id => selectedInstalledMods.has(id));

                          if (allHighlightedSelected) {
                            // Unselect all highlighted mods + current
                            setSelectedInstalledMods((prev) => {
                              const next = new Set(prev);
                              next.delete(modId);
                              highlightedInstalledMods.forEach((id) => next.delete(id));
                              return next;
                            });
                            // Keep highlighted mods highlighted
                          } else {
                            // Add all highlighted mods + current mod to selection
                            setSelectedInstalledMods((prev) => {
                              const next = new Set(prev);
                              next.add(modId);
                              highlightedInstalledMods.forEach((id) => next.add(id));
                              return next;
                            });
                            // Keep highlighted mods highlighted
                          }
                        } else if (isCurrentModSelected) {
                          // Unselect ALL selected mods
                          setSelectedInstalledMods(new Set());
                        } else {
                          // Select current mod
                          setSelectedInstalledMods((prev) => {
                            const next = new Set(prev);
                            next.add(modId);
                            return next;
                          });
                        }
                      }}
                      className={`w-full py-3 rounded-xl text-sm font-medium ${selectedInstalledMods.has((selectedMod as Mod).id) || highlightedInstalledMods.size > 0
                        ? ''
                        : 'bg-white/10 text-white hover:bg-white/20'
                        }`}
                      style={selectedInstalledMods.has((selectedMod as Mod).id) || highlightedInstalledMods.size > 0 ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
                    >
                      {highlightedInstalledMods.size > 0
                        ? (() => {
                          const modId = (selectedMod as Mod).id;
                          const isCurrentModSelected = selectedInstalledMods.has(modId);
                          const isCurrentModHighlighted = highlightedInstalledMods.has(modId);
                          const totalCount = highlightedInstalledMods.size + (isCurrentModHighlighted ? 0 : 1);
                          const allHighlightedSelected = isCurrentModSelected &&
                            Array.from(highlightedInstalledMods).every(id => selectedInstalledMods.has(id));
                          return allHighlightedSelected
                            ? t('modManager.unselectCountMods').replace('{{count}}', totalCount.toString())
                            : t('modManager.selectCountMods').replace('{{count}}', totalCount.toString());
                        })()
                        : selectedInstalledMods.has((selectedMod as Mod).id)
                          ? selectedInstalledMods.size > 1
                            ? t('modManager.unselectAllCount').replace('{{count}}', selectedInstalledMods.size.toString())
                            : t('modManager.unselectMod')
                          : t('modManager.selectMod')}
                    </button>
                  ) : (
                    // Browse mod - toggle selection (also handles highlighted mods)
                    <button
                      onClick={() => {
                        const modId = (selectedMod as CurseForgeMod).id;
                        const hasHighlighted = highlightedBrowseMods.size > 0;
                        const isCurrentModSelected = selectedBrowseMods.has(modId);

                        if (hasHighlighted) {
                          // Check if all highlighted mods + current are already selected
                          const allHighlightedSelected = isCurrentModSelected &&
                            Array.from(highlightedBrowseMods).every(id => selectedBrowseMods.has(id));

                          if (allHighlightedSelected) {
                            // Unselect all highlighted mods + current
                            setSelectedBrowseMods((prev) => {
                              const next = new Set(prev);
                              next.delete(modId);
                              highlightedBrowseMods.forEach((id) => next.delete(id));
                              return next;
                            });
                            // Keep highlighted mods highlighted
                          } else {
                            // Add all highlighted mods + current mod to selection
                            setSelectedBrowseMods((prev) => {
                              const next = new Set(prev);
                              next.add(modId);
                              loadModFiles(modId);
                              highlightedBrowseMods.forEach((id) => {
                                next.add(id);
                                loadModFiles(id);
                              });
                              return next;
                            });
                            // Keep highlighted mods highlighted
                          }
                        } else if (isCurrentModSelected) {
                          // Unselect ALL selected mods
                          setSelectedBrowseMods(new Set());
                        } else {
                          // Select current mod
                          setSelectedBrowseMods((prev) => {
                            const next = new Set(prev);
                            next.add(modId);
                            loadModFiles(modId);
                            return next;
                          });
                        }
                      }}
                      className={`w-full py-3 rounded-xl text-sm font-medium ${selectedBrowseMods.has((selectedMod as CurseForgeMod).id) || highlightedBrowseMods.size > 0
                        ? ''
                        : 'bg-white/10 text-white hover:bg-white/20'
                        }`}
                      style={selectedBrowseMods.has((selectedMod as CurseForgeMod).id) || highlightedBrowseMods.size > 0 ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
                    >
                      {highlightedBrowseMods.size > 0
                        ? (() => {
                          const modId = (selectedMod as CurseForgeMod).id;
                          const isCurrentModSelected = selectedBrowseMods.has(modId);
                          const isCurrentModHighlighted = highlightedBrowseMods.has(modId);
                          const totalCount = highlightedBrowseMods.size + (isCurrentModHighlighted ? 0 : 1);
                          const allHighlightedSelected = isCurrentModSelected &&
                            Array.from(highlightedBrowseMods).every(id => selectedBrowseMods.has(id));
                          return allHighlightedSelected
                            ? t('modManager.unselectCountMods').replace('{{count}}', totalCount.toString())
                            : t('modManager.selectCountForDownload').replace('{{count}}', totalCount.toString());
                        })()
                        : selectedBrowseMods.has((selectedMod as CurseForgeMod).id)
                          ? selectedBrowseMods.size > 1
                            ? t('modManager.unselectAllCount').replace('{{count}}', selectedBrowseMods.size.toString())
                            : t('modManager.unselectMod')
                          : t('modManager.selectModForDownload')}
                    </button>
                  )}
                </div>
              </>
            ) : (
              // Empty state when no mod selected
              <div className="h-full flex flex-col items-center justify-center text-white/30">
                <Package size={48} className="mb-4 opacity-50" />
                <p className="text-sm">{t('modManager.selectModToView')}</p>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Updates Modal */}
      {showUpdatesModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div
            className="absolute inset-0 bg-[#0a0a0a]/90"
            onClick={() => setShowUpdatesModal(false)}
          />
          <div className="relative w-full max-w-lg mx-4 overflow-hidden glass-panel-static-solid">
            <div className="p-4 border-b border-white/10 flex items-center justify-between">
              <h3 className="text-lg font-bold text-white">{t('modManager.checkForUpdates')}</h3>
              <button
                onClick={() => setShowUpdatesModal(false)}
                className="p-1 hover:bg-white/10 rounded-lg"
              >
                <X size={18} className="text-white/60" />
              </button>
            </div>

            <div className="p-4 max-h-80 overflow-y-auto">
              {isLoadingUpdates ? (
                <div className="flex items-center justify-center py-8">
                  <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
                </div>
              ) : installedMods.length === 0 ? (
                <div className="text-center py-8 text-white/50">
                  <Package size={40} className="mx-auto mb-3 opacity-50" />
                  <p>{t('modManager.noModsInstalled')}</p>
                </div>
              ) : modsWithUpdates.length === 0 ? (
                <div className="text-center py-8 text-white/50">
                  <Check size={40} className="mx-auto mb-3 text-green-400" />
                  <p className="text-green-400 font-medium">{t('modManager.allUpToDate')}</p>
                </div>
              ) : (
                <div className="space-y-2">
                  <p className="text-white/50 text-sm mb-3">
                    {t('modManager.updatesAvailable').replace('{{count}}', modsWithUpdates.length.toString())}
                  </p>
                  {modsWithUpdates.map((mod) => (
                    <div
                      key={mod.id}
                      className="p-3 rounded-xl bg-white/5 border border-white/10 flex items-center gap-3"
                    >
                      <div className="w-10 h-10 rounded-lg bg-white/10 flex items-center justify-center overflow-hidden flex-shrink-0">
                        {mod.iconUrl ? (
                          <img loading="lazy" src={mod.iconUrl} alt="" className="w-full h-full object-cover" />
                        ) : (
                          <Package size={18} className="text-white/40" />
                        )}
                      </div>
                      <div className="flex-1 min-w-0">
                        <p className="text-white font-medium truncate">{mod.name}</p>
                        <div className="flex items-center gap-2 text-xs">
                          <span className="text-white/50">{mod.version}</span>
                          <span className="text-green-400">→</span>
                          <span className="text-green-400">{mod.latestVersion || 'Latest'}</span>
                        </div>
                      </div>
                      <button
                        onClick={() => {
                          if (mod.slug) {
                            BrowserOpenURL(`https://www.curseforge.com/hytale/mods/${mod.slug}/files`);
                          } else if (mod.curseForgeId) {
                            BrowserOpenURL(`https://www.curseforge.com/hytale/mods/${mod.curseForgeId}/files`);
                          }
                        }}
                        className="p-1.5 rounded-lg bg-white/5 hover:bg-white/10 text-white/60 hover:text-white flex-shrink-0"
                        title={t('modManager.viewChangelog')}
                      >
                        <FileText size={14} />
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div className="p-4 border-t border-white/10 flex justify-end gap-2">
              <button
                onClick={() => setShowUpdatesModal(false)}
                className="px-4 py-2 rounded-xl bg-white/10 text-white text-sm font-medium hover:bg-white/20"
              >
                {t('common.close')}
              </button>
              {modsWithUpdates.length > 0 && (
                <button
                  onClick={() => handleConfirmUpdate(modsWithUpdates)}
                  disabled={isDownloading}
                  className="px-4 py-2 rounded-xl bg-green-500 text-white text-sm font-medium hover:bg-green-600 disabled:opacity-50 flex items-center gap-2"
                >
                  {isDownloading ? (
                    <>
                      <Loader2 size={14} className="animate-spin" />
                      {t('modManager.updating')}
                    </>
                  ) : (
                    <>
                      <ArrowUpCircle size={14} />
                      {t('modManager.updateAll')} ({modsWithUpdates.length})
                    </>
                  )}
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Confirmation Modal */}
      {confirmModal && (
        <ConfirmModal
          title={confirmModal.type === 'download' ? t('modManager.downloadMods') : t('modManager.deleteMods')}
          message={
            confirmModal.type === 'download'
              ? t('modManager.confirmDownload').replace('{{count}}', confirmModal.items.length.toString())
              : t('modManager.confirmDelete').replace('{{count}}', confirmModal.items.length.toString())
          }
          confirmText={confirmModal.type === 'download' ? t('modManager.download') : t('common.delete')}
          confirmColor={confirmModal.type === 'download' ? 'hover:opacity-90' : 'bg-red-500 hover:bg-red-600'}
          confirmStyle={confirmModal.type === 'download' ? { backgroundColor: accentColor } : undefined}
          confirmTextColor={confirmModal.type === 'download' ? accentTextColor : undefined}
          onConfirm={confirmModal.type === 'download' ? handleConfirmDownload : handleConfirmDelete}
          onCancel={() => setConfirmModal(null)}
        >
          <div className="max-h-40 overflow-y-auto space-y-1">
            {confirmModal.items.map((item) => (
              <div key={String(item.id)} className="px-3 py-2 bg-white/5 rounded-lg text-white/80 text-sm">
                {item.name}
              </div>
            ))}
          </div>
        </ConfirmModal>
      )}

      {/* Download Progress Overlay */}
      {isDownloading && downloadProgress && (
        <div className="fixed inset-0 flex items-center justify-center z-50 bg-[#0a0a0a]/90">
          <div className="p-6 max-w-md w-full mx-4 shadow-xl glass-panel-static-solid">
            <div className="flex items-center gap-3 mb-4">
              <Loader2 size={24} className="animate-spin" style={{ color: accentColor }} />
              <div>
                <h3 className="text-white font-semibold">{t('modManager.downloadingMods')}</h3>
                <p className="text-white/60 text-sm">
                  {downloadProgress.current} {t('modManager.of')} {downloadProgress.total}
                </p>
              </div>
            </div>

            {/* Progress Bar */}
            <div className="w-full bg-white/10 rounded-full h-2 mb-3 overflow-hidden">
              <div
                className="h-full rounded-full transition-all duration-300 ease-out"
                style={{ width: `${(downloadProgress.current / downloadProgress.total) * 100}%`, backgroundColor: accentColor }}
              />
            </div>


            {/* Current Mod Name */}
            <p className="text-white/80 text-sm truncate">
              {downloadProgress.currentMod}
            </p>

            {/* Per-mod status list */}
            <div className="mt-4 space-y-2 max-h-52 overflow-y-auto">
              {downloadJobs.map((job) => (
                <div key={String(job.id)} className="flex items-center gap-2 text-white/80 text-sm bg-white/5 rounded-lg px-3 py-2">
                  {job.status === 'success' && <Check size={14} className="text-green-400" />}
                  {job.status === 'running' && <Loader2 size={14} className="animate-spin" style={{ color: accentColor }} />}
                  {job.status === 'pending' && <RefreshCw size={14} className="text-white/50" />}
                  {job.status === 'error' && <AlertCircle size={14} className="text-red-400" />}
                  <div className="flex-1 truncate">{job.name}</div>
                  <span className="text-white/40 text-xs">{job.status}</span>
                  {job.attempts > 0 && <span className="text-white/30 text-xs">x{job.attempts}</span>}
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* Fullscreen Image Viewer */}
      {fullscreenImage && (
        <div
          className="fixed inset-0 flex items-center justify-center z-[60] bg-[#0a0a0a]/98"
          onClick={() => setFullscreenImage(null)}
        >
          <button
            onClick={() => setFullscreenImage(null)}
            className="absolute top-4 right-4 p-2 rounded-xl bg-white/10 hover:bg-white/20 text-white/80 hover:text-white transition-colors"
          >
            <X size={24} />
          </button>
            <img
              src={fullscreenImage.url}
              alt={fullscreenImage.title}
              className="max-w-[90vw] max-h-[90vh] object-contain rounded-xl shadow-2xl"
              onClick={(e) => e.stopPropagation()}
            />
            <div className="absolute bottom-4 left-4 flex flex-col gap-1 items-start text-left">
              {fullscreenImage.title && (
                <span className="px-3 py-1 rounded-lg bg-black/60 text-white font-medium shadow-lg shadow-black/30">
                  {fullscreenImage.title}
                </span>
              )}
              <span className="px-3 py-1 rounded-lg bg-black/40 text-white/70 text-sm shadow-md shadow-black/30">
                {t('modManager.clickToClose')}
              </span>
            </div>
        </div>
      )}

      {/* Export Modal */}
      {showExportModal && (
        <div className="fixed inset-0 flex items-center justify-center z-[60] bg-[#0a0a0a]/95">
          <div className="p-6 w-full max-w-md glass-panel-static-solid">
            <div className="flex items-center justify-between mb-6">
              <h3 className="text-xl font-bold text-white">{t('modManager.exportMods')}</h3>
              <button
                onClick={() => setShowExportModal(false)}
                className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white"
              >
                <X size={20} />
              </button>
            </div>

            {/* Export Type */}
            <div className="mb-4">
              <label className="block text-white/60 text-sm mb-2">{t('modManager.exportType.title')}</label>
              <div className="flex gap-2">
                <button
                  onClick={() => setExportType('modlist')}
                  className={`flex-1 p-3 rounded-xl border transition-colors ${
                    exportType === 'modlist'
                      ? 'border-2'
                      : 'border-white/10 hover:border-white/20'
                  }`}
                  style={exportType === 'modlist' ? { borderColor: accentColor, backgroundColor: `${accentColor}20` } : undefined}
                >
                  <FileText size={20} className="mx-auto mb-1" style={exportType === 'modlist' ? { color: accentColor } : { color: 'rgba(255,255,255,0.6)' }} />
                  <div className="text-sm text-white font-medium">{t('modManager.exportType.modList')}</div>
                  <div className="text-xs text-white/40">{t('modManager.exportType.modListHint')}</div>
                </button>
                <button
                  onClick={() => setExportType('zip')}
                  className={`flex-1 p-3 rounded-xl border transition-colors ${
                    exportType === 'zip'
                      ? 'border-2'
                      : 'border-white/10 hover:border-white/20'
                  }`}
                  style={exportType === 'zip' ? { borderColor: accentColor, backgroundColor: `${accentColor}20` } : undefined}
                >
                  <Package size={20} className="mx-auto mb-1" style={exportType === 'zip' ? { color: accentColor } : { color: 'rgba(255,255,255,0.6)' }} />
                  <div className="text-sm text-white font-medium">{t('modManager.exportType.zipArchive')}</div>
                  <div className="text-xs text-white/40">{t('modManager.exportType.zipArchiveHint')}</div>
                </button>
              </div>
            </div>

            {/* Export Folder */}
            <div className="mb-6">
              <label className="block text-white/60 text-sm mb-2">{t('modManager.exportType.exportFolder')}</label>
              <div className="flex gap-2">
                <input
                  type="text"
                  value={exportPath}
                  onChange={(e) => setExportPath(e.target.value)}
                  placeholder={t('modManager.exportType.selectFolder')}
                  className="flex-1 px-4 py-2 bg-[#252525] border border-white/10 rounded-xl text-white placeholder-white/40 focus:outline-none focus:border-white/30"
                />
                <button
                  onClick={handleBrowseExportFolder}
                  className="px-4 py-2 rounded-xl bg-white/10 hover:bg-white/20 text-white transition-colors"
                >
                  <FolderOpen size={20} />
                </button>
              </div>
            </div>

            {/* Export Button */}
            <button
              onClick={handleExport}
              disabled={isExporting || !exportPath}
              className={`w-full py-3 rounded-xl font-bold text-lg transition-all ${
                isExporting || !exportPath
                  ? 'bg-white/10 text-white/40 cursor-not-allowed'
                  : 'hover:opacity-90'
              }`}
              style={!isExporting && exportPath ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
            >
              {isExporting ? (
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
    </div>
  );
};
