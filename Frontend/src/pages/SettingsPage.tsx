import * as React from 'preact/compat';
import { h, ComponentChildren } from 'preact';
import { Suspense, lazy } from 'preact/compat';
import { motion } from '../lib/motion';
import { useI18n } from '../lib/i18n';
import { PageContainer } from '../components/ui/PageContainer';
import { SettingsHeader } from '../components/ui/SettingsHeader';
import { LoadingSpinner } from '../components/ui/LoadingSpinner';
import { pageVariants } from '../constants/animations';

const SettingsPageContent = lazy(() => import('./settings/SettingsPage').then(m => ({ default: m.SettingsPage })));

interface SettingsPageProps {
  launcherBranch: string;
  onLauncherBranchChange: (branch: string) => void;
  rosettaWarning?: { message: string; command: string; tutorialUrl?: string } | null;
  onBackgroundModeChange?: (mode: string) => void;
  onInstanceDeleted?: () => void;
  onNavigateToMods?: () => void;
  onAuthSettingsChange?: () => void;
  isGameRunning?: boolean;
  onMovingDataChange?: (isMoving: boolean) => void;
}

export const SettingsPage: React.FC<SettingsPageProps> = (props) => {
  const { t } = useI18n();

  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full w-full"
    >
      <PageContainer contentClassName="h-full">
        <div className="h-full flex flex-col">
          <div className="flex-shrink-0 mb-4">
            <SettingsHeader title={t('settings.title')} />
          </div>
          <div className="flex-1 min-h-0">
            <Suspense fallback={
              <div className="flex items-center justify-center h-full">
                <LoadingSpinner />
              </div>
            }>
              <SettingsPageContent
                onClose={() => {}}
                launcherBranch={props.launcherBranch}
                onLauncherBranchChange={props.onLauncherBranchChange}
                rosettaWarning={props.rosettaWarning}
                onBackgroundModeChange={props.onBackgroundModeChange}
                onInstanceDeleted={props.onInstanceDeleted}
                onAuthSettingsChange={props.onAuthSettingsChange}
                pageMode={true}
                isGameRunning={props.isGameRunning}
                onMovingDataChange={props.onMovingDataChange}
              />
            </Suspense>
          </div>
        </div>
      </PageContainer>
    </motion.div>
  );
};
