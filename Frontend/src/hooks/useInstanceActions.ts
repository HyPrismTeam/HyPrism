import { useCallback } from 'preact/hooks';
import { ipc, invoke, send, type SaveInfo } from '../lib/ipc';
import type { InstalledVersionInfo, ModInfo } from '../types';

/**
 * IPC wrapper functions for instance operations.
 * Extracted from InstancesPage to reduce component size.
 */

export const exportInstance = async (instanceId: string): Promise<string> => {
  try {
    return await invoke<string>('hyprism:instance:export', { instanceId });
  } catch (e) {
    console.warn('[IPC] ExportInstance:', e);
    return '';
  }
};

export const deleteInstance = async (instanceId: string, branch: string, version: number): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:delete', { instanceId, branch, version });
  } catch (e) {
    console.warn('[IPC] DeleteGame:', e);
    return false;
  }
};

export const openInstanceFolder = (instanceId: string): void => {
  send('hyprism:instance:openFolder', { instanceId });
};

export const importInstanceFromZip = async (): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:import');
  } catch (e) {
    console.warn('[IPC] ImportInstanceFromZip:', e);
    return false;
  }
};

export const getCustomInstanceDir = async (): Promise<string> => {
  return (await ipc.settings.get()).dataDirectory ?? '';
};

export const getInstanceInstalledMods = async (branch: string, version: number, instanceId?: string): Promise<ModInfo[]> => {
  try {
    return await invoke<ModInfo[]>('hyprism:mods:installed', { branch, version, instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceInstalledMods:', e);
    return [];
  }
};

export const uninstallInstanceMod = async (modId: string, branch: string, version: number, instanceId?: string): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:mods:uninstall', { modId, branch, version, instanceId });
  } catch (e) {
    console.warn('[IPC] UninstallInstanceMod:', e);
    return false;
  }
};

export const openInstanceModsFolder = (instanceId: string): void => {
  send('hyprism:instance:openModsFolder', { instanceId });
};

export const checkInstanceModUpdates = async (branch: string, version: number, instanceId?: string): Promise<ModInfo[]> => {
  try {
    return await invoke<ModInfo[]>('hyprism:mods:checkUpdates', { branch, version, instanceId });
  } catch (e) {
    console.warn('[IPC] CheckInstanceModUpdates:', e);
    return [];
  }
};

export const getInstanceSaves = async (instanceId: string, branch: string, version: number): Promise<SaveInfo[]> => {
  try {
    return await invoke<SaveInfo[]>('hyprism:instance:saves', { instanceId, branch, version });
  } catch (e) {
    console.warn('[IPC] GetInstanceSaves:', e);
    return [];
  }
};

export const openSaveFolder = (instanceId: string, branch: string, version: number, saveName: string): void => {
  send('hyprism:instance:openSaveFolder', { instanceId, branch, version, saveName });
};

export const deleteSaveFolder = async (instanceId: string, branch: string, version: number, saveName: string): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:deleteSave', { instanceId, branch, version, saveName });
  } catch (e) {
    console.warn('[IPC] DeleteSaveFolder:', e);
    return false;
  }
};

export const getInstanceIcon = async (instanceId: string): Promise<string | null> => {
  try {
    return await invoke<string | null>('hyprism:instance:getIcon', { instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceIcon:', e);
    return null;
  }
};

/**
 * Hook that provides instance action handlers with message state management.
 */
export function useInstanceActions(
  setMessage: (msg: { type: 'success' | 'error'; text: string } | null) => void,
  loadInstances: () => Promise<void>,
  t: (key: string, paramsOrFallback?: Record<string, string | number> | string) => string
) {
  const handleExport = useCallback(async (inst: InstalledVersionInfo, setExportingInstance: (id: string | null) => void) => {
    setExportingInstance(inst.id);
    try {
      const result = await exportInstance(inst.id);
      if (result) {
        setMessage({ type: 'success', text: t('instances.exportedSuccess') });
      }
    } catch {
      setMessage({ type: 'error', text: t('instances.exportFailed') });
    }
    setExportingInstance(null);
    setTimeout(() => setMessage(null), 3000);
  }, [setMessage, t]);

  const handleDelete = useCallback(async (
    inst: InstalledVersionInfo,
    selectedInstance: InstalledVersionInfo | null,
    setSelectedInstance: (inst: InstalledVersionInfo | null) => void,
    setInstanceToDelete: (inst: InstalledVersionInfo | null) => void,
    onInstanceDeleted?: () => void
  ) => {
    try {
      await deleteInstance(inst.id, inst.branch, inst.version);
      setInstanceToDelete(null);
      if (selectedInstance?.id === inst.id) {
        setSelectedInstance(null);
      }
      await loadInstances();
      onInstanceDeleted?.();
      setMessage({ type: 'success', text: t('instances.deleted') });
      setTimeout(() => setMessage(null), 3000);
    } catch {
      setMessage({ type: 'error', text: t('instances.deleteFailed') });
    }
  }, [loadInstances, setMessage, t]);

  const handleImport = useCallback(async (setIsImporting: (v: boolean) => void) => {
    setIsImporting(true);
    try {
      const result = await importInstanceFromZip();
      if (result) {
        setMessage({ type: 'success', text: t('instances.importedSuccess') });
        await loadInstances();
      }
    } catch {
      setMessage({ type: 'error', text: t('instances.importFailed') });
    }
    setIsImporting(false);
    setTimeout(() => setMessage(null), 3000);
  }, [loadInstances, setMessage, t]);

  return {
    handleExport,
    handleDelete,
    handleImport,
    openFolder: openInstanceFolder,
    openModsFolder: openInstanceModsFolder,
  };
}
