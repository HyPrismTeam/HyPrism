import React, { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { X, RefreshCw, Check, User, Edit3, Copy, CheckCircle, Plus, Trash2, Dices, FolderOpen, CopyPlus } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useAccentColor } from '../contexts/AccentColorContext';
import { ipc, Profile, ProfileSnapshot } from '@/lib/ipc';
import { DeleteProfileConfirmationModal } from './modals/DeleteProfileConfirmationModal';

// TODO: These functions need IPC channels in IpcService.cs
// For now they use ipc.profile.* where possible and stubs elsewhere
async function GetNick(): Promise<string> {
  const p = await ipc.profile.get();
  return p.nick ?? 'HyPrism';
}
async function SetNick(name: string): Promise<void> {
  console.warn('[IPC] SetNick: no dedicated channel yet');
  // Could be implemented via ipc.profile.switch or a future channel
}
async function GetUUID(): Promise<string> {
  const p = await ipc.profile.get();
  return p.uuid ?? '';
}
async function SetUUID(uuid: string): Promise<void> {
  console.warn('[IPC] SetUUID: no dedicated channel yet');
}
async function GetAvatarPreview(): Promise<string | null> {
  const p = await ipc.profile.get();
  return p.avatarPath ?? null;
}
async function GetAvatarPreviewForUUID(_uuid: string): Promise<string | null> {
  console.warn('[IPC] GetAvatarPreviewForUUID: no dedicated channel yet');
  return null;
}
async function GetProfiles(): Promise<Profile[]> {
  return ipc.profile.list();
}
async function GetActiveProfileIndex(): Promise<number> {
  console.warn('[IPC] GetActiveProfileIndex: no dedicated channel yet');
  return 0;
}
async function CreateProfile(_name: string, _uuid: string): Promise<Profile | null> {
  console.warn('[IPC] CreateProfile: no dedicated channel yet');
  return null;
}
async function DeleteProfile(_id: string): Promise<boolean> {
  console.warn('[IPC] DeleteProfile: no dedicated channel yet');
  return false;
}
async function SwitchProfile(index: number): Promise<boolean> {
  const r = await ipc.profile.switch({ index });
  return r.success;
}
async function SaveCurrentAsProfile(): Promise<void> {
  console.warn('[IPC] SaveCurrentAsProfile: no dedicated channel yet');
}
async function OpenCurrentProfileFolder(): Promise<void> {
  console.warn('[IPC] OpenCurrentProfileFolder: no dedicated channel yet');
}
async function DuplicateProfileWithoutData(_id: string): Promise<Profile | null> {
  console.warn('[IPC] DuplicateProfileWithoutData: no dedicated channel yet');
  return null;
}

interface ProfileEditorProps {
    isOpen: boolean;
    onClose: () => void;
    onProfileUpdate?: () => void;
    pageMode?: boolean;
}

function generateRandomName(): string {
    // Short adjectives (max 5 chars) + short nouns (max 6 chars) + 4-digit number = max 15 chars
    const adjectives = [
        'Happy', 'Swift', 'Brave', 'Noble', 'Quiet', 'Bold', 'Lucky', 'Epic',
        'Jolly', 'Lunar', 'Solar', 'Azure', 'Royal', 'Foxy', 'Wacky', 'Zesty',
        'Fizzy', 'Dizzy', 'Funky', 'Jazzy', 'Snowy', 'Rainy', 'Sunny', 'Windy',
        'Fiery', 'Icy', 'Misty', 'Dusty', 'Rusty', 'Shiny', 'Silky', 'Fuzzy'
    ];
    const nouns = [
        'Panda', 'Tiger', 'Wolf', 'Dragon', 'Knight', 'Ranger', 'Mage', 'Fox',
        'Bear', 'Eagle', 'Hawk', 'Lion', 'Falcon', 'Raven', 'Owl', 'Shark',
        'Cobra', 'Viper', 'Lynx', 'Badger', 'Otter', 'Mantis', 'Pirate', 'Ninja',
        'Viking', 'Wizard', 'Scout', 'Hero', 'Ace', 'Star', 'King', 'Queen'
    ];
    const adj = adjectives[Math.floor(Math.random() * adjectives.length)];
    const noun = nouns[Math.floor(Math.random() * nouns.length)];
    const num = Math.floor(Math.random() * 9000) + 1000; // 4-digit number
    const name = `${adj}${noun}${num}`;
    // Safety check - truncate to 16 if somehow still too long
    return name.length <= 16 ? name : name.substring(0, 16);
}

export const ProfileEditor: React.FC<ProfileEditorProps> = ({ isOpen, onClose, onProfileUpdate, pageMode: isPageMode = false }) => {
    const { t } = useTranslation();
    const { accentColor } = useAccentColor();
    const [uuid, setUuid] = useState<string>('');
    const [username, setUsernameState] = useState<string>('');
    const [isLoading, setIsLoading] = useState(true);
    const [isEditingUsername, setIsEditingUsername] = useState(false);
    const [isEditingUuid, setIsEditingUuid] = useState(false);
    const [editUsername, setEditUsername] = useState('');
    const [editUuid, setEditUuid] = useState('');
    const [copiedUuid, setCopiedUuid] = useState(false);
    const [saveStatus, setSaveStatus] = useState<'idle' | 'saving' | 'saved'>('idle');
    const [localAvatar, setLocalAvatar] = useState<string | null>(null);
    
    // Profile management state
    const [profiles, setProfiles] = useState<Profile[]>([]);
    const [profileAvatars, setProfileAvatars] = useState<Record<string, string | null>>({});
    const [currentProfileIndex, setCurrentProfileIndex] = useState<number>(-1);
    
    // New profile creation flow - directly opens name editor
    const [isCreatingNewProfile, setIsCreatingNewProfile] = useState(false);
    
    // Delete confirmation modal state
    const [deleteConfirmation, setDeleteConfirmation] = useState<{ id: string; name: string } | null>(null);

    // Load profiles and their avatars
    const loadProfiles = useCallback(async () => {
        try {
            const [profileList, activeIndex] = await Promise.all([
                GetProfiles(),
                GetActiveProfileIndex()
            ]);
            console.log('[ProfileEditor] Loaded profiles:', JSON.stringify(profileList, null, 2), 'Active index:', activeIndex);
            setProfiles(profileList || []);
            setCurrentProfileIndex(activeIndex);
            
            // Load avatars for all profiles - explicitly set null for profiles without avatars
            const avatars: Record<string, string | null> = {};
            for (const profile of profileList || []) {
                try {
                    const avatar = await GetAvatarPreviewForUUID(profile.uuid ?? '');
                    // Explicitly set null if no avatar returned (new profiles have no avatar)
                    avatars[profile.uuid ?? ''] = avatar || null;
                } catch {
                    avatars[profile.uuid ?? ''] = null;
                }
            }
            setProfileAvatars(avatars);
        } catch (err) {
            console.error('Failed to load profiles:', err);
        }
    }, []);

    // Load avatar preview
    const loadAvatar = useCallback(async () => {
        try {
            const avatar = await GetAvatarPreview();
            // Only set avatar if one exists - don't preserve old avatar
            setLocalAvatar(avatar || null);
        } catch (err) {
            console.error('Failed to load avatar:', err);
            setLocalAvatar(null);
        }
    }, []);

    useEffect(() => {
        if (isOpen) {
            const initializeEditor = async () => {
                await loadProfile();
                await loadAvatar();
                // Ensure current profile is saved to the profiles list
                try {
                    await SaveCurrentAsProfile();
                } catch (err) {
                    console.error('Failed to save current profile:', err);
                }
                await loadProfiles();
                setIsCreatingNewProfile(false);
            };
            initializeEditor();
        }
    }, [isOpen, loadAvatar, loadProfiles]);

    // Poll for avatar updates while editor is open
    useEffect(() => {
        if (!isOpen) return;
        const interval = setInterval(() => {
            GetAvatarPreview().then(avatar => {
                if (avatar && avatar !== localAvatar) {
                    setLocalAvatar(avatar);
                }
            }).catch(() => {});
        }, 3000); // Check every 3 seconds while editor is open
        return () => clearInterval(interval);
    }, [isOpen, localAvatar]);

    const loadProfile = async () => {
        setIsLoading(true);
        try {
            const [userUuid, userName] = await Promise.all([
                GetUUID(),
                GetNick()
            ]);
            setUuid(userUuid || generateUUID());
            // Use 'HyPrism' as fallback - matches backend default
            const displayName = userName || 'HyPrism';
            setUsernameState(displayName);
            setEditUsername(displayName);
            setEditUuid(userUuid || '');
        } catch (err) {
            console.error('Failed to load profile:', err);
            setUuid(generateUUID());
            setUsernameState('HyPrism');
        }
        setIsLoading(false);
    };

    const generateUUID = () => {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
            const r = (Math.random() * 16) | 0;
            const v = c === 'x' ? r : (r & 0x3) | 0x8;
            return v.toString(16);
        });
    };

    // Auto-save current profile whenever username or uuid changes
    const autoSaveProfile = useCallback(async () => {
        try {
            await SaveCurrentAsProfile();
            await loadProfiles();
        } catch (err) {
            console.error('Failed to auto-save profile:', err);
        }
    }, [loadProfiles]);

    const handleSaveUsername = async () => {
        const trimmedUsername = editUsername.trim();
        if (trimmedUsername && trimmedUsername.length >= 1 && trimmedUsername.length <= 16) {
            setSaveStatus('saving');
            try {
                await SetNick(trimmedUsername);
                setUsernameState(trimmedUsername);
                setIsEditingUsername(false);
                setIsCreatingNewProfile(false);
                
                // Auto-save the profile
                await autoSaveProfile();
                
                setSaveStatus('saved');
                onProfileUpdate?.();
                setTimeout(() => setSaveStatus('idle'), 2000);
            } catch (err) {
                console.error('Failed to save username:', err);
                setSaveStatus('idle');
            }
        }
    };

    const handleSaveUuid = async () => {
        const trimmed = editUuid.trim();
        if (trimmed) {
            setSaveStatus('saving');
            try {
                await SetUUID(trimmed);
                setUuid(trimmed);
                setIsEditingUuid(false);
                
                // Auto-save the profile
                await autoSaveProfile();
                
                setSaveStatus('saved');
                onProfileUpdate?.();
                setTimeout(() => setSaveStatus('idle'), 2000);
            } catch (err) {
                console.error('Failed to save UUID:', err);
                setSaveStatus('idle');
            }
        }
    };

    const handleRandomizeUuid = () => {
        const newUuid = generateUUID();
        setEditUuid(newUuid);
    };

    const handleCopyUuid = async () => {
        try {
            await navigator.clipboard.writeText(uuid);
            setCopiedUuid(true);
            setTimeout(() => setCopiedUuid(false), 2000);
        } catch (err) {
            console.error('Failed to copy UUID:', err);
        }
    };

    const handleSaveUsernameWithName = async (name: string) => {
        if (name.trim() && name.length <= 16) {
            setSaveStatus('saving');
            try {
                await SetNick(name.trim());
                setUsernameState(name.trim());
                setEditUsername(name.trim());
                setIsEditingUsername(false);
                setIsCreatingNewProfile(false);
                
                // Auto-save the profile
                await autoSaveProfile();
                
                setSaveStatus('saved');
                onProfileUpdate?.();
                setTimeout(() => setSaveStatus('idle'), 2000);
            } catch (err) {
                console.error('Failed to save username:', err);
                setSaveStatus('idle');
            }
        }
    };

    const handleUsernameKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') {
            handleSaveUsername();
        } else if (e.key === 'Escape') {
            if (isCreatingNewProfile) {
                // If creating new profile and escaping, generate random name
                const randomName = generateRandomName();
                handleSaveUsernameWithName(randomName);
            } else {
                setEditUsername(username);
                setIsEditingUsername(false);
            }
        }
    };

    const handleUuidKeyDown = (e: React.KeyboardEvent) => {
        if (e.key === 'Enter') {
            handleSaveUuid();
        } else if (e.key === 'Escape') {
            setEditUuid(uuid);
            setIsEditingUuid(false);
        }
    };
    
    // Profile management handlers
    const handleSwitchProfile = async (profileId: string) => {
        try {
            // Find the actual index of this profile in the full profiles list
            const actualIndex = profiles.findIndex(p => p.id === profileId);
            if (actualIndex === -1) {
                console.error('Profile not found in profiles list');
                return;
            }
            
            const targetProfile = profiles[actualIndex];
            
            // Optimistically update the UI immediately (no loading state)
            setUsernameState(targetProfile.name);
            setEditUsername(targetProfile.name);
            setUuid(targetProfile.uuid ?? '');
            setEditUuid(targetProfile.uuid ?? '');
            setCurrentProfileIndex(actualIndex);
            
            // Pre-load the avatar for the target profile (use null if no avatar exists)
            const targetAvatar = profileAvatars[targetProfile.uuid ?? ''];
            setLocalAvatar(targetAvatar || null);
            
            console.log('[ProfileEditor] Switching to profile at index:', actualIndex, 'with ID:', profileId);
            const success = await SwitchProfile(actualIndex);
            
            if (success) {
                // Refresh avatar in the background (don't wait)
                GetAvatarPreview().then(avatar => {
                    // Set avatar or null if no avatar exists
                    setLocalAvatar(avatar || null);
                }).catch(() => {
                    setLocalAvatar(null);
                });
                
                onProfileUpdate?.();
            } else {
                // Revert on failure
                await loadProfile();
                await loadAvatar();
                await loadProfiles(); // Also refresh current profile index
            }
        } catch (err) {
            console.error('Failed to switch profile:', err);
        }
    };
    
    const handleDeleteProfile = (profileId: string, e: React.MouseEvent) => {
        e.stopPropagation();
        e.preventDefault();
        
        // Find the profile name for the confirmation modal
        const profileToDelete = profiles.find(p => p.id === profileId);
        const profileName = profileToDelete?.name || 'this profile';
        
        // Show in-launcher confirmation modal
        setDeleteConfirmation({ id: profileId, name: profileName });
    };
    
    const handleConfirmDelete = async () => {
        if (!deleteConfirmation) return;
        
        console.log('[ProfileEditor] Deleting profile:', deleteConfirmation.id);
        
        try {
            const success = await DeleteProfile(deleteConfirmation.id);
            console.log('[ProfileEditor] Delete result:', success);
            if (success) {
                // Reload profiles after delete
                await loadProfiles();
                onProfileUpdate?.();
            }
        } catch (err) {
            console.error('Failed to delete profile:', err);
        }
        
        setDeleteConfirmation(null);
    };
    
    const handleCreateProfile = async () => {
        // Generate new UUID and random name for the new profile
        const newUuid = generateUUID();
        const randomName = generateRandomName();
        
        console.log('[ProfileEditor] Creating new profile:', randomName, newUuid);
        
        try {
            // Create profile with random name and new UUID
            const profile = await CreateProfile(randomName, newUuid);
            console.log('[ProfileEditor] Created profile:', profile);
            
            if (profile) {
                // Reload profiles list
                await loadProfiles();
                
                // Find the new profile and switch to it
                const updatedProfiles = await GetProfiles();
                const newProfileIndex = updatedProfiles?.findIndex(p => p.uuid === newUuid);
                if (newProfileIndex !== undefined && newProfileIndex >= 0) {
                    console.log('[ProfileEditor] Switching to new profile at index:', newProfileIndex);
                    await SwitchProfile(newProfileIndex);
                    // Update UI with new profile data
                    setUsernameState(randomName);
                    setEditUsername(randomName);
                    setUuid(newUuid);
                    setEditUuid(newUuid);
                    setLocalAvatar(null); // New profile has no avatar
                }
                
                // Clear any cached avatar for the new UUID (it's a new identity with no skin)
                setProfileAvatars(prev => ({
                    ...prev,
                    [newUuid]: null  // New profile has no avatar yet
                }));
                await loadProfiles();
                onProfileUpdate?.();
            }
        } catch (err) {
            console.error('Failed to create profile:', err);
        }
    };

    const handleDuplicateProfile = async (profileId: string, e: React.MouseEvent) => {
        e.stopPropagation();
        try {
            console.log('[ProfileEditor] Duplicating profile (without data):', profileId);
            const newProfile = await DuplicateProfileWithoutData(profileId);
            if (newProfile) {
                console.log('[ProfileEditor] Duplicated profile:', newProfile);
                await loadProfiles();
                onProfileUpdate?.();
            }
        } catch (err) {
            console.error('Failed to duplicate profile:', err);
        }
    };

    if (!isOpen && !isPageMode) return null;

    return (
        <AnimatePresence>
            <motion.div
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                className={isPageMode
                    ? "w-full h-full"
                    : "fixed inset-0 z-[200] flex items-center justify-center bg-black/60 backdrop-blur-sm"
                }
                onClick={(e) => !isPageMode && e.target === e.currentTarget && onClose()}
            >
                <motion.div
                    initial={{ opacity: 0, scale: 0.95, y: 20 }}
                    animate={{ opacity: 1, scale: 1, y: 0 }}
                    exit={{ opacity: 0, scale: 0.95, y: 20 }}
                    className={`bg-[#1a1a1a] border border-white/10 rounded-2xl shadow-2xl overflow-hidden flex ${isPageMode ? 'w-full h-full' : 'w-full max-w-3xl mx-4 max-h-[80vh]'}`}
                >
                    {/* Left Sidebar - Profiles List */}
                    <div className="w-48 bg-[#151515] border-r border-white/5 flex flex-col py-4 overflow-y-auto">
                        {!isPageMode && <h2 className="text-lg font-bold text-white px-4 mb-4">{t('profiles.savedProfiles')}</h2>}
                        
                        {/* Profile Navigation - All Profiles */}
                        <nav className="flex-1 space-y-1 px-2 overflow-y-auto">
                            {/* All Profiles - show all of them with current one highlighted */}
                            {(() => {
                                const filtered = profiles.filter(p => p.name && p.name.trim() !== '');
                                console.log('[ProfileEditor] Rendering profiles:', JSON.stringify({
                                    total: profiles.length,
                                    filtered: filtered.length,
                                    currentProfileIndex,
                                    allProfiles: profiles,
                                    filteredProfiles: filtered
                                }, null, 2));
                                return filtered.map((profile, displayIndex) => {
                                // Find the actual index in the original profiles array
                                const actualIndex = profiles.findIndex(p => p.id === profile.id);
                                const isCurrentProfile = actualIndex === currentProfileIndex;
                                const profileAvatar = profileAvatars[profile.uuid ?? ''];
                                // Check if this is a duplicate (folder name differs from display name)
                                const isDuplicate = profile.folderName && profile.folderName !== profile.name;
                                
                                return (
                                    <div
                                        key={profile.id}
                                        className={`w-full flex items-center gap-2 px-2 py-1.5 rounded-lg text-sm transition-colors group ${
                                            isCurrentProfile 
                                                ? '' 
                                                : 'text-white/60 hover:text-white hover:bg-white/5'
                                        }`}
                                        style={isCurrentProfile ? { backgroundColor: `${accentColor}20`, color: accentColor } : undefined}
                                    >
                                        <button
                                            onClick={() => !isCurrentProfile && handleSwitchProfile(profile.id)}
                                            className="flex items-center gap-3 flex-1 min-w-0"
                                            disabled={isCurrentProfile}
                                        >
                                            <div 
                                                className="w-8 h-8 rounded-full overflow-hidden flex-shrink-0 flex items-center justify-center"
                                                style={isCurrentProfile 
                                                    ? { borderWidth: '2px', borderColor: accentColor, backgroundColor: profileAvatar ? 'transparent' : `${accentColor}20` }
                                                    : { borderWidth: '1px', borderColor: 'rgba(255,255,255,0.2)', backgroundColor: profileAvatar ? 'transparent' : 'rgba(255,255,255,0.05)' }
                                                }
                                            >
                                                {profileAvatar ? (
                                                    <img
                                                        src={profileAvatar}
                                                        className="w-full h-full object-cover object-[center_20%]"
                                                        alt="Avatar"
                                                    />
                                                ) : (
                                                    <User size={14} style={isCurrentProfile ? { color: accentColor } : { color: 'rgba(255,255,255,0.4)' }} />
                                                )}
                                            </div>
                                            <span className={`truncate ${isCurrentProfile ? 'font-medium' : ''}`}>
                                                {profile.name || 'Unnamed'}
                                                {isDuplicate && (
                                                    <span className="text-white/30 text-xs ml-1">({profile.folderName?.replace(profile.name + ' ', '')})</span>
                                                )}
                                            </span>
                                        </button>
                                        <div className="flex items-center gap-0.5 flex-shrink-0 opacity-0 group-hover:opacity-100 transition-opacity">
                                            <button
                                                onClick={(e) => handleDuplicateProfile(profile.id, e)}
                                                className="p-1.5 rounded-lg text-white/40 hover:text-white hover:bg-white/10 transition-all"
                                                title={t('profiles.duplicateProfile')}
                                            >
                                                <CopyPlus size={14} />
                                            </button>
                                            {!isCurrentProfile && (
                                                <button
                                                    onClick={(e) => handleDeleteProfile(profile.id, e)}
                                                    className="p-1.5 rounded-lg text-red-400/60 hover:text-red-400 hover:bg-red-500/20 transition-all"
                                                    title={t('profiles.deleteProfile')}
                                                >
                                                    <Trash2 size={14} />
                                                </button>
                                            )}
                                        </div>
                                    </div>
                                );
                            })})()}
                            
                            {profiles.filter(p => p.name && p.name.trim() !== '').length === 0 && (
                                <p className="text-center text-white/20 text-xs py-4 px-2">
                                    {t('profiles.noProfiles')}
                                </p>
                            )}
                        </nav>
                        
                        {/* Create New Profile Button at Bottom */}
                        <div className="px-2 pt-4 border-t border-white/5 mx-2">
                            <button
                                onClick={handleCreateProfile}
                                className="w-full flex items-center justify-center gap-2 px-3 py-2.5 rounded-lg border border-dashed border-white/20 text-white/40 hover:text-white/60 hover:border-white/40 text-sm transition-colors"
                            >
                                <Plus size={14} />
                                <span>{t('profiles.createNew')}</span>
                            </button>
                        </div>
                    </div>

                    {/* Right Content - Profile Details */}
                    <div className="flex-1 flex flex-col min-w-0">
                        {/* Header - only in modal mode */}
                        {!isPageMode && (
                            <div className="flex items-center justify-between p-4 border-b border-white/5">
                                <h3 className="text-white font-medium">{t('profiles.editor')}</h3>
                                <button
                                    onClick={onClose}
                                    className="p-2 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/10 transition-colors"
                                >
                                    <X size={20} />
                                </button>
                            </div>
                        )}

                        {/* Content */}
                        <div className="flex-1 p-6 overflow-y-auto">
                            {isLoading ? (
                                <div className="flex items-center justify-center py-12">
                                    <RefreshCw size={32} className="animate-spin text-white/40" />
                                </div>
                            ) : (
                                <div className="space-y-6">
                                    {/* Profile Picture Section */}
                                    <div className="flex flex-col items-center gap-4">
                                        {/* Avatar - Local or Placeholder */}
                                        <div 
                                            className="w-24 h-24 rounded-full overflow-hidden border-2 flex items-center justify-center"
                                            style={{ borderColor: accentColor, backgroundColor: localAvatar ? 'transparent' : `${accentColor}20` }}
                                            title={t('profiles.yourAvatar')}
                                        >
                                            {localAvatar ? (
                                                <img
                                                    src={localAvatar}
                                                    className="w-full h-full object-cover object-[center_15%]"
                                                    alt="Player Avatar"
                                                />
                                            ) : (
                                                <User size={40} style={{ color: accentColor }} />
                                            )}
                                        </div>
                                        
                                        {/* Username Display/Edit */}
                                        <div className="flex items-center gap-2">
                                            {isEditingUsername ? (
                                                <div className="flex items-center gap-2">
                                                    <input
                                                        type="text"
                                                        value={editUsername}
                                                        onChange={(e) => setEditUsername(e.target.value)}
                                                        onKeyDown={handleUsernameKeyDown}
                                                        maxLength={16}
                                                        autoFocus
                                                        placeholder={isCreatingNewProfile ? t('profiles.enterName') : ''}
                                                        className="bg-[#151515] text-white text-xl font-bold px-3 py-1 rounded-lg border outline-none w-48 text-center"
                                                        style={{ borderColor: accentColor }}
                                                    />
                                                    <motion.button
                                                        whileHover={{ scale: 1.1 }}
                                                        whileTap={{ scale: 0.9 }}
                                                        onClick={() => setEditUsername(generateRandomName())}
                                                        className="p-2 rounded-lg bg-white/10 text-white/70 hover:bg-white/20"
                                                        title={t('profiles.generateName')}
                                                    >
                                                        <Dices size={16} />
                                                    </motion.button>
                                                    <motion.button
                                                        whileHover={{ scale: 1.1 }}
                                                        whileTap={{ scale: 0.9 }}
                                                        onClick={handleSaveUsername}
                                                        className="p-2 rounded-lg"
                                                        style={{ backgroundColor: `${accentColor}33`, color: accentColor }}
                                                    >
                                                        <Check size={16} />
                                                    </motion.button>
                                                </div>
                                            ) : (
                                                <>
                                                    <span className="text-2xl font-bold text-white">{username}</span>
                                                    <motion.button
                                                        whileHover={{ scale: 1.1 }}
                                                        whileTap={{ scale: 0.9 }}
                                                        onClick={async () => {
                                                            const newName = generateRandomName();
                                                            await SetNick(newName);
                                                            setUsernameState(newName);
                                                            setEditUsername(newName);
                                                            onProfileUpdate?.();
                                                        }}
                                                        className="p-1.5 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/5"
                                                        title={t('profiles.generateName')}
                                                    >
                                                        <Dices size={14} />
                                                    </motion.button>
                                                    <motion.button
                                                        whileHover={{ scale: 1.1 }}
                                                        whileTap={{ scale: 0.9 }}
                                                        onClick={() => {
                                                            setEditUsername(username);
                                                            setIsEditingUsername(true);
                                                        }}
                                                        className="p-1.5 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/5"
                                                        title={t('profiles.editUsername')}
                                                    >
                                                        <Edit3 size={14} />
                                                    </motion.button>
                                                </>
                                            )}
                                        </div>
                                        
                                        {/* New profile hint */}
                                        {isCreatingNewProfile && isEditingUsername && (
                                            <p className="text-xs text-white/40 text-center">
                                                {t('profiles.enterNewName')}
                                            </p>
                                        )}
                                    </div>

                                    {/* UUID Section */}
                                    <div className="p-4 rounded-xl bg-[#151515] border border-white/10">
                                        <div className="flex items-center justify-between mb-2">
                                            <label className="text-sm text-white/60">{t('profiles.playerUuid')}</label>
                                            <div className="flex items-center gap-1">
                                                <motion.button
                                                    whileHover={{ scale: 1.05 }}
                                                    whileTap={{ scale: 0.95 }}
                                                    onClick={handleCopyUuid}
                                                    className="p-1.5 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/10"
                                                    title={t('profiles.copyUuid')}
                                                >
                                                    {copiedUuid ? <CheckCircle size={14} className="text-green-400" /> : <Copy size={14} />}
                                                </motion.button>
                                                <motion.button
                                                    whileHover={{ scale: 1.05 }}
                                                    whileTap={{ scale: 0.95 }}
                                                    onClick={async () => {
                                                        const newUuid = generateUUID();
                                                        await SetUUID(newUuid);
                                                        setUuid(newUuid);
                                                        setEditUuid(newUuid);
                                                        onProfileUpdate?.();
                                                    }}
                                                    className="p-1.5 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/10"
                                                    title={t('profiles.randomUuid')}
                                                >
                                                    <Dices size={14} />
                                                </motion.button>
                                                <motion.button
                                                    whileHover={{ scale: 1.05 }}
                                                    whileTap={{ scale: 0.95 }}
                                                    onClick={() => {
                                                        setEditUuid(uuid);
                                                        setIsEditingUuid(true);
                                                    }}
                                                    className="p-1.5 rounded-lg text-white/40 hover:text-white/80 hover:bg-white/10"
                                                    title={t('profiles.editUuid')}
                                                >
                                                    <Edit3 size={14} />
                                                </motion.button>
                                            </div>
                                        </div>
                                        
                                        {isEditingUuid ? (
                                            <div className="flex items-center gap-2">
                                                <input
                                                    type="text"
                                                    value={editUuid}
                                                    onChange={(e) => setEditUuid(e.target.value)}
                                                    onKeyDown={handleUuidKeyDown}
                                                    autoFocus
                                                    className="flex-1 bg-[#0a0a0a] text-white font-mono text-sm px-3 py-2 rounded-lg border outline-none"
                                                    style={{ borderColor: accentColor }}
                                                />
                                                <motion.button
                                                    whileHover={{ scale: 1.05 }}
                                                    whileTap={{ scale: 0.95 }}
                                                    onClick={handleRandomizeUuid}
                                                    className="p-2 rounded-lg bg-white/10 text-white/70 hover:bg-white/20"
                                                    title={t('profiles.randomUuid')}
                                                >
                                                    <Dices size={16} />
                                                </motion.button>
                                                <motion.button
                                                    whileHover={{ scale: 1.05 }}
                                                    whileTap={{ scale: 0.95 }}
                                                    onClick={handleSaveUuid}
                                                    className="p-2 rounded-lg"
                                                    style={{ backgroundColor: `${accentColor}33`, color: accentColor }}
                                                    title={t('profiles.saveUuid')}
                                                >
                                                    <Check size={16} />
                                                </motion.button>
                                            </div>
                                        ) : (
                                            <p className="text-white font-mono text-sm truncate">{uuid}</p>
                                        )}
                                    </div>

                                    {/* Open Profile Folder Button */}
                                    <motion.button
                                        whileHover={{ scale: 1.02 }}
                                        whileTap={{ scale: 0.98 }}
                                        onClick={() => OpenCurrentProfileFolder()}
                                        className="w-full p-3 rounded-xl bg-[#151515] border border-white/10 hover:border-white/20 flex items-center justify-center gap-2 text-white/60 hover:text-white transition-colors"
                                    >
                                        <FolderOpen size={18} />
                                        <span className="text-sm">{t('profiles.openFolder')}</span>
                                    </motion.button>

                                    {/* Save Status */}
                                    {saveStatus === 'saved' && (
                                        <motion.div
                                            initial={{ opacity: 0, y: 10 }}
                                            animate={{ opacity: 1, y: 0 }}
                                            exit={{ opacity: 0 }}
                                            className="flex items-center justify-center gap-2 text-green-400 text-sm"
                                        >
                                            <CheckCircle size={16} />
                                            {t('profiles.saved')}
                                        </motion.div>
                                    )}
                                </div>
                            )}
                        </div>
                    </div>
                </motion.div>
            </motion.div>
            
            {/* Delete Profile Confirmation Modal */}
            {deleteConfirmation && (
                <DeleteProfileConfirmationModal
                    profileName={deleteConfirmation.name}
                    onConfirm={handleConfirmDelete}
                    onCancel={() => setDeleteConfirmation(null)}
                />
            )}
        </AnimatePresence>
    );
};

export default ProfileEditor;
