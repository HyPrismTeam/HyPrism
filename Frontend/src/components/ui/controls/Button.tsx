import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';
import { useAccentColor } from '../../../contexts/AccentColorContext';

export type ButtonVariant = 'default' | 'primary' | 'danger';
export type ButtonSize = 'sm' | 'md';

type CommonButtonProps = {
  className?: string;
  disabled?: boolean;
  title?: string;
};

export function Button({
  variant = 'default',
  size = 'md',
  className = '',
  disabled,
  title,
  onClick,
  children,
  style,
  type = 'button',
}: CommonButtonProps & {
  variant?: ButtonVariant;
  size?: ButtonSize;
  onClick?: (e: any) => void;
  style?: Record<string, any>;
  type?: 'button' | 'submit' | 'reset';
  children?: ComponentChildren;
}) {
  const { accentColor, accentTextColor } = useAccentColor();

  const base =
    'inline-flex items-center justify-center gap-2 rounded-xl font-medium transition-all disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-transparent';
  const padding = size === 'sm' ? 'h-10 px-3 text-xs' : 'h-10 px-4 text-sm';

  const variantClass =
    variant === 'primary'
      ? 'shadow-sm hover:brightness-110'
      : variant === 'danger'
        ? 'bg-red-500-20 text-red-400 hover:bg-red-500-30 border border-red-500-20'
        : 'glass-control-solid text-white-70 hover:text-white hover:bg-white-5 border border-white-5 hover:border-white-20';

  const accentStyle: Record<string, any> | undefined =
    variant === 'primary'
      ? { backgroundColor: accentColor, color: accentTextColor, ...style }
      : style;

  return (
    <button
      type={type}
      title={title}
      disabled={disabled}
      onClick={onClick}
      style={accentStyle}
      className={`${base} ${padding} ${variantClass} ${className}`.trim()}
    >
      {children}
    </button>
  );
}
