import { h, ComponentChildren } from 'preact';
import { memo } from 'preact/compat';
import { useState, useEffect, useRef, useMemo, useCallback } from 'preact/hooks';

// ---------------------------------------------------------------------------
// Sciter audio detection
// Sciter's QuickJS engine does NOT support HTMLAudioElement / <audio> tags.
// When running inside Sciter, we skip all audio logic entirely and render
// nothing.  The plan (Phase 4) is to route audio through a backend C# IPC
// service (hyprism:audio:*), but until that's implemented the player is a
// silent no-op so it doesn't spam unhandled-rejection errors.
// ---------------------------------------------------------------------------
const AUDIO_SUPPORTED = (() => {
  try {
    if (typeof Audio === 'undefined') return false;
    const a = new Audio();
    return typeof a.play === 'function';
  } catch {
    return false;
  }
})();

// Import music tracks (still imported so Vite copies them to wwwroot — they
// will be needed by the backend audio service later).
import menu01 from '../../assets/music/menu_01.ogg';
import menu02 from '../../assets/music/menu_02.ogg';
import menu03 from '../../assets/music/menu_03.ogg';
import menu04 from '../../assets/music/menu_04.ogg';
import menu05 from '../../assets/music/menu_05.ogg';
import menu06 from '../../assets/music/menu_06.ogg';
import menu07 from '../../assets/music/menu_07.ogg';
import menu08 from '../../assets/music/menu_08.ogg';
import menu09 from '../../assets/music/menu_09.ogg';
import menu10 from '../../assets/music/menu_10.ogg';

const musicTracks = [
  menu01, menu02, menu03, menu04, menu05,
  menu06, menu07, menu08, menu09, menu10
];

// Sciter-safe audio helpers
function audioPlay(audio: HTMLAudioElement): Promise<void> {
  if (typeof audio.play === 'function') {
    return audio.play();
  }
  return Promise.reject(new Error('audio.play not supported'));
}
function audioPause(audio: HTMLAudioElement): void {
  if (typeof audio.pause === 'function') audio.pause();
}
function audioSetVolume(audio: HTMLAudioElement, vol: number): void {
  try { audio.volume = vol; } catch {}
}
function audioGetVolume(audio: HTMLAudioElement): number {
  try { return audio.volume ?? 0; } catch { return 0; }
}
function audioIsPaused(audio: HTMLAudioElement): boolean {
  try { return audio.paused ?? true; } catch { return true; }
}

interface MusicPlayerProps {
  muted?: boolean;
  forceMuted?: boolean;
}

export const MusicPlayer = memo(({ muted = false, forceMuted = false }: MusicPlayerProps) => {
  // ── Sciter: no audio support → render nothing ──────────────────────────
  if (!AUDIO_SUPPORTED) return null;

  const [currentTrack, setCurrentTrack] = useState(() => 
    Math.floor(Math.random() * musicTracks.length)
  );
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const fadeIntervalRef = useRef<number | null>(null);
  // Use a ref for fading state to avoid stale closures in the mute/unmute effect
  const isFadingRef = useRef(false);
  const targetVolumeRef = useRef(0.3);

  // Get a random track different from the current one
  const getNextRandomTrack = useCallback((current: number) => {
    if (musicTracks.length <= 1) return 0;
    let next = current;
    while (next === current) {
      next = Math.floor(Math.random() * musicTracks.length);
    }
    return next;
  }, []);

  // Helper to cancel any running fade
  const cancelFade = useCallback(() => {
    if (fadeIntervalRef.current) {
      clearInterval(fadeIntervalRef.current);
      fadeIntervalRef.current = null;
    }
    isFadingRef.current = false;
  }, []);

  // Handle forceMuted or muted prop change with smooth fade
  useEffect(() => {
    const audio = audioRef.current;
    if (!audio) return;

    const shouldBeSilent = muted || forceMuted;

    // Always cancel any in-progress fade before starting a new one
    cancelFade();

    if (shouldBeSilent) {
      // Fade out: reduce volume then pause
      isFadingRef.current = true;
      const startVolume = audioGetVolume(audio);
      if (startVolume <= 0 || audioIsPaused(audio)) {
        // Already silent/paused — just ensure it's paused
        audioPause(audio);
        isFadingRef.current = false;
        return;
      }
      const steps = 20;
      const stepTime = 50; // 1s total fade
      const volumeStep = startVolume / steps;
      let currentStep = 0;

      fadeIntervalRef.current = window.setInterval(() => {
        currentStep++;
        const newVol = Math.max(0, startVolume - (volumeStep * currentStep));
        audioSetVolume(audio, newVol);
        if (currentStep >= steps) {
          cancelFade();
          audioPause(audio);
          audioSetVolume(audio, 0);
        }
      }, stepTime);
    } else {
      // Unmute: start playback and fade in regardless of paused state
      isFadingRef.current = true;
      const targetVolume = targetVolumeRef.current;
      audioSetVolume(audio, 0);

      const startFadeIn = () => {
        const steps = 20;
        const stepTime = 50; // 1s total fade
        const volumeStep = targetVolume / steps;
        let currentStep = 0;

        fadeIntervalRef.current = window.setInterval(() => {
          currentStep++;
          audioSetVolume(audio, Math.min(targetVolume, volumeStep * currentStep));
          if (currentStep >= steps) {
            cancelFade();
          }
        }, stepTime);
      };

      // Always try to play (handles both paused and already-playing-at-zero-volume cases)
      audioPlay(audio)
        .then(startFadeIn)
        .catch(err => {
          console.log('Failed to resume audio:', err);
          cancelFade();
        });
    }

    return () => {
      // Only clean up interval on unmount, not on re-render
      // The cancelFade() at the top of the effect handles re-entry
    };
  }, [forceMuted, muted, cancelFade]);

  // Handle track ending - play next random track
  const handleEnded = useCallback(() => {
    const nextTrack = getNextRandomTrack(currentTrack);
    setCurrentTrack(nextTrack);
  }, [currentTrack, getNextRandomTrack]);

  // Play audio when track changes
  useEffect(() => {
    if (!audioRef.current) return;

    const audio = audioRef.current;
    audioSetVolume(audio, targetVolumeRef.current);

    // Play if not muted and not force muted
    if (!muted && !forceMuted) {
      audioPlay(audio).catch(err => {
        console.log('Auto-play blocked:', err);
        const handleUserInteraction = async () => {
            try { await audioPlay(audio); } catch {}
            document.removeEventListener('click', handleUserInteraction);
            document.removeEventListener('keydown', handleUserInteraction);
        };
        document.addEventListener('click', handleUserInteraction);
        document.addEventListener('keydown', handleUserInteraction);
      });
    }
  }, [currentTrack, muted, forceMuted]);

  // Cleanup fade interval on unmount
  useEffect(() => {
    return () => cancelFade();
  }, [cancelFade]);

  return (
    <audio
      ref={audioRef}
      src={musicTracks[currentTrack]}
      onEnded={handleEnded}
      preload="auto"
    />
  );
});

// displayName for dev tools
(MusicPlayer as any).displayName = 'MusicPlayer';
