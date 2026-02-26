/**
 * Sciter / QuickJS compatibility polyfills.
 *
 * Import this file once at the entry point (main.tsx) BEFORE any other code
 * that relies on these APIs.
 */

// ---------------------------------------------------------------------------
// localStorage — Sciter / QuickJS does not have Web Storage API
// ---------------------------------------------------------------------------
if (typeof globalThis.localStorage === 'undefined') {
  const _store: Record<string, string> = {};
  (globalThis as any).localStorage = {
    getItem(key: string): string | null {
      return Object.prototype.hasOwnProperty.call(_store, key) ? _store[key] : null;
    },
    setItem(key: string, value: string): void {
      _store[key] = String(value);
    },
    removeItem(key: string): void {
      delete _store[key];
    },
    clear(): void {
      for (const k of Object.keys(_store)) delete _store[k];
    },
    get length(): number {
      return Object.keys(_store).length;
    },
    key(index: number): string | null {
      return Object.keys(_store)[index] ?? null;
    },
  };
}

// ---------------------------------------------------------------------------
// sessionStorage — same shim (some libraries reference it)
// ---------------------------------------------------------------------------
if (typeof globalThis.sessionStorage === 'undefined') {
  (globalThis as any).sessionStorage = (globalThis as any).localStorage;
}

// ---------------------------------------------------------------------------
// btoa / atob — QuickJS does not expose these Web APIs by default
// ---------------------------------------------------------------------------

if (typeof globalThis.btoa !== 'function') {
  const B64_CHARS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

  globalThis.btoa = (binary: string): string => {
    let result = '';
    let i = 0;
    while (i < binary.length) {
      const a = binary.charCodeAt(i++);
      const b = binary.charCodeAt(i++);
      const c = binary.charCodeAt(i++);
      result += B64_CHARS[(a >> 2) & 63];
      result += B64_CHARS[((a << 4) | ((b || 0) >> 4)) & 63];
      result += isNaN(b) ? '=' : B64_CHARS[((b << 2) | ((c || 0) >> 6)) & 63];
      result += isNaN(c) ? '=' : B64_CHARS[c & 63];
    }
    return result;
  };
}

if (typeof globalThis.atob !== 'function') {
  const B64_LOOKUP: Record<string, number> = {};
  'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/'
    .split('')
    .forEach((c, i) => { B64_LOOKUP[c] = i; });

  globalThis.atob = (base64: string): string => {
    const str = base64.replace(/=+$/, '');
    let result = '';
    let buf = 0;
    let bits = 0;
    for (const ch of str) {
      buf = (buf << 6) | (B64_LOOKUP[ch] ?? 0);
      bits += 6;
      if (bits >= 8) {
        bits -= 8;
        result += String.fromCharCode((buf >> bits) & 0xff);
      }
    }
    return result;
  };
}
