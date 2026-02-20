import React from 'react';
import { Github, Bug, Loader2 } from 'lucide-react';
import { IconButton } from '@/components/ui/Controls';
import { openUrl } from '@/utils/openUrl';
import { DiscordIcon } from '@/components/icons/DiscordIcon';
import type { UseOnboardingReturn } from '@/hooks/useOnboarding';
import appIcon from '@/assets/images/logo.png';

interface AboutStepProps {
  onboarding: UseOnboardingReturn;
}

export const AboutStep: React.FC<AboutStepProps> = ({ onboarding }) => {
  return (
    <div className="space-y-6">
      <div>
        <h2 className="text-xl font-semibold text-white mb-2">{onboarding.t('onboarding.aboutHyPrism')}</h2>
        <p className="text-sm text-white/60">{onboarding.t('onboarding.allSet')}</p>
      </div>
      
      {/* App Icon and Info */}
      <div className="flex flex-col items-center py-2">
        <img 
          src={appIcon} 
          alt="HyPrism" 
          className="w-16 h-16 mb-2"
        />
        <h3 className="text-xl font-bold text-white">HyPrism</h3>
        <p className="text-sm text-white/50">{onboarding.t('onboarding.unofficial')}</p>
        <p className="text-xs text-white/30 mt-1">v{onboarding.launcherVersion}</p>
      </div>
      
      {/* Social Buttons */}
      <div className="flex justify-center gap-4">
        <IconButton
          variant="ghost"
          onClick={onboarding.openGitHub}
          title={onboarding.t('social.github')}
          size="lg"
        >
          <Github size={28} className="text-white" />
        </IconButton>
        <IconButton
          variant="ghost"
          onClick={onboarding.openDiscord}
          title={onboarding.t('social.discord')}
          size="lg"
        >
          <DiscordIcon size={28} color="white" />
        </IconButton>
        <IconButton
          variant="ghost"
          onClick={onboarding.openBugReport}
          title={onboarding.t('onboarding.bugReport')}
          size="lg"
        >
          <Bug size={28} className="text-white" />
        </IconButton>
      </div>
      
      {/* Contributors Section */}
      <div className="pt-2">
        {onboarding.isLoadingContributors ? (
          <div className="flex justify-center py-4">
            <Loader2 size={24} className="animate-spin" style={{ color: onboarding.accentColor }} />
          </div>
        ) : onboarding.contributors.length > 0 ? (
          <div className="space-y-3">
            {/* Maintainer & Auth Server Creator */}
            <div className="flex justify-center gap-3 flex-wrap">
              {onboarding.maintainer && (
                <button
                  onClick={() => openUrl(onboarding.maintainer!.html_url)}
                  className="flex items-center gap-2 p-2 rounded-lg hover:bg-white/5 transition-colors"
                >
                  <img 
                    src={onboarding.maintainer.avatar_url} 
                    alt={onboarding.maintainer.login}
                    className="w-12 h-12 rounded-full"
                  />
                  <div className="text-left">
                    <span className="text-white font-medium text-sm">{onboarding.maintainer.login}</span>
                    <p className="text-xs text-white/40">{onboarding.t('onboarding.maintainerRole')}</p>
                  </div>
                </button>
              )}
              <button
                onClick={() => openUrl('https://github.com/sanasol')}
                className="flex items-center gap-2 p-2 rounded-lg hover:bg-white/5 transition-colors"
              >
                <img 
                  src="https://avatars.githubusercontent.com/u/1709666?v=4" 
                  alt="sanasol"
                  className="w-12 h-12 rounded-full"
                />
                <div className="text-left">
                  <span className="text-white font-medium text-sm">sanasol</span>
                  <p className="text-xs text-white/40">{onboarding.t('onboarding.authServerCreator')}</p>
                </div>
              </button>
            </div>

            {/* Other Contributors */}
            {onboarding.otherContributors.length > 0 && (
              <div className="grid grid-cols-5 gap-3 justify-items-center">
                {onboarding.otherContributors.map((contributor) => (
                  <button
                    key={contributor.login}
                    onClick={() => openUrl(contributor.html_url)}
                    className="flex flex-col items-center gap-1.5 p-2 rounded-lg hover:bg-white/5 transition-colors w-full"
                    title={`${contributor.login} - ${contributor.contributions} contributions`}
                  >
                    <img 
                      src={contributor.avatar_url} 
                      alt={contributor.login}
                      className="w-12 h-12 rounded-full"
                    />
                    <span className="text-xs text-white/60 max-w-full truncate text-center">
                      {onboarding.truncateName(contributor.login, 10)}
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
          {onboarding.t('onboarding.disclaimer')}
        </p>
      </div>
    </div>
  );
};
