import React, { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { 
    Check, 
    ChevronRight, 
    FolderOpen, 
    User, 
    Palette, 
    Globe, 
    SkipForward,
    Github,
    Bug,
    RefreshCw,
    ArrowRight,
    Image,
    Info,
    Loader2,
    LogIn,
    Monitor,
    HardDrive,
    Settings,
    Cpu
} from 'lucide-react';
import { ipc } from '@/lib/ipc';

// Alias for compatibility
const BrowserOpenURL = (url: string) => ipc.browser.open(url);

// Settings / profile backed
async function GetLauncherFolderPath(): Promise<string> { console.warn('[IPC] GetLauncherFolderPath: stub'); return ''; }
async function GetCustomInstanceDir(): Promise<string> { return (await ipc.settings.get()).dataDirectory ?? ''; }
async function SetInstanceDirectory(_dir: string): Promise<void> { console.warn('[IPC] SetInstanceDirectory: no channel'); }
async function BrowseFolder(_initialDir?: string): Promise<string> { console.warn('[IPC] BrowseFolder: no channel'); return ''; }
async function SetHasCompletedOnboarding(v: boolean): Promise<void> { await ipc.settings.update({ hasCompletedOnboarding: v }); }
async function GetRandomUsername(): Promise<string> { console.warn('[IPC] GetRandomUsername: stub'); return 'HyPrism' + Math.floor(Math.random() * 9999); }
async function GetLauncherVersion(): Promise<string> { return (await ipc.settings.get()).launcherVersion ?? ''; }
async function SetBackgroundModeBackend(v: string): Promise<void> { await ipc.settings.update({ backgroundMode: v }); }
async function GetDiscordLink(): Promise<string> { return 'https://discord.gg/hyprism'; }
import { useAccentColor } from '../../contexts/AccentColorContext';

import { Language } from '../../constants/enums';
import { LANGUAGE_CONFIG } from '../../constants/languages';
import { ACCENT_COLORS } from '../../constants/colors';
import appIcon from '../../assets/images/logo.png';

// Background images (matching SettingsModal) - using the correct path
const backgroundModulesJpg = import.meta.glob('../../assets/backgrounds/bg_*.jpg', { query: '?url', import: 'default', eager: true });
const backgroundModulesPng = import.meta.glob('../../assets/backgrounds/bg_*.png', { query: '?url', import: 'default', eager: true });
const allBackgrounds = { ...backgroundModulesJpg, ...backgroundModulesPng };
const backgroundImages = Object.entries(allBackgrounds)
  .sort(([a], [b]) => {
    const numA = parseInt(a.match(/bg_(\d+)/)?.[1] || '0');
    const numB = parseInt(b.match(/bg_(\d+)/)?.[1] || '0');
    return numA - numB;
  })
  .map(([path, url]) => ({ 
    name: path.match(/bg_(\d+)/)?.[0] || 'bg_1', 
    url: url as string 
  }));

// Discord icon component
const DiscordIcon: React.FC<{ size?: number; color?: string; className?: string }> = ({ 
    size = 24, 
    color = "white",
    className = ""
}) => (
    <svg 
        width={size} 
        height={size} 
        viewBox="0 0 24 24" 
        fill={color}
        className={className}
    >
        <path d="M20.317 4.37a19.791 19.791 0 0 0-4.885-1.515.074.074 0 0 0-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 0 0-5.487 0 12.64 12.64 0 0 0-.617-1.25.077.077 0 0 0-.079-.037A19.736 19.736 0 0 0 3.677 4.37a.07.07 0 0 0-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 0 0 .031.057 19.9 19.9 0 0 0 5.993 3.03.078.078 0 0 0 .084-.028 14.09 14.09 0 0 0 1.226-1.994.076.076 0 0 0-.041-.106 13.107 13.107 0 0 1-1.872-.892.077.077 0 0 1-.008-.128 10.2 10.2 0 0 0 .372-.292.074.074 0 0 1 .077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 0 1 .078.01c.12.098.246.198.373.292a.077.077 0 0 1-.006.127 12.299 12.299 0 0 1-1.873.892.077.077 0 0 0-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 0 0 .084.028 19.839 19.839 0 0 0 6.002-3.03.077.077 0 0 0 .032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 0 0-.031-.03zM8.02 15.33c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.956-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.956 2.418-2.157 2.418zm7.975 0c-1.183 0-2.157-1.085-2.157-2.419 0-1.333.955-2.419 2.157-2.419 1.21 0 2.176 1.096 2.157 2.42 0 1.333-.946 2.418-2.157 2.418z"/>
    </svg>
);

// Contributor interface
interface Contributor {
    login: string;
    avatar_url: string;
    html_url: string;
    contributions: number;
}

// GPU adapter interface
interface GpuAdapter {
    name: string;
    vendor: string;
    type: string;
}

// Cache key for localStorage
const ONBOARDING_CACHE_KEY = 'hyprism_onboarding_state';

interface OnboardingState {
    phase: 'splash' | 'auth' | 'setup';
    currentStep: string;
    username: string;
    backgroundMode: string;
    selectedLanguage: string;
    isAuthenticated: boolean;
}

interface OnboardingModalProps {
    onComplete: () => void;
}

type OnboardingPhase = 'splash' | 'auth' | 'setup';
type OnboardingStep = 'language' | 'profile' | 'hardware' | 'visual' | 'location' | 'about';

export const OnboardingModal: React.FC<OnboardingModalProps> = ({ onComplete }) => {
    const { i18n, t } = useTranslation();
    const { accentColor, accentTextColor, setAccentColor } = useAccentColor();
  
    // Initialize from cache or defaults
    const getCachedState = (): Partial<OnboardingState> => {
        try {
            const cached = localStorage.getItem(ONBOARDING_CACHE_KEY);
            if (cached) {
                return JSON.parse(cached);
            }
        } catch {}
        return {};
    };
    
    const cachedState = getCachedState();
    
    // Phase and step state - restore from cache
    const [phase, setPhase] = useState<OnboardingPhase>(cachedState.phase || 'splash');
    const [currentStep, setCurrentStep] = useState<OnboardingStep>((cachedState.currentStep as OnboardingStep) || 'language');
    const [splashAnimationComplete, setSplashAnimationComplete] = useState(cachedState.phase !== 'splash');
    const [isReady, setIsReady] = useState(false);
    
    // Auth state
    const [isAuthenticated, setIsAuthenticated] = useState(cachedState.isAuthenticated || false);
    const [isAuthenticating, setIsAuthenticating] = useState(false);
    const [authError, setAuthError] = useState<string | null>(null);
    const [authErrorType, setAuthErrorType] = useState<'warning' | 'error'>('error');
    const [authenticatedUsername, setAuthenticatedUsername] = useState<string | null>(null);
    
    // Form state - restore from cache
    const [username, setUsername] = useState(cachedState.username || '');
    const [instanceDir, setInstanceDir] = useState('');
    const [defaultInstanceDir, setDefaultInstanceDir] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const [isGeneratingUsername, setIsGeneratingUsername] = useState(false);
    const [launcherVersion, setLauncherVersion] = useState('2.0.2');
    
    // Visual settings - restore from cache
    const [backgroundMode, setBackgroundMode] = useState(cachedState.backgroundMode || 'slideshow');
    const [currentBackgroundIndex, setCurrentBackgroundIndex] = useState(0);
    
    // GPU settings
    const [gpuAdapters, setGpuAdapters] = useState<GpuAdapter[]>([]);
    const [gpuPreference, setGpuPreference] = useState<string>('auto');
    const [hasMultipleGpus, setHasMultipleGpus] = useState(false);
    const [gpuAlreadyConfigured, setGpuAlreadyConfigured] = useState(false);
    
    // Profile state
    const [hasExistingProfiles, setHasExistingProfiles] = useState(false);
    
    // Contributors
    const [contributors, setContributors] = useState<Contributor[]>([]);
    const [isLoadingContributors, setIsLoadingContributors] = useState(false);
    
    // Dynamic steps based on auth status and GPU count
    const steps = useMemo(() => {
        const allSteps: { id: OnboardingStep; label: string; icon: React.ElementType }[] = [
            { id: 'language', label: t('onboarding.language'), icon: Globe },
        ];
        
        // Only show profile step if not authenticated and no existing profiles
        if (!isAuthenticated && !hasExistingProfiles) {
            allSteps.push({ id: 'profile', label: t('onboarding.profile'), icon: User });
        }
        
        // Only show hardware step if multiple GPUs detected AND not already configured
        if (hasMultipleGpus && !gpuAlreadyConfigured) {
            allSteps.push({ id: 'hardware', label: t('onboarding.hardware'), icon: Cpu });
        }
        
        allSteps.push(
            { id: 'visual', label: t('onboarding.visual'), icon: Palette },
            { id: 'location', label: t('onboarding.location'), icon: FolderOpen },
            { id: 'about', label: t('onboarding.about'), icon: Info }
        );
        
        return allSteps;
    }, [isAuthenticated, hasExistingProfiles, hasMultipleGpus, gpuAlreadyConfigured, t]);
    
    // Save state to cache whenever it changes
    const saveToCache = useCallback(() => {
        try {
            const state: OnboardingState = {
                phase,
                currentStep,
                username,
                backgroundMode,
                selectedLanguage: i18n.language,
                isAuthenticated
            };
            localStorage.setItem(ONBOARDING_CACHE_KEY, JSON.stringify(state));
        } catch {}
    }, [phase, currentStep, username, backgroundMode, i18n.language, isAuthenticated]);
    
    useEffect(() => {
        saveToCache();
    }, [saveToCache]);
    
    // Clear cache on complete
    const clearCache = () => {
        try {
            localStorage.removeItem(ONBOARDING_CACHE_KEY);
        } catch {}
    };
    
    // Load defaults
    useEffect(() => {
        const loadDefaults = async () => {
            try {
                const folderPath = await GetLauncherFolderPath();
                const customDir = await GetCustomInstanceDir();
                const defaultDir = customDir || `${folderPath}/instances`;
                setDefaultInstanceDir(defaultDir);
                setInstanceDir(defaultDir);
                
                // Get version
                const version = await GetLauncherVersion();
                setLauncherVersion(version);
                
                // Generate initial random username only if not cached
                if (!cachedState.username) {
                    const randomName = await GetRandomUsername();
                    setUsername(randomName);
                }
                
                // Load GPU adapters
                try {
                    const adapters = await ipc.system.gpuAdapters();
                    setGpuAdapters(adapters || []);
                    setHasMultipleGpus(adapters && adapters.length > 1);
                    
                    // Load current GPU preference
                    const settings = await ipc.settings.get();
                    const currentGpuPref = settings.gpuPreference ?? 'auto';
                    setGpuPreference(currentGpuPref);
                    // If already configured (not auto), skip hardware step
                    setGpuAlreadyConfigured(currentGpuPref !== 'auto');
                } catch (err) {
                    console.error('Failed to load GPU info:', err);
                }
                
                // Check for existing profiles
                try {
                    const profiles = await ipc.profile.list();
                    const hasProfiles = profiles && profiles.length > 0;
                    setHasExistingProfiles(hasProfiles);
                    if (hasProfiles) {
                        setIsAuthenticated(true); // Treat as authenticated if profiles exist
                    }
                } catch (err) {
                    console.error('Failed to load profiles:', err);
                }
                
                // Mark as ready after a small delay to ensure smooth animation
                setTimeout(() => setIsReady(true), 50);
            } catch (err) {
                console.error('Failed to load defaults:', err);
                setIsReady(true);
            }
        };
        loadDefaults();
    }, []);
    
    // Load contributors when reaching about step
    useEffect(() => {
        if (currentStep === 'about' && contributors.length === 0 && !isLoadingContributors) {
            loadContributors();
        }
    }, [currentStep]);
    
    const loadContributors = async () => {
        setIsLoadingContributors(true);
        try {
            const response = await fetch('https://api.github.com/repos/yyyumeniku/HyPrism/contributors');
            if (response.ok) {
                const data = await response.json();
                setContributors(data);
            }
        } catch (err) {
            console.error('Failed to load contributors:', err);
        }
        setIsLoadingContributors(false);
    };
    
    // Slideshow effect for background
    useEffect(() => {
        if (backgroundMode === 'slideshow') {
            const interval = setInterval(() => {
                setCurrentBackgroundIndex(prev => (prev + 1) % backgroundImages.length);
            }, 8000);
            return () => clearInterval(interval);
        }
    }, [backgroundMode]);
    
    // Splash animation timing
    useEffect(() => {
        if (phase === 'splash' && !splashAnimationComplete) {
            const timer = setTimeout(() => {
                setSplashAnimationComplete(true);
            }, 2500);
            return () => clearTimeout(timer);
        }
    }, [phase, splashAnimationComplete]);
    
    const handleEnterAuth = () => {
        // Skip auth phase if user already has profiles
        if (hasExistingProfiles) {
            setPhase('setup');
            setCurrentStep('language');
        } else {
            setPhase('auth');
        }
    };
    
    const handleLanguageChange = (langCode: Language) => {
        i18n.changeLanguage(langCode);
    };
    
    const handleGenerateUsername = async () => {
        setIsGeneratingUsername(true);
        try {
            const randomName = await GetRandomUsername();
            setUsername(randomName);
        } catch (err) {
            console.error('Failed to generate username:', err);
        } finally {
            setIsGeneratingUsername(false);
        }
    };
    
    const handleNextStep = () => {
        const stepIds = steps.map(s => s.id);
        const currentIndex = stepIds.indexOf(currentStep);
        if (currentIndex < stepIds.length - 1) {
            setCurrentStep(stepIds[currentIndex + 1]);
        }
    };
    
    const handlePrevStep = () => {
        const stepIds = steps.map(s => s.id);
        const currentIndex = stepIds.indexOf(currentStep);
        if (currentIndex > 0) {
            setCurrentStep(stepIds[currentIndex - 1]);
        }
    };
    
    const handleBrowseInstanceDir = async () => {
        try {
            const selectedPath = await BrowseFolder(instanceDir || defaultInstanceDir);
            if (selectedPath) {
                setInstanceDir(selectedPath);
            }
        } catch (err) {
            console.error('Failed to browse folder:', err);
        }
    };
    
    const handleBackgroundModeChange = async (mode: string) => {
        setBackgroundMode(mode);
        // Save to backend immediately for real-time preview
        try {
            await SetBackgroundModeBackend(mode);
        } catch {}
    };
    
    const handleGpuPreferenceChange = async (preference: string) => {
        setGpuPreference(preference);
        try {
            await ipc.settings.update({ gpuPreference: preference });
        } catch (err) {
            console.error('Failed to update GPU preference:', err);
        }
    };
    
    // Auth handlers
    const handleLogin = async () => {
        setIsAuthenticating(true);
        setAuthError(null);
        setAuthErrorType('error');
        
        try {
            const result = await ipc.auth.login();
            if (result?.loggedIn && result.username && result.uuid) {
                // Create official profile from Hytale account data
                const profile = await ipc.profile.create({
                    name: result.username,
                    uuid: result.uuid,
                    isOfficial: true,
                });
                if (profile && profile.id) {
                    setIsAuthenticated(true);
                    setAuthenticatedUsername(result.username);
                    // Move to setup phase
                    setPhase('setup');
                    setCurrentStep('language');
                } else {
                    setAuthError(t('onboarding.auth.createFailed'));
                    setAuthErrorType('error');
                }
            } else if (result?.errorType === 'no_profile') {
                // Yellow warning — account works but has no game profiles
                setAuthError(t('onboarding.auth.noHytaleProfile'));
                setAuthErrorType('warning');
            } else {
                setAuthError(t('onboarding.auth.failed'));
                setAuthErrorType('error');
            }
        } catch (err) {
            console.error('[Onboarding] Auth failed:', err);
            setAuthError(t('onboarding.auth.error'));
            setAuthErrorType('error');
        } finally {
            setIsAuthenticating(false);
        }
    };
    
    const handleSkipAuth = () => {
        setIsAuthenticated(false);
        setPhase('setup');
        setCurrentStep('language');
    };
    
    const handleComplete = async () => {
        setIsLoading(true);
        try {
            // Save username only if not authenticated (unofficial profile)
            if (!isAuthenticated && username.trim()) {
                // Create unofficial profile
                const uuid = crypto.randomUUID();
                await ipc.profile.create({
                    name: username.trim(),
                    uuid: uuid,
                    isOfficial: false,
                });
            }
            
            // Save instance directory if changed from default
            if (instanceDir && instanceDir !== defaultInstanceDir) {
                await SetInstanceDirectory(instanceDir);
            }
            
            // Save background mode
            await SetBackgroundModeBackend(backgroundMode);
            
            // Mark onboarding as complete
            await SetHasCompletedOnboarding(true);
            
            // Clear cache
            clearCache();
            
            onComplete();
        } catch (err) {
            console.error('Failed to complete onboarding:', err);
        } finally {
            setIsLoading(false);
        }
    };
    
    const handleSkip = async () => {
        setIsLoading(true);
        try {
            // If not authenticated, create a default profile
            if (!isAuthenticated) {
                const randomName = username.trim() || `HyPrism${Math.floor(Math.random() * 9999)}`;
                const uuid = crypto.randomUUID();
                await ipc.profile.create({
                    name: randomName,
                    uuid: uuid,
                    isOfficial: false,
                });
            }
            
            // Mark onboarding as complete
            await SetHasCompletedOnboarding(true);
            
            // Clear cache
            clearCache();
            
            onComplete();
        } catch (err) {
            console.error('Failed to skip onboarding:', err);
        } finally {
            setIsLoading(false);
        }
    };
    
    const openGitHub = () => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism');
    const openDiscord = async () => {
        const link = await GetDiscordLink();
        BrowserOpenURL(link);
    };
    const openBugReport = () => BrowserOpenURL('https://github.com/yyyumeniku/HyPrism/issues/new');
    
    const truncateName = (name: string, maxLength: number) => {
        if (name.length <= maxLength) return name;
        return name.substring(0, maxLength - 1) + '…';
    };
    
    const currentStepIndex = steps.findIndex(s => s.id === currentStep);
    
    // Get current background for preview
    const getCurrentBackground = () => {
        if (backgroundMode === 'slideshow') {
            const index = currentBackgroundIndex % backgroundImages.length;
            return backgroundImages[index]?.url || backgroundImages[0]?.url;
        }
        const selected = backgroundImages.find(bg => bg.name === backgroundMode);
        return selected?.url || backgroundImages[0]?.url;
    };
    
    // Get maintainer and other contributors
    const maintainer = contributors.find(c => c.login.toLowerCase() === 'yyyumeniku');
    const otherContributors = contributors.filter(c => c.login.toLowerCase() !== 'yyyumeniku').slice(0, 10);
    
    // Don't render anything until ready to prevent flash
    if (!isReady) {
        return (
            <div className="fixed inset-0 z-[100] bg-[#0a0a0a]" />
        );
    }
    
    // Splash Screen Phase
    if (phase === 'splash') {
        const bgImage = getCurrentBackground();
        
        return (
            <div className="fixed inset-0 z-[100] flex flex-col items-center justify-center overflow-hidden">
                {/* Background with blur */}
                <div 
                    className="absolute inset-0 bg-cover bg-center transition-all duration-1000"
                    style={{ 
                        backgroundImage: bgImage ? `url(${bgImage})` : 'none',
                        filter: 'blur(16px) brightness(0.5)',
                        transform: 'scale(1.1)'
                    }}
                />
                
                {/* Dark overlay - lighter for better background visibility */}
                <div className="absolute inset-0 bg-black/40" />
                
                {/* Content */}
                <div className="relative z-10 flex flex-col items-center">
                    {/* Animated logo */}
                    <img 
                        src={appIcon} 
                        alt="HyPrism" 
                        className="w-32 h-32 mb-6"
                        style={{
                            animation: 'bounceIn 1s ease-out forwards',
                        }}
                    />
                    <h1 
                        className="text-5xl font-bold text-white mb-2"
                        style={{ 
                            animation: 'slideUp 0.8s ease-out forwards',
                            animationDelay: '0.3s',
                            opacity: 0
                        }}
                    >
                        HyPrism
                    </h1>
                    <p 
                        className="text-lg text-white/60 mb-2"
                        style={{ 
                            animation: 'slideUp 0.8s ease-out forwards',
                            animationDelay: '0.5s',
                            opacity: 0
                        }}
                    >
                        {t('onboarding.unofficial')}
                    </p>
                    <p 
                        className="text-sm text-white/40"
                        style={{ 
                            animation: 'slideUp 0.8s ease-out forwards',
                            animationDelay: '0.7s',
                            opacity: 0
                        }}
                    >
                        v{launcherVersion}
                    </p>
                    
                    {/* Buttons - always present to prevent layout jump */}
                    <div 
                        className="mt-12 flex flex-col items-center gap-4"
                        style={{ 
                            opacity: splashAnimationComplete ? 1 : 0,
                            transition: 'opacity 0.5s ease-out',
                            pointerEvents: splashAnimationComplete ? 'auto' : 'none'
                        }}
                    >
                        {/* Continue button */}
                        <button
                            onClick={handleEnterAuth}
                            className="flex items-center gap-3 px-8 py-4 rounded-2xl font-semibold text-lg transition-all hover:scale-105 hover:shadow-lg"
                            style={{ 
                                backgroundColor: accentColor, 
                                color: accentTextColor
                            }}
                        >
                            {t('common.continue')}
                            <ArrowRight size={22} />
                        </button>
                    </div>
                </div>
                
                {/* Skip button - visible during entire onboarding */}
                <button
                    onClick={handleSkip}
                    className="absolute bottom-8 right-8 z-10 flex items-center gap-2 font-semibold text-sm text-white/60 hover:text-white hover:scale-[1.02] active:scale-[0.98] transition-all duration-150"
                >
                    <SkipForward size={16} />
                    {t('onboarding.skip')}
                </button>
                
                {/* CSS animations */}
                <style>{`
                    @keyframes bounceIn {
                        0% { opacity: 0; transform: scale(0.3); }
                        50% { opacity: 1; transform: scale(1.05); }
                        70% { transform: scale(0.95); }
                        100% { transform: scale(1); }
                    }
                    @keyframes slideUp {
                        from { opacity: 0; transform: translateY(20px); }
                        to { opacity: 1; transform: translateY(0); }
                    }
                    @keyframes fadeIn {
                        from { opacity: 0; }
                        to { opacity: 1; }
                    }
                `}</style>
            </div>
        );
    }
    
    // Auth Phase
    if (phase === 'auth') {
        const bgImage = getCurrentBackground();
        
        return (
            <div className="fixed inset-0 z-[100] flex items-center justify-center overflow-hidden">
                {/* Background with blur */}
                <div 
                    className="absolute inset-0 bg-cover bg-center transition-all duration-1000"
                    style={{ 
                        backgroundImage: bgImage ? `url(${bgImage})` : 'none',
                        filter: 'blur(20px) brightness(0.3)',
                        transform: 'scale(1.1)'
                    }}
                />
                
                {/* Dark overlay */}
                <div className="absolute inset-0 bg-black/50" />
                
                {/* Modal */}
                <div 
                    className="relative z-10 w-full max-w-md mx-4 overflow-hidden shadow-2xl glass-panel-static-solid"
                    style={{ animation: 'fadeIn 0.3s ease-out forwards' }}
                >
                    {/* Content */}
                    <div className="p-8 flex flex-col items-center text-center">
                        <img src={appIcon} alt="HyPrism" className="w-20 h-20 mb-6" />
                        
                        <h1 className="text-2xl font-bold text-white mb-2">
                            {t('onboarding.auth.title')}
                        </h1>
                        <p className="text-sm text-white/60 mb-8 max-w-sm">
                            {t('onboarding.auth.description')}
                        </p>
                        
                        {/* Error message */}
                        {authError && (
                            <div 
                                className={`w-full p-3 rounded-xl mb-6 text-sm ${
                                    authErrorType === 'warning' 
                                        ? 'bg-yellow-500/20 border border-yellow-500/30 text-yellow-200' 
                                        : 'bg-red-500/20 border border-red-500/30 text-red-200'
                                }`}
                            >
                                {authError}
                            </div>
                        )}
                        
                        {/* Buttons */}
                        <div className="w-full space-y-3">
                            <button
                                onClick={handleLogin}
                                disabled={isAuthenticating}
                                className="w-full flex items-center justify-center gap-3 px-6 py-4 rounded-xl font-semibold transition-all hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
                                style={{ 
                                    backgroundColor: accentColor, 
                                    color: accentTextColor
                                }}
                            >
                                {isAuthenticating ? (
                                    <Loader2 size={20} className="animate-spin" />
                                ) : (
                                    <LogIn size={20} />
                                )}
                                {isAuthenticating ? t('onboarding.auth.authenticating') : t('onboarding.auth.login')}
                            </button>
                            
                            <button
                                onClick={handleSkipAuth}
                                disabled={isAuthenticating}
                                className="w-full flex items-center justify-center gap-2 px-6 py-3 rounded-xl font-medium bg-white/5 text-white/60 hover:bg-white/10 hover:text-white transition-all disabled:opacity-50"
                            >
                                <SkipForward size={18} />
                                {t('onboarding.auth.skip')}
                            </button>
                        </div>
                    </div>
                </div>
                
                {/* Skip entire onboarding button */}
                <button
                    onClick={handleSkip}
                    disabled={isLoading || isAuthenticating}
                    className="absolute bottom-8 right-8 z-10 flex items-center gap-2 font-semibold text-sm text-white/60 hover:text-white hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 disabled:opacity-50 disabled:cursor-not-allowed"
                >
                    <SkipForward size={16} />
                    {t('onboarding.skip')}
                </button>
                
                {/* CSS animations */}
                <style>{`
                    @keyframes fadeIn {
                        from { opacity: 0; }
                        to { opacity: 1; }
                    }
                `}</style>
            </div>
        );
    }
    
    // Setup Phase - Multi-step wizard
    const bgImage = getCurrentBackground();
    
    // Fixed height for content area to prevent jumping
    const CONTENT_HEIGHT = 420;
    
    return (
        <div className="fixed inset-0 z-[100] flex items-center justify-center overflow-hidden">
            {/* Background with blur - changes in real-time */}
            <div 
                className="absolute inset-0 bg-cover bg-center transition-all duration-1000"
                style={{ 
                    backgroundImage: bgImage ? `url(${bgImage})` : 'none',
                    filter: 'blur(20px) brightness(0.3)',
                    transform: 'scale(1.1)'
                }}
            />
            
            {/* Dark overlay */}
            <div className="absolute inset-0 bg-black/50" />
            
            {/* Modal */}
            <div className="relative z-10 w-full max-w-3xl mx-4 overflow-hidden shadow-2xl glass-panel-static-solid">
                {/* Header */}
                <div className="p-6 border-b border-white/10 bg-gradient-to-r from-[#151515]/80 to-[#111111]/80">
                    <div className="flex items-center justify-between">
                        <div className="flex items-center gap-4">
                            <img src={appIcon} alt="HyPrism" className="w-12 h-12 rounded-xl" />
                            <div>
                                <h1 className="text-2xl font-bold text-white">{t('onboarding.welcome')}</h1>
                                <p className="text-sm text-white/60">
                                    {isAuthenticated && authenticatedUsername 
                                        ? t('onboarding.welcomeBack', { name: authenticatedUsername })
                                        : t('onboarding.letsSetUp')
                                    }
                                </p>
                            </div>
                        </div>
                        <p className="text-xs text-white/40">v{launcherVersion}</p>
                    </div>
                    
                    {/* Step indicator */}
                    <div className="flex items-center gap-1 mt-6 overflow-x-auto pb-1">
                        {steps.map((step, index) => (
                            <React.Fragment key={step.id}>
                                <button
                                    onClick={() => setCurrentStep(step.id)}
                                    className={`flex items-center gap-2 px-3 py-1.5 rounded-full transition-all whitespace-nowrap ${
                                        index === currentStepIndex 
                                            ? 'bg-white/10' 
                                            : index < currentStepIndex 
                                                ? 'opacity-100 hover:bg-white/5' 
                                                : 'opacity-40 hover:opacity-60'
                                    }`}
                                    style={index === currentStepIndex ? { backgroundColor: `${accentColor}20`, borderColor: `${accentColor}50` } : {}}
                                >
                                    <div 
                                        className="w-6 h-6 rounded-full flex items-center justify-center text-xs font-medium"
                                        style={{ 
                                            backgroundColor: index <= currentStepIndex ? accentColor : 'rgba(255,255,255,0.1)',
                                            color: index <= currentStepIndex ? accentTextColor : 'white'
                                        }}
                                    >
                                        {index < currentStepIndex ? <Check size={12} strokeWidth={3} /> : index + 1}
                                    </div>
                                    <span className={`text-sm ${index === currentStepIndex ? 'text-white' : 'text-white/60'}`}>
                                        {step.label}
                                    </span>
                                </button>
                                {index < steps.length - 1 && (
                                    <ChevronRight size={14} className="text-white/20 flex-shrink-0" />
                                )}
                            </React.Fragment>
                        ))}
                    </div>
                </div>
                
                {/* Content - Fixed height */}
                <div 
                    className="p-6 overflow-y-auto"
                    style={{ height: CONTENT_HEIGHT }}
                >
                    {/* Step 1: Language Selection */}
                    {currentStep === 'language' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-xl font-semibold text-white mb-2">{t('onboarding.chooseLanguage')}</h2>
                                <p className="text-sm text-white/60">{t('onboarding.selectLanguageHint')}</p>
                            </div>
                            
                            <div className="grid grid-cols-2 sm:grid-cols-3 gap-2 max-h-[280px] overflow-y-auto pr-2">
                                {Object.values(LANGUAGE_CONFIG).map((lang) => (
                                    <button
                                        key={lang.code}
                                        onClick={() => handleLanguageChange(lang.code)}
                                        className="p-3 rounded-xl border transition-all text-left hover:border-white/20"
                                        style={{
                                            backgroundColor: i18n.language === lang.code ? `${accentColor}20` : 'rgba(26,26,26,0.8)',
                                            borderColor: i18n.language === lang.code ? `${accentColor}50` : 'rgba(255,255,255,0.1)'
                                        }}
                                    >
                                        <div className="flex items-center gap-2">
                                            {i18n.language === lang.code && (
                                                <div 
                                                    className="w-5 h-5 rounded-full flex items-center justify-center flex-shrink-0"
                                                    style={{ backgroundColor: accentColor }}
                                                >
                                                    <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />
                                                </div>
                                            )}
                                            <div className={i18n.language !== lang.code ? 'ml-7' : ''}>
                                                <span className="text-white text-sm font-medium block">{lang.nativeName}</span>
                                                <span className="text-xs text-white/40">{lang.name}</span>
                                            </div>
                                        </div>
                                    </button>
                                ))}
                            </div>
                        </div>
                    )}
                    
                    {/* Step 2: Profile Setup (only if not authenticated) */}
                    {currentStep === 'profile' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-xl font-semibold text-white mb-2">{t('onboarding.setupProfile')}</h2>
                                <p className="text-sm text-white/60">{t('onboarding.chooseUsername')}</p>
                            </div>
                            
                            {/* Username */}
                            <div>
                                <label className="block text-sm text-white/60 mb-2">{t('onboarding.username')}</label>
                                <div className="flex items-center gap-3">
                                    <div 
                                        className="w-12 h-12 rounded-xl flex items-center justify-center flex-shrink-0" 
                                        style={{ backgroundColor: `${accentColor}20` }}
                                    >
                                        <User size={24} style={{ color: accentColor }} />
                                    </div>
                                    <input
                                        type="text"
                                        value={username}
                                        onChange={(e) => setUsername(e.target.value.slice(0, 16))}
                                        placeholder={t('onboarding.enterUsername')}
                                        className="flex-1 h-12 px-4 rounded-xl bg-[#1a1a1a]/80 border border-white/10 text-white text-sm focus:outline-none focus:border-white/30"
                                        maxLength={16}
                                    />
                                    <button
                                        onClick={handleGenerateUsername}
                                        disabled={isGeneratingUsername}
                                        className="h-12 px-4 rounded-xl bg-[#1a1a1a]/80 border border-white/10 flex items-center gap-2 text-white/60 hover:text-white hover:border-white/20 transition-colors disabled:opacity-50"
                                        title={t('onboarding.generateUsername')}
                                    >
                                        <RefreshCw size={18} className={isGeneratingUsername ? 'animate-spin' : ''} />
                                    </button>
                                </div>
                                <p className="text-xs text-white/40 mt-2">{t('onboarding.usernameHint')}</p>
                            </div>
                        </div>
                    )}
                    
                    {/* Step: Hardware (GPU Selection) - only if multiple GPUs */}
                    {currentStep === 'hardware' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-xl font-semibold text-white mb-2">{t('onboarding.hardwareSettings')}</h2>
                                <p className="text-sm text-white/60">{t('onboarding.gpuHint')}</p>
                            </div>
                            
                            {/* GPU Preference */}
                            <div>
                                <label className="text-sm text-white/60 mb-3 flex items-center gap-2">
                                    <Cpu size={14} />
                                    {t('onboarding.gpuPreference')}
                                </label>
                                <div className="space-y-2">
                                    {(['auto', 'dedicated', 'integrated'] as const).map((option) => {
                                        const icons: Record<string, React.ReactNode> = {
                                            dedicated: <Monitor size={18} />,
                                            integrated: <HardDrive size={18} />,
                                            auto: <Settings size={18} />,
                                        };
                                        const isSelected = gpuPreference === option;
                                        // Find GPU model name for this type
                                        const matchingGpu = gpuAdapters.find(g => g.type === option);
                                        const gpuModelName = option === 'auto'
                                            ? (gpuAdapters.length > 0 ? gpuAdapters.map(g => g.name).join(' / ') : undefined)
                                            : matchingGpu?.name;
                                        return (
                                            <button
                                                key={option}
                                                onClick={() => handleGpuPreferenceChange(option)}
                                                className="w-full p-3 rounded-xl border transition-all text-left cursor-pointer"
                                                style={{
                                                    backgroundColor: isSelected ? `${accentColor}15` : '#151515',
                                                    borderColor: isSelected ? `${accentColor}50` : 'rgba(255,255,255,0.08)'
                                                }}
                                            >
                                                <div className="flex items-center justify-between">
                                                    <div className="flex items-center gap-3">
                                                        <div
                                                            className="w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0"
                                                            style={{ backgroundColor: isSelected ? `${accentColor}25` : 'rgba(255,255,255,0.06)' }}
                                                        >
                                                            <span style={{ color: isSelected ? accentColor : 'rgba(255,255,255,0.5)' }}>{icons[option]}</span>
                                                        </div>
                                                        <div>
                                                            <div className="text-white text-sm font-medium">{t(`onboarding.gpu_${option}`)}</div>
                                                            {gpuModelName ? (
                                                                <div className="text-[11px] text-white/40 mt-0.5">{gpuModelName}</div>
                                                            ) : (
                                                                <div className="text-[11px] text-white/30 mt-0.5">{t(`onboarding.gpu_${option}Hint`)}</div>
                                                            )}
                                                        </div>
                                                    </div>
                                                    {isSelected && (
                                                        <div
                                                            className="w-5 h-5 rounded-full flex items-center justify-center flex-shrink-0"
                                                            style={{ backgroundColor: accentColor }}
                                                        >
                                                            <Check size={12} style={{ color: accentTextColor }} strokeWidth={3} />
                                                        </div>
                                                    )}
                                                </div>
                                            </button>
                                        );
                                    })}
                                </div>
                            </div>
                        </div>
                    )}
                    
                    {/* Step: Visual Settings */}
                    {currentStep === 'visual' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-xl font-semibold text-white mb-2">{t('onboarding.customizeAppearance')}</h2>
                                <p className="text-sm text-white/60">{t('onboarding.chooseAccentAndBg')}</p>
                            </div>
                            
                            {/* Accent Color - Circular */}
                            <div>
                                <label className="text-sm text-white/60 mb-3 flex items-center gap-2">
                                    <Palette size={14} />
                                    {t('onboarding.accentColor')}
                                </label>
                                <div className="flex flex-wrap gap-3">
                                    {ACCENT_COLORS.map((color) => (
                                        <button
                                            key={color}
                                            onClick={() => setAccentColor(color)}
                                            className={`w-10 h-10 rounded-full transition-all ${
                                                accentColor === color 
                                                    ? 'ring-2 ring-white ring-offset-2 ring-offset-[#111111]' 
                                                    : 'hover:scale-110'
                                            }`}
                                            style={{ backgroundColor: color }}
                                        >
                                            {accentColor === color && (
                                                <div className="w-full h-full flex items-center justify-center">
                                                    <Check 
                                                        size={18} 
                                                        className={color === '#FFFFFF' ? 'text-black' : 'text-white'} 
                                                        strokeWidth={3} 
                                                    />
                                                </div>
                                            )}
                                        </button>
                                    ))}
                                </div>
                            </div>
                            
                            {/* Background Chooser */}
                            <div>
                                <label className="text-sm text-white/60 mb-3 flex items-center gap-2">
                                    <Image size={14} />
                                    {t('onboarding.background')}
                                </label>
                                
                                {/* Slideshow option */}
                                <div 
                                    className="p-3 rounded-xl border cursor-pointer transition-colors mb-3"
                                    style={{
                                        backgroundColor: backgroundMode === 'slideshow' ? `${accentColor}20` : 'rgba(26,26,26,0.8)',
                                        borderColor: backgroundMode === 'slideshow' ? `${accentColor}50` : 'rgba(255,255,255,0.1)'
                                    }}
                                    onClick={() => handleBackgroundModeChange('slideshow')}
                                >
                                    <div className="flex items-center gap-3">
                                        <div 
                                            className="w-5 h-5 rounded-full border-2 flex items-center justify-center"
                                            style={{ borderColor: backgroundMode === 'slideshow' ? accentColor : 'rgba(255,255,255,0.3)' }}
                                        >
                                            {backgroundMode === 'slideshow' && (
                                                <div className="w-2.5 h-2.5 rounded-full" style={{ backgroundColor: accentColor }} />
                                            )}
                                        </div>
                                        <div>
                                            <span className="text-white text-sm font-medium">{t('onboarding.slideshow')}</span>
                                            <p className="text-xs text-white/40">{t('onboarding.slideshowHint')}</p>
                                        </div>
                                    </div>
                                </div>
                                
                                {/* Background grid */}
                                <p className="text-xs text-white/40 mb-2">{t('onboarding.staticBackground')}</p>
                                <div className="grid grid-cols-4 gap-2">
                                    {backgroundImages.slice(0, 8).map((bg) => (
                                        <div
                                            key={bg.name}
                                            className="relative aspect-video rounded-lg overflow-hidden cursor-pointer border-2 transition-all hover:opacity-100"
                                            style={{
                                                borderColor: backgroundMode === bg.name ? accentColor : 'transparent',
                                                boxShadow: backgroundMode === bg.name ? `0 0 0 2px ${accentColor}30` : 'none',
                                                opacity: backgroundMode === bg.name ? 1 : 0.7
                                            }}
                                            onClick={() => handleBackgroundModeChange(bg.name)}
                                        >
                                            <img 
                                                src={bg.url} 
                                                alt={bg.name}
                                                className="w-full h-full object-cover"
                                            />
                                            {backgroundMode === bg.name && (
                                                <div className="absolute inset-0 flex items-center justify-center bg-black/30">
                                                    <Check size={20} className="text-white" />
                                                </div>
                                            )}
                                        </div>
                                    ))}
                                </div>
                            </div>
                        </div>
                    )}
                    
                    {/* Step: Instance Location */}
                    {currentStep === 'location' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-xl font-semibold text-white mb-2">{t('onboarding.chooseLocation')}</h2>
                                <p className="text-sm text-white/60">{t('onboarding.locationHint')}</p>
                            </div>
                            
                            <div className="p-4 rounded-xl bg-[#1a1a1a]/80 border border-white/10">
                                <label className="block text-sm text-white/60 mb-3">{t('onboarding.instanceFolder')}</label>
                                <div className="flex gap-2">
                                    <input
                                        type="text"
                                        value={instanceDir}
                                        onChange={(e) => setInstanceDir(e.target.value)}
                                        className="flex-1 h-12 px-4 rounded-xl bg-[#0a0a0a]/80 border border-white/10 text-white text-sm focus:outline-none focus:border-white/30 truncate"
                                    />
                                    <button
                                        onClick={handleBrowseInstanceDir}
                                        className="h-12 px-4 rounded-xl bg-[#0a0a0a]/80 border border-white/10 flex items-center gap-2 text-white/60 hover:text-white hover:border-white/20 transition-colors"
                                    >
                                        <FolderOpen size={18} />
                                        <span className="text-sm">{t('common.browse')}</span>
                                    </button>
                                </div>
                                <p className="text-xs text-white/40 mt-2">{t('onboarding.filesStoredHere')}</p>
                            </div>
                            
                            {/* Quick info */}
                            <div className="p-4 rounded-xl bg-[#1a1a1a]/50 border border-white/5">
                                <p className="text-xs text-white/40 leading-relaxed">
                                    {t('onboarding.changeInSettings')}
                                </p>
                            </div>
                        </div>
                    )}
                    
                    {/* Step: About */}
                    {currentStep === 'about' && (
                        <div className="space-y-6">
                            <div>
                                <h2 className="text-xl font-semibold text-white mb-2">{t('onboarding.aboutHyPrism')}</h2>
                                <p className="text-sm text-white/60">{t('onboarding.allSet')}</p>
                            </div>
                            
                            {/* App Icon and Info */}
                            <div className="flex flex-col items-center py-2">
                                <img 
                                    src={appIcon} 
                                    alt="HyPrism" 
                                    className="w-16 h-16 mb-2"
                                />
                                <h3 className="text-xl font-bold text-white">HyPrism</h3>
                                <p className="text-sm text-white/50">{t('onboarding.unofficial')}</p>
                                <p className="text-xs text-white/30 mt-1">v{launcherVersion}</p>
                            </div>
                            
                            {/* Social Buttons */}
                            <div className="flex justify-center gap-4">
                                <button
                                    onClick={openGitHub}
                                    className="opacity-80 hover:opacity-100 transition-opacity"
                                    title="GitHub"
                                >
                                    <Github size={28} className="text-white" />
                                </button>
                                <button
                                    onClick={openDiscord}
                                    className="opacity-80 hover:opacity-100 transition-opacity"
                                    title="Discord"
                                >
                                    <DiscordIcon size={28} color="white" />
                                </button>
                                <button
                                    onClick={openBugReport}
                                    className="opacity-80 hover:opacity-100 transition-opacity"
                                    title={t('onboarding.bugReport')}
                                >
                                    <Bug size={28} className="text-white" />
                                </button>
                            </div>
                            
                            {/* Contributors Section */}
                            <div className="pt-2">
                                {isLoadingContributors ? (
                                    <div className="flex justify-center py-4">
                                        <Loader2 size={24} className="animate-spin" style={{ color: accentColor }} />
                                    </div>
                                ) : contributors.length > 0 ? (
                                    <div className="space-y-3">
                                        {/* Maintainer & Auth Server Creator */}
                                        <div className="flex justify-center gap-3 flex-wrap">
                                            {maintainer && (
                                                <button
                                                    onClick={() => BrowserOpenURL(maintainer.html_url)}
                                                    className="flex items-center gap-2 p-2 rounded-lg hover:bg-white/5 transition-colors"
                                                >
                                                    <img 
                                                        src={maintainer.avatar_url} 
                                                        alt={maintainer.login}
                                                        className="w-12 h-12 rounded-full"
                                                    />
                                                    <div className="text-left">
                                                        <span className="text-white font-medium text-sm">{maintainer.login}</span>
                                                        <p className="text-xs text-white/40">{t('onboarding.maintainerRole')}</p>
                                                    </div>
                                                </button>
                                            )}
                                            <button
                                                onClick={() => BrowserOpenURL('https://github.com/sanasol')}
                                                className="flex items-center gap-2 p-2 rounded-lg hover:bg-white/5 transition-colors"
                                            >
                                                <img 
                                                    src="https://avatars.githubusercontent.com/u/1709666?v=4" 
                                                    alt="sanasol"
                                                    className="w-12 h-12 rounded-full"
                                                />
                                                <div className="text-left">
                                                    <span className="text-white font-medium text-sm">sanasol</span>
                                                    <p className="text-xs text-white/40">{t('onboarding.authServerCreator')}</p>
                                                </div>
                                            </button>
                                        </div>

                                        {/* Other Contributors - same size as Settings modal */}
                                        {otherContributors.length > 0 && (
                                            <div className="grid grid-cols-5 gap-3 justify-items-center">
                                                {otherContributors.map((contributor) => (
                                                    <button
                                                        key={contributor.login}
                                                        onClick={() => BrowserOpenURL(contributor.html_url)}
                                                        className="flex flex-col items-center gap-1.5 p-2 rounded-lg hover:bg-white/5 transition-colors w-full"
                                                        title={`${contributor.login} - ${contributor.contributions} contributions`}
                                                    >
                                                        <img 
                                                            src={contributor.avatar_url} 
                                                            alt={contributor.login}
                                                            className="w-12 h-12 rounded-full"
                                                        />
                                                        <span className="text-xs text-white/60 max-w-full truncate text-center">
                                                            {truncateName(contributor.login, 10)}
                                                        </span>
                                                    </button>
                                                ))}
                                            </div>
                                        )}
                                    </div>
                                ) : null}
                            </div>
                            
                            {/* Disclaimer */}
                            <div className="p-3 rounded-xl bg-[#1a1a1a]/80 border border-white/5">
                                <p className="text-white/50 text-xs text-center">
                                    {t('onboarding.disclaimer')}
                                </p>
                            </div>
                        </div>
                    )}
                </div>
                
                {/* Footer */}
                <div className="p-6 border-t border-white/10 bg-[#0a0a0a]/80">
                    <div className="flex justify-end items-center">
                        <div className="flex items-center gap-3">
                            {/* Back button */}
                            {currentStepIndex > 0 && (
                                <button
                                    onClick={handlePrevStep}
                                    className="px-6 py-3 rounded-xl font-medium bg-white/5 text-white/60 hover:bg-white/10 hover:text-white transition-all"
                                >
                                    {t('common.back')}
                                </button>
                            )}
                            
                            {/* Next/Finish button */}
                            {currentStep !== 'about' ? (
                                <button
                                    onClick={handleNextStep}
                                    disabled={currentStep === 'profile' && (!username.trim() || username.trim().length < 1)}
                                    className="flex items-center gap-2 px-6 py-3 rounded-xl font-medium transition-all hover:opacity-90 disabled:opacity-50 disabled:cursor-not-allowed"
                                    style={{ backgroundColor: accentColor, color: accentTextColor }}
                                >
                                    {t('common.continue')}
                                    <ChevronRight size={18} />
                                </button>
                            ) : (
                                <button
                                    onClick={handleComplete}
                                    disabled={isLoading}
                                    className="flex items-center gap-2 px-6 py-3 rounded-xl font-medium transition-all hover:opacity-90 disabled:opacity-50"
                                    style={{ backgroundColor: accentColor, color: accentTextColor }}
                                >
                                    {isLoading ? (
                                        <Loader2 size={18} className="animate-spin" />
                                    ) : (
                                        <>
                                            <ArrowRight size={18} />
                                            {t('onboarding.enterLauncher')}
                                        </>
                                    )}
                                </button>
                            )}
                        </div>
                    </div>
                </div>
            </div>
            
            {/* Skip button - visible during setup phase */}
            <button
                onClick={handleSkip}
                disabled={isLoading}
                className="absolute bottom-8 right-8 z-10 flex items-center gap-2 font-semibold text-sm text-white/60 hover:text-white hover:scale-[1.02] active:scale-[0.98] transition-all duration-150 disabled:opacity-50 disabled:cursor-not-allowed"
            >
                <SkipForward size={16} />
                {t('onboarding.skip')}
            </button>
            
            {/* CSS animations */}
            <style>{`
                @keyframes bounceIn {
                    0% { opacity: 0; transform: scale(0.3); }
                    50% { opacity: 1; transform: scale(1.05); }
                    70% { transform: scale(0.95); }
                    100% { transform: scale(1); }
                }
                @keyframes slideUp {
                    from { opacity: 0; transform: translateY(20px); }
                    to { opacity: 1; transform: translateY(0); }
                }
                @keyframes fadeIn {
                    from { opacity: 0; }
                    to { opacity: 1; }
                }
            `}</style>
        </div>
    );
};

export default OnboardingModal;
