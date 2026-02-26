import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';

type CommonButtonProps = {
  className?: string;
  disabled?: boolean;
  title?: string;
};

export function LinkButton({
  className = '',
  disabled,
  title,
  onClick,
  children,
  style,
  type = 'button',
}: 
  CommonButtonProps & {
    onClick?: (e: any) => void;
    style?: Record<string, any>;
    type?: 'button' | 'submit' | 'reset';
    children?: import('preact').ComponentChildren;
  }) {
  const base =
    'inline-flex items-center gap-1.5 text-white-70 hover:text-white hover:underline underline-offset-4 transition-colors disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:no-underline';

  return (
    <button
      type={type}
      title={title}
      disabled={disabled}
      onClick={onClick}
      style={style}
      className={`${base} ${className}`.trim()}
    >
      {children}
    </button>
  );
}
