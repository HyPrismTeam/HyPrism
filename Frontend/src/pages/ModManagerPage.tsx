import React, { Suspense, lazy, useRef } from 'react';
import { motion } from 'framer-motion';
import { Package } from 'lucide-react';
import { useTranslation } from 'react-i18next';

const ModManager = lazy(() => import('../components/ModManager').then(m => ({ default: m.ModManager })));

const pageVariants = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0 },
  exit: { opacity: 0, y: -12 },
};

interface ModManagerPageProps {
  currentBranch: string;
  currentVersion: number;
  currentProfileName?: string;
  initialSearchQuery?: string;
}

export const ModManagerPage: React.FC<ModManagerPageProps> = ({ currentBranch, currentVersion, currentProfileName, initialSearchQuery }) => {
  const { t } = useTranslation();
  const headerActionsRef = useRef<HTMLDivElement>(null);

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
          <Package size={22} className="text-white/80" />
          <h1 className="text-xl font-bold text-white">{t('modManager.title')}</h1>
        </div>
        {/* Portal target for ModManager action buttons */}
        <div ref={headerActionsRef} />
      </div>

      {/* Content */}
      <div className="flex-1 min-h-0">
        <Suspense fallback={
          <div className="flex items-center justify-center h-full">
            <div className="w-8 h-8 border-2 border-white/20 border-t-white rounded-full animate-spin" />
          </div>
        }>
          <ModManager
            onClose={() => {}}
            currentBranch={currentBranch}
            currentVersion={currentVersion}
            currentProfileName={currentProfileName}
            initialSearchQuery={initialSearchQuery}
            pageMode={true}
            headerActionsRef={headerActionsRef}
          />
        </Suspense>
      </div>
    </motion.div>
  );
};
