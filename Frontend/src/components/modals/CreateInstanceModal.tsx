import React, { useState, useEffect, useRef } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { X, ChevronDown, Check, Image, Box, Loader2, GitBranch, Lock } from 'lucide-react';
import { useAccentColor } from '../../contexts/AccentColorContext';

import { ipc, invoke, VersionInfo } from '@/lib/ipc';
import { GameBranch } from '@/constants/enums';

interface CreateInstanceModalProps {
  isOpen: boolean;
  onClose: () => void;
  onCreateStart?: (branch: string, version: number, name: string) => void;
}

export const CreateInstanceModal: React.FC<CreateInstanceModalProps> = ({
  isOpen,
  onClose,
  onCreateStart,
}) => {
  const { t } = useTranslation();
  const { accentColor, accentTextColor } = useAccentColor();

  // Form state
  const [selectedBranch, setSelectedBranch] = useState<string>(GameBranch.RELEASE);
  const [selectedVersion, setSelectedVersion] = useState<number>(0);
  const [customName, setCustomName] = useState<string>('');
  const [isNameLocked, setIsNameLocked] = useState(false); // Track if user manually edited name
  const [iconFile, setIconFile] = useState<File | null>(null);
  const [iconPreview, setIconPreview] = useState<string | null>(null);

  // UI state
  const [isBranchOpen, setIsBranchOpen] = useState(false);
  const [isVersionOpen, setIsVersionOpen] = useState(false);
  const [availableVersions, setAvailableVersions] = useState<VersionInfo[]>([]);
  const [isLoadingVersions, setIsLoadingVersions] = useState(false);
  const [isCreating, setIsCreating] = useState(false);

  const branchRef = useRef<HTMLDivElement>(null);
  const versionRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const versionsRequestIdRef = useRef(0);

  // Load versions when branch changes
  useEffect(() => {
    const loadVersions = async () => {
      const requestId = ++versionsRequestIdRef.current;
      setIsLoadingVersions(true);
      setAvailableVersions([]);
      try {
        const response = await ipc.game.versionsWithSources({ branch: selectedBranch });
        if (requestId !== versionsRequestIdRef.current || !isOpen) {
          return;
        }

        const versions = response.versions || [];
        // Add "Latest" entry at the beginning if not already present
        const hasLatest = versions.some(v => v.version === 0);
        if (!hasLatest) {
          const latestSource = response.officialSourceAvailable && response.hasOfficialAccount ? 'Official' : 'Mirror';
          versions.unshift({ version: 0, source: latestSource as 'Official' | 'Mirror', isLatest: true });
        }
        setAvailableVersions(versions);
        // Default to "Latest" (version 0) when changing branches
        setSelectedVersion(0);
      } catch (err) {
        if (requestId !== versionsRequestIdRef.current || !isOpen) {
          return;
        }
        console.error('Failed to load versions:', err);
        setAvailableVersions([{ version: 0, source: 'Mirror', isLatest: true }]);
      } finally {
        if (requestId === versionsRequestIdRef.current && isOpen) {
          setIsLoadingVersions(false);
        }
      }
    };

    if (isOpen) {
      loadVersions();
    }
  }, [selectedBranch, isOpen]);

  // Generate default name when branch/version changes (only if not manually edited)
  useEffect(() => {
    if (!isNameLocked) {
      const branchLabel = selectedBranch === GameBranch.RELEASE ? 'Release' : 'Pre-Release';
      const versionLabel = selectedVersion === 0 ? 'Latest' : `v${selectedVersion}`;
      setCustomName(`${branchLabel} ${versionLabel}`);
    }
  }, [selectedBranch, selectedVersion, isNameLocked]);

  // Close dropdowns on outside click
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (branchRef.current && !branchRef.current.contains(e.target as Node)) {
        setIsBranchOpen(false);
      }
      if (versionRef.current && !versionRef.current.contains(e.target as Node)) {
        setIsVersionOpen(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, []);

  // Reset state when modal opens
  useEffect(() => {
    if (isOpen) {
      setSelectedBranch(GameBranch.RELEASE);
      setSelectedVersion(0);
      setIsNameLocked(false);
      setIconFile(null);
      setIconPreview(null);
      setIsCreating(false);
    }
  }, [isOpen]);

  const handleIconSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      setIconFile(file);
      // Create preview URL
      const reader = new FileReader();
      reader.onloadend = () => {
        setIconPreview(reader.result as string);
      };
      reader.readAsDataURL(file);
    }
  };

  const handleCreate = async () => {
    if (isCreating) return;
    setIsCreating(true);

    try {
      // Resolve the version (0 = latest)
      const resolvedVersion = selectedVersion === 0 ? await getLatestVersion(selectedBranch) : selectedVersion;

      // Create the instance (directory + metadata only, no download)
      const createResult = await ipc.instance.create({
        branch: selectedBranch,
        version: resolvedVersion,
        customName: customName?.trim() || undefined,
      });

      // Handle icon upload if provided
      if (iconFile && createResult?.id) {
        try {
          const base64 = await fileToBase64(iconFile);
          await invoke('hyprism:instance:setIcon', {
            instanceId: createResult.id,
            iconBase64: base64
          });
        } catch (err) {
          console.warn('Failed to set icon:', err);
        }
      }

      // Notify parent (refresh instance list)
      onCreateStart?.(selectedBranch, resolvedVersion, customName);

      onClose();
    } catch (err) {
      console.error('Failed to create instance:', err);
      setIsCreating(false);
    }
  };

  const getLatestVersion = async (branch: string): Promise<number> => {
    try {
      const versions = await ipc.game.versions({ branch });
      return versions.find(v => v !== 0) || 1;
    } catch {
      return 1;
    }
  };

  const fileToBase64 = (file: File): Promise<string> => {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onloadend = () => {
        const base64 = (reader.result as string).split(',')[1];
        resolve(base64);
      };
      reader.onerror = reject;
      reader.readAsDataURL(file);
    });
  };

  if (!isOpen) return null;

  const branchLabel = selectedBranch === GameBranch.RELEASE 
    ? t('main.release') 
    : t('main.preRelease');

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className={`fixed inset-0 z-[200] flex items-center justify-center ${
          'bg-[#0a0a0a]/90'
        }`}
        onClick={(e) => e.target === e.currentTarget && onClose()}
      >
        <motion.div
          initial={{ opacity: 0, scale: 0.95, y: 20 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.95, y: 20 }}
          className={`w-full max-w-md mx-4 ${
            'glass-panel-static-solid'
          } shadow-2xl`}
        >
          {/* Header */}
          <div className="flex items-center justify-between p-5 border-b border-white/[0.06]">
            <div className="flex items-center gap-3">
              <div 
                className="w-10 h-10 rounded-xl flex items-center justify-center"
                style={{ backgroundColor: `${accentColor}20` }}
              >
                <Box size={20} style={{ color: accentColor }} />
              </div>
              <div>
                <h2 className="text-lg font-bold text-white">{t('instances.createInstance')}</h2>
                <p className="text-xs text-white/40">{t('instances.createInstanceHint')}</p>
              </div>
            </div>
            <button
              onClick={onClose}
              className="p-2 rounded-xl text-white/40 hover:text-white hover:bg-white/10 transition-colors"
            >
              <X size={20} />
            </button>
          </div>

          {/* Content */}
          <div className="p-4 space-y-3">
            {/* Instance Name + Icon row */}
            <div className="flex items-end gap-3">
              {/* Icon Preview - compact */}
              <div className="flex flex-col items-center gap-1 flex-shrink-0">
                <div 
                  className="w-14 h-10 rounded-xl border-2 border-dashed flex items-center justify-center overflow-hidden cursor-pointer hover:border-white/40 transition-colors"
                  style={{ borderColor: iconPreview ? accentColor : 'rgba(255,255,255,0.2)' }}
                  onClick={() => fileInputRef.current?.click()}
                >
                  {iconPreview ? (
                    <img src={iconPreview} alt="Instance icon" className="w-full h-full object-cover" />
                  ) : (
                    <Image size={18} className="text-white/30" />
                  )}
                </div>
              </div>
              <input
                ref={fileInputRef}
                type="file"
                accept="image/png,image/jpeg,image/webp"
                className="hidden"
                onChange={handleIconSelect}
              />
              {/* Instance Name */}
              <div className="flex-1 space-y-1">
                <label className="text-xs text-white/50">{t('instances.instanceName')}</label>
                <div className="relative">
                  <input
                    type="text"
                    value={customName}
                    onChange={(e) => {
                      setCustomName(e.target.value);
                      setIsNameLocked(true);
                    }}
                    placeholder={t('instances.instanceNamePlaceholder')}
                    className="w-full h-10 px-3 pr-9 rounded-xl bg-[#2c2c2e] border border-white/[0.06] text-white text-sm focus:outline-none focus:border-white/20 transition-colors"
                    maxLength={32}
                  />
                  {isNameLocked && (
                    <Lock size={14} className="absolute right-3 top-1/2 -translate-y-1/2 text-white/30" />
                  )}
                </div>
              </div>
            </div>

            {/* Branch & Version selectors side by side */}
            <div className="grid grid-cols-2 gap-3">
              {/* Branch Selector */}
              <div className="space-y-1">
                <label className="text-xs text-white/50">{t('common.branch')}</label>
                <div ref={branchRef} className="relative">
                  <button
                    onClick={() => {
                      setIsBranchOpen(!isBranchOpen);
                      setIsVersionOpen(false);
                    }}
                    className="w-full h-10 px-3 rounded-xl bg-[#2c2c2e] border border-white/[0.06] flex items-center justify-between text-white text-sm transition-colors hover:border-white/[0.12]"
                    style={{ borderColor: isBranchOpen ? `${accentColor}50` : undefined }}
                  >
                    <div className="flex items-center gap-2">
                      <GitBranch size={14} className="text-white/40" />
                      <span className="font-medium">{branchLabel}</span>
                    </div>
                    <ChevronDown 
                      size={14} 
                      className={`text-white/40 transition-transform ${isBranchOpen ? 'rotate-180' : ''}`} 
                    />
                  </button>

                  {isBranchOpen && (
                    <div className="absolute top-full left-0 right-0 mt-1 z-10 bg-[#2c2c2e] border border-white/[0.08] rounded-xl shadow-xl shadow-black/50 overflow-hidden">
                      {[GameBranch.RELEASE, GameBranch.PRE_RELEASE].map((branch) => (
                        <button
                          key={branch}
                          onClick={() => {
                            setSelectedBranch(branch);
                            setIsBranchOpen(false);
                          }}
                          className={`w-full px-3 py-2 flex items-center gap-2 text-sm ${
                            selectedBranch === branch
                              ? 'text-white'
                              : 'text-white/70 hover:bg-white/10 hover:text-white'
                          }`}
                          style={selectedBranch === branch ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                        >
                          {selectedBranch === branch && <Check size={12} style={{ color: accentColor }} strokeWidth={3} />}
                          <span className={selectedBranch === branch ? '' : 'ml-[18px]'}>
                            {branch === GameBranch.RELEASE ? t('main.release') : t('main.preRelease')}
                          </span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </div>

              {/* Version Selector */}
              <div className="space-y-1">
                <label className="text-xs text-white/50">{t('common.version')}</label>
                <div ref={versionRef} className="relative">
                  <button
                    onClick={() => {
                      setIsVersionOpen(!isVersionOpen);
                      setIsBranchOpen(false);
                    }}
                    disabled={isLoadingVersions}
                    className="w-full h-10 px-3 rounded-xl bg-[#2c2c2e] border border-white/[0.06] flex items-center justify-between text-white text-sm transition-colors hover:border-white/[0.12] disabled:opacity-50"
                    style={{ borderColor: isVersionOpen ? `${accentColor}50` : undefined }}
                  >
                    <span className="font-medium">
                      {isLoadingVersions ? (
                        <Loader2 size={14} className="animate-spin" />
                      ) : selectedVersion === 0 ? (
                        t('main.latest')
                      ) : (
                        `v${selectedVersion}`
                      )}
                    </span>
                    <ChevronDown 
                      size={14} 
                      className={`text-white/40 transition-transform ${isVersionOpen ? 'rotate-180' : ''}`} 
                    />
                  </button>

                  {isVersionOpen && !isLoadingVersions && (
                    <div className="absolute top-full left-0 right-0 mt-1 z-10 max-h-48 overflow-y-auto bg-[#2c2c2e] border border-white/[0.08] rounded-xl shadow-xl shadow-black/50">
                      {availableVersions.map((versionInfo) => (
                        <button
                          key={versionInfo.version}
                          onClick={() => {
                            setSelectedVersion(versionInfo.version);
                            setIsVersionOpen(false);
                          }}
                          className={`w-full px-3 py-2 flex items-center justify-between text-sm ${
                            selectedVersion === versionInfo.version
                              ? 'text-white'
                              : 'text-white/70 hover:bg-white/10 hover:text-white'
                          }`}
                          style={selectedVersion === versionInfo.version ? { backgroundColor: `${accentColor}20`, color: accentColor } : {}}
                        >
                          <div className="flex items-center gap-2">
                            {selectedVersion === versionInfo.version && <Check size={12} style={{ color: accentColor }} strokeWidth={3} />}
                            <span className={selectedVersion === versionInfo.version ? '' : 'ml-[18px]'}>
                              {versionInfo.version === 0 ? t('main.latest') : `v${versionInfo.version}`}
                            </span>
                          </div>
                          <span className="text-[10px] text-white/30 uppercase tracking-wider">
                            {versionInfo.source === 'Official' ? 'Hytale' : 'Mirror'}
                          </span>
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              </div>
            </div>
          </div>

          {/* Footer */}
          <div className="flex items-center justify-end gap-3 p-4 border-t border-white/[0.06]">
            <button
              onClick={onClose}
              className="px-4 py-2 rounded-xl text-sm text-white/60 hover:text-white hover:bg-white/10 transition-colors"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleCreate}
              disabled={isCreating || !customName.trim()}
              className="px-5 py-2 rounded-xl text-sm font-medium transition-all disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
              style={{ backgroundColor: accentColor, color: accentTextColor }}
            >
              {isCreating ? (
                <>
                  <Loader2 size={14} className="animate-spin" />
                  {t('instances.creating')}
                </>
              ) : (
                <>
                  <Box size={14} />
                  {t('instances.create')}
                </>
              )}
            </button>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
};

export default CreateInstanceModal;
