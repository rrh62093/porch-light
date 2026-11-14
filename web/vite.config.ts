import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    // Same-origin API in dev, mirroring CloudFront routing /api/* to App Runner in production.
    // changeOrigin stays false so the API sees the browser's Host and its same-origin check passes.
    proxy: { '/api': { target: 'http://localhost:5208', changeOrigin: false } },
  },
});
