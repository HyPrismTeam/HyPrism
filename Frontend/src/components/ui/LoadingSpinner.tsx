import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';

interface LoadingSpinnerProps {
  /** Size class override, e.g. "w-6 h-6" or "w-8 h-8". Defaults to "w-8 h-8" */
  size?: string;
  className?: string;
}

/**
 * Minimal loading spinner used across modal fallbacks and loading states.
 */
export const LoadingSpinner = ({
  size = 'w-8 h-8',
  className = '',
}: LoadingSpinnerProps) => (
  <div
    className={`${size} border-2 border-white-20 border-t-white rounded-full animate-spin ${className}`}
  />
);
