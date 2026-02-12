import { useMemo } from 'react';
import { useAnimatedGlass } from '../contexts/AnimatedGlassContext';

/**
 * Centralized hook for glass styling (transparent, no blur).
 *
 * When `animatedGlass` is ON  → returns transparent glass styles (no blur).
 * When `animatedGlass` is OFF → returns opaque solid-color fallback.
 *
 * ## Variants
 * - **overlay** – full-screen modal overlay
 * - **panel**  – floating panel / dock / sidebar
 * - **dropdown** – small dropdown menu
 * - **bar**    – inline glass bar (dashboard action bar, etc.)
 */

export type GlassVariant = 'overlay' | 'panel' | 'dropdown' | 'bar';

interface GlassStyle {
  /** Inline CSS style object to spread on the element */
  style: React.CSSProperties;
  /** Tailwind-compatible class string (safe to concat) */
  className: string;
  /** Whether glass/transparency is enabled (for conditional rendering) */
  isGlass: boolean;
}

// ── Solid fallbacks (no transparency, fully opaque) ──────────────

const solidOverlay: React.CSSProperties = {
  background: 'rgba(0, 0, 0, 0.85)',
};

const solidPanel: React.CSSProperties = {
  background: 'rgba(28, 28, 30, 0.98)',
  border: '1px solid rgba(255,255,255,0.08)',
  boxShadow: '0 8px 32px rgba(0,0,0,0.5)',
};

const solidDropdown: React.CSSProperties = {
  background: 'rgba(26, 26, 26, 0.98)',
  border: '1px solid rgba(255,255,255,0.10)',
  boxShadow: '0 4px 16px rgba(0,0,0,0.5)',
};

const solidBar: React.CSSProperties = {
  background: 'rgba(26, 26, 26, 0.95)',
  border: '1px solid rgba(255,255,255,0.08)',
  boxShadow: '0 4px 24px rgba(0,0,0,0.4)',
};

// ── Glass styles (transparent, no blur) ──────────────────────────

const glassOverlay: React.CSSProperties = {
  // Transparent overlay — no backdrop-filter
};

const glassPanel: React.CSSProperties = {
  background: 'linear-gradient(135deg, rgba(255,255,255,0.08) 0%, rgba(255,255,255,0.04) 100%)',
  border: '1px solid rgba(255,255,255,0.12)',
  boxShadow: '0 8px 32px rgba(0,0,0,0.4), 0 2px 8px rgba(0,0,0,0.2), inset 0 1px 0 rgba(255,255,255,0.08)',
};

const glassDropdown: React.CSSProperties = {
  background: 'rgba(26, 26, 26, 0.85)',
  border: '1px solid rgba(255,255,255,0.10)',
  boxShadow: '0 4px 16px rgba(0,0,0,0.4)',
};

const glassBar: React.CSSProperties = {
  background: 'rgba(255,255,255,0.06)',
  border: '1px solid rgba(255,255,255,0.1)',
  boxShadow: '0 4px 24px rgba(0,0,0,0.3)',
};

// ── Classname strings ────────────────────────────────────────────
const glassOverlayCls = 'bg-black/60';
const solidOverlayCls = '';

export function useGlassStyle(variant: GlassVariant): GlassStyle {
  const { animatedGlass } = useAnimatedGlass();

  return useMemo(() => {
    const isGlass = animatedGlass;

    switch (variant) {
      case 'overlay':
        return {
          style: isGlass ? glassOverlay : solidOverlay,
          className: isGlass ? glassOverlayCls : solidOverlayCls,
          isGlass,
        };
      case 'panel':
        return {
          style: isGlass ? glassPanel : solidPanel,
          className: '',
          isGlass,
        };
      case 'dropdown':
        return {
          style: isGlass ? glassDropdown : solidDropdown,
          className: '',
          isGlass,
        };
      case 'bar':
        return {
          style: isGlass ? glassBar : solidBar,
          className: '',
          isGlass,
        };
    }
  }, [animatedGlass, variant]);
}

export default useGlassStyle;
