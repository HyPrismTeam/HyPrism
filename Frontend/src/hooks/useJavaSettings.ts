import { useState, useCallback } from 'preact/hooks';
import { ipc } from '../lib/ipc';
import { useI18n } from '../lib/i18n';

// IPC helpers
async function FileExists(path: string): Promise<boolean> { return await ipc.file.exists(path); }
async function BrowseJavaExecutable(): Promise<string> { return (await ipc.file.browseJavaExecutable()) ?? ''; }

type GcMode = 'auto' | 'g1';
type RuntimeMode = 'bundled' | 'custom';

interface UseJavaSettingsOptions {
  systemMemoryMb: number;
}

/**
 * Parse -Xmx or -Xms value from Java arguments string
 */
const parseJavaHeapMb = (args: string, flag: 'xmx' | 'xms'): number | null => {
  const match = args.match(new RegExp(`(?:^|\\s)-${flag}(\\d+(?:\\.\\d+)?)([kKmMgG])(?:\\s|$)`, 'i'));
  if (!match) return null;

  const value = Number.parseFloat(match[1]);
  if (!Number.isFinite(value) || value <= 0) return null;

  const unit = match[2].toUpperCase();
  if (unit === 'G') return Math.round(value * 1024);
  if (unit === 'K') return Math.max(1, Math.round(value / 1024));
  return Math.round(value);
};

/**
 * Insert or update -Xmx/-Xms argument
 */
const upsertJavaHeapArgument = (args: string, flag: 'Xmx' | 'Xms', ramMb: number): string => {
  const pattern = new RegExp(`(?:^|\\s)-${flag}\\S+`, 'gi');
  const sanitized = args.replace(pattern, ' ').replace(/\s+/g, ' ').trim();
  const heapArg = `-${flag}${ramMb}M`;
  return sanitized.length > 0 ? `${heapArg} ${sanitized}` : heapArg;
};

const removeJavaFlag = (args: string, pattern: RegExp): string => {
  return args.replace(pattern, ' ').replace(/\s+/g, ' ').trim();
};

const upsertJavaGcMode = (args: string, mode: GcMode): string => {
  const withoutGc = removeJavaFlag(args, /(?:^|\s)-XX:[+-]UseG1GC(?:\s|$)/gi);
  if (mode === 'auto') return withoutGc;
  return withoutGc.length > 0 ? `-XX:+UseG1GC ${withoutGc}` : '-XX:+UseG1GC';
};

const detectJavaGcMode = (args: string): GcMode => {
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

/**
 * Format RAM in GB label
 */
export const formatRamLabel = (ramMb: number): string => {
  const gb = ramMb / 1024;
  return Number.isInteger(gb) ? `${gb} GB` : `${gb.toFixed(1)} GB`;
};

/**
 * Hook to manage all Java-related settings.
 * Consolidates RAM sliders, GC mode, runtime mode, custom path logic.
 */
export function useJavaSettings({ systemMemoryMb }: UseJavaSettingsOptions) {
  const { t } = useI18n();
  
  const minJavaRamMb = 1024;
  const detectedSystemRamMb = Math.max(4096, systemMemoryMb);
  const maxJavaRamMb = Math.max(minJavaRamMb, Math.floor((detectedSystemRamMb * 0.75) / 256) * 256);

  const [javaArguments, setJavaArguments] = useState('');
  const [javaRamMb, setJavaRamMb] = useState(4096);
  const [javaInitialRamMb, setJavaInitialRamMb] = useState(1024);
  const [javaGcMode, setJavaGcMode] = useState<GcMode>('auto');
  const [javaRuntimeMode, setJavaRuntimeMode] = useState<RuntimeMode>('bundled');
  const [customJavaPath, setCustomJavaPath] = useState('');
  const [javaCustomPathError, setJavaCustomPathError] = useState('');
  const [javaArgumentsError, setJavaArgumentsError] = useState('');

  /**
   * Initialize Java settings from stored config
   */
  const loadFromSettings = useCallback((settingsSnapshot: {
    javaArguments?: string;
    useCustomJava?: boolean;
    customJavaPath?: string;
  }) => {
    const loadedJavaArgs = settingsSnapshot.javaArguments;
    const normalizedJavaArgs = typeof loadedJavaArgs === 'string' ? loadedJavaArgs : '';
    setJavaArguments(normalizedJavaArgs);

    // Parse max heap
    const parsedJavaRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xmx');
    const targetJavaRamMb = parsedJavaRamMb ?? 4096;
    const clampedJavaRamMb = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, Math.round(targetJavaRamMb / 256) * 256));
    setJavaRamMb(clampedJavaRamMb);

    // Parse initial heap
    const parsedJavaInitialRamMb = parseJavaHeapMb(normalizedJavaArgs, 'xms');
    const fallbackInitial = Math.max(minJavaRamMb, Math.min(clampedJavaRamMb, Math.floor(clampedJavaRamMb / 2 / 256) * 256 || minJavaRamMb));
    const initialRamTarget = parsedJavaInitialRamMb ?? fallbackInitial;
    const clampedInitial = Math.min(clampedJavaRamMb, Math.max(minJavaRamMb, Math.round(initialRamTarget / 256) * 256));
    setJavaInitialRamMb(clampedInitial);

    setJavaGcMode(detectJavaGcMode(normalizedJavaArgs));
    setJavaRuntimeMode(settingsSnapshot.useCustomJava ? 'custom' : 'bundled');
    setCustomJavaPath(typeof settingsSnapshot.customJavaPath === 'string' ? settingsSnapshot.customJavaPath : '');
  }, [maxJavaRamMb, minJavaRamMb]);

  const handleSaveJavaArguments = useCallback(async () => {
    const { sanitized, blocked } = sanitizeAdvancedJavaArguments(javaArguments);

    if (blocked) {
      setJavaArgumentsError(t('settings.javaSettings.jvmArgumentsBlocked'));
    } else {
      setJavaArgumentsError('');
    }

    setJavaArguments(sanitized);

    try {
      await ipc.settings.update({ javaArguments: sanitized });
      const parsedMax = parseJavaHeapMb(sanitized, 'xmx');
      if (parsedMax != null) {
        const clampedMax = Math.min(maxJavaRamMb, Math.max(minJavaRamMb, Math.round(parsedMax / 256) * 256));
        setJavaRamMb(clampedMax);

        const parsedInitial = parseJavaHeapMb(sanitized, 'xms');
        if (parsedInitial != null) {
          const clampedInitial = Math.min(clampedMax, Math.max(minJavaRamMb, Math.round(parsedInitial / 256) * 256));
          setJavaInitialRamMb(clampedInitial);
        }
      }
      setJavaGcMode(detectJavaGcMode(sanitized));
    } catch (err) {
      console.error('Failed to update Java arguments:', err);
    }
  }, [javaArguments, maxJavaRamMb, minJavaRamMb, t]);

  const handleJavaRamChange = useCallback(async (value: number) => {
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
  }, [javaArguments, javaInitialRamMb, maxJavaRamMb, minJavaRamMb]);

  const handleJavaInitialRamChange = useCallback(async (value: number) => {
    const clampedInitial = Math.min(javaRamMb, Math.max(minJavaRamMb, value));
    setJavaInitialRamMb(clampedInitial);

    const updatedJavaArgs = upsertJavaHeapArgument(javaArguments, 'Xms', clampedInitial);
    setJavaArguments(updatedJavaArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedJavaArgs });
    } catch (err) {
      console.error('Failed to update Java initial RAM arguments:', err);
    }
  }, [javaArguments, javaRamMb, minJavaRamMb]);

  const handleJavaGcModeChange = useCallback(async (mode: GcMode) => {
    setJavaGcMode(mode);
    const updatedJavaArgs = upsertJavaGcMode(javaArguments, mode);
    setJavaArguments(updatedJavaArgs);

    try {
      await ipc.settings.update({ javaArguments: updatedJavaArgs });
    } catch (err) {
      console.error('Failed to update Java GC mode:', err);
    }
  }, [javaArguments]);

  const handleJavaRuntimeModeChange = useCallback(async (mode: RuntimeMode) => {
    setJavaRuntimeMode(mode);
    setJavaCustomPathError('');

    try {
      await ipc.settings.update({ useCustomJava: mode === 'custom' });
    } catch (err) {
      console.error('Failed to update Java runtime mode:', err);
    }
  }, []);

  const handleCustomJavaPathSave = useCallback(async () => {
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
  }, [customJavaPath, t]);

  const handleBrowseCustomJavaPath = useCallback(async () => {
    try {
      const picked = await BrowseJavaExecutable();
      if (!picked) return;
      setCustomJavaPath(picked);
      setJavaCustomPathError('');
    } catch (err) {
      console.error('Failed to browse Java executable:', err);
    }
  }, []);

  return {
    // State
    javaArguments,
    javaRamMb,
    javaInitialRamMb,
    javaGcMode,
    javaRuntimeMode,
    customJavaPath,
    javaCustomPathError,
    javaArgumentsError,
    // Computed
    minJavaRamMb,
    maxJavaRamMb,
    // Setters
    setJavaArguments,
    setCustomJavaPath,
    // Handlers
    loadFromSettings,
    handleSaveJavaArguments,
    handleJavaRamChange,
    handleJavaInitialRamChange,
    handleJavaGcModeChange,
    handleJavaRuntimeModeChange,
    handleCustomJavaPathSave,
    handleBrowseCustomJavaPath,
    // Utilities
    formatRamLabel,
  };
}
