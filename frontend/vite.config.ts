/// <reference types="vitest/config" />
import tailwindcss from '@tailwindcss/vite';
import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import { viteStaticCopy } from 'vite-plugin-static-copy';

// The API the dev server forwards to; the end-to-end tests start their own backend on another port.
const backend = process.env.BACKEND_URL ?? 'http://localhost:5080';

// pdf.js loads these at runtime (character maps, the 14 standard fonts, decoders, colour profiles); they are
// served from the app itself so the viewer works without any CDN.
const pdfjsAssets = ['cmaps', 'standard_fonts', 'wasm', 'iccs'].map((folder) => ({
  src: `node_modules/pdfjs-dist/${folder}/*`,
  dest: `pdfjs/${folder}`,
  rename: { stripBase: true as const },
}));

export default defineConfig({
  plugins: [react(), tailwindcss(), viteStaticCopy({ targets: pdfjsAssets })],
  server: {
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      '/api': backend,
      '/health': backend,
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
    // Dates are shown in local time; tests expect Turkish time wherever they run.
    env: { TZ: 'Europe/Istanbul' },
  },
});
