import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';
import { useAccentColor } from '../../contexts/AccentColorContext';

export type SelectionCardVariant = 'default' | 'success' | 'warning' | 'danger';

interface SelectionCardProps {
  title: ComponentChildren;
  description?: ComponentChildren;
  icon?: ComponentChildren;
  selected?: boolean;
  disabled?: boolean;
  variant?: SelectionCardVariant;
  highlightColor?: string;
  onClick?: () => void;
  className?: string;
}

export function SelectionCard({
  title,
  description,
  icon,
  selected = false,
  disabled = false,
  variant = 'default',
  highlightColor,
  onClick,
  className = '',
}: SelectionCardProps) {
  const { accentColor } = useAccentColor();

  const baseClass =
    'w-full text-left rounded-xl border transition-all select-none focus:outline-none focus-visible:ring-2 focus-visible:ring-white-20';

  const surfaceClass = 'glass-control-solid';

  const variantClass = (() => {
    if (disabled) return 'opacity-50 cursor-not-allowed';

    switch (variant) {
      case 'danger':
        return selected
          ? 'border-red-500/40 bg-red-500/10'
          : 'border-white-5 hover:border-red-500/30 hover:bg-white-5 cursor-pointer';
      case 'success':
        return selected
          ? 'border-green-500/40 bg-green-500/10'
          : 'border-white-5 hover:border-green-500/30 hover:bg-white-5 cursor-pointer';
      case 'warning':
        return selected
          ? 'border-yellow-500/40 bg-yellow-500/10'
          : 'border-white-5 hover:border-yellow-500/30 hover:bg-white-5 cursor-pointer';
      case 'default':
      default:
        return selected
          ? 'border-white-20 bg-white-5 cursor-pointer'
          : 'border-white-5 hover:border-white-20 hover:bg-white-5 cursor-pointer';
    }
  })();

  const selectedStyle: Record<string, any> | undefined = (() => {
    if (!selected) return undefined;
    if (variant !== 'default') return undefined;

    const color = highlightColor ?? accentColor;
    return {
      borderColor: color,
      background: `${color}1f`, // hex alpha 0x1f ≈ 12% — rgba channel-var not supported in Sciter
    };
  })();

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={disabled ? undefined : onClick}
      className={`${baseClass} ${surfaceClass} ${variantClass} ${className}`.trim()}
      style={selectedStyle}
    >
      <div className="flex items-center gap-4 p-4">
        {icon ? (
          <div className="w-10 h-10 rounded-lg bg-white-5 border border-white-5 flex items-center justify-center text-white-70 flex-shrink-0">
            {icon}
          </div>
        ) : null}

        <div className="min-w-0">
          <div className="text-white font-medium leading-tight">{title}</div>
          {description ? (
            <div className="mt-1 text-sm text-white-50 leading-snug">{description}</div>
          ) : null}
        </div>
      </div>
    </button>
  );
}
