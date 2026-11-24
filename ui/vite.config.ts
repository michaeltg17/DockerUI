/// <reference types="vite/client" />

import react from '@vitejs/plugin-react';
import { defineConfig } from 'vite';
import viteTsconfigPaths from 'vite-tsconfig-paths';

export default defineConfig(({ mode }) => ({
  base: './',
  plugins: [react(), viteTsconfigPaths()],
  server: {
    port: 3000,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
        ws: true,
      },
    },
  },
  preview: {
    port: 3000,
  },
  optimizeDeps: { exclude: ['fsevents'] },
  build: {
    // The e2e image (Dockerfile.e2e builds with --mode e2e) ships source maps so
    // the browser V8 coverage captured by the e2e suite can be remapped to ui/src;
    // production builds stay map-free.
    sourcemap: mode === 'e2e',
    rollupOptions: {
      output: {
        experimentalMinChunkSize: 3500,
      },
    },
  },
}));
