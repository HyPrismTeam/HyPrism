import { defineConfig } from 'vite'
import preact from '@preact/preset-vite'

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [preact()],
  base: './',
  server: {
    port: 34115,
    strictPort: true,
  },
  build: {
    outDir: '../wwwroot',
    emptyOutDir: true,
    sourcemap: false,
    minify: 'esbuild',
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

