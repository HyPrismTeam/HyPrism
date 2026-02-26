/**
 * Sciter-compatible clipboard utility.
 * 
 * Sciter (QuickJS) does NOT have navigator.clipboard, but may support
 * document.execCommand('copy') via a text selection trick.
 * This utility tries both approaches with graceful fallback.
 */
export async function copyToClipboard(text: string): Promise<boolean> {
  // Method 1: Modern Clipboard API (works in Chromium-based browsers)
  if (typeof navigator !== 'undefined' && navigator.clipboard && typeof navigator.clipboard.writeText === 'function') {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Fall through to legacy method
    }
  }

  // Method 2: Legacy execCommand (may work in some Sciter versions)
  try {
    const el = document.createElement('textarea');
    el.value = text;
    el.style.position = 'fixed';
    el.style.top = '-9999px';
    el.style.left = '-9999px';
    document.body.appendChild(el);
    el.focus();
    el.select();
    const success = document.execCommand('copy');
    document.body.removeChild(el);
    return success;
  } catch {
    console.warn('copyToClipboard: all methods failed');
    return false;
  }
}
