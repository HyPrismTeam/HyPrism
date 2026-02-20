import React from 'react';
import { useTranslation } from 'react-i18next';
import { Github, Bug } from 'lucide-react';
import { Button } from '@/components/ui/Controls';
import { DiscordIcon } from '@/components/icons/DiscordIcon';
import appIcon from '@/assets/images/logo.png';
import type { Contributor } from '@/hooks/useSettings';

interface AboutTabProps {
  gc: string;
  accentColor: string;
  contributors: Contributor[];
  isLoadingContributors: boolean;
  contributorsError: string | null;
  openGitHub: () => void;
  openDiscord: () => void;
  openBugReport: () => void;
  resetOnboarding: () => Promise<void>;
}

export const AboutTab: React.FC<AboutTabProps> = ({
  gc,
  accentColor,
  contributors,
  isLoadingContributors,
  contributorsError,
  openGitHub,
  openDiscord,
  openBugReport,
  resetOnboarding,
}) => {
  const { t } = useTranslation();

  const truncateName = (name: string, maxLength: number = 10) => {
    if (name.length <= maxLength) return name;
    return name.slice(0, maxLength - 2) + '...';
  };

  const maintainerFallback: Contributor = {
    login: 'yyyumeniku',
    avatar_url: 'https://avatars.githubusercontent.com/u/108021304?v=4',
    html_url: 'https://github.com/yyyumeniku',
    contributions: 0,
  };

  const maintainer = contributors.find(c => c.login.toLowerCase() === 'yyyumeniku') ?? maintainerFallback;
  const otherContributors = contributors.filter(c => !['yyyumeniku', 'freakdaniel'].includes(c.login.toLowerCase()));

  const openUrl = (url: string) => {
    import('@/utils/openUrl').then(({ openUrl: open }) => open(url));
  };

  return (
    <div className="space-y-6 w-full max-w-6xl mx-auto">
      <div className="grid grid-cols-1 xl:grid-cols-[260px_minmax(0,1fr)] gap-6 items-start">
        <div className={`p-5 rounded-2xl ${gc} space-y-5`}>
          <div className="flex flex-col items-center text-center">
            <img
              src={appIcon}
              alt="HyPrism"
              className="w-20 h-20 mb-3"
            />
            <h3 className="text-xl font-bold text-white">HyPrism</h3>
            <p className="text-sm text-white/50">{t('settings.aboutSettings.unofficial')}</p>
          </div>

          <div className="flex justify-center gap-4">
            <button
              onClick={openGitHub}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={t('social.github')}
            >
              <Github size={28} className="text-white" />
            </button>
            <button
              onClick={openDiscord}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={t('social.discord')}
            >
              <DiscordIcon size={20} color="white" />
            </button>
            <button
              onClick={openBugReport}
              className="opacity-80 hover:opacity-100 transition-opacity"
              title={t('settings.aboutSettings.bugReport')}
            >
              <Bug size={28} className="text-white" />
            </button>
          </div>

          <Button
            onClick={async () => {
              await resetOnboarding();
              window.location.reload();
            }}
            className="w-full"
          >
            {t('settings.aboutSettings.replayIntro')}
          </Button>
        </div>

        <div className="pt-1">
          {isLoadingContributors ? (
            <div className="flex justify-center py-4">
              <div className="w-6 h-6 border-2 rounded-full animate-spin" style={{ borderColor: `${accentColor}30`, borderTopColor: accentColor }} />
            </div>
          ) : (
            <div className="space-y-4">
              {/* Maintainer & Auth Server Creator */}
              <div className="flex flex-wrap gap-3 xl:gap-4">
                {maintainer && (
                  <button
                    onClick={() => openUrl(maintainer.html_url)}
                    className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors min-w-[240px] max-w-[360px]"
                  >
                    <img
                      src={maintainer.avatar_url}
                      alt={maintainer.login}
                      className="w-12 h-12 rounded-full"
                    />
                    <div className="text-left">
                      <span className="text-white font-medium text-sm">{maintainer.login}</span>
                      <p className="text-xs text-white/40">{t('settings.aboutSettings.maintainerRole')}</p>
                    </div>
                  </button>
                )}
                <button
                  onClick={() => openUrl('https://github.com/sanasol')}
                  className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors min-w-[240px] max-w-[360px]"
                >
                  <img
                    src="https://avatars.githubusercontent.com/u/1709666?v=4"
                    alt="sanasol"
                    className="w-12 h-12 rounded-full"
                  />
                  <div className="text-left">
                    <span className="text-white font-medium text-sm">sanasol</span>
                    <p className="text-xs text-white/40">{t('settings.aboutSettings.authRole')}</p>
                  </div>
                </button>
                <button
                  onClick={() => openUrl('https://github.com/freakdaniel')}
                  className="flex items-center gap-3 p-2 rounded-lg hover:bg-white/5 transition-colors min-w-[240px] max-w-[360px]"
                >
                  <img
                    src="https://avatars.githubusercontent.com/u/212660794?v=4"
                    alt="freakdaniel"
                    className="w-12 h-12 rounded-full"
                  />
                  <div className="text-left">
                    <span className="text-white font-medium text-sm">Daniel Freak</span>
                    <p className="text-xs text-white/40">{t('settings.aboutSettings.codevRole')}</p>
                  </div>
                </button>
              </div>

              {/* Description */}
              <p className="text-xs text-white/40 text-center xl:text-left">{t('settings.aboutSettings.contributorsDescription')}</p>

              {contributorsError ? (
                <p className="text-xs text-white/35 text-center xl:text-left">{contributorsError}</p>
              ) : null}

              {/* Other Contributors */}
              {otherContributors.length > 0 && (
                <div className="flex flex-wrap gap-3 sm:gap-4 justify-center xl:justify-start">
                  {otherContributors.map((contributor) => (
                    <button
                      key={contributor.login}
                      onClick={() => openUrl(contributor.html_url)}
                      className="flex flex-col items-center gap-1.5 p-2 rounded-lg hover:bg-white/5 transition-colors w-[88px]"
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
          )}
        </div>
      </div>

      {/* Disclaimer */}
      <div className={`p-4 rounded-2xl ${gc}`}>
        <p className="text-white/50 text-sm text-center">
          {t('settings.aboutSettings.disclaimer')}
        </p>
      </div>
    </div>
  );
};
