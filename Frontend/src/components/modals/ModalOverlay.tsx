import React from 'react';
import { motion } from 'framer-motion';
import { useAnimatedGlass } from '../../contexts/AnimatedGlassContext';

interface ModalOverlayProps {
  /** z-index class, e.g. "z-50" or "z-[200]" */
  zClass?: string;
  /** Extra classes for the outer container */
  className?: string;
  /** Click handler for backdrop dismiss */
  onClick?: (e: React.MouseEvent) => void;
  children: React.ReactNode;
}

/**
 * Full-screen modal overlay with smooth blur animation (glass)
 * or opaque solid background (when AnimatedGlass is OFF).
 *
 * Replaces the repeated pattern:
 *   `<motion.div className="fixed inset-0 bg-black/60 backdrop-blur-sm ...">`
 */
export const ModalOverlay: React.FC<ModalOverlayProps> = ({
  zClass = 'z-50',
  className = '',
  onClick,
  children,
}) => {
  const { animatedGlass } = useAnimatedGlass();

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.2 }}
      className={`fixed inset-0 ${zClass} flex items-center justify-center p-8 ${
        animatedGlass ? 'bg-black/60 modal-overlay-glass' : ''
      } ${className}`}
      style={!animatedGlass ? { background: 'rgba(0, 0, 0, 0.85)' } : undefined}
      onClick={onClick}
    >
      {children}
    </motion.div>
  );
};

export default ModalOverlay;
