import { useCallback } from 'react';
import { ipc, invoke, type SaveInfo } from '@/lib/ipc';
import type { InstalledVersionInfo } from '@/types';

/**
 * IPC wrapper functions for instance operations.
 * Extracted from InstancesPage to reduce component size.
 */

export const exportInstance = async (instanceId: string): Promise<string> => {
  try {
    return await ipc.instance.export({ instanceId });
  } catch (e) {
    console.warn('[IPC] ExportInstance:', e);
    return '';
  }
};

export const deleteInstance = async (instanceId: string): Promise<boolean> => {
  try {
    return await ipc.instance.delete({ instanceId });
  } catch (e) {
    console.warn('[IPC] DeleteGame:', e);
    return false;
  }
};

export const openInstanceFolder = (instanceId: string): void => {
  ipc.instance.openFolder({ instanceId });
};

export const importInstanceFromZip = async (): Promise<boolean> => {
  try {
    return await ipc.instance.import();
  } catch (e) {
    console.warn('[IPC] ImportInstanceFromZip:', e);
    return false;
  }
};

export const getCustomInstanceDir = async (): Promise<string> => {
  return (await ipc.settings.get()).dataDirectory ?? '';
};

export const getInstanceInstalledMods = async (instanceId: string): Promise<unknown[]> => {
  try {
    return await ipc.mods.installed({ instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceInstalledMods:', e);
    return [];
  }
};

export const uninstallInstanceMod = async (modId: string, instanceId: string): Promise<boolean> => {
  try {
    return await ipc.mods.uninstall({ modId, instanceId });
  } catch (e) {
    console.warn('[IPC] UninstallInstanceMod:', e);
    return false;
  }
};

export const openInstanceModsFolder = (instanceId: string): void => {
  ipc.instance.openModsFolder({ instanceId });
};

export const checkInstanceModUpdates = async (instanceId: string): Promise<unknown[]> => {
  try {
    return await ipc.mods.checkUpdates({ instanceId });
  } catch (e) {
    console.warn('[IPC] CheckInstanceModUpdates:', e);
    return [];
  }
};

export const getInstanceSaves = async (instanceId: string): Promise<SaveInfo[]> => {
  try {
    return await ipc.instance.saves({ instanceId });
  } catch (e) {
    console.warn('[IPC] GetInstanceSaves:', e);
    return [];
  }
};

export const openSaveFolder = (instanceId: string, saveName: string): void => {
  ipc.instance.openSaveFolder({ instanceId, saveName });
};

export const deleteSaveFolder = async (instanceId: string, saveName: string): Promise<boolean> => {
  try {
    return await invoke<boolean>('hyprism:instance:deleteSave', { instanceId, saveName });
  } catch (e) {
    console.warn('[IPC] DeleteSaveFolder:', e);
    return false;
  }
};

export const getInstanceIcon = async (instanceId: string): Promise<string | null> => {
  try {
    return await ipc.instance.getIcon({ instanceId });
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
  t: (key: string, params?: Record<string, unknown>) => string
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
      await deleteInstance(inst.id);
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
