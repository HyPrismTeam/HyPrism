import { defineConfig, type Plugin } from 'vite'
import preact from '@preact/preset-vite'

// ---------------------------------------------------------------------------
// Sciter QuickJS compatibility: strict-equality transform
//
// Sciter 6's QuickJS fork throws "operator ==: no function defined" when
// abstract equality (==) is used on plain objects.  Preact's minified
// vendor bundle uses `==` ~120 times (null-checks, prop diffing, etc.).
//
// This plugin post-processes the vendor chunk to:
//   1. Rewrite `x==null` / `null==x` → strict null-or-undefined check
//   2. Rewrite remaining `==` → `===`  and  `!=` → `!==`
//
// Because `sourcemap: false`, no map fixup is needed.
// ---------------------------------------------------------------------------
// ---------------------------------------------------------------------------
// Helper: fix `null !== (VAR = expr)` patterns left behind by the simple
// regex-based Step 1 (which only handles `[\w$.]+` identifiers).
//
// Preact frequently uses  `null != (o = arr[i])` to assign-and-test.
// After Step 2 converts  `!=` → `!==`, we get `null !== (o = arr[i])` which
// is wrong:  `null !== undefined` → true,  breaking null-or-undefined guards.
//
// This function scans the code for `null !== (` sequences, finds the
// matching `)` (with balanced-paren counting), extracts the assigned
// variable name, and rewrites to:
//   (null !== (VAR = expr) && void 0 !== VAR)
// ---------------------------------------------------------------------------
function fixParenthesizedNullChecks(code: string): string {
  const needle = 'null !== (';
  let result = '';
  let searchFrom = 0;

  while (true) {
    const idx = code.indexOf(needle, searchFrom);
    if (idx === -1) {
      result += code.slice(searchFrom);
      break;
    }

    // Copy everything before the match
    result += code.slice(searchFrom, idx);

    // Find the matching closing paren starting after "null !== "
    const parenStart = idx + needle.length - 1; // index of '('
    let depth = 1;
    let j = parenStart + 1;
    while (j < code.length && depth > 0) {
      if (code[j] === '(') depth++;
      else if (code[j] === ')') depth--;
      j++;
    }

    if (depth !== 0) {
      // Unbalanced — leave as-is
      result += needle;
      searchFrom = idx + needle.length;
      continue;
    }

    // parenStart .. j-1 is the balanced (...) including parens
    const innerExpr = code.slice(parenStart + 1, j - 1); // without outer ( )

    // Extract the assigned variable name: first \w+ before `=` (but not ==, ===)
    const assignMatch = innerExpr.match(/^(\w+)\s*=[^=]/);
    if (assignMatch) {
      const varName = assignMatch[1];
      const fullParen = code.slice(parenStart, j); // (VAR = expr)
      result += `(null !== ${fullParen} && void 0 !== ${varName})`;
    } else {
      // No assignment pattern — just emit the null !== (...) as is
      result += code.slice(idx, j);
    }
    searchFrom = j;
  }

  return result;
}

function sciterStrictEqualityPlugin(): Plugin {
  return {
    name: 'sciter-strict-equality',
    generateBundle(_options, bundle) {
      for (const [, chunk] of Object.entries(bundle)) {
        if (chunk.type !== 'chunk') continue;
        // Apply to ALL JS chunks (vendor + app code) for full safety
        let code = chunk.code;

        // ── Step 1 – null-equality patterns ────────────────────────────
        // Handle optional whitespace around operators so unminified builds
        // are also transformed correctly.
        // `ident == null` → `(ident===null||ident===void 0)`
        code = code.replace(/([\w$.]+)\s*==\s*null/g, '($1===null||$1===void 0)');
        // `null == ident` → `(null===ident||void 0===ident)`
        code = code.replace(/null\s*==\s*([\w$.]+)/g, '(null===$1||void 0===$1)');
        // Same for !=
        code = code.replace(/([\w$.]+)\s*!=\s*null/g, '($1!==null&&$1!==void 0)');
        code = code.replace(/null\s*!=\s*([\w$.]+)/g, '(null!==$1&&void 0!==$1)');

        // ── Step 2 – remaining abstract equality ───────────────────────
        // Match `==` NOT preceded by [!=<>] and NOT followed by `=`
        code = code.replace(/(?<![!=<>])={2}(?!=)/g, '===');
        // Match `!=` NOT followed by `=` (avoids `!==`)
        code = code.replace(/(?<!=)!=(?!=)/g, '!==');

        // ── Step 3 – fix parenthesized null checks ────────────────────
        // After Step 2 converts `null != (expr)` → `null !== (expr)`,
        // we must also check for void 0.  The pattern is always
        //   `null !== (VAR = something)`
        // We transform → `(null !== (VAR = something) && void 0 !== VAR)`
        // using balanced-paren matching so nested expressions are handled.
        code = fixParenthesizedNullChecks(code);

        chunk.code = code;
      }
    },
  };
}

// ---------------------------------------------------------------------------
// Sciter asset URL fix
//
// Vite emits `"" + new URL("file.ext", import.meta.url).href` for static
// imports (images, audio, etc.).  In a browser, `import.meta.url` points to
// the JS module file (inside `assets/`), so the relative URL resolves to
// `assets/file.ext`.  But in Sciter, `import.meta.url` points to the HTML
// document (`wwwroot/index.html`), making the URL resolve to
// `wwwroot/file.ext` — which doesn't exist.
//
// This plugin rewrites those patterns to plain `"./assets/file.ext"` strings
// which resolve correctly from the HTML document's location.
// ---------------------------------------------------------------------------
function sciterAssetUrlPlugin(): Plugin {
  return {
    name: 'sciter-asset-url',
    generateBundle(_options, bundle) {
      for (const [, chunk] of Object.entries(bundle)) {
        if (chunk.type !== 'chunk') continue;
        // Pattern: "" + new URL("filename.ext", import.meta.url).href
        // Allow optional whitespace (unminified builds have spaces)
        chunk.code = chunk.code.replace(
          /""\s*\+\s*new\s+URL\(\s*"([^"]+)"\s*,\s*import\.meta\.url\s*\)\s*\.href/g,
          '"./assets/$1"'
        );
      }
    },
  };
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [preact(), sciterStrictEqualityPlugin(), sciterAssetUrlPlugin()],
  base: './',
  server: {
    port: 34115,
    strictPort: true,
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    // Minification disabled — Sciter's QuickJS fork doesn't support some
    // syntax patterns that esbuild's minifier produces (e.g. shorthand
    // property names, optional chaining).  For a local desktop app loaded
    // from filesystem, bundle size is not a concern.
    minify: false,
    // Disable Vite's modulepreload polyfill — it injects a fetch() IIFE that
    // uses relative hrefs and Sciter's fetch() requires explicit file:// URLs.
    modulePreload: false,
    rollupOptions: {
      output: {
        // Keep vendor (preact) separate from app code
        manualChunks: {
          vendor: ['preact', 'preact/hooks', 'preact/compat'],
        },
      },
    },
  },
  resolve: {
    alias: {
      '@': '/src',
      // Keep react compat aliases so ipc.ts and other files can use preact/compat
      'react': 'preact/compat',
      'react-dom': 'preact/compat',
      'react/jsx-runtime': 'preact/jsx-runtime',
    },
  },
})

