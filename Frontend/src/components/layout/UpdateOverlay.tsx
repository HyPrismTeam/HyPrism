import React, { memo } from 'react';
import { motion } from 'framer-motion';
import { DownloadCloud } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { useAccentColor } from '../../contexts/AccentColorContext';

import { formatBytes } from '../../utils/format';

interface UpdateOverlayProps {
  progress: number;
  downloaded: number;
  total: number;
}

export const UpdateOverlay: React.FC<UpdateOverlayProps> = memo(({ progress, downloaded, total }) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      className={`absolute inset-0 z-[100] bg-[#090909]/95  flex flex-col items-center justify-center p-20 text-center`}
    >
      <motion.div
        animate={{
          y: [0, -10, 0],
          scale: [1, 1.05, 1]
        }}
        transition={{
          duration: 2,
          repeat: Infinity,
          ease: "easeInOut"
        }}
      >
        <DownloadCloud size={80} className="mb-8" style={{ color: accentColor }} />
      </motion.div>

      <h1 className="text-5xl font-black mb-4 tracking-tight text-white">
        {t('updateOverlay.title')}
      </h1>

      <p className="text-gray-400 mb-12 max-w-md text-lg font-medium">
        {t('updateOverlay.message')}
      </p>

      {/* Progress bar */}
      <div className="w-full max-w-md">
        <div className="relative h-3 bg-white/5 rounded-full overflow-hidden">
          <motion.div
            initial={{ width: 0 }}
            animate={{ width: `${Math.min(progress, 100)}%` }}
            transition={{ duration: 0.3 }}
            className="absolute inset-y-0 left-0 rounded-full"
            style={{ background: `linear-gradient(to right, ${accentColor}, ${accentColor}cc)` }}
          />
          <div className="absolute inset-0 animate-shimmer" />
        </div>

        <div className="flex justify-between items-center mt-4 text-sm">
          <span className="text-gray-400">
            {formatBytes(downloaded)} / {formatBytes(total)}
          </span>
          <span className="font-bold" style={{ color: accentColor }}>{Math.round(progress)}%</span>
        </div>
      </div>

      <p className="text-xs text-gray-500 mt-8">
        {t('updateOverlay.autoRestart')}
      </p>
    </motion.div>
  );
});

UpdateOverlay.displayName = 'UpdateOverlay';
