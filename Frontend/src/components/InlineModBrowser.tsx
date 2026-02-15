import React, { useState, useEffect, useCallback, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Search, Download, Package, Loader2, AlertCircle,
  Check, ChevronDown, Upload,
  ArrowLeft, X
} from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useAccentColor } from '../contexts/AccentColorContext';
import { ipc, type ModInfo, type ModCategory, type ModFileInfo } from '@/lib/ipc';

// ------- Helpers -------

const formatDownloads = (count: number): string => {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`;
  if (count >= 1_000) return `${(count / 1_000).toFixed(1)}K`;
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

const readFileAsBase64 = (file: File): Promise<string> => {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => {
      const ab = reader.result as ArrayBuffer;
      const bytes = new Uint8Array(ab);
      let binary = '';
      for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
      resolve(btoa(binary));
    };
    reader.onerror = () => reject(reader.error);
    reader.readAsArrayBuffer(file);
  });
};

// ------- Types -------

type DownloadJob = {
  id: string;
  name: string;
  status: 'pending' | 'running' | 'success' | 'error';
  attempts: number;
  error?: string;
};

interface InlineModBrowserProps {
  currentInstanceId?: string;
  currentBranch: string;
  currentVersion: number;
  installedModIds?: Set<string>;
  installedFileIds?: Set<string>;
  onModsInstalled?: () => void;
  onBack?: () => void;
  refreshSignal?: number;
}

// ------- Component -------

export const InlineModBrowser: React.FC<InlineModBrowserProps> = ({
  currentInstanceId,
  currentBranch,
  currentVersion,
  installedModIds,
  installedFileIds,
  onModsInstalled,
  onBack,
  refreshSignal,
}) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();
  const normalizeId = (value: string | number | null | undefined) => String(value ?? '');

  // --- Search state ---
  const [searchQuery, setSearchQuery] = useState('');
  const [searchResults, setSearchResults] = useState<ModInfo[]>([]);
  const [categories, setCategories] = useState<ModCategory[]>([]);
  const [selectedCategory, setSelectedCategory] = useState(0);
  const [selectedSortField, setSelectedSortField] = useState(6);
  const [isSearching, setIsSearching] = useState(false);
  const [hasSearched, setHasSearched] = useState(false);
  const [currentPage, setCurrentPage] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoadingMore, setIsLoadingMore] = useState(false);
  const [isCategoryDropdownOpen, setIsCategoryDropdownOpen] = useState(false);
  const [isSortDropdownOpen, setIsSortDropdownOpen] = useState(false);
  const [selectedMods, setSelectedMods] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const browseSelectionAnchorRef = useRef<number | null>(null);

  // --- Mod files cache ---
  const [modFilesCache, setModFilesCache] = useState<Map<string, ModFileInfo[]>>(new Map());
  const [selectedVersions, setSelectedVersions] = useState<Map<string, string>>(new Map());

  // --- Detail panel ---
  const [selectedMod, setSelectedMod] = useState<ModInfo | null>(null);
  const [selectedModFiles, setSelectedModFiles] = useState<ModFileInfo[]>([]);
  const [isLoadingModFiles, setIsLoadingModFiles] = useState(false);
  const [detailSelectedFileId, setDetailSelectedFileId] = useState<string | undefined>();
  const [activeScreenshot, setActiveScreenshot] = useState(0);
  const [fullscreenImage, setFullscreenImage] = useState<{ url: string; title: string } | null>(null);

  // --- Download ---
  const [isDownloading, setIsDownloading] = useState(false);
  const [downloadProgress, setDownloadProgress] = useState<{ processed: number; total: number; currentMod: string; success: number; failed: number } | null>(null);
  const [downloadJobs, setDownloadJobs] = useState<DownloadJob[]>([]);

  // --- Import ---
  const [isDragging, setIsDragging] = useState(false);
  const [isImporting, setIsImporting] = useState(false);
  const [importProgress, setImportProgress] = useState<string | null>(null);

  // --- Refs ---
  const searchTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const categoryDropdownRef = useRef<HTMLDivElement>(null);
  const sortDropdownRef = useRef<HTMLDivElement>(null);

  // --- Sort options ---
  const sortOptions = [
    { id: 1, name: t('modManager.sortRelevancy') },
    { id: 2, name: t('modManager.sortPopularity') },
    { id: 3, name: t('modManager.sortLatestUpdate') },
    { id: 11, name: t('modManager.sortCreationDate') },
    { id: 6, name: t('modManager.sortTotalDownloads') },
  ];

  // ------- Data loading -------

  useEffect(() => {
    ipc.mods.categories().then(cats => setCategories(cats || [])).catch(() => {});
  }, []);

  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (categoryDropdownRef.current && !categoryDropdownRef.current.contains(e.target as Node))
        setIsCategoryDropdownOpen(false);
      if (sortDropdownRef.current && !sortDropdownRef.current.contains(e.target as Node))
        setIsSortDropdownOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  const handleSearch = useCallback(async (page = 0, append = false) => {
    if (!append) setIsSearching(true);
    else setIsLoadingMore(true);

    try {
      const pageSize = 20;
      const cats = selectedCategory === 0 ? [] : [selectedCategory.toString()];

      const result = await ipc.mods.search({
        query: searchQuery,
        page,
        pageSize,
        categories: cats,
        sortField: selectedSortField,
        sortOrder: 1, // desc
      });

      const mods: ModInfo[] = result?.mods ?? [];

      if (append) {
        setSearchResults(prev => [...prev, ...mods]);
      } else {
        setSearchResults(mods);
      }
      setTotalCount(result?.totalCount ?? 0);
      setHasMore(mods.length >= pageSize);
      setCurrentPage(page);
    } catch (err: unknown) {
      const e = err as Error;
      setError(e.message || t('modManager.searchFailed'));
      if (!append) setSearchResults([]);
    }

    setIsSearching(false);
    setIsLoadingMore(false);
    setHasSearched(true);
  }, [searchQuery, selectedCategory, selectedSortField]);

  // Debounced search on query/filter changes
  useEffect(() => {
    if (searchTimeoutRef.current) clearTimeout(searchTimeoutRef.current);
    searchTimeoutRef.current = setTimeout(() => handleSearch(0, false), 300);
    return () => { if (searchTimeoutRef.current) clearTimeout(searchTimeoutRef.current); };
  }, [searchQuery, selectedCategory, selectedSortField, handleSearch]);

  // Force refresh when the parent tells us the Browse tab became active again.
  useEffect(() => {
    if (refreshSignal == null) return;
    if (!hasSearched) return;
    void handleSearch(0, false);
  }, [refreshSignal, hasSearched, handleSearch]);

  // Infinite scroll handler
  const handleScroll = useCallback((e: React.UIEvent<HTMLDivElement>) => {
    const target = e.target as HTMLDivElement;
    const scrollBottom = target.scrollHeight - target.scrollTop - target.clientHeight;
    if (scrollBottom < 200 && !isLoadingMore && !isSearching && hasMore) {
      handleSearch(currentPage + 1, true);
    }
  }, [isLoadingMore, isSearching, hasMore, currentPage, handleSearch]);

  // ------- Mod files -------

  const loadModFiles = async (modId: string): Promise<ModFileInfo[]> => {
    if (modFilesCache.has(modId)) return modFilesCache.get(modId) || [];

    try {
      const result = await ipc.mods.files({ modId, pageSize: 50 });
      let files = result?.files ?? [];
      files.sort((a, b) => new Date(b.fileDate).getTime() - new Date(a.fileDate).getTime());
      setModFilesCache(prev => new Map(prev).set(modId, files));
      if (files.length > 0 && !selectedVersions.has(modId)) {
        setSelectedVersions(prev => new Map(prev).set(modId, files[0].id));
      }
      return files;
    } catch {
      return [];
    }
  };

  const handleModClick = async (mod: ModInfo) => {
    setSelectedMod(mod);
    setActiveScreenshot(0);
    setIsLoadingModFiles(true);

    const modId = normalizeId(mod.id);
    if (modId) {
      const files = await loadModFiles(modId);
      const selectedFileId = selectedVersions.get(modId) || files[0]?.id;
      setSelectedModFiles(files);
      setDetailSelectedFileId(selectedFileId);
      if (selectedFileId) setSelectedVersions(prev => new Map(prev).set(modId, selectedFileId));
    } else {
      setSelectedModFiles([]);
      setDetailSelectedFileId(undefined);
    }
    setIsLoadingModFiles(false);
  };

  const getCurseForgeUrl = useCallback((mod: ModInfo): string => {
    if (mod.slug) {
      return `https://www.curseforge.com/hytale/mods/${mod.slug}`;
    }
    return `https://www.curseforge.com/hytale/mods/search?search=${encodeURIComponent(mod.name || String(mod.id))}`;
  }, []);

  const handleOpenModPage = useCallback((e: React.MouseEvent, mod: ModInfo) => {
    e.preventDefault();
    e.stopPropagation();
    ipc.browser.open(getCurseForgeUrl(mod));
  }, [getCurseForgeUrl]);

  const toggleModSelection = (mod: ModInfo, index: number) => {
    const modId = normalizeId(mod.id);
    let shouldPrefetch = false;
    setSelectedMods((prev) => {
      const next = new Set(prev);
      if (next.has(modId)) {
        next.delete(modId);
      } else {
        next.add(modId);
        shouldPrefetch = true;
      }
      return next;
    });
    browseSelectionAnchorRef.current = index;
    if (shouldPrefetch) {
      void loadModFiles(modId);
    }
  };

  const handleBrowseShiftLeftClick = (e: React.MouseEvent, index: number) => {
    if (!e.shiftKey) {
      return;
    }

    e.preventDefault();

    if (searchResults.length === 0) {
      return;
    }

    const anchor = browseSelectionAnchorRef.current ?? index;
    const start = Math.min(anchor, index);
    const end = Math.max(anchor, index);
    const ids = searchResults.slice(start, end + 1).map((mod) => normalizeId(mod.id));

    setSelectedMods(new Set(ids));
    ids.forEach((id) => { void loadModFiles(id); });
  };

  // ------- Download -------

  const getInstalledModIdSet = useCallback(async (): Promise<Set<string>> => {
    try {
      const installed = await ipc.mods.installed({
        branch: currentBranch,
        version: currentVersion,
        instanceId: currentInstanceId,
      });

      const ids = new Set<string>();
      for (const m of installed || []) {
        if (m?.id) ids.add(String(m.id));
      }
      return ids;
    } catch {
      return new Set();
    }
  }, [currentBranch, currentInstanceId, currentVersion]);

  const runDownloadQueue = async (items: Array<{ id: string; name: string; fileId: string }>) => {
    const maxRetries = 3;
    setIsDownloading(true);
    setDownloadProgress({ processed: 0, total: items.length, currentMod: '', success: 0, failed: 0 });
    setDownloadJobs(items.map(i => ({ id: i.id, name: i.name, status: 'pending' as const, attempts: 0 })));

    const beforeInstalled = await getInstalledModIdSet();
    const expectedIds = items.map((i) => `cf-${i.id}`);

    let processed = 0;
    let success = 0;
    let failed = 0;
    for (const item of items) {
      let itemSucceeded = false;
      for (let attempt = 1; attempt <= maxRetries; attempt++) {
        setDownloadJobs(prev => prev.map(j => j.id === item.id ? { ...j, status: 'running', attempts: attempt } : j));
        try {
          const ok = await ipc.mods.install({ modId: item.id, fileId: item.fileId, branch: currentBranch, version: currentVersion, instanceId: currentInstanceId });
          if (!ok) throw new Error(t('modManager.backendRefused'));
          setDownloadJobs(prev => prev.map(j => j.id === item.id ? { ...j, status: 'success' } : j));
          itemSucceeded = true;
          break;
        } catch (err: unknown) {
          const e = err as Error;
          const isLast = attempt === maxRetries;
          setDownloadJobs(prev => prev.map(j => j.id === item.id ? { ...j, status: isLast ? 'error' : 'pending', error: e?.message } : j));
          if (!isLast) await new Promise(r => setTimeout(r, 500 * attempt));
        }
      }

      processed++;
      if (itemSucceeded) success++;
      else failed++;

      setDownloadProgress({ processed, total: items.length, currentMod: item.name, success, failed });
    }

    // Reconcile against actual installed mods so the counter reflects reality.
    // Poll briefly to cover cases where the backend is still finalizing writes.
    let stableCount = -1;
    let stableHits = 0;
    let afterInstalled = new Set<string>();
    for (let i = 0; i < 8; i++) {
      afterInstalled = await getInstalledModIdSet();
      const verifiedNow = expectedIds.filter((id) => afterInstalled.has(id)).length;
      if (verifiedNow === stableCount) stableHits++;
      else {
        stableCount = verifiedNow;
        stableHits = 0;
      }
      if (verifiedNow === expectedIds.length) break;
      if (stableHits >= 2) break;
      await new Promise((r) => setTimeout(r, 500));
    }

    const verifiedInstalled = expectedIds.filter((id) => afterInstalled.has(id)).length;
    const verifiedNew = expectedIds.filter((id) => afterInstalled.has(id) && !beforeInstalled.has(id)).length;

    setDownloadJobs((prev) => {
      // Mark jobs as success only if the mod is actually present after the queue.
      return prev.map((j) => {
        const expected = `cf-${j.id}`;
        if (afterInstalled.has(expected)) {
          return { ...j, status: 'success' as const };
        }
        // If it was already error, keep error; otherwise mark as error.
        if (j.status === 'error') return j;
        return { ...j, status: 'error' as const, error: t('modManager.downloadFailed') };
      });
    });

    // Prefer showing *verified* installed count for accuracy.
    // If some were already installed before, verifiedInstalled may exceed verifiedNew.
    setDownloadProgress({
      processed: items.length,
      total: items.length,
      currentMod: items[items.length - 1]?.name ?? '',
      success: verifiedInstalled,
      failed: Math.max(0, items.length - verifiedInstalled),
    });

    // Keep console signal for debugging: how many were newly added.
    console.log('[ModBrowser] Queue reconciliation:', { total: items.length, verifiedInstalled, verifiedNew });
  };

  const handleDownloadSelected = async () => {
    if (selectedMods.size === 0) return;
    if (isDownloading) return;
    try {
      console.log('[ModBrowser] handleDownloadSelected called, selectedMods:', Array.from(selectedMods));

      const items: Array<{ id: string; name: string; fileId: string }> = [];
      for (const modId of selectedMods) {
        console.log('[ModBrowser] Processing mod:', modId);
        const mod = searchResults.find(m => m.id === modId);
        console.log('[ModBrowser] Found mod in searchResults:', mod?.name || 'NOT FOUND');
        let fileId = selectedVersions.get(modId);
        console.log('[ModBrowser] Selected version fileId:', fileId || 'NOT SET');
        if (!fileId) {
          console.log('[ModBrowser] Loading files for mod:', modId);
          const files = await loadModFiles(modId);
          console.log('[ModBrowser] Loaded files:', files?.length || 0);
          fileId = files?.[0]?.id;
          console.log('[ModBrowser] Using first file:', fileId);
        }
        if (fileId && mod) {
          items.push({ id: modId, name: mod.name, fileId });
          console.log('[ModBrowser] Added to items:', { id: modId, name: mod.name, fileId });
        } else {
          console.log('[ModBrowser] SKIPPED mod (missing fileId or mod):', { modId, hasFileId: !!fileId, hasMod: !!mod });
        }
      }

      console.log('[ModBrowser] Final items array:', items);
      if (items.length === 0) {
        setError(t('modManager.noDownloadableFiles'));
        return;
      }

      await runDownloadQueue(items);
    } catch (err: unknown) {
      setError((err as Error)?.message || t('modManager.downloadFailed'));
    } finally {
      setIsDownloading(false);
      setDownloadProgress(null);
      setDownloadJobs([]);
      setSelectedMods(new Set());
      onModsInstalled?.();
    }
  };

  const handleInstallSingleMod = async (modId: string, fileId: string, name: string) => {
    if (isDownloading) return;
    try {
      await runDownloadQueue([{ id: modId, name, fileId }]);
    } catch {
      /* handled in queue */
    } finally {
      setIsDownloading(false);
      setDownloadProgress(null);
      setDownloadJobs([]);
      onModsInstalled?.();
    }
  };

  // ------- Drag & Drop -------

  const handleDragOver = (e: React.DragEvent) => { e.preventDefault(); e.stopPropagation(); setIsDragging(true); };
  const handleDragLeave = (e: React.DragEvent) => { e.preventDefault(); e.stopPropagation(); if (e.currentTarget === e.target) setIsDragging(false); };

  const handleDrop = async (e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
    const files = Array.from(e.dataTransfer.files);
    if (files.length === 0) return;
    setIsImporting(true);
    let successCount = 0;
    try {
      for (const file of files) {
        setImportProgress(t('modManager.installingMod').replace('{{name}}', file.name));
        const electronFile = file as unknown as { path?: string };
        if (electronFile.path) {
          const ok = await ipc.mods.installLocal({ sourcePath: electronFile.path, branch: currentBranch, version: currentVersion, instanceId: currentInstanceId });
          if (ok) successCount++;
        } else {
          const base64 = await readFileAsBase64(file);
          const ok = await ipc.mods.installBase64({ fileName: file.name, base64Content: base64, branch: currentBranch, version: currentVersion, instanceId: currentInstanceId });
          if (ok) successCount++;
        }
      }
      if (successCount > 0) {
        setImportProgress(t('modManager.installedCount').replace('{{count}}', successCount.toString()));
        setTimeout(() => setImportProgress(null), 3000);
        onModsInstalled?.();
      }
    } catch {
      setError(t('modManager.importFailed'));
    } finally {
      setIsImporting(false);
    }
  };

  // ------- Render -------

  const getCategoryName = (id: number) => {
    const cat = categories.find(c => c.id === id);
    if (!cat) return t('modManager.allMods');
    const key = `modManager.category.${cat.name.replace(/[\s\\/]+/g, '_').toLowerCase()}`;
    const translated = t(key);
    return translated !== key ? translated : cat.name;
  };
  const getSortName = (id: number) => sortOptions.find(s => s.id === id)?.name ?? '';

  return (
    <div
      className="h-full flex flex-col"
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      {/* Header with search, categories, sort, and back button */}
      <div className="p-4 border-b border-white/[0.06] flex flex-col gap-3 flex-shrink-0">
        <div className="flex items-center gap-3">
          {/* Back button */}
          {onBack && (
            <button
              onClick={onBack}
              className="p-2 rounded-xl text-white/60 hover:text-white hover:bg-white/10 transition-all flex-shrink-0"
              title={t('common.back')}
            >
              <ArrowLeft size={18} />
            </button>
          )}

          {/* Search input */}
          <div className="relative flex-1">
            <Search size={16} className="absolute left-3 top-1/2 -translate-y-1/2 text-white/40" />
            <input
              type="text"
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              onKeyDown={(e) => {
                if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'a') {
                  e.preventDefault();
                  e.stopPropagation();
                  (e.currentTarget as HTMLInputElement).select();
                }
              }}
              placeholder={t('modManager.searchMods')}
              className="w-full h-10 pl-10 pr-4 rounded-xl bg-[#2c2c2e] border border-white/[0.08] text-white text-sm placeholder-white/40 focus:outline-none focus:border-white/20"
              autoFocus
            />
          </div>

          {/* Category dropdown */}
          <div className="relative" ref={categoryDropdownRef}>
            <button
              onClick={() => { setIsCategoryDropdownOpen(!isCategoryDropdownOpen); setIsSortDropdownOpen(false); }}
              className="h-10 px-3 rounded-xl bg-[#2c2c2e] border border-white/[0.08] text-white/70 text-sm flex items-center gap-2 hover:border-white/20 transition-all whitespace-nowrap"
            >
              {getCategoryName(selectedCategory)}
              <ChevronDown size={14} className={`transition-transform ${isCategoryDropdownOpen ? 'rotate-180' : ''}`} />
            </button>
            {isCategoryDropdownOpen && (
              <div className="absolute right-0 top-full mt-1 w-48 bg-[#1a1a1a] border border-white/10 rounded-xl shadow-xl z-50 overflow-hidden max-h-64 overflow-y-auto">
                {categories.map(cat => (
                  <button
                    key={cat.id}
                    onClick={() => { setSelectedCategory(cat.id); setIsCategoryDropdownOpen(false); }}
                    className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${
                      selectedCategory === cat.id ? 'text-white' : 'text-white/60'
                    }`}
                    style={selectedCategory === cat.id ? { backgroundColor: `${accentColor}20` } : undefined}
                  >
                    {(() => {
                      const key = `modManager.category.${cat.name.replace(/[\s\\/]+/g, '_').toLowerCase()}`;
                      const translated = t(key);
                      return translated !== key ? translated : cat.name;
                    })()}
                  </button>
                ))}
              </div>
            )}
          </div>

          {/* Sort dropdown */}
          <div className="relative" ref={sortDropdownRef}>
            <button
              onClick={() => { setIsSortDropdownOpen(!isSortDropdownOpen); setIsCategoryDropdownOpen(false); }}
              className="h-10 px-3 rounded-xl bg-[#2c2c2e] border border-white/[0.08] text-white/70 text-sm flex items-center gap-2 hover:border-white/20 transition-all whitespace-nowrap"
            >
              {getSortName(selectedSortField)}
              <ChevronDown size={14} className={`transition-transform ${isSortDropdownOpen ? 'rotate-180' : ''}`} />
            </button>
            {isSortDropdownOpen && (
              <div className="absolute right-0 top-full mt-1 w-48 bg-[#1a1a1a] border border-white/10 rounded-xl shadow-xl z-50 overflow-hidden">
                {sortOptions.map(opt => (
                  <button
                    key={opt.id}
                    onClick={() => { setSelectedSortField(opt.id); setIsSortDropdownOpen(false); }}
                    className={`w-full px-4 py-2.5 text-sm text-left hover:bg-white/10 transition-colors ${
                      selectedSortField === opt.id ? 'text-white' : 'text-white/60'
                    }`}
                    style={selectedSortField === opt.id ? { backgroundColor: `${accentColor}20` } : undefined}
                  >
                    {opt.name}
                  </button>
                ))}
              </div>
            )}
          </div>
        </div>

        {/* Batch download bar (only when mods are selected) */}
        {selectedMods.size > 0 && !isDownloading && (
          <div className="flex items-center justify-between px-3 py-2 rounded-xl border border-white/[0.08] bg-[#2c2c2e]">
            <span className="text-sm text-white/70">
              {selectedMods.size} {t('modManager.modsSelected')}
            </span>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setSelectedMods(new Set())}
                className="px-3 py-1.5 rounded-lg text-xs text-white/60 hover:text-white hover:bg-white/10 transition-all"
              >
                {t('common.clear')}
              </button>
              <button
                onClick={handleDownloadSelected}
                className="px-3 py-1.5 rounded-lg text-xs font-medium flex items-center gap-1.5 transition-all"
                style={{ backgroundColor: accentColor, color: accentTextColor }}
              >
                <Download size={12} />
                {t('modManager.downloadSelected')}
              </button>
            </div>
          </div>
        )}

        {/* Download progress bar */}
        {isDownloading && downloadProgress && (
          <div className="px-3 py-2 rounded-xl border border-white/[0.08] bg-[#2c2c2e] space-y-2">
            <div className="flex items-center justify-between text-sm">
              <span className="text-white/70">
                {t('modManager.downloading')} ({downloadProgress.success}/{downloadProgress.total})
              </span>
              <span className="text-white/50 text-xs truncate ml-2">{downloadProgress.currentMod}</span>
            </div>
            <div className="h-1.5 bg-[#1c1c1e] rounded-full overflow-hidden">
              <div
                className="h-full rounded-full transition-all duration-300"
                style={{ width: `${(downloadProgress.processed / downloadProgress.total) * 100}%`, backgroundColor: accentColor }}
              />
            </div>
            {downloadJobs.length > 0 && (
              <div className="space-y-1 max-h-24 overflow-y-auto">
                {downloadJobs.map(job => (
                  <div key={job.id} className="flex items-center gap-2 text-xs">
                    {job.status === 'running' && <Loader2 size={10} className="animate-spin text-white/60" />}
                    {job.status === 'success' && <Check size={10} className="text-green-400" />}
                    {job.status === 'error' && <AlertCircle size={10} className="text-red-400" />}
                    {job.status === 'pending' && <div className="w-2.5 h-2.5 rounded-full bg-white/20" />}
                    <span className={`truncate ${job.status === 'error' ? 'text-red-400' : 'text-white/60'}`}>{job.name}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Error toast */}
      <AnimatePresence>
        {error && (
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className="mx-4 mt-2 px-3 py-2 rounded-xl bg-red-500/15 border border-red-500/20 text-red-400 text-sm flex items-center gap-2"
          >
            <AlertCircle size={14} />
            <span className="flex-1 truncate">{error}</span>
            <button onClick={() => setError(null)} className="p-0.5 hover:text-white transition-colors"><X size={14} /></button>
          </motion.div>
        )}
      </AnimatePresence>

      {/* Import progress */}
      {importProgress && (
        <div className="mx-4 mt-2 px-3 py-2 rounded-xl bg-blue-500/15 border border-blue-500/20 text-blue-300 text-sm flex items-center gap-2">
          {isImporting && <Loader2 size={14} className="animate-spin" />}
          {!isImporting && <Check size={14} className="text-green-400" />}
          <span>{importProgress}</span>
        </div>
      )}

      {/* Main content: grid + detail panel */}
      <div className="flex-1 flex overflow-hidden">
        {/* Mod grid */}
        <div
          ref={scrollContainerRef}
          className="overflow-y-auto p-4 min-w-0"
          onScroll={handleScroll}
          style={{
            flex: selectedMod ? '0 0 55%' : '1 1 100%',
            transition: 'flex 0.25s cubic-bezier(0.4, 0, 0.2, 1)',
          }}
        >
          {isSearching ? (
            <div className="flex items-center justify-center h-full">
              <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
            </div>
          ) : !hasSearched ? (
            <div className="flex items-center justify-center h-full">
              <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
            </div>
          ) : searchResults.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-full text-white/40">
              <Package size={48} className="mb-4 opacity-40" />
              <p className="text-lg font-medium">{t('modManager.noModsFound')}</p>
              <p className="text-sm mt-1 text-white/30">{t('modManager.noMatch')}</p>
            </div>
          ) : (
            <div className="grid grid-cols-1 gap-2">
              {searchResults.map((mod, index) => {
                const modId = normalizeId(mod.id);
                const isSelected = selectedMods.has(modId);
                const isDetailSelected = normalizeId(selectedMod?.id) === modId;
                const isInstalled = installedModIds?.has(`cf-${modId}`) ?? false;

                return (
                  <div
                    key={mod.id}
                    onClick={(e) => {
                      if (e.shiftKey) {
                        handleBrowseShiftLeftClick(e, index);
                        return;
                      }
                      browseSelectionAnchorRef.current = index;
                      handleModClick(mod);
                    }}
                    className={`group flex items-center gap-3 p-3 rounded-xl cursor-pointer transition-all border ${
                      isDetailSelected
                        ? 'border-white/20 bg-[#2c2c2e]'
                        : isSelected
                          ? 'border-white/[0.08] bg-[#252527]'
                          : 'border-transparent hover:bg-[#252527]'
                    }`}
                  >
                    {/* Checkbox */}
                    <button
                      onClick={e => {
                        e.stopPropagation();
                        if (e.shiftKey) {
                          handleBrowseShiftLeftClick(e, index);
                          return;
                        }
                        toggleModSelection(mod, index);
                      }}
                      className={`w-5 h-5 rounded border-2 flex items-center justify-center flex-shrink-0 ${
                        isSelected ? '' : 'bg-transparent border-white/30 hover:border-white/50'
                      }`}
                      style={isSelected ? { backgroundColor: accentColor, borderColor: accentColor } : undefined}
                    >
                      {isSelected && <Check size={12} style={{ color: accentTextColor }} />}
                    </button>

                    {/* Icon */}
                    <div className="w-12 h-12 rounded-lg bg-[#1c1c1e] flex items-center justify-center overflow-hidden flex-shrink-0">
                      {mod.iconUrl ? (
                        <img src={mod.iconUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                      ) : (
                        <Package size={20} className="text-white/30" />
                      )}
                    </div>

                    {/* Info */}
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2">
                        <button
                          onClick={(e) => handleOpenModPage(e, mod)}
                          className="text-white font-medium truncate hover:underline underline-offset-2 text-left"
                          title="Open CurseForge page"
                        >
                          {mod.name}
                        </button>
                        {isInstalled && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/20 text-green-400 flex-shrink-0">
                            {t('modManager.installedBadge')}
                          </span>
                        )}
                        {mod.categories?.length > 0 && typeof mod.categories[0] === 'string' && (
                          <span className="px-1.5 py-0.5 rounded text-[10px] text-white/40 bg-[#2c2c2e] flex-shrink-0">
                            {(() => {
                              const raw = mod.categories[0] as string;
                              const key = `modManager.category.${raw.replace(/[\s\\/]+/g, '_').toLowerCase()}`;
                              const translated = t(key);
                              return translated !== key ? translated : raw;
                            })()}
                          </span>
                        )}
                      </div>
                      <p className="text-white/40 text-xs truncate mt-0.5">{mod.summary}</p>
                    </div>

                    {/* Stats */}
                    <div className="flex flex-col items-end gap-1 flex-shrink-0">
                      <span className="text-white/50 text-xs flex items-center gap-1">
                        <Download size={10} />
                        {formatDownloads(mod.downloadCount)}
                      </span>
                      <span className="text-white/30 text-xs">{mod.author}</span>
                    </div>
                  </div>
                );
              })}

              {/* Infinite scroll loading indicator */}
              {isLoadingMore && (
                <div className="flex justify-center py-4">
                  <Loader2 size={24} className="animate-spin" style={{ color: accentColor }} />
                </div>
              )}
              {!hasMore && searchResults.length > 0 && (
                <p className="text-center text-white/30 text-xs py-4">{t('modManager.allModsLoaded')}</p>
              )}
            </div>
          )}
        </div>

        {/* Detail panel */}
        <AnimatePresence mode="wait">
          {selectedMod && (
            <motion.div
              key="detail"
              initial={{ opacity: 0, width: 0 }}
              animate={{ opacity: 1, width: '45%' }}
              exit={{ opacity: 0, width: 0 }}
              transition={{ duration: 0.25, ease: [0.4, 0, 0.2, 1] }}
              className="border-l border-white/[0.06] flex flex-col overflow-y-auto overflow-x-hidden flex-shrink-0"
            >
              {/* Close detail */}
              <div className="flex items-center justify-between p-4 border-b border-white/[0.06]">
                <button
                  onClick={(e) => handleOpenModPage(e, selectedMod)}
                  className="text-white font-bold text-lg truncate flex-1 text-left hover:underline underline-offset-2"
                  title="Open CurseForge page"
                >
                  {selectedMod.name}
                </button>
                <button
                  onClick={() => setSelectedMod(null)}
                  className="p-1.5 rounded-lg text-white/40 hover:text-white hover:bg-white/10 transition-all ml-2"
                >
                  <X size={16} />
                </button>
              </div>

              {/* Screenshots carousel */}
              {selectedMod.screenshots && selectedMod.screenshots.length > 0 && (
                <div className="relative px-4 pt-3">
                  <div className="aspect-video bg-[#0a0a0a] rounded-xl overflow-hidden">
                    <img
                      src={selectedMod.screenshots[activeScreenshot]?.url || selectedMod.screenshots[activeScreenshot]?.thumbnailUrl}
                      alt={selectedMod.screenshots[activeScreenshot]?.title}
                      className="w-full h-full object-cover cursor-pointer"
                      onClick={() => setFullscreenImage({
                        url: selectedMod.screenshots![activeScreenshot].url,
                        title: selectedMod.screenshots![activeScreenshot].title,
                      })}
                    />
                  </div>
                  {selectedMod.screenshots.length > 1 && (
                    <div className="flex gap-2 mt-2 overflow-x-auto pb-1">
                      {selectedMod.screenshots.map((ss, i) => (
                        <button
                          key={ss.id}
                          onClick={() => setActiveScreenshot(i)}
                          className={`w-16 h-10 rounded-lg overflow-hidden flex-shrink-0 border-2 transition-all ${
                            i === activeScreenshot ? 'border-white/40' : 'border-transparent opacity-60 hover:opacity-100'
                          }`}
                        >
                          <img src={ss.thumbnailUrl} alt="" className="w-full h-full object-cover" loading="lazy" />
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {/* Mod info */}
              <div className="p-4 space-y-4">
                <p className="text-white/60 text-sm leading-relaxed">{selectedMod.summary}</p>

                <div className="grid grid-cols-2 gap-3 text-sm">
                  <div>
                    <span className="text-white/40 text-xs">{t('modManager.author')}</span>
                    <p className="text-white/80 mt-0.5">{selectedMod.author || t('modManager.unknownAuthor')}</p>
                  </div>
                  <div>
                    <span className="text-white/40 text-xs">{t('modManager.downloads')}</span>
                    <p className="text-white/80 mt-0.5">{formatDownloads(selectedMod.downloadCount)}</p>
                  </div>
                </div>

                {/* File version selector */}
                <div>
                  <span className="text-white/40 text-xs block mb-2">{t('modManager.selectVersion')}</span>
                  {isLoadingModFiles ? (
                    <div className="flex items-center justify-center py-4">
                      <Loader2 size={18} className="animate-spin" style={{ color: accentColor }} />
                    </div>
                  ) : selectedModFiles.length === 0 ? (
                    <p className="text-white/30 text-sm">{t('modManager.noFilesAvailable')}</p>
                  ) : (
                    <div className="space-y-1.5 max-h-40 overflow-y-auto">
                      {selectedModFiles.slice(0, 10).map(file => {
                        const fileId = normalizeId(file.id);
                        const isFileInstalled = installedFileIds?.has(fileId) ?? false;
                        return (
                        <button
                          key={file.id}
                          onClick={() => {
                            const selectedModId = normalizeId(selectedMod.id);
                            setDetailSelectedFileId(fileId);
                            setSelectedVersions(prev => new Map(prev).set(selectedModId, fileId));
                          }}
                          className={`w-full text-left px-3 py-2 rounded-lg text-sm transition-all border ${
                            isFileInstalled
                              ? 'border-green-500/30 bg-green-500/10'
                              : detailSelectedFileId === fileId
                                ? 'border-white/20 bg-[#2c2c2e]'
                                : 'border-transparent hover:bg-[#252527]'
                          }`}
                        >
                          <div className="flex items-center justify-between">
                            <span className="text-white/80 truncate flex-1">{file.displayName || file.fileName}</span>
                            <div className="flex items-center gap-1.5 flex-shrink-0">
                              {isFileInstalled && (
                                <span className="px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-500/20 text-green-400">
                                  {t('modManager.installedBadge')}
                                </span>
                              )}
                              <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${
                                file.releaseType === 1 ? 'bg-green-500/20 text-green-400'
                                  : file.releaseType === 2 ? 'bg-yellow-500/20 text-yellow-400'
                                    : 'bg-red-500/20 text-red-400'
                              }`}>
                                {getReleaseTypeLabel(file.releaseType, t)}
                              </span>
                            </div>
                          </div>
                          {file.gameVersions && file.gameVersions.length > 0 && (
                            <span className="text-white/30 text-xs">{file.gameVersions.join(', ')}</span>
                          )}
                        </button>
                        );
                      })}
                    </div>
                  )}
                </div>

                {/* Install button */}
                {(() => {
                  const fileId = detailSelectedFileId || normalizeId(selectedModFiles[0]?.id);
                  const isSelectedFileInstalled = fileId ? (installedFileIds?.has(fileId) ?? false) : false;
                  return (
                    <button
                      onClick={() => {
                        const selectedModId = normalizeId(selectedMod.id);
                        if (fileId && !isSelectedFileInstalled && selectedModId) {
                          handleInstallSingleMod(selectedModId, fileId, selectedMod.name);
                        }
                      }}
                      disabled={isDownloading || selectedModFiles.length === 0 || isSelectedFileInstalled}
                      className={`w-full py-2.5 rounded-xl text-sm font-bold flex items-center justify-center gap-2 transition-all disabled:opacity-40 ${
                        isSelectedFileInstalled ? 'cursor-default' : ''
                      }`}
                      style={isSelectedFileInstalled
                        ? { backgroundColor: 'rgba(34, 197, 94, 0.15)', color: 'rgb(74, 222, 128)' }
                        : { backgroundColor: accentColor, color: accentTextColor }
                      }
                    >
                      {isDownloading ? (
                        <Loader2 size={16} className="animate-spin" />
                      ) : isSelectedFileInstalled ? (
                        <Check size={16} />
                      ) : (
                        <Download size={16} />
                      )}
                      {isSelectedFileInstalled ? t('modManager.installedBadge') : t('modManager.download')}
                    </button>
                  );
                })()}
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>

      {/* Drag overlay */}
      {isDragging && (
        <div className="absolute inset-0 z-50 flex items-center justify-center bg-black/60 border-2 border-dashed rounded-2xl" style={{ borderColor: accentColor }}>
          <div className="flex flex-col items-center gap-3">
            <Upload size={48} style={{ color: accentColor }} />
            <p className="text-white font-medium text-lg">{t('modManager.dropModsHere')}</p>
          </div>
        </div>
      )}

      {/* Fullscreen image viewer */}
      <AnimatePresence>
        {fullscreenImage && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[500] bg-black/90 flex items-center justify-center cursor-pointer"
            onClick={() => setFullscreenImage(null)}
          >
            <img src={fullscreenImage.url} alt={fullscreenImage.title} className="max-w-[90vw] max-h-[90vh] object-contain rounded-xl" />
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
};
