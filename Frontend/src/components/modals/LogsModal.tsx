import React, { useState, useEffect, useRef } from 'react';
import { motion } from 'framer-motion';
import { X, Terminal, Loader2, Download, Copy, Check } from 'lucide-react';
import { useAccentColor } from '../../contexts/AccentColorContext';
import { ModalOverlay } from './ModalOverlay';

interface LogsModalProps {
  onClose: () => void;
  getGameLogs: () => Promise<string>;
}
import { useTranslation } from 'react-i18next';

export const LogsModal: React.FC<LogsModalProps> = ({
  onClose,
  getGameLogs
}) => {
  const { t } = useTranslation();
  const { accentColor } = useAccentColor();
  const [logs, setLogs] = useState<string>('');
  const [isLoading, setIsLoading] = useState(true);
  const [copied, setCopied] = useState(false);
  const logsRef = useRef<HTMLPreElement>(null);
  const shouldScrollRef = useRef(true);

  const fetchLogs = async () => {
    try {
      const content = await getGameLogs();
      setLogs(content);
      setIsLoading(false);

      // Auto-scroll to bottom only if user is already at bottom
      setTimeout(() => {
        if (logsRef.current && shouldScrollRef.current) {
          logsRef.current.scrollTop = logsRef.current.scrollHeight;
        }
      }, 50);
    } catch (err) {
      setLogs(t('logs.failedToLoad') + (err instanceof Error ? err.message : String(err)));
      setIsLoading(false);
    }
  };

  // Initial load
  useEffect(() => {
    fetchLogs();
  }, []);

  // Auto-refresh every 3 seconds
  useEffect(() => {
    const interval = setInterval(fetchLogs, 3000);
    return () => clearInterval(interval);
  }, []);

  // Track if user has scrolled away from bottom
  const handleScroll = () => {
    if (logsRef.current) {
      const { scrollTop, scrollHeight, clientHeight } = logsRef.current;
      shouldScrollRef.current = scrollHeight - scrollTop - clientHeight < 50;
    }
  };

  const copyLogs = async () => {
    try {
      await navigator.clipboard.writeText(logs);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Failed to copy:', err);
    }
  };

  const downloadLogs = () => {
    const blob = new Blob([logs], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `hyprism-logs-${new Date().toISOString().split('T')[0]}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  };

  return (
    <ModalOverlay zClass="z-50">
      <motion.div
        initial={{ scale: 0.9, opacity: 0 }}
        animate={{ scale: 1, opacity: 1 }}
        exit={{ scale: 0.9, opacity: 0 }}
        className="w-full max-w-3xl h-[80vh] bg-[#0d0d0d] rounded-2xl border border-white/10 overflow-hidden flex flex-col"
      >
        {/* Header */}
        <div className="flex items-center justify-between p-5 border-b border-white/10 flex-shrink-0">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-xl flex items-center justify-center" style={{ backgroundColor: `${accentColor}33` }}>
              <Terminal size={20} style={{ color: accentColor }} />
            </div>
            <div>
              <h2 className="text-lg font-bold text-white">{t('logs.title')}</h2>
              <p className="text-xs text-gray-400">{t('logs.description')}</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="p-2 rounded-lg hover:bg-white/5 text-gray-400 hover:text-white transition-colors"
          >
            <X size={20} />
          </button>
        </div>

        {/* Logs Content */}
        <div className="flex-1 p-4 overflow-hidden">
          {isLoading && !logs ? (
            <div className="flex flex-col items-center justify-center h-full">
              <Loader2 size={32} className="animate-spin mb-4" style={{ color: accentColor }} />
              <p className="text-gray-400">{t('logs.loading')}</p>
            </div>
          ) : (
            <pre
              ref={logsRef}
              onScroll={handleScroll}
              className="w-full h-full overflow-auto bg-black/50 rounded-xl p-4 text-xs font-mono text-gray-300 whitespace-pre-wrap"
              style={{ tabSize: 4 }}
            >
              {logs || t('logs.noLogs')}
            </pre>
          )}
        </div>

        {/* Footer */}
        <div className="flex items-center justify-between p-5 border-t border-white/10 bg-black/30 flex-shrink-0">
          <div className="flex items-center gap-2 text-xs text-gray-500">
            <div className="w-2 h-2 rounded-full bg-green-500 animate-pulse" />
            {t('logs.autoRefresh')}
          </div>

          <div className="flex gap-3">
            <button
              onClick={copyLogs}
              className="flex items-center gap-2 px-4 py-2 rounded-lg bg-white/5 text-gray-300 hover:bg-white/10 transition-colors"
            >
              {copied ? <Check size={14} className="text-green-400" /> : <Copy size={14} />}
              {copied ? t('common.copied') : t('common.copy')}
            </button>
            <button
              onClick={downloadLogs}
              className="flex items-center gap-2 px-4 py-2 rounded-lg bg-white/5 text-gray-300 hover:bg-white/10 transition-colors"
            >
              <Download size={14} />
              {t('logs.download')}
            </button>
            <button
              onClick={onClose}
              className="px-6 py-2 rounded-lg transition-colors font-medium hover:opacity-80"
              style={{ backgroundColor: `${accentColor}33`, color: accentColor }}
            >
              {t('common.close')}
            </button>
          </div>
        </div>
      </motion.div>
    </ModalOverlay>
  );
};
