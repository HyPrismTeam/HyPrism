import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';

export function MenuActionButton({
  onClick,
  className = '',
  children,
}: {
  onClick?: (e: any) => void;
  className?: string;
  children?: import('preact').ComponentChildren;
  }) {
  const base =
    'w-full h-11 px-4 flex items-center justify-center gap-2 text-sm font-black tracking-tight transition-all text-white-80 hover:text-white hover:bg-white-5';
  return (
    <button type="button" onClick={onClick} className={`${base} ${className}`.trim()}>
      {children}
    </button>
  );
}
