import React, { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import {
  X, Search, Download, Trash2, FolderOpen,
  Package, Loader2, AlertCircle,
  RefreshCw, Check, ChevronDown, ChevronLeft, ChevronRight, ArrowUpCircle, FileText
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { BrowserOpenURL } from '../../wailsjs/runtime/runtime';
import {
  SearchMods, GetModFiles, GetModCategories,
  GetInstanceInstalledMods, InstallModFileToInstance,
  UninstallInstanceMod,
  OpenInstanceModsFolder, CheckInstanceModUpdates
} from '../../wailsjs/go/app/App';
import { GameBranch } from '@/constants/enums';

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
  onConfirm: () => void;
  onCancel: () => void;
  children?: React.ReactNode;
}> = ({ title, message, confirmText, confirmColor, onConfirm, onCancel, children }) => {
  const { t } = useTranslation();
  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center bg-black/60">
      <div className="bg-[#1a1a1a] border border-white/10 rounded-2xl p-6 max-w-md w-full mx-4">
        <h3 className="text-lg font-bold text-white mb-2">{title}</h3>
        <p className="text-white/60 text-sm mb-4">{message}</p>
        {children}
        <div className="flex gap-3 mt-4">
          <button
            onClick={onCancel}
            className="flex-1 py-2 rounded-xl bg-white/10 text-white text-sm hover:bg-white/20"
          >
            {t('Cancel')}
          </button>
          <button onClick={onConfirm} className={`flex-1 py-2 rounded-xl text-white text-sm ${confirmColor}`}>
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
    case 1: return t('Release');
    case 2: return t('Beta');
    case 3: return t('Alpha');
    default: return t('Unknown');
  }
};

export const ModManager: React.FC<ModManagerProps> = ({
  onClose,
  currentBranch,
  currentVersion,
  initialSearchQuery = ''
}) => {
  const { t } = useTranslation();
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

  // Browse mods
  const [searchQuery, setSearchQuery] = useState(initialSearchQuery);
  const [searchResults, setSearchResults] = useState<CurseForgeMod[]>([]);
  const [categories, setCategories] = useState<ModCategory[]>([]);
  const [selectedCategory, setSelectedCategory] = useState(0);
  const [isSearching, setIsSearching] = useState(false);
  const [currentPage, setCurrentPage] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [isCategoryDropdownOpen, setIsCategoryDropdownOpen] = useState(false);
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

  // Confirmation modals
  const [confirmModal, setConfirmModal] = useState<{
    type: 'download' | 'delete';
    items: Array<{ id: string | number; name: string; fileId?: number | string }>;
  } | null>(null);

  // Multi-select tracking
  const [lastClickedIndex, setLastClickedIndex] = useState<number | null>(null);

  // Refs
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const categoryDropdownRef = useRef<HTMLDivElement>(null);

  const instanceBranchLabel = currentBranch === GameBranch.RELEASE
    ? t('Release')
    : currentBranch === GameBranch.PRE_RELEASE
      ? t('Pre-Release')
      : t('Release');
  const instanceName = currentVersion === 0 ? t('latest') : `${instanceBranchLabel} v${currentVersion}`;

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

  // Load categories
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

  // Close category dropdown on click outside
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (categoryDropdownRef.current && !categoryDropdownRef.current.contains(e.target as Node)) {
        setIsCategoryDropdownOpen(false);
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
        // sortField: 2=Popularity, sortOrder: 2=desc
        const result: any = await SearchMods(searchQuery, pageNum, pageSize, categoryFilter, 2, 2);
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
    [searchQuery, selectedCategory, currentPage]
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
  }, [searchQuery, selectedCategory, activeTab]);

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
      const files = Array.isArray(result) ? result : (result?.Files ?? result?.files ?? []);
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
      const resolved = await Promise.all(
        selected.map(async (modId) => {
          const mod = searchResults.find((m) => m.id === modId);
          const modKey = normalizeModKey(modId);
          let fileId = selectedVersions.get(modKey);

          if (!fileId) {
            const files = await loadModFiles(modId);
            fileId = selectedVersions.get(modKey) || files?.[0]?.id;
          }

          if (!fileId) return null;
          return { id: modId, name: mod?.name || 'Unknown', fileId } as { id: string | number; name: string; fileId: string | number };
        })
      );

      items = resolved.filter((x): x is { id: string | number; name: string; fileId: string | number } => x !== null);
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
      setError(t('No downloadable files found for the selected mods.'));
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

  const runDownloadQueue = async (items: Array<{ id: string | number; name: string; fileId: string | number }>) => {
    const maxConcurrency = 3;
    const maxRetries = 2;

    setIsDownloading(true);
    setDownloadProgress({ current: 0, total: items.length, currentMod: '' });
    setDownloadJobs(items.map((item) => ({ id: item.id, name: item.name, status: 'pending', attempts: 0 })));

    let completed = 0;
    const queue = [...items];

    const runJob = async (item: { id: string | number; name: string; fileId: string | number }) => {
      for (let attempt = 1; attempt <= maxRetries; attempt++) {
        setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: 'running', attempts: attempt } : j));
        try {
          const ok = await InstallModFileToInstance(String(item.id), String(item.fileId), currentBranch, currentVersion);
          if (!ok) {
            throw new Error(t('Backend refused to install this mod. Make sure the game is installed.'));
          }
          setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: 'success', attempts: attempt } : j));
          return;
        } catch (err: any) {
          const isLast = attempt === maxRetries;
          setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: isLast ? 'error' : 'pending', attempts: attempt, error: err?.message } : j));
          if (isLast) {
            throw err;
          }
        }
      }
    };

    const worker = async () => {
      while (true) {
        const next = queue.shift();
        if (!next) break;
        try {
          await runJob(next);
        } finally {
          completed += 1;
          setDownloadProgress({ current: completed, total: items.length, currentMod: next.name });
        }
      }
    };

    await Promise.all(Array.from({ length: Math.min(maxConcurrency, items.length) }, () => worker()));
  };

  // Handle confirm download
  const handleConfirmDownload = async () => {
    if (!confirmModal || confirmModal.type !== 'download') return;

    const items = confirmModal.items.filter((i) => i.fileId);
    setConfirmModal(null);
    const errors: string[] = [];

    try {
      // No need to check if game is installed - mods can be downloaded anytime
      await runDownloadQueue(items as Array<{ id: string | number; name: string; fileId: string | number }>);
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

  // Check for updates and show modal
  const handleCheckUpdates = async () => {
    setIsLoadingUpdates(true);
    try {
      const updates = await CheckInstanceModUpdates(currentBranch, currentVersion);
      setModsWithUpdates(updates || []);
      setShowUpdatesModal(true);
    } catch (err) {
      console.error('Failed to check for updates:', err);
      setModsWithUpdates([]);
      setShowUpdatesModal(true);
    }
    setIsLoadingUpdates(false);
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

  const getCategoryName = () => {
    if (selectedCategory === 0) return t('All');
    const cat = categories.find((c) => c.id === selectedCategory);
    return cat ? t(cat.name) : t('All');
  };

  // Get screenshots for selected mod - check both browse mods and installed mods
  const screenshots = selectedMod ? (
    // Support backend PascalCase payloads
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (selectedMod as any).screenshots || (selectedMod as any).Screenshots || []
  ) : [];

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/80 backdrop-blur-sm p-8">
      <div className="w-full max-w-6xl h-[85vh] bg-[#1a1a1a] rounded-2xl border border-white/10 flex flex-col overflow-hidden">
        {/* Header */}
        <div className="p-4 border-b border-white/10 flex items-center justify-between flex-shrink-0">
          {/* Left side - Instance info */}
          <div className="flex items-center gap-3">
            <Package size={24} className="text-[#FFA845]" />
            <h2 className="text-lg font-bold text-white">{t('Mod Manager')} <span className="text-white/50 font-normal">({instanceName})</span></h2>
          </div>

          {/* Right side - Action buttons */}
          <div className="flex items-center gap-1">
            <button
              onClick={handleCheckUpdates}
              disabled={isLoadingUpdates || isDownloading}
              className="p-2 rounded-xl hover:bg-white/10 text-green-400 hover:text-green-300 disabled:opacity-50"
              title={t('Check for updates')}
            >
              <RefreshCw size={20} className={isLoadingUpdates ? 'animate-spin' : ''} />
            </button>
            <button
              onClick={(activeTab === 'browse' && selectedBrowseMods.size > 0) || (activeTab === 'installed' && selectedInstalledMods.size > 0) ? showDownloadConfirmation : undefined}
              disabled={isDownloading || !((activeTab === 'browse' && selectedBrowseMods.size > 0) || (activeTab === 'installed' && selectedInstalledMods.size > 0))}
              className={`p-2 rounded-xl ${(activeTab === 'browse' && selectedBrowseMods.size > 0) || (activeTab === 'installed' && selectedInstalledMods.size > 0)
                ? 'text-[#FFA845] hover:bg-[#FFA845]/10'
                : 'text-white/20 cursor-not-allowed'
                }`}
              title={
                activeTab === 'browse' && selectedBrowseMods.size > 0
                  ? t(`Download {{count}} mod(s)`).replace('{{count}}', selectedBrowseMods.size.toString())
                  : activeTab === 'installed' && selectedInstalledMods.size > 0
                    ? t(`Re-download {{count}} mod(s)`).replace('{{count}}', selectedInstalledMods.size.toString())
                    : t('Select mods to download')
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
              title={(selectedInstalledMods.size > 0 || highlightedInstalledMods.size > 0) ? t(`Delete {{count}} mod(s)`).replace('{{count}}', (selectedInstalledMods.size + highlightedInstalledMods.size).toString()) : t('Select mods to delete')}
            >
              <Trash2 size={20} />
            </button>
            <button
              onClick={handleOpenFolder}
              className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white"
              title={t('Open Mods Folder')}
            >
              <FolderOpen size={20} />
            </button>
            <button onClick={onClose} className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white">
              <X size={20} />
            </button>
          </div>
        </div>

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
            {t('Installed Mods')} ({installedMods.length})
            {activeTab === 'installed' && <div className="absolute bottom-0 left-0 right-0 h-0.5 bg-[#FFA845]" />}
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
            {t('Browse Mods')}
            {selectedBrowseMods.size > 0 && (
              <span className="ml-2 text-white/50">
                {selectedBrowseMods.size}
              </span>
            )}
            {activeTab === 'browse' && <div className="absolute bottom-0 left-0 right-0 h-0.5 bg-[#FFA845]" />}
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
                placeholder={t('Search installed mods...')}
                className="w-full h-10 pl-10 pr-4 rounded-xl bg-white/5 border border-white/10 text-white text-sm placeholder-white/40 focus:outline-none focus:border-[#FFA845]/50"
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
                placeholder={t('Search mods...')}
                className="w-full h-10 pl-10 pr-4 rounded-xl bg-white/5 border border-white/10 text-white text-sm placeholder-white/40 focus:outline-none focus:border-[#FFA845]/50"
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
                    className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${selectedCategory === 0 ? 'text-[#FFA845] bg-white/5' : 'text-white/70'
                      }`}
                  >
                    {t('All Categories')}
                  </button>
                  {categories.map((cat) => (
                    <button
                      key={cat.id}
                      onClick={() => {
                        setSelectedCategory(cat.id);
                        setIsCategoryDropdownOpen(false);
                      }}
                      className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${selectedCategory === cat.id ? 'text-[#FFA845] bg-white/5' : 'text-white/70'
                        }`}
                    >
                      {t(cat.name)}
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
                  <Loader2 size={32} className="animate-spin text-[#FFA845]" />
                </div>
              ) : filteredInstalledMods.length === 0 ? (
                <div className="h-full flex flex-col items-center justify-center text-white/30">
                  <Package size={48} className="mb-4 opacity-50" />
                  <p className="text-sm">{installedSearchQuery ? t('No mods match your search') : t('No mods installed')}</p>
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
                          ? 'bg-[#FFA845]/20 border-[#FFA845]'
                          : 'bg-white/5 border-white/10 hover:border-white/20'
                          }`}
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
                            className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${isSelected ? 'bg-[#FFA845] border-[#FFA845]' : 'bg-transparent border-white/30 hover:border-white/50'
                              }`}
                            title={isSelected ? t('Selected') : t('Select (Shift+click for range)')}
                          >
                            {isSelected && <Check size={12} className="text-white" />}
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
                              <span>{mod.author || t('Unknown')}</span>
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
                  <Loader2 size={32} className="animate-spin text-[#FFA845]" />
                </div>
              ) : searchResults.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-20 text-white/40">
                  <Search size={48} className="mb-4 opacity-50" />
                  <p className="text-lg font-medium">{t('No mods found')}</p>
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
                          ? 'bg-[#FFA845]/20 border-[#FFA845]'
                          : 'bg-white/5 border-white/10 hover:border-white/20'
                          }`}
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
                            className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0  ${isSelected ? 'bg-[#FFA845] border-[#FFA845]' : 'bg-transparent border-white/30 hover:border-white/50'
                              }`}
                            title={isSelected ? t('Selected for download') : t('Select for download (Shift+click for range)')}
                          >
                            {isSelected && <Check size={12} className="text-white" />}
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
                                className="text-white font-medium truncate hover:text-[#FFA845]  text-left"
                              >
                                {mod.name}
                              </button>
                              {isInstalled && (
                                <span className="px-2 py-0.5 text-xs rounded-full bg-green-500/20 text-green-400 flex-shrink-0">
                                  {t('Installed')}
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-2 text-white/50 text-xs">
                              <span>{mod.author || mod.authors?.[0]?.name || t('Unknown')}</span>
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
                      <Loader2 size={24} className="animate-spin text-[#FFA845]" />
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
                    <button
                      onClick={() => {
                        const slug = 'slug' in selectedMod ? selectedMod.slug : undefined;
                        if (slug) {
                          BrowserOpenURL(`https://www.curseforge.com/hytale/mods/${slug}`);
                        }
                      }}
                      className="text-lg font-bold text-white truncate hover:text-[#FFA845] text-left block w-full"
                    >
                      {selectedMod.name}
                    </button>
                    <p className="text-white/50 text-sm truncate">
                      {'authors' in selectedMod ? selectedMod.authors?.[0]?.name : 'author' in selectedMod ? selectedMod.author : ''}
                    </p>
                  </div>
                </div>

                {/* Detail Content - Scrollable */}
                <div className="flex-1 overflow-y-auto p-4 space-y-4 min-h-0">
                  {/* Description */}
                  <div>
                    <h4 className="text-white/50 text-xs uppercase mb-2">{t('Description')}</h4>
                    <p className="text-white/70 text-sm">
                      {'summary' in selectedMod ? selectedMod.summary : 'description' in selectedMod ? selectedMod.description : t('No description')}
                    </p>
                  </div>

                  {/* Screenshots - show for all mods */}
                  {screenshots && screenshots.length > 0 && (
                    <div>
                      <h4 className="text-white/50 text-xs uppercase mb-2">{t('Screenshots')}</h4>
                      <div className="relative">
                        <button
                          onClick={() => setFullscreenImage({
                            url: screenshots[activeScreenshot]?.url,
                            title: screenshots[activeScreenshot]?.title || ''
                          })}
                          className="w-full h-40 rounded-xl overflow-hidden bg-black/30 cursor-pointer hover:ring-2 hover:ring-[#FFA845]/50 transition-all"
                          title={t('Click to view full image')}
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
                              {screenshots.map((_, i) => (
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
                    <h4 className="text-white/50 text-xs uppercase mb-2">{t('Version selected:')}</h4>
                    {isLoadingModFiles ? (
                      <div className="flex items-center gap-2 text-white/40">
                        <Loader2 size={14} className="animate-spin" />
                        <span className="text-sm">{t('Loading versions...')}</span>
                      </div>
                    ) : selectedModFiles.length === 0 ? (
                      <p className="text-white/40 text-sm">{t('No versions available')}</p>
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
                        ? 'bg-[#FFA845] text-white'
                        : 'bg-white/10 text-white hover:bg-white/20'
                        }`}
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
                            ? t('Unselect {{count}} mods').replace('{{count}}', totalCount.toString())
                            : t('Select {{count}} mods').replace('{{count}}', totalCount.toString());
                        })()
                        : selectedInstalledMods.has((selectedMod as Mod).id)
                          ? selectedInstalledMods.size > 1
                            ? t('Unselect all ({{count}})').replace('{{count}}', selectedInstalledMods.size.toString())
                            : t('Unselect mod')
                          : t('Select mod')}
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
                        ? 'bg-[#FFA845] text-white'
                        : 'bg-white/10 text-white hover:bg-white/20'
                        }`}
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
                            ? t('Unselect {{count}} mods').replace('{{count}}', totalCount.toString())
                            : t('Select {{count}} mods for download').replace('{{count}}', totalCount.toString());
                        })()
                        : selectedBrowseMods.has((selectedMod as CurseForgeMod).id)
                          ? selectedBrowseMods.size > 1
                            ? t('Unselect all ({{count}})').replace('{{count}}', selectedBrowseMods.size.toString())
                            : t('Unselect mod')
                          : t('Select mod for download')}
                    </button>
                  )}
                </div>
              </>
            ) : (
              // Empty state when no mod selected
              <div className="h-full flex flex-col items-center justify-center text-white/30">
                <Package size={48} className="mb-4 opacity-50" />
                <p className="text-sm">{t('Select a mod to view details')}</p>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Updates Modal */}
      {showUpdatesModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm"
            onClick={() => setShowUpdatesModal(false)}
          />
          <div className="relative bg-[#1a1a1a]/95 rounded-2xl border border-white/10 w-full max-w-lg mx-4 overflow-hidden">
            <div className="p-4 border-b border-white/10 flex items-center justify-between">
              <h3 className="text-lg font-bold text-white">{t('Check for Updates')}</h3>
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
                  <Loader2 size={32} className="animate-spin text-[#FFA845]" />
                </div>
              ) : installedMods.length === 0 ? (
                <div className="text-center py-8 text-white/50">
                  <Package size={40} className="mx-auto mb-3 opacity-50" />
                  <p>{t('No mods installed')}</p>
                </div>
              ) : modsWithUpdates.length === 0 ? (
                <div className="text-center py-8 text-white/50">
                  <Check size={40} className="mx-auto mb-3 text-green-400" />
                  <p className="text-green-400 font-medium">{t('All mods are up to date!')}</p>
                </div>
              ) : (
                <div className="space-y-2">
                  <p className="text-white/50 text-sm mb-3">
                    {t('{{count}} mod(s) have updates available:').replace('{{count}}', modsWithUpdates.length.toString())}
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
                        title={t('View changelog')}
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
                {t('Close')}
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
                      {t('Updating...')}
                    </>
                  ) : (
                    <>
                      <ArrowUpCircle size={14} />
                      {t('Update All')} ({modsWithUpdates.length})
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
          title={confirmModal.type === 'download' ? t('Download Mods') : t('Delete Mods')}
          message={
            confirmModal.type === 'download'
              ? t('Are you sure you want to download {{count}} mod(s)?').replace('{{count}}', confirmModal.items.length.toString())
              : t('Are you sure you want to delete {{count}} mod(s)? This cannot be undone.').replace('{{count}}', confirmModal.items.length.toString())
          }
          confirmText={confirmModal.type === 'download' ? t('Download') : t('Delete')}
          confirmColor={
            confirmModal.type === 'download'
              ? 'bg-[#FFA845] hover:bg-[#FF9530]'
              : 'bg-red-500 hover:bg-red-600'
          }
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
        <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50">
          <div className="bg-black/80 border border-white/10 rounded-2xl p-6 max-w-md w-full mx-4 shadow-xl">
            <div className="flex items-center gap-3 mb-4">
              <Loader2 size={24} className="animate-spin text-[#FFA845]" />
              <div>
                <h3 className="text-white font-semibold">{t('Downloading Mods')}</h3>
                <p className="text-white/60 text-sm">
                  {downloadProgress.current} {t('of')} {downloadProgress.total}
                </p>
              </div>
            </div>

            {/* Progress Bar */}
            <div className="w-full bg-white/10 rounded-full h-2 mb-3 overflow-hidden">
              <div
                className="bg-[#FFA845] h-full rounded-full transition-all duration-300 ease-out"
                style={{ width: `${(downloadProgress.current / downloadProgress.total) * 100}%` }}
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
                  {job.status === 'running' && <Loader2 size={14} className="animate-spin text-[#FFA845]" />}
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
          className="fixed inset-0 bg-black/90 backdrop-blur-sm flex items-center justify-center z-[60]"
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
                {t('Click anywhere to close')}
              </span>
            </div>
        </div>
      )}
    </div>
  );
};
