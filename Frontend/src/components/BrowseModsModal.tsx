import React, { useState, useEffect, useCallback, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  X, Search, Download, Package, Loader2, AlertCircle,
  Check, ChevronDown, ChevronLeft, ChevronRight,
  Upload, FilePlus2
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useAccentColor } from '../contexts/AccentColorContext';

import { ipc } from '@/lib/ipc';

// Alias for compatibility
const BrowserOpenURL = (url: string) => ipc.browser.open(url);

// TODO: These need dedicated IPC channels in IpcService.cs
const _stub = <T,>(name: string, fb: T) => async (..._a: unknown[]): Promise<T> => { console.warn(`[IPC] ${name}: no channel`); return fb; };
const SearchMods = _stub<{ Mods?: CurseForgeMod[]; mods?: CurseForgeMod[] }>('SearchMods', { Mods: [] });
const GetModFiles = _stub<{ Files?: ModFile[]; files?: ModFile[] } | ModFile[]>('GetModFiles', []);
const GetModCategories = _stub<ModCategory[]>('GetModCategories', []);
const InstallModFileToInstance = _stub('InstallModFileToInstance', false);
const BrowseModFiles = _stub<string[]>('BrowseModFiles', []);
const InstallLocalModFile = _stub('InstallLocalModFile', false);
const InstallModFromBase64 = _stub('InstallModFromBase64', false);
const GetShowAlphaMods = _stub('GetShowAlphaMods', false);

// Types
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

interface BrowseModsModalProps {
  isOpen: boolean;
  onClose: () => void;
  currentBranch: string;
  currentVersion: number;
  onModsInstalled?: () => void;
}

type DownloadJobStatus = {
  id: string | number;
  name: string;
  status: 'pending' | 'running' | 'success' | 'error';
  attempts: number;
  error?: string;
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

export const BrowseModsModal: React.FC<BrowseModsModalProps> = ({
  isOpen,
  onClose,
  currentBranch,
  currentVersion,
  onModsInstalled
}) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();

  
  // Browse mods state
  const [searchQuery, setSearchQuery] = useState('');
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
  const [selectedMods, setSelectedMods] = useState<Set<number | string>>(new Set());

  // Mod files cache and selection
  const [modFilesCache, setModFilesCache] = useState<Map<string, ModFile[]>>(new Map());
  const [selectedVersions, setSelectedVersions] = useState<Map<string, number | string>>(new Map());

  // Detail panel
  const [selectedMod, setSelectedMod] = useState<CurseForgeMod | null>(null);
  const [selectedModFiles, setSelectedModFiles] = useState<ModFile[]>([]);
  const [isLoadingModFiles, setIsLoadingModFiles] = useState(false);
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

  // Alpha mods setting
  const [showAlphaMods, setShowAlphaMods] = useState(false);

  // Refs  
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const categoryDropdownRef = useRef<HTMLDivElement>(null);
  const sortDropdownRef = useRef<HTMLDivElement>(null);

  // Sort options
  const sortOptions = [
    { id: 1, name: t('modManager.sortRelevancy') },
    { id: 2, name: t('modManager.sortPopularity') },
    { id: 3, name: t('modManager.sortLatestUpdate') },
    { id: 11, name: t('modManager.sortCreationDate') },
    { id: 6, name: t('modManager.sortTotalDownloads') },
  ];

  const normalizeModKey = (id: number | string | undefined | null) => String(id ?? '');

  // Load categories on mount
  useEffect(() => {
    if (!isOpen) return;
    
    const loadCategories = async () => {
      try {
        const cats = await GetModCategories();
        setCategories(cats || []);
      } catch (err) {
        console.error('Failed to load categories:', err);
      }
    };
    loadCategories();
  }, [isOpen]);

  // Load alpha mods setting
  useEffect(() => {
    if (!isOpen) return;
    
    const loadAlphaModsSetting = async () => {
      try {
        const showAlpha = await GetShowAlphaMods();
        setShowAlphaMods(showAlpha);
      } catch (err) {
        console.error('Failed to load alpha mods setting:', err);
      }
    };
    loadAlphaModsSetting();
  }, [isOpen]);

  // Close dropdowns on click outside
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
        const categoryFilter = selectedCategory === 0 ? [] : [selectedCategory.toString()];
        const result = await SearchMods(searchQuery, pageNum, pageSize, categoryFilter, selectedSortField, 2);
        const mods = result?.Mods ?? result?.mods ?? [];

        if (resetResults) {
          setSearchResults(mods);
        } else {
          setSearchResults((prev) => [...prev, ...mods]);
        }
        setHasMore(mods.length >= pageSize);
      } catch (err: unknown) {
        const error = err as Error;
        setError(error.message || 'Failed to search mods');
        if (resetResults) setSearchResults([]);
      }

      setIsSearching(false);
      setIsLoadingMore(false);
    },
    [searchQuery, selectedCategory, selectedSortField, currentPage]
  );

  // Real-time search with debounce
  useEffect(() => {
    if (!isOpen) return;

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
  }, [searchQuery, selectedCategory, selectedSortField, isOpen, handleSearch]);

  // Load more on scroll
  const handleScroll = useCallback(
    (e: React.UIEvent<HTMLDivElement>) => {
      const target = e.target as HTMLDivElement;
      const scrollBottom = target.scrollHeight - target.scrollTop - target.clientHeight;

      if (scrollBottom < 200 && !isLoadingMore && hasMore) {
        setIsLoadingMore(true);
        setCurrentPage((prev) => prev + 1);
      }
    },
    [isLoadingMore, hasMore]
  );

  useEffect(() => {
    if (currentPage > 0 && isLoadingMore) {
      handleSearch(false);
    }
  }, [currentPage, isLoadingMore, handleSearch]);

  // Clear cache when showAlphaMods setting changes
  useEffect(() => {
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

    try {
      const result = await GetModFiles(String(modId), 0, 50);
      let files = Array.isArray(result) ? result : (result?.Files ?? result?.files ?? []);
      
      // Filter out alpha mods unless showAlphaMods is enabled
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
    }
  };

  // Handle mod click - show in detail panel
  const handleModClick = async (mod: CurseForgeMod) => {
    setSelectedMod(mod);
    setActiveScreenshot(0);
    setIsLoadingModFiles(true);

    const modKey = normalizeModKey(mod.id);
    if (mod.id) {
      const files = await loadModFiles(mod.id);
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
    setIsLoadingModFiles(false);
  };

  // Toggle mod selection
  const toggleModSelection = async (mod: CurseForgeMod) => {
    const modId = mod.id;
    setSelectedMods((prev) => {
      const next = new Set(prev);
      if (next.has(modId)) {
        next.delete(modId);
      } else {
        next.add(modId);
        loadModFiles(modId);
      }
      return next;
    });
  };

  // Run download queue
  const runDownloadQueue = async (items: Array<{ id: string | number; name: string; fileId: string | number }>) => {
    const maxRetries = 3;

    setIsDownloading(true);
    setDownloadProgress({ current: 0, total: items.length, currentMod: '' });
    setDownloadJobs(items.map((item) => ({ id: item.id, name: item.name, status: 'pending', attempts: 0 })));

    let completed = 0;
    let successCount = 0;

    for (const item of items) {
      for (let attempt = 1; attempt <= maxRetries; attempt++) {
        setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: 'running', attempts: attempt } : j));
        try {
          console.log(`[BrowseModsModal] Downloading ${item.name} (attempt ${attempt}/${maxRetries})`);
          const ok = await InstallModFileToInstance(String(item.id), String(item.fileId), currentBranch, currentVersion);
          if (!ok) {
            throw new Error(t('modManager.backendRefused'));
          }
          console.log(`[BrowseModsModal] Successfully installed ${item.name}`);
          setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: 'success', attempts: attempt } : j));
          successCount++;
          break;
        } catch (err: unknown) {
          const error = err as Error;
          console.error(`[BrowseModsModal] Failed to install ${item.name} (attempt ${attempt}):`, error?.message);
          const isLast = attempt === maxRetries;
          setDownloadJobs((prev) => prev.map((j) => j.id === item.id ? { ...j, status: isLast ? 'error' : 'pending', attempts: attempt, error: error?.message } : j));
          if (!isLast) {
            await new Promise(resolve => setTimeout(resolve, 500 * attempt));
          }
        }
      }
      completed += 1;
      setDownloadProgress({ current: completed, total: items.length, currentMod: item.name });
    }

    console.log(`[BrowseModsModal] Download complete: ${successCount}/${items.length} mods installed successfully`);
  };

  // Handle download selected mods
  const handleDownloadSelected = async () => {
    if (selectedMods.size === 0) return;

    const items: Array<{ id: string | number; name: string; fileId: string | number }> = [];

    for (const modId of selectedMods) {
      const mod = searchResults.find((m) => m.id === modId);
      const modKey = normalizeModKey(modId);
      
      let fileId = selectedVersions.get(modKey);
      
      if (!fileId) {
        const files = await loadModFiles(modId);
        fileId = files?.[0]?.id;
      }

      if (fileId && mod) {
        items.push({ id: modId, name: mod.name, fileId });
      }
    }

    if (items.length === 0) {
      setError(t('modManager.noDownloadableFiles'));
      return;
    }

    try {
      await runDownloadQueue(items);
    } catch (err: unknown) {
      const error = err as Error;
      setError(error?.message || 'Failed to download mods');
    }

    setIsDownloading(false);
    setDownloadProgress(null);
    setDownloadJobs([]);
    setSelectedMods(new Set());
    onModsInstalled?.();
  };

  // Handle browse for mod files
  const handleBrowseModFiles = async () => {
    try {
      const filePaths = await BrowseModFiles();
      if (!filePaths || filePaths.length === 0) return;
      
      setIsImporting(true);
      let successCount = 0;
      
      for (const filePath of filePaths) {
        const fileName = filePath.split(/[/\\]/).pop() || '';
        setImportProgress(t('modManager.installingMod').replace('{{name}}', fileName));
        const success = await InstallLocalModFile(filePath, currentBranch, currentVersion);
        if (success) {
          successCount++;
        }
      }
      
      if (successCount > 0) {
        setImportProgress(t('modManager.installedCount').replace('{{count}}', successCount.toString()));
        setTimeout(() => setImportProgress(null), 3000);
        onModsInstalled?.();
      } else {
        setError(t('modManager.installFailed'));
      }
    } catch (err: unknown) {
      const error = err as Error;
      setError(error?.message || t('modManager.importFailed'));
    } finally {
      setIsImporting(false);
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
    if (e.currentTarget === e.target) {
      setIsDragging(false);
    }
  };
  
  const readFileAsBase64 = (file: File): Promise<string> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => {
        const arrayBuffer = reader.result as ArrayBuffer;
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
    
    const files = Array.from(e.dataTransfer.files);
    if (files.length === 0) return;
    
    setIsImporting(true);
    let successCount = 0;
    
    try {
      const firstFile = files[0] as unknown as { path?: string };
      const hasFilePath = !!firstFile.path;
      
      for (const file of files) {
        const filePath = (file as unknown as { path?: string }).path;
        setImportProgress(t('modManager.installingMod').replace('{{name}}', file.name));
        
        if (hasFilePath && filePath) {
          const success = await InstallLocalModFile(filePath, currentBranch, currentVersion);
          if (success) successCount++;
        } else {
          try {
            const base64Content = await readFileAsBase64(file);
            const success = await InstallModFromBase64(file.name, base64Content, currentBranch, currentVersion);
            if (success) successCount++;
          } catch (readErr) {
            console.error('[BrowseModsModal] Failed to read file:', file.name, readErr);
          }
        }
      }
      
      if (successCount > 0) {
        setImportProgress(t('modManager.installedCount').replace('{{count}}', successCount.toString()));
        setTimeout(() => setImportProgress(null), 3000);
        onModsInstalled?.();
      } else {
        setError(t('modManager.installFailed'));
      }
    } catch (err: unknown) {
      const error = err as Error;
      setError(error?.message || t('modManager.importFailed'));
    } finally {
      setIsImporting(false);
    }
  };

  const getCategoryName = () => {
    if (selectedCategory === 0) return t('common.all');
    const cat = categories.find((c) => c.id === selectedCategory);
    return cat ? t(cat.name) : t('common.all');
  };

  const screenshots = selectedMod?.screenshots || [];

  if (!isOpen) return null;

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className={`fixed inset-0 z-[100] flex items-center justify-center p-8 bg-[#0a0a0a]/95`}
        onClick={(e) => e.target === e.currentTarget && onClose()}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
      >
        <motion.div
          initial={{ scale: 0.95, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          exit={{ scale: 0.95, opacity: 0 }}
          className={`w-full max-w-6xl h-[85vh] flex flex-col overflow-hidden transition-colors glass-panel-static-solid ${isDragging ? '!border-2' : ''}`}
          style={isDragging ? { borderColor: accentColor } : undefined}
        >
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
          
          {/* Success message */}
          {importProgress && !isImporting && (
            <div className="absolute top-4 left-1/2 -translate-x-1/2 z-[100] px-4 py-2 rounded-xl text-sm" style={{ backgroundColor: accentColor, color: accentTextColor }}>
              {importProgress}
            </div>
          )}

          {/* Header */}
          <div className="p-4 border-b border-white/10 flex items-center justify-between flex-shrink-0">
            <div className="flex items-center gap-3">
              <Package size={24} style={{ color: accentColor }} />
              <div>
                <h2 className="text-lg font-bold text-white">{t('modManager.browseMods')}</h2>
                <p className="text-xs text-white/40">
                  {currentBranch} {currentVersion === 0 ? t('common.latest') : `v${currentVersion}`}
                </p>
              </div>
            </div>
            <div className="flex items-center gap-1">
              <button
                onClick={handleDownloadSelected}
                disabled={isDownloading || selectedMods.size === 0}
                className={`px-4 py-2 rounded-xl text-sm font-medium flex items-center gap-2 ${
                  selectedMods.size > 0 ? '' : 'opacity-50 cursor-not-allowed'
                }`}
                style={selectedMods.size > 0 ? { backgroundColor: accentColor, color: accentTextColor } : { backgroundColor: 'rgba(255,255,255,0.1)', color: 'rgba(255,255,255,0.5)' }}
              >
                <Download size={16} />
                {t('modManager.installSelected')} {selectedMods.size > 0 && `(${selectedMods.size})`}
              </button>
              <button
                onClick={handleBrowseModFiles}
                disabled={isImporting}
                className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white"
                title={t('modManager.addMods')}
              >
                <FilePlus2 size={20} />
              </button>
              <button onClick={onClose} className="p-2 rounded-xl hover:bg-white/10 text-white/60 hover:text-white">
                <X size={20} />
              </button>
            </div>
          </div>

          {/* Search bar */}
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
                    className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${selectedCategory === 0 ? 'bg-white/5' : 'text-white/70'}`}
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
                      className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${selectedCategory === cat.id ? 'bg-white/5' : 'text-white/70'}`}
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
                      className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors flex items-center justify-between ${selectedSortField === option.id ? 'bg-white/5' : 'text-white/70'}`}
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
              onScroll={handleScroll}
            >
              {isSearching && searchResults.length === 0 ? (
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
                  {searchResults.map((mod) => {
                    const isSelected = selectedMods.has(mod.id);
                    const isViewing = selectedMod?.id === mod.id;

                    return (
                      <div
                        key={mod.id}
                        className={`p-3 rounded-xl border cursor-pointer ${
                          isViewing
                            ? ''
                            : 'bg-white/5 border-white/10 hover:border-white/20'
                        }`}
                        style={isViewing ? { backgroundColor: `${accentColor}33`, borderColor: accentColor } : undefined}
                        onClick={() => handleModClick(mod)}
                      >
                        <div className="flex items-center gap-3">
                          {/* Selection checkbox */}
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              toggleModSelection(mod);
                            }}
                            className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${
                              isSelected ? '' : 'bg-transparent border-white/30 hover:border-white/50'
                            }`}
                            style={isSelected ? { backgroundColor: accentColor, borderColor: accentColor } : undefined}
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
              )}
            </div>

            {/* Right side - Detail Panel */}
            <div className="w-1/2 h-full flex flex-col border-l border-white/10 flex-shrink-0">
              {selectedMod ? (
                <>
                  {/* Detail Header */}
                  <div className="p-4 border-b border-white/10 flex items-center gap-4 flex-shrink-0">
                    <div className="w-14 h-14 rounded-xl bg-white/10 flex items-center justify-center overflow-hidden flex-shrink-0">
                      {(selectedMod.iconUrl || selectedMod.logo?.thumbnailUrl) ? (
                        <img loading="lazy" src={selectedMod.iconUrl || selectedMod.logo?.thumbnailUrl} alt="" className="w-full h-full object-cover" />
                      ) : (
                        <Package size={24} className="text-white/40" />
                      )}
                    </div>
                    <div className="flex-1 min-w-0">
                      <button
                        onClick={() => {
                          if (selectedMod.slug) {
                            BrowserOpenURL(`https://www.curseforge.com/hytale/mods/${selectedMod.slug}`);
                          }
                        }}
                        className="text-lg font-bold text-white truncate hover:opacity-80 text-left block w-full"
                      >
                        {selectedMod.name}
                      </button>
                      <p className="text-white/50 text-sm truncate">
                        {selectedMod.author || selectedMod.authors?.[0]?.name || ''}
                      </p>
                      {/* Categories */}
                      {selectedMod.categories && selectedMod.categories.length > 0 && (
                        <div className="flex items-center gap-1.5 flex-wrap mt-1.5">
                          {selectedMod.categories.map((cat, idx) => {
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
                        {selectedMod.summary || t('modManager.noDescription')}
                      </p>
                    </div>

                    {/* Screenshots */}
                    {screenshots.length > 0 && (
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

                  {/* Fixed Footer - Version + Install Button */}
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
                              const modKey = normalizeModKey(selectedMod.id);
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

                    {/* Install button */}
                    <button
                      onClick={() => toggleModSelection(selectedMod)}
                      className={`w-full py-3 rounded-xl text-sm font-medium ${
                        selectedMods.has(selectedMod.id)
                          ? ''
                          : 'bg-white/10 text-white hover:bg-white/20'
                      }`}
                      style={selectedMods.has(selectedMod.id) ? { backgroundColor: accentColor, color: accentTextColor } : undefined}
                    >
                      {selectedMods.has(selectedMod.id)
                        ? t('modManager.selectedForInstall')
                        : t('modManager.selectForInstall')}
                    </button>
                  </div>
                </>
              ) : (
                // Empty state
                <div className="h-full flex flex-col items-center justify-center text-white/30">
                  <Package size={48} className="mb-4 opacity-50" />
                  <p className="text-sm">{t('modManager.selectModToView')}</p>
                </div>
              )}
            </div>
          </div>
        </motion.div>

        {/* Download Progress Overlay */}
        {isDownloading && downloadProgress && (
          <div className={`fixed inset-0 flex items-center justify-center z-[200] bg-[#0a0a0a]/90`}>
            <div className="bg-black/80 border border-white/10 rounded-2xl p-6 max-w-md w-full mx-4 shadow-xl">
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
                    {job.status === 'pending' && <Download size={14} className="text-white/50" />}
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
            className={`fixed inset-0 flex items-center justify-center z-[250] bg-[#0a0a0a]/98`}
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
      </motion.div>
    </AnimatePresence>
  );
};
