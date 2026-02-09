import React, { useState, useEffect, useCallback } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { HardDrive, FolderOpen, Trash2, Download, Upload, RefreshCw, Clock, Calendar, Box, Loader2, AlertTriangle, Check } from 'lucide-react';
import { useAccentColor } from '../contexts/AccentColorContext';
import { ipc, InstalledInstance } from '@/lib/ipc';
import { formatBytes } from '../utils/format';

// IPC stubs - only for features not yet implemented
const _stub = <T,>(name: string, fb: T) => async (..._a: any[]): Promise<T> => { console.warn(`[IPC] ${name}: no channel`); return fb; };
const ExportInstance = _stub('ExportInstance', '');
const DeleteGame = _stub('DeleteGame', false);
const OpenInstanceFolder = _stub('OpenInstanceFolder', undefined as void);
const ImportInstanceFromZip = _stub('ImportInstanceFromZip', true);
const GetCustomInstanceDir = async (): Promise<string> => { return (await ipc.settings.get()).dataDirectory ?? ''; };

// Convert InstalledInstance to InstalledVersionInfo
const toVersionInfo = (inst: InstalledInstance): InstalledVersionInfo => ({
  branch: inst.branch,
  version: inst.version,
  path: inst.path,
  sizeBytes: inst.totalSize,
  isLatest: false,
  isLatestInstance: inst.version === 0,
});

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

const pageVariants = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -12 },
};

interface InstancesPageProps {
  onInstanceDeleted?: () => void;
}

export const InstancesPage: React.FC<InstancesPageProps> = ({ onInstanceDeleted }) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();
  const [instances, setInstances] = useState<InstalledVersionInfo[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [instanceDir, setInstanceDir] = useState('');
  const [instanceToDelete, setInstanceToDelete] = useState<InstalledVersionInfo | null>(null);
  const [exportingInstance, setExportingInstance] = useState<string | null>(null);
  const [isImporting, setIsImporting] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);

  const loadInstances = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await ipc.game.instances();
      console.log('[InstancesPage] Loaded instances:', data);
      setInstances((data || []).map(toVersionInfo));
    } catch (err) {
      console.error('Failed to load instances:', err);
    }
    setIsLoading(false);
  }, []);

  useEffect(() => {
    loadInstances();
    GetCustomInstanceDir().then(dir => dir && setInstanceDir(dir)).catch(() => {});
  }, [loadInstances]);

  const handleExport = async (inst: InstalledVersionInfo) => {
    const key = `${inst.branch}-${inst.version}`;
    setExportingInstance(key);
    try {
      const result = await ExportInstance(inst.branch, inst.version);
      if (result) {
        setMessage({ type: 'success', text: t('instances.exportedSuccess') });
      } else {
        setMessage({ type: 'error', text: t('instances.exportFailed') });
      }
    } catch (err) {
      setMessage({ type: 'error', text: t('instances.exportFailed') });
    }
    setExportingInstance(null);
    setTimeout(() => setMessage(null), 3000);
  };

  const handleDelete = async (inst: InstalledVersionInfo) => {
    try {
      await DeleteGame(inst.branch, inst.version);
      setInstanceToDelete(null);
      loadInstances();
      onInstanceDeleted?.();
      setMessage({ type: 'success', text: t('instances.deleted') });
      setTimeout(() => setMessage(null), 3000);
    } catch (err) {
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
    } catch (err) {
      setMessage({ type: 'error', text: t('instances.importFailed') });
    }
    setIsImporting(false);
    setTimeout(() => setMessage(null), 3000);
  };

  const handleOpenFolder = (inst: InstalledVersionInfo) => {
    OpenInstanceFolder(inst.branch, inst.version);
  };

  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full flex flex-col px-8 pt-14 pb-28"
    >
      {/* Header */}
      <div className="flex items-center justify-between mb-6 flex-shrink-0">
        <div className="flex items-center gap-3">
          <HardDrive size={22} className="text-white/80" />
          <h1 className="text-xl font-bold text-white">{t('instances.title')}</h1>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={handleImport}
            disabled={isImporting}
            className="px-4 py-2 rounded-xl text-sm font-medium flex items-center gap-2 transition-all hover:brightness-110 active:scale-95"
            style={{
              background: 'rgba(255,255,255,0.06)',
              border: '1px solid rgba(255,255,255,0.1)',
              color: 'rgba(255,255,255,0.7)',
            }}
          >
            {isImporting ? <Loader2 size={14} className="animate-spin" /> : <Upload size={14} />}
            {t('instances.import')}
          </button>
          <button
            onClick={loadInstances}
            disabled={isLoading}
            className="p-2 rounded-xl text-white/50 hover:text-white hover:bg-white/5 transition-all active:scale-95"
          >
            <RefreshCw size={16} className={isLoading ? 'animate-spin' : ''} />
          </button>
        </div>
      </div>

      {/* Message */}
      <AnimatePresence>
        {message && (
          <motion.div
            initial={{ opacity: 0, y: -10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            className={`mb-4 px-4 py-2 rounded-xl text-sm flex items-center gap-2 flex-shrink-0 ${
              message.type === 'success' ? 'bg-green-500/10 text-green-400 border border-green-500/20' : 'bg-red-500/10 text-red-400 border border-red-500/20'
            }`}
          >
            {message.type === 'success' ? <Check size={14} /> : <AlertTriangle size={14} />}
            {message.text}
          </motion.div>
        )}
      </AnimatePresence>

      {/* Instance Dir */}
      {instanceDir && (
        <div className="mb-4 px-3 py-2 rounded-xl bg-white/5 border border-white/5 text-xs text-white/40 flex-shrink-0">
          <span className="text-white/25">{t('instances.storage')}:</span> {instanceDir}
        </div>
      )}

      {/* Content */}
      {isLoading ? (
        <div className="flex-1 flex items-center justify-center">
          <Loader2 size={32} className="animate-spin" style={{ color: accentColor }} />
        </div>
      ) : instances.length === 0 ? (
        <div className="flex-1 flex items-center justify-center">
          <div className="text-center">
            <Box size={48} className="text-white/15 mx-auto mb-4" />
            <p className="text-white/40 text-lg">{t('instances.noInstances')}</p>
            <p className="text-white/25 text-sm mt-2">{t('instances.downloadFromDashboard')}</p>
          </div>
        </div>
      ) : (
        <div className="flex-1 overflow-y-auto pr-1 -mr-1">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            {instances.map((inst, i) => {
              const key = `${inst.branch}-${inst.version}`;
              const isExporting = exportingInstance === key;
              return (
                <motion.div
                  key={key}
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ delay: i * 0.04 }}
                  className="p-4 rounded-2xl flex flex-col gap-3 group"
                  style={{
                    background: 'rgba(255,255,255,0.04)',
                    border: '1px solid rgba(255,255,255,0.08)',
                  }}
                >
                  {/* Top row: name + actions */}
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-3">
                      <div
                        className="w-10 h-10 rounded-xl flex items-center justify-center text-sm font-bold"
                        style={{ backgroundColor: `${accentColor}20`, color: accentColor }}
                      >
                        {inst.isLatestInstance ? '⟳' : `v${inst.version}`}
                      </div>
                      <div>
                        <div className="text-white font-semibold text-sm flex items-center gap-2">
                          {inst.isLatestInstance ? t('common.latest') : `v${inst.version}`}
                          <span className="text-white/30 text-xs font-normal capitalize">{inst.branch}</span>
                        </div>
                        {inst.sizeBytes != null && inst.sizeBytes > 0 && (
                          <div className="text-white/30 text-xs">{formatBytes(inst.sizeBytes)}</div>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                      <button onClick={() => handleOpenFolder(inst)}
                        className="p-2 rounded-lg text-white/40 hover:text-white hover:bg-white/10 transition-all" title={t('common.openFolder')}>
                        <FolderOpen size={14} />
                      </button>
                      <button onClick={() => handleExport(inst)} disabled={isExporting}
                        className="p-2 rounded-lg text-white/40 hover:text-white hover:bg-white/10 transition-all" title={t('common.export')}>
                        {isExporting ? <Loader2 size={14} className="animate-spin" /> : <Download size={14} />}
                      </button>
                      <button onClick={() => setInstanceToDelete(inst)}
                        className="p-2 rounded-lg text-white/40 hover:text-red-400 hover:bg-red-400/10 transition-all" title={t('common.delete')}>
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </div>

                  {/* Stats */}
                  <div className="flex items-center gap-4 text-[10px] text-white/30">
                    {inst.playTimeFormatted && (
                      <span className="flex items-center gap-1"><Clock size={10} />{inst.playTimeFormatted}</span>
                    )}
                    {inst.lastPlayedAt && (
                      <span className="flex items-center gap-1"><Calendar size={10} />{new Date(inst.lastPlayedAt).toLocaleDateString()}</span>
                    )}
                  </div>
                </motion.div>
              );
            })}
          </div>
        </div>
      )}

      {/* Delete Confirmation */}
      <AnimatePresence>
        {instanceToDelete && (
          <motion.div
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            className="fixed inset-0 z-[300] flex items-center justify-center bg-black/60 backdrop-blur-sm"
            onClick={(e) => e.target === e.currentTarget && setInstanceToDelete(null)}
          >
            <motion.div
              initial={{ scale: 0.95, opacity: 0 }}
              animate={{ scale: 1, opacity: 1 }}
              exit={{ scale: 0.95, opacity: 0 }}
              className="bg-[#1a1a1a] border border-white/10 rounded-2xl p-6 max-w-sm mx-4 shadow-2xl"
            >
              <h3 className="text-white font-bold text-lg mb-2">{t('instances.deleteTitle')}</h3>
              <p className="text-white/60 text-sm mb-4">
                {t('instances.deleteConfirm')} <strong>{instanceToDelete.isLatestInstance ? t('common.latest') : `v${instanceToDelete.version}`}</strong> ({instanceToDelete.branch})?
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
    </motion.div>
  );
};
