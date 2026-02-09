import React, { Suspense, lazy } from 'react';
import { motion } from 'framer-motion';
import { Users } from 'lucide-react';
import { useTranslation } from 'react-i18next';

const ProfileEditor = lazy(() => import('../components/ProfileEditor'));

const pageVariants = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -12 },
};

interface ProfilesPageProps {
  onProfileUpdate?: () => void;
}

export const ProfilesPage: React.FC<ProfilesPageProps> = ({ onProfileUpdate }) => {
  const { t } = useTranslation();

  return (
    <motion.div
      variants={pageVariants}
      initial="initial"
      animate="animate"
      exit="exit"
      transition={{ duration: 0.3, ease: 'easeOut' }}
      className="h-full flex flex-col px-8 pt-14 pb-28"
    >
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
    </motion.div>
  );
};
