import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';

export function ModalFooterActions({
  children,
  className = '',
  justify = 'end',
}: {
  className?: string;
  justify?: 'start' | 'between' | 'end';
} & { children?: ComponentChildren }) {
  const j = justify === 'start' ? 'justify-start' : justify === 'between' ? 'justify-between' : 'justify-end';

  return (
    <div className={`flex gap-3 p-5 border-t border-white-10 bg-black-30 ${j} ${className}`.trim()}>
      {children}
    </div>
  );
}
