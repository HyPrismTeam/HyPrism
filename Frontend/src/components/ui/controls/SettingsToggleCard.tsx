import { h, ComponentChildren } from 'preact';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';
import { Switch } from './Switch';

/**
 * A settings row card with an icon, title, optional description, optional badge and a toggle switch.
 *
 * Replaces all the inline 20-line toggle patterns throughout SettingsModal.
 */
export interface SettingsToggleCardProps {
  /** Lucide icon or any ComponentChildren rendered inside a 32×32 rounded box. */
  icon: ComponentChildren;
  /** Primary label */
  title: ComponentChildren;
  /** Secondary description below the title */
  description?: ComponentChildren;
  /** Optional badge (e.g. "BETA") rendered next to the title */
  badge?: ComponentChildren;
  /** Current toggle state */
  checked: boolean;
  /** Called when the toggle is changed */
  onCheckedChange: (next: boolean) => void;
  /** Disables the whole card */
  disabled?: boolean;
  className?: string;
}

export function SettingsToggleCard({
  icon,
  title,
  description,
  badge,
  checked,
  onCheckedChange,
  disabled = false,
  className = '',
}: SettingsToggleCardProps) {
  return (
    <div
      className={`flex items-center justify-between p-4 rounded-2xl glass-control-solid cursor-pointer hover:border-white/[0.12] transition-all ${disabled ? 'opacity-50 cursor-not-allowed' : ''} ${className}`.trim()}
      onClick={() => !disabled && onCheckedChange(!checked)}
    >
      <div className="flex items-center gap-3 min-w-0">
        <div className="w-8 h-8 rounded-lg bg-white-5 flex items-center justify-center flex-shrink-0">
          {icon}
        </div>
        <div className="min-w-0">
          {badge ? (
            <div className="flex items-center gap-2">
              <span className="text-white text-sm font-medium">{title}</span>
              {badge}
            </div>
          ) : (
            <span className="text-white text-sm font-medium">{title}</span>
          )}
          {description && <p className="text-xs text-white-40">{description}</p>}
        </div>
      </div>
      <Switch checked={checked} onCheckedChange={onCheckedChange} disabled={disabled} />
    </div>
  );
}
