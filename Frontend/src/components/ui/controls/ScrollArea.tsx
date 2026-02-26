import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';

export type ScrollAxis = 'x' | 'y' | 'both';

export function ScrollArea({
  children,
  axis = 'both',
  thin = false,
  className = '',
}: {
  axis?: ScrollAxis;
  thin?: boolean;
  className?: string;
} & { children?: ComponentChildren }) {
  const overflow =
    axis === 'x'
      ? 'overflow-x-auto overflow-y-hidden'
      : axis === 'y'
        ? 'overflow-y-auto overflow-x-hidden'
        : 'overflow-auto';

  return (
    <div className={`${overflow} ${thin ? 'thin-scrollbar' : ''} ${className}`.trim()}>
      {children}
    </div>
  );
}
