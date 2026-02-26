import * as React from 'preact/compat';
import { h, ComponentChildren } from 'preact';
import { Suspense, lazy } from 'preact/compat';
import { motion } from '../lib/motion';
import {  Users  } from '../components/icons/LucideIcons';
import { useI18n } from '../lib/i18n';
import { PageContainer } from '../components/ui/PageContainer';
import { pageVariants } from '../constants/animations';

const ProfileEditor = lazy(() => import('../components/ProfileEditor'));

interface ProfilesPageProps {
  onProfileUpdate?: () => void;
}

export const ProfilesPage: React.FC<ProfilesPageProps> = ({ onProfileUpdate }) => {
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
      {/* Page Header */}
      <div className="flex items-center justify-between mb-4 flex-shrink-0">
        <div className="flex items-center gap-3">
          <Users size={22} className="text-white/80" />
          <h1 className="text-xl font-bold text-white">{t('profiles.title')}</h1>
        </div>
      </div>
      {/* Content */}
      <div className="flex-1 min-h-0">
        <Suspense fallback={
          <div className="flex items-center justify-center h-full">
            <div className="w-8 h-8 border-2 border-white/20 border-t-white rounded-full animate-spin" />
          </div>
        }>
          <ProfileEditor
            isOpen={true}
            onClose={() => {}}
            onProfileUpdate={onProfileUpdate}
            pageMode={true}
          />
        </Suspense>
      </div>
      </div>
      </PageContainer>
    </motion.div>
  );
};
