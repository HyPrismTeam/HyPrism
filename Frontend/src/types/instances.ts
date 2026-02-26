import type { InstanceValidationDetails } from '../lib/ipc';

export interface ModInfo {
  id: string;
  name: string;
  slug?: string;
  version: string;
  fileName?: string;
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

export type InstanceTab = 'content' | 'browse' | 'worlds';
